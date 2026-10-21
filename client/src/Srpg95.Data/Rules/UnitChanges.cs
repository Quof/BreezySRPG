using Srpg95.Data;

namespace Srpg95.Rules;

/// <summary>Changes to a unit's class, stats and equipment, as SRPGEXEC.EXE makes them (FORMAT_NOTES §16).</summary>
public static class UnitChanges
{
    /// <summary>Whether the class may use the item (ITEM.DAT class_usable).</summary>
    public static bool Usable(Item it, int classId) =>
        classId >= 0 && classId < it.ClassUsable.Length && it.ClassUsable[classId] != 0;

    /// <summary>Equipment slot of an item: 0 weapon, 1 armor, 2 accessory, -1 not equipment (item type − 1).</summary>
    public static int Slot(Item it) => it.Type is ItemType.Weapon or ItemType.Armor or ItemType.Accessory ? (int)it.Type - 1 : -1;

    /// <summary>
    /// FUN_0040bb0c: a class change moves Str, Mag, Skl and Sta by the difference of the two classes' initial stats
    /// (CLASS.DAT +0x1A4), each kept within 1–30; HP and MP don't change. Returns the new base stats.
    /// </summary>
    public static int[] ClassChangeStats(int[] baseStats, ClassDef from, ClassDef to)
    {
        var b = (int[])baseStats.Clone();
        for (int i = 0; i < 4; i++)
            b[2 + i] = Math.Clamp(b[2 + i] + to.InitStats[i] - from.InitStats[i], 1, 30);
        return b;
    }

    /// <summary>
    /// FUN_0040b8f0: a stat-up item adds its positive values; max HP and MP stop at 60, the other stats at 30.
    /// Returns the new base stats and what was actually gained.
    /// </summary>
    public static (int[] Base, int[] Gained) StatUp(int[] baseStats, int[] up)
    {
        var b = (int[])baseStats.Clone();
        var gained = new int[6];
        for (int i = 0; i < 6 && i < up.Length; i++)
        {
            if (up[i] <= 0) continue;
            int cap = i < 2 ? 60 : 30;
            int nb = Math.Min(cap, b[i] + up[i]);
            gained[i] = Math.Max(0, nb - b[i]);
            b[i] = Math.Max(b[i], nb);
        }
        return (b, gained);
    }

    /// <summary>The class a class-change item turns <paramref name="from"/> into, or −1 (ITEM.DAT 20 from/to pairs).</summary>
    public static int ClassChangeTarget(Item it, int from)
    {
        for (int k = 0; k + 1 < it.S.ClassChange.Length; k += 2)
            if (it.S.ClassChange[k] == from && it.S.ClassChange[k + 1] >= 0) return it.S.ClassChange[k + 1];
        return -1;
    }

    /// <summary>A unit's starting equipment as a map object brings it (FUN_00409658): only items of the right type the class can use.</summary>
    public static (int Weapon, int Armor, int Accessory) StartingEquipment(Database db, Unit u)
    {
        int Check(int id, ItemType type) =>
            id >= 0 && id < db.Items.Length && db.Items[id].Type == type && Usable(db.Items[id], u.ClassId) ? id : -1;
        return (Check(u.Weapon, ItemType.Weapon), Check(u.Armor, ItemType.Armor), Check(u.Accessory, ItemType.Accessory));
    }
}
