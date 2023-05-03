using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;

namespace PoshTransports;

public class StreamConnectionInfo : UnauthenticatedRunspaceConnectionInfo
{
  internal readonly Stream Stream;
  public StreamConnectionInfo(Stream stream) => Stream = stream;

  public override BaseClientSessionTransportManager CreateClientSessionTransportManager(
    Guid instanceId,
    string sessionName,
    PSRemotingCryptoHelper cryptoHelper
  ) => new StreamTransportManager(
    instanceId,
    sessionName,
    cryptoHelper,
    Stream
  );

  public override string ComputerName
  {
    get => $"stream://{Stream.GetHashCode()}";
    set => throw new NotImplementedException("Cannot fetch Computername");
  }

  /// <summary>
  /// Create shallow copy of NamedPipeInfo object.
  /// </summary>
  public override RunspaceConnectionInfo Clone()
  {
    return new StreamConnectionInfo(Stream);
  }
}

class StreamTransportManager : ClientSessionTransportManagerBase
{
  private readonly Stream Stream;
  private readonly Guid InstanceId;

  /// <summary>
  /// Instantiates a new Stream Transport. To the <paramref name="stream"/> parameter, provide a bidirectional UTF8-encoded stream that sends and accepts PSRP single line XML messages
  /// </summary>
  /// <param name="instanceId"></param>
  /// <param name="cryptoHelper"></param>
  /// <param name="stream"></param>
  internal StreamTransportManager(Guid instanceId, string _, PSRemotingCryptoHelper cryptoHelper, Stream stream) : base(instanceId, cryptoHelper)
  {
    InstanceId = instanceId;
    Stream = stream;
  }
  public override void CreateAsync()
  {
    SetMessageWriter(new StreamWriter(Stream)
    {
      AutoFlush = true
    });
    StartReaderThread(new StreamReader(Stream));
  }

  public override void CloseAsync()
  {
    Stream.Dispose();
    base.CloseAsync();
  }

  /// <summary>
  /// Starts dedicated streamReader thread for messages coming from the stream
  /// </summary>
  private void StartReaderThread(StreamReader reader)
  {
    // The lambda is a workaround to Threads not supporting generic syntax for parameters
    // Ref: https://stackoverflow.com/questions/3360555/how-to-pass-parameters-to-threadstart-method-in-thread
    Thread processReaderThread = new(() => ProcessReaderThread(reader))
    {
      IsBackground = true,
      Name = InstanceId.ToString()
    };

    processReaderThread.Start();
  }

  /// <summary>
  /// Delegate that parses line-delimited PSRP messages from the transport on a separate thread
  /// </summary>
  private void ProcessReaderThread(StreamReader reader)
  {
    try
    {
      // Send one fragment.
      SendOneItem();

      // Start reader loop.
      while (true)
      {
        string? data = reader.ReadLine();
        if (data is null)
        {
          break; // Reaching the end of this loop without an ObjectDisposedException will get handled as an error.
        }
        HandleDataReceived(data);
      }
    }
    catch (ObjectDisposedException)
    {
      // Normal reader thread end.
    }
    catch (Exception e)
    {
      throw new NotImplementedException($"Error in reader thread: {e.Message}");
    }
  }

  protected override void CleanupConnection()
  {
    Stream.Dispose();
  }
}