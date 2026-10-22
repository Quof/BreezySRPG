using Microsoft.Xna.Framework;

namespace BreezySRPG95.Ui;

/// <summary>
/// The gilded skin's gold, painted procedurally at the render scale (<see cref="Ui.RenderScale"/>) so that it is smooth at
/// any window size, and kept simple: a box has perfectly straight edges with a thin gold border along them — polished
/// metal lit from the top left, a little brighter on the side facing the light, with a slow sheen along it. The dividers,
/// the selection glow, the arrows and the line round the map view are made the same way. Pictures are premultiplied, k render
/// pixels to a 640 × 480 pixel.
/// </summary>
static class Gilding
{
    /// <summary>How far a box's picture reaches outside the box (for its pointer), in 640 × 480 pixels.</summary>
    public const int Margin = 10;
    public const int DividerHeight = 4, ArrowSize = 12;
    /// <summary>The border's width, in 640 × 480 pixels.</summary>
    const float Border = 1.1f;

    // ---------------- the metal ----------------

    static Vector3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    static readonly (float S, Vector3 C)[] Ramp =
    {
        (0.00f, Rgb(40, 24, 8)), (0.20f, Rgb(96, 62, 20)), (0.38f, Rgb(150, 106, 38)), (0.52f, Rgb(192, 146, 62)),
        (0.66f, Rgb(222, 180, 96)), (0.80f, Rgb(240, 208, 136)), (0.92f, Rgb(252, 230, 176)), (1.05f, Rgb(255, 246, 222)),
    };

    /// <summary>Gold at a brightness: 0 the deepest shadow, about 0.5 a face turned to nothing, 1 the highlight.</summary>
    static Vector3 GoldAt(float s)
    {
        if (s <= 0) return Ramp[0].C;
        for (int i = 1; i < Ramp.Length; i++)
            if (s <= Ramp[i].S) return Vector3.Lerp(Ramp[i - 1].C, Ramp[i].C, (s - Ramp[i - 1].S) / (Ramp[i].S - Ramp[i - 1].S));
        return Ramp[^1].C;
    }

    static readonly Vector3 Light = Vector3.Normalize(new Vector3(-1f, -1.3f, 1.6f));
    static readonly Vector3 Half = Vector3.Normalize(Light + Vector3.UnitZ);
    /// <summary>Which way the light runs across the screen (shadows fall that way).</summary>
    static readonly Vector2 ShadowDir = Vector2.Normalize(new Vector2(1, 1.3f));

    /// <summary>Polished metal's brightness for a surface normal: the light from the top left, the bright sky above that it reflects, a highlight.</summary>
    static float Shade(Vector3 n)
    {
        float diffuse = MathF.Max(0, Vector3.Dot(n, Light));
        float spec = MathF.Pow(MathF.Max(0, Vector3.Dot(n, Half)), 24);
        float sky = 0.5f - 0.5f * n.Y;
        return 0.1f + 0.42f * diffuse + 0.26f * sky * sky + 0.45f * spec;
    }

    /// <summary>The gold's colour at (x, y) with a surface normal: its shade and a slow sheen along it.</summary>
    static Vector3 Gold(Vector3 n, float x, float y) =>
        GoldAt(Shade(n) * (1 + 0.09f * MathF.Sin(0.05f * (x + 1.3f * y) + 0.6f)) + 0.02f * MathF.Sin(0.31f * (x - y)));

    /// <summary>A rounded wire's normal across its width: <paramref name="s"/> −1 at its outer edge to 1 at its inner, <paramref name="gin"/> pointing inward.</summary>
    static Vector3 WireNormal(float s, Vector2 gin)
    {
        float phi = MathF.Asin(Math.Clamp(s, -1, 1)) * 0.6f, sp = MathF.Sin(phi);
        return new Vector3(sp * gin.X, sp * gin.Y, MathF.Cos(phi));
    }

    /// <summary>Composites black at <paramref name="a"/> over a premultiplied colour.</summary>
    static Vector4 Darken(Vector4 c, float a) => new(c.X * (1 - a), c.Y * (1 - a), c.Z * (1 - a), c.W + (1 - c.W) * a);

    static float Smooth(float e, float x) => x <= 0 ? 0 : x >= e ? 1 : x / e * (x / e) * (3 - 2 * x / e);

    // ---------------- shapes ----------------

    static float BoxSdf(float x, float y, float w, float h)
    {
        float qx = MathF.Abs(x - w / 2) - w / 2, qy = MathF.Abs(y - h / 2) - h / 2;
        float ox = MathF.Max(qx, 0), oy = MathF.Max(qy, 0);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0);
    }

    static float TriSdf(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2, v0 = p - p0, v1 = p - p1, v2 = p - p2;
        Vector2 q0 = v0 - e0 * Math.Clamp(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0), 0, 1);
        Vector2 q1 = v1 - e1 * Math.Clamp(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1), 0, 1);
        Vector2 q2 = v2 - e2 * Math.Clamp(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2), 0, 1);
        float s = MathF.Sign(e0.X * e2.Y - e0.Y * e2.X);
        float dd = MathF.Min(MathF.Min(Vector2.Dot(q0, q0), Vector2.Dot(q1, q1)), Vector2.Dot(q2, q2));
        float side = MathF.Min(MathF.Min(s * (v0.X * e0.Y - v0.Y * e0.X), s * (v1.X * e1.Y - v1.Y * e1.X)), s * (v2.X * e2.Y - v2.Y * e2.X));
        return -MathF.Sqrt(dd) * MathF.Sign(side);
    }

    // ---------------- pictures ----------------

    /// <summary>
    /// A gilded box w × h (its picture <see cref="Margin"/> larger all round): the fill a little lighter at the top and
    /// faintly shaded under the border; the thin border along the straight edges and round the pointer, if any
    /// (<paramref name="pointer"/> −1 / +1: out of the left / right side, its tip <paramref name="pointerY"/> below the top).
    /// </summary>
    public static Color[] Box(int w, int h, Color fill, float alpha, int pointer, int pointerY, int k)
    {
        const int m = Margin;
        int tw = (w + 2 * m) * k, th = (h + 2 * m) * k;
        const float b = Border;

        // the pointer: a small triangle out of the side whose long sides run on into the box, so the two are one shape
        Vector2 t0 = default, t1 = default, t2 = default;
        if (pointer != 0)
        {
            const float reach = 7, half = 6.5f, inner = 4;
            float spread = half * (reach + inner) / reach;
            float tip = pointer < 0 ? -reach : w + reach, bx = pointer < 0 ? inner : w - inner;
            (t0, t1, t2) = (new(tip, pointerY), new(bx, pointerY - spread), new(bx, pointerY + spread));
        }
        float Sdf(float x, float y)
        {
            float s = BoxSdf(x, y, w, h);
            return pointer == 0 ? s : MathF.Min(s, TriSdf(new Vector2(x, y), t0, t1, t2));
        }
        Vector2 Inward(float x, float y)
        {
            if (pointer == 0)
            {
                float l = x, r = w - x, t = y, bo = h - y, min = MathF.Min(MathF.Min(l, r), MathF.Min(t, bo));
                return min == t ? Vector2.UnitY : min == bo ? -Vector2.UnitY : min == l ? Vector2.UnitX : -Vector2.UnitX;
            }
            const float e = 0.2f;
            var g = new Vector2(Sdf(x - e, y) - Sdf(x + e, y), Sdf(x, y - e) - Sdf(x, y + e));
            float len = g.Length();
            return len > 1e-5f ? g / len : Vector2.UnitY;
        }

        var bottom = fill.ToVector3();
        var top = Vector3.Lerp(bottom, Rgb(72, 88, 136), 0.22f);
        var px = new Color[tw * th];
        Parallel.For(0, th, row =>
        {
            float y = (row + 0.5f) / k - m;
            var fillRow = Vector3.Lerp(top, bottom, Math.Clamp(y / h, 0, 1));
            for (int col = 0; col < tw; col++)
            {
                float x = (col + 0.5f) / k - m, sd = Sdf(x, y);
                float cover = Math.Clamp(0.5f - sd * k, 0, 1);
                if (cover <= 0) continue;
                float d = -sd;
                var c = new Vector4(fillRow * alpha, alpha);
                if (d < b + 4)
                {
                    var gin = Inward(x, y);
                    // the border's faint shadow on the fill, under the top and left sides that the light comes over
                    c = Darken(c, 0.3f * MathF.Exp(-MathF.Max(0, d - b) / 1.2f) * (0.3f + 0.7f * MathF.Max(0, Vector2.Dot(gin, ShadowDir))));
                    float edge = Math.Clamp((b - d) * k + 0.5f, 0, 1);
                    if (edge > 0) c = Vector4.Lerp(c, new Vector4(Gold(WireNormal(2 * Math.Clamp(d, 0, b) / b - 1, gin), x, y), 1), edge);
                }
                px[row * tw + col] = new Color(c * cover);
            }
        });
        return px;
    }

    /// <summary>A divider w wide and <see cref="DividerHeight"/> high: a thin gold line along its middle, fading out toward both ends.</summary>
    public static Color[] Divider(int w, int k)
    {
        int tw = w * k, th = DividerHeight * k;
        float cy = DividerHeight / 2f, fade = MathF.Min(40, w * 0.3f);
        var px = new Color[tw * th];
        for (int row = 0; row < th; row++)
        {
            float y = (row + 0.5f) / k;
            for (int col = 0; col < tw; col++)
            {
                float x = (col + 0.5f) / k, taper = Smooth(fade, x) * Smooth(fade, w - x);
                float half = 0.55f * (0.45f + 0.55f * taper), dy = y - cy;
                float cover = Math.Clamp((half - MathF.Abs(dy)) * k + 0.5f, 0, 1) * (0.15f + 0.85f * taper);
                if (cover <= 0) continue;
                var n = WireNormal(Math.Clamp(dy / half, -1, 1), Vector2.UnitY);
                px[row * tw + col] = new Color(new Vector4(Gold(n, x, y), 1) * cover);
            }
        }
        return px;
    }

    /// <summary>The selection glow w × h: warm gold fading to the right and softly at its ends, top and bottom.</summary>
    public static Color[] Highlight(int w, int h, int k)
    {
        int tw = w * k, th = h * k;
        var glow = Rgb(226, 172, 72);
        var px = new Color[tw * th];
        for (int row = 0; row < th; row++)
        {
            float y = (row + 0.5f) / k;
            for (int col = 0; col < tw; col++)
            {
                float x = (col + 0.5f) / k;
                float a = (0.48f * MathF.Pow(Math.Clamp(1 - x / w, 0, 1), 0.7f) + 0.1f) * Smooth(2, y) * Smooth(2, h - y) * Smooth(1.5f, x) * Smooth(4, w - x);
                px[row * tw + col] = new Color(new Vector4(glow * a, a));
            }
        }
        return px;
    }

    /// <summary>A small gold arrowhead pointing up, faceted, in the middle of an <see cref="ArrowSize"/>-square picture.</summary>
    public static Color[] Arrow(int k)
    {
        var a = new Vector2(6, 3.2f);
        var b = new Vector2(2.4f, 8.2f);
        var c = new Vector2(9.6f, 8.2f);
        int n = ArrowSize * k;
        var px = new Color[n * n];
        for (int row = 0; row < n; row++)
            for (int col = 0; col < n; col++)
            {
                float x = (col + 0.5f) / k, y = (row + 0.5f) / k;
                var p = new Vector2(x, y);
                float sd = TriSdf(p, a, b, c), cover = Math.Clamp(0.5f - sd * k, 0, 1);
                if (cover <= 0) continue;
                const float e = 0.2f;
                var g = new Vector2(TriSdf(p - new Vector2(e, 0), a, b, c) - TriSdf(p + new Vector2(e, 0), a, b, c),
                    TriSdf(p - new Vector2(0, e), a, b, c) - TriSdf(p + new Vector2(0, e), a, b, c));
                if (g.LengthSquared() > 1e-8f) g.Normalize();
                float bevel = Math.Clamp(1 - -sd / 1.4f, 0, 1) * 0.8f;
                var nrm = Vector3.Normalize(new Vector3(-g.X * bevel, -g.Y * bevel, 1));
                px[row * n + col] = new Color(new Vector4(Gold(nrm, x, y), 1) * cover);
            }
        return px;
    }

    /// <summary>
    /// The screen frame round the 608 × 448 view at (16, 16), the part of the 640 × 480 screen in <paramref name="part"/>:
    /// dark strips, and a thin gold wire where the original's silver one runs (SFRAME_H / SFRAME_V: 9½ px in from the
    /// screen's edges, its corners rounded).
    /// </summary>
    public static Color[] ScreenFrame(Rectangle part, int k)
    {
        const float inset = 9.5f, radius = 5f, half = 0.8f;
        int tw = part.Width * k, th = part.Height * k;
        var px = new Color[tw * th];
        var dark = Rgb(8, 10, 20);
        // signed distance to the wire's middle line, a rounded rectangle (negative inside it)
        static float Line(float x, float y)
        {
            float qx = MathF.Abs(x - 320) - (320 - inset - radius), qy = MathF.Abs(y - 240) - (240 - inset - radius);
            return new Vector2(MathF.Max(qx, 0), MathF.Max(qy, 0)).Length() + MathF.Min(MathF.Max(qx, qy), 0) - radius;
        }
        for (int row = 0; row < th; row++)
        {
            float y = part.Y + (row + 0.5f) / k;
            for (int col = 0; col < tw; col++)
            {
                float x = part.X + (col + 0.5f) / k;
                if (x >= 16 && x <= 624 && y >= 16 && y <= 464) continue;
                var c = new Vector4(dark, 1);
                float d = Line(x, y), cover = Math.Clamp((half - MathF.Abs(d)) * k + 0.5f, 0, 1);
                if (cover > 0)
                {
                    const float e = 0.05f;
                    var gin = -new Vector2(Line(x + e, y) - Line(x - e, y), Line(x, y + e) - Line(x, y - e));
                    if (gin.LengthSquared() > 1e-8f) gin.Normalize();
                    c = Vector4.Lerp(c, new Vector4(Gold(WireNormal(Math.Clamp(-d / half, -1, 1), gin), x, y), 1), cover);
                }
                px[row * tw + col] = new Color(c);
            }
        }
        return px;
    }
}
