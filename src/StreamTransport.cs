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
  private readonly StreamReader Reader;
  private readonly StreamWriter Writer;
  /// <summary>
  /// An optional stream that, if provided, will be disposed
  /// </summary>
  private readonly Stream? Stream;

  /// <summary>
  /// When specified, the stream will disposed after the PSRP session is closed. This can be useful if your transport proves a new stream each time
  /// </summary>
  public bool DisposeStream;
  public readonly string SessionName;

  private readonly Guid InstanceId;

  /// <summary>
  /// Instantiates a new Stream Transport. To the <paramref name="stream"/> parameter, provide a bidirectional UTF8-encoded stream that sends and accepts PSRP single line XML messages
  /// </summary>
  internal StreamTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper, StreamReader reader, StreamWriter writer, bool disposeStream = false) : base(instanceId, cryptoHelper)
  {
    InstanceId = instanceId;
    SessionName = sessionName;
    Reader = reader;
    writer.AutoFlush = true;
    Writer = writer;
    DisposeStream = disposeStream;
  }

  internal StreamTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper, Stream stream) : this(instanceId, sessionName, cryptoHelper, new(stream), new(stream))
  {
    Stream = stream;
  }

  public override void CreateAsync()
  {
    SetMessageWriter(Writer);
    StartReaderThread(Reader);
  }

  public override void CloseAsync()
  {
    Writer.Flush();
  }

  protected override void CleanupConnection()
  {
    Writer.Dispose();
    Reader.Dispose();
    if (DisposeStream && Stream is not null)
    {
      Stream.Dispose();
    }
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
}