using Srpg95.Data;

namespace Erumani;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var opts = Options.Parse(args);
            var game = new GameFolder(opts.GameDir, opts.RtpDir);
            using var g = new ErumaniGame(game, opts);
            g.Run();
            return 0;
        }
        catch (Exception e)
        {
            // WinExe has no console: leave the error next to the executable.
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), e.ToString());
            return 1;
        }
    }
}

/// <summary>
/// Command line: --game DIR  --rtp DIR  --map N (1-based, like SMAP_nnn)
///               --screenshot FILE.png  (render one frame at 640x480, save it, exit)
///               --advance N            (with --screenshot: auto-confirm N messages/choices first)
/// </summary>
public sealed record Options(string GameDir, string? RtpDir, int? StartMap, string? Screenshot, int Advance)
{
    public static Options Parse(string[] args)
    {
        string? game = null, rtp = null, shot = null;
        int? map = null;
        int advance = 0;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--game": game = args[i + 1]; break;
                case "--rtp": rtp = args[i + 1]; break;
                case "--map": map = int.Parse(args[i + 1]) - 1; break;
                case "--screenshot": shot = args[i + 1]; break;
                case "--advance": advance = int.Parse(args[i + 1]); break;
            }
        }
        game ??= FindGameFolder() ?? throw new DirectoryNotFoundException(
            "Game folder not found. Pass --game <folder containing SRPGEXEC.EXE>.");
        rtp ??= FindUp(AppContext.BaseDirectory, Path.Combine("srpg maker 95", "RTP"));
        return new Options(game, rtp, map, shot, advance);
    }

    /// <summary>Next to the exe, or in a parent directory (development layout: ./えるまに).</summary>
    static string? FindGameFolder()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "SRPGEXEC.EXE"))) return d.FullName;
            foreach (var sub in d.EnumerateDirectories())
                if (File.Exists(Path.Combine(sub.FullName, "SRPGEXEC.EXE")) && !sub.Name.Contains("Copy") && !sub.Name.Contains("Play"))
                    return sub.FullName;
        }
        return null;
    }

    static string? FindUp(string start, string relative)
    {
        for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }
}
