# PoshAnywhere

This module provides multiple additional transport mechanisms for PowerShell remoting.

This is an implementation of the [Custom Transport Feature Introduced in PowerShell 7.3](https://github.com/PowerShell/PowerShell/pull/17011) using the [Named Pipe Connection Custom Transport Example](https://github.com/PaulHigin/NamedPipeConnection) as a reference.

## Custom Transport Flow

```mermaid
sequenceDiagram
  actor A as User
  participant C as NewCustomSessionCmdlet
  participant F as RunspaceFactory
  participant R as Runspace
  participant I as RunspaceConnectionInfo
  participant T as ClientSessionTransportManagerBase
  A->>C: New-CustomSession
  C->>F: CreateRunspace(connectionInfo)
  F->>I: CreateClientSessionTransportManager()
  I->>T: CreateAsync()
  T-->>I: return
  I-->>F: ClientSessionTransportManager
  F-->>C: Runspace
  C->>R: OpenAsync()
  R->>I: Clone()
  I-->>R: RunspaceConnectionInfo
  R-->>C: return

```
