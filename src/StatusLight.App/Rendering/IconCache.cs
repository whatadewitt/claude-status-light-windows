using System.Drawing;
using System.IO;
using StatusLight.App.Platform;
using StatusLight.Core;

namespace StatusLight.App.Rendering;

/// <summary>
/// LRU cache of rendered icons. Owns every HICON: <see cref="Win32.DestroyIcon"/> runs on
/// eviction and on Dispose, so callers must not hold an <see cref="Icon"/> past the next few
/// <see cref="Get"/> calls (the tray and floating window re-fetch on every update).
/// Keyed by the arguments plus the custom icon's mtime, so dropping in a new icon.png
/// takes effect on the next update. UI-thread only; not thread-safe.
/// </summary>
public sealed class IconCache : IDisposable
{
    public const int DefaultCapacity = 64;

    public static string DefaultCustomIconPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "status-light", "icon.png");

    private readonly record struct Key(LightState State, int Side, int Frame, int Agents, long CustomStamp);

    private sealed record Entry(Key Key, Icon Icon, nint Handle);

    private readonly int _capacity;
    private readonly string _customIconPath;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new(); // most recently used first
    private long _customStamp = -1;
    private Image? _custom;
    private bool _disposed;

    public IconCache(int capacity = DefaultCapacity, string? customIconPath = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _customIconPath = customIconPath ?? DefaultCustomIconPath;
    }

    public Icon Get(LightState s, int side, int frame, int agents)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshCustom();
        var frames = Mascot.Frames.Count;
        // A custom icon doesn't dance, so every frame shares one entry.
        var frameKey = _custom is null ? ((frame % frames) + frames) % frames : 0;
        var key = new Key(s, side, frameKey, Math.Clamp(agents, 0, 9), _customStamp);

        if (_map.TryGetValue(key, out var hit))
        {
            _lru.Remove(hit);
            _lru.AddFirst(hit);
            return hit.Value.Icon;
        }

        nint handle;
        using (var bmp = IconRenderer.Render(s, side, key.Frame, key.Agents, _custom))
            handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle); // does not own the handle; we destroy it
        _map[key] = _lru.AddFirst(new Entry(key, icon, handle));

        // Never evict the entry just returned (the head), even if capacity were 1.
        while (_map.Count > _capacity && _lru.Last != _lru.First) Evict(_lru.Last!);
        return icon;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        while (_lru.Last is { } last) Evict(last);
        _custom?.Dispose();
        _custom = null;
    }

    private void Evict(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
        node.Value.Icon.Dispose();
        Win32.DestroyIcon(node.Value.Handle);
    }

    private void RefreshCustom()
    {
        long stamp;
        try
        {
            stamp = File.Exists(_customIconPath) ? File.GetLastWriteTimeUtc(_customIconPath).Ticks : 0;
        }
        catch (Exception)
        {
            stamp = 0;
        }
        if (stamp == _customStamp) return;

        _custom?.Dispose();
        _custom = stamp == 0 ? null : LoadCustom(_customIconPath);
        _customStamp = stamp;
    }

    /// <summary>Loads into memory so the file is not locked; an unreadable image falls back to the mascot.</summary>
    private static Image? LoadCustom(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
