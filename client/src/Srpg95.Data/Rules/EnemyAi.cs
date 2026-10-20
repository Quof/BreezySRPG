using Srpg95.Data;

namespace Srpg95.Rules;

/// <summary>What a computer-controlled unit decided to do this phase.</summary>
public sealed record AiPlan(BattleUnit Unit, (int X, int Y) Destination, List<(int X, int Y)> Path, BattleUnit? Target, short SwitchToApply);

/// <summary>
/// Enemy / neutral behaviour from each object's four movement patterns (FORMAT_NOTES §11.1):
/// the highest-numbered pattern whose switch is ON wins (pattern 1 has no switch).
///   Rush: attack the best reachable target, else head for the nearest ally.
///   Wait: attack only if a target is reachable this turn.
///   Specific: walk toward the pattern's location; on arrival, turn its switch ON.
///   None: stay put, attack whatever is already in range.
/// Target choice follows the game's own readme ("enemies prefer the unit with the lowest
/// defense or magic defense"): lowest defense, then lowest HP. Provisional.
/// Neutral units never attack (help text); with Rush they walk up to allies.
/// </summary>
public static class EnemyAi
{
    public static MovePattern ActivePattern(BattleUnit u, bool[] switches)
    {
        var mp = u.MovePatterns;
        if (mp.Length == 0) return new MovePattern(-1, 0, 0, 0, -1);
        for (int i = mp.Length - 1; i >= 1; i--)
            if (mp[i].Switch >= 0 && mp[i].Switch < switches.Length && switches[mp[i].Switch]) return mp[i];
        return mp[0];
    }

    public static AiPlan Plan(Battlefield f, BattleUnit u, bool[] switches)
    {
        var p = ActivePattern(u, switches);
        var here = (u.X, u.Y);
        if (u.SleepTurns > 0) return new AiPlan(u, here, new(), null, -1);
        bool attacks = u.Side == Side.Enemy;

        if (p.Action == 3)   // none: don't move
            return new AiPlan(u, here, new(), attacks ? BestTarget(f, f.TargetsFrom(u, u.X, u.Y)) : null, -1);

        var reach = f.Reachable(u);
        if (attacks && p.Action is 0 or 1)
        {
            // best (target, standing tile): lowest defense, lowest HP, shortest walk
            var options = reach.Keys
                .SelectMany(t => f.TargetsFrom(u, t.X, t.Y).Select(b => (Tile: t, Target: b)))
                .OrderBy(o => f.Stats(o.Target).Defense).ThenBy(o => o.Target.Hp).ThenBy(o => reach[o.Tile].Cost)
                .ToList();
            if (options.Count > 0)
            {
                var (tile, target) = options[0];
                return new AiPlan(u, tile, Battlefield.PathTo(reach, tile), target, -1);
            }
            if (p.Action == 1) return new AiPlan(u, here, new(), null, -1);
        }

        (int X, int Y)? goal = p.Action switch
        {
            2 => (p.X, p.Y),
            0 => Nearest(f, u, attacks),
            _ => null,
        };
        if (goal is null) return new AiPlan(u, here, new(), null, -1);
        // pick the reachable tile closest to the goal (by straight-line distance; provisional)
        var dest = reach.Keys.OrderBy(t => Math.Abs(t.X - goal.Value.X) + Math.Abs(t.Y - goal.Value.Y))
            .ThenBy(t => reach[t].Cost).First();
        BattleUnit? after = attacks ? BestTarget(f, f.TargetsFrom(u, dest.X, dest.Y)) : null;
        short sw = p.Action == 2 && dest == (p.X, p.Y) ? p.SwitchToApply : (short)-1;
        return new AiPlan(u, dest, Battlefield.PathTo(reach, dest), after, sw);
    }

    static (int X, int Y)? Nearest(Battlefield f, BattleUnit u, bool hostileOnly)
    {
        var allies = f.Alive.Where(b => b.Side == Side.Ally && (!hostileOnly || Battlefield.Hostile(u, b))).ToList();
        if (allies.Count == 0) return null;
        var n = allies.OrderBy(b => Math.Abs(b.X - u.X) + Math.Abs(b.Y - u.Y)).First();
        return (n.X, n.Y);
    }

    static BattleUnit? BestTarget(Battlefield f, IEnumerable<BattleUnit> targets) =>
        targets.OrderBy(b => f.Stats(b).Defense).ThenBy(b => b.Hp).FirstOrDefault();
}
