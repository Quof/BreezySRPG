using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Erumani.Ui;

/// <summary>
/// A string rendered to a texture, <see cref="Scale"/> texture pixels to a pixel of the 640 × 480 screen: 1 for the
/// original's bitmap text (scaled with the rest of the picture), the render scale for the restyled skins' smooth text, so
/// that it lands on the window's own pixels and stays crisp however large the window is.
/// </summary>
public readonly record struct TextImage(Texture2D Texture, int Scale)
{
    /// <summary>Its size in 640 × 480 pixels.</summary>
    public int Width => (Texture.Width + Scale - 1) / Scale;
    public int Height => (Texture.Height + Scale - 1) / Scale;

    /// <summary>Draws it with its corner at <paramref name="pos"/> (640 × 480 pixels, under the frame's scale transform).</summary>
    public void Draw(SpriteBatch sb, Vector2 pos, Color c)
    {
        if (Scale == 1) { sb.Draw(Texture, pos, c); return; }
        // on whole render pixels, so each texel lands on exactly one
        var p = new Vector2(MathF.Round(pos.X * Scale) / Scale, MathF.Round(pos.Y * Scale) / Scale);
        sb.Draw(Texture, p, null, c, 0, Vector2.Zero, 1f / Scale, SpriteEffects.None, 0);
    }
}

/// <summary>
/// Text rendered exactly like the original runtime: Windows GDI, MS Gothic 18 px bold
/// (lfHeight -18, FW_BOLD), no anti-aliasing. Verified pixel-identical against a screenshot. Each distinct
/// string becomes a white-on-transparent texture, tinted at draw time. The restyled skins use a smooth face instead,
/// rendered at the frame's render scale (<see cref="Ui.RenderScale"/>); layout still measures at 640 × 480.
/// </summary>
public sealed class GdiText : IDisposable
{
    readonly GraphicsDevice _device;
    readonly IntPtr _dc, _font, _modernFont;
    readonly string? _modernFace;
    readonly int _modernWeight;
    /// <summary>The smooth face at <see cref="_scaledFontScale"/> times the height (created when the render scale changes).</summary>
    IntPtr _scaledFont;
    int _scaledFontScale;
    IntPtr _selected;
    /// <summary>Rendered strings, all in one mode: the bitmap font or the smooth one, at one scale.</summary>
    readonly Dictionary<string, Texture2D> _cache = new();
    (bool Smooth, int Scale) _cacheMode = (false, 1);
    public string FaceName { get; }
    public int Height { get; }

    public GdiText(GraphicsDevice device, int height = 18, int weight = 700, string envOverride = "ERUMANI_FONT")
    {
        _device = device;
        // calibration hook: e.g. ERUMANI_FONT="height,weight"
        if (Environment.GetEnvironmentVariable(envOverride) is { } env && env.Split(',') is [var hs, var ws])
            (height, weight) = (int.Parse(hs), int.Parse(ws));
        Height = height;
        _modernWeight = weight >= 700 ? 600 : weight;
        _dc = CreateCompatibleDC(IntPtr.Zero);
        // MS Gothic is what the original used; the others ship with every Windows 10/11.
        foreach (var face in new[] { "ＭＳ ゴシック", "MS Gothic", "Yu Gothic", "Meiryo" })
        {
            var f = CreateFontW(-height, 0, 0, 0, weight, 0, 0, 0, 128 /*SHIFTJIS_CHARSET*/, 0, 0,
                3 /*NONANTIALIASED_QUALITY*/, 1 /*FIXED_PITCH*/, face);
            if (f == IntPtr.Zero) continue;
            var before = SelectObject(_dc, f);
            if (FaceOf() == face || face == "Meiryo")
            {
                _font = _selected = f;
                FaceName = FaceOf();
                (_modernFont, _modernFace) = CreateModernFont(height);
                Select(_font);
                return;
            }
            SelectObject(_dc, before);
            DeleteObject(f);
        }
        throw new InvalidOperationException("No Japanese GDI font available.");
    }

    string FaceOf()
    {
        var sb = new System.Text.StringBuilder(64);
        GetTextFaceW(_dc, sb.Capacity, sb);
        return sb.ToString();
    }

    void Select(IntPtr font)
    {
        if (font == _selected) return;
        SelectObject(_dc, font);
        _selected = font;
    }

    IntPtr Smooth(string face, int height) =>
        CreateFontW(-height, 0, 0, 0, _modernWeight, 0, 0, 0, 128, 0, 0, 4 /*ANTIALIASED_QUALITY*/, 0, face);

    /// <summary>The modern interface's font: a smooth (anti-aliased) UI face at the same pixel height.</summary>
    (IntPtr, string?) CreateModernFont(int height)
    {
        foreach (var face in new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo" })
        {
            var f = Smooth(face, height);
            if (f == IntPtr.Zero) continue;
            Select(f);
            if (FaceOf() == face) return (f, face);
            Select(_font);
            DeleteObject(f);
        }
        return (IntPtr.Zero, null);
    }

    bool UseSmooth => Ui.Restyled && _modernFont != IntPtr.Zero;

    /// <summary>The font strings are rendered in now, and at what scale.</summary>
    (IntPtr Font, bool Smooth, int Scale) Current()
    {
        if (!UseSmooth) return (_font, false, 1);
        int k = Math.Max(1, Ui.RenderScale);
        if (k == 1) return (_modernFont, true, 1);
        if (_scaledFontScale != k)
        {
            if (_scaledFont != IntPtr.Zero)
            {
                if (_selected == _scaledFont) Select(_font);
                DeleteObject(_scaledFont);
            }
            _scaledFont = Smooth(_modernFace!, Height * k);
            _scaledFontScale = k;
        }
        return (_scaledFont, true, k);
    }

    /// <summary>A string's width in 640 × 480 pixels.</summary>
    public int Measure(string s)
    {
        if (s.Length == 0) return 0;
        Select(UseSmooth ? _modernFont : _font);
        GetTextExtentPoint32W(_dc, s, s.Length, out var size);
        return size.cx;
    }

    /// <summary>White text on transparent background (null for an empty string).</summary>
    public TextImage? Get(string s)
    {
        if (s.Length == 0) return null;
        var (font, smooth, k) = Current();
        if ((smooth, k) != _cacheMode)
        {
            ClearCache();
            _cacheMode = (smooth, k);
        }
        if (_cache.TryGetValue(s, out var t)) return new TextImage(t, k);
        Select(font);
        GetTextExtentPoint32W(_dc, s, s.Length, out var size);
        int w = Math.Max(1, size.cx), h = Math.Max(1, size.cy);
        var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(_dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(_dc, bmp);
        SetBkColor(_dc, 0);
        SetTextColor(_dc, 0xFFFFFF);
        SetBkMode(_dc, 2 /*OPAQUE*/);
        TextOutW(_dc, 0, 0, s, s.Length);
        GdiFlush();
        var px = new byte[w * h * 4];
        Marshal.Copy(bits, px, 0, px.Length);
        SelectObject(_dc, old);
        DeleteObject(bmp);
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte a = smooth ? Math.Max(px[i * 4], px[i * 4 + 1]) : px[i * 4] > 127 ? (byte)255 : (byte)0;   // smooth, or 1-bit glyphs
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = a;
            rgba[i * 4 + 3] = a;
        }
        t = new Texture2D(_device, w, h);
        t.SetData(rgba);
        if (_cache.Count > 2000) ClearCache();
        _cache[s] = t;
        return new TextImage(t, k);
    }

    void ClearCache()
    {
        foreach (var x in _cache.Values) x.Dispose();
        _cache.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        DeleteDC(_dc);   // first, so none of the fonts is still selected into it
        DeleteObject(_font);
        if (_modernFont != IntPtr.Zero) DeleteObject(_modernFont);
        if (_scaledFont != IntPtr.Zero) DeleteObject(_scaledFont);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int bmiColors;
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline,
        uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int GetTextFaceW(IntPtr hdc, int n, System.Text.StringBuilder face);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr hdc, string s, int n, out SIZE size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr hdc, int x, int y, string s, int n);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr hdc, uint c);
    [DllImport("gdi32.dll")] static extern uint SetBkColor(IntPtr hdc, uint c);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
