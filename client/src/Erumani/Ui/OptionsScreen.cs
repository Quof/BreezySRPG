using Erumani.Config;
using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Erumani.Ui;

/// <summary>
/// The options menu (modern; from the title screen or F1 anywhere): display, audio, gameplay and controls.
/// Left/right or a click changes a value; Confirm on 「Controls…」 opens the key list, where Confirm on an action
/// waits for a key (pressing a key already bound to it removes it; right click cancels).
/// </summary>
public sealed class OptionsScreen
{
    sealed record Row(string Label, Func<string> Value, Action<int>? Change = null, Action? Activate = null);

    readonly Settings _s;
    readonly Ui _ui;
    readonly string _gameRoot;
    readonly Action _apply;
    readonly Camp.MenuList _list = new() { Rect = new Rectangle(96, 40, 448, 400), ColumnX = new[] { 436 }, SkipDisabled = true };
    List<Row> _rows = new();
    bool _keysPage;
    GameAction? _waiting;

    public bool Done { get; private set; }

    public OptionsScreen(Settings settings, Ui ui, string gameRoot, Action apply)
    {
        (_s, _ui, _gameRoot, _apply) = (settings, ui, gameRoot, apply);
        BuildMain();
    }

    static string OnOff(bool b) => b ? "On" : "Off";

    static T Cycle<T>(T[] values, T current, int dir)
    {
        int i = Array.IndexOf(values, current);
        return values[((i < 0 ? 0 : i) + dir + values.Length) % values.Length];
    }

    void BuildMain()
    {
        _keysPage = false;
        var fonts = Settings.FindSoundFonts(_gameRoot);
        string FontName() => _s.ResolveSoundFont(_gameRoot) is { } f ? Path.GetFileName(f) : "(none: put a .sf2 in soundfonts/)";
        _rows = new List<Row>
        {
            new("— Display —", () => ""),
            new("Window size", () => $"{640 * _s.WindowScale}×{480 * _s.WindowScale}", d => _s.WindowScale = Math.Clamp(_s.WindowScale + d, 1, 4)),
            new("Fullscreen", () => OnOff(_s.Fullscreen), _ => _s.Fullscreen = !_s.Fullscreen),
            new("Scaling", () => _s.Scaling switch { Scaling.Integer => "Pixel-perfect (whole multiples)", Scaling.Fit => "Fit (keep 4:3)", _ => "Stretch" },
                d => _s.Scaling = Cycle(Enum.GetValues<Scaling>(), _s.Scaling, d)),
            new("Filter", () => _s.Smooth ? "Smooth" : "Sharp", _ => _s.Smooth = !_s.Smooth),
            new("— Audio —", () => ""),
            new("Music volume", () => $"{_s.MusicVolume}%", d => _s.MusicVolume = Math.Clamp(_s.MusicVolume + 10 * d, 0, 100)),
            new("Sound volume", () => $"{_s.SoundVolume}%", d => _s.SoundVolume = Math.Clamp(_s.SoundVolume + 10 * d, 0, 100)),
            new("MIDI playback", () => _s.Music == MusicEngine.SoundFont ? "SoundFont" : "Windows MIDI (original)",
                d => _s.Music = Cycle(Enum.GetValues<MusicEngine>(), _s.Music, d)),
            new("SoundFont", FontName, d =>
            {
                if (fonts.Count == 0) return;
                var cur = _s.ResolveSoundFont(_gameRoot);
                _s.SoundFont = Path.GetFileName(Cycle(fonts.ToArray(), cur ?? fonts[0], d));
            }),
            new("Text advance sound", () => OnOff(_s.MessageSound), _ => _s.MessageSound = !_s.MessageSound),
            new("— Gameplay —", () => ""),
            new("Battle animations", () => OnOff(_s.BattleScenes), _ => _s.BattleScenes = !_s.BattleScenes),
            new("Fast-forward key", () => _s.FastForwardMode == FastForwardMode.Hold ? "Hold" : "Toggle",
                d => _s.FastForwardMode = Cycle(Enum.GetValues<FastForwardMode>(), _s.FastForwardMode, d)),
            new("Fast-forward speed", () => $"×{_s.FastForwardSpeed}", d => _s.FastForwardSpeed = Cycle(new[] { 2, 3, 4, 6, 8 }, _s.FastForwardSpeed, d)),
            new("Fast-forward skips battles", () => OnOff(_s.FastForwardSkipsScenes), _ => _s.FastForwardSkipsScenes = !_s.FastForwardSkipsScenes),
            new("Fast-forward advances text", () => OnOff(_s.FastForwardAdvancesText), _ => _s.FastForwardAdvancesText = !_s.FastForwardAdvancesText),
            new("", () => ""),
            new("Controls…", () => "", Activate: BuildKeys),
            new("Back", () => "", Activate: () => Done = true),
        };
        Refresh();
    }

    static readonly Dictionary<GameAction, string> ActionNames = new()
    {
        [GameAction.Confirm] = "Confirm", [GameAction.Cancel] = "Cancel / back", [GameAction.Info] = "Unit status",
        [GameAction.Up] = "Cursor / menu up", [GameAction.Down] = "Cursor / menu down", [GameAction.Left] = "Cursor left", [GameAction.Right] = "Cursor right",
        [GameAction.CameraUp] = "Camera up (menu up)", [GameAction.CameraDown] = "Camera down (menu down)",
        [GameAction.CameraLeft] = "Camera left", [GameAction.CameraRight] = "Camera right",
        [GameAction.FastForward] = "Fast-forward", [GameAction.Backlog] = "Text backlog", [GameAction.Options] = "Options", [GameAction.Fullscreen] = "Fullscreen",
    };

    void BuildKeys()
    {
        _keysPage = true;
        _rows = Enum.GetValues<GameAction>().Select(a => new Row(ActionNames[a],
            () => _waiting == a ? "press a key… (right click: cancel)" : string.Join(", ", _s.Keys.GetValueOrDefault(a, new()).Select(Controls.Name)),
            Activate: () => _waiting = a)).ToList();
        _rows.Add(new Row("", () => ""));
        _rows.Add(new Row("Reset to defaults", () => "", Activate: () => { _s.Keys = Settings.DefaultKeys(); Changed(); }));
        _rows.Add(new Row("Back", () => "", Activate: BuildMain));
        _list.Index = 0;
        Refresh();
    }

    void Refresh()
    {
        _list.Set(_rows.Select(r => r.Label).ToArray(), _rows.Select(r => r.Change is not null || r.Activate is not null).ToArray());
        _list.Columns = new[] { _rows.Select(r => r.Value()).ToArray() };
        _list.Rect = new Rectangle(96, 32, 448, 16 * _rows.Count + 34);
        if (!_list.Enabled[_list.Index]) _list.Index = Math.Max(0, Array.IndexOf(_list.Enabled, true));
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
        if (i.Cancel || i.RightClick) { if (_keysPage) BuildMain(); else Done = true; Sfx.Cancel(); return; }
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
        var title = _keysPage ? "Controls" : "Options";
        if (_ui.SmallText.Get(title) is { } t) sb.Draw(t, new Vector2(_list.Rect.X + 8, _list.Rect.Y - 16), Ui.PanelText);
    }
}
