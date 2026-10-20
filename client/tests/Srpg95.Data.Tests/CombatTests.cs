using Srpg95.Data;

namespace Srpg95.Data.Tests;

/// <summary>
/// Checks Combat's status-window formulas against the values the original runtime wrote into
/// save files (unit record [26..31] current stats, [32..35] Attack/Speed/Defense/MagicDefense,
/// [36] attack buff). Uses the save-testplay folder when present.
/// </summary>
public class CombatTests
{
    static readonly string? SaveDir = FindSaves();

    static string? FindSaves()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "えるまに - Save testplay");
            if (Directory.Exists(p) && Directory.GetFiles(p, "SAVE*.DAT").Length > 0) return p;
        }
        return null;
    }

    [Fact]
    public void Status_window_values_match_the_original_runtime()
    {
        if (SaveDir is null) return;
        var items = new GameFolder(SaveDir).ReadData("ITEM.DAT");
        var db = Bin.Records(items, 640, 100, "ITEM.DAT").Select((r, i) => Item.Parse(i, r.Span)).ToArray();
        Item? It(int i) => i >= 0 && i < db.Length ? db[i] : null;
        int records = 0;
        foreach (var path in Directory.GetFiles(SaveDir, "SAVE*.DAT"))
        {
            var b = File.ReadAllBytes(path);
            for (int slot = 0; slot < 150; slot++)
            {
                int o = 0x7d0 + slot * 640;
                if (Bin.I32(b, o) == 0) continue;
                var cur = Bin.I32s(b, o + 0x68, 6);   // HP, MP, Str, Mag, Skl, Sta (incl. accessory)
                var shown = Bin.I32s(b, o + 0x80, 4);
                int powerUp = Bin.I32(b, o + 0x90);
                var w = It(Bin.I32(b, o + 0xa0));
                var a = It(Bin.I32(b, o + 0xa4));
                Assert.Equal(shown[0], Combat.Attack(cur[2], w, powerUp));
                Assert.Equal(shown[1], Combat.Speed(cur[4], w, a));
                Assert.Equal(shown[2], Combat.Defense(cur[5], a));
                Assert.Equal(shown[3], Combat.MagicDefense(cur[3], a));
                records++;
            }
        }
        Assert.True(records > 0);
    }
}
