using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani.Camp;

/// <summary>
/// A vertical text list in a green SYSTEM panel: 12 px text, 16 px rows, optional right-aligned columns and a
/// header row with a rule. The hovered row gets the flashing light-green / white bar with dark text (as the
/// battle lists; T23 recording).
/// </summary>
public sealed class MenuList
{
    public Rectangle Rect;
    public string[] Items = Array.Empty<string>();
    public string[][]? Columns;      // right-aligned columns: text per row, right edge at ColumnX (from Rect.X)
    public int[] ColumnX = Array.Empty<int>();
    public bool[] Enabled = Array.Empty<bool>();
    public int Index;
    public string? Header;           // a title line with a rule under it (save / load lists)
    public string[]? HeaderColumns;  // a column header row with a rule (shop: アイテム / 値段 / 所持数)
    public int TopPad, Indent = 9, RowInset = 4;
    public bool SkipDisabled;   // keyboard movement jumps over disabled rows (headers in the options)

    int RowsTop => Rect.Y + 9 + TopPad + (Header is not null ? 34 : 0) + (HeaderColumns is not null ? 24 : 0);

    public Rectangle Row(int i) => new(Rect.X + RowInset, RowsTop + i * 16, Rect.Width - 2 * RowInset, 16);

    public void Set(string[] items, bool[]? enabled = null)
    {
        Items = items;
        Enabled = enabled ?? items.Select(_ => true).ToArray();
        Index = Math.Clamp(Index, 0, Math.Max(0, items.Length - 1));
    }

    public int Navigate(BattleInput i, Point mouse)
    {
        if (Items.Length == 0) return -1;
        for (int step = 0; step < Items.Length && (i.Up || i.Down); step++)
        {
            Index = (Index + (i.Up ? Items.Length - 1 : 1)) % Items.Length;
            if (!SkipDisabled || Enabled[Index]) break;
        }
        int hover = Enumerable.Range(0, Items.Length).FirstOrDefault(k => Row(k).Contains(mouse), -1);
        if (i.MouseMoved && hover >= 0) Index = hover;
        if (i.Confirm || (i.Click && hover >= 0))
        {
            if (Enabled[Index]) { Sfx.Click(); return Index; }
            Sfx.Buzzer();
        }
        return -1;
    }

    public void Draw(SpriteBatch sb, Ui.Ui ui, bool active, int frame)
    {
        ui.Panel(sb, Rect);
        var white = Ui.Ui.PanelText;
        if (Header is not null)
        {
            if (ui.SmallText.Get(Header) is { } h) sb.Draw(h, new Vector2(Rect.X + 13, Rect.Y + 10), white);
            ui.Fill(sb, new Rectangle(Rect.X + 12, Rect.Y + 32, Rect.Width - 24, 1), white);
        }
        if (HeaderColumns is not null)
        {
            int y = Rect.Y + 9 + TopPad;
            if (ui.SmallText.Get(HeaderColumns[0]) is { } h0) sb.Draw(h0, new Vector2(Rect.X + 24, y), white);
            for (int c = 1; c < HeaderColumns.Length && c - 1 < ColumnX.Length; c++)
                if (ui.SmallText.Get(HeaderColumns[c]) is { } hc) sb.Draw(hc, new Vector2(Rect.X + ColumnX[c - 1] - hc.Width, y), white);
            ui.Fill(sb, new Rectangle(Rect.X + 12, y + 18, Rect.Width - 24, 1), white);
        }
        bool modern = Ui.Ui.Modern;
        var bar = frame / 8 % 2 == 0 ? new Color(160, 248, 96) : new Color(248, 248, 248);
        for (int k = 0; k < Items.Length; k++)
        {
            var r = Row(k);
            bool hot = active && k == Index;
            if (hot && modern) ui.ModernHighlight(sb, r, frame);
            else if (hot) ui.Fill(sb, r, bar);
            var c = hot ? (modern ? Color.White : new Color(0, 32, 0)) : !Enabled[k] ? (modern ? new Color(110, 116, 132) : new Color(120, 140, 120)) : white;
            if (ui.SmallText.Get(Items[k]) is { } t) sb.Draw(t, new Vector2(r.X + Indent, r.Y + 1), c);
            if (Columns is null) continue;
            for (int col = 0; col < Columns.Length && col < ColumnX.Length; col++)
                if (k < Columns[col].Length && Columns[col][k] is { Length: > 0 } s && ui.SmallText.Get(s) is { } ct)
                    sb.Draw(ct, new Vector2(Rect.X + ColumnX[col] - ct.Width, r.Y + 1), c);
        }
    }
}

/// <summary>
/// The camp between maps (SMAP camp_before_battle). Layout measured from recordings of the original (T23):
/// CAMP_nnn full screen with the camp BGM; menu 96x129 at (16,16): 次のステージ / ユニット編成 / アイテム購入 /
/// アイテム売却 / セーブする / ロードする / ゲーム終了. Sub-screens replace the menu:
/// - ユニット編成: unit list 180x400 at (88,40), each with 出撃 / ――; commands 武器 / 防具 / 装飾品を替える, 道具を使う,
///   技を見る at (288,40) (greyed when there's nothing to choose); 「出撃ユニット数 n／max」 at (432,40); the status
///   window at (288,152).
/// - equipment / skill lists 180 (204) x 336 at (80,104), the description window 480x48 at (80,32), status beside.
/// - shop: list 264x336 at (80,96) with a 「アイテム 値段 (下取り額) 所持数」 header and a rule; gold "n,肉" in a
///   192x32 panel at (368,96); description at the top. Sale price = half the price (ルーティのマント 10 → 5).
/// - 次のステージ: 「キャンプを終了して／進撃を開始します」, a rule and はい／いいえ in a panel at (16,16).
/// Guessed: 道具を使う (not recorded yet) and how a unit's 出撃 is toggled (left/right or clicking its 出撃 column).
/// </summary>
public sealed class CampScreen
{
    enum Mode { Menu, Units, UnitCommands, Equip, UseItem, Skills, Buy, Sell, Save, Load, Confirm }

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
    readonly MenuList _units = new() { Rect = new Rectangle(88, 40, 180, 400), ColumnX = new[] { 168 } };
    readonly MenuList _commands = new() { Rect = new Rectangle(288, 40, 108, 96) };
    readonly MenuList _list = new();
    readonly MenuList _confirm = new() { Rect = new Rectangle(16, 16, 132, 100), Indent = 44 };
    readonly List<int> _unitIds = new();
    int _unit = -1, _slot;   // equipment slot 0 weapon, 1 armor, 2 accessory
    List<int> _rowItems = new();
    List<Magic> _rowSpells = new();
    string? _message;
    int _messageFrames;
    string[] _confirmLines = Array.Empty<string>();
    Action? _confirmYes;
    Mode _confirmFrom;

    public Action? StartNext { get; set; }
    public Action<SaveFile>? Loaded { get; set; }
    public Action? Quit { get; set; }
    public string Bgm => _next.Bgm[0];
    public bool AcceptsCancel => _mode != Mode.Menu;

    public CampScreen(Database db, GameFolder folder, Textures tex, Ui.Ui ui, GameState state, MapData next, int nextIndex)
    {
        (_db, _folder, _tex, _ui, _state, _next, _nextIndex) = (db, folder, tex, ui, state, next, nextIndex);
        _field = new Battlefield(next, db, null);
        _menu.Set(new[] { W(24), W(25), W(26), W(27), W(21), W(22), W(23) },   // WORD.DAT 24-27, 21-23
            new[] { true, _state.Roster.Count > 0, next.ShopItems.Length > 0, true, true, true, true });
        // who sorties: everyone until changed here, up to the map's start positions (units the map places come anyway)
        _state.Sortie = new HashSet<int>(_state.Roster.Keys.Take(SortieMax));
    }

    string W(int i) => i >= 0 && i < _db.Words.Length ? _db.Words[i] : "";
    string Gold(int n) => $"{n}{W(31)}";   // shown literally: 「60,肉」 (T23)
    int SortieMax => Math.Max(1, _next.StartPositions.Length);

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
        bool back = i.Cancel || i.RightClick;
        switch (_mode)
        {
            case Mode.Menu:
                switch (_menu.Navigate(i, mouse))
                {
                    case 0: Confirm(new[] { W(55), W(56) }, () => StartNext?.Invoke()); break;   // キャンプを終了して / 進撃を開始します
                    case 1: OpenUnits(); break;
                    case 2: OpenShop(sell: false); break;
                    case 3: OpenShop(sell: true); break;
                    case 4: OpenFiles(save: true); break;
                    case 5: OpenFiles(save: false); break;
                    case 6: Confirm(new[] { W(57) }, () => Quit?.Invoke()); break;           // ゲームを終了します
                }
                break;
            case Mode.Confirm:
                if (back) { _mode = _confirmFrom; break; }
                if (_confirm.Navigate(i, mouse) is var c and >= 0) { if (c == 0) _confirmYes?.Invoke(); else _mode = _confirmFrom; }
                break;
            case Mode.Units:
                if (back) { _mode = Mode.Menu; break; }
                if (i.Left || i.Right || (i.Click && SortieColumnHit(mouse))) { ToggleSortie(); break; }
                if (_units.Navigate(i, mouse) is var u and >= 0) { _unit = _unitIds[u]; OpenCommands(); }
                break;
            case Mode.UnitCommands:
                if (back) { _mode = Mode.Units; break; }
                switch (_commands.Navigate(i, mouse))
                {
                    case 0: case 1: case 2: _slot = _commands.Index; OpenEquip(); break;
                    case 3: OpenUseItem(); break;
                    case 4: OpenSkills(); break;
                }
                break;
            case Mode.Equip:
                if (back) { _mode = Mode.UnitCommands; RefreshCommands(); break; }
                if (_list.Navigate(i, mouse) is var e and >= 0) Equip(_rowItems[e]);
                break;
            case Mode.UseItem:
                if (back) { _mode = Mode.UnitCommands; RefreshCommands(); break; }
                if (_list.Navigate(i, mouse) is var it and >= 0) UseItem(_rowItems[it]);
                break;
            case Mode.Skills:
                if (back) { _mode = Mode.UnitCommands; break; }
                _list.Navigate(i, mouse);
                break;
            case Mode.Buy:
                if (back) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var b and >= 0) Buy(_rowItems[b]);
                break;
            case Mode.Sell:
                if (back) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var s and >= 0) Sell(_rowItems[s]);
                break;
            case Mode.Save:
            case Mode.Load:
                if (back) { _mode = Mode.Menu; break; }
                if (_list.Navigate(i, mouse) is var f and >= 0) { if (_mode == Mode.Save) SaveTo(f + 1); else LoadFrom(f + 1); }
                break;
        }
    }

    void Confirm(string[] lines, Action yes)
    {
        _confirmFrom = _mode;
        _confirmLines = lines;
        _confirmYes = yes;
        _confirm.Rect = new Rectangle(16, 16, 132, 16 * lines.Length + 68);
        _confirm.TopPad = 16 * lines.Length + 12;
        _confirm.Set(new[] { W(45), W(46) });   // はい / いいえ
        _confirm.Index = 0;
        _mode = Mode.Confirm;
    }

    // ---------------- ユニット編成 ----------------

    void OpenUnits()
    {
        _unitIds.Clear();
        _unitIds.AddRange(_state.Roster.Keys);
        RefreshUnits();
        _mode = Mode.Units;
    }

    void RefreshUnits()
    {
        _units.Set(_unitIds.Select(id => _db.Units[id].Name).ToArray());
        _units.Columns = new[] { _unitIds.Select(id => _state.Sortie!.Contains(id) ? W(39) : W(40)).ToArray() };   // 出撃 / ――
    }

    bool SortieColumnHit(Point mouse) =>
        Enumerable.Range(0, _unitIds.Count).Any(k => _units.Row(k).Contains(mouse) && mouse.X >= _units.Rect.X + 130);

    /// <summary>出撃 ↔ ――, up to the map's start positions; locked when the map disables party changes (guess).</summary>
    void ToggleSortie()
    {
        if (_next.DisablePartyChanges || _unitIds.Count == 0) return;
        int id = _unitIds[_units.Index];
        if (!_state.Sortie!.Remove(id) && _state.Sortie.Count < SortieMax) _state.Sortie.Add(id);
        RefreshUnits();
    }

    static readonly ItemType[] SlotTypes = { ItemType.Weapon, ItemType.Armor, ItemType.Accessory };

    static bool Usable(Item it, int classId) => classId >= 0 && classId < it.ClassUsable.Length && it.ClassUsable[classId] != 0;

    IEnumerable<int> EquipChoices(int slot, RosterEntry e) =>
        _state.Items.Keys.Where(id => _db.Items[id].Type == SlotTypes[slot] && Usable(_db.Items[id], e.ClassId)).Order();

    IEnumerable<int> UseChoices(RosterEntry e) =>
        _state.Items.Keys.Where(id => _db.Items[id].Type is ItemType.StatUp or ItemType.ClassChange && Usable(_db.Items[id], e.ClassId)).Order();

    void OpenCommands()
    {
        _commands.Set(new[] { W(33), W(34), W(35), W(36), W(37) });   // 武器 / 防具 / 装飾品を替える, 道具を使う, 技を見る
        RefreshCommands();
        _mode = Mode.UnitCommands;
    }

    /// <summary>Greyed when there is nothing to choose (the original greys 装飾品を替える / 道具を使う then, T23).</summary>
    void RefreshCommands()
    {
        var e = _state.Roster[_unit];
        bool Has(int slot) => EquipChoices(slot, e).Any() || (slot switch { 0 => e.Weapon, 1 => e.Armor, _ => e.Accessory }) >= 0;
        _commands.Enabled = new[] { Has(0), Has(1), Has(2), UseChoices(e).Any(), true };
    }

    void ShowList(int width, string[] names, string[][]? columns, int[] columnX, bool[]? enabled = null)
    {
        _list.Rect = new Rectangle(80, 104, width, 336);
        _list.Header = null;
        _list.HeaderColumns = null;
        _list.Indent = 9;
        _list.Columns = columns;
        _list.ColumnX = columnX;
        _list.Index = 0;
        _list.Set(names, enabled);
    }

    void OpenEquip()
    {
        var e = _state.Roster[_unit];
        _rowItems = new List<int> { -1 };   // 「（装備をはずす）」 first
        _rowItems.AddRange(EquipChoices(_slot, e));
        ShowList(180, _rowItems.Select(id => id < 0 ? W(32) : _db.Items[id].Name).ToArray(),
            new[] { _rowItems.Select(id => id < 0 ? "" : $"{_state.ItemCount(id)}").ToArray() }, new[] { 168 });
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
        _mode = Mode.UnitCommands;
        RefreshCommands();
    }

    // 道具を使う: not recorded yet. Guess: the same list as equipment, with stat-up and class-change items; a stat-up
    // raises the base stats, a class-change item needs its level and a matching (from → to) pair.
    void OpenUseItem()
    {
        var e = _state.Roster[_unit];
        _rowItems = UseChoices(e).ToList();
        ShowList(180, _rowItems.Select(id => _db.Items[id].Name).ToArray(),
            new[] { _rowItems.Select(id => $"{_state.ItemCount(id)}").ToArray() }, new[] { 168 },
            _rowItems.Select(id => ItemWorks(id, e)).ToArray());
        _mode = Mode.UseItem;
    }

    bool ItemWorks(int id, RosterEntry e)
    {
        var it = _db.Items[id];
        return it.Type == ItemType.StatUp || (e.Level >= it.S.ClassChangeLevel && ClassChangeTarget(it, e.ClassId) >= 0);
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
            Say($"{name}{W(64)}{W(68)}");   // guess: 「〜が上昇した」
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
        _rowSpells = _field.KnownSpells(Unit(_unit)).ToList();
        ShowList(204, _rowSpells.Select(m => m.Name).ToArray(),
            new[] { _rowSpells.Select(_ => W(3)).ToArray(), _rowSpells.Select(m => $"{m.MpCost}").ToArray() }, new[] { 164, 192 });
        _mode = Mode.Skills;
    }

    // ---------------- shop ----------------

    /// <summary>Sale price: half the price (T23: ルーティのマント 10 → 5, ポーション 10 → 5); price 0 = unsellable.</summary>
    static int SellPrice(Item it) => it.Price / 2;

    void OpenShop(bool sell)
    {
        _mode = sell ? Mode.Sell : Mode.Buy;
        _list.Rect = new Rectangle(80, 96, 264, 336);
        _list.Header = null;
        _list.Indent = 9;
        _list.HeaderColumns = new[] { "アイテム", W(sell ? 29 : 28), W(30) };   // 値段 / 下取り額, 所持数
        _list.ColumnX = new[] { 204, 252 };
        _list.Index = 0;
        RefreshShop();
    }

    void RefreshShop()
    {
        bool sell = _mode == Mode.Sell;
        _rowItems = sell
            ? _state.Items.Keys.Where(id => _db.Items[id].Price > 0).Order().ToList()
            : _next.ShopItems.Where(id => id >= 0 && id < _db.Items.Length).ToList();
        _list.Set(_rowItems.Select(id => _db.Items[id].Name).ToArray(),
            sell ? null : _rowItems.Select(id => _db.Items[id].Price <= _state.Gold).ToArray());
        _list.Columns = new[]
        {
            _rowItems.Select(id => $"{(sell ? SellPrice(_db.Items[id]) : _db.Items[id].Price)}").ToArray(),
            _rowItems.Select(id => $"{_state.ItemCount(id)}").ToArray(),
        };
    }

    void Buy(int id)
    {
        var it = _db.Items[id];
        if (it.Price > _state.Gold) return;
        _state.Gold -= it.Price;
        _state.AddItem(id, 1);
        RefreshShop();
    }

    void Sell(int id)
    {
        _state.Gold += SellPrice(_db.Items[id]);
        _state.AddItem(id, -1);
        RefreshShop();
    }

    // ---------------- save / load ----------------

    void OpenFiles(bool save)
    {
        _list.Header = W(save ? 53 : 54);
        _list.HeaderColumns = null;
        _list.Columns = null;
        _list.Indent = 16;
        _list.Rect = new Rectangle(120, 16, 96, 177);
        var exists = Enumerable.Range(1, 8).Select(n => File.Exists(SaveFile.PathFor(_folder, n))).ToArray();
        _list.Set(Enumerable.Range(81, 8).Select(W).ToArray(), save ? null : exists);   // ファイル１–８
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
        var mode = _mode == Mode.Confirm ? _confirmFrom : _mode;
        switch (mode)
        {
            case Mode.Menu or Mode.Save or Mode.Load:
                if (_mode != Mode.Confirm) _menu.Draw(sb, _ui, _mode == Mode.Menu, frame);
                if (mode is Mode.Save or Mode.Load) _list.Draw(sb, _ui, true, frame);
                break;
            case Mode.Units or Mode.UnitCommands:
                _units.Draw(sb, _ui, true, frame);
                if (mode == Mode.Units) _commands.Set(new[] { W(33), W(34), W(35), W(36), W(37) });
                _commands.Draw(sb, _ui, mode == Mode.UnitCommands, frame);
                DrawSortieCount(sb);
                int shown = mode == Mode.Units && _unitIds.Count > 0 ? _unitIds[_units.Index] : _unit;
                if (shown >= 0) Ui.StatusWindow.Draw(sb, _ui, _tex, _db, _field, Unit(shown), StatusRect);
                break;
            case Mode.Equip or Mode.UseItem or Mode.Skills:
                _list.Draw(sb, _ui, true, frame);
                Ui.StatusWindow.Draw(sb, _ui, _tex, _db, _field, Unit(_unit), StatusRect);
                DrawDescription(sb);
                break;
            case Mode.Buy or Mode.Sell:
                _list.Draw(sb, _ui, true, frame);
                var g = new Rectangle(368, 96, 192, 32);
                _ui.Panel(sb, g);
                if (_ui.SmallText.Get(Gold(_state.Gold)) is { } gt) sb.Draw(gt, new Vector2(g.Right - 12 - gt.Width, g.Y + 10), Ui.Ui.PanelText);
                DrawDescription(sb);
                break;
        }
        if (_mode == Mode.Confirm) DrawConfirm(sb, frame);
        if (_messageFrames > 0 && _message is not null)
        {
            int w = Math.Max(240, _ui.SmallText.Measure(_message) + 32);
            var r = new Rectangle(320 - w / 2, 64, w, 33);
            _ui.Panel(sb, r);
            if (_ui.SmallText.Get(_message) is { } t) sb.Draw(t, new Vector2(r.X + 16, r.Y + 11), Ui.Ui.PanelText);
        }
    }

    static readonly Rectangle StatusRect = new(288, 152, 264, 288);

    void DrawSortieCount(SpriteBatch sb)
    {
        var r = new Rectangle(432, 40, 112, 48);
        _ui.Panel(sb, r);
        if (_ui.SmallText.Get(W(38)) is { } a) sb.Draw(a, new Vector2(r.X + 12, r.Y + 8), Ui.Ui.PanelText);   // 出撃ユニット数
        if (_ui.SmallText.Get($"{_state.Sortie!.Count}／{SortieMax,2}") is { } b) sb.Draw(b, new Vector2(r.Right - 12 - b.Width, r.Y + 26), Ui.Ui.PanelText);
    }

    /// <summary>Description window 480x48 at (80,32): "name：" and the text (items and spells).</summary>
    void DrawDescription(SpriteBatch sb)
    {
        var d = new Rectangle(80, 32, 480, 48);
        _ui.Panel(sb, d);
        string? name = null, desc = null;
        if (_mode == Mode.Skills && _list.Index < _rowSpells.Count) (name, desc) = (_rowSpells[_list.Index].Name, _rowSpells[_list.Index].Desc);
        else if (_mode != Mode.Skills && _list.Index < _rowItems.Count && _rowItems[_list.Index] >= 0)
            (name, desc) = (_db.Items[_rowItems[_list.Index]].Name, _db.Items[_rowItems[_list.Index]].Desc);
        if (name is null) return;
        if (_ui.SmallText.Get($"{name}：") is { } a) sb.Draw(a, new Vector2(d.X + 16, d.Y + 10), Ui.Ui.PanelText);
        if (_ui.SmallText.Get(desc!) is { } b) sb.Draw(b, new Vector2(d.X + 16, d.Y + 26), Ui.Ui.PanelText);
    }

    /// <summary>Confirmation (T23): message lines, a rule, はい／いいえ, in a panel at (16,16).</summary>
    void DrawConfirm(SpriteBatch sb, int frame)
    {
        var r = _confirm.Rect;
        _confirm.Draw(sb, _ui, true, frame);
        for (int k = 0; k < _confirmLines.Length; k++)
            if (_ui.SmallText.Get(_confirmLines[k]) is { } t) sb.Draw(t, new Vector2(r.X + 13, r.Y + 9 + k * 16), Ui.Ui.PanelText);
        _ui.Fill(sb, new Rectangle(r.X + 12, r.Y + 9 + 16 * _confirmLines.Length + 4, r.Width - 24, 1), Ui.Ui.PanelText);
    }
}
