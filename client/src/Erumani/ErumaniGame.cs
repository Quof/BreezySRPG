using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Erumani.Config;
using Srpg95.Data;

namespace Erumani;

/// <summary>
/// The client. Loads the original data, renders at the original 640x480 and scales to the
/// window. Each map's OPENING event runs when the map loads.
/// Controls: see client/README.md (rebindable under Options, F1).
/// </summary>
public sealed class ErumaniGame : Game
{
    public const int VirtualWidth = 640, VirtualHeight = 480;
    /// <summary>Map viewport inside the SFRAME border (original layout).</summary>
    public const int ViewX = 16, ViewY = 16, ViewWidth = 608, ViewHeight = 448;

    readonly GraphicsDeviceManager _gfx;
    readonly GameFolder _folder;
    readonly Options _opts;
    Database _db = null!;
    Textures _tex = null!;
    Ui.Ui _ui = null!;
    Audio _audio = null!;
    SpriteBatch _sb = null!;
    RenderTarget2D _screen = null!;
    /// <summary>The frame before a wipe started: what the wipe takes away (ScreenEffects.Transition).</summary>
    RenderTarget2D _snapshot = null!;
    readonly GameState _state = new();
    ScreenEffects _fx = new();
    MapSession? _session;
    EventRunner? _runner;
    BattleController? _battle;
    Camp.CampScreen? _camp;
    TitleScreen? _title;
    Settings _settings = null!;
    Controls _controls = null!;
    Ui.OptionsScreen? _options;
    Ui.Backlog _backlog = null!;
    bool _ffToggle;
    bool _gaveItems;
    Queue<string> _script = new();
    readonly List<string> _trace = new();
    int _scriptWait, _idleFrames;
    MapOutcome _seenOutcome;
    int[] _mapIndices = Array.Empty<int>();
    int _mapPos, _frame, _shotAdvance;
    bool _debug;
    string _status = "";
    KeyboardState _prevKeys;
    MouseState _prevMouse;
    int _activeFrames, _leftHeld;
    readonly Queue<string> _choose = new();

    static readonly BlendState Multiply = new()
    {
        ColorSourceBlend = Blend.DestinationColor, ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.Zero,
    };

    public ErumaniGame(GameFolder folder, Options opts)
    {
        _folder = folder;
        _opts = opts;
        _gfx = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VirtualWidth * 2,
            PreferredBackBufferHeight = VirtualHeight * 2,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = false;   // the SYSTEM.BMP cursor is drawn instead
        Window.AllowUserResizing = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60);
        _shotAdvance = opts.Advance;
        foreach (var tok in (opts.Keys ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) _script.Enqueue(tok.ToLowerInvariant());
        foreach (var c in (opts.Choose ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) _choose.Enqueue(c.ToLowerInvariant());
    }

    protected override void LoadContent()
    {
        _db = Database.Load(_folder);
        _tex = new Textures(GraphicsDevice, _folder);
        _ui = new Ui.Ui(GraphicsDevice, _tex);
        _audio = new Audio(_folder) { Muted = _opts.Screenshot is not null };
        Sfx.Audio = _audio;
        _settings = Settings.Load(persistent: _opts.Screenshot is null);
        _controls = new Controls(_settings);
        _backlog = new Ui.Backlog(_ui);
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "soundfonts"));
        ApplySettings();
        _sb = new SpriteBatch(GraphicsDevice);
        _screen = new RenderTarget2D(GraphicsDevice, VirtualWidth, VirtualHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _snapshot = new RenderTarget2D(GraphicsDevice, VirtualWidth, VirtualHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        _mapIndices = Directory.GetFiles(Path.Combine(_folder.Root, "MAP"), "SMAP_*.DAT")
            .Select(p => int.Parse(Path.GetFileNameWithoutExtension(p)[5..]) - 1).Order().ToArray();
        SaveFile.Directory = _opts.SaveDir;
        int start = _opts.StartMap ?? _db.StartMap;
        if (_opts.LoadSlot is int slot && SaveFile.Load(SaveFile.PathFor(_folder, slot)) is { } save) { ApplySave(save); return; }
        if (_opts.CampBefore is int camp) { OpenCamp(Math.Max(0, Array.IndexOf(_mapIndices, camp))); return; }
        if (_opts.StartMap is null && _opts.Event is null) { OpenTitle(); return; }
        LoadMap(Math.Max(0, Array.IndexOf(_mapIndices, start)));
    }

    void OpenTitle()
    {
        _camp = null;
        _session = null;
        _runner = null;
        _battle = null;
        _pendingMap = _pendingCamp = null;
        _pendingTitle = false;
        _audio.PlayBgm("");
        _backlog.Clear();
        _title = new TitleScreen(_db, _folder, _tex, _ui)
        {
            NewGame = () => { _state.Reset(); LoadMap(Math.Max(0, Array.IndexOf(_mapIndices, _db.StartMap))); },
            Loaded = ApplySave,
            Options = OpenOptions,
            Quit = Exit,
        };
        Window.Title = "Erumani";
        _status = "";
    }

    /// <summary>The camp before map <paramref name="pos"/> (index into the map list).</summary>
    void OpenCamp(int pos)
    {
        _mapPos = pos;
        int idx = _mapIndices[pos];
        var map = MapData.Load(_folder.MapPath(idx));
        _state.MapIndex = idx;
        _state.NextMap = (idx + 1) % 100;
        _session = null;
        _runner = null;
        _battle = null;
        _pendingMap = null;
        _camp = new Camp.CampScreen(_db, _folder, _tex, _ui, _state, map, idx)
        {
            StartNext = () => LoadMap(pos, fromCamp: true),
            Loaded = ApplySave,
            Quit = Exit,
        };
        _audio.PlayBgm(_camp.Bgm);
        Window.Title = $"Erumani — camp before SMAP_{idx + 1:000} {map.Name}";
        _status = "";
    }

    /// <summary>Loads a SAVEn.DAT: camp saves reopen their camp; battle saves restart their map with the saved army
    /// (restoring the battle itself comes later).</summary>
    void ApplySave(SaveFile save)
    {
        save.ApplyTo(_state);
        int pos = Math.Max(0, Array.IndexOf(_mapIndices, save.MapIndex));
        if (save.AtCamp) OpenCamp(pos);
        else LoadMap(pos, save);
    }

    void LoadMap(int pos, SaveFile? resume = null, bool fromCamp = false)
    {
        _camp = null;
        _title = null;
        _mapPos = pos;
        int idx = _mapIndices[_mapPos];
        var map = MapData.Load(_folder.MapPath(idx));
        _state.MapIndex = idx;
        if (resume is null) _state.NextMap = (idx + 1) % 100;   // FUN_0041d5e4: the following map unless a "Next Map" says otherwise
        _fx = new ScreenEffects();
        _session = new MapSession(map, _db, _state, _tex, _folder.Chipset(map.Tileset));
        _runner = new EventRunner(_state, _session, _audio, _fx) { Shown = _backlog.Add };
        if (_opts.Screenshot is not null) _session.Field.Rng = new Random(1);   // reproducible scripted runs
        _battle = new BattleController(_session, _runner, _state, _db, _ui, _tex);
        // the exe stops the music when a map loads (FUN_0041d5e4 plays ""), except that a camp's music carries on into
        // the opening; the player-turn music comes with PLAYER TURN. A battle save brings back the music it saved.
        if (resume is not null) _audio.PlayBgm(resume.Bgm is { Length: > 0 } b ? b : map.Bgm[1]);
        else if (!fromCamp) _audio.PlayBgm("");
        // developer options (--level, --give): set up situations for testing
        if (_opts.Level is int lv)
            foreach (var u in _session.Field.Units.Where(u => u.Side == Srpg95.Rules.Side.Ally && u.Level < lv)) u.Level = lv;
        if (_opts.Place is { } place)
            foreach (var e in place.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (e.Split(':') is [var id, var at] && at.Split('.') is [var px, var py, ..] parts
                    && _session.Field.Units.FirstOrDefault(u => u.Unit == int.Parse(id)) is { } pu)
                {
                    (pu.X, pu.Y, pu.ForceShown, pu.Hidden) = (int.Parse(px), int.Parse(py), true, false);
                    if (parts.Length > 2) pu.Hp = int.Parse(parts[2]);
                }
        if (_opts.Give is { } give && !_gaveItems)
        {
            _gaveItems = true;
            foreach (var g in give.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (g.Split(':') is [var id, var n]) _state.AddItem(int.Parse(id), int.Parse(n));
        }
        ApplyGameplay();
        _battle.PlayMapBgm = slot => _audio.PlayBgm(map.Bgm[slot]);
        _battle.PlaySound = _audio.PlaySe;
        _battle.SlotExists = n => File.Exists(SaveFile.PathFor(_folder, n));
        _battle.SaveRequested = n =>
        {
            string path = SaveFile.PathFor(_folder, n);
            SaveFile.ForBattle(_state, _db, idx, _audio.CurrentBgm ?? map.Bgm[1], _session.Field.Phase, _folder.MapPath(idx),
                _session.Field.Units, _battle.FiredSlots, SaveFile.Load(path)?.Raw ?? _state.LastSave).Write(path);
        };
        _battle.LoadRequested = n => { if (SaveFile.Load(SaveFile.PathFor(_folder, n)) is not { } f) return false; ApplySave(f); return true; };
        _battle.QuitRequested = Exit;
        if (resume is not null)
        {
            // a battle save: its units, phase and fired events instead of the opening (statuses aren't saved)
            _session.RestoreFrom(resume);
            _battle.Resume(resume.FiredSlots());
        }
        else _battle.Start(_opts.Event);
        _seenOutcome = MapOutcome.None;
        _pendingMap = null;
        Window.Title = $"Erumani — SMAP_{idx + 1:000} {map.Name}";
        _status = "";
    }

    // ---------------- settings ----------------

    void OpenOptions() => _options = new Ui.OptionsScreen(_settings, _ui, _folder.Root, ApplySettings);

    void ApplySettings()
    {
        _audio.MusicVolume = _settings.MusicVolume / 100f;
        _audio.SoundVolume = _settings.SoundVolume / 100f;
        _audio.MessageSound = _settings.MessageSound;
        _audio.SetSoundFont(_settings.Music == MusicEngine.SoundFont ? _settings.ResolveSoundFont(_folder.Root) : null);
        ApplyGameplay();
        ApplyDisplay();
    }

    void ApplyGameplay()
    {
        if (_runner is not null) _runner.FastForwardAdvancesText = _settings.FastForwardAdvancesText;
        if (_battle is not null)
        {
            _battle.ShowScenes = _settings.BattleScenes;
            _battle.FastForwardSkipsScenes = _settings.FastForwardSkipsScenes;
            _battle.SkipEnemyTurn = _settings.SkipEnemyTurn;
        }
        Ui.Ui.Modern = _settings.ModernUi;
    }

    void ApplyDisplay()
    {
        if (_opts.Screenshot is not null) return;
        _gfx.HardwareModeSwitch = false;   // borderless fullscreen
        if (_settings.Fullscreen)
        {
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _gfx.PreferredBackBufferWidth = mode.Width;
            _gfx.PreferredBackBufferHeight = mode.Height;
            _gfx.IsFullScreen = true;
        }
        else
        {
            _gfx.IsFullScreen = false;
            _gfx.PreferredBackBufferWidth = VirtualWidth * _settings.WindowScale;
            _gfx.PreferredBackBufferHeight = VirtualHeight * _settings.WindowScale;
        }
        _gfx.ApplyChanges();
    }

    void EndUpdate(GameTime gameTime, KeyboardState k, MouseState m)
    {
        _prevKeys = k;
        _prevMouse = m;
        base.Update(gameTime);
    }

    protected override void Update(GameTime gameTime)
    {
        var k = Keyboard.GetState();
        var m = Mouse.GetState();
        _controls.Update(IsActive ? k : default);
        _mouseVirtual = ToVirtual(m.Position);
        bool Pressed(Keys key) => k.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);
        bool alt = k.IsKeyDown(Keys.LeftAlt) || k.IsKeyDown(Keys.RightAlt);
        // clicks only count inside the window, and not just after it lost or regained focus (clicking another window,
        // or clicking back into this one, used to open the turn menu)
        _activeFrames = IsActive ? _activeFrames + 1 : 0;
        bool mouseHere = _activeFrames > 6 && Window.ClientBounds is var cb && m.X >= 0 && m.Y >= 0 && m.X < cb.Width && m.Y < cb.Height;
        bool click = mouseHere && m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool rclick = mouseHere && m.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
        int wheel = IsActive && _opts.Screenshot is null ? (m.ScrollWheelValue - _prevMouse.ScrollWheelValue) / 120 : 0;
        bool mouseMoved = m.Position != _prevMouse.Position;

        if (_runner?.Choice is { } ch && mouseMoved)
        {
            var cr = ChoiceRect(ch);
            _choiceActive = cr.Contains(_mouseVirtual);
            if (_choiceActive) _runner.HoverChoice((_mouseVirtual.Y - cr.Y - 8) / 16);
        }
        if (_controls.Pressed(GameAction.Fullscreen) || (alt && Pressed(Keys.Enter)))
        {
            _settings.Fullscreen = !_settings.Fullscreen;
            _settings.Save();
            ApplyDisplay();
        }
        if (Pressed(Keys.F5))
        {
            _settings.Scaling = _settings.Scaling == Scaling.Integer ? Scaling.Fit : Scaling.Integer;
            _settings.Save();
        }
        if (Pressed(Keys.F3)) _debug = !_debug;
        if (_session is not null && Pressed(Keys.PageDown)) LoadMap((_mapPos + 1) % _mapIndices.Length);
        if (_session is not null && Pressed(Keys.PageUp)) LoadMap((_mapPos + _mapIndices.Length - 1) % _mapIndices.Length);

        // keyboard: arrows move the map cursor and menus; WASD scroll the camera, and move in menus too
        bool menuOpen = _options is not null || _title is not null || _camp is not null || _runner?.Choice is not null || _battle?.MenuOpen == true;
        bool up = _controls.Repeat(GameAction.Up) || (menuOpen && _controls.Repeat(GameAction.CameraUp));
        bool down = _controls.Repeat(GameAction.Down) || (menuOpen && _controls.Repeat(GameAction.CameraDown));
        bool left = _controls.Repeat(GameAction.Left) || (menuOpen && _controls.Repeat(GameAction.CameraLeft));
        bool right = _controls.Repeat(GameAction.Right) || (menuOpen && _controls.Repeat(GameAction.CameraRight));
        bool confirm = (!alt && _controls.Pressed(GameAction.Confirm)) || click;
        bool cancel = _controls.Pressed(GameAction.Cancel) || rclick;
        if (_settings.FastForwardMode == FastForwardMode.Toggle && _controls.Pressed(GameAction.FastForward)) _ffToggle = !_ffToggle;
        bool ff = _settings.FastForwardMode == FastForwardMode.Hold ? _controls.Down(GameAction.FastForward) : _ffToggle;
        // holding the left button (after a quarter second, so ordinary clicks stay clicks) fast-forwards too
        _leftHeld = mouseHere && m.LeftButton == ButtonState.Pressed ? _leftHeld + 1 : 0;
        if (_settings.FastForwardMouse && _leftHeld > 15) ff = true;

        var input = new EventInput(confirm, cancel, up, down, ff);
        if (_opts.Screenshot is not null) input = new EventInput(false, false, false, false, false);   // scripted runs ignore real input
        if (_opts.Screenshot is not null && _shotAdvance != 0 && (_battle is { WaitingForClick: true } || _battle is { EventRunning: true } && _runner is { Message: not null } or { Choice: not null } or { WaitingForButton: true }))
        {
            if (_runner is { Choice: { } options } && _choose.TryDequeue(out var pick))
                _runner.HoverChoice(pick == "last" ? options.Length - 1 : int.TryParse(pick, out var n) ? Math.Clamp(n, 0, options.Length - 1) : 0);
            input = input with { Confirm = true };
            _shotAdvance--;
        }

        if (input.Up || input.Down) _choiceActive = true;
        var battleInput = new BattleInput(
            Confirm: input.Confirm, Cancel: input.Cancel, Up: up, Down: down, Left: left, Right: right, MouseTile: MouseTile(),
            MouseMoved: mouseMoved, Click: click, RightClick: rclick, FastForward: input.FastForward,
            Info: _controls.Pressed(GameAction.Info), Wheel: wheel);
        if (_battle is not null) _battle.MouseVirtual = _mouseVirtual;
        if (_opts.Screenshot is not null)   // ignore the real mouse while scripted
            battleInput = Scripted(new BattleInput(input.Confirm, false, false, false, false, false, null, false, false, false, false));

        // the CANCEL sound, wherever cancel does something (the options screen plays its own)
        if (battleInput.Cancel || battleInput.RightClick)
            if (_options is null && (_title?.AcceptsCancel == true || _camp?.AcceptsCancel == true || _battle?.AcceptsCancel == true || _backlog.Open))
                Sfx.Cancel();

        // options (F1 anywhere, or from the title): pauses everything else
        if (_options is null && _opts.Screenshot is null && _controls.Pressed(GameAction.Options)) OpenOptions();
        if (_options is not null)
        {
            _frame++;
            _audio.Update();
            _options.Update(battleInput, _mouseVirtual, _controls);
            if (_options.Done) _options = null;
            EndUpdate(gameTime, k, m);
            return;
        }

        // cutscene skip (modern option): Space during an event asks はい / いいえ first
        if (_opts.Screenshot is null && _skipPrompt is null && _controls.Pressed(GameAction.SkipCutscene) && CanSkipCutscene)
            OpenSkipPrompt();
        if (_skipPrompt is not null)
        {
            _frame++;
            _audio.Update();
            if (_opts.Screenshot is null)
            {
                int pick = _skipPrompt.Navigate(battleInput with { MouseMoved = mouseMoved }, _mouseVirtual);
                if (pick == 0) { _skipPrompt = null; _runner?.Skip(); }
                else if (pick == 1 || cancel) _skipPrompt = null;
            }
            EndUpdate(gameTime, k, m);
            return;
        }

        // text backlog: mouse wheel (or its key) on a map, outside battle scenes
        if (_session is not null && _battle?.SceneShowing != true)
        {
            if (wheel != 0) _backlog.Scroll(wheel);
            if (_controls.Pressed(GameAction.Backlog)) _backlog.Toggle();
            if (_backlog.Open)
            {
                if (cancel || click) _backlog.Close();
                _frame++;
                _audio.Update();
                EndUpdate(gameTime, k, m);
                return;
            }
        }

        if (_title is not null)
        {
            _frame++;
            _audio.Update();
            _title.Update(battleInput with { MouseMoved = battleInput.MouseMoved || _opts.Screenshot is null && mouseMoved }, _mouseVirtual);
            _prevKeys = k;
            _prevMouse = m;
            base.Update(gameTime);
            return;
        }
        if (_pendingTitle && battleInput.Confirm) { OpenTitle(); return; }
        if (_camp is not null)
        {
            _frame++;
            _audio.Update();
            _camp.Update(battleInput with { MouseMoved = battleInput.MouseMoved || _opts.Screenshot is null && mouseMoved }, _mouseVirtual);
            _prevKeys = k;
            _prevMouse = m;
            base.Update(gameTime);
            return;
        }

        if (_pendingCamp is int pc && (battleInput.Confirm || _autoAdvance && !_fx.Busy))
        {
            _pendingCamp = null;
            _autoAdvance = false;
            OpenCamp(pc);
            return;
        }
        if (_pendingMap is int pm && (battleInput.Confirm || _autoAdvance && !_fx.Busy))
        {
            _pendingMap = null;
            _autoAdvance = false;
            LoadMap(pm);
            input = input with { Confirm = false };
            battleInput = battleInput with { Confirm = false, Click = false };
        }

        // fast-forward: several simulation steps per frame (speed from the options)
        int steps = input.FastForward ? Math.Max(1, _settings.FastForwardSpeed) : 1;
        for (int i = 0; i < steps; i++)
        {
            if (i == 0) Step(k, input, battleInput);
            else Step(k, input with { Confirm = false, Up = false, Down = false, Cancel = false },
                battleInput with { Confirm = false, Cancel = false, Up = false, Down = false, Left = false, Right = false, Click = false, RightClick = false, MouseMoved = false });
        }
        _prevKeys = k;
        _prevMouse = m;
        base.Update(gameTime);
    }

    /// <summary>Screenshot mode: feeds the --keys queue, one token every 10 frames while the map is idle.</summary>
    BattleInput Scripted(BattleInput bi)
    {
        if (_script.TryPeek(out var next) && next.StartsWith("shot+")) { _script.Dequeue(); _shotAt = _frame + int.Parse(next[5..]); }
        // telop tokens fire wherever they are in the queue: the caption may come while other input is still queued
        var telopTok = _script.FirstOrDefault(s => s.StartsWith("telop"));
        if (telopTok is not null && System.Text.RegularExpressions.Regex.Match(telopTok, @"^telop(\d?)\+(\d+)$") is { Success: true } tm
            && _battle?.CurrentTelop is { } cur && (tm.Groups[1].Value == "" || cur == $"TELOP{tm.Groups[1].Value}.BMP"))
        {
            _script = new Queue<string>(_script.Where(s => !ReferenceEquals(s, telopTok)));
            _shotAt = _frame + int.Parse(tm.Groups[2].Value);
        }
        // cutscene skip: "skip" opens the prompt on the next message, "skipyes" answers はい
        if (_script.TryPeek(out var sk) && sk == "skip" && _runner is { Running: true, Message: not null }) { _script.Dequeue(); OpenSkipPrompt(); }
        if (_script.TryPeek(out sk) && sk == "skipyes" && _skipPrompt is not null) { _script.Dequeue(); _skipPrompt = null; _runner?.Skip(); }
        bool idle = _camp is not null || _title is not null || _battle is { Busy: false } && _runner is { Message: null, Choice: null };
        _idleFrames = idle ? _idleFrames + 1 : 0;
        if (!idle || _script.Count == 0 || _script.Peek().StartsWith("telop") || ++_scriptWait < 10) return bi;
        _scriptWait = 0;
        _idleFrames = 0;
        var tok = _script.Dequeue();
        _trace.Add($"{_frame}: {tok} in {(_title is not null ? "Title" : _camp is not null ? "Camp" : _battle!.DebugState.Split('\n')[0])}");
        return tok switch
        {
            "up" => bi with { Up = true },
            "down" => bi with { Down = true },
            "left" => bi with { Left = true },
            "right" => bi with { Right = true },
            "enter" => bi with { Confirm = true },
            "esc" => bi with { Cancel = true },
            "backlog" => Do(() => _backlog.Scroll(1), bi),
            _ when tok.StartsWith("at:") && tok[3..].Split('.') is [var x, var y] =>
                bi with { MouseTile = new Point(int.Parse(x), int.Parse(y)), MouseMoved = true },
            _ => bi,
        };
    }

    static BattleInput Do(Action a, BattleInput bi) { a(); return bi; }

    int _shotAt = int.MaxValue;
    bool ShotReady => _frame > 30000 || _frame >= _shotAt ||
        (_frame > 2 && (_opts.Keys is null ? _shotAdvance <= 0 && (_shotAdvance == 0 || _idleFrames > 30) : _script.Count == 0 && _idleFrames > 30));

    /// <summary>Map tile under the mouse, or null outside the viewport.</summary>
    Point? MouseTile()
    {
        if (_session is null) return null;
        var v = _mouseVirtual;
        if (v.X < ViewX || v.Y < ViewY || v.X >= ViewX + ViewWidth || v.Y >= ViewY + ViewHeight) return null;
        int tx = (v.X - ViewX + _session.Camera.X) / MapSession.Tile, ty = (v.Y - ViewY + _session.Camera.Y) / MapSession.Tile;
        return tx < _session.Map.Width && ty < _session.Map.Height ? new Point(tx, ty) : null;
    }

    void Step(KeyboardState k, EventInput input, BattleInput battleInput)
    {
        _frame++;
        _fx.Update();
        _audio.Update();
        if (_session is null || _battle is null) return;
        _session.Update();
        _battle.Update(battleInput, input);

        if (_battle.Outcome != _seenOutcome)
        {
            _seenOutcome = _battle.Outcome;
            OnMapEnded();
        }

        // camera: WASD, or the mouse at the viewport's edge (provisional), when nothing is animating
        if (_battle.Busy) return;
        const int speed = 8;
        var mv = _mouseVirtual;
        bool inView = IsActive && mv.X >= 0 && mv.Y >= 0 && mv.X < VirtualWidth && mv.Y < VirtualHeight;
        bool keys = !_battle.MenuOpen && _runner?.Choice is null;
        if ((keys && _controls.Down(GameAction.CameraLeft)) || (inView && mv.X < ViewX + 4)) _session.Camera.X -= speed;
        if ((keys && _controls.Down(GameAction.CameraRight)) || (inView && mv.X >= ViewX + ViewWidth - 4)) _session.Camera.X += speed;
        if ((keys && _controls.Down(GameAction.CameraUp)) || (inView && mv.Y < ViewY + 4)) _session.Camera.Y -= speed;
        if ((keys && _controls.Down(GameAction.CameraDown)) || (inView && mv.Y >= ViewY + ViewHeight - 4)) _session.Camera.Y += speed;
        _session.ClampCamera();
    }

    void OnMapEnded()
    {
        int idx = _mapIndices[_mapPos];
        switch (_battle!.Outcome)
        {
            case MapOutcome.Won:
            case MapOutcome.Lost:
                // FUN_0041083c → FUN_0041d5e4: won (after COMPLETE!!) or "lost" through an event, the game goes straight
                // on to header 0x010's map — a "Next Map" command's, else the following one — with its camp first if it
                // has one and the army isn't empty; the army was saved into the roster (BattleController.End)
                int next = _state.NextMap;
                _status = "";
                _autoAdvance = true;
                _fx.StartFade(true, 0, 6);   // the screen fades out first (brightness −5 a tick)
                if (Array.IndexOf(_mapIndices, next) is var p and >= 0)
                {
                    var nextMap = MapData.Load(_folder.MapPath(next));
                    if (nextMap.CampBeforeBattle && _state.Roster.Count > 0)
                    {
                        Party.PrepareSortie(_state, nextMap);
                        _pendingCamp = p;
                    }
                    else _pendingMap = p;
                }
                else _status = $"SMAP_{next + 1:000} doesn't exist";
                break;
            case MapOutcome.GameOver:
                _audio.PlayBgm(_session!.Map.Bgm[7]);    // game over
                _status = "";
                _pendingTitle = true;   // after GAMEOVER.BMP, a click goes back to the title
                break;
            case MapOutcome.ReturnToTitle:
                _status = "";
                OpenTitle();
                break;
        }
    }

    Camp.MenuList? _skipPrompt;

    /// <summary>An event is playing on a map and isn't waiting for a choice.</summary>
    bool CanSkipCutscene => _session is not null && _title is null && _camp is null && _runner is { Running: true, Choice: null }
        && _battle?.SceneShowing != true;

    void OpenSkipPrompt()
    {
        // the original's はい / いいえ (WORD.DAT 45, 46), defaulting to はい
        _skipPrompt = new Camp.MenuList { Rect = new Rectangle(216, 196, 208, 84), Header = "イベントをスキップしますか？", Indent = 16 };
        _skipPrompt.Set(new[] { _db.Words[45], _db.Words[46] });
        Sfx.Click();
    }

    void DrawSkipPrompt()
    {
        if (_skipPrompt is null) return;
        _ui.Fill(_sb, new Rectangle(0, 0, 640, 480), Color.Black * 0.35f);
        _skipPrompt.Draw(_sb, _ui, true, _frame);
    }

    int? _pendingMap, _pendingCamp;
    bool _autoAdvance;
    bool _pendingTitle;

    string SafeName(int i) => i >= 0 && i < _db.MapNames.Length ? _db.MapNames[i] : "?";

    protected override void Draw(GameTime gameTime)
    {
        // a wipe just started: keep the last frame, which the wipe will take away bit by bit
        if (_fx.SnapshotRequested)
        {
            GraphicsDevice.SetRenderTarget(_snapshot);
            _sb.Begin(samplerState: SamplerState.PointClamp);
            _sb.Draw(_screen, Vector2.Zero, Color.White);
            _sb.End();
            _fx.SnapshotTaken();
        }
        GraphicsDevice.SetRenderTarget(_screen);
        GraphicsDevice.Clear(Color.Black);
        if (_title is not null)
        {
            _sb.Begin(samplerState: SamplerState.PointClamp);
            _title.Draw(_sb, _frame);
            _options?.Draw(_sb, _frame);
            if (_opts.Screenshot is null && IsActive) _ui.Cursor(_sb, _mouseVirtual, false, _frame);
            _sb.End();
            Present(gameTime);
            return;
        }
        if (_camp is not null)
        {
            _sb.Begin(samplerState: SamplerState.PointClamp);
            _camp.Draw(_sb, _frame);
            _options?.Draw(_sb, _frame);
            if (_opts.Screenshot is null && IsActive) _ui.Cursor(_sb, _mouseVirtual, false, _frame);
            _sb.End();
            Present(gameTime);
            return;
        }
        var view = new Rectangle(ViewX + _fx.ShakeOffset.X, ViewY + _fx.ShakeOffset.Y, ViewWidth, ViewHeight);
        var viewClip = new Rectangle(ViewX, ViewY, ViewWidth, ViewHeight);

        // map, picture and screen effects live inside the 608x448 viewport
        _sb.Begin(samplerState: SamplerState.PointClamp);
        if (_session is not null)
        {
            _session.DrawTiles(_sb, view);
            _battle?.DrawOverlays(_sb, view, _frame);
            _session.DrawUnits(_sb, view, _frame);
        }
        DrawPicture();
        _sb.End();
        DrawTone(viewClip);
        _sb.Begin(samplerState: SamplerState.PointClamp);
        if (_fx.Fade > 0) _ui.Fill(_sb, viewClip, _fx.FadeColor * _fx.Fade);
        // frame on top of the picture (as in the original), then windows and cursor
        if (_session is { } s)
            _ui.ScreenFrame(_sb, s.Camera.Y > 0, s.Camera.Y < s.PixelHeight - ViewHeight,
                s.Camera.X > 0, s.Camera.X < s.PixelWidth - ViewWidth);
        _battle?.DrawUi(_sb, view, _frame);
        DrawMessage();
        DrawChoice();
        DrawStatus();
        if (_debug) DrawDebug();
        _backlog.Draw(_sb);
        DrawWipe();
        DrawSkipPrompt();
        _options?.Draw(_sb, _frame);
        if (_opts.Screenshot is null && IsActive)
            _ui.Cursor(_sb, _mouseVirtual, _runner?.Message is not null && !_backlog.Open && _options is null, _frame);
        _sb.End();

        Present(gameTime);
    }

    /// <summary>Screenshot (in screenshot mode) or scale the 640x480 screen to the window.</summary>
    void Present(GameTime gameTime)
    {
        if (_opts.Screenshot is { } shot && ShotReady)
        {
            GraphicsDevice.SetRenderTarget(null);
            using (var f = File.Create(shot)) _screen.SaveAsPng(f, VirtualWidth, VirtualHeight);
            File.WriteAllText(Path.ChangeExtension(shot, ".txt"), $"frame={_frame} script-left={_script.Count} {(_title is not null ? "Title" : _camp is not null ? $"Camp gold={_state.Gold} items={string.Join(",", _state.Items.Select(kv => $"{kv.Key}x{kv.Value}"))} roster={string.Join(",", _state.Roster.Select(kv => $"{kv.Key}:c{kv.Value.ClassId}/w{kv.Value.Weapon}/a{kv.Value.Armor}/x{kv.Value.Accessory}"))}" : _battle?.DebugState)}\n" + string.Join("\n", _trace) + "\n");
            Exit();
            return;
        }

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var bb = GraphicsDevice.PresentationParameters.Bounds;
        float scale = Math.Min((float)bb.Width / VirtualWidth, (float)bb.Height / VirtualHeight);
        if (_settings.Scaling == Scaling.Integer && scale >= 1) scale = MathF.Floor(scale);
        int w = (int)(VirtualWidth * scale), h = (int)(VirtualHeight * scale);
        if (_settings.Scaling == Scaling.Stretch) (w, h) = (bb.Width, bb.Height);
        _dest = new Rectangle((bb.Width - w) / 2, (bb.Height - h) / 2, w, h);
        _sb.Begin(samplerState: _settings.Smooth ? SamplerState.LinearClamp : SamplerState.PointClamp);
        _sb.Draw(_screen, _dest, Color.White);
        _sb.End();
        base.Draw(gameTime);
    }

    Rectangle _dest = new(0, 0, VirtualWidth, VirtualHeight);
    Point _mouseVirtual;

    /// <summary>Window pixel -> 640x480 virtual pixel.</summary>
    Point ToVirtual(Point p) => _dest.Width == 0 ? p : new Point(
        (p.X - _dest.X) * VirtualWidth / _dest.Width, (p.Y - _dest.Y) * VirtualHeight / _dest.Height);

    void DrawPicture()
    {
        foreach (var (file, position, transparent) in _fx.Pictures) DrawPicture(file, position, transparent);
    }

    /// <summary>
    /// A wipe (FUN_00422a00) in progress: the parts of the old screen (the snapshot, or a solid colour when a screen
    /// display starts from one) not yet replaced are drawn over the new one. 1 random 8×8 blocks (240 more a tick),
    /// 2 / 3 one-pixel lines every 20 px, 4 / 5 8-px bands, top-down (left-right) and back.
    /// </summary>
    void DrawWipe()
    {
        if (_fx.Transition == 0) return;
        int tick = _fx.TransitionTick;
        var from = _fx.TransitionFrom;
        void Old(Rectangle r)
        {
            if (from is { } c) _ui.Fill(_sb, r, c);
            else _sb.Draw(_snapshot, r, r, Color.White);
        }
        static bool BandShown(int band, int tick, int half) => band % 4 is 0 or 2 ? tick >= band / 4 : tick >= 2 * half - 1 - band / 4;
        switch (_fx.Transition)
        {
            case 1:
                for (int k = Math.Min(4800, 240 * (tick + 1)); k < 4800; k++)
                {
                    int b = _fx.WipeOrder[k];
                    Old(new Rectangle(b % 80 * 8, b / 80 * 8, 8, 8));
                }
                break;
            case 2:
                for (int y = 0; y < VirtualHeight; y++) if (y % 20 > tick) Old(new Rectangle(0, y, VirtualWidth, 1));
                break;
            case 3:
                for (int x = 0; x < VirtualWidth; x++) if (x % 20 > tick) Old(new Rectangle(x, 0, 1, VirtualHeight));
                break;
            case 4:
                for (int band = 0; band < 60; band++) if (!BandShown(band, tick, 15)) Old(new Rectangle(0, band * 8, VirtualWidth, 8));
                break;
            case 5:
                for (int band = 0; band < 80; band++) if (!BandShown(band, tick, 20)) Old(new Rectangle(band * 8, 0, 8, VirtualHeight));
                break;
        }
    }

    void DrawPicture(string file, int position, bool transparent)
    {
        var t = _tex.Get(file, transparent);
        if (t is null) return;
        // Numeric-keypad positions (7 8 9 / 4 5 6 / 1 2 3) inside the 16-px screen frame (FUN_004233f0): a picture as
        // wide or tall as the view itself slides out to the screen's edge instead.
        int p = Math.Clamp(position, 1, 9) - 1;
        int col = p % 3, row = 2 - p / 3, w = t.Width, h = t.Height;
        int x = col switch
        {
            0 => w >= 624 ? Math.Max(0, 640 - w) : 16,
            1 => 320 - w / 2,
            _ => w >= 624 ? Math.Max(0, 640 - w) : 624 - w,
        };
        int y = row switch
        {
            0 => h >= 464 ? Math.Max(0, 480 - h) : 16,
            1 => 240 - h / 2,
            _ => h >= 464 ? Math.Max(0, 480 - h) : 464 - h,
        };
        _sb.Draw(t, new Vector2(x, y), Color.White);
    }

    /// <summary>Colour tone: multiply for channels under 100 %, add for channels over (vividness not applied yet).</summary>
    void DrawTone(Rectangle area)
    {
        var tone = _fx.FlashTone ?? _fx.Tone;   // a flash shows its tone for a moment (FUN_00423914)
        if (tone.X == 100 && tone.Y == 100 && tone.Z == 100) return;
        static int Mul(float v) => (int)(Math.Clamp(v, 0, 100) * 2.55f);
        static int Add(float v) => (int)(Math.Clamp(v - 100, 0, 100) * 2.55f);
        _sb.Begin(blendState: Multiply);
        _ui.Fill(_sb, area, new Color(Mul(tone.X), Mul(tone.Y), Mul(tone.Z)));
        _sb.End();
        _sb.Begin(blendState: BlendState.Additive);
        _ui.Fill(_sb, area, new Color(Add(tone.X), Add(tone.Y), Add(tone.Z)));
        _sb.End();
    }

    // Layout measured from screenshots of the original (FORMAT_NOTES T17):
    // window 414x126 at y 33 / 177 / 321 (top / centre / bottom); x 113 without a face,
    // x 193 with a face on the left (face 128x128 at (32, y-1), speech tail on the window's
    // left edge). Text: GDI MS Gothic 18 px bold, colour (16,16,0), origin (+17, +11), 22 px lines.
    // Face-on-the-right placement is mirrored and still unverified.
    void DrawMessage()
    {
        var msg = _runner?.Message ?? (_runner?.Choice is not null ? _runner.LastMessage : null);
        if (msg is null) return;
        int y = msg.Window switch { 0 => 33, 1 => 177, _ => 321 };
        var face = msg.Face > 0 ? _tex.Get($"FACE_{msg.Face:000}.BMP") : null;
        int x = face is null ? 113 : msg.FaceRight ? VirtualWidth - 193 - Ui.Ui.MessageWidth : 193;
        _ui.MessageWindow(_sb, new Point(x, y), tail: face is not null && !msg.FaceRight);
        if (face is not null)
        {
            int fx = msg.FaceRight ? VirtualWidth - 32 - 128 : 32;
            _sb.Draw(face, new Rectangle(fx, y - 1, 128, 128), null, Color.White, 0, Vector2.Zero,
                msg.FaceFlip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }
        for (int i = 0; i < msg.Lines.Count; i++)
            _ui.DrawText(_sb, msg.Lines[i], new Vector2(x + 17, y + 11 + i * Ui.Ui.LineHeight), Ui.Ui.TextColor);
    }

    // Measured on the 11話 camp menu (T17b): green SYSTEM.BMP panel, 1-px light/dark edges,
    // white MS Gothic 12 px regular (text pixels match), origin (+13, +10), 16 px lines, width = text + 24.
    // The panel's bottom sits at y 303, just above the bottom message window. Its x was 208
    // for a 168-px panel; the rule behind that (centre 292?) is provisional.
    bool _choiceActive;

    Rectangle ChoiceRect(string[] opts)
    {
        int w = opts.Max(o => _ui.SmallText.Measure(o)) + 24, h = opts.Length * 16 + 16;
        return new Rectangle(292 - w / 2, 304 - h, w, h);
    }

    void DrawChoice()
    {
        if (_runner?.Choice is not { } opts) return;
        var r = ChoiceRect(opts);
        _ui.Panel(_sb, r);
        for (int i = 0; i < opts.Length; i++)
        {
            var pos = new Vector2(r.X + 13, r.Y + 10 + i * 16);
            // The reference shows no highlight while the mouse is elsewhere; the hover/keyboard
            // highlight colour is provisional.
            var c = _choiceActive && i == _runner.ChoiceIndex ? new Color(255, 255, 120) : Ui.Ui.PanelText;
            if (_ui.SmallText.Get(opts[i]) is { } t) _sb.Draw(t, pos, c);
        }
    }

    void DrawStatus()
    {
        if (_status.Length == 0) return;
        int w = _ui.Text.Measure(_status) + 32;
        var r = new Rectangle((VirtualWidth - w) / 2, ViewY + ViewHeight - 44, w, 36);
        _ui.Window(_sb, r);
        _ui.DrawText(_sb, _status, new Vector2(r.X + 16, r.Y + 10), Ui.Ui.TextColor);
    }

    void DrawDebug()
    {
        var lines = new List<string>
        {
            $"SMAP_{_mapIndices[_mapPos] + 1:000} {_session?.Map.Name}  BGM {_audio.CurrentBgm ?? "-"}  font {_ui.Text.FaceName}",
            $"gold {_state.Gold}  roster {string.Join(",", _state.Roster.Select(kv => kv.Value.Sortie ? $"{kv.Key}*" : $"{kv.Key}"))}  next {_state.NextMap}  event {_runner?.Name} {(_runner?.Running == true ? "running" : "idle")}",
        };
        lines.AddRange(_runner?.Log.TakeLast(8) ?? Enumerable.Empty<string>());
        _ui.Fill(_sb, new Rectangle(0, 0, VirtualWidth, 8 + lines.Count * 18), Color.Black * 0.75f);
        for (int i = 0; i < lines.Count; i++)
            _ui.DrawText(_sb, lines[i], new Vector2(6, 4 + i * 18), Color.LightGreen);
    }

    protected override void UnloadContent()
    {
        _tex?.Dispose();
        _ui?.Dispose();
        _audio?.Dispose();
        _screen?.Dispose();
        base.UnloadContent();
    }
}
