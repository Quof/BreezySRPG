namespace Srpg95.Data;

/// <summary>
/// Battle formulas, as SRPGEXEC.EXE computes them (disassembly: stats FUN_0040a68c, strike FUN_0041a9c4,
/// exchange FUN_0041b4b0, spells FUN_0041ba0c, level-up FUN_0040ab..; FORMAT_NOTES §15). All integer maths
/// (C division, truncating toward zero); derived stats and damage are clamped to 0–99.
/// </summary>
public static class Combat
{
    public const int MaxLevel = 20;
    public const int ExpPerLevel = 100;

    /// <summary>Effective Str, Mag, Skl, Sta (+ move bonus) including the accessory.</summary>
    public static (int Str, int Mag, int Skl, int Sta, int Move) WithAccessory(int str, int mag, int skl, int sta, Item? acc)
    {
        if (acc is null || acc.Type != ItemType.Accessory) return (str, mag, skl, sta, 0);
        var s = acc.S;
        return (str + s.AccStr, mag + s.AccMag, skl + s.AccSkl, sta + s.AccSta, s.AccMove);
    }

    static int Clamp99(int v) => Math.Clamp(v, 0, 99);

    /// <summary>攻撃力 = Str + weapon Power + attack buff, 0–99.</summary>
    public static int Attack(int str, Item? weapon, int powerUp = 0) => Clamp99(str + (weapon?.S.WpnPower ?? 0) + powerUp);

    /// <summary>攻撃精度 ("attack speed") = Skl + weapon Usability / 3 − armor Weight, 0–99.</summary>
    public static int Speed(int skl, Item? weapon, Item? armor) => Clamp99(skl + (weapon?.S.WpnUsability ?? 0) / 3 - (armor?.S.ArmWeight ?? 0));

    /// <summary>物理防御 = Sta + armor Defense + defense buff, 0–99.</summary>
    public static int Defense(int sta, Item? armor, int defUp = 0) => Clamp99(sta + (armor?.S.ArmDefense ?? 0) + defUp);

    /// <summary>特殊防御 = Mag / 2 + armor Magic Defense + buff, 0–99.</summary>
    public static int MagicDefense(int mag, Item? armor, int mdefUp = 0) => Clamp99(mag / 2 + (armor?.S.ArmMagicDefense ?? 0) + mdefUp);

    /// <summary>Physical hit chance (%), help formula.</summary>
    public static int HitChance(int atkSkl, Item? weapon, int defSkl, Item? defArmor, int terrainDefense) =>
        70 + atkSkl * 2 + (weapon?.S.WpnUsability ?? 0) - (defSkl * 3 + terrainDefense - (defArmor?.S.ArmWeight ?? 0));

    /// <summary>Damage-spell hit chance (%): Skl×2 + accuracy − (dSkl×3 − weight) − terrain. Status spells use accuracy alone.</summary>
    public static int MagicHitChance(int atkSkl, Magic spell, int defSkl, Item? defArmor, int terrainDefense) =>
        atkSkl * 2 + spell.Accuracy - (defSkl * 3 + terrainDefense - (defArmor?.S.ArmWeight ?? 0));

    /// <summary>Critical chance (%), rolled only after a hit: Skl + critical − (dSkl/2 + crit avoid).</summary>
    public static int CritChance(int atkSkl, Item? weapon, int defSkl, Item? defArmor) =>
        atkSkl + (weapon?.S.WpnCritical ?? 0) - (defSkl / 2 + (defArmor?.S.ArmCritAvoid ?? 0));

    /// <summary>
    /// Attribute multiplier on the attack: ×3/2 when an attack attribute hits a weakness of the defender's class
    /// that its armor doesn't guard; ×4/5 when the armor guards every attack attribute and none is a weakness
    /// (the help says 50 %; the runtime does 80 %); else ×1.
    /// </summary>
    public static int ApplyAttributes(int attack, byte[] attackAttr, byte[] weakness, byte[]? armorAttr)
    {
        bool any = false, weak = false, allGuarded = true;
        for (int i = 0; i < 6; i++)
        {
            if (attackAttr[i] == 0) continue;
            any = true;
            bool guarded = armorAttr is not null && armorAttr[i] != 0;
            if (!guarded) { allGuarded = false; if (weakness[i] != 0) weak = true; }
            if (weakness[i] != 0) allGuarded = false;
        }
        if (weak) return attack * 3 / 2;
        if (any && allGuarded) return attack * 4 / 5;
        return attack;
    }

    /// <summary>True if an attack attribute hits an unguarded weakness (×1.5).</summary>
    public static bool Effective(byte[] attackAttr, byte[] weakness, byte[]? armorAttr) =>
        ApplyAttributes(100, attackAttr, weakness, armorAttr) == 150;

    /// <summary>Physical damage before a critical: attack (attributes applied) − defense − terrain/10, 0–99.</summary>
    public static int PhysicalDamage(int attack, int defense, int terrainDefense) => Clamp99(attack - defense - terrainDefense / 10);

    /// <summary>A critical multiplies the damage by 5/2 (cap 99); with 0 damage there is no critical.</summary>
    public static int Critical(int damage) => Math.Min(99, damage * 5 / 2);

    /// <summary>Damage spell: Mag + spell damage (attributes applied) − magic defense − terrain/10, 0–99.</summary>
    public static int MagicDamage(int power, int magicDefense, int terrainDefense) => Clamp99(power - magicDefense - terrainDefense / 10);

    /// <summary>Double attack (when GAME.DAT allows it): attacker speed ≥ defender speed + 3, attacker only.</summary>
    public static bool Doubles(int attackerSpeed, int defenderSpeed) => attackerSpeed >= defenderSpeed + 3;

    /// <summary>EXP for a damaging hit (15 + target level − own level) or a kill (40 + 2·(target − own)); at least 1.</summary>
    public static int Exp(int ownLevel, int targetLevel, bool killed) =>
        Math.Max(1, killed ? 40 + targetLevel * 2 - ownLevel * 2 : 15 + targetLevel - ownLevel);

    /// <summary>Raise/lower spells: value + Magic × relation / 10.</summary>
    public static int SpellEffect(Magic spell, int mag) => spell.Value + mag * spell.Relation / 10;

    /// <summary>Poison: a quarter of the current HP per turn, rounded down (FUN_00410b90); it never takes the last HP.</summary>
    public static int PoisonDamage(int hp) => hp / 4;
}
