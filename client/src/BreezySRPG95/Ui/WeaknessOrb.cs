using Microsoft.Xna.Framework;

namespace BreezySRPG95.Ui;

/// <summary>
/// The status window's weakness orbs (modern), drawn here rather than taken from a font: a glossy ball lit from the top
/// left — deep at the bottom right, a hard-edged shine, a faint reflected light low down, no outline — and, for colour-blind
/// players, its attribute's letter A–F in a small italic serif built from strokes (thick stems and bowls, hairline bars and
/// serifs), set in the ball's exact middle. Made at the render scale, so it stays sharp at any window size.
/// </summary>
public static class WeaknessOrb
{
    public const int Size = 16;

    /// <summary>A stroke from one point to the next of the glyph's 0–1 box (y down), thick or a hairline.</summary>
    readonly record struct Stroke(Vector2[] Points, bool Thick);

    static Vector2 P(float x, float y) => new(x, y);
    static Stroke Thick(params Vector2[] p) => new(p, true);
    static Stroke Thin(params Vector2[] p) => new(p, false);

    /// <summary>Points round an ellipse from angle <paramref name="a0"/> to <paramref name="a1"/> (degrees, 0 = right, 90 = down).</summary>
    static Vector2[] Arc(float cx, float cy, float rx, float ry, float a0, float a1, int steps = 10) =>
        Enumerable.Range(0, steps + 1).Select(i =>
        {
            float a = MathHelper.ToRadians(a0 + (a1 - a0) * i / steps);
            return P(cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a));
        }).ToArray();

    static readonly Dictionary<char, Stroke[]> Glyphs = new()
    {
        ['A'] = new[] { Thin(P(0.08f, 1), P(0.5f, 0)), Thick(P(0.5f, 0), P(0.9f, 1)), Thin(P(0.27f, 0.62f), P(0.74f, 0.62f)),
            Thin(P(-0.04f, 1), P(0.22f, 1)), Thin(P(0.76f, 1), P(1.02f, 1)) },
        ['B'] = new[] { Thick(P(0.16f, 0), P(0.16f, 1)), Thin(P(0.02f, 0), P(0.56f, 0)),
            Thick(Arc(0.56f, 0.24f, 0.26f, 0.24f, -90, 90)), Thin(P(0.16f, 0.48f), P(0.6f, 0.48f)),
            Thick(Arc(0.6f, 0.74f, 0.3f, 0.26f, -90, 90)), Thin(P(0.02f, 1), P(0.6f, 1)) },
        ['C'] = new[] { Thick(Arc(0.54f, 0.5f, 0.42f, 0.5f, -40, -300, 16)), Thin(P(0.86f, 0.18f), P(0.88f, 0.02f)) },
        ['D'] = new[] { Thick(P(0.16f, 0), P(0.16f, 1)), Thin(P(0.02f, 0), P(0.46f, 0)),
            Thick(Arc(0.46f, 0.5f, 0.44f, 0.5f, -90, 90, 14)), Thin(P(0.02f, 1), P(0.46f, 1)) },
        ['E'] = new[] { Thick(P(0.18f, 0), P(0.18f, 1)), Thin(P(0.02f, 0), P(0.86f, 0)), Thin(P(0.86f, 0), P(0.88f, 0.18f)),
            Thin(P(0.18f, 0.5f), P(0.66f, 0.5f)), Thin(P(0.02f, 1), P(0.9f, 1)), Thin(P(0.9f, 1), P(0.92f, 0.8f)) },
        ['F'] = new[] { Thick(P(0.18f, 0), P(0.18f, 1)), Thin(P(0.02f, 0), P(0.88f, 0)), Thin(P(0.88f, 0), P(0.9f, 0.18f)),
            Thin(P(0.18f, 0.5f), P(0.66f, 0.5f)), Thin(P(0.02f, 1), P(0.4f, 1)) },
    };

    /// <summary>The orb's pixels at render scale <paramref name="k"/> (premultiplied), with <paramref name="letter"/> on it in <paramref name="ink"/> if given.</summary>
    public static Color[] Make(int k, Color colour, char? letter, Color ink)
    {
        int n = Size * k;
        const int ss = 3;
        float r = Size / 2f - 0.25f, c = Size / 2f;
        var body = colour.ToVector3();
        var deep = body * 0.38f;
        var bright = Vector3.Lerp(body, Vector3.One, 0.35f);

        // the letter: its strokes in pixels, sheared to an italic and set so their ink sits in the ball's middle
        const float height = 9.5f, width = 7f, shear = 0.22f, thick = 1.55f, thin = 0.75f;
        var segs = new List<(Vector2 A, Vector2 B, float W)>();
        if (letter is { } ch && Glyphs.TryGetValue(ch, out var strokes))
        {
            Vector2 Place(Vector2 g) => new(g.X * width + (1 - g.Y) * height * shear, g.Y * height);
            foreach (var s in strokes)
                for (int i = 0; i + 1 < s.Points.Length; i++)
                    segs.Add((Place(s.Points[i]), Place(s.Points[i + 1]), s.Thick ? thick : thin));
            float minX = segs.Min(s => MathF.Min(s.A.X, s.B.X) - s.W / 2), maxX = segs.Max(s => MathF.Max(s.A.X, s.B.X) + s.W / 2);
            float minY = segs.Min(s => MathF.Min(s.A.Y, s.B.Y) - s.W / 2), maxY = segs.Max(s => MathF.Max(s.A.Y, s.B.Y) + s.W / 2);
            var shift = new Vector2(c - (minX + maxX) / 2, c - (minY + maxY) / 2);
            for (int i = 0; i < segs.Count; i++) segs[i] = (segs[i].A + shift, segs[i].B + shift, segs[i].W);
        }
        static float Dist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.LengthSquared() < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            return Vector2.Distance(p, a + t * ab);
        }
        var inkV = ink.ToVector3();
        var shadowV = inkV.Length() > 1 ? body * 0.25f : Vector3.Lerp(body, Vector3.One, 0.6f);

        var px = new Color[n * n];
        for (int py = 0; py < n; py++)
            for (int pxl = 0; pxl < n; pxl++)
            {
                var sum = Vector3.Zero;
                int cover = 0;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        var p = new Vector2((pxl + (sx + 0.5f) / ss) / k, (py + (sy + 0.5f) / ss) / k);
                        float d = Vector2.Distance(p, new Vector2(c, c));
                        if (d > r) continue;
                        cover++;
                        // the body: lit from the top left, falling to deep at the bottom right
                        float light = Math.Clamp(1 - Vector2.Distance(p, new Vector2(c - 3.2f, c - 3.4f)) / (2 * r), 0, 1);
                        var col = Vector3.Lerp(deep, bright, MathF.Pow(light, 1.4f));
                        // a faint reflected light low down inside the edge
                        float low = Math.Clamp(1 - Vector2.Distance(p, new Vector2(c + 1.5f, c + 5.2f)) / 3.2f, 0, 1);
                        col = Vector3.Lerp(col, Vector3.Lerp(body, Vector3.One, 0.5f), 0.35f * low * low);
                        // the shine: a hard-edged ellipse near the top left, then a softer glow round it
                        var q = (p - new Vector2(c - 2.6f, c - 3.6f)) / new Vector2(2.6f, 1.7f);
                        float e = q.Length();
                        if (e < 1) col = Vector3.Lerp(col, Vector3.One, 0.92f);
                        else if (e < 1.7f) col = Vector3.Lerp(col, Vector3.One, 0.25f * (1.7f - e) / 0.7f);
                        // the letter, with a soft shadow a little down and right for contrast
                        if (segs.Count > 0)
                        {
                            float dl = segs.Min(s => Dist(p, s.A, s.B) - s.W / 2);
                            float ds = segs.Min(s => Dist(p - new Vector2(0.6f, 0.6f), s.A, s.B) - s.W / 2);
                            if (ds < 0.5f) col = Vector3.Lerp(col, shadowV, 0.45f);
                            if (dl < 0) col = inkV;
                        }
                        sum += Vector3.Clamp(col, Vector3.Zero, Vector3.One);
                    }
                float a = cover / (float)(ss * ss);
                px[py * n + pxl] = cover == 0 ? Color.Transparent : new Color(sum / cover * a) { A = (byte)MathF.Round(a * 255) };   // premultiplied
            }
        return px;
    }
}
