using Srpg95.Data;

namespace Srpg95.Rules;

/// <summary>One strike in an exchange.</summary>
public readonly record struct Strike(BattleUnit Attacker, BattleUnit Target, bool Hit, bool Critical, int Damage, bool Killed);

/// <summary>Everything that happened in one action: strikes (attacks), effects (spells/items), EXP, level-ups, new spells.</summary>
public sealed class ExchangeResult
{
    public List<Strike> Strikes { get; } = new();
    public Dictionary<BattleUnit, int> ExpGained { get; } = new();
    public Dictionary<BattleUnit, int[]> LevelUps { get; } = new();   // stat increases (6) per unit
    public List<Effect> Effects { get; } = new();
    /// <summary>Kill rewards (the defeated object's reward gold / item), for the player's side.</summary>
    public int RewardGold { get; set; }
    public List<int> RewardItems { get; } = new();
    public Dictionary<BattleUnit, List<Magic>> Learned { get; } = new();
}

/// <summary>
/// Battle-map rules: terrain, movement ranges, attacks, EXP/levels, turn upkeep and win/lose.
/// Formulas: <see cref="Combat"/> (help "Battle Algorithm"). Everything marked provisional
/// still needs checking against the original runtime.
/// </summary>
/// <summary>Tiles a unit can stop on, plus the full path links (including tiles it may only pass through).</summary>
public sealed class ReachMap(Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)> links)
    : Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)>
{
    public Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)> Links { get; } = links;
}

public sealed partial class Battlefield
{
    public MapData Map { get; }
    public Database Db { get; }
    public List<BattleUnit> Units { get; } = new();
    readonly byte[] _chipTerrain;
    public Random Rng { get; set; } = new();
    /// <summary>Phase counter, same encoding as the data: ally turn t = 2t−1, enemy turn t = 2t.</summary>
    public int Phase { get; private set; } = 1;
    public bool AllyPhase => Phase % 2 == 1;
    public int Turn => (Phase + 1) / 2;

    public Battlefield(MapData map, Database db, byte[]? chipTerrain)
    {
        Map = map;
        Db = db;
        _chipTerrain = chipTerrain ?? new byte[384];
    }

    // ---------------- lookup ----------------

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Map.Width && y < Map.Height;

    public Terrain TerrainAt(int x, int y)
    {
        int chip = Map.Tiles[y, x];
        int t = chip < _chipTerrain.Length ? _chipTerrain[chip] : 0;
        return Db.Terrains[Math.Min(t, Db.Terrains.Length - 1)];
    }

    public IEnumerable<BattleUnit> Alive => Units.Where(u => !u.Dead && !u.Hidden);

    public BattleUnit? UnitAt(int x, int y) => Units.FirstOrDefault(u => !u.Dead && !u.Hidden && u.X == x && u.Y == y);

    Item? Item(int id) => id >= 0 && id < Db.Items.Length ? Db.Items[id] : null;

    public ClassDef Class(BattleUnit u) => Db.Classes[Math.Clamp(u.ClassId, 0, Db.Classes.Length - 1)];

    public UnitStats Stats(BattleUnit u)
    {
        var w = Item(u.Weapon);
        var a = Item(u.Armor);
        var (str, mag, skl, sta, moveBonus) = Combat.WithAccessory(u.Base[2], u.Base[3], u.Base[4], u.Base[5], Item(u.Accessory));
        var cls = Class(u);
        var (rmin, rmax) = w is { Type: ItemType.Weapon } && w.S.WpnRange >= 0 && w.S.WpnRange < Data.Item.WeaponRanges.Length
            ? Data.Item.WeaponRanges[w.S.WpnRange] : (1, 1);
        return new UnitStats(str, mag, skl, sta, cls.Movement + moveBonus, cls.MoveType,
            Combat.Attack(str, w, u.PowerUp), Combat.Speed(skl, w, a), Combat.Defense(sta, a, u.DefUp),
            Combat.MagicDefense(mag, a, u.MdefUp), rmin, rmax);
    }

    /// <summary>Allies can't attack neutral units; neutral units never attack (help text).</summary>
    public static bool Hostile(BattleUnit a, BattleUnit b) => (a.Side, b.Side) switch
    {
        (Side.Ally, Side.Enemy) or (Side.Enemy, Side.Ally) => true,
        (Side.Enemy, Side.Neutral) => !b.Unattackable,
        _ => false,
    };

    static bool Friendly(BattleUnit a, BattleUnit b) => a.Side == b.Side || (a.Side, b.Side) is (Side.Ally, Side.Neutral) or (Side.Neutral, Side.Ally);

    // ---------------- movement ----------------

    /// <summary>Cost to enter a tile for this unit (0 = impassable): class override, else terrain cost for the move type.</summary>
    public int MoveCost(BattleUnit u, int x, int y)
    {
        var t = TerrainAt(x, y);
        var cls = Class(u);
        int ov = t.Index < cls.TerrainMoveOverride.Length ? cls.TerrainMoveOverride[t.Index] : -1;
        if (ov >= 0) return ov;
        int mt = Math.Clamp(cls.MoveType, 0, 2);
        return t.MoveCost[mt];
    }

    /// <summary>
    /// Tiles the unit can end its move on, with path back-links. Friendly units can be passed
    /// through but not stopped on; hostile ones block. (Provisional: no zone of control.)
    /// </summary>
    public Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)> Reachable(BattleUnit u)
    {
        int budget = Stats(u).Movement;
        var best = new Dictionary<(int, int), (int Cost, (int, int) From)> { [(u.X, u.Y)] = (0, (u.X, u.Y)) };
        var queue = new PriorityQueue<(int X, int Y), int>();
        queue.Enqueue((u.X, u.Y), 0);
        while (queue.TryDequeue(out var p, out int cost))
        {
            if (cost > best[p].Cost) continue;
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int nx = p.X + dx, ny = p.Y + dy;
                if (!InBounds(nx, ny)) continue;
                int c = MoveCost(u, nx, ny);
                if (c <= 0) continue;
                var occ = UnitAt(nx, ny);
                if (occ is not null && occ != u && !Friendly(u, occ)) continue;
                int nc = cost + c;
                if (nc > budget) continue;
                if (best.TryGetValue((nx, ny), out var old) && old.Cost <= nc) continue;
                best[(nx, ny)] = (nc, p);
                queue.Enqueue((nx, ny), nc);
            }
        }
        // tiles another unit stands on can be walked through but not stopped on: drop them from the result, but keep
        // every link so paths through them stay whole (a hole there made units jump and land on allies)
        var result = new ReachMap(best);
        foreach (var (k, v) in best)
            if (UnitAt(k.Item1, k.Item2) is not { } o || o == u) result[k] = v;
        return result;
    }

    public static List<(int X, int Y)> PathTo(Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)> reach, (int X, int Y) to)
    {
        var links = reach is ReachMap rm ? rm.Links : reach;
        var path = new List<(int, int)>();
        var p = to;
        while (links.TryGetValue(p, out var e) && e.From != p) { path.Add(p); p = e.From; }
        path.Reverse();
        return path;
    }

    static int Dist(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    public bool InRange(BattleUnit a, int fromX, int fromY, BattleUnit b)
    {
        var s = Stats(a);
        int d = Dist(fromX, fromY, b.X, b.Y);
        return d >= s.RangeMin && d <= s.RangeMax;
    }

    public IEnumerable<BattleUnit> TargetsFrom(BattleUnit a, int x, int y) =>
        Alive.Where(b => Hostile(a, b) && InRange(a, x, y, b));

    // ---------------- combat ----------------

    Strike StrikeOnce(BattleUnit atk, BattleUnit def)
    {
        var sa = Stats(atk);
        var sd = Stats(def);
        var w = Item(atk.Weapon);
        var arm = Item(def.Armor);
        int terrain = TerrainAt(def.X, def.Y).DefenseBonus;
        // FUN_0041a9c4: hit roll, crit roll only after a hit, integer damage, crit ×5/2 unless the damage is 0
        int hit = Combat.HitChance(sa.Skl, w, sd.Skl, arm, terrain);
        if (def.SleepTurns > 0) hit = 100;   // provisional (sleep isn't in the strike routine itself)
        bool hits = Rng.Next(100) < hit;
        if (!hits) return new Strike(atk, def, false, false, 0, false);
        bool crit = Rng.Next(100) < Combat.CritChance(sa.Skl, w, sd.Skl, arm);
        int attack = w is null ? sa.Attack : Combat.ApplyAttributes(sa.Attack, w.S.WpnAttackAttr, Class(def).Weakness, arm?.S.ArmDefenseAttr);
        int dmg = Combat.PhysicalDamage(attack, sd.Defense, terrain);
        if (dmg == 0) crit = false;
        if (crit) dmg = Combat.Critical(dmg);
        def.Hp = Math.Max(0, def.Hp - dmg);
        if (def.Hp == 0) def.Dead = true;
        return new Strike(atk, def, true, crit, dmg, def.Dead);
    }

    /// <summary>
    /// FUN_0041b4b0: the attacker strikes; the defender counters (GAME.DAT [2]) if it survived, isn't asleep and
    /// the attacker is in its range; the attacker strikes again (GAME.DAT [3], off in えるまに) if the last strike's
    /// target survived and it is 3+ speed faster. A player unit's EXP for the exchange is the largest value of its
    /// damaging strikes (a 0-damage hit gives none); a kill also collects the defeated object's rewards.
    /// </summary>
    public ExchangeResult Attack(BattleUnit atk, BattleUnit def)
    {
        var r = new ExchangeResult();
        r.Strikes.Add(StrikeOnce(atk, def));
        if (Db.Counterattacks && !def.Dead && def.SleepTurns == 0 && InRange(def, def.X, def.Y, atk))
            r.Strikes.Add(StrikeOnce(def, atk));
        if (Db.DoubleAttacks && !def.Dead && !atk.Dead && Combat.Doubles(Stats(atk).Speed, Stats(def).Speed))
            r.Strikes.Add(StrikeOnce(atk, def));
        foreach (var u in new[] { atk, def })
        {
            if (u.Side != Side.Ally) continue;
            int best = 0;
            foreach (var s in r.Strikes.Where(s => s.Attacker == u && s.Damage > 0))
            {
                best = Math.Max(best, Combat.Exp(u.Level, s.Target.Level, s.Killed));
                if (s.Killed) CollectReward(r, s.Target);
            }
            if (best > 0 && !u.Dead) AwardExp(r, u, best);
        }
        atk.Acted = true;
        return r;
    }

    /// <summary>A defeated map object's reward gold and item (SMAP object +0x50 / +0x4C).</summary>
    void CollectReward(ExchangeResult r, BattleUnit dead)
    {
        if (dead.MapObject < 0 || Map.Objects.FirstOrDefault(o => o.Index == dead.MapObject) is not { } o) return;
        r.RewardGold += o.RewardGold;
        if (o.RewardItem >= 0) r.RewardItems.Add(o.RewardItem);
    }

    /// <summary>EXP into the result, with any level-up and the spells that level teaches (WORD 73 を習得した).</summary>
    void AwardExp(ExchangeResult r, BattleUnit u, int exp)
    {
        r.ExpGained[u] = exp;
        var before = KnownSpells(u).ToHashSet();
        if (GainExp(u, exp) is not { } up) return;
        r.LevelUps[u] = up;
        var learned = KnownSpells(u).Where(m => !before.Contains(m)).ToList();
        if (learned.Count > 0) r.Learned[u] = learned;
    }

    static readonly int[] StatCaps = { 60, 60, 30, 30, 30, 30 };   // HP, MP, Str, Mag, Skl, Sta (FUN at 0x0040ab.., level-up)

    /// <summary>
    /// Adds EXP; every 100 is a level (several at once if need be), max 20 where EXP becomes 0. Each stat gains
    /// growth/100 plus one more with (growth % 100)% chance (growth = UNIT.DAT growth + accessory bonus), capped
    /// at HP/MP 60 and the rest 30; current HP/MP rise with their maximums. Returns the summed gains.
    /// </summary>
    public int[]? GainExp(BattleUnit u, int exp)
    {
        if (u.Level >= Combat.MaxLevel) return null;
        u.Exp += exp;
        int[]? total = null;
        while (u.Exp >= Combat.ExpPerLevel)
        {
            if (u.Level >= Combat.MaxLevel) { u.Level = Combat.MaxLevel; u.Exp = 0; break; }
            u.Exp -= Combat.ExpPerLevel;
            u.Level++;
            total ??= new int[6];
            int bonus = Item(u.Accessory) is { Type: ItemType.Accessory } acc ? acc.S.AccGrowthBonus : 0;
            for (int i = 0; i < 6; i++)
            {
                int g = u.Growth[i] + bonus;
                int gain = g / 100 + (Rng.Next(100) < g % 100 ? 1 : 0);
                if (u.Base[i] + gain > StatCaps[i]) gain = Math.Max(0, StatCaps[i] - u.Base[i]);
                u.Base[i] += gain;
                total[i] += gain;
                if (i == 0) u.Hp += gain;
                if (i == 1) u.Mp += gain;
            }
            if (u.Level >= Combat.MaxLevel) u.Exp = 0;
        }
        return total;
    }

    // ---------------- turns ----------------

    /// <summary>
    /// Ends the current phase and starts the next one: clears "acted", applies terrain HP
    /// recovery, poison, buff decay and sleep for the side that is about to move.
    /// </summary>
    public void NextPhase()
    {
        Phase++;
        var side = AllyPhase ? Side.Ally : Side.Enemy;
        foreach (var u in Alive.Where(u => u.Side == side || (side == Side.Enemy && u.Side == Side.Neutral)))
        {
            u.Acted = false;
            int rec = TerrainAt(u.X, u.Y).HpRecovery;
            if (rec != 0) u.Hp = Math.Clamp(u.Hp + rec, 1, u.MaxHp);   // provisional: terrain never kills
            if (u.PoisonTurns > 0)
            {
                u.Hp = Math.Max(1, u.Hp - Combat.PoisonDamage(u.Hp));
                u.PoisonTurns--;
            }
            if (u.SleepTurns > 0) u.SleepTurns--;
            if (u.SleepTurns > 0) u.Acted = true;   // still asleep: loses this turn
            u.PowerUp -= Math.Sign(u.PowerUp);
            u.DefUp -= Math.Sign(u.DefUp);
            u.MdefUp -= Math.Sign(u.MdefUp);
        }
    }

    /// <summary>Win condition 0 = defeat all enemies, 1 = defeat every leader, 2 = event only.</summary>
    public bool Won() => Map.WinCondition switch
    {
        0 => !Alive.Any(u => u.Side == Side.Enemy),
        1 => Units.Any(u => u.Leader && u.Side == Side.Enemy) && !Alive.Any(u => u.Leader && u.Side == Side.Enemy),
        _ => false,
    };

    /// <summary>Lost when every ally is down or a unit from the map's character list dies.</summary>
    public bool Lost() =>
        !Alive.Any(u => u.Side == Side.Ally) ||
        Units.Any(u => u.Dead && u.Side == Side.Ally && Map.CharacterList.Contains(u.Unit));

    /// <summary>Restarts the phase counter (used when a map starts).</summary>
    public void SetPhase(int phase) => Phase = phase;
}
