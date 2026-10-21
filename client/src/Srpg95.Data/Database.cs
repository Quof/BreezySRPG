namespace Srpg95.Data;

// Record layouts mirror re/srpg95_data.py and re/FORMAT_NOTES.md (§2-§8, §12).
// IDs are 0-based record indices; the editor shows them +1.

public sealed record Unit(
    int Index, string Name, int Face, int ClassId, int Level, int Exp,
    int[] BaseStats, int[] GrowthPct, int Weapon, int Armor, int Accessory, string DeathMessage)
{
    // BaseStats / GrowthPct order: HP, MP, Str, Mag, Skl, Sta
    internal static Unit Parse(int i, ReadOnlySpan<byte> r) => new(
        i, Bin.Str(r, 0x00, 32), Bin.I32(r, 0x20), Bin.I32(r, 0x24), Bin.I32(r, 0x28), Bin.I32(r, 0x2c),
        Bin.I32s(r, 0x30, 6), Bin.I32s(r, 0x48, 6), Bin.I32(r, 0x60), Bin.I32(r, 0x64), Bin.I32(r, 0x68),
        Bin.Str(r, 0x6c, 0x1e0 - 0x6c));
}

public sealed record ClassDef(
    int Index, string Name, int Movement, int MoveType, int[] TerrainMoveOverride, int UnitGraphic,
    int AnimWeaponA, int AnimWeaponB, int AnimWeaponC, int AnimNoWeapon, int AnimMagicEnemy, int AnimMagicAlly,
    int[] InitStats, int[] SkillLevels, int[] SkillIds, byte[] Weakness, byte[] Resist)
{
    // MoveType 0 walk, 1 fly, 2 special; InitStats: Str, Mag, Skl, Sta; Resist: poison, sleep, atk down, def down
    internal static ClassDef Parse(int i, ReadOnlySpan<byte> r) => new(
        i, Bin.Str(r, 0, 32), Bin.I32(r, 0x20), Bin.I32(r, 0x24), Bin.I32s(r, 0x28, 70), Bin.I32(r, 0x140),
        Bin.I32(r, 0x144), Bin.I32(r, 0x148), Bin.I32(r, 0x14c), Bin.I32(r, 0x150), Bin.I32(r, 0x154), Bin.I32(r, 0x158),
        Bin.I32s(r, 0x1a4, 4), Bin.I32s(r, 0x1b4, 20), Bin.I32s(r, 0x204, 20), r.Slice(0x254, 6).ToArray(), r.Slice(0x25c, 4).ToArray());
}

public enum ItemType { Event, Weapon, Armor, Accessory, Medicine, StatUp, ClassChange, Magic }

public sealed record Item(int Index, string Name, ItemType Type, int Price, string Desc, byte[] ClassUsable, ItemStats S)
{
    public static readonly (int Min, int Max)[] WeaponRanges = { (1, 1), (1, 2), (1, 3), (2, 2), (2, 3), (2, 4), (2, 5), (3, 10) };
    /// <summary>A medicine's reach by its range type (FUN_00411ba0's tables at 0x43323c / 0x43325c): 0 the user alone.</summary>
    public static readonly (int Min, int Max)[] MedicineRanges = { (0, 0), (0, 1), (0, 2), (1, 1), (1, 2), (1, 3), (1, 4), (2, 9) };

    public static Item Parse(int i, ReadOnlySpan<byte> r) => new(
        i, Bin.Str(r, 0, 32), (ItemType)Bin.I32(r, 0x20), Bin.I32(r, 0x24), Bin.Str(r, 0x28, 0x50),
        r.Slice(0x78, 200).ToArray(), ItemStats.Parse(r));
}

/// <summary>All type-specific blocks; only the block matching Item.Type is meaningful.</summary>
public sealed record ItemStats(
    int WpnRange, int WpnPower, int WpnUsability, int WpnCritical, int WpnAnimation, byte[] WpnAttackAttr,
    int ArmDefense, int ArmMagicDefense, int ArmCritAvoid, int ArmWeight, byte[] ArmDefenseAttr,
    int AccStr, int AccMag, int AccSkl, int AccSta, int AccMove, int AccGrowthBonus, byte[] AccSafe,
    int MedHp, int MedMp, int MedDefUp, int MedMdefUp, byte[] MedCure, int MedRange,
    int[] StatUp, int[] ClassChange, int ClassChangeLevel, int MagicSpell)
{
    public static ItemStats Parse(ReadOnlySpan<byte> r) => new(
        Bin.I32(r, 0x140), Bin.I32(r, 0x144), Bin.I32(r, 0x148), Bin.I32(r, 0x14c), Bin.I32(r, 0x150), r.Slice(0x154, 6).ToArray(),
        Bin.I32(r, 0x15c), Bin.I32(r, 0x160), Bin.I32(r, 0x164), Bin.I32(r, 0x168), r.Slice(0x16c, 6).ToArray(),
        Bin.I32(r, 0x174), Bin.I32(r, 0x178), Bin.I32(r, 0x17c), Bin.I32(r, 0x180), Bin.I32(r, 0x184), Bin.I32(r, 0x188), r.Slice(0x25c, 4).ToArray(),
        Bin.I32(r, 0x18c), Bin.I32(r, 0x190), Bin.I32(r, 0x194), Bin.I32(r, 0x198), r.Slice(0x254, 4).ToArray(), Bin.I32(r, 0x268),
        Bin.I32s(r, 0x19c, 6), Bin.I32s(r, 0x1b4, 40), Bin.I32(r, 0x264), Bin.I32(r, 0x26c));
}

public sealed record Magic(
    int Index, string Name, int Type, int MpCost, string Desc, int Damage, int Accuracy, byte[] AttackAttr,
    int Animation, int ExpValue, int DegradeEffect, int Target, int Value, int Relation, int RaiseEffect,
    bool RangeWholeMap, bool EffectWholeMap, bool[,] RangeGrid, bool[,] EffectGrid)
{
    /// <summary>ANIME.DAT index of this spell's animation.</summary>
    public int AnimeIndex => 80 + Animation;

    internal static Magic Parse(int i, ReadOnlySpan<byte> r) => new(
        i, Bin.Str(r, 0, 32), Bin.I32(r, 0x20), Bin.I32(r, 0x24), Bin.Str(r, 0x28, 0x50), Bin.I32(r, 0x78), Bin.I32(r, 0x7c),
        r.Slice(0x88, 6).ToArray(), Bin.I32(r, 0x90), Bin.I32(r, 0x94), Bin.I32(r, 0x98), Bin.I32(r, 0x9c), Bin.I32(r, 0xa0),
        Bin.I32(r, 0xa4), Bin.I32(r, 0xa8), Bin.I32(r, 0xe8) != 0, Bin.I32(r, 0xec) != 0, Grid(r, 0xf0), Grid(r, 0x144));

    /// <summary>21x21 [row, col], [10,10] = caster; col c is bit 31-c (bit 21 = centre).</summary>
    static bool[,] Grid(ReadOnlySpan<byte> r, int off)
    {
        var g = new bool[21, 21];
        for (int row = 0; row < 21; row++)
        {
            uint v = (uint)Bin.I32(r, off + row * 4);
            for (int c = 0; c < 21; c++) g[row, c] = ((v >> (31 - c)) & 1) != 0;
        }
        return g;
    }
}

public sealed record Terrain(int Index, string Name, int DefenseBonus, int HpRecovery, int[] MoveCost, int BattleBackground)
{
    // MoveCost indexed by ClassDef.MoveType; 0 = impassable
    internal static Terrain Parse(int i, ReadOnlySpan<byte> r) => new(
        i, Bin.Str(r, 0, 32), Bin.I32(r, 0x20), Bin.I32(r, 0x24), Bin.I32s(r, 0x28, 3), Bin.I32(r, 0x34));
}

public readonly record struct AnimCell(short X, short Y, short Cell);

public sealed record Animation(
    int Index, string Name, int PreviewGraphic, AnimCell[][] Frames, int FrameCount, int HitFrame, int FlashTime,
    string HitSound, string MissSound, int HitSoundFrame, int MissSoundFrame)
{
    public bool IsSpell => Index >= 80;

    internal static Animation Parse(int i, ReadOnlySpan<byte> r)
    {
        var frames = new AnimCell[90][];
        for (int f = 0; f < 90; f++)
        {
            frames[f] = new AnimCell[4];
            for (int l = 0; l < 4; l++)
            {
                int o = 0x28 + f * 24 + l * 6;
                frames[f][l] = new AnimCell(Bin.I16(r, o), Bin.I16(r, o + 2), Bin.I16(r, o + 4));
            }
        }
        return new(i, Bin.Str(r, 0, 32), Bin.I32(r, 0x24), frames, Bin.I32(r, 0x898), Bin.I32(r, 0x89c), Bin.I32(r, 0x8a0),
            Bin.Str(r, 0x8c0, 0x30), Bin.Str(r, 0x8f0, 0x30), Bin.I32(r, 0x920), Bin.I32(r, 0x924));
    }
}

/// <summary>All game-wide databases of one game folder.</summary>
public sealed class Database
{
    public required Unit[] Units { get; init; }
    public required ClassDef[] Classes { get; init; }
    public required Item[] Items { get; init; }
    public required Magic[] Magic { get; init; }
    public required Terrain[] Terrains { get; init; }
    public required Animation[] Animations { get; init; }
    public required string[] Words { get; init; }
    public required string[] MapNames { get; init; }
    public required string[] VariableNames { get; init; }
    public required string[] SwitchNames { get; init; }
    /// <summary>GAME.DAT [0]: 0-based index of the first map (confirmed by SRPGEXEC.EXE).</summary>
    public required int StartMap { get; init; }
    /// <summary>GAME.DAT [1]: battle background from both combatants' terrain (else the defender's).</summary>
    public bool SplitBackgrounds { get; init; }
    /// <summary>GAME.DAT [2]: defenders counter-attack.</summary>
    public bool Counterattacks { get; init; } = true;
    /// <summary>GAME.DAT [3]: an attacker 3+ speed faster strikes twice (off in えるまに).</summary>
    public bool DoubleAttacks { get; init; }
    /// <summary>GAME.DAT [4]: battle animations play slower by this many percent (the tick is 33 + 33·[4]/100 ms; 0 in えるまに).</summary>
    public int SceneSlowdown { get; init; }

    delegate T RecordParser<T>(int index, ReadOnlySpan<byte> record);

    public static Database Load(GameFolder g)
    {
        static T[] Table<T>(byte[] d, int size, int count, string what, RecordParser<T> parse)
        {
            var recs = Bin.Records(d, size, count, what);
            var r = new T[count];
            for (int i = 0; i < count; i++) r[i] = parse(i, recs[i].Span);
            return r;
        }
        static string[] Names(byte[] d) => Enumerable.Range(0, d.Length / 32).Select(i => Bin.Str(d, i * 32, 32)).ToArray();

        return new Database
        {
            Units = Table(g.ReadData("UNIT.DAT"), 480, 250, "UNIT.DAT", Unit.Parse),
            Classes = Table(g.ReadData("CLASS.DAT"), 640, 150, "CLASS.DAT", ClassDef.Parse),
            Items = Table(g.ReadData("ITEM.DAT"), 640, 100, "ITEM.DAT", Item.Parse),
            Magic = Table(g.ReadData("MAGIC.DAT"), 480, 100, "MAGIC.DAT", Data.Magic.Parse),
            Terrains = Table(g.ReadData("GEOLOGY.DAT"), 60, 70, "GEOLOGY.DAT", Terrain.Parse),
            Animations = Table(g.ReadData("ANIME.DAT"), 2400, 160, "ANIME.DAT", Animation.Parse),
            Words = Names(g.ReadData("WORD.DAT")),
            MapNames = Names(g.ReadData("MAPNAME.DAT")),
            VariableNames = Names(g.ReadData("VARNAME.DAT")),
            SwitchNames = Names(g.ReadData("SWNAME.DAT")),
            StartMap = Bin.I32(g.ReadData("GAME.DAT"), 0),
            SplitBackgrounds = Bin.I32(g.ReadData("GAME.DAT"), 4) == 1,
            Counterattacks = Bin.I32(g.ReadData("GAME.DAT"), 8) != 0,
            DoubleAttacks = Bin.I32(g.ReadData("GAME.DAT"), 12) != 0,
            SceneSlowdown = Bin.I32(g.ReadData("GAME.DAT"), 16),
        };
    }
}
