namespace Srpg95.Data;

// SMAP_nnn.DAT layout: re/FORMAT_NOTES.md §10-§11.

public enum ObjectType { Enemy, Ally, Neutral, Other }

public readonly record struct MovePattern(short Switch, short Action, short X, short Y, short SwitchToApply);

public readonly record struct EventSlot(short Condition, short Switch, short Turn, short TargetUnit)
{
    // 0 do-not-perform, 1 switch, 2 turn, 4 first contact (any time), 5 when unit dies,
    // 6 passes by, 7 stops on, 8 victory, 9 first contact (after move), 10/11 first attack (before/after)
    public bool IsEnemyTurn => Turn % 2 == 0;
    public int TurnNumber => (Turn + 1) / 2;
}

public sealed record MapObject(
    int Index, bool InUse, int X, int Y, ObjectType Type, short SwitchCondition, int Unit, int Graphic,
    MovePattern[] MovePatterns, bool Leader, bool Guest, int RewardItem, int RewardGold, EventSlot[] Slots, bool Unattackable)
{
    internal static MapObject Parse(int i, ReadOnlySpan<byte> r)
    {
        var mp = new MovePattern[4];
        for (int k = 0; k < 4; k++)
        {
            int o = 0x1c + k * 10;
            mp[k] = new(Bin.I16(r, o), Bin.I16(r, o + 2), Bin.I16(r, o + 4), Bin.I16(r, o + 6), Bin.I16(r, o + 8));
        }
        var sl = new EventSlot[5];
        for (int k = 0; k < 5; k++)
        {
            int o = 0x54 + k * 8;
            sl[k] = new(Bin.I16(r, o), Bin.I16(r, o + 2), Bin.I16(r, o + 4), Bin.I16(r, o + 6));
        }
        return new(i, Bin.I32(r, 0) != 0, Bin.I32(r, 4), Bin.I32(r, 8), (ObjectType)Bin.I32(r, 0xc), Bin.I16(r, 0x10),
            Bin.I32(r, 0x14), Bin.I32(r, 0x18), mp, Bin.I32(r, 0x44) != 0, Bin.I32(r, 0x48) != 0, Bin.I32(r, 0x4c),
            Bin.I32(r, 0x50), sl, Bin.I32(r, 0xa0) != 0);
    }

    public static string ScriptName(int objectIndex, int slot1Based) => $"EV{objectIndex:0000}{slot1Based:00}";
}

public sealed class MapData
{
    public const int MaxSize = 40;
    const int TilesOff = 0x4b0, ObjOff = 0x1130, FatOff = 0x5140, DirOff = 0x9f60, BlockOff = 0x9f60 + 5000 * 16;

    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Tileset { get; init; }
    public required int WinCondition { get; init; }          // 0 defeat all, 1 leader, 2 event
    /// <summary>camp, player turn, enemy turn, player battle, enemy battle, boss battle, victory, game over</summary>
    public required string[] Bgm { get; init; }
    public required bool CampBeforeBattle { get; init; }
    public required int[] ShopItems { get; init; }
    public required (int X, int Y)[] StartPositions { get; init; }
    public required int[] CharacterList { get; init; }
    public required int CampBackground { get; init; }
    public required bool DisablePartyChanges { get; init; }
    /// <summary>[y, x] chip index into MAPC_tileset.BMP (12 chips per row).</summary>
    public required ushort[,] Tiles { get; init; }
    public required MapObject[] Objects { get; init; }
    public required Dictionary<string, EventScript> Scripts { get; init; }

    public static MapData Load(string path) => Parse(File.ReadAllBytes(path));

    public static MapData Parse(byte[] d)
    {
        int w = Bin.I32(d, 0x20), h = Bin.I32(d, 0x24);
        var tiles = new ushort[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tiles[y, x] = Bin.U16(d, TilesOff + (y * MaxSize + x) * 2);

        var objects = new List<MapObject>();
        for (int i = 0; i < 100; i++)
        {
            var rec = d.AsSpan(ObjOff + i * 164, 164);
            if (rec.ContainsAnyExcept((byte)0)) objects.Add(MapObject.Parse(i, rec));
        }

        int nStart = Bin.I32(d, 0x348), nChars = Bin.I32(d, 0x43c), nShop = Bin.I32(d, 0x344);
        var starts = new (int, int)[Math.Clamp(nStart, 0, 20)];
        for (int i = 0; i < starts.Length; i++) starts[i] = (Bin.I32(d, 0x34c + i * 4), Bin.I32(d, 0x39c + i * 4));

        return new MapData
        {
            Name = Bin.Str(d, 0, 32),
            Width = w,
            Height = h,
            Tileset = Bin.I32(d, 0x28),
            WinCondition = Bin.I32(d, 0x2c),
            Bgm = Enumerable.Range(0, 8).Select(i => Bin.Str(d, 0x30 + i * 0x30, 0x30)).ToArray(),
            CampBeforeBattle = Bin.I32(d, 0x1b0) != 0,
            ShopItems = Bin.I32s(d, 0x1b4, Math.Clamp(nShop, 0, 100)),
            StartPositions = starts,
            CharacterList = Bin.I32s(d, 0x3ec, Math.Clamp(nChars, 0, 20)),
            CampBackground = Bin.I32(d, 0x440),
            DisablePartyChanges = Bin.I32(d, 0x450) != 0,
            Tiles = tiles,
            Objects = objects.ToArray(),
            Scripts = ReadScripts(d),
        };
    }

    /// <summary>The map's internal file system: FAT of block chains + 5000-entry directory + 100-byte blocks.</summary>
    static Dictionary<string, EventScript> ReadScripts(byte[] d)
    {
        var result = new Dictionary<string, EventScript>();
        int nBlocks = Math.Max(0, (d.Length - BlockOff) / 100);
        for (int i = 0; i < 5000; i++)
        {
            var e = d.AsSpan(DirOff + i * 16, 16);
            if (!e.ContainsAnyExcept((byte)0)) continue;
            string name = Bin.Str(e, 0, 8);
            int first = Bin.I32(e, 8), size = Bin.I32(e, 12);
            var buf = new List<byte>(Math.Max(0, size));
            int k = first, guard = 0;
            bool missing = false;
            while (k >= 0 && guard++ < 5000)
            {
                if (k < nBlocks) buf.AddRange(d.AsSpan(BlockOff + k * 100, 100).ToArray());
                else { missing = true; buf.AddRange(new byte[100]); }
                k = Bin.I32(d, FatOff + k * 4);
            }
            var file = buf.Take(Math.Max(0, size)).ToArray();
            result[name] = EventScript.Parse(name, file, missing);
        }
        return result;
    }
}

public static class Chipset
{
    /// <summary>MAPC_nnn.DAT: GEOLOGY (terrain) id for each of the 384 chips of MAPC_nnn.BMP.</summary>
    public static byte[] ParseTerrain(byte[] data) =>
        data.Length == 384 ? data : throw new InvalidDataException($"MAPC: expected 384 bytes, got {data.Length}");

    public const int ChipSize = 32, ChipsPerRow = 12;
}
