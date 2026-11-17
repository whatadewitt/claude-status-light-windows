using StatusLight.Core;

namespace StatusLight.Core.Tests;

public class SessionTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static SessionState S(
        LightState state = LightState.Idle, string tty = "", int? pid = 4242, double age = 0,
        int agents = 0, string? title = null, string[]? shells = null, string? mode = null,
        SessionOrigin origin = SessionOrigin.Windows, string? distro = null) => new()
    {
        SessionId = "s", State = state, Cwd = "/tmp/mlb-props", Tty = tty, Pid = pid,
        UpdatedAt = Now.AddSeconds(-age), Agents = agents, Title = title,
        Shells = shells ?? [], PermissionMode = mode, Origin = origin, Distro = distro,
    };

    private static SessionState Bg(string? title) => S(title: title) with { Cwd = "proj" };

    [Theory]
    [InlineData(@"C:\Users\luke\code\api-server", "api-server")]
    [InlineData(@"C:\Users\luke\code\api-server\", "api-server")]
    [InlineData("/home/luke/mlb-props", "mlb-props")]
    [InlineData("", "session")]
    public void Project_name_splits_both_separators_on_any_os(string cwd, string want) =>
        Assert.Equal(want, SessionText.ProjectName(cwd));

    [Fact]
    public void Mode_suffixes()
    {
        Assert.Equal("", SessionText.ModeSuffix(null));
        Assert.Equal("", SessionText.ModeSuffix("default"));
        Assert.Equal(" · plan", SessionText.ModeSuffix("plan"));
        Assert.Equal(" · auto-edit", SessionText.ModeSuffix("acceptEdits"));
        Assert.Equal(" · auto", SessionText.ModeSuffix("auto"));
        Assert.Equal(" · bypass", SessionText.ModeSuffix("bypassPermissions"));
        Assert.Equal(" · don't-ask", SessionText.ModeSuffix("dontAsk"));
        Assert.Equal(" · newMode", SessionText.ModeSuffix("newMode"));
    }

    [Fact]
    public void Agents_suffix()
    {
        Assert.Equal("", SessionText.AgentsSuffix(0));
        Assert.Equal(" · 1 agent", SessionText.AgentsSuffix(1));
        Assert.Equal(" · 2 agents", SessionText.AgentsSuffix(2));
    }

    [Fact]
    public void Background_untitled_shows_bg() =>
        Assert.Equal("proj (bg)", SessionText.DisplayName(Bg(null)));

    [Fact]
    public void Background_titled_shows_project_and_title() =>
        Assert.Equal("mlb-props · Improve win rate", SessionText.DisplayName(S(title: "Improve win rate")));

    [Fact]
    public void Interactive_session_ignores_title() =>
        Assert.Equal("mlb-props", SessionText.DisplayName(S(tty: "wt:g", title: "chat title")));

    [Fact]
    public void Background_title_truncates_at_48() =>
        Assert.Equal("proj · " + new string('x', 47) + "…", SessionText.DisplayName(Bg(new string('x', 60))));

    [Fact]
    public void Single_shell_shows_command_truncated_at_40()
    {
        Assert.Equal("", SessionText.ShellsSuffix(S()));
        Assert.Equal(" · sh: uv run python train.py", SessionText.ShellsSuffix(S(shells: ["uv run python train.py"])));
        Assert.Equal(" · sh: " + new string('x', 39) + "…", SessionText.ShellsSuffix(S(shells: [new string('x', 60)])));
    }

    [Fact]
    public void Multiple_shells_show_count()
    {
        Assert.Equal(" · running 2 shells…", SessionText.ShellsSuffix(S(shells: ["a", "b"])));
        Assert.Equal(" · running 3 shells…", SessionText.ShellsSuffix(S(shells: ["a", "b", "c"])));
    }

    [Fact]
    public void Titled_background_row_hides_shells()
    {
        var agent = S(title: "Improve win rate", shells: ["sweep"]);
        Assert.Equal("", SessionText.ShellsSuffix(agent));
        Assert.Contains("sh: sweep", SessionText.Tooltip(agent, Now));
        var interactive = S(tty: "wt:g", title: "chat title", shells: ["sleep 5"]);
        Assert.Equal(" · sh: sleep 5", SessionText.ShellsSuffix(interactive));
    }

    [Fact]
    public void Parked_only_when_headless_idle_over_two_minutes()
    {
        Assert.True(S(age: 180).IsParked(Now));
        Assert.False(S(age: 30).IsParked(Now));
        Assert.False(S(state: LightState.Working, age: 180).IsParked(Now));
        Assert.False(S(tty: "wt:g", age: 3600).IsParked(Now));
        Assert.Contains("parked — idle 3m, process alive", SessionText.Tooltip(S(age: 180), Now));
    }

    [Fact]
    public void Tags()
    {
        Assert.Equal(HostTag.PowerShell, S(tty: "wt:abc").Tag);
        Assert.Equal(HostTag.PowerShell, S(tty: "console:1").Tag);
        Assert.Equal(HostTag.ClaudeApp, S(tty: "claude-app:x").Tag);
        Assert.Equal(HostTag.Wsl, S(tty: "wt:abc", origin: SessionOrigin.Wsl).Tag);
        Assert.Equal(HostTag.None, S(tty: "").Tag);
        Assert.Equal("", SessionText.TagText(HostTag.None));
        Assert.Equal("PowerShell", SessionText.TagText(HostTag.PowerShell));
        Assert.Equal("WSL", SessionText.TagText(HostTag.Wsl));
        Assert.Equal("Claude app", SessionText.TagText(HostTag.ClaudeApp));
    }

    [Fact]
    public void Interactive_windows_session_is_not_background() =>
        Assert.False(S(pid: 10, tty: "wt:guid").IsBackground);

    [Fact]
    public void Rows_compose_suffixes()
    {
        var s = S(tty: "wt:g", agents: 2, shells: ["sleep 5"], mode: "plan", state: LightState.Working);
        Assert.Equal("mlb-props · plan · sh: sleep 5 · 2 agents", SessionText.PanelRow(s));
        Assert.Equal("mlb-props · plan · sh: sleep 5 — Running… · 2 agents", SessionText.MenuRow(s));
    }

    [Fact]
    public void Tooltip_lines()
    {
        Assert.Equal("/tmp/mlb-props\nunknown · wt:g\nsh: uv run train.py",
            SessionText.Tooltip(S(tty: "wt:g", shells: ["uv run train.py"]), Now));
        Assert.Equal("/tmp/mlb-props\nunknown · tty unknown\nbackground session (no terminal)",
            SessionText.Tooltip(S(), Now));
        Assert.Contains("mode: default", SessionText.Tooltip(S(mode: "default"), Now));
        Assert.DoesNotContain("mode:", SessionText.Tooltip(S(), Now));
        Assert.Contains("WSL session (Ubuntu)",
            SessionText.Tooltip(S(tty: "wt:g", origin: SessionOrigin.Wsl, distro: "Ubuntu"), Now));
        Assert.Equal(300 + 4, SessionText.Tooltip(S(tty: "wt:g", shells: [new string('y', 500)]), Now)
            .Split('\n')[^1].Length);
    }
}
