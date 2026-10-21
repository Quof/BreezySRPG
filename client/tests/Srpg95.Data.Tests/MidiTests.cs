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
}