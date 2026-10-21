using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani.Ui;

/// <summary>
/// Drawing primitives for the 640x480 screen as the original runtime draws them: panels in SYSTEM.BMP's swatches
/// (FUN_0041203c), message balloons (FUN_004121ac), menu bars, the screen frame, the mouse cursor, the exe's own
/// 12-px font (KANJIFONT) and GDI text.
/// </summary>
public sealed class Ui : IDisposable
{
    public const int LineHeight = 22;
    /// <summary>Message / window text: dark on the original's light green windows, light on the modern dark ones.</summary>
    public static Color TextColor => Modern ? new Color(236, 240, 248) : new Color(16, 16, 0);

    /// <summary>The modern interface (options): dark translucent rounded windows, smooth fonts, accent highlights.</summary>
    public static bool Modern { get; set; }
    public static readonly Color ModernBase = new(22, 26, 38), Accent = new(104, 168, 255);
    public static Color ModernBorder => new Color(255, 255, 255) * 0.16f;
    public static readonly Color WindowFill = new(184, 200, 160);
    static readonly Color EdgeLight = new(241, 241, 241), EdgeDark = new(96, 96, 96);

    readonly GraphicsDevice _device;
    readonly Texture2D _pixel;
    readonly Textures _tex;
    public GdiText Text { get; }
    /// <summary>Menu/panel text (choices, camp menu): smaller than message text.</summary>
    public GdiText SmallText { get; }
    /// <summary>16 px bold (FUN_00407888): unit names on the battle scene's info panels and the status window.</summary>
    public GdiText NameText { get; }

    public Ui(GraphicsDevice device, Textures tex)
    {
        _device = device;
        _tex = tex;
        Text = new GdiText(device);
        SmallText = new GdiText(device, 12, 400, "ERUMANI_SMALLFONT");
        NameText = new GdiText(device, 16, 700, "ERUMANI_FONT");
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Rounded rectangle (radius 8) filled with the window colour; 1-px outline that is light
    /// on the top/left and dark on the bottom/right, blending around the corners.
    /// </summary>
    Texture2D BuildWindow(int w, int h)
    {
        const float r = 8f;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Math.Clamp(x + 0.5f, r, w - r), cy = Math.Clamp(y + 0.5f, r, h - r);
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                bool inCorner = dx != 0 && dy != 0;
                float dist = inCorner ? r - d : Math.Min(Math.Min(x + 0.5f, w - x - 0.5f), Math.Min(y + 0.5f, h - y - 0.5f));
                if (dist < 0) continue;
                if (dist < 1)
                {
                    // normal direction decides light (up/left) vs dark (down/right)
                    float nx = inCorner ? dx / d : (x < w / 2 ? -1 : 1) * (Math.Abs(x + 0.5f - w / 2f) > w / 2f - 1 ? 1 : 0);
                    float ny = inCorner ? dy / d : (y < h / 2 ? -1 : 1) * (Math.Abs(y + 0.5f - h / 2f) > h / 2f - 1 ? 1 : 0);
                    float t = Math.Clamp((nx + ny) * 0.5f + 0.5f, 0, 1);
                    px[y * w + x] = Color.Lerp(EdgeLight, EdgeDark, t);
                }
                else px[y * w + x] = WindowFill;
            }
        var tex = new Texture2D(_device, w, h);
        tex.SetData(px);
        return tex;
    }

    public void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_pixel, r, c);

    /// <summary>The original's 12-px font, read from SRPGEXEC.EXE (null when it can't be).</summary>
    public ExeFont? MenuFont { get; set; }

    /// <summary>
    /// The original's 12-px text (FUN_00407744) at GDI position (x, y): the exe's own font; with the modern skin or no exe,
    /// MS Gothic 12 px a pixel right and two down (where its glyphs then fall closest to the exe's).
    /// </summary>
    public void Text12(SpriteBatch sb, string s, int x, int y, Color c)
    {
        if (!Modern && MenuFont?.Get(s) is { } t) sb.Draw(t, new Vector2(x, y), c);
        else if (SmallText.Get(s) is { } g) sb.Draw(g, new Vector2(x + 1, y + 2), c);
    }

    /// <summary>The width <see cref="Text12"/> gives a string.</summary>
    public int Measure12(string s) => !Modern && MenuFont is not null ? ExeFont.Measure(s) : SmallText.Measure(s);

    /// <summary>SYSTEM.BMP with palette index 0 transparent (arrows, cursors, balloon pieces).</summary>
    public Texture2D? SystemBmp => _tex.Get("SYSTEM.BMP");

    public void Frame(SpriteBatch sb, Rectangle r, Color c, int t = 1)
    {
        Fill(sb, new Rectangle(r.X, r.Y, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Y, t, r.Height), c);
        Fill(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    /// <summary>
    /// The message balloon (FUN_004121ac, its skin from FUN_00411d74): SYSTEM.BMP's 48 × 48 corner as a nine-slice — 16-px
    /// corners and edges, the fill between — 416 × 128 px from <paramref name="topLeft"/>; then the speech tail
    /// (FUN_00406f98), 32 × 16 from SYSTEM.BMP (0,48) 16 px left of the balloon for a face on the left (<paramref name="tail"/>
    /// −1) or (32,48) 16 px before its right edge for a face on the right (+1), 16 px down. Palette index 0 is transparent.
    /// </summary>
    public void MessageBalloon(SpriteBatch sb, Point topLeft, int tail)
    {
        if (Modern)
        {
            ModernBox(sb, new Rectangle(topLeft.X + 1, topLeft.Y + 1, BalloonWidth - 2, BalloonHeight - 2), ModernBase, 12, 0.92f);
            return;
        }
        var sys = _tex.Get("SYSTEM.BMP");
        if (sys is null) return;
        for (int y = 0; y < BalloonHeight; y += 16)
            for (int x = 0; x < BalloonWidth; x += 16)
            {
                int sx = x == 0 ? 0 : x == BalloonWidth - 16 ? 32 : 16, sy = y == 0 ? 0 : y == BalloonHeight - 16 ? 32 : 16;
                sb.Draw(sys, new Vector2(topLeft.X + x, topLeft.Y + y), new Rectangle(sx, sy, 16, 16), Color.White);
            }
        if (tail < 0) sb.Draw(sys, new Vector2(topLeft.X - 16, topLeft.Y + 16), new Rectangle(0, 48, 32, 16), Color.White);
        if (tail > 0) sb.Draw(sys, new Vector2(topLeft.X + BalloonWidth - 16, topLeft.Y + 16), new Rectangle(32, 48, 32, 16), Color.White);
    }

    /// <summary>The message balloon's size (FUN_004148a4: 26 × 8 cells of 16 px).</summary>
    public const int BalloonWidth = 26 * 16, BalloonHeight = 8 * 16;

    /// <summary>Generic window for menus (same skin, any size).</summary>
    public void Window(SpriteBatch sb, Rectangle r)
    {
        if (Modern) { ModernBox(sb, r, ModernBase, 10, 0.92f); return; }
        // Built on demand per size; menus are few.
        if (!_windows.TryGetValue((r.Width, r.Height), out var t))
            _windows[(r.Width, r.Height)] = t = BuildWindow(r.Width, r.Height);
        sb.Draw(t, r.Location.ToVector2(), Color.White);
    }
    readonly Dictionary<(int, int), Texture2D> _windows = new();

    // ---------------- modern interface ----------------

    readonly Dictionary<(int W, int H, int R, Color Fill, float A), Texture2D> _modern = new();

    /// <summary>A soft-shadowed, rounded, translucent box with a faint light edge and a slight top-down gradient.</summary>
    public void ModernBox(SpriteBatch sb, Rectangle r, Color fill, int radius, float alpha)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var key = (r.Width, r.Height, radius, fill, alpha);
        if (!_modern.TryGetValue(key, out var tex)) _modern[key] = tex = BuildRounded(r.Width, r.Height, radius, fill, alpha);
        sb.Draw(tex, new Vector2(r.X + 2, r.Y + 3), Color.Black * 0.45f);    // shadow
        sb.Draw(tex, r.Location.ToVector2(), Color.White);
    }

    Texture2D BuildRounded(int w, int h, int radius, Color fill, float alpha)
    {
        float rad = Math.Min(radius, Math.Min(w, h) / 2f);
        var border = new Color(255, 255, 255);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            // gradient: a little lighter at the top
            var row = Color.Lerp(Color.Lerp(fill, Color.White, 0.07f), fill, h <= 1 ? 1 : y / (h - 1f));
            for (int x = 0; x < w; x++)
            {
                float cx = Math.Clamp(x + 0.5f, rad, w - rad), cy = Math.Clamp(y + 0.5f, rad, h - rad);
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = rad - MathF.Sqrt(dx * dx + dy * dy);                 // distance inside the rounded edge
                float edge = Math.Min(Math.Min(x + 0.5f, w - x - 0.5f), Math.Min(y + 0.5f, h - y - 0.5f));
                float inside = dx != 0 && dy != 0 ? d : edge;
                float cover = Math.Clamp(inside + 0.5f, 0, 1);                 // anti-aliased outline
                if (cover <= 0) continue;
                float rim = Math.Clamp(1.5f - inside, 0, 1);                   // 1-px light rim
                var c = Color.Lerp(row, border, rim * 0.22f);
                px[y * w + x] = c * (alpha * cover);
            }
        }
        var tex = new Texture2D(_device, w, h);
        tex.SetData(px);
        return tex;
    }

    /// <summary>The modern selection bar: a softly pulsing accent fill with a bright left notch.</summary>
    public void ModernHighlight(SpriteBatch sb, Rectangle r, int frame)
    {
        Fill(sb, r, Accent * (0.55f + 0.15f * MathF.Sin(frame * 0.12f)));
        Fill(sb, new Rectangle(r.X, r.Y, 2, r.Height), Accent);
    }

    static readonly Color[] ModernTints = { new(36, 64, 128), new(122, 36, 44), new(120, 96, 32), ModernBase };

    public void DrawText(SpriteBatch sb, string s, Vector2 pos, Color c)
    {
        if (Text.Get(s) is { } t) sb.Draw(t, pos, c);
    }

    /// <summary>
    /// Screen frame from SFRAME_H/V.BMP: 16-px strips around the 608x448 map viewport, copied opaque over whatever is
    /// under them — units reaching past the view, pictures (FUN_00413768 → FUN_004069a4). Strip variants: red arrow =
    /// can scroll that way, grey = can't.
    /// </summary>
    public void ScreenFrame(SpriteBatch sb, bool up, bool down, bool left, bool right)
    {
        if (Modern)
        {
            var bg = new Color(12, 14, 20);
            Fill(sb, new Rectangle(0, 0, 640, 16), bg);
            Fill(sb, new Rectangle(0, 464, 640, 16), bg);
            Fill(sb, new Rectangle(0, 16, 16, 448), bg);
            Fill(sb, new Rectangle(624, 16, 16, 448), bg);
            Frame(sb, new Rectangle(15, 15, 610, 450), ModernBorder);
            // a small chevron in the middle of each side that can still scroll
            void Chevron(int cx, int cy, int dx, int dy)
            {
                for (int k = 0; k < 4; k++)
                {
                    int len = 7 - 2 * k;
                    if (dx == 0) Fill(sb, new Rectangle(cx - len / 2, cy + (dy < 0 ? k : -k), len, 1), Accent);
                    else Fill(sb, new Rectangle(cx + (dx < 0 ? k : -k), cy - len / 2, 1, len), Accent);
                }
            }
            if (up) Chevron(320, 6, 0, -1);
            if (down) Chevron(320, 473, 0, 1);
            if (left) Chevron(6, 240, -1, 0);
            if (right) Chevron(633, 240, 1, 0);
            return;
        }
        var h = _tex.Get("SFRAME_H.BMP", transparent: false);
        var v = _tex.Get("SFRAME_V.BMP", transparent: false);
        if (h is null || v is null) return;
        sb.Draw(h, new Vector2(0, 0), new Rectangle(0, up ? 0 : 32, 640, 16), Color.White);
        sb.Draw(h, new Vector2(0, 464), new Rectangle(0, down ? 16 : 48, 640, 16), Color.White);
        sb.Draw(v, new Vector2(0, 16), new Rectangle(left ? 0 : 32, 0, 16, 448), Color.White);
        sb.Draw(v, new Vector2(624, 16), new Rectangle(right ? 16 : 48, 0, 16, 448), Color.White);
    }

    /// <summary>
    /// The mouse pointer from SYSTEM.BMP (FUN_00409db8): the arrow (y 0) or the hourglass (y 32), 32×32 cells from x 128
    /// drawn with their corner at the pointer; the four frames change every 2 game ticks.
    /// </summary>
    public void Cursor(SpriteBatch sb, Point pos, bool busy, int frame)
    {
        var sys = _tex.Get("SYSTEM.BMP");
        if (sys is null) return;
        int f = frame / 4 % 4;
        sb.Draw(sys, pos.ToVector2(), new Rectangle(128 + f * 32, busy ? 32 : 0, 32, 32), Color.White);
    }

    /// <summary>Panel textures: 64x64 tiles in SYSTEM.BMP's bottom row (verified for green).</summary>
    public enum PanelColor { Blue = 0, Red = 1, Yellow = 2, Green = 3 }

    /// <summary>
    /// Light/dark edge colours per panel colour (FUN_0041203c): the SYSTEM.BMP swatches FUN_004035e4 reads on row 288,
    /// 16 px apart, (light, dark) pairs in Blue/Red/Yellow/Green order. Blue's light one is (248,216,192) on that row
    /// (the rows below it are (240,248,248)); recording r1's blue and red panels have edges of the same brightness.
    /// </summary>
    static readonly (Color Light, Color Dark)[] Edges =
    {
        (new(248, 216, 192), new(0, 0, 48)), (new(248, 216, 192), new(64, 0, 0)),
        (new(248, 248, 224), new(40, 40, 8)), (new(248, 248, 224), new(0, 32, 0)),
    };

    public static (Color Light, Color Dark) EdgeColors(PanelColor c) =>
        Modern ? (new Color(255, 255, 255) * 0.22f, new Color(0, 0, 0) * 0.5f) : Edges[(int)c];

    /// <summary>
    /// Textured panel (menus, choices, EXP messages, battle info): SYSTEM.BMP tile tiled from the
    /// panel's top-left, 1-px light edge top/left, dark bottom/right (colours per <see cref="Edges"/>).
    /// </summary>
    public void Panel(SpriteBatch sb, Rectangle r, PanelColor color = PanelColor.Green)
    {
        if (Modern) { ModernBox(sb, r, ModernTints[(int)color], 8, 0.9f); return; }
        var sys = _tex.Get("SYSTEM.BMP", transparent: false);
        if (sys is null) { Fill(sb, r, new Color(0, 72, 0)); return; }
        int tx = (int)color * 64;
        for (int y = r.Y; y < r.Bottom; y += 64)
            for (int x = r.X; x < r.Right; x += 64)
            {
                int w = Math.Min(64, r.Right - x), h = Math.Min(64, r.Bottom - y);
                sb.Draw(sys, new Vector2(x, y), new Rectangle(tx, 224, w, h), Color.White);
            }
        var (light, dark) = Edges[(int)color];
        Fill(sb, new Rectangle(r.X, r.Y, r.Width, 1), light);
        Fill(sb, new Rectangle(r.X, r.Y, 1, r.Height), light);
        Fill(sb, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), dark);
        Fill(sb, new Rectangle(r.Right - 1, r.Y, 1, r.Height), dark);
    }

    public static readonly Color PanelText = new(248, 248, 248);
    /// <summary>A menu entry that can't be picked (FUN_00414344: SYSTEM.BMP swatch 9).</summary>
    public static readonly Color DisabledText = new(152, 152, 152);

    /// <summary>
    /// A menu row (FUN_00414344 / FUN_004144f4, colours from SYSTEM.BMP's swatches at y 288): the chosen row's bar —
    /// light green under (0,32,0) text and white under (0,64,0) text, swapping every 6 game ticks, or grey (122,122,122)
    /// under (0,32,0) text when it can't be picked; other rows white, or grey when they can't be picked. Draws the bar
    /// and returns the text colour; the modern skin uses its accent bar.
    /// </summary>
    public Color MenuRow(SpriteBatch sb, Rectangle bar, bool chosen, bool enabled, int frame)
    {
        if (Modern)
        {
            if (chosen) ModernHighlight(sb, bar, frame);
            return chosen ? (enabled ? Color.White : new Color(196, 204, 222)) : enabled ? PanelText : new Color(110, 116, 132);
        }
        if (!chosen) return enabled ? PanelText : DisabledText;
        bool green = frame / 12 % 2 == 0;
        Fill(sb, bar, !enabled ? new Color(122, 122, 122) : green ? new Color(160, 248, 96) : new Color(248, 248, 248));
        return !enabled || green ? new Color(0, 32, 0) : new Color(0, 64, 0);
    }

    /// <summary>Stat numbers added on panels (item summaries, terrain): a soft yellow, readable on both skins.</summary>
    public static readonly Color StatText = new(248, 232, 152);

    public void Dispose()
    {
        Text.Dispose();
        SmallText.Dispose();
        NameText.Dispose();
        MenuFont?.Dispose();
        _pixel.Dispose();
        foreach (var t in _windows.Values) t.Dispose();
        foreach (var t in _modern.Values) t.Dispose();
    }
}
