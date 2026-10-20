namespace Erumani.Runtime;

/// <summary>Persistent game state (what a save file will hold). Format of SAVEn.DAT pending (T16).</summary>
public sealed class GameState
{
    public const int SwitchCount = 500, VariableCount = 500;

    public bool[] Switches { get; } = new bool[SwitchCount];
    public int[] Variables { get; } = new int[VariableCount];
    public Dictionary<int, int> Items { get; } = new();
    public int Gold { get; set; }
    /// <summary>Unit ids that have joined the player's army.</summary>
    public HashSet<int> Party { get; } = new();
    /// <summary>Per-unit overrides set by events (class change, equipment change).</summary>
    public Dictionary<int, int> ClassOverride { get; } = new();
    public Dictionary<int, int> WeaponOverride { get; } = new();

    /// <summary>The player's army between maps (save-file roster, FORMAT_NOTES §14), by unit id.</summary>
    public Dictionary<int, RosterEntry> Roster { get; } = new();

    /// <summary>Camps reached so far (save header 0x004).</summary>
    public int CampCount { get; set; }
    /// <summary>Units chosen to sortie on the next map (null = everyone in the roster).</summary>
    public HashSet<int>? Sortie { get; set; }
    /// <summary>Bytes of the last save loaded, the template for unknown fields when saving.</summary>
    public byte[]? LastSave { get; set; }

    public int ItemCount(int item) => Items.TryGetValue(item, out var n) ? n : 0;

    /// <summary>A new game.</summary>
    public void Reset()
    {
        Array.Clear(Switches);
        Array.Clear(Variables);
        Items.Clear();
        Gold = 0;
        Party.Clear();
        ClassOverride.Clear();
        WeaponOverride.Clear();
        Roster.Clear();
        CampCount = 0;
        Sortie = null;
        LastSave = null;
    }

    public void AddItem(int item, int count)
    {
        int n = Math.Max(0, ItemCount(item) + count);
        if (n == 0) Items.Remove(item); else Items[item] = n;
    }
}

/// <summary>A party member's persistent state between maps.</summary>
public sealed record RosterEntry(int ClassId, int Level, int Exp, int[] Base, int Weapon, int Armor, int Accessory);

/// <summary>Battle-map state the event conditions can look at.</summary>
public interface IMapContext
{
    /// <summary>Phase counter: ally turn t = 2t-1, enemy turn t = 2t (same encoding as the data).</summary>
    int Phase { get; }
    /// <summary>0 alive, 1 dead, 2 not present, 3 controlled by player, 4 controlled by computer.</summary>
    bool UnitState(int unit, int state);
    void ScrollTo(int x, int y);
    void QueueMove(int unit, IReadOnlyList<byte> steps);
    /// <summary>Starts all queued moves; returns true while they are still animating.</summary>
    void ExecuteMoves();
    bool MovesRunning { get; }
}
