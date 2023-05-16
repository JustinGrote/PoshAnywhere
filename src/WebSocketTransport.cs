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
  public WebSocketConnectionInfo(int port, string hostname = "localhost", bool useSSL = true)
  {
    // We want a client at this point but we don't want to connect until the transport manager initiates it
    Port = port;
    Hostname = hostname;
    string Protocol = useSSL ? "wss" : "ws";
    WebSocketUri = new($"{Protocol}://{Hostname}:{Port}/psrp");
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

class WebSocketTransportManager : SimpleTransportManagerBase
{
  private readonly Uri WebSocketUri;
  private readonly Guid InstanceId;
  private readonly ClientWebSocket Client = new();

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

  protected override void HandleDataFromClient(string data)
  {
    // We do not add a newline here, it will be added on the other side when passed to the named pipe, because websockets has the concept of messages and we don't need a delimiter, it'll simply signal the client when the bytes are finished writing
    Client.SendAsync(
      Encoding.UTF8.GetBytes(data),
      WebSocketMessageType.Text,
      true,
      default
    ).GetAwaiter().GetResult();
  }

  protected override async Task<string> ReceiveDataFromTransport()
  {
    using MemoryStream receiveStream = new();
    using StreamReader reader = new(receiveStream);
    // Incoming data should be UTF8 encoded string, so we can just pass that to handleDataReceived which doesn't expect a complete PSRP message as far as I can tell
    WebSocketReceiveResult receiveResult;
    do
    {
      byte[] buffer = new byte[8194];

      receiveResult = await Client.ReceiveAsync(buffer, default);
      receiveStream.Write(buffer, 0, receiveResult.Count);
    } while (!receiveResult.EndOfMessage);
    // Rewind the memorystream so it can be read by readline
    receiveStream.Position = 0;
    return reader.ReadLine() ?? throw new InvalidDataException("Received null data from websocket, this should never happen.");
  }

  public override void CreateAsync()
  {
    Client.ConnectAsync(WebSocketUri, default).GetAwaiter().GetResult();
    base.CreateAsync();
  }

  public override void CloseAsync()
  {
    Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session closed", default);
    base.CloseAsync();
  }

  protected override void CleanupConnection()
  {
    Client.Dispose();
  }
}