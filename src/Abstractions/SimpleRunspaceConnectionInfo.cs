
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;

namespace PoshTransports;

public abstract class SimpleRunspaceConnectionInfo : UnauthenticatedRunspaceConnectionInfo
{
  internal readonly TaskCompletionSource<Runspace> CreateRunspaceTaskCompletionSource = new();
  private readonly CancellationTokenSource CancellationTokenSource = new();
  private readonly TransportProvider TransportProvider;
  private readonly PSCmdlet PSCmdlet;
  private readonly BlockingCollection<object> ConnectionResult = new();
  private readonly string? Name;
  public CancellationToken CancellationToken => CancellationTokenSource.Token;

  /// <summary>
  /// This task completes when the runspace is ready to use and be wrapped in a PSSession
  /// </summary>
  public Task<Runspace> RunspaceConnectedTask => CreateRunspaceTaskCompletionSource.Task;

  protected SimpleRunspaceConnectionInfo(PSCmdlet psCmdlet, TransportProvider transportProvider, string? name)
  {
    PSCmdlet = psCmdlet;
    TransportProvider = transportProvider;
    Name = name;
    CancellationToken.Register(() => CreateRunspaceTaskCompletionSource.TrySetCanceled());
  }

  public void HandleConnectionException(Exception exception) => CreateRunspaceTaskCompletionSource.SetException(exception);

  public Runspace CreateRunspace()
  {
    Runspace Runspace = RunspaceFactory.CreateRunspace(
      this,
      PSCmdlet.Host,
      TypeTable.LoadDefaultTypeFiles(),
      null,
      Name
    );

    EventHandler<RunspaceStateEventArgs>? handler = null;
    Runspace.StateChanged += handler = (_, stateEventArgs) =>
    {
      switch (stateEventArgs.RunspaceStateInfo.State)
      {
        case RunspaceState.Opened:
        case RunspaceState.Closed:
        case RunspaceState.Broken:
          Runspace.StateChanged -= handler;
          // This will resume execution of the cmdlet that should be waiting on this Task.
          CreateRunspaceTaskCompletionSource.TrySetResult(Runspace);
          break;
      }
    };

    return Runspace;
  }

  public override BaseClientSessionTransportManager CreateClientSessionTransportManager(
    Guid instanceId,
    string sessionName,
    PSRemotingCryptoHelper cryptoHelper
  ) => new SimpleTransportManager(
    instanceId,
    cryptoHelper,
    this,
    TransportProvider
  );

  public async Task<PSSession> ConnectAsync()
  {
    Runspace Runspace = CreateRunspace();
    try
    {
      Runspace.OpenAsync();
      await RunspaceConnectedTask;
    }
    catch (OperationCanceledException cancelEx)
    {
      throw new PipelineStoppedException(cancelEx.Message, cancelEx);
    }

    return PSSession.Create(
      runspace: Runspace,
      transportName: "WebSocket",
      psCmdlet: PSCmdlet
    );
  }

  /// <summary>
  /// Produces a blocking collection that includes all logs from the connection process and finally a pssession object. The PSCmdlet should process these objects within its own scope and distribute to the appropriate verbose/warning/debug streams, preferably with the WriteEnumerable method.
  /// </summary>
  public BlockingCollection<object> CmdletConnect()
  {
    Task.Run(CmdletConnectAsync);
    return ConnectionResult;
  }

  async Task CmdletConnectAsync()
  {
    try
    {
      ConnectionResult.Add(await ConnectAsync());
    }
    catch (OperationCanceledException ex)
    {
      ConnectionResult.Add("The connection was canceled: " + ex.Message);
    }
    finally
    {
      // Will unblock the PSCmdlet
      ConnectionResult.CompleteAdding();
    }
  }

  public void Cancel()
  {
    CancellationTokenSource.Cancel();
    CreateRunspaceTaskCompletionSource.TrySetCanceled(CancellationToken);
  }
}