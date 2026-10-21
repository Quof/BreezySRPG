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

    static MessageNode Say(string s) => new(2, new List<string> { s });

    /// <summary>What a runner shows now: a message's lines, a choice and its option, "…" while it waits, "end".</summary>
    static string Shown(EventRunner r) => r.Message is { } m ? string.Join("/", m.Lines)
        : r.Choice is not null ? $"choice {r.ChoiceIndex}" : r.Running ? "…" : "end";

    /// <summary>The player's next step: the second option at a choice, a click otherwise.</summary>
    static void Next(EventRunner r)
    {
        if (r.Choice is not null) r.HoverChoice(1);
        r.Update(new EventInput(true, false, false, false, false));
    }

    [Fact]
    public void An_event_saved_halfway_goes_on_from_the_same_place()
    {
        // A; if switch 0 { B with a choice under it: X → C, Y → D, D2; E; a wait }; F
        var tree = new List<ScriptNode>
        {
            Say("A"),
            new BranchNode(new Command(Op.ConditionalBranch, 0, new byte[20]), new List<ScriptNode>
            {
                Say("B"),
                new ChoiceNode(new Command(Op.CharacterChoice, 1, new byte[2]), new[] { "X", "Y" },
                    new List<List<ScriptNode>> { new() { Say("C") }, new() { Say("D"), Say("D2") } }),
                Say("E"),
                new CommandNode(new Command(Op.Wait, 1, new byte[] { 2, 0 })),
            }),
            Say("F"),
        };
        var script = new EventScript { Name = "EV000101", Comment = "", Commands = new(), Tree = tree, RoundTrips = true };
        var state = new GameState();
        state.Switches[0] = true;
        EventRunner NewRunner() => new(state, new CompatibilityTests.NoMap(), new Audio(new GameFolder(Path.GetTempPath())), new ScreenEffects());
        static List<string> Rest(EventRunner r)
        {
            var seen = new List<string> { Shown(r) };
            for (int i = 0; i < 200 && r.Running; i++) { Next(r); seen.Add(Shown(r)); }
            return seen;
        }

        var live = NewRunner();
        live.Start(script);
        live.Update(default);
        var saves = new List<RunnerSave>();
        var trace = new List<string>();
        while (live.Running)
        {
            // every point the event stops at, through JSON as it goes into the save's note
            saves.Add(System.Text.Json.JsonSerializer.Deserialize<RunnerSave>(System.Text.Json.JsonSerializer.Serialize(live.Save()))!);
            trace.Add(Shown(live));
            Next(live);
        }
        trace.Add(Shown(live));
        Assert.Equal(new[] { "A", "choice 0", "D", "D2", "E" }, trace.Where(s => s != "…").Take(5));
        Assert.Equal("F", trace.Where(s => s != "…").ElementAt(5));
        for (int k = 0; k < saves.Count; k++)
        {
            var loaded = NewRunner();
            Assert.True(loaded.Restore(script, saves[k]));
            Assert.Equal(trace.Skip(k), Rest(loaded));
        }
        // a save that names a block the script doesn't have puts nothing in motion
        Assert.False(NewRunner().Restore(script, saves[2] with { Frames = new() { new[] { 0, 2 }, new[] { 5, 0 } } }));
    }

    [Fact]
    public void A_save_made_in_an_event_keeps_the_event_in_its_note()
    {
        var root = Directory.CreateTempSubdirectory("easysrpg95-scene").FullName;
        try
        {
            var g = new GameFolder(root);
            var save = new byte[SaveFile.Size];
            var scene = new SceneSave
            {
                Event = new RunnerSave("EV000301", new() { new[] { 0, 6 }, new[] { -1, 2 } }, 2, false, false,
                    new MessageView(2, new[] { "ジュナ：", "・・・・・・" }, 2, false, false), null, false, 0, 0, false),
                Map = new MapSceneSave(false, new FightSave("r3", "r2", -1, new ResultSave(
                        new() { new StrikeSave("r3", "r2", true, false, 6, false, 29, 6) }, new(), new() { ["r3"] = 16 }, new(), 0, new(), new(), null)),
                    false, null, null, null, 1, 0, new(), new(), null, 7, 11, new() { new UnitSceneSave("r1", 3, -1, new[] { 1, 1, 0 }) }),
                Brightness = 0,
                Pictures = new() { new PictureSave("BACK_001.BMP", 8, false) },
                Bgm = "安らぎ.MID",
            };
            SaveExtras.Write(g, 9, save, 61, null, scene);
            var back = SaveExtras.For(SaveExtras.Read(g), 9, save)!.Scene!;
            Assert.Equal("EV000301", back.Event.Script);
            Assert.Equal(new[] { -1, 2 }, back.Event.Frames[1]);
            Assert.Equal(new[] { "ジュナ：", "・・・・・・" }, back.Event.Message!.Lines);
            Assert.Equal(29, back.Map.Fight!.Result.Strikes[0].HpAfter);
            Assert.Equal(16, back.Map.Fight.Result.Exp["r3"]);
            Assert.Equal(new[] { 1, 1, 0 }, back.Map.Units[0].Pending);
            Assert.Equal((0, "BACK_001.BMP", "安らぎ.MID"), (back.Brightness, back.Pictures[0].File, back.Bgm));
            // a save written over by the original game: its note, the event with it, no longer counts
            var other = (byte[])save.Clone();
            other[0x20C] = 3;
            Assert.Null(SaveExtras.For(SaveExtras.Read(g), 9, other));
        }
        finally { Directory.Delete(root, recursive: true); }
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
