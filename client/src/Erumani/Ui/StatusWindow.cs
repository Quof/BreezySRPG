using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani.Ui;

/// <summary>Unit status window (screenshot 6 layout, approximate): blue panel with WORD.DAT labels and the face.
/// Shared by the battle map (right click) and the camp's unit screen.</summary>
public static class StatusWindow
{
    public static readonly Rectangle Default = new(188, 96, 264, 288);

    public static void Draw(SpriteBatch sb, Ui ui, Textures tex, Database db, Battlefield field, BattleUnit u, Rectangle? at = null)
    {
        var r = at ?? Default;
        ui.Panel(sb, r, Ui.PanelColor.Blue);
        var st = field.Stats(u);
        var cls = field.Class(u);
        string Word(int i) => i >= 0 && i < db.Words.Length ? db.Words[i] : "";
        void T(string s, int x, int y) { if (ui.SmallText.Get(s) is { } t) sb.Draw(t, new Vector2(r.X + x, r.Y + y), Ui.PanelText); }
        T(u.Name, 12, 10);
        T(cls.Name, 12, 38);
        T($"{Word(1)}  {u.Level} ({u.Exp})", 150, 38);
        T($"{Word(2)}  {u.Hp}／{u.MaxHp}", 12, 58);
        T($"{Word(3)}  {u.Mp}／{u.MaxMp}", 150, 58);
        int[] vals = { st.Attack, st.Speed, st.Defense, st.MagicDefense, st.Str, st.Mag, st.Skl, st.Sta };
        for (int i = 0; i < 8; i++) T($"{Word(4 + i)}  {vals[i]}", 12, 90 + i * 16 + (i >= 4 ? 8 : 0));
        string It(int id) => id >= 0 && id < db.Items.Length ? db.Items[id].Name : "―";
        T($"{Word(12)}  {It(u.Weapon)}", 12, 236);
        T($"{Word(13)}  {It(u.Armor)}", 12, 252);
        T($"{Word(14)}  {It(u.Accessory)}", 12, 268);
        int face = db.Units[Math.Clamp(u.Unit, 0, db.Units.Length - 1)].Face;
        if (face > 0 && tex.Get($"FACE_{face:000}.BMP") is { } f) sb.Draw(f, new Vector2(r.X + 128, r.Y + 90), Color.White);
    }
}
