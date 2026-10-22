using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani.Ui;

/// <summary>
/// The unit status window (FUN_00412448), shared by the battle map (right click, at (188, 96)) and the camp's unit
/// screen: a 264 × 288 panel (22 × 18 units) in the unit's side's colour — blue for the player's, red for the
/// computer's, yellow for neutral ones — its text and lines in that colour's light and dark swatches (FUN_004035e4).
/// Text is 12 px except the name (16 px bold); GDI positions, from the panel's corner:
/// - the name at (12, 14) in a raised box (8, 7)–(255, 36): light top and left, dark bottom and right;
/// - the class at (12, 40), drawn twice a pixel apart; ＬＶ at (144, 40) and 「   %2d (%2d)」 level and EXP at (180, 40);
/// - ＨＰ at (12, 64) with 「  %2d／%2d」 at 60; ＴＰ at (144, 64) with its numbers at 192;
/// - 攻撃力 攻撃精度 物理防御 特殊防御 at y 88, 104, 120, 136 and the four base stats (with the accessory) at 160, 176,
///   192, 208, each label at 12 and 「  %2d」 at 84;
/// - 武器 防具 装飾品 at y 232, 248, 264 with the item's name at 84, nothing for an empty slot;
/// - the face, 128 × 128, at (124, 92) in a sunken frame (123, 91)–(252, 220).
/// Modern additions: a piece of equipment's stats in a small box under its name, while the pointer is on the name or
/// when the status key picked it (<paramref name="shown"/>); the restyled skins draw the class once in a 14-px bold, as
/// the original's doubled 12 px blurs in a smooth face. On the battle map, pulsing arrows at the sides lead to a second
/// page (modern): the unit's abilities, each with its TP cost, the chosen one's name and description in a box above.
/// </summary>
public static class StatusWindow
{
    public static readonly Rectangle Default = new(188, 96, 264, 288);

    /// <summary>The abilities page's rows: as many as fit under the header.</summary>
    public const int SkillRows = 12;
    const int SkillTop = 88;

    /// <summary>The abilities a unit knows, as the page lists them.</summary>
    public static List<Magic> Skills(Battlefield field, BattleUnit u) => field.KnownSpells(u).ToList();

    /// <summary>The abilities page's row under <paramref name="p"/> (0 the first row shown), or −1.</summary>
    public static int SkillRowAt(Rectangle r, Point p)
    {
        if (p.X < r.X + 8 || p.X >= r.Right - 8 || p.Y < r.Y + SkillTop) return -1;
        int row = (p.Y - r.Y - SkillTop) / 16;
        return row < SkillRows ? row : -1;
    }

    /// <param name="shown">the equipment slot whose box shows (0 weapon, 1 armour, 2 accessory), −1 none</param>
    /// <param name="mouse">the pointer: on an item's name, that item's box shows instead</param>
    /// <param name="page">0 the stats and equipment, 1 the abilities (with <paramref name="skill"/> chosen, the list from <paramref name="top"/>)</param>
    /// <param name="arrows">the pulsing arrows to the other page (the battle map's window)</param>
    public static void Draw(SpriteBatch sb, Ui ui, Textures tex, Database db, Battlefield field, BattleUnit u, Rectangle? at = null,
        int shown = -1, Point? mouse = null, int page = 0, int skill = 0, int top = 0, bool arrows = false, int frame = 0)
    {
        var r = at ?? Default;
        var colour = u.Side switch { Side.Neutral => Ui.PanelColor.Yellow, Side.Enemy => Ui.PanelColor.Red, _ => Ui.PanelColor.Blue };
        ui.Panel(sb, r, colour);
        var (light, dark) = Ui.EdgeColors(colour);
        var text = Ui.Restyled ? Ui.PanelText : light;
        var st = field.Stats(u);
        string Word(int i) => i >= 0 && i < db.Words.Length ? db.Words[i] : "";
        void T(string s, int x, int y) => ui.Text12(sb, s, r.X + x, r.Y + y, text);
        void Line(int x, int y, int w, int h, Color c) => ui.Fill(sb, new Rectangle(r.X + x, r.Y + y, w, h), c);

        if (arrows) Arrows(sb, ui, r, frame);
        int face = db.Units[Math.Clamp(u.Unit, 0, db.Units.Length - 1)].Face;
        if (page == 0 && face > 0 && tex.Get($"FACE_{face:000}.BMP") is { } f) sb.Draw(f, new Vector2(r.X + 124, r.Y + 92), Color.White);
        if (Ui.Gilded)
        {
            // a gilded frame round the face and a divider under the name, in place of the etched lines
            if (page == 0) ui.GildedFrame(sb, new Rectangle(r.X + 120, r.Y + 88, 136, 136));
            ui.GoldRule(sb, r.X + 8, r.Y + 36, 248);
        }
        else
        {
            if (page == 0)
            {
                Line(123, 91, 130, 1, dark);
                Line(123, 220, 130, 1, light);
                Line(123, 91, 1, 130, dark);
                Line(252, 91, 1, 130, light);
            }

            Line(8, 7, 246, 1, light);
            Line(8, 36, 246, 1, dark);
            Line(8, 7, 1, 28, light);
            Line(255, 7, 1, 28, dark);
        }
        // the name through GDI's DrawText (FUN_00407954), its corner at the rectangle's
        ui.NameText.Get(u.Name)?.Draw(sb, new Vector2(r.X + 12, r.Y + 14), text);

        string cls = field.Class(u).Name;
        if (Ui.Restyled && ui.BoldText.Get(cls) is { } bold) bold.Draw(sb, new Vector2(r.X + 12, Ui.Line16Y(bold, r.Y + 40) - 1), text);
        else
        {
            T(cls, 12, 40);
            T(cls, 13, 40);
        }
        T(Word(1), 144, 40);
        T($"   {u.Level,2} ({u.Exp,2})", 180, 40);
        T(Word(2), 12, 64);
        T($"  {u.Hp,2}／{u.MaxHp,2}", 60, 64);
        T(Word(3), 144, 64);
        T($"  {u.Mp,2}／{u.MaxMp,2}", 192, 64);

        if (page == 1) { SkillsPage(sb, ui, field, u, r, skill, top, text, colour); return; }

        int[] vals = { st.Attack, st.Speed, st.Defense, st.MagicDefense, st.Str, st.Mag, st.Skl, st.Sta };
        int[] rows = { 88, 104, 120, 136, 160, 176, 192, 208 };
        for (int i = 0; i < 8; i++)
        {
            T(Word(4 + i), 12, rows[i]);
            T($"  {vals[i],2}", 84, rows[i]);
        }

        int[] held = { u.Weapon, u.Armor, u.Accessory };
        for (int s = 0; s < 3; s++)
        {
            T(Word(12 + s), 12, 232 + 16 * s);
            if (held[s] >= 0 && held[s] < db.Items.Length) T(db.Items[held[s]].Name, 84, 232 + 16 * s);
        }

        // the stats of the piece under the pointer, else of the one the status key picked, a little below its name
        int slot = Enumerable.Range(0, 3).FirstOrDefault(s => mouse is { } m && held[s] >= 0 && held[s] < db.Items.Length
            && new Rectangle(r.X + 80, r.Y + 232 + 16 * s, Math.Max(40, ui.Measure12(db.Items[held[s]].Name) + 8), 16).Contains(m), shown);
        if (slot is < 0 or > 2 || held[slot] < 0 || held[slot] >= db.Items.Length) return;
        var lines = ItemInfo.Lines(db.Items[held[slot]], db);
        if (lines.Count == 0) return;
        int w = lines.Max(ui.Measure12) + 24, h = 16 * lines.Count + 16;
        // from the top of the row below (covering that row's name whole, no sliver of it above the box)
        var box = new Rectangle(r.X + 84, Math.Min(r.Y + 232 + 16 * slot + 16, 472 - h), w, h);
        // solid under the restyled skins' see-through windows, so the window's own lines don't show through
        if (Ui.Modern) ui.ModernBox(sb, box, new Color(8, 10, 18), 8, 1f, shadow: false);
        else if (Ui.Gilded) ui.Fill(sb, box, new Color(8, 10, 18));
        ui.Panel(sb, box, colour);
        for (int k = 0; k < lines.Count; k++) ui.Text12(sb, lines[k], box.X + 12, box.Y + 8 + 16 * k, text);
    }

    /// <summary>
    /// The abilities page: each ability's name (??? when it has none) and TP cost, a row each, the chosen one marked; its
    /// name and description (??? when there is none) in a box just above the window.
    /// </summary>
    static void SkillsPage(SpriteBatch sb, Ui ui, Battlefield field, BattleUnit u, Rectangle r, int skill, int top, Color text, Ui.PanelColor colour)
    {
        var skills = Skills(field, u);
        static string Named(string s) => string.IsNullOrWhiteSpace(s) ? "???" : s.Trim();
        if (skills.Count == 0) { ui.Text12(sb, Lang.T("No abilities", "技なし"), r.X + 12, r.Y + SkillTop, text); return; }
        for (int row = 0; row < SkillRows && top + row < skills.Count; row++)
        {
            int i = top + row, y = r.Y + SkillTop + 16 * row;
            if (i == skill) ui.Fill(sb, new Rectangle(r.X + 8, y - 1, r.Width - 16, 16), text * 0.25f);
            ui.Text12(sb, Named(skills[i].Name), r.X + 12, y, text);
            ui.Text12(sb, $"{Lang.T("TP", "ＴＰ")} {skills[i].MpCost,2}", r.X + 196, y, text);
        }
        // more above or below: a small mark at the list's edge
        if (top > 0) ui.Text12(sb, "▲", r.Right - 24, r.Y + SkillTop - 14, text);
        if (top + SkillRows < skills.Count) ui.Text12(sb, "▼", r.Right - 24, r.Y + SkillTop + 16 * SkillRows - 2, text);

        if (skill < 0 || skill >= skills.Count) return;
        var m = skills[skill];
        string[] lines = { Named(m.Name) + "：", Named(m.Desc) };
        int w = Math.Max(r.Width, lines.Max(ui.Measure12) + 24), h = 16 * lines.Length + 16;
        var box = new Rectangle(Math.Clamp(r.X, 4, 636 - w), Math.Max(4, r.Y - h - 4), w, h);
        if (Ui.Modern) ui.ModernBox(sb, box, new Color(8, 10, 18), 8, 1f, shadow: false);
        else if (Ui.Gilded) ui.Fill(sb, box, new Color(8, 10, 18));
        ui.Panel(sb, box, colour);
        for (int k = 0; k < lines.Length; k++) ui.Text12(sb, lines[k], box.X + 12, box.Y + 8 + 16 * k, text);
    }

    /// <summary>Arrows at the window's sides, just outside it, that grow and shrink a pixel every few ticks: another page.</summary>
    static void Arrows(SpriteBatch sb, Ui ui, Rectangle r, int frame)
    {
        int size = 5 + new[] { 0, 1, 2, 1 }[frame / 8 % 4];
        int cy = r.Y + r.Height / 2;
        var c = new Color(236, 228, 120);
        for (int i = 0; i < size; i++)
        {
            ui.Fill(sb, new Rectangle(r.X - 3 - size + i, cy - i, 1, 2 * i + 1), c);
            ui.Fill(sb, new Rectangle(r.Right + 2 + size - i, cy - i, 1, 2 * i + 1), c);
        }
    }

    /// <summary>The status key again: the next piece of equipment the unit holds (weapon, armour, accessory), then none.</summary>
    public static int NextShown(BattleUnit u, int shown)
    {
        int[] held = { u.Weapon, u.Armor, u.Accessory };
        for (int s = shown + 1; s < 3; s++) if (held[s] >= 0) return s;
        return -1;
    }
}
