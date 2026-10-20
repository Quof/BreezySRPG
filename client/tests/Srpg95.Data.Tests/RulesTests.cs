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
}
