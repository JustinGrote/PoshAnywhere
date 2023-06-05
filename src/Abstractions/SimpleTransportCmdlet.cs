using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;
/// <summary>
/// Attempts to connect to the specified host computer and returns
/// a PSSession object representing the remote session. This cmdlet will observe and surface errors up to the point of connection, any further issues will be found on the runspace state itself
/// </summary>
public abstract class SimpleTransportCmdletBase : PSCmdlet
{
  /// <summary>
  /// This is used to track the active connection info task so that we can cancel it if the user hits ctrl+c without affecting the already connected sessions.
  /// </summary>
  private SimpleRunspaceConnectionInfo? CurrentConnectionInfo;
  protected override sealed void ProcessRecord()
  {
    CurrentConnectionInfo = CreateConnectionInfo();
    using var cmdletConnectOutput = CurrentConnectionInfo.CmdletConnect();
    WriteEnumerable<PSSession>(cmdletConnectOutput.GetConsumingEnumerable());
    base.ProcessRecord();
  }

  /// <summary>
  /// This method will be called once for each pipeline object that is received by the cmdlet. Implement this and return a connection info object and it will be processed by the cmdlet
  /// </summary>
  protected abstract SimpleRunspaceConnectionInfo CreateConnectionInfo();

  /// <summary>
  /// StopProcessing override.
  /// </summary>
  protected override sealed void StopProcessing()
  {
    CurrentConnectionInfo?.Cancel();
  }

  internal void WriteEnumerable<T>(IEnumerable<object> collection)
  {
    foreach (var item in collection)
    {
      switch (item)
      {
        case string log:
          WriteVerbose(log);
          break;
        case VerboseRecord log:
          WriteVerbose(log.Message);
          break;
        case DebugRecord log:
          WriteDebug(log.Message);
          break;
        case WarningRecord log:
          WriteWarning(log.Message);
          break;
        case InformationRecord log:
          WriteInformation(log);
          break;
        case ErrorRecord error:
          WriteError(error);
          break;
        case Exception exception:
          ThrowTerminatingError(new ErrorRecord(exception, "UnhandledCustomTransportException", ErrorCategory.ConnectionError, CurrentConnectionInfo));
          break;
        case T expectedObject:
          WriteObject(expectedObject);
          break;
        default:
          throw new InvalidDataException($"The connectionInfo returned an unexpected type. The only valid types are {typeof(T)}, string to indicate a verbose log message, the various Powershell Record types, and exception to be surfaced as a terminating error");
      }
    }
  }
}
