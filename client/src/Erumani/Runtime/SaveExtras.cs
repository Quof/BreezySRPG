using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Srpg95.Data;

namespace Erumani.Runtime;

/// <summary>
/// What EasySRPG95 keeps about a save beyond SAVEn.DAT, none of which SRPG Maker 95 has: the play time, a picture of
/// the screen when it was saved, and for a save made in the middle of an event the rest of that event (<see
/// cref="SceneSave"/>). They live in an "EasySRPG95" folder beside the saves — SAVEn.png and saves.json — so the save
/// files stay exactly the original's. A note counts only while its save file is the one it was written with (the
/// file's hash): a slot the original game saved over since shows none.
/// </summary>
public static class SaveExtras
{
    /// <summary>A save's note: its play time (null when it isn't known), the hash of the file it belongs to, and the event it was saved in.</summary>
    public sealed record Note(double? PlaySeconds, string Hash, SceneSave? Scene = null);

    static string Folder(GameFolder g) => Path.Combine(SaveFile.Directory ?? g.Root, "EasySRPG95");
    public static string PicturePath(GameFolder g, int slot) => Path.Combine(Folder(g), $"SAVE{slot}.png");
    static string IndexPath(GameFolder g) => Path.Combine(Folder(g), "saves.json");

    /// <summary>Indented, the messages of a saved event kept readable (not \u escapes), nothing written for what isn't there.</summary>
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Hash(byte[] save) => Convert.ToHexString(SHA1.HashData(save))[..20];

    /// <summary>Every note, by slot (none when the folder or its index isn't there or can't be read).</summary>
    public static Dictionary<int, Note> Read(GameFolder g)
    {
        try
        {
            if (!File.Exists(IndexPath(g))) return new();
            var raw = JsonSerializer.Deserialize<Dictionary<string, Note>>(File.ReadAllText(IndexPath(g)), Json) ?? new();
            return raw.Where(kv => int.TryParse(kv.Key, out _)).ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        }
        catch (Exception) { return new(); }
    }

    /// <summary>The note for the save now in <paramref name="slot"/>, if it still belongs to it.</summary>
    public static Note? For(Dictionary<int, Note> notes, int slot, byte[] save) =>
        notes.TryGetValue(slot, out var n) && n.Hash == Hash(save) ? n : null;

    /// <summary>The note of whichever slot holds exactly this save (a loaded save's play time, wherever it came from).</summary>
    public static Note? Find(GameFolder g, byte[] save)
    {
        string h = Hash(save);
        return Read(g).Values.FirstOrDefault(n => n.Hash == h);
    }

    /// <summary>Notes the save just written to <paramref name="slot"/>: its play time, its picture (a PNG, or none) and the event it was made in.</summary>
    public static void Write(GameFolder g, int slot, byte[] save, double? playSeconds, byte[]? png, SceneSave? scene = null)
    {
        var notes = Read(g);
        notes[slot] = new Note(playSeconds, Hash(save), scene);
        Directory.CreateDirectory(Folder(g));
        File.WriteAllText(IndexPath(g), JsonSerializer.Serialize(notes.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), Json));
        if (png is not null) File.WriteAllBytes(PicturePath(g, slot), png);
        else if (File.Exists(PicturePath(g, slot))) File.Delete(PicturePath(g, slot));
    }
}
