namespace Srpg95.Data;

/// <summary>
/// Battle formulas from the editor help ("Battle Algorithm"). The four status-window values
/// (攻撃力, 攻撃精度, 物理防御, 特殊防御) are verified against 172 unit records written by the
/// original runtime (save files, see FORMAT_NOTES §14); the rest follows the help text and is
/// provisional until checked in play.
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

    /// <summary>攻撃力 = Str + weapon Power (+ attack buff). Verified.</summary>
    public static int Attack(int str, Item? weapon, int powerUp = 0) => str + (weapon?.S.WpnPower ?? 0) + powerUp;

    /// <summary>攻撃精度 ("attack speed") = Skl + weapon Usability / 3 − armor Weight. Verified.</summary>
    public static int Speed(int skl, Item? weapon, Item? armor) => skl + (weapon?.S.WpnUsability ?? 0) / 3 - (armor?.S.ArmWeight ?? 0);

    /// <summary>物理防御 = Sta + armor Defense (+ defense buff). Verified (without buffs).</summary>
    public static int Defense(int sta, Item? armor, int defUp = 0) => sta + (armor?.S.ArmDefense ?? 0) + defUp;

    /// <summary>特殊防御 = Mag / 2 + armor Magic Defense (+ buff). Verified (without buffs).</summary>
    public static int MagicDefense(int mag, Item? armor, int mdefUp = 0) => mag / 2 + (armor?.S.ArmMagicDefense ?? 0) + mdefUp;

    /// <summary>Physical hit chance (%), help formula.</summary>
    public static int HitChance(int atkSkl, Item? weapon, int defSkl, Item? defArmor, int terrainDefense) =>
        70 + atkSkl * 2 + (weapon?.S.WpnUsability ?? 0) - (defSkl * 3 + terrainDefense - (defArmor?.S.ArmWeight ?? 0));

    /// <summary>Magic hit chance (%), help formula.</summary>
    public static int MagicHitChance(int atkSkl, Magic spell, int defSkl, Item? defArmor, int terrainDefense) =>
        atkSkl * 2 + spell.Accuracy - (defSkl * 3 + terrainDefense - (defArmor?.S.ArmWeight ?? 0));

    /// <summary>Critical chance (%), help formula.</summary>
    public static int CritChance(int atkSkl, Item? weapon, int defSkl, Item? defArmor) =>
        atkSkl + (weapon?.S.WpnCritical ?? 0) - (defSkl / 2 + (defArmor?.S.ArmCritAvoid ?? 0));

    /// <summary>True if the weapon's attack attributes hit one of the defender's weaknesses
    /// and the defender's armor doesn't guard that attribute.</summary>
    public static bool Effective(byte[] attackAttr, byte[] weakness, byte[]? armorAttr)
    {
        for (int i = 0; i < 6; i++)
            if (attackAttr[i] != 0 && weakness[i] != 0 && (armorAttr is null || armorAttr[i] == 0)) return true;
        return false;
    }

    /// <summary>
    /// Physical damage: attack − (defense + terrain/10); attribute weakness ×1.5 on attack;
    /// critical = attack×2.5 − defense×2.5. Never below 0 (provisional).
    /// </summary>
    public static int PhysicalDamage(int attack, int defense, int terrainDefense, bool effective, bool critical)
    {
        double atk = effective ? attack * 1.5 : attack;
        double def = defense + terrainDefense / 10.0;
        double d = critical ? atk * 2.5 - def * 2.5 : atk - def;
        return Math.Max(0, (int)Math.Floor(d));
    }

    public static int MagicDamage(int mag, Magic spell, int magicDefense, bool effective)
    {
        double power = mag + spell.Damage;
        if (effective) power *= 1.5;
        return Math.Max(0, (int)Math.Floor(power - magicDefense));
    }

    /// <summary>Double attack when the attacker is at least 3 points faster (attacker only).</summary>
    public static bool Doubles(int attackerSpeed, int defenderSpeed) => attackerSpeed >= defenderSpeed + 3;

    /// <summary>EXP for hitting (15 + target level − own level) or killing (40 + 2·(target − own)); at least 1 (provisional).</summary>
    public static int Exp(int ownLevel, int targetLevel, bool killed) =>
        Math.Max(1, killed ? 40 + targetLevel * 2 - ownLevel * 2 : 15 + targetLevel - ownLevel);

    /// <summary>Raise/lower spells: value + Magic × relation / 10.</summary>
    public static int SpellEffect(Magic spell, int mag) => spell.Value + mag * spell.Relation / 10;

    /// <summary>Poison: 25 % of current HP per turn, rounded up.</summary>
    public static int PoisonDamage(int hp) => (hp + 3) / 4;
}
