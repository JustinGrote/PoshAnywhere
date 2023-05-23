
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;

public abstract class SimpleRunspaceConnectionInfo : UnauthenticatedRunspaceConnectionInfo
{
  internal readonly TaskCompletionSource<Runspace> CreateRunspaceTaskCompletionSource = new();
  private readonly CancellationTokenSource CancellationTokenSource = new();
  private readonly PSCmdlet PSCmdlet;
  private readonly string? Name;
  public CancellationToken CancellationToken => CancellationTokenSource.Token;

  /// <summary>
  /// This task completes when the runspace is ready to use and be wrapped in a PSSession
  /// </summary>
  public Task<Runspace> RunspaceConnectedTask => CreateRunspaceTaskCompletionSource.Task;

  protected SimpleRunspaceConnectionInfo(PSCmdlet psCmdlet, string? name)
  {
    PSCmdlet = psCmdlet;
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

  public async Task<PSSession> ConnectAsync()
  {
    Runspace Runspace = CreateRunspace();
    try
    {
      Runspace.OpenAsync();
      await RunspaceConnectedTask;
    }
    catch (TaskCanceledException cancelEx)
    {
      throw new PipelineStoppedException(cancelEx.Message, cancelEx);
    }
    return PSSession.Create(
      runspace: Runspace,
      transportName: "WebSocket",
      psCmdlet: PSCmdlet
    );
  }

  public PSSession Connect() => ConnectAsync().GetAwaiter().GetResult();

  public void Cancel()
  {
    CancellationTokenSource.Cancel();
    CreateRunspaceTaskCompletionSource.TrySetCanceled(CancellationToken);
  }
}