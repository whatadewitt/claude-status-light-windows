<#
.SYNOPSIS
Removes Claude Status Light for Windows.

.DESCRIPTION
Stops the app, removes its start-at-login entry, strips its hooks from
%USERPROFILE%\.claude\settings.json and from each WSL distro's ~/.claude/settings.json,
then deletes the WSL status-hook.sh, the install dir and the sessions dir.
Keeps %USERPROFILE%\.claude\status-light\icon.png.

.PARAMETER SkipWsl
Don't touch WSL distros.

.PARAMETER NoStop
Test-only: don't stop a running app and don't remove the HKCU Run (start at login) value,
so the Pester tests don't touch the real app.
#>
[CmdletBinding()]
param(
    [switch]$SkipWsl,
    [switch]$NoStop
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# 1. Stop the app and remove the start-at-login entry.
if (-not $NoStop) {
    Stop-App
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Get-ItemProperty -Path $runKey -Name $AppName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $AppName
        Write-Host 'Removed start at login'
    }
}

# 2. Remove the hooks (Windows, then WSL), deleting the WSL hook script as we go.
$hookExe = Join-Path $InstallDir 'status-hook.exe'
if (Test-Path -LiteralPath $hookExe -PathType Leaf) {
    $settingsPath = Join-Path $ClaudeDir 'settings.json'
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        Invoke-HookSettings -HookExe $hookExe -Mode uninstall -SettingsPath $settingsPath -HookPath $hookExe
    }

    if (-not $SkipWsl) {
        foreach ($t in @(Get-WslTargets)) {
            Write-Host "Removing WSL hooks from $($t.Name)"
            $linuxHook = "$($t.LinuxHome)/.claude/status-light/status-hook.sh"
            $wslSettings = "$($t.UncHome)\.claude\settings.json"
            if (Test-Path -LiteralPath $wslSettings -PathType Leaf) {
                Invoke-HookSettings -HookExe $hookExe -Mode uninstall -SettingsPath $wslSettings -HookPath $linuxHook
            }
            $wslHook = "$($t.UncHome)\.claude\status-light\status-hook.sh"
            if (Test-Path -LiteralPath $wslHook -PathType Leaf) { Remove-Item -LiteralPath $wslHook -Force }
        }
    }
} else {
    Write-Warning "$hookExe not found, so no hooks were removed. Delete the status-hook entries from your Claude Code settings.json files by hand."
}

# 3. Delete the installed files and session state; icon.png stays.
if (Test-Path -LiteralPath $InstallDir) {
    Invoke-WithRetry -Action { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
    Write-Host "Removed $InstallDir"
}
$sessionsDir = Join-Path $ClaudeDir 'status-light\sessions'
if (Test-Path -LiteralPath $sessionsDir) { Remove-Item -LiteralPath $sessionsDir -Recurse -Force }

Write-Host 'Claude Status Light has been removed. Restart open Claude Code sessions so the hooks unload.'
