using Srpg95.Data;
using Srpg95.Rules;

namespace Srpg95.Data.Tests;

/// <summary>Battle-rule sanity checks on real map 1 (ルーティ and ジュナ vs ナス).</summary>
public class RulesTests
{
    static string? Game()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "えるまに", "SRPGEXEC.EXE"))) return Path.Combine(d.FullName, "えるまに");
        return null;
    }

    static Battlefield Map1(out Database db)
    {
        var g = new GameFolder(Game()!);
        db = Database.Load(g);
        var map = MapData.Load(g.MapPath(0));
        var f = new Battlefield(map, db, g.Chipset(map.Tileset)) { Rng = new Random(1) };
        foreach (var o in map.Objects.Where(o => o.InUse && o.Type != ObjectType.Other))
        {
            var u = db.Units[o.Unit];
            f.Units.Add(new BattleUnit
            {
                Unit = o.Unit, Side = o.Type == ObjectType.Ally ? Side.Ally : Side.Enemy, MapObject = o.Index,
                ClassId = u.ClassId, Level = u.Level, Base = (int[])u.BaseStats.Clone(), Growth = u.GrowthPct,
                Hp = u.BaseStats[0], Mp = u.BaseStats[1], Weapon = u.Weapon, Armor = u.Armor, Accessory = u.Accessory,
                X = o.X, Y = o.Y, MovePatterns = o.MovePatterns, Name = u.Name,
            });
        }
        return f;
    }

    [Fact]
    public void Movement_respects_terrain_and_budget()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var juna = f.Units.Single(u => u.Unit == 243);
        var reach = f.Reachable(juna);
        int move = f.Stats(juna).Movement;
        Assert.Equal(6, move);                                   // class 1 movement 6 (save record [19])
        Assert.All(reach.Values, v => Assert.InRange(v.Cost, 0, move));
        Assert.Contains((7, 11), reach.Keys);                    // T16: ジュナ moved (7,12) -> (7,11)
        Assert.DoesNotContain((8, 12), reach.Keys);              // occupied by ルーティ
        // the river row (y 6) is only crossable at the bridge: nothing reachable north of it in one move
        Assert.DoesNotContain(reach.Keys, k => k.Y < 6);
    }

    [Fact]
    public void Status_values_match_ジュナ_on_map_1()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var s = f.Stats(f.Units.Single(u => u.Unit == 243));
        // her status window (T17 screenshot): 攻撃力 11, 攻撃精度 17, 物理防御 3, 特殊防御 2
        Assert.Equal((11, 17, 3, 2), (s.Attack, s.Speed, s.Defense, s.MagicDefense));
    }

    [Fact]
    public void Exchange_and_ai_produce_consistent_state()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var juna = f.Units.Single(u => u.Unit == 243);
        var enemy = f.Units.First(u => u.Side == Side.Enemy && u.Unit == 4);
        enemy.X = juna.X; enemy.Y = juna.Y - 1;
        var r = f.Attack(juna, enemy);
        Assert.NotEmpty(r.Strikes);
        Assert.True(juna.Acted);
        Assert.All(r.Strikes, s => Assert.True(s.Damage >= 0));
        var plan = EnemyAi.Plan(f, f.Units.Single(u => u.Unit == 3), new bool[500]);
        Assert.True(f.Reachable(plan.Unit).ContainsKey(plan.Destination));
    }

    [Fact]
    public void Spells_are_learned_by_class_level()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var rutee = f.Units.Single(u => u.Unit == 242);   // class 0: ルーチェ＝フォルツァ at 8, イオンビーム at 12
        Assert.Empty(f.KnownSpells(rutee));
        rutee.Level = 12;
        Assert.Equal(new[] { 4, 45 }, f.KnownSpells(rutee).Select(m => m.Index));
        // level 11 -> 12 teaches イオンビーム
        rutee.Level = 11;
        rutee.Exp = 99;
        var enemy = f.Units.First(u => u.Unit == 4);
        enemy.X = rutee.X; enemy.Y = rutee.Y - 1;
        enemy.SleepTurns = 3;                      // asleep: always hit, so EXP (and the level-up) is certain
        var ex = f.Attack(rutee, enemy);
        Assert.Equal(12, rutee.Level);
        Assert.Equal("イオンビーム", Assert.Single(ex.Learned[rutee]).Name);
    }

    [Fact]
    public void Damage_spell_costs_mp_hits_the_area_and_gives_exp()
    {
        if (Game() is null) return;
        var f = Map1(out var db);
        var rutee = f.Units.Single(u => u.Unit == 242);
        rutee.Level = 12;
        rutee.Mp = 20;
        var ion = db.Magic[45];
        var enemy = f.Units.First(u => u.Unit == 4);
        // put the enemy on a tile the spell can be aimed at
        var aim = f.SpellRange(ion, rutee.X, rutee.Y).First(a => f.UnitAt(a.X, a.Y) is null);
        enemy.X = aim.X; enemy.Y = aim.Y;
        Assert.Contains(enemy, f.SpellTargets(rutee, ion, aim.X, aim.Y));
        int hpBefore = enemy.Hp;
        var r = f.Cast(rutee, ion, aim.X, aim.Y);
        Assert.Equal(20 - ion.MpCost, rutee.Mp);
        var e = Assert.Single(r.Effects);
        Assert.Same(enemy, e.Target);
        if (e.Kind == EffectKind.Damage)
        {
            Assert.Equal(Math.Min(hpBefore, f.PredictSpellDamage(rutee, ion, enemy)), hpBefore - enemy.Hp);
            Assert.True(r.ExpGained.ContainsKey(rutee));
        }
        Assert.True(rutee.Acted);
        Assert.DoesNotContain(r.Effects, x => x.Target.Side == Side.Ally);   // イオンビーム is "enemies only"
    }

    [Fact]
    public void Sleep_skips_two_turns_and_wakes_on_the_third()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var enemy = f.Units.First(u => u.Unit == 4);
        enemy.SleepTurns = 3;                      // what a landed Sleep spell sets during the ally phase
        var acted = new List<bool>();
        for (int turn = 0; turn < 3; turn++)
        {
            f.NextPhase();                         // enemy phase starts
            acted.Add(enemy.SleepTurns > 0);       // still asleep at its phase start = can't move (FUN_00410b90)
            f.NextPhase();                         // back to the allies
        }
        Assert.Equal(new[] { true, true, false }, acted);
    }

    [Fact]
    public void Potion_heals_up_to_max_hp_within_range()
    {
        if (Game() is null) return;
        var f = Map1(out var db);
        var juna = f.Units.Single(u => u.Unit == 243);
        var rutee = f.Units.Single(u => u.Unit == 242);
        var potion = db.Items[40];                 // ポーション: HP 8, range 1
        Assert.True(f.CanUseInBattle(juna, potion));
        Assert.Contains(rutee, f.MedicineTargets(juna, potion));   // (7,12) and (8,12) are adjacent
        rutee.Hp = rutee.MaxHp - 3;
        var r = f.UseMedicine(juna, potion, rutee);
        Assert.Equal(rutee.MaxHp, rutee.Hp);
        Assert.Equal(potion.S.MedHp, Assert.Single(r.Effects).Amount);   // the message shows the item's amount (FUN_0040b188)
        Assert.True(juna.Acted);
    }

    [Fact]
    public void Enemy_casts_a_spell_when_it_beats_its_weapon()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var nasu = f.Units.Single(u => u.Unit == 3);   // class 2: ナスビーム at level 10
        nasu.Level = 10;
        nasu.Mp = 30;
        nasu.Weapon = -1;                              // bare-handed, so the spell clearly wins
        foreach (var a in f.Units.Where(u => u.Side == Side.Ally)) { a.X = nasu.X; a.Y = nasu.Y + 3 + (a.Unit - 242); }
        var plan = EnemyAi.Plan(f, nasu, new bool[500]);
        var m = Assert.IsType<Magic>(plan.Spell);
        Assert.Equal("ナスビーム", m.Name);
        Assert.NotEmpty(f.SpellTargets(nasu, m, plan.Aim.X, plan.Aim.Y));
    }

    [Fact]
    public void Paths_through_an_ally_are_whole_and_never_end_on_a_unit()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var juna = f.Units.Single(u => u.Unit == 243);
        var rutee = f.Units.Single(u => u.Unit == 242);
        (juna.X, juna.Y) = (7, 9);       // the user's screenshot
        (rutee.X, rutee.Y) = (8, 10);
        var reach = f.Reachable(rutee);
        Assert.DoesNotContain((7, 9), reach.Keys);   // can't stop on ジュナ
        foreach (var dest in reach.Keys)
        {
            var path = Battlefield.PathTo(reach, dest);
            var at = (rutee.X, rutee.Y);
            foreach (var step in path)
            {
                Assert.Equal(1, Math.Abs(step.X - at.Item1) + Math.Abs(step.Y - at.Item2));   // one tile at a time
                at = step;
            }
            Assert.Equal(dest, at);
        }
        Assert.Contains((7, 7), reach.Keys);   // reached by walking through ジュナ
    }

    [Fact]
    public void Ai_attacks_the_unit_it_can_kill_and_walks_whole_paths()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var juna = f.Units.Single(u => u.Unit == 243);
        var rutee = f.Units.Single(u => u.Unit == 242);
        var minion = f.Units.First(u => u.Side == Side.Enemy && u.Unit != 3);
        foreach (var e in f.Units.Where(u => u.Side == Side.Enemy && u != minion)) e.Hidden = true;
        // both allies two tiles away; ルーティ can be killed, so she's worth 100 more (FUN_004162c4)
        (minion.X, minion.Y) = (8, 10);
        (juna.X, juna.Y) = (6, 10);
        (rutee.X, rutee.Y) = (10, 10);
        rutee.Hp = 1;
        var plan = EnemyAi.Plan(f, minion, new bool[500]);
        Assert.Same(rutee, plan.Target);
        Assert.True(f.InRange(minion, plan.Destination.X, plan.Destination.Y, rutee));
        Assert.True(f.Reachable(minion).ContainsKey(plan.Destination));
        var at = (minion.X, minion.Y);
        foreach (var step in plan.Path) { Assert.Equal(1, Math.Abs(step.X - at.Item1) + Math.Abs(step.Y - at.Item2)); at = step; }
        Assert.Equal(plan.Destination, at);
    }

    [Fact]
    public void Ai_rushes_toward_a_target_out_of_reach()
    {
        if (Game() is null) return;
        var f = Map1(out _);
        var minion = f.Units.First(u => u.Side == Side.Enemy && u.Unit != 3 && u.MovePatterns.FirstOrDefault().Action == 0);
        foreach (var e in f.Units.Where(u => u.Side == Side.Enemy && u != minion)) e.Hidden = true;
        int before = f.Units.Where(u => u.Side == Side.Ally).Min(a => Math.Abs(minion.X - a.X) + Math.Abs(minion.Y - a.Y));
        var plan = EnemyAi.Plan(f, minion, new bool[500]);
        if (plan.Target is not null) return;   // already in reach on this map: nothing to check
        Assert.NotEmpty(plan.Path);
        Assert.True(f.Reachable(minion).ContainsKey(plan.Destination));
        Assert.Null(f.UnitAt(plan.Destination.X, plan.Destination.Y));
        var nearest = f.Units.Where(u => u.Side == Side.Ally).Min(a => Math.Abs(plan.Destination.X - a.X) + Math.Abs(plan.Destination.Y - a.Y));
        Assert.True(nearest < before);
    }

    [Fact]
    public void Enemy_phases_never_stack_units()
    {
        Assert.NotNull(Game());
        for (int seed = 0; seed < 20; seed++)
        {
            var f = Map1(out _);
            f.Rng = new Random(seed);
            for (int turn = 0; turn < 8; turn++)
                foreach (var e in f.Alive.Where(u => u.Side == Side.Enemy).OrderBy(u => u.MapObject).ToList())
                {
                    if (e.Dead) continue;
                    var plan = EnemyAi.Plan(f, e, new bool[500]);
                    (e.X, e.Y) = plan.Destination;
                    var stacked = f.Alive.GroupBy(u => (u.X, u.Y)).FirstOrDefault(g => g.Count() > 1);
                    Assert.True(stacked is null, $"seed {seed} turn {turn}: {string.Join(", ", stacked?.Select(u => u.Name) ?? [])} on {stacked?.Key}");
                    if (plan.Target is { Dead: false } t && f.InRange(e, e.X, e.Y, t)) f.Attack(e, t);
                }
        }
    }

    [Fact]
    public void Ai_spell_scores_follow_the_exe()
    {
        if (Game() is null) return;
        var f = Map1(out var db);
        var nasu = f.Units.Single(u => u.Unit == 3);
        var juna = f.Units.Single(u => u.Unit == 243);
        var heal = db.Magic.First(m => m.Type == 2 && m.RaiseEffect == 0 && !m.EffectWholeMap && m.Value > 0);
        (juna.X, juna.Y) = (nasu.X, nasu.Y + 1);
        // healing an enemy of the caster's own side: min(power, missing) × 10 + 50; a player unit in the area −150
        var minion = f.Units.First(u => u.Side == Side.Enemy && u != nasu);
        (minion.X, minion.Y) = (nasu.X + 5, nasu.Y);
        minion.Hp = 1;
        int power = heal.Value + heal.Relation * f.Stats(nasu).Mag / 10;
        int expected = Math.Min(power, minion.MaxHp - 1) * 10 + 50;
        var area = f.SpellArea(heal, minion.X, minion.Y).ToHashSet();
        int others = f.Alive.Count(u => u != minion && area.Contains((u.X, u.Y)) && u.Side != Side.Enemy);
        Assert.Equal(Math.Max(0, expected - 150 * others), EnemyAi.SpellScore(f, nasu, heal, (minion.X, minion.Y)));
    }
}