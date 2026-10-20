"""Make maps trivially winnable in a TEST COPY of the game (never the original).

    python re/tools/wintool.py weaken  [GAME_COPY_DIR]   # enemies: 1 HP, no attack/defense
    python re/tools/wintool.py restore [GAME_COPY_DIR]   # put the original UNIT.DAT back
    python re/tools/wintool.py status  [GAME_COPY_DIR]

Default GAME_COPY_DIR: "えるまに - Save testplay" next to the original game folder.

How it works: UNIT.DAT holds every unit's starting stats. Enemies are created from it
when a map starts, so lowering enemy-only units' stats makes every fight one hit, while
all map events (story switches, variables, joins) still run normally.

Units are left untouched if they ever appear as an ally, are in a map's character list,
or join through a "change ally unit" command, and neutral (NPC) units too, so the party
and story characters keep their real stats. Your party's current stats live in the save
files and are not affected either.

Apply it at a camp (before the next map starts); a map already in progress keeps the
stats it loaded with.
"""
import os
import shutil
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import srpg95_data as sd  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ORIGINAL = os.path.join(REPO, 'えるまに')
DEFAULT_COPY = os.path.join(REPO, 'えるまに - Save testplay')
BACKUP_DIR = '_wintool_backup'
UNIT_SIZE = 480


def find(folder, name):
    return sd._find(folder, name)


def protected_units(folder):
    """Units that ever fight for the player or join the party, plus neutral NPCs."""
    allies, enemies = set(), set()
    map_dir = find(folder, 'MAP')
    for f in sorted(os.listdir(map_dir)):
        if not (f.upper().startswith('SMAP_') and f.upper().endswith('.DAT')):
            continue
        m, _ = sd.parse_smap(open(os.path.join(map_dir, f), 'rb').read())
        allies.update(u for u in m['character_list'][:m['character_list_count']] if u >= 0)
        for o in m['objects']:
            if not o['in_use'] or o['type'] == 3:
                continue
            (enemies if o['type'] == 0 else allies).add(o['unit'])
        for ev in m['events'].values():
            for c in ev.get('commands', []):
                if c['op'] == '16' and c.get('add') == 1:      # change ally unit: join
                    allies.add(c['unit'])
    return allies, enemies - allies


def weaken(folder):
    path = find(folder, 'UNIT.DAT')
    backup = os.path.join(folder, BACKUP_DIR)
    os.makedirs(backup, exist_ok=True)
    saved = os.path.join(backup, 'UNIT.DAT')
    if not os.path.exists(saved):
        shutil.copy2(path, saved)
        print(f'backed up original UNIT.DAT -> {saved}')
    # always start from the original so running twice is harmless
    data = bytearray(open(saved, 'rb').read())
    allies, targets = protected_units(folder)
    units = sd.parse_unit(bytes(data))
    for u in sorted(targets):
        o = u * UNIT_SIZE
        # base stats at 0x30: HP, MP, Str, Mag, Skl, Sta
        struct.pack_into('<6i', data, o + 0x30, 1, 0, 0, 0, 0, 0)
        struct.pack_into('<2i', data, o + 0x64, -1, -1)            # armor, accessory: none
    open(path, 'wb').write(data)
    print(f'weakened {len(targets)} enemy-only units; left {len(allies)} ally/story units untouched')
    sample = ', '.join(units[u]['name'] for u in sorted(targets)[:8])
    print(f'e.g. {sample} ...')


def restore(folder):
    saved = os.path.join(folder, BACKUP_DIR, 'UNIT.DAT')
    if not os.path.exists(saved):
        print('nothing to restore (no backup found)')
        return
    shutil.copy2(saved, find(folder, 'UNIT.DAT'))
    print('original UNIT.DAT restored')


def status(folder):
    saved = os.path.join(folder, BACKUP_DIR, 'UNIT.DAT')
    cur = open(find(folder, 'UNIT.DAT'), 'rb').read()
    if not os.path.exists(saved):
        print('not weakened (no backup present)')
    elif open(saved, 'rb').read() == cur:
        print('backup present, UNIT.DAT is the original')
    else:
        print('weakened (original kept in', os.path.join(folder, BACKUP_DIR), ')')


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ('weaken', 'restore', 'status'):
        sys.exit(__doc__)
    folder = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_COPY
    if os.path.normcase(folder) == os.path.normcase(os.path.abspath(ORIGINAL)):
        sys.exit('refusing to modify the original game folder; use a copy')
    if not os.path.isdir(folder) or not find(folder, 'UNIT.DAT'):
        sys.exit(f'not a game folder: {folder}')
    {'weaken': weaken, 'restore': restore, 'status': status}[sys.argv[1]](folder)


if __name__ == '__main__':
    main()
