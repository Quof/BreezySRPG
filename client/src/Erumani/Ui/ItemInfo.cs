using Srpg95.Data;
using static Erumani.Ui.Lang;

namespace Erumani.Ui;

/// <summary>
/// One-line stat summary for an item (modern addition): the numbers that the original only mentions, if at all, in
/// the item's description text. Shown on the description window's name line, in the Language setting's words.
/// </summary>
public static class ItemInfo
{
    static string Signed(int v) => v >= 0 ? $"+{v}" : $"{v}";

    /// <summary>One stat: its words, its number and whether the number is an amount added (a sign goes with it in the item's box).</summary>
    readonly record struct Part(string En, string Ja, string Value, bool Amount = false);

    static List<Part> Parts(Item it, Database db)
    {
        var s = it.S;
        var parts = new List<Part>();
        void Add(string en, string ja, int v) { if (v != 0) parts.Add(new(en, ja, $"{v}", true)); }
        string Span(int lo, int hi) => lo == hi ? $"{lo}" : $"{lo}～{hi}";
        switch (it.Type)
        {
            case ItemType.Weapon:
                parts.Add(new("Pow", "威力", $"{s.WpnPower}", true));
                parts.Add(new("Hit", "命中", $"{s.WpnUsability}", true));
                Add("Crit", "必殺", s.WpnCritical);
                if (s.WpnRange >= 0 && s.WpnRange < Item.WeaponRanges.Length && Item.WeaponRanges[s.WpnRange] is var (lo, hi))
                    parts.Add(new("Range", "射程", Span(lo, hi)));
                break;
            case ItemType.Armor:
                parts.Add(new("Def", "防御", $"{s.ArmDefense}", true));
                parts.Add(new("M.Def", "特防", $"{s.ArmMagicDefense}", true));
                Add("Crit evade", "必殺回避", s.ArmCritAvoid);
                Add("Weight", "重さ", s.ArmWeight);
                break;
            case ItemType.Accessory:
                Add("Atk", "攻撃", s.AccStr);
                Add("Spc", "特殊", s.AccMag);
                Add("Hit/Eva", "命中回避", s.AccSkl);
                Add("End", "耐久", s.AccSta);
                Add("Move", "移動", s.AccMove);
                if (s.AccGrowthBonus != 0) parts.Add(new("Growth", "成長率", $"{Signed(s.AccGrowthBonus)}%"));
                break;
            case ItemType.Medicine:
                Add("HP", "HP", s.MedHp);
                Add("TP", "TP", s.MedMp);
                Add("Def", "防御", s.MedDefUp);
                Add("M.Def", "特防", s.MedMdefUp);
                var (min, max) = s.MedRange >= 0 && s.MedRange < Item.MedicineRanges.Length ? Item.MedicineRanges[s.MedRange] : (0, 0);
                parts.Add(max == 0 ? new("Self only", "自分のみ", "") : new("Area", "範囲", min == 0 ? $"{max}" : Span(min, max)));
                break;
            case ItemType.StatUp:
                (string En, string Ja)[] names = { ("Max HP", "最大HP"), ("Max TP", "最大TP"), ("Atk", "攻撃"), ("Spc", "特殊"), ("Hit/Eva", "命中回避"), ("End", "耐久") };
                for (int i = 0; i < names.Length && i < s.StatUp.Length; i++) Add(names[i].En, names[i].Ja, s.StatUp[i]);
                break;
            case ItemType.Magic:
                if (s.MagicSpell >= 0 && s.MagicSpell < db.Magic.Length) parts.Add(new("", "", $"「{db.Magic[s.MagicSpell].Name}」"));
                break;
        }
        return parts;
    }

    /// <summary>The stats on one line, for the description window's name line.</summary>
    public static string Summary(Item it, Database db) =>
        string.Join(Japanese ? "　" : "  ", Parts(it, db).Select(p =>
        {
            // as it always read: signs on the bonuses of accessories, medicines and stat-up items only
            bool signed = p.Amount && it.Type is ItemType.Accessory or ItemType.Medicine or ItemType.StatUp;
            string v = signed && int.TryParse(p.Value, out int n) ? Signed(n) : p.Value;
            return p.Ja.Length == 0 ? v : (Japanese ? p.Ja : p.En + " ") + v;
        }));

    /// <summary>
    /// The stats a line each, every amount signed (威力＋４, 命中＋４０, 必殺－１５, 射程１), in Japanese the digits and
    /// signs full-width — the box shown for a piece of equipment in the status window.
    /// </summary>
    public static List<string> Lines(Item it, Database db) => Parts(it, db).Select(p =>
    {
        string v = p.Amount && int.TryParse(p.Value, out int n) ? Signed(n) : p.Value;
        if (!Japanese) return p.En.Length == 0 ? v : v.Length == 0 ? p.En : $"{p.En} {v}";
        string wide = new(v.Select(c => c switch { >= '0' and <= '9' => (char)('０' + c - '0'), '+' => '＋', '-' => '－', '%' => '％', _ => c }).ToArray());
        return p.Ja + wide;
    }).ToList();
}
