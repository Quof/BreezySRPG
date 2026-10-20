using System.Text.Json;
using Srpg95.Data;

namespace Srpg95.Data.Tests;

/// <summary>
/// Checks the C# loaders against the Python reference parser's JSON output
/// (re/json, produced by re/srpg95_data.py from the original game folder).
/// Tests pass trivially if the game folder or JSON isn't present (e.g. on CI).
/// </summary>
public class CrossCheckTests
{
    static readonly string? Repo = FindRepo();
    static string GameDir => Path.Combine(Repo!, "えるまに");
    static string JsonDir => Path.Combine(Repo!, "re", "json");
    static bool Available => Repo is not null && Directory.Exists(GameDir) && Directory.Exists(JsonDir);

    static string? FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "re")) && Directory.Exists(Path.Combine(d.FullName, "client")))
                return d.FullName;
        return null;
    }

    static JsonElement Json(string relative) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(JsonDir, relative))).RootElement;

    static int[] Ints(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();

    static readonly Lazy<Database> Db = new(() => Database.Load(new GameFolder(GameDir)));

    [Fact]
    public void Units_match_reference()
    {
        if (!Available) { Assert.True(Environment.GetEnvironmentVariable("SRPG95_REQUIRE_DATA") != "1", "game data / re/json not found"); return; }
        var js = Json("unit.json").EnumerateArray().ToArray();
        Assert.Equal(js.Length, Db.Value.Units.Length);
        foreach (var (u, j) in Db.Value.Units.Zip(js))
        {
            Assert.Equal(j.GetProperty("name").GetString(), u.Name);
            Assert.Equal(j.GetProperty("face").GetInt32(), u.Face);
            Assert.Equal(j.GetProperty("class_id").GetInt32(), u.ClassId);
            Assert.Equal(Ints(j.GetProperty("base_stats")), u.BaseStats);
            Assert.Equal(Ints(j.GetProperty("growth_pct")), u.GrowthPct);
            Assert.Equal(j.GetProperty("weapon").GetInt32(), u.Weapon);
            Assert.Equal(j.GetProperty("death_message").GetString(), u.DeathMessage);
        }
    }

    [Fact]
    public void Classes_items_magic_terrain_animations_match_reference()
    {
        if (!Available) { Assert.True(Environment.GetEnvironmentVariable("SRPG95_REQUIRE_DATA") != "1", "game data / re/json not found"); return; }
        var db = Db.Value;
        foreach (var (c, j) in db.Classes.Zip(Json("class.json").EnumerateArray()))
        {
            Assert.Equal(j.GetProperty("name").GetString(), c.Name);
            Assert.Equal(j.GetProperty("movement").GetInt32(), c.Movement);
            Assert.Equal(j.GetProperty("unit_graphic").GetInt32(), c.UnitGraphic);
            Assert.Equal(Ints(j.GetProperty("terrain_move_override")), c.TerrainMoveOverride);
            Assert.Equal(Ints(j.GetProperty("skill_ids")), c.SkillIds);
        }
        foreach (var (it, j) in db.Items.Zip(Json("item.json").EnumerateArray()))
        {
            Assert.Equal(j.GetProperty("name").GetString(), it.Name);
            Assert.Equal(j.GetProperty("type").GetInt32(), (int)it.Type);
            Assert.Equal(j.GetProperty("price").GetInt32(), it.Price);
            Assert.Equal(j.GetProperty("wpn_power").GetInt32(), it.S.WpnPower);
            Assert.Equal(j.GetProperty("med_range").GetInt32(), it.S.MedRange);
        }
        foreach (var (m, j) in db.Magic.Zip(Json("magic.json").EnumerateArray()))
        {
            Assert.Equal(j.GetProperty("name").GetString(), m.Name);
            Assert.Equal(j.GetProperty("mp_cost").GetInt32(), m.MpCost);
            Assert.Equal(j.GetProperty("target").GetInt32(), m.Target);
            var rows = j.GetProperty("range_grid").EnumerateArray().Select(r => r.GetString()!).ToArray();
            for (int r = 0; r < 21; r++)
                for (int c = 0; c < 21; c++)
                    Assert.Equal(rows[r][c] == '#', m.RangeGrid[r, c]);
        }
        foreach (var (t, j) in db.Terrains.Zip(Json("geology.json").EnumerateArray()))
        {
            Assert.Equal(j.GetProperty("name").GetString(), t.Name);
            Assert.Equal(Ints(j.GetProperty("move_cost")), t.MoveCost);
        }
        foreach (var (a, j) in db.Animations.Zip(Json("anime.json").EnumerateArray()))
        {
            Assert.Equal(j.GetProperty("frame_count").GetInt32(), a.FrameCount);
            Assert.Equal(j.GetProperty("hit_sound").GetString(), a.HitSound);
            var f0 = j.GetProperty("frames")[0][0];
            Assert.Equal(f0[0].GetInt32(), a.Frames[0][0].X);
        }
        Assert.Equal(Json("mapname.json").EnumerateArray().Select(x => x.GetString()), db.MapNames);
    }

    [Fact]
    public void All_maps_match_reference()
    {
        if (!Available) { Assert.True(Environment.GetEnvironmentVariable("SRPG95_REQUIRE_DATA") != "1", "game data / re/json not found"); return; }
        int scripts = 0, roundTrips = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(GameDir, "MAP"), "SMAP_*.DAT"))
        {
            var m = MapData.Load(path);
            var j = Json(Path.Combine("map", Path.GetFileNameWithoutExtension(path).ToLowerInvariant() + ".json"));
            Assert.Equal(j.GetProperty("name").GetString(), m.Name);
            Assert.Equal(j.GetProperty("tileset").GetInt32(), m.Tileset);
            var tiles = j.GetProperty("tiles").EnumerateArray().ToArray();
            Assert.Equal(tiles.Length, m.Height);
            for (int y = 0; y < m.Height; y++)
            {
                var row = Ints(tiles[y]);
                for (int x = 0; x < m.Width; x++) Assert.Equal(row[x], m.Tiles[y, x]);
            }
            var objs = j.GetProperty("objects").EnumerateArray().ToArray();
            Assert.Equal(objs.Length, m.Objects.Length);
            foreach (var (o, jo) in m.Objects.Zip(objs))
            {
                Assert.Equal(jo.GetProperty("index").GetInt32(), o.Index);
                Assert.Equal(jo.GetProperty("x").GetInt32(), o.X);
                Assert.Equal(jo.GetProperty("unit").GetInt32(), o.Unit);
                Assert.Equal(jo.GetProperty("event_slots")[0].GetProperty("condition").GetInt32(), o.Slots[0].Condition);
            }
            var evs = j.GetProperty("events");
            Assert.Equal(evs.EnumerateObject().Select(p => p.Name).OrderBy(x => x), m.Scripts.Keys.OrderBy(x => x));
            foreach (var p in evs.EnumerateObject())
            {
                var s = m.Scripts[p.Name];
                scripts++;
                bool pyError = p.Value.TryGetProperty("error", out _);
                Assert.Equal(pyError, s.Error is not null);
                if (pyError) continue;
                Assert.Equal(p.Value.GetProperty("commands").GetArrayLength(), s.Commands.Count);
                Assert.True(s.RoundTrips, $"{path} {p.Name} does not round-trip");
                roundTrips++;
            }
        }
        Assert.Equal(446, scripts);
        Assert.Equal(443, roundTrips);
    }

    [Fact]
    public void All_bitmaps_decode()
    {
        if (!Available) { Assert.True(Environment.GetEnvironmentVariable("SRPG95_REQUIRE_DATA") != "1", "game data / re/json not found"); return; }
        foreach (var path in Directory.GetFiles(Path.Combine(GameDir, "BMP")))
        {
            if (!path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) continue;
            var bmp = IndexedBitmap.Load(path);
            Assert.Equal(bmp.Width * bmp.Height, bmp.Pixels.Length);
        }
    }
}
