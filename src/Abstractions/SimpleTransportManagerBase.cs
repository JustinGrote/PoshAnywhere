
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
using System.Text;

namespace PoshTransports;

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

/// <summary>
/// This transport base lets you simply implement two functions: HandleDataFromClient and ReceiveDataFromTransport. It will handle the rest of the PSRP protocol for you. You may optionally initialize the additional CreateAsync, CloseAsync functions. If your transport has an issue that requires it to close, throw a PSRemotingTransportException, any other exceptions will be treated as a critical error and will crash the program.
/// </summary>
abstract class SimpleTransportManagerBase : ClientSessionTransportManagerBase
{
  /// <summary>
  /// This function will be called whenever a new PSRP message arrives from the client. You should generally just pass the message to your transport
  /// </summary>
  protected abstract void HandleDataFromClient(string data);

  /// <summary>
  /// This function MUST await and return the next PSRP message in XML form to send to the client. It will automatically be looped on a nonblocking thread. If the function does not block, it will be called as fast as possible, which will likely cause high CPU usage.
  /// Some examples of how to implement this function:
  /// - Await a task or async function that returns the next message
  /// - Register for a event that your transport emits and act on that event.
  /// - Implement a while (true) loop that periodically polls your transport for the next message
  /// </summary>
  protected abstract Task<string> ReceiveDataFromTransport();

  protected SimpleTransportManagerBase(Guid runspaceId, PSRemotingCryptoHelper cryptoHelper) : base(runspaceId, cryptoHelper)
  {
    SetMessageWriter(new ActionTextWriter(HandleDataFromClient));
  }

  /// <summary>
  /// Initializes the Client Transport. If you override this method for more setup, make sure to call base.CreateAsync() at the end of your function.
  /// </summary>
  public override void CreateAsync()
  {
    // Runs the data transport handler on a new thread
    Task.Factory.StartNew(WaitForDataFromTransport, TaskCreationOptions.LongRunning);
  }

  private async void WaitForDataFromTransport()
  {
    SendOneItem();
    try
    {
      while (true)
      {
        HandleDataReceived(
          await ReceiveDataFromTransport()
        );
      }
    }
    catch (ObjectDisposedException)
    {
      // This is the normal expectation when the client closes the connection and should stop the task.
    }
    catch (PSRemotingTransportException transportError)
    {
      RaiseErrorHandler(
        new TransportErrorOccuredEventArgs(
          transportError, TransportMethodEnum.CloseShellOperationEx
        )
      );
      CloseAsync();
      CleanupConnection();
      Dispose();
    }
    catch (Exception err)
    {
      throw new InvalidOperationException($"Critical Unhandled Non-TransportException occurred in custom transport: {err.Message}. More detail in InnerException property", err);
    }
  }
}
