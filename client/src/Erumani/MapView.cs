using Erumani.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani;

/// <summary>A unit (or "Other" object) on the battle map, with its live position.</summary>
public sealed class MapUnit(MapObject source)
{
    public MapObject Source { get; } = source;
    public int Unit => Source.Unit;
    public int X { get; set; } = source.X;
    public int Y { get; set; } = source.Y;
    /// <summary>Sprite-sheet row: 0 down (facing viewer), 1 up, 2 left, 4 right (inferred).</summary>
    public int Facing { get; set; }
    public bool Dead { get; set; }
    // movement animation
    public Queue<byte> PendingSteps { get; } = new();
    public Queue<byte> RunningSteps { get; } = new();
    public Vector2 Offset;              // pixel offset while stepping
    public int StepFrames;
}

/// <summary>Runtime state of one battle map plus its drawing.</summary>
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
    public List<MapUnit> Units { get; }
    public Point Camera;
    public int Phase { get; set; } = 1;
    public int PixelWidth => Map.Width * Tile;
    public int PixelHeight => Map.Height * Tile;

    public MapSession(MapData map, Database db, GameState state, Textures tex)
    {
        Map = map;
        _db = db;
        _state = state;
        _tex = tex;
        _chips = tex.Get($"MAPC_{map.Tileset:000}.BMP", transparent: false);
        Units = map.Objects.Where(o => o.InUse).Select(o => new MapUnit(o)).ToList();
    }

    /// <summary>Objects with a switch condition only appear while that switch is ON.</summary>
    public bool Visible(MapUnit u) => !u.Dead && (u.Source.SwitchCondition < 0 || _state.Switches[u.Source.SwitchCondition]);

    IEnumerable<MapUnit> Present(int unit) =>
        Units.Where(u => u.Source.Type != ObjectType.Other && u.Unit == unit && Visible(u));

    public bool UnitState(int unit, int state) => state switch
    {
        0 => Present(unit).Any(),
        1 => Units.Any(u => u.Unit == unit && u.Dead),
        2 => !Present(unit).Any(),
        3 => Present(unit).Any(u => u.Source.Type == ObjectType.Ally),
        4 => Present(unit).Any(u => u.Source.Type is ObjectType.Enemy or ObjectType.Neutral),
        _ => false,
    };

    public void ScrollTo(int x, int y)
    {
        Camera = new Point(x * Tile + Tile / 2 - ErumaniGame.ViewWidth / 2, y * Tile + Tile / 2 - ErumaniGame.ViewHeight / 2);
        ClampCamera();
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
        foreach (var s in steps) u.PendingSteps.Enqueue(s);
    }

    public void ExecuteMoves()
    {
        foreach (var u in Units)
            while (u.PendingSteps.Count > 0) u.RunningSteps.Enqueue(u.PendingSteps.Dequeue());
    }

    public bool MovesRunning => Units.Any(u => u.RunningSteps.Count > 0 || u.StepFrames > 0);

    static readonly int[] FacingRow = { 1, 4, 0, 2 };          // step dir up/right/down/left -> sheet row
    static readonly Point[] StepDelta = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

    public void Update()
    {
        foreach (var u in Units)
        {
            if (u.StepFrames > 0)
            {
                u.StepFrames--;
                var d = StepDelta[ReverseFacing(u.Facing)];
                u.Offset = new Vector2(-d.X, -d.Y) * Tile * u.StepFrames / FramesPerTile;
                continue;
            }
            if (u.RunningSteps.Count == 0) continue;
            int s = u.RunningSteps.Dequeue();
            int dir = s % 4;
            u.Facing = FacingRow[dir];
            if (s < 4)
            {
                u.X += StepDelta[dir].X;
                u.Y += StepDelta[dir].Y;
                u.StepFrames = FramesPerTile;
                u.Offset = new Vector2(-StepDelta[dir].X, -StepDelta[dir].Y) * Tile;
            }
        }
    }

    static int ReverseFacing(int row) => Array.IndexOf(FacingRow, row) is var i and >= 0 ? i : 2;

    static Rectangle ChipRect(int chip)
    {
        var (cx, cy) = Chipset.ChipCell(chip);
        return new(cx * Tile, cy * Tile, Tile, Tile);
    }

    Texture2D? UnitSheet(int unitId)
    {
        if (unitId < 0 || unitId >= _db.Units.Length) return null;
        int cls = _state.ClassOverride.TryGetValue(unitId, out var c) ? c : _db.Units[unitId].ClassId;
        if (cls < 0 || cls >= _db.Classes.Length) return null;
        int g = _db.Classes[cls].UnitGraphic;
        if (!_unitSheets.TryGetValue(g, out var t))
            _unitSheets[g] = t = _tex.Get($"UNIT_{g:000}.BMP");
        return t;
    }

    /// <param name="frame">global frame counter for the idle animation</param>
    public void Draw(SpriteBatch sb, Rectangle viewport, int frame)
    {
        if (_chips is null) return;
        int x0 = Math.Max(0, Camera.X / Tile), y0 = Math.Max(0, Camera.Y / Tile);
        int x1 = Math.Min(Map.Width - 1, (Camera.X + viewport.Width) / Tile);
        int y1 = Math.Min(Map.Height - 1, (Camera.Y + viewport.Height) / Tile);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                sb.Draw(_chips, new Vector2(viewport.X + x * Tile - Camera.X, viewport.Y + y * Tile - Camera.Y),
                    ChipRect(Map.Tiles[y, x]), Color.White);

        foreach (var u in Units.Where(Visible).OrderBy(u => u.Y))
        {
            var pos = new Vector2(viewport.X + u.X * Tile - Camera.X, viewport.Y + u.Y * Tile - Camera.Y) + u.Offset;
            if (u.Source.Type == ObjectType.Other)
            {
                if (u.Source.Graphic >= 0) sb.Draw(_chips, pos, ChipRect(u.Source.Graphic), Color.White);
                continue;
            }
            var sheet = UnitSheet(u.Unit);
            if (sheet is null) continue;
            // 3 frames x 5 poses of 64x64 cells, character centred; idle cycles frames 0-1-2-1.
            int col = (frame / 15 % 4) switch { 0 => 0, 1 => 1, 2 => 2, _ => 1 };
            sb.Draw(sheet, pos - new Vector2(16, 16), new Rectangle(col * 64, u.Facing * 64, 64, 64), Color.White);
        }
    }
}
