using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;
using System.Text;

namespace PoshTransports;

/// <summary>
/// This interface provides a simple way to implement a PSRP custom transport provider. You should implement this interface and pass it to the constructor of a new SimpleRunspaceConnectionInfo object. You can safely throw any exception which will flag the runspace as broken with your exception as the reason, or an error reported to the upstream PSCmdlet or host responsible for creating the runspace if the runspace has not connected yet.
/// </summary>
public interface TransportProvider : IDisposable
{
  /// <summary>
  /// This function will be called whenever a new PSRP message arrives from the client. You should generally just pass the message to your transport
  /// </summary>
  void HandleDataFromClient(string data);

  /// <summary>
  /// This function MUST await and return the next PSRP message in XML form to send to the client and return the PSRP message as a string. It will automatically be looped on a nonblocking thread. If the function does not block, it will be called as fast as possible, which will likely cause high CPU usage.
  /// Some examples of how to implement this function:
  /// - Await a task or async function that returns the next message
  /// - Register for a event that your transport emits and act on that event.
  /// - Implement a while (true) loop that periodically polls your transport for the next message
  /// </summary>
  Task<string> ReceiveDataFromTransport();

  /// <summary>
  /// This optional method is called to setup the connection. You should initialize your transport here.
  /// </summary>
  void CreateConnection() { }

  /// <summary>
  /// This optional method will be called when the client wants to close the connection (for example, when Remove-PSSession is called). You should signal your transport to close the connection. The client will likely send at least one more PSRP Close message to your server, so your transport should be able to handle that gracefully before closing.
  /// </summary>
  void CloseConnection() { }

  /// <summary>
  /// This optional method will be called after the connection is closed. You should dispose and clean up any resources here.
  /// </summary>
  void CleanupConnection() { }
}

/// <summary>
/// This transport base lets you simply implement two functions: HandleDataFromClient and ReceiveDataFromTransport. It will handle the rest of the PSRP protocol for you. You may optionally initialize the additional CreateAsync, CloseAsync functions. If your transport has an issue that requires it to close, throw a PSRemotingTransportException, any other exceptions will be treated as a critical error and will crash the program.
/// </summary>
public class SimpleTransportManager : ClientSessionTransportManagerBase
{
  /// <summary>
  /// Use this CancellationToken where usable, it is called when the client wants to cancel the transport operation.
  /// </summary>
  public readonly CancellationToken CancellationToken;

  public readonly TaskCompletionSource<Runspace> CreateRunspaceTCS;

  public readonly TransportProvider TransportProvider;
  bool CloseRequested;

  public SimpleTransportManager(Guid runspaceId, PSRemotingCryptoHelper cryptoHelper, SimpleRunspaceConnectionInfo connectionInfo, TransportProvider transportProvider) : base(runspaceId, cryptoHelper)
  {
    SetMessageWriter(new ActionTextWriter(HandleDataFromClient));
    CancellationToken = connectionInfo.CancellationToken;
    CreateRunspaceTCS = connectionInfo.CreateRunspaceTaskCompletionSource;
    TransportProvider = transportProvider;
  }

  public override void CreateAsync()
  {
    try
    {
      TransportProvider.CreateConnection();
      // Runs the data transport handler on a new thread
      var receiveTask = Task.Factory.StartNew(HandleTransportDataReceived, TaskCreationOptions.LongRunning);
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.CreateShellEx);
    }
  }

  public override void CloseAsync()
  {
    try
    {
      base.CloseAsync();
      TransportProvider.CloseConnection();
      // Stop the message receive loop
      CloseRequested = true;
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.CloseShellOperationEx);
    }
  }

  protected override void CleanupConnection()
  {
    try
    {
      TransportProvider.CleanupConnection();
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.CloseShellOperationEx);
    }
  }

  private async void HandleTransportDataReceived()
  {
    // Makes it easier to debug. Since it is started longrunning it is a dedicated thread and wont have this name in the threadpool
    Thread.CurrentThread.Name = "TransportManager-HandleTransportDataReceived";
    SendOneItem();

    try
    {
      while (!CloseRequested)
      {
        HandleDataReceived(
          await TransportProvider.ReceiveDataFromTransport()
        );
      }
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.ReceiveShellOutputEx);
    }
  }

  protected void HandleDataFromClient(string data)
  {
    try
    {
      TransportProvider.HandleDataFromClient(data);
    }
    catch (Exception ex)
    {
      HandleTransportException(ex, TransportMethodEnum.SendShellInputEx);
    }
  }

  private void HandleTransportException(Exception ex, TransportMethodEnum method = TransportMethodEnum.Unknown)
  {
    if (ex is ObjectDisposedException)
    {
      // This is the normal expectation when the client closes the connection and should stop the task.
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

    CreateRunspaceTCS.TrySetException(ex);
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