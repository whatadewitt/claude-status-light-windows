using System.Text.Json.Nodes;
using StatusLight.Core.Install;

namespace StatusLight.Core.Tests;

public sealed class HooksMergerTests : IDisposable
{
    private const string Hook = "/opt/sl/status-hook";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hm-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public HooksMergerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private static JsonNode P(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void Add_OnNull_AddsAllNineEvents_MatcherOnlyOnToolEvents()
    {
        var (outcome, json) = HooksMerger.Add(null, Hook);
        Assert.Equal(MergeOutcome.Changed, outcome);
        var hooks = P(json!)["hooks"]!.AsObject();
        Assert.Equal(9, hooks.Count);
        foreach (var (evt, state) in HookEvents.All)
        {
            var group = hooks[evt]!.AsArray().Single()!.AsObject();
            Assert.Equal(HookEvents.MatcherEvents.Contains(evt) ? "*" : null, (string?)group["matcher"]);
            var h = group["hooks"]!.AsArray().Single()!;
            Assert.Equal("command", (string?)h["type"]);
            Assert.Equal($"\"{Hook}\" {state}", (string?)h["command"]);
        }
    }

    [Fact]
    public void Add_Twice_IsUnchanged()
    {
        var (_, json) = HooksMerger.Add(null, Hook);
        var (outcome, _) = HooksMerger.Add(json, Hook);
        Assert.Equal(MergeOutcome.Unchanged, outcome);
    }

    [Fact]
    public void Add_PreservesUnrelatedHooksAndKeys()
    {
        var original = """{"model":"x","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo hi"}]}]}}""";
        var (_, json) = HooksMerger.Add(original, Hook);
        var root = P(json!);
        Assert.Equal("x", (string?)root["model"]);
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("echo hi", (string?)stop[0]!["hooks"]![0]!["command"]);
    }

    [Fact]
    public void Remove_StripsOnlyOurCommands_AndDropsEmptiedStructure()
    {
        var original = """{"model":"x","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo hi"}]}]}}""";
        var (_, added) = HooksMerger.Add(original, Hook);
        var (outcome, removed) = HooksMerger.Remove(added, Hook);
        Assert.Equal(MergeOutcome.Changed, outcome);
        Assert.True(JsonNode.DeepEquals(P(original), P(removed!)));
    }

    [Fact]
    public void Remove_DropsHooksKeyWhenEmpty()
    {
        var (_, added) = HooksMerger.Add("""{"model":"x"}""", Hook);
        var (_, removed) = HooksMerger.Remove(added, Hook);
        Assert.True(JsonNode.DeepEquals(P("""{"model":"x"}"""), P(removed!)));
        Assert.Null(P(removed!)["hooks"]);
    }

    [Fact]
    public void Remove_PreservesKeyOrder()
    {
        var original = """{"z":1,"a":2,"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo hi"}]}],"Alpha":[]}}""";
        var (_, added) = HooksMerger.Add(original, Hook);
        var (_, removed) = HooksMerger.Remove(added, Hook);
        Assert.Equal(new[] { "z", "a", "hooks" }, P(removed!).AsObject().Select(k => k.Key));
        Assert.Equal(new[] { "Stop", "Alpha" }, P(removed!)["hooks"]!.AsObject().Select(k => k.Key));
    }

    [Fact]
    public void Remove_WhenNothingToRemove_IsUnchanged()
    {
        Assert.Equal(MergeOutcome.Unchanged, HooksMerger.Remove(null, Hook).Item1);
        Assert.Equal(MergeOutcome.Unchanged, HooksMerger.Remove("""{"model":"x"}""", Hook).Item1);
    }

    [Fact]
    public void InvalidJson_IsReported()
    {
        Assert.Equal(MergeOutcome.InvalidJson, HooksMerger.Add("{ not json", Hook).Item1);
        Assert.Equal(MergeOutcome.InvalidJson, HooksMerger.Remove("{ not json", Hook).Item1);
        Assert.Equal(MergeOutcome.InvalidJson, HooksMerger.Add("[1]", Hook).Item1);
    }

    [Fact]
    public void CommandFor_WindowsDrivePath_UsesForwardSlashes()
    {
        Assert.Equal("\"C:/Users/x/status-hook.exe\" working", HooksMerger.CommandFor(@"C:\Users\x\status-hook.exe", "working"));
    }

    [Fact]
    public void CommandFor_LinuxPath_PassesThrough()
    {
        Assert.Equal("\"/opt/a b/hook\" idle", HooksMerger.CommandFor("/opt/a b/hook", "idle"));
    }

    [Fact]
    public void Remove_FindsWhatAddWroteForWindowsPath()
    {
        const string win = @"C:\Users\x\status-hook.exe";
        var (_, added) = HooksMerger.Add(null, win);
        Assert.Contains("C:/Users/x/status-hook.exe", added);
        var (outcome, removed) = HooksMerger.Remove(added, win);
        Assert.Equal(MergeOutcome.Changed, outcome);
        Assert.Null(P(removed!)["hooks"]);
    }

    [Fact]
    public void Add_WritesQuotesUnescapedUnicodeAndTwoSpaceIndent()
    {
        var (_, json) = HooksMerger.Add(null, "/opt/é/hook");
        Assert.Contains("\\\"/opt/é/hook\\\" idle", json);
        Assert.StartsWith("{\n  \"hooks\": {\n    \"SessionStart\"", json);
        Assert.EndsWith("}\n", json);
    }

    [Fact]
    public void AddHooks_InvalidJson_Returns2_FileUntouched_NoBackup()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(2, SettingsFile.AddHooks(path, Hook, Now));
        Assert.Equal("{ not json", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void AddHooks_BomFile_AcceptedAndWrittenWithoutBom()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat("{\"model\":\"x\"}"u8.ToArray()).ToArray());
        Assert.Equal(0, SettingsFile.AddHooks(path, Hook, Now));
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("x", (string?)P(File.ReadAllText(path))["model"]);
    }

    [Fact]
    public void AddHooks_CreatesMissingDirectory_NoBackupForNewFile()
    {
        var path = Path.Combine(_dir, ".claude", "settings.json");
        Assert.Equal(0, SettingsFile.AddHooks(path, Hook, Now));
        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void ChangingCall_WritesExactlyOneBackup_UnchangedWritesNone()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"model":"x"}""");
        Assert.Equal(0, SettingsFile.AddHooks(path, Hook, Now));
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
        Assert.Equal("""{"model":"x"}""", File.ReadAllText($"{path}.bak.1700000000"));

        Assert.Equal(0, SettingsFile.AddHooks(path, Hook, Now));
        Assert.Equal(2, Directory.GetFiles(_dir).Length);

        Assert.Equal(0, SettingsFile.RemoveHooks(path, Hook, Now));
        Assert.Equal(3, Directory.GetFiles(_dir).Length);
        Assert.True(File.Exists($"{path}.bak.1700000000.1"));
    }
}
