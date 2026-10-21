using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani;

/// <summary>Per-frame input for the battle map.</summary>
public readonly record struct BattleInput(
    bool Confirm, bool Cancel, bool Up, bool Down, bool Left, bool Right,
    Point? MouseTile, bool MouseMoved, bool Click, bool RightClick, bool FastForward, bool Info = false, int Wheel = 0);

public enum MapOutcome { None, Won, Lost, GameOver, ReturnToTitle }

/// <summary>
/// Battle-map flow: events (opening, turn, contact, attack, death, victory triggers), the
/// player phase (select, move, act), the enemy phase (AI), and win/lose. Layout of menus and
/// popups is provisional where no reference screenshot exists (FORMAT_NOTES §13).
/// </summary>
public sealed class BattleController
{
    enum State { Events, Idle, Selected, Moving, ActionMenu, Targeting, Showing, TurnMenu, EnemyPhase, Status, Ended,
        SpellList, ItemList, Aiming, MedicineTarget, Scene, Files, QuitConfirm, EquipList, WholeMap }

    readonly MapSession _s;
    readonly EventRunner _runner;
    readonly GameState _state;
    readonly Database _db;
    readonly Ui.Ui _ui;
    readonly Textures _tex;
    readonly HashSet<string> _fired = new();
    State _state_ = State.Events;
    Point _cursor;
    BattleUnit? _sel;
    (int X, int Y) _selFrom;
    Dictionary<(int X, int Y), (int Cost, (int X, int Y) From)>? _reach;
    List<BattleUnit> _targets = new();
    int _menuIndex;
    string[] _menu = Array.Empty<string>();
    bool[] _menuEnabled = Array.Empty<bool>();
    readonly Queue<string> _popups = new();
    /// <summary>Per popup: the unit its strike killed, kept on the map until that popup is dismissed.</summary>
    readonly Queue<BattleUnit?> _popupKills = new();
    BattleUnit? _popupKill;
    string? _popup;
    int _popupFrames, _wait;
    AiPlan? _plan;
    BattleUnit? _statusUnit;

    public MapOutcome Outcome { get; private set; }
    /// <summary>One-line state summary (screenshot mode writes it next to the PNG).</summary>
    public string DebugState => $"{_state_} phase={_s.Phase} sel={_sel?.Name} at={_sel?.X},{_sel?.Y} cursor={_cursor.X},{_cursor.Y} outcome={Outcome} telop={_telop} events={_eventsRun} last={_runner.Name} [{_runner.DebugState}] plan={_plan?.Unit.Name}/{_planWalking} group={_group} pan={_s.Panning} cam={_s.Camera} moves={_s.MovesRunning} wait={_wait} fired={string.Join(",", _fired)} scene={_scene?.DebugPhase}" +
        string.Concat(_s.Field.Units.Select(u => $"\n  {u.Side} {u.Name} ({u.X},{u.Y}) HP {u.Hp} Lv {u.Level} Exp {u.Exp} hidden={u.Hidden} run={_s.Visuals[u].Running.Count}/{_s.Visuals[u].StepFrames}/{_s.Visuals[u].Pending.Count}{(u.Dead ? " dead" : "")}"));
    public bool EventRunning => _state_ == State.Events && _runner.Running;
    public bool Busy => _telop is not null || _state_ is State.Events or State.Moving or State.Showing or State.EnemyPhase or State.Scene;
    /// <summary>Asks the game to play a sound effect (EFS wav).</summary>
    public Action<string>? PlaySound { get; set; }

    // ---------------- battle scenes ----------------

    readonly Queue<BattleScene> _scenes = new();
    BattleScene? _scene;
    public bool SceneShowing => _scene is not null;
    /// <summary>Cancel would do something now (for the CANCEL sound).</summary>
    public bool AcceptsCancel => _state_ is State.WholeMap or State.Selected or State.ActionMenu or State.Targeting or State.TurnMenu or State.Status
        or State.SpellList or State.ItemList or State.Aiming or State.MedicineTarget or State.Files or State.QuitConfirm or State.EquipList;
    /// <summary>A menu has keyboard focus (W/S move in it instead of the camera).</summary>
    public bool MenuOpen => _state_ is State.WholeMap or State.ActionMenu or State.TurnMenu or State.SpellList or State.ItemList or State.Files
        or State.QuitConfirm or State.EquipList;
    public bool WaitingForClick => _scene?.WaitingForClick == true;
    /// <summary>Set to false to skip the battle scenes (popups only), e.g. a future "animations off" option.</summary>
    public bool ShowScenes { get; set; } = true;
    public bool FastForwardSkipsScenes { get; set; } = true;

    /// <summary>Battle music while a scene plays: map BGM slot 3 (player attacks), 4 (enemy attacks), 5 (a leader is involved).</summary>
    void BattleMusic(BattleUnit actor, BattleUnit other)
    {
        bool boss = (actor.Leader && actor.Side == Side.Enemy) || (other.Leader && other.Side == Side.Enemy);
        PlayMapBgm?.Invoke(boss ? 5 : actor.Side == Side.Ally ? 3 : 4);
    }

    void RunScenes()
    {
        if (_scenes.Count == 0) { NextPopup(); return; }
        _scene = _scenes.Dequeue();
        _scene.PlaySound = PlaySound;
        _state_ = State.Scene;
    }

    void UpdateScene(BattleInput i)
    {
        // skipping (modern): cancel / right click, or fast-forward when that option is on
        if (i.Cancel || i.RightClick || (i.FastForward && FastForwardSkipsScenes)) _scene!.Skip();
        _scene!.Update(i.FastForward, i.Confirm || i.Click);
        if (!_scene.Done) return;
        _scene = null;
        if (_scenes.Count > 0) { RunScenes(); return; }
        PlayMapBgm?.Invoke(_s.Field.AllyPhase ? 1 : 2);   // back to the phase music
        NextPopup();
    }

    Dictionary<BattleUnit, int> HpSnapshot(IEnumerable<BattleUnit> units) => units.Distinct().ToDictionary(u => u, u => u.Hp);

    /// <summary>Asks the game to play one of the map's BGM slots (1 player turn, 2 enemy turn, 6 victory...).</summary>
    public Action<int>? PlayMapBgm { get; set; }

    // ---------------- captions (TELOP1-3.BMP) ----------------

    // TELOP1 "PLAYER TURN", TELOP2 "ENEMY TURN", TELOP3 "COMPLETE!!" (512x96, index 0 transparent).
    // COMPLETE!! after the last victory/death event, with the fanfare, then the camp (T19e).
    // The turn captions are shipped with the game but their timing is provisional.
    string? _telop;
    public string? CurrentTelop => _telop;

    // ---------------- saving and loading (turn menu) ----------------

    /// <summary>セーブする / ロードする / ゲーム終了 from the turn menu; the game writes / reads the file.</summary>
    public Action<int>? SaveRequested { get; set; }
    public Func<int, bool>? LoadRequested { get; set; }
    public Func<int, bool>? SlotExists { get; set; }
    public Action? QuitRequested { get; set; }
    readonly Camp.MenuList _files = new() { Rect = new Rectangle(128, 24, 96, 177), Indent = 16 };
    readonly Camp.MenuList _quit = new() { Rect = new Rectangle(128, 24, 132, 84), Indent = 44, TopPad = 28 };
    bool _saving;

    /// <summary>Event slots that have run (object, slot), for a battle save's fired flags.</summary>
    public IEnumerable<(int Object, int Slot)> FiredSlots =>
        _fired.Where(n => n.Length == 8 && n.StartsWith("EV")).Select(n => (int.Parse(n[2..6]), int.Parse(n[6..8])));

    /// <summary>Continue a restored battle: no opening, the saved events stay fired, straight into the phase.</summary>
    public void Resume(IEnumerable<(int Object, int Slot)> fired)
    {
        foreach (var (o, s) in fired) _fired.Add(MapObject.ScriptName(o, s));
        _opening = false;
        if (_s.FirstPlayerUnit() is { } first) _cursor = new Point(first.X, first.Y);
        _group = 1;
        _plan = null;
        _state_ = _s.Field.AllyPhase ? State.Idle : State.EnemyPhase;
    }

    void OpenFiles(bool save)
    {
        _saving = save;
        _files.Header = Word(save ? 53 : 54);   // セーブします / ロードします
        _files.Set(Enumerable.Range(81, 8).Select(Word).ToArray(),   // ファイル１–８
            save ? null : Enumerable.Range(1, 8).Select(n => SlotExists?.Invoke(n) ?? false).ToArray());
        _files.Index = 0;
        _state_ = State.Files;
    }

    void UpdateFiles(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { _state_ = State.TurnMenu; return; }
        int pick = _files.Navigate(i, MouseVirtual);
        if (pick < 0) return;
        if (_saving) { SaveRequested?.Invoke(pick + 1); Popup(Word(53)); NextPopup(); }
        else LoadRequested?.Invoke(pick + 1);
    }

    void UpdateQuit(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { _state_ = State.TurnMenu; return; }
        if (_quit.Navigate(i, MouseVirtual) is var c and >= 0) { if (c == 0) QuitRequested?.Invoke(); else _state_ = State.TurnMenu; }
    }
    int _telopFrames;
    Action? _afterTelop;

    // T20a: captions slide in from the right and pause about a second in the middle.
    // Slide speed and the exit (out to the left) are guesses; COMPLETE!! stays in the middle.
    const int TelopSlide = 16, TelopHold = 60;
    int _telopTotal;
    bool _telopExits;

    void Telop(int n, int frames, Action then)
    {
        _telop = $"TELOP{n}.BMP";
        _telopFrames = _telopTotal = frames;
        _telopExits = n != 3;
        _afterTelop = then;
        if (n is 1 or 2) PlayMapBgm?.Invoke(n);   // player / enemy turn music (T20b)
    }

    void TurnTelop(int n, Action then) => Telop(n, TelopSlide * 2 + TelopHold, then);

    /// <summary>Caption x offset from centred: from off the right edge, to 0, then off the left edge.</summary>
    float TelopOffset(int width, Rectangle viewport)
    {
        int elapsed = _telopTotal - _telopFrames;
        float from = viewport.Width / 2f + width / 2f;
        if (elapsed < TelopSlide) return from * (1 - (float)elapsed / TelopSlide);
        if (_telopExits && _telopFrames < TelopSlide) return -from * (1 - (float)_telopFrames / TelopSlide);
        return 0;
    }

    bool UpdateTelop(BattleInput i)
    {
        if (_telop is null) return false;
        _telopFrames -= i.FastForward ? 4 : 1;
        if (_telopFrames <= 0 || i.Confirm || i.Click)
        {
            _telop = null;
            var then = _afterTelop;
            _afterTelop = null;
            then?.Invoke();
        }
        return true;
    }

    void Win()
    {
        _state_ = State.Ended;
        PlayMapBgm?.Invoke(6);
        Telop(3, TelopSlide + 540, () => End(MapOutcome.Won));   // ~9 s with the fanfare (T20d)
    }

    public BattleController(MapSession s, EventRunner runner, GameState state, Database db, Ui.Ui ui, Textures tex)
        => (_s, _runner, _state, _db, _ui, _tex) = (s, runner, state, db, ui, tex);

    string Word(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";

    // ---------------- events (FORMAT_NOTES §16: FUN_0040c24c and the triggers around it) ----------------
    //
    // One event at a time, as the original runs them: when one ends, the checkpoint looks again from the top, so what
    // an event changes decides what comes next. A fired flag per object slot keeps most kinds to a single run; switch,
    // pass-by and stop-on slots have none and run again whenever their condition holds again (the colosseum and
    // bonus-map menus, the 開発室 warp tiles rely on it).

    /// <summary>The unit whose action just ended: the checkpoint looks at its contacts first.</summary>
    BattleUnit? _actor;
    /// <summary>Pass-by / stop-on marks on "Other" objects' slots: the unit that went over or stopped on the tile.</summary>
    readonly Dictionary<(int Obj, int Slot), BattleUnit> _passMarks = new(), _stopMarks = new();
    /// <summary>What continues once the running event ends (a fight waiting on its "before" events, the "after" ones...).</summary>
    Action? _resume;
    bool _opening;
    /// <summary>The fight whose "after" (cond 11) events are still to be looked at: the attacker and the opposing units it met.</summary>
    (BattleUnit Attacker, List<BattleUnit> Others)? _after;

    public void Start(string? firstEvent)
    {
        _opening = true;
        if (_s.Map.Scripts.TryGetValue(firstEvent ?? "OPENING", out var sc)) { StartScript(sc); return; }
        OpeningDone(ran: false);
    }

    /// <summary>
    /// After the opening (FUN_0040d18c's end in state 0): the camera goes to the first player unit, then the PLAYER TURN
    /// caption, and only then the checkpoint (turn-1 events). Without an opening the caption comes straight away.
    /// </summary>
    void OpeningDone(bool ran)
    {
        _opening = false;
        if (ran && _s.FirstPlayerUnit() is { } u) _s.ShowTile(u.X, u.Y);
        _state_ = State.Idle;
        TurnTelop(1, () => { if (!CheckPoint()) _state_ = State.Idle; });
    }

    int _eventsRun;

    void StartScript(EventScript sc)
    {
        _eventsRun++;
        _runner.Start(sc);
        _state_ = State.Events;
    }

    MapObject Obj(BattleUnit u) => _s.Map.Objects.First(x => x.Index == u.MapObject);

    bool Fired(MapObject o, int slot) => _fired.Contains(MapObject.ScriptName(o.Index, slot));

    /// <summary>FUN_0040cfe0: starts a slot's script; a slot without one doesn't count and the search goes on.</summary>
    bool StartSlot(MapObject o, int slot, bool once)
    {
        string name = MapObject.ScriptName(o.Index, slot);
        if (!_s.Map.Scripts.TryGetValue(name, out var sc)) return false;
        if (once) _fired.Add(name);
        StartScript(sc);
        return true;
    }

    bool SwitchOn(int sw) => sw >= 0 && sw < GameState.SwitchCount && _state.Switches[sw];

    /// <summary>
    /// FUN_0040c24c, the checkpoint: (1) contacts after movement (cond 9), the unit that just acted first, then every
    /// unit; (2) each shown "Other" object, by index; (3) each unit on the map, by record number, a fallen one being
    /// taken off once its own slots had their chance; then (4) lost? (5) won? (victory slots, then COMPLETE!!).
    /// Every object's slots are tried from 5 down to 1. Returns true when something took over.
    /// </summary>
    bool CheckPoint()
    {
        if (_actor is { } a && Contact(a, 9)) return true;
        foreach (var u in _s.InRecordOrder.ToList())
            if (Contact(u, 9)) return true;
        foreach (var o in _s.Others.Where(_s.OtherVisible).OrderBy(o => o.Index).ToList())
            if (SlotEvents(o, null)) return true;
        foreach (var u in _s.InRecordOrder.Where(u => !u.Cleared).ToList())
        {
            if (u.MapObject >= 0 && SlotEvents(Obj(u), u)) return true;
            if (u.Dead) u.Cleared = true;
        }
        if (_s.Field.Lost()) { End(MapOutcome.GameOver); return true; }
        if (!_s.Field.Won()) return false;
        foreach (var o in _s.Others.Where(_s.OtherVisible).OrderBy(o => o.Index).ToList())
            for (int slot = 5; slot >= 1; slot--)
                if (o.Slots[slot - 1].Condition == 8 && !Fired(o, slot) && StartSlot(o, slot, once: true)) return true;
        Win();
        return true;
    }

    /// <summary>
    /// FUN_0040c584: one object's slots, 5 down to 1. A unit's object first sets its Specific pattern's switch when the
    /// unit stands on the pattern's tile. Switch (1) while ON; turn (2) on its phase; contact any time (4) while the
    /// unit lives; death (5) once it has fallen; pass-by (6) / stop-on (7) on "Other" objects after a unit's move.
    /// </summary>
    bool SlotEvents(MapObject o, BattleUnit? u)
    {
        if (u is { Dead: false }) ApplySpecificSwitch(u);
        for (int slot = 5; slot >= 1; slot--)
        {
            var sl = o.Slots[slot - 1];
            if (sl.Condition == 0 || Fired(o, slot)) continue;
            switch (sl.Condition)
            {
                case 1:
                    if (SwitchOn(sl.Switch) && StartSlot(o, slot, once: false)) return true;
                    break;
                case 2:
                    if (sl.Turn == _s.Field.Phase && StartSlot(o, slot, once: true)) return true;
                    break;
                case 4:
                    if (u is { Dead: false } && InContact(u, sl.TargetUnit) && StartSlot(o, slot, once: true)) return true;
                    break;
                case 5:
                    if (u is { Dead: true } && (sl.TargetUnit < 0 || sl.TargetUnit == u.KilledBy) && StartSlot(o, slot, once: true)) return true;
                    break;
                case 6:
                case 7:
                    var marks = sl.Condition == 6 ? _passMarks : _stopMarks;
                    if (u is null && marks.TryGetValue((o.Index, slot), out var by) && MarkMatches(sl, by) && StartSlot(o, slot, once: false))
                    {
                        marks.Remove((o.Index, slot));
                        return true;
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>A pass-by / stop-on slot fires for its target unit, or (target "Nothing") for any player unit that isn't neutral.</summary>
    static bool MarkMatches(EventSlot sl, BattleUnit by) => sl.TargetUnit >= 0 ? by.Unit == sl.TargetUnit : by.Side == Side.Ally;

    /// <summary>A Specific move pattern's "switch to apply" turns ON while the unit stands on the pattern's tile.</summary>
    void ApplySpecificSwitch(BattleUnit u)
    {
        if (u.MovePatterns.Length == 0) return;
        var p = EnemyAi.ActivePattern(u, _state.Switches);
        if (p.Action != 2 || (u.X, u.Y) != (p.X, p.Y) || p.SwitchToApply < 0 || p.SwitchToApply >= GameState.SwitchCount) return;
        _state.Switches[p.SwitchToApply] = true;
        _s.RefreshSpawns();
    }

    /// <summary>
    /// FUN_0040cad8: a living unit's contact slots (4 any time / 9 after movement), 5 down to 1, fire when a unit stands
    /// next to it: its target unit, or (target "Nothing") a unit of the other side (allies and enemies against each
    /// other; a neutral unit and the player's units count as each other's other side too).
    /// </summary>
    bool Contact(BattleUnit owner, int cond)
    {
        if (owner.Dead || owner.Hidden || owner.MapObject < 0) return false;
        var o = Obj(owner);
        for (int slot = 5; slot >= 1; slot--)
        {
            var sl = o.Slots[slot - 1];
            if (sl.Condition == cond && !Fired(o, slot) && InContact(owner, sl.TargetUnit) && StartSlot(o, slot, once: true)) return true;
        }
        return false;
    }

    bool InContact(BattleUnit owner, int target) =>
        _s.Field.Alive.Any(v => v != owner && Adjacent(v, owner) && (target >= 0 ? v.Unit == target : Opposed(owner, v)));

    static bool Opposed(BattleUnit a, BattleUnit b)
    {
        bool ca = a.Side == Side.Enemy, cb = b.Side == Side.Enemy, na = a.Side == Side.Neutral, nb = b.Side == Side.Neutral;
        return (ca != cb && !na && !nb) || (nb && !na && !ca) || (na && !nb && !cb);
    }

    /// <summary>
    /// FUN_0040ecf0, a move's end (any side): "Other" objects on every tile walked onto get a pass-by mark, the one
    /// on the final tile a stop-on mark (staying put marks both); then contacts (cond 4), the mover first. A contact
    /// event ends the mover's action. Returns true if one started.
    /// </summary>
    bool MoveEnded(BattleUnit mover, IReadOnlyList<(int X, int Y)> path)
    {
        var tiles = path.Count > 0 ? path : new List<(int X, int Y)> { (mover.X, mover.Y) };
        foreach (var t in tiles)
            foreach (var o in _s.Others.Where(o => (o.X, o.Y) == t))
                for (int slot = 1; slot <= 5; slot++) _passMarks[(o.Index, slot)] = mover;
        foreach (var o in _s.Others.Where(o => (o.X, o.Y) == (mover.X, mover.Y)))
            for (int slot = 1; slot <= 5; slot++) _stopMarks[(o.Index, slot)] = mover;
        if (Contact(mover, 4) || _s.InRecordOrder.ToList().Any(u => Contact(u, 4)))
        {
            mover.Acted = true;
            return true;
        }
        return false;
    }

    /// <summary>The units a fight or spell meets on the other side, as the exe lists them for its first-attack slots (enemies vs. everyone else).</summary>
    static List<BattleUnit> OpposingOf(BattleUnit actor, IEnumerable<BattleUnit> met) =>
        met.Where(t => (t.Side == Side.Enemy) != (actor.Side == Side.Enemy)).Distinct().ToList();

    /// <summary>
    /// FUN_0040cd98: first-attack slots (10 before / 11 after) between the attacker and one unit it meets: the
    /// attacker's own slots first, then the other's; a slot's owner must still be alive.
    /// </summary>
    bool AttackTrigger(BattleUnit attacker, BattleUnit other, int cond)
    {
        bool Check(BattleUnit owner, BattleUnit with)
        {
            if (owner.Dead || owner.Hp <= 0 || owner.MapObject < 0) return false;
            var o = Obj(owner);
            for (int slot = 5; slot >= 1; slot--)
            {
                var sl = o.Slots[slot - 1];
                if (sl.Condition == cond && !Fired(o, slot) && (sl.TargetUnit < 0 || sl.TargetUnit == with.Unit) && StartSlot(o, slot, once: true)) return true;
            }
            return false;
        }
        return Check(attacker, other) || Check(other, attacker);
    }

    /// <summary>
    /// FUN_00410388: before a fight or spell, its "before" events one at a time; the fight follows straight after
    /// them (no checkpoint in between). Returns true if one started.
    /// </summary>
    bool BeforeFight(BattleUnit attacker, List<BattleUnit> met, Action fight)
    {
        foreach (var t in met)
            if (AttackTrigger(attacker, t, 10)) { _resume = fight; return true; }
        return false;
    }

    /// <summary>
    /// FUN_0041093c / FUN_00410900: after a fight's EXP, its "after" events one at a time, then the checkpoint.
    /// </summary>
    void AfterFight()
    {
        if (_after is { } a)
            foreach (var t in a.Others)
                if (AttackTrigger(a.Attacker, t, 11)) { _resume = AfterFight; return; }
        _after = null;
        if (!CheckPoint()) Continue();
    }

    /// <summary>Back to whoever's phase it is once nothing else is going on.</summary>
    void Continue()
    {
        _sel = null;
        _state_ = _s.Field.AllyPhase ? State.Idle : State.EnemyPhase;
    }

    static bool Adjacent(BattleUnit a, BattleUnit b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    /// <summary>
    /// A map's end. Won: COMPLETE!! and the fanfare first. Lost through an event (map_complete "lose"): the original uses
    /// it to move on quietly — the same way to the next map, the army saved, without the caption.
    /// </summary>
    void End(MapOutcome o)
    {
        Outcome = o;
        _state_ = State.Ended;
        if (o is MapOutcome.Won or MapOutcome.Lost) _s.SaveRoster();
    }

    // ---------------- update ----------------

    public void Update(BattleInput input, EventInput ev)
    {
        if (input.MouseTile is { } mt && input.MouseMoved) _cursor = mt;
        if (UpdateTelop(input)) return;
        switch (_state_)
        {
            case State.Events: UpdateEvents(ev); break;
            case State.Idle: UpdateIdle(input); break;
            case State.Selected: UpdateSelected(input); break;
            case State.Moving: if (!_s.MovesRunning) OpenActionMenu(); break;
            case State.ActionMenu: UpdateActionMenu(input); break;
            case State.Targeting: UpdateTargeting(input); break;
            case State.Showing: UpdateShowing(input); break;
            case State.TurnMenu: UpdateTurnMenu(input); break;
            case State.EnemyPhase: UpdateEnemyPhase(input); break;
            case State.Status: if (input.Confirm || input.Cancel || input.Click || input.RightClick) _state_ = State.Idle; break;
            case State.SpellList: UpdateSpellList(input); break;
            case State.ItemList: UpdateItemList(input); break;
            case State.EquipList: UpdateEquipList(input); break;
            case State.Aiming: UpdateAiming(input); break;
            case State.MedicineTarget: UpdateMedicineTarget(input); break;
            case State.Scene: UpdateScene(input); break;
            case State.Files: UpdateFiles(input); break;
            case State.QuitConfirm: UpdateQuit(input); break;
            case State.WholeMap: if (input.Confirm || input.Cancel || input.Click || input.RightClick) { Sfx.Cancel(); _state_ = State.Idle; } break;
        }
    }

    void UpdateEvents(EventInput ev)
    {
        if (_runner.Running)
        {
            _runner.Update(ev);
            if (_runner.Running) return;
            switch (_runner.End)
            {
                case EventEnd.MapWon: Win(); return;
                case EventEnd.MapLost: End(MapOutcome.Lost); return;
                case EventEnd.GameOver: End(MapOutcome.GameOver); return;
                case EventEnd.ReturnToTitle: End(MapOutcome.ReturnToTitle); return;
            }
        }
        // the event is over: the opening hands over to the first turn; a waiting fight or "after" check goes on;
        // otherwise the checkpoint looks again (FUN_0040d18c's end → state 2)
        if (_opening) { OpeningDone(ran: true); return; }
        if (_resume is { } r) { _resume = null; r(); return; }
        if (CheckPoint()) return;
        Continue();
    }

    void MoveCursor(BattleInput i)
    {
        int dx = (i.Right ? 1 : 0) - (i.Left ? 1 : 0), dy = (i.Down ? 1 : 0) - (i.Up ? 1 : 0);
        if (dx == 0 && dy == 0) return;
        _cursor = new Point(Math.Clamp(_cursor.X + dx, 0, _s.Map.Width - 1), Math.Clamp(_cursor.Y + dy, 0, _s.Map.Height - 1));
        // keep the cursor on screen
        var c = new Point(_cursor.X * MapSession.Tile, _cursor.Y * MapSession.Tile);
        if (c.X < _s.Camera.X) _s.Camera.X = c.X;
        if (c.Y < _s.Camera.Y) _s.Camera.Y = c.Y;
        if (c.X + MapSession.Tile > _s.Camera.X + ErumaniGame.ViewWidth) _s.Camera.X = c.X + MapSession.Tile - ErumaniGame.ViewWidth;
        if (c.Y + MapSession.Tile > _s.Camera.Y + ErumaniGame.ViewHeight) _s.Camera.Y = c.Y + MapSession.Tile - ErumaniGame.ViewHeight;
        _s.ClampCamera();
    }

    bool Pick(BattleInput i) => i.Confirm || i.Click;

    /// <summary>
    /// FUN_0040e658, the player's phase: a click on a unit that can act shows its movement range — the player's own
    /// to move it, anyone else's just to look (FUN_0040ebcc) — while acted or sleeping units do nothing; a right
    /// click (or the info key) opens the status window; a click on an empty tile opens the turn menu.
    /// </summary>
    void UpdateIdle(BattleInput i)
    {
        MoveCursor(i);
        var u = _s.Field.UnitAt(_cursor.X, _cursor.Y);
        if ((i.RightClick || i.Info) && u is not null) { _statusUnit = u; _state_ = State.Status; return; }
        if (!Pick(i)) return;
        Sfx.Click();
        if (u is null) { OpenTurnMenu(); return; }
        if (u.Acted || u.SleepTurns > 0) return;
        _sel = u;
        _selFrom = (u.X, u.Y);
        _reach = _s.Field.Reachable(u);
        _viewOnly = u.Side != Side.Ally;
        _state_ = State.Selected;
    }

    /// <summary>The range shown belongs to an enemy or neutral unit: any click closes it.</summary>
    bool _viewOnly;

    void UpdateSelected(BattleInput i)
    {
        MoveCursor(i);
        if (i.Cancel || i.RightClick) { _state_ = State.Idle; _sel = null; return; }
        if (!Pick(i) || _sel is null || _reach is null) return;
        if (_viewOnly) { _state_ = State.Idle; _sel = null; return; }
        var t = (_cursor.X, _cursor.Y);
        if (!_reach.ContainsKey(t)) { _state_ = State.Idle; _sel = null; return; }
        var path = Battlefield.PathTo(_reach, t);
        Sfx.Click();
        _s.Walk(_sel, path);
        _pendingPath = path;
        _state_ = State.Moving;
    }
    List<(int X, int Y)> _pendingPath = new();

    /// <summary>The move is over: tile marks and contacts (a contact event ends the unit's action), else the action menu.</summary>
    void OpenActionMenu()
    {
        var u = _sel!;
        if (MoveEnded(u, _pendingPath)) { _actor = u; _sel = null; return; }
        BuildActionMenu();
        _state_ = State.ActionMenu;
    }

    void BuildActionMenu()
    {
        var u = _sel!;
        _targets = _s.Field.TargetsFrom(u, u.X, u.Y).ToList();
        // WORD.DAT: 16 攻撃, 17 必殺技, 89 装備, 18 アイテム, 15 行動終了. The original's order (T23 recording) is
        // 行動終了 / 攻撃 / 必殺技 / 装備 / アイテム; 行動終了 is moved to the bottom here (player request).
        _menu = new[] { Word(16), Word(17), Word(89), Word(18), Word(15) };
        _menuEnabled = new[] { _targets.Count > 0, _s.Field.KnownSpells(u).Any(), WeaponChoices(u).Count > 1 || WeaponChoices(u).Any(w => w.Index != u.Weapon),
            UsableItems(u).Any(), true };
        _menuIndex = _targets.Count > 0 ? 0 : 4;
    }

    // ---------------- magic and items ----------------

    List<Magic> _spells = new();
    List<Item> _items = new();
    Magic? _aimSpell;
    Item? _aimItem;
    HashSet<(int X, int Y)> _aimRange = new();
    List<BattleUnit> _medTargets = new();

    IEnumerable<Item> UsableItems(BattleUnit u) =>
        _state.Items.Where(kv => kv.Value > 0 && kv.Key >= 0 && kv.Key < _db.Items.Length)
            .Select(kv => _db.Items[kv.Key]).Where(it => _s.Field.CanUseInBattle(u, it)).OrderBy(it => it.Index);

    bool ItemReady(BattleUnit u, Item it) => it.Type is ItemType.StatUp or ItemType.ClassChange || (_s.Field.ItemSpell(it) is { } m
        ? _s.Field.SpellAims(u, m, u.X, u.Y).Any()
        : _s.Field.MedicineTargets(u, it).Any());

    /// <summary>Weapons the unit can switch to: the one in hand, then usable ones from the inventory.</summary>
    List<Item> WeaponChoices(BattleUnit u)
    {
        var cls = u.ClassId;
        var list = new List<Item>();
        if (u.Weapon >= 0 && u.Weapon < _db.Items.Length) list.Add(_db.Items[u.Weapon]);
        list.AddRange(_state.Items.Keys.Where(id => id >= 0 && id < _db.Items.Length && id != u.Weapon).Select(id => _db.Items[id])
            .Where(it => it.Type == ItemType.Weapon && cls >= 0 && cls < it.ClassUsable.Length && it.ClassUsable[cls] != 0).OrderBy(it => it.Index));
        return list;
    }

    /// <summary>装備: switch weapons without ending the action (provisional: the original's 装備 screen wasn't recorded).</summary>
    void OpenEquipList()
    {
        _items = WeaponChoices(_sel!);
        _menu = _items.Select(it => it.Name).ToArray();
        _menuEnabled = _items.Select(_ => true).ToArray();
        _menuIndex = 0;
        _itemPage = 0;
        _state_ = State.EquipList;
    }

    void UpdateEquipList(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { OpenActionMenuAgain(); return; }
        int pick = ListNavigate(i);
        if (pick < 0) return;
        var u = _sel!;
        var it = _items[pick];
        if (it.Index != u.Weapon)
        {
            if (u.Weapon >= 0) _state.AddItem(u.Weapon, 1);
            _state.AddItem(it.Index, -1);
            u.Weapon = it.Index;
        }
        OpenActionMenuAgain();
        _menuIndex = 0;
    }

    void OpenSpellList()
    {
        var u = _sel!;
        _spells = _s.Field.KnownSpells(u).ToList();
        _menu = _spells.Select(m => m.Name).ToArray();
        _menuEnabled = _spells.Select(m => Battlefield.CanCast(u, m) && _s.Field.SpellAims(u, m, u.X, u.Y).Any()).ToArray();
        _menuIndex = Math.Max(0, Array.IndexOf(_menuEnabled, true));
        _state_ = State.SpellList;
    }

    void OpenItemList()
    {
        var u = _sel!;
        _items = UsableItems(u).ToList();
        _menu = _items.Select(it => it.Name).ToArray();
        _menuEnabled = _items.Select(it => ItemReady(u, it)).ToArray();
        _menuIndex = Math.Max(0, Array.IndexOf(_menuEnabled, true));
        _itemPage = _menuIndex / ItemRows;
        _state_ = State.ItemList;
    }

    // Lists measured from the original (T21): spell list at x 352, one 16-px row per spell, "ＴＰ cost／current";
    // item list a fixed 180x240 panel at (352,224) with 12 rows per page and a "ページ n／m" footer.
    // The hovered row gets a flashing light-green/white bar; a description window sits at (80,32) 480x48.
    const int ItemRows = 12;
    int _itemPage;

    Rectangle SpellListRect()
    {
        int h = _menu.Length * 16 + 16;
        int y = _sel is null ? 240 : (int)_s.ScreenPos(_sel.X, _sel.Y, ViewportRect).Y;
        return new Rectangle(352, Math.Clamp(y, ErumaniGame.ViewY, ErumaniGame.ViewY + ErumaniGame.ViewHeight - h), 228, h);
    }

    static readonly Rectangle ItemListRect = new(352, 224, 180, 240);

    bool ItemLike => _state_ is State.ItemList or State.EquipList;

    Rectangle RowRect(int row) => ItemLike
        ? new Rectangle(ItemListRect.X + 4, ItemListRect.Y + 8 + (row % ItemRows) * 16, ItemListRect.Width - 8, 16)
        : new Rectangle(SpellListRect().X + 6, SpellListRect().Y + 8 + row * 16, SpellListRect().Width - 12, 16);

    int ListNavigate(BattleInput i)
    {
        int n = _menu.Length;
        if (n == 0) return -1;
        if (i.Up) _menuIndex = (_menuIndex + n - 1) % n;
        if (i.Down) _menuIndex = (_menuIndex + 1) % n;
        if (ItemLike)
        {
            int pages = (n + ItemRows - 1) / ItemRows;
            if (i.Left || i.Right) _menuIndex = Math.Min(n - 1, ((_menuIndex / ItemRows + (i.Right ? 1 : pages - 1)) % pages) * ItemRows);
            _itemPage = _menuIndex / ItemRows;
        }
        int first = ItemLike ? _itemPage * ItemRows : 0;
        int count = ItemLike ? Math.Min(ItemRows, n - first) : n;
        int hover = -1;
        for (int r = 0; r < count; r++)
            if (RowRect(first + r).Contains(MouseVirtual)) hover = first + r;
        if (i.MouseMoved && hover >= 0) _menuIndex = hover;
        if (i.Confirm || (i.Click && hover >= 0))
        {
            if (_menuEnabled[_menuIndex]) { Sfx.Click(); return _menuIndex; }
            Sfx.Buzzer();
        }
        return -1;
    }

    void UpdateSpellList(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { OpenActionMenuAgain(); return; }
        int pick = ListNavigate(i);
        if (pick >= 0) BeginAim(_spells[pick], null);
    }

    void UpdateItemList(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { OpenActionMenuAgain(); return; }
        int pick = ListNavigate(i);
        if (pick < 0) return;
        var it = _items[pick];
        if (it.Type is ItemType.StatUp or ItemType.ClassChange) { DoSelfItem(_sel!, it); return; }
        if (_s.Field.ItemSpell(it) is { } m) { BeginAim(m, it); return; }
        _aimItem = it;
        if (it.S.MedRange <= 0) { DoMedicine(_sel!, it, _sel!); return; }   // range 0 = oneself
        _medTargets = _s.Field.MedicineTargets(_sel!, it).ToList();
        _cursor = new Point(_sel!.X, _sel.Y);
        _state_ = State.MedicineTarget;
    }

    void BeginAim(Magic m, Item? fromItem)
    {
        var u = _sel!;
        _aimSpell = m;
        _aimItem = fromItem;
        _aimRange = _s.Field.SpellRange(m, u.X, u.Y).ToHashSet();
        // start on a unit the spell would affect (hostile first for harmful spells)
        var aims = _s.Field.SpellAims(u, m, u.X, u.Y).ToList();
        var first = aims.OrderBy(a => _s.Field.UnitAt(a.X, a.Y) is { } t && _s.Field.Affects(u, m, t) ? 0 : 1)
            .ThenBy(a => Math.Abs(a.X - u.X) + Math.Abs(a.Y - u.Y)).FirstOrDefault((u.X, u.Y));
        _cursor = new Point(first.X, first.Y);
        _state_ = State.Aiming;
    }

    void UpdateAiming(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { if (_aimItem is null) OpenSpellList(); else OpenItemList(); return; }
        MoveCursor(i);
        if (!Pick(i) || _aimSpell is null) return;
        var a = (_cursor.X, _cursor.Y);
        if (!_aimRange.Contains(a) || _s.Field.SpellTargets(_sel!, _aimSpell, a.X, a.Y).Count == 0) { Sfx.Buzzer(); return; }
        Sfx.Click();
        DoCast(_sel!, _aimSpell, a, _aimItem);
    }

    void UpdateMedicineTarget(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { OpenItemList(); return; }
        if (i.Left || i.Up || i.Right || i.Down)
        {
            int k = _medTargets.FindIndex(t => t.X == _cursor.X && t.Y == _cursor.Y);
            k = (k + ((i.Right || i.Down) ? 1 : _medTargets.Count - 1) + _medTargets.Count) % _medTargets.Count;
            _cursor = new Point(_medTargets[k].X, _medTargets[k].Y);
        }
        if (!Pick(i)) return;
        var target = _medTargets.FirstOrDefault(t => t.X == _cursor.X && t.Y == _cursor.Y);
        if (target is not null && _aimItem is not null) { Sfx.Click(); DoMedicine(_sel!, _aimItem, target); }
    }

    void DoCast(BattleUnit caster, Magic m, (int X, int Y) aim, Item? item)
    {
        // the units the spell meets on the other side get their first-attack slots (FUN_00410388)
        var met = OpposingOf(caster, _s.Field.SpellTargets(caster, m, aim.X, aim.Y));
        if (BeforeFight(caster, met, () => DoCast(caster, m, aim, item))) return;
        if (caster.Dead || caster.Hidden) { AfterFight(); return; }
        if (item is not null) { _state.AddItem(item.Index, -1); Popup($"{caster.Name}は{item.Name}{Word(58)}"); }   // "を使います"
        var before = HpSnapshot(_s.Field.SpellTargets(caster, m, aim.X, aim.Y).Append(caster));
        int mpBefore = caster.Mp;
        var r = _s.Field.Cast(caster, m, aim.X, aim.Y, fromItem: item is not null);
        if (ShowScenes && !Quiet && r.Effects.Count > 0)
        {
            QueueSpellScenes(caster, m, r, before);
            if (_scenes.Count > 0) _scenes.Peek().ShowMpBefore(caster, mpBefore);
        }
        if (!Quiet) ShowResult(caster, r, numbersShown: ShowScenes);
        _after = (caster, met);
        Finish(caster);
    }

    /// <summary>
    /// A stat-up or class-change item used in battle, on the user (FUN_0040b074): stat-ups add their positive values
    /// (max HP/MP up to 60, the rest 30, current HP/MP with them); a class change restarts the level at 1, moves the
    /// stats by the classes' initial-stat difference and sends unusable equipment to the inventory.
    /// </summary>
    void DoSelfItem(BattleUnit user, Item it)
    {
        _state.AddItem(it.Index, -1);
        Popup($"{user.Name}は{it.Name}{Word(58)}");
        if (it.Type == ItemType.StatUp)
        {
            var (b, gained) = UnitChanges.StatUp(user.Base, it.S.StatUp);
            Array.Copy(b, user.Base, 6);
            user.Hp += gained[0];
            user.Mp += gained[1];
            int[] words = { 2, 3, 8, 9, 10, 11 };   // ＨＰ ＴＰ 攻撃補正値 特殊補正値 命中回避値 耐久力
            for (int k = 0; k < 6; k++)
                if (gained[k] > 0) Popup($"{Word(words[k])}{Word(64)}{Wide(gained[k])}{Word(65)}{Word(68)}");
        }
        else
        {
            int to = UnitChanges.ClassChangeTarget(it, user.ClassId);
            var from = _db.Classes[Math.Clamp(user.ClassId, 0, _db.Classes.Length - 1)];
            Array.Copy(UnitChanges.ClassChangeStats(user.Base, from, _db.Classes[to]), user.Base, 6);
            user.ClassId = to;
            user.Level = 1;
            int Keep(int item) { if (item >= 0 && !UnitChanges.Usable(_db.Items[item], to)) { _state.AddItem(item, 1); return -1; } return item; }
            (user.Weapon, user.Armor, user.Accessory) = (Keep(user.Weapon), Keep(user.Armor), Keep(user.Accessory));
            Popup($"{user.Name}は{_db.Classes[to].Name}{Word(72)}");   // 「に昇格」
        }
        _after = null;
        Finish(user);
    }

    void DoMedicine(BattleUnit user, Item it, BattleUnit target)
    {
        _state.AddItem(it.Index, -1);
        Popup($"{user.Name}は{it.Name}{Word(58)}");
        ShowResult(user, _s.Field.UseMedicine(user, it, target));
        Finish(user);
    }

    void Finish(BattleUnit actor)
    {
        actor.Acted = true;
        _actor = actor;
        RunScenes();
    }

    /// <summary>
    /// Spell scene (T22 heal and spell recordings): one scene per cast. First the caster alone on its side plays
    /// its class cast animation (magic-to-ally / magic-to-enemy) and goes straight into the wipe; then each
    /// affected unit alone on its side in turn: pause, the spell animation (ANIME 80 + Magic.Animation),
    /// flash, number, drain, and a death message if it dies. Pauses: cast 11 ticks (heal) / 7 (attack spell);
    /// each target 10, hold 10.
    /// </summary>
    void QueueSpellScenes(BattleUnit caster, Magic m, ExchangeResult r, Dictionary<BattleUnit, int> before)
    {
        var cls = _s.Field.Class(caster);
        bool harmful = m.Type is 0 or 1;
        var cast = _db.Animations[Math.Clamp(harmful ? cls.AnimMagicEnemy : cls.AnimMagicAlly, 0, _db.Animations.Length - 1)];
        var spell = _db.Animations[Math.Clamp(m.AnimeIndex, 0, _db.Animations.Length - 1)];
        var first = r.Effects[0].Target;
        var steps = new List<BattleScene.Step>
        {
            new(caster, first, cast, BattleScene.SheetFor(_db, _s.Field, caster, cast), true, 0, false,
                Caption: "", Solo: caster, Pause: harmful ? 7 : 11, Hold: 0),
        };
        foreach (var e in r.Effects)
        {
            bool numeric = e.Kind is EffectKind.Damage or EffectKind.Heal or EffectKind.Miss;
            steps.Add(new(caster, e.Target, spell, BattleScene.SheetFor(_db, _s.Field, caster, spell),
                e.Kind != EffectKind.Miss && e.Kind != EffectKind.Resisted, e.Amount, e.Kind == EffectKind.Heal,
                Caption: numeric ? null : "", Solo: e.Target, Pause: 10, Hold: 10));
        }
        _scenes.Enqueue(new BattleScene(_db, _tex, _ui, _s.Field, caster, first, steps, before));
        BattleMusic(caster, first);
    }

    void ShowResult(BattleUnit actor, ExchangeResult r, bool numbersShown = false)
    {
        foreach (var e in r.Effects)
        {
            // the battle scene already showed damage and misses
            if (numbersShown && e.Kind is EffectKind.Damage or EffectKind.Miss) continue;
            var n = e.Target.Name;
            // T21: "ＨＰが８ポイント回復した" (no name, full-width digits); the other stats follow the same pattern (provisional)
            string Points(string what, int v, int verb) => $"{what}{Word(64)}{Wide(v)}{Word(65)}{Word(verb)}";
            Popup(e.Kind switch
            {
                EffectKind.Damage => $"{actor.Name} → {n}：{e.Amount}{(e.Killed ? "　撃破" : "")}",
                EffectKind.Miss => $"{actor.Name} → {n}：ミス",
                EffectKind.Resisted => $"{n}：無効",
                EffectKind.Heal => Points(Word(2), e.Amount, 66),
                EffectKind.MpHeal => Points(Word(3), e.Amount, 66),
                EffectKind.PowerUp => Points(Word(4), e.Amount, 68),
                EffectKind.DefUp => Points(Word(6), e.Amount, 68),
                EffectKind.MdefUp => Points(Word(7), e.Amount, 68),
                EffectKind.AttackDown => Points(Word(4), e.Amount, 69),
                EffectKind.DefenseDown => Points(Word(6), e.Amount, 69),
                EffectKind.Poison => $"{n}は毒を受けた",
                EffectKind.Sleep => $"{n}は眠った",
                EffectKind.CurePoison => $"{n}の{Word(74)}",                                   // "毒が消えた"
                EffectKind.WakeUp => $"{n}は{Word(76)}",                                       // "目を覚ました"
                EffectKind.Cleanse => $"{n}の状態異常が回復した",
                _ => $"{n}は再行動できる",
            }, e.Killed ? e.Target : null);
            if (e.Killed) _s.Ghosts.Add(e.Target);
        }
        ShowGrowth(r);
    }

    /// <summary>Full-width digits, as the original's messages print numbers.</summary>
    static string Wide(int v) => string.Concat(v.ToString().Select(c => c is >= '0' and <= '9' ? (char)('０' + (c - '0')) : c));

    void ShowGrowth(ExchangeResult r)
    {
        // kill rewards (the original adds them after the battle): gold 「n,肉を獲得」 (WORD 61), items 「〜を獲得」 (62)
        if (r.RewardGold > 0)
        {
            _state.Gold += r.RewardGold;
            Popup($"{Wide(r.RewardGold)}{Word(61)}");
        }
        foreach (var item in r.RewardItems)
        {
            _state.AddItem(item, 1);
            if (item >= 0 && item < _db.Items.Length) Popup($"{_db.Items[item].Name}{Word(62)}");
        }
        foreach (var (u, exp) in r.ExpGained)
            Popup($"{Wide(exp)}{Word(60)}");             // 「１７の経験値を獲得」: no name, full-width digits (T22)
        foreach (var (u, _) in r.LevelUps)
            Popup($"{u.Name}{Word(70)}{u.Level}{Word(71)}", sound: Sfx.LevelUp);   // "はレベル" n "になった"
        foreach (var (u, spells) in r.Learned)
            foreach (var m in spells)
                Popup($"{u.Name}は{m.Name}{Word(73)}");         // "を習得した"
    }

    Point _turnMenuAt;

    Rectangle MenuRect()
    {
        int w = _menu.Max(m => _ui.SmallText.Measure(m)) + 24, h = _menu.Length * 16 + 16;
        // the turn menu opens with its corner on the clicked tile (original screenshot); the action menu beside the unit
        var p = _sel is null ? _turnMenuAt
            : _s.ScreenPos(_sel.X + 1, _sel.Y, ViewportRect).ToPoint() + new Point(8, 0);
        if (_sel is null) p.X = Math.Min(p.X, ErumaniGame.ViewX + ErumaniGame.ViewWidth - w);
        else if (p.X + w > ErumaniGame.ViewX + ErumaniGame.ViewWidth) p.X -= w + MapSession.Tile + 16;
        p.Y = Math.Clamp(p.Y, ErumaniGame.ViewY, ErumaniGame.ViewY + ErumaniGame.ViewHeight - h);
        return new Rectangle(p.X, p.Y, w, h);
    }

    static readonly Rectangle ViewportRect = new(ErumaniGame.ViewX, ErumaniGame.ViewY, ErumaniGame.ViewWidth, ErumaniGame.ViewHeight);

    int MenuNavigate(BattleInput i, Point mouseVirtual)
    {
        if (i.Up) _menuIndex = (_menuIndex + _menu.Length - 1) % _menu.Length;
        if (i.Down) _menuIndex = (_menuIndex + 1) % _menu.Length;
        var r = MenuRect();
        if (i.MouseMoved && r.Contains(mouseVirtual)) _menuIndex = Math.Clamp((mouseVirtual.Y - r.Y - 8) / 16, 0, _menu.Length - 1);
        if (i.Confirm || (i.Click && r.Contains(mouseVirtual)))
        {
            if (_menuEnabled[_menuIndex]) { Sfx.Click(); return _menuIndex; }
            Sfx.Buzzer();
        }
        return -1;
    }

    public Point MouseVirtual { get; set; }

    void UpdateActionMenu(BattleInput i)
    {
        if (i.Cancel || i.RightClick)
        {
            // undo the move (allowed until the unit acts); every pass-by / stop-on mark goes too (FUN_0040f0ac)
            _sel!.X = _selFrom.X;
            _sel.Y = _selFrom.Y;
            _passMarks.Clear();
            _stopMarks.Clear();
            _state_ = State.Selected;
            return;
        }
        int pick = MenuNavigate(i, MouseVirtual);
        if (pick == 0) { _state_ = State.Targeting; _forecastArmed = null; _cursor = new Point(_targets[0].X, _targets[0].Y); }
        else if (pick == 1) OpenSpellList();
        else if (pick == 2) OpenEquipList();
        else if (pick == 3) OpenItemList();
        else if (pick == 4) FinishAction();
    }

    void UpdateTargeting(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { OpenActionMenuAgain(); return; }
        if (i.Left || i.Up || i.Right || i.Down)
        {
            int k = _targets.FindIndex(t => t.X == _cursor.X && t.Y == _cursor.Y);
            k = (k + ((i.Right || i.Down) ? 1 : _targets.Count - 1)) % _targets.Count;
            _cursor = new Point(_targets[k].X, _targets[k].Y);
        }
        if (!Pick(i)) return;
        var target = _targets.FirstOrDefault(t => t.X == _cursor.X && t.Y == _cursor.Y);
        if (target is null) return;
        // a click first brings up that target's forecast, a second click attacks (keys: the forecast is already up)
        if (i.Click && !i.Confirm && _forecastArmed != target) { _forecastArmed = target; Sfx.Click(); return; }
        _forecastArmed = null;
        Sfx.Click();
        DoAttack(_sel!, target);
    }

    BattleUnit? _forecastArmed;

    /// <summary>
    /// Terrain window (modern addition, Fire Emblem style) in the map's bottom-right corner for the tile under the
    /// cursor: its name, 地形効果 (hit chance taken off attacks on a unit there), the damage it takes off (effect/10)
    /// and the HP recovered each turn. Moves to the bottom-left while the cursor is in that corner.
    /// </summary>
    void DrawTerrainInfo(SpriteBatch sb)
    {
        var ter = _s.Field.TerrainAt(_cursor.X, _cursor.Y);
        // signed from the point of view of a unit standing there: 地形効果 +20% = attacks on it 20% less likely to hit
        string Signed(int v) => v > 0 ? $"+{v}" : $"{v}";
        var lines = new List<(string Label, string Value)> { (Word(75), $"{Signed(ter.DefenseBonus)}%") };
        if (ter.DefenseBonus / 10 != 0) lines.Add(("被ダメージ", Signed(-(ter.DefenseBonus / 10))));
        if (ter.HpRecovery != 0) lines.Add(("HP回復", $"{ter.HpRecovery}"));
        const int w = 136;
        int h = 30 + lines.Count * 16 + 4;
        var p = _s.ScreenPos(_cursor.X, _cursor.Y, ViewportRect);
        int right = ErumaniGame.ViewX + ErumaniGame.ViewWidth - 8, bottom = ErumaniGame.ViewY + ErumaniGame.ViewHeight - 8;
        bool inCorner = p.X + MapSession.Tile > right - w - 8 && p.Y + MapSession.Tile > bottom - h - 8;
        var r = new Rectangle(inCorner ? ErumaniGame.ViewX + 8 : right - w, bottom - h, w, h);
        _ui.Panel(sb, r);
        void T(string s, int x, int y, Color c, bool alignRight = false)
        {
            if (_ui.SmallText.Get(s) is not { } t) return;
            sb.Draw(t, new Vector2(alignRight ? x - t.Width : x, y), c);
        }
        T(ter.Name, r.X + 10, r.Y + 8, Ui.Ui.PanelText);
        _ui.Fill(sb, new Rectangle(r.X + 8, r.Y + 25, w - 16, 1), Ui.Ui.Modern ? Ui.Ui.ModernBorder : Ui.Ui.PanelText * 0.6f);
        for (int k = 0; k < lines.Count; k++)
        {
            T(lines[k].Label, r.X + 10, r.Y + 30 + k * 16, Ui.Ui.PanelText);
            T(lines[k].Value, r.Right - 10, r.Y + 30 + k * 16, Ui.Ui.StatText, alignRight: true);
        }
    }

    /// <summary>
    /// Battle forecast (modern addition, Fire Emblem style) while choosing a target: both sides' HP before → after
    /// if every strike hits, damage per hit (×n for several strikes), hit and critical chances; "－" where there's no counter.
    /// </summary>
    void DrawForecast(SpriteBatch sb, BattleUnit atk, BattleUnit def)
    {
        var (fa, fd) = _s.Field.Forecast(atk, def);
        int defAfter = Math.Max(0, def.Hp - fa.Damage * fa.Strikes);
        int atkAfter = defAfter == 0 ? atk.Hp : Math.Max(0, atk.Hp - fd.Damage * fd.Strikes);
        const int w = 264, h = 122;
        // up and to the right of the two combatants' tiles; up-left when that would run off the right edge
        var a = _s.ScreenPos(atk.X, atk.Y, ViewportRect).ToPoint();
        var d = _s.ScreenPos(def.X, def.Y, ViewportRect).ToPoint();
        int left = Math.Min(a.X, d.X), right = Math.Max(a.X, d.X) + MapSession.Tile, top = Math.Min(a.Y, d.Y);
        int viewRight = ErumaniGame.ViewX + ErumaniGame.ViewWidth;
        int x = right + 8 + w <= viewRight - 4 ? right + 8 : left - 8 - w;
        x = Math.Clamp(x, ErumaniGame.ViewX + 4, viewRight - w - 4);
        int py = Math.Clamp(top - h - 8, ErumaniGame.ViewY + 4, ErumaniGame.ViewY + ErumaniGame.ViewHeight - h - 4);
        var r = new Rectangle(x, py, w, h);
        _ui.Panel(sb, r);
        var white = Ui.Ui.PanelText;
        var dim = new Color(200, 210, 200);
        void T(string s, int cx, int y, Color c, int align)   // align: -1 left, 0 centre, 1 right
        {
            if (_ui.SmallText.Get(s) is not { } t) return;
            float px = align < 0 ? cx : align > 0 ? cx - t.Width : cx - t.Width / 2f;
            sb.Draw(t, new Vector2(r.X + px, r.Y + y), c);
        }
        Color SideColor(BattleUnit u) => u.Side == Side.Ally ? new Color(64, 120, 248) : u.Side == Side.Enemy ? new Color(232, 56, 56) : new Color(232, 192, 48);
        // names over coloured strips
        _ui.Fill(sb, new Rectangle(r.X + 8, r.Y + 26, 108, 2), SideColor(atk));
        _ui.Fill(sb, new Rectangle(r.Right - 116, r.Y + 26, 108, 2), SideColor(def));
        T(atk.Name, 10, 8, white, -1);
        T(def.Name, w - 10, 8, white, 1);
        string Strikes(Battlefield.StrikeForecast f) => f.Strikes == 0 ? "－" : f.Strikes > 1 ? $"{f.Damage} ×{f.Strikes}" : $"{f.Damage}";
        string Pct(int v, Battlefield.StrikeForecast f) => f.Strikes == 0 ? "－" : $"{v}%";
        var rows = new (string Label, string Left, string Right)[]
        {
            (Word(2), $"{atk.Hp} → {atkAfter}", $"{def.Hp} → {defAfter}"),
            ("威力", Strikes(fa), Strikes(fd)),
            ("命中", Pct(fa.Hit, fa), Pct(fd.Hit, fd)),
            ("必殺", Pct(fa.Crit, fa), Pct(fd.Crit, fd)),
        };
        for (int k = 0; k < rows.Length; k++)
        {
            int y = 36 + k * 20;
            T(rows[k].Label, w / 2, y, dim, 0);
            T(rows[k].Left, 92, y, white, 1);
            T(rows[k].Right, w - 92, y, white, -1);
        }
    }

    void OpenActionMenuAgain()
    {
        BuildActionMenu();
        _state_ = State.ActionMenu;
    }

    void DoAttack(BattleUnit atk, BattleUnit def)
    {
        var met = OpposingOf(atk, new[] { def });
        if (BeforeFight(atk, met, () => DoAttack(atk, def))) return;
        // a "before" event can change things (rarely): no fight if either side is gone
        if (atk.Dead || atk.Hidden || def.Dead || def.Hidden) { atk.Acted = true; _actor = atk; AfterFight(); return; }
        var before = HpSnapshot(new[] { atk, def });
        var r = _s.Field.Attack(atk, def);
        if (Quiet) { }
        else if (ShowScenes)
        {
            var steps = r.Strikes.Select(st =>
            {
                var a = BattleScene.WeaponAnim(_db, _s.Field, st.Attacker);
                return new BattleScene.Step(st.Attacker, st.Target, a, BattleScene.SheetFor(_db, _s.Field, st.Attacker, a), st.Hit, st.Damage, false);
            }).ToList();
            _scenes.Enqueue(new BattleScene(_db, _tex, _ui, _s.Field, atk, def, steps, before));
            BattleMusic(atk, def);
        }
        else
            foreach (var st in r.Strikes)
            {
                Popup(st.Hit
                    ? $"{st.Attacker.Name} → {st.Target.Name}：{st.Damage}{(st.Critical ? "（会心）" : "")}{(st.Killed ? "　撃破" : "")}"
                    : $"{st.Attacker.Name} → {st.Target.Name}：ミス", st.Killed ? st.Target : null);
                if (st.Killed) _s.Ghosts.Add(st.Target);
            }
        ShowGrowth(r);
        _after = (atk, met);
        _actor = atk;
        RunScenes();
    }

    readonly Queue<Action?> _popupSounds = new();

    void Popup(string text, BattleUnit? kill = null, Action? sound = null)
    {
        _popups.Enqueue(text);
        _popupKills.Enqueue(kill);
        _popupSounds.Enqueue(sound ?? (kill is not null ? Sfx.Down : null));
    }

    void NextPopup()
    {
        if (_popupKill is not null) { _s.Ghosts.Remove(_popupKill); _popupKill = null; }
        if (_popups.Count > 0) { _popup = _popups.Dequeue(); _popupKill = _popupKills.Dequeue(); _popupSounds.Dequeue()?.Invoke(); _popupFrames = 0; _state_ = State.Showing; }
        else
        {
            // everything shown: the fight's "after" events, then the checkpoint (FUN_0041093c)
            _popup = null;
            _sel = null;
            AfterFight();
        }
    }

    void UpdateShowing(BattleInput i)
    {
        _popupFrames += i.FastForward ? 4 : 1;
        if (i.Confirm || i.Click || _popupFrames > 60) NextPopup();
    }

    void FinishAction()
    {
        _sel!.Acted = true;
        _actor = _sel;
        _sel = null;
        if (CheckPoint()) return;
        _state_ = State.Idle;   // the turn only ends from the menu (T19a)
    }

    void OpenTurnMenu()
    {
        // WORD.DAT: 19 ターン終了, 20 全体マップ, 21 セーブする, 22 ロードする, 23 ゲーム終了 (the original's test play
        // adds デバッグ, which isn't part of the game)
        _menu = new[] { Word(19), Word(20), Word(21), Word(22), Word(23) };
        _menuEnabled = new[] { true, true, true, true, true };
        _menuIndex = 0;
        _turnMenuAt = _s.ScreenPos(_cursor.X, _cursor.Y, ViewportRect).ToPoint();
        _sel = null;
        _state_ = State.TurnMenu;
    }

    void UpdateTurnMenu(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { _state_ = State.Idle; return; }
        switch (MenuNavigate(i, MouseVirtual))
        {
            case 0: EndPlayerTurn(); break;
            case 1: _state_ = State.WholeMap; break;
            case 2: OpenFiles(save: true); break;
            case 3: OpenFiles(save: false); break;
            case 4:
                _quit.Set(new[] { Word(45), Word(46) });   // ゲームを終了します: はい / いいえ
                _quit.Index = 0;
                _state_ = State.QuitConfirm;
                break;
        }
    }

    /// <summary>
    /// ターン終了 (FUN_00410618 → FUN_00410b90): the phase changes (upkeep for the enemies), ENEMY TURN, then the
    /// checkpoint before the first enemy moves.
    /// </summary>
    void EndPlayerTurn()
    {
        _s.Field.NextPhase();          // enemy phase
        _state_ = State.EnemyPhase;
        _plan = null;
        _group = 1;
        _wait = 0;
        TurnTelop(2, () => { if (!CheckPoint()) _state_ = State.EnemyPhase; });
    }

    bool _planWalking;
    /// <summary>Who moves now in the computer's phase: 1 the enemies, then 2 the neutral units (FUN_0040e658).</summary>
    int _group = 1;

    /// <summary>Option: the enemy phase resolves without walking, panning, battle scenes or result popups (events still play).</summary>
    public bool SkipEnemyTurn { get; set; }
    bool Quiet => SkipEnemyTurn && !_s.Field.AllyPhase;

    /// <summary>
    /// The computer's phase (FUN_0040e658): every enemy that hasn't acted, by record number, then every neutral unit;
    /// each one's AI decides, the camera shows it while it walks, it acts, and the checkpoint runs before the next.
    /// A unit with nothing to do is done at once (no camera). Then the camera goes to the first player unit and the
    /// player's phase begins.
    /// </summary>
    void UpdateEnemyPhase(BattleInput i)
    {
        if (_wait > 0) { _wait -= i.FastForward ? 4 : 1; return; }
        if (_plan is { } p)
        {
            if (!_planWalking)
            {
                if (Quiet) (p.Unit.X, p.Unit.Y) = p.Destination;
                else
                {
                    _s.ShowTile(p.Unit.X, p.Unit.Y);
                    _s.Walk(p.Unit, p.Path);
                }
                _planWalking = true;
                return;
            }
            if (_s.MovesRunning) return;
            _plan = null;
            p.Unit.Acted = true;
            _actor = p.Unit;
            // the move's end: tile marks and contacts; a contact event takes the unit's action away
            if (MoveEnded(p.Unit, p.Path)) return;
            if (p.Spell is { } sp && Battlefield.CanCast(p.Unit, sp) && _s.Field.SpellTargets(p.Unit, sp, p.Aim.X, p.Aim.Y).Count > 0)
            {
                _cursor = new Point(p.Aim.X, p.Aim.Y);
                DoCast(p.Unit, sp, p.Aim, null);
                return;
            }
            if (p.Target is { Dead: false } t && _s.Field.InRange(p.Unit, p.Unit.X, p.Unit.Y, t)) { DoAttack(p.Unit, t); return; }
            if (CheckPoint()) return;
            _wait = Quiet ? 0 : 8;
            return;
        }
        while (true)
        {
            var u = _s.InRecordOrder.FirstOrDefault(u => !u.Dead && !u.Acted && (_group == 1 ? u.Side == Side.Enemy : u.Side == Side.Neutral));
            if (u is null)
            {
                if (_group == 1) { _group = 2; continue; }
                break;
            }
            var plan = EnemyAi.Plan(_s.Field, u, _state.Switches);
            if (plan.Path.Count == 0 && plan.Target is null && plan.Spell is null)
            {
                u.Acted = true;   // nothing to do (FUN_0041589c returns at once)
                continue;
            }
            _plan = plan;
            _planWalking = false;
            return;
        }
        // the computer's phase is over: the camera to the first player unit, the player's phase (upkeep), PLAYER TURN
        _group = 1;
        if (!Quiet && _s.FirstPlayerUnit() is { } f) _s.ShowTile(f.X, f.Y);
        _s.Field.NextPhase();
        _state_ = State.Idle;
        TurnTelop(1, () => { if (!CheckPoint()) _state_ = State.Idle; });
    }

    // ---------------- drawing ----------------

    public void DrawOverlays(SpriteBatch sb, Rectangle viewport, int frame)
    {
        if (_state_ == State.Selected && _reach is not null) _s.DrawRange(sb, viewport, _reach.Keys, 0, frame);
        if (_state_ == State.Targeting && _sel is not null)
        {
            var st = _s.Field.Stats(_sel);
            var tiles = new List<(int, int)>();
            for (int y = _sel.Y - st.RangeMax; y <= _sel.Y + st.RangeMax; y++)
                for (int x = _sel.X - st.RangeMax; x <= _sel.X + st.RangeMax; x++)
                {
                    int d = Math.Abs(x - _sel.X) + Math.Abs(y - _sel.Y);
                    if (d >= st.RangeMin && d <= st.RangeMax && _s.Field.InBounds(x, y)) tiles.Add((x, y));
                }
            _s.DrawRange(sb, viewport, tiles, 1, frame);
        }
        // spell range and the area around the aim in stripes, the tiles of affected units solid: blue (tile 3) for
        // own-side spells (T21), orange/yellow (tile 2) for attack spells (T22 recording); medicine range salmon (tile 1)
        if (_state_ == State.Aiming && _aimSpell is not null && _sel is not null)
        {
            int colour = _aimSpell.Type is 0 or 1 ? 2 : 3;
            var tiles = new HashSet<(int X, int Y)>(_aimRange);
            if (_aimRange.Contains((_cursor.X, _cursor.Y)))
            {
                tiles.UnionWith(_s.Field.SpellArea(_aimSpell, _cursor.X, _cursor.Y));
                _s.DrawRange(sb, viewport, tiles, colour, frame);
                _s.DrawRange(sb, viewport, _s.Field.SpellTargets(_sel, _aimSpell, _cursor.X, _cursor.Y).Select(u => (u.X, u.Y)), colour, frame, solid: true);
            }
            else _s.DrawRange(sb, viewport, tiles, colour, frame);
        }
        if (_state_ == State.MedicineTarget && _sel is not null && _aimItem is not null)
        {
            int r = _aimItem.S.MedRange;
            var tiles = new List<(int, int)>();
            for (int y = _sel.Y - r; y <= _sel.Y + r; y++)
                for (int x = _sel.X - r; x <= _sel.X + r; x++)
                    if (Math.Abs(x - _sel.X) + Math.Abs(y - _sel.Y) <= r && _s.Field.InBounds(x, y)) tiles.Add((x, y));
            _s.DrawRange(sb, viewport, tiles, 1, frame);
        }
    }

    public void DrawUi(SpriteBatch sb, Rectangle viewport, int frame)
    {
        bool showCursor = _state_ is State.Idle or State.Selected or State.Targeting or State.Aiming or State.MedicineTarget;
        if (showCursor && _s.Field.InBounds(_cursor.X, _cursor.Y))
        {
            var p = _s.ScreenPos(_cursor.X, _cursor.Y, viewport).ToPoint();
            _ui.Frame(sb, new Rectangle(p.X, p.Y, 32, 32), Color.Black);
            _ui.Frame(sb, new Rectangle(p.X + 1, p.Y + 1, 30, 30), Color.White, 2);
            if (_s.Field.UnitAt(_cursor.X, _cursor.Y) is { } u) DrawHoverLabel(sb, u, p);
            if (_state_ != State.Targeting) DrawTerrainInfo(sb);
        }
        if (_state_ is State.ActionMenu or State.TurnMenu or State.Files or State.QuitConfirm) DrawMenu(sb, frame);
        if (_state_ == State.Targeting && _sel is not null && _targets.FirstOrDefault(t => t.X == _cursor.X && t.Y == _cursor.Y) is { } ft)
            DrawForecast(sb, _sel, ft);
        if (_state_ == State.WholeMap)
            _s.DrawWholeMap(sb, _ui, _s.Field.Alive.Select(u => (u.X, u.Y, u.Side switch
            {
                Side.Ally => new Color(48, 96, 248), Side.Enemy => new Color(232, 40, 40), _ => new Color(240, 200, 40),
            })));
        if (_state_ == State.Files) _files.Draw(sb, _ui, true, frame);
        if (_state_ == State.QuitConfirm)
        {
            _quit.Draw(sb, _ui, true, frame);
            if (_ui.SmallText.Get(Word(57)) is { } q) sb.Draw(q, new Vector2(_quit.Rect.X + 13, _quit.Rect.Y + 9), Ui.Ui.PanelText);
            _ui.Fill(sb, new Rectangle(_quit.Rect.X + 12, _quit.Rect.Y + 29, _quit.Rect.Width - 24, 1), Ui.Ui.PanelText);
        }
        if (_state_ is State.SpellList or State.ItemList or State.EquipList) DrawList(sb, frame);
        if (_state_ == State.Showing && _popup is not null) DrawPopup(sb, _popup);
        if (_state_ == State.Status && _statusUnit is not null) Ui.StatusWindow.Draw(sb, _ui, _tex, _db, _s.Field, _statusUnit);
        if (_scene is not null) { _scene.Draw(sb); if (_state_ == State.Showing && _popup is not null) DrawPopup(sb, _popup); }
        if (Outcome == MapOutcome.GameOver && _tex.Get("GAMEOVER.BMP", transparent: false) is { } go)
            sb.Draw(go, Vector2.Zero, Color.White);   // full screen; provisional
        if (_telop is not null && _tex.Get(_telop) is { } t)
        {
            // clipped to the viewport so the slide doesn't cover the screen frame
            int x = viewport.Center.X - t.Width / 2 + (int)TelopOffset(t.Width, viewport);
            int left = Math.Max(x, viewport.Left), right = Math.Min(x + t.Width, viewport.Right);
            if (right > left)
                sb.Draw(t, new Vector2(left, viewport.Center.Y - t.Height / 2), new Rectangle(left - x, 0, right - left, t.Height), Color.White);
        }
    }

    /// <summary>Name and "HP／max" above the hovered unit (screenshot 5; font and offsets provisional).</summary>
    void DrawHoverLabel(SpriteBatch sb, BattleUnit u, Point tile)
    {
        string hp = $"{u.Hp}／{u.MaxHp}";
        var name = _ui.SmallText.Get(u.Name);
        var num = _ui.SmallText.Get(hp);
        void Outlined(Texture2D? t, Vector2 at)
        {
            if (t is null) return;
            foreach (var d in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
                sb.Draw(t, at + d, Color.Black);
            sb.Draw(t, at, Color.White);
        }
        Outlined(name, new Vector2(tile.X - 4, tile.Y - 32));
        Outlined(num, new Vector2(tile.X - 4, tile.Y - 18));
    }

    void DrawMenu(SpriteBatch sb, int frame)
    {
        var r = MenuRect();
        _ui.Panel(sb, r);
        bool modern = Ui.Ui.Modern;
        for (int i = 0; i < _menu.Length; i++)
        {
            // the selected row: the flashing green bar of the original's menus (screenshot), or the modern accent bar
            var row = new Rectangle(r.X + 4, r.Y + 8 + i * 16, r.Width - 8, 17);
            if (i == _menuIndex)
            {
                if (modern) _ui.ModernHighlight(sb, row, frame);
                else _ui.Fill(sb, row, frame / 8 % 2 == 0 ? new Color(160, 248, 96) : new Color(248, 248, 248));
            }
            var c = !_menuEnabled[i] ? (modern ? (i == _menuIndex ? new Color(196, 204, 222) : new Color(110, 116, 132)) : new Color(120, 140, 120))
                : i == _menuIndex ? (modern ? Color.White : new Color(0, 32, 0)) : Ui.Ui.PanelText;
            if (_ui.SmallText.Get(_menu[i]) is { } t) sb.Draw(t, new Vector2(r.X + 13, r.Y + 10 + i * 16), c);
        }
    }

    void DrawList(SpriteBatch sb, int frame)
    {
        bool items = ItemLike;
        var r = items ? ItemListRect : SpellListRect();
        _ui.Panel(sb, r);
        var bar = frame / 8 % 2 == 0 ? new Color(160, 248, 96) : new Color(248, 248, 248);   // flashing (T21)
        int first = items ? _itemPage * ItemRows : 0;
        int count = items ? Math.Min(ItemRows, _menu.Length - first) : _menu.Length;
        void T(string s, int x, int y, Color c, bool right = false)
        {
            if (_ui.SmallText.Get(s) is not { } tx) return;
            sb.Draw(tx, new Vector2(right ? x - tx.Width : x, y), c);
        }
        for (int k = 0; k < count; k++)
        {
            int i = first + k;
            var row = RowRect(i);
            bool hot = i == _menuIndex;
            bool modern = Ui.Ui.Modern;
            if (hot && modern) _ui.ModernHighlight(sb, row, frame);
            else if (hot) _ui.Fill(sb, row, bar);
            var c = hot ? (modern ? Color.White : new Color(0, 32, 0)) : !_menuEnabled[i] ? (modern ? new Color(110, 116, 132) : new Color(120, 140, 120)) : Ui.Ui.PanelText;
            T(_menu[i], row.X + 8, row.Y + 2, c);
            if (_state_ == State.EquipList) T(_items[i].Index == _sel?.Weapon ? Word(89) : $"× {_state.ItemCount(_items[i].Index)}", row.Right - 8, row.Y + 2, c, right: true);
            else if (items) T($"× {_state.ItemCount(_items[i].Index)}", row.Right - 8, row.Y + 2, c, right: true);
            else
            {
                T(Word(3), row.X + 140, row.Y + 2, c);                                     // ＴＰ
                T($"{_spells[i].MpCost}／{_sel?.Mp ?? 0}", row.Right - 8, row.Y + 2, c, right: true);
            }
        }
        if (items)
        {
            int pages = Math.Max(1, (_menu.Length + ItemRows - 1) / ItemRows);
            _ui.Fill(sb, new Rectangle(r.X + 12, r.Bottom - 32, r.Width - 24, 1), Ui.Ui.PanelText);
            T(Word(63), r.X + 12, r.Bottom - 24, Ui.Ui.PanelText);                                   // ページ
            T($"{_itemPage + 1,2}／{pages,2}", r.X + 60, r.Bottom - 24, Ui.Ui.PanelText);
            // page arrows: SYSTEM.BMP 16x16 icons at y 304 (x 0 left, x 16 right)
            if (_tex.Get("SYSTEM.BMP") is { } sys)
            {
                sb.Draw(sys, new Vector2(r.Right - 55, r.Bottom - 27), new Rectangle(0, 304, 16, 16), Color.White);
                sb.Draw(sys, new Vector2(r.Right - 31, r.Bottom - 27), new Rectangle(16, 304, 16, 16), Color.White);
            }
        }
        // description of the hovered entry
        if (_menuIndex >= 0 && _menuIndex < _menu.Length)
        {
            string desc = items ? _items[_menuIndex].Desc : _spells[_menuIndex].Desc;
            var d = new Rectangle(80, 32, 480, 48);
            _ui.Panel(sb, d);
            T($"{_menu[_menuIndex]}：", d.X + 16, d.Y + 10, Ui.Ui.PanelText);
            if (items) T(Ui.ItemInfo.Summary(_items[_menuIndex], _db), d.Right - 16, d.Y + 10, Ui.Ui.StatText, right: true);
            T(desc, d.X + 16, d.Y + 26, Ui.Ui.PanelText);
        }
    }

    /// <summary>Green bar like the original's EXP message (240x33 at (200,64)).</summary>
    void DrawPopup(SpriteBatch sb, string text)
    {
        int w = Math.Max(240, _ui.SmallText.Measure(text) + 32);
        var r = new Rectangle(320 - w / 2, 64, w, 33);
        _ui.Panel(sb, r);
        if (_ui.SmallText.Get(text) is { } t) sb.Draw(t, new Vector2(r.X + 16, r.Y + 11), Ui.Ui.PanelText);
    }
}
