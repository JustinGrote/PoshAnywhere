using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;
/// <summary>
/// Attempts to connect to the specified host computer and returns
/// a PSSession object representing the remote session.
/// </summary>
[Cmdlet(VerbsCommon.New, "WebSocketSession")]
[OutputType(typeof(PSSession))]
public sealed class NewWebSocketSessionCmdlet : PSCmdlet
{
  private WebSocketConnectionInfo? ConnectionInfo;
  private Runspace? Runspace;
  private readonly CancellationTokenSource CancelSource = new();

  /// <summary>
  /// The websocket port to connect to.
  /// </summary>
  [Parameter()]
  [ValidateNotNullOrEmpty]
  public int? Port = 7073;

  /// <summary>
  /// The named pipe to connect to. For powershell sessions, this is something like "PSHost.133276154520353422.6344.DefaultAppDomain.pwsh" or, if you started pwsh with -CustomPipeName, just the name of the pipe e.g. "MyCustomPipeName"
  /// </summary>
  [Parameter()]
  [ValidateNotNullOrEmpty]
  public string? Hostname = "localhost";

  /// <summary>
  /// Specify to use plaintext http instead of ssl
  /// </summary>
  [Parameter()]
  public SwitchParameter NoSSL;

  /// <summary>
  /// EndProcessing override.
  /// </summary>
  protected override void BeginProcessing()
  {
    // Convert ConnectingTimeout value from seconds to milliseconds.
    ConnectionInfo = new WebSocketConnectionInfo(
      this,
      Port ?? 7073,
      Hostname ?? "localhost",
      !NoSSL,
      CancelSource.Token
    );

    Runspace = RunspaceFactory.CreateRunspace(
      ConnectionInfo,
      Host,
      TypeTable.LoadDefaultTypeFiles(),
      null,
      $"WebSocket-{Hostname}:{Port}"
    );

    // Wait for runspace to be usable
    ManualResetEvent waitForRunspaceUsable = new(false);
    EventHandler<RunspaceStateEventArgs>? handler = null;
    Runspace.StateChanged += handler = (source, stateEventArgs) =>
    {
      switch (stateEventArgs.RunspaceStateInfo.State)
      {
        case RunspaceState.Opened:
        case RunspaceState.Closed:
        case RunspaceState.Broken:
          if (Runspace is null) { break; }
          Runspace.StateChanged -= handler;
          waitForRunspaceUsable.Set();
          break;
      }
    };

    WriteVerbose($"Connecting to websocket host: {ConnectionInfo.WebSocketUri}");
    try
    {
      Runspace.OpenAsync();
    }
    catch (Exception err)
    {
      ThrowTerminatingError(
        new ErrorRecord(
          err,
          "WebSocketTransportError",
          ErrorCategory.ConnectionError,
          ConnectionInfo
        )
      );
    }
    // We use this instead of Open() to make this cmdlet cancellable
    while (!waitForRunspaceUsable.WaitOne(500)) { }

    if (Runspace.RunspaceAvailability != RunspaceAvailability.None)
    {
      WriteObject(
        PSSession.Create(
          runspace: Runspace,
          transportName: "WebSocket",
          psCmdlet: this
        )
      );
    }
  }

  /// <summary>
  /// StopProcessing override.
  /// </summary>
  protected override void StopProcessing()
  {
    WriteVerbose("Cancelling New-WebSocketSession");
    CancelSource.Cancel();
    Runspace?.Dispose();
  }
}
