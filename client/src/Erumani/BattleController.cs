using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani;

/// <summary>Per-frame input for the battle map.</summary>
public readonly record struct BattleInput(
    bool Confirm, bool Cancel, bool Up, bool Down, bool Left, bool Right,
    Point? MouseTile, bool MouseMoved, bool Click, bool RightClick, bool FastForward);

public enum MapOutcome { None, Won, Lost, GameOver, ReturnToTitle }

/// <summary>
/// Battle-map flow: events (opening, turn, contact, attack, death, victory triggers), the
/// player phase (select, move, act), the enemy phase (AI), and win/lose. Layout of menus and
/// popups is provisional where no reference screenshot exists (FORMAT_NOTES §13).
/// </summary>
public sealed class BattleController
{
    enum State { Events, Idle, Selected, Moving, ActionMenu, Targeting, Showing, TurnMenu, EnemyPhase, Status, Ended,
        SpellList, ItemList, Aiming, MedicineTarget, Scene }

    readonly MapSession _s;
    readonly EventRunner _runner;
    readonly GameState _state;
    readonly Database _db;
    readonly Ui.Ui _ui;
    readonly Textures _tex;
    readonly Queue<(string Name, EventScript Script)> _events = new();
    readonly HashSet<string> _fired = new();
    readonly HashSet<string> _switchArmed = new();
    State _state_ = State.Events;
    State _afterShow = State.Idle;
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
    Queue<BattleUnit> _enemyQueue = new();
    AiPlan? _plan;
    BattleUnit? _statusUnit;
    bool _victoryQueued;

    public MapOutcome Outcome { get; private set; }
    /// <summary>One-line state summary (screenshot mode writes it next to the PNG).</summary>
    public string DebugState => $"{_state_} phase={_s.Phase} sel={_sel?.Name} at={_sel?.X},{_sel?.Y} cursor={_cursor.X},{_cursor.Y} outcome={Outcome} telop={_telop} plan={_plan?.Unit.Name}/{_planWalking} queue={_enemyQueue.Count} pan={_s.Panning} cam={_s.Camera} moves={_s.MovesRunning} wait={_wait}" +
        string.Concat(_s.Field.Units.Select(u => $"\n  {u.Side} {u.Name} ({u.X},{u.Y}) HP {u.Hp} Lv {u.Level} Exp {u.Exp} hidden={u.Hidden} run={_s.Visuals[u].Running.Count}/{_s.Visuals[u].StepFrames}/{_s.Visuals[u].Pending.Count}{(u.Dead ? " dead" : "")}"));
    public int? NextMap => _runner.NextMap;
    public bool EventRunning => _state_ == State.Events && _runner.Running;
    public bool Busy => _telop is not null || _state_ is State.Events or State.Moving or State.Showing or State.EnemyPhase or State.Scene;
    /// <summary>Asks the game to play a sound effect (EFS wav).</summary>
    public Action<string>? PlaySound { get; set; }

    // ---------------- battle scenes ----------------

    readonly Queue<BattleScene> _scenes = new();
    BattleScene? _scene;
    State _afterScenes;
    public bool SceneShowing => _scene is not null;
    /// <summary>Set to false to skip the battle scenes (popups only), e.g. a future "animations off" option.</summary>
    public bool ShowScenes { get; set; } = true;

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
    int _telopFrames;
    Action? _afterTelop;
    bool _announcePlayer;

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

    // ---------------- events ----------------

    public void Start(string? firstEvent)
    {
        if (_s.Map.Scripts.TryGetValue(firstEvent ?? "OPENING", out var opening)) _events.Enqueue((firstEvent ?? "OPENING", opening));
        QueuePhaseEvents();
        _state_ = State.Events;
        _announcePlayer = true;
    }

    IEnumerable<(MapObject Obj, int Slot, EventSlot S)> Slots() =>
        from o in _s.Map.Objects where o.InUse
        from i in Enumerable.Range(1, 5)
        let sl = o.Slots[i - 1] where sl.Condition != 0
        select (o, i, sl);

    BattleUnit? UnitOf(MapObject o) => _s.Field.Units.FirstOrDefault(u => u.MapObject == o.Index);

    bool ObjectPresent(MapObject o) =>
        o.Type == ObjectType.Other ? _s.OtherVisible(o) : UnitOf(o) is { Dead: false, Hidden: false };

    void Queue(MapObject o, int slot, bool once = true)
    {
        string name = MapObject.ScriptName(o.Index, slot);
        if (!_s.Map.Scripts.TryGetValue(name, out var sc)) return;
        if (once && !_fired.Add(name)) return;
        if (_events.Any(e => e.Name == name)) return;
        _events.Enqueue((name, sc));
    }

    void QueuePhaseEvents()
    {
        foreach (var (o, i, sl) in Slots())
            if (sl.Condition == 2 && sl.Turn == _s.Field.Phase && ObjectPresent(o)) Queue(o, i);
        QueueSwitchEvents();
    }

    /// <summary>Switch-condition slots run when their switch is ON; again only after it went OFF (provisional).</summary>
    void QueueSwitchEvents()
    {
        foreach (var (o, i, sl) in Slots())
        {
            if (sl.Condition != 1) continue;
            string name = MapObject.ScriptName(o.Index, i);
            bool on = sl.Switch >= 0 && sl.Switch < GameState.SwitchCount && _state.Switches[sl.Switch];
            if (!on) { _switchArmed.Remove(name); continue; }
            if (_switchArmed.Add(name) && ObjectPresent(o)) Queue(o, i, once: false);
        }
    }

    bool Targets(EventSlot sl, BattleUnit by) => sl.TargetUnit < 0 || sl.TargetUnit == by.Unit;

    void AfterMove(BattleUnit mover, IReadOnlyList<(int X, int Y)> path)
    {
        if (mover.Side != Side.Ally) return;
        foreach (var (o, i, sl) in Slots())
        {
            if (!ObjectPresent(o) || !Targets(sl, mover)) continue;
            if (o.Type == ObjectType.Other)
            {
                bool stops = (mover.X, mover.Y) == (o.X, o.Y);
                bool passes = stops || path.Contains((o.X, o.Y));
                if ((sl.Condition == 7 && stops) || (sl.Condition == 6 && passes)) Queue(o, i);
            }
            else if (sl.Condition == 4 && UnitOf(o) is { } t && Adjacent(mover, t)) Queue(o, i);
        }
    }

    void AfterAction(BattleUnit actor)
    {
        if (actor.Side != Side.Ally) return;
        foreach (var (o, i, sl) in Slots())
            if (sl.Condition == 9 && ObjectPresent(o) && Targets(sl, actor) && UnitOf(o) is { } t && Adjacent(actor, t)) Queue(o, i);
    }

    void AttackTriggers(BattleUnit attacker, BattleUnit target, int condition)
    {
        if (attacker.Side != Side.Ally || target.MapObject < 0) return;
        var o = _s.Map.Objects.First(x => x.Index == target.MapObject);
        for (int i = 1; i <= 5; i++)
            if (o.Slots[i - 1].Condition == condition && Targets(o.Slots[i - 1], attacker)) Queue(o, i);
    }

    void DeathTriggers()
    {
        foreach (var (o, i, sl) in Slots())
            if (sl.Condition == 5 && UnitOf(o) is { Dead: true }) Queue(o, i);
    }

    static bool Adjacent(BattleUnit a, BattleUnit b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    /// <summary>After anything happens: deaths, switches, win/lose. Returns true if events were queued.</summary>
    bool CheckPoint()
    {
        _s.UpdateVisibility();
        DeathTriggers();
        QueueSwitchEvents();
        if (_s.Field.Won())
        {
            if (!_victoryQueued)
            {
                _victoryQueued = true;
                // victory events run in reading order (help text)
                foreach (var (o, i, sl) in Slots().Where(x => x.S.Condition == 8).OrderBy(x => x.Obj.Y).ThenBy(x => x.Obj.X))
                    if (ObjectPresent(o) || o.Type == ObjectType.Other) Queue(o, i);
            }
            // death and victory events (e.g. the boss's last words) play out first
            if (_events.Count == 0) { Win(); return true; }
        }
        if (_s.Field.Lost()) { End(MapOutcome.GameOver); return true; }
        if (_events.Count > 0) { _state_ = State.Events; return true; }
        return false;
    }

    void End(MapOutcome o)
    {
        Outcome = o;
        _state_ = State.Ended;
        if (o == MapOutcome.Won) _s.SaveRoster();
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
            case State.Aiming: UpdateAiming(input); break;
            case State.MedicineTarget: UpdateMedicineTarget(input); break;
            case State.Scene: UpdateScene(input); break;
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
            _s.UpdateVisibility();
        }
        if (_events.Count > 0)
        {
            var (_, sc) = _events.Dequeue();
            _runner.Start(sc);
            return;
        }
        if (CheckPoint()) return;
        if (_resumeMenu && _sel is { Dead: false }) { _resumeMenu = false; _state_ = State.ActionMenu; return; }
        _resumeMenu = false;
        if (_s.Field.AllyPhase)
        {
            _state_ = State.Idle;
            if (_announcePlayer) { _announcePlayer = false; TurnTelop(1, () => { }); }
            return;
        }
        _state_ = State.EnemyPhase;
        if (_enemyQueue.Count == 0 && _plan is null) BeginEnemyPhase();
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

    void UpdateIdle(BattleInput i)
    {
        MoveCursor(i);
        var u = _s.Field.UnitAt(_cursor.X, _cursor.Y);
        if (i.RightClick && u is not null) { _statusUnit = u; _state_ = State.Status; return; }
        if (!Pick(i)) return;
        if (u is { Side: Side.Ally, Acted: false })
        {
            _sel = u;
            _selFrom = (u.X, u.Y);
            _reach = _s.Field.Reachable(u);
            _state_ = State.Selected;
        }
        else if (u is not null) { _statusUnit = u; _state_ = State.Status; }
        else OpenTurnMenu();
    }

    void UpdateSelected(BattleInput i)
    {
        MoveCursor(i);
        if (i.Cancel || i.RightClick) { _state_ = State.Idle; _sel = null; return; }
        if (!Pick(i) || _sel is null || _reach is null) return;
        var t = (_cursor.X, _cursor.Y);
        if (!_reach.ContainsKey(t)) { _state_ = State.Idle; _sel = null; return; }
        var path = Battlefield.PathTo(_reach, t);
        _s.Walk(_sel, path);
        _pendingPath = path;
        _state_ = State.Moving;
    }
    List<(int X, int Y)> _pendingPath = new();

    void OpenActionMenu()
    {
        AfterMove(_sel!, _pendingPath);
        BuildActionMenu();
        if (_events.Count > 0) { _resumeMenu = true; _state_ = State.Events; return; }
        _state_ = State.ActionMenu;
    }
    bool _resumeMenu;

    void BuildActionMenu()
    {
        var u = _sel!;
        _targets = _s.Field.TargetsFrom(u, u.X, u.Y).ToList();
        // WORD.DAT: 16 攻撃, 17 必殺技 (magic), 18 アイテム, 15 行動終了
        _menu = new[] { Word(16), Word(17), Word(18), Word(15) };
        _menuEnabled = new[] { _targets.Count > 0, _s.Field.KnownSpells(u).Any(), UsableItems(u).Any(), true };
        _menuIndex = _targets.Count > 0 ? 0 : 3;
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

    bool ItemReady(BattleUnit u, Item it) => _s.Field.ItemSpell(it) is { } m
        ? _s.Field.SpellAims(u, m, u.X, u.Y).Any()
        : _s.Field.MedicineTargets(u, it).Any();

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

    Rectangle RowRect(int row) => _state_ == State.ItemList
        ? new Rectangle(ItemListRect.X + 4, ItemListRect.Y + 8 + (row % ItemRows) * 16, ItemListRect.Width - 8, 16)
        : new Rectangle(SpellListRect().X + 6, SpellListRect().Y + 8 + row * 16, SpellListRect().Width - 12, 16);

    int ListNavigate(BattleInput i)
    {
        int n = _menu.Length;
        if (n == 0) return -1;
        if (i.Up) _menuIndex = (_menuIndex + n - 1) % n;
        if (i.Down) _menuIndex = (_menuIndex + 1) % n;
        if (_state_ == State.ItemList)
        {
            int pages = (n + ItemRows - 1) / ItemRows;
            if (i.Left || i.Right) _menuIndex = Math.Min(n - 1, ((_menuIndex / ItemRows + (i.Right ? 1 : pages - 1)) % pages) * ItemRows);
            _itemPage = _menuIndex / ItemRows;
        }
        int first = _state_ == State.ItemList ? _itemPage * ItemRows : 0;
        int count = _state_ == State.ItemList ? Math.Min(ItemRows, n - first) : n;
        int hover = -1;
        for (int r = 0; r < count; r++)
            if (RowRect(first + r).Contains(MouseVirtual)) hover = first + r;
        if (i.MouseMoved && hover >= 0) _menuIndex = hover;
        if ((i.Confirm || (i.Click && hover >= 0)) && _menuEnabled[_menuIndex]) return _menuIndex;
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
        if (!_aimRange.Contains(a) || _s.Field.SpellTargets(_sel!, _aimSpell, a.X, a.Y).Count == 0) return;
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
        if (target is not null && _aimItem is not null) DoMedicine(_sel!, _aimItem, target);
    }

    void DoCast(BattleUnit caster, Magic m, (int X, int Y) aim, Item? item)
    {
        var hostiles = _s.Field.SpellTargets(caster, m, aim.X, aim.Y).Where(t => Battlefield.Hostile(caster, t)).ToList();
        foreach (var t in hostiles) AttackTriggers(caster, t, 10);
        if (item is not null) { _state.AddItem(item.Index, -1); Popup($"{caster.Name}は{item.Name}{Word(58)}"); }   // "を使います"
        var before = HpSnapshot(_s.Field.SpellTargets(caster, m, aim.X, aim.Y).Append(caster));
        var r = _s.Field.Cast(caster, m, aim.X, aim.Y, fromItem: item is not null);
        if (ShowScenes && r.Effects.Count > 0) QueueSpellScenes(caster, m, r, before);
        ShowResult(caster, r, numbersShown: ShowScenes);
        foreach (var t in hostiles) AttackTriggers(caster, t, 11);
        Finish(caster);
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
        _afterShow = actor.Side == Side.Ally ? State.Idle : State.EnemyPhase;
        if (actor.Side == Side.Ally) AfterAction(actor);
        RunScenes();
    }

    /// <summary>
    /// Spell scenes: the caster's class cast animation (magic-to-enemy for harmful spells, magic-to-ally
    /// otherwise), then the spell's own animation (ANIME 80 + Magic.Animation) on each affected unit in turn.
    /// One scene per target (provisional).
    /// </summary>
    void QueueSpellScenes(BattleUnit caster, Magic m, ExchangeResult r, Dictionary<BattleUnit, int> before)
    {
        var cls = _s.Field.Class(caster);
        bool harmful = m.Type is 0 or 1;
        var cast = _db.Animations[Math.Clamp(harmful ? cls.AnimMagicEnemy : cls.AnimMagicAlly, 0, _db.Animations.Length - 1)];
        var spell = _db.Animations[Math.Clamp(m.AnimeIndex, 0, _db.Animations.Length - 1)];
        bool first = true;
        foreach (var e in r.Effects)
        {
            bool numeric = e.Kind is EffectKind.Damage or EffectKind.Heal or EffectKind.Miss;
            var steps = new List<BattleScene.Step>();
            if (first) steps.Add(new BattleScene.Step(caster, e.Target, cast, BattleScene.SheetFor(_db, _s.Field, caster, cast), true, 0, false, Caption: ""));
            steps.Add(new BattleScene.Step(caster, e.Target, spell, BattleScene.SheetFor(_db, _s.Field, caster, spell),
                e.Kind != EffectKind.Miss && e.Kind != EffectKind.Resisted, e.Amount, e.Kind == EffectKind.Heal,
                Caption: numeric ? null : ""));
            _scenes.Enqueue(new BattleScene(_db, _tex, _ui, _s.Field, caster, e.Target, steps, before));
            first = false;
        }
        BattleMusic(caster, r.Effects[0].Target);
    }

    /// <summary>Result popups (WORD.DAT phrases where the game has them; the rest provisional).</summary>
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
        foreach (var (u, exp) in r.ExpGained)
            Popup($"{Wide(exp)}{Word(60)}");             // 「１７の経験値を獲得」: no name, full-width digits (T22)
        foreach (var (u, _) in r.LevelUps)
            Popup($"{u.Name}{Word(70)}{u.Level}{Word(71)}");   // "はレベル" n "になった"
        foreach (var (u, spells) in r.Learned)
            foreach (var m in spells)
                Popup($"{u.Name}は{m.Name}{Word(73)}");         // "を習得した"
    }

    Rectangle MenuRect()
    {
        int w = _menu.Max(m => _ui.SmallText.Measure(m)) + 24, h = _menu.Length * 16 + 16;
        var p = _sel is null ? new Point(ErumaniGame.ViewX + 8, ErumaniGame.ViewY + 8)
            : _s.ScreenPos(_sel.X + 1, _sel.Y, ViewportRect).ToPoint() + new Point(8, 0);
        if (p.X + w > ErumaniGame.ViewX + ErumaniGame.ViewWidth) p.X -= w + MapSession.Tile + 16;
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
        if ((i.Confirm || (i.Click && r.Contains(mouseVirtual))) && _menuEnabled[_menuIndex]) return _menuIndex;
        return -1;
    }

    public Point MouseVirtual { get; set; }

    void UpdateActionMenu(BattleInput i)
    {
        if (i.Cancel || i.RightClick)
        {
            // undo the move (allowed until the unit acts)
            _sel!.X = _selFrom.X;
            _sel.Y = _selFrom.Y;
            _state_ = State.Selected;
            return;
        }
        int pick = MenuNavigate(i, MouseVirtual);
        if (pick == 0) { _state_ = State.Targeting; _cursor = new Point(_targets[0].X, _targets[0].Y); }
        else if (pick == 1) OpenSpellList();
        else if (pick == 2) OpenItemList();
        else if (pick == 3) FinishAction();
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
        DoAttack(_sel!, target);
    }

    void OpenActionMenuAgain()
    {
        BuildActionMenu();
        _state_ = State.ActionMenu;
    }

    void DoAttack(BattleUnit atk, BattleUnit def)
    {
        AttackTriggers(atk, def, 10);
        var before = HpSnapshot(new[] { atk, def });
        var r = _s.Field.Attack(atk, def);
        if (ShowScenes)
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
        AttackTriggers(atk, def, 11);
        _afterShow = atk.Side == Side.Ally ? State.Idle : State.EnemyPhase;
        if (atk.Side == Side.Ally) AfterAction(atk);
        RunScenes();
    }

    void Popup(string text, BattleUnit? kill = null)
    {
        _popups.Enqueue(text);
        _popupKills.Enqueue(kill);
    }

    void NextPopup()
    {
        if (_popupKill is not null) { _s.Ghosts.Remove(_popupKill); _popupKill = null; }
        if (_popups.Count > 0) { _popup = _popups.Dequeue(); _popupKill = _popupKills.Dequeue(); _popupFrames = 0; _state_ = State.Showing; }
        else
        {
            _popup = null;
            _sel = null;
            if (CheckPoint()) return;
            _state_ = _afterShow;
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
        AfterAction(_sel);
        _sel = null;
        if (CheckPoint()) return;
        _state_ = State.Idle;   // the turn only ends from the menu (T19a)
    }

    void OpenTurnMenu()
    {
        // WORD.DAT: 19 ターン終了, 20 全体マップ, 21 セーブする, 22 ロードする, 23 ゲーム終了
        _menu = new[] { Word(19), Word(20), Word(21), Word(22), Word(23) };
        _menuEnabled = new[] { true, false, false, false, false };
        _menuIndex = 0;
        _sel = null;
        _state_ = State.TurnMenu;
    }

    void UpdateTurnMenu(BattleInput i)
    {
        if (i.Cancel || i.RightClick) { _state_ = State.Idle; return; }
        if (MenuNavigate(i, MouseVirtual) == 0) EndPlayerTurn();
    }

    void EndPlayerTurn()
    {
        _s.Field.NextPhase();          // enemy phase
        _state_ = State.EnemyPhase;
        _enemyQueue.Clear();
        _plan = null;
        TurnTelop(2, () =>
        {
            QueuePhaseEvents();
            if (CheckPoint()) return;
            BeginEnemyPhase();
        });
    }

    bool _planWalking;

    void BeginEnemyPhase()
    {
        _enemyQueue = new Queue<BattleUnit>(_s.Field.Alive.Where(u => u.Side != Side.Ally && !u.Acted).OrderBy(u => u.MapObject));
        _plan = null;
        _wait = 20;
    }

    void UpdateEnemyPhase(BattleInput i)
    {
        if (_wait > 0) { _wait -= i.FastForward ? 4 : 1; return; }
        if (_plan is { } p)
        {
            if (!_planWalking)
            {
                // the screen scrolls to each enemy before it moves (T19f), then follows it to its destination
                if (_s.Panning) return;
                _s.Walk(p.Unit, p.Path);
                if (p.Path.Count > 0) _s.PanTo(p.Destination.X, p.Destination.Y);
                _planWalking = true;
                return;
            }
            if (_s.MovesRunning || _s.Panning) return;
            if (p.SwitchToApply >= 0 && p.SwitchToApply < GameState.SwitchCount) _state.Switches[p.SwitchToApply] = true;
            _plan = null;
            p.Unit.Acted = true;
            if (p.Spell is { } sp && Battlefield.CanCast(p.Unit, sp) && _s.Field.SpellTargets(p.Unit, sp, p.Aim.X, p.Aim.Y).Count > 0)
            {
                _cursor = new Point(p.Aim.X, p.Aim.Y);
                DoCast(p.Unit, sp, p.Aim, null);
                return;
            }
            if (p.Target is { Dead: false } t && _s.Field.InRange(p.Unit, p.Unit.X, p.Unit.Y, t)) { DoAttack(p.Unit, t); return; }
            if (CheckPoint()) return;
            _wait = 8;
            return;
        }
        while (_enemyQueue.Count > 0)
        {
            var u = _enemyQueue.Dequeue();
            if (u.Dead || u.Hidden) continue;
            _plan = EnemyAi.Plan(_s.Field, u, _state.Switches);
            _planWalking = false;
            _s.PanTo(u.X, u.Y);
            return;
        }
        // enemy phase over: back to the player
        _s.Field.NextPhase();
        _state_ = State.Idle;
        TurnTelop(1, () =>
        {
            QueuePhaseEvents();
            CheckPoint();
        });
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
        // T21: spell range and the area around the aim in blue stripes (tile 3), the tiles of affected units
        // solid blue; medicine range in salmon stripes (tile 1)
        if (_state_ == State.Aiming && _aimSpell is not null && _sel is not null)
        {
            var tiles = new HashSet<(int X, int Y)>(_aimRange);
            if (_aimRange.Contains((_cursor.X, _cursor.Y)))
            {
                tiles.UnionWith(_s.Field.SpellArea(_aimSpell, _cursor.X, _cursor.Y));
                _s.DrawRange(sb, viewport, tiles, 3, frame);
                _s.DrawRange(sb, viewport, _s.Field.SpellTargets(_sel, _aimSpell, _cursor.X, _cursor.Y).Select(u => (u.X, u.Y)), 3, frame, solid: true);
            }
            else _s.DrawRange(sb, viewport, tiles, 3, frame);
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
        }
        if (_state_ is State.ActionMenu or State.TurnMenu) DrawMenu(sb);
        if (_state_ is State.SpellList or State.ItemList) DrawList(sb, frame);
        if (_state_ == State.Showing && _popup is not null) DrawPopup(sb, _popup);
        if (_state_ == State.Status && _statusUnit is not null) DrawStatus(sb, _statusUnit);
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

    void DrawMenu(SpriteBatch sb)
    {
        var r = MenuRect();
        _ui.Panel(sb, r);
        for (int i = 0; i < _menu.Length; i++)
        {
            var c = !_menuEnabled[i] ? new Color(120, 140, 120) : i == _menuIndex ? new Color(255, 255, 120) : Ui.Ui.PanelText;
            if (_ui.SmallText.Get(_menu[i]) is { } t) sb.Draw(t, new Vector2(r.X + 13, r.Y + 10 + i * 16), c);
        }
    }

    void DrawList(SpriteBatch sb, int frame)
    {
        bool items = _state_ == State.ItemList;
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
            if (hot) _ui.Fill(sb, row, bar);
            var c = hot ? new Color(0, 32, 0) : !_menuEnabled[i] ? new Color(120, 140, 120) : Ui.Ui.PanelText;
            T(_menu[i], row.X + 8, row.Y + 2, c);
            if (items) T($"× {_state.ItemCount(_items[i].Index)}", row.Right - 8, row.Y + 2, c, right: true);
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

    /// <summary>Status window (screenshot 6 layout, approximate): blue panel with WORD.DAT labels.</summary>
    void DrawStatus(SpriteBatch sb, BattleUnit u)
    {
        var r = new Rectangle(188, 96, 264, 288);
        _ui.Panel(sb, r, Ui.Ui.PanelColor.Blue);
        var st = _s.Field.Stats(u);
        var cls = _s.Field.Class(u);
        void T(string s, int x, int y) { if (_ui.SmallText.Get(s) is { } t) sb.Draw(t, new Vector2(r.X + x, r.Y + y), Ui.Ui.PanelText); }
        T(u.Name, 12, 10);
        T(cls.Name, 12, 38);
        T($"{Word(1)}  {u.Level} ({u.Exp})", 150, 38);
        T($"{Word(2)}  {u.Hp}／{u.MaxHp}", 12, 58);
        T($"{Word(3)}  {u.Mp}／{u.MaxMp}", 150, 58);
        int[] vals = { st.Attack, st.Speed, st.Defense, st.MagicDefense, st.Str, st.Mag, st.Skl, st.Sta };
        for (int i = 0; i < 8; i++) T($"{Word(4 + i)}  {vals[i]}", 12, 90 + i * 16 + (i >= 4 ? 8 : 0));
        string It(int id) => id >= 0 && id < _db.Items.Length ? _db.Items[id].Name : "―";
        T($"{Word(12)}  {It(u.Weapon)}", 12, 236);
        T($"{Word(13)}  {It(u.Armor)}", 12, 252);
        T($"{Word(14)}  {It(u.Accessory)}", 12, 268);
        int face = _db.Units[u.Unit].Face;
        if (face > 0 && _tex.Get($"FACE_{face:000}.BMP") is { } f) sb.Draw(f, new Vector2(r.X + 128, r.Y + 90), Color.White);
    }
}
