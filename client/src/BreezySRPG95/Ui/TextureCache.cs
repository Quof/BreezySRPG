using Microsoft.Xna.Framework.Graphics;

namespace BreezySRPG95.Ui;

/// <summary>The frame being drawn, for <see cref="TextureCache{TKey}"/>: textures used in it are never let go.</summary>
public static class TextureCaches
{
    public static int Frame;
}

/// <summary>
/// Textures made on demand (rendered text, the skins' boxes and gold pieces), kept until they pass a memory budget; past
/// it, the ones used longest ago go one at a time, so nothing is ever made again in bulk. A texture used in the frame being
/// drawn is never let go: a sprite batch draws it only at its End.
/// </summary>
public sealed class TextureCache<TKey>(long budgetBytes) : IDisposable where TKey : notnull
{
    readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    readonly LinkedList<Entry> _order = new();   // most recently used first
    long _bytes;

    sealed class Entry(TKey key, Texture2D texture)
    {
        public TKey Key { get; } = key;
        public Texture2D Texture { get; } = texture;
        public int Used = TextureCaches.Frame;
    }

    public int Count => _map.Count;

    public bool TryGet(TKey key, out Texture2D texture)
    {
        if (_map.TryGetValue(key, out var node))
        {
            node.Value.Used = TextureCaches.Frame;
            if (node != _order.First) { _order.Remove(node); _order.AddFirst(node); }
            texture = node.Value.Texture;
            return true;
        }
        texture = null!;
        return false;
    }

    public Texture2D Add(TKey key, Texture2D texture)
    {
        if (_map.Remove(key, out var old)) Drop(old);
        _map[key] = _order.AddFirst(new Entry(key, texture));
        _bytes += Size(texture);
        while (_bytes > budgetBytes && _order.Last is { } last && last.Value.Used != TextureCaches.Frame)
        {
            _map.Remove(last.Value.Key);
            Drop(last);
        }
        return texture;
    }

    void Drop(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _bytes -= Size(node.Value.Texture);
        node.Value.Texture.Dispose();
    }

    static long Size(Texture2D t) => (long)t.Width * t.Height * 4;

    public void Clear()
    {
        foreach (var e in _order) e.Texture.Dispose();
        _order.Clear();
        _map.Clear();
        _bytes = 0;
    }

    public void Dispose() => Clear();
}
