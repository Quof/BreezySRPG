using Erumani.Runtime;

namespace Srpg95.Data.Tests;

/// <summary>SAVEn.DAT round trips against the user's real saves (えるまに - Save testplay).</summary>
public class SaveFileTests
{
    static string? Folder(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, name))) return Path.Combine(d.FullName, name);
        return null;
    }

    [Fact]
    public void Every_camp_save_rebuilds_its_roster_records()
    {
        var test = Folder("えるまに - Save testplay");
        var game = Folder("えるまに");
        if (test is null || game is null) return;
        var db = Database.Load(new GameFolder(game));
        int checkedSaves = 0;
        foreach (var path in Directory.GetFiles(test, "SAVE?.DAT"))
        {
            var save = SaveFile.Load(path);
            if (save is not { AtCamp: true }) continue;
            var state = new GameState();
            save.ApplyTo(state);
            var blank = (byte[])save.Raw.Clone();
            Array.Clear(blank, 0x101D0, 50 * 640);
            var bgm = System.Text.Encoding.GetEncoding(932).GetString(save.Raw, 0x53C, 48).TrimEnd('\0');
            var fresh = SaveFile.ForCamp(state, db, save.MapNumber - 1, bgm, blank);
            for (int u = 0; u < state.Roster.Count; u++)
                for (int k = 0; k < 160; k++)
                {
                    if (k is 5 or 6 or 50 or 52 or 54 or 55 or 56) continue;   // last battle's leftovers: position, movement left, [52], [56]
                    int off = 0x101D0 + u * 640 + k * 4;
                    Assert.True(BitConverter.ToInt32(save.Raw, off) == BitConverter.ToInt32(fresh.Raw, off),
                        $"{Path.GetFileName(path)} roster {u} field [{k}]: {BitConverter.ToInt32(save.Raw, off)} vs {BitConverter.ToInt32(fresh.Raw, off)}");
                }
            checkedSaves++;
        }
        Assert.True(checkedSaves > 0 || Environment.GetEnvironmentVariable("SRPG95_REQUIRE_DATA") != "1");
    }

    [Fact]
    public void Camp_save_round_trips_byte_for_byte()
    {
        var test = Folder("えるまに - Save testplay");
        var game = Folder("えるまに");
        if (test is null || game is null || !File.Exists(Path.Combine(test, "SAVE6.DAT")))
        {
            Assert.True(Environment.GetEnvironmentVariable("SRPG95_REQUIRE_DATA") != "1", "save data missing");
            return;
        }
        var db = Database.Load(new GameFolder(game));
        var save = SaveFile.Load(Path.Combine(test, "SAVE6.DAT"))!;   // camp before map 2
        Assert.True(save.AtCamp);
        Assert.Equal(2, save.MapNumber);

        var state = new GameState();
        save.ApplyTo(state);
        Assert.Equal(new[] { 242, 243 }, state.Roster.Keys.Order());
        Assert.Equal(1, state.ItemCount(40));                        // one ポーション
        Assert.Equal(10, state.Gold);

        // rebuild it from the parsed state on a blank template for the roster part and compare every byte
        var bgm = System.Text.Encoding.GetEncoding(932).GetString(save.Raw, 0x53C, 48).TrimEnd('\0');
        var again = SaveFile.ForCamp(state, db, save.MapNumber - 1, bgm, save.Raw);
        Assert.Equal(save.Raw, again.Raw);

        // and with the roster records rebuilt from nothing (positions aside, which a camp doesn't know)
        var blankRoster = (byte[])save.Raw.Clone();
        Array.Clear(blankRoster, 0x101D0, 50 * 640);
        var fresh = SaveFile.ForCamp(state, db, save.MapNumber - 1, bgm, blankRoster);
        int[] skip = { 5, 6, 54, 55 };   // x, y and position before moving
        for (int u = 0; u < 2; u++)
            for (int k = 0; k < 160; k++)
            {
                if (skip.Contains(k)) continue;
                int off = 0x101D0 + u * 640 + k * 4;
                Assert.True(BitConverter.ToInt32(save.Raw, off) == BitConverter.ToInt32(fresh.Raw, off),
                    $"roster {u} field [{k}]: {BitConverter.ToInt32(save.Raw, off)} vs {BitConverter.ToInt32(fresh.Raw, off)}");
            }
    }
}
