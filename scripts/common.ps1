# Helpers shared by install.ps1 and uninstall.ps1 (dot-sourced; not meant to be run directly).
# Must work on Windows PowerShell 5.1 and PowerShell 7. Keep this file ASCII: 5.1 reads
# BOM-less scripts in the ANSI code page.

$AppName = 'ClaudeStatusLight'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeStatusLight'
$ClaudeDir = Join-Path $env:USERPROFILE '.claude'
$InstalledFiles = @('ClaudeStatusLight.exe', 'status-hook.exe', 'status-hook.sh')

function Invoke-Native {
    # Runs a native command and returns its exit code (and, with -Quiet, its stdout lines;
    # stderr is discarded). Otherwise stdout goes to the host. $ErrorActionPreference is
    # relaxed for the call because Windows PowerShell 5.1 turns redirected native stderr
    # into error records, which 'Stop' would make fatal.
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$Quiet
    )
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = @()
        if ($Quiet) {
            $out = @(& $FilePath @ArgumentList 2>$null)
        } else {
            & $FilePath @ArgumentList | Out-Host
        }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
    } finally {
        $ErrorActionPreference = $savedPreference
    }
}

function Invoke-WithRetry {
    # Runs $Action, retrying every 250 ms for up to $Seconds while it throws (e.g. a file
    # locked by a status-hook.exe that Claude Code is running right now).
    param([Parameter(Mandatory = $true)][scriptblock]$Action, [int]$Seconds = 5)
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ($true) {
        try {
            & $Action
            return
        } catch {
            if ([DateTime]::UtcNow -ge $deadline) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Stop-App {
    foreach ($p in @(Get-Process -Name $AppName -ErrorAction SilentlyContinue)) {
        Write-Host "Stopping $AppName (pid $($p.Id))"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        [void]$p.WaitForExit(10000)
    }
}

function Invoke-HookSettings {
    # Runs status-hook.exe --install-hooks / --uninstall-hooks on one settings.json.
    param(
        [Parameter(Mandatory = $true)][string]$HookExe,
        [Parameter(Mandatory = $true)][ValidateSet('install', 'uninstall')][string]$Mode,
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [Parameter(Mandatory = $true)][string]$HookPath
    )
    $r = Invoke-Native -FilePath $HookExe -ArgumentList @("--$Mode-hooks", $SettingsPath, $HookPath)
    if ($r.ExitCode -eq 2) {
        throw "$SettingsPath is not valid JSON $([char]0x2014) fix it and re-run"
    }
    if ($r.ExitCode -ne 0) {
        throw "status-hook.exe --$Mode-hooks failed for $SettingsPath (exit $($r.ExitCode))"
    }
}

function Get-WslTargets {
    # WSL distros whose default user has a ~/.claude directory, as objects with
    # Name, LinuxHome (/home/me) and UncHome (\\wsl.localhost\<distro>\home\me).
    # Note: this starts each listed distro. Skips docker-desktop* distros.
    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { return }

    $savedWslUtf8 = $env:WSL_UTF8
    $env:WSL_UTF8 = '1'
    try {
        $list = Invoke-Native -FilePath wsl.exe -ArgumentList @('-l', '-q') -Quiet
        if ($list.ExitCode -ne 0) { return }

        foreach ($line in $list.Output) {
            # Strip NULs in case wsl.exe ignored WSL_UTF8 and wrote UTF-16.
            $distro = ("$line" -replace "`0", '').Trim()
            if (-not $distro -or $distro -like 'docker-desktop*') { continue }

            # 'cd && pwd' prints $HOME without needing embedded double quotes, which
            # Windows PowerShell 5.1 mangles in native arguments.
            $h = Invoke-Native -FilePath wsl.exe -ArgumentList @('-d', $distro, '-e', 'sh', '-c', 'cd && pwd') -Quiet
            if ($h.ExitCode -ne 0) { continue }
            $linuxHome = (($h.Output -join "`n") -replace "`0", '').Trim()
            if (-not $linuxHome.StartsWith('/')) { continue }

            $t = Invoke-Native -FilePath wsl.exe -ArgumentList @('-d', $distro, '-e', 'test', '-d', "$linuxHome/.claude") -Quiet
            if ($t.ExitCode -ne 0) { continue }

            [pscustomobject]@{
                Name      = $distro
                LinuxHome = $linuxHome
                UncHome   = "\\wsl.localhost\$distro" + $linuxHome.Replace('/', '\')
            }
        }
    } finally {
        $env:WSL_UTF8 = $savedWslUtf8
    }
}
