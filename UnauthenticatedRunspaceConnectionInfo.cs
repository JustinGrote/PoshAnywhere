using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;

namespace PoshTransports;

/// <summary>
/// A RunspaceConnectionInfo that does not require or present authentication information
/// </summary>
public abstract class UnauthenticatedRunspaceConnectionInfo : RunspaceConnectionInfo
{
  // These first few overrides are not relevant to what we are doing here

  public override PSCredential? Credential
  {
    get => null;
    set => throw new NotImplementedException();
  }

  public override AuthenticationMechanism AuthenticationMechanism
  {
    get => AuthenticationMechanism.Default;
    set => throw new NotImplementedException();
  }

  public override string CertificateThumbprint
  {
    get => throw new NotImplementedException();
    set => throw new NotImplementedException();
  }
}
