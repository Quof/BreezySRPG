using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani;

/// <summary>
/// The full-screen battle scene played for each attack or spell (FORMAT_NOTES §7, §15, T22).
///
/// Data (confirmed): ANIME.DAT frames hold up to 4 layers of (x, y, cell); cells are 192x192 tiles
/// of a horizontal strip bitmap: CA{animation index + 1:00}_{class unit graphic:000}.BMP for weapon/cast
/// animations (the same animation drawn with each class's sprite), MAGA_{index - 79:000}.BMP for spells
/// (record 80+). Checked against every file name the game's classes and spells need.
///
/// Measured from a recording of the original (T22, 60 fps capture; the game updates at 30 Hz, so one
/// "tick" here = 2 frames of ours):
/// - Allies stand on the right, enemies on the left, whoever attacks. Animations are authored with the
///   actor on the right facing left; an actor on the left plays them mirrored. One animation frame per tick.
/// - Stage = BACK_nnn (640x320) at the top; two 320x160 info panels below (enemy red left, ally blue right).
/// - Timeline: map darkens in 4 steps and holds; screen black; panels fade in (4 ticks); the stage wipes in
///   left to right (5 ticks x 128 px); 10-tick pause; the animation; 15-tick hold. Between strikes the stage
///   wipes out, stays black 12 ticks and wipes in again (panels stay). At the end the stage wipes out, the
///   panels stay 13 ticks, and the map brightens back in 4 steps. A defeated unit's death message (UNIT.DAT)
///   replaces the panels 30 ticks after the animation and waits for a click.
/// - The target flickers as a white silhouette for flash_time frames centred on hit_frame; the damage number
///   (SYSTEM.BMP purple digits, y 128, 24 px apart, centred on the target) appears at hit_frame, drops onto
///   the ground line (cell top y 268) and bounces once. The actor keeps its last frame after the animation.
/// </summary>
public sealed class BattleScene
{
    const int Cell = 192, StageWidth = 640, StageHeight = 320, TickFrames = 2;

    /// <summary>
    /// One animation to play: who acts, on whom, and what the rules decided. <paramref name="Solo"/> puts a
    /// single unit on the stage, on its own side with only its panel: a spell plays as the caster alone, then
    /// each target alone in turn (T22 heal and spell recordings). Pause / Hold are the ticks before and after it.
    /// </summary>
    public sealed record Step(BattleUnit Actor, BattleUnit Target, Animation Anim, string Sheet,
        bool Hit, int Amount, bool Recovery, string? Caption = null, BattleUnit? Solo = null, int Pause = 10, int Hold = 15);

    enum Phase { DimMap, PanelsIn, WipeIn, Pause, Anim, Hold, WipeOut, Gap, Death, Vanish, EndHold, MapIn, Done }

    readonly Database _db;
    readonly Textures _tex;
    readonly Ui.Ui _ui;
    readonly Battlefield _field;
    readonly List<Step> _steps;
    readonly Dictionary<BattleUnit, int> _hp = new();
    /// <summary>TP shown before the caster's animation starts (the cost comes off when it begins, T22 heal).</summary>
    readonly Dictionary<BattleUnit, int> _mp = new();
    public void ShowMpBefore(BattleUnit u, int mp) => _mp[u] = mp;
    readonly BattleUnit _right, _left;
    readonly Texture2D? _back;
    int _stepIndex = -1, _frame, _sub, _phaseTicks;
    Phase _phase = Phase.DimMap;
    (int Amount, bool Recovery, bool Miss, BattleUnit On, int Age)? _number;
    BattleUnit? _dying;
    /// <summary>Units that have blinked out after their death message (no longer drawn).</summary>
    readonly HashSet<BattleUnit> _gone = new();
    bool _actorFrozen;

    public Action<string>? PlaySound { get; set; }
    public bool Done => _phase == Phase.Done;
    public string DebugPhase => $"{_phase}/{_phaseTicks} step={_stepIndex}";
    /// <summary>A death message is up, waiting for a click.</summary>
    public bool WaitingForClick => _phase == Phase.Death;
    /// <summary>True while only the map (darkened) should show under the scene: before and after it.</summary>
    public bool OverMap => _phase is Phase.DimMap or Phase.MapIn;

    public BattleScene(Database db, Textures tex, Ui.Ui ui, Battlefield field, BattleUnit first, BattleUnit second,
        IEnumerable<Step> steps, IReadOnlyDictionary<BattleUnit, int> hpBefore)
    {
        (_db, _tex, _ui, _field) = (db, tex, ui, field);
        // allies on the right, enemies on the left (T22); two allies (a heal): the actor on the right
        (_right, _left) = first.Side == Side.Ally && second.Side != Side.Ally ? (first, second)
            : second.Side == Side.Ally && first.Side != Side.Ally ? (second, first)
            : (first, second);
        _steps = steps.ToList();
        foreach (var (u, hp) in hpBefore) _hp[u] = hp;
        // background: the defender's terrain (provisional)
        var defender = _steps.Count > 0 ? _steps[0].Target : second;
        int back = field.TerrainAt(defender.X, defender.Y).BattleBackground;
        _back = back > 0 ? tex.Get($"BACK_{back:000}.BMP", transparent: false) : null;
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

    Step? Current => _stepIndex >= 0 && _stepIndex < _steps.Count ? _steps[_stepIndex] : null;

    /// <summary>The step whose units are on stage now (the next one while the stage is wiping in or black).</summary>
    Step? Shown => _phase is Phase.DimMap or Phase.PanelsIn or Phase.WipeIn or Phase.Gap
        ? (_stepIndex + 1 < _steps.Count ? _steps[_stepIndex + 1] : _steps.LastOrDefault())
        : Current ?? _steps.LastOrDefault();

    // a solo unit stands on its own side (allies right, enemies left), the other half empty (T22 spell recordings)
    BattleUnit? StageRight => Shown?.Solo is { } s ? (s.Side == Side.Ally ? s : null) : _right;
    BattleUnit? StageLeft => Shown?.Solo is { } s ? (s.Side == Side.Ally ? null : s) : _left;

    // ---------------- update ----------------

    public void Update(bool fastForward, bool confirm)
    {
        if (_phase == Phase.Death)
        {
            if (confirm) Go(Phase.Vanish);
            return;
        }
        int frames = fastForward ? 4 : 1;
        for (int i = 0; i < frames && _phase != Phase.Done && _phase != Phase.Death; i++)
            if (++_sub >= TickFrames) { _sub = 0; Tick(); }
    }

    void Go(Phase p) { _phase = p; _phaseTicks = 0; }

    /// <summary>Skip to the end (the map brightens back in). Results were already applied by the rules.</summary>
    public void Skip()
    {
        if (_phase is Phase.MapIn or Phase.Done) return;
        _stepIndex = _steps.Count;
        _number = null;
        _dying = null;
        Go(Phase.MapIn);
    }

    // Displayed HP drains toward the real value from the hit on: 1 HP per tick for weapons, 1 per 2 ticks for
    // spells (two samples in the T22 recordings; approximate).
    readonly Dictionary<BattleUnit, int> _shown = new();
    int _drainTick;

    void Drain()
    {
        bool slow = Current?.Anim.IsSpell == true;
        if (slow && ++_drainTick % 2 != 0) return;
        foreach (var (u, target) in _hp)
        {
            int shown = _shown.TryGetValue(u, out var s) ? s : target;
            if (shown != target) _shown[u] = shown + Math.Sign(target - shown);
        }
    }

    void Tick()
    {
        _phaseTicks++;
        Drain();
        if (_number is { } n) _number = n with { Age = n.Age + 1 };
        switch (_phase)
        {
            case Phase.DimMap: if (_phaseTicks >= 15) Go(Phase.PanelsIn); break;   // 5 ticks, 4 dimming steps, then a hold
            case Phase.PanelsIn: if (_phaseTicks >= 4) Go(Phase.WipeIn); break;
            case Phase.WipeIn:
                if (_phaseTicks >= 5)
                {
                    _stepIndex++;
                    _frame = 0;
                    _actorFrozen = false;
                    Go(Phase.Pause);
                }
                break;
            case Phase.Pause: if (_phaseTicks >= (Current?.Pause ?? 10)) Go(Phase.Anim); break;
            case Phase.Anim: AnimTick(); break;
            case Phase.Hold:
                if (_dying is not null && _phaseTicks >= 30) { Go(Phase.Death); Runtime.Sfx.Down(); break; }
                if (_dying is null && _phaseTicks >= (Current?.Hold ?? 15)) Go(Phase.WipeOut);
                break;
            case Phase.WipeOut:
                if (_phaseTicks >= 5)
                {
                    _number = null;
                    _dying = null;   // after a death message the next target still follows (area spells)
                    Go(_stepIndex + 1 < _steps.Count ? Phase.Gap : Phase.EndHold);
                }
                break;
            // T22 death recording: after the click the defeated unit blinks (off/on each tick, 4 times), is gone, and
            // the stage wipes out ~11 ticks later; the death message stays up meanwhile
            case Phase.Vanish:
                if (_phaseTicks == 8 && _dying is not null) _gone.Add(_dying);
                if (_phaseTicks >= 19) Go(Phase.WipeOut);
                break;
            case Phase.Gap: if (_phaseTicks >= 14) Go(Phase.WipeIn); break;   // the panels already show the next step's units
            case Phase.EndHold: if (_phaseTicks >= 13) Go(Phase.MapIn); break;
            case Phase.MapIn: if (_phaseTicks >= 4) Go(Phase.Done); break;
        }
    }

    void AnimTick()
    {
        if (Current is not { } st) { Go(Phase.Hold); return; }
        var a = st.Anim;
        if (_frame == 0) _mp.Remove(st.Actor);
        if (st.Hit && _frame == a.HitSoundFrame && a.HitSound.Length > 0) PlaySound?.Invoke(a.HitSound);
        if (!st.Hit && _frame == a.MissSoundFrame && a.MissSound.Length > 0) PlaySound?.Invoke(a.MissSound);
        if (_frame == a.HitFrame)
        {
            if (st.Hit)
            {
                _shown[st.Target] = Hp(st.Target);
                _hp[st.Target] = st.Recovery
                    ? Math.Min(st.Target.MaxHp, Hp(st.Target) + st.Amount)
                    : Math.Max(0, Hp(st.Target) - st.Amount);
                if (!st.Recovery && _hp[st.Target] == 0) _dying = st.Target;
            }
            if (st.Caption is null) _number = (st.Amount, st.Recovery, !st.Hit, st.Target, 0);
        }
        if (_frame + 1 >= Math.Max(1, a.FrameCount)) { _actorFrozen = true; Go(Phase.Hold); return; }
        _frame++;
    }

    int Hp(BattleUnit u) => _hp.TryGetValue(u, out var h) ? h : u.Hp;

    /// <summary>White-silhouette flicker: flash_time frames centred on hit_frame, with a normal frame now and then.</summary>
    bool Flashing(BattleUnit u)
    {
        if (_phase != Phase.Anim || Current is not { } st || st.Target != u || !st.Hit || st.Caption is not null && !st.Recovery) return false;
        int start = st.Anim.HitFrame - st.Anim.FlashTime / 2, k = _frame - start;
        return k >= 0 && k < Math.Max(1, st.Anim.FlashTime) && !(k % 3 == 1 && k < 8);
    }

    /// <summary>
    /// Hit knock-back (the "stutter"): while the flash lasts, the target is pushed away from the attacker by
    /// 0 px on the first tick, 11 px on the second, then 8 px until the flash ends. Not in any data file (it is
    /// engine behaviour); the offsets are read frame by frame off the T22 recording, both directions. Heals don't do it.
    /// </summary>
    int Knockback(BattleUnit u)
    {
        if (_phase != Phase.Anim || Current is not { } st || st.Target != u || !st.Hit || st.Recovery) return 0;
        int start = st.Anim.HitFrame - st.Anim.FlashTime / 2, k = _frame - start;
        if (k <= 0 || k >= Math.Max(1, st.Anim.FlashTime)) return 0;
        int away = u == StageRight ? 1 : -1;
        return away * (k == 1 ? 11 : 8);
    }

    // ---------------- drawing ----------------

    /// <summary>
    /// Darkening of the map before and after the scene, the palette's brightness (FUN_00410388, FUN_0041093c): 5 ticks
    /// after the attack is chosen it drops 20 % a tick to 20 % and stays there while the scene loads; afterwards the
    /// map comes back from 20 % in 20 % steps.
    /// </summary>
    public float MapDim => _phase switch
    {
        Phase.DimMap => _phaseTicks < 5 ? 0 : Math.Min(4, _phaseTicks - 4) * 0.2f,
        Phase.MapIn => Math.Max(0, 4 - _phaseTicks) * 0.2f,
        _ => 1f,
    };

    /// <summary>A step's animation is playing: the exe's tick is 33 ms (slowed by GAME.DAT [4]) from here on.</summary>
    public bool Animating => _phase == Phase.Anim;

    public void Draw(SpriteBatch sb)
    {
        if (OverMap)
        {
            _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * MapDim);
            return;
        }
        _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black);
        // stage, revealed / hidden by the left-to-right wipe
        int visible = _phase switch
        {
            Phase.DimMap or Phase.PanelsIn or Phase.Gap or Phase.EndHold => 0,
            Phase.WipeIn => Math.Min(StageWidth, (_phaseTicks + 1) * 128),
            Phase.WipeOut => -Math.Min(StageWidth, (_phaseTicks + 1) * 128),   // negative: black from the left
            _ => StageWidth,
        };
        if (visible != 0) DrawStage(sb, visible);
        if (_phase is Phase.Death or Phase.Vanish && _dying is not null) DrawDeathMessage(sb, _dying);
        else
        {
            float alpha = _phase == Phase.PanelsIn ? (_phaseTicks + 1) / 4f : 1f;
            DrawPanels(sb, alpha);
        }
    }

    void DrawStage(SpriteBatch sb, int visible)
    {
        var clip = visible > 0 ? new Rectangle(0, 0, visible, StageHeight) : new Rectangle(-visible, 0, StageWidth + visible, StageHeight);
        if (clip.Width <= 0) return;
        var old = sb.GraphicsDevice.ScissorRectangle;
        // the scene is drawn to the 640x480 virtual screen, so a scissor in its coordinates is exact
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, rasterizerState: Scissor);
        sb.GraphicsDevice.ScissorRectangle = clip;
        if (Background() is { } back) sb.Draw(back, Vector2.Zero, Color.White);
        var st = Current;
        bool showActor = st is not null && (_phase == Phase.Anim || (_actorFrozen && _phase is Phase.Hold or Phase.Death or Phase.Vanish or Phase.WipeOut));
        foreach (var (u, right) in new[] { (StageRight, true), (StageLeft, false) })
        {
            if (u is null || _gone.Contains(u)) continue;
            if (_phase == Phase.Vanish && u == _dying && _phaseTicks % 2 == 0) continue;   // blinking out
            bool inAnim = showActor && st!.Actor == u && !st.Anim.IsSpell;
            if (!inAnim) DrawIdle(sb, u, right);
        }
        if (showActor) DrawFrame(sb, st!);
        DrawNumber(sb);
        sb.End();
        sb.GraphicsDevice.ScissorRectangle = old;
        sb.Begin(samplerState: SamplerState.PointClamp);
    }

    /// <summary>BACK_nnn of the terrain under the unit being acted on (confirmed by the user, T22).</summary>
    Texture2D? Background()
    {
        var target = Shown?.Target ?? _left;
        int back = _field.TerrainAt(target.X, target.Y).BattleBackground;
        return back > 0 ? _tex.Get($"BACK_{back:000}.BMP", transparent: false) : _back;
    }

    static readonly RasterizerState Scissor = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    void DrawIdle(SpriteBatch sb, BattleUnit u, bool right)
    {
        var a = WeaponAnim(_db, _field, u);
        string name = SheetFor(_db, _field, u, a);
        var sheet = Flashing(u) ? _tex.Silhouette(name) : _tex.Get(name);
        if (sheet is null) return;
        foreach (var l in a.Frames[0])
        {
            if (l.Cell < 0) continue;
            // frame 0 of a weapon animation is the actor standing on the right; mirror it for the left side
            DrawCell(sb, sheet, l.Cell, (right ? l.X : StageWidth - l.X) + Knockback(u), l.Y, flip: !right);
            break;
        }
    }

    void DrawFrame(SpriteBatch sb, Step st)
    {
        var sheet = _tex.Get(st.Sheet);
        int f = Math.Min(_frame, st.Anim.Frames.Length - 1);
        if (sheet is null) return;
        // weapon/cast animations: actor authored on the right; spells: authored landing on the left
        bool mirror = st.Anim.IsSpell ? st.Target == StageRight : st.Actor == StageLeft;
        foreach (var l in st.Anim.Frames[f])
        {
            if (l.Cell < 0) continue;
            DrawCell(sb, sheet, l.Cell, mirror ? StageWidth - l.X : l.X, l.Y, flip: mirror);
        }
    }

    /// <summary>
    /// Draws one 192x192 cell centred on (cx, cy). Some sheets are a pixel short (CA05_006, ジュリア's, is 575
    /// wide), so the last cell is drawn clipped rather than skipped; skipping it hid her whole run.
    /// </summary>
    static void DrawCell(SpriteBatch sb, Texture2D sheet, int cell, int cx, int cy, bool flip)
    {
        int w = Math.Min(Cell, sheet.Width - cell * Cell);
        if (cell < 0 || w <= 0) return;
        int x = cx - Cell / 2 + (flip ? Cell - w : 0);   // a flipped cell loses its missing column on the left
        sb.Draw(sheet, new Vector2(x, cy - Cell / 2), new Rectangle(cell * Cell, 0, w, Cell), Color.White,
            0, Vector2.Zero, 1, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
    }

    // bounce of the damage number per tick, relative to resting on the ground line (T22)
    static readonly int[] Bounce = { -72, -72, -40, -16, 0, -8, -20, -30, -30, -20, -8, 0 };

    /// <summary>SYSTEM.BMP digits: 24x32 cells, digit k at x 24k; purple y 128 = damage (T22), red y 160 = recovery (guess).</summary>
    void DrawNumber(SpriteBatch sb)
    {
        if (_number is not { } n) return;
        // centre measured once per side: 159 on the left (T22 attack), 511 on the right (T22 heal)
        int cx = n.On == StageRight ? 511 : 160;
        int y = 268 + Bounce[Math.Min(n.Age, Bounce.Length - 1)];
        var sys = _tex.Get("SYSTEM.BMP");
        if (sys is null) return;
        string s = n.Miss ? "0" : n.Amount.ToString();   // a miss shows as a purple 0 (T22 recording)
        int row = n.Recovery ? 160 : 128;
        int x = cx - s.Length * 12;
        foreach (char c in s)
        {
            sb.Draw(sys, new Vector2(x, y), new Rectangle((c - '0') * 24, row, 24, 32), Color.White);
            x += 24;
        }
    }

    // bar gradients sampled from the recording (median of many columns; colours approximate)
    static readonly Color[] HpFull =
    {
        new(192, 0, 0), new(248, 64, 56), new(200, 88, 40), new(255, 208, 152), new(176, 104, 32), new(255, 224, 152),
        new(240, 248, 144), new(240, 248, 144), new(240, 248, 144), new(240, 248, 144), new(255, 216, 144), new(176, 104, 32),
        new(255, 208, 160), new(200, 88, 40), new(248, 64, 56), new(192, 0, 0),
    };
    static readonly Color[] BarEmpty =
    {
        new(128, 0, 0), new(160, 16, 16), new(216, 0, 0), new(232, 0, 0), new(216, 0, 0), new(232, 0, 0),
        new(248, 0, 0), new(248, 0, 0), new(248, 0, 0), new(248, 0, 0), new(232, 0, 8), new(216, 0, 0),
        new(232, 0, 0), new(216, 0, 0), new(152, 16, 8), new(120, 0, 0),
    };
    static readonly Color[] TpFull =
    {
        new(0, 96, 0), new(40, 152, 40), new(40, 152, 24), new(144, 248, 120), new(56, 152, 8), new(144, 248, 96),
        new(160, 248, 96), new(160, 248, 96), new(152, 248, 88), new(152, 248, 88), new(152, 248, 104), new(56, 152, 8),
        new(144, 248, 128), new(40, 152, 24), new(40, 152, 40), new(0, 96, 0),
    };

    /// <summary>
    /// Info panels (T22): name (18 px) over an etched rule; ＨＰ / ＴＰ with "cur／max" right-aligned over
    /// 16-px gradient bars (empty part red); two rows of stats: 攻撃力 物理防御 特殊補正値 / 攻撃精度 特殊防御 地形効果.
    /// </summary>
    void DrawPanels(SpriteBatch sb, float alpha)
    {
        foreach (var (unit, r) in new[] { (StageLeft, new Rectangle(0, 320, 320, 160)), (StageRight, new Rectangle(320, 320, 320, 160)) })
        {
            if (unit is not { } u) continue;
            var color = u.Side == Side.Ally ? Ui.Ui.PanelColor.Blue : Ui.Ui.PanelColor.Red;
            _ui.Panel(sb, r, color);
            var (light, dark) = Ui.Ui.EdgeColors(color);
            var text = Ui.Ui.PanelText;
            void T(Ui.GdiText font, string s, int x, int y, bool right = false)
            {
                if (font.Get(s) is not { } t) return;
                sb.Draw(t, new Vector2(r.X + (right ? x - t.Width : x), r.Y + y), text);
            }
            T(_ui.NameText, u.Name, 12, 8);
            _ui.Fill(sb, new Rectangle(r.X + 12, r.Y + 28, 296, 1), dark);
            _ui.Fill(sb, new Rectangle(r.X + 12, r.Y + 29, 296, 1), light);
            int hp = Math.Max(0, _shown.TryGetValue(u, out var shownHp) ? shownHp : Hp(u));
            T(_ui.SmallText, _db.Words[2], 14, 35);
            T(_ui.SmallText, $"{hp}／{u.MaxHp,2}", 307, 35, right: true);
            Bar(sb, r.X + 12, r.Y + 48, hp, u.MaxHp, HpFull, light, dark);
            T(_ui.SmallText, _db.Words[3], 14, 75);
            int mp = _mp.TryGetValue(u, out var m0) ? m0 : u.Mp;
            T(_ui.SmallText, $"{mp}／{u.MaxMp,2}", 307, 75, right: true);
            Bar(sb, r.X + 12, r.Y + 88, mp, u.MaxMp, TpFull, light, dark);
            var s = _field.Stats(u);
            int terrain = _field.TerrainAt(u.X, u.Y).DefenseBonus;
            (int Word, int Value)[,] stats =
            {
                { (4, s.Attack), (6, s.Defense), (9, s.Mag) },
                { (5, s.Speed), (7, s.MagicDefense), (75, terrain) },
            };
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                {
                    int x = 13 + col * 106, y = 118 + row * 16;
                    T(_ui.SmallText, _db.Words[stats[row, col].Word], x, y);
                    T(_ui.SmallText, stats[row, col].Value.ToString(), x + 84, y, right: true);
                }
        }
        if (alpha < 1) _ui.Fill(sb, new Rectangle(0, 320, 640, 160), Color.Black * (1 - alpha));
    }

    void Bar(SpriteBatch sb, int x, int y, int value, int max, Color[] full, Color light, Color dark)
    {
        const int w = 296;
        int filled = max <= 0 ? w : w * Math.Clamp(value, 0, max) / max;
        _ui.Fill(sb, new Rectangle(x, y - 1, w, 1), dark);
        for (int row = 0; row < 16; row++)
        {
            if (filled > 0) _ui.Fill(sb, new Rectangle(x, y + row, filled, 1), full[row]);
            if (filled < w) _ui.Fill(sb, new Rectangle(x + filled, y + row, w - filled, 1), BarEmpty[row]);
        }
        _ui.Fill(sb, new Rectangle(x, y + 16, w, 1), light);
    }

    /// <summary>
    /// A defeated unit's death message (UNIT.DAT; FUN_0041d24c), in the scene's 160-px strip from y 320: its face at
    /// (32, 336), the balloon at (192, 336) with its tail to the left whether or not there's a face, at most 5 lines of
    /// text 18 px into it from y 348 (recording r1: the balloon's light edge at y 337, x 193).
    /// </summary>
    void DrawDeathMessage(SpriteBatch sb, BattleUnit u)
    {
        var unit = _db.Units[Math.Clamp(u.Unit, 0, _db.Units.Length - 1)];
        if (unit.Face > 0 && _tex.Get($"FACE_{unit.Face:000}.BMP") is { } face) sb.Draw(face, new Rectangle(32, 336, 128, 128), Color.White);
        _ui.MessageBalloon(sb, new Point(192, 336), -1);
        var lines = unit.DeathMessage.Replace("\r", "").Split('\n');
        for (int i = 0; i < Math.Min(5, lines.Length); i++)
            _ui.DrawText(sb, lines[i], new Vector2(210, 348 + i * Ui.Ui.LineHeight), Ui.Ui.TextColor);
    }
}
