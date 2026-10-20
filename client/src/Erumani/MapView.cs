using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Srpg95.Data;

namespace Erumani;

/// <summary>Draws one map: tiles from MAPC_nnn.BMP and the map's objects.</summary>
public sealed class MapView
{
    public const int Tile = Chipset.ChipSize;   // 32 px

    readonly MapData _map;
    readonly Database _db;
    readonly Texture2D? _chips;
    readonly Dictionary<int, Texture2D?> _unitSheets = new();
    readonly Textures _tex;

    public MapData Map => _map;
    public int PixelWidth => _map.Width * Tile;
    public int PixelHeight => _map.Height * Tile;

    public MapView(MapData map, Database db, Textures tex)
    {
        _map = map;
        _db = db;
        _tex = tex;
        _chips = tex.Get($"MAPC_{map.Tileset:000}.BMP", transparent: false);
    }

    static Rectangle ChipRect(int chip)
    {
        var (cx, cy) = Chipset.ChipCell(chip);
        return new(cx * Tile, cy * Tile, Tile, Tile);
    }

    Texture2D? UnitSheet(int unitId)
    {
        if (unitId < 0 || unitId >= _db.Units.Length) return null;
        int cls = _db.Units[unitId].ClassId;
        if (cls < 0 || cls >= _db.Classes.Length) return null;
        int g = _db.Classes[cls].UnitGraphic;
        if (!_unitSheets.TryGetValue(g, out var t))
            _unitSheets[g] = t = _tex.Get($"UNIT_{g:000}.BMP");
        return t;
    }

    /// <param name="camera">top-left of the view in map pixels</param>
    /// <param name="frame">animation frame counter (sprite sheets have 3 frames per row)</param>
    public void Draw(SpriteBatch sb, Point camera, Rectangle viewport, int frame, bool showDeleted)
    {
        if (_chips is null) return;
        int x0 = Math.Max(0, camera.X / Tile), y0 = Math.Max(0, camera.Y / Tile);
        int x1 = Math.Min(_map.Width - 1, (camera.X + viewport.Width) / Tile);
        int y1 = Math.Min(_map.Height - 1, (camera.Y + viewport.Height) / Tile);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                sb.Draw(_chips, new Vector2(viewport.X + x * Tile - camera.X, viewport.Y + y * Tile - camera.Y),
                    ChipRect(_map.Tiles[y, x]), Color.White);

        foreach (var o in _map.Objects)
        {
            if (!o.InUse && !showDeleted) continue;
            var pos = new Vector2(viewport.X + o.X * Tile - camera.X, viewport.Y + o.Y * Tile - camera.Y);
            var tint = o.InUse ? Color.White : Color.White * 0.35f;
            if (o.Type == ObjectType.Other)
            {
                if (o.Graphic >= 0) sb.Draw(_chips, pos, ChipRect(o.Graphic), tint);
                continue;
            }
            var sheet = UnitSheet(o.Unit);
            if (sheet is null) continue;
            // Unit sheets are 3 frames x 5 poses of 64x64 cells, character centred.
            // Row 0 = facing the viewer (inferred); idle animation cycles frames 0-1-2-1.
            int col = (frame / 15 % 4) switch { 0 => 0, 1 => 1, 2 => 2, _ => 1 };
            var src = new Rectangle(col * 64, 0, 64, 64);
            sb.Draw(sheet, pos - new Vector2(16, 16), src, tint);
        }
    }
}
