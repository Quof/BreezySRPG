using Srpg95.Data;

namespace Srpg95.Rules;

/// <summary>What a computer-controlled unit decided to do this phase.</summary>
public sealed record AiPlan(BattleUnit Unit, (int X, int Y) Destination, List<(int X, int Y)> Path, BattleUnit? Target, short SwitchToApply,
    Magic? Spell = null, (int X, int Y) Aim = default);

/// <summary>
/// Enemy / neutral behaviour as SRPGEXEC.EXE decides it (FUN_0041589c and helpers, FORMAT_NOTES §16).
/// The active move pattern is the highest-numbered one whose switch is ON (pattern 1 has none). Asleep: do nothing.
/// Unless the pattern is Specific:
///   1. Spells (not neutral units): every known spell the unit has MP for is scored at every aim it could reach
///      this turn (<see cref="SpellScore"/>); the best spell with a score above 0 is cast from the nearest tile.
///   2. Attacks (not neutral units): for each target, the best tile to attack it from anywhere on the map is the one
///      with the lowest (walk cost − terrain recovery − terrain defense/10, −4 if reachable this turn). A target is
///      worth (Attack − Defense)×3 + 100 (+200 instead if that kills) − walk cost; the best one reachable this turn is attacked.
/// Otherwise move: Rush heads for the most valuable target (neutral units: the first player unit), Specific for its
/// location, Wait and None stay. The unit walks the exe's path toward it as far as this turn allows and stops on the
/// last free tile. None never moves but may still attack or cast from where it stands.
/// </summary>
public static class EnemyAi
{
    public static MovePattern ActivePattern(BattleUnit u, bool[] switches)
    {
        var mp = u.MovePatterns;
        if (mp.Length == 0) return new MovePattern(-1, 0, 0, 0, -1);
        for (int i = mp.Length - 1; i >= 0; i--)
            if (mp[i].Switch >= 0 && mp[i].Switch < switches.Length && switches[mp[i].Switch]) return mp[i];
        return mp[0];
    }

    /// <summary>
    /// Every tile a computer unit could hit in its coming phase, as its AI would go about it (a modern aid; the original
    /// shows nothing like it), and the tiles it could stop on to do it: every free one its movement reaches this turn —
    /// only its own for the None pattern — and from them its <see cref="Hits"/>. Nothing for a unit that sleeps through
    /// that phase or follows a Specific pattern, which never attacks.
    /// </summary>
    public static (HashSet<(int X, int Y)> Stops, HashSet<(int X, int Y)> Hits) Threat(Battlefield f, BattleUnit u, bool[] switches)
    {
        var p = ActivePattern(u, switches);
        if (u.SleepTurns > 1 || p.Action == 2) return (new(), new());   // asleep still once its phase's upkeep has counted down
        var stops = f.Flood(u, p.Action < 3 ? f.Stats(u).Movement : 0).Keys
            .Where(t => f.UnitAt(t.X, t.Y) is not { } o || o == u).ToHashSet();
        return (stops, Hits(f, u, stops));
    }

    /// <summary>
    /// The tiles a unit could hit from any of <paramref name="stops"/> (a modern aid): its weapon's reach from each, and
    /// the area of every harmful spell it has the TP for, aimed anywhere in its range from them — none for a neutral
    /// unit, which casts nothing (FUN_0041589c).
    /// </summary>
    public static HashSet<(int X, int Y)> Hits(Battlefield f, BattleUnit u, IEnumerable<(int X, int Y)> stops)
    {
        var tiles = new HashSet<(int X, int Y)>();
        var st = f.Stats(u);
        var from = stops.ToList();
        foreach (var (sx, sy) in from)
            for (int y = sy - st.RangeMax; y <= sy + st.RangeMax; y++)
                for (int x = sx - st.RangeMax; x <= sx + st.RangeMax; x++)
                    if (Math.Abs(x - sx) + Math.Abs(y - sy) is var d && d >= st.RangeMin && d <= st.RangeMax && f.InBounds(x, y))
                        tiles.Add((x, y));
        if (u.Side == Side.Neutral || from.Count == 0) return tiles;
        foreach (var m in f.KnownSpells(u).Where(m => m.Type is 0 or 1 && Battlefield.CanCast(u, m)))
        {
            var aims = new HashSet<(int X, int Y)>();
            foreach (var (sx, sy) in m.RangeWholeMap || m.EffectWholeMap ? from.Take(1) : from) aims.UnionWith(f.SpellRange(m, sx, sy));
            foreach (var (ax, ay) in m.EffectWholeMap ? aims.Take(1) : aims) tiles.UnionWith(f.SpellArea(m, ax, ay));
        }
        return tiles;
    }

    /// <summary>Path costs for one unit's turn: every tile it could ever walk to, and which of them it can stop on this turn.</summary>
    sealed class Walk
    {
        public required Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)> Cost;
        public required int Budget;      // movement this turn (0 for the None pattern)
        public required int Movement;    // the unit's movement stat
        /// <summary>The exe's "moves spent": movement − moves left, so None-pattern units count their whole movement.</summary>
        public int Spent((int X, int Y) t) => Cost[t].Cost + Movement - Budget;
        public bool ThisTurn((int X, int Y) t) => Cost.TryGetValue(t, out var c) && c.Cost <= Budget;
    }

    public static AiPlan Plan(Battlefield f, BattleUnit u, bool[] switches)
    {
        var p = ActivePattern(u, switches);
        var here = (u.X, u.Y);
        if (u.SleepTurns > 0) return new AiPlan(u, here, new(), null, -1);
        int movement = f.Stats(u).Movement;
        var w = new Walk { Cost = f.Flood(u, int.MaxValue / 4), Budget = p.Action < 3 ? movement : 0, Movement = movement };
        bool neutral = u.Side == Side.Neutral;
        bool Free((int X, int Y) t) => f.UnitAt(t.X, t.Y) is not { } o || o == u;
        // tiles to stop on this turn, in the exe's scan order (rows, then columns)
        var stops = w.Cost.Keys.Where(t => w.ThisTurn(t) && Free(t)).OrderBy(t => t.Y).ThenBy(t => t.X).ToList();

        var targets = new List<(BattleUnit Unit, (int X, int Y) Tile, int Spent, int Value)>();
        if (p.Action != 2)
        {
            if (!neutral && BestSpell(f, u, w, stops) is { } s)
                return Go(f, u, w, s.Tile, null, -1, s.Spell, s.Spell.EffectWholeMap ? s.Tile : s.Aim);

            // FUN_004160f0 / FUN_004162c4: best tile and value for every unit the enemy may attack
            var st = f.Stats(u);
            foreach (var t in f.Alive.Where(b => b.Side != Side.Enemy && !(b.Side == Side.Neutral && b.Unattackable)).OrderBy(b => b.Slot))
            {
                (int X, int Y)? tile = null;
                int best = int.MaxValue;
                foreach (var c in w.Cost.Keys.Where(Free).OrderBy(c => c.Y).ThenBy(c => c.X))
                {
                    int d = Math.Abs(c.X - t.X) + Math.Abs(c.Y - t.Y);
                    if (d < st.RangeMin || d > st.RangeMax) continue;
                    var ter = f.TerrainAt(c.X, c.Y);
                    int score = w.Spent(c) - ter.HpRecovery - ter.DefenseBonus / 10 - (w.ThisTurn(c) ? 4 : 0);
                    if (score < best) { best = score; tile = c; }
                }
                if (tile is not { } at) continue;
                int spent = w.Spent(at);
                int dmg = st.Attack - f.Stats(t).Defense;
                int value = dmg < 1 ? 1 : dmg * 3 + (dmg < t.Hp ? 100 : 200) - spent;
                targets.Add((t, at, spent, value));
            }
            if (!neutral)
            {
                (BattleUnit Unit, (int X, int Y) Tile, int Spent, int Value)? pick = null;
                foreach (var t in targets)
                    if (t.Spent <= movement && t.Value > (pick?.Value ?? 1)) pick = t;
                if (pick is { } a) return Go(f, u, w, a.Tile, a.Unit, -1);
            }
        }

        (int X, int Y)? goal = null;
        if (p.Action == 0)
        {
            if (!neutral)
            {
                int top = 0;
                foreach (var t in targets)
                    if (t.Value > top) { top = t.Value; goal = (t.Unit.X, t.Unit.Y); }
            }
            else if (f.Alive.Where(b => b.Side == Side.Ally).OrderBy(b => b.Slot).FirstOrDefault() is { } ally) goal = (ally.X, ally.Y);
        }
        if (p.Action == 2) goal = (p.X, p.Y);
        if (goal is not { } g) return new AiPlan(u, here, new(), null, -1);

        var path = Trace(f, u, w, g);
        // as far along as this turn allows, then back to the last tile nobody else stands on
        int n = 0;
        while (n < path.Count && w.ThisTurn(path[n])) n++;
        while (n > 0 && !Free(path[n - 1])) n--;
        path.RemoveRange(n, path.Count - n);
        var dest = n > 0 ? path[^1] : here;
        short sw = p.Action == 2 && dest == g ? p.SwitchToApply : (short)-1;
        return new AiPlan(u, dest, path, null, sw);
    }

    static AiPlan Go(Battlefield f, BattleUnit u, Walk w, (int X, int Y) dest, BattleUnit? target, short sw, Magic? spell = null, (int X, int Y) aim = default) =>
        new(u, dest, Trace(f, u, w, dest), target, sw, spell, aim);

    /// <summary>
    /// FUN_004113c4: the exe's path, traced back from the goal to the unit by always stepping to the first neighbour
    /// that is closer to the unit (x−1, x+1, y−1, y+1 when the unit is farther away sideways, else y−1, y+1, x−1, x+1).
    /// Returned unit-first, without the unit's own tile; the goal itself needn't be walkable.
    /// </summary>
    static List<(int X, int Y)> Trace(Battlefield f, BattleUnit u, Walk w, (int X, int Y) goal)
    {
        int V((int X, int Y) t) => w.Cost.TryGetValue(t, out var c) ? -c.Cost : int.MinValue;
        var back = new List<(int X, int Y)>();
        var cur = goal;
        for (int guard = 0; cur != (u.X, u.Y) && guard < 10000; guard++)
        {
            back.Add(cur);
            int dx = Math.Abs(u.X - cur.X), dy = Math.Abs(u.Y - cur.Y);
            var order = dy < dx
                ? new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }
                : new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
            (int X, int Y)? next = null;
            foreach (var (ox, oy) in order)
            {
                var n = (cur.X + ox, cur.Y + oy);
                if (f.InBounds(n.Item1, n.Item2) && V(n) != int.MinValue && V(n) > V(cur)) { next = n; break; }
            }
            if (next is not { } nx) return new();
            cur = nx;
        }
        back.Reverse();
        return back;
    }

    /// <summary>FUN_0041589c's spell pass: the best (spell, aim) by <see cref="SpellScore"/>, cast from the cheapest tile (FUN_00416c44).</summary>
    static (Magic Spell, (int X, int Y) Tile, (int X, int Y) Aim)? BestSpell(Battlefield f, BattleUnit u, Walk w, List<(int X, int Y)> stops)
    {
        (Magic, (int X, int Y) Aim, int Score)? best = null;
        foreach (var m in f.KnownSpells(u))
        {
            if (!Battlefield.CanCast(u, m)) continue;
            // aims: anywhere for whole-map ranges, else within range of a tile the unit can stop on this turn (FUN_00416478)
            var aims = new HashSet<(int X, int Y)>();
            foreach (var s in m.RangeWholeMap ? stops.Take(1) : stops) aims.UnionWith(f.SpellRange(m, s.X, s.Y));
            if (m.EffectWholeMap) aims = aims.OrderBy(a => a.Y).ThenBy(a => a.X).Take(1).ToHashSet();   // same area wherever it's aimed
            int top = 0;
            (int X, int Y) at = default;
            foreach (var a in aims.OrderBy(a => a.Y).ThenBy(a => a.X))
            {
                int score = SpellScore(f, u, m, a);
                if (score > top) { top = score; at = a; }
            }
            if (top > (best?.Score ?? 0)) best = (m, at, top);
        }
        if (best is not { } b) return null;
        if (b.Item1.RangeWholeMap) return (b.Item1, (u.X, u.Y), b.Aim);
        (int X, int Y) tile = (u.X, u.Y);
        int cheapest = int.MaxValue;
        foreach (var s in stops)
        {
            if (!f.SpellRange(b.Item1, s.X, s.Y).Contains(b.Aim)) continue;
            int c = w.Spent(s);
            if (c < cheapest) { cheapest = c; tile = s; }
        }
        return (b.Item1, tile, b.Aim);
    }

    /// <summary>
    /// FUN_0041664c, a spell's worth when aimed at <paramref name="aim"/>, counting every unit in its area where it
    /// stands now (the caster only if the aim is in range from where it stands). Player-side units are the targets of
    /// damage and status spells, the caster's side of support spells; each unit on the wrong side costs 150.
    /// Damage: (spell damage + Mag − MDef) × 5 + 80 when positive. Poison / sleep: rand(20) + HP × 2 + 40 if not
    /// already so. Support: healing min(power, HP missing) × 10 + 50; Power / Defense Up rand(20) + power × 10 +
    /// Attack × 3 (× 2) + 30 if the current buff is below power / 2; and so on. Never below 0.
    /// </summary>
    public static int SpellScore(Battlefield f, BattleUnit caster, Magic m, (int X, int Y) aim)
    {
        var area = f.SpellArea(m, aim.X, aim.Y).ToHashSet();
        var cs = f.Stats(caster);
        int power = m.Value + m.Relation * cs.Mag / 10;
        int half = power / 2;
        var rng = f.Rng;
        int score = 0;
        foreach (var t in f.Alive.Where(t => area.Contains((t.X, t.Y))))
        {
            if (t == caster && !f.SpellRange(m, caster.X, caster.Y).Contains(aim)) continue;
            bool foe = t.Side != Side.Enemy;    // the exe's "not computer-controlled": allies and neutral units
            var ts = f.Stats(t);
            switch (m.Type)
            {
                case 0:
                    if (!foe) score -= 150;
                    else if (m.Damage + cs.Mag - ts.MagicDefense is var d && d > 0) score += d * 5 + 80;
                    break;
                case 1:
                    if (!foe) { score -= 150; break; }
                    switch (m.DegradeEffect)
                    {
                        case 0: if (t.PoisonTurns == 0) score += rng.Next(20) + t.Hp * 2 + 40; break;
                        case 1: if (t.SleepTurns == 0) score += rng.Next(20) + t.Hp * 2 + 40; break;
                        // as in the exe: only scores against a unit whose buff is already above half the power
                        case 2: if (half < t.PowerUp) score += rng.Next(40) + power * 10 + 30; break;
                        case 3: if (half < t.DefUp) score += rng.Next(40) + power * 10 + 30; break;
                    }
                    break;
                default:
                    if (foe) { score -= 150; break; }
                    switch (m.RaiseEffect)
                    {
                        case 0:
                            if (Math.Min(power, t.MaxHp - t.Hp) is var h && h > 0) score += h * 10 + 50;
                            break;
                        case 1: if (t.PowerUp < half) score += rng.Next(20) + power * 10 + ts.Attack * 3 + 30; break;
                        case 2: if (t.DefUp < half) score += rng.Next(20) + power * 10 + ts.Attack * 2 + 30; break;
                        case 3: if (t.PoisonTurns > 0) score += rng.Next(20) + t.PoisonTurns * 30 + 40; break;
                        case 4:
                            if (t.PoisonTurns > 0) score += t.PoisonTurns * 20 + 40;
                            if (t.SleepTurns > 0) score += t.PoisonTurns * 30 + 50;   // sic: the exe reuses the poison count
                            if (t.PowerUp < -2) score += -t.PowerUp * 10 + 60;
                            if (t.DefUp < -2) score += -t.DefUp * 10 + 60;
                            break;
                        case 5: if (t.Acted) score = rng.Next(20) + 95; break;   // replaces the running total, as in the exe
                    }
                    break;
            }
        }
        return Math.Max(0, score);
    }
}
