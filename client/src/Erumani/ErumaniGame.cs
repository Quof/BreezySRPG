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
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60);
        _shotAdvance = opts.Advance;
    }

    protected override void LoadContent()
    {
        _db = Database.Load(_folder);
        _tex = new Textures(GraphicsDevice, _folder);
        _ui = new Ui.Ui(GraphicsDevice);
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
        _session = new MapSession(map, _db, _state, _tex);
        _runner = new EventRunner(_state, _session, _audio, _fx);
        if (map.Scripts.TryGetValue("OPENING", out var opening)) _runner.Start(opening);
        if (!_runner.Running) _audio.PlayBgm(map.Bgm[1]);   // player-turn BGM
        Window.Title = $"Erumani — SMAP_{idx + 1:000} {map.Name}";
        _status = "";
    }

    protected override void Update(GameTime gameTime)
    {
        var k = Keyboard.GetState();
        var m = Mouse.GetState();
        bool Pressed(Keys key) => k.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);
        bool alt = k.IsKeyDown(Keys.LeftAlt) || k.IsKeyDown(Keys.RightAlt);
        bool click = IsActive && m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool rclick = IsActive && m.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

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

        if (_opts.Screenshot is not null && _shotAdvance > 0 && _runner is { Message: not null } or { Choice: not null })
        {
            input = input with { Confirm = true };
            _shotAdvance--;
        }

        // Fast-forward: several simulation steps per frame while Tab is held.
        int steps = input.FastForward ? 4 : 1;
        for (int i = 0; i < steps; i++)
        {
            Step(k, i == 0 ? input : input with { Confirm = false, Up = false, Down = false, Cancel = false });
        }

        _prevKeys = k;
        _prevMouse = m;
        base.Update(gameTime);
    }

    void Step(KeyboardState k, EventInput input)
    {
        _frame++;
        _fx.Update();
        _audio.Update();
        if (_session is null || _runner is null) return;
        _session.Update();

        if (_runner.Running)
        {
            _runner.Update(input);
            if (!_runner.Running) OnEventFinished();
            return;
        }

        const int speed = 8;
        if (k.IsKeyDown(Keys.Left) || k.IsKeyDown(Keys.A)) _session.Camera.X -= speed;
        if (k.IsKeyDown(Keys.Right) || k.IsKeyDown(Keys.D)) _session.Camera.X += speed;
        if (k.IsKeyDown(Keys.Up) || k.IsKeyDown(Keys.W)) _session.Camera.Y -= speed;
        if (k.IsKeyDown(Keys.Down) || k.IsKeyDown(Keys.S)) _session.Camera.Y += speed;
        _session.ClampCamera();
    }

    void OnEventFinished()
    {
        var r = _runner!;
        _status = r.End switch
        {
            EventEnd.MapWon or EventEnd.MapLost =>
                $"Map complete ({(r.End == EventEnd.MapWon ? "win" : "lose")})" +
                (r.NextMap is int n ? $" → next map SMAP_{n + 1:000} {SafeName(n)}  [PageDown/Enter]" : ""),
            EventEnd.GameOver => "GAME OVER",
            EventEnd.ReturnToTitle => "Return to title",
            _ => "Opening finished — battle gameplay is the next milestone",
        };
        if (r.End is EventEnd.None or EventEnd.EndEvent) _audio.PlayBgm(_session!.Map.Bgm[1]);   // back to player-turn BGM
        if (r.End is EventEnd.MapWon or EventEnd.MapLost && r.NextMap is int next && Array.IndexOf(_mapIndices, next) is var p and >= 0)
            _pendingMap = p;
    }

    int? _pendingMap;

    string SafeName(int i) => i >= 0 && i < _db.MapNames.Length ? _db.MapNames[i] : "?";

    protected override void Draw(GameTime gameTime)
    {
        if (_pendingMap is int pm && (_prevKeys.IsKeyDown(Keys.Enter) || _opts.Screenshot is not null && false))
        {
            _pendingMap = null;
            LoadMap(pm);
        }

        GraphicsDevice.SetRenderTarget(_screen);
        GraphicsDevice.Clear(Color.Black);
        var view = new Rectangle(_fx.ShakeOffset.X, _fx.ShakeOffset.Y, VirtualWidth, VirtualHeight);
        _sb.Begin(samplerState: SamplerState.PointClamp);
        _session?.Draw(_sb, view, _frame);
        DrawPicture();
        _sb.End();
        DrawTone();
        _sb.Begin(samplerState: SamplerState.PointClamp);
        if (_fx.FlashAlpha > 0) _ui.Fill(_sb, new Rectangle(0, 0, VirtualWidth, VirtualHeight), _fx.FlashColor * _fx.FlashAlpha);
        if (_fx.Fade > 0) _ui.Fill(_sb, new Rectangle(0, 0, VirtualWidth, VirtualHeight), _fx.FadeColor * _fx.Fade);
        DrawMessage();
        DrawChoice();
        DrawStatus();
        if (_debug) DrawDebug();
        _sb.End();

        if (_opts.Screenshot is { } shot && _shotAdvance == 0 && _frame > 2)
        {
            GraphicsDevice.SetRenderTarget(null);
            using (var f = File.Create(shot)) _screen.SaveAsPng(f, VirtualWidth, VirtualHeight);
            Exit();
            return;
        }

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var bb = GraphicsDevice.PresentationParameters.Bounds;
        float scale = Math.Min((float)bb.Width / VirtualWidth, (float)bb.Height / VirtualHeight);
        if (_integerScale && scale >= 1) scale = MathF.Floor(scale);
        int w = (int)(VirtualWidth * scale), h = (int)(VirtualHeight * scale);
        _sb.Begin(samplerState: SamplerState.PointClamp);
        _sb.Draw(_screen, new Rectangle((bb.Width - w) / 2, (bb.Height - h) / 2, w, h), Color.White);
        _sb.End();
        base.Draw(gameTime);
    }

    void DrawPicture()
    {
        if (_fx.Picture is null || _fx.PictureAlpha <= 0) return;
        var t = _tex.Get(_fx.Picture, _fx.PictureTransparent);
        if (t is null) return;
        // Numeric-keypad positions: 7 8 9 / 4 5 6 / 1 2 3.
        int p = Math.Clamp(_fx.PicturePosition, 1, 9) - 1;
        int col = p % 3, row = 2 - p / 3;
        int x = col switch { 0 => 0, 1 => (VirtualWidth - t.Width) / 2, _ => VirtualWidth - t.Width };
        int y = row switch { 0 => 0, 1 => (VirtualHeight - t.Height) / 2, _ => VirtualHeight - t.Height };
        _sb.Draw(t, new Vector2(x, y), Color.White * _fx.PictureAlpha);
    }

    /// <summary>Colour tone: multiply for channels under 100 %, add for channels over (vividness not applied yet).</summary>
    void DrawTone()
    {
        var tone = _fx.Tone;
        if (tone.X == 100 && tone.Y == 100 && tone.Z == 100) return;
        var full = new Rectangle(0, 0, VirtualWidth, VirtualHeight);
        static int Mul(float v) => (int)(Math.Clamp(v, 0, 100) * 2.55f);
        static int Add(float v) => (int)(Math.Clamp(v - 100, 0, 100) * 2.55f);
        _sb.Begin(blendState: Multiply);
        _ui.Fill(_sb, full, new Color(Mul(tone.X), Mul(tone.Y), Mul(tone.Z)));
        _sb.End();
        _sb.Begin(blendState: BlendState.Additive);
        _ui.Fill(_sb, full, new Color(Add(tone.X), Add(tone.Y), Add(tone.Z)));
        _sb.End();
    }

    // Provisional layout (T17 asks for screenshots of the original): 5 lines x 20 px,
    // 128x128 face inside the window, windows at top / centre / bottom.
    void DrawMessage()
    {
        if (_runner?.Message is not { } msg) return;
        const int h = 144;
        int y = msg.Window switch { 0 => 8, 1 => (VirtualHeight - h) / 2, _ => VirtualHeight - h - 8 };
        var r = new Rectangle(8, y, VirtualWidth - 16, h);
        _ui.Window(_sb, r);
        int textX = r.X + 16;
        if (msg.Face > 0 && _tex.Get($"FACE_{msg.Face:000}.BMP") is { } face)
        {
            int fx = msg.FaceRight ? r.Right - 8 - 128 : r.X + 8;
            _sb.Draw(face, new Rectangle(fx, r.Y + 8, 128, 128), null, Color.White, 0, Vector2.Zero,
                msg.FaceFlip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
            if (!msg.FaceRight) textX = r.X + 8 + 128 + 16;
        }
        for (int i = 0; i < msg.Lines.Count; i++)
            _ui.Text(_sb, msg.Lines[i], new Vector2(textX, r.Y + 14 + i * Ui.Ui.LineHeight), Color.White);
        if (_frame / 20 % 2 == 0)
            _ui.Text(_sb, "▼", new Vector2(r.Right - 28, r.Bottom - 26), Color.White);
    }

    void DrawChoice()
    {
        if (_runner?.Choice is not { } opts) return;
        int w = (int)opts.Max(o => _ui.Measure(o).X) + 56, h = opts.Length * Ui.Ui.LineHeight + 24;
        var r = new Rectangle((VirtualWidth - w) / 2, (VirtualHeight - h) / 2, w, h);
        _ui.Window(_sb, r);
        for (int i = 0; i < opts.Length; i++)
        {
            var pos = new Vector2(r.X + 32, r.Y + 12 + i * Ui.Ui.LineHeight);
            if (i == _runner.ChoiceIndex)
            {
                _ui.Fill(_sb, new Rectangle(r.X + 8, (int)pos.Y - 1, w - 16, Ui.Ui.LineHeight), new Color(120, 140, 255) * 0.4f);
                _ui.Text(_sb, "▶", pos - new Vector2(20, 0), Color.White);
            }
            _ui.Text(_sb, opts[i], pos, Color.White);
        }
    }

    void DrawStatus()
    {
        if (_status.Length == 0) return;
        var r = new Rectangle(8, VirtualHeight - 36, VirtualWidth - 16, 28);
        _ui.Window(_sb, r, 0.9f);
        _ui.Text(_sb, _status, new Vector2(r.X + 10, r.Y + 6), new Color(255, 240, 160));
    }

    void DrawDebug()
    {
        var lines = new List<string>
        {
            $"SMAP_{_mapIndices[_mapPos] + 1:000} {_session?.Map.Name}  BGM {_audio.CurrentBgm ?? "-"}  font {_ui.FontName}",
            $"gold {_state.Gold}  party {string.Join(",", _state.Party)}  event {_runner?.Name} {(_runner?.Running == true ? "running" : "idle")}",
        };
        lines.AddRange(_runner?.Log.TakeLast(8) ?? Enumerable.Empty<string>());
        _ui.Fill(_sb, new Rectangle(0, 0, VirtualWidth, 8 + lines.Count * 18), Color.Black * 0.7f);
        for (int i = 0; i < lines.Count; i++)
            _ui.Font.DrawText(_sb, lines[i], new Vector2(6, 4 + i * 18), Color.LightGreen);
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
