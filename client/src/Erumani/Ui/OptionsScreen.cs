using Erumani.Config;
using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Erumani.Ui.Lang;

namespace Erumani.Ui;

/// <summary>
/// The options menu (modern; from the title screen, F1 anywhere, or a right click during an event). It opens on a list
/// of categories (Display, Audio, Gameplay, Controls); each opens its own page. A toggle changes on a click or left /
/// right; a setting with several values steps through them with left / right and opens a drop-down list of them on a
/// click or Confirm; on the Controls page Confirm on an action waits for a key (pressing a key already bound to it
/// removes it; right click cancels). Every word is in the Language setting's language.
/// </summary>
public sealed class OptionsScreen
{
    /// <summary>
    /// A row: its label and value. <see cref="Change"/> steps a toggle or a choice (−1 / +1); a choice also lists its values
    /// (<see cref="Choices"/>, the current one <see cref="Chosen"/>) for the drop-down, which sets one with <see cref="Pick"/>;
    /// <see cref="Activate"/> opens a page or runs a command.
    /// </summary>
    sealed record Row(string Label, Func<string> Value, Action<int>? Change = null, Action? Activate = null,
        Func<string[]>? Choices = null, Func<int>? Chosen = null, Action<int>? Pick = null);

    static Row Choice(string label, Func<string[]> names, Func<int> chosen, Action<int> pick) =>
        new(label, () => names().ElementAtOrDefault(chosen()) ?? "",
            d => { int n = names().Length; pick(((Math.Max(0, chosen()) + d) % n + n) % n); },
            Choices: names, Chosen: chosen, Pick: pick);

    static Row Toggle(string label, Func<bool> get, Action<bool> set) => new(label, () => OnOff(get()), _ => set(!get()));

    enum Page { Main, Display, Audio, Gameplay, Controls }

    readonly Settings _s;
    readonly Ui _ui;
    readonly string _gameRoot;
    readonly Action _apply;
    readonly Camp.MenuList _list = new() { Rect = new Rectangle(96, 40, 448, 400), ColumnX = new[] { 428 }, SkipDisabled = true };
    List<Row> _rows = new();
    Page _page;
    int _mainIndex;
    GameAction? _waiting;
    /// <summary>The open drop-down list and the row it belongs to.</summary>
    Camp.MenuList? _drop;
    int _dropRow;

    public bool Done { get; private set; }

    public OptionsScreen(Settings settings, Ui ui, string gameRoot, Action apply)
    {
        (_s, _ui, _gameRoot, _apply) = (settings, ui, gameRoot, apply);
        Open(Page.Main);
    }

    void Open(Page page, int index = 0)
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
                new(T("Display", "画面"), () => "", Activate: () => Open(Page.Display)),
                new(T("Audio", "サウンド"), () => "", Activate: () => Open(Page.Audio)),
                new(T("Gameplay", "ゲーム"), () => "", Activate: () => Open(Page.Gameplay)),
                new(T("Controls", "操作"), () => "", Activate: () => Open(Page.Controls)),
                new("", () => ""),
                new(T("Back", "戻る"), () => "", Activate: () => Done = true),
            },
        };
        if (page != Page.Main) _rows.AddRange(new[] { new Row("", () => ""), new Row(T("Back", "戻る"), () => "", Activate: () => Open(Page.Main)) });
        _list.Index = page == Page.Main && index == 0 ? _mainIndex : index;
        Refresh();
    }

    string PageTitle => _page switch
    {
        Page.Display => T("Display", "画面"), Page.Audio => T("Audio", "サウンド"), Page.Gameplay => T("Gameplay", "ゲーム"),
        Page.Controls => T("Controls", "操作"), _ => T("Options", "環境設定"),
    };

    /// <summary>Window sizes to offer: 4:3 sizes the screen can hold (640 × 480 always), the whole multiples marked.</summary>
    static readonly (int W, int H)[] Sizes =
    {
        (640, 480), (800, 600), (960, 720), (1024, 768), (1152, 864), (1280, 960), (1400, 1050), (1440, 1080),
        (1600, 1200), (1920, 1440), (2048, 1536), (2560, 1920),
    };

    static List<(int W, int H)> FittingSizes()
    {
        var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        return Sizes.Where(s => s == (640, 480) || (s.W <= mode.Width && s.H <= mode.Height - 48)).ToList();
    }

    List<Row> DisplayRows()
    {
        var sizes = FittingSizes();
        string[] SizeNames() => sizes.Select(s => s.W % 640 == 0 && s.W / 640 * 480 == s.H ? $"{s.W} × {s.H}  (×{s.W / 640})" : $"{s.W} × {s.H}")
            .Append(T("Fullscreen", "全画面")).ToArray();
        int SizeChosen()
        {
            if (_s.Fullscreen) return sizes.Count;
            var now = _s.WindowSize;
            int i = sizes.IndexOf(now);
            return i >= 0 ? i : sizes.FindLastIndex(s => s.W <= now.Width);
        }
        void SizePick(int i)
        {
            if (i >= sizes.Count) { _s.Fullscreen = true; return; }
            _s.Fullscreen = false;
            (_s.WindowWidth, _s.WindowHeight) = sizes[i];
        }
        return new()
        {
            Choice(T("Interface style", "インターフェース"), () => new[] { T("Original", "オリジナル"), T("Modern", "モダン"), T("Gilded", "ゴールド") },
                () => (int)_s.Skin, i => _s.Skin = (UiSkin)i),
            Choice(T("Font (Modern / Gilded)", "フォント（モダン・ゴールド）"),
                () => new[] { "Noto Sans JP", "Noto Serif JP", T("BIZ UDPGothic", "BIZ UDPゴシック"), T("Yu Gothic UI", "游ゴシック UI") }
                    .Select((name, i) => GdiText.Available((UiFont)i) ? name : name + T(" (not on this PC)", "（このPCにありません）")).ToArray(),
                () => (int)_s.Font, i => _s.Font = (UiFont)i),
            Choice(T("Language", "言語"), () => new[] { "English", "日本語" }, () => (int)_s.Language, i => _s.Language = (UiLanguage)i),
            Choice(T("Window size", "画面解像度"), SizeNames, SizeChosen, SizePick),
            Choice(T("Scaling", "拡大方式"),
                () => new[] { T("Pixel-perfect (whole multiples)", "ドット等倍（整数倍）"), T("Fit (keep 4:3)", "画面に合わせる（4:3）"), T("Stretch", "引き伸ばし") },
                () => (int)_s.Scaling, i => _s.Scaling = (Scaling)i),
            Choice(T("Filter", "フィルター"), () => new[] { T("Sharp", "シャープ"), T("Smooth", "なめらか") }, () => _s.Smooth ? 1 : 0, i => _s.Smooth = i == 1),
            Choice(T("Save / load screen", "セーブ・ロード画面"), () => new[] { T("Extended (100 files)", "拡張（100ファイル）"), T("Original (8 files)", "オリジナル（8ファイル）") },
                () => (int)_s.SaveScreen, i => _s.SaveScreen = (SaveScreen)i),
        };
    }

    List<Row> AudioRows()
    {
        var fonts = new List<string> { Settings.BuiltIn };
        fonts.AddRange(Settings.FindSoundFonts(_gameRoot));
        string FontName(string f) => f == Settings.BuiltIn ? T("GeneralUser GS (built-in)", "GeneralUser GS（内蔵）") : Path.GetFileName(f);
        return new()
        {
            new(T("BGM volume", "BGMの音量"), () => $"{_s.MusicVolume}%", d => _s.MusicVolume = Math.Clamp(_s.MusicVolume + 10 * d, 0, 100)),
            new(T("SE volume", "SEの音量"), () => $"{_s.SoundVolume}%", d => _s.SoundVolume = Math.Clamp(_s.SoundVolume + 10 * d, 0, 100)),
            Choice(T("MIDI playback", "MIDI再生"), () => new[] { T("Windows MIDI (original)", "Windows MIDI（オリジナル）"), T("SoundFont", "サウンドフォント") },
                () => (int)_s.Music, i => _s.Music = (MusicEngine)i),
            Choice(T("SoundFont", "サウンドフォント"), () => fonts.Select(FontName).ToArray(),
                () => Math.Max(0, fonts.IndexOf(_s.ResolveSoundFont(_gameRoot))),
                i => _s.SoundFont = fonts[i] == Settings.BuiltIn ? null : Path.GetFileName(fonts[i])),
            Toggle(T("Confirm sound (thunk)", "決定音"), () => _s.MessageSound, v => _s.MessageSound = v),
        };
    }

    List<Row> GameplayRows() => new()
    {
        Toggle(T("Battle animations", "戦闘アニメーション"), () => _s.BattleScenes, v => _s.BattleScenes = v),
        Toggle(T("Skip enemy turn", "敵ターンスキップ"), () => _s.SkipEnemyTurn, v => _s.SkipEnemyTurn = v),
        Choice(T("Fast-forward key", "早送りキー"), () => new[] { T("Hold", "押している間"), T("Toggle", "切り替え") },
            () => (int)_s.FastForwardMode, i => _s.FastForwardMode = (FastForwardMode)i),
        Toggle(T("Hold left click to fast-forward", "左クリック長押しで早送り"), () => _s.FastForwardMouse, v => _s.FastForwardMouse = v),
        Choice(T("Fast-forward speed", "早送り速度"), () => Speeds.Select(n => $"×{n}").ToArray(),
            () => Math.Max(0, Array.IndexOf(Speeds, _s.FastForwardSpeed)), i => _s.FastForwardSpeed = Speeds[i]),
        Toggle(T("Fast-forward skips battles", "早送り中は戦闘をスキップ"), () => _s.FastForwardSkipsScenes, v => _s.FastForwardSkipsScenes = v),
        Toggle(T("Fast-forward advances text", "早送りでメッセージを送る"), () => _s.FastForwardAdvancesText, v => _s.FastForwardAdvancesText = v),
    };

    static readonly int[] Speeds = { 2, 3, 4, 6, 8 };

    static string ActionName(GameAction a) => a switch
    {
        GameAction.Confirm => T("Confirm", "決定"), GameAction.Cancel => T("Cancel / back", "キャンセル／戻る"),
        GameAction.Info => T("Unit status", "ユニット情報"),
        GameAction.Up => T("Cursor / menu up", "カーソル・メニュー上"), GameAction.Down => T("Cursor / menu down", "カーソル・メニュー下"),
        GameAction.Left => T("Cursor left", "カーソル左"), GameAction.Right => T("Cursor right", "カーソル右"),
        GameAction.CameraUp => T("Camera up (menu up)", "画面スクロール上（メニュー上）"),
        GameAction.CameraDown => T("Camera down (menu down)", "画面スクロール下（メニュー下）"),
        GameAction.CameraLeft => T("Camera left", "画面スクロール左"), GameAction.CameraRight => T("Camera right", "画面スクロール右"),
        GameAction.FastForward => T("Fast-forward", "早送り"), GameAction.Backlog => T("Text backlog", "メッセージ履歴"),
        GameAction.Options => T("Options", "環境設定"), GameAction.Fullscreen => T("Fullscreen", "全画面切り替え"),
        GameAction.SkipCutscene => T("Skip cutscene", "イベントスキップ"),
        GameAction.EnemyRanges => T("Enemy ranges (all at once)", "敵の行動範囲表示"),
        _ => a.ToString(),
    };

    List<Row> ControlRows()
    {
        var rows = Enum.GetValues<GameAction>().Select(a => new Row(ActionName(a),
            () => _waiting == a ? T("press a key… (right click: cancel)", "キーを押してください…（右クリックで取消）")
                : string.Join(", ", _s.Keys.GetValueOrDefault(a, new()).Select(Controls.Name)),
            Activate: () => _waiting = a)).ToList();
        rows.Add(new Row("", () => ""));
        rows.Add(new Row(T("Reset to defaults", "初期設定に戻す"), () => "", Activate: () => { _s.Keys = Settings.DefaultKeys(); Changed(); }));
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

    /// <summary>A setting changed: saved, applied, and the page made again (its words follow the language).</summary>
    void Changed()
    {
        _s.Save();
        _apply();
        Open(_page, _list.Index);
    }

    /// <summary>Opens row <paramref name="row"/>'s drop-down below it, its right edge at the values' (kept on the screen).</summary>
    void OpenDrop(int row)
    {
        var names = _rows[row].Choices!();
        int w = Math.Max(96, names.Max(_ui.Measure12) + 36), h = 16 * names.Length + 16;
        var bar = _list.Row(row);
        int x = Math.Clamp(_list.Rect.X + _list.ColumnX[0] + 18 - w, 8, 632 - w);
        int y = bar.Bottom + 2 + h <= 472 ? bar.Bottom + 2 : Math.Max(8, bar.Y - 2 - h);
        _drop = new Camp.MenuList { Rect = new Rectangle(x, y, w, h), ItemX = 18, ItemWidth = w - 36 };
        _drop.Set(names);
        _drop.Index = Math.Clamp(_rows[row].Chosen!(), 0, names.Length - 1);
        _dropRow = row;
        Sfx.Click();
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
        if (_drop is { } drop)
        {
            if (i.Cancel || i.RightClick || (i.Click && !drop.Rect.Contains(mouse))) { _drop = null; Sfx.Cancel(); return; }
            int chosen = drop.Navigate(i with { Left = false, Right = false }, mouse);
            if (chosen < 0) return;
            _drop = null;
            _rows[_dropRow].Pick!(chosen);
            Changed();
            return;
        }
        if (i.Cancel || i.RightClick) { if (_page != Page.Main) Open(Page.Main); else Done = true; Sfx.Cancel(); return; }
        var row = _rows.ElementAtOrDefault(_list.Index);
        if ((i.Left || i.Right) && row?.Change is not null) { row.Change(i.Right ? 1 : -1); Sfx.Click(); Changed(); return; }
        int pick = _list.Navigate(i, mouse);
        if (pick < 0) return;
        var r = _rows[pick];
        if (r.Choices is not null) OpenDrop(pick);
        else if (r.Activate is not null) r.Activate();
        else if (r.Change is not null) { r.Change(1); Changed(); }
    }

    public void Draw(SpriteBatch sb, int frame)
    {
        _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black * 0.6f);
        _list.Draw(sb, _ui, _drop is null, frame);
        // a small ▼ after the values that open a drop-down
        for (int k = 0; k < _rows.Count; k++)
        {
            if (_rows[k].Choices is null) continue;
            var bar = _list.Row(k);
            int x = _list.Rect.X + _list.ColumnX[0] + 6, y = bar.Y + 6;
            var c = Ui.Gilded ? Ui.Gold : Ui.Modern ? Ui.Accent : Ui.PanelText;
            for (int t = 0; t < 3; t++) _ui.Fill(sb, new Rectangle(x + t, y + t, 7 - 2 * t, 1), c);
        }
        _ui.SmallText.Get(PageTitle)?.Draw(sb, new Vector2(_list.Rect.X + 8, _list.Rect.Y - 16), Ui.Gilded ? Ui.GoldLight : Ui.PanelText);
        if (_drop is { } drop)
        {
            // solid under the (translucent) restyled windows, so the rows behind stay hidden; rounded like Modern's window
            if (Ui.Modern) _ui.ModernBox(sb, drop.Rect, new Color(8, 10, 18), 10, 1f, shadow: false);
            else _ui.Fill(sb, drop.Rect, new Color(8, 10, 18));
            drop.Draw(sb, _ui, true, frame);
        }
    }
}
