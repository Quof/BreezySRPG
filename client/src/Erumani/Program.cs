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
///               --event NAME           (run this script on load instead of OPENING, e.g. EV000202)
///               --advance -1           (confirm every message/choice)
///               --level N              (developer: allies start the map at level N or higher)
///               --give "ID:N,.."      (developer: add N of ITEM.DAT item ID (0-based) to the inventory)
///               --place "UNIT:X.Y,.."  (developer: move a unit (UNIT.DAT id) to a tile and keep it shown; :X.Y.HP also sets HP)
///               --saves DIR            (where SAVEn.DAT files are read and written; default: the game folder)
///               --load N               (load SAVEn.DAT from the game folder at start)
///               --camp N               (developer: open the camp before map N, 1-based)
///               --keys "a,b,..."       (with --screenshot: scripted input once the map is idle;
///                                       up/down/left/right/enter/esc, at:X,Y moves the cursor to a tile,
///                                       wait skips a beat, shot+N takes the shot N frames later even mid-animation;
///                                       telop+N / telopK+N wait for the next caption (any, or TELOPK.BMP) and shoot N frames in;
///                                       otherwise the shot is taken once the queue is done and the map is idle)
/// </summary>
public sealed record Options(string GameDir, string? RtpDir, int? StartMap, string? Screenshot, int Advance, string? Event, string? Keys = null, int? Level = null, string? Give = null, string? Place = null, int? LoadSlot = null, int? CampBefore = null, string? SaveDir = null)
{
    public static Options Parse(string[] args)
    {
        string? game = null, rtp = null, shot = null, ev = null, keys = null, give = null, place = null, saves = null;
        int? level = null, loadSlot = null, campBefore = null;
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
                case "--event": ev = args[i + 1]; break;
                case "--keys": keys = args[i + 1]; break;
                case "--level": level = int.Parse(args[i + 1]); break;
                case "--give": give = args[i + 1]; break;
                case "--place": place = args[i + 1]; break;
                case "--load": loadSlot = int.Parse(args[i + 1]); break;
                case "--saves": saves = args[i + 1]; break;
                case "--camp": campBefore = int.Parse(args[i + 1]) - 1; break;
            }
        }
        game ??= FindGameFolder() ?? throw new DirectoryNotFoundException(
            "Game folder not found. Pass --game <folder containing SRPGEXEC.EXE>.");
        rtp ??= FindUp(AppContext.BaseDirectory, Path.Combine("srpg maker 95", "RTP"));
        return new Options(game, rtp, map, shot, advance, ev, keys, level, give, place, loadSlot, campBefore, saves);
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
