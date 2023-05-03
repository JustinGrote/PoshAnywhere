
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
namespace PoshTransports;

/// <summary>
/// The actual transport manager that will be used to send and receive messages. Note that it derives from ClientSessionTransportManagerBase, which derives from BaseClientSessionTransportManager. This is confusing.
/// </summary>
class TcpClientSessionTransportManager : ClientSessionTransportManagerBase
{
  private readonly TcpConnectionInfo connectionInfo;
  internal TcpClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper, TcpConnectionInfo connectionInfo) : base(instanceId, cryptoHelper) => this.connectionInfo = connectionInfo;

  public override void CloseAsync()
  {
    connectionInfo.Client.Close();
    base.CloseAsync();
  }

  public override void CreateAsync()
  {
    var client = connectionInfo.Client;
    client.Connect(connectionInfo.Hostname, connectionInfo.Port);
    var stream = client.GetStream();
    // We dont have to do anything special so we just pass the textwriter directly to PSRP. This is what it will emit the PSRP messages to. We should make a custome writer if we cannot handle simple line-delimited PSRP messages
    SetMessageWriter(new StreamWriter(stream));
    StartReaderThread(new StreamReader(stream));
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
      Name = connectionInfo.ComputerName
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
    connectionInfo.Client.Dispose();
  }
}
