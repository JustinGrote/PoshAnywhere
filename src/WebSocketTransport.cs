using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;
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

  // public override void CloseAsync()
  // {
  //   client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session closed", default);
  // }

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

      MemoryStream receiveStream = new();
      StreamReader reader = new(receiveStream);
      StreamWriter writer = new(receiveStream)
      {
        AutoFlush = true
      };
      WebSocketReceiveResult receiveResult;
      // Start reader loop.
      do
      {
        // Incoming data should be UTF8 encoded string, so we can just pass that to handleDataReceived which doesn't expect a complete PSRP message as far as I can tell
        byte[] buffer = new byte[8194];
        receiveResult = client.ReceiveAsync(buffer, default).GetAwaiter().GetResult();
        receiveStream.Write(buffer, 0, receiveResult.Count);
      } while (!receiveResult.EndOfMessage);

      // Newline indicates the end of the message for the readline handler
      writer.Write('\n');
      // Rewind the memorystream so it can be read by readline
      receiveStream.Position = 0;
      var data = reader.ReadLine();

      if (data is null)
      {
        throw new InvalidDataException("Received null data from websocket, this should never happen.");
      }
      Console.WriteLine($"WEBSOCKET SERVER RECEIVE: {data}");
      data += '\n';
      HandleDataReceived(data);
    }
    catch (ObjectDisposedException)
    {
      Console.WriteLine("Reader thread ended.");
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

    Console.WriteLine($"Websocket CLIENT SEND: {data}");

    // We do not add a newline here, it will be added on the other side when passed to the named pipe, because websockets has the concept of messages and we don't need a delimiter, it'll simply signal the client when the bytes are finished writing
    Client.SendAsync(
      Encoding.GetBytes(data),
      WebSocketMessageType.Text,
      true,
      default
    ).GetAwaiter().GetResult();
  }
}