using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani;

/// <summary>
/// Title screen: TITLE.BMP full screen (the logo is part of the picture) with a menu of 「はじめませう」 (new game,
/// WORD 47), ロードする (22) and ゲーム終了 (23); loading opens the same ファイル１–８ list as the camp.
/// Menu position and contents are provisional: the original's title menu hasn't been seen yet.
/// </summary>
public sealed class TitleScreen
{
    readonly Database _db;
    readonly GameFolder _folder;
    readonly Textures _tex;
    readonly Ui.Ui _ui;
    readonly Camp.MenuList _menu = new() { Rect = new Rectangle(272, 360, 96, 65) };
    readonly Camp.MenuList _files = new() { Rect = new Rectangle(380, 248, 96, 177), Indent = 16 };
    bool _loading;

    public Action? NewGame { get; set; }
    public Action<SaveFile>? Loaded { get; set; }
    public Action? Quit { get; set; }

    public TitleScreen(Database db, GameFolder folder, Textures tex, Ui.Ui ui)
    {
        (_db, _folder, _tex, _ui) = (db, folder, tex, ui);
        _menu.Set(new[] { W(47), W(22), W(23) });
    }

    string W(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";

    public void Update(BattleInput i, Point mouse)
    {
        if (_loading)
        {
            if (i.Cancel || i.RightClick) { _loading = false; return; }
            if (_files.Navigate(i, mouse) is var f and >= 0 && SaveFile.Load(SaveFile.PathFor(_folder, f + 1)) is { } save) Loaded?.Invoke(save);
            return;
        }
        switch (_menu.Navigate(i, mouse))
        {
            case 0: NewGame?.Invoke(); break;
            case 1:
                _files.Header = W(54);   // ロードします
                _files.Set(Enumerable.Range(81, 8).Select(W).ToArray(),
                    Enumerable.Range(1, 8).Select(n => File.Exists(SaveFile.PathFor(_folder, n))).ToArray());
                _files.Index = Math.Max(0, Array.IndexOf(_files.Enabled, true));
                _loading = true;
                break;
            case 2: Quit?.Invoke(); break;
        }
    }

    public void Draw(SpriteBatch sb, int frame)
    {
        if (_tex.Get("TITLE.BMP", transparent: false) is { } t) sb.Draw(t, Vector2.Zero, Color.White);
        _menu.Draw(sb, _ui, !_loading, frame);
        if (_loading) _files.Draw(sb, _ui, true, frame);
    }
}
