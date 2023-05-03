using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;

namespace PoshTransports;

/// <summary>
/// Attaches to a TCP port where PS remoting messages are provided
/// </summary>
public class TcpConnectionInfo : UnauthenticatedRunspaceConnectionInfo
{
  internal readonly int Port;
  internal readonly string Hostname;
  internal readonly TcpClient Client;
  public TcpConnectionInfo(int port, string hostname = "localhost")
  {
    // We want a client at this point but we don't want to connect until the transport manager initiates it
    Port = port;
    Hostname = hostname;
    Client = new();
  }

  public override BaseClientSessionTransportManager CreateClientSessionTransportManager(
    Guid instanceId,
    string sessionName,
    PSRemotingCryptoHelper cryptoHelper
  ) => new TcpClientSessionTransportManager(
    instanceId,
    sessionName,
    cryptoHelper,
    this
  );

  public override string ComputerName
  {
    get => $"tcp://{Hostname}:{Port}";
    set => throw new NotImplementedException("Cannot fetch Computername");
  }

  /// <summary>
  /// Create shallow copy of NamedPipeInfo object.
  /// </summary>
  public override RunspaceConnectionInfo Clone()
  {
    return new TcpConnectionInfo(Port, Hostname);
  }


}
