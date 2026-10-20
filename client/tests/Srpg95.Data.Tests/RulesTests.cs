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
        Assert.Contains(aim, f.SpellAims(rutee, ion, rutee.X, rutee.Y));
        int hpBefore = enemy.Hp;
        var r = f.Cast(rutee, ion, aim.X, aim.Y);
        Assert.Equal(20 - ion.MpCost, rutee.Mp);
        var e = Assert.Single(r.Effects);
        Assert.Same(enemy, e.Target);
        if (e.Kind == EffectKind.Damage)
        {
            Assert.Equal(f.PredictSpellDamage(rutee, ion, enemy), hpBefore - enemy.Hp);
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
            acted.Add(enemy.Acted);                // Acted at phase start = can't move
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
        Assert.Equal(3, Assert.Single(r.Effects).Amount);
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
}
