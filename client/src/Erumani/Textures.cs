using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani;

/// <summary>Loads original BMPs (game folder, then RTP) into textures, cached by file name.</summary>
public sealed class Textures(GraphicsDevice device, GameFolder game) : IDisposable
{
    readonly Dictionary<(string, bool), Texture2D?> _cache = new();

    /// <param name="transparent">Treat palette index 0 as transparent (sprite sheets).</param>
    public Texture2D? Get(string fileName, bool transparent = true)
    {
        if (_cache.TryGetValue((fileName, transparent), out var t)) return t;
        var bmp = game.Bitmap(fileName);
        if (bmp is not null)
        {
            t = new Texture2D(device, bmp.Width, bmp.Height, false, SurfaceFormat.Color);
            t.SetData(bmp.ToRgba(transparent));
        }
        _cache[(fileName, transparent)] = t;
        return t;
    }

    readonly Dictionary<string, Texture2D?> _silhouettes = new();

    /// <summary>The sprite sheet with every opaque pixel white (the battle scene's hit flash, FUN_00419574).</summary>
    public Texture2D? Silhouette(string fileName)
    {
        if (_silhouettes.TryGetValue(fileName, out var t)) return t;
        var bmp = game.Bitmap(fileName);
        if (bmp is not null)
        {
            var px = bmp.ToRgba(true);
            for (int i = 0; i + 3 < px.Length; i += 4)   // RGBA bytes, premultiplied
                if (px[i + 3] != 0) px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 255;
            t = new Texture2D(device, bmp.Width, bmp.Height, false, SurfaceFormat.Color);
            t.SetData(px);
        }
        _silhouettes[fileName] = t;
        return t;
    }

    public void Dispose()
    {
        foreach (var t in _cache.Values) t?.Dispose();
        foreach (var t in _silhouettes.Values) t?.Dispose();
        _cache.Clear();
        _silhouettes.Clear();
    }
}
