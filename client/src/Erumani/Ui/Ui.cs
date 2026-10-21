using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani.Ui;

/// <summary>
/// Drawing primitives for the 640x480 screen, matched pixel-for-pixel against screenshots
/// of the original runtime (FORMAT_NOTES T17): message window, speech tail, screen frame,
/// mouse cursor and GDI text.
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

    /// <summary>Message window including its 1-px outline (fill is 412x124).</summary>
    public const int MessageWidth = 414, MessageHeight = 126;

    readonly GraphicsDevice _device;
    readonly Texture2D _pixel, _messageWindow, _tail;
    readonly Textures _tex;
    public GdiText Text { get; }
    /// <summary>Menu/panel text (choices, camp menu): smaller than message text.</summary>
    public GdiText SmallText { get; }
    /// <summary>16 px bold: unit names on the battle scene's info panels (T22).</summary>
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
        _messageWindow = BuildWindow(MessageWidth, MessageHeight);
        _tail = BuildTail();
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

    /// <summary>Speech tail pointing down-left from the window's left edge (measured: 16x19).</summary>
    Texture2D BuildTail()
    {
        const int w = 17, h = 20;
        var px = new Color[w * h];
        // triangle: (16,0) top on the window edge, (0,18) tip, (16,15) back on the edge
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float upper = 16 - y * (16f / 18f);          // edge from (16,0) to (0,18)
                float lower = 16 - (y - 15) * (16f / 3f);    // edge from (16,15) to (0,18)
                if (x < upper - 0.5f || (y > 15 && x > lower + 0.5f) || y > 18) continue;
                bool edgeUpper = x < upper + 0.5f;
                bool edgeLower = y >= 15 && x > lower - 1.5f;
                px[y * w + x] = edgeUpper ? EdgeLight : edgeLower ? EdgeLight : WindowFill;
            }
        var tex = new Texture2D(_device, w, h);
        tex.SetData(px);
        return tex;
    }

    public void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_pixel, r, c);

    public void Frame(SpriteBatch sb, Rectangle r, Color c, int t = 1)
    {
        Fill(sb, new Rectangle(r.X, r.Y, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Y, t, r.Height), c);
        Fill(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    /// <summary>The message window at its outline position; tail drawn when a face is shown on the left.</summary>
    public void MessageWindow(SpriteBatch sb, Point topLeft, bool tail)
    {
        if (Modern)
        {
            ModernBox(sb, new Rectangle(topLeft.X, topLeft.Y, MessageWidth, MessageHeight), ModernBase, 12, 0.92f);
            Fill(sb, new Rectangle(topLeft.X + 14, topLeft.Y + 1, MessageWidth - 28, 2), Accent * 0.8f);
            return;
        }
        sb.Draw(_messageWindow, topLeft.ToVector2(), Color.White);
        if (tail) sb.Draw(_tail, new Vector2(topLeft.X - 15, topLeft.Y + 13), Color.White);
    }

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
    /// Screen frame from SFRAME_H/V.BMP: 16-px strips around the 608x448 map viewport.
    /// Strip variants: red arrow = can scroll that way, grey = can't.
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
        var h = _tex.Get("SFRAME_H.BMP");
        var v = _tex.Get("SFRAME_V.BMP");
        if (h is null || v is null) return;
        sb.Draw(h, new Vector2(0, 0), new Rectangle(0, up ? 0 : 32, 640, 16), Color.White);
        sb.Draw(h, new Vector2(0, 464), new Rectangle(0, down ? 16 : 48, 640, 16), Color.White);
        sb.Draw(v, new Vector2(0, 16), new Rectangle(left ? 0 : 32, 0, 16, 448), Color.White);
        sb.Draw(v, new Vector2(624, 16), new Rectangle(right ? 16 : 48, 0, 16, 448), Color.White);
    }

    /// <summary>Mouse cursor from SYSTEM.BMP: arrow (4 frames at y 0) or hourglass (4 frames at y 32).</summary>
    public void Cursor(SpriteBatch sb, Point pos, bool busy, int frame)
    {
        var sys = _tex.Get("SYSTEM.BMP");
        if (sys is null) return;
        int f = frame / 8 % 4;
        if (busy) sb.Draw(sys, new Vector2(pos.X - 9, pos.Y - 8), new Rectangle(128 + f * 32, 32, 18, 17), Color.White);
        else sb.Draw(sys, pos.ToVector2(), new Rectangle(128 + f * 32, 0, 20, 20), Color.White);
    }

    /// <summary>Panel textures: 64x64 tiles in SYSTEM.BMP's bottom row (verified for green).</summary>
    public enum PanelColor { Blue = 0, Red = 1, Yellow = 2, Green = 3 }

    /// <summary>
    /// Light/dark edge colours per panel colour: SYSTEM.BMP swatches at y 290, 16 px each,
    /// (light, dark) pairs in Blue/Red/Yellow/Green order. Green and red match screenshots (T17, T22).
    /// </summary>
    static readonly (Color Light, Color Dark)[] Edges =
    {
        (new(240, 248, 248), new(0, 0, 48)), (new(248, 216, 192), new(64, 0, 0)),
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

    public void Dispose()
    {
        Text.Dispose();
        SmallText.Dispose();
        NameText.Dispose();
        _pixel.Dispose();
        _messageWindow.Dispose();
        _tail.Dispose();
        foreach (var t in _windows.Values) t.Dispose();
        foreach (var t in _modern.Values) t.Dispose();
    }
}
