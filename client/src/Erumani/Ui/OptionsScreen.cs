using Erumani.Config;
using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Erumani.Ui;

/// <summary>
/// The options menu (modern; from the title screen or F1 anywhere). It opens on a list of categories (Display, Audio,
/// Gameplay, Controls); each opens its own page. Left/right or a click changes a value; on the Controls page Confirm
/// on an action waits for a key (pressing a key already bound to it removes it; right click cancels).
/// </summary>
public sealed class OptionsScreen
{
    sealed record Row(string Label, Func<string> Value, Action<int>? Change = null, Action? Activate = null);

    enum Page { Main, Display, Audio, Gameplay, Controls }

    readonly Settings _s;
    readonly Ui _ui;
    readonly string _gameRoot;
    readonly Action _apply;
    readonly Camp.MenuList _list = new() { Rect = new Rectangle(96, 40, 448, 400), ColumnX = new[] { 436 }, SkipDisabled = true };
    List<Row> _rows = new();
    Page _page;
    int _mainIndex;
    GameAction? _waiting;

    public bool Done { get; private set; }

    public OptionsScreen(Settings settings, Ui ui, string gameRoot, Action apply)
    {
        (_s, _ui, _gameRoot, _apply) = (settings, ui, gameRoot, apply);
        Open(Page.Main);
    }

    static string OnOff(bool b) => b ? "On" : "Off";

    static T Cycle<T>(T[] values, T current, int dir)
    {
        int i = Array.IndexOf(values, current);
        return values[((i < 0 ? 0 : i) + dir + values.Length) % values.Length];
    }

    void Open(Page page)
    {
        if (_page == Page.Main && page != Page.Main) _mainIndex = _list.Index;
        _page = page;
        _rows = page switch
        {
            Page.Display => DisplayRows(),
            Page.Audio => AudioRows(),
            Page.Gameplay => GameplayRows(),
            Page.Controls => ControlRows(),
            _ => new List<Row>
            {
                new("Display", () => "", Activate: () => Open(Page.Display)),
                new("Audio", () => "", Activate: () => Open(Page.Audio)),
                new("Gameplay", () => "", Activate: () => Open(Page.Gameplay)),
                new("Controls", () => "", Activate: () => Open(Page.Controls)),
                new("", () => ""),
                new("Back", () => "", Activate: () => Done = true),
            },
        };
        if (page != Page.Main) _rows.AddRange(new[] { new Row("", () => ""), new Row("Back", () => "", Activate: () => Open(Page.Main)) });
        _list.Index = page == Page.Main ? _mainIndex : 0;
        Refresh();
    }

    List<Row> DisplayRows() => new()
    {
        new("Modern interface", () => OnOff(_s.ModernUi), _ => _s.ModernUi = !_s.ModernUi),
        new("Window size", () => $"{640 * _s.WindowScale}×{480 * _s.WindowScale}", d => _s.WindowScale = Math.Clamp(_s.WindowScale + d, 1, 4)),
        new("Fullscreen", () => OnOff(_s.Fullscreen), _ => _s.Fullscreen = !_s.Fullscreen),
        new("Scaling", () => _s.Scaling switch { Scaling.Integer => "Pixel-perfect (whole multiples)", Scaling.Fit => "Fit (keep 4:3)", _ => "Stretch" },
            d => _s.Scaling = Cycle(Enum.GetValues<Scaling>(), _s.Scaling, d)),
        new("Filter", () => _s.Smooth ? "Smooth" : "Sharp", _ => _s.Smooth = !_s.Smooth),
    };

    List<Row> AudioRows()
    {
        var fonts = new List<string> { Settings.BuiltIn };
        fonts.AddRange(Settings.FindSoundFonts(_gameRoot));
        string FontName() => _s.ResolveSoundFont(_gameRoot) is var f && f == Settings.BuiltIn ? f : Path.GetFileName(f);
        return new()
        {
            new("Music volume", () => $"{_s.MusicVolume}%", d => _s.MusicVolume = Math.Clamp(_s.MusicVolume + 10 * d, 0, 100)),
            new("Sound volume", () => $"{_s.SoundVolume}%", d => _s.SoundVolume = Math.Clamp(_s.SoundVolume + 10 * d, 0, 100)),
            new("MIDI playback", () => _s.Music == MusicEngine.SoundFont ? "SoundFont" : "Windows MIDI (original)",
                d => _s.Music = Cycle(Enum.GetValues<MusicEngine>(), _s.Music, d)),
            new("SoundFont", FontName, d =>
            {
                var next = Cycle(fonts.ToArray(), _s.ResolveSoundFont(_gameRoot), d);
                _s.SoundFont = next == Settings.BuiltIn ? null : Path.GetFileName(next);
            }),
            new("Confirm sound (thunk)", () => OnOff(_s.MessageSound), _ => _s.MessageSound = !_s.MessageSound),
        };
    }

    List<Row> GameplayRows() => new()
    {
        new("Battle animations", () => OnOff(_s.BattleScenes), _ => _s.BattleScenes = !_s.BattleScenes),
        new("Skip enemy turn", () => OnOff(_s.SkipEnemyTurn), _ => _s.SkipEnemyTurn = !_s.SkipEnemyTurn),
        new("Fast-forward key", () => _s.FastForwardMode == FastForwardMode.Hold ? "Hold" : "Toggle",
            d => _s.FastForwardMode = Cycle(Enum.GetValues<FastForwardMode>(), _s.FastForwardMode, d)),
        new("Fast-forward speed", () => $"×{_s.FastForwardSpeed}", d => _s.FastForwardSpeed = Cycle(new[] { 2, 3, 4, 6, 8 }, _s.FastForwardSpeed, d)),
        new("Fast-forward skips battles", () => OnOff(_s.FastForwardSkipsScenes), _ => _s.FastForwardSkipsScenes = !_s.FastForwardSkipsScenes),
        new("Fast-forward advances text", () => OnOff(_s.FastForwardAdvancesText), _ => _s.FastForwardAdvancesText = !_s.FastForwardAdvancesText),
    };

    static readonly Dictionary<GameAction, string> ActionNames = new()
    {
        [GameAction.Confirm] = "Confirm", [GameAction.Cancel] = "Cancel / back", [GameAction.Info] = "Unit status",
        [GameAction.Up] = "Cursor / menu up", [GameAction.Down] = "Cursor / menu down", [GameAction.Left] = "Cursor left", [GameAction.Right] = "Cursor right",
        [GameAction.CameraUp] = "Camera up (menu up)", [GameAction.CameraDown] = "Camera down (menu down)",
        [GameAction.CameraLeft] = "Camera left", [GameAction.CameraRight] = "Camera right",
        [GameAction.FastForward] = "Fast-forward", [GameAction.Backlog] = "Text backlog", [GameAction.Options] = "Options", [GameAction.Fullscreen] = "Fullscreen",
    };

    List<Row> ControlRows()
    {
        var rows = Enum.GetValues<GameAction>().Select(a => new Row(ActionNames[a],
            () => _waiting == a ? "press a key… (right click: cancel)" : string.Join(", ", _s.Keys.GetValueOrDefault(a, new()).Select(Controls.Name)),
            Activate: () => _waiting = a)).ToList();
        rows.Add(new Row("", () => ""));
        rows.Add(new Row("Reset to defaults", () => "", Activate: () => { _s.Keys = Settings.DefaultKeys(); Changed(); }));
        return rows;
    }

    void Refresh()
    {
        _list.Set(_rows.Select(r => r.Label).ToArray(), _rows.Select(r => r.Change is not null || r.Activate is not null).ToArray());
        _list.Columns = new[] { _rows.Select(r => r.Value()).ToArray() };
        int w = _page == Page.Main ? 200 : 448;
        _list.Rect = new Rectangle((640 - w) / 2, _page == Page.Main ? 150 : 32, w, 16 * _rows.Count + 34);
        if (_list.Index >= _rows.Count || !_list.Enabled[_list.Index]) _list.Index = Math.Max(0, Array.IndexOf(_list.Enabled, true));
    }

    void Changed()
    {
        _s.Save();
        _apply();
        Refresh();
    }

    public void Update(BattleInput i, Point mouse, Controls controls)
    {
        if (_waiting is { } a)
        {
            if (i.RightClick) { _waiting = null; Refresh(); return; }
            if (controls.NewKey() is { } key)
            {
                var list = _s.Keys.GetValueOrDefault(a) ?? new();
                if (!list.Remove(key)) list.Add(key);
                _s.Keys[a] = list;
                _waiting = null;
                Changed();
            }
            return;
        }
        if (i.Cancel || i.RightClick) { if (_page != Page.Main) Open(Page.Main); else Done = true; Sfx.Cancel(); return; }
        var row = _rows.ElementAtOrDefault(_list.Index);
        if ((i.Left || i.Right) && row?.Change is not null) { row.Change(i.Right ? 1 : -1); Sfx.Click(); Changed(); return; }
        int pick = _list.Navigate(i, mouse);
        if (pick < 0) return;
        var r = _rows[pick];
        if (r.Activate is not null) r.Activate();
        else if (r.Change is not null) { r.Change(1); Changed(); }
    }

    public void Draw(SpriteBatch sb, int frame)
    {
        _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * 0.6f);
        _list.Draw(sb, _ui, true, frame);
        var title = _page == Page.Main ? "Options" : _page.ToString();
        if (_ui.SmallText.Get(title) is { } t) sb.Draw(t, new Vector2(_list.Rect.X + 8, _list.Rect.Y - 16), Ui.PanelText);
    }
}
