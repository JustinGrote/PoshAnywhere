using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;
/// <summary>
/// Attempts to connect to the specified host computer and returns
/// a PSSession object representing the remote session. This cmdlet will observe and surface errors up to the point of connection, any further issues will be found on the runspace state itself
/// </summary>
[Cmdlet(VerbsCommon.New, "WebSocketSession")]
[OutputType(typeof(PSSession))]
public sealed class NewWebSocketSessionCmdlet : PSCmdlet
{
  private WebSocketConnectionInfo? ConnectionInfo;

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

  protected override void BeginProcessing()
  {
    ConnectionInfo = new WebSocketConnectionInfo(
      this,
      Port ?? 7073,
      Hostname ?? "localhost",
      !NoSSL
    );

    WriteObject(
      ConnectionInfo.Connect()
    );
  }

  /// <summary>
  /// StopProcessing override.
  /// </summary>
  protected override void StopProcessing()
  {
    ConnectionInfo?.Cancel();
  }
}
