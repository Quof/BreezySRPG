using Erumani.Config;
using Erumani.Runtime;

namespace Srpg95.Data.Tests;

/// <summary>EasySRPG95's own additions around saves and settings: the notes beside the saves, play time, window sizes, the camera.</summary>
public class ClientFeatureTests
{
    static string? Folder(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, name))) return Path.Combine(d.FullName, name);
        return null;
    }

    [Fact]
    public void A_note_belongs_to_the_save_it_was_written_with()
    {
        var root = Directory.CreateTempSubdirectory("easysrpg95-notes").FullName;
        try
        {
            var g = new GameFolder(root);
            var save = new byte[SaveFile.Size];
            save[100] = 7;
            SaveExtras.Write(g, 12, save, 3725.5, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            var notes = SaveExtras.Read(g);
            Assert.Equal(3725.5, SaveExtras.For(notes, 12, save)?.PlaySeconds);
            Assert.True(File.Exists(SaveExtras.PicturePath(g, 12)));
            Assert.Equal(3725.5, SaveExtras.Find(g, save)?.PlaySeconds);   // a loaded save finds its note whatever the slot
            // the original game saving over the file: the note no longer counts
            var other = (byte[])save.Clone();
            other[100] = 8;
            Assert.Null(SaveExtras.For(notes, 12, other));
            Assert.Null(SaveExtras.For(notes, 13, save));
            // a save with no known play time keeps no picture it isn't given
            SaveExtras.Write(g, 12, other, null, null);
            Assert.Null(SaveExtras.For(SaveExtras.Read(g), 12, other)!.PlaySeconds);
            Assert.False(File.Exists(SaveExtras.PicturePath(g, 12)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Play_time_reads_as_hours_minutes_seconds()
    {
        Assert.Equal("02:08:32", Erumani.Ui.Lang.PlayTime(2 * 3600 + 8 * 60 + 32.9));
        Assert.Equal("123:00:05", Erumani.Ui.Lang.PlayTime(123 * 3600 + 5));
        Assert.Equal("--:--:--", Erumani.Ui.Lang.PlayTime(null));
    }

    [Fact]
    public void Window_size_comes_from_the_old_scale_until_one_is_chosen()
    {
        Assert.Equal((1280, 960), new Settings().WindowSize);                     // the default, ×2
        Assert.Equal((1920, 1440), new Settings { WindowScale = 3 }.WindowSize);   // a settings.json from before
        Assert.Equal((1024, 768), new Settings { WindowWidth = 1024, WindowHeight = 768 }.WindowSize);
    }

    [Fact]
    public void Battle_saves_keep_the_camera()
    {
        var test = Folder("えるまに - Save testplay");
        var game = Folder("えるまに");
        if (test is null || game is null || !File.Exists(Path.Combine(test, "SAVE3.DAT"))) return;
        var folder = new GameFolder(game);
        var save = SaveFile.Load(Path.Combine(test, "SAVE3.DAT"))!;
        var state = new GameState();
        save.ApplyTo(state);
        var again = SaveFile.ForBattle(state, Database.Load(folder), save.MapIndex, "", save.Phase, folder.MapPath(save.MapIndex),
            Array.Empty<Srpg95.Rules.BattleUnit>(), save.FiredSlots(), save.Raw, camera: (3, 5));
        Assert.Equal((3, 5), again.CameraTile);   // header 0x008 / 0x00C, as the exe keeps its camera
    }
}
