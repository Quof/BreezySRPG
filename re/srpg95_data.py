"""Parsers for Sim RPG Maker 95 (シミュレーションRPGツクール95) game data files.

One function per file format. Every parser takes raw ``bytes`` and returns
plain Python data (dicts/lists/ints/strs) ready for ``json.dump``.

Conventions (see FORMAT_NOTES.md for the per-field evidence):
  * All integers are little-endian. Most fields are signed int32.
  * Text is Shift-JIS; we decode with Python's ``cp932`` codec (Microsoft's
    Shift-JIS superset, which is what Win95-era tools actually wrote).
  * String fields are fixed-size buffers. The game reads up to the first NUL;
    bytes after the NUL are stale leftovers from earlier edits and are dropped.
  * Any byte range of a record that no named field covers is emitted as
    ``unknown_XXX`` (hex offset within the record) *if it is non-zero*, so no
    information is lost. An absent ``unknown_XXX`` key means "all zero".

Usage:
    python srpg95_data.py <game_dir> <out_dir>
"""
import hashlib
import json
import os
import struct
import sys

ENCODING = 'cp932'

# --------------------------------------------------------------------------
# Low-level helpers
# --------------------------------------------------------------------------


def _cstr(buf, off, size):
    raw = buf[off:off + size]
    nul = raw.find(b'\0')
    if nul >= 0:
        raw = raw[:nul]
    return raw.decode(ENCODING, errors='replace')


def _i32(buf, off):
    return struct.unpack_from('<i', buf, off)[0]


def _i32s(buf, off, n):
    return list(struct.unpack_from(f'<{n}i', buf, off))


def _u8s(buf, off, n):
    return list(buf[off:off + n])


def _grid21(buf, off):
    """21x21 boolean grid stored as 21 int32 rows, top row first (row 10 =
    centre). Columns are bits 31..11 from left to right (bit 21 = centre
    column, so a cell dx squares right of centre is bit 21 - dx). Returns 21
    strings of '#'/'.' as seen in the editor, caster at the centre."""
    rows = []
    for r in range(21):
        v = struct.unpack_from('<I', buf, off + r * 4)[0]
        rows.append(''.join('#' if (v >> (31 - c)) & 1 else '.' for c in range(21)))
    return rows


# Field spec: (offset, name, kind, arg)
#   kind 'str'  -> arg = buffer size in bytes
#   kind 'i32'  -> single int32 (arg ignored)
#   kind 'i32[]'-> arg = element count
#   kind 'i16', 'i16[]' -> as above, int16
#   kind 'u8[]' -> arg = element count
#   kind 'grid21' -> 84-byte 21x21 grid (arg ignored)
def _field_size(kind, arg):
    return {'str': arg, 'i32': 4, 'i32[]': 4 * (arg or 0), 'i16': 2,
            'i16[]': 2 * (arg or 0), 'u8[]': arg or 0, 'grid21': 84}[kind]


def _parse_record(rec, spec):
    out = {}
    covered = bytearray(len(rec))
    for off, name, kind, arg in spec:
        size = _field_size(kind, arg)
        if kind == 'str':
            out[name] = _cstr(rec, off, size)
        elif kind == 'i32':
            out[name] = _i32(rec, off)
        elif kind == 'i32[]':
            out[name] = _i32s(rec, off, arg)
        elif kind == 'i16':
            out[name] = struct.unpack_from('<h', rec, off)[0]
        elif kind == 'i16[]':
            out[name] = list(struct.unpack_from(f'<{arg}h', rec, off))
        elif kind == 'u8[]':
            out[name] = _u8s(rec, off, arg)
        elif kind == 'grid21':
            out[name] = _grid21(rec, off)
        for i in range(off, off + size):
            covered[i] = 1
    out.update(_unknowns(rec, covered))
    return out


def _unknowns(rec, covered):
    """Emit every non-zero uncovered chunk. Aligned 4-byte chunks become an
    int32; anything else is emitted byte-by-byte."""
    out = {}
    i, n = 0, len(rec)
    while i < n:
        if covered[i]:
            i += 1
            continue
        if i % 4 == 0 and i + 4 <= n and not any(covered[i:i + 4]):
            v = _i32(rec, i)
            if v:
                out[f'unknown_{i:03x}'] = v
            i += 4
        else:
            if rec[i]:
                out[f'unknown_{i:03x}_u8'] = rec[i]
            i += 1
    return out


def _parse_table(data, rec_size, count, spec):
    if len(data) != rec_size * count:
        raise ValueError(f'expected {rec_size * count} bytes, got {len(data)}')
    return [dict(index=i, **_parse_record(data[i * rec_size:(i + 1) * rec_size], spec))
            for i in range(count)]


# --------------------------------------------------------------------------
# Record layouts
# --------------------------------------------------------------------------

UNIT_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x20, 'face', 'i32', None),          # FACE_%03d.BMP
    (0x24, 'class_id', 'i32', None),      # CLASS.DAT index
    (0x28, 'level', 'i32', None),
    (0x2c, 'exp', 'i32', None),
    (0x30, 'base_stats', 'i32[]', 6),     # HP, MP, Str, Mag, Skl, Sta
    (0x48, 'growth_pct', 'i32[]', 6),     # same order
    (0x60, 'weapon', 'i32', None),        # ITEM.DAT index, -1 = none
    (0x64, 'armor', 'i32', None),
    (0x68, 'accessory', 'i32', None),
    (0x6c, 'death_message', 'str', 0x1e0 - 0x6c),
]

CLASS_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x20, 'movement', 'i32', None),
    (0x24, 'move_type', 'i32', None),               # 0 walk, 1 fly, 2 special
    (0x28, 'terrain_move_override', 'i32[]', 70),   # per GEOLOGY id, -1 = default
    (0x140, 'unit_graphic', 'i32', None),           # UNIT_%03d.BMP / CAxx_%03d.BMP
    (0x144, 'anim_weapon_a', 'i32', None),          # ANIME.DAT index
    (0x148, 'anim_weapon_b', 'i32', None),
    (0x14c, 'anim_weapon_c', 'i32', None),
    (0x150, 'anim_no_weapon', 'i32', None),
    (0x154, 'anim_magic_enemy', 'i32', None),
    (0x158, 'anim_magic_ally', 'i32', None),
    (0x1a4, 'init_stats', 'i32[]', 4),              # Str, Mag, Skl, Sta
    (0x1b4, 'skill_levels', 'i32[]', 20),           # 0 = end of list
    (0x204, 'skill_ids', 'i32[]', 20),              # MAGIC.DAT index
    (0x254, 'weakness_attr', 'u8[]', 6),            # attributes A..F
    (0x25c, 'resist', 'u8[]', 4),                   # poison, sleep, atk down, def down
]

# The editor's weapon Range dropdown, in order (confirmed by screenshot).
WEAPON_RANGES = [(1, 1), (1, 2), (1, 3), (2, 2), (2, 3), (2, 4), (2, 5), (3, 10)]

ITEM_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x20, 'type', 'i32', None),   # 0 event,1 weapon,2 armor,3 accessory,4 medicine,5 stat-up,6 class change,7 magic
    (0x24, 'price', 'i32', None),
    (0x28, 'desc', 'str', 0x50),
    (0x78, 'class_usable', 'u8[]', 200),   # first 150 = CLASS.DAT ids; 150..199 always 1
    # weapon
    (0x140, 'wpn_range', 'i32', None),     # index into WEAPON_RANGES
    (0x144, 'wpn_power', 'i32', None),
    (0x148, 'wpn_usability', 'i32', None),
    (0x14c, 'wpn_critical', 'i32', None),
    (0x150, 'wpn_animation', 'i32', None),  # 0=A 1=B 2=C
    (0x154, 'wpn_attack_attr', 'u8[]', 6),
    # armor
    (0x15c, 'arm_defense', 'i32', None),
    (0x160, 'arm_magic_defense', 'i32', None),
    (0x164, 'arm_crit_avoid', 'i32', None),
    (0x168, 'arm_weight', 'i32', None),
    (0x16c, 'arm_defense_attr', 'u8[]', 6),
    # accessory
    (0x174, 'acc_str', 'i32', None),
    (0x178, 'acc_mag', 'i32', None),
    (0x17c, 'acc_skl', 'i32', None),
    (0x180, 'acc_sta', 'i32', None),
    (0x184, 'acc_move', 'i32', None),
    (0x188, 'acc_growth_bonus', 'i32', None),
    (0x25c, 'acc_safe', 'u8[]', 4),          # poison, sleep, atk down, def down
    # medicine
    (0x18c, 'med_hp', 'i32', None),
    (0x190, 'med_mp', 'i32', None),
    (0x194, 'med_def_up', 'i32', None),
    (0x198, 'med_mdef_up', 'i32', None),
    (0x254, 'med_cure', 'u8[]', 4),          # poison, sleep, atk down, def down
    (0x268, 'med_range', 'i32', None),
    # stat-up
    (0x19c, 'statup', 'i32[]', 6),           # HP, MP, Str, Mag, Skl, Sta
    # class change
    (0x1b4, 'class_change', 'i32[]', 40),    # 20 (from, to) pairs, -1 = empty
    (0x264, 'class_change_level', 'i32', None),
    # magic item
    (0x26c, 'magic_spell', 'i32', None),     # MAGIC.DAT index
]

MAGIC_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x20, 'type', 'i32', None),      # 0 damage, 1 degrade status, 2 raise status
    (0x24, 'mp_cost', 'i32', None),
    (0x28, 'desc', 'str', 0x50),
    (0x78, 'damage', 'i32', None),
    (0x7c, 'accuracy', 'i32', None),
    (0x88, 'attack_attr', 'u8[]', 6),
    (0x90, 'animation', 'i32', None),  # ANIME.DAT index = 80 + value
    (0x94, 'exp_value', 'i32', None),
    (0x98, 'degrade_effect', 'i32', None),  # 0 poison, 1 sleep, 2 atk down, 3 def down
    (0x9c, 'target', 'i32', None),          # 0 "Allies/Enemies" (both sides), 1 "Enemies Only"
    (0xa0, 'value', 'i32', None),
    (0xa4, 'relation', 'i32', None),
    (0xa8, 'raise_effect', 'i32', None),    # 0 HP, 1 atk up, 2 def up, 3 cure poison, 4 cleanse, 5 add turn
    (0xe8, 'range_whole_map', 'i32', None),
    (0xec, 'effect_whole_map', 'i32', None),
    (0xf0, 'range_grid', 'grid21', None),
    (0x144, 'effect_grid', 'grid21', None),
]

GEOLOGY_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x20, 'defense_bonus', 'i32', None),
    (0x24, 'hp_recovery', 'i32', None),
    (0x28, 'move_cost', 'i32[]', 3),      # per move type, 0 = impassable
    (0x34, 'battle_background', 'i32', None),
]

ANIME_FRAMES = 90
ANIME_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x24, 'preview_graphic', 'i32', None),   # YYY in CAxx_YYY.BMP used by the editor preview
    (0x898, 'frame_count', 'i32', None),
    (0x89c, 'hit_frame', 'i32', None),
    (0x8a0, 'flash_time', 'i32', None),       # tenths of a second
    (0x8c0, 'hit_sound', 'str', 0x30),
    (0x8f0, 'miss_sound', 'str', 0x30),
    (0x920, 'hit_sound_frame', 'i32', None),    # 0-based (editor shows +1)
    (0x924, 'miss_sound_frame', 'i32', None),   # 0-based (editor shows +1)
]


# --------------------------------------------------------------------------
# Per-format parsers
# --------------------------------------------------------------------------


def parse_name_table(data):
    """MAPNAME.DAT, VARNAME.DAT, SWNAME.DAT: array of 32-byte strings."""
    if len(data) % 32:
        raise ValueError('name table size not a multiple of 32')
    return [_cstr(data, i, 32) for i in range(0, len(data), 32)]


# WORD.DAT index -> Vocabulary Editor label (all four Term tabs, confirmed
# by screenshot). 41-44, 48-51 and 59 hold text but have no box in the
# editor; 0, 77-80 and 90-99 are empty.
WORD_LABELS = {
    # Term 1
    1: 'level', 2: 'hp', 3: 'mp', 4: 'attack', 5: 'attack_speed',
    6: 'defense', 7: 'magic_defense', 8: 'strength', 9: 'magic',
    10: 'skill', 11: 'stamina', 12: 'weapon', 13: 'armor', 14: 'accessory',
    15: 'cmd_end', 16: 'cmd_attack', 17: 'cmd_magic', 18: 'cmd_item',
    19: 'cmd_end_turn', 20: 'cmd_view_map', 21: 'cmd_save', 22: 'cmd_load',
    23: 'cmd_exit', 45: 'yes', 46: 'no', 75: 'terrain_effect', 89: 'equipment',
    # Term 2
    24: 'continue', 25: 'organize_units', 26: 'buy_items', 27: 'sell_items',
    28: 'cost', 29: 'sell_amount', 30: 'owned', 31: 'currency', 32: 'unequip',
    33: 'change_weapon', 34: 'change_armor', 35: 'change_accessory',
    36: 'use_item', 37: 'view_magic', 38: 'unit_slots', 39: 'slot', 40: 'stay',
    47: 'new_game', 81: 'file_1', 82: 'file_2', 83: 'file_3', 84: 'file_4',
    85: 'file_5', 86: 'file_6', 87: 'file_7', 88: 'file_8',
    # Term 3
    52: 'finish_turn', 53: 'select_save_file', 54: 'select_file_to_load',
    55: 'finish_camp_line1', 56: 'finish_camp_line2', 57: 'quit_game',
    58: 'was_used', 60: 'exp_earned', 61: 'gold_earned', 62: 'was_found',
    # Term 4
    63: 'page', 64: 'however', 65: 'point', 66: 'recovered',
    67: 'strength_increased', 68: 'increase', 69: 'decrease',
    70: 'reached_level', 71: 'became', 72: 'promoted_to', 73: 'learned',
    74: 'removed_poison', 76: 'woke_up',
}


def parse_word(data):
    """WORD.DAT: 100 x 32-byte vocabulary strings, with editor labels where known."""
    return [{'index': i, 'label': WORD_LABELS.get(i), 'text': t}
            for i, t in enumerate(parse_name_table(data))]


def parse_unit(data):
    """UNIT.DAT: 250 records x 480 bytes."""
    return _parse_table(data, 480, 250, UNIT_SPEC)


def parse_class(data):
    """CLASS.DAT: 150 records x 640 bytes."""
    return _parse_table(data, 640, 150, CLASS_SPEC)


def parse_item(data):
    """ITEM.DAT: 100 records x 640 bytes."""
    items = _parse_table(data, 640, 100, ITEM_SPEC)
    for it in items:
        if 0 <= it['wpn_range'] < len(WEAPON_RANGES):
            it['wpn_range_min_max'] = list(WEAPON_RANGES[it['wpn_range']])
    return items


def parse_magic(data):
    """MAGIC.DAT: 100 records x 480 bytes."""
    return _parse_table(data, 480, 100, MAGIC_SPEC)


def parse_geology(data):
    """GEOLOGY.DAT (terrain types): 70 records x 60 bytes."""
    return _parse_table(data, 60, 70, GEOLOGY_SPEC)


def parse_anime(data):
    """ANIME.DAT: 160 records x 2400 bytes (0-79 weapon anims, 80-159 spell anims).

    Each record holds 90 frames; each frame has 4 cell layers of
    (x, y, cell) int16, cell -1 = layer unused."""
    rec_size, count = 2400, 160
    if len(data) != rec_size * count:
        raise ValueError(f'expected {rec_size * count} bytes, got {len(data)}')
    out = []
    frame_spec = [(0x28, '_frames', 'u8[]', ANIME_FRAMES * 24)]
    for i in range(count):
        rec = data[i * rec_size:(i + 1) * rec_size]
        d = _parse_record(rec, ANIME_SPEC + frame_spec)
        del d['_frames']
        frames = []
        for f in range(ANIME_FRAMES):
            vals = struct.unpack_from('<12h', rec, 0x28 + f * 24)
            frames.append([list(vals[k:k + 3]) for k in range(0, 12, 3)])
        d['frames'] = frames
        out.append(dict(index=i, kind='weapon' if i < 80 else 'spell', **d))
    return out


def chip_cell(chip):
    """Chip number -> (column, row) in MAPC_xxx.BMP (12 x 32 chips of 32 px).
    The sheet is two 6-column halves (editor tabs A/B): chips 0-191 run through
    the left half row by row, 192-383 through the right half."""
    half, r = divmod(chip, 192)
    return half * 6 + r % 6, r // 6


def parse_mapc(data):
    """MAPC_xxx.DAT: terrain (GEOLOGY id) of each of the 384 chips of
    MAPC_xxx.BMP, indexed by chip number (see chip_cell for the layout)."""
    if len(data) != 384:
        raise ValueError(f'expected 384 bytes, got {len(data)}')
    return {'chip_terrain': list(data)}


SMAP_HEADER_SPEC = [
    (0x00, 'name', 'str', 32),
    (0x20, 'width', 'i32', None),
    (0x24, 'height', 'i32', None),
    (0x28, 'tileset', 'i32', None),       # MAPC_%03d.BMP / .DAT
    (0x2c, 'win_condition', 'i32', None),  # 0 defeat all, 1 leader, 2 event
    (0x30, 'bgm_camp', 'str', 0x30),
    (0x60, 'bgm_player_turn', 'str', 0x30),
    (0x90, 'bgm_enemy_turn', 'str', 0x30),
    (0xc0, 'bgm_player_battle', 'str', 0x30),
    (0xf0, 'bgm_enemy_battle', 'str', 0x30),
    (0x120, 'bgm_boss_battle', 'str', 0x30),
    (0x150, 'bgm_victory', 'str', 0x30),
    (0x180, 'bgm_game_over', 'str', 0x30),
    (0x1b0, 'camp_before_battle', 'i32', None),  # 0 no, 1 yes
    (0x1b4, 'shop_items', 'i32[]', 100),  # ITEM.DAT ids, first shop_item_count valid
    (0x344, 'shop_item_count', 'i32', None),
    (0x348, 'start_pos_count', 'i32', None),
    (0x34c, 'start_pos_x', 'i32[]', 20),
    (0x39c, 'start_pos_y', 'i32[]', 20),
    (0x3ec, 'character_list', 'i32[]', 20),  # UNIT.DAT ids
    (0x43c, 'character_list_count', 'i32', None),
    (0x440, 'camp_background', 'i32', None),  # CAMP_%03d.BMP
    (0x444, 'whole_tile_grid', 'i32', None),  # 1 (every shipped map): tiles stored as the whole 40x40 grid; 0: h rows of w
    (0x450, 'disable_party_changes', 'i32', None),  # 1 = ticked
]
SMAP_HEADER_SIZE = 0x4b0
SMAP_TILES_OFF = 0x4b0
SMAP_EVENTS_OFF = SMAP_TILES_OFF + 40 * 40 * 2   # 0x1130 with the whole tile grid

# Event area layout, read in this order by the runtime's map loader (SRPGEXEC FUN_004048ac: header, tiles, objects,
# then the container through FUN_0040289c). Offsets are those of a map with the whole tile grid:
SMAP_OBJ_OFF, SMAP_OBJ_SIZE, SMAP_OBJ_COUNT = 0x1130, 164, 100   # map objects
SMAP_FAT_OFF, SMAP_FAT_COUNT = 0x5140, 5000                       # block chain table
SMAP_DIR_OFF, SMAP_DIR_COUNT = 0x9f60, 5000                       # 16-byte file entries
SMAP_BLOCK_OFF, SMAP_BLOCK_SIZE = 0x9f60 + 5000 * 16, 100         # 0x1d7e0: the blocks in use, packed
FAT_FREE, FAT_END = -1, -2

MAP_OBJECT_SPEC = [
    (0x00, 'in_use', 'i32', None),          # 0 = deleted slot (stale data kept)
    (0x04, 'x', 'i32', None),
    (0x08, 'y', 'i32', None),
    (0x0c, 'type', 'i32', None),            # 0 enemy, 1 ally, 2 neutral, 3 other
    (0x10, 'switch_condition', 'i16', None),  # SWNAME id, -1 = none
    (0x14, 'unit', 'i32', None),            # UNIT.DAT id (units only)
    (0x18, 'graphic', 'i32', None),         # "Other": tileset chip, -1 = none
    (0x1c, '_move_patterns', 'i16[]', 20),  # 4 x (switch, action, x, y, switch_to_apply)
    (0x44, 'leader', 'i32', None),          # enemy "Leader" tick-box
    (0x48, 'guest', 'i32', None),           # ally "Guest" tick-box
    (0x4c, 'reward_item', 'i32', None),     # ITEM.DAT id, -1 = none
    (0x50, 'reward_gold', 'i32', None),
    (0x54, '_event_slots', 'i16[]', 20),    # 5 x (condition, switch, turn, target_unit), see EVENT_CONDITIONS
    (0xa0, 'unattackable', 'i32', None),    # neutral "Unattackable" tick-box
]
MOVE_ACTIONS = ['rush', 'wait', 'specific', 'none']

# Event-slot condition codes (not in dialog order). 0/1/2/4/5 confirmed by
# editor test T11; the rest inferred from which object types use them.
EVENT_CONDITIONS = {
    0: 'do_not_perform', 1: 'switch', 2: 'turn',
    4: 'first_contact_any_time', 5: 'when_unit_dies',
    6: 'unit_passes_by', 7: 'unit_stops_on', 8: 'victory_conditions_met',
    9: 'first_contact_after_movement', 10: 'first_attack_before_attack',
    11: 'first_attack_after_attack',
}


def parse_map_object(rec):
    """One 164-byte map object (unit placement or "Other" event)."""
    d = _parse_record(rec, MAP_OBJECT_SPEC)
    mp = d.pop('_move_patterns')
    d['move_patterns'] = [dict(zip(('switch', 'action', 'x', 'y', 'switch_to_apply'), mp[i * 5:i * 5 + 5]))
                          for i in range(4)]
    for p in d['move_patterns']:
        if 0 <= p['action'] < 4:
            p['action_name'] = MOVE_ACTIONS[p['action']]
    ev = d.pop('_event_slots')
    d['event_slots'] = [dict(zip(('condition', 'switch', 'turn', 'target_unit'), ev[i * 4:i * 4 + 4]))
                        for i in range(5)]
    for sl in d['event_slots']:
        sl['condition_name'] = EVENT_CONDITIONS.get(sl['condition'])
        if sl['condition'] == 2:   # turn field counts phases: ally turn t = 2t-1, enemy turn t = 2t
            sl['turn_side'] = 'enemy' if sl['turn'] % 2 == 0 else 'ally'
            sl['turn_number'] = (sl['turn'] + 1) // 2
    return d


# Event command opcodes. Names follow the order of the editor's command
# buttons (help file "Event Editor Commands", rows 1-3), which the opcode
# numbers increase with; payload layouts are only partly decoded.
EVENT_OPCODES = {
    0x00: 'end_of_script',
    0x01: 'message', 0xc9: 'message_line',
    0x02: 'face_graphic', 0x03: 'switch_operation', 0x04: 'item_management',
    0x05: 'gold_management', 0x0b: 'screen_display', 0x0c: 'erase_screen',
    0x0d: 'picture_display', 0x0e: 'erase_picture', 0x10: 'scroll_screen',
    0x12: 'flash_tint_screen', 0x13: 'shake_screen', 0x16: 'change_ally_unit',
    0x17: 'unit_class_change', 0x18: 'unit_equipment_change', 0x1f: 'move_unit',
    0x20: 'execute_movement', 0x24: 'bgm', 0x25: 'sound_effect',
    0x29: 'variable_operation', 0x2a: 'conditional_branch', 0x2d: 'character_choice',
    0x33: 'button_input', 0x34: 'wait', 0x36: 'select_map', 0x38: 'end_event',
    0x3a: 'map_complete', 0x3b: 'game_over', 0x3d: 'return_to_title',
    0xc7: 'end_block', 0xca: 'end_branch', 0xcc: 'choice_case', 0xcd: 'end_choice',
}


MOVE_STEPS = ['move_up', 'move_right', 'move_down', 'move_left',
              'face_up', 'face_right', 'face_down', 'face_left']

# Conditional Branch option codes (dropdown order, confirmed by T14/T15).
BRANCH_TYPES = ['switch', 'turn', 'item', 'unit', 'variable']
BRANCH_SWITCH_STATES = ['on', 'off']
BRANCH_TURN_MODES = ['after', 'before', 'exact']
BRANCH_ITEM_STATES = ['party_possesses', 'party_does_not_possess']
BRANCH_UNIT_STATES = ['alive', 'dead', 'not_present', 'controlled_by_player', 'controlled_by_computer']
BRANCH_COMPARISONS = ['>=', '<=', '==']


def _name(table, i):
    return table[i] if 0 <= i < len(table) else None


def _branch_summary(d):
    """Only the fields of the selected condition type apply; name them."""
    t = d['cond_type']
    out = {'cond': _name(BRANCH_TYPES, t)}
    if t == 0:
        out.update(test_switch=d['switch'], test_state=_name(BRANCH_SWITCH_STATES, d['switch_state']))
    elif t == 1:   # turn counts phases like event slots: ally t = 2t-1, enemy t = 2t
        out.update(test_side='enemy' if d['turn'] % 2 == 0 else 'ally', test_turn=(d['turn'] + 1) // 2,
                   test_mode=_name(BRANCH_TURN_MODES, d['turn_mode']))
    elif t == 2:
        out.update(test_item=d['item'], test_state=_name(BRANCH_ITEM_STATES, d['item_state']))
    elif t == 3:
        out.update(test_unit=d['unit'], test_state=_name(BRANCH_UNIT_STATES, d['unit_state']))
    elif t == 4:
        out.update(test_variable=d['variable'], test_op=_name(BRANCH_COMPARISONS, d['comparison']),
                   test_value=d['compare_value'])
    return out

# Fixed-layout payloads: op -> (struct format, field names). IDs are 0-based
# record indices (the editor shows +1). See FORMAT_NOTES §11.3 for evidence.
_ARG_LAYOUTS = {
    0x02: ('<BBH', ('position', 'flip', 'face')),          # position 0 left, 1 right; flip 1 = mirrored
    0x03: ('<HH', ('on', 'switch')),
    0x04: ('<HHH', ('add', 'item', 'count')),               # add 1 / remove 0
    0x05: ('<Hi', ('add', 'amount')),                       # add 1 / remove 0
    0x0b: ('<BB', ('color', 'effect')),                     # color 0 from black, 1 from white
    0x0c: ('<BB', ('color', 'effect')),
    0x0e: ('<H', ('method',)),
    0x10: ('<HH', ('x', 'y')),
    0x12: ('<Hhhhh', ('mode', 'v1', 'v2', 'v3', 'v4')),     # mode 0-2 Flash 1-3, 3-5 Fast/Medium/Slow Change
    0x13: ('<H', ('strength',)),                            # 0 weak, 1 medium, 2 strong
    0x16: ('<HH', ('add', 'unit')),                         # add 1 / remove 0
    0x17: ('<HH', ('unit', 'class_id')),
    0x18: ('<HH', ('unit', 'item')),
    # source 0 constant, 1 variable, 2 random; all three source fields are kept
    # by the editor, only the selected one applies. operation 0 set, 1 add,
    # 2 subtract, 3 multiply, 4 divide.
    0x29: ('<HHhHHH', ('variable', 'source', 'constant', 'source_variable', 'random_max', 'operation')),
    # one field per condition kind; cond_type selects which apply
    # (0 switch, 1 turn, 2 item, 3 unit, 4 variable).
    0x2a: ('<HHHHHHBBBBhHi', ('cond_type', 'switch', 'turn', 'item', 'unit', 'variable',
                             'switch_state', 'turn_mode', 'item_state', 'unit_state',
                             'compare_value', 'comparison', 'reserved')),
    0x34: ('<H', ('tenths',)),
    0x36: ('<H', ('map',)),                                 # 0-based MAPNAME/SMAP index
    0x3a: ('<H', ('win',)),                                 # 1 win, 0 lose
    0xcc: ('<B', ('case',)),
}


def _decode_args(op, p):
    """Argument decoding; the raw payload is always kept too."""
    lay = _ARG_LAYOUTS.get(op)
    if lay and struct.calcsize(lay[0]) == len(p):
        d = dict(zip(lay[1], struct.unpack(lay[0], p)))
        if op == 0x2a:
            d.update(_branch_summary(d))
        return d
    if op in (0x01, 0xc9) and len(p) >= 3:
        return {'window': struct.unpack_from('<H', p, 0)[0],   # 0 top, 1 centre, 2 bottom
                'text': _cstr(p, 2, len(p) - 2)}
    if op in (0x24, 0x25):
        return {'file': _cstr(p, 0, len(p))}
    if op == 0x0d and len(p) > 6:
        # position: numeric-keypad layout (7 8 9 / 4 5 6 / 1 2 3), 5 = centre
        return {'method': p[0], 'position': p[1], 'remove_transparent': p[2],
                'params': list(p[3:6]), 'file': _cstr(p, 6, len(p) - 6)}
    if op == 0x1f and len(p) >= 4:
        unit, n = struct.unpack_from('<HH', p, 0)
        steps = list(p[4:4 + n])
        return {'unit': unit, 'steps': steps,
                'step_names': [MOVE_STEPS[x] if x < len(MOVE_STEPS) else None for x in steps]}
    if op == 0x2d and len(p) >= 2:
        n = struct.unpack_from('<H', p, 0)[0]
        opts = p[2:].split(b'\0')[:n]
        return {'options': [o.decode(ENCODING, errors='replace') for o in opts]}
    if p and len(p) % 2 == 0:
        return {'u16': list(struct.unpack(f'<{len(p) // 2}h', p))}
    return {}


def parse_event_file(data):
    """One event script file from a map's internal file system:
    100-byte comment, int32 stream length, then commands of
    u8 opcode, u16 payload length, u8 nesting depth, payload; the stream
    ends with an end_of_script command and one 0x00 byte."""
    out = {'comment': _cstr(data, 0, 100)}
    if len(data) < 104:
        return dict(out, error='file shorter than header', raw=data.hex())
    n = _i32(data, 100)
    if not 0 <= n <= len(data) - 104:
        return dict(out, error=f'bad stream length {n}', raw=data.hex())
    s = data[104:104 + n]
    cmds, o = [], 0
    while o < len(s) - 1:
        op, ln, depth = s[o], struct.unpack_from('<H', s, o + 1)[0], s[o + 3]
        p = s[o + 4:o + 4 + ln]
        if len(p) != ln:
            out['error'] = f'command at {o} overruns stream'
            break
        c = {'op': f'{op:02x}', 'name': EVENT_OPCODES.get(op), 'depth': depth, 'raw': p.hex()}
        c.update(_decode_args(op, p))
        cmds.append(c)
        o += 4 + ln
    out['commands'] = cmds
    out['stream_trailer'] = s[o:].hex()
    if len(data) > 104 + n:
        out['file_trailer'] = data[104 + n:].hex()
    # lossless check: re-encode and compare
    rebuilt = b''.join(bytes([int(c['op'], 16)]) + struct.pack('<H', len(bytes.fromhex(c['raw'])))
                       + bytes([c['depth']]) + bytes.fromhex(c['raw']) for c in cmds) + s[o:]
    out['roundtrip_ok'] = rebuilt == s
    return out


def _read_map_files(data, at=SMAP_FAT_OFF):
    """The map's script container -> {name: info}, read as the runtime reads it
    (SRPGEXEC FUN_0040289c): a FAT of 5000 block links (-1 free, -2 end of
    chain), 5000 16-byte directory entries, then only the blocks in use (FAT
    entry != -1), 100 bytes each, packed in block order. A file is its chain
    from first_block cut to size (FUN_0040262c). A block the file lacks reads
    as zeros and is listed in missing_blocks (none in the shipped games)."""
    fat = struct.unpack_from(f'<{SMAP_FAT_COUNT}i', data, at)
    dir_off = at + SMAP_FAT_COUNT * 4
    packed, o = {}, dir_off + SMAP_DIR_COUNT * 16
    for k, link in enumerate(fat):
        if link != FAT_FREE:
            packed[k] = o
            o += SMAP_BLOCK_SIZE

    def block(k):
        o = packed.get(k)
        return (data[o:o + SMAP_BLOCK_SIZE] if o is not None else b'').ljust(SMAP_BLOCK_SIZE, b'\0')

    files = {}
    for i in range(SMAP_DIR_COUNT):
        e = data[dir_off + i * 16:dir_off + i * 16 + 16]
        if not any(e):
            continue
        name = e[:8].split(b'\0')[0].decode('ascii', errors='replace')
        first, size = struct.unpack_from('<2i', e, 8)
        chain, k = [], first
        while 0 <= k < SMAP_FAT_COUNT and len(chain) <= SMAP_FAT_COUNT:
            chain.append(k)
            k = fat[k]
        body = b''.join(block(c) for c in chain)[:size]
        files[name] = {
            'dir_index': i, 'first_block': first, 'size': size, 'blocks': chain,
            'chain_end_ok': k == FAT_END,
            'missing_blocks': [c for c in chain if packed.get(c, len(data)) + SMAP_BLOCK_SIZE > len(data)],
            'data': body,
        }
    return files


def parse_smap(data):
    """SMAP_xxx.DAT: map header, tile grid, 100 map objects, and the internal
    file system holding the event scripts, read in that order (FUN_004048ac).
    The tiles are the whole 40x40 grid when whole_tile_grid is set (every
    shipped map), else h rows of w chips.
    Returns (json_dict, raw_event_area_bytes)."""
    hdr = _parse_record(data[:SMAP_HEADER_SIZE], SMAP_HEADER_SPEC)
    w, h = hdr['width'], hdr['height']
    if hdr['whole_tile_grid']:
        grid = struct.unpack_from('<1600H', data, SMAP_TILES_OFF)
        hdr['tiles'] = [list(grid[y * 40:y * 40 + w]) for y in range(h)]
        outside = {f'{x},{y}': grid[y * 40 + x] for y in range(40) for x in range(40)
                   if (x >= w or y >= h) and grid[y * 40 + x]}
        if outside:
            hdr['tiles_outside_map'] = outside
        obj_off = SMAP_OBJ_OFF
    else:
        grid = struct.unpack_from(f'<{w * h}H', data, SMAP_TILES_OFF)
        hdr['tiles'] = [list(grid[y * w:y * w + w]) for y in range(h)]
        obj_off = SMAP_TILES_OFF + w * h * 2
    objs = []
    for i in range(SMAP_OBJ_COUNT):
        rec = data[obj_off + i * SMAP_OBJ_SIZE:obj_off + (i + 1) * SMAP_OBJ_SIZE]
        if any(rec):
            objs.append(dict(index=i, **parse_map_object(rec)))
    hdr['objects'] = objs
    events = {}
    for name, f in _read_map_files(data, obj_off + SMAP_OBJ_COUNT * SMAP_OBJ_SIZE).items():
        ev = parse_event_file(f.pop('data'))
        f.pop('blocks')
        if not f['missing_blocks']:
            del f['missing_blocks']
        ev['storage'] = f
        events[name] = ev
    hdr['events'] = events
    blob = data[obj_off:]
    hdr['event_area_sha1'] = hashlib.sha1(blob).hexdigest()
    return hdr, blob


SAVE_SIZE = 114400
SAVE_UNIT_OFF, SAVE_UNIT_SIZE, SAVE_MAP_UNITS, SAVE_ROSTER_UNITS = 0x7d0, 640, 100, 50
SAVE_OBJECTS_OFF = SAVE_UNIT_OFF + (SAVE_MAP_UNITS + SAVE_ROSTER_UNITS) * SAVE_UNIT_SIZE   # 0x17ed0

SAVE_HEADER_SPEC = [
    (0x10, 'map_number', 'i32', None),        # 1-based SMAP number (inferred)
    (0x14, 'switches', 'u8[]', 500),          # one byte per switch, 1 = ON (confirmed T18)
    (0x20c, 'phase', 'i32', None),            # turn/phase counter (inferred)
    (0x210, '_inventory', 'i32[]', 200),      # 100 x (item id, count) (inferred T18)
    (0x530, 'inventory_count', 'i32', None),  # number of used inventory entries (inferred T18)
    (0x534, 'gold', 'i32', None),             # confirmed (T16: 20)
    (0x538, 'party_count', 'i32', None),      # inferred (2 = ルーティ + ジュナ)
    (0x53c, 'bgm', 'str', 0x30),              # current BGM file
    (0x56c, 'tone', 'i32[]', 4),              # screen tone R, G, B, vividness (inferred)
    (0x5dc, '_variables', 'i16[]', 250),      # variables 0-249 as int16 (inferred T18c: var 19 = 1 at 0x602)
]

SAVE_UNIT_SPEC = [
    (0x00, 'in_use', 'i32', None),
    (0x04, 'present', 'i32', None),           # on the map and alive (defeated enemy -> 0)
    (0x08, 'acted', 'i32', None),             # finished its action this turn (confirmed T16)
    (0x0c, 'computer', 'i32', None),          # 1 for enemies (inferred)
    (0x10, 'unknown_010', 'i32', None),       # always -1 so far
    (0x14, 'x', 'i32', None),                 # confirmed T16 (Juna moved up: y 12 -> 11)
    (0x18, 'y', 'i32', None),
    (0x1c, 'map_object', 'i32', None),        # SMAP object index, -1 = roster (camp) entry
    (0x20, 'unit', 'i32', None),              # UNIT.DAT id
    (0x24, 'class_id', 'i32', None),
    (0x28, 'level', 'i32', None),             # confirmed (ルーティ 1)
    (0x2c, 'exp', 'i32', None),               # confirmed (ルーティ 49)
    (0x4c, 'movement', 'i32', None),
    (0x50, 'base_stats', 'i32[]', 6),         # max HP, max MP, Str, Mag, Skl, Sta (without equipment)
    (0x68, 'current_stats', 'i32[]', 6),      # current HP, current MP, Str..Sta incl. accessory
    (0x80, 'combat', 'i32[]', 4),             # Attack, Accuracy, Defense, Magic Defense (match status screen)
    (0xa0, 'weapon', 'i32', None),
    (0xa4, 'armor', 'i32', None),
    (0xa8, 'accessory', 'i32', None),
    (0xac, 'range_min', 'i32', None),
    (0xb0, 'range_max', 'i32', None),
    (0xc8, 'move_left', 'i32', None),         # confirmed T16 (6 -> 5 after a 1-tile move)
    (0xd8, 'turn_start_pos', 'i32[]', 2),     # position before moving this turn (confirmed T16)
    (0xf0, 'terrain_move_cost', 'i32[]', 70),
]


def parse_save(data):
    """SAVEn.DAT (114,400 B): header, 100 map-unit + 50 roster-unit records of 640 B,
    then a copy of the map's 100 object records (164 B, SMAP layout)."""
    if len(data) != SAVE_SIZE:
        raise ValueError(f'expected {SAVE_SIZE} bytes, got {len(data)}')
    out = _parse_record(data[:SAVE_UNIT_OFF], SAVE_HEADER_SPEC)
    var = out.pop('_variables')
    out['variables_set'] = {i: v for i, v in enumerate(var) if v}
    sw = out.pop('switches')
    out['switches_on'] = [i for i, v in enumerate(sw) if v]
    inv = out.pop('_inventory')
    out['inventory'] = [{'item': inv[i * 2], 'count': inv[i * 2 + 1]} for i in range(max(0, min(100, out['inventory_count'])))]
    units = []
    for i in range(SAVE_MAP_UNITS + SAVE_ROSTER_UNITS):
        rec = data[SAVE_UNIT_OFF + i * SAVE_UNIT_SIZE:SAVE_UNIT_OFF + (i + 1) * SAVE_UNIT_SIZE]
        if any(rec):
            units.append(dict(slot=i, roster=i >= SAVE_MAP_UNITS, **_parse_record(rec, SAVE_UNIT_SPEC)))
    out['units'] = units
    out['map_objects'] = [dict(index=i, **parse_map_object(data[SAVE_OBJECTS_OFF + i * 164:SAVE_OBJECTS_OFF + (i + 1) * 164]))
                          for i in range(100) if any(data[SAVE_OBJECTS_OFF + i * 164:SAVE_OBJECTS_OFF + (i + 1) * 164])]
    return out


def parse_game(data):
    """GAME.DAT: 200 bytes = 50 int32, mostly unknown."""
    v = _i32s(data, 0, len(data) // 4)
    out = {'start_map': v[0]}   # 0-based MAPNAME/SMAP index (inferred)
    out.update({f'unknown_{i * 4:03x}': x for i, x in enumerate(v) if i and x})
    return out


def parse_test(data):
    """TEST.DAT: 6000 bytes, editor test-play settings (unknown)."""
    v = _i32s(data, 0, len(data) // 4)
    return {f'unknown_{i * 4:03x}': x for i, x in enumerate(v) if x}


def parse_editor(data):
    """EDITOR.DAT: 4 bytes, editor state (last opened map, inferred)."""
    return {'last_map': _i32(data, 0)}


# --------------------------------------------------------------------------
# CLI: dump everything to JSON
# --------------------------------------------------------------------------

TABLES = {
    'UNIT.DAT': parse_unit,
    'CLASS.DAT': parse_class,
    'ITEM.DAT': parse_item,
    'MAGIC.DAT': parse_magic,
    'GEOLOGY.DAT': parse_geology,
    'ANIME.DAT': parse_anime,
    'WORD.DAT': parse_word,
    'MAPNAME.DAT': parse_name_table,
    'VARNAME.DAT': parse_name_table,
    'SWNAME.DAT': parse_name_table,
    'GAME.DAT': parse_game,
    'TEST.DAT': parse_test,
    'EDITOR.DAT': parse_editor,
}


def _find(game_dir, name):
    """Case-insensitive lookup (the original files mix SWNAME.DAT / Swname.dat)."""
    for f in os.listdir(game_dir):
        if f.upper() == name.upper():
            return os.path.join(game_dir, f)
    return None


def _write(path, obj):
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)


def dump_all(game_dir, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for name, fn in TABLES.items():
        p = _find(game_dir, name)
        if not p:
            print(f'skip {name} (missing)')
            continue
        _write(os.path.join(out_dir, name.lower().replace('.dat', '.json')),
               fn(open(p, 'rb').read()))
        print(f'ok   {name}')
    map_dir = _find(game_dir, 'MAP')
    if not map_dir:
        return
    maps_out = os.path.join(out_dir, 'map')
    os.makedirs(maps_out, exist_ok=True)
    for f in sorted(os.listdir(map_dir)):
        base, up = os.path.splitext(f)[0].lower(), f.upper()
        data = open(os.path.join(map_dir, f), 'rb').read()
        if up.startswith('MAPC_') and up.endswith('.DAT'):
            _write(os.path.join(maps_out, base + '.json'), parse_mapc(data))
        elif up.startswith('SMAP_') and up.endswith('.DAT'):
            hdr, blob = parse_smap(data)
            _write(os.path.join(maps_out, base + '.json'), hdr)
            with open(os.path.join(maps_out, base + '.events.bin'), 'wb') as b:
                b.write(blob)
    print(f'ok   MAP/ -> {maps_out}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    dump_all(sys.argv[1], sys.argv[2])
