using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;
/// <summary>
/// Attempts to connect to the specified host computer and returns
/// a PSSession object representing the remote session.
/// </summary>
[Cmdlet(VerbsCommon.New, "StreamSession")]
[OutputType(typeof(PSSession))]
public sealed class NewStreamSessionCmdlet : PSCmdlet
{
  private StreamConnectionInfo? _connectionInfo;
  private Runspace? _runspace;
  private ManualResetEvent? _openAsync;

  /// <summary>
  /// The named pipe to connect to. For powershell sessions, this is something like "PSHost.133276154520353422.6344.DefaultAppDomain.pwsh" or, if you started pwsh with -CustomPipeName, just the name of the pipe e.g. "MyCustomPipeName"
  /// </summary>
  [Parameter(Position = 0, Mandatory = true, ValueFromPipeline = true)]
  [ValidateNotNullOrEmpty]
  public Stream? Stream { get; set; }

  /// <summary>
  /// EndProcessing override.
  /// </summary>
  protected override void BeginProcessing()
  {
    // Convert ConnectingTimeout value from seconds to milliseconds.
    _connectionInfo = new StreamConnectionInfo(Stream!);

    _runspace = RunspaceFactory.CreateRunspace(
      connectionInfo: _connectionInfo,
      host: Host,
      typeTable: TypeTable.LoadDefaultTypeFiles(),
      applicationArguments: null,
      name: "Stream"
    );

    // The HandleRunspaceStateChanged will release this thread lock once the runspace is opened (or fails to open)
    _openAsync = new ManualResetEvent(false);
    _runspace.StateChanged += HandleRunspaceStateChanged;

    try
    {
      _runspace.OpenAsync();
      _openAsync.WaitOne();

      WriteObject(
        PSSession.Create(
          runspace: _runspace,
          transportName: "StreamTransport",
          psCmdlet: this
        )
      );
    }
    finally
    {
      _openAsync.Dispose();
    }
  }

  /// <summary>
  /// StopProcessing override.
  /// </summary>
  protected override void StopProcessing()
  {
    _runspace?.Dispose();
  }

  private void HandleRunspaceStateChanged(
    object? source,
    RunspaceStateEventArgs stateEventArgs)
  {
    switch (stateEventArgs.RunspaceStateInfo.State)
    {
      case RunspaceState.Opened:
      case RunspaceState.Closed:
      case RunspaceState.Broken:
        if (_runspace is null) { break; }
        _runspace.StateChanged -= HandleRunspaceStateChanged;
        ReleaseWait();
        break;
    }
  }

  private void ReleaseWait()
  {
    try
    {
      _openAsync?.Set();
    }
    catch (ObjectDisposedException)
    { }
  }
}
