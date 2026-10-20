using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;
using Srpg95.Rules;

namespace Erumani;

/// <summary>Per-unit animation state (the rules state lives in <see cref="BattleUnit"/>).</summary>
public sealed class UnitVisual
{
    /// <summary>Sprite-sheet row: 0 down (facing viewer), 1 up, 2 left, 4 right (inferred).</summary>
    public int Facing;
    public Queue<byte> Pending { get; } = new();
    public Queue<byte> Running { get; } = new();
    public Vector2 Offset;
    public Vector2 StepFrom;   // pixel offset at the start of the current step
    public int StepFrames;
}

/// <summary>Runtime state of one battle map: the rules battlefield plus drawing and animation.</summary>
public sealed class MapSession : IMapContext
{
    public const int Tile = Chipset.ChipSize;   // 32 px
    const int FramesPerTile = 8;

    readonly Database _db;
    readonly GameState _state;
    readonly Textures _tex;
    readonly Texture2D? _chips;
    readonly Dictionary<int, Texture2D?> _unitSheets = new();

    public MapData Map { get; }
    public Battlefield Field { get; }
    public Dictionary<BattleUnit, UnitVisual> Visuals { get; } = new();
    /// <summary>Units already dead in the rules but still drawn until their defeat has been shown.</summary>
    public HashSet<BattleUnit> Ghosts { get; } = new();
    /// <summary>"Other" objects (event tiles, graphics).</summary>
    public List<MapObject> Others { get; }
    public Point Camera;
    public int Phase => Field.Phase;
    public int PixelWidth => Map.Width * Tile;
    public int PixelHeight => Map.Height * Tile;

    public MapSession(MapData map, Database db, GameState state, Textures tex, byte[]? chipTerrain)
    {
        Map = map;
        _db = db;
        _state = state;
        _tex = tex;
        _chips = tex.Get($"MAPC_{map.Tileset:000}.BMP", transparent: false);
        Field = new Battlefield(map, db, chipTerrain);
        Others = map.Objects.Where(o => o.InUse && o.Type == ObjectType.Other).ToList();
        foreach (var o in map.Objects.Where(o => o.InUse && o.Type != ObjectType.Other))
            Add(CreateUnit(o.Unit, o.Type switch { ObjectType.Ally => Side.Ally, ObjectType.Enemy => Side.Enemy, _ => Side.Neutral },
                o.X, o.Y, o));
        DeployRoster();
        UpdateVisibility();
    }

    void Add(BattleUnit u)
    {
        Field.Units.Add(u);
        Visuals[u] = new UnitVisual();
    }

    /// <summary>Builds a unit from UNIT.DAT, or from the roster for party members (allies only).</summary>
    BattleUnit CreateUnit(int unitId, Side side, int x, int y, MapObject? o)
    {
        var u = _db.Units[Math.Clamp(unitId, 0, _db.Units.Length - 1)];
        var r = side == Side.Ally && _state.Roster.TryGetValue(unitId, out var e) ? e : null;
        var baseStats = (int[])(r?.Base ?? u.BaseStats).Clone();
        return new BattleUnit
        {
            Unit = unitId, Side = side, MapObject = o?.Index ?? -1, Name = u.Name,
            ClassId = _state.ClassOverride.TryGetValue(unitId, out var c) ? c : r?.ClassId ?? u.ClassId,
            Level = r?.Level ?? u.Level, Exp = r?.Exp ?? u.Exp, Base = baseStats, Growth = u.GrowthPct,
            Hp = baseStats[0], Mp = baseStats[1],
            Weapon = _state.WeaponOverride.TryGetValue(unitId, out var w) ? w : r?.Weapon ?? u.Weapon,
            Armor = r?.Armor ?? u.Armor, Accessory = r?.Accessory ?? u.Accessory,
            X = x, Y = y, Leader = o?.Leader ?? false, Guest = o?.Guest ?? false, Unattackable = o?.Unattackable ?? false,
            MovePatterns = o?.MovePatterns ?? Array.Empty<MovePattern>(),
        };
    }

    /// <summary>
    /// Party members not placed by the map itself go onto the map's start positions, in roster
    /// order (provisional: the camp's sortie screen chooses this in the original).
    /// </summary>
    void DeployRoster()
    {
        var placed = Field.Units.Select(u => u.Unit).ToHashSet();
        var free = new Queue<(int X, int Y)>(Map.StartPositions.Where(p => Field.UnitAt(p.X, p.Y) is null));
        foreach (var id in _state.Roster.Keys)
        {
            if (placed.Contains(id) || free.Count == 0) continue;
            var p = free.Dequeue();
            Add(CreateUnit(id, Side.Ally, p.X, p.Y, null));
        }
    }

    /// <summary>Objects with a switch condition are only on the map while that switch is ON.</summary>
    public void UpdateVisibility()
    {
        foreach (var u in Field.Units)
        {
            if (u.MapObject < 0 || u.ForceShown) { if (u.ForceShown) u.Hidden = false; continue; }
            var o = Map.Objects.First(x => x.Index == u.MapObject);
            u.Hidden = o.SwitchCondition >= 0 && o.SwitchCondition < GameState.SwitchCount && !_state.Switches[o.SwitchCondition];
        }
    }

    public bool OtherVisible(MapObject o) => o.SwitchCondition < 0 || _state.Switches[o.SwitchCondition];

    /// <summary>Allies that survive a won map join (or update) the roster, unless they're guests.</summary>
    public void SaveRoster()
    {
        foreach (var u in Field.Units.Where(u => u.Side == Side.Ally && !u.Guest && !u.Dead))
            _state.Roster[u.Unit] = new RosterEntry(u.ClassId, u.Level, u.Exp, (int[])u.Base.Clone(), u.Weapon, u.Armor, u.Accessory);
    }

    // ---------------- IMapContext (event interpreter) ----------------

    IEnumerable<BattleUnit> Present(int unit) => Field.Alive.Where(u => u.Unit == unit);

    public bool UnitState(int unit, int state) => state switch
    {
        0 => Present(unit).Any(),
        1 => Field.Units.Any(u => u.Unit == unit && u.Dead),
        2 => !Present(unit).Any(),
        3 => Present(unit).Any(u => u.Side == Side.Ally),
        4 => Present(unit).Any(u => u.Side != Side.Ally),
        _ => false,
    };

    public void ScrollTo(int x, int y)
    {
        Camera = new Point(x * Tile + Tile / 2 - ErumaniGame.ViewWidth / 2, y * Tile + Tile / 2 - ErumaniGame.ViewHeight / 2);
        ClampCamera();
    }

    Point? _panTarget;
    const int PanSpeed = 16;   // px per frame (provisional)

    /// <summary>Scrolls smoothly until tile (x,y) is on screen, centring it; no-op if already well inside the view.</summary>
    public void PanTo(int x, int y)
    {
        int px = x * Tile, py = y * Tile, m = 2 * Tile;
        if (px >= Camera.X + m && px + Tile <= Camera.X + ErumaniGame.ViewWidth - m &&
            py >= Camera.Y + m && py + Tile <= Camera.Y + ErumaniGame.ViewHeight - m) return;
        var saved = Camera;
        ScrollTo(x, y);
        _panTarget = Camera;
        Camera = saved;
    }

    public bool Panning => _panTarget is not null;

    void UpdatePan()
    {
        if (_panTarget is not { } t) return;
        Camera.X += Math.Clamp(t.X - Camera.X, -PanSpeed, PanSpeed);
        Camera.Y += Math.Clamp(t.Y - Camera.Y, -PanSpeed, PanSpeed);
        if (Camera == t) _panTarget = null;
    }

    public void ClampCamera()
    {
        Camera.X = Math.Clamp(Camera.X, 0, Math.Max(0, PixelWidth - ErumaniGame.ViewWidth));
        Camera.Y = Math.Clamp(Camera.Y, 0, Math.Max(0, PixelHeight - ErumaniGame.ViewHeight));
    }

    public void QueueMove(int unit, IReadOnlyList<byte> steps)
    {
        var u = Present(unit).FirstOrDefault();
        if (u is null) return;
        foreach (var s in steps) Visuals[u].Pending.Enqueue(s);
    }

    public void ExecuteMoves()
    {
        foreach (var v in Visuals.Values)
            while (v.Pending.Count > 0) v.Running.Enqueue(v.Pending.Dequeue());
    }

    public bool MovesRunning => Visuals.Values.Any(v => v.Running.Count > 0 || v.StepFrames > 0);

    /// <summary>Animates a unit along a path of tiles (battle movement).</summary>
    public void Walk(BattleUnit u, IReadOnlyList<(int X, int Y)> path)
    {
        int x = u.X, y = u.Y;
        foreach (var p in path)
        {
            byte dir = (p.X - x, p.Y - y) switch { (0, -1) => 0, (1, 0) => 1, (0, 1) => 2, _ => 3 };
            Visuals[u].Running.Enqueue(dir);
            (x, y) = p;
        }
    }

    static readonly int[] FacingRow = { 1, 4, 0, 2 };          // step dir up/right/down/left -> sheet row
    static readonly Point[] StepDelta = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

    public void Update()
    {
        UpdatePan();
        foreach (var (u, v) in Visuals)
        {
            if (v.StepFrames > 0)
            {
                v.StepFrames--;
                v.Offset = v.StepFrom * v.StepFrames / FramesPerTile;
                continue;
            }
            if (v.Running.Count == 0) continue;
            int s = v.Running.Dequeue();
            int dir = s % 4;
            v.Facing = FacingRow[dir];
            if (s < 4)
            {
                u.X += StepDelta[dir].X;
                u.Y += StepDelta[dir].Y;
                v.StepFrames = FramesPerTile;
                v.Offset = v.StepFrom = new Vector2(-StepDelta[dir].X, -StepDelta[dir].Y) * Tile;
            }
        }
    }

    // ---------------- drawing ----------------

    static Rectangle ChipRect(int chip)
    {
        var (cx, cy) = Chipset.ChipCell(chip);
        return new(cx * Tile, cy * Tile, Tile, Tile);
    }

    Texture2D? UnitSheet(BattleUnit u)
    {
        int g = _db.Classes[Math.Clamp(u.ClassId, 0, _db.Classes.Length - 1)].UnitGraphic;
        if (!_unitSheets.TryGetValue(g, out var t))
            _unitSheets[g] = t = _tex.Get($"UNIT_{g:000}.BMP");
        return t;
    }

    public Vector2 ScreenPos(int x, int y, Rectangle viewport) =>
        new(viewport.X + x * Tile - Camera.X, viewport.Y + y * Tile - Camera.Y);

    public void DrawTiles(SpriteBatch sb, Rectangle viewport)
    {
        if (_chips is null) return;
        int x0 = Math.Max(0, Camera.X / Tile), y0 = Math.Max(0, Camera.Y / Tile);
        int x1 = Math.Min(Map.Width - 1, (Camera.X + viewport.Width) / Tile);
        int y1 = Math.Min(Map.Height - 1, (Camera.Y + viewport.Height) / Tile);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                sb.Draw(_chips, ScreenPos(x, y, viewport), ChipRect(Map.Tiles[y, x]), Color.White);
        foreach (var o in Others)
            if (o.Graphic >= 0 && OtherVisible(o)) sb.Draw(_chips, ScreenPos(o.X, o.Y, viewport), ChipRect(o.Graphic), Color.White);
    }

    /// <summary>Range overlay from SYSTEM.BMP's 32x32 range tiles (y 64): 0 cyan = move, 1 salmon = attack /
    /// medicine, 2 orange = attack spells, 3 blue = own-side spells. The original draws only the tile's odd pixel
    /// rows, opaque, leaving the map visible in between (T19c, pixel-matched); "solid" tiles (units a spell will
    /// affect) use every row. The checker scrolls up-left: its phase moves 2 px diagonally every 4 frames
    /// (measured on the T22 spell recording; it wraps every 16 px).</summary>
    public void DrawRange(SpriteBatch sb, Rectangle viewport, IEnumerable<(int X, int Y)> tiles, int colour, int frame, bool solid = false)
    {
        var sys = _tex.Get("SYSTEM.BMP", transparent: false);
        if (sys is null) return;
        int phase = frame / 4 * 2 % 16;   // the colour block is two identical 32-px tiles wide, so x + phase never runs off it
        foreach (var (x, y) in tiles)
        {
            var p = ScreenPos(x, y, viewport);
            for (int row = solid ? 0 : 1; row < Tile; row += solid ? 1 : 2)
                sb.Draw(sys, p + new Vector2(0, row), new Rectangle(colour * 64 + phase, 64 + (row + phase) % Tile, Tile, 1), Color.White);
        }
    }

    public void DrawUnits(SpriteBatch sb, Rectangle viewport, int frame)
    {
        var sys = _tex.Get("SYSTEM.BMP");
        foreach (var u in Field.Alive.Concat(Ghosts).OrderBy(u => u.Y))
        {
            var v = Visuals[u];
            var pos = ScreenPos(u.X, u.Y, viewport) + v.Offset;
            var sheet = UnitSheet(u);
            if (sheet is null) continue;
            // 3 frames x 5 poses of 64x64 cells, character centred; idle cycles frames 0-1-2-1.
            int col = u.Acted ? 1 : (frame / 15 % 4) switch { 0 => 0, 1 => 1, 2 => 2, _ => 1 };
            sb.Draw(sheet, pos - new Vector2(16, 16), new Rectangle(col * 64, v.Facing * 64, 64, 64), Color.White);
            if (u.Acted && u.Side == Side.Ally && sys is not null)
                sb.Draw(sys, pos + new Vector2(6, 19), new Rectangle(70, 19, 22, 10), Color.White);   // "END"
        }
    }
}
