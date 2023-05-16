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
  private WebSocketConnectionInfo? _connectionInfo;
  private Runspace? _runspace;

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
    _connectionInfo = new WebSocketConnectionInfo(
      port: Port ?? 7073,
      hostname: Hostname ?? "localhost",
      useSSL: !NoSSL
    );

    _runspace = RunspaceFactory.CreateRunspace(
      connectionInfo: _connectionInfo,
      host: Host,
      typeTable: TypeTable.LoadDefaultTypeFiles(),
      applicationArguments: null,
      name: $"WebSocket-{Hostname}:{Port}"
    );

    // Wait for runspace to be usable
    ManualResetEvent waitForRunspaceUsable = new(false);
    EventHandler<RunspaceStateEventArgs>? handler = null;
    _runspace.StateChanged += handler = (source, stateEventArgs) =>
    {
      switch (stateEventArgs.RunspaceStateInfo.State)
      {
        case RunspaceState.Opened:
        case RunspaceState.Closed:
        case RunspaceState.Broken:
          if (_runspace is null) { break; }
          _runspace.StateChanged -= handler;
          waitForRunspaceUsable.Set();
          break;
      }
    };

    WriteVerbose($"Connecting to websocket host: {_connectionInfo.WebSocketUri}");

    _runspace.OpenAsync();
    // We use this instead of Open() to make this cmdlet cancellable
    while (!waitForRunspaceUsable.WaitOne(500)) { }

    WriteObject(
      PSSession.Create(
        runspace: _runspace,
        transportName: "WebSocket",
        psCmdlet: this
      )
    );
  }

  /// <summary>
  /// StopProcessing override.
  /// </summary>
  protected override void StopProcessing()
  {
    _runspace?.Dispose();
  }
}
