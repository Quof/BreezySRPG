using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani.Camp;

/// <summary>A vertical text menu in a green SYSTEM panel (12 px text, 16 px rows), optional right-aligned column.</summary>
sealed class MenuList
{
    public Rectangle Rect;
    public string[] Items = Array.Empty<string>();
    public string[]? Right;
    public bool[] Enabled = Array.Empty<bool>();
    public int Index;
    public string? Header;   // a title row with a rule under it (save / load lists)
    public int TopPad;       // extra space above the rows (text drawn there by the caller)

    // with a header (save / load lists, T17b screenshot): title at +10, rule at +32, rows from +43, indented to +20
    int RowsTop => Rect.Y + 9 + TopPad + (Header is null ? 0 : 34);
    int Indent => Header is null ? 9 : 16;

    public Rectangle Row(int i) => new(Rect.X + 4, RowsTop + i * 16, Rect.Width - 8, 16);

    public void Set(string[] items, bool[]? enabled = null, string[]? right = null)
    {
        Items = items;
        Enabled = enabled ?? items.Select(_ => true).ToArray();
        Right = right;
        Index = Math.Clamp(Index, 0, Math.Max(0, items.Length - 1));
    }

    public int Navigate(BattleInput i, Point mouse)
    {
        if (Items.Length == 0) return -1;
        if (i.Up) Index = (Index + Items.Length - 1) % Items.Length;
        if (i.Down) Index = (Index + 1) % Items.Length;
        int hover = Enumerable.Range(0, Items.Length).FirstOrDefault(k => Row(k).Contains(mouse), -1);
        if (i.MouseMoved && hover >= 0) Index = hover;
        if ((i.Confirm || (i.Click && hover >= 0)) && Enabled[Index]) return Index;
        return -1;
    }

    public void Draw(SpriteBatch sb, Ui.Ui ui, bool active)
    {
        ui.Panel(sb, Rect);
        if (Header is not null)
        {
            if (ui.SmallText.Get(Header) is { } h) sb.Draw(h, new Vector2(Rect.X + 13, Rect.Y + 10), Ui.Ui.PanelText);
            ui.Fill(sb, new Rectangle(Rect.X + 12, Rect.Y + 32, Rect.Width - 24, 1), Ui.Ui.PanelText);
        }
        for (int k = 0; k < Items.Length; k++)
        {
            var r = Row(k);
            var c = !Enabled[k] ? new Color(120, 140, 120) : active && k == Index ? new Color(255, 255, 120) : Ui.Ui.PanelText;
            if (ui.SmallText.Get(Items[k]) is { } t) sb.Draw(t, new Vector2(r.X + Indent, r.Y + 1), c);
            if (Right?[k] is { Length: > 0 } s && ui.SmallText.Get(s) is { } rt) sb.Draw(rt, new Vector2(r.Right - 6 - rt.Width, r.Y + 1), c);
        }
    }
}

/// <summary>
/// The camp between maps (SMAP camp_before_battle): CAMP_nnn full screen, the camp BGM, and the menu at (16,16):
/// 次のステージ / ユニット編成 / アイテム購入 / アイテム売却 / セーブする / ロードする / ゲーム終了 (T17b screenshot).
/// The sub-screens (army, shop, equipment) haven't been seen yet; their layout is provisional, built from the
/// same panels and WORD.DAT strings (FORMAT_NOTES §15).
/// </summary>
public sealed class CampScreen
{
    enum Mode { Menu, Units, UnitCommands, Equip, UseItem, Skills, Buy, Sell, Save, Load, Sortie, Confirm }

    readonly Database _db;
    readonly GameFolder _folder;
    readonly Textures _tex;
    readonly Ui.Ui _ui;
    readonly GameState _state;
    readonly MapData _next;
    readonly int _nextIndex;
    readonly Battlefield _field;   // for stats (terrain unused)
    Mode _mode = Mode.Menu;
    readonly MenuList _menu = new() { Rect = new Rectangle(16, 16, 96, 129) };
    readonly MenuList _list = new();
    readonly MenuList _sub = new();
    readonly List<int> _units = new();
    int _unit = -1, _slot;   // equipment slot 0 weapon, 1 armor, 2 accessory
    List<int> _rowItems = new();
    string? _message;
    int _messageFrames;
    (string[] Lines, Action Yes)? _confirm;
    readonly HashSet<int> _sortie = new();

    /// <summary>次のステージ (with the chosen sortie), a save loaded, or ゲーム終了.</summary>
    public Action? StartNext { get; set; }
    public Action<SaveFile>? Loaded { get; set; }
    public Action? Quit { get; set; }
    public string Bgm => _next.Bgm[0];

    public CampScreen(Database db, GameFolder folder, Textures tex, Ui.Ui ui, GameState state, MapData next, int nextIndex)
    {
        (_db, _folder, _tex, _ui, _state, _next, _nextIndex) = (db, folder, tex, ui, state, next, nextIndex);
        _field = new Battlefield(next, db, null);
        // WORD.DAT 24-27, 21-23
        _menu.Set(new[] { W(24), W(25), W(26), W(27), W(21), W(22), W(23) },
            new[] { true, _state.Roster.Count > 0, next.ShopItems.Length > 0, true, true, true, true });
    }

    string W(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";
    string Gold(int n) => $"{n}{W(31).TrimStart(',')}";   // ",肉": the comma stands for the number

    BattleUnit Unit(int id)
    {
        var e = _state.Roster[id];
        var u = _db.Units[id];
        return new BattleUnit
        {
            Unit = id, Side = Side.Ally, ClassId = e.ClassId, Level = e.Level, Exp = e.Exp, Base = e.Base, Growth = u.GrowthPct,
            Hp = e.Base[0], Mp = e.Base[1], Weapon = e.Weapon, Armor = e.Armor, Accessory = e.Accessory, Name = u.Name,
        };
    }

    void Say(string s) { _message = s; _messageFrames = 90; }

    // ---------------- update ----------------

    public void Update(BattleInput i, Point mouse)
    {
        if (_messageFrames > 0) { _messageFrames--; if (i.Confirm || i.Click) _messageFrames = 0; return; }
        switch (_mode)
        {
            case Mode.Menu: UpdateMenu(i, mouse); break;
            case Mode.Confirm:
                if (i.Cancel || i.RightClick) { _mode = Mode.Menu; break; }
                if (_sub.Navigate(i, mouse) is var c and >= 0) { if (c == 0) _confirm!.Value.Yes(); else _mode = Mode.Menu; }
                break;
            case Mode.Units:
                if (i.Cancel || i.RightClick) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var u and >= 0) { _unit = _units[u]; OpenUnitCommands(); }
                break;
            case Mode.UnitCommands:
                if (i.Cancel || i.RightClick) { _mode = Mode.Units; break; }
                switch (_sub.Navigate(i, mouse))
                {
                    case 0: case 1: case 2: _slot = _sub.Index; OpenEquip(); break;
                    case 3: OpenUseItem(); break;
                    case 4: OpenSkills(); break;
                }
                break;
            case Mode.Equip:
                if (i.Cancel || i.RightClick) { _mode = Mode.UnitCommands; break; }
                if (_list.Navigate(i, mouse) is var e and >= 0) Equip(_rowItems[e]);
                break;
            case Mode.UseItem:
                if (i.Cancel || i.RightClick) { _mode = Mode.UnitCommands; break; }
                if (_list.Navigate(i, mouse) is var it and >= 0) UseItem(_rowItems[it]);
                break;
            case Mode.Skills:
                if (i.Cancel || i.RightClick || i.Confirm || i.Click) _mode = Mode.UnitCommands;
                break;
            case Mode.Buy:
                if (i.Cancel || i.RightClick) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var b and >= 0) Buy(_rowItems[b]);
                break;
            case Mode.Sell:
                if (i.Cancel || i.RightClick) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var s and >= 0) Sell(_rowItems[s]);
                break;
            case Mode.Save:
            case Mode.Load:
                if (i.Cancel || i.RightClick) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var f and >= 0) { if (_mode == Mode.Save) SaveTo(f + 1); else LoadFrom(f + 1); }
                break;
            case Mode.Sortie:
                if (i.Cancel || i.RightClick) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var p and >= 0)
                {
                    if (p == _units.Count) { ConfirmNext(); break; }   // 出撃
                    int id = _units[p];
                    if (!_sortie.Remove(id) && _sortie.Count < SortieMax) _sortie.Add(id);
                    RefreshSortie();
                }
                break;
        }
    }

    void UpdateMenu(BattleInput i, Point mouse)
    {
        switch (_menu.Navigate(i, mouse))
        {
            case 0: OpenNext(); break;
            case 1: OpenUnits(); break;
            case 2: OpenBuy(); break;
            case 3: OpenSell(); break;
            case 4: OpenFiles(save: true); break;
            case 5: OpenFiles(save: false); break;
            case 6: Confirm(new[] { W(57) }, () => Quit?.Invoke()); break;   // ゲームを終了します
        }
    }

    void Confirm(string[] lines, Action yes)
    {
        _confirm = (lines, yes);
        _sub.Rect = new Rectangle(232, 200, 176, 16 * lines.Length + 16 + 2 * 16 + 8);
        _sub.Set(new[] { W(45), W(46) });   // はい / いいえ
        _sub.TopPad = 16 * lines.Length + 8;
        _sub.Index = 0;
        _mode = Mode.Confirm;
    }

    // 次のステージ: choose who sorties when the roster is bigger than the map's start positions (provisional),
    // then 「キャンプを終了して / 進撃を開始します」 (WORD 55, 56) with はい / いいえ
    int SortieMax => Math.Max(1, _next.StartPositions.Length);

    void OpenNext()
    {
        _units.Clear();
        _units.AddRange(_state.Roster.Keys);
        if (_units.Count > SortieMax && !_next.DisablePartyChanges)
        {
            _sortie.Clear();
            foreach (var id in _units.Take(SortieMax)) _sortie.Add(id);
            _list.Rect = new Rectangle(128, 16, 200, 16 * (_units.Count + 1) + 18 + 24);
            _list.Index = 0;
            RefreshSortie();
            _mode = Mode.Sortie;
            return;
        }
        _state.Sortie = null;
        ConfirmNext();
    }

    void RefreshSortie()
    {
        _list.Header = $"{W(38)}  {_sortie.Count}／{SortieMax}";
        _list.Set(_units.Select(id => _db.Units[id].Name).Append(W(39)).ToArray(), null,
            _units.Select(id => _sortie.Contains(id) ? W(39) : W(40)).Append("").ToArray());
    }

    void ConfirmNext()
    {
        if (_mode == Mode.Sortie) _state.Sortie = new HashSet<int>(_sortie);
        Confirm(new[] { W(55), W(56) }, () => StartNext?.Invoke());
    }

    // ユニット編成: pick a unit, then 武器 / 防具 / 装飾品を替える, 道具を使う, 技を見る (WORD 33-37)
    void OpenUnits()
    {
        _units.Clear();
        _units.AddRange(_state.Roster.Keys);
        _list.Header = null;
        _list.Rect = new Rectangle(128, 16, 176, 16 * _units.Count + 18);
        _list.Set(_units.Select(id => _db.Units[id].Name).ToArray(), null, _units.Select(id => $"{W(1)} {_state.Roster[id].Level}").ToArray());
        _mode = Mode.Units;
    }

    void OpenUnitCommands()
    {
        _sub.TopPad = 0;
        _sub.Rect = new Rectangle(128, 16 + 16 * _units.Count + 26, 128, 5 * 16 + 18);
        _sub.Set(new[] { W(33), W(34), W(35), W(36), W(37) });
        _mode = Mode.UnitCommands;
    }

    static readonly ItemType[] SlotTypes = { ItemType.Weapon, ItemType.Armor, ItemType.Accessory };

    bool Usable(Item it, int classId) => classId >= 0 && classId < it.ClassUsable.Length && it.ClassUsable[classId] != 0;

    void OpenEquip()
    {
        var e = _state.Roster[_unit];
        // （装備をはずす） first, then matching items from the inventory the class may use
        _rowItems = new List<int> { -1 };
        _rowItems.AddRange(_state.Items.Keys.Where(id => _db.Items[id].Type == SlotTypes[_slot] && Usable(_db.Items[id], e.ClassId)).Order());
        ShowItemList(_rowItems.Select(id => id < 0 ? W(32) : _db.Items[id].Name).ToArray(),
            _rowItems.Select(id => id < 0 ? "" : $"×{_state.ItemCount(id)}").ToArray());
        _mode = Mode.Equip;
    }

    void Equip(int item)
    {
        var e = _state.Roster[_unit];
        int old = _slot switch { 0 => e.Weapon, 1 => e.Armor, _ => e.Accessory };
        if (old >= 0) _state.AddItem(old, 1);
        if (item >= 0) _state.AddItem(item, -1);
        _state.Roster[_unit] = _slot switch
        {
            0 => e with { Weapon = item },
            1 => e with { Armor = item },
            _ => e with { Accessory = item },
        };
        if (item >= 0) Say($"{_db.Units[_unit].Name}は{_db.Items[item].Name}{W(59)}");   // 「を装備します」
        OpenEquip();
    }

    // 道具を使う at camp: stat-up and class-change items (medicine: HP is full at camp) — provisional
    void OpenUseItem()
    {
        var e = _state.Roster[_unit];
        _rowItems = _state.Items.Keys.Where(id => _db.Items[id].Type is ItemType.StatUp or ItemType.ClassChange && Usable(_db.Items[id], e.ClassId)).Order().ToList();
        ShowItemList(_rowItems.Select(id => _db.Items[id].Name).ToArray(), _rowItems.Select(id => $"×{_state.ItemCount(id)}").ToArray(),
            _rowItems.Select(id => ItemWorks(id, e)).ToArray());
        _mode = Mode.UseItem;
    }

    bool ItemWorks(int id, RosterEntry e)
    {
        var it = _db.Items[id];
        if (it.Type == ItemType.StatUp) return true;
        return e.Level >= it.S.ClassChangeLevel && ClassChangeTarget(it, e.ClassId) >= 0;
    }

    static int ClassChangeTarget(Item it, int from)
    {
        for (int k = 0; k + 1 < it.S.ClassChange.Length; k += 2)
            if (it.S.ClassChange[k] == from && it.S.ClassChange[k + 1] >= 0) return it.S.ClassChange[k + 1];
        return -1;
    }

    void UseItem(int id)
    {
        var e = _state.Roster[_unit];
        var it = _db.Items[id];
        string name = _db.Units[_unit].Name;
        if (it.Type == ItemType.StatUp)
        {
            var b = (int[])e.Base.Clone();
            for (int k = 0; k < 6; k++) b[k] += it.S.StatUp[k];
            _state.Roster[_unit] = e with { Base = b };
            Say($"{name}{W(64)}{W(68)}");   // provisional wording: 「〜が上昇した」
        }
        else
        {
            int to = ClassChangeTarget(it, e.ClassId);
            _state.Roster[_unit] = e with { ClassId = to };
            Say($"{name}は{_db.Classes[to].Name}{W(72)}");   // 「に昇格」
        }
        _state.AddItem(id, -1);
        OpenUseItem();
    }

    void OpenSkills()
    {
        var u = Unit(_unit);
        var spells = _field.KnownSpells(u).ToList();
        _list.Header = null;
        _list.Rect = new Rectangle(312, 16, 312, Math.Max(1, spells.Count) * 16 + 18);
        _list.Set(spells.Count > 0 ? spells.Select(m => m.Name).ToArray() : new[] { W(40) }, null,
            spells.Select(m => $"{W(3)} {m.MpCost}").ToArray());
        _mode = Mode.Skills;
    }

    void ShowItemList(string[] names, string[] right, bool[]? enabled = null)
    {
        _list.Header = null;
        _list.Rect = new Rectangle(312, 16, 312, Math.Max(1, names.Length) * 16 + 18);
        _list.Set(names.Length > 0 ? names : new[] { W(40) }, names.Length > 0 ? enabled : new[] { false }, names.Length > 0 ? right : new[] { "" });
    }

    // アイテム購入 / アイテム売却: price list with 値段 / 下取り額, owned count (所持数), gold
    void OpenBuy()
    {
        _rowItems = _next.ShopItems.Where(id => id >= 0 && id < _db.Items.Length).ToList();
        RefreshBuy();
        _mode = Mode.Buy;
    }

    void RefreshBuy() => ShowItemList(_rowItems.Select(id => _db.Items[id].Name).ToArray(),
        _rowItems.Select(id => Gold(_db.Items[id].Price)).ToArray(),
        _rowItems.Select(id => _db.Items[id].Price <= _state.Gold).ToArray());

    void Buy(int id)
    {
        var it = _db.Items[id];
        if (it.Price > _state.Gold) return;
        _state.Gold -= it.Price;
        _state.AddItem(id, 1);
        Say($"{it.Name}{W(62)}");   // 「を獲得」
        RefreshBuy();
    }

    /// <summary>Sale price: half the price (provisional); price 0 = unsellable (help text).</summary>
    static int SellPrice(Item it) => it.Price / 2;

    void OpenSell()
    {
        _rowItems = _state.Items.Keys.Where(id => _db.Items[id].Price > 0).Order().ToList();
        ShowItemList(_rowItems.Select(id => $"{_db.Items[id].Name} ×{_state.ItemCount(id)}").ToArray(),
            _rowItems.Select(id => Gold(SellPrice(_db.Items[id]))).ToArray());
        _mode = Mode.Sell;
    }

    void Sell(int id)
    {
        _state.Gold += SellPrice(_db.Items[id]);
        _state.AddItem(id, -1);
        OpenSell();
    }

    // セーブする / ロードする: 96x177 list, header + rule + ファイル１–８ (WORD 53/54, 81–88); empty slots grey when loading
    void OpenFiles(bool save)
    {
        _list.Header = W(save ? 53 : 54);
        _list.Rect = new Rectangle(120, 16, 96, 177);
        var exists = Enumerable.Range(1, 8).Select(n => File.Exists(SaveFile.PathFor(_folder, n))).ToArray();
        _list.Set(Enumerable.Range(81, 8).Select(W).ToArray(), save ? null : exists);
        _list.Index = 0;
        _mode = save ? Mode.Save : Mode.Load;
    }

    void SaveTo(int slot)
    {
        string path = SaveFile.PathFor(_folder, slot);
        var template = SaveFile.Load(path)?.Raw ?? _state.LastSave;
        SaveFile.ForCamp(_state, _db, _nextIndex, Bgm, template).Write(path);
        Say(W(53));
        _mode = Mode.Menu;
    }

    void LoadFrom(int slot)
    {
        if (SaveFile.Load(SaveFile.PathFor(_folder, slot)) is { } f) Loaded?.Invoke(f);
    }

    // ---------------- drawing ----------------

    public void Draw(SpriteBatch sb, int frame)
    {
        if (_tex.Get($"CAMP_{_next.CampBackground:000}.BMP", transparent: false) is { } back) sb.Draw(back, Vector2.Zero, Color.White);
        else _ui.Fill(sb, new Rectangle(0, 0, 640, 480), Color.Black);
        _menu.Draw(sb, _ui, _mode == Mode.Menu);

        // gold, under the menu (provisional)
        var g = new Rectangle(16, 151, 96, 26);
        _ui.Panel(sb, g);
        if (_ui.SmallText.Get(Gold(_state.Gold)) is { } gt) sb.Draw(gt, new Vector2(g.Right - 10 - gt.Width, g.Y + 7), Ui.Ui.PanelText);

        switch (_mode)
        {
            case Mode.Units: _list.Draw(sb, _ui, true); break;
            case Mode.UnitCommands or Mode.Equip or Mode.UseItem or Mode.Skills:
                _list.Draw(sb, _ui, _mode is Mode.Equip or Mode.UseItem);
                if (_mode == Mode.UnitCommands) DrawUnitsBehind(sb);
                _sub.Draw(sb, _ui, _mode == Mode.UnitCommands);
                Ui.StatusWindow.Draw(sb, _ui, _tex, _db, _field, Unit(_unit), new Rectangle(312, 176, 264, 288));
                if (_mode is Mode.Equip or Mode.UseItem) DrawDescription(sb);
                break;
            case Mode.Buy or Mode.Sell or Mode.Save or Mode.Load or Mode.Sortie:
                _list.Draw(sb, _ui, true);
                if (_mode is Mode.Buy or Mode.Sell) DrawDescription(sb);
                break;
            case Mode.Confirm: DrawConfirm(sb); break;
        }
        if (_messageFrames > 0 && _message is not null)
        {
            int w = Math.Max(240, _ui.SmallText.Measure(_message) + 32);
            var r = new Rectangle(320 - w / 2, 64, w, 33);
            _ui.Panel(sb, r);
            if (_ui.SmallText.Get(_message) is { } t) sb.Draw(t, new Vector2(r.X + 16, r.Y + 11), Ui.Ui.PanelText);
        }
    }

    void DrawUnitsBehind(SpriteBatch sb)
    {
        var units = new MenuList { Rect = new Rectangle(128, 16, 176, 16 * _units.Count + 18) };
        units.Set(_units.Select(id => _db.Units[id].Name).ToArray(), null, _units.Select(id => $"{W(1)} {_state.Roster[id].Level}").ToArray());
        units.Index = _units.IndexOf(_unit);
        units.Draw(sb, _ui, true);
    }

    /// <summary>The item description window at the top, as in battle (80,32 480x48).</summary>
    void DrawDescription(SpriteBatch sb)
    {
        if (_list.Index < 0 || _list.Index >= _rowItems.Count || _rowItems[_list.Index] < 0) return;
        var it = _db.Items[_rowItems[_list.Index]];
        var d = new Rectangle(80, 400, 480, 48);   // bottom here: the menus sit at the top
        _ui.Panel(sb, d);
        if (_ui.SmallText.Get($"{it.Name}：") is { } a) sb.Draw(a, new Vector2(d.X + 16, d.Y + 10), Ui.Ui.PanelText);
        if (_ui.SmallText.Get(it.Desc) is { } b) sb.Draw(b, new Vector2(d.X + 16, d.Y + 26), Ui.Ui.PanelText);
    }

    void DrawConfirm(SpriteBatch sb)
    {
        _sub.Draw(sb, _ui, true);
        var lines = _confirm!.Value.Lines;
        for (int k = 0; k < lines.Length; k++)
            if (_ui.SmallText.Get(lines[k]) is { } t) sb.Draw(t, new Vector2(_sub.Rect.X + 13, _sub.Rect.Y + 9 + k * 16), Ui.Ui.PanelText);
    }
}