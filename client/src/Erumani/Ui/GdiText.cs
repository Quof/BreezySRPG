using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace Erumani.Ui;

/// <summary>
/// Text rendered exactly like the original runtime: Windows GDI, MS Gothic 18 px bold
/// (lfHeight -18, FW_BOLD), no anti-aliasing. Verified pixel-identical against a screenshot. Each distinct
/// string becomes a white-on-transparent texture, tinted at draw time.
/// </summary>
public sealed class GdiText : IDisposable
{
    readonly GraphicsDevice _device;
    readonly IntPtr _dc, _font, _modernFont;
    bool _modernSelected;
    readonly Dictionary<string, Texture2D> _cache = new();
    public string FaceName { get; }
    public int Height { get; }

    public GdiText(GraphicsDevice device, int height = 18, int weight = 700, string envOverride = "ERUMANI_FONT")
    {
        _device = device;
        // calibration hook: e.g. ERUMANI_FONT="height,weight"
        if (Environment.GetEnvironmentVariable(envOverride) is { } env && env.Split(',') is [var hs, var ws])
            (height, weight) = (int.Parse(hs), int.Parse(ws));
        Height = height;
        _dc = CreateCompatibleDC(IntPtr.Zero);
        // MS Gothic is what the original used; the others ship with every Windows 10/11.
        foreach (var face in new[] { "ＭＳ ゴシック", "MS Gothic", "Yu Gothic", "Meiryo" })
        {
            var f = CreateFontW(-height, 0, 0, 0, weight, 0, 0, 0, 128 /*SHIFTJIS_CHARSET*/, 0, 0,
                3 /*NONANTIALIASED_QUALITY*/, 1 /*FIXED_PITCH*/, face);
            if (f == IntPtr.Zero) continue;
            SelectObject(_dc, f);
            var sb = new System.Text.StringBuilder(64);
            GetTextFaceW(_dc, sb.Capacity, sb);
            if (sb.ToString() == face || face == "Meiryo")
            {
                _font = f;
                FaceName = sb.ToString();
                _modernFont = CreateModernFont(height, weight);
                SelectObject(_dc, _font);
                return;
            }
            DeleteObject(f);
        }
        throw new InvalidOperationException("No Japanese GDI font available.");
    }

    /// <summary>The modern interface's font: a smooth (anti-aliased) UI face at the same pixel height.</summary>
    IntPtr CreateModernFont(int height, int weight)
    {
        foreach (var face in new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo" })
        {
            var f = CreateFontW(-height, 0, 0, 0, weight >= 700 ? 600 : weight, 0, 0, 0, 128, 0, 0, 4 /*ANTIALIASED_QUALITY*/, 0, face);
            if (f == IntPtr.Zero) continue;
            SelectObject(_dc, f);
            var sb = new System.Text.StringBuilder(64);
            GetTextFaceW(_dc, sb.Capacity, sb);
            if (sb.ToString() == face) return f;
            DeleteObject(f);
        }
        return IntPtr.Zero;
    }

    bool UseModern()
    {
        bool modern = Ui.Modern && _modernFont != IntPtr.Zero;
        if (modern != _modernSelected)
        {
            SelectObject(_dc, modern ? _modernFont : _font);
            _modernSelected = modern;
        }
        return modern;
    }

    public int Measure(string s)
    {
        if (s.Length == 0) return 0;
        UseModern();
        GetTextExtentPoint32W(_dc, s, s.Length, out var size);
        return size.cx;
    }

    /// <summary>White text on transparent background (null for an empty string).</summary>
    public Texture2D? Get(string s)
    {
        if (s.Length == 0) return null;
        bool modern = UseModern();
        string key = modern ? "\u0001" + s : s;
        if (_cache.TryGetValue(key, out var t)) return t;
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
            byte a = modern ? Math.Max(px[i * 4], px[i * 4 + 1]) : px[i * 4] > 127 ? (byte)255 : (byte)0;   // smooth, or 1-bit glyphs
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = a;
            rgba[i * 4 + 3] = a;
        }
        t = new Texture2D(_device, w, h);
        t.SetData(rgba);
        if (_cache.Count > 2000) ClearCache();
        _cache[key] = t;
        return t;
    }

    void ClearCache()
    {
        foreach (var x in _cache.Values) x.Dispose();
        _cache.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        DeleteObject(_font);
        if (_modernFont != IntPtr.Zero) DeleteObject(_modernFont);
        DeleteDC(_dc);
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
