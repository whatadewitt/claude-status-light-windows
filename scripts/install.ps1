<#
.SYNOPSIS
Installs (or updates) Claude Status Light for Windows.

.DESCRIPTION
Downloads the latest release (or -Version), verifies its SHA-256, copies the app and hook
to %LOCALAPPDATA%\Programs\ClaudeStatusLight, merges the hooks into
%USERPROFILE%\.claude\settings.json and into each WSL distro that has ~/.claude, then
starts the app (which registers itself to start at login). Re-run it to update.

.PARAMETER Version
Release tag to install, e.g. v0.1.0. Default: the latest release.

.PARAMETER LocalBuild
Install from a folder holding ClaudeStatusLight.exe, status-hook.exe and status-hook.sh
instead of downloading a release.

.PARAMETER Repo
GitHub repo (owner/name) to download from. Default: parsed from this clone's origin remote.

.PARAMETER SkipWsl
Don't touch WSL distros.

.PARAMETER NoStart
Test-only: leave any running app alone (don't stop it before copying) and don't start the
app afterwards, so the Pester tests don't touch the real app.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$LocalBuild,
    [string]$Repo,
    [switch]$SkipWsl,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$AssetName = 'ClaudeStatusLight-win-x64.zip'

function Resolve-RepoName {
    if ($Repo) { return $Repo }
    $noRepo = "Can't tell which GitHub repo to download from: pass -Repo <owner>/<name> (or -LocalBuild <dir>)."
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw $noRepo }
    $r = Invoke-Native -FilePath git -ArgumentList @('-C', $PSScriptRoot, 'remote', 'get-url', 'origin') -Quiet
    if ($r.ExitCode -ne 0 -or $r.Output.Count -eq 0) { throw $noRepo }
    $url = "$($r.Output[0])".Trim()
    # https://github.com/owner/name(.git), git@github.com:owner/name(.git), ssh://git@github.com/owner/name(.git)
    if ($url -match 'github\.com[:/](?<owner>[^/]+)/(?<name>[^/]+?)(\.git)?/?$') {
        return "$($Matches['owner'])/$($Matches['name'])"
    }
    throw "origin remote '$url' is not a GitHub repo: pass -Repo <owner>/<name> (or -LocalBuild <dir>)."
}

function Get-ReleaseFiles {
    # Downloads and verifies the release zip into $WorkDir; returns the folder it was extracted to.
    param([Parameter(Mandatory = $true)][string]$WorkDir)
    $repoName = Resolve-RepoName
    $zip = Join-Path $WorkDir $AssetName
    $label = $Version
    if (-not $label) { $label = 'latest release' }

    $useGh = $false
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $useGh = (Invoke-Native -FilePath gh -ArgumentList @('auth', 'status') -Quiet).ExitCode -eq 0
    }

    if ($useGh) {
        Write-Host "Downloading $label of $repoName with gh"
        $ghArgs = @('release', 'download')
        if ($Version) { $ghArgs += $Version }
        $ghArgs += @('--repo', $repoName, '--pattern', 'ClaudeStatusLight-win-x64.zip*', '--dir', $WorkDir)
        $r = Invoke-Native -FilePath gh -ArgumentList $ghArgs
        if ($r.ExitCode -ne 0) { throw "gh release download failed (exit $($r.ExitCode))." }
    } else {
        if ($Version) {
            $base = "https://github.com/$repoName/releases/download/$Version"
        } else {
            $base = "https://github.com/$repoName/releases/latest/download"
        }
        Write-Host "Downloading $label from $base"
        # Windows PowerShell 5.1 may not offer TLS 1.2 by default.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'  # the 5.1 progress bar makes downloads crawl
        foreach ($name in @($AssetName, "$AssetName.sha256")) {
            Invoke-WebRequest -UseBasicParsing -Uri "$base/$name" -OutFile (Join-Path $WorkDir $name)
        }
    }

    $expected = ((Get-Content -LiteralPath "$zip.sha256" -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "SHA-256 mismatch for ${AssetName}: expected $expected, got $actual. Nothing was installed."
    }
    Write-Host "SHA-256 verified"

    $extracted = Join-Path $WorkDir 'files'
    Expand-Archive -LiteralPath $zip -DestinationPath $extracted -Force
    return $extracted
}

$workDir = $null
try {
    # 1. Get the files.
    if ($LocalBuild) {
        $source = (Resolve-Path -LiteralPath $LocalBuild).Path
        Write-Host "Installing from local build $source"
    } else {
        $workDir = Join-Path ([IO.Path]::GetTempPath()) ('ClaudeStatusLight-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $workDir | Out-Null
        $source = Get-ReleaseFiles -WorkDir $workDir
    }
    foreach ($name in $InstalledFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
            throw "$source is missing $name."
        }
    }

    # 2. Install the files.
    if (-not $NoStart) { Stop-App }
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    foreach ($name in $InstalledFiles) {
        Invoke-WithRetry -Action { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $InstallDir $name) -Force }
    }
    Write-Host "Installed to $InstallDir"
    $hookExe = Join-Path $InstallDir 'status-hook.exe'

    # 3. Native hooks.
    Invoke-HookSettings -HookExe $hookExe -Mode install -SettingsPath (Join-Path $ClaudeDir 'settings.json') -HookPath $hookExe

    # 4. WSL hooks.
    if (-not $SkipWsl) {
        $targets = @(Get-WslTargets)
        if ($targets.Count -eq 0) { Write-Host 'No WSL distro with ~/.claude found; skipping WSL.' }
        $shBytes = [IO.File]::ReadAllBytes((Join-Path $InstallDir 'status-hook.sh'))
        $shBytes = [byte[]]@($shBytes | Where-Object { $_ -ne 13 })  # LF only, whatever git did
        foreach ($t in $targets) {
            Write-Host "Installing WSL hooks in $($t.Name)"
            $linuxHook = "$($t.LinuxHome)/.claude/status-light/status-hook.sh"
            $uncDir = "$($t.UncHome)\.claude\status-light"
            [void][IO.Directory]::CreateDirectory($uncDir)
            [IO.File]::WriteAllBytes("$uncDir\status-hook.sh", $shBytes)
            $r = Invoke-Native -FilePath wsl.exe -ArgumentList @('-d', $t.Name, '-e', 'chmod', '+x', $linuxHook)
            if ($r.ExitCode -ne 0) { throw "chmod +x $linuxHook failed in $($t.Name) (exit $($r.ExitCode))." }
            Invoke-HookSettings -HookExe $hookExe -Mode install -SettingsPath "$($t.UncHome)\.claude\settings.json" -HookPath $linuxHook
        }
    }

    # 5. Start the app; it registers itself to start at login.
    if (-not $NoStart) {
        Start-Process -FilePath (Join-Path $InstallDir 'ClaudeStatusLight.exe') -WorkingDirectory $InstallDir
    }
    Write-Host 'Restart open Claude Code sessions so the new hooks load.'
} finally {
    if ($workDir) { Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue }
}
