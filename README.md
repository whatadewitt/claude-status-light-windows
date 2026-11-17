# Claude Status Light for Windows

A small Windows tray app that shows what Claude Code is doing at a glance: whether any
session is blocked on you, busy, or done. It's the Windows port of the
[macOS Claude Status Light](https://github.com/whatadewitt/claude-status-light).

| Light | Meaning |
|---|---|
| Gray (off) | No active session |
| Red | Waiting on you: a permission prompt or a question from Claude |
| Yellow | Working: tools, thinking, or background shells Claude started that are still running |
| Green | Done, awaiting your next task |

With several sessions, the light shows the most urgent one. Red always wins; a setting
(on by default) decides whether green outranks yellow. A blue badge counts running
subagents.

## What it watches

Every Claude Code session on this machine, wherever it runs. Each row is tagged with
where the session lives:

- **PowerShell**: native Windows Claude Code in Windows Terminal or a console window.
- **Claude app**: sessions in the Claude desktop app.
- **WSL**: Claude Code inside a WSL distro such as Ubuntu.

## Where it shows

- **Tray icon**: the Claude Code pixel mascot, tinted to the light, dancing while a
  session works. Click it for the session list, the floating window toggle, Settings,
  "Set custom icon..." and Quit. Hover for the overall state.
- **Floating window**: an always-on-top list of sessions. Lock it to a screen corner or
  drag it anywhere. With **auto-hide** it shrinks to the mascot, slides out on hover, and
  pops out by itself when a session turns red.

Click a session to bring its window forward (Windows Terminal, the console window, or
the Claude app). Each row shows the project folder plus suffixes for subagents,
permission mode (`plan`, `auto-edit`, ...) and background shells.

For a custom icon, drop an image at `%USERPROFILE%\.claude\status-light\icon.png`
("Set custom icon..." opens that folder).

## Install

Needs 64-bit Windows 10 or 11. No .NET SDK is required: releases are built by GitHub
Actions. Clone this repo, then from the clone run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

This downloads the latest release, checks its SHA-256, installs it to
`%LOCALAPPDATA%\Programs\ClaudeStatusLight`, adds the hooks to
`%USERPROFILE%\.claude\settings.json` (backing it up first as `settings.json.bak.<time>`),
installs the WSL hook (see below), and starts the app, which then starts at login.
**Restart open Claude Code sessions** so they load the new hooks.

If the GitHub CLI (`gh`) is installed and signed in, the download goes through it, so a
private repo works too. Otherwise it uses the public release URL.

| Option | Effect |
|---|---|
| `-Version v0.1.0` | Install that release instead of the latest |
| `-Repo owner/name` | Download from that repo instead of this clone's `origin` |
| `-LocalBuild <dir>` | Install from a folder with `ClaudeStatusLight.exe`, `status-hook.exe` and `status-hook.sh` |
| `-SkipWsl` | Leave WSL alone |

## Update

Re-run `scripts\install.ps1`. It stops the app, replaces the files in place, and leaves
your settings alone (the hook merge is idempotent).

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
```

This stops the app, removes start at login, removes the hooks from the Windows and WSL
`settings.json` files, and deletes the WSL hook script and session files, the install folder
and `%USERPROFILE%\.claude\status-light\sessions`. It keeps your custom `icon.png` and the
app's preferences in `%APPDATA%\ClaudeStatusLight`.

## How it works

```
PowerShell / Claude app ──hooks──▶ status-hook.exe ──▶ %USERPROFILE%\.claude\status-light\sessions\<id>.json ─┐
                                                                                                          ├─▶ tray app
WSL (Ubuntu) ───────────hooks──▶ status-hook.sh  ──▶ ~/.claude/status-light/sessions/<id>.json (Linux)  ──┘
                                                       read via \\wsl.localhost\<distro>\...
```

Claude Code runs a hook on each lifecycle event (session start, prompt, tool use,
notification, stop, subagents, session end). The hook records the session's state, its
folder, its terminal and the Claude process ID in a small JSON file, in the same format as
the Mac version. `status-hook.exe` is a tiny Native AOT program; it always exits 0 and
never prints, so it can't slow down or break Claude Code.

The tray app reads those files every second, drops sessions whose process has gone, and
shows the most urgent state. It logs startup and any errors it recovers from to
`%LOCALAPPDATA%\ClaudeStatusLight\log.txt` (rolled to `log.1.txt` at about 1 MB).

## WSL notes

- `install.ps1` installs the hook into every WSL distro that has a `~/.claude` folder
  (skipping `docker-desktop` distros): it writes `~/.claude/status-light/status-hook.sh`
  and merges its hooks into that distro's `~/.claude/settings.json`. If you set up Claude
  Code in a new distro later, re-run `install.ps1`. If `settings.json` is a symlink (e.g.
  into your dotfiles), the installer resolves it inside the distro and edits the real file.
- The app reads WSL sessions through `\\wsl.localhost\<distro>` every 3 seconds, and only
  for distros that are already running (`wsl -l --running`). It never starts a stopped
  distro or keeps one awake. On a machine with no WSL distro registered it doesn't poll WSL
  at all (it checks again every minute).
- Windows can't see Linux processes, so liveness comes from one `ps` call per poll in a
  distro that has session files.
- Clicking a WSL session brings the most recently used Windows Terminal window forward
  (not the exact tab).

## Developing

- `dotnet test tests/StatusLight.Core.Tests` runs the core tests on any OS, including
  macOS. Running the whole solution's tests (`dotnet test StatusLight.slnx`) needs Windows;
  CI does it on `windows-latest`, along with the Pester tests in `tests/scripts`.
- Pushing a `v*` tag runs `.github/workflows/release.yml`, which publishes
  `ClaudeStatusLight-win-x64.zip` and its `.sha256` as a GitHub release.
- The app registers its own exe for start at login every time it launches, so running a
  dev build points start at login at that build. Re-run `install.ps1` (or launch the
  installed app) to point it back.

## Remote sessions

This version shows sessions on this machine only. The Mac version can also show sessions
running on other machines; that remote support is coming here next. See the
[Mac repo](https://github.com/whatadewitt/claude-status-light) for how it works there.
