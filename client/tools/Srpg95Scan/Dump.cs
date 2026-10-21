using System.Text;
using Srpg95.Data;

// dump MAPDIR SMAPNUM...: a map's header, start positions, character list, objects and the stored tiles beyond its size
static class Dump
{
    public static void Script(string root, int number, string name, StringBuilder o)
    {
        var m = MapData.Load(Path.Combine(root, "MAP", $"SMAP_{number:000}.DAT"));
        var objs = name == "*" ? m.Scripts.Keys.Order().ToList() : new List<string> { name };
        foreach (var n in objs)
        {
            o.AppendLine($"== SMAP_{number:000} {n}");
            if (m.Scripts.TryGetValue(n, out var sc)) Tree(sc.Tree, 1, o);
        }
        foreach (var ob in m.Objects.Where(ob => ob.InUse))
            o.AppendLine($"  obj {ob.Index} {ob.Type} ({ob.X},{ob.Y}) unit {ob.Unit} sw {ob.SwitchCondition} slots " +
                string.Join(" ", ob.Slots.Select((s, i) => s.Condition == 0 ? "" : $"[{i + 1}: cond {s.Condition} sw {s.Switch} turn {s.Turn} target {s.TargetUnit}]")));
    }

    static void Tree(List<ScriptNode> nodes, int depth, StringBuilder o)
    {
        string pad = new(' ', depth * 2);
        foreach (var n in nodes)
            switch (n)
            {
                case MessageNode mn: o.AppendLine($"{pad}MSG[{mn.Window}] {string.Join(" / ", mn.Lines)}"); break;
                case BranchNode b:
                    var c = b.Condition;
                    o.AppendLine($"{pad}IF type {c.U16(0)} sw {c.U16(2)} turn {c.U16(4)} item {c.U16(6)} unit {c.U16(8)} var {c.U16(10)} states {c.Payload[12]}/{c.Payload[13]}/{c.Payload[14]}/{c.Payload[15]} cmp {c.I16(16)} op {c.U16(18)}");
                    Tree(b.Then, depth + 1, o);
                    break;
                case ChoiceNode ch:
                    o.AppendLine($"{pad}CHOICE {string.Join(" | ", ch.Options)}");
                    for (int i = 0; i < ch.Cases.Count; i++) { o.AppendLine($"{pad} case {i}:"); Tree(ch.Cases[i], depth + 2, o); }
                    break;
                case CommandNode { Command: var cmd }:
                    string args = cmd.Op switch
                    {
                        Op.SwitchOperation => $"switch {cmd.U16(2)} = {cmd.U16(0)}",
                        Op.VariableOperation => $"var {cmd.U16(0)} src {cmd.U16(2)} const {cmd.I16(4)} var {cmd.U16(6)} rnd {cmd.U16(8)} op {cmd.U16(10)}",
                        Op.Bgm or Op.SoundEffect => cmd.Str(0),
                        Op.PictureDisplay => $"{cmd.Str(6)} method {cmd.Payload[0]} pos {cmd.Payload[1]}",
                        Op.SelectMap or Op.MapComplete or Op.Wait or Op.ErasePicture or Op.ShakeScreen => $"{cmd.U16(0)}",
                        Op.ScreenDisplay or Op.EraseScreen => $"{cmd.Payload[0]}/{cmd.Payload[1]}",
                        Op.FaceGraphic => $"face {cmd.U16(2)}",
                        Op.ChangeAllyUnit or Op.UnitClassChange or Op.UnitEquipmentChange or Op.ItemManagement => $"{cmd.U16(0)} {cmd.U16(2)} {(cmd.Payload.Length > 4 ? cmd.U16(4) : 0)}",
                        Op.MoveUnit => $"unit {cmd.U16(0)} steps {string.Join(",", cmd.Payload.Skip(4).Take(cmd.U16(2)))}",
                        Op.ScrollScreen => $"({cmd.U16(0)},{cmd.U16(2)})",
                        _ => Convert.ToHexString(cmd.Payload),
                    };
                    o.AppendLine($"{pad}{cmd.Op} {args}");
                    break;
            }
    }

    public static void Map(string root, int number, StringBuilder o)
    {
        var path = Path.Combine(root, "MAP", $"SMAP_{number:000}.DAT");
        var raw = File.ReadAllBytes(path);
        var m = MapData.Parse(raw);
        o.AppendLine($"== {root} SMAP_{number:000} {m.Name} {m.Width}x{m.Height} tileset {m.Tileset} win {m.WinCondition} camp {m.CampBeforeBattle} disableParty {m.DisablePartyChanges}");
        o.AppendLine("  start: " + string.Join(" ", m.StartPositions.Select(p => $"({p.X},{p.Y})")));
        o.AppendLine("  chars: " + string.Join(" ", m.CharacterList));
        int nonzero = 0, outside = 0;
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
            {
                int v = Bin.U16(raw, 0x4b0 + (y * 40 + x) * 2);
                if (x >= m.Width || y >= m.Height) { outside++; if (v != 0) nonzero++; }
            }
        o.AppendLine($"  stored tiles outside the map: {outside}, non-zero {nonzero}");
        foreach (var ob in m.Objects.Where(ob => ob.InUse))
        {
            bool off = ob.X >= m.Width || ob.Y >= m.Height;
            o.AppendLine($"  obj {ob.Index,2} {ob.Type,-7} ({ob.X},{ob.Y}){(off ? " OFF" : "")} unit {ob.Unit} sw {ob.SwitchCondition} leader {ob.Leader} guest {ob.Guest} " +
                $"move {string.Join("/", ob.MovePatterns.Select(p => $"{p.Switch}:{p.Action}"))} slots {string.Join(",", ob.Slots.Where(s => s.Condition != 0).Select(s => $"{s.Condition}"))}");
        }
    }
}
