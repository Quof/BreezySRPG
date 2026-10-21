using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani.Ui;

/// <summary>
/// The original's 12-px text (FUN_00407744 / FUN_00407698): SRPGEXEC.EXE's own font KANJIFONT (resource type "KANJI",
/// FUN_0040a284), 12 × 12 one-bit glyphs of 24 bytes — per row, the first byte holds pixels 0–7 and the second's high
/// nibble pixels 8–11. A half-width character (0x20–0xDF) is glyph c − 0x20, drawn 3 px below the text's y, 6 px on; a
/// full-width one (lead 0x81–0x9F or 0xE0–0xEF) is glyph (lead − 0x80, or − 0xC0) × 192 + trail − 0x40, 2 px below,
/// 12 px on; a lead 0xF0–0xFC character is blank; byte 0x01 puts the next byte's glyph (+ 0x20) in a 12-px cell. Each
/// string becomes a white-on-transparent texture whose corner is the text's (x, y), tinted when drawn.
/// </summary>
public sealed class ExeFont : IDisposable
{
    readonly GraphicsDevice _device;
    readonly byte[] _glyphs;
    readonly Dictionary<string, Texture2D> _cache = new();

    public ExeFont(GraphicsDevice device, byte[] glyphs) => (_device, _glyphs) = (device, glyphs);

    /// <summary>The glyphs of a string and where each goes; the advance in px.</summary>
    static List<(int Glyph, int X, int Y)> Layout(string s, out int advance)
    {
        var b = Bin.Sjis.GetBytes(s);
        var glyphs = new List<(int, int, int)>();
        int x = 0;
        for (int i = 0; i < b.Length;)
        {
            int c = b[i];
            if (c is 1 or >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC)
            {
                if (i + 1 >= b.Length) break;
                if (c == 1) glyphs.Add((b[i + 1] + 0x20, x, 3));
                else if (c <= 0xEF) glyphs.Add((c * 0xC0 + b[i + 1] - (c <= 0x9F ? 0x6040 : 0x9040), x, 2));
                x += 12;
                i += 2;
            }
            else
            {
                if (c is >= 0x20 and < 0xE0) { glyphs.Add((c - 0x20, x, 3)); x += 6; }
                i++;
            }
        }
        advance = x;
        return glyphs;
    }

    public static int Measure(string s)
    {
        Layout(s, out int advance);
        return advance;
    }

    /// <summary>White text on transparent background, its corner at the text's (x, y) (null for nothing to draw).</summary>
    public Texture2D? Get(string s)
    {
        if (s.Length == 0) return null;
        if (_cache.TryGetValue(s, out var t)) return t;
        var layout = Layout(s, out int advance);
        int w = Math.Max(advance, layout.Count > 0 ? layout.Max(g => g.X) + 12 : 0), h = 15;
        if (w == 0) return null;
        var px = new Color[w * h];
        foreach (var (g, gx, gy) in layout)
        {
            if (g < 0 || (g + 1) * 24 > _glyphs.Length) continue;
            for (int r = 0; r < 12; r++)
            {
                int row = _glyphs[g * 24 + r * 2] | _glyphs[g * 24 + r * 2 + 1] << 8;
                for (int c = 0; c < 12; c++)
                    if ((row & (c < 8 ? 0x80 >> c : 0x8000 >> (c - 8))) != 0) px[(gy + r) * w + gx + c] = Color.White;
            }
        }
        t = new Texture2D(_device, w, h);
        t.SetData(px);
        if (_cache.Count > 2000) ClearCache();
        _cache[s] = t;
        return t;
    }

    void ClearCache()
    {
        foreach (var x in _cache.Values) x.Dispose();
        _cache.Clear();
    }

    public void Dispose() => ClearCache();
}
