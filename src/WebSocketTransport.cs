using System.Management.Automation;
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
  internal readonly CancellationToken CancellationToken;
  internal readonly PSCmdlet PSCmdlet;
  public WebSocketConnectionInfo(PSCmdlet psCmdlet, int port, string hostname = "localhost", bool useSSL = true, CancellationToken cancellationToken = default)
  {
    WebSocketTarget = new(hostname, port, useSSL);
    CancellationToken = cancellationToken;
    PSCmdlet = psCmdlet;
  }

  public override BaseClientSessionTransportManager CreateClientSessionTransportManager(
    Guid instanceId,
    string sessionName,
    PSRemotingCryptoHelper cryptoHelper
  ) => new WebSocketTransportManager(
    instanceId,
    sessionName,
    cryptoHelper,
    this
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
  private readonly WebSocketConnectionInfo ConnectionInfo;
  private readonly CancellationToken CancellationToken;
  private readonly PSCmdlet PSCmdlet;

  /// <summary>
  /// Instantiates a new WebSocket transport
  /// </summary>
  /// <param name="instanceId"></param>
  /// <param name="cryptoHelper"></param>
  internal WebSocketTransportManager(Guid instanceId, string _, PSRemotingCryptoHelper cryptoHelper, WebSocketConnectionInfo connectionInfo) : base(instanceId, cryptoHelper)
  {
    InstanceId = instanceId;
    ConnectionInfo = connectionInfo;
    WebSocketUri = ConnectionInfo.WebSocketUri;
    CancellationToken = ConnectionInfo.CancellationToken;
    PSCmdlet = ConnectionInfo.PSCmdlet;
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
      CancellationToken
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
      receiveResult = await Client.ReceiveAsync(buffer, CancellationToken);
      await receiveStream.WriteAsync(buffer.AsMemory(0, receiveResult.Count), CancellationToken);
    } while (!receiveResult.EndOfMessage);

    // Rewind the memorystream so it can be read by readline
    receiveStream.Position = 0;
    var message = await reader.ReadLineAsync(CancellationToken);

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
    try
    {
      Client.ConnectAsync(WebSocketUri, CancellationToken).GetAwaiter().GetResult();
      base.CreateAsync();
    }
    catch (Exception ex)
    {
      HandleWebSocketError(ex, true);
    }
  }

  public override void CloseAsync()
  {
    try
    {
      Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session closed", new CancellationTokenSource(3000).Token);
      base.CloseAsync();
    }
    catch (Exception ex)
    {
      HandleWebSocketError(ex);
    }
  }

  protected override void CleanupConnection()
  {
    try
    {
      Client.Dispose();
    }
    catch (Exception ex)
    {
      HandleWebSocketError(ex);
    }
  }

  /// <summary>
  /// Transports expect very specific exception type to be handled gracefully, but we also want to inform our calling PSCmdlet if still in the connecting phase
  /// </summary>
  private void HandleWebSocketError(Exception exception, bool throwCmdletError = false)
  {
    PSRemotingTransportException transportException = new($"Websocket Transport Error: {exception.Message} - {exception.InnerException?.Message}", exception);

    if (throwCmdletError)
    {
      PSCmdlet.ThrowTerminatingError(new ErrorRecord(
        transportException,
        "WebsocketTransportError",
        ErrorCategory.ConnectionError,
        this
      ));
    }

    RaiseErrorHandler(new TransportErrorOccuredEventArgs(transportException, TransportMethodEnum.CloseShellOperationEx));
  }
}