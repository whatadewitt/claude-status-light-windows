using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatusLight.Core.Install;

public enum MergeOutcome { Changed, Unchanged, InvalidJson }

/// <summary>Idempotently adds/removes the status-light hooks in a Claude Code settings.json document.</summary>
public static class HooksMerger
{
    /// <summary>`"{hookPath}" {state}`. A path with a drive letter has its backslashes turned into forward slashes.</summary>
    public static string CommandFor(string hookPath, string state) => $"\"{Normalize(hookPath)}\" {state}";

    private static string Normalize(string hookPath) =>
        hookPath.Length >= 2 && char.IsAsciiLetter(hookPath[0]) && hookPath[1] == ':'
            ? hookPath.Replace('\\', '/')
            : hookPath;

    public static (MergeOutcome, string? Json) Add(string? existingJson, string hookPath)
    {
        RequirePath(hookPath);
        if (!TryParseRoot(existingJson, out var root)) return (MergeOutcome.InvalidJson, null);

        JsonObject hooks;
        var changed = false;
        switch (root["hooks"])
        {
            case null: hooks = new JsonObject(); root["hooks"] = hooks; break;
            case JsonObject o: hooks = o; break;
            default: return (MergeOutcome.InvalidJson, null);
        }

        foreach (var (evt, state) in HookEvents.All)
        {
            var cmd = CommandFor(hookPath, state);
            JsonArray groups;
            switch (hooks[evt])
            {
                case null: groups = new JsonArray(); hooks[evt] = groups; break;
                case JsonArray a: groups = a; break;
                default: return (MergeOutcome.InvalidJson, null);
            }

            var already = groups.OfType<JsonObject>()
                .Select(g => g["hooks"] as JsonArray)
                .Where(h => h is not null)
                .SelectMany(h => h!.OfType<JsonObject>())
                .Any(h => CommandOf(h).Trim() == cmd);
            if (already) continue;

            var group = new JsonObject();
            if (HookEvents.MatcherEvents.Contains(evt)) group["matcher"] = "*";
            group["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = cmd });
            groups.Add((JsonNode)group);
            changed = true;
        }

        return changed ? (MergeOutcome.Changed, Serialize(root)) : (MergeOutcome.Unchanged, null);
    }

    public static (MergeOutcome, string? Json) Remove(string? existingJson, string hookPath)
    {
        RequirePath(hookPath);
        if (!TryParseRoot(existingJson, out var root)) return (MergeOutcome.InvalidJson, null);
        if (root["hooks"] is not JsonObject hooks) return (MergeOutcome.Unchanged, null);

        var normalized = Normalize(hookPath);
        var changed = false;

        foreach (var evt in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[evt] is not JsonArray groups || groups.Count == 0) continue; // empty arrays aren't ours

            var kept = new List<JsonNode?>();
            foreach (var group in groups.ToList())
            {
                if (group is not JsonObject g || g["hooks"] is not JsonArray inner)
                {
                    kept.Add(group);
                    continue;
                }

                var survivors = inner.Where(h => !(h is JsonObject ho && IsOurs(CommandOf(ho), hookPath, normalized))).ToList();
                if (survivors.Count != inner.Count) changed = true;
                if (survivors.Count > 0)
                {
                    if (survivors.Count != inner.Count)
                    {
                        inner.Clear();
                        foreach (var s in survivors) inner.Add(s);
                    }
                    kept.Add(group);
                }
                else if (inner.Count == 0)
                {
                    kept.Add(group); // an already-empty group isn't ours; leave it
                }
            }

            if (kept.Count > 0)
            {
                if (kept.Count != groups.Count)
                {
                    groups.Clear();
                    foreach (var k in kept) groups.Add(k);
                }
            }
            else
            {
                hooks.Remove(evt);
                changed = true;
            }
        }

        if (!changed) return (MergeOutcome.Unchanged, null);
        if (hooks.Count == 0) root.Remove("hooks");
        return (MergeOutcome.Changed, Serialize(root));
    }

    private static void RequirePath(string? hookPath)
    {
        if (string.IsNullOrWhiteSpace(hookPath)) throw new ArgumentException("hookPath must not be empty.", nameof(hookPath));
    }

    private static bool IsOurs(string command, string raw, string normalized) =>
        command.Contains(raw, StringComparison.Ordinal) || command.Contains(normalized, StringComparison.Ordinal);

    private static string CommandOf(JsonObject hook) =>
        hook["command"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static bool TryParseRoot(string? json, out JsonObject root)
    {
        root = new JsonObject();
        if (json is null) return true;
        try
        {
            if (JsonNode.Parse(json) is JsonObject o)
            {
                root = o;
                return true;
            }
        }
        catch (JsonException) { }
        return false;
    }

    private static string Serialize(JsonNode node)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            node.WriteTo(w);
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }
}
