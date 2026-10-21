using Srpg95.Data;

namespace Srpg95.Data.Tests;

/// <summary>Rules read from SRPGEXEC.EXE's disassembly (FORMAT_NOTES §15 "From the executable").</summary>
public class ExeRulesTests
{
    static readonly byte[] None = new byte[6];
    static byte[] Attr(params int[] set) { var a = new byte[6]; foreach (var i in set) a[i] = 1; return a; }

    [Fact]
    public void Attributes_weakness_is_three_halves_and_full_guard_is_four_fifths()
    {
        Assert.Equal(15, Combat.ApplyAttributes(10, Attr(0), Attr(0), null));          // weak, unguarded
        Assert.Equal(8, Combat.ApplyAttributes(10, Attr(0), None, Attr(0)));            // guarded: ×4/5, not ×1/2
        Assert.Equal(10, Combat.ApplyAttributes(10, Attr(0), Attr(0), Attr(0)));        // weak but guarded: neither
        Assert.Equal(10, Combat.ApplyAttributes(10, Attr(0, 1), None, Attr(0)));       // one attribute unguarded
        Assert.Equal(10, Combat.ApplyAttributes(10, None, Attr(0), null));              // no attribute
    }

    [Fact]
    public void Damage_is_integer_and_clamped_and_criticals_need_damage()
    {
        Assert.Equal(9, Combat.PhysicalDamage(12, 2, 15));    // 12 − 2 − 15/10 (truncated)
        Assert.Equal(0, Combat.PhysicalDamage(3, 9, 0));
        Assert.Equal(99, Combat.PhysicalDamage(150, 0, 0));
        Assert.Equal(22, Combat.Critical(9));                 // 9 × 5 / 2
        Assert.Equal(99, Combat.Critical(60));
        Assert.Equal(11, Combat.PhysicalDamage(10, 0, -15));   // negative terrain: −15/10 = −1 → +1 damage
    }

    [Fact]
    public void Derived_stats_are_clamped_to_0_99()
    {
        Assert.Equal(99, Combat.Attack(120, null));
        Assert.Equal(0, Combat.Defense(-5, null));
    }

    [Fact]
    public void Game_dat_turns_double_attacks_off_in_erumani()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var g = Path.Combine(d.FullName, "えるまに");
            if (!File.Exists(Path.Combine(g, "SRPGEXEC.EXE"))) continue;
            var db = Database.Load(new GameFolder(g));
            Assert.Equal(98, db.StartMap);
            Assert.True(db.Counterattacks);
            Assert.False(db.DoubleAttacks);
            Assert.False(db.SplitBackgrounds);
            return;
        }
    }
}
