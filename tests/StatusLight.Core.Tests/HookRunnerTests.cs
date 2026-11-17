using System.Text.Json;
using StatusLight.Core.Hook;

namespace StatusLight.Core.Tests;

public sealed class HookRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slhook-" + Guid.NewGuid().ToString("N"));
    private string Sessions => Path.Combine(_root, "sessions");
    private int _marker;
    private ProcessIdentity _identity = new(100, 5, "WindowsTerminal", "wt:abc", 77);
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private HookRunner Runner() => new(Sessions, () => _identity, () => _now, () => $"m{++_marker}.1");

    private void Run(string state, string payload, string cwd = @"C:\fallback") => Runner().Run(state, payload, cwd);

    private string F(string id) => Path.Combine(Sessions, id + ".json");
    private string A(string id) => Path.Combine(Sessions, id + ".agents");
    private SessionRecord Read(string id) => SessionFiles.TryRead(F(id))!;
    private static string P(string id, string extra = "") => "{\"session_id\":\"" + id + "\"" + extra + "}";

    [Fact]
    public void Identity_and_fields_are_recorded()
    {
        Run("working", P("a", ",\"cwd\":\"/tmp/proj\",\"transcript_path\":\"/tmp/t/abc.jsonl\""));
        var r = Read("a");
        Assert.Equal("working", r.State);
        Assert.Equal("a", r.SessionId);
        Assert.Equal("/tmp/proj", r.Cwd);
        Assert.Equal(100, r.Pid);
        Assert.Equal(5, r.PidStart);
        Assert.Equal("WindowsTerminal", r.TermProgram);
        Assert.Equal("wt:abc", r.Tty);
        Assert.Equal("/tmp/t/abc.jsonl", r.TranscriptPath);
        Assert.Equal(1_700_000_000, r.UpdatedAt);
    }

    [Fact]
    public void State_file_json_has_mac_field_order_and_integers()
    {
        Run("attention", P("f", ",\"cwd\":\"/tmp/proj\""));
        using var doc = JsonDocument.Parse(File.ReadAllText(F("f")));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "state", "session_id", "cwd", "term_program", "tty", "pid", "transcript_path", "permission_mode", "updated_at", "pid_start", "hwnd" }, names);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("pid").ValueKind);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("updated_at").ValueKind);
    }

    [Fact]
    public void Missing_transcript_records_empty()
    {
        Run("working", P("nt", ",\"cwd\":\"/x\""));
        Assert.Equal("", Read("nt").TranscriptPath);
    }

    [Fact]
    public void End_removes_session_file_and_agents()
    {
        Run("working", P("e"));
        Run("agent-start", P("e"));
        Run("end", P("e"));
        Assert.False(File.Exists(F("e")));
        Assert.False(Directory.Exists(A("e")));
    }

    [Theory]
    [InlineData("Claude is waiting for your input", "idle")]
    [InlineData("Claude needs your permission to use Bash", "attention")]
    public void Notification_message_classification(string message, string expected)
    {
        Run("attention", P("n", ",\"message\":\"" + message + "\""));
        Assert.Equal(expected, Read("n").State);
    }

    [Fact]
    public void AskUserQuestion_pre_upgrades_working_to_attention()
    {
        Run("working", P("ask", ",\"hook_event_name\":\"PreToolUse\",\"tool_name\":\"AskUserQuestion\",\"tool_input\":{}"));
        Run("working", P("bash", ",\"hook_event_name\":\"PreToolUse\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"}"));
        Assert.Equal("attention", Read("ask").State);
        Assert.Equal("working", Read("bash").State);
    }

    [Fact]
    public void Answering_AskUserQuestion_returns_to_working()
    {
        Run("working", P("ans", ",\"hook_event_name\":\"PreToolUse\",\"tool_name\":\"AskUserQuestion\""));
        Run("working", P("ans", ",\"hook_event_name\":\"PostToolUse\",\"tool_name\":\"AskUserQuestion\""));
        Assert.Equal("working", Read("ans").State);
    }

    [Fact]
    public void Agent_markers_are_maintained_per_session()
    {
        Run("working", P("ag"));
        Run("agent-start", P("ag"));
        Run("agent-start", P("ag"));
        Assert.Equal(2, Directory.GetFiles(A("ag")).Length);
        Run("agent-stop", P("ag"));
        Assert.Single(Directory.GetFiles(A("ag")));
        Run("agent-stop", P("ag"));
        Run("agent-stop", P("ag"));
        Assert.Empty(Directory.GetFiles(A("ag")));
        Assert.Equal("working", Read("ag").State);
    }

    [Fact]
    public void Markers_are_empty_files()
    {
        Run("working", P("mk"));
        Run("agent-start", P("mk"));
        Assert.Equal(0, new FileInfo(Directory.GetFiles(A("mk"))[0]).Length);
    }

    [Fact]
    public void Agent_stop_without_dir_is_harmless()
    {
        Run("agent-stop", P("none"));
        Assert.False(Directory.Exists(A("none")));
    }

    [Fact]
    public void Agent_start_without_session_creates_nothing()
    {
        Run("agent-start", P("ghost"));
        Assert.False(File.Exists(F("ghost")));
        Assert.False(Directory.Exists(A("ghost")));
    }

    [Fact]
    public void SessionStart_wipes_markers()
    {
        Run("working", P("ss"));
        Run("agent-start", P("ss"));
        Run("idle", P("ss", ",\"hook_event_name\":\"SessionStart\""));
        Assert.False(Directory.Exists(A("ss")));
        Assert.Equal("idle", Read("ss").State);
    }

    [Fact]
    public void Sticky_terminal_identity_survives_ttyless_event()
    {
        Run("idle", P("st"));
        _identity = new ProcessIdentity(200, 9, "unknown", "", 0);
        Run("working", P("st"));
        var r = Read("st");
        Assert.Equal("working", r.State);
        Assert.Equal(100, r.Pid);
        Assert.Equal(5, r.PidStart);
        Assert.Equal("wt:abc", r.Tty);
        Assert.Equal("WindowsTerminal", r.TermProgram);
        Assert.Equal(77, r.Hwnd);
    }

    [Fact]
    public void No_stickiness_when_session_never_had_a_terminal()
    {
        _identity = new ProcessIdentity(4242, 1, "unknown", "", 0);
        Run("idle", P("hl"));
        _identity = new ProcessIdentity(77, 2, "unknown", "", 0);
        Run("working", P("hl"));
        Assert.Equal(77, Read("hl").Pid);
        Assert.Equal("", Read("hl").Tty);
    }

    [Fact]
    public void Later_tty_event_updates_identity()
    {
        Run("idle", P("up"));
        _identity = new ProcessIdentity(300, 11, "Code", "wt:def", 7);
        Run("working", P("up"));
        var r = Read("up");
        Assert.Equal(300, r.Pid);
        Assert.Equal("wt:def", r.Tty);
        Assert.Equal(7, r.Hwnd);
    }

    [Fact]
    public void Permission_mode_is_sticky_when_event_omits_it()
    {
        Run("working", P("pm", ",\"permission_mode\":\"plan\""));
        Run("attention", P("pm", ",\"message\":\"Claude needs your permission to use Bash\""));
        Assert.Equal("plan", Read("pm").PermissionMode);
        Run("working", P("pm", ",\"permission_mode\":\"acceptEdits\""));
        Assert.Equal("acceptEdits", Read("pm").PermissionMode);
    }

    [Fact]
    public void Session_never_seeing_mode_records_empty()
    {
        Run("working", P("nm"));
        Assert.Equal("", Read("nm").PermissionMode);
    }

    [Fact]
    public void Path_traversal_id_is_sanitized()
    {
        Run("working", "{\"session_id\":\"..\\\\..\\\\evil\"}");
        Assert.True(File.Exists(Path.Combine(Sessions, "....evil.json")));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Empty_stdin_writes_default_session()
    {
        Run("idle", "", @"C:\work");
        var r = Read("default");
        Assert.Equal("default", r.SessionId);
        Assert.Equal(@"C:\work", r.Cwd);
    }

    [Fact]
    public void Garbage_stdin_writes_default_session()
    {
        Run("idle", "not json");
        Assert.Equal("idle", Read("default").State);
    }

    [Fact]
    public void Write_succeeds_while_reader_holds_file_open()
    {
        Run("idle", P("lk"));
        using (var held = new FileStream(F("lk"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            Run("working", P("lk"));
        }
        Assert.Equal("working", Read("lk").State);
    }

    [Fact]
    public void TryRead_returns_null_for_truncated_json()
    {
        Directory.CreateDirectory(Sessions);
        File.WriteAllText(F("tr"), "{\"state\":\"wor");
        Assert.Null(SessionFiles.TryRead(F("tr")));
        Assert.True(File.Exists(F("tr")));
    }

    [Fact]
    public void TryRead_returns_null_for_missing_file()
    {
        Assert.Null(SessionFiles.TryRead(F("nope")));
    }

    [Fact]
    public void TryRead_accepts_string_updated_at_and_mac_files_without_windows_fields()
    {
        Directory.CreateDirectory(Sessions);
        File.WriteAllText(F("mac"), "{\"state\":\"idle\",\"session_id\":\"mac\",\"pid\":4242,\"updated_at\":\"123\"}");
        var r = SessionFiles.TryRead(F("mac"))!;
        Assert.Equal(123, r.UpdatedAt);
        Assert.Equal(4242, r.Pid);
        Assert.Equal(0, r.PidStart);
        Assert.Equal(0, r.Hwnd);
        Assert.Equal("", r.Tty);
    }

    [Fact]
    public void Windows_backslash_cwd_round_trips()
    {
        Run("working", "{\"session_id\":\"bs\",\"cwd\":\"C:\\\\Users\\\\x\\\\proj\"}");
        Assert.Equal(@"C:\Users\x\proj", Read("bs").Cwd);
    }

    [Fact]
    public void Payload_with_unexpected_types_is_tolerated()
    {
        var p = HookPayload.Parse("{\"session_id\":5,\"tool_input\":{\"a\":1},\"cwd\":\"/x\",\"message\":[1]}");
        Assert.Null(p.SessionId);
        Assert.Equal("/x", p.Cwd);
        Assert.Null(p.Message);
    }

    [Fact]
    public void Default_marker_name_is_pid_dot_epoch()
    {
        var m = System.Text.RegularExpressions.Regex.Match(HookRunner.DefaultMarkerName(), @"^(\d+)\.(\d+)$");
        Assert.True(m.Success);
        Assert.Equal(Environment.ProcessId.ToString(), m.Groups[1].Value);
    }

    [Fact]
    public void Deeply_nested_payload_still_yields_session_id()
    {
        var deep = new string('[', 200) + new string(']', 200);
        var p = HookPayload.Parse("{\"session_id\":\"deep\",\"tool_input\":" + deep + "}");
        Assert.Equal("deep", p.SessionId);
    }

    [Fact]
    public void Sanitize_falls_back_to_default()
    {
        Assert.Equal("default", SessionIds.Sanitize("///"));
        Assert.Equal("default", SessionIds.Sanitize(null));
        Assert.Equal("a.b_c-d", SessionIds.Sanitize("a.b/_c\\-d"));
    }

    [Fact]
    public void No_temp_files_left_behind()
    {
        Run("working", P("tmp"));
        Assert.Equal(new[] { "tmp.json" }, Directory.GetFiles(Sessions).Select(Path.GetFileName).ToArray());
    }
}
