using BreezySRPG95.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace BreezySRPG95;

/// <summary>
/// Title screen: TITLE.BMP full screen (the logo is part of the picture) with a menu of 「はじめませう」 (new game,
/// WORD 47), ロードする (22) and ゲーム終了 (23) at (266, 320), 9 × 12 px wide with 84-px items (FUN_00422510); loading
/// replaces it with 「ロードします」, a rule and ファイル１–８ at (272, 280), 96 × 176 with the files 60 px wide in the middle
/// (FUN_0042267c). Both open with the pointer on their first row.
/// </summary>
public sealed class TitleScreen
{
    readonly Database _db;
    readonly GameFolder _folder;
    readonly Textures _tex;
    readonly Ui.Ui _ui;
    // the original's three rows plus 「環境設定」 (modern settings)
    readonly Camp.MenuList _menu = new() { Rect = new Rectangle(266, 320, 108, 80), ItemWidth = 84 };
    readonly Camp.MenuList _files = new() { Rect = new Rectangle(272, 280, 96, 176), ItemX = 18, ItemWidth = 60 };
    bool _loading;
    /// <summary>Moves the mouse pointer (FUN_00409d24); a menu that opens puts it on its first row (FUN_00413100).</summary>
    public Action<Point>? WarpPointer { get; set; }
    bool _pointMenu = true;

    public Action? NewGame { get; set; }
    /// <summary>A save picked to load, and its file number (its BreezySRPG95 note is the file's own).</summary>
    public Action<SaveFile, int>? Loaded { get; set; }
    public Action? Quit { get; set; }
    public Action? Options { get; set; }
    public bool AcceptsCancel => _loading;
    /// <summary>The extended load screen (modern), when the options choose it over the original's file list (null).</summary>
    public Func<bool, Ui.SaveMenu?>? NewSaveMenu { get; set; }
    Ui.SaveMenu? _saveMenu;

    public TitleScreen(Database db, GameFolder folder, Textures tex, Ui.Ui ui)
    {
        (_db, _folder, _tex, _ui) = (db, folder, tex, ui);
        _menu.Set(new[] { W(47), W(22), "", W(23) });
    }

    string W(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";

    public void Update(BattleInput i, Point mouse)
    {
        _menu.Items[2] = Ui.Lang.T("Options", "環境設定");   // in the language the options may just have changed
        if (_pointMenu)
        {
            _pointMenu = false;
            if (_saveMenu is null) WarpPointer?.Invoke((_loading ? _files : _menu).Row((_loading ? _files : _menu).Index).Center);
        }
        if (_loading && _saveMenu is { } files)
        {
            var (result, slot) = files.Update(i, mouse);
            if (result == Ui.SaveMenu.Result.None) return;
            if (result == Ui.SaveMenu.Result.Picked && SaveFile.Load(SaveFile.PathFor(_folder, slot)) is { } picked) { Loaded?.Invoke(picked, slot); return; }
            files.Dispose();
            _saveMenu = null;
            _loading = false;
            _menu.Index = 1;
            _pointMenu = true;
            return;
        }
        if (_loading)
        {
            if (i.Cancel || i.RightClick) { _loading = false; _menu.Index = 1; _pointMenu = true; return; }
            if (_files.Navigate(i, mouse) is var f and >= 0 && SaveFile.Load(SaveFile.PathFor(_folder, f + 1)) is { } save) Loaded?.Invoke(save, f + 1);
            return;
        }
        switch (_menu.Navigate(i, mouse))
        {
            case 0: NewGame?.Invoke(); break;
            case 1:
                if (NewSaveMenu?.Invoke(false) is { } menu) { _saveMenu = menu; _loading = true; break; }
                _files.Header = new[] { W(54), "-" };   // ロードします
                _files.Set(Enumerable.Range(81, 8).Select(W).ToArray(),
                    Enumerable.Range(1, 8).Select(n => File.Exists(SaveFile.PathFor(_folder, n))).ToArray());
                _files.Index = 0;
                _loading = true;
                _pointMenu = true;
                break;
            case 2: Options?.Invoke(); break;
            case 3: Quit?.Invoke(); break;
        }
    }

    public void Draw(SpriteBatch sb, int frame)
    {
        _menu.Items[2] = Ui.Lang.T("Options", "環境設定");
        if (_tex.Get("TITLE.BMP", transparent: false) is { } t) sb.Draw(t, Vector2.Zero, Color.White);
        if (!_loading) _menu.Draw(sb, _ui, true, frame);
        else if (_saveMenu is not null) _saveMenu.Draw(sb, frame);
        else _files.Draw(sb, _ui, true, frame);
    }
}
