using Microsoft.Xna.Framework;

namespace Erumani.Ui;

/// <summary>
/// The gilded skin's gold, painted procedurally at the render scale (<see cref="Ui.RenderScale"/>) so that it is smooth at
/// any window size, and kept simple: a box has perfectly straight edges with a thin gold border along them — polished
/// metal lit from the top left, a little brighter on the side facing the light, with a slow sheen along it — and, inside
/// it, a small delicate curl in each corner (two volutes along the edges and a leaf between them). The dividers, the
/// selection glow, the arrows and the line round the map view are made the same way. Pictures are premultiplied, k render
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

    // ---------------- the corner curls ----------------

    /// <summary>Thin raised gold strokes that taper along curves, found by position through a grid.</summary>
    sealed class Relief
    {
        readonly List<(Vector2 A, Vector2 B, float Ra, float Rb)> _segs = new();
        List<int>?[] _grid = Array.Empty<List<int>?>();
        float _x0, _y0;
        int _gw, _gh;
        const float Cell = 2f, Reach = 1.5f;

        public bool Empty => _segs.Count == 0;

        /// <summary>A stroke through <paramref name="pts"/>, <paramref name="radii"/> its half-width at each.</summary>
        public void Stroke(IReadOnlyList<Vector2> pts, IReadOnlyList<float> radii)
        {
            for (int i = 1; i < pts.Count; i++) _segs.Add((pts[i - 1], pts[i], radii[i - 1], radii[i]));
        }

        /// <summary>A stroke through <paramref name="pts"/>, its half-width <paramref name="width"/>(t) at the fraction t of its length.</summary>
        public void Stroke(IReadOnlyList<Vector2> pts, Func<float, float> width)
        {
            if (pts.Count < 2) return;
            var len = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) len[i] = len[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = MathF.Max(len[^1], 1e-4f);
            Stroke(pts, len.Select(l => width(l / total)).ToArray());
        }

        public void Build()
        {
            if (_segs.Count == 0) return;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var (a, b, ra, rb) in _segs)
            {
                float r = MathF.Max(ra, rb) + Reach;
                x0 = MathF.Min(x0, MathF.Min(a.X, b.X) - r);
                y0 = MathF.Min(y0, MathF.Min(a.Y, b.Y) - r);
                x1 = MathF.Max(x1, MathF.Max(a.X, b.X) + r);
                y1 = MathF.Max(y1, MathF.Max(a.Y, b.Y) + r);
            }
            (_x0, _y0) = (x0, y0);
            _gw = (int)((x1 - x0) / Cell) + 1;
            _gh = (int)((y1 - y0) / Cell) + 1;
            _grid = new List<int>?[_gw * _gh];
            for (int i = 0; i < _segs.Count; i++)
            {
                var (a, b, ra, rb) = _segs[i];
                float r = MathF.Max(ra, rb) + Reach;
                int cx0 = (int)((MathF.Min(a.X, b.X) - r - x0) / Cell), cx1 = (int)((MathF.Max(a.X, b.X) + r - x0) / Cell);
                int cy0 = (int)((MathF.Min(a.Y, b.Y) - r - y0) / Cell), cy1 = (int)((MathF.Max(a.Y, b.Y) + r - y0) / Cell);
                for (int cy = cy0; cy <= Math.Min(cy1, _gh - 1); cy++)
                    for (int cx = cx0; cx <= Math.Min(cx1, _gw - 1); cx++)
                        (_grid[cy * _gw + cx] ??= new()).Add(i);
            }
        }

        List<int>? At(float x, float y)
        {
            int cx = (int)MathF.Floor((x - _x0) / Cell), cy = (int)MathF.Floor((y - _y0) / Cell);
            return cx < 0 || cy < 0 || cx >= _gw || cy >= _gh ? null : _grid[cy * _gw + cx];
        }

        /// <summary>How much of a pixel <paramref name="px"/> wide at (x, y) the strokes cover, and the normal of the topmost there.</summary>
        public float Sample(float x, float y, float px, out Vector3 normal)
        {
            normal = Vector3.UnitZ;
            if (At(x, y) is not { } cell) return 0;
            float cover = 0, best = -1;
            foreach (int i in cell)
            {
                var (a, b, ra, rb) = _segs[i];
                float abx = b.X - a.X, aby = b.Y - a.Y, l2 = abx * abx + aby * aby;
                float t = l2 > 0 ? Math.Clamp(((x - a.X) * abx + (y - a.Y) * aby) / l2, 0, 1) : 0;
                float dx = x - (a.X + abx * t), dy = y - (a.Y + aby * t), dist = MathF.Sqrt(dx * dx + dy * dy);
                float r = ra + (rb - ra) * t;
                float c = Math.Clamp((r - dist) / px + 0.5f, 0, 1);
                if (c <= 0) continue;
                cover = MathF.Max(cover, c);
                float u = MathF.Min(dist / r, 0.95f), z = r * MathF.Sqrt(1 - u * u);
                if (z <= best) continue;
                best = z;
                float slope = u / MathF.Sqrt(1 - u * u) * 0.7f;
                normal = dist > 1e-4f ? Vector3.Normalize(new Vector3(dx / dist * slope, dy / dist * slope, 1)) : Vector3.UnitZ;
            }
            return cover;
        }
    }

    static List<Vector2> Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, int n = 18)
    {
        var pts = new List<Vector2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            pts.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
        }
        return pts;
    }

    /// <summary>A spiral round <paramref name="c"/> from the angle <paramref name="a0"/> (y down: <paramref name="dir"/> 1 turns clockwise on screen), its radius shrinking from r0 to r1 over <paramref name="turns"/>.</summary>
    static List<Vector2> Spiral(Vector2 c, float a0, int dir, float turns, float r0, float r1)
    {
        int n = Math.Max(10, (int)(turns * 32));
        var pts = new List<Vector2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, a = a0 + dir * t * turns * MathF.Tau, r = r0 + (r1 - r0) * t;
            pts.Add(c + r * new Vector2(MathF.Cos(a), MathF.Sin(a)));
        }
        return pts;
    }

    /// <summary>
    /// A corner's curl in the corner's own frame — x along one edge and y along the other, both from the corner into the
    /// box, <paramref name="o"/> just inside the border, <paramref name="f"/> its size — mapped to the picture by
    /// <paramref name="map"/>: along each edge a fine stroke curling into a small volute, a leaf between them.
    /// </summary>
    static void Corner(Relief rel, Func<Vector2, Vector2> map, float o, float f)
    {
        float ws = 0.8f + 0.2f * f;
        Vector2 P(float a, float i) => new(o + a * f, o + i * f);
        rel.Stroke(Bezier(P(0.5f, 0.5f), P(1.7f, 1.7f), P(2.9f, 2.9f), P(4.4f, 4.4f)).Select(map).ToList(),
            t => ws * (t < 0.35f ? 0.25f + 0.33f * t / 0.35f : 0.58f * MathF.Pow(1 - (t - 0.35f) / 0.65f, 1.2f) + 0.03f));
        for (int side = 0; side < 2; side++)
        {
            Func<Vector2, Vector2> m = side == 0 ? map : p => map(new Vector2(p.Y, p.X));
            var lead = Bezier(P(0.3f, 0.3f), P(3.2f, -0.15f), P(6.4f, 0.0f), P(9.0f, 1.3f));
            var volute = Spiral(P(8.7f, 3.0f), MathF.Atan2(-1.7f, 0.3f), 1, 1.3f, 1.726f * f, 0.3f * f);
            var pts = lead.Concat(volute.Skip(1)).Select(m).ToList();
            var radii = lead.Select((_, i) => ws * (0.3f + 0.12f * i / (lead.Count - 1f)))
                .Concat(volute.Skip(1).Select((_, i) => ws * (0.42f - 0.24f * (i + 1) / (volute.Count - 1f)))).ToList();
            rel.Stroke(pts, radii);
        }
    }

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

    /// <summary>Corners of a box (<see cref="Box"/>), as bits.</summary>
    public const int TopLeft = 1, TopRight = 2, BottomLeft = 4, BottomRight = 8, AllCorners = 15;

    /// <summary>
    /// A gilded box w × h (its picture <see cref="Margin"/> larger all round): the fill a little lighter at the top and
    /// faintly shaded under the border; the thin border along the straight edges and round the pointer, if any
    /// (<paramref name="pointer"/> −1 / +1: out of the left / right side, its tip <paramref name="pointerY"/> below the top);
    /// a curl, sized by <paramref name="f"/>, in each of the <paramref name="corners"/>.
    /// </summary>
    public static Color[] Box(int w, int h, Color fill, float alpha, int pointer, int pointerY, float f, int corners, int k)
    {
        const int m = Margin;
        int tw = (w + 2 * m) * k, th = (h + 2 * m) * k;
        const float b = Border;
        var rel = new Relief();
        float o = b + 1.0f;
        if ((corners & TopLeft) != 0) Corner(rel, p => p, o, f);
        if ((corners & TopRight) != 0) Corner(rel, p => new Vector2(w - p.X, p.Y), o, f);
        if ((corners & BottomLeft) != 0) Corner(rel, p => new Vector2(p.X, h - p.Y), o, f);
        if ((corners & BottomRight) != 0) Corner(rel, p => new Vector2(w - p.X, h - p.Y), o, f);
        rel.Build();

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
                if (d > b)
                {
                    float on = rel.Sample(x, y, 1f / k, out var n);
                    if (on > 0) c = Vector4.Lerp(c, new Vector4(Gold(n, x, y), 1), on);
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
    /// dark strips, and the thin gold border just outside the view.
    /// </summary>
    public static Color[] ScreenFrame(Rectangle part, int k)
    {
        const float b = Border;
        int tw = part.Width * k, th = part.Height * k;
        var px = new Color[tw * th];
        var dark = Rgb(8, 10, 20);
        for (int row = 0; row < th; row++)
        {
            float y = part.Y + (row + 0.5f) / k;
            for (int col = 0; col < tw; col++)
            {
                float x = part.X + (col + 0.5f) / k;
                float dx = MathF.Max(MathF.Max(16 - x, x - 624), 0), dy = MathF.Max(MathF.Max(16 - y, y - 464), 0), dv = MathF.Max(dx, dy);
                if (dv <= 0) continue;
                var c = new Vector4(dark, 1);
                float edge = Math.Clamp((b - dv) * k + 0.5f, 0, 1);
                if (edge > 0)
                {
                    var gin = dx >= dy ? new Vector2(x < 16 ? 1 : -1, 0) : new Vector2(0, y < 16 ? 1 : -1);
                    c = Vector4.Lerp(c, new Vector4(Gold(WireNormal(2 * Math.Clamp(b - dv, 0, b) / b - 1, gin), x, y), 1), edge);
                }
                px[row * tw + col] = new Color(c);
            }
        }
        return px;
    }
}
