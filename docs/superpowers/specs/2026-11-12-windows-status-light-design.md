# Claude Status Light for Windows — Design

**Date:** 2026-11-12
**Status:** draft, awaiting approval
**Reference implementation:** the macOS app at https://github.com/whatadewitt/claude-status-light (same author). Where this doc says "same as the Mac version", the Mac source is the spec. The key files are `hooks/status-hook.sh`, `Sources/ClaudeStatusLight/Model.swift`, `StateStore.swift`, `ShellScanner.swift`, `FloatingPanelController.swift`, `PanelGeometry.swift`, `IconRenderer.swift`, `Settings.swift` and `scripts/merge_settings.py`.

## Intent

The user already runs the Mac status light. They also work from a Windows machine, where they run Claude Code in three places:

- PowerShell (native Windows Claude Code)
- the Ubuntu shell (WSL on the same machine)
- the Claude desktop app

They want the same at-a-glance stoplight on Windows. It should show whether any session is blocked on them, busy, or done.

The bigger goal, deliberately deferred, is the Mac version's **remote** feature. The user usually runs Claude Code on a Linux box in their basement, reached from this Windows machine, and wants those sessions in the same light. Version one is the local light only. It must be shaped so remote sessions plug in later without a rewrite.

## What version one is

A Windows tray app that shows the state of every Claude Code session on this machine.

### Light states (same as the Mac version)

| Light | Meaning |
|---|---|
| Gray / off | No active session |
| Red | Waiting on you (permission prompt, `AskUserQuestion`) |
| Yellow | Working (tools, thinking, or Claude-spawned background shells still running) |
| Green | Done, awaiting your next task |

When several sessions are running, the light shows the most urgent one. Red always wins. A setting (default on) decides whether green outranks yellow, as on the Mac.

### Surfaces (user's choice)

1. **System tray icon.**
   - It shows the tinted Claude Code pixel mascot, ported from the Mac `IconRenderer`.
   - A blue badge counts running subagents.
   - Left-click opens a menu that lists the sessions, then "Show floating window" (toggle), "Settings…", "Set custom icon…" and "Quit".
   - Hovering shows the aggregate state.
2. **Floating window.**
   - It stays on top of other windows and lists the sessions.
   - It can lock to any screen corner, or be unlocked and dragged anywhere; its position is remembered.
   - **Auto-hide** (on in Settings) collapses it to just the mascot tile. It slides out on hover, and pops out by itself when any session turns red.
   - In multi-monitor setups it remembers which monitor it's on and stays visible if that monitor goes away.

There is no taskbar or app-icon tint (user's choice).

**Mascot dance (my call, for parity).** As on the Mac, the mascot animates at about 0.5 s per frame while any session is working (`AppDelegate.updateDance`, `IconRenderer.danceMoves`), in the tray and the floating window. Render the frames once per DPI and cache them, so the animation costs only an icon swap. When nothing is working, the animation stops completely, so an idle app does no work.

### Session rows

Each row matches the Mac row format: dot, project folder name, then suffixes.

- `· N agents` when subagents are running.
- A permission-mode suffix: `· plan`, `· auto-edit`, `· bypass`, `· auto`, `· don't-ask`. Unknown modes show their raw name.
- `· sh: <cmd>` (or `· running N shells…`) when an otherwise-idle session still has Claude-spawned background shells running.
- Background (headless) sessions show their task title from the transcript, or `(bg)` if they have none.
- Background sessions idle for more than 2 minutes are dimmed ("parked").
- A tag shows where the session runs: **PowerShell**, **WSL**, or **Claude app**. That's the user's mockup; derive it from the recorded terminal or parent process.
- The hover tooltip shows the full cwd, terminal, mode, and full shell commands, as on the Mac.

All Mac extras are in scope (user's choice): auto-hide, custom icon, background-shell detection, parked dimming, and permission-mode labels.

### Click a session to bring it forward

Clicking a row (in the tray menu or the floating window) brings that session's top-level window to the front:

- the Windows Terminal window
- the console window hosting PowerShell
- the Claude app window

Focusing the exact tab is a non-goal. Windows Terminal has no clean per-tab focus API.

**WSL sessions (my call):** a WSL session file has only Linux PIDs, so clicking one brings the most recently active Windows Terminal window forward. That's good enough for version one, and it keeps the WSL hook script unchanged rather than forking it to record Windows-side fields.

### Custom icon

Drop an image at `%USERPROFILE%\.claude\status-light\icon.png`, or use "Set custom icon…", which opens that folder in Explorer. The app then draws the full-color artwork with a small status dot in the corner, as on the Mac.

### Settings window

Settings opens from the tray menu. It has these settings:

- show floating window
- lock to corner, and which corner
- auto-hide
- green beats yellow
- start at login
- reveal the icon folder

## How it works

```
PowerShell / Claude app ──hooks──▶ status-hook.exe ──▶ %USERPROFILE%\.claude\status-light\sessions\<id>.json ─┐
                                                                                                          ├─▶ tray app
WSL (Ubuntu) ───────────hooks──▶ status-hook.sh  ──▶ ~/.claude/status-light/sessions/<id>.json (Linux)  ──┘
                                                       read via \\wsl.localhost\<distro>\...
```

### Hook events (same mapping as the Mac `merge_settings.py`)

| Event | State | Notes |
|---|---|---|
| `SessionStart` | idle | Also clears leftover agent markers. |
| `UserPromptSubmit`, `PreToolUse`, `PostToolUse` | working | `PreToolUse` for `AskUserQuestion` is upgraded to attention. |
| `Notification` | attention | The "waiting for your input" reminder stays idle. |
| `Stop` | idle | |
| `SubagentStart` / `SubagentStop` | agent-start / agent-stop | One marker file per subagent in `<id>.agents\`. |
| `SessionEnd` | end | Deletes the session's files. |

`PreToolUse` and `PostToolUse` take matcher `*`.

### State file format

Keep the Mac JSON format **identical**: `state`, `session_id`, `cwd`, `term_program`, `tty`, `pid`, `transcript_path`, `permission_mode`, `updated_at` (epoch seconds). The `.agents` marker directory is unchanged too.

The sticky rules from `status-hook.sh` carry over. Terminal identity and permission mode keep their recorded values when an event doesn't carry them. Session ids are sanitized to `[A-Za-z0-9._-]`.

Windows-only fields may be **added**, for example the host window handle or process name for focusing. Existing fields must not change meaning. This keeps the door open for remote sessions, whose files come from the Mac and Linux hooks.

**`tty` on native Windows (my call).** Windows has no tty. The native hook therefore writes a non-empty *terminal identity string* into `tty` whenever the session has an interactive host:

- `wt:<WT_SESSION guid>` for Windows Terminal
- `console:<host pid>` for a plain console window
- `claude-app:<pid>` for the Claude desktop app

It leaves `tty` empty only for genuinely headless sessions: a known Claude PID but no interactive host in its ancestry, such as daemon-spawned background agents.

This keeps the Mac's one rule working unchanged ("background = pid known and tty empty"), and with it the "(bg)" and title rows, parked dimming, and the sticky terminal-identity rule. PowerShell and Claude app sessions are ordinary interactive rows: never "(bg)", never dimmed.

### Native Windows hook: `status-hook.exe` (my call)

The hook is a small console program compiled with Native AOT, so it starts fast and doesn't depend on any shell. It is not a bash or PowerShell script.

- `settings.json` calls it by absolute path, e.g. `"C:\...\status-hook.exe" working`. This works however Claude Code on Windows launches hook commands.
- It is a direct port of `status-hook.sh`: same classification rules, same sticky rules, same file output.
- To find "the Claude process", it walks up the parent-process chain, skipping shells (`bash`, `sh`, `cmd`, `powershell`, `pwsh`, `conhost`).
- It records enough to identify the top-level window later. At minimum that's the terminal host or Claude app process, found by walking further up the chain. Recording the `HWND` is fine as an optimisation.
- It must always exit 0, never block, and swallow every error. The Mac hook has the same contract.

### WSL hook

Inside WSL, Claude Code is ordinary Linux Claude Code. Install the Mac `status-hook.sh` there unchanged, if it runs cleanly on Linux; the builder should verify that. Merge its hooks into the WSL user's `~/.claude/settings.json`. Copy the script into this repo with a note on its origin.

### Reading sessions

The app polls on a short interval and also uses a file watcher where it's reliable (Windows dirs). It has two **session sources** behind one interface:

1. **Windows source.** Reads `%USERPROFILE%\.claude\status-light\sessions`.
   - Liveness: if the recorded PID isn't running, the session is dead; delete its files, same as the Mac.
   - Background shells: look for direct children of the Claude PID whose command line carries the Claude shell-snapshot wrapper signature, as in `ShellScanner.swift`.
   - The builder must confirm what that signature looks like for native Windows Claude Code. Bash tool commands there run through Git Bash, so `.claude/shell-snapshots/` probably still appears, but verify.
2. **WSL source.** Reads each running distro's `\\wsl.localhost\<distro>\home\<user>\.claude\status-light\sessions`.
   - Windows can't see Linux PIDs. Liveness and background shells instead come from one `wsl.exe -d <distro> -e ps -eo pid=,ppid=,args=` call per poll. That call runs only when that distro has session files.
   - File watchers over `\\wsl.localhost` are unreliable, so this source polls.
   - Distros: detect the default/running distros (`wsl.exe -l --running`). The user has one Ubuntu distro, but don't hard-code the name.

The aggregate light, row text, parked dimming and transcript titles all work on the merged list. Transcript titles use the same head-of-file `agent-name` / `ai-title` scan, applied through the WSL path for WSL sessions.

**Why two sources:** the WSL source is a small version of the remote problem. Its sessions live on another OS, its PIDs are foreign, and they can't be focused per-tab. The future relay or SSH source for the basement box should be a third implementation of the same interface. Rows from it would carry an origin label, as on the Mac (`basement · project`).

### Install / uninstall

**Builds happen in GitHub Actions, not on the user's machine (user's choice).** The Windows machine needs no .NET SDK or C++ build tools.

- **CI on pushes and PRs:** a `windows-latest` workflow builds everything and runs the tests. The core-library tests can also run on Linux or macOS.
- **Releases on version tags (`v*`):** the workflow publishes the self-contained app and the Native AOT hook as a release zip, with a SHA-256 checksum.
- **Where the repo lives:** this needs the repo on the user's GitHub; the local folder isn't a git repo yet.
- **Private repos (my call):** `install.ps1` downloads with `gh release download` when `gh` is available and signed in, so private repos work. Otherwise it falls back to an anonymous download from the public releases URL.

The install and uninstall scripts are PowerShell, so no Python is needed on Windows.

- `install.ps1` does the following:
  1. Downloads the latest release, or a version given with `-Version`, and verifies its checksum. A `-LocalBuild <dir>` switch installs from a folder instead, for development.
  2. Copies the app and hook to `%LOCALAPPDATA%\Programs\ClaudeStatusLight\`. It stops a running app first, so its exe isn't locked during the copy.
  3. Merges the hook entries into `%USERPROFILE%\.claude\settings.json` the same way `merge_settings.py` does: idempotent, timestamped backup first, aborts on invalid JSON.
  4. Registers start-at-login with an HKCU Run key.
  5. Starts the app.
  6. If WSL with a distro is present, installs `status-hook.sh` into the distro and merges its hooks into the Linux `~/.claude/settings.json`, with the same backup and idempotency.
- Re-running `install.ps1` updates to the latest release in place.
- `uninstall.ps1` reverses all of it, including the WSL side.
- The user restarts open Claude Code sessions afterwards so the new hooks load. The Claude app picks up user settings the same way.

## Tech choices (my calls)

- **C# on .NET 10** (the current LTS). This is a correction to ".NET 8", which I said in chat.
- **WPF** for the floating window and settings window. A tray icon (WinForms `NotifyIcon` or an equivalent) for the tray. The app is self-contained, single-file, unsigned, and needs no installer framework.
- **The hook** is a separate Native AOT console project.
- **The core logic** lives in a plain `net10.0` class library with no Windows UI dependency, so its tests can run anywhere. That covers the state model, file parsing, classification, aggregation, row text, title extraction and shell-command extraction.
- **Port the Mac test cases** (`hook_test.sh`, `StateStoreTests`, `SessionLabelTests`, `PanelGeometryTests`) as tests for the matching C# pieces.
- **Settings** are stored as JSON in `%APPDATA%\ClaudeStatusLight\settings.json`.
- The app follows the Windows light/dark theme for its windows. The tray icon renders crisply at the tray's actual DPI.

## Goals

- At a glance, from the tray or the floating window, know whether any session on this machine (native or WSL) needs the user.
- Feature parity with the Mac version's local features.
- Clean seams for the remote work that comes next.

## Non-goals (for version one)

- Remote sessions: the basement box, other machines, the Cloudflare relay, pairing, cloud sandboxes.
- Exact-tab focus in Windows Terminal.
- Taskbar or app-icon tinting.
- Code signing, MSIX, winget or any store packaging.

## Anti-goals (would be a failure even if it "works")

- **The hook slows down or breaks Claude Code.** It must be fast, always exit 0, and never prompt.
- **Install damages a user's `settings.json`.** Native or WSL, always back up first and never drop existing entries.
- **The app keeps WSL awake or starts a stopped distro just to poll it.** Only touch a distro that's already running.
- **The state-file format drifts from the Mac's,** so remote sessions later need translation.
- **The tray app is noticeably heavy** in CPU or memory while idle.

## Left to the builder

- How Claude Code on native Windows invokes hook commands, and the exact parent-process chain from the hook to the Claude process, to the terminal host, to the Claude app. Verify on a real machine.
- The Windows background-shell signature.
- Poll intervals within the "not heavy" constraint. The Mac polls every couple of seconds.
- Exact visual polish of the floating window and settings window, within "looks at home on Windows 11".
- How the Claude app session is told apart from a terminal session (process name in the ancestry is the obvious route). The user confirmed their Claude app sessions run locally on Windows, so this is in scope.

## Development environment

The doc was written on the user's Mac, where development may start. The core library and its tests run there. The WPF app can be compiled there with `EnableWindowsTargeting`, but it can't run there, and the Native AOT hook can't be built there. Anything Windows-specific gets verified in CI and on the user's Windows machine:

- the process chain
- window focusing
- tray rendering
- WSL access
