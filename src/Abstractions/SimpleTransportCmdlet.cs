using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;
/// <summary>
/// Attempts to connect to the specified host computer and returns
/// a PSSession object representing the remote session. This cmdlet will observe and surface errors up to the point of connection, any further issues will be found on the runspace state itself
/// </summary>
public abstract class SimpleTransportCmdletBase : PSCmdlet
{
  internal SimpleRunspaceConnectionInfo? ConnectionInfo { get; set; }

  /// <summary>
  /// StopProcessing override.
  /// </summary>
  protected override void StopProcessing()
  {
    ConnectionInfo?.Cancel();
  }
}
