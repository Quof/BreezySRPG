using Srpg95.Data;

namespace Srpg95.Rules;

public enum Side { Ally, Enemy, Neutral }

/// <summary>A unit fighting on a battle map (state mirrors a save-file unit record, FORMAT_NOTES §14).</summary>
public sealed class BattleUnit
{
    public required int Unit { get; init; }
    public required Side Side { get; set; }
    /// <summary>SMAP object index (-1 = deployed from the roster onto a start position).</summary>
    public int MapObject { get; init; } = -1;
    public int ClassId { get; set; }
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    /// <summary>Max HP, max MP, Str, Mag, Skl, Sta without equipment.</summary>
    public int[] Base { get; init; } = new int[6];
    public int[] Growth { get; init; } = new int[6];
    public int Hp { get; set; }
    public int Mp { get; set; }
    public int Weapon { get; set; } = -1;
    public int Armor { get; set; } = -1;
    public int Accessory { get; set; } = -1;
    public int X { get; set; }
    public int Y { get; set; }
    public int Facing { get; set; }
    public bool Acted { get; set; }
    public bool Dead { get; set; }
    /// <summary>Not on the map right now (its object's switch condition is OFF).</summary>
    public bool Hidden { get; set; }
    public bool Leader { get; init; }
    public bool Guest { get; init; }
    public bool Unattackable { get; init; }
    public int PowerUp { get; set; }
    public int DefUp { get; set; }
    public int MdefUp { get; set; }
    public int PoisonTurns { get; set; }
    public int SleepTurns { get; set; }
    public MovePattern[] MovePatterns { get; init; } = Array.Empty<MovePattern>();
    public string Name { get; init; } = "";

    public int MaxHp => Base[0];
    public int MaxMp => Base[1];
}

/// <summary>Derived values for a unit, computed against the game databases.</summary>
public readonly record struct UnitStats(
    int Str, int Mag, int Skl, int Sta, int Movement, int MoveType,
    int Attack, int Speed, int Defense, int MagicDefense, int RangeMin, int RangeMax);
