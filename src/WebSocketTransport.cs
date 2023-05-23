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

public class WebSocketConnectionInfo : SimpleRunspaceConnectionInfo
{
  internal readonly WebSocketTarget WebSocketTarget;
  internal Uri WebSocketUri => WebSocketTarget.WebSocketUri;

  public WebSocketConnectionInfo(PSCmdlet psCmdlet, int port, string hostname = "localhost", bool useSSL = true) : base(psCmdlet)
  {
    WebSocketTarget = new(hostname, port, useSSL);
  }

  public override BaseClientSessionTransportManager CreateClientSessionTransportManager(
    Guid instanceId,
    string sessionName,
    PSRemotingCryptoHelper cryptoHelper
  ) => new SimpleTransportManager(
    instanceId,
    cryptoHelper,
    this,
    new WebSocketTransport(this)
  );

  public override string ComputerName
  {
    get => WebSocketTarget.Hostname;
    set => throw new NotImplementedException("Cannot fetch Computername");
  }
}

class WebSocketTransport : TransportProvider
{
  private readonly ClientWebSocket Client = new();
  private Task? activeHandleDataTask;

  private readonly WebSocketConnectionInfo ConnectionInfo;
  CancellationToken CancellationToken => ConnectionInfo.CancellationToken;
  Uri WebSocketUri => ConnectionInfo.WebSocketUri;

  public WebSocketTransport(WebSocketConnectionInfo connectionInfo)
  {
    ConnectionInfo = connectionInfo;
  }

  public void HandleDataFromClient(string data)
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

  public async Task<string> ReceiveDataFromTransport()
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
        _ => new WebSocketException(
            WebSocketError.ConnectionClosedPrematurely,
            $"Server sent a unexpected close status of {receiveResult.CloseStatus} with reason {receiveResult.CloseStatusDescription}"
        )
      };
    }
    catch (WebSocketException websocketEx)
    {
      throw new PSRemotingTransportException($"Websocket Error while receiving data: {websocketEx.Message}. See InnerException property for more detail.", websocketEx);
    }
  }

  public void CreateConnection()
  {
    try
    {
      Client.ConnectAsync(WebSocketUri, CancellationToken).GetAwaiter().GetResult();
    }
    catch (WebSocketException websocketEx)
    {
      throw new PSRemotingTransportException($"Websocket Error while connecting: {websocketEx.Message}. See InnerException property for more detail.", websocketEx);
    }
  }

  public void CloseConnection()
  {
    Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session closed", new CancellationTokenSource(3000).Token);
  }

  protected void CleanupConnection()
  {
    Client.Dispose();
  }

  public void Dispose()
  {
    CleanupConnection();
  }
}