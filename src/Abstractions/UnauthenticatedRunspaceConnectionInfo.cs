using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PoshTransports;

/// <summary>
/// A RunspaceConnectionInfo that does not require or present authentication information
/// </summary>
public abstract class UnauthenticatedRunspaceConnectionInfo : RunspaceConnectionInfo
{
  // These are null overrides that are required to be implemented but we do not use.

  public override PSCredential? Credential
  {
    get => null;
    set => throw new NotImplementedException("Cannot set Credential");
  }

  public override AuthenticationMechanism AuthenticationMechanism
  {
    get => AuthenticationMechanism.Default;
    set => throw new NotImplementedException("Cannot set AuthenticationMechanism");
  }

  public override string CertificateThumbprint
  {
    get => throw new NotImplementedException("Cannot fetch CertificateThumbprint");
    set => throw new NotImplementedException("Cannot set CertificateThumbprint");
  }

  /// <summary>
  /// You may need to override this with an actual clone of your ConnectionInfo if you run into an edge case.
  /// </summary>
  public override RunspaceConnectionInfo Clone() => this;
}
