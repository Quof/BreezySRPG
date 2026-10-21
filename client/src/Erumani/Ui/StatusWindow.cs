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
/// </summary>
public static class StatusWindow
{
    public static readonly Rectangle Default = new(188, 96, 264, 288);

    public static void Draw(SpriteBatch sb, Ui ui, Textures tex, Database db, Battlefield field, BattleUnit u, Rectangle? at = null)
    {
        var r = at ?? Default;
        var colour = u.Side switch { Side.Neutral => Ui.PanelColor.Yellow, Side.Enemy => Ui.PanelColor.Red, _ => Ui.PanelColor.Blue };
        ui.Panel(sb, r, colour);
        var (light, dark) = Ui.EdgeColors(colour);
        var text = Ui.Modern ? Ui.PanelText : light;
        var st = field.Stats(u);
        string Word(int i) => i >= 0 && i < db.Words.Length ? db.Words[i] : "";
        void T(string s, int x, int y) => ui.Text12(sb, s, r.X + x, r.Y + y, text);
        void Line(int x, int y, int w, int h, Color c) => ui.Fill(sb, new Rectangle(r.X + x, r.Y + y, w, h), c);

        int face = db.Units[Math.Clamp(u.Unit, 0, db.Units.Length - 1)].Face;
        if (face > 0 && tex.Get($"FACE_{face:000}.BMP") is { } f) sb.Draw(f, new Vector2(r.X + 124, r.Y + 92), Color.White);
        Line(123, 91, 130, 1, dark);
        Line(123, 220, 130, 1, light);
        Line(123, 91, 1, 130, dark);
        Line(252, 91, 1, 130, light);

        Line(8, 7, 246, 1, light);
        Line(8, 36, 246, 1, dark);
        Line(8, 7, 1, 28, light);
        Line(255, 7, 1, 28, dark);
        // the name through GDI's DrawText (FUN_00407954), its corner at the rectangle's
        if (ui.NameText.Get(u.Name) is { } name) sb.Draw(name, new Vector2(r.X + 12, r.Y + 14), text);

        string cls = field.Class(u).Name;
        T(cls, 12, 40);
        T(cls, 13, 40);
        T(Word(1), 144, 40);
        T($"   {u.Level,2} ({u.Exp,2})", 180, 40);
        T(Word(2), 12, 64);
        T($"  {u.Hp,2}／{u.MaxHp,2}", 60, 64);
        T(Word(3), 144, 64);
        T($"  {u.Mp,2}／{u.MaxMp,2}", 192, 64);

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
    }
}
