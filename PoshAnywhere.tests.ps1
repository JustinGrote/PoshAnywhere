Describe 'Websocket Client' {
  BeforeAll {
    $SCRIPT:psanywhereServer = Start-ThreadJob -Scriptblock { ./PoshAnywhereServer.ps1 }
    Import-Module $PSScriptRoot/bin/Debug/net7.0/publish/PoshAnywhere.dll
  }
  It 'Should connect to the server' {
    New-WebSocketSession
    | Should -Not -BeNullOrEmpty
  }
}
AfterAll {
  Stop-Job $psanywhereServer
}