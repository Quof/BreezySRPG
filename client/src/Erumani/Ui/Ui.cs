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
    public static readonly Color TextColor = new(16, 16, 0);
    public static readonly Color WindowFill = new(184, 200, 160);
    static readonly Color EdgeLight = new(241, 241, 241), EdgeDark = new(96, 96, 96);

    /// <summary>Message window including its 1-px outline (fill is 412x124).</summary>
    public const int MessageWidth = 414, MessageHeight = 126;

    readonly GraphicsDevice _device;
    readonly Texture2D _pixel, _messageWindow, _tail;
    readonly Textures _tex;
    public GdiText Text { get; }

    public Ui(GraphicsDevice device, Textures tex)
    {
        _device = device;
        _tex = tex;
        Text = new GdiText(device);
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
        sb.Draw(_messageWindow, topLeft.ToVector2(), Color.White);
        if (tail) sb.Draw(_tail, new Vector2(topLeft.X - 15, topLeft.Y + 13), Color.White);
    }

    /// <summary>Generic window for menus (same skin, any size).</summary>
    public void Window(SpriteBatch sb, Rectangle r)
    {
        // Built on demand per size; menus are few.
        if (!_windows.TryGetValue((r.Width, r.Height), out var t))
            _windows[(r.Width, r.Height)] = t = BuildWindow(r.Width, r.Height);
        sb.Draw(t, r.Location.ToVector2(), Color.White);
    }
    readonly Dictionary<(int, int), Texture2D> _windows = new();

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

    public void Dispose()
    {
        Text.Dispose();
        _pixel.Dispose();
        _messageWindow.Dispose();
        _tail.Dispose();
        foreach (var t in _windows.Values) t.Dispose();
    }
}
