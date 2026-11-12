using System.Text;
using System.Text.Json;

namespace StatusLight.Core;

/// <summary>
/// Task title from a session transcript. Title lines
/// ({"type":"agent-name","agentName":...} / {"type":"ai-title","aiTitle":...})
/// are written near the top, so reading the head is enough.
/// </summary>
public static class TranscriptTitles
{
    /// <summary>
    /// Latest agent-name in the transcript head, falling back to the latest
    /// ai-title. A line cut off by the read window fails to parse and is skipped.
    /// </summary>
    public static string? ExtractTitle(string head)
    {
        string? agentName = null;
        string? aiTitle = null;
        foreach (var rawLine in head.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.Contains("\"agent-name\"", StringComparison.Ordinal)
                && !line.Contains("\"ai-title\"", StringComparison.Ordinal))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;
                switch (type.GetString())
                {
                    case "agent-name":
                        agentName = StringProp(root, "agentName") ?? agentName;
                        break;
                    case "ai-title":
                        aiTitle = StringProp(root, "aiTitle") ?? aiTitle;
                        break;
                }
            }
            catch (JsonException)
            {
                // Truncated or malformed line: skip.
            }
        }
        var title = agentName ?? aiTitle;
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static string? StringProp(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Reads the first 64 KiB of a transcript and caches the title by (path, size). Never throws.</summary>
public sealed class TranscriptTitleCache
{
    private const int HeadBytes = 64 * 1024;
    private readonly Dictionary<string, (long Size, string? Title)> _cache = new();

    public string? TitleFor(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var size = info.Length;
            if (_cache.TryGetValue(path, out var cached) && cached.Size == size)
                return cached.Title;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[HeadBytes];
            var total = 0;
            int n;
            while (total < buf.Length && (n = fs.Read(buf, total, buf.Length - total)) > 0)
                total += n;

            var title = TranscriptTitles.ExtractTitle(Encoding.UTF8.GetString(buf, 0, total));
            _cache[path] = (size, title);
            return title;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
