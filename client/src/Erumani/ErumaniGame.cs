using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Srpg95.Data;

namespace Erumani;

/// <summary>
/// Milestone-1 client: loads the original data, renders maps at the original 640x480
/// and scales them to the window. Controls:
///   arrows / WASD  scroll        PageUp / PageDown  previous / next map
///   F4 / Alt+Enter fullscreen    F5  integer scaling on/off
///   F2  show deleted objects     Tab (hold)  fast-forward      Esc  quit
/// </summary>
public sealed class ErumaniGame : Game
{
    public const int VirtualWidth = 640, VirtualHeight = 480;

    readonly GraphicsDeviceManager _gfx;
    readonly GameFolder _folder;
    readonly Options _opts;
    Database _db = null!;
    Textures _tex = null!;
    SpriteBatch _sb = null!;
    RenderTarget2D _screen = null!;
    Texture2D _pixel = null!;
    MapView? _view;
    int[] _mapIndices = Array.Empty<int>();
    int _mapPos;
    Point _camera;
    int _frame;
    bool _integerScale = true, _showDeleted;
    KeyboardState _prevKeys;

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
    }

    protected override void LoadContent()
    {
        _db = Database.Load(_folder);
        _tex = new Textures(GraphicsDevice, _folder);
        _sb = new SpriteBatch(GraphicsDevice);
        _screen = new RenderTarget2D(GraphicsDevice, VirtualWidth, VirtualHeight);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _mapIndices = Directory.GetFiles(Path.Combine(_folder.Root, "MAP"), "SMAP_*.DAT")
            .Select(p => int.Parse(Path.GetFileNameWithoutExtension(p)[5..]) - 1).Order().ToArray();
        int start = _opts.StartMap ?? _db.StartMap;
        _mapPos = Math.Max(0, Array.IndexOf(_mapIndices, start));
        LoadMap();
    }

    void LoadMap()
    {
        int idx = _mapIndices[_mapPos];
        var map = MapData.Load(_folder.MapPath(idx));
        _view = new MapView(map, _db, _tex);
        _camera = Point.Zero;
        int units = map.Objects.Count(o => o.InUse && o.Type != ObjectType.Other);
        int broken = map.Scripts.Values.Count(s => s.Error is not null);
        Window.Title = $"Erumani — SMAP_{idx + 1:000} {map.Name}  ({map.Width}x{map.Height}, {units} units, " +
                       $"{map.Scripts.Count} scripts{(broken > 0 ? $", {broken} damaged" : "")})";
    }

    protected override void Update(GameTime gameTime)
    {
        var k = Keyboard.GetState();
        bool Pressed(Keys key) => k.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);
        bool alt = k.IsKeyDown(Keys.LeftAlt) || k.IsKeyDown(Keys.RightAlt);

        if (Pressed(Keys.Escape)) Exit();
        if (Pressed(Keys.F4) || (alt && Pressed(Keys.Enter))) ToggleFullscreen();
        if (Pressed(Keys.F5)) _integerScale = !_integerScale;
        if (Pressed(Keys.F2)) _showDeleted = !_showDeleted;
        if (Pressed(Keys.PageDown)) { _mapPos = (_mapPos + 1) % _mapIndices.Length; LoadMap(); }
        if (Pressed(Keys.PageUp)) { _mapPos = (_mapPos + _mapIndices.Length - 1) % _mapIndices.Length; LoadMap(); }

        // Fast-forward: run the simulation several steps per frame while Tab is held.
        int steps = k.IsKeyDown(Keys.Tab) ? 4 : 1;
        for (int i = 0; i < steps; i++) Step(k);

        _prevKeys = k;
        base.Update(gameTime);
    }

    void Step(KeyboardState k)
    {
        _frame++;
        if (_view is null) return;
        const int speed = 8;
        if (k.IsKeyDown(Keys.Left) || k.IsKeyDown(Keys.A)) _camera.X -= speed;
        if (k.IsKeyDown(Keys.Right) || k.IsKeyDown(Keys.D)) _camera.X += speed;
        if (k.IsKeyDown(Keys.Up) || k.IsKeyDown(Keys.W)) _camera.Y -= speed;
        if (k.IsKeyDown(Keys.Down) || k.IsKeyDown(Keys.S)) _camera.Y += speed;
        _camera.X = Math.Clamp(_camera.X, 0, Math.Max(0, _view.PixelWidth - VirtualWidth));
        _camera.Y = Math.Clamp(_camera.Y, 0, Math.Max(0, _view.PixelHeight - VirtualHeight));
    }

    void ToggleFullscreen()
    {
        _gfx.HardwareModeSwitch = false;   // borderless fullscreen
        _gfx.ToggleFullScreen();
    }

    protected override void Draw(GameTime gameTime)
    {
        // 1. Render the game at its native 640x480.
        GraphicsDevice.SetRenderTarget(_screen);
        GraphicsDevice.Clear(Color.Black);
        _sb.Begin(samplerState: SamplerState.PointClamp);
        _view?.Draw(_sb, _camera, new Rectangle(0, 0, VirtualWidth, VirtualHeight), _frame, _showDeleted);
        _sb.End();

        if (_opts.Screenshot is { } shot)
        {
            GraphicsDevice.SetRenderTarget(null);
            using (var f = File.Create(shot)) _screen.SaveAsPng(f, VirtualWidth, VirtualHeight);
            Exit();
            return;
        }

        // 2. Scale to the window, keeping the 4:3 aspect (integer steps by default).
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var bb = GraphicsDevice.PresentationParameters.Bounds;
        float scale = Math.Min((float)bb.Width / VirtualWidth, (float)bb.Height / VirtualHeight);
        if (_integerScale && scale >= 1) scale = MathF.Floor(scale);
        int w = (int)(VirtualWidth * scale), h = (int)(VirtualHeight * scale);
        var dest = new Rectangle((bb.Width - w) / 2, (bb.Height - h) / 2, w, h);
        _sb.Begin(samplerState: SamplerState.PointClamp);
        _sb.Draw(_screen, dest, Color.White);
        _sb.End();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _tex?.Dispose();
        _screen?.Dispose();
        _pixel?.Dispose();
        base.UnloadContent();
    }
}
