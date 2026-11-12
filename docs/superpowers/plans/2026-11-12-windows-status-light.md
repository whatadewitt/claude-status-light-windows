# Claude Status Light for Windows — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app with a floating window that shows the stoplight state of every Claude Code session on this machine: native PowerShell, the Claude desktop app, and WSL.

**Architecture:**

- **Projects.**
  - `StatusLight.Core` is a UI-free `net10.0` library holding all logic that can be tested on any OS: the state model, row text, hook logic, settings.json merging, the session readers, the WSL source and panel geometry.
  - `StatusLight.Hook` is a Native AOT console exe (`status-hook.exe`) that Claude Code calls on every hook event.
  - `StatusLight.App` is a WPF + WinForms-`NotifyIcon` tray app.
- **WSL.** Inside WSL, the unchanged macOS `status-hook.sh` writes Linux-side state files. The app reads them over `\\wsl.localhost`.
- **Builds.** GitHub Actions builds and publishes releases, and `install.ps1` downloads them.

**Tech Stack:** C# / .NET 10 (LTS), WPF + `System.Windows.Forms.NotifyIcon`, System.Drawing for icon rendering, System.Text.Json (source-generated, AOT-safe), System.Management (WMI command lines), xUnit, GitHub Actions, PowerShell install scripts.

**Spec:** `docs/superpowers/specs/2026-11-12-windows-status-light-design.md` (read it first).

**Reference implementation:** https://github.com/whatadewitt/claude-status-light (macOS). Clone it next to this repo, because several tasks port from it:

```bash
git clone https://github.com/whatadewitt/claude-status-light ../claude-status-light
```

## Global Constraints

- **Target framework:** `net10.0` for Core and tests; `net10.0-windows` for App, Hook and the Windows tests. Set `Nullable=enable`, `TreatWarningsAsErrors=true` and `LangVersion=latest` in `Directory.Build.props`.
- **AOT-safe Core:** Core must be AOT-compatible (`<IsAotCompatible>true</IsAotCompatible>`). All JSON goes through a source-generated `JsonSerializerContext` or `JsonNode`/`Utf8JsonReader`, never reflection-based serialization.
- **State file format** is identical to the Mac format: `state`, `session_id`, `cwd`, `term_program`, `tty`, `pid` (int, 0 = unknown), `transcript_path`, `permission_mode`, `updated_at` (int epoch seconds). Only these Windows fields may be **added**:
  - `pid_start` (long: process creation time as FILETIME UTC ticks; 0 = unknown)
  - `hwnd` (long; 0 = none)

  The reader also accepts `updated_at` as a string.
- **State directory:** sessions live in `%USERPROFILE%\.claude\status-light\sessions\<id>.json`. Subagent markers live in `<id>.agents\` (one file each, named `<hookPid>.<epochSeconds>`).
- **Session-id sanitization:** keep only `[A-Za-z0-9._-]`; if nothing is left, use `default`.
- **Hook events → state argument:**

  | Events | State argument |
  |---|---|
  | `SessionStart` | `idle` |
  | `UserPromptSubmit`, `PreToolUse`, `PostToolUse` | `working` |
  | `Notification` | `attention` |
  | `Stop` | `idle` |
  | `SubagentStart` | `agent-start` |
  | `SubagentStop` | `agent-stop` |
  | `SessionEnd` | `end` |

  Only `PreToolUse` and `PostToolUse` get `"matcher": "*"`. The hook command string is `"<hookPath>" <state>`. On Windows, `<hookPath>` uses **forward slashes**, e.g. `"C:/Users/x/AppData/Local/Programs/ClaudeStatusLight/status-hook.exe" working`.
- **The hook never fails Claude Code:** `status-hook.exe <state>` always exits 0, writes nothing to stdout or stderr, and catches every exception.
- **Colors:**

  | State | Color |
  |---|---|
  | off | `#8E8E93` |
  | working | `#FFCC00` |
  | idle | `#34C759` |
  | attention | `#FF3B30` |
  | agent badge | `#0A84FF` |
- **Timing values** ported from the Mac:
  - poll every 1 s (native); WSL every 3 s
  - dance frame every 0.5 s
  - parked after 2 min of headless idle
  - stale after 12 h when there's no pid
  - auto-hide collapse grace 0.4 s
  - floating tile 44 px, with a 28 px icon inside
- **WSL access:** the app never starts a stopped WSL distro. It only touches distros listed by `wsl.exe -l --running -q`.
- **Settings defaults:** `ShowFloatingWindow=true`, `LockToCorner=true`, `Corner=TopRight`, `AutoHide=false`, `GreenBeatsYellow=true`, `StartAtLogin=true`. Stored at `%APPDATA%\ClaudeStatusLight\settings.json`.
- **Install location:** `%LOCALAPPDATA%\Programs\ClaudeStatusLight\` containing `ClaudeStatusLight.exe`, `status-hook.exe` and `status-hook.sh`.
- **Release asset:** `ClaudeStatusLight-win-x64.zip` plus `ClaudeStatusLight-win-x64.zip.sha256`.

## Development environment

- Development starts on the user's Mac, which has no .NET SDK. Install it with `brew install --cask dotnet-sdk`, then check that `dotnet --list-sdks` shows 10.x.
- Core tests run on the Mac with `dotnet test tests/StatusLight.Core.Tests`.
- App and Hook compile on the Mac thanks to `EnableWindowsTargeting=true`, but they can only run, AOT-publish and pass the Windows tests in CI (`windows-latest`) or on the user's Windows machine.
- **Creating and pushing the GitHub repo is the user's decision.** Ask before doing it. Until then, Windows-only verification steps are marked **[CI]**.

## Review Focus

1. **The hook writes while the app reads.** On Windows, a reader holding the file open can make the hook's write fail with a sharing violation, and a half-written file can be read. Expected behaviour:
   - The hook writes to a temp file and renames it, with up to 5 retries 20 ms apart.
   - The reader opens with `FileShare.ReadWrite | FileShare.Delete` and skips unreadable or partial JSON for that tick only. It never deletes the file and never crashes.
   - Tested in Tasks 4 and 7.
2. **PID reuse.** Windows reuses PIDs quickly, so a dead session's pid can belong to an unrelated process minutes later. Expected: a session is alive only if the pid exists **and** its creation time equals the recorded `pid_start` (when non-zero). Tested in Task 7.
3. **The pop-out steals focus.** When a session turns red and the auto-hidden window pops out, the user may be mid-typing in a terminal. Expected: the floating window never activates. It uses `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and `ShowActivated=false`. Tested in Task 10.
4. **The user's `settings.json` is unusual.** It may have a UTF-8 BOM (common after PowerShell edits), other hooks on the same events, unrelated keys, or be missing entirely (as may the `.claude` folder). Expected:
   - A BOM is tolerated and the file is written back without one.
   - All other content is preserved byte-equivalent as JSON.
   - A missing file or folder is created.
   - Invalid JSON aborts with exit 2 and no write.
   - Tested in Task 6.
5. **Icon handle leak from the dance.** At 2 fps, `Icon.FromHandle(bitmap.GetHicon())` without `DestroyIcon` leaks a GDI handle every frame, and the process hits the 10,000-handle limit within hours. Expected: the GDI object count stays flat across thousands of renders. Tested in Task 9.

---

### Task 1: Repo skeleton, light state and aggregation, CI

**Files:**
- Create: `.gitignore`, `.gitattributes`, `Directory.Build.props`, `StatusLight.slnx`
- Create: `src/StatusLight.Core/StatusLight.Core.csproj`, `src/StatusLight.Core/LightState.cs`
- Create: `tests/StatusLight.Core.Tests/StatusLight.Core.Tests.csproj`, `tests/StatusLight.Core.Tests/LightStateTests.cs`
- Create: `.github/workflows/ci.yml`
- Create: `hooks/wsl/status-hook.sh`, copied byte-for-byte from `../claude-status-light/hooks/status-hook.sh`, with a header comment noting its origin and commit SHA.
- Create: `tests/wsl/hook_test.sh`, copied from `../claude-status-light/Tests/hook_test.sh`, with `HOOK=` pointed at `hooks/wsl/status-hook.sh`.

**Interfaces:**
- Produces:
  - `enum LightState { Off, Working, Idle, Attention }`
  - `static class LightStates` with:
    - `bool TryParse(string? raw, out LightState state)` (accepts lowercase `off|working|idle|attention`)
    - `string ToWire(LightState s)`
    - `string Label(LightState s)`
    - `LightState Aggregate(IEnumerable<LightState> states, bool greenBeatsYellow)`

- [ ] **Step 1:** Run `git init`. Write the `.gitignore` (`bin/`, `obj/`, `.superpowers/`, `*.user`, `artifacts/`) and the `.gitattributes` (`* text=auto`, `*.sh text eol=lf`, `*.ps1 text eol=crlf`).
- [ ] **Step 2:** Write `Directory.Build.props` per Global Constraints. Create the Core and Core.Tests projects and add both to `StatusLight.slnx`.
- [ ] **Step 3: Write the failing tests** in `LightStateTests.cs`:

```csharp
[Fact] public void Empty_is_off() => Assert.Equal(LightState.Off, LightStates.Aggregate([], true));
[Fact] public void Red_always_wins() {
    Assert.Equal(LightState.Attention, LightStates.Aggregate([LightState.Idle, LightState.Attention, LightState.Working], true));
    Assert.Equal(LightState.Attention, LightStates.Aggregate([LightState.Idle, LightState.Attention, LightState.Working], false));
}
[Fact] public void Green_beats_yellow_when_set() => Assert.Equal(LightState.Idle, LightStates.Aggregate([LightState.Working, LightState.Idle], true));
[Fact] public void Yellow_beats_green_when_unset() => Assert.Equal(LightState.Working, LightStates.Aggregate([LightState.Working, LightState.Idle], false));
[Theory, InlineData("idle", LightState.Idle), InlineData("attention", LightState.Attention)]
public void Parses_wire(string raw, LightState want) { Assert.True(LightStates.TryParse(raw, out var s)); Assert.Equal(want, s); }
[Fact] public void Rejects_unknown() => Assert.False(LightStates.TryParse("end", out _));
[Fact] public void Labels_match_mac() {
    Assert.Equal("Waiting for input", LightStates.Label(LightState.Attention));
    Assert.Equal("Running…", LightStates.Label(LightState.Working));
    Assert.Equal("Awaiting next task", LightStates.Label(LightState.Idle));
    Assert.Equal("Not running", LightStates.Label(LightState.Off));
}
```

- [ ] **Step 4:** Run `dotnet test tests/StatusLight.Core.Tests`. Expected: FAIL (types missing).
- [ ] **Step 5:** Implement `LightState.cs`. The priority order is the Mac `StateStore.aggregate`.
- [ ] **Step 6:** Run `dotnet test tests/StatusLight.Core.Tests`. Expected: PASS.
- [ ] **Step 7:** Write `ci.yml` with two jobs, triggered on push and pull_request:
  - `core` on `ubuntu-latest`: `actions/setup-dotnet` 10.x, `dotnet test tests/StatusLight.Core.Tests`, then `bash tests/wsl/hook_test.sh`.
  - `windows` on `windows-latest`: `dotnet build StatusLight.slnx -c Release`, then `dotnet test StatusLight.slnx -c Release`.
- [ ] **Step 8:** Run `bash tests/wsl/hook_test.sh`. Expected: every section prints `ok`. This proves the copied WSL hook works on Linux semantics; it runs on macOS too.
- [ ] **Step 9: Commit:** `git add -A && git commit -m "chore: skeleton, light state aggregation, CI, WSL hook copy"`

---

### Task 2: Session model and row text

**Files:**
- Create: `src/StatusLight.Core/SessionState.cs`, `src/StatusLight.Core/SessionText.cs`
- Test: `tests/StatusLight.Core.Tests/SessionTextTests.cs`. Port the cases from the Mac `Tests/ClaudeStatusLightTests/SessionLabelTests.swift` in addition to those below.

**Interfaces:**
- Consumes: `LightState` (Task 1).
- Produces:
  - `enum SessionOrigin { Windows, Wsl }`
  - `enum HostTag { None, PowerShell, Wsl, ClaudeApp }`
  - `sealed record SessionState` with:
    - `required string SessionId`
    - `required LightState State`
    - `string Cwd = ""`
    - `string TermProgram = "unknown"`
    - `string Tty = ""`
    - `int? Pid`
    - `long PidStart`
    - `long Hwnd`
    - `DateTimeOffset UpdatedAt`
    - `int Agents`
    - `string? Title`
    - `IReadOnlyList<string> Shells = []`
    - `string? PermissionMode`
    - `SessionOrigin Origin`
    - `string? Distro`
    - computed: `bool IsBackground` (Pid not null and Tty empty), `HostTag Tag`, `string Project`
    - method: `bool IsParked(DateTimeOffset now)`
  - `static class SessionText` with:
    - `string ProjectName(string cwd)`
    - `string DisplayName(SessionState s)`
    - `string AgentsSuffix(int n)`
    - `string ModeSuffix(string? mode)`
    - `string ShellsSuffix(SessionState s)`
    - `string PanelRow(SessionState s)` = `DisplayName + ModeSuffix + ShellsSuffix + AgentsSuffix`
    - `string MenuRow(SessionState s)` = `DisplayName + ModeSuffix + ShellsSuffix + " — " + Label + AgentsSuffix`
    - `string Tooltip(SessionState s, DateTimeOffset now)`
    - `string TagText(HostTag t)`, which returns `""`, `"PowerShell"`, `"WSL"` or `"Claude app"`

  `Tag` rules:
  - `Origin == Wsl` → `Wsl`
  - `Tty` starts with `claude-app:` → `ClaudeApp`
  - `Tty` starts with `wt:` or `console:` → `PowerShell`
  - otherwise `None`

- [ ] **Step 1: Write the failing tests**, including:

```csharp
[Theory]
[InlineData(@"C:\Users\luke\code\api-server", "api-server")]
[InlineData(@"C:\Users\luke\code\api-server\", "api-server")]
[InlineData("/home/luke/mlb-props", "mlb-props")]
[InlineData("", "session")]
public void Project_name_splits_both_separators_on_any_os(string cwd, string want) => Assert.Equal(want, SessionText.ProjectName(cwd));

[Fact] public void Mode_suffixes() {
    Assert.Equal("", SessionText.ModeSuffix("default"));
    Assert.Equal(" · plan", SessionText.ModeSuffix("plan"));
    Assert.Equal(" · auto-edit", SessionText.ModeSuffix("acceptEdits"));
    Assert.Equal(" · bypass", SessionText.ModeSuffix("bypassPermissions"));
    Assert.Equal(" · don't-ask", SessionText.ModeSuffix("dontAsk"));
    Assert.Equal(" · newMode", SessionText.ModeSuffix("newMode"));
}
[Fact] public void Agents_suffix() { Assert.Equal("", SessionText.AgentsSuffix(0)); Assert.Equal(" · 1 agent", SessionText.AgentsSuffix(1)); Assert.Equal(" · 2 agents", SessionText.AgentsSuffix(2)); }
[Fact] public void Background_untitled_shows_bg() => Assert.Equal("proj (bg)", SessionText.DisplayName(Bg(title: null)));
[Fact] public void Background_title_truncates_at_48() // a 60-char title becomes 47 chars + "…"
[Fact] public void Single_shell_shows_command_truncated_at_40()   // " · sh: " + 39 chars + "…"
[Fact] public void Multiple_shells_show_count() // " · running 3 shells…"
[Fact] public void Titled_background_row_hides_shells()
[Fact] public void Parked_only_when_headless_idle_over_two_minutes()
[Fact] public void Tags() // wt:… → PowerShell, console:… → PowerShell, claude-app:… → ClaudeApp, Wsl origin → Wsl, empty tty → None
[Fact] public void Interactive_windows_session_is_not_background() // Pid=10, Tty="wt:guid" → IsBackground false
```

- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter SessionTextTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `SessionState.cs` and `SessionText.cs`. Port the semantics from the Mac `Model.swift`. `ProjectName` splits on both `/` and `\` explicitly; don't use `Path.GetFileName`, which ignores `\` on macOS and Linux.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5: Commit:** `git commit -am "feat(core): session model and row text"` (`git add` the new files first).

---

### Task 3: Transcript titles and shell-command extraction

**Files:**
- Create: `src/StatusLight.Core/TranscriptTitles.cs`, `src/StatusLight.Core/ShellCommands.cs`
- Test: `tests/StatusLight.Core.Tests/TranscriptTitlesTests.cs`, `tests/StatusLight.Core.Tests/ShellCommandsTests.cs`. Also port the Mac `ShellScannerTests.swift` command-extraction cases.

**Interfaces:**
- Produces:
  - `static string? TranscriptTitles.ExtractTitle(string head)`
  - `sealed class TranscriptTitleCache` with `string? TitleFor(string path)`. It reads the first 64 KiB, caches by (path, size), and returns null for missing or empty paths.
  - `static class ShellCommands` with:
    - `bool IsClaudeShell(string commandLine)`: true if the line contains `.claude/shell-snapshots/snapshot-` or `.claude\shell-snapshots\snapshot-`
    - `string CommandFromArgv(string commandLine)`

- [ ] **Step 1: Write the failing tests:**
  - `agent-name` wins over `ai-title`.
  - The latest of each kind wins.
  - A line cut off at the 64 KiB boundary is skipped.
  - An empty title gives null.
  - The cache does not re-read when the size is unchanged; assert this by deleting and recreating a file of equal size with different content, and checking the old title is returned.
  - Shell cases, with these expected values:
    - `CommandFromArgv("/bin/bash -c source /home/u/.claude/shell-snapshots/snapshot-bash-1.sh && eval 'uv run python x.py' < /dev/null")` → `"uv run python x.py"`
    - `'\''` → `'`
    - an unrecognized shape is returned unchanged
    - `IsClaudeShell` is true for both the slash and backslash forms
- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter "TranscriptTitlesTests|ShellCommandsTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement both, porting `StateStore.extractTitle` and `ShellScanner.command(fromArgv:)`.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): transcript titles and shell command extraction"`

---

### Task 4: Hook logic (classification, sticky rules, atomic files)

**Files:**
- Create: `src/StatusLight.Core/Hook/HookPayload.cs`, `HookClassifier.cs`, `SessionRecord.cs`, `SessionFiles.cs`, `HookRunner.cs`, `ProcessIdentity.cs`, `SessionIds.cs`, `StatusLightJsonContext.cs`
- Test: `tests/StatusLight.Core.Tests/HookRunnerTests.cs`

**Interfaces:**
- Produces:
  - `sealed record HookPayload(string? SessionId, string? Cwd, string? TranscriptPath, string? PermissionMode, string? HookEventName, string? ToolName, string? Message)`
  - `static HookPayload HookPayload.Parse(string json)`. It never throws; invalid or empty input returns all nulls.
  - `static string HookClassifier.Resolve(string stateArg, HookPayload p)`:
    - `attention` plus a message containing "waiting for your input" (case-insensitive) → `idle`
    - `working` plus `HookEventName=="PreToolUse"` plus `ToolName=="AskUserQuestion"` → `attention`
    - otherwise `stateArg`
  - `static string SessionIds.Sanitize(string? id)`
  - `sealed record ProcessIdentity(int Pid, long PidStart, string TermProgram, string Tty, long Hwnd)`, with `static ProcessIdentity Unknown => new(0, 0, "unknown", "", 0)`
  - `sealed record SessionRecord`: the JSON DTO with every Global-Constraints field, snake_case names, serialized via `StatusLightJsonContext`.
  - `static class SessionFiles` with:
    - `SessionRecord? TryRead(string path)`: opens with `FileShare.ReadWrite | FileShare.Delete`; returns null on any IO or parse failure; accepts `updated_at` as a number or string.
    - `void WriteAtomic(string path, SessionRecord r)`: temp file in the same dir, then `File.Move(overwrite: true)`, retried 5× at 20 ms.
  - `sealed class HookRunner(string sessionsDir, Func<ProcessIdentity> identify, Func<DateTimeOffset> clock, Func<string> markerName)` with `void Run(string stateArg, string payloadJson, string fallbackCwd)`

- [ ] **Step 1: Write the failing tests.** These are a C# port of every section of `../claude-status-light/Tests/hook_test.sh`, using a temp sessions dir and a fake `identify`. In addition:

```csharp
[Fact] public void Sticky_terminal_identity_survives_ttyless_event() {
    // first event: identify → (Pid 100, PidStart 5, "WindowsTerminal", "wt:abc", 0)
    // second event: identify → (Pid 200, PidStart 9, "unknown", "", 0)
    // file has pid 100, pid_start 5, tty "wt:abc", term_program "WindowsTerminal"
}
[Fact] public void Permission_mode_is_sticky_when_event_omits_it()
[Fact] public void Path_traversal_id_is_sanitized() // session_id "..\\..\\evil" → file "....evil.json" inside sessionsDir
[Fact] public void Empty_stdin_writes_default_session()
[Fact] public void Write_succeeds_while_reader_holds_file_open() {
    // open existing <id>.json with FileShare.ReadWrite|Delete, keep it open, Run(...), assert new content readable after close
}
[Fact] public void TryRead_returns_null_for_truncated_json() // write "{\"state\":\"wor" → null, file not deleted
[Fact] public void Windows_backslash_cwd_round_trips() // cwd "C:\\Users\\x\\proj" in payload → record.Cwd == @"C:\Users\x\proj"
```

- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter HookRunnerTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the files. Port the behaviour from `status-hook.sh`:
  - `agent-start` creates nothing unless the session file exists.
  - `agent-stop` deletes one marker, and is harmless when there are none.
  - `SessionStart` wipes the `.agents` directory.
  - `end` deletes the file and the agents directory.
  - The sticky rule: when the new identity has an empty `Tty` and the existing record has a non-empty one, keep the old `Pid`, `PidStart`, `TermProgram`, `Tty` and `Hwnd`.
  - `fallbackCwd` is used when the payload has no cwd.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5: Commit:** `git commit -m "feat(core): hook classification, sticky identity, atomic session files"`

---

### Task 5: `status-hook.exe` and Windows process identity

**Files:**
- Create: `src/StatusLight.Core/Hook/HostClassifier.cs`, `src/StatusLight.Core/Hook/ProcEntry.cs`
- Create: `src/StatusLight.Hook/StatusLight.Hook.csproj` (`OutputType=Exe`, `AssemblyName=status-hook`, `PublishAot=true`, `RuntimeIdentifier=win-x64`, `InvariantGlobalization=true`)
- Create: `src/StatusLight.Hook/Program.cs`, `src/StatusLight.Hook/WindowsProcessChain.cs`
- Create: `tests/StatusLight.Windows.Tests/StatusLight.Windows.Tests.csproj` (`net10.0-windows`; CI windows job only)
- Test: `tests/StatusLight.Core.Tests/HostClassifierTests.cs`, `tests/StatusLight.Windows.Tests/HookExeTests.cs`

**Interfaces:**
- Consumes: `HookRunner`, `ProcessIdentity` (Task 4).
- Produces:
  - `sealed record ProcEntry(int Pid, int ParentPid, string Name, string ImagePath, long StartFileTime)`. `Name` is lowercase and has no `.exe`.
  - `static ProcessIdentity HostClassifier.Classify(IReadOnlyList<ProcEntry> ancestors, IReadOnlyDictionary<string,string?> env, long consoleHwnd)`. `ancestors[0]` is the hook's parent, then upward.
  - `static bool HostClassifier.IsClaudeDesktop(ProcEntry p)`
  - `static readonly IReadOnlySet<string> HostClassifier.Shells` = `{bash, sh, dash, zsh, fish, cmd, powershell, pwsh, conhost, openconsole, env}`
  - CLI: `status-hook.exe <state>` reads stdin and runs, always exiting 0. `--install-hooks` and `--uninstall-hooks` are added in Task 6.

**Classify rules**, in order:

1. Claude = the first ancestor whose `Name` is not in `Shells`. If there is none, return `ProcessIdentity.Unknown`.
2. If any ancestor *above* Claude satisfies `IsClaudeDesktop`, the result is `(claude.Pid, claude.StartFileTime, "ClaudeApp", $"claude-app:{desktop.Pid}", 0)`.
3. Else, if `env["WT_SESSION"]` is non-empty: `(…, "WindowsTerminal", $"wt:{WT_SESSION}", 0)`.
4. Else, if `consoleHwnd != 0`: `(…, env["TERM_PROGRAM"] ?? "console", $"console:{claude.Pid}", consoleHwnd)`.
5. Else (headless): `(…, env["TERM_PROGRAM"] ?? "unknown", "", 0)`.

`IsClaudeDesktop`: `Name == "claude"` and `ImagePath` contains `\AnthropicClaude\` or `\WindowsApps\Claude` (case-insensitive). **Verify on the user's machine** by running `Get-Process claude | Select Path` with the desktop app open and a CLI session running. If the real paths differ, update the rule and the test fixtures. Also check whether Claude Code sets `CLAUDE_CODE_ENTRYPOINT` to a desktop-specific value; if it does, prefer that check.

- [ ] **Step 1: Write the failing tests** (`HostClassifierTests`):
  - A shells-only chain gives Unknown.
  - `bash → claude(cli path ~/.local/bin/claude.exe)` with `WT_SESSION=g` gives tty `wt:g` and the claude pid.
  - `cmd → claude → Claude(desktop path …\AnthropicClaude\app-1.0\claude.exe)` gives tty `claude-app:<desktopPid>`, even when `WT_SESSION` is set.
  - With no WT and `consoleHwnd=77`: tty `console:<pid>` and hwnd 77.
  - With no console: tty empty, so the session counts as background.
- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter HostClassifierTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `HostClassifier`. Then implement `WindowsProcessChain.Ancestors(int pid, int max = 12)`:
  - One `CreateToolhelp32Snapshot` call for pid, ppid and exe name.
  - `QueryFullProcessImageName` and `GetProcessTimes` via `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`; an access-denied process gets an empty path and 0.
  - Stop on a cycle, or when a parent's start time is later than the child's (the parent pid was reused).
- [ ] **Step 4:** Implement `Program.cs`:
  - `args[0]` is the state.
  - Read stdin with a 2 s cap.
  - `identify` = `HostClassifier.Classify(WindowsProcessChain.Ancestors(Environment.ProcessId).Skip(1)…, env, GetConsoleWindow())`, so the hook itself is excluded.
  - The sessions dir is `%USERPROFILE%\.claude\status-light\sessions`.
  - Wrap everything in try/catch and `return 0`.
- [ ] **Step 5:** Run `dotnet test tests/StatusLight.Core.Tests`. Expected: PASS.
- [ ] **Step 6:** Write `HookExeTests` **[CI]**:
  - Publish the hook to a temp dir in a fixture (`dotnet publish src/StatusLight.Hook -c Release -o <tmp>`), and set `USERPROFILE` to a temp dir for the child.
  - **Direct:** start `status-hook.exe working` from the test process with a payload on stdin. The recorded `pid` equals the test process id, and `pid_start` is non-zero.
  - **Through a shell:** run via `cmd /c "<exe>" idle`. The recorded `pid` still equals the test process id, because cmd is skipped.
  - **Garbage stdin:** `"not json"` gives exit code 0 and a `default.json` file.
- [ ] **Step 7:** In `ci.yml`'s windows job, add `dotnet publish src/StatusLight.Hook -c Release -r win-x64` before the tests. Push (once the user has created the repo). Expected: the windows job is green.
- [ ] **Step 8: Commit:** `git commit -m "feat(hook): AOT status-hook.exe with Windows host detection"`

---

### Task 6: settings.json hook merging (`--install-hooks` / `--uninstall-hooks`)

**Files:**
- Create: `src/StatusLight.Core/Install/HookEvents.cs`, `HooksMerger.cs`, `SettingsFile.cs`
- Modify: `src/StatusLight.Hook/Program.cs` (add the two subcommands)
- Test: `tests/StatusLight.Core.Tests/HooksMergerTests.cs`

**Interfaces:**
- Produces:
  - `static class HookEvents` with `IReadOnlyList<(string Event, string State)> All` and `IReadOnlySet<string> MatcherEvents`
  - `static string HooksMerger.CommandFor(string hookPath, string state)` → `"\"{hookPath}\" {state}"`
  - `enum MergeOutcome { Changed, Unchanged, InvalidJson }`
  - `static (MergeOutcome, string? Json) HooksMerger.Add(string? existingJson, string hookPath)`
  - `static (MergeOutcome, string? Json) HooksMerger.Remove(string? existingJson, string hookPath)`
  - `static int SettingsFile.AddHooks(string settingsPath, string hookPath, DateTimeOffset now)` and `static int SettingsFile.RemoveHooks(string settingsPath, string hookPath, DateTimeOffset now)`. They return 0 for OK (changed or unchanged) and 2 for invalid JSON (nothing written). Before any change they write a backup to `settings.json.bak.<epoch>`. They create missing directories, strip a leading BOM, write UTF-8 without a BOM, and indent with 2 spaces.
  - CLI: `status-hook.exe --install-hooks <settingsPath> <hookPath>` and `--uninstall-hooks <settingsPath> <hookPath>`, whose exit code is the return value above. On Windows, `hookPath` is normalised to forward slashes **only when it contains a drive letter**. Linux paths pass through.

- [ ] **Step 1: Write the failing tests**, porting `merge_settings.py` semantics:
  - `Add` on null gives all 9 events, with `matcher:"*"` only on Pre/PostToolUse.
  - `Add` twice gives `Unchanged`.
  - An existing unrelated hook on `Stop` and an unrelated top-level key `"model":"x"` are both preserved.
  - `Remove` strips only commands containing `hookPath`, drops emptied groups and events, and drops `"hooks"` if it ends up empty.
  - `"{ not json"` gives `InvalidJson`, and `SettingsFile.AddHooks` returns 2 with the file untouched.
  - A BOM-prefixed file is accepted and written back without a BOM.
  - A missing `.claude` dir is created.
  - Exactly one backup file is created per changing call, and none on `Unchanged`.
  - The command for `C:\Users\x\status-hook.exe` is `"C:/Users/x/status-hook.exe" working`.
- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter HooksMergerTests`. Expected: FAIL.
- [ ] **Step 3:** Implement with `JsonNode`, and add the CLI branches to `Program.cs`.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5: Commit:** `git commit -m "feat: idempotent settings.json hook merging via status-hook.exe"`

---

### Task 7: Reading sessions (directory reader, Windows source, monitor)

**Files:**
- Create: `src/StatusLight.Core/Sources/IProcessTable.cs`, `SessionDirectoryReader.cs`, `ISessionSource.cs`, `SessionMonitor.cs`
- Create: `src/StatusLight.App/Platform/WindowsProcessTable.cs`. The App project is created here: `net10.0-windows`, `UseWPF=true`, `UseWindowsForms=true`, `OutputType=WinExe`, `AssemblyName=ClaudeStatusLight`, referencing Core and `System.Management`. Include a stub `App.xaml`/`App.xaml.cs` (`ShutdownMode=OnExplicitShutdown`, no windows) so it builds; Task 9 fills it in.
- Test: `tests/StatusLight.Core.Tests/SessionDirectoryReaderTests.cs`, `SessionMonitorTests.cs`, `tests/StatusLight.Windows.Tests/WindowsProcessTableTests.cs`

**Interfaces:**
- Consumes: `SessionFiles`, `SessionState`, `TranscriptTitleCache`, `ShellCommands`, `LightStates.Aggregate`.
- Produces:
  - `enum ProcessLiveness { Alive, Dead, Unknown }`
  - `interface IProcessTable` with:
    - `ProcessLiveness Check(int pid, long pidStart)`: `pidStart == 0` skips the start-time comparison.
    - `IReadOnlyList<string> ChildCommandLines(int pid)`
  - `sealed class SessionDirectoryReader(string sessionsDir, SessionOrigin origin, string? distro, IProcessTable processes, Func<string, string> mapTranscriptPath)` with `IReadOnlyList<SessionState> Read(DateTimeOffset now)`
  - `interface ISessionSource` with `IReadOnlyList<SessionState> Poll(DateTimeOffset now)`
  - `sealed class LocalWindowsSource(SessionDirectoryReader reader) : ISessionSource`
  - `sealed record MonitorSnapshot(IReadOnlyList<SessionState> Sessions, LightState Aggregate, int TotalAgents, bool AnyWorking, bool PopOut)`
  - `sealed class SessionMonitor(IReadOnlyList<ISessionSource> sources, Func<bool> greenBeatsYellow)` with `MonitorSnapshot Poll(DateTimeOffset now)`

**Reader rules** (port `StateStore.activeSessions`):
- `Dead` → delete the json and its `.agents` dir, and skip the session.
- `Unknown` → keep it.
- No pid and `updated_at` older than 12 h → skip without deleting.
- Unreadable or partial file → skip this tick, don't delete.
- Agents = file count in `<id>.agents`.
- Shells are scanned only when the state is idle and the pid is known: `ChildCommandLines(pid).Where(IsClaudeShell).Select(CommandFromArgv)`. A non-empty result makes the effective state Working.
- Sorted by `UpdatedAt` descending.

**SessionMonitor rules:**
- Merge all sources. If one source throws, it contributes nothing that tick and the others still show.
- `PopOut` = the set of (origin, distro, id) keys in Attention contains a key that wasn't in the previous poll.
- `AnyWorking` = any session state is Working.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public void Dead_pid_removes_files()
[Fact] public void Reused_pid_with_different_start_time_is_dead() // fake table: Check(42, 5) → Dead because table's start for 42 is 9
[Fact] public void Unknown_liveness_keeps_session()
[Fact] public void Stale_pidless_session_skipped_not_deleted()
[Fact] public void Idle_with_claude_shell_child_becomes_working_with_command()
[Fact] public void Working_session_is_not_shell_scanned() // fake table records no ChildCommandLines call
[Fact] public void Partial_json_skipped_and_kept()
[Fact] public void Agents_counted_from_markers()
[Fact] public void Monitor_pops_out_only_on_new_attention() // poll1: A attention → PopOut true; poll2 same → false; poll3 B attention → true
[Fact] public void Monitor_survives_throwing_source()
```

- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter "SessionDirectoryReaderTests|SessionMonitorTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement the Core files.
- [ ] **Step 4:** Implement `WindowsProcessTable`:
  - `Check` uses `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` and `GetProcessTimes`.
  - Results: the process is not found → `Dead`; access is denied → `Unknown`; the start time differs (when `pidStart != 0`) → `Dead`.
  - `ChildCommandLines` uses WMI: `SELECT CommandLine FROM Win32_Process WHERE ParentProcessId = {pid}`.
- [ ] **Step 5:** Run `dotnet test tests/StatusLight.Core.Tests`. Expected: PASS.
- [ ] **Step 6:** Add `WindowsProcessTableTests` **[CI]**:
  - The current process with its true start time → `Alive`.
  - The current pid with the wrong start time → `Dead`.
  - An exited child's pid → `Dead`.
  - A spawned `cmd /c "echo .claude\shell-snapshots\snapshot-x && eval 'ping -n 5 127.0.0.1'"` appears in `ChildCommandLines(Environment.ProcessId)`.
- [ ] **Step 7: Commit:** `git commit -m "feat: session directory reader, Windows process table, monitor"`

---

### Task 8: WSL session source

**Files:**
- Create: `src/StatusLight.Core/Wsl/ICommandRunner.cs`, `WslOutput.cs`, `WslPaths.cs`, `WslProcessTable.cs`, `WslSessionSource.cs`
- Create: `src/StatusLight.App/Platform/WslCommandRunner.cs`
- Test: `tests/StatusLight.Core.Tests/WslSessionSourceTests.cs`

**Interfaces:**
- Consumes: `SessionDirectoryReader`, `IProcessTable`, `ISessionSource` (Task 7).
- Produces:
  - `sealed record CommandResult(int ExitCode, string Stdout)`
  - `interface ICommandRunner` with `CommandResult Run(IReadOnlyList<string> args, TimeSpan timeout)`. The args go to `wsl.exe`.
  - `static IReadOnlyList<string> WslOutput.ParseDistroList(string stdout)`: strips `\0` and `\r`, trims, drops empties.
  - `static IReadOnlyList<PsRow> WslOutput.ParsePs(string stdout)` with `sealed record PsRow(int Pid, int Ppid, string Args)`
  - `static string WslPaths.Map(string root, string linuxPath)` → `Path.Combine([root, ..linuxPath.Split('/', RemoveEmptyEntries)])`
  - `sealed class WslProcessTable(IReadOnlyList<PsRow> rows) : IProcessTable`. A pid present → Alive, absent → Dead; `pidStart` is ignored.
  - `sealed class WslSessionSource(ICommandRunner wsl, Func<string, string> uncRoot, TimeSpan minInterval) : ISessionSource`. The default `uncRoot` is `d => $@"\\wsl.localhost\{d}"`, and the app passes `minInterval` = 3 s. When less than `minInterval` has passed since the last real poll, `Poll(now)` returns the previous result without running anything.

**Poll algorithm:**
1. Run `-l --running -q` and parse it into distros.
2. For each distro, if its home isn't cached, run `-d <d> -e sh -c 'printf %s "$HOME"'` once and cache it. Drop the cache entry when the distro is no longer running.
3. The sessions dir is `WslPaths.Map(uncRoot(d), home + "/.claude/status-light/sessions")`. If it contains no `*.json`, skip the distro and run **no** `ps`.
4. Run `-d <d> -e ps -eo pid=,ppid=,args=` and read the files with a `SessionDirectoryReader(dir, Wsl, d, new WslProcessTable(rows), p => WslPaths.Map(uncRoot(d), p))`.

`WslCommandRunner` (App) starts `wsl.exe` with:
- `CreateNoWindow=true` and `UseShellExecute=false`, so no console window flashes
- environment `WSL_UTF8=1`
- UTF-8 stdout decoding, falling back to UTF-16LE when the bytes contain NULs
- a 5 s timeout that kills the process and returns exit −1

- [ ] **Step 1: Write the failing tests** with a fake runner that records calls and serves canned output, and a temp dir as `uncRoot`:
  - **Stopped distros are left alone:** with `-l --running` returning empty, only that one call is made.
  - **No work without files:** a running distro with no session files gets the home lookup and no `ps` call.
  - **Liveness from ps:** a file whose pid appears in the ps output is returned with `Origin=Wsl`, `Distro="Ubuntu"`, `Tag=Wsl`; a file whose pid is absent is deleted.
  - **Background shells:** an idle session with a ps child carrying `/.claude/shell-snapshots/snapshot-` becomes Working.
  - **UTF-16 tolerance:** `ParseDistroList("U\0b\0u\0n\0t\0u\0\r\0\n\0")` → `["Ubuntu"]`.
  - **Home is cached:** the home lookup is made once across two polls.
  - **Throttled:** two polls 1 s apart with `minInterval` = 3 s give exactly one `-l --running` call, and the second poll returns the same sessions.
- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter WslSessionSourceTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the Core files and `WslCommandRunner`.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5: Commit:** `git commit -m "feat: WSL session source over wsl.localhost"`

---

### Task 9: App shell (settings, icon rendering, dance, tray)

**Files:**
- Create: `src/StatusLight.Core/AppSettings.cs`, `src/StatusLight.Core/Mascot.cs`, `src/StatusLight.Core/DanceSequencer.cs`
- Create: `src/StatusLight.App/App.xaml`, `App.xaml.cs`, `AppHost.cs`, `Rendering/IconRenderer.cs`, `Rendering/IconCache.cs`, `Tray/TrayController.cs`, `Platform/Win32.cs`, `app.manifest` (PerMonitorV2 DPI awareness)
- Test: `tests/StatusLight.Core.Tests/AppSettingsTests.cs`, `DanceSequencerTests.cs`, `tests/StatusLight.Windows.Tests/IconCacheTests.cs`

**Interfaces:**
- Consumes: `SessionMonitor`, `MonitorSnapshot`, `LocalWindowsSource`, `WslSessionSource`, `SessionText`.
- Produces:
  - `enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }`
  - `sealed record AppSettings` with Global-Constraints defaults plus `double? FloatingX`, `double? FloatingY`, `string? FloatingMonitor`
  - `sealed class SettingsStore(string path)` with:
    - `AppSettings Load()`: a corrupt or missing file gives the defaults; never throws.
    - `void Save(AppSettings s)`
    - `event Action<AppSettings>? Changed`
  - `static class Mascot` with `IReadOnlyList<string[]> Frames` and `IReadOnlyList<int[]> DanceMoves`, copied verbatim from the Mac `IconRenderer.swift` `mascotFrames` and `danceMoves`
  - `sealed class DanceSequencer(Random rng)` with `int Tick()` (the next frame index; never repeats the same move twice in a row) and `void Reset()` (frame 0)
  - `static Bitmap IconRenderer.Render(LightState state, int side, int frame, int agents, Image? custom)`
  - `sealed class IconCache : IDisposable` with `Icon Get(LightState s, int side, int frame, int agents)`. It owns every HICON and calls `DestroyIcon` on eviction and on Dispose. It is keyed by its arguments plus the custom-icon mtime.
  - `sealed class AppHost` (composition root) with `event Action<MonitorSnapshot>? Updated`, `MonitorSnapshot Current` and `int DanceFrame`

**Rendering:**
- Without a custom icon, draw the mascot grid scaled to fit, centred, in the state colour, with nearest-neighbour cells.
- With a custom icon, draw it full-colour with a state dot in the bottom-right: a white ring with the coloured core inset 28 %.
- When `agents > 0`, add a blue `#0A84FF` badge bottom-left with a white ring and a numeral (`9` max).

**AppHost:**
- A `DispatcherTimer` of 1 s runs `monitor.Poll` via `Task.Run`, with no overlapping polls.
- The WSL source is created with `minInterval` = 3 s, so it throttles itself.
- A `FileSystemWatcher` on the native sessions dir triggers a poll, debounced 150 ms.
- While `AnyWorking`, a 0.5 s timer advances `DanceSequencer`; when nothing is working, the timer stops and the sequencer resets.

**Single instance:** a named mutex `Local\ClaudeStatusLight`; a second launch exits immediately.

**Tray:**
- The `NotifyIcon` icon comes from `IconCache.Get(state, SystemInformation.SmallIconSize.Width, frame, agents)`.
- The tooltip is `"Claude · " + LightStates.Label(aggregate)`, truncated to 127 chars.
- Left and right click both open a `ContextMenuStrip` rebuilt from `Current`:
  1. a header `Sessions` (disabled), or `No active sessions`
  2. one item per session: text `SessionText.MenuRow`, a coloured dot image, tooltip `SessionText.Tooltip`, the tag appended as `  [PowerShell]`; background rows are disabled
  3. a separator
  4. `Show floating window` (checkable)
  5. `Settings…`
  6. `Set custom icon…` (opens `%USERPROFILE%\.claude\status-light` in Explorer, creating it if needed)
  7. `Quit`

  Row clicks call `WindowFocuser.Focus` (Task 11; until then a no-op stub with that signature).

- [ ] **Step 1: Write the failing tests:**
  - `AppSettingsTests`: defaults; corrupt file gives defaults; round trip.
  - `DanceSequencerTests`: frames always come from `DanceMoves`; there are no back-to-back identical moves over 200 ticks with a seeded `Random`; `Reset` gives frame 0 on the next read.
- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter "AppSettingsTests|DanceSequencerTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement the Core files.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5:** Implement the App files above.
- [ ] **Step 6:** Write `IconCacheTests.No_gdi_leak_across_many_renders` **[CI]**: take `GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS)` as a baseline, run 5,000 `Get` calls cycling the frames 0–7 with a cache cap of 64 entries, then assert the count is ≤ baseline + 200.
- [ ] **Step 7:** Push. Expected: CI green **[CI]**.
- [ ] **Step 8:** Manual check on Windows, using `-LocalBuild` from Task 12 or `dotnet run --project src/StatusLight.App`:
  - The tray shows a gray mascot.
  - Starting `claude` in PowerShell turns it green.
  - Sending a prompt makes it yellow and dancing.
  - A permission prompt turns it red.
- [ ] **Step 9: Commit:** `git commit -m "feat(app): tray icon, renderer, dance, polling host"`

---

### Task 10: Floating window (corners, drag, auto-hide, no focus stealing)

**Files:**
- Create: `src/StatusLight.Core/PanelGeometry.cs`
- Create: `src/StatusLight.App/Floating/FloatingWindow.xaml`, `FloatingWindow.xaml.cs`, `FloatingController.cs`, `Platform/WindowStyles.cs`
- Test: `tests/StatusLight.Core.Tests/PanelGeometryTests.cs`, `tests/StatusLight.Windows.Tests/WindowStylesTests.cs`

**Interfaces:**
- Consumes: `AppHost.Updated`, `SettingsStore`, `IconCache`, `SessionText`.
- Produces:
  - `readonly record struct RectD(double X, double Y, double Width, double Height)` (y-down; `Bottom = Y + Height`)
  - `static ScreenCorner PanelGeometry.Quadrant(RectD rect, RectD screen)`
  - `static RectD PanelGeometry.Frame(double w, double h, ScreenCorner keeping, RectD of, RectD within)`
  - `static bool PanelGeometry.ShouldPopOut(IReadOnlySet<string> waiting, IReadOnlySet<string> previouslyWaiting)`
  - `static int WindowStyles.FloatingExStyle(int existing)`, which returns `existing | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`

**Window:**
- `WindowStyle=None`, `AllowsTransparency=true`, `Topmost=true`, `ShowInTaskbar=false`, `ShowActivated=false`.
- Apply `FloatingExStyle` in `SourceInitialized`.
- Rounded translucent background that follows the Windows app theme (`HKCU\...\Themes\Personalize\AppsUseLightTheme`).
- Header: the 20 px mascot plus the aggregate label.
- Rows: dot, `SessionText.PanelRow`, and the right-aligned tag in a pill. Parked rows sit at 50 % opacity. The tooltip is `SessionText.Tooltip`. Clicking a row calls `WindowFocuser.Focus`.

**Placement:**
- **Locked:** pin to the chosen corner of the current monitor's work area, with an 8 px margin.
- **Unlocked:** drag by the header. On drag end, save `FloatingX`, `FloatingY` and the `FloatingMonitor` device name.
- **Restore:** if that monitor is gone or the saved point is off every work area, fall back to the top-right of the primary monitor.

**Auto-hide** (port `FloatingPanelController`, flipping y for Windows):
- At rest it's a 44 px tile with a 28 px mascot.
- Mouse enter expands it, keeping `Quadrant(tile)` as the pinned corner.
- Mouse leave collapses it after 0.4 s unless the pointer re-enters or it's held open.
- `MonitorSnapshot.PopOut` expands it and holds it open until the pointer has entered and left, or no session is waiting.

- [ ] **Step 1: Write the failing tests:**
  - `PanelGeometryTests`: port every Mac `PanelGeometryTests.swift` case with y flipped. For example, a tile in the top-right of a `(0,0,1920,1080)` screen gives `TopRight`, and `Frame` with `TopRight` keeps `rect.Right` and `rect.Y`. Add one more: a frame that would spill below the work area is nudged up inside it.
  - `WindowStylesTests`: `FloatingExStyle(0)` contains `0x08000000` (NOACTIVATE) and `0x00000080` (TOOLWINDOW).
- [ ] **Step 2:** Run `dotnet test tests/StatusLight.Core.Tests --filter PanelGeometryTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `PanelGeometry`, `WindowStyles`, the window and the controller.
- [ ] **Step 4:** Run `dotnet test tests/StatusLight.Core.Tests`. Expected: PASS. Then push; expected: the windows CI job is green **[CI]**.
- [ ] **Step 5:** Manual check on Windows:
  - Type in a terminal while another session triggers a permission prompt. The window pops out and the keystrokes keep going to the terminal.
  - Drag, quit and relaunch: the window comes back at the same position.
  - Unplug the second monitor: the window moves back onto the primary.
- [ ] **Step 6: Commit:** `git commit -m "feat(app): floating window with corners, drag, auto-hide"`

---

### Task 11: Click-to-focus, Settings window, start at login

**Files:**
- Create: `src/StatusLight.App/Platform/WindowFocuser.cs`, `Platform/StartupRegistration.cs`, `Settings/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`
- Test: `tests/StatusLight.Windows.Tests/StartupRegistrationTests.cs`

**Interfaces:**
- Consumes: `SessionState.Tty`, `SessionState.Hwnd`, `SessionState.Origin`, `SettingsStore`.
- Produces:
  - `static bool WindowFocuser.Focus(SessionState s)`
  - `sealed class StartupRegistration(string valueName = "ClaudeStatusLight")` with `void Apply(bool enabled, string exePath)` and `bool IsRegistered()`. It writes the HKCU Run value `"<exePath>"`.

**Focus rules:**

| Session | Target window |
|---|---|
| `claude-app:<pid>` | the first visible, unowned top-level window of that pid |
| `wt:*` or `Origin==Wsl` | the topmost visible window with class `CASCADIA_HOSTING_WINDOW_CLASS` in `EnumWindows` z-order |
| `console:*` with `Hwnd != 0` | that hwnd, if `IsWindow` |
| anything else | return false |

Restore the window if it's minimised (`IsIconic` → `ShowWindow(SW_RESTORE)`), then `SetForegroundWindow`. The click may come from the no-activate floating window, which doesn't hold foreground rights. In that case, fall back to `AttachThreadInput` to the current foreground thread around the call, and detach afterwards.

**Settings window** (opened from the tray), with each change saved immediately:
- Show floating window
- Lock to corner, plus a corner dropdown
- Auto-hide
- "Done beats busy" (green over yellow)
- Start at login
- "Reveal icon folder…"

The app applies `StartupRegistration.Apply(settings.StartAtLogin, Environment.ProcessPath)` at every launch.

- [ ] **Step 1:** Write `StartupRegistrationTests` **[CI]** using value name `ClaudeStatusLightTest`. `Apply(true, @"C:\x\a.exe")` then `IsRegistered()` gives true, and the stored value is `"\"C:\x\a.exe\""`. `Apply(false, …)` removes the value. Clean up in Dispose.
- [ ] **Step 2:** Implement the three files.
- [ ] **Step 3:** Push. Expected: the windows CI job is green **[CI]**.
- [ ] **Step 4:** Manual check on Windows. Clicking each kind of row (PowerShell in Windows Terminal, a plain console PowerShell, the Claude app, WSL), from both the tray menu and the floating window, brings that window to the front, including when it was minimised.
- [ ] **Step 5: Commit:** `git commit -m "feat(app): click-to-focus, settings window, start at login"`

---

### Task 12: Release workflow, install and uninstall, README

**Files:**
- Create: `.github/workflows/release.yml`, `scripts/install.ps1`, `scripts/uninstall.ps1`, `README.md`
- Test: `tests/scripts/Install.Tests.ps1` (Pester, run in the windows CI job)

**Interfaces:**
- Consumes: `status-hook.exe --install-hooks` and `--uninstall-hooks` (Task 6), `hooks/wsl/status-hook.sh`, and the release asset names in Global Constraints.

**release.yml** runs on `push: tags: ['v*']` on `windows-latest`. It:
1. Runs the tests.
2. Publishes the app with `dotnet publish src/StatusLight.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out`.
3. Publishes the hook with `dotnet publish src/StatusLight.Hook -c Release -r win-x64 -o out`.
4. Copies `hooks/wsl/status-hook.sh` into `out`.
5. Zips exactly these three files to `ClaudeStatusLight-win-x64.zip` and writes the `.sha256` (lowercase hex, filename only).
6. Runs `gh release create $GITHUB_REF_NAME --generate-notes` with both files attached.

**install.ps1** takes `param([string]$Version, [string]$LocalBuild, [string]$Repo, [switch]$SkipWsl, [switch]$NoStart)`. `uninstall.ps1` takes `param([switch]$SkipWsl, [switch]$NoStop)`. `-NoStart` and `-NoStop` exist for the Pester tests.

1. **Get the files.**
   - With `-LocalBuild <dir>`: use that dir's three files.
   - Otherwise resolve the repo: `$Repo`, else `git remote get-url origin` parsed to `owner/name`.
   - If `gh auth status` succeeds, run `gh release download [$Version] --repo $Repo --pattern 'ClaudeStatusLight-win-x64.zip*'`.
   - Otherwise download from `https://github.com/$Repo/releases/{latest/download|download/$Version}/…`.
   - Verify the SHA-256 and abort on a mismatch.
2. **Install the files.** Stop any `ClaudeStatusLight` process (and wait for it to exit). Copy to the install dir, retrying for 5 s on a locked `status-hook.exe`.
3. **Native hooks.** Run `& status-hook.exe --install-hooks "$env:USERPROFILE\.claude\settings.json" "<installDir>\status-hook.exe"`. Exit 2 means: print "settings.json is not valid JSON — fix it and re-run" and stop.
4. **WSL hooks** (unless `-SkipWsl`, and only if `wsl.exe` exists). For each distro in `wsl.exe -l -q` (with `WSL_UTF8=1`; skip names starting with `docker-desktop`) for which `wsl -d $d -e test -d ~/.claude` succeeds:
   - Get `$home` via `printf %s "$HOME"`.
   - Write the hook bytes **with any `\r` stripped** to `\\wsl.localhost\$d$home\.claude\status-light\status-hook.sh` using `[IO.File]::WriteAllBytes`, after creating the directory.
   - Run `wsl -d $d -e chmod +x …`.
   - Run `status-hook.exe --install-hooks "\\wsl.localhost\$d$home\.claude\settings.json" "$home/.claude/status-light/status-hook.sh"`.
5. **Start the app** (it registers start-at-login itself), then print "Restart open Claude Code sessions so the new hooks load."

**uninstall.ps1** reverses the install:
1. Stop the app and remove the HKCU Run value.
2. Run `--uninstall-hooks` on Windows and on each WSL distro's settings.
3. Delete the WSL `status-hook.sh`, the install dir and `%USERPROFILE%\.claude\status-light\sessions`.
4. Keep `icon.png`.

**README:** what it is, the light table, the surfaces, install and update (re-run `install.ps1`), uninstall, how it works (the spec diagram), the WSL notes, and a pointer to the Mac repo for the upcoming remote feature.

- [ ] **Step 1:** Write the Pester tests in `Install.Tests.ps1`. Run `install.ps1 -LocalBuild <published out dir> -SkipWsl` with `$env:USERPROFILE` and `$env:LOCALAPPDATA` redirected to temp dirs and `-NoStart`, a switch added for tests. Assert:
  - The three files are present.
  - `settings.json` has 9 hook events.
  - A re-run leaves exactly the same `settings.json`.
  - `uninstall.ps1` (same env, `-NoStop`) removes the hooks and the install dir.
- [ ] **Step 2:** Add a Pester step to the windows CI job after `dotnet publish`: `Invoke-Pester tests/scripts`.
- [ ] **Step 3:** Implement `release.yml`, `install.ps1`, `uninstall.ps1` and `README.md`.
- [ ] **Step 4:** Push. Expected: the windows CI job is green, including Pester **[CI]**.
- [ ] **Step 5:** With the user's go-ahead, tag `v0.1.0` and push the tag. Expected: the release workflow publishes both assets.
- [ ] **Step 6:** Manual end-to-end on the user's Windows machine:
  1. `git clone` the repo and run `scripts\install.ps1`.
  2. Restart the Claude Code sessions.
  3. Check that one session each in PowerShell, the Claude app and WSL Ubuntu shows with the right tag and colour.
  4. Close a WSL terminal without `/exit` and check its row disappears within about 3 s.
  5. Run `wsl --shutdown` and check the app doesn't restart the distro; `wsl -l --running` stays empty for a minute.
- [ ] **Step 7: Commit:** `git commit -m "feat: release workflow, install/uninstall scripts, README"`
