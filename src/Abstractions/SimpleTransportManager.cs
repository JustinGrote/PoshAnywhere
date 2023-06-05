using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;
using System.Text;

namespace PoshTransports;

/// <summary>
/// This interface provides a simple way to implement a PSRP custom transport provider. You should implement this interface and pass it to the constructor of a new SimpleRunspaceConnectionInfo object. You can safely throw any exception which will flag the runspace as broken with your exception as the reason, or an error reported to the upstream PSCmdlet or host responsible for creating the runspace if the runspace has not connected yet. Use Dispose to clean up any resources you have allocated.
/// </summary>
public interface TransportProvider : IDisposable
{
  /// <summary>
  /// This function will be called whenever a new PSRP message arrives from the client PSRP engine. You should generally just pass the message to your transport
  /// </summary>
  Task HandleDataFromClient(string data, CancellationToken cancellationToken = default);

  /// <summary>
  /// <para>
  /// This function MUST await and return the next PSRP message in XML form to send to the client and return the PSRP message as a string. It will automatically be looped on a nonblocking thread. If the function does not block, it will be called as fast as possible, which will likely cause high CPU usage.
  /// Some examples of how to implement this function:
  /// - Await a task or async function that returns the next message
  /// - Register for a event that your transport emits and act on that event.
  /// - Implement a while (true) loop that periodically polls your transport for the next message
  /// </para>
  /// <para>Your implementation should also handle the CancellationToken, and throw an OperationCanceledException to signal that you have gracefully closed the connection.</para>
  /// </summary>
  Task<string> HandleDataFromTransport(CancellationToken cancellationToken = default);

  /// <summary>
  /// This optional method is called to setup the connection. You should initialize your transport here.
  /// </summary>
  void CreateConnection(CancellationToken cancellationToken = default) { }

  /// <summary>
  /// This optional method will be called when the client wants to close the connection (for example, when Remove-PSSession is called). You should signal your transport to close the connection. The client will likely send at least one more PSRP Close message to your server, so your transport should be able to handle that gracefully before closing.
  /// </summary>
  void CloseConnection(CancellationToken cancellationToken = default) { }
}

/// <summary>
/// This transport base lets you simply implement two functions: HandleDataFromClient and ReceiveDataFromTransport. It will handle the rest of the PSRP protocol for you. You may optionally initialize the additional CreateAsync, CloseAsync functions. If your transport has an issue that requires it to close, throw a PSRemotingTransportException, any other exceptions will be treated as a critical error and will crash the program.
/// </summary>
public class SimpleTransportManager : ClientSessionTransportManagerBase
{
  /// <summary>
  /// Use this CancellationToken where usable, it is called when the client wants to cancel the transport operation.
  /// </summary>
  public readonly TransportProvider TransportProvider;
  internal readonly SimpleRunspaceConnectionInfo ConnectionInfo;
  internal TaskCompletionSource<Runspace> CreateRunspaceTCS => ConnectionInfo.CreateRunspaceTaskCompletionSource;
  internal CancellationToken CancellationToken => ConnectionInfo.CancellationToken;
  private Task? ReceiveHandlerTask;

  public SimpleTransportManager(Guid runspaceId, PSRemotingCryptoHelper cryptoHelper, SimpleRunspaceConnectionInfo connectionInfo, TransportProvider transportProvider) : base(runspaceId, cryptoHelper)
  {
    SetMessageWriter(new ActionTextWriter(HandleDataFromClient));
    ConnectionInfo = connectionInfo;
    TransportProvider = transportProvider;
  }

  public override void CreateAsync()
  {
    try
    {
      TransportProvider.CreateConnection();
      // Runs the data transport handler on a new fire-and-forget thread. A cancellationToken will signal the end of this task
      ReceiveHandlerTask = Task.Run(TransportDataReceiveHandler, CancellationToken);
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.CreateShellEx);
    }
  }

  public override void CloseAsync()
  {
    Console.WriteLine("CloseAsync");
    try
    {
      base.CloseAsync();
      TransportProvider.CloseConnection(CancellationToken);
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.CloseShellOperationEx);
    }
  }

  protected override void CleanupConnection()
  {
    Console.WriteLine("CleanupConnectionBegin");
    try
    {
      TransportProvider.Dispose();
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.CloseShellOperationEx);
    }
    Console.WriteLine("CleanupConnectionEnd");
  }

  private async Task TransportDataReceiveHandler()
  {
    SendOneItem();
    while (!CancellationToken.IsCancellationRequested)
    {
      try
      {
        var data = await TransportProvider.HandleDataFromTransport(CancellationToken);
        HandleDataReceived(data);
      }
      catch (OperationCanceledException)
      {
        // A cancellation means we want to end the loop
        return;
      }
      catch (Exception ex)
      {
        HandleTransportException(ex, TransportMethodEnum.ReceiveShellOutputEx);
      }
    }
  }

  protected void HandleDataFromClient(string data)
  {
    try
    {
      TransportProvider.HandleDataFromClient(data, CancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.SendShellInputEx);
    }
  }

  private void HandleTransportException(Exception ex, TransportMethodEnum method = TransportMethodEnum.Unknown)
  {
    if (ex is OperationCanceledException)
    {
      throw ex;
    }

    if (ex is ObjectDisposedException)
    {
      // This is the normal expectation when the client closes the connection and should stop the task
      return;
    }

    PSRemotingTransportException transportEx = ex switch
    {
      PSRemotingTransportException tEx => tEx,
      _ => new PSRemotingTransportException(
        $"Unhandled Exception: {ex.Message}",
        ex
      )
    };

    // This should signal the client to close the connection
    RaiseErrorHandler(
      new TransportErrorOccuredEventArgs(
        transportEx, method
      )
    );

    // We expose the original exception to the cmdlet if we are still in the connecting phase
    CreateRunspaceTCS.TrySetException(ex);
  }

  protected override void Dispose(bool disposing)
  {
    TransportProvider.Dispose();
    // TODO: Wait for the receive handler task to finish to confirm we dont have an outstanding issue
    base.Dispose(disposing);
  }
}

/// <summary>
/// Calls the specified Action whenever a new line is written to the textWriter. Used as an adapter to receive PSRP client messages.
/// </summary>
class ActionTextWriter : TextWriter
{
  private readonly Action<string> Action;
  public ActionTextWriter(Action<string> action)
  {
    Action = action;
  }

  public override Encoding Encoding => Encoding.UTF8;

  public override void WriteLine(string? data)
  {
    if (data is not null)
    {
      Action(data);
    }
  }
}