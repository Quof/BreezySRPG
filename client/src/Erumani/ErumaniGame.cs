using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Erumani.Config;
using Srpg95.Data;

namespace Erumani;

/// <summary>
/// The client. Loads the original data, draws in the original's 640x480 coordinates at the window's resolution
/// (see <see cref="_scale"/>). Each map's OPENING event runs when the map loads.
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
    /// <summary>What is shown: the graded frame where the shake puts it, the client's own windows over it.</summary>
    RenderTarget2D _final = null!;
    /// <summary>
    /// The render scale: the frames above are 640·k × 480·k, drawn in 640 × 480 coordinates through <see cref="_view"/>,
    /// k the smallest whole scale that fills the window — pixel art scaled up whole, the restyled skins' text rendered at
    /// that size (<see cref="Ui.Ui.RenderScale"/>), so a large window gets crisp text, not a stretched 640 × 480 picture.
    /// </summary>
    int _scale;
    Matrix _view = Matrix.Identity;
    const int MaxScale = 6;
    /// <summary>The palette's brightness and tone on the GPU (Shaders/Grade.fx); null where it won't run, and the CPU grades.</summary>
    Effect? _gradeFx;
    /// <summary>The grade's channel tables (red, green, blue in a 256 × 1 texture's channels) and what they were made for.</summary>
    Texture2D _gradeTable = null!;
    readonly Color[] _gradeTableData = new Color[256];
    (int R, int G, int B, int Bright)? _gradeTableFor;
    /// <summary>The CPU grade's frame and its pixels (only when the shader can't be used).</summary>
    Texture2D? _graded;
    Color[] _gradePixels = Array.Empty<Color>();
    readonly GameState _state = new();
    /// <summary>The screen's effects for the whole run: brightness and tone carry over from screen to screen as the palette does.</summary>
    readonly ScreenEffects _fx = new();
    /// <summary>What happens once the current fade-out has gone dark (title and camp choices, the logos).</summary>
    Action? _afterFade;
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
    /// <summary>The enemy ranges key's toggle, kept from map to map.</summary>
    bool _showThreat;
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
    readonly Random _chooseRng = new(1);
    int _choicesSeen;
    bool _choiceWasUp;

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
        // two frames per game tick: 33 ms at start-up, 30 ms on maps and camps (GameClock)
        TargetElapsedTime = TimeSpan.FromMilliseconds(GameClock.FrameMs);
        GameClock.Changed += () => TargetElapsedTime = TimeSpan.FromMilliseconds(GameClock.FrameMs);
        if (opts.Fast && opts.Screenshot is not null)
        {
            // a scripted run at full speed: everything advances by frames, so only the wall-clock time changes (and it
            // doesn't idle while its window is in the background, as several runs at once are)
            IsFixedTimeStep = false;
            InactiveSleepTime = TimeSpan.Zero;
            _gfx.SynchronizeWithVerticalRetrace = false;
        }
        _shotAdvance = opts.Advance;
        foreach (var tok in (opts.Keys ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) _script.Enqueue(tok.ToLowerInvariant());
        foreach (var c in (opts.Choose ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) _choose.Enqueue(c.ToLowerInvariant());
    }

    protected override void LoadContent()
    {
        _db = Database.Load(_folder);
        if (_db.Words.Length > 63) Camp.MenuList.PageLabel = _db.Words[63];   // ページ
        _tex = new Textures(GraphicsDevice, _folder);
        _ui = new Ui.Ui(GraphicsDevice, _tex);
        // the original's 12-px text comes from its own font (FUN_0040a284 loads KANJIFONT)
        if (_folder.Find("SRPGEXEC.EXE") is { } exePath && ExeResources.Raw(exePath, 0, "KANJI", "KANJIFONT") is { Length: >= 24 } glyphs)
            _ui.MenuFont = new Ui.ExeFont(GraphicsDevice, glyphs);
        _audio = new Audio(_folder) { Muted = _opts.Screenshot is not null };
        Sfx.Audio = _audio;
        _settings = Settings.Load(persistent: _opts.Screenshot is null);
        _controls = new Controls(_settings);
        _backlog = new Ui.Backlog(_ui);
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "soundfonts"));
        ApplySettings();
        _sb = new SpriteBatch(GraphicsDevice);
        _gradeTable = new Texture2D(GraphicsDevice, 256, 1, false, SurfaceFormat.Color);
        _gradeFx = LoadGradeShader();
        SetScale(1);

        _mapIndices = Directory.GetFiles(Path.Combine(_folder.Root, "MAP"), "SMAP_*.DAT")
            .Select(p => int.Parse(Path.GetFileNameWithoutExtension(p)[5..]) - 1).Order().ToArray();
        SaveFile.Directory = _opts.SaveDir;
        int start = _opts.StartMap ?? _db.StartMap;
        if (_opts.LoadSlot is int slot && SaveFile.Load(SaveFile.PathFor(_folder, slot)) is { } save) { ApplySave(save); return; }
        if (_opts.CampBefore is int camp) { OpenCamp(Math.Max(0, Array.IndexOf(_mapIndices, camp)), fromMap: true); return; }
        if (_opts.StartMap is null && _opts.Event is null)
        {
            if (_opts.Screenshot is null || _script.Contains("logos")) OpenLogos(); else OpenTitle();
            return;
        }
        LoadMap(Math.Max(0, Array.IndexOf(_mapIndices, start)));
    }

    // ---------------- start-up logos (FUN_004256b8, FUN_00425750, FUN_004257b0) ----------------
    // A normal start shows the bitmaps LOGO1 (ASCII) and LOGO2 (アスキーツクールシリーズ) from SRPGEXEC.EXE's own
    // resources, each fading in 10 % a tick, held until its 51st tick (or a click once fully bright), fading out the same
    // way; then the title. (The editor's test play, started with '@', skips them.)

    readonly List<Texture2D> _logos = new();
    int _logo = -1, _logoTick, _logoFrame;

    void OpenLogos()
    {
        string? exe = _folder.Find("SRPGEXEC.EXE");
        foreach (var name in new[] { "LOGO1", "LOGO2" })
            if (exe is not null && ExeResources.Bitmap(exe, name) is { } bmp)
            {
                var t = new Texture2D(GraphicsDevice, bmp.Width, bmp.Height, false, SurfaceFormat.Color);
                t.SetData(bmp.ToRgba(false));
                _logos.Add(t);
            }
        if (_logos.Count == 0) { OpenTitle(); return; }
        _logo = 0;
        StartLogo();
    }

    void StartLogo()
    {
        _logoTick = _logoFrame = 0;
        _fx.SetTone(100, 100, 100, 100);
        _fx.FadeIn(0, 10);
    }

    void UpdateLogos(bool click)
    {
        if (_fx.Dark)
        {
            if (++_logo < _logos.Count) { StartLogo(); return; }
            _logo = -1;
            foreach (var t in _logos) t.Dispose();
            _logos.Clear();
            OpenTitle();
            return;
        }
        if ((_logoTick > 50 || click) && _fx.Brightness == 100 && !_fx.FadingIn) _fx.FadeOut(10);
        if (_logoFrame++ % 2 == 0) _logoTick++;
    }

    void OpenTitle()
    {
        _camp = null;
        _session = null;
        _runner = null;
        _battle = null;
        _pendingMap = _pendingCamp = null;
        _gameOver = -1;
        _audio.PlayBgm("");
        _backlog.Clear();
        // FUN_00421cf4: the tone back to normal, the title faded in 10 % a tick; each choice fades it out the same way
        // first (FUN_00422340 → FUN_004222b8 / FUN_004222f8 / FUN_00422298)
        _fx.ResetForMap();
        _fx.SetTone(100, 100, 100, 100);
        _fx.FadeIn(0, 10);
        _title = new TitleScreen(_db, _folder, _tex, _ui)
        {
            NewGame = () => AfterFade(10, () => { _state.Reset(); LoadMap(Math.Max(0, Array.IndexOf(_mapIndices, _db.StartMap))); }),
            Loaded = save => AfterFade(10, () => ApplySave(save)),
            Options = OpenOptions,
            Quit = () => AfterFade(10, Exit),
            WarpPointer = WarpMouse,
        };
        Window.Title = GameTitle;
        _status = "";
        if (_opts.Screenshot is not null) Trace($"{_frame}: title");
    }

    /// <summary>The window's title: the game folder's name (the client plays any SRPG Maker 95 game).</summary>
    string GameTitle => new DirectoryInfo(_folder.Root).Name;

    /// <summary>Fades the screen out, <paramref name="step"/> % a tick, then does <paramref name="then"/>.</summary>
    void AfterFade(int step, Action then)
    {
        _fx.FadeOut(step);
        _afterFade = then;
    }

    /// <summary>
    /// The camp before map <paramref name="pos"/> (index into the map list). Coming from a map (FUN_0041d5e4) or a new
    /// game, the tone goes back to normal, the camp music starts and the game tick becomes 30 ms; the camp fades in
    /// 10 % a tick either way, and its 次のステージ / ロード / ゲーム終了 fade it out the same way first.
    /// </summary>
    void OpenCamp(int pos, bool fromMap)
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
        _fx.ResetForMap();
        _fx.SetTone(100, 100, 100, 100);
        _fx.FadeIn(0, 10);
        _camp = new Camp.CampScreen(_db, _folder, _tex, _ui, _state, map, idx)
        {
            StartNext = () => AfterFade(10, () => LoadMap(pos, fromCamp: true)),
            Loaded = save => AfterFade(10, () => ApplySave(save)),
            Quit = () => AfterFade(10, Exit),
            WarpPointer = WarpMouse,
        };
        if (fromMap)
        {
            _audio.PlayBgm(_camp.Bgm);
            GameClock.SetTick(30);
        }
        Window.Title = $"{GameTitle} — camp before SMAP_{idx + 1:000} {map.Name}";
        _status = "";
        if (_opts.Screenshot is not null) Trace($"{_frame}: camp before SMAP_{idx + 1:000} {map.Name} (army {_state.Roster.Count}, gold {_state.Gold})");
    }

    /// <summary>
    /// Loads a SAVEn.DAT (FUN_0040bfbc): the music stops, camp saves reopen their camp and battle saves their map with
    /// the saved army; the saved tone comes back, the screen stays black for 800 ms, then the saved music plays and the
    /// screen fades in (a camp 10 % a tick, a map from −20 by 20).
    /// </summary>
    void ApplySave(SaveFile save)
    {
        save.ApplyTo(_state);
        _audio.PlayBgm("");
        int pos = Math.Max(0, Array.IndexOf(_mapIndices, save.MapIndex));
        if (save.AtCamp) OpenCamp(pos, fromMap: false);
        else LoadMap(pos, save);
        var tone = save.Tone;
        _fx.SetTone(tone[0], tone[1], tone[2], tone[3]);
        _fx.FadeIn(save.AtCamp ? 0 : -20, save.AtCamp ? 10 : 20, delayMs: 800);
        _bgmAfterHold = save.Bgm;
    }

    /// <summary>The music a loaded save starts once its 800 ms of black are over.</summary>
    string? _bgmAfterHold;
    bool _eventStarted;

    void LoadMap(int pos, SaveFile? resume = null, bool fromCamp = false)
    {
        _camp = null;
        _title = null;
        _mapPos = pos;
        int idx = _mapIndices[_mapPos];
        var map = MapData.Load(_folder.MapPath(idx));
        _state.MapIndex = idx;
        if (_opts.Screenshot is not null) Trace($"{_frame}: map SMAP_{idx + 1:000} {map.Name} (army {_state.Roster.Count(kv => kv.Value.Sortie)}/{_state.Roster.Count})");
        if (resume is null) _state.NextMap = (idx + 1) % 100;   // FUN_0041d5e4: the following map unless a "Next Map" says otherwise
        // FUN_0041dc34: pictures go, brightness and tone stay; the screen fades in from −20 by 20 a tick (one black tick,
        // then 20 % steps) while the opening already runs; the tick becomes 30 ms and the mouse goes to (320, 256)
        _fx.ResetForMap();
        if (resume is null)
        {
            _fx.FadeIn(-20, 20);
            GameClock.SetTick(30);
            WarpMouse(new Point(320, 256));
        }
        _session = new MapSession(map, _db, _state, _tex, _folder.Chipset(map.Tileset));
        _runner = new EventRunner(_state, _session, _audio, _fx) { Shown = _backlog.Add };
        if (_opts.Screenshot is not null) _session.Field.Rng = new Random(1);   // reproducible scripted runs
        _battle = new BattleController(_session, _runner, _state, _db, _ui, _tex, _fx) { MusicLooped = () => _audio.BgmLooped, ShowThreat = _showThreat };
        // the exe stops the music when a map loads (FUN_0041d5e4 plays ""), except that a camp's music carries on into
        // the opening; the player-turn music comes with PLAYER TURN. A battle save's music comes after its hold (ApplySave).
        if (resume is null && !fromCamp) _audio.PlayBgm("");
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
        _battle.WarpPointer = WarpMouse;
        _battle.SlotExists = n => File.Exists(SaveFile.PathFor(_folder, n));
        _battle.SaveRequested = n =>
        {
            string path = SaveFile.PathFor(_folder, n);
            var tone = _fx.Tone;
            SaveFile.ForBattle(_state, _db, idx, _audio.CurrentBgm ?? map.Bgm[1], _session.Field.Phase, _folder.MapPath(idx),
                _session.Field.Units, _battle.FiredSlots, SaveFile.Load(path)?.Raw ?? _state.LastSave,
                new[] { (int)tone.X, (int)tone.Y, (int)tone.Z, (int)tone.W }).Write(path);
        };
        _battle.LoadRequested = n => { if (SaveFile.Load(SaveFile.PathFor(_folder, n)) is not { } f) return false; ApplySave(f); return true; };
        _battle.QuitRequested = Exit;
        if (resume is not null)
        {
            // a battle save: its units (with their statuses), phase and fired events instead of the opening
            _session.RestoreFrom(resume);
            _battle.Resume(resume.FiredSlots());
        }
        else
        {
            _battle.Start(_eventStarted ? null : _opts.Event);   // --event takes the place of the first map's opening only
            _eventStarted = true;
        }
        _seenOutcome = MapOutcome.None;
        _pendingMap = null;
        Window.Title = $"{GameTitle} — SMAP_{idx + 1:000} {map.Name}";
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
        Ui.Ui.Skin = _settings.Skin;
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
        // a scripted run (--screenshot) takes its input from its script alone, never from the real keyboard and mouse
        var k = _opts.Screenshot is null ? Keyboard.GetState() : default;
        var m = _opts.Screenshot is null ? Mouse.GetState() : default;
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
        if (_warpedTo is { } warped) { if (m.Position == warped) mouseMoved = false; _warpedTo = null; }

        // a character choice (FUN_0042523c, FUN_00413100): it opens with the pointer on its first option; the option under
        // the pointer is the one chosen, and a click off the options does nothing
        if (_runner?.Choice is { } ch)
        {
            var cr = ChoiceRect(ch);
            if (!ReferenceEquals(ch, _choiceShown))
            {
                _choiceShown = ch;
                _choiceActive = true;
                WarpMouse(ChoiceRow(cr, 0).Center);
            }
            int hover = Enumerable.Range(0, ch.Length).FirstOrDefault(i => ChoiceRow(cr, i).Contains(_mouseVirtual), -1);
            if (mouseMoved)
            {
                _choiceActive = hover >= 0;
                if (hover >= 0) _runner.HoverChoice(hover);
            }
            if (click && hover < 0) click = false;
        }
        else _choiceShown = null;
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
        // "choice+N" / "choiceK+N": the shot N frames after the next (the K-th) character choice comes up, left unanswered
        bool choiceUp = _runner?.Choice is not null;
        if (choiceUp && !_choiceWasUp) _choicesSeen++;
        _choiceWasUp = choiceUp;
        if (_opts.Screenshot is not null && choiceUp && _script.FirstOrDefault(s => s.StartsWith("choice")) is { } choiceTok
            && System.Text.RegularExpressions.Regex.Match(choiceTok, @"^choice(\d*)\+(\d+)$") is { Success: true } cm
            && (cm.Groups[1].Value == "" || int.Parse(cm.Groups[1].Value) == _choicesSeen))
        {
            _script = new Queue<string>(_script.Where(s => !ReferenceEquals(s, choiceTok)));
            _shotAt = _frame + int.Parse(cm.Groups[2].Value);
            _shotAdvance = 0;
        }
        if (_opts.Screenshot is not null && _shotAdvance != 0 && (_battle is { WaitingForClick: true } || _battle is { EventRunning: true } && _runner is { Message: not null } or { Choice: not null } or { WaitingForButton: true }))
        {
            if (_runner is { Choice: { } options } && _choose.TryDequeue(out var pick))
            {
                if (pick == "random") _choose.Enqueue(pick);   // every choice from here on at random (seeded, so a run repeats)
                _runner.HoverChoice(pick == "random" ? _chooseRng.Next(options.Length) : pick == "last" ? options.Length - 1
                    : int.TryParse(pick, out var n) ? Math.Clamp(n, 0, options.Length - 1) : 0);
            }
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

        // the enemy ranges key (modern aid): on a map, toggles the enemies' combined reach
        if (_session is not null && _opts.Screenshot is null && _controls.Pressed(GameAction.EnemyRanges)) ToggleThreat();

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

        // screens that are only pictures (logos, game over), a loaded save's hold, a choice's fade-out: nothing else runs
        if (_logo >= 0 || _gameOver >= 0 || _fx.Holding || _afterFade is not null)
        {
            _frame++;
            _audio.Update();
            bool press = battleInput.Click || battleInput.Confirm;
            if (_logo >= 0) UpdateLogos(press);
            else if (_gameOver >= 0) UpdateGameOver(press);
            _fx.Update();
            if (!_fx.Holding && _bgmAfterHold is { } bgm) { _bgmAfterHold = null; _audio.PlayBgm(bgm); }
            if (_afterFade is { } after && _fx.Dark) { _afterFade = null; after(); }
            EndUpdate(gameTime, k, m);
            return;
        }

        if (_title is not null)
        {
            _frame++;
            _audio.Update();
            _fx.Update();
            _title.Update(battleInput with { MouseMoved = battleInput.MouseMoved || _opts.Screenshot is null && mouseMoved }, _mouseVirtual);
            EndUpdate(gameTime, k, m);
            return;
        }
        if (_camp is not null)
        {
            _frame++;
            _audio.Update();
            _fx.Update();
            _camp.Update(battleInput with { MouseMoved = battleInput.MouseMoved || _opts.Screenshot is null && mouseMoved }, _mouseVirtual);
            EndUpdate(gameTime, k, m);
            return;
        }

        // a map's end has faded the screen out (BattleController): straight on to the next camp or map (FUN_0041d5e4)
        if (_pendingCamp is int pc)
        {
            _pendingCamp = null;
            OpenCamp(pc, fromMap: true);
            EndUpdate(gameTime, k, m);
            return;
        }
        if (_pendingMap is int pm)
        {
            _pendingMap = null;
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
        EdgeScroll(m, mouseHere);
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
        // "wipe+N": the shot N frames into the next screen wipe; "fx+N" into the next blocking effect of any kind
        var wipeTok = _script.FirstOrDefault(s => s.StartsWith("wipe+"));
        if (wipeTok is not null && _fx.Transition != 0)
        {
            _script = new Queue<string>(_script.Where(s => !ReferenceEquals(s, wipeTok)));
            _shotAt = _frame + int.Parse(wipeTok[5..]);
        }
        var fxTok = _script.FirstOrDefault(s => s.StartsWith("fx+"));
        if (fxTok is not null && _fx.Busy)
        {
            _script = new Queue<string>(_script.Where(s => !ReferenceEquals(s, fxTok)));
            _shotAt = _frame + int.Parse(fxTok[3..]);
        }
        // "panel+N": the shot N frames after the next message panel comes up; "click+N" after the battle next waits for a
        // click of any kind (a death message, a panel, COMPLETE!!) — it then waits, nothing clicks it
        foreach (var (prefix, waiting) in new[] { ("panel+", _battle?.PanelShowing == true), ("click+", _battle?.WaitingForClick == true) })
        {
            var waitTok = _script.FirstOrDefault(s => s.StartsWith(prefix));
            if (waitTok is null || !waiting) continue;
            _script = new Queue<string>(_script.Where(s => !ReferenceEquals(s, waitTok)));
            _shotAt = _frame + int.Parse(waitTok[prefix.Length..]);
            _shotAdvance = 0;
            bi = bi with { Confirm = false };
        }
        // "state:Name+N": the shot N frames after the battle map first enters that state (e.g. "state:Acting+2")
        if (_battle is not null && _script.FirstOrDefault(s => s.StartsWith("state:")) is { } stateTok
            && _battle.DebugState.StartsWith(stateTok[6..stateTok.LastIndexOf('+')] + " ", StringComparison.OrdinalIgnoreCase))
        {
            _script = new Queue<string>(_script.Where(s => !ReferenceEquals(s, stateTok)));
            _shotAt = _frame + int.Parse(stateTok[(stateTok.LastIndexOf('+') + 1)..]);
            _shotAdvance = 0;
        }
        bool idle = (_camp is not null || _title is not null || _battle is { Busy: false } && _runner is { Message: null, Choice: null })
            && !_fx.Holding && _afterFade is null;   // a loaded save's black hold and a choice's fade-out take no input
        _idleFrames = idle ? _idleFrames + 1 : 0;
        if (!idle || _script.Count == 0 || _script.Peek().StartsWith("telop") || _script.Peek().StartsWith("wipe") || _script.Peek().StartsWith("fx+")
            || _script.Peek().StartsWith("panel+") || _script.Peek().StartsWith("choice") || _script.Peek().StartsWith("click+") || _script.Peek().StartsWith("state:")
            || _script.Peek() == "logos" || ++_scriptWait < 10) return bi;
        _scriptWait = 0;
        _idleFrames = 0;
        var tok = _script.Dequeue();
        Trace($"{_frame}: {tok} in {(_title is not null ? "Title" : _camp is not null ? "Camp" : _battle!.DebugState.Split('\n')[0])}");
        return tok switch
        {
            "up" => bi with { Up = true },
            "down" => bi with { Down = true },
            "left" => bi with { Left = true },
            "right" => bi with { Right = true },
            "enter" => bi with { Confirm = true },
            "esc" => bi with { Cancel = true },
            "info" => bi with { Info = true },
            "backlog" => Do(() => _backlog.Scroll(1), bi),
            "threat" => Do(ToggleThreat, bi),
            "options" => Do(OpenOptions, bi),
            "endturn" => Do(() => _battle?.ScriptEndTurn(), bi),
            _ when tok.StartsWith("at:") && tok[3..].Split('.') is [var x, var y] =>
                bi with { MouseTile = new Point(int.Parse(x), int.Parse(y)), MouseMoved = true },
            _ => bi,
        };
    }

    static BattleInput Do(Action a, BattleInput bi) { a(); return bi; }

    /// <summary>A scripted run's trace: kept for the .txt beside the screenshot and appended to its .log as it happens.</summary>
    void Trace(string line)
    {
        _trace.Add(line);
        if (_opts.Screenshot is { } shot)
            try { File.AppendAllText(Path.ChangeExtension(shot, ".log"), line + "\n"); } catch (IOException) { }
    }

    void ToggleThreat()
    {
        _showThreat = !_showThreat;
        if (_battle is not null) _battle.ShowThreat = _showThreat;
        Sfx.Click();
    }

    int _shotAt = int.MaxValue;
    bool ShotReady => _frame > _opts.Frames || _frame >= _shotAt ||
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
            if (_session is null || _battle is null) return;   // a return to the title has closed the map
        }

        // camera: WASD (a modern addition) when nothing is animating; the mouse scrolls through the frame (EdgeScroll)
        if (_battle.Busy) return;
        const int speed = 8;
        bool keys = !_battle.MenuOpen && _runner?.Choice is null;
        if (keys && _controls.Down(GameAction.CameraLeft)) _session.Camera.X -= speed;
        if (keys && _controls.Down(GameAction.CameraRight)) _session.Camera.X += speed;
        if (keys && _controls.Down(GameAction.CameraUp)) _session.Camera.Y -= speed;
        if (keys && _controls.Down(GameAction.CameraDown)) _session.Camera.Y += speed;
        _session.ClampCamera();
    }

    /// <summary>
    /// FUN_0040dee0: in the player's phase, while the map waits for a unit, a target or an aim and the view isn't panning,
    /// the left button held with the pointer on the frame scrolls the view that way.
    /// </summary>
    void EdgeScroll(MouseState m, bool mouseHere)
    {
        if (_session is null || _battle is null) return;
        int dx = 0, dy = 0;
        if (_opts.Screenshot is null && mouseHere && m.LeftButton == ButtonState.Pressed && _battle.EdgeScrolls && !_session.Panning)
        {
            dx = _mouseVirtual.X < ViewX ? -1 : _mouseVirtual.X >= ViewX + ViewWidth ? 1 : 0;
            dy = _mouseVirtual.Y < ViewY ? -1 : _mouseVirtual.Y >= ViewY + ViewHeight ? 1 : 0;
        }
        _session.EdgeScroll(dx, dy);
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
                _status = "";
                OpenGameOver();
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
        // laid out like the original's questions (FUN_00418824): the question, a rule, はい / いいえ (WORD.DAT 45, 46) in the
        // middle, defaulting to はい
        const string question = "イベントをスキップしますか？";
        int w = ((Bin.Sjis.GetByteCount(question) + 1) / 2 + 2) * 12;
        _skipPrompt = new Camp.MenuList
        {
            Rect = new Rectangle(320 - w / 2, 200, w, 80), Header = new[] { question, "-" }, ItemX = w / 2 - 18, ItemWidth = 36,
        };
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

    // ---------------- game over (FUN_00421858) ----------------
    // GAMEOVER.BMP over the whole screen with the map's BGM slot 7, the tone back to normal, fading in 5 % a tick; a
    // click once it's bright fades it out the same way; then 2 s of black, the music stops, and the title 0.5 s later.

    int _gameOver = -1;   // 0 shown, 1 fading out, 2 black
    double _gameOverMs;

    void OpenGameOver()
    {
        if (_opts.Screenshot is not null) Trace($"{_frame}: game over on SMAP_{_state.MapIndex + 1:000}");
        _gameOver = 0;
        _fx.ResetForMap();
        _fx.SetTone(100, 100, 100, 100);
        _fx.FadeIn(0, 5);
        if (_session is not null) _audio.PlayBgm(_session.Map.Bgm[7]);
    }

    void UpdateGameOver(bool click)
    {
        switch (_gameOver)
        {
            case 0:
                if (click && !_fx.FadingIn) { _fx.FadeOut(5); _gameOver = 1; }
                break;
            case 1:
                if (_fx.Dark) { _gameOver = 2; _gameOverMs = 0; }
                break;
            default:
                _gameOverMs += GameClock.FrameMs;
                if (_gameOverMs >= 2000) _audio.PlayBgm("");
                if (_gameOverMs >= 2500) OpenTitle();
                break;
        }
    }

    string SafeName(int i) => i >= 0 && i < _db.MapNames.Length ? _db.MapNames[i] : "?";

    protected override void Draw(GameTime gameTime)
    {
        SetScale(_opts.Screenshot is not null ? Math.Clamp(_opts.Scale, 1, MaxScale) : ScaleFor(Destination()));
        // a wipe just started: keep the last frame, which the wipe will take away bit by bit
        if (_fx.SnapshotRequested)
        {
            GraphicsDevice.SetRenderTarget(_snapshot);
            _sb.Begin(samplerState: SamplerState.PointClamp);
            _sb.Draw(_screen, Vector2.Zero, Color.White);
            _sb.End();
            _fx.SnapshotTaken();
        }
        // 1. the game's own screen, as the exe draws it on its 8-bit surface (mouse pointer included)
        GraphicsDevice.SetRenderTarget(_screen);
        GraphicsDevice.Clear(Color.Black);
        bool overlay = _options is not null || _skipPrompt is not null || _backlog.Open;
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _view);
        if (_logo >= 0 && _logo < _logos.Count) _sb.Draw(_logos[_logo], Vector2.Zero, Color.White);
        else if (_gameOver >= 0)
        {
            if (_gameOver < 2 && _tex.Get("GAMEOVER.BMP", transparent: false) is { } go) _sb.Draw(go, Vector2.Zero, Color.White);
        }
        else if (_title is not null) _title.Draw(_sb, _frame);
        else if (_camp is not null) _camp.Draw(_sb, _frame);
        else DrawMap();
        // the pointer the exe draws: none on the logos and GAMEOVER.BMP, the arrow on the title and at camp, the map's own
        int pointer = _logo >= 0 || _gameOver >= 0 ? 2 : _title is not null || _camp is not null ? 0 : _battle?.PointerMode ?? 0;
        if (!overlay && pointer < 2 && _opts.Screenshot is null && IsActive) _ui.Cursor(_sb, _mouseVirtual, pointer == 1, _frame);
        _sb.End();

        // 2. the palette's brightness and tone, the shake, then the client's own windows (options, skip prompt, backlog)
        GraphicsDevice.SetRenderTarget(_final);
        GraphicsDevice.Clear(Color.Black);
        DrawGraded(new Rectangle(_fx.ShakeOffset, new Point(VirtualWidth, VirtualHeight)));   // the uncovered strip stays black (FUN_00423e70)
        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _view);
        if (_debug && _session is not null) DrawDebug();
        if (overlay)
        {
            _backlog.Draw(_sb);
            DrawSkipPrompt();
            _options?.Draw(_sb, _frame);
            if (_opts.Screenshot is null && IsActive) _ui.Cursor(_sb, _mouseVirtual, false, _frame);
        }
        _sb.End();

        Present(gameTime);
    }

    /// <summary>
    /// A battle map in the exe's order (its map loop around FUN_0041371c): the tiles with their overlays, the screen frame,
    /// the units (over the frame where they reach past the view), the picture (a full-screen one covers the frame, as in
    /// the original's cutscenes), then the battle UI and the event windows.
    /// </summary>
    void DrawMap()
    {
        var view = new Rectangle(ViewX, ViewY, ViewWidth, ViewHeight);
        if (_session is not null)
        {
            _session.DrawTiles(_sb, view);
            _battle?.DrawThreat(_sb, view);
            _battle?.DrawOverlays(_sb, view, _frame);
        }
        // the frame's red arrows point where the view can scroll, only in the player's phase while nothing else goes on,
        // blinking: 18 ticks on, 6 off (FUN_00413768)
        if (_session is { } s)
        {
            bool on = _battle?.ScrollArrows == true && _frame / 2 / 6 % 4 != 0;
            _ui.ScreenFrame(_sb, on && s.Camera.Y > 0, on && s.Camera.Y < s.PixelHeight - ViewHeight,
                on && s.Camera.X > 0, on && s.Camera.X < s.PixelWidth - ViewWidth);
            _session.DrawUnits(_sb, view, _frame, _battle?.FacingUnit);
        }
        DrawPicture();
        _battle?.DrawUi(_sb, view, _frame);
        DrawMessage();
        DrawChoice();
        DrawStatus();
        // an erase wiping its colour in: the new screen is all that colour (palette entry 0 / 255)
        if (_fx.Fill is { } fill) _ui.Fill(_sb, new Rectangle(0, 0, VirtualWidth, VirtualHeight), fill);
        DrawWipe();
    }

    /// <summary>
    /// The game's screen onto the final frame at <paramref name="at"/> (640 × 480 coordinates) through the palette's
    /// brightness and tone, as FUN_00405a18 rewrites palette entries 10–245: the vividness mixes each colour with its grey
    /// (3R + 6G + B) / 10, the tone scales each channel (above 100 % towards white), the brightness scales the result (above
    /// 100 towards white). Black — palette entry 0 and the other fixed entries, which no game picture uses for anything
    /// else — stays black. On the GPU (Shaders/Grade.fx), or pixel by pixel on the CPU where the shader can't run.
    /// </summary>
    void DrawGraded(Rectangle at)
    {
        var tone = _fx.FlashTone ?? _fx.Tone;
        int bright = Math.Clamp(_fx.Brightness, 0, 200), tr = (int)tone.X, tg = (int)tone.Y, tb = (int)tone.Z, v = (int)tone.W;
        Texture2D shown = _screen;
        Effect? effect = null;
        if (bright != 100 || tr != 100 || tg != 100 || tb != 100 || v < 100)
        {
            if (_gradeTableFor != (tr, tg, tb, bright))
            {
                FillGradeTable(_gradeTableData, tr, tg, tb, bright);
                _gradeTable.SetData(_gradeTableData);
                _gradeTableFor = (tr, tg, tb, bright);
            }
            if (_gradeFx is not null)
            {
                _gradeFx.Parameters["Table"].SetValue(_gradeTable);
                _gradeFx.Parameters["Vivid"].SetValue((float)v);
                effect = _gradeFx;
            }
            else shown = GradeOnCpu(v);
        }
        _sb.Begin(samplerState: SamplerState.PointClamp, effect: effect, transformMatrix: _view);
        _sb.Draw(shown, at, Color.White);
        _sb.End();
    }

    /// <summary>The grade's channel tables, one per channel of FUN_00405a18: the tone (≤ 100 % scales, above moves towards 255), then the brightness.</summary>
    static void FillGradeTable(Color[] table, int tr, int tg, int tb, int bright)
    {
        static int Channel(int c, int tone, int bright)
        {
            int x = tone <= 100 ? c * tone / 100 : (255 - c) * (tone - 100) / 100 + c;
            if (bright < 100) x = x * bright / 100;
            else if (bright > 100) x = (255 - x) * (bright - 100) / 100 + x;
            return Math.Clamp(x, 0, 255);
        }
        for (int c = 0; c < 256; c++) table[c] = new Color(Channel(c, tr, bright), Channel(c, tg, bright), Channel(c, tb, bright), 255);
    }

    /// <summary>The grade of each pixel (the shader's sums): black stays, the vividness greys, the tables map each channel.</summary>
    static void GradePixels(Span<Color> px, Color[] table, int v)
    {
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            int r = c.R, g = c.G, b = c.B;
            if ((r | g | b) == 0) continue;
            if (v < 100)
            {
                int grey = (r * 3 + g * 6 + b) / 10;
                if (v <= 0) r = g = b = grey;
                else { r = ((100 - v) * grey + r * v) / 100; g = ((100 - v) * grey + g * v) / 100; b = ((100 - v) * grey + b * v) / 100; }
            }
            px[i] = new Color(table[r].R, table[g].G, table[b].B, c.A);
        }
    }

    Texture2D GradeOnCpu(int v)
    {
        if (_gradePixels.Length != _screen.Width * _screen.Height) _gradePixels = new Color[_screen.Width * _screen.Height];
        _graded ??= new Texture2D(GraphicsDevice, _screen.Width, _screen.Height, false, SurfaceFormat.Color);
        _screen.GetData(_gradePixels);
        GradePixels(_gradePixels, _gradeTableData, v);
        _graded.SetData(_gradePixels);
        return _graded;
    }

    /// <summary>
    /// The grade shader (Shaders/Grade.fx, compiled for OpenGL and embedded), once a test frame of every kind of colour has
    /// come out of it exactly as <see cref="GradePixels"/> grades it; otherwise null, and the CPU grades.
    /// </summary>
    Effect? LoadGradeShader()
    {
        Effect? fx = null;
        try
        {
            using (var s = typeof(ErumaniGame).Assembly.GetManifestResourceStream("Grade.ogl.mgfx"))
            {
                if (s is null) return null;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                fx = new Effect(GraphicsDevice, ms.ToArray());
            }
            var rng = new Random(95);
            var input = new Color[256 * 4];
            for (int i = 0; i < input.Length; i++)
                input[i] = i < 8 ? new Color(i * 36, 255 - i * 36, i % 2 * 255, 255) : i < 16 ? new Color(0, 0, 0, (i - 8) * 32) : new Color(rng.Next(256), rng.Next(256), rng.Next(256), 255);
            var table = new Color[256];
            using var src = new Texture2D(GraphicsDevice, 256, 4);
            using var rt = new RenderTarget2D(GraphicsDevice, 256, 4);
            using var sb = new SpriteBatch(GraphicsDevice);
            src.SetData(input);
            foreach (var (r, g, b, bright, v) in new[] { (60, 100, 140, 80, 100), (100, 100, 100, 150, 40), (130, 70, 100, 100, 0), (100, 100, 100, 37, 73) })
            {
                FillGradeTable(table, r, g, b, bright);
                _gradeTable.SetData(table);
                fx.Parameters["Table"].SetValue(_gradeTable);
                fx.Parameters["Vivid"].SetValue((float)v);
                GraphicsDevice.SetRenderTarget(rt);
                GraphicsDevice.Clear(Color.Transparent);
                sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, effect: fx);
                sb.Draw(src, Vector2.Zero, Color.White);
                sb.End();
                GraphicsDevice.SetRenderTarget(null);
                var got = new Color[input.Length];
                rt.GetData(got);
                var want = (Color[])input.Clone();
                GradePixels(want, table, v);
                if (!got.AsSpan().SequenceEqual(want))
                {
                    _trace.Add("grade: the shader's colours differ from the CPU's; grading on the CPU");
                    fx.Dispose();
                    return null;
                }
            }
            return fx;
        }
        catch (Exception e)
        {
            GraphicsDevice.SetRenderTarget(null);
            _trace.Add($"grade: no shader ({e.GetType().Name}); grading on the CPU");
            fx?.Dispose();
            return null;
        }
    }

    /// <summary>Draws from now on at <paramref name="k"/> render pixels per 640 × 480 pixel, remaking the frames at that size.</summary>
    void SetScale(int k)
    {
        if (k == _scale) return;
        _scale = k;
        _view = Matrix.CreateScale(k);
        Ui.Ui.RenderScale = k;
        _screen?.Dispose();
        _snapshot?.Dispose();
        _final?.Dispose();
        _graded?.Dispose();
        _graded = null;
        RenderTarget2D Frame() => new(GraphicsDevice, VirtualWidth * k, VirtualHeight * k, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _screen = Frame();
        _snapshot = Frame();
        _final = Frame();
    }

    /// <summary>The render scale for a frame shown at <paramref name="dest"/>: the smallest whole scale at least as large, so the frame is only ever scaled down to the window, or not at all.</summary>
    static int ScaleFor(Rectangle dest) =>
        Math.Clamp((int)MathF.Ceiling(Math.Max(dest.Width / (float)VirtualWidth, dest.Height / (float)VirtualHeight) - 0.05f), 1, MaxScale);

    /// <summary>Where the frame goes in the window: scaled as the Scaling setting says, centred.</summary>
    Rectangle Destination()
    {
        var bb = GraphicsDevice.PresentationParameters.Bounds;
        float scale = Math.Min((float)bb.Width / VirtualWidth, (float)bb.Height / VirtualHeight);
        if (_settings.Scaling == Scaling.Integer && scale >= 1) scale = MathF.Floor(scale);
        int w = (int)(VirtualWidth * scale), h = (int)(VirtualHeight * scale);
        if (_settings.Scaling == Scaling.Stretch) (w, h) = (bb.Width, bb.Height);
        return new Rectangle((bb.Width - w) / 2, (bb.Height - h) / 2, w, h);
    }

    /// <summary>Screenshot (in screenshot mode, at the render scale) or the frame to the window: one to one when the window is a whole multiple of 640 × 480, else scaled down from the next whole multiple (smoothly with the Smooth filter).</summary>
    void Present(GameTime gameTime)
    {
        if (_opts.Screenshot is { } shot && ShotReady)
        {
            GraphicsDevice.SetRenderTarget(null);
            using (var f = File.Create(shot)) _final.SaveAsPng(f, _final.Width, _final.Height);
            File.WriteAllText(Path.ChangeExtension(shot, ".txt"), $"frame={_frame} script-left={_script.Count} {(_title is not null ? "Title" : _camp is not null ? $"Camp gold={_state.Gold} items={string.Join(",", _state.Items.Select(kv => $"{kv.Key}x{kv.Value}"))} roster={string.Join(",", _state.Roster.Select(kv => $"{kv.Key}:c{kv.Value.ClassId}/w{kv.Value.Weapon}/a{kv.Value.Armor}/x{kv.Value.Accessory}"))}" : _battle?.DebugState)}\n" + string.Join("\n", _trace) + "\n");
            Exit();
            return;
        }

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        _dest = Destination();
        bool exact = _dest.Width == _final.Width && _dest.Height == _final.Height;
        _sb.Begin(samplerState: _settings.Smooth && !exact ? SamplerState.LinearClamp : SamplerState.PointClamp);
        _sb.Draw(_final, _dest, Color.White);
        _sb.End();
        base.Draw(gameTime);
    }

    Rectangle _dest = new(0, 0, VirtualWidth, VirtualHeight);
    Point _mouseVirtual;

    /// <summary>
    /// Moves the mouse pointer to a 640×480 position, as FUN_00409d24 does, while the pointer is in the active window; the
    /// move isn't the player's, so it changes no hovered row or tile (the game set those already).
    /// </summary>
    void WarpMouse(Point v)
    {
        if (_opts.Screenshot is not null || !IsActive || _dest.Width == 0) return;
        var m = Mouse.GetState();
        var cb = Window.ClientBounds;
        if (m.X < 0 || m.Y < 0 || m.X >= cb.Width || m.Y >= cb.Height) return;
        var p = new Point(_dest.X + v.X * _dest.Width / VirtualWidth, _dest.Y + v.Y * _dest.Height / VirtualHeight);
        Mouse.SetPosition(p.X, p.Y);
        _warpedTo = p;
    }

    /// <summary>Where <see cref="WarpMouse"/> last put the pointer (window pixels), until the next frame has seen it.</summary>
    Point? _warpedTo;

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
            else _sb.Draw(_snapshot, r, new Rectangle(r.X * _scale, r.Y * _scale, r.Width * _scale, r.Height * _scale), Color.White);
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

    void DrawMessage()
    {
        var msg = _runner?.Message ?? (_runner?.Choice is not null ? _runner.LastMessage : null);
        if (msg is null) return;
        DrawMessage(_sb, _ui, _tex, msg.Window, msg.Face, msg.FaceRight, msg.FaceFlip, msg.Lines);
    }

    /// <summary>A message (FUN_004148a4) in its window — top, centre, bottom (FUN_0040d18c): the window 608 × 160 at (16, 16 + 144 × window), laid out by <see cref="Ui.Ui.Message"/>.</summary>
    public static void DrawMessage(SpriteBatch sb, Ui.Ui ui, Textures tex, int window, int faceId, bool faceRight, bool faceFlip, IReadOnlyList<string> lines) =>
        ui.Message(sb, faceId > 0 ? tex.Get($"FACE_{faceId:000}.BMP") : null, 16 + 144 * Math.Clamp(window, 0, 2), faceRight, faceFlip, lines);

    /// <summary>The bar shows on the choice's row: the pointer is on it (it starts on the first), or keys moved it.</summary>
    bool _choiceActive;
    string[]? _choiceShown;

    /// <summary>
    /// A character choice (FUN_0042523c): a green panel ((the longest option's bytes + 1) / 2 + 2) × 12 px wide and a 16-px
    /// row per option plus one high, at x 320 − units × 8 (208 for the 11話 camp menu's 168 px). Under a message window
    /// still up at the bottom (y 304) it sits on that window; with one at the top or centre the exe's sum (16 × the
    /// window's height + its y) overshoots and the clamp puts it at the frame's bottom; with none it is centred on y 240.
    /// Kept inside the frame (FUN_0041855c). The options in the exe's 12-px font at (+12, +8 + 16 × row), as every menu.
    /// </summary>
    Rectangle ChoiceRect(string[] opts)
    {
        int units = opts.Max(o => Bin.Sjis.GetByteCount(o) + 1) / 2 + 2, w = units * 12, h = (opts.Length + 1) * 16;
        int y = _runner?.LastMessage is { } m ? (m.Window == 2 ? 304 - h : 464 - h) : 240 - h / 2;
        return new Rectangle(Math.Clamp(320 - units * 8, 16, 624 - w), Math.Clamp(y, 16, 464 - h), w, h);
    }

    /// <summary>An option's bar (FUN_004144f4), the span the pointer chooses it in (FUN_00413100).</summary>
    static Rectangle ChoiceRow(Rectangle r, int i) => new(r.X + 6, r.Y + 8 + i * 16, r.Width - 12, 16);

    void DrawChoice()
    {
        if (_runner?.Choice is not { } opts) return;
        var r = ChoiceRect(opts);
        _ui.Panel(_sb, r);
        for (int i = 0; i < opts.Length; i++)
        {
            var c = _ui.MenuRow(_sb, ChoiceRow(r, i), _choiceActive && i == _runner.ChoiceIndex, true, _frame);
            _ui.Text12(_sb, opts[i], r.X + 12, r.Y + 8 + i * 16, c);
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
        _snapshot?.Dispose();
        _final?.Dispose();
        _graded?.Dispose();
        _gradeTable?.Dispose();
        _gradeFx?.Dispose();
        base.UnloadContent();
    }
}
