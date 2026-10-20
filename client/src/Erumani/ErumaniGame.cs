using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Srpg95.Data;

namespace Erumani;

/// <summary>
/// The client. Loads the original data, renders at the original 640x480 and scales to the
/// window. Each map's OPENING event runs when the map loads.
/// Controls:
///   Enter / Space / Z / left click   confirm        Esc / X / right click  cancel
///   arrows / WASD  scroll (when no event is running), choose in menus
///   PageUp / PageDown  previous / next map       F3  debug overlay
///   F4 / Alt+Enter  fullscreen     F5  integer scaling     Tab (hold)  fast-forward
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
    readonly GameState _state = new();
    ScreenEffects _fx = new();
    MapSession? _session;
    EventRunner? _runner;
    BattleController? _battle;
    bool _gaveItems;
    Queue<string> _script = new();
    readonly List<string> _trace = new();
    int _scriptWait, _idleFrames;
    MapOutcome _seenOutcome;
    int[] _mapIndices = Array.Empty<int>();
    int _mapPos, _frame, _shotAdvance;
    bool _integerScale = true, _debug;
    string _status = "";
    KeyboardState _prevKeys;
    MouseState _prevMouse;

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
    }

    protected override void LoadContent()
    {
        _db = Database.Load(_folder);
        _tex = new Textures(GraphicsDevice, _folder);
        _ui = new Ui.Ui(GraphicsDevice, _tex);
        _audio = new Audio(_folder) { Muted = _opts.Screenshot is not null };
        _sb = new SpriteBatch(GraphicsDevice);
        _screen = new RenderTarget2D(GraphicsDevice, VirtualWidth, VirtualHeight);

        _mapIndices = Directory.GetFiles(Path.Combine(_folder.Root, "MAP"), "SMAP_*.DAT")
            .Select(p => int.Parse(Path.GetFileNameWithoutExtension(p)[5..]) - 1).Order().ToArray();
        int start = _opts.StartMap ?? _db.StartMap;
        LoadMap(Math.Max(0, Array.IndexOf(_mapIndices, start)));
    }

    void LoadMap(int pos)
    {
        _mapPos = pos;
        int idx = _mapIndices[_mapPos];
        var map = MapData.Load(_folder.MapPath(idx));
        _fx = new ScreenEffects();
        _session = new MapSession(map, _db, _state, _tex, _folder.Chipset(map.Tileset));
        _runner = new EventRunner(_state, _session, _audio, _fx);
        if (_opts.Screenshot is not null) _session.Field.Rng = new Random(1);   // reproducible scripted runs
        _battle = new BattleController(_session, _runner, _state, _db, _ui, _tex);
        _audio.PlayBgm(map.Bgm[1]);   // player-turn BGM; the opening usually sets its own
        // developer options (--level, --give): set up situations for testing
        if (_opts.Level is int lv)
            foreach (var u in _session.Field.Units.Where(u => u.Side == Srpg95.Rules.Side.Ally && u.Level < lv)) u.Level = lv;
        if (_opts.Give is { } give && !_gaveItems)
        {
            _gaveItems = true;
            foreach (var g in give.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (g.Split(':') is [var id, var n]) _state.AddItem(int.Parse(id), int.Parse(n));
        }
        _battle.PlayMapBgm = slot => _audio.PlayBgm(map.Bgm[slot]);
        _battle.PlaySound = _audio.PlaySe;
        _battle.Start(_opts.Event);
        _seenOutcome = MapOutcome.None;
        _pendingMap = null;
        Window.Title = $"Erumani — SMAP_{idx + 1:000} {map.Name}";
        _status = "";
    }

    protected override void Update(GameTime gameTime)
    {
        var k = Keyboard.GetState();
        var m = Mouse.GetState();
        _mouseVirtual = ToVirtual(m.Position);
        bool Pressed(Keys key) => k.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);
        bool alt = k.IsKeyDown(Keys.LeftAlt) || k.IsKeyDown(Keys.RightAlt);
        bool click = IsActive && m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool rclick = IsActive && m.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

        if (_runner?.Choice is { } ch && m.Position != _prevMouse.Position)
        {
            var cr = ChoiceRect(ch);
            _choiceActive = cr.Contains(_mouseVirtual);
            if (_choiceActive) _runner.HoverChoice((_mouseVirtual.Y - cr.Y - 8) / 16);
        }
        if (Pressed(Keys.F4) || (alt && Pressed(Keys.Enter))) { _gfx.HardwareModeSwitch = false; _gfx.ToggleFullScreen(); }
        if (Pressed(Keys.F5)) _integerScale = !_integerScale;
        if (Pressed(Keys.F3)) _debug = !_debug;
        if (Pressed(Keys.PageDown)) LoadMap((_mapPos + 1) % _mapIndices.Length);
        if (Pressed(Keys.PageUp)) LoadMap((_mapPos + _mapIndices.Length - 1) % _mapIndices.Length);

        var input = new EventInput(
            Confirm: (!alt && (Pressed(Keys.Enter) || Pressed(Keys.Space) || Pressed(Keys.Z))) || click,
            Cancel: Pressed(Keys.Escape) || Pressed(Keys.X) || rclick,
            Up: Pressed(Keys.Up),
            Down: Pressed(Keys.Down),
            FastForward: k.IsKeyDown(Keys.Tab));

        if (_opts.Screenshot is not null) input = new EventInput(false, false, false, false, false);   // scripted runs ignore real input
        if (_opts.Screenshot is not null && _shotAdvance != 0 && _battle is { EventRunning: true } && _runner is { Message: not null } or { Choice: not null })
        {
            input = input with { Confirm = true };
            _shotAdvance--;
        }

        if (input.Up || input.Down) _choiceActive = true;
        var battleInput = new BattleInput(
            Confirm: input.Confirm, Cancel: input.Cancel, Up: Pressed(Keys.Up), Down: Pressed(Keys.Down),
            Left: Pressed(Keys.Left), Right: Pressed(Keys.Right), MouseTile: MouseTile(),
            MouseMoved: m.Position != _prevMouse.Position, Click: click, RightClick: rclick, FastForward: input.FastForward);
        if (_battle is not null) _battle.MouseVirtual = _mouseVirtual;
        if (_opts.Screenshot is not null)   // ignore the real mouse while scripted
            battleInput = Scripted(new BattleInput(input.Confirm, false, false, false, false, false, null, false, false, false, false));

        if (_pendingMap is int pm && battleInput.Confirm)
        {
            _pendingMap = null;
            LoadMap(pm);
            input = input with { Confirm = false };
            battleInput = battleInput with { Confirm = false, Click = false };
        }

        // Fast-forward: several simulation steps per frame while Tab is held.
        int steps = input.FastForward ? 4 : 1;
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
        bool idle = _battle is { Busy: false } && _runner is { Message: null, Choice: null };
        _idleFrames = idle ? _idleFrames + 1 : 0;
        if (!idle || _script.Count == 0 || _script.Peek().StartsWith("telop") || ++_scriptWait < 10) return bi;
        _scriptWait = 0;
        _idleFrames = 0;
        var tok = _script.Dequeue();
        _trace.Add($"{_frame}: {tok} in {_battle!.DebugState.Split('\n')[0]}");
        return tok switch
        {
            "up" => bi with { Up = true },
            "down" => bi with { Down = true },
            "left" => bi with { Left = true },
            "right" => bi with { Right = true },
            "enter" => bi with { Confirm = true },
            "esc" => bi with { Cancel = true },
            _ when tok.StartsWith("at:") && tok[3..].Split('.') is [var x, var y] =>
                bi with { MouseTile = new Point(int.Parse(x), int.Parse(y)), MouseMoved = true },
            _ => bi,
        };
    }

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
        if (k.IsKeyDown(Keys.A) || (inView && mv.X < ViewX + 4)) _session.Camera.X -= speed;
        if (k.IsKeyDown(Keys.D) || (inView && mv.X >= ViewX + ViewWidth - 4)) _session.Camera.X += speed;
        if (k.IsKeyDown(Keys.W) || (inView && mv.Y < ViewY + 4)) _session.Camera.Y -= speed;
        if (k.IsKeyDown(Keys.S) || (inView && mv.Y >= ViewY + ViewHeight - 4)) _session.Camera.Y += speed;
        _session.ClampCamera();
    }

    void OnMapEnded()
    {
        int idx = _mapIndices[_mapPos];
        switch (_battle!.Outcome)
        {
            case MapOutcome.Won:
                int next = _battle.NextMap ?? idx + 1;   // provisional: no "Next Map" means the following map
                _status = $"MAP CLEAR → SMAP_{next + 1:000} {SafeName(next)}  [Enter]";
                if (Array.IndexOf(_mapIndices, next) is var p and >= 0) _pendingMap = p;
                break;
            case MapOutcome.Lost:
                _status = "Map lost" + (_battle.NextMap is int n ? $" → SMAP_{n + 1:000}  [Enter]" : "  [Enter: retry]");
                _pendingMap = _battle.NextMap is int n2 && Array.IndexOf(_mapIndices, n2) is var p2 and >= 0 ? p2 : _mapPos;
                break;
            case MapOutcome.GameOver:
                _audio.PlayBgm(_session!.Map.Bgm[7]);    // game over
                _status = "GAME OVER  [Enter: retry]";
                _pendingMap = _mapPos;
                break;
            case MapOutcome.ReturnToTitle:
                _status = "Return to title  [Enter: restart]";
                _pendingMap = Math.Max(0, Array.IndexOf(_mapIndices, _db.StartMap));
                break;
        }
    }

    int? _pendingMap;

    string SafeName(int i) => i >= 0 && i < _db.MapNames.Length ? _db.MapNames[i] : "?";

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(_screen);
        GraphicsDevice.Clear(Color.Black);
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
        if (_fx.FlashAlpha > 0) _ui.Fill(_sb, viewClip, _fx.FlashColor * _fx.FlashAlpha);
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
        if (_opts.Screenshot is null && IsActive)
            _ui.Cursor(_sb, _mouseVirtual, _runner?.Message is not null, _frame);
        _sb.End();

        if (_opts.Screenshot is { } shot && ShotReady)
        {
            GraphicsDevice.SetRenderTarget(null);
            using (var f = File.Create(shot)) _screen.SaveAsPng(f, VirtualWidth, VirtualHeight);
            File.WriteAllText(Path.ChangeExtension(shot, ".txt"), $"frame={_frame} script-left={_script.Count} {_battle?.DebugState}\n" + string.Join("\n", _trace) + "\n");
            Exit();
            return;
        }

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var bb = GraphicsDevice.PresentationParameters.Bounds;
        float scale = Math.Min((float)bb.Width / VirtualWidth, (float)bb.Height / VirtualHeight);
        if (_integerScale && scale >= 1) scale = MathF.Floor(scale);
        int w = (int)(VirtualWidth * scale), h = (int)(VirtualHeight * scale);
        _dest = new Rectangle((bb.Width - w) / 2, (bb.Height - h) / 2, w, h);
        _sb.Begin(samplerState: SamplerState.PointClamp);
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
        if (_fx.Picture is null || _fx.PictureAlpha <= 0) return;
        var t = _tex.Get(_fx.Picture, _fx.PictureTransparent);
        if (t is null) return;
        // Numeric-keypad positions on the full 640x480 screen: 7 8 9 / 4 5 6 / 1 2 3.
        int p = Math.Clamp(_fx.PicturePosition, 1, 9) - 1;
        int col = p % 3, row = 2 - p / 3;
        int x = col switch { 0 => 0, 1 => (VirtualWidth - t.Width) / 2, _ => VirtualWidth - t.Width };
        int y = row switch { 0 => 0, 1 => (VirtualHeight - t.Height) / 2, _ => VirtualHeight - t.Height };
        _sb.Draw(t, new Vector2(x, y), Color.White * _fx.PictureAlpha);
    }

    /// <summary>Colour tone: multiply for channels under 100 %, add for channels over (vividness not applied yet).</summary>
    void DrawTone(Rectangle area)
    {
        var tone = _fx.Tone;
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
            $"gold {_state.Gold}  party {string.Join(",", _state.Party)}  event {_runner?.Name} {(_runner?.Running == true ? "running" : "idle")}",
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
