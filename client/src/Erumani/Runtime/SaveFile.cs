using System.Buffers.Binary;
using System.Text;
using Srpg95.Data;

namespace Erumani.Runtime;

/// <summary>
/// SAVEn.DAT, the original runtime's save format (FORMAT_NOTES §14): a 2000-byte header, 100 map-unit and
/// 50 roster unit records of 640 bytes, and a copy of the map's object table. Writing starts from the
/// bytes of the slot's previous file when there is one, so fields the client doesn't understand survive.
/// Only camp saves are written in full; a battle save's map state is kept as it was (battle saves: later).
/// </summary>
public sealed class SaveFile
{
    public const int Size = 114_400, UnitOff = 0x7D0, UnitSize = 640, MapUnits = 100, RosterUnits = 50;
    const int RosterOff = UnitOff + MapUnits * UnitSize;   // 0x101D0

    public byte[] Raw { get; }

    static SaveFile() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    SaveFile(byte[] raw) => Raw = raw;

    /// <summary>Where SAVEn.DAT files live: the game folder (as the original), unless --saves points elsewhere.</summary>
    public static string? Directory { get; set; }

    public static string PathFor(GameFolder game, int slot) => Path.Combine(Directory ?? game.Root, $"SAVE{slot}.DAT");

    public static SaveFile? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var raw = File.ReadAllBytes(path);
        return raw.Length == Size ? new SaveFile(raw) : null;
    }

    int I32(int off) => BinaryPrimitives.ReadInt32LittleEndian(Raw.AsSpan(off));
    void I32(int off, int v) => BinaryPrimitives.WriteInt32LittleEndian(Raw.AsSpan(off), v);

    /// <summary>1 when saved at a camp, 0 during a battle (inferred).</summary>
    public bool AtCamp => I32(0x000) == 1;
    /// <summary>Map number, 1-based: the map being played, or the map the camp leads to.</summary>
    public int MapNumber => I32(0x010);
    public int Gold => I32(0x534);
    public int CampCount => I32(0x004);

    /// <summary>Copies the save into the game state: switches, variables, inventory, gold and the roster.</summary>
    public void ApplyTo(GameState s)
    {
        for (int i = 0; i < GameState.SwitchCount; i++) s.Switches[i] = Raw[0x14 + i] != 0;
        Array.Clear(s.Variables);
        for (int i = 0; i < 250; i++) s.Variables[i] = BinaryPrimitives.ReadInt16LittleEndian(Raw.AsSpan(0x5DC + i * 2));
        s.Items.Clear();
        int count = Math.Clamp(I32(0x530), 0, 100);
        for (int i = 0; i < count; i++)
        {
            int item = I32(0x210 + i * 8), n = I32(0x214 + i * 8);
            if (item >= 0 && n > 0) s.AddItem(item, n);
        }
        s.Gold = Gold;
        s.CampCount = CampCount;
        s.Roster.Clear();
        s.Party.Clear();
        for (int i = 0; i < RosterUnits; i++)
        {
            int off = RosterOff + i * UnitSize;
            if (I32(off) == 0) continue;
            int Rec(int k) => I32(off + k * 4);
            int unit = Rec(8);
            var bases = Enumerable.Range(20, 6).Select(Rec).ToArray();
            s.Roster[unit] = new RosterEntry(Rec(9), Rec(10), Rec(11), bases, Rec(40), Rec(41), Rec(42));
            s.Party.Add(unit);
        }
        s.LastSave = (byte[])Raw.Clone();
    }

    /// <summary>
    /// A camp save of the current state, leading to map <paramref name="mapIndex"/> (0-based). Header fields and
    /// roster records the client knows are written; everything else comes from <paramref name="template"/>
    /// (the slot's old file, else the last save loaded, else zeros).
    /// </summary>
    public static SaveFile ForCamp(GameState s, Database db, int mapIndex, string bgm, byte[]? template)
    {
        var f = new SaveFile(template is { Length: Size } ? (byte[])template.Clone() : new byte[Size]);
        f.I32(0x000, 1);
        f.I32(0x004, s.CampCount);
        f.I32(0x010, mapIndex + 1);
        for (int i = 0; i < GameState.SwitchCount; i++) f.Raw[0x14 + i] = (byte)(s.Switches[i] ? 1 : 0);
        Array.Clear(f.Raw, 0x210, 0x320);
        int n = 0;
        foreach (var (item, count) in s.Items.Where(kv => kv.Value > 0).Take(100))
        {
            f.I32(0x210 + n * 8, item);
            f.I32(0x214 + n * 8, count);
            n++;
        }
        f.I32(0x530, n);
        f.I32(0x534, s.Gold);
        f.I32(0x538, s.Roster.Count);
        var name = new byte[48];
        var enc = Encoding.GetEncoding(932).GetBytes(bgm);
        Array.Copy(enc, name, Math.Min(enc.Length, 47));
        name.CopyTo(f.Raw, 0x53C);
        if (f.I32(0x56C) == 0) for (int i = 0; i < 4; i++) f.I32(0x56C + i * 4, 100);   // screen tone 100 %
        for (int i = 0; i < 250; i++)
            BinaryPrimitives.WriteInt16LittleEndian(f.Raw.AsSpan(0x5DC + i * 2), (short)Math.Clamp(s.Variables[i], short.MinValue, short.MaxValue));

        // roster: keep each unit's old record if the template had it (unknown fields), then patch the known ones
        var old = new Dictionary<int, byte[]>();
        for (int i = 0; i < RosterUnits; i++)
        {
            int off = RosterOff + i * UnitSize;
            if (f.I32(off) != 0) old[f.I32(off + 32)] = f.Raw.AsSpan(off, UnitSize).ToArray();
        }
        Array.Clear(f.Raw, RosterOff, RosterUnits * UnitSize);
        int slot = 0;
        foreach (var (unit, e) in s.Roster.Take(RosterUnits))
        {
            int off = RosterOff + slot++ * UnitSize;
            bool had = old.TryGetValue(unit, out var rec);
            if (had) rec!.CopyTo(f.Raw, off);
            WriteUnit(f, off, db, unit, e, keepBattleState: had);
        }
        return f;
    }

    /// <summary>The roster record fields of §14 (stats with equipment, range, terrain costs), HP/MP full as at camp.</summary>
    /// <param name="keepBattleState">leave the last battle's leftovers alone: movement left [50] (SAVE5 has 0 there)</param>
    static void WriteUnit(SaveFile f, int off, Database db, int unit, RosterEntry e, bool keepBattleState)
    {
        void R(int k, int v) => f.I32(off + k * 4, v);
        var cls = db.Classes[Math.Clamp(e.ClassId, 0, db.Classes.Length - 1)];
        Item? It(int id) => id >= 0 && id < db.Items.Length ? db.Items[id] : null;
        var (w, a) = (It(e.Weapon), It(e.Armor));
        var (str, mag, skl, sta, moveBonus) = Combat.WithAccessory(e.Base[2], e.Base[3], e.Base[4], e.Base[5], It(e.Accessory));
        var (rmin, rmax) = w is { Type: ItemType.Weapon } && w.S.WpnRange is >= 0 and < 8 ? Item.WeaponRanges[w.S.WpnRange] : (1, 1);
        R(0, 1); R(1, 1); R(2, 0); R(3, 0); R(4, -1); R(7, -1); R(8, unit); R(9, e.ClassId); R(10, e.Level); R(11, e.Exp); R(13, 1);
        R(19, cls.Movement + moveBonus);
        for (int i = 0; i < 6; i++) R(20 + i, e.Base[i]);
        R(26, e.Base[0]); R(27, e.Base[1]); R(28, str); R(29, mag); R(30, skl); R(31, sta);
        R(32, Combat.Attack(str, w)); R(33, Combat.Speed(skl, w, a)); R(34, Combat.Defense(sta, a)); R(35, Combat.MagicDefense(mag, a));
        R(36, 0);
        R(40, e.Weapon); R(41, e.Armor); R(42, e.Accessory); R(43, rmin); R(44, rmax);
        if (!keepBattleState) R(50, cls.Movement + moveBonus);
        // [130..149] known spells (MAGIC ids, in class skill-list order), [150] their count: グレイ 8 (活性波),
        // ジュリア 1 (剣の舞) then 1, 2 (虎伏演舞) once she reached level 4 (all saves agree)
        var spells = new List<int>();
        for (int i = 0; i < cls.SkillLevels.Length && cls.SkillLevels[i] > 0; i++)
            if (cls.SkillLevels[i] <= e.Level) spells.Add(cls.SkillIds[i]);
        for (int i = 0; i < 20; i++) R(130 + i, i < spells.Count ? spells[i] : 0);
        R(150, Math.Min(20, spells.Count));
        int mt = Math.Clamp(cls.MoveType, 0, 2);
        for (int t = 0; t < 70; t++)
        {
            int ov = t < cls.TerrainMoveOverride.Length ? cls.TerrainMoveOverride[t] : -1;
            R(60 + t, ov >= 0 ? ov : t < db.Terrains.Length ? db.Terrains[t].MoveCost[mt] : 0);
        }
    }

    public void Write(string path) => File.WriteAllBytes(path, Raw);
}
