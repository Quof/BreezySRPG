using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani;

/// <summary>
/// The full-screen battle scene played for each attack or spell (FORMAT_NOTES §7, §15).
///
/// Data (confirmed): ANIME.DAT frames hold up to 4 layers of (x, y, cell); cells are 192x192 tiles
/// of a horizontal strip bitmap: CA{animation index + 1:00}_{class unit graphic:000}.BMP for weapon/cast
/// animations (the same animation drawn with each class's sprite), MAGA_{index - 79:000}.BMP for spells
/// (record 80+). Checked against every file name the game's classes and spells need. BACK_nnn.BMP backgrounds are 640x320.
///
/// Inferred: x/y are the cell centre on a 640-wide stage. Weapon animations are authored with the
/// attacker on the right (x 480) facing left and the target on the left (x 160). The unit that started
/// the fight stays on the right; a counter-attack plays the animation mirrored.
///
/// Guesses (no reference yet, T22): 3 game frames per animation frame; the target's idle pose is
/// frame 0 of its own weapon animation, mirrored; damage digits from SYSTEM.BMP (red y 160 = damage,
/// blue y 128 = recovery); HP panels under the background; the target flashes at the hit frame.
/// </summary>
public sealed class BattleScene
{
    const int Cell = 192, StageWidth = 640, TicksPerFrame = 3;

    /// <summary>One animation to play: who acts, on whom, and what the rules decided.</summary>
    public sealed record Step(BattleUnit Actor, BattleUnit Target, Animation Anim, string Sheet,
        bool Hit, int Amount, bool Recovery, string? Caption = null);

    readonly Database _db;
    readonly Textures _tex;
    readonly Ui.Ui _ui;
    readonly Battlefield _field;
    readonly Queue<Step> _steps;
    readonly Dictionary<BattleUnit, int> _hp = new();
    readonly BattleUnit _right, _left;
    readonly Texture2D? _back;
    Step? _step;
    int _frame, _tick, _numberTicks, _endWait;
    (int Amount, bool Recovery, bool Miss, BattleUnit On)? _number;
    BattleUnit? _flashing;
    int _flashTicks;

    public Action<string>? PlaySound { get; set; }
    public bool Done { get; private set; }

    public BattleScene(Database db, Textures tex, Ui.Ui ui, Battlefield field, BattleUnit right, BattleUnit left,
        IEnumerable<Step> steps, IReadOnlyDictionary<BattleUnit, int> hpBefore)
    {
        (_db, _tex, _ui, _field, _right, _left) = (db, tex, ui, field, right, left);
        _steps = new Queue<Step>(steps);
        foreach (var (u, hp) in hpBefore) _hp[u] = hp;
        // background: the defender's terrain (provisional)
        int back = field.TerrainAt(left.X, left.Y).BattleBackground;
        _back = back > 0 ? tex.Get($"BACK_{back:000}.BMP", transparent: false) : null;
        Next();
    }

    // ---------------- building steps ----------------

    /// <summary>The weapon (or bare-handed) animation of a unit: CLASS anim A/B/C by the weapon's type, else "no weapon".</summary>
    public static Animation WeaponAnim(Database db, Battlefield f, BattleUnit u)
    {
        var c = f.Class(u);
        var w = u.Weapon >= 0 && u.Weapon < db.Items.Length && db.Items[u.Weapon].Type == ItemType.Weapon ? db.Items[u.Weapon] : null;
        int a = w is null ? c.AnimNoWeapon : w.S.WpnAnimation switch { 1 => c.AnimWeaponB, 2 => c.AnimWeaponC, _ => c.AnimWeaponA };
        return db.Animations[Math.Clamp(a, 0, db.Animations.Length - 1)];
    }

    public static string SheetFor(Database db, Battlefield f, BattleUnit u, Animation a) =>
        a.IsSpell ? $"MAGA_{a.Index - 79:000}.BMP" : $"CA{a.Index + 1:00}_{f.Class(u).UnitGraphic:000}.BMP";

    // ---------------- update ----------------

    void Next()
    {
        _step = _steps.Count > 0 ? _steps.Dequeue() : null;
        _frame = 0;
        _tick = 0;
        if (_step is null) _endWait = 30;
    }

    public void Update(bool fastForward, bool confirm)
    {
        int speed = fastForward ? 4 : 1;
        for (int s = 0; s < speed; s++) Tick();
        if (confirm && _step is null) _endWait = 0;
        if (_step is null && _endWait <= 0) Done = true;
    }

    void Tick()
    {
        if (_numberTicks > 0 && --_numberTicks == 0) _number = null;
        if (_flashTicks > 0 && --_flashTicks == 0) _flashing = null;
        if (_step is not { } st) { _endWait--; return; }
        if (++_tick < TicksPerFrame) return;
        _tick = 0;
        var a = st.Anim;
        if (st.Hit && _frame == a.HitSoundFrame && a.HitSound.Length > 0) PlaySound?.Invoke(a.HitSound);
        if (!st.Hit && _frame == a.MissSoundFrame && a.MissSound.Length > 0) PlaySound?.Invoke(a.MissSound);
        if (_frame == a.HitFrame)
        {
            if (st.Hit)
            {
                _hp[st.Target] = st.Recovery
                    ? Math.Min(st.Target.MaxHp, Hp(st.Target) + st.Amount)
                    : Math.Max(0, Hp(st.Target) - st.Amount);
                if (!st.Recovery) { _flashing = st.Target; _flashTicks = Math.Max(1, a.FlashTime) * TicksPerFrame; }
            }
            if (st.Caption is null) { _number = (st.Amount, st.Recovery, !st.Hit, st.Target); _numberTicks = 45; }
        }
        if (++_frame >= Math.Max(1, a.FrameCount)) Next();
    }

    int Hp(BattleUnit u) => _hp.TryGetValue(u, out var h) ? h : u.Hp;

    // ---------------- drawing ----------------

    public void Draw(SpriteBatch sb)
    {
        _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black);
        if (_back is not null) sb.Draw(_back, Vector2.Zero, Color.White);

        // idle poses: whoever isn't animating stands at frame 0 of their weapon animation
        foreach (var u in new[] { _right, _left })
        {
            if (u == _left && _left == _right) continue;   // one unit (a spell on oneself): drawn once, on the right
            if (_step is { } st && st.Actor == u && !st.Anim.IsSpell) continue;   // it's in the animation itself
            if (_hp.TryGetValue(u, out var h) && h <= 0 && (_flashing != u)) continue;
            DrawIdle(sb, u, u == _right);
        }
        if (_step is { } s) DrawFrame(sb, s);
        DrawNumber(sb);
        DrawPanels(sb);
    }

    bool Visible(BattleUnit u) => _flashing != u || _flashTicks / 2 % 2 == 0;

    void DrawIdle(SpriteBatch sb, BattleUnit u, bool right)
    {
        if (!Visible(u)) return;
        var a = WeaponAnim(_db, _field, u);
        var sheet = _tex.Get(SheetFor(_db, _field, u, a));
        if (sheet is null) return;
        foreach (var l in a.Frames[0])
        {
            if (l.Cell < 0) continue;
            // frame 0 of a weapon animation is the attacker standing on the right; mirror it for the left side
            DrawCell(sb, sheet, l.Cell, right ? l.X : StageWidth - l.X, l.Y, flip: !right);
            break;
        }
    }

    void DrawFrame(SpriteBatch sb, Step st)
    {
        var sheet = _tex.Get(st.Sheet);
        if (sheet is null || _frame >= st.Anim.Frames.Length) return;
        // weapon/cast animations are authored right-to-left (actor right); spell animations hit the left side.
        // Mirror when the actor stands on the left, or a spell lands on the right-hand unit.
        bool mirror = st.Anim.IsSpell ? st.Target == _right && _left != _right || (_left == _right) : st.Actor == _left;
        foreach (var l in st.Anim.Frames[_frame])
        {
            if (l.Cell < 0) continue;
            DrawCell(sb, sheet, l.Cell, mirror ? StageWidth - l.X : l.X, l.Y, flip: mirror);
        }
    }

    static void DrawCell(SpriteBatch sb, Texture2D sheet, int cell, int cx, int cy, bool flip)
    {
        if ((cell + 1) * Cell > sheet.Width) return;
        sb.Draw(sheet, new Vector2(cx - Cell / 2, cy - Cell / 2), new Rectangle(cell * Cell, 0, Cell, Cell), Color.White,
            0, Vector2.Zero, 1, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
    }

    /// <summary>SYSTEM.BMP digits: 24x32 cells, digit k at x 24k; y 160 red (damage), y 128 blue (recovery).</summary>
    void DrawNumber(SpriteBatch sb)
    {
        if (_number is not { } n) return;
        float cx = n.On == _right ? 480 : 160, cy = 150 - (45 - _numberTicks) / 3f;
        if (n.Miss)
        {
            if (_ui.Text.Get("MISS") is { } t) sb.Draw(t, new Vector2(cx - t.Width / 2, cy), Color.White);
            return;
        }
        var sys = _tex.Get("SYSTEM.BMP");
        if (sys is null) return;
        string s = n.Amount.ToString();
        int y = n.Recovery ? 128 : 160;
        float x = cx - s.Length * 12;
        foreach (char c in s)
        {
            sb.Draw(sys, new Vector2(x, cy), new Rectangle((c - '0') * 24, y, 24, 32), Color.White);
            x += 22;
        }
    }

    /// <summary>Name, HP and an HP bar for both sides (left panel = left unit). Layout provisional.</summary>
    void DrawPanels(SpriteBatch sb)
    {
        foreach (var (u, r) in new[] { (_left, new Rectangle(0, 320, 320, 160)), (_right, new Rectangle(320, 320, 320, 160)) })
        {
            _ui.Panel(sb, r, u.Side == Side.Ally ? Ui.Ui.PanelColor.Blue : Ui.Ui.PanelColor.Red);
            if (_ui.Text.Get(u.Name) is { } name) sb.Draw(name, new Vector2(r.X + 24, r.Y + 20), Ui.Ui.PanelText);
            int hp = Math.Max(0, Hp(u));
            if (_ui.Text.Get($"{_db.Words[2]}  {hp}／{u.MaxHp}") is { } t) sb.Draw(t, new Vector2(r.X + 24, r.Y + 56), Ui.Ui.PanelText);
            var bar = new Rectangle(r.X + 24, r.Y + 96, r.Width - 48, 12);
            _ui.Fill(sb, bar, new Color(16, 16, 0));
            if (u.MaxHp > 0)
                _ui.Fill(sb, new Rectangle(bar.X + 1, bar.Y + 1, (bar.Width - 2) * hp / u.MaxHp, bar.Height - 2),
                    hp * 4 <= u.MaxHp ? new Color(248, 80, 64) : new Color(96, 232, 96));
        }
    }
}
