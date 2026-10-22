namespace Srpg95.Data;

/// <summary>
/// A game folder (the one containing SRPGEXEC.EXE and the .DAT files), with an optional
/// RTP folder as a fallback for graphics, music and sounds the game doesn't ship itself.
/// File lookup is case-insensitive because the original files mix cases (Swname.dat, ...).
/// </summary>
public sealed class GameFolder
{
    public string Root { get; }
    public string? RtpRoot { get; }
    readonly Dictionary<string, Dictionary<string, string>> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public GameFolder(string root, string? rtpRoot = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        Root = root;
        RtpRoot = rtpRoot is not null && Directory.Exists(rtpRoot) ? rtpRoot : null;
    }

    Dictionary<string, string> Listing(string dir)
    {
        if (!_dirs.TryGetValue(dir, out var map))
        {
            map = new(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFileSystemEntries(dir))
                    map.TryAdd(Path.GetFileName(f), f);
            _dirs[dir] = map;
        }
        return map;
    }

    string? FindIn(string root, string relative)
    {
        string dir = root;
        var parts = relative.Split('/', '\\');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!Listing(dir).TryGetValue(parts[i], out var next)) return null;
            dir = next;
        }
        return dir;
    }

    /// <summary>A folder (BGM, EFS…) in the game folder and in the RTP folder, those that exist, the game's first.</summary>
    public List<string> Folders(string name) =>
        new[] { FindIn(Root, name), RtpRoot is null ? null : FindIn(RtpRoot, name) }.OfType<string>().Where(Directory.Exists).ToList();

    /// <summary>Path of a file in the game folder, else the RTP folder, else null.</summary>
    public string? Find(string relative) => FindIn(Root, relative) ?? (RtpRoot is null ? null : FindIn(RtpRoot, relative));

    /// <summary>Reads a data file from the game folder itself (data never falls back to the RTP).</summary>
    public byte[] ReadData(string relative) =>
        File.ReadAllBytes(FindIn(Root, relative) ?? throw new FileNotFoundException($"missing game file {relative}", Path.Combine(Root, relative)));

    public IndexedBitmap? Bitmap(string fileName) => Find("BMP/" + fileName) is { } p ? IndexedBitmap.Load(p) : null;

    public string MapPath(int mapIndex) => FindIn(Root, $"MAP/SMAP_{mapIndex + 1:000}.DAT")
        ?? throw new FileNotFoundException($"map {mapIndex} (SMAP_{mapIndex + 1:000}.DAT) not found");

    public byte[]? Chipset(int tileset) => FindIn(Root, $"MAP/MAPC_{tileset:000}.DAT") is { } p ? File.ReadAllBytes(p) : null;
}
