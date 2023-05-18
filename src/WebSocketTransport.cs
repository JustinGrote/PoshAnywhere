using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
using System.Net.WebSockets;
using System.Text;

namespace PoshTransports;

public record WebSocketTarget(string Hostname, int Port, bool UseSSL)
{
  public string Protocol => UseSSL ? "wss" : "ws";
  public Uri WebSocketUri => new($"{Protocol}://{Hostname}:{Port}/psrp");
}
/// <summary>
/// Attaches to a TCP port where PS remoting messages are provided
/// </summary>
public class WebSocketConnectionInfo : UnauthenticatedRunspaceConnectionInfo
{
  internal readonly WebSocketTarget WebSocketTarget;
  internal Uri WebSocketUri => WebSocketTarget.WebSocketUri;
  public WebSocketConnectionInfo(int port, string hostname = "localhost", bool useSSL = true)
  {
    WebSocketTarget = new(hostname, port, useSSL);
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
    get => WebSocketTarget.Hostname;
    set => throw new NotImplementedException("Cannot fetch Computername");
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

  private Task? activeHandleDataTask;
  protected override void HandleDataFromClient(string data)
  {
    // We do not add a newline here, it will be added on the other side when passed to the named pipe, because websockets has the concept of messages and we don't need a delimiter, it'll simply signal the client when the bytes are finished writing

    // We can only send one message at a time, so we need to wait for the previous message to finish sending before we can send the next one
    // Ref: https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.sendasync?view=net-7.0
    if (activeHandleDataTask is not null)
    {
      while (!activeHandleDataTask.Wait(500)) { }
    }

    activeHandleDataTask = Client.SendAsync(
      Encoding.UTF8.GetBytes(data),
      WebSocketMessageType.Text,
      true,
      default
    );
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
    var message = await reader.ReadLineAsync();

    string errMessage = $"Websocket Server sent a close status of {receiveResult.CloseStatus} with reason {receiveResult.CloseStatusDescription}";

    return message ?? throw receiveResult.CloseStatus switch
    {
      WebSocketCloseStatus.NormalClosure => new PSRemotingTransportException("Server sent a NormalClosure, these are initiated from the client so this should never happen"),
      WebSocketCloseStatus.Empty => new PSRemotingTransportException("Received null data from websocket, this should never happen."),
      _ => new PSRemotingTransportException(
        "Websocket Unexpectedly Closed, see InnerException for details",
        new WebSocketException(
          WebSocketError.ConnectionClosedPrematurely,
          $"Server sent a unexpected close status of {receiveResult.CloseStatus} with reason {receiveResult.CloseStatusDescription}"
        )
      )
    };
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