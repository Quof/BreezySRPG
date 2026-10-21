namespace Erumani.Runtime;

/// <summary>Persistent game state (what a save file will hold). Format of SAVEn.DAT pending (T16).</summary>
public sealed class GameState
{
    public const int SwitchCount = 500, VariableCount = 500;

    public bool[] Switches { get; } = new bool[SwitchCount];
    public int[] Variables { get; } = new int[VariableCount];
    public Dictionary<int, int> Items { get; } = new();
    public int Gold { get; set; }

    /// <summary>
    /// The player's army between maps (save-file roster, FORMAT_NOTES §14), by unit id. The original sorts it by
    /// unit id at every map's end (FUN_0041d5e4), so the camp and the deployment follow unit-id order.
    /// </summary>
    public SortedDictionary<int, RosterEntry> Roster { get; } = new();

    /// <summary>The map being played or the camp's map, 0-based (save header 0x004).</summary>
    public int MapIndex { get; set; }
    /// <summary>The map that comes next, 0-based (save header 0x010): MapIndex + 1 unless a "Next Map" command set it.</summary>
    public int NextMap { get; set; }
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
        Roster.Clear();
        MapIndex = 0;
        NextMap = 1;
        LastSave = null;
    }

    /// <summary>FUN_0040add8 / FUN_0040ae68: at most 99 of an item and 100 different items; −1 is no item.</summary>
    public void AddItem(int item, int count)
    {
        if (item < 0) return;
        if (!Items.ContainsKey(item) && count > 0 && Items.Count >= 100) return;
        int n = Math.Clamp(ItemCount(item) + count, 0, 99);
        if (n == 0) Items.Remove(item); else Items[item] = n;
    }
}

/// <summary>A party member's persistent state between maps.</summary>
public sealed record RosterEntry(int ClassId, int Level, int Exp, int[] Base, int Weapon, int Armor, int Accessory)
{
    /// <summary>出撃: goes onto the next map's start positions (record [13]).</summary>
    public bool Sortie { get; init; } = true;
}

/// <summary>Battle-map state the event conditions can look at.</summary>
public interface IMapContext
{
    /// <summary>Phase counter: ally turn t = 2t-1, enemy turn t = 2t (same encoding as the data).</summary>
    int Phase { get; }
    /// <summary>0 alive, 1 dead, 2 not present, 3 controlled by player, 4 controlled by computer.</summary>
    bool UnitState(int unit, int state);
    /// <summary>Scrolls (smoothly) until the tile is in the view's middle box; <see cref="Panning"/> while it moves.</summary>
    void ShowTile(int x, int y);
    bool Panning { get; }
    void QueueMove(int unit, IReadOnlyList<byte> steps);
    /// <summary>Starts all queued moves; <see cref="MovesRunning"/> while they are still animating.</summary>
    void ExecuteMoves();
    bool MovesRunning { get; }
    /// <summary>Puts every moving unit at the end of its moves at once (cutscene skip).</summary>
    void FinishMoves();
    /// <summary>A switch changed: units appear and leave by their objects' switch conditions (FUN_00409658).</summary>
    void SwitchesChanged();
    /// <summary>"Party possesses item" (FUN_00424e70): in the inventory, or equipped by a player-side unit on the map.</summary>
    bool PartyHas(int item);
    /// <summary>Event party commands, acting on the roster and on units of that id on the map (FORMAT_NOTES §16).</summary>
    void PartyAdd(int unit);
    void PartyRemove(int unit);
    void ChangeClass(int unit, int cls);
    void ChangeEquipment(int unit, int item);
}
