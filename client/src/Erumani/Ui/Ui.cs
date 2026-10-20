using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Erumani.Ui;

/// <summary>
/// Drawing primitives for the 640x480 screen: Japanese text and windows.
/// The look (colours, sizes) is provisional until compared with screenshots
/// of the original runtime (FORMAT_NOTES test T17).
/// </summary>
public sealed class Ui : IDisposable
{
    public const int FontSize = 16, LineHeight = 20;

    readonly FontSystem _fonts = new(new FontSystemSettings { FontResolutionFactor = 1, KernelWidth = 0, KernelHeight = 0 });
    readonly Texture2D _pixel;
    public SpriteFontBase Font { get; }
    public string FontName { get; }

    public Ui(GraphicsDevice device)
    {
        // MS Gothic is what the original runtime used; Yu Gothic / Meiryo ship with every Windows 10/11.
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string? path = new[] { "msgothic.ttc", "YuGothR.ttc", "meiryo.ttc", "msgothic.ttf" }
            .Select(f => Path.Combine(fonts, f)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("No Japanese font found (MS Gothic, Yu Gothic or Meiryo).");
        FontName = Path.GetFileName(path);
        _fonts.AddFont(FirstFontOf(File.ReadAllBytes(path)));
        Font = _fonts.GetFont(FontSize);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// StbTrueType can't open .ttc collections. Inside a collection every font's table
    /// directory uses absolute file offsets, so copying font 0's directory to the start
    /// of the file yields a valid single font (as long as no table data is overwritten).
    /// </summary>
    static byte[] FirstFontOf(byte[] data)
    {
        static int U32(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];
        static int U16(byte[] b, int o) => b[o] << 8 | b[o + 1];
        if (data.Length < 16 || data[0] != 't' || data[1] != 't' || data[2] != 'c' || data[3] != 'f') return data;
        int dir = U32(data, 12), tables = U16(data, dir + 4), len = 12 + 16 * tables;
        for (int i = 0; i < tables; i++)
            if (U32(data, dir + 12 + i * 16 + 8) < len)
                throw new InvalidDataException("font collection layout not supported");
        var copy = (byte[])data.Clone();
        Buffer.BlockCopy(data, dir, copy, 0, len);
        return copy;
    }

    public void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_pixel, r, c);

    public void Frame(SpriteBatch sb, Rectangle r, Color c, int t = 1)
    {
        Fill(sb, new Rectangle(r.X, r.Y, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Y, t, r.Height), c);
        Fill(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    /// <summary>Message/menu window (provisional style).</summary>
    public void Window(SpriteBatch sb, Rectangle r, float alpha = 1f)
    {
        Fill(sb, r, new Color(16, 24, 72) * (0.85f * alpha));
        Frame(sb, r, new Color(200, 210, 255) * alpha, 2);
        Frame(sb, new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), new Color(90, 100, 160) * alpha);
    }

    public void Text(SpriteBatch sb, string s, Vector2 pos, Color c, bool shadow = true)
    {
        if (shadow) Font.DrawText(sb, s, pos + Vector2.One, Color.Black * 0.8f);
        Font.DrawText(sb, s, pos, c);
    }

    public Vector2 Measure(string s) => Font.MeasureString(s);

    public void Dispose()
    {
        _fonts.Dispose();
        _pixel.Dispose();
    }
}
