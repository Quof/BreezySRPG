using MeltySynth;

namespace Srpg95.Data.Tests;

/// <summary>SoundFont MIDI: every BGM file parses, and one renders sound (needs ERUMANI_TEST_SF2 = a .sf2 path).</summary>
public class MidiTests
{
    static string? Game()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "えるまに", "SRPGEXEC.EXE"))) return Path.Combine(d.FullName, "えるまに");
        return null;
    }

    [Fact]
    public void All_game_midis_parse_and_render()
    {
        if (Game() is not { } g) return;
        var mids = Directory.GetFiles(Path.Combine(g, "BGM"), "*.mid", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(mids);
        foreach (var m in mids) Assert.True(new MidiFile(m).Length > TimeSpan.Zero, m);

        if (Environment.GetEnvironmentVariable("ERUMANI_TEST_SF2") is not { } sf2 || !File.Exists(sf2)) return;
        var seq = new MidiFileSequencer(new Synthesizer(sf2, 44100));
        seq.Play(new MidiFile(mids[0]), loop: true);
        var l = new float[44100 * 3];
        var r = new float[44100 * 3];
        seq.Render(l, r);
        Assert.True(l.Max(Math.Abs) > 0.01f, "silent render");
    }

    [Fact]
    public void Built_in_SoundFont_renders_the_soundtrack()
    {
        if (Game() is not { } g) return;
        using var s = typeof(Erumani.Runtime.Audio).Assembly.GetManifestResourceStream("GeneralUser-GS.sf2");
        Assert.NotNull(s);
        var seq = new MidiFileSequencer(new Synthesizer(new SoundFont(s!), 44100));
        foreach (var mid in Directory.GetFiles(Path.Combine(g, "BGM"), "*.mid").Take(5))
        {
            seq.Play(new MidiFile(mid), loop: true);
            var l = new float[44100 * 4];
            var r = new float[44100 * 4];
            seq.Render(l, r);
            Assert.True(l.Max(Math.Abs) > 0.01f, $"silent: {Path.GetFileName(mid)}");
        }
    }

    static IEnumerable<string> GameMidis()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "えるまに", "SRPGEXEC.EXE")))
                return Directory.GetDirectories(d.FullName).Where(g => Directory.Exists(Path.Combine(g, "BGM")) && !g.Contains("scan", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(g => Directory.GetFiles(Path.Combine(g, "BGM"), "*.mid")).ToList();
        return Array.Empty<string>();
    }

    [Fact]
    public void Silence_at_a_tracks_ends_is_trimmed_away()
    {
        // ＯtoＺ map 3's legend.mid: 1.3 s before its first note, 26.5 s after its last
        var legend = GameMidis().FirstOrDefault(f => Path.GetFileName(f) == "legend.mid");
        if (legend is null) return;
        var bytes = File.ReadAllBytes(legend);
        var (lead, tail, notes) = MidiTrim.Silence(bytes)!.Value;
        Assert.True(tail > 20);
        var trimmed = MidiTrim.Trim(bytes);
        var (lead2, tail2, notes2) = MidiTrim.Silence(trimmed)!.Value;
        Assert.Equal(notes, notes2);
        Assert.Equal(0, lead2, 2);   // it starts on its first note
        Assert.Equal(0, tail2, 2);   // and ends on its last
        double before = new MidiFile(new MemoryStream(bytes)).Length.TotalSeconds, after = new MidiFile(new MemoryStream(trimmed)).Length.TotalSeconds;
        Assert.Equal(before - lead - tail, after, 1);
        // a limit keeps that much of the two together, the start's first
        var kept = MidiTrim.Silence(MidiTrim.Trim(bytes, 3))!.Value;
        Assert.Equal(lead, kept.Lead, 2);
        Assert.Equal(3 - lead, kept.Tail, 1);
    }

    [Fact]
    public void Every_games_midis_keep_their_notes_and_lose_their_silence()
    {
        int checkedFiles = 0;
        foreach (var f in GameMidis())
        {
            var bytes = File.ReadAllBytes(f);
            if (MidiTrim.Silence(bytes) is not { } s) continue;
            var trimmed = MidiTrim.Trim(bytes);
            var t = MidiTrim.Silence(trimmed)!.Value;
            Assert.Equal(s.Notes, t.Notes);
            Assert.True(t.Lead + t.Tail <= MidiTrim.MaxSilenceSeconds + 0.05 || trimmed == bytes, $"{f}: {t.Lead:F1} + {t.Tail:F1} s");
            if (s.Lead + s.Tail <= MidiTrim.MaxSilenceSeconds) Assert.Same(bytes, trimmed);   // nothing to trim: as it is
            Assert.True(new MidiFile(new MemoryStream(trimmed)).Length > TimeSpan.Zero, f);
            checkedFiles++;
        }
        if (checkedFiles > 0) Assert.True(checkedFiles > 50);
    }

    [Fact]
    public void An_RPG_Maker_loop_marker_is_found_before_the_last_note_only()
    {
        var mids = GameMidis().ToList();
        // ＯtoＺ's 2003繰り返される戦い.mid loops from 13.9 s (controller 111); trimmed, as much earlier as its lead-in was
        if (mids.FirstOrDefault(f => f.EndsWith("2003繰り返される戦い.mid") && f.Contains("ＯtoＺ")) is { } battle)
        {
            var bytes = File.ReadAllBytes(battle);
            var lead = MidiTrim.Silence(bytes)!.Value.Lead;
            Assert.Equal(13.9, MidiTrim.LoopStart(bytes)!.Value, 1);
            Assert.Equal(13.9 - lead, MidiTrim.LoopStart(MidiTrim.Trim(bytes))!.Value, 1);
            // MeltySynth reads it for its RPG Maker loop
            Assert.True(new MidiFile(new MemoryStream(MidiTrim.Trim(bytes)), MidiFileLoopType.RpgMaker).Length > TimeSpan.Zero);
        }
        // えるまに's ミッションクリアー.MID has its marker at its very end: a fanfare that loops nothing
        if (mids.FirstOrDefault(f => Path.GetFileName(f) == "ミッションクリアー.MID") is { } fanfare)
            Assert.Null(MidiTrim.LoopStart(File.ReadAllBytes(fanfare)));
    }
}