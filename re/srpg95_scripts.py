"""Event scripts as nested, cross-referenced trees (built on srpg95_data).

srpg95_data.parse_smap() gives each event script as a flat command list
with nesting depths. This module turns that into the form an engine wants:

  * message + its continuation lines become one node with `lines`
  * conditional_branch nodes get a `then` body
  * character_choice nodes get one body per option in `cases`
  * block markers (end_block / end_branch / choice_case / end_choice) and
    end_of_script disappear into the structure
  * every ID gets a readable `*_name` next to it (unit, item, class, switch,
    variable, map, face file)
  * every referenced asset file is checked against the game folder and the
    RTP folder

Usage:
    python srpg95_scripts.py <game_dir> <out_dir> [<rtp_dir>]
writes <out_dir>/scripts/smap_nnn.json + .txt and <out_dir>/scripts/report.txt
"""
import json
import os
import sys

import srpg95_data as sd

STRUCTURAL = {'c7', 'ca', 'cc', 'cd', '00'}
WINDOW_NAMES = ['top', 'centre', 'bottom']
PICTURE_POSITIONS = {7: 'upper_left', 8: 'above', 9: 'upper_right', 4: 'left', 5: 'centre',
                     6: 'right', 1: 'lower_left', 2: 'below', 3: 'lower_right'}


# --------------------------------------------------------------------------
# Tree building
# --------------------------------------------------------------------------

def _node(c):
    n = {'cmd': c['name'] or f"op_{c['op']}"}
    n.update({k: v for k, v in c.items() if k not in ('op', 'name', 'depth', 'raw')})
    if c['name'] is None:
        n['raw'] = c['raw']
    return n


def build_tree(commands):
    """Flat command list -> (nested node list, list of structural problems).

    Grammar (verified on every intact script):
      2A(d) body(d+1) C7(d+1) CA(d)
      2D(d) [CC(d) body(d+1) C7(d+1)]* CD(d)
    """
    problems = []
    pos = 0

    def parse_block(depth):
        nonlocal pos
        out = []
        while pos < len(commands):
            c = commands[pos]
            op = c['op']
            if c['depth'] != depth and op not in ('ca', 'cc', 'cd'):
                problems.append(f'command {pos} ({op}) at depth {c["depth"]}, expected {depth}')
            if op in ('c7', 'ca', 'cc', 'cd'):
                return out                      # caller consumes the marker
            pos += 1
            if op == '00':
                continue
            if op == 'c9':
                if out and out[-1]['cmd'] == 'message':
                    out[-1]['lines'].append(c.get('text', ''))
                else:
                    problems.append(f'command {pos - 1}: message_line without message')
                    out.append({'cmd': 'message', 'window': c.get('window'), 'lines': [c.get('text', '')]})
                continue
            n = _node(c)
            if op == '01':
                n = {'cmd': 'message', 'window': c.get('window'), 'lines': [c.get('text', '')]}
            elif op == '2a':
                n['then'] = parse_block(depth + 1)
                expect('c7', depth + 1)
                expect('ca', depth)
            elif op == '2d':
                n['cases'] = []
                while pos < len(commands) and commands[pos]['op'] == 'cc':
                    pos += 1
                    n['cases'].append(parse_block(depth + 1))
                    expect('c7', depth + 1)
                expect('cd', depth)
                if len(n['cases']) != len(n.get('options', [])):
                    problems.append(f'choice with {len(n.get("options", []))} options but {len(n["cases"])} cases')
            out.append(n)
        return out

    def expect(op, depth):
        nonlocal pos
        if pos < len(commands) and commands[pos]['op'] == op and commands[pos]['depth'] == depth:
            pos += 1
        else:
            got = commands[pos]['op'] if pos < len(commands) else 'end'
            problems.append(f'expected {op} at depth {depth} at command {pos}, got {got}')

    tree = parse_block(0)
    if pos < len(commands):
        problems.append(f'{len(commands) - pos} commands left after top level (stray {commands[pos]["op"]})')
    return tree, problems


# --------------------------------------------------------------------------
# Cross-references
# --------------------------------------------------------------------------

class GameDB:
    """Name tables and asset folders needed to annotate scripts."""

    def __init__(self, game_dir, rtp_dir=None):
        def load(name, fn):
            p = sd._find(game_dir, name)
            return fn(open(p, 'rb').read()) if p else []
        self.units = load('UNIT.DAT', sd.parse_unit)
        self.items = load('ITEM.DAT', sd.parse_item)
        self.classes = load('CLASS.DAT', sd.parse_class)
        self.switches = load('SWNAME.DAT', sd.parse_name_table)
        self.variables = load('VARNAME.DAT', sd.parse_name_table)
        self.maps = load('MAPNAME.DAT', sd.parse_name_table)
        md = sd._find(game_dir, 'MAP')
        self.map_files = {int(f[5:8]) - 1 for f in os.listdir(md)
                          if f.upper().startswith('SMAP_') and f[5:8].isdigit()} if md else set()
        self.asset_dirs = {}
        for sub in ('BGM', 'EFS', 'BMP'):
            names = set()
            for root in (game_dir, rtp_dir):
                d = root and sd._find(root, sub)
                if d:
                    names |= {f.upper() for f in os.listdir(d)}
            self.asset_dirs[sub] = names

    @staticmethod
    def _get(table, i, key='name'):
        if not isinstance(i, int) or not 0 <= i < len(table):
            return None
        v = table[i]
        return v.get(key) if isinstance(v, dict) else v

    def unit(self, i): return self._get(self.units, i)
    def item(self, i): return self._get(self.items, i)
    def cls(self, i): return self._get(self.classes, i)
    def switch(self, i): return self._get(self.switches, i)
    def variable(self, i): return self._get(self.variables, i)
    def map(self, i): return self._get(self.maps, i)

    def asset_exists(self, sub, name):
        return name.upper() in self.asset_dirs.get(sub, set())


def link(nodes, db, report, where):
    """Add *_name fields and check assets, recursively. Appends problems to report."""
    for n in nodes:
        cmd = n['cmd']

        def name(field, fn, label=None, check_empty=True):
            if field in n:
                v = fn(n[field])
                n[(label or field) + '_name'] = v
                if v is None:
                    report.append(f'{where}: {cmd}.{field}={n[field]} out of range')
                elif check_empty and v == '':
                    report.append(f'{where}: {cmd}.{field}={n[field]} refers to an empty record')

        if cmd == 'message' and isinstance(n.get('window'), int) and 0 <= n['window'] < 3:
            n['window_name'] = WINDOW_NAMES[n['window']]
        elif cmd == 'face_graphic':
            n['face_file'] = f"FACE_{n['face']:03d}.BMP"
            if n['face'] and not db.asset_exists('BMP', n['face_file']):
                report.append(f'{where}: missing {n["face_file"]}')
        elif cmd == 'switch_operation':
            name('switch', db.switch, check_empty=False)
        elif cmd == 'item_management':
            name('item', db.item)
        elif cmd in ('change_ally_unit', 'move_unit'):
            name('unit', db.unit)
        elif cmd == 'unit_class_change':
            name('unit', db.unit)
            name('class_id', db.cls, 'class')
        elif cmd == 'unit_equipment_change':
            name('unit', db.unit)
            name('item', db.item)
        elif cmd == 'variable_operation':
            name('variable', db.variable, check_empty=False)
            n['source_name'] = ['constant', 'variable', 'random'][n['source']] if n.get('source') in (0, 1, 2) else None
            n['operation_name'] = (['set', 'add', 'subtract', 'multiply', 'divide'][n['operation']]
                                   if n.get('operation') in range(5) else None)
            if n.get('source') == 1:
                name('source_variable', db.variable, check_empty=False)
        elif cmd == 'conditional_branch':
            for f, fn in (('test_switch', db.switch), ('test_variable', db.variable),
                          ('test_item', db.item), ('test_unit', db.unit)):
                if f in n:
                    name(f, fn, check_empty=f in ('test_item', 'test_unit'))
        elif cmd == 'select_map':
            name('map', db.map, check_empty=False)
            n['map_file'] = f"SMAP_{n['map'] + 1:03d}.DAT"
            if n['map'] not in db.map_files:
                report.append(f'{where}: select_map to missing {n["map_file"]}')
        elif cmd == 'picture_display':
            n['position_name'] = PICTURE_POSITIONS.get(n.get('position'))
            if not db.asset_exists('BMP', n.get('file', '')):
                report.append(f'{where}: missing picture {n.get("file")}')
        elif cmd == 'bgm':
            f = n.get('file', '')
            if f != '(OFF)' and not db.asset_exists('BGM', f):
                report.append(f'{where}: missing BGM {f}')
        elif cmd == 'sound_effect':
            if not db.asset_exists('EFS', n.get('file', '')):
                report.append(f'{where}: missing sound {n.get("file")}')
        for key in ('then',):
            if key in n:
                link(n[key], db, report, where)
        for body in n.get('cases', []):
            link(body, db, report, where)


# --------------------------------------------------------------------------
# Readable listing
# --------------------------------------------------------------------------

def _describe(n):
    cmd = n['cmd']
    skip = {'cmd', 'then', 'cases', 'lines', 'window', 'options'}
    parts = [f'{k}={v}' for k, v in n.items() if k not in skip and not k.endswith('_name')
             and not (cmd == 'conditional_branch' and k not in ('cond',) and not k.startswith('test_'))]
    names = [f'{k[:-5]}「{v}」' for k, v in n.items() if k.endswith('_name') and v not in (None, '')]
    return f'{cmd} ' + ' '.join(parts + names)


def render(nodes, indent=0):
    pad = '    ' * indent
    out = []
    for n in nodes:
        if n['cmd'] == 'message':
            out.append(f'{pad}message[{n.get("window_name", n.get("window"))}] ' + ' / '.join(n['lines']))
        elif n['cmd'] == 'character_choice':
            out.append(f'{pad}character_choice')
            for opt, body in zip(n.get('options', []), n.get('cases', [])):
                out.append(f'{pad}  case 「{opt}」:')
                out += render(body, indent + 1)
        elif n['cmd'] == 'conditional_branch':
            out.append(f'{pad}if ' + _describe(n)[len('conditional_branch '):] + ':')
            out += render(n['then'], indent + 1)
        else:
            out.append(pad + _describe(n))
    return out


# --------------------------------------------------------------------------
# Whole map
# --------------------------------------------------------------------------

def map_scripts(smap_json, db, map_label):
    """Return {'objects': [...], 'scripts': {...}} with trees, plus report lines."""
    report = []
    scripts = {}
    for fname, ev in smap_json['events'].items():
        where = f'{map_label}/{fname}'
        entry = {'comment': ev.get('comment', '')}
        if 'error' in ev:
            entry['error'] = ev['error']
            report.append(f'{where}: unreadable ({ev["error"]})')
        else:
            tree, problems = build_tree(ev['commands'])
            for p in problems:
                report.append(f'{where}: {p}')
            link(tree, db, report, where)
            entry['tree'] = tree
            if problems:
                entry['structure_problems'] = problems
        scripts[fname] = entry
    objects = []
    for o in smap_json['objects']:
        if not o['in_use']:
            continue
        d = {k: o[k] for k in ('index', 'x', 'y', 'type', 'unit', 'switch_condition', 'leader', 'guest',
                               'reward_item', 'reward_gold', 'unattackable')}
        d['type_name'] = ['enemy', 'ally', 'neutral', 'other'][o['type']] if 0 <= o['type'] < 4 else None
        if o['type'] < 3:
            d['unit_name'] = db.unit(o['unit'])
        d['move_patterns'] = o['move_patterns']
        d['events'] = []
        for i, sl in enumerate(o['event_slots'], 1):
            fname = f'EV{o["index"]:04d}{i:02d}'
            if sl['condition'] == 0 and fname not in scripts:
                continue
            e = dict(slot=i, script=fname if fname in scripts else None, **sl)
            if sl['target_unit'] >= 0:
                e['target_unit_name'] = db.unit(sl['target_unit'])
            if sl['condition'] == 1:
                e['switch_name'] = db.switch(sl['switch'])
            if sl['condition'] != 0 and fname not in scripts:
                report.append(f'{map_label}: object {o["index"]} slot {i} has a condition but no script')
            d['events'].append(e)
        objects.append(d)
    return {'name': smap_json['name'], 'objects': objects, 'scripts': scripts}, report


def dump_scripts(game_dir, out_dir, rtp_dir=None):
    db = GameDB(game_dir, rtp_dir)
    out = os.path.join(out_dir, 'scripts')
    os.makedirs(out, exist_ok=True)
    map_dir = sd._find(game_dir, 'MAP')
    full_report = []
    for f in sorted(os.listdir(map_dir)):
        if not (f.upper().startswith('SMAP_') and f.upper().endswith('.DAT')):
            continue
        base = os.path.splitext(f)[0].lower()
        smap, _ = sd.parse_smap(open(os.path.join(map_dir, f), 'rb').read())
        doc, report = map_scripts(smap, db, base)
        full_report += report
        with open(os.path.join(out, base + '.json'), 'w', encoding='utf-8') as fh:
            json.dump(doc, fh, ensure_ascii=False, indent=1)
        lines = [f'# {base}  {doc["name"]}', '']
        for o in doc['objects']:
            if not o['events']:
                continue
            who = o.get('unit_name') or ''
            lines.append(f'## object {o["index"]} {o["type_name"]} {who} at ({o["x"]},{o["y"]})')
            for e in o['events']:
                lines.append(f'  slot {e["slot"]}: {e["condition_name"]}'
                             + (f' target「{e.get("target_unit_name")}」' if e.get('target_unit_name') else '')
                             + (f' switch「{e.get("switch_name")}」' if e.get('switch_name') else '')
                             + (f' {e["turn_side"]} turn {e["turn_number"]}' if 'turn_side' in e else '')
                             + f' -> {e["script"]}')
        lines.append('')
        for fname, s in doc['scripts'].items():
            lines.append(f'=== {fname}' + (f'  ({s["comment"]})' if s['comment'] else ''))
            if 'error' in s:
                lines.append(f'  !! unreadable: {s["error"]}')
            else:
                lines += render(s['tree'], 1)
            lines.append('')
        with open(os.path.join(out, base + '.txt'), 'w', encoding='utf-8') as fh:
            fh.write('\n'.join(lines))
    with open(os.path.join(out, 'report.txt'), 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(full_report) + '\n')
    return full_report


if __name__ == '__main__':
    if len(sys.argv) not in (3, 4):
        sys.exit(__doc__)
    rep = dump_scripts(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) == 4 else None)
    print(f'{len(rep)} report lines -> {os.path.join(sys.argv[2], "scripts", "report.txt")}')
