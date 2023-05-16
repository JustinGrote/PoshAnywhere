using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
namespace PoshTransports;

internal sealed class ProcessTransportManager : ClientSessionTransportManagerBase
{
  public ProcessTransportManager(Guid runspaceId, PSRemotingCryptoHelper cryptoHelper) : base(runspaceId, cryptoHelper)
  {
  }

  public override void CreateAsync()
  {
    throw new NotImplementedException();
  }

  protected override void CleanupConnection()
  {
    throw new NotImplementedException();
  }
}