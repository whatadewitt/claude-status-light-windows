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
