using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;
/// <summary>
/// Attempts to connect to the specified host computer and returns
/// a PSSession object representing the remote session.
/// </summary>
[Cmdlet(VerbsCommon.New, "TcpSession")]
[OutputType(typeof(PSSession))]
public sealed class NewTcpSessionCommand : PSCmdlet
{
  private TcpConnectionInfo? _connectionInfo;
  private Runspace? _runspace;
  private ManualResetEvent? _openAsync;

  /// <summary>
  /// Name of host computer to connect to.
  /// </summary>
  [Parameter(Position = 0, Mandatory = true)]
  [ValidateNotNullOrEmpty]
  public int Port { get; set; }

  /// <summary>
  /// Optional value in seconds that limits the time allowed for a connection to be established.
  /// </summary>
  [Parameter]
  [ValidateNotNullOrEmpty]
  public string Hostname { get; set; } = "localhost";

  /// <summary>
  /// Optional name for the new PSSession.
  /// </summary>
  [Parameter]
  [ValidateNotNullOrEmpty]
  public string? Name { get; set; }

  /// <summary>
  /// EndProcessing override.
  /// </summary>
  protected override void BeginProcessing()
  {
    // Convert ConnectingTimeout value from seconds to milliseconds.
    _connectionInfo = new TcpConnectionInfo(Port, Hostname);

    _runspace = RunspaceFactory.CreateRunspace(
        connectionInfo: _connectionInfo,
        host: Host,
        typeTable: TypeTable.LoadDefaultTypeFiles(),
        applicationArguments: null,
        name: Name);

    _openAsync = new ManualResetEvent(false);
    _runspace.StateChanged += HandleRunspaceStateChanged;

    try
    {
      _runspace.OpenAsync();
      _openAsync.WaitOne();

      WriteObject(
          PSSession.Create(
              runspace: _runspace,
              transportName: "PSNPTest",
              psCmdlet: this));
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
    // Currently no action
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
