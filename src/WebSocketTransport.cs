using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;
using System.Net.WebSockets;
using System.Text;

namespace PoshTransports;

/// <summary>
/// Attaches to a TCP port where PS remoting messages are provided
/// </summary>
public class WebSocketConnectionInfo : UnauthenticatedRunspaceConnectionInfo
{
  internal readonly int Port;
  internal readonly string Hostname;
  internal Uri WebSocketUri;
  public WebSocketConnectionInfo(int port, string hostname = "localhost")
  {
    // We want a client at this point but we don't want to connect until the transport manager initiates it
    Port = port;
    Hostname = hostname;
    WebSocketUri = new($"ws://{Hostname}:{Port}/psrp");
  }

  public override BaseClientSessionTransportManager CreateClientSessionTransportManager(
    Guid instanceId,
    string sessionName,
    PSRemotingCryptoHelper cryptoHelper
  ) => new WebSocketTransportManager(
    instanceId,
    sessionName,
    cryptoHelper,
    WebSocketUri
  );

  public override string ComputerName
  {
    get => WebSocketUri.ToString();
    set => throw new NotImplementedException("Cannot fetch Computername");
  }

  /// <summary>
  /// Create shallow copy of NamedPipeInfo object.
  /// </summary>
  public override RunspaceConnectionInfo Clone()
  {
    return new WebSocketConnectionInfo(Port, Hostname);
  }
}

class WebSocketTransportManager : ClientSessionTransportManagerBase
{
  private readonly Uri WebSocketUri;
  private readonly Guid InstanceId;
  private readonly ClientWebSocket client = new();

  /// <summary>
  /// Instantiates a new WebSocket transport
  /// </summary>
  /// <param name="instanceId"></param>
  /// <param name="cryptoHelper"></param>
  internal WebSocketTransportManager(Guid instanceId, string _, PSRemotingCryptoHelper cryptoHelper, Uri webSocketUri) : base(instanceId, cryptoHelper)
  {
    InstanceId = instanceId;
    WebSocketUri = webSocketUri;
  }

  public override void CreateAsync()
  {
    client.ConnectAsync(WebSocketUri, default).GetAwaiter().GetResult();
    SetMessageWriter(new WebSocketTextWriter(client));
    StartReaderThread(client);
  }

  public override void CloseAsync()
  {
    client.Dispose();
    base.CloseAsync();
  }

  /// <summary>
  /// Starts dedicated streamReader thread for messages coming from the stream
  /// </summary>
  private void StartReaderThread(ClientWebSocket client)
  {
    // The lambda is a workaround to Threads not supporting generic syntax for parameters
    // Ref: https://stackoverflow.com/questions/3360555/how-to-pass-parameters-to-threadstart-method-in-thread
    Thread processReaderThread = new(() => ProcessReaderThread(client))
    {
      IsBackground = true,
      Name = InstanceId.ToString()
    };

    processReaderThread.Start();
  }

  /// <summary>
  /// Delegate that parses line-delimited PSRP messages from the transport on a separate thread
  /// </summary>
  private void ProcessReaderThread(ClientWebSocket client)
  {
    try
    {
      // Send one fragment.
      SendOneItem();

      // Start reader loop.
      while (true)
      {
        // Incoming data should be UTF8 encoded string, so we can just pass that to handleDataReceived which doesn't expect a complete PSRP message as far as I can tell
        byte[] buffer = new byte[1024];
        var receiveResult = client.ReceiveAsync(buffer, default).GetAwaiter().GetResult();
        if (receiveResult.CloseStatus == WebSocketCloseStatus.NormalClosure)
        {
          break;
        }

        var data = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
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
    client.Dispose();
  }
}

/// <summary>
/// Sends to the attached websocket when written to
/// </summary>
class WebSocketTextWriter : TextWriter
{
  private ClientWebSocket Client { get; }
  public override Encoding Encoding => Encoding.UTF8;

  public WebSocketTextWriter(ClientWebSocket client) => Client = client;

  public override void WriteLine(string? data)
  {
    if (data is null)
    {
      return;
    }

    Client.SendAsync(
      Encoding.GetBytes(data),
      WebSocketMessageType.Text,
      true,
      default
    ).GetAwaiter().GetResult();
  }
}