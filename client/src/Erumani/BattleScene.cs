using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani;

/// <summary>
/// The full-screen battle scene for an attack or a spell (FUN_00419574, its steps built by FUN_0041a9c4, FUN_0041b6ec and
/// FUN_0041ba0c). Data: ANIME.DAT frames hold up to 4 layers of (x, y, cell), the cell's centre, of 192 × 192 cells in a
/// strip bitmap — CA{animation + 1:00}_{class graphic:000}.BMP for weapon and cast animations, MAGA_{animation −
/// 79:000}.BMP for spells. The computer's units stand on the left, everyone else's on the right; animations are
/// authored with the actor on the right (spells landing on the left) and mirrored about the stage's middle.
///
/// A scene is a list of steps — a strike (the attacker and its target), a cast (the caster alone) or a spell landing on
/// one unit (that unit alone) — each run on a counter c from 0 to its length, a game tick each:
/// - the palette's brightness 0 for c 0–1, then 50, 75, 100, and down 25 a tick over the last 4 ticks;
/// - the background (BACK_nnn of the target's terrain, the caster's for a cast) and the units slide in from the acting
///   side's end, 106 px a tick over the first 10 ticks, and out the other way over the last 15;
/// - the animation runs from c 15, a frame a tick, then holds its last frame;
/// - from 15 ticks after its hit frame a unit that's hit flashes white every other tick for the animation's flash time
///   and, if damaged, shakes 8 px away every other pair of ticks; one missed sidesteps 4, 8, 8, 4 px;
/// - from 19 ticks after it the damage (purple) or recovery (red) number stands at the unit's feet, bouncing for 11
///   ticks, until 20 ticks before the end, and the HP bar moves linearly over amount × 37 / max HP ticks; a cast's TP
///   drops at 16 ticks after;
/// - a unit brought to 0 blinks out between 40 and 30 ticks before the end; its death message, if it has one, takes
///   the panels' place for the last 80 ticks (the DOWN sound at 58 before the end). The scene never waits for a click.
/// Lengths: a strike the animation + 45 ticks, a spell's damage or recovery + 35 — both plus the drain beyond 5 ticks,
/// 25 more when the unit falls and 30 more for its death message — a cast + 20, any other spell + 25.
/// </summary>
public sealed class BattleScene
{
    const int Cell = 192, StageWidth = 640, StageHeight = 320, TickFrames = 2;

    public enum StepKind { Strike, Cast, Spell }

    /// <summary>
    /// One step. <paramref name="Actor"/> acts (none for a spell's landing), <paramref name="Target"/> is acted on (none
    /// for a cast); <paramref name="Anim"/> and <paramref name="Sheet"/> are the actor's animation, or the spell's.
    /// </summary>
    public sealed record Step(StepKind Kind, BattleUnit? Actor, BattleUnit? Target, Animation Anim, string Sheet, bool Hit)
    {
        public bool Critical { get; init; }
        /// <summary>A number and an HP bar that moves (+0x611e): strikes, damage and recovery spells.</summary>
        public bool Numbered { get; init; }
        public bool Recovery { get; init; }
        /// <summary>The number shown (+0x6134): the damage as rolled, the HP actually restored.</summary>
        public int Amount { get; init; }
        /// <summary>How far the HP bar moves (+0x6138): the damage up to the HP left, or the recovery.</summary>
        public int Moved { get; init; }
        /// <summary>The target's HP afterwards (+0x6140); 0 for a spell without a number (so the exe's DOWN sound plays for those too).</summary>
        public int HpAfter { get; init; }
        /// <summary>A cast's TP afterwards (+0x6144).</summary>
        public int MpAfter { get; init; }
        public bool DeathMessage { get; init; }

        public int Drain => Target is { MaxHp: > 0 } t ? Moved * 37 / t.MaxHp : 0;
        /// <summary>15 ticks after the hit frame (the flash, the shake, the TP); the number and the bar start 4 later.</summary>
        public int HitTick => Anim.HitFrame + 15;

        public int Length
        {
            get
            {
                int frames = Anim.FrameCount;
                if (Kind == StepKind.Cast) return frames + 20;
                if (Kind == StepKind.Spell && !Numbered) return frames + 25;
                int len = frames + (Kind == StepKind.Strike ? 45 : 35);
                if (Drain > 4) len += Drain - 5;
                if (HpAfter == 0) len += 25 + (DeathMessage ? 30 : 0);
                return len;
            }
        }

        /// <summary>The stage's mirrored side flag of the exe (+0x60f0): the acting unit is the computer's (or, for a spell's landing, the target isn't).</summary>
        public bool Flag => Kind == StepKind.Spell ? Target!.Side != Side.Enemy : Actor!.Side == Side.Enemy;
    }

    enum Phase { DimMap, Steps, Done }

    readonly Database _db;
    readonly Textures _tex;
    readonly Ui.Ui _ui;
    readonly Battlefield _field;
    readonly List<Step> _steps;
    /// <summary>HP and TP the panels show, per unit, as of the current step's start.</summary>
    readonly Dictionary<BattleUnit, int> _hp = new(), _mp = new();
    int _stepIndex, _c, _sub, _phaseTicks;
    Phase _phase = Phase.DimMap;

    public Action<string>? PlaySound { get; set; }
    public bool Done => _phase == Phase.Done;
    public string DebugPhase => $"{_phase}/{_phaseTicks} step={_stepIndex} c={_c} len={Current?.Length} frames={Current?.Anim.FrameCount} hit={Current?.Anim.HitFrame} drain={Current?.Drain}";
    /// <summary>The scene runs on its own (FUN_00419574 reads no click).</summary>
    public bool WaitingForClick => false;
    /// <summary>True while only the map (darkened) should show under the scene, before it.</summary>
    public bool OverMap => _phase == Phase.DimMap;

    public BattleScene(Database db, Textures tex, Ui.Ui ui, Battlefield field, IEnumerable<Step> steps, IReadOnlyDictionary<BattleUnit, int> hpBefore)
    {
        (_db, _tex, _ui, _field) = (db, tex, ui, field);
        _steps = steps.ToList();
        foreach (var (u, hp) in hpBefore) _hp[u] = hp;
    }

    /// <summary>TP shown before the caster's step (the cost comes off at its hit, FUN_00419574).</summary>
    public void ShowMpBefore(BattleUnit u, int mp) => _mp[u] = mp;

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

    Step? Current => _phase == Phase.Steps && _stepIndex < _steps.Count ? _steps[_stepIndex] : null;

    // ---------------- update ----------------

    public void Update(bool fastForward, bool confirm)
    {
        int frames = fastForward ? 4 : 1;
        for (int i = 0; i < frames && _phase != Phase.Done; i++)
            if (++_sub >= TickFrames) { _sub = 0; Tick(); }
    }

    /// <summary>Skip to the end (modern). Results were already applied by the rules.</summary>
    public void Skip()
    {
        foreach (var st in _steps.Skip(_stepIndex)) EndStep(st);
        _stepIndex = _steps.Count;
        _phase = Phase.Done;
    }

    void Tick()
    {
        _phaseTicks++;
        switch (_phase)
        {
            // the map darkens (FUN_00410388, matched to the recordings): 5 ticks, 4 steps of 20 %, a hold
            case Phase.DimMap:
                if (_phaseTicks >= 15) { _phase = Phase.Steps; _stepIndex = 0; _c = 0; }
                break;
            case Phase.Steps:
                if (Current is not { } st) { _phase = Phase.Done; break; }
                StepTick(st);
                break;
        }
    }

    void StepTick(Step st)
    {
        _c++;
        if (st.Hit && _c == st.Anim.HitSoundFrame + 15 && st.Anim.HitSound.Length > 0) PlaySound?.Invoke(st.Anim.HitSound);
        if (!st.Hit && _c == st.Anim.MissSoundFrame + 15 && st.Anim.MissSound.Length > 0) PlaySound?.Invoke(st.Anim.MissSound);
        if (_c == st.Length - 58 && st.Kind != StepKind.Cast && st.HpAfter == 0) Sfx.Down();
        if (_c < st.Length) return;
        EndStep(st);
        _stepIndex++;
        _c = 0;
        if (_stepIndex >= _steps.Count) _phase = Phase.Done;
    }

    /// <summary>A step's end: the HP and TP it left.</summary>
    void EndStep(Step st)
    {
        if (st.Numbered && st.Target is { } t) _hp[t] = st.HpAfter;
        if (st.Kind == StepKind.Cast && st.Actor is { } a) _mp[a] = st.MpAfter;
    }

    /// <summary>The HP the panel and the dying blink go by: the bar moving after the hit (FUN_00419574).</summary>
    int ShownHp(BattleUnit u)
    {
        int start = _hp.TryGetValue(u, out var h) ? h : u.Hp;
        if (Current is not { Numbered: true } st || st.Target != u || _c <= st.HitTick + 4) return start;
        int remaining = st.HitTick + 4 + st.Drain - _c;
        if (remaining < 0 || st.Drain <= 0) return st.HpAfter;
        return st.Recovery ? st.HpAfter - st.Moved * remaining / st.Drain : st.HpAfter + st.Moved * remaining / st.Drain;
    }

    int ShownMp(BattleUnit u)
    {
        int start = _mp.TryGetValue(u, out var m) ? m : u.Mp;
        return Current is { Kind: StepKind.Cast } st && st.Actor == u && _c > st.HitTick ? st.MpAfter : start;
    }

    // ---------------- drawing ----------------

    /// <summary>The map's darkening before the scene (an overlay over the map).</summary>
    public float MapDim => _phase == Phase.DimMap ? (_phaseTicks < 5 ? 0 : Math.Min(4, _phaseTicks - 4) * 0.2f) : 0f;

    /// <summary>The step's animation is playing: the tick is 33 ms slowed by GAME.DAT [4] from c 15 to 15 before the end.</summary>
    public bool Animating => Current is { } st && _c >= 15 && _c < st.Length - 15;

    // the critical hit's brightness for 10 ticks from its hit (DAT_0043341c; the exe indexes it by the counter alone,
    // reading other stack memory unless the hit frame is 24–33, so this is the evident intent)
    static readonly int[] CriticalFlash = { 140, 180, 80, 160, 100, 180, 60, 90, 180, 200 };

    /// <summary>The palette's brightness while a step runs (FUN_00419574), null before the steps.</summary>
    public int? Brightness
    {
        get
        {
            if (_phase == Phase.Done) return 0;
            if (Current is not { } st) return null;
            int len = st.Length;
            int b = _c < 2 ? 0 : _c < 5 ? _c * 25 : _c < len - 4 ? 100 : Math.Max(0, (len - _c) * 25);
            if (st.Critical && _c >= st.HitTick && _c < st.HitTick + 10) b = CriticalFlash[_c - st.HitTick];
            return b;
        }
    }

    public void Draw(SpriteBatch sb)
    {
        if (OverMap)
        {
            _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * MapDim);
            return;
        }
        _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black);
        if (Current is not { } st) return;
        int len = st.Length;
        // the slide: in from the acting side's end over 10 ticks, out the other way over the last 15
        int dir = st.Flag ? 1 : -1;
        int slide = _c < 10 ? dir * (10 - _c) * 106 : _c >= len - 15 ? -dir * (_c - (len - 15)) * 106 : 0;
        DrawStage(sb, st, slide);
        if (st.DeathMessage && _c > len - 80 && st.Target is { } dying) DrawDeathMessage(sb, dying);
        else DrawPanels(sb, st);
    }

    void DrawStage(SpriteBatch sb, Step st, int slide)
    {
        var ground = st.Kind == StepKind.Cast ? st.Actor! : st.Target!;
        int back = _field.TerrainAt(ground.X, ground.Y).BattleBackground;
        if (back > 0 && _tex.Get($"BACK_{back:000}.BMP", transparent: false) is { } bg) sb.Draw(bg, new Vector2(slide, 0), Color.White);
        if (st.Kind != StepKind.Cast && st.Target is { } t) DrawTarget(sb, st, t, slide);
        DrawActor(sb, st, slide);
        DrawNumber(sb, st, slide);
    }

    /// <summary>The unit acted on: its own animation's first frame, shaken, flashing or blinking out (FUN_00419574).</summary>
    void DrawTarget(SpriteBatch sb, Step st, BattleUnit t, int slide)
    {
        bool left = !st.Flag;   // the exe mirrors the target when its flag is clear
        int away = left ? -1 : 1, k = _c - st.HitTick, shift = 0;
        if (st.Numbered && !st.Recovery)
        {
            if (!st.Hit) shift = k is 0 or 3 ? 4 : k is 1 or 2 ? 8 : 0;
            else if (k >= 0 && k < st.Anim.FlashTime && (_c & 3) < 2) shift = 8;
        }
        if (ShownHp(t) == 0 && (_c > st.Length - 30 || (_c > st.Length - 40 && _c % 2 == 0))) return;
        bool flash = st.Hit && k >= 0 && k < st.Anim.FlashTime && _c % 2 == 0;
        var a = WeaponAnim(_db, _field, t);
        string name = SheetFor(_db, _field, t, a);
        var sheet = flash ? _tex.Silhouette(name) : _tex.Get(name);
        if (sheet is null || a.Frames.Length == 0) return;
        foreach (var l in a.Frames[0])
        {
            if (l.Cell < 0) continue;
            DrawCell(sb, sheet, l.Cell, (left ? StageWidth - l.X : l.X) + away * shift + slide, l.Y, flip: left);
        }
    }

    /// <summary>The actor's animation (or the spell's), from c 15 a frame a tick, then its last frame.</summary>
    void DrawActor(SpriteBatch sb, Step st, int slide)
    {
        var sheet = _tex.Get(st.Sheet);
        if (sheet is null || st.Anim.Frames.Length == 0) return;
        int f = Math.Clamp(Math.Min(st.Anim.FrameCount - 1, _c - 15), 0, st.Anim.Frames.Length - 1);
        bool mirror = st.Flag;   // the exe mirrors the actor's (or the spell's) animation when the flag is set
        foreach (var l in st.Anim.Frames[f])
        {
            if (l.Cell < 0) continue;
            DrawCell(sb, sheet, l.Cell, (mirror ? StageWidth - l.X : l.X) + slide, l.Y, flip: mirror);
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

    // the number's bounce for its first 11 ticks, px up from its resting place (DAT_00433444)
    static readonly int[] Bounce = { 72, 60, 40, 16, 0, 8, 20, 30, 30, 20, 8 };

    /// <summary>
    /// The number (FUN_00419574): SYSTEM.BMP's 24 × 32 digits, purple at y 128 or red at y 160 for recovery, two at most —
    /// the ones' at the x of the unit's first layer (mirrored on the left), the tens' 24 px before it — 64 px below that
    /// layer's centre but no lower than y 288, bouncing for 11 ticks, from 19 ticks after the hit to 20 before the end.
    /// </summary>
    void DrawNumber(SpriteBatch sb, Step st, int slide)
    {
        if (!st.Numbered || st.Target is not { } t || _c < st.HitTick + 4 || _c > st.Length - 20) return;
        var a = WeaponAnim(_db, _field, t);
        int x = 0x70, y = 0xd0;
        if (a.Frames.Length > 0 && a.Frames[0].FirstOrDefault(l => l.Cell >= 0) is { Cell: >= 0 } layer) (x, y) = (layer.X, layer.Y);
        if (!st.Flag) x = StageWidth - x;
        y = Math.Min(y + 64, 288);
        int k = _c - (st.HitTick + 4);
        if (k < Bounce.Length) y -= Bounce[k];
        var sys = _tex.Get("SYSTEM.BMP");
        if (sys is null) return;
        int row = st.Recovery ? 160 : 128, n = Math.Clamp(st.Amount, 0, 99);
        sb.Draw(sys, new Vector2(x + slide, y), new Rectangle(n % 10 * 24, row, 24, 32), Color.White);
        if (n > 9) sb.Draw(sys, new Vector2(x + slide - 24, y), new Rectangle(n / 10 * 24, row, 24, 32), Color.White);
    }

    /// <summary>
    /// The info panels (FUN_0041d214 → FUN_0041c960): 320 × 160 at (0,320) and (320,320) — the step's units by their
    /// side, black where a cast or a spell's landing has nobody — in the unit's side's colour (blue for the player's,
    /// red for the computer's, yellow for neutral ones), text in its light swatch (GDI positions): the name, 16 px bold,
    /// at (12,8) over an etched rule at y 28; ＨＰ at (12,32) and 「%2d／%2d」 at 272 over its bar at y 48; ＴＰ at (12,72)
    /// and its numbers over the bar at y 88; then 攻撃力 物理防御 特殊補正値 / 攻撃精度 特殊防御 地形効果 at x 12, 118, 224 on
    /// y 116 / 132, each 「%4d」 60 px further on.
    /// </summary>
    void DrawPanels(SpriteBatch sb, Step st)
    {
        BattleUnit? right = null, leftUnit = null;
        void Put(BattleUnit? u, bool onLeft) { if (onLeft) leftUnit = u; else right = u; }
        switch (st.Kind)
        {
            case StepKind.Strike: Put(st.Actor, st.Flag); Put(st.Target, !st.Flag); break;
            case StepKind.Cast: Put(st.Actor, st.Flag); break;
            default: Put(st.Target, !st.Flag); break;
        }
        foreach (var (unit, r) in new[] { (leftUnit, new Rectangle(0, 320, 320, 160)), (right, new Rectangle(320, 320, 320, 160)) })
        {
            if (unit is not { } u) continue;   // nobody: the black it's cleared to
            var color = u.Side switch { Side.Neutral => Ui.Ui.PanelColor.Yellow, Side.Enemy => Ui.Ui.PanelColor.Red, _ => Ui.Ui.PanelColor.Blue };
            _ui.Panel(sb, r, color);
            var (light, dark) = Ui.Ui.EdgeColors(color);
            var text = Ui.Ui.Modern ? Ui.Ui.PanelText : light;
            void T(string s, int x, int y) => _ui.Text12(sb, s, r.X + x, r.Y + y, text);
            // the name through GDI's DrawText (FUN_00407954), its corner at the rectangle's
            if (_ui.NameText.Get(u.Name) is { } name) sb.Draw(name, new Vector2(r.X + 12, r.Y + 8), text);
            _ui.Fill(sb, new Rectangle(r.X + 12, r.Y + 28, 296, 1), dark);
            _ui.Fill(sb, new Rectangle(r.X + 12, r.Y + 29, 296, 1), light);
            int hp = Math.Max(0, ShownHp(u)), mp = ShownMp(u);
            T(_db.Words[2], 12, 32);
            T($"{hp,2}／{u.MaxHp,2}", 272, 32);
            Bar(sb, r.X + 12, r.Y + 48, hp, u.MaxHp, 0, light, dark);
            T(_db.Words[3], 12, 72);
            T($"{mp,2}／{u.MaxMp,2}", 272, 72);
            Bar(sb, r.X + 12, r.Y + 88, mp, u.MaxMp, 16, light, dark);
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
                    int x = 12 + col * 106, y = 116 + row * 16;
                    T(_db.Words[stats[row, col].Word], x, y);
                    T($"{stats[row, col].Value,4}", x + 60, y);
                }
        }
    }

    /// <summary>
    /// An HP / TP bar (FUN_0041c960, its pieces put together by FUN_00411d74): 296 × 16 of SYSTEM.BMP's 8 × 16 pieces —
    /// the empty one at (56, <paramref name="row"/>) all along, the full one at (48, row) for value × 296 / max px (all
    /// of it when there's no maximum) — in a sunken frame a pixel outside, dark top and left, light bottom and right.
    /// </summary>
    void Bar(SpriteBatch sb, int x, int y, int value, int max, int row, Color light, Color dark)
    {
        const int w = 296;
        int filled = max <= 0 ? w : w * Math.Clamp(value, 0, max) / max;
        if (_tex.Get("SYSTEM.BMP", transparent: false) is { } sys)   // copied opaque (FUN_004069a4)
            for (int px = 0; px < w; px += 8)
            {
                int full = Math.Clamp(filled - px, 0, 8);
                if (full > 0) sb.Draw(sys, new Vector2(x + px, y), new Rectangle(48, row, full, 16), Color.White);
                if (full < 8) sb.Draw(sys, new Vector2(x + px + full, y), new Rectangle(56 + full, row, 8 - full, 16), Color.White);
            }
        _ui.Fill(sb, new Rectangle(x - 1, y - 1, w + 2, 1), dark);
        _ui.Fill(sb, new Rectangle(x - 1, y + 16, w + 2, 1), light);
        _ui.Fill(sb, new Rectangle(x - 1, y - 1, 1, 18), dark);
        _ui.Fill(sb, new Rectangle(x + w, y - 1, 1, 18), light);
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
