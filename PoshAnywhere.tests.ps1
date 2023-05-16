Describe 'Websocket Client' {
  BeforeEach {
    $SCRIPT:psanywhereServer = Start-ThreadJob -ScriptBlock {
      Start-Transcript -Path TEMP:/PoshAnywhereServer.log -UseMinimalHeader
      ./PoshAnywhereServer.ps1
    }
    Import-Module $PSScriptRoot/bin/Debug/net7.0/publish/PoshAnywhere.dll
  }

  It 'Should connect to the server' {
    $session = New-WebSocketSession -NoSsl
    $session | Should -Not -BeNullOrEmpty
    $session.State | Should -Be 'Opened'
  }



  AfterEach {
    Stop-Job $psanywhereServer
  }
}
