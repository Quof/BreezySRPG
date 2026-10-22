using System.Text;
using BreezySRPG95.Runtime;
using MeltySynth;
using Srpg95.Data;

// Compatibility scan: runs the client's own loaders over every file of each game folder and reports what they
// can't read and every value outside what the client knows. See client/tools/README.md.
//   Srpg95Scan OUTDIR GAMEDIR...          a report per game (OUTDIR/<folder name>.txt)
//   Srpg95Scan dump GAMEDIR N...          maps' headers, start positions, character lists and objects
//   Srpg95Scan script GAMEDIR N NAME|*    a map's event scripts as a tree (* = all of them) and its objects' slots
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: Srpg95Scan OUTDIR GAMEDIR... | dump GAMEDIR N... | script GAMEDIR N NAME|*");
    return;
}
// the RTP (fallback graphics and sounds) as the client finds it: "srpg maker 95/RTP" in a folder above
string? rtp = null;
for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && rtp is null; d = d.Parent)
    if (Directory.Exists(Path.Combine(d.FullName, "srpg maker 95", "RTP"))) rtp = Path.Combine(d.FullName, "srpg maker 95", "RTP");
if (args[0] == "script")
{
    var o = new StringBuilder();
    Dump.Script(args[1], int.Parse(args[2]), args[3], o);
    Console.OutputEncoding = Encoding.UTF8;
    Console.Write(o.ToString());
    return;
}
if (args[0] == "dump")
{
    var o = new StringBuilder();
    foreach (var n in args.Skip(2)) Dump.Map(args[1], int.Parse(n), o);
    Console.OutputEncoding = Encoding.UTF8;
    Console.Write(o.ToString());
    return;
}
var outDir = args[0];
Directory.CreateDirectory(outDir);
foreach (var root in args.Skip(1))
{
    var sb = new StringBuilder();
    try { Scanner.Scan(root, rtp, sb); }
    catch (Exception e) { sb.AppendLine("SCAN CRASHED: " + e); }
    var name = new DirectoryInfo(root).Name;
    File.WriteAllText(Path.Combine(outDir, name + ".txt"), sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"{name}: {sb.Length} chars");
}

static class Scanner
{
    static StringBuilder S = null!;
    static void W(string s) => S.AppendLine(s);
    static readonly Dictionary<string, SortedDictionary<string, int>> Hist = new();
    static void H(string key, object v)
    {
        if (!Hist.TryGetValue(key, out var d)) Hist[key] = d = new();
        string k = v.ToString()!;
        d[k] = d.GetValueOrDefault(k) + 1;
    }
    static readonly SortedSet<string> Issues = new();
    static void I(string s) => Issues.Add(s);
    static readonly SortedDictionary<string, SortedSet<string>> Missing = new();
    static GameFolder G = null!;
    static void Need(string rel, string where)
    {
        if (G.Find(rel) is null)
        {
            if (!Missing.TryGetValue(rel, out var w)) Missing[rel] = w = new();
            if (w.Count < 4) w.Add(where);
        }
    }

    static IEnumerable<ScriptNode> Walk(IEnumerable<ScriptNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n is BranchNode b) foreach (var x in Walk(b.Then)) yield return x;
            if (n is ChoiceNode c) foreach (var x in c.Cases.SelectMany(Walk)) yield return x;
        }
    }

    public static void Scan(string root, string? rtp, StringBuilder sb)
    {
        S = sb; Hist.Clear(); Issues.Clear(); Missing.Clear();
        G = new GameFolder(root, rtp);
        W($"== {root}");
        var game = File.ReadAllBytes(Path.Combine(root, "GAME.DAT"));
        W("GAME.DAT: " + string.Join(",", Enumerable.Range(0, 50).Select(i => Bin.I32(game, i * 4)).Select((v, i) => (v, i)).Where(t => t.v != 0).Select(t => $"[{t.i}]={t.v}")));

        // ---------- files ----------
        foreach (var f in Directory.GetFiles(Path.Combine(root, "BMP")))
        {
            if (!f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) continue;
            var b = File.ReadAllBytes(f);
            if (b.Length < 54 || b[0] != 'B' || b[1] != 'M') { I($"BMP not a bitmap: {Path.GetFileName(f)} ({b.Length} B)"); continue; }
            int hdr = Bin.I32(b, 14), bpp = Bin.U16(b, 28), comp = hdr >= 40 ? Bin.I32(b, 30) : 0;
            H("bmp bpp/comp/hdr", $"{bpp}/{comp}/{hdr}");
            try { IndexedBitmap.Decode(b, f); }
            catch (Exception e) { I($"BMP undecodable: {Path.GetFileName(f)} ({bpp} bpp, comp {comp}, hdr {hdr}): {e.Message}"); }
        }
        foreach (var f in Directory.GetFiles(Path.Combine(root, "BGM")))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            H("bgm ext", ext);
            if (ext == ".mid")
            {
                try { var m = new MidiFile(f); H("midi ok", "yes"); }
                catch (Exception e) { I($"MIDI unreadable by MeltySynth: {Path.GetFileName(f)}: {e.Message}"); }
            }
            else if (ext == ".wav") H("bgm wav fmt", WavFmt(f));
        }
        foreach (var f in Directory.GetFiles(Path.Combine(root, "EFS")))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            H("efs ext", ext);
            if (ext == ".wav") { var fmt = WavFmt(f); H("efs wav fmt", fmt); if (!fmt.StartsWith("1/")) I($"EFS non-PCM WAV: {Path.GetFileName(f)} fmt {fmt}"); }
        }

        // ---------- databases ----------
        Database db;
        try { db = Database.Load(G); }
        catch (Exception e) { W("DATABASE LOAD FAILED: " + e.Message); Flush(); return; }

        foreach (var it in db.Items.Where(i => i.Name.Length > 0))
        {
            H("item type", it.Type);
            if ((int)it.Type is < 0 or > 7) I($"item {it.Index} {it.Name}: type {(int)it.Type}");
            if (it.Type == ItemType.Weapon) { H("weapon range idx", it.S.WpnRange); H("weapon anim", it.S.WpnAnimation); if (it.S.WpnRange is < 0 or > 7) I($"item {it.Index} {it.Name}: weapon range {it.S.WpnRange}"); }
            if (it.Type == ItemType.Medicine) { H("med range", it.S.MedRange); if (it.S.MedRange is < 0 or > 7) I($"item {it.Index} {it.Name}: medicine range {it.S.MedRange}"); }
            if (it.Type == ItemType.Magic) { if (it.S.MagicSpell < 0 || it.S.MagicSpell >= 100 || db.Magic[it.S.MagicSpell].Name.Length == 0) I($"item {it.Index} {it.Name}: magic spell {it.S.MagicSpell}"); }
            if (it.Type == ItemType.ClassChange)
                for (int k = 0; k < 20; k++) { int a = it.S.ClassChange[2 * k], c = it.S.ClassChange[2 * k + 1]; if ((a >= 0 || c >= 0) && (a < 0 || a >= 150 || c < 0 || c >= 150)) I($"item {it.Index} {it.Name}: class change pair {a}->{c}"); }
        }
        foreach (var m in db.Magic.Where(m => m.Name.Length > 0))
        {
            H("magic type", m.Type);
            H("magic target", m.Target);
            if (m.Type == 1) H("degrade effect", m.DegradeEffect);
            if (m.Type == 2) H("raise effect", m.RaiseEffect);
            if (m.RangeWholeMap || m.EffectWholeMap) H("magic whole map range/effect", $"{m.RangeWholeMap}/{m.EffectWholeMap}");
            if (m.Type is < 0 or > 2) I($"magic {m.Index} {m.Name}: type {m.Type}");
            if (m.Type == 1 && m.DegradeEffect is < 0 or > 3) I($"magic {m.Index} {m.Name}: degrade effect {m.DegradeEffect}");
            if (m.Type == 2 && m.RaiseEffect is < 0 or > 5) I($"magic {m.Index} {m.Name}: raise effect {m.RaiseEffect}");
            if (m.Target is < 0 or > 1) I($"magic {m.Index} {m.Name}: target {m.Target}");
            if (m.Animation is < 0 or > 79) I($"magic {m.Index} {m.Name}: animation {m.Animation}");
        }
        foreach (var t in db.Terrains.Where(t => t.Name.Length > 0)) { H("terrain bg", t.BattleBackground > 0 ? "set" : "0"); }
        foreach (var a in db.Animations.Where(a => a.Name.Length > 0))
        {
            if (a.FrameCount is < 0 or > 90) I($"anime {a.Index} {a.Name}: frame count {a.FrameCount}");
            if (a.HitSound.Length > 0) Need("EFS/" + a.HitSound, $"anime {a.Index}");
            if (a.MissSound.Length > 0) Need("EFS/" + a.MissSound, $"anime {a.Index}");
        }

        // which units, classes, spells the game can actually field
        var usedUnits = new SortedSet<int>();
        var usedClasses = new SortedSet<int>();
        var mapFiles = Directory.GetFiles(Path.Combine(root, "MAP"), "SMAP_*.DAT").Order().ToList();
        var maps = new List<(int Index, MapData Map)>();
        foreach (var p in mapFiles)
        {
            int idx = int.Parse(Path.GetFileNameWithoutExtension(p)[5..]) - 1;
            try { maps.Add((idx, MapData.Load(p))); }
            catch (Exception e) { I($"SMAP_{idx + 1:000} UNREADABLE: {e.Message}"); }
            var raw = File.ReadAllBytes(p);
            H("map whole grid flag (0x444)", Bin.I32(raw, 0x444));
            if (Bin.I32(raw, 0x448) != 0) H("map 0x448", Bin.I32(raw, 0x448));
            if (Bin.I32(raw, 0x44c) != 0) H("map 0x44c", Bin.I32(raw, 0x44c));
            for (int o = 0x454; o < 0x4b0; o += 4) if (Bin.I32(raw, o) != 0) I($"SMAP_{idx + 1:000}: header 0x{o:x} = {Bin.I32(raw, o)}");
        }
        W($"maps: {maps.Count}, SMAP numbers {string.Join(",", maps.Select(m => m.Index + 1))}");
        if (db.StartMap < 0 || !maps.Any(m => m.Index == db.StartMap)) I($"start map {db.StartMap} missing");

        foreach (var (idx, map) in maps)
        {
            string M = $"SMAP_{idx + 1:000}";
            H("map size", $"{map.Width}x{map.Height}");
            if (map.Width < 19 || map.Height < 14) I($"{M} smaller than the view: {map.Width}x{map.Height}");
            if (map.Width > 40 || map.Height > 40 || map.Width <= 0 || map.Height <= 0) I($"{M} bad size {map.Width}x{map.Height}");
            H("win condition", map.WinCondition);
            H("camp before battle", map.CampBeforeBattle);
            Need($"BMP/MAPC_{map.Tileset:000}.BMP", M);
            if (G.Chipset(map.Tileset) is null) I($"{M}: MAPC_{map.Tileset:000}.DAT missing");
            for (int s = 0; s < 8; s++) if (map.Bgm[s].Length > 0 && map.Bgm[s] != "(OFF)") Need("BGM/" + map.Bgm[s], $"{M} bgm slot {s}");
            if (map.CampBeforeBattle) Need($"BMP/CAMP_{map.CampBackground:000}.BMP", M);
            foreach (var si in map.ShopItems) if (si < 0 || si >= 100 || db.Items[si].Name.Length == 0) I($"{M}: shop item {si}");
            H("start positions", map.StartPositions.Length);
            foreach (var (x, y) in map.StartPositions) if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) I($"{M}: start position ({x},{y}) off the map");
            foreach (var c in map.CharacterList) if (c < 0 || c >= 250) I($"{M}: character list unit {c}");
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++) if (map.Tiles[y, x] >= 384) I($"{M}: tile {map.Tiles[y, x]} at ({x},{y})");

            foreach (var o in map.Objects)
            {
                if (!o.InUse) { H("object", "deleted"); continue; }
                H("object type", o.Type);
                if ((int)o.Type is < 0 or > 3) I($"{M} object {o.Index}: type {(int)o.Type}");
                if (o.X < 0 || o.Y < 0 || o.X >= map.Width || o.Y >= map.Height) I($"{M} object {o.Index}: at ({o.X},{o.Y}) off the map");
                if (o.Type != ObjectType.Other)
                {
                    if (o.Unit < 0 || o.Unit >= 250) I($"{M} object {o.Index}: unit {o.Unit}");
                    else { usedUnits.Add(o.Unit); }
                    for (int k = 0; k < 4; k++) { var mp = o.MovePatterns[k]; if (k == 0 || mp.Switch >= 0) H("move action", mp.Action); if (mp.Action is < 0 or > 3 && (k == 0 || mp.Switch >= 0)) I($"{M} object {o.Index}: move action {mp.Action}"); }
                    if (o.Leader) H("leader side", o.Type);
                    if (o.Guest) H("guest side", o.Type);
                    if (o.Unattackable) H("unattackable side", o.Type);
                    if (o.RewardItem >= 100) I($"{M} object {o.Index}: reward item {o.RewardItem}");
                }
                else { H("other graphic", o.Graphic >= 0 ? "chip" : "none"); if (o.Graphic >= 384) I($"{M} object {o.Index}: graphic {o.Graphic}"); }
                if (o.SwitchCondition >= 500) I($"{M} object {o.Index}: switch condition {o.SwitchCondition}");
                for (int k = 0; k < 5; k++)
                {
                    var sl = o.Slots[k];
                    if (sl.Condition == 0) continue;
                    H($"slot cond ({o.Type})", sl.Condition);
                    if (sl.Condition is 3 or < 0 or > 11) I($"{M} object {o.Index} slot {k + 1}: condition {sl.Condition}");
                    if (!map.Scripts.ContainsKey(MapObject.ScriptName(o.Index, k + 1))) H("slot without script", sl.Condition);
                    if (sl.TargetUnit >= 250) I($"{M} object {o.Index} slot {k + 1}: target {sl.TargetUnit}");
                }
            }
            foreach (var (name, sc) in map.Scripts)
            {
                if (sc.Error is not null) I($"{M} {name}: malformed: {sc.Error}");
                if (!sc.RoundTrips) I($"{M} {name}: doesn't re-encode");
                foreach (var c in sc.Commands) H("opcode", $"{(int)c.Op:X2} {c.Op}");
                foreach (var n in Walk(sc.Tree))
                {
                    string at = $"{M} {name}";
                    switch (n)
                    {
                        case MessageNode mn:
                            H("message window", mn.Window);
                            H("message lines", mn.Lines.Count);
                            int widest = mn.Lines.Max(l => Bin.Sjis.GetByteCount(l));
                            H("message widest (bytes, /4)", widest / 4 * 4);
                            if (mn.Window is < 0 or > 2) I($"{at}: message window {mn.Window}");
                            break;
                        case ChoiceNode cn:
                            H("choice options", cn.Options.Length);
                            if (cn.Cases.Count != cn.Options.Length) H("choice cases != options", $"{cn.Options.Length}/{cn.Cases.Count}");
                            break;
                        case BranchNode bn:
                            var c = bn.Condition;
                            H("branch type", c.U16(0));
                            if (c.U16(0) > 4) I($"{at}: branch type {c.U16(0)}");
                            switch (c.U16(0))
                            {
                                case 0: H("branch switch state", c.Payload[12]); if (c.U16(2) >= 500) I($"{at}: switch {c.U16(2)}"); break;
                                case 1: H("branch turn mode", c.Payload[13]); break;
                                case 2: H("branch item state", c.Payload[14]); if (c.U16(6) >= 100) I($"{at}: item {c.U16(6)}"); break;
                                case 3: H("branch unit state", c.Payload[15]); if (c.U16(8) >= 250) I($"{at}: unit {c.U16(8)}"); break;
                                case 4: H("branch var cmp", c.U16(18)); H("variable index used (/50)", c.U16(10) / 50 * 50); if (c.U16(10) >= 500) I($"{at}: var {c.U16(10)}"); break;
                            }
                            break;
                        case CommandNode { Command: var cmd }:
                            if (!EventRunner.Handled.Contains(cmd.Op) && cmd.Op != Op.EndOfScript) I($"{at}: UNHANDLED opcode {(int)cmd.Op:X2} {cmd.Op} payload {Convert.ToHexString(cmd.Payload)}");
                            Check(cmd, at, db, maps.Select(m => m.Index).ToHashSet(), usedUnits, usedClasses);
                            break;
                    }
                }
            }
        }

        foreach (var u in usedUnits)
        {
            var unit = db.Units[u];
            if (unit.ClassId < 0 || unit.ClassId >= 150) { I($"unit {u} {unit.Name}: class {unit.ClassId}"); continue; }
            usedClasses.Add(unit.ClassId);
            if (unit.Face > 0) Need($"BMP/FACE_{unit.Face:000}.BMP", $"unit {u}");
            foreach (var e in new[] { unit.Weapon, unit.Armor, unit.Accessory }) if (e >= 100) I($"unit {u}: equipment {e}");
            H("unit level", unit.Level / 5 * 5);
        }
        // class changes reachable by items
        foreach (var it in db.Items.Where(i => i.Name.Length > 0 && i.Type == ItemType.ClassChange))
            for (int k = 0; k < 20; k++) if (it.S.ClassChange[2 * k + 1] is >= 0 and < 150) usedClasses.Add(it.S.ClassChange[2 * k + 1]);
        var usedSpells = new SortedSet<int>();
        foreach (var ci in usedClasses)
        {
            var c = db.Classes[ci];
            H("class move type", c.MoveType);
            if (c.MoveType is < 0 or > 2) I($"class {ci} {c.Name}: move type {c.MoveType}");
            Need($"BMP/UNIT_{c.UnitGraphic:000}.BMP", $"class {ci}");
            foreach (var a in new[] { c.AnimWeaponA, c.AnimWeaponB, c.AnimWeaponC, c.AnimNoWeapon, c.AnimMagicEnemy, c.AnimMagicAlly })
            {
                if (a < 0) continue;
                if (a >= 80) { I($"class {ci} {c.Name}: animation {a}"); continue; }
                if (db.Animations[a].FrameCount > 0) Need($"BMP/CA{a + 1:00}_{c.UnitGraphic:000}.BMP", $"class {ci} {c.Name}");
            }
            for (int k = 0; k < 20 && c.SkillLevels[k] > 0; k++)
            {
                if (c.SkillIds[k] is < 0 or >= 100) I($"class {ci}: skill {c.SkillIds[k]}");
                else usedSpells.Add(c.SkillIds[k]);
            }
        }
        foreach (var it in db.Items.Where(i => i.Name.Length > 0 && i.Type == ItemType.Magic && i.S.MagicSpell is >= 0 and < 100)) usedSpells.Add(it.S.MagicSpell);
        foreach (var s in usedSpells)
        {
            var m = db.Magic[s];
            if (m.Animation is >= 0 and < 80 && db.Animations[80 + m.Animation].FrameCount > 0) Need($"BMP/MAGA_{m.Animation + 1:000}.BMP", $"spell {s} {m.Name}");
        }
        foreach (var (idx, map) in maps)
        {
            var chips = G.Chipset(map.Tileset);
            if (chips is null) continue;
            var terr = new SortedSet<int>();
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++) terr.Add(chips[Math.Min((int)map.Tiles[y, x], 383)]);
            foreach (var o in map.Objects.Where(o => o.InUse && o.Type == ObjectType.Other && o.Graphic is >= 0 and < 384)) terr.Add(chips[o.Graphic]);
            foreach (var t in terr)
            {
                if (t >= 70) { I($"SMAP_{idx + 1:000}: terrain id {t}"); continue; }
                int bg = db.Terrains[t].BattleBackground;
                if (bg > 0) Need($"BMP/BACK_{bg:000}.BMP", $"SMAP_{idx + 1:000} terrain {t}");
            }
        }
        foreach (var sys in new[] { "SYSTEM.BMP", "SFRAME_H.BMP", "SFRAME_V.BMP", "TELOP1.BMP", "TELOP2.BMP", "TELOP3.BMP", "TITLE.BMP", "GAMEOVER.BMP" })
            Need("BMP/" + sys, "system");
        foreach (var s in new[] { "CLICK.WAV", "CANCEL.WAV", "BUZZER.WAV", "DOWN.WAV", "LEVELUP.WAV" }) Need("EFS/" + s, "system");
        // sizes of the system graphics
        foreach (var sys in new[] { "SYSTEM.BMP", "SFRAME_H.BMP", "SFRAME_V.BMP", "TELOP1.BMP", "TITLE.BMP", "GAMEOVER.BMP" })
            if (G.Find("BMP/" + sys) is { } p) { try { var bm = IndexedBitmap.Load(p); W($"{sys}: {bm.Width}x{bm.Height}{(p.StartsWith(root) ? "" : " (RTP)")}"); } catch (Exception e) { W($"{sys}: {e.Message}"); } }
        Flush();
    }

    static void Check(Command c, string at, Database db, HashSet<int> mapIndices, SortedSet<int> usedUnits, SortedSet<int> usedClasses)
    {
        var p = c.Payload;
        switch (c.Op)
        {
            case Op.FaceGraphic:
                H("face pos/flip", $"{p[0]}/{p[1]}");
                if (c.U16(2) > 0) Need($"BMP/FACE_{c.U16(2):000}.BMP", at);
                break;
            case Op.SwitchOperation: H("switch op on", c.U16(0)); if (c.U16(2) >= 500) I($"{at}: switch {c.U16(2)}"); break;
            case Op.ItemManagement: H("item op add", c.U16(0)); if (c.U16(2) >= 100) I($"{at}: item {c.U16(2)}"); H("item op count", c.U16(4)); break;
            case Op.GoldManagement: H("gold op add", c.U16(0)); break;
            case Op.ScreenDisplay: H("screen display color/effect", $"{p[0]}/{p[1]}"); if (p[1] > 6 || p[0] > 1) I($"{at}: screen display {p[0]}/{p[1]}"); break;
            case Op.EraseScreen: H("erase screen color/effect", $"{p[0]}/{p[1]}"); if (p[1] > 6 || p[0] > 1) I($"{at}: erase screen {p[0]}/{p[1]}"); break;
            case Op.PictureDisplay:
                H("picture method/pos/transparent", $"{p[0]}/{p[1]}/{p[2]}");
                if (p[0] > 5 || p[1] is < 1 or > 9) I($"{at}: picture method {p[0]} pos {p[1]}");
                Need("BMP/" + c.Str(6), at);
                break;
            case Op.ErasePicture: H("erase picture method", c.U16(0)); if (c.U16(0) > 5) I($"{at}: erase picture {c.U16(0)}"); break;
            case Op.ScrollScreen: H("scroll", "yes"); break;
            case Op.FlashTintScreen: H("flash mode", c.U16(0)); if (c.U16(0) > 5) I($"{at}: flash mode {c.U16(0)}"); break;
            case Op.ShakeScreen: H("shake", c.U16(0)); if (c.U16(0) > 2) I($"{at}: shake {c.U16(0)}"); break;
            case Op.ChangeAllyUnit: H("party add", c.U16(0)); if (c.U16(2) >= 250) I($"{at}: party unit {c.U16(2)}"); else usedUnits.Add(c.U16(2)); break;
            case Op.UnitClassChange: if (c.U16(0) >= 250 || c.U16(2) >= 150) I($"{at}: class change {c.U16(0)} -> {c.U16(2)}"); else usedClasses.Add(c.U16(2)); break;
            case Op.UnitEquipmentChange:
                if (c.U16(0) >= 250 || c.U16(2) >= 100) I($"{at}: equipment change {c.U16(0)} -> {c.U16(2)}");
                else H("equip change item type", db.Items[c.U16(2)].Type);
                break;
            case Op.MoveUnit:
                foreach (var st in p.Skip(4).Take(c.U16(2))) { H("move step", st); if (st > 7) I($"{at}: move step {st}"); }
                if (c.U16(0) >= 250) I($"{at}: move unit {c.U16(0)}");
                break;
            case Op.Bgm: if (c.Str(0) is { Length: > 0 } b && b != "(OFF)") Need("BGM/" + b, at); break;
            case Op.SoundEffect: if (c.Str(0) is { Length: > 0 } se) Need("EFS/" + se, at); break;
            case Op.VariableOperation:
                H("var op source/operation", $"{c.U16(2)}/{c.U16(10)}");
                H("variable index used (/50)", c.U16(0) / 50 * 50);
                if (c.U16(0) >= 500 || c.U16(2) > 2 || c.U16(10) > 4) I($"{at}: variable op var {c.U16(0)} src {c.U16(2)} op {c.U16(10)}");
                if (c.U16(2) == 1) H("variable index used (/50)", c.U16(6) / 50 * 50);
                break;
            case Op.Wait: H("wait (/10)", c.U16(0) / 10 * 10); break;
            case Op.SelectMap: if (!mapIndices.Contains(c.U16(0))) I($"{at}: select map {c.U16(0)} (SMAP_{c.U16(0) + 1:000}) missing"); break;
            case Op.MapComplete: H("map complete win", c.U16(0)); break;
        }
        if (c.Payload.Length > 0 && c.Op is Op.ExecuteMovement or Op.ButtonInput or Op.EndEvent or Op.GameOver or Op.ReturnToTitle)
            I($"{at}: {c.Op} has payload {Convert.ToHexString(c.Payload)}");
    }

    static string WavFmt(string f)
    {
        try
        {
            var b = File.ReadAllBytes(f);
            if (b.Length < 12 || b[0] != 'R' || b[8] != 'W') return "not-riff";
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int len = Bin.I32(b, pos + 4);
                if (id == "fmt ") return $"{Bin.U16(b, pos + 8)}/{Bin.U16(b, pos + 10)}ch/{Bin.U16(b, pos + 22)}bit/{Bin.I32(b, pos + 12)}";
                pos += 8 + len + (len & 1);
            }
            return "no-fmt";
        }
        catch (Exception e) { return "error " + e.Message; }
    }

    static void Flush()
    {
        W("");
        W("---- histograms ----");
        foreach (var (k, d) in Hist.OrderBy(k => k.Key)) W($"{k}: " + string.Join("  ", d.Select(kv => $"{kv.Key}×{kv.Value}")));
        W("");
        W($"---- missing files ({Missing.Count}) ----");
        foreach (var (f, w) in Missing) W($"{f}  <- {string.Join("; ", w)}");
        W("");
        W($"---- issues ({Issues.Count}) ----");
        foreach (var i in Issues) W(i);
    }
}
