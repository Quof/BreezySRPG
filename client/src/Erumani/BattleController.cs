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
    enum State { Events, Idle, Selected, Moving, ActionMenu, Targeting, Showing, TurnMenu, EnemyPhase, Status, Ended }

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
    public string DebugState => $"{_state_} phase={_s.Phase} sel={_sel?.Name} at={_sel?.X},{_sel?.Y} cursor={_cursor.X},{_cursor.Y} outcome={Outcome}" +
        string.Concat(_s.Field.Units.Where(u => !u.Hidden).Select(u => $"\n  {u.Side} {u.Name} ({u.X},{u.Y}) HP {u.Hp} Lv {u.Level} Exp {u.Exp}{(u.Dead ? " dead" : "")}"));
    public int? NextMap => _runner.NextMap;
    public bool EventRunning => _state_ == State.Events && _runner.Running;
    public bool Busy => _state_ is State.Events or State.Moving or State.Showing or State.EnemyPhase;

    public BattleController(MapSession s, EventRunner runner, GameState state, Database db, Ui.Ui ui, Textures tex)
        => (_s, _runner, _state, _db, _ui, _tex) = (s, runner, state, db, ui, tex);

    string Word(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";

    // ---------------- events ----------------

    public void Start(string? firstEvent)
    {
        if (_s.Map.Scripts.TryGetValue(firstEvent ?? "OPENING", out var opening)) _events.Enqueue((firstEvent ?? "OPENING", opening));
        QueuePhaseEvents();
        _state_ = State.Events;
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
            if (_events.Count == 0) { End(MapOutcome.Won); return true; }
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
                case EventEnd.MapWon: End(MapOutcome.Won); return;
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
        if (_s.Field.AllyPhase) { _state_ = State.Idle; MaybeAutoEndTurn(); return; }
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
        _targets = _s.Field.TargetsFrom(_sel!, _sel!.X, _sel.Y).ToList();
        // WORD.DAT: 16 攻撃, 17 必殺技 (magic), 18 アイテム, 15 行動終了
        _menu = new[] { Word(16), Word(17), Word(18), Word(15) };
        _menuEnabled = new[] { _targets.Count > 0, false, false, true };   // magic/items: next milestone
        _menuIndex = _targets.Count > 0 ? 0 : 3;
        if (_events.Count > 0) { _resumeMenu = true; _state_ = State.Events; return; }
        _state_ = State.ActionMenu;
    }
    bool _resumeMenu;

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

    void OpenActionMenuAgain() => _state_ = State.ActionMenu;

    void DoAttack(BattleUnit atk, BattleUnit def)
    {
        AttackTriggers(atk, def, 10);
        var r = _s.Field.Attack(atk, def);
        foreach (var st in r.Strikes)
        {
            Popup(st.Hit
                ? $"{st.Attacker.Name} → {st.Target.Name}：{st.Damage}{(st.Critical ? "（会心）" : "")}{(st.Killed ? "　撃破" : "")}"
                : $"{st.Attacker.Name} → {st.Target.Name}：ミス", st.Killed ? st.Target : null);
            if (st.Killed) _s.Ghosts.Add(st.Target);
        }
        foreach (var (u, exp) in r.ExpGained)
            Popup($"{u.Name}：{exp}{Word(60)}");            // "の経験値を獲得"
        foreach (var (u, _) in r.LevelUps)
            Popup($"{u.Name}{Word(70)}{u.Level}{Word(71)}");   // "はレベル" n "になった"
        AttackTriggers(atk, def, 11);
        _afterShow = atk.Side == Side.Ally ? State.Idle : State.EnemyPhase;
        if (atk.Side == Side.Ally) AfterAction(atk);
        NextPopup();
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
            if (_state_ == State.Idle) MaybeAutoEndTurn();
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
        _state_ = State.Idle;
        MaybeAutoEndTurn();
    }

    void MaybeAutoEndTurn()
    {
        // provisional: the original may require "ターン終了"; we end the turn when nobody can act
        if (!_s.Field.Alive.Any(u => u.Side == Side.Ally && !u.Acted)) EndPlayerTurn();
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
        QueuePhaseEvents();
        if (CheckPoint()) return;
        _state_ = State.EnemyPhase;
        BeginEnemyPhase();
    }

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
            if (_s.MovesRunning) return;
            if (p.SwitchToApply >= 0 && p.SwitchToApply < GameState.SwitchCount) _state.Switches[p.SwitchToApply] = true;
            _plan = null;
            p.Unit.Acted = true;
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
            _s.Walk(u, _plan.Path);
            if (_plan.Path.Count > 0) _s.ScrollTo(_plan.Destination.X, _plan.Destination.Y);
            return;
        }
        // enemy phase over: back to the player
        _s.Field.NextPhase();
        QueuePhaseEvents();
        if (CheckPoint()) return;
        _state_ = State.Idle;
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
    }

    public void DrawUi(SpriteBatch sb, Rectangle viewport, int frame)
    {
        bool showCursor = _state_ is State.Idle or State.Selected or State.Targeting;
        if (showCursor && _s.Field.InBounds(_cursor.X, _cursor.Y))
        {
            var p = _s.ScreenPos(_cursor.X, _cursor.Y, viewport).ToPoint();
            _ui.Frame(sb, new Rectangle(p.X, p.Y, 32, 32), Color.Black);
            _ui.Frame(sb, new Rectangle(p.X + 1, p.Y + 1, 30, 30), Color.White, 2);
            if (_s.Field.UnitAt(_cursor.X, _cursor.Y) is { } u) DrawHoverLabel(sb, u, p);
        }
        if (_state_ is State.ActionMenu or State.TurnMenu) DrawMenu(sb);
        if (_state_ == State.Showing && _popup is not null) DrawPopup(sb, _popup);
        if (_state_ == State.Status && _statusUnit is not null) DrawStatus(sb, _statusUnit);
        if (_state_ == State.EnemyPhase && _wait > 10 && _plan is null && _enemyQueue.Count > 0 && frame % 60 < 50)
            DrawPopup(sb, "ENEMY PHASE");
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
