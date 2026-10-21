using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani;

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
    // the original's three rows plus 「オプション」 (modern settings)
    readonly Camp.MenuList _menu = new() { Rect = new Rectangle(266, 320, 108, 80), ItemWidth = 84 };
    readonly Camp.MenuList _files = new() { Rect = new Rectangle(272, 280, 96, 176), ItemX = 18, ItemWidth = 60 };
    bool _loading;
    /// <summary>Moves the mouse pointer (FUN_00409d24); a menu that opens puts it on its first row (FUN_00413100).</summary>
    public Action<Point>? WarpPointer { get; set; }
    bool _pointMenu = true;

    public Action? NewGame { get; set; }
    public Action<SaveFile>? Loaded { get; set; }
    public Action? Quit { get; set; }
    public Action? Options { get; set; }
    public bool AcceptsCancel => _loading;

    public TitleScreen(Database db, GameFolder folder, Textures tex, Ui.Ui ui)
    {
        (_db, _folder, _tex, _ui) = (db, folder, tex, ui);
        _menu.Set(new[] { W(47), W(22), "オプション", W(23) });
    }

    string W(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";

    public void Update(BattleInput i, Point mouse)
    {
        if (_pointMenu)
        {
            _pointMenu = false;
            WarpPointer?.Invoke((_loading ? _files : _menu).Row((_loading ? _files : _menu).Index).Center);
        }
        if (_loading)
        {
            if (i.Cancel || i.RightClick) { _loading = false; _menu.Index = 1; _pointMenu = true; return; }
            if (_files.Navigate(i, mouse) is var f and >= 0 && SaveFile.Load(SaveFile.PathFor(_folder, f + 1)) is { } save) Loaded?.Invoke(save);
            return;
        }
        switch (_menu.Navigate(i, mouse))
        {
            case 0: NewGame?.Invoke(); break;
            case 1:
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
        if (_tex.Get("TITLE.BMP", transparent: false) is { } t) sb.Draw(t, Vector2.Zero, Color.White);
        if (!_loading) _menu.Draw(sb, _ui, true, frame);
        if (_loading) _files.Draw(sb, _ui, true, frame);
    }
}
