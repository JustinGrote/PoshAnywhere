#requires -Modules @{ModuleName='Pester';ModuleVersion='5.0'}
Describe 'Websocket Client' {
  BeforeAll {
    Import-Module $PSScriptRoot/bin/Debug/net7.0/publish/PoshAnywhere.dll -Force
  }
  Context 'Failed Operations' {
    It 'Fails to Connect if server is not available' {
      {
        New-WebSocketSession -Port 12345 -ErrorAction Stop
      } | Should -Throw '*target machine actively refused it*'
    }
  }
  Context 'Server Interaction' {
    BeforeEach {
      $SCRIPT:psanywhereServer = Start-ThreadJob -Scriptblock {
        Start-Transcript -Path "TEMP:/PoshAnywhereServer-$PID.log" -UseMinimalHeader
        try {
          ./PoshAnywhereServer.ps1 -Verbose -Debug
        } finally {
          Stop-Transcript
        }
      }
    }

    It 'Connect to Server' {
      $session = New-WebSocketSession -NoSsl
      $session | Should -Not -BeNullOrEmpty
      $session.State | Should -Be 'Opened'
    }

    It 'Issues Command' {
      $session = New-WebSocketSession -NoSsl
      Invoke-Command -Session $session -ScriptBlock {
        'PESTER TEST'
      } | Should -Be 'PESTER TEST'
    }

    AfterEach {
      $serverOutput = $psanywhereServer
      | Stop-Job -PassThru
      | Receive-Job -Wait -AutoRemoveJob -Verbose *>&1
      $serverOutput | Write-Host -Fore Magenta
    }
  }

}
