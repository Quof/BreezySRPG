using Srpg95.Data;

namespace Erumani.Ui;

/// <summary>
/// One-line stat summary for an item (modern addition): the numbers that the original only mentions, if at all, in
/// the item's description text. Shown on the description window's name line.
/// </summary>
public static class ItemInfo
{
    static string Signed(int v) => v >= 0 ? $"+{v}" : $"{v}";

    public static string Summary(Item it, Database db)
    {
        var s = it.S;
        var parts = new List<string>();
        void Add(string label, int v, bool signed = false) { if (v != 0) parts.Add(label + (signed ? Signed(v) : v.ToString())); }
        switch (it.Type)
        {
            case ItemType.Weapon:
                parts.Add($"威力{s.WpnPower}");
                parts.Add($"命中{s.WpnUsability}");
                Add("必殺", s.WpnCritical);
                if (s.WpnRange >= 0 && s.WpnRange < Item.WeaponRanges.Length && Item.WeaponRanges[s.WpnRange] is var (lo, hi))
                    parts.Add(lo == hi ? $"射程{lo}" : $"射程{lo}～{hi}");
                break;
            case ItemType.Armor:
                parts.Add($"防御{s.ArmDefense}");
                parts.Add($"特防{s.ArmMagicDefense}");
                Add("必殺回避", s.ArmCritAvoid);
                Add("重さ", s.ArmWeight);
                break;
            case ItemType.Accessory:
                Add("攻撃", s.AccStr, true);
                Add("特殊", s.AccMag, true);
                Add("命中回避", s.AccSkl, true);
                Add("耐久", s.AccSta, true);
                Add("移動", s.AccMove, true);
                if (s.AccGrowthBonus != 0) parts.Add($"成長率{Signed(s.AccGrowthBonus)}%");
                break;
            case ItemType.Medicine:
                Add("HP", s.MedHp, true);
                Add("TP", s.MedMp, true);
                Add("防御", s.MedDefUp, true);
                Add("特防", s.MedMdefUp, true);
                var (min, max) = s.MedRange >= 0 && s.MedRange < Item.MedicineRanges.Length ? Item.MedicineRanges[s.MedRange] : (0, 0);
                parts.Add(max == 0 ? "自分のみ" : min == 0 ? $"範囲{max}" : $"範囲{min}～{max}");
                break;
            case ItemType.StatUp:
                string[] names = { "最大HP", "最大TP", "攻撃", "特殊", "命中回避", "耐久" };
                for (int i = 0; i < names.Length && i < s.StatUp.Length; i++) Add(names[i], s.StatUp[i], true);
                break;
            case ItemType.Magic:
                if (s.MagicSpell >= 0 && s.MagicSpell < db.Magic.Length) parts.Add($"「{db.Magic[s.MagicSpell].Name}」");
                break;
        }
        return string.Join("　", parts);
    }
}
