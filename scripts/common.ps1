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
    # into error records, which 'Stop' would make fatal. A command that fails to launch
    # reports exit code -1, never a stale $LASTEXITCODE from an earlier call.
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$Quiet
    )
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out = @()
    $global:LASTEXITCODE = -1
    try {
        if ($Quiet) {
            $out = @(& $FilePath @ArgumentList 2>$null)
        } else {
            & $FilePath @ArgumentList | Out-Host
        }
        $code = $global:LASTEXITCODE
    } catch {
        $code = -1
    } finally {
        $ErrorActionPreference = $savedPreference
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $out }
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
        throw "$SettingsPath is not valid JSON or UTF-8 $([char]0x2014) fix it and re-run"
    }
    if ($r.ExitCode -ne 0) {
        throw "status-hook.exe --$Mode-hooks failed for $SettingsPath (exit $($r.ExitCode))"
    }
}

function ConvertTo-WslWindowsPath {
    # The Windows path of an absolute Linux path in a distro: /mnt/<drive>/rest is the Windows
    # drive itself (<DRIVE>:\rest); anything else goes through \\wsl.localhost\<distro>.
    param(
        [Parameter(Mandatory = $true)][string]$Distro,
        [Parameter(Mandatory = $true)][string]$LinuxPath
    )
    if ($LinuxPath -match '^/mnt/(?<drive>[A-Za-z])(?<rest>/.*)?$') {
        $rest = $Matches['rest']
        if (-not $rest) { $rest = '/' }
        return $Matches['drive'].ToUpperInvariant() + ':' + $rest.Replace('/', '\')
    }
    return "\\wsl.localhost\$Distro" + $LinuxPath.Replace('/', '\')
}

function Get-WslTargets {
    # WSL distros whose default user has a ~/.claude directory, as objects with
    # Name, LinuxHome (/home/me), UncHome (\\wsl.localhost\<distro>\home\me) and
    # SettingsPath: the Windows path of ~/.claude/settings.json with symlinks resolved inside
    # the distro (\\wsl.localhost can't follow a Linux symlink), e.g. C:\Users\me\dotfiles\x.json
    # for a link into /mnt/c.
    # Note: this starts each listed distro. Skips docker-desktop* distros.
    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { return }

    $savedWslUtf8 = $env:WSL_UTF8
    $env:WSL_UTF8 = '1'
    # Decode wsl.exe output as UTF-8 (5.1 defaults to the OEM code page), so a non-ASCII
    # Linux home survives. Setting it can fail without a console; then keep the default.
    $savedEncoding = $null
    try {
        $savedEncoding = [Console]::OutputEncoding
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    } catch { }
    try {
        $list = Invoke-Native -FilePath wsl.exe -ArgumentList @('-l', '-q') -Quiet
        if ($list.ExitCode -ne 0) { return }

        foreach ($line in $list.Output) {
            # Strip NULs in case wsl.exe ignored WSL_UTF8 and wrote UTF-16.
            $distro = ("$line" -replace "`0", '').Trim()
            if (-not $distro -or $distro -like 'docker-desktop*') { continue }

            # No shell and no quoting (5.1 mangles embedded double quotes in native arguments);
            # the app probes the home the same way.
            $h = Invoke-Native -FilePath wsl.exe -ArgumentList @('-d', $distro, '-e', 'printenv', 'HOME') -Quiet
            if ($h.ExitCode -ne 0) { continue }
            $linuxHome = (($h.Output -join "`n") -replace "`0", '').Trim()
            if (-not $linuxHome.StartsWith('/')) { continue }

            $t = Invoke-Native -FilePath wsl.exe -ArgumentList @('-d', $distro, '-e', 'test', '-d', "$linuxHome/.claude") -Quiet
            if ($t.ExitCode -ne 0) { continue }

            $linuxSettings = "$linuxHome/.claude/settings.json"
            $resolved = ''
            $l = Invoke-Native -FilePath wsl.exe -ArgumentList @('-d', $distro, '-e', 'readlink', '-f', $linuxSettings) -Quiet
            if ($l.ExitCode -eq 0) { $resolved = (($l.Output -join "`n") -replace "`0", '').Trim() }
            if (-not $resolved.StartsWith('/')) { $resolved = $linuxSettings }

            [pscustomobject]@{
                Name         = $distro
                LinuxHome    = $linuxHome
                UncHome      = "\\wsl.localhost\$distro" + $linuxHome.Replace('/', '\')
                SettingsPath = ConvertTo-WslWindowsPath -Distro $distro -LinuxPath $resolved
            }
        }
    } finally {
        $env:WSL_UTF8 = $savedWslUtf8
        if ($savedEncoding) {
            try { [Console]::OutputEncoding = $savedEncoding } catch { }
        }
    }
}
