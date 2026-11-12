#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

# Runs install.ps1 / uninstall.ps1 against a published build with USERPROFILE and
# LOCALAPPDATA redirected to a temp dir. CI publishes the build to <repo>\out;
# STATUSLIGHT_BUILD_DIR overrides that. Needs Windows (runs status-hook.exe).

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $installScript = Join-Path $repoRoot 'scripts\install.ps1'
    $uninstallScript = Join-Path $repoRoot 'scripts\uninstall.ps1'
    $buildDir = $env:STATUSLIGHT_BUILD_DIR
    if (-not $buildDir) { $buildDir = Join-Path $repoRoot 'out' }

    $savedProfile = $env:USERPROFILE
    $savedLocalAppData = $env:LOCALAPPDATA
    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('sl-install-' + [guid]::NewGuid().ToString('N'))
    $env:USERPROFILE = Join-Path $tempRoot 'profile'
    $env:LOCALAPPDATA = Join-Path $tempRoot 'local'
    New-Item -ItemType Directory -Path $env:USERPROFILE, $env:LOCALAPPDATA | Out-Null

    $installDir = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeStatusLight'
    $settingsPath = Join-Path $env:USERPROFILE '.claude\settings.json'
    $statusLightDir = Join-Path $env:USERPROFILE '.claude\status-light'

    & $installScript -LocalBuild $buildDir -SkipWsl -NoStart
    $firstInstallHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
}

AfterAll {
    $env:USERPROFILE = $savedProfile
    $env:LOCALAPPDATA = $savedLocalAppData
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'install.ps1' {
    It 'copies the three files into the install dir' {
        foreach ($name in 'ClaudeStatusLight.exe', 'status-hook.exe', 'status-hook.sh') {
            Join-Path $installDir $name | Should -Exist
        }
    }

    It 'adds all 9 hook events to settings.json, pointing at the installed hook' {
        $json = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $events = @($json.hooks.PSObject.Properties | ForEach-Object { $_.Name })
        $expected = 'SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'Notification',
            'Stop', 'SubagentStart', 'SubagentStop', 'SessionEnd'

        $events.Count | Should -Be 9
        (($events | Sort-Object) -join ',') | Should -Be (($expected | Sort-Object) -join ',')

        $hook = ($installDir -replace '\\', '/') + '/status-hook.exe'
        $json.hooks.Stop[0].hooks[0].command | Should -Be "`"$hook`" idle"
    }

    It 'leaves settings.json byte-identical (and makes no backup) when re-run' {
        & $installScript -LocalBuild $buildDir -SkipWsl -NoStart

        (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash | Should -Be $firstInstallHash
        @(Get-ChildItem -Path "$settingsPath.bak.*").Count | Should -Be 0
    }
}

Describe 'uninstall.ps1' {
    BeforeAll {
        $sessionsDir = Join-Path $statusLightDir 'sessions'
        New-Item -ItemType Directory -Force -Path $sessionsDir | Out-Null
        Set-Content -LiteralPath (Join-Path $sessionsDir 'abc.json') -Value '{}'
        $iconPath = Join-Path $statusLightDir 'icon.png'
        Set-Content -LiteralPath $iconPath -Value 'not really a png'

        & $uninstallScript -SkipWsl -NoStop
    }

    It 'removes the status-light hooks from settings.json' {
        $settingsPath | Should -Exist
        Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | Should -Not -Match 'status-hook'
    }

    It 'removes the install dir' {
        $installDir | Should -Not -Exist
    }

    It 'removes the sessions dir but keeps icon.png' {
        $sessionsDir | Should -Not -Exist
        $iconPath | Should -Exist
    }
}

Describe 'install.ps1 with an invalid settings.json' {
    BeforeAll {
        $outerProfile = $env:USERPROFILE
        $env:USERPROFILE = Join-Path $tempRoot 'bad-profile'
        $badSettings = Join-Path $env:USERPROFILE '.claude\settings.json'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $badSettings) | Out-Null
        [IO.File]::WriteAllText($badSettings, '{ "model": ')
        $badHash = (Get-FileHash -LiteralPath $badSettings -Algorithm SHA256).Hash
    }

    AfterAll {
        $env:USERPROFILE = $outerProfile
    }

    It 'stops with the not-valid-JSON error, leaving the file unchanged and unbacked-up' {
        { & $installScript -LocalBuild $buildDir -SkipWsl -NoStart } | Should -Throw -ExpectedMessage '*not valid JSON*'

        (Get-FileHash -LiteralPath $badSettings -Algorithm SHA256).Hash | Should -Be $badHash
        @(Get-ChildItem -Path "$badSettings.bak.*").Count | Should -Be 0
    }
}

Describe 'Get-WslTargets' {
    BeforeAll {
        . (Join-Path $repoRoot 'scripts\common.ps1')

        Mock Get-Command -ParameterFilter { $Name -contains 'wsl.exe' } -MockWith {
            [pscustomobject]@{ Name = 'wsl.exe' }
        }

        # Fake wsl.exe: the distro list, each distro's home, and `test -d <home>/.claude`.
        Mock Invoke-Native -MockWith {
            $a = @($ArgumentList)
            if ($a[0] -eq '-l') {
                # UTF-16LE read one byte per char: NULs between letters, and a NUL left at the
                # start of the next line.
                $ubuntu = -join ('Ubuntu'.ToCharArray() | ForEach-Object { "$_`0" })
                $lines = @("$ubuntu`r`0", "`0", 'docker-desktop', 'docker-desktop-data', '', 'Windowsy', 'NoClaude', 'Debian')
                return [pscustomobject]@{ ExitCode = 0; Output = $lines }
            }
            $homes = @{ Ubuntu = "/home/me`r"; Windowsy = 'C:\Users\me'; NoClaude = '/home/nc'; Debian = '/home/d e' }
            if ($a -contains 'sh') {
                return [pscustomobject]@{ ExitCode = 0; Output = @($homes[$a[1]]) }
            }
            $code = 0
            if ($a[1] -eq 'NoClaude') { $code = 1 }
            return [pscustomobject]@{ ExitCode = $code; Output = @() }
        }
    }

    It 'lists distros with ~/.claude, skipping docker-desktop, blank lines and non-Linux homes' {
        $env:WSL_UTF8 = $null

        $targets = @(Get-WslTargets)

        $targets.Count | Should -Be 2
        $targets[0].Name | Should -Be 'Ubuntu'
        $targets[0].LinuxHome | Should -Be '/home/me'
        $targets[0].UncHome | Should -Be '\\wsl.localhost\Ubuntu\home\me'
        $targets[1].Name | Should -Be 'Debian'
        $targets[1].LinuxHome | Should -Be '/home/d e'
        $targets[1].UncHome | Should -Be '\\wsl.localhost\Debian\home\d e'

        Should -Invoke Invoke-Native -Times 0 -Exactly -ParameterFilter { @($ArgumentList -like 'docker-desktop*').Count -gt 0 }
        Should -Invoke Invoke-Native -Times 0 -Exactly -ParameterFilter { ($ArgumentList -contains 'Windowsy') -and ($ArgumentList -contains 'test') }
        $env:WSL_UTF8 | Should -BeNullOrEmpty
    }
}
