using Srpg95.Data;

namespace Srpg95.Rules;

public enum EffectKind
{
    Damage, Miss, Resisted, Heal, MpHeal, PowerUp, DefUp, MdefUp,
    Poison, Sleep, AttackDown, DefenseDown, CurePoison, WakeUp, Cleanse, ExtraTurn,
}

/// <summary>What a spell or item did to one unit.</summary>
public readonly record struct Effect(BattleUnit Target, EffectKind Kind, int Amount, bool Killed);

/// <summary>
/// Magic and battle items (help "Magic Editor", "Item Editor", "Battle Algorithm"; FORMAT_NOTES §4, §5).
/// Provisional choices are marked; FORMAT_NOTES §15 lists them.
/// </summary>
public sealed partial class Battlefield
{
    Magic? Spell(int id) => id >= 0 && id < Db.Magic.Length ? Db.Magic[id] : null;

    /// <summary>Spells from the class's skill list whose level the unit has reached (0 ends the list).</summary>
    public IEnumerable<Magic> KnownSpells(BattleUnit u) => KnownSpells(Class(u), u.Level);

    IEnumerable<Magic> KnownSpells(ClassDef c, int level)
    {
        for (int i = 0; i < c.SkillLevels.Length && c.SkillLevels[i] > 0; i++)
            if (c.SkillLevels[i] <= level && Spell(c.SkillIds[i]) is { } m) yield return m;
    }

    public static bool CanCast(BattleUnit u, Magic m) => u.Mp >= m.MpCost;

    /// <summary>Tiles the spell can be aimed at from (x,y): the range grid (centre = caster), or the whole map.</summary>
    public IEnumerable<(int X, int Y)> SpellRange(Magic m, int x, int y) =>
        m.RangeWholeMap ? AllTiles() : GridTiles(m.RangeGrid, x, y);

    /// <summary>Tiles the spell hits when aimed at (x,y): the effect grid around the aim, or the whole map.</summary>
    public IEnumerable<(int X, int Y)> SpellArea(Magic m, int x, int y) =>
        m.EffectWholeMap ? AllTiles() : GridTiles(m.EffectGrid, x, y);

    IEnumerable<(int X, int Y)> AllTiles()
    {
        for (int y = 0; y < Map.Height; y++)
            for (int x = 0; x < Map.Width; x++) yield return (x, y);
    }

    IEnumerable<(int X, int Y)> GridTiles(bool[,] g, int cx, int cy)
    {
        for (int r = 0; r < 21; r++)
            for (int c = 0; c < 21; c++)
                if (g[r, c] && InBounds(cx + c - 10, cy + r - 10)) yield return (cx + c - 10, cy + r - 10);
    }

    /// <summary>
    /// Whether a spell cast by <paramref name="caster"/> affects <paramref name="t"/>. Target 1 = one side only
    /// (hostile units for Damage/Degrade, the caster's side for Raise); 0 = everyone in the area
    /// ("splashes both indiscriminately"). The caster never hits itself with a harmful spell (provisional).
    /// </summary>
    public bool Affects(BattleUnit caster, Magic m, BattleUnit t)
    {
        bool harmful = m.Type is 0 or 1;
        if (harmful && (t == caster || t.Unattackable)) return false;
        if (m.Target == 0) return true;
        return harmful ? Hostile(caster, t) : Friendly(caster, t);
    }

    public List<BattleUnit> SpellTargets(BattleUnit caster, Magic m, int aimX, int aimY)
    {
        var area = SpellArea(m, aimX, aimY).ToHashSet();
        return Alive.Where(t => area.Contains((t.X, t.Y)) && Affects(caster, m, t)).ToList();
    }

    /// <summary>Aim tiles from (x,y) that would affect at least one unit (provisional: the cursor must hit someone).</summary>
    public IEnumerable<(int X, int Y)> SpellAims(BattleUnit caster, Magic m, int x, int y) =>
        SpellRange(m, x, y).Where(a => SpellTargets(caster, m, a.X, a.Y).Count > 0);

    bool Resists(BattleUnit t, int effect)
    {
        if (effect is < 0 or > 3) return false;
        if (Class(t).Resist[effect] != 0) return true;
        return Item(t.Accessory) is { Type: ItemType.Accessory } acc && acc.S.AccSafe[effect] != 0;
    }

    /// <summary>FUN_0041ba0c: damage spells roll Skl×2 + accuracy − (dSkl×3 − weight) − terrain; status spells roll accuracy alone.</summary>
    int MagicHit(BattleUnit caster, Magic m, BattleUnit t)
    {
        if (m.Type == 1) return m.Accuracy;
        if (t.SleepTurns > 0) return 100;   // provisional, like physical attacks
        return Combat.MagicHitChance(Stats(caster).Skl, m, Stats(t).Skl, Item(t.Armor), TerrainAt(t.X, t.Y).DefenseBonus);
    }

    /// <summary>Spell damage (no roll): (Mag + damage, attributes applied) − magic defense − terrain/10, 0–99. No criticals.</summary>
    public int PredictSpellDamage(BattleUnit caster, Magic m, BattleUnit t)
    {
        var arm = Item(t.Armor);
        int power = Combat.ApplyAttributes(Stats(caster).Mag + m.Damage, m.AttackAttr, Class(t).Weakness, arm?.S.ArmDefenseAttr);
        return Combat.MagicDamage(power, Stats(t).MagicDefense, TerrainAt(t.X, t.Y).DefenseBonus);
    }

    /// <summary>
    /// Casts a spell at (aimX, aimY). From a magic item it costs no MP and gives no EXP (provisional).
    /// EXP: Damage spells as attacks (kill or hit, against the highest-level unit); Degrade/Raise give
    /// the spell's Exp Value per cast (help: "upon casting this spell").
    /// </summary>
    public ExchangeResult Cast(BattleUnit caster, Magic m, int aimX, int aimY, bool fromItem = false)
    {
        var r = new ExchangeResult();
        if (!fromItem) caster.Mp = Math.Max(0, caster.Mp - m.MpCost);
        int power = Combat.SpellEffect(m, Stats(caster).Mag);
        foreach (var t in SpellTargets(caster, m, aimX, aimY))
            r.Effects.Add(Apply(caster, m, t, power));
        caster.Acted = true;
        if (caster.Side != Side.Ally || fromItem || caster.Dead) return r;

        // damage spells: EXP summed over every enemy damaged (unlike attacks, which take the largest), kills collect
        // rewards; status / raise spells: the spell's Exp Value (FUN_0041ba0c, FUN_0041b6ec)
        int exp = 0;
        if (m.Type == 0)
            foreach (var e in r.Effects.Where(e => e.Kind == EffectKind.Damage && e.Amount > 0 && e.Target.Side != Side.Ally))
            {
                exp += Combat.Exp(caster.Level, e.Target.Level, e.Killed);
                if (e.Killed) CollectReward(r, e.Target);
            }
        else exp = m.ExpValue;
        if (exp > 0) AwardExp(r, caster, exp);
        return r;
    }

    Effect Apply(BattleUnit caster, Magic m, BattleUnit t, int power)
    {
        switch (m.Type)
        {
            case 0:
            {
                if (Rng.Next(100) >= MagicHit(caster, m, t)) return new Effect(t, EffectKind.Miss, 0, false);
                int dmg = PredictSpellDamage(caster, m, t);
                t.Hp = Math.Max(0, t.Hp - dmg);
                if (t.Hp == 0) t.Dead = true;
                return new Effect(t, EffectKind.Damage, dmg, t.Dead);
            }
            case 1:
            {
                if (Resists(t, m.DegradeEffect)) return new Effect(t, EffectKind.Resisted, 0, false);
                if (Rng.Next(100) >= MagicHit(caster, m, t)) return new Effect(t, EffectKind.Miss, 0, false);
                switch (m.DegradeEffect)
                {
                    case 0: t.PoisonTurns = 3; return new Effect(t, EffectKind.Poison, 0, false);
                    // asleep for 2 of its own turns, acting again on the 3rd (help); counted down at its phase start
                    case 1: t.SleepTurns = 3; return new Effect(t, EffectKind.Sleep, 0, false);
                    // buffs don't stack: a down only replaces a smaller one, an up a lower one (FUN_00417.., battle end)
                    case 2: t.PowerUp = Math.Min(t.PowerUp, -power); return new Effect(t, EffectKind.AttackDown, power, false);
                    default: t.DefUp = Math.Min(t.DefUp, -power); return new Effect(t, EffectKind.DefenseDown, power, false);
                }
            }
            default:
                switch (m.RaiseEffect)
                {
                    case 0:
                    {
                        int before = t.Hp;
                        t.Hp = Math.Min(t.MaxHp, t.Hp + power);
                        return new Effect(t, EffectKind.Heal, t.Hp - before, false);
                    }
                    case 1: t.PowerUp = Math.Max(t.PowerUp, power); return new Effect(t, EffectKind.PowerUp, power, false);
                    case 2: t.DefUp = Math.Max(t.DefUp, power); return new Effect(t, EffectKind.DefUp, power, false);
                    case 3: t.PoisonTurns = 0; return new Effect(t, EffectKind.CurePoison, 0, false);
                    case 4: Cleanse(t); return new Effect(t, EffectKind.Cleanse, 0, false);
                    default:
                        if (t != caster) t.Acted = false;
                        return new Effect(t, EffectKind.ExtraTurn, 0, false);
                }
        }
    }

    static void Cleanse(BattleUnit t)
    {
        t.PoisonTurns = 0;
        t.SleepTurns = 0;
        if (t.PowerUp < 0) t.PowerUp = 0;
        if (t.DefUp < 0) t.DefUp = 0;
    }

    // ---------------- items ----------------

    /// <summary>Medicine and magic items can be used in battle by classes ticked in Class Useage (stat-up/class-change: camp only, provisional).</summary>
    public bool CanUseInBattle(BattleUnit u, Item it) =>
        it.Type is ItemType.Medicine or ItemType.Magic && u.ClassId >= 0 && u.ClassId < it.ClassUsable.Length && it.ClassUsable[u.ClassId] != 0
        && (it.Type != ItemType.Magic || Spell(it.S.MagicSpell) is not null);

    public Magic? ItemSpell(Item it) => it.Type == ItemType.Magic ? Spell(it.S.MagicSpell) : null;

    /// <summary>Units a medicine can be given to: the user's side within its range (0 = the user only).</summary>
    public IEnumerable<BattleUnit> MedicineTargets(BattleUnit user, Item it) =>
        Alive.Where(t => Friendly(user, t) && Dist(user.X, user.Y, t.X, t.Y) <= Math.Max(0, it.S.MedRange));

    /// <summary>Medicine: HP/MP recovery, Defense/Magic Defense Up (resetting the counter, curing Defense Down), cures.</summary>
    public ExchangeResult UseMedicine(BattleUnit user, Item it, BattleUnit t)
    {
        var r = new ExchangeResult();
        var s = it.S;
        if (s.MedHp > 0)
        {
            int before = t.Hp;
            t.Hp = Math.Min(t.MaxHp, t.Hp + s.MedHp);
            r.Effects.Add(new Effect(t, EffectKind.Heal, t.Hp - before, false));
        }
        if (s.MedMp > 0)
        {
            int before = t.Mp;
            t.Mp = Math.Min(t.MaxMp, t.Mp + s.MedMp);
            r.Effects.Add(new Effect(t, EffectKind.MpHeal, t.Mp - before, false));
        }
        if (s.MedDefUp > 0) { t.DefUp = s.MedDefUp; r.Effects.Add(new Effect(t, EffectKind.DefUp, s.MedDefUp, false)); }
        if (s.MedMdefUp > 0) { t.MdefUp = s.MedMdefUp; r.Effects.Add(new Effect(t, EffectKind.MdefUp, s.MedMdefUp, false)); }
        if (s.MedCure[0] != 0 && t.PoisonTurns > 0) { t.PoisonTurns = 0; r.Effects.Add(new Effect(t, EffectKind.CurePoison, 0, false)); }
        if (s.MedCure[1] != 0 && t.SleepTurns > 0) { t.SleepTurns = 0; r.Effects.Add(new Effect(t, EffectKind.WakeUp, 0, false)); }
        if (s.MedCure[2] != 0 && t.PowerUp < 0) t.PowerUp = 0;
        if (s.MedCure[3] != 0 && t.DefUp < 0) t.DefUp = 0;
        user.Acted = true;
        return r;
    }
}
