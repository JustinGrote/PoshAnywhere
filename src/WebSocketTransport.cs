using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Net.WebSockets;
using System.Text;

namespace PoshTransports;

public record WebSocketTarget(string Hostname, int Port, bool UseSSL)
{
  public string Protocol => UseSSL ? "wss" : "ws";
  public Uri WebSocketUri => new($"{Protocol}://{Hostname}:{Port}/psrp");

  public static WebSocketTarget Create(string Hostname, int Port, bool UseSSL) => new(Hostname, Port, UseSSL);
  public static WebSocketTarget Parse(Uri WebSocketUri) => new(WebSocketUri.Host, WebSocketUri.Port, WebSocketUri.Scheme == "wss");

  public override string ToString() => WebSocketUri.ToString();
}

public class WebSocketConnectionInfo : SimpleRunspaceConnectionInfo
{
  public readonly WebSocketTarget WebSocketTarget;
  public Uri WebSocketUri => WebSocketTarget.WebSocketUri;

  public WebSocketConnectionInfo(PSCmdlet psCmdlet, WebSocketTransport webSocketTransport, string? name) : base(psCmdlet, webSocketTransport, name)
  {
    WebSocketTarget = webSocketTransport.WebSocketTarget;
  }

  public WebSocketConnectionInfo(PSCmdlet psCmdlet, WebSocketTarget webSocketTarget, string? name) : this(psCmdlet, new WebSocketTransport(webSocketTarget), name) { }

  public WebSocketConnectionInfo(PSCmdlet psCmdlet, int port, string hostname = "localhost", bool useSSL = true) : this(psCmdlet, new WebSocketTarget(hostname, port, useSSL), null) { }

  public WebSocketConnectionInfo(PSCmdlet psCmdlet, int port, string hostname = "localhost", bool useSSL = true, string? name = null) : this(psCmdlet, new WebSocketTarget(hostname, port, useSSL), name) { }

  public override string ComputerName
  {
    get => WebSocketTarget.Hostname;
    set => throw new NotImplementedException("Cannot set Computername");
  }
}

public class WebSocketTransport : TransportProvider
{
  public readonly WebSocketTarget WebSocketTarget;
  public Uri WebSocketUri => WebSocketTarget.WebSocketUri;
  private readonly ClientWebSocket Client = new();
  private Task? activeHandleDataTask;

  public WebSocketTransport(WebSocketTarget webSocketTarget)
  {
    WebSocketTarget = webSocketTarget;
  }

  public async Task HandleDataFromClient(string message, CancellationToken cancellationToken)
  {
    // We do not add a newline here, it will be added on the other side when passed to the named pipe, because websockets has the concept of messages and we don't need a delimiter, it'll simply signal the client when the bytes are finished writing

    // We can only send one message at a time, so we need to wait for the previous message to finish sending before we can send the next one
    // Ref: https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.sendasync?view=net-7.0

    if (activeHandleDataTask is not null)
    {
      await activeHandleDataTask;
    }

    if (Client.State != WebSocketState.Open)
    {
      throw new PSRemotingTransportException("Websocket is not open, cannot send data");
    }

    activeHandleDataTask = Client.SendAsync(
      Encoding.UTF8.GetBytes(message),
      WebSocketMessageType.Text,
      true,
      cancellationToken
    );
  }

  public async Task<string> HandleDataFromTransport(CancellationToken cancellationToken)
  {
    try
    {
      using MemoryStream receiveStream = new();
      using StreamReader reader = new(receiveStream);
      // Incoming data should be UTF8 encoded string, so we can just pass that to handleDataReceived which doesn't expect a complete PSRP message as far as I can tell
      WebSocketReceiveResult receiveResult;
      do
      {
        byte[] buffer = new byte[8194];
        cancellationToken.ThrowIfCancellationRequested();

        receiveResult = await Client.ReceiveAsync(buffer, cancellationToken);

        await receiveStream.WriteAsync(buffer.AsMemory(0, receiveResult.Count), cancellationToken);
      } while (!receiveResult.EndOfMessage);

      // Rewind the memorystream so it can be read by readline
      cancellationToken.ThrowIfCancellationRequested();
      receiveStream.Position = 0;
      var message = await reader.ReadLineAsync(cancellationToken);

      if (message is null && receiveResult.CloseStatus == WebSocketCloseStatus.NormalClosure)
      {
        // Represents a normal closure, so we just send an empty string to the client which should be a noop
        throw new OperationCanceledException("Websocket closed normally");
      }

      return message ?? throw receiveResult.CloseStatus switch
      {
        WebSocketCloseStatus.Empty => new PSRemotingTransportException("Received null data from websocket, this should never happen."),
        _ => new WebSocketException(
            WebSocketError.ConnectionClosedPrematurely,
            $"Websocket Server sent a close status of {receiveResult.CloseStatus} with reason {receiveResult.CloseStatusDescription}"
        )
      };
    }
    catch (WebSocketException websocketEx)
    {
      throw new PSRemotingTransportException($"Websocket Error while receiving data: {websocketEx.Message}. See InnerException property for more detail.", websocketEx);
    }
  }

  public void CreateConnection(CancellationToken cancellationToken)
  {
    try
    {
      Client
      .ConnectAsync(WebSocketUri, cancellationToken)
      .GetAwaiter()
      .GetResult();
    }
    catch (WebSocketException websocketEx)
    {
      throw new PSRemotingTransportException($"Websocket Error while connecting: {websocketEx.Message}. See InnerException property for more detail.", websocketEx);
    }
  }

  public void Dispose()
  {
    if (Client.State == WebSocketState.Open)
    {
      Client.CloseAsync(
        WebSocketCloseStatus.NormalClosure,
        "Client initiated a normal close of the session, probably due to the PSSession being removed",
        default
      ).GetAwaiter().GetResult();
    }
    Client.Dispose();
    GC.SuppressFinalize(this);
  }
}