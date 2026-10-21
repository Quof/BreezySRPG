# Sim RPG Maker 95 data formats (えるまに)

Phase 1 notes: what the data files contain and how they are laid out.
Parser: [`srpg95_data.py`](srpg95_data.py) (one function per format).
JSON dumps: `json/` (this game), `json_rtp/` (the editor's default RTP data), `json_kikumaru/` (the bundled English sample project).

## Confidence levels

- **confirmed**: the decoded value matched an independent known value. Usually that is the RTP default data compared with the help-file screenshots (e.g. RTP unit 0 "Richard" has HP 20 / growth 180 / Long Sword, exactly as `ue2.PNG` shows). Unambiguous cross-references (filenames, names that match each other) also count.
- **inferred**: consistent with all the data and the editor's field order, but not pinned down by a known value. Each one comes with its evidence. Anything worth an editor test is listed in [Tests to run](#tests-to-run).

Evidence sources:
1. `srpg maker 95/Help/SRPG95.chm` (English RPGCrisis help file, decompiled with `hh -decompile`). It documents every editor field and includes screenshots of the RTP defaults.
2. `srpg maker 95/RTP/*.DAT`, the stock data those screenshots show.
3. Cross-checks between the three datasets (game, RTP, Kikumaru).

## General conventions (all files)

| Item | Finding | Confidence |
|---|---|---|
| Endianness | little-endian | confirmed |
| Integers | almost all fields are int32. Booleans and attribute sets are packed u8 arrays | confirmed |
| Text encoding | Shift-JIS. Decode as `cp932` because the files contain NEC/IBM extensions and half-width kana (`ﾎﾞｽ戦闘.MID`) | confirmed |
| Strings | fixed-size buffers, NUL-terminated. Bytes after the NUL are stale leftovers from earlier edits, e.g. MAPNAME `１話\0vニング`. The parser drops them | confirmed |
| Line breaks in text | `\r\n` | confirmed |
| IDs | References to database records (class, item, spell, animation, unit) are 0-based, and the editor shows them +1 (`001:Lord M` is index 0). Frame numbers are 0-based too. **Graphic file numbers** (face, unit_graphic, battle_background, camp_background, tileset) are stored exactly as shown and map directly to `XXX_%03d.BMP` | confirmed (T2, T6, T1, T5) |
| "none" sentinel | `-1` (0xFFFFFFFF), e.g. empty equipment slot | confirmed |
| File sizes | every database is a fixed-size array. All 250/150/100… slots always exist, and empty slots keep editor defaults rather than zeros | confirmed |
| Unknown bytes | the parser emits any non-zero byte not covered by a named field as `unknown_XXX` (hex offset), so nothing is lost. After this session, every non-zero byte in UNIT/CLASS/ITEM/MAGIC/GEOLOGY across all three datasets is covered by a named field | — |

---

## 1. Folder inventory

### Game folder `えるまに/` (the game to reimplement)

| Group | Files | What it is |
|---|---|---|
| Runtime | `SRPGEXEC.EXE` (1,074,688 B), `HARMONY.DLL` | Player executable. HARMONY.DLL also ships with the editor and is presumably the MIDI/sound helper (unverified). Neither is needed by a new client |
| Databases | `UNIT CLASS ITEM MAGIC GEOLOGY ANIME .DAT` | Fixed-size record arrays (§2–§7) |
| Name tables | `WORD MAPNAME VARNAME SWNAME .DAT` | Arrays of 32-byte strings (§8) |
| Misc data | `GAME.DAT` (200 B), `TEST.DAT` (6000 B), `EDITOR.DAT` (4 B) | Game settings, editor test-play settings, editor state (§12) |
| Maps | `MAP/SMAP_001…069, 099, 100.DAT` (79 files, 120–233 KB) | Map header, tile grid and events (§10). `SMAP_nnn` ↔ `MAPNAME[nnn-1]` |
| Tileset terrain | `MAP/MAPC_001,002,003,008,009,013,017,025.DAT` (384 B each) | Terrain type of each tileset chip (§9) |
| Graphics | `BMP/` 333 files, all 8-bit paletted | see below |
| Music | `BGM/` 72 `.mid` + 1 `.wav` | Referenced by name from maps and events |
| Sound FX | `EFS/` 82 `.wav` | Referenced by name from ANIME and events |
| Extras | `はじめる前に.txt` (readme: mouse-only controls, F4 toggles window size, credits), 6 `.url` shortcuts | Not data |

`BMP/` naming groups (sizes measured from the BMP headers):

| Pattern | Count | Size | Meaning | Confidence |
|---|---|---|---|---|
| `FACE_nnn` | 89 | 128×128 | Portraits. `nnn` = UNIT.face | confirmed (T1: portrait #10 → face 10) |
| `UNIT_nnn` | 71 | 192×320 (one is 32×32) | Map sprite sheet, 6×10 cells of 32×32. `nnn` = CLASS.unit_graphic | confirmed by help + cross-ref |
| `CAxx_yyy` | 78 | 192×192 cells, up to 12 per row (≤2304 wide) | Weapon battle-animation cells. `xx` = ANIME index+1, `yyy` = class unit_graphic | confirmed (`CA03_004`: anime 2 "ナス攻撃" × class "ナス" unit_graphic 4) |
| `MAGA_nnn` | 49 | 192×192 cells | Spell animation cells. `nnn` = spell-animation number | inferred (help) |
| `BACK_nnn` | 15 | 640×320 | Battle backgrounds. `nnn` = GEOLOGY.battle_background | inferred |
| `MAPC_nnn` | 12 | 384×1024 | Tilesets: 12×32 chips of 32×32 = 384 chips | confirmed (384 chips = 384-byte MAPC.DAT, max tile value 383) |
| `CAMP_nnn` | 1 | 640×480 | Camp background (SMAP.camp_background) | inferred |
| `TITLE`, `GAMEOVER` | 1 each | 640×480 | Title and game-over screens | name-evident |
| `SYSTEM` | 1 | 256×320 | UI parts (cursor, window skin…) | to investigate |
| `SFRAME_H`, `SFRAME_V` | 1 each | 640×64, 64×448 | Screen frame pieces | to investigate |
| `TELOP1-3` | 3 | 512×96 | Caption banners | to investigate |
| `act*`, `acted*` | 10 | 640×480 / 640×320 | Game-specific pictures shown by events ("Picture Display") | inferred |
| `Thumbs.db` | 1 | — | Windows thumbnail cache, ignore | — |

### Editor folder `srpg maker 95/`

`SRPG95.exe` (editor), `Help/SRPG95.chm` (English help), `RTP/` (default database plus `BMP/BGM/EFS/MAP` assets, 693 BMPs), `Projects/Kikumaru's Adventure/` (English sample project), installer and uninstaller files, `UNLHA32.DLL` (LZH archive library), `Extras/msgothi0.ttc` (MS Gothic font).

**Asset lookup order is still to be determined.** A game can reference assets that exist only in the RTP; for example, GEOLOGY.battle_background goes up to 66, but the game folder has only 15 BACK files. The new client will probably need the RTP folder as a fallback.

---

## 2. UNIT.DAT — 250 records × 480 B

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x000 | 32 | str | name | confirmed | RTP "Richard". Observed max 20 bytes |
| 0x020 | 4 | i32 | face (FACE_%03d, value = portrait number as shown in the editor) | confirmed | T1: portrait #10 stored as 10 |
| 0x024 | 4 | i32 | class_id | confirmed | Richard → class 0 "Lord M". Game units named 兵士 → class 24 兵士 |
| 0x028 | 4 | i32 | level | confirmed | Richard 1. Game max 20 |
| 0x02C | 4 | i32 | exp | confirmed | Richard 0 (range 0–55) |
| 0x030 | 24 | i32[6] | base_stats: HP, MP, Str, Mag, Skl, Sta | confirmed | 20,5,5,1,4,4 = screenshot |
| 0x048 | 24 | i32[6] | growth_pct (same order) | confirmed | 180,130,155,120,160,150 = screenshot. T1 "MP growth 111" was stored in slot 3 (0x054, Mag), so the row edited was probably the 4th one. The RTP check already fixes the order |
| 0x060 | 4 | i32 | weapon (ITEM id, -1 none) | confirmed | 5 = "Long Sword" |
| 0x064 | 4 | i32 | armor | confirmed | 40 = "Iron Mail" |
| 0x068 | 4 | i32 | accessory | confirmed | 49 = "Power Wrist" |
| 0x06C | 372 | str | death_message | confirmed | "If...I had...more time. \r\n". T1: the editor accepted 198 bytes of text (NUL at 0x132). It hard-wraps by inserting `\r\n` after every 39 bytes, and those CRLFs are stored and count toward the limit. Bytes 0x133–0x1DF are never used by the editor |

Editor defaults for an empty slot: face 1, class 0, level 1, stats 20,20,5,5,5,5, growth 80,80,30,30,30,30, equipment -1.

Note: the game's WORD.DAT renames the four base stats: Strength = 攻撃補正値, Magic = 特殊補正値, Skill = 命中回避値, Stamina = 耐久力 (T7). 攻撃力/攻撃精度/物理防御/特殊防御 are the *derived* combat values Attack, Attack Speed, Defense and Magic Defense, not unit fields. MP is labelled ＴＰ.

## 3. CLASS.DAT — 150 records × 640 B

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x000 | 32 | str | name | confirmed | "Lord M" |
| 0x020 | 4 | i32 | movement | confirmed | 6 |
| 0x024 | 4 | i32 | move_type: 0 Walk, 1 Fly, 2 Special. Selects the GEOLOGY move_cost column | confirmed | dropdown order Walk/Fly/Special. T2: Special stored as 2 |
| 0x028 | 280 | i32[70] | terrain_move_override, indexed by GEOLOGY id, -1 = use terrain default | confirmed | T2: terrain #4 (雪原, id 3) cost 4 → slot 3 = 4. The editor's Move Rule list marks overrides with `*` |
| 0x140 | 4 | i32 | unit_graphic (UNIT_%03d, CAxx_%03d) | confirmed | Lord M 1. CA03_004 cross-ref |
| 0x144 | 4 | i32 | anim_weapon_a (ANIME id, 0-based; editor shows +1) | confirmed | T2: editor 005…010 stored as 4…9 in exactly this field order |
| 0x148 | 4 | i32 | anim_weapon_b | confirmed | ″ |
| 0x14C | 4 | i32 | anim_weapon_c | confirmed | ″ |
| 0x150 | 4 | i32 | anim_no_weapon | confirmed | ″ |
| 0x154 | 4 | i32 | anim_magic_enemy | confirmed | ″ |
| 0x158 | 4 | i32 | anim_magic_ally | confirmed | ″ |
| 0x15C | 72 | — | (always zero) | — | |
| 0x1A0 | 4 | i32 | (always zero) | — | possibly an init-stat slot for HP |
| 0x1A4 | 16 | i32[4] | init_stats: Str, Mag, Skl, Sta | confirmed | 10,6,7,9 = screenshot |
| 0x1B4 | 80 | i32[20] | skill_levels (level learned, 0 = end) | confirmed | T2: adding a level-13 skill wrote 13 into slot 1 |
| 0x204 | 80 | i32[20] | skill_ids (MAGIC id) | inferred (strong) | paired with skill_levels. The T2 skill's id was 0, which was already there, so the id slot showed no change |
| 0x254 | 6 | u8[6] | weakness_attr A–F | confirmed | T2: "Attribute B only" → bytes [0,1,0,0,0,0]. RTP undead weak to E (= Light), sea monsters to A (= Fire) |
| 0x25A | 2 | — | (always zero) | — | |
| 0x25C | 4 | u8[4] | resist: poison, sleep, atk down, def down | confirmed (sleep position), inferred (rest) | T2: "Sleep only" → [0,1,0,0]. Remaining order from the help text |
| 0x260 | 32 | — | (always zero) | — | |

## 4. ITEM.DAT — 100 records × 640 B

All type-specific blocks coexist in every record. The editor keeps stale values from other types, so read only the block matching `type`.

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x000 | 32 | str | name | confirmed | "Knife" |
| 0x020 | 4 | i32 | type: 0 Event, 1 Weapon, 2 Armor, 3 Accessory, 4 Medicine, 5 Stat-up, 6 Class change, 7 Magic item | 1 confirmed, rest inferred (strong) | Knife = 1. RTP names per value (Potions = 4, "HP Up" = 5, "Master Stone" = 6). Game 小型爆弾 = 7 |
| 0x024 | 4 | i32 | price | confirmed | Knife 30 |
| 0x028 | 80 | str | desc | confirmed | |
| 0x078 | 200 | u8[200] | class_usable (index = CLASS id). Slots 150–199 are always 1 | confirmed | T3: unticking class 002 cleared byte 0x079 |
| 0x140 | 4 | i32 | wpn_range: index into the dropdown `1-1, 1-2, 1-3, 2-2, 2-3, 2-4, 2-5, 3-10` (min-max distance). The parser also emits `wpn_range_min_max` | confirmed | T3 screenshot. Game item 001 = 1 = "1-2", matching its description 射程1～2 |
| 0x144 | 4 | i32 | wpn_power | confirmed | 1 |
| 0x148 | 4 | i32 | wpn_usability | confirmed | 10 |
| 0x14C | 4 | i32 | wpn_critical (signed) | confirmed | 2 |
| 0x150 | 4 | i32 | wpn_animation 0=A 1=B 2=C | 0 confirmed | |
| 0x154 | 6 | u8[6] | wpn_attack_attr A–F | inferred | "Flame Sword" A, "Thors Hammer" D |
| 0x15C | 4 | i32 | arm_defense | inferred | "Iron Mail" 8, weight 4 |
| 0x160 | 4 | i32 | arm_magic_defense | inferred | "Evil Clothes" 5 |
| 0x164 | 4 | i32 | arm_crit_avoid | inferred | "Angel Clothes" 60 |
| 0x168 | 4 | i32 | arm_weight | inferred | |
| 0x16C | 6 | u8[6] | arm_defense_attr A–F | inferred | "Ifrits Armor" A, "Leviathan" B. Game 物理ガード A, 闘気ガード B, 熱ガード C |
| 0x174–0x184 | 5×4 | i32 | acc_str, acc_mag, acc_skl, acc_sta, acc_move | inferred (strong) | "Power Wrist" Str 2, "Boots" move 2, "Ring of Speed" move 6 |
| 0x188 | 4 | i32 | acc_growth_bonus (Exp Growth %) | confirmed | T3: 37 |
| 0x18C | 4 | i32 | med_hp | inferred (strong) | Small/Medium/Large HP Potion 10/25/60 |
| 0x190 | 4 | i32 | med_mp | inferred (strong) | Mana potions 10/25/60 |
| 0x194 | 4 | i32 | med_def_up | confirmed | T3: 11 |
| 0x198 | 4 | i32 | med_mdef_up | confirmed | T3: 12 |
| 0x19C | 24 | i32[6] | statup: HP, MP, Str, Mag, Skl, Sta | inferred (strong) | "HP Up" [4,0,…], "Stamina Up" […,2] |
| 0x1B4 | 160 | i32[40] | class_change: 20 (from, to) CLASS id pairs, -1 empty | inferred | "Royal Crest" (1→10, 4→13…) |
| 0x254 | 4 | u8[4] | med_cure: the editor's "Heal" group of 4 checkboxes (Poison, Sleep, Attack Down, Defense Down) | confirmed | T3: Sleep + Defense Down → [0,1,0,1] |
| 0x258 | 4 | — | (always zero) | unknown | not the Heal checkbox (that is 0x254) |
| 0x25C | 4 | u8[4] | acc_safe: poison, sleep, atk down, def down | inferred | Bandanna = sleep. "Ring of Protection" = atk/def down |
| 0x264 | 4 | i32 | class_change_level (default 10) | inferred | class-change items 7/8/12/20, everything else 10 |
| 0x268 | 4 | i32 | med_range (0 = self) | confirmed | T3: Range 2 stored as 2 |
| 0x26C | 4 | i32 | magic_spell (MAGIC id) | confirmed | game 小型爆弾 → MAGIC 85 "小型爆弾" |
| 0x270 | 16 | — | (always zero) | — | |

## 5. MAGIC.DAT — 100 records × 480 B

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x000 | 32 | str | name | confirmed | "Fire Level 1" |
| 0x020 | 4 | i32 | type: 0 Damage, 1 Degrade status, 2 Raise status | 0 confirmed, 1/2 inferred (strong) | Poison/Sleep = 1, Heal/Attack Up = 2 |
| 0x024 | 4 | i32 | mp_cost | confirmed | 2 |
| 0x028 | 80 | str | desc | confirmed | |
| 0x078 | 4 | i32 | damage | confirmed | 5 |
| 0x07C | 4 | i32 | accuracy | confirmed | 80 |
| 0x080 | 8 | — | (always zero) | — | |
| 0x088 | 6 | u8[6] | attack_attr A–F | confirmed (A), inferred (rest) | Fire = A. RTP Water = B, Earth = C, Lightning = D, Light = E, Darkness = F |
| 0x08E | 2 | — | (always zero) | — | |
| 0x090 | 4 | i32 | animation: **ANIME index = 80 + value** | confirmed | Fire Level 1 → 0 → ANIME[80] "Fire Level 1". Game ヒール → 24 → ANIME[104] "ヒール" |
| 0x094 | 4 | i32 | exp_value (shown only for Degrade/Raise types; Damage spells keep a hidden value) | confirmed | T4: ヒール Exp Value 77 |
| 0x098 | 4 | i32 | degrade_effect: 0 poison, 1 sleep, 2 atk down, 3 def down | inferred (strong) | RTP Poison/Sleep/Attack Down/Defense Down = 0/1/2/3 |
| 0x09C | 4 | i32 | target: 0 "Allies/Enemies" (both sides), 1 "Enemies Only" | confirmed | T4: dropdown order Allies/Enemies, Enemies Only. Setting the last option stored 1. RTP Fire Level 1 = 1 = "Enemies Only", as in the help screenshot. (My earlier "contradiction" came from a screenshot taken after the edit, before saving.) Heal spells also store 1, so for Raise spells 1 presumably means "own side only"; the label may change with type, not checked |
| 0x0A0 | 4 | i32 | value / power (degrade, raise) | inferred (strong) | Heal 1–4 = 10/20/40/60 |
| 0x0A4 | 4 | i32 | relation (10 = 1:1 with caster Magic) | inferred | |
| 0x0A8 | 4 | i32 | raise_effect: 0 HP, 1 atk up, 2 def up, 3 cure poison, 4 cleanse, 5 add turn | inferred (strong) | RTP names in that order |
| 0x0AC | 60 | — | (always zero) | — | |
| 0x0E8 | 4 | i32 | range_whole_map ("Apply To Entire Map") | inferred | only 3 game spells, including マクロヒール. **T4** |
| 0x0EC | 4 | i32 | effect_whole_map | inferred | ″ |
| 0x0F0 | 84 | grid21 | range_grid (where the spell can be aimed) | confirmed (encoding) | see below |
| 0x144 | 84 | grid21 | effect_grid (splash around the aim point) | inferred (tab order) | |
| 0x198 | 72 | — | (always zero) | — | |

**grid21 encoding (confirmed):** 21 int32 rows, top row first, row 10 = centre. Within a row, bit 21 is the centre column, and a cell `dx` squares to the **right** of centre is bit `21 - dx`. So the leftmost column (dx = -10) is bit 31 and the rightmost (dx = +10) is bit 11. Bits 0–10 are never set (checked on all 100 spells). Max radius 10 ("maximum range is 10").

Evidence:
- **T4:** a cell 3 right of centre → row 10, bit 18. A cell 1 up → row 9, bit 21.
- RTP Fire Level 1 decodes to the radius-2 diamond in the help screenshot.
- Game spell 0 (ハウリングプラズマ) decodes to a radius-4 diamond with a hollow centre, and the editor shows those centre cells unset. Unset cells are real data and are not hit.

The parser renders each row left to right as the editor shows it. (Before T4 it rendered mirrored; every earlier pattern was symmetric, so no earlier conclusion changes.)

## 6. GEOLOGY.DAT (terrain types) — 70 records × 60 B

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x00 | 32 | str | name | confirmed | "Meadow" |
| 0x20 | 4 | i32 | defense_bonus | inferred (strong) | RTP Forest 20, Bridge -10 |
| 0x24 | 4 | i32 | hp_recovery per turn | inferred (strong) | game ダメージ床 ("damage floor") = -5, 城 = 4 |
| 0x28 | 12 | i32[3] | move_cost: Walking, Flying, Special (index = CLASS.move_type; 0 = impassable) | confirmed | T5 screenshot labels the three boxes Walking/Flying/Special. The help's "Special = can both fly and walk" is wrong: Special is a third, independent cost column (e.g. Sea [0,1,0] is impassable for Special) |
| 0x34 | 4 | i32 | battle_background (number shown in the editor; BACK_%03d presumably) | confirmed (field) / inferred (file mapping) | T5: set to 12, stored 12. Whether 12 → BACK_012.BMP is not yet checked |
| 0x38 | 4 | — | (always zero) | — | |

## 7. ANIME.DAT — 160 records × 2400 B

Records 0–79 are weapon/cast animations, used by CLASS anim_* and ITEM wpn_animation. Records 80–159 are spell animations (MAGIC.animation + 80).

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x000 | 32 | str | name | confirmed | "Sword Slash A" |
| 0x020 | 4 | ? | unknown_020: either 0 or 0x0000FFFF (int16 -1 + 0?) | unknown | 5 game records, 9 RTP |
| 0x024 | 4 | i32 | preview_graphic: the `yyy` of CAxx_yyy used by the editor's "Filename" | inferred | game ANIME 2 "ナス攻撃" = 52, and file CA03_052 exists |
| 0x028 | 2160 | frame[90] | frames (max 90 per help) | inferred (strong) | see below |
| 0x898 | 4 | i32 | frame_count ("Max Frame Number") | inferred (strong) | 20–45 |
| 0x89C | 4 | i32 | hit_frame | inferred | < frame_count |
| 0x8A0 | 4 | i32 | flash_time (1/10 s) | inferred | mostly 10 |
| 0x8A4 | 28 | — | (always zero) | — | |
| 0x8C0 | 48 | str | hit_sound (EFS wav filename) | confirmed | "THUNDER2.WAV" |
| 0x8F0 | 48 | str | miss_sound | inferred (order) | "WHACK.WAV" |
| 0x920 | 4 | i32 | hit_sound_frame, **0-based** (editor shows +1) | confirmed | T6: editor 5 → stored 4 |
| 0x924 | 4 | i32 | miss_sound_frame, **0-based** | confirmed | T6: editor 6 → stored 5 |
| 0x928 | 56 | — | (always zero) | — | |

**Frame** (24 B) = 4 cell layers × (int16 x, int16 y, int16 cell). cell = -1 means the layer is empty. Frame 0 starts at 0x028, layer 0 first, and x is the first int16 in pixels (confirmed by T6: moving the cell 16 px right changed frame 0 / layer 0 x from 480 to 496). Still inferred:
- y is the second int16.
- x/y anchor at the cell's top-left (vs centre).
- cell indexes the 192×192 tiles of the CA/MAGA bitmap.
- Layers with cell -1 can still hold stale x/y.
- Frames ≥ frame_count are ignored.

Whether hit_frame is 0-based like the sound frames is also unconfirmed. It probably is.

**Battle bitmaps (confirmed by file names):**
- Weapon and cast animations: `CA{animation index + 1:00}_{CLASS.unit_graphic:000}.BMP`. The same animation is drawn with each class's own sprite sheet. Every animation the game's classes use resolves to an existing file (85 of 86; the missing one belongs to class 26 透明, "transparent").
- Spell animations: `MAGA_{animation index − 79:000}.BMP` (records 80+). All 99 spells resolve.
- Each sheet is a horizontal strip of 192×192 cells (`cell` = index into the strip), palette index 0 transparent.
- Not every sheet is a whole number of cells wide: ジュリア's CA05_006 is 575 px and CA07_006 767 px (a pixel short), and MAGA_047 is 940 px (a partial fifth cell). The original draws a cut-off last cell, so skipping it is wrong: ジュリア's run uses that cell and would vanish.
- `preview_graphic` is only the editor's preview choice; it is not part of the file name.
- The CA sprites face left. A weapon animation's frame 0 shows the attacker standing at (480, 208), presumably the cell centre, which puts its feet near y 296 on the 640×320 `BACK_nnn` background.

## 8. Name tables — WORD / MAPNAME / VARNAME / SWNAME

Each is an array of 32-byte strings (confirmed).

| File | Entries | Content |
|---|---|---|
| WORD.DAT | 100 | UI vocabulary, parsed by `parse_word` into `{index, label, text}`. See the label table below. The game's currency name is "肉" (meat): entry 31 ",肉" |
| MAPNAME.DAT | 100 | Editor map names. Index n ↔ `SMAP_{n+1:03d}` (confirmed: equals each SMAP's own name field) |
| VARNAME.DAT | 500 | Editor names of the 500 variables |
| SWNAME.DAT | 500 | Editor names of the 500 switches |

**WORD.DAT labels.** All confirmed from the Vocabulary Editor's four "Term" tabs (T7, T7b) by matching each text box to its entry. Every one of the 76 labels matches the stored text. The full index → label map is `WORD_LABELS` in the parser. Term 1:

| Index | Editor label | This game's text |
|---|---|---|
| 1 | Level | ＬＶ |
| 2 | HP | ＨＰ |
| 3 | MP | ＴＰ |
| 4 | Attack | 攻撃力 |
| 5 | Attack Speed | 攻撃精度 |
| 6 | Defense | 物理防御 |
| 7 | Magic Defense | 特殊防御 |
| 8 | Strength | 攻撃補正値 |
| 9 | Magic | 特殊補正値 |
| 10 | Skill | 命中回避値 |
| 11 | Stamina | 耐久力 |
| 12 | Weapon | 武器 |
| 13 | Armor | 防具 |
| 14 | Accessory | 装飾品 |
| 15 | End (unit action) | 行動終了 |
| 16 | Attack (command) | 攻撃 |
| 17 | Magic (command) | 必殺技 |
| 18 | Item (command) | アイテム |
| 19 | End Turn | ターン終了 |
| 20 | View Map | 全体マップ |
| 21 | Save | セーブする |
| 22 | Load | ロードする |
| 23 | Exit | ゲーム終了 |
| 45 | Yes | はい |
| 46 | No | いいえ |
| 75 | Terrain Effect | 地形効果 |
| 89 | Equipment | 装備 |

The tab layout is not in index order (Yes/No/Terrain Effect/Equipment sit far from 1–23).

The other three tabs:

| Tab | Indices | Contents |
|---|---|---|
| Term 2 | 24–40 | camp menu: Continue, Organize Units, Buy/Sell Items, Cost, Sell Amount, Owned, Currency, Unequip, Change Weapon/Armor/Accessory, Use Item, View Magic, Unit Slots, Slot, Stay |
| Term 2 | 47 | New Game |
| Term 2 | 81–88 | File 1–8 (save slots) |
| Term 3 | 52–58, 60–62 | Finish Turn, Select Save File, Select File To Load, Finish Camp Line 1/2, Quit Game, Was Used, Exp. Earned, Gold Earned, Was Found |
| Term 4 | 63–74, 76 | Page, However, Point, Recovered, Strength Increased, Increase, Decrease, Reached Level, Became, Promoted To, Learned, Removed Poison, Woke Up |

Several of these are sentence fragments the engine glues to names and numbers (e.g. unit + 70 "はレベル" + n + 71 "になった"). The exact assembly order needs to be checked in the running game.

**Entries with text but no editor box:** 41–44 (ファイル１–４), 48–51 (ファイル１–４をロード), 59 (を装備します). They look like leftovers from an older 4-slot save screen. This editor exposes 8 slots at 81–88. Whether the runtime still reads them is unknown. Index 0 and 77–80, 90–99 are empty.

## 9. MAPC_nnn.DAT — 384 B

One u8 per chip of `MAPC_nnn.BMP`, indexed by **chip number** (same numbering as map tiles, see §10), value = GEOLOGY id. Inferred (strong): sizes match exactly and the max value is 69 (70 terrains).

## 10. SMAP_nnn.DAT — maps (variable size, always a multiple of 100 B)

### Header 0x000–0x4AF

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x000 | 32 | str | name | confirmed | = MAPNAME[n-1] |
| 0x020 | 4 | i32 | width (19–40) | inferred (strong) | all tiles outside w×h are 0 in all 79 maps |
| 0x024 | 4 | i32 | height (14–40) | inferred (strong) | ″ |
| 0x028 | 4 | i32 | tileset (MAPC_%03d) | inferred (strong) | values 1, 2, 3, 9, 13, 25 all exist as MAPC files |
| 0x02C | 4 | i32 | win_condition: 0 Defeat All Enemies, 1 Defeat Enemy Leader, 2 Event | 0/1 confirmed, 2 inferred | T8: "Defeat All Enemies" = 0, "Defeat Enemy Leader" = 1. 2 is the only remaining dropdown option and appears in 6 maps |
| 0x030 | 8×48 | str | BGM: camp, player turn, enemy turn, player battle, enemy battle, boss battle, victory, game over | confirmed | SMAP_001: コミカル, コミカル, 敵行動, 戦闘, 戦闘, ﾎﾞｽ戦闘, ミッションクリアー, ミッション失敗 |
| 0x1B0 | 4 | i32 | camp_before_battle (0 No, 1 Yes) | confirmed | T8: No → Yes changed 0 → 1. The editor says camp is impossible on the 1st map |
| 0x1B4 | 400 | i32[100] | shop_items (ITEM ids, 0-based). Only the first shop_item_count entries are valid | confirmed | T8: "item 001 then item 005" → [0, 4] |
| 0x344 | 4 | i32 | shop_item_count | confirmed | T8: 0 → 2. Across the 79 maps, SMAP_006 and SMAP_019 have stale ids past the count, so always use the count |
| 0x348 | 4 | i32 | start_pos_count | inferred | SMAP_003: 4 positions at x 27–28 on a 30-wide map |
| 0x34C | 80 | i32[20] | start_pos_x | inferred | |
| 0x39C | 80 | i32[20] | start_pos_y | inferred | |
| 0x3EC | 80 | i32[20] | character_list (UNIT ids: forced participants / game over if killed) | inferred (strong) | ids 239–246 = main cast サラ, ルーティ, ハンス, ゴーバック… |
| 0x43C | 4 | i32 | character_list_count | inferred | |
| 0x440 | 4 | i32 | camp_background (CAMP_%03d) | confirmed | T8: 7 → 3 |
| 0x444 | 4 | i32 | unknown_444 (always 1) | unknown | stayed 1 after the T8 redo |
| 0x448 | 4 | i32 | unknown_448 (rare: 74, 254, 318) | unknown | |
| 0x44C | 4 | i32 | unknown_44c (rare: 46, 94, 96, 250) | unknown | |
| 0x450 | 4 | i32 | disable_party_changes (1 = ticked) | confirmed | T10: screenshot shows it ticked on map 1 (stored 1). Unticking + Save current map stored 0. (The T8-redo untick didn't take; the box is greyed out while Camp Before Battle = No) |
| 0x454 | 92 | — | (always zero) | — | |

Lists keep stale entries past their count (e.g. 4 x-coords with count 1), so always honour the count field.

### Tile grid 0x4B0–0x112F (confirmed layout)

40×40 uint16, row-major, stride 40, independent of width. Value = chip number in MAPC_tileset.BMP (0–383). Single layer; no separate upper layer has turned up.

**Chip numbering (confirmed against an editor screenshot of map 1):** the 12 × 32-chip sheet is two 6-column halves, the editor's "A" and "B" tabs. Chip k < 192 is at column k % 6, row k / 6 of the left half. Chip k ≥ 192 is at column 6 + (k−192) % 6, row (k−192) / 6. An earlier straight 12-wide reading produced scrambled maps.

### Event area 0x1130–EOF

Decoded in phase 2. See **§11**. Layout summary (confirmed on all 79 maps):

| Off | Size | Content |
|---|---|---|
| 0x01130 | 100 × 164 | map objects (unit placements and "Other" events). Slot 0 is never used |
| 0x05140 | 5000 × i32 | block chain table (FAT): next block index, -2 = end of chain, -1 = free |
| 0x09F60 | 5000 × 16 | file directory: `char name[8]`, `i32 first_block`, `i32 size_bytes`. All-zero = empty |
| 0x1D7E0 | n × 100 | data blocks |

File size is always `0x1D7E0 + 100 × (number of non-free FAT entries)`. The parser still writes the raw area to `json/map/smap_nnn.events.bin` as a backup.

## 11. Map objects and event scripts (phase 2)

Phase 2 progress:
1. Storage (objects, internal file system, command framing): done.
2. Command payloads: done for every command the game uses.
3. Script trees + cross-references: done (§11.4).
4. Checks in the running game: open (see Active tests).
5. Save files: not started, optional.

Sources: 79 game maps + 7 Kikumaru maps (+ RTP maps, which have no events), and the help pages "Creating A Event" (incl. the Object Editor and Event Start Conditions screenshots) and "Event Editor Commands".

### 11.1 Map object — 164 B, 100 slots at 0x1130

Every non-zero byte is covered (1,608 objects checked).

| Off | Size | Type | Field | Conf. | Evidence |
|---|---|---|---|---|---|
| 0x00 | 4 | i32 | in_use: 1, or 0 = deleted (data left stale; 308 of 1,552 game objects) | inferred (strong) | |
| 0x04 | 4 | i32 | x (tiles) | inferred (strong) | within map width |
| 0x08 | 4 | i32 | y | inferred (strong) | within map height |
| 0x0C | 4 | i32 | type: 0 Enemy, 1 Ally, 2 Neutral, 3 Other | inferred (strong) | Object Editor dropdown order. Ally objects hold the main cast (unit ids 236–249) |
| 0x10 | 2 | i16 | switch_condition (switch id, -1 none; high half always 0) | inferred | values 19–103 ≤ 500 switches |
| 0x14 | 4 | i32 | unit (UNIT id) | inferred (strong) | SMAP_001: 242 ルーティ, 243 ジュナ, 3/4 ナス (whose dialogue they own) |
| 0x18 | 4 | i32 | graphic (tileset chip for "Other", -1 none) | inferred | only type 3 uses values ≠ -1 (0, 143, 190) |
| 0x1C | 4 × 10 | i16 × 5 | move_patterns[4]: switch, action (0 Rush, 1 Wait, 2 Specific, 3 None), x, y, switch_to_apply | inferred | pattern 1's switch is always -1 (greyed out in the editor). The one action-2 object has a location set |
| 0x44 | 4 | i32 | leader | inferred | 44 enemies, never Other |
| 0x48 | 4 | i32 | guest | inferred | 59 allies |
| 0x4C | 4 | i32 | reward_item (ITEM id, -1) | inferred (strong) | 40 ポーション, 70 エーテル薬 … |
| 0x50 | 4 | i32 | reward_gold | inferred (strong) | 20, 40, 1000 … |
| 0x54 | 5 × 8 | i16 × 4 | event_slots[5]: condition, switch (0-based id), turn, target_unit (UNIT id, -1 = Nothing) | confirmed | T11: object 2's slots read back exactly as set (see the condition codes below) |

**Event-slot condition codes.** The codes are *not* in dialog order, and 3 is never used.

| Code | Condition | Conf. | Evidence |
|---|---|---|---|
| 0 | Do Not Perform | confirmed | 7,624 empty slots |
| 1 | Switch (uses `switch`) | confirmed | T11: switch 007 → cond 1, switch 6 |
| 2 | Turn (uses `turn`) | confirmed | T11: Enemy turn 3 → cond 2, turn 6 |
| 4 | First Contact (Any Time) | confirmed | T11, target ルーティ (unit 99) |
| 5 | When Unit Dies | confirmed | T11. Also ルーティ's slot-5 script is her "lost already?" line |
| 6, 7, 8 | Unit Passes By / Unit Stops On It / Victory Conditions Met | inferred (order) | used only by "Other" objects |
| 9, 10, 11 | First Contact (After Movement) / First Attack (Before) / First Attack (After) | inferred | used only by unit objects. 10 is likely "Before Attack" (the T11 screenshot showed it selected with target ナス, matching ルーティ/ジュナ's cond-10 slots that target ナス) |

**Turn encoding:** `turn` counts phases. Ally turn t = 2t−1, Enemy turn t = 2t. Confirmed for enemy 3 → 6 (T11) and the default Ally 1 → 1. Both odd and even values occur in the game.
| 0xA0 | 4 | i32 | unattackable | inferred | set only on 2 neutral objects |

### 11.2 Event files

Names (confirmed on all 469 files):
- `OPENING` is the map's opening event.
- `EVoooo ss` is object `oooo` (0-based slot index), event slot `ss` (1–5). Every one of the 392 EV files maps to an existing object.

Files are read by following the FAT chain from `first_block` and truncating to `size`.

**Damaged files in the shipped game.** The editor sizes the map file by the *count* of used blocks without compacting them. When the FAT has free holes, the highest-numbered blocks are cut off. In **SMAP_056** this ruined three scripts:
- `EV001701` is missing 7 of its 12 blocks.
- `EV003601` and `EV003602` point at blocks that now hold the middle of other scripts.

The parser reports them with `error` / `missing_blocks`. Kikumaru's SMAP_006 `OPENING` has the same defect.

**T13 (provisional):** for now, assume the intended dialogue plays. This is a placeholder, **not verified** against the original runtime, and has to be checked later by playing episode 32. If the dialogue does play, the original must recover the text some other way than the FAT chain, and that needs investigating. `EV001701`'s first 5 blocks (≈500 bytes) are intact and decodable up to the cut. `EV003601`/`EV003602` currently decode to the middle of other scripts.

**File layout:**

| Off | Size | Field |
|---|---|---|
| 0 | 100 | comment (str). Editor-only label, e.g. `OPENING` = "オープニングイベント" |
| 100 | 4 | stream length n |
| 104 | n | command stream |

The stream is a sequence of commands followed by one `0x00` byte. The last command is always `00` (end_of_script, len 0). **Command framing** (confirmed; 465 of 469 files re-encode byte-identically, and the 4 that don't are the damaged files above):

| Off | Size | Field |
|---|---|---|
| 0 | u8 | opcode |
| 1 | u16 | payload length |
| 3 | u8 | nesting depth (0–6) |
| 4 | len | payload |

**Block structure** (confirmed on every script; there is no "else"):
- `2A` conditional_branch at depth d → body at d+1 → `C7` end_block (d+1) → `CA` end_branch (d)
- `2D` character_choice at d → repeated [`CC` choice_case (d, payload u8 case index) → body (d+1) → `C7` (d+1)] → `CD` end_choice (d)

Only `2A` and `CC` open a deeper level (964 + 306 = 1,270 = number of `C7`).

### 11.3 Opcodes

**All opcode names are now confirmed by T12**: one of each command, in a known order, matching the editor's own command list. Payload fields are confirmed where T12 set a distinctive value, and the rest are marked. All IDs are 0-based record indices (the editor shows +1). Counts are across this game's 443 intact scripts.

| Op | n | Name | Payload | Field status |
|---|---|---|---|---|
| 00 | 443 | end_of_script | — | confirmed |
| 01 | 12,474 | message | u16 window, str text | confirmed: window 0 = top "(AB)", 1 = centre "(CE)", 2 = bottom "(UN)" (T12 choice messages) |
| C9 | 18,783 | message_line | u16 window, str | confirmed: further lines of the preceding message |
| 02 | 11,231 | face_graphic | u8 position, u8 flip, u16 face | confirmed: position 0 left / 1 right, flip 1 = mirrored (T14: Right → 1/0, Left+Flip → 0/1, Right+Flip → 1/1) |
| 03 | 507 | switch_operation | u16 on, u16 switch | confirmed: 007 ON → (1, 6), 008 OFF → (0, 7) |
| 04 | 51 | item_management | u16 add, u16 item, u16 count | confirmed: add item 005 ×3 → (1, 4, 3). 0 = remove inferred |
| 05 | 9 | gold_management | u16 add, i32 amount | confirmed: −1234 → (0, 1234). 1 = add inferred |
| 0B | 233 | screen_display | u8 color, u8 effect | confirmed: color 0 from black, 1 from white. effect = dropdown index (1st = 0, 2nd "Random Blocks" = 1) |
| 0C | 229 | erase_screen | u8 color, u8 effect | same as 0B |
| 0D | 230 | picture_display | u8 method, u8 position, u8 remove_transparent, u8 ×3, str file | confirmed. method = dropdown index (0 Instantaneous). **position uses numeric-keypad layout** (7 8 9 / 4 5 6 / 1 2 3): Upper Left 7, Center 5, Lower Right 3 tested. So the game's common 8 = "Above" and 2 = "Below". remove_transparent 1 = ticked. The last 3 bytes are always 0 |
| 0E | 77 | erase_picture | u16 method | 0 = Instant confirmed |
| 10 | 181 | scroll_screen | u16 x, u16 y | confirmed (5, 9) |
| 12 | 391 | flash_tint_screen | u16 mode, i16 red, green, blue, vividness | confirmed: mode = Type dropdown (Flash 1/2/3 = 0–2, Fast/Medium/Slow Change = 3–5). Red/Green/Blue are 0–200 %. Vividness is 0–100 % (0 = monochrome) per the dialog |
| 13 | 342 | shake_screen | u16 strength | confirmed: 0 Weak, 1 Medium, 2 Strong |
| 16 | 35 | change_ally_unit | u16 add, u16 unit | confirmed: Added unit 005 → (1, 4). 0 = remove inferred |
| 17 | 87 | unit_class_change | u16 unit, u16 class | confirmed (4, 6) |
| 18 | 20 | unit_equipment_change | u16 unit, u16 item | confirmed (4, 1). Slot is implied by the item's type (inferred) |
| 1F | 22 | move_unit | u16 unit, u16 n, u8 step[n] | confirmed: steps 0–3 = move up/right/down/left, 4–7 = face up/right/down/left (T12 sequence and T14 "Up Up Left Right Down" → 0 0 3 1 2) |
| 20 | 18 | execute_movement | — | confirmed |
| 24 | 721 | bgm | str file | confirmed. `(OFF)` = stop |
| 25 | 1,780 | sound_effect | str file | confirmed |
| 29 | 562 | variable_operation | u16 variable, u16 source, i16 constant, u16 source_variable, u16 random_max, u16 operation | confirmed: operation 0 Set, 1 Added, 2 Subtracted, 3 Multiplied, 4 Divided (T14). source = radio button (0 Numerical Value, 1 Variable, 2 Random Number Value; 0 and 1 tested, 2 from dialog order). **The editor keeps all three source fields**, so only the selected one applies (e.g. game "var 008 = 28" also carries a stale variable 9 and random 9) |
| 2A | 964 | conditional_branch | u16 cond_type, u16 switch, u16 turn, u16 item, u16 unit, u16 variable, u8 switch_state, u8 turn_mode, u8 item_state, u8 unit_state, i16 compare_value, u16 comparison, i32 0 | confirmed (T14 + T15). cond_type 0 Switch, 1 Turn, 2 Item, 3 Unit, 4 Variable. Like 0x29, every kind's fields are kept and only cond_type's apply. Option codes:
- switch_state: 0 ON, 1 OFF.
- turn: phase-encoded like event slots (ally t = 2t−1, so Ally 1/2/3 → 1/3/5). turn_mode: 0 After, 1 Before, 2 Exact.
- item_state: 0 Party Possesses, 1 Party Does Not Possess.
- unit_state: 0 Alive, 1 Dead, 2 Not Present, 3 Controlled by Player, 4 Controlled by Computer.
- comparison: 0 "= OR >" (≥), 1 "= OR <" (≤), 2 "=".

The parser adds `cond` and `test_*` fields naming only the active condition |
| 2D | 93 | character_choice | u16 n, n × str | confirmed |
| 33 | 7 | button_input | — | confirmed (no variable in this version) |
| 34 | 1,022 | wait | u16 tenths of a second | confirmed: 25 → "Wait For 2.5 S" |
| 36 | 42 | select_map ("Next Map") | u16 map | confirmed: 7話 → 6 (0-based) |
| 38 | 210 | end_event | — | confirmed |
| 3A | 90 | map_complete | u16 win | confirmed: 1 = Win; 0 = Lose inferred |
| 3B | 10 | game_over | — | confirmed |
| 3D | 3 | return_to_title | — | confirmed |
| C7 / CA / CC / CD | | end_block / end_branch / choice_case (u8 index) / end_choice | | confirmed (structure, see 11.2) |

Play Movie was not tested and doesn't occur in this game.

The parser keeps every payload as hex (`raw`) and adds named fields per the table above. Payloads it can't name fall back to a neutral `u16` list.

### 11.4 Script trees and cross-reference check (`srpg95_scripts.py`)

A second module builds on the parser and turns each map's flat command lists into what an engine will run:

```bash
python re/srpg95_scripts.py えるまに re/json "srpg maker 95/RTP"
```

For every map this writes `json/scripts/smap_nnn.json` (objects + script trees) and `smap_nnn.txt` (a readable listing, useful for review and translation). All findings go to `json/scripts/report.txt`.

**Tree shape:**
- A `message` node has `window`, `window_name` and `lines`. The first line usually starts with the speaker, e.g. `ルーティ：`.
- `conditional_branch` has `cond`, `test_*` and `then`.
- `character_choice` has `options` and one body per option in `cases`.
- Block markers and end_of_script are absorbed into the structure.
- Every ID gets a `*_name` (unit, item, class, switch, variable, map), faces get `face_file`, picture positions get `position_name`, and variable ops get `source_name` / `operation_name`.
- Objects list only their used event slots, with the condition named and `script` pointing at the tree.

**Result on this game:** all 443 intact scripts build with **zero structure problems**. Every ID is in range and every BGM/picture/face file exists in the game or RTP folders, with these exceptions, all quirks of the shipped game:

| Where | Finding | Client implication |
|---|---|---|
| SMAP_056 EV001701, EV003601, EV003602 | damaged scripts (§11.2) | T13 provisional: assume dialogue plays; revisit |
| SMAP_067 EV000201 | sound `空湾曲1.wav` exists in neither EFS folder | play nothing (presumed original behaviour) |
| SMAP_041 OPENING | the "deploy ハンス and ゴーバック" guard: the ハンス branch restarts map 41 correctly, but the ゴーバック branch's Next Map is index 99 = **SMAP_100**, an empty map. An authoring bug | reproduce by default; candidate for an optional fix |
| SMAP_021 object 18, SMAP_037 object 1 | slot 1 has a start condition but no script | nothing happens |

Kikumaru: only its known damaged SMAP_006 `OPENING`.

## 12. Small files

| File | Layout | Findings |
|---|---|---|
| GAME.DAT (200 B) | i32[50] | [0] = 98 here and 0 in Kikumaru: maybe the **start map** (0-based → SMAP_099 "０話" prologue). Inferred, and slightly at odds with the editor's "Unable to Camp on 1st Map" wording. [1]..[4] unknown (game: 0,1,0,0; Kikumaru: 1,1,1,50). This editor version has no Game Properties dialog: the Game menu holds only Test Play Game (F9), Test Play Current Map (F8) and "Launch In Full Screen". GAME.DAT was not rewritten during any test session. T9b: toggling "Launch In Full Screen" and reopening the project does not change GAME.DAT, so that setting is stored outside the game data (registry or editor config, not investigated). [0] = start map stays inferred; [1]..[4] remain unidentified |
| TEST.DAT (6000 B) | i32[1500] | Editor test-play config: 1,1,0,5,40,49,0… (RTP-like defaults). Not needed by the runtime (inferred) |
| EDITOR.DAT (4 B) | i32 | Last map opened in the editor, 0-based. Confirmed: 58 (SMAP_059) became 0 after the T10 session ended on map 1. Irrelevant to the runtime |

---

## 13. Graphics conventions and save files

Learned while building the client (phase 3):

| Item | Finding | Conf. |
|---|---|---|
| BMP format | All 1,026 game + RTP BMPs are BITMAPINFOHEADER, 8-bit, uncompressed, bottom-up (2 `Thumbs.db` aside) | confirmed |
| Transparency | palette **index 0** is the colour key of sprite sheets (fills cell backgrounds; most common index in UNIT/CA/MAGA/SYSTEM) | inferred (strong) |
| MAPC_nnn.BMP | 12 × 32 chips of 32×32 in two 6-column halves. Chip k → left half rows for k < 192, right half for k ≥ 192 (§10) | confirmed: user's editor screenshot of map 1 matches exactly. The first version used a 12-wide row-major reading and was **wrong**; the user caught it |
| UNIT_nnn.BMP | **3 columns × 5 rows of 64×64 cells**, the 32-px character centred, so it overhangs its tile by 16 px each side. Columns are animation frames. Rows are poses: row 0 faces the viewer, row 1 faces away, rows 2/4 are side poses. Row 3's meaning and the exact direction mapping are open | inferred |
| SYSTEM.BMP | UI atlas: window skin, "END" label, cursor arrows, blue/red damage digits 0–9, PSN/SLP status badges, coloured range-overlay tiles | inferred (visual) |
| Save files | see §14 | — |

**Screen layout of the runtime** (all confirmed by pixel comparison with T17 screenshots, parser/client match ≥ 99.6 % of pixels and 100 % of text pixels):

| Element | Layout |
|---|---|
| Screen frame | SFRAME_H/V strips, 16 px on each side. Map viewport 608×448 at (16,16). Strip variants: rows/columns 0–15 = red arrow, 32–47 = grey (red presumably = can scroll that way) |
| Erased screen | the viewport fills with (16,16,0); the frame stays |
| Message window | 414×126 including a 1-px outline, at y 33 / 177 / 321 (top / centre / bottom). Fill (184,200,160), rounded radius ≈ 8, outline light (241) on the top/left and dark (96) on the bottom/right |
| With a face | window at x 193; face 128×128 at (32, y−1); a speech tail on the window's left edge 13 px below its top |
| Without a face | window at x 113 (centred) |
| Text | GDI **MS Gothic 18 px bold**, no anti-aliasing, colour (16,16,0), origin (window x + 17, y + 11), 22 px line spacing. Appears all at once |
| Mouse cursor | SYSTEM.BMP arrow (128,0)+n·32, 20×20, hotspot (0,0); hourglass (128,32)+n·32 while a message waits |
| Fades (screen display / erase) | about 0.5 s (T17b) |
| Textured panels | SYSTEM.BMP bottom row has four 64×64 tiles at y 224: blue x 0, red x 64, yellow x 128, green x 192. The tile is repeated from the panel's top-left corner (green verified on 400 sampled pixels). Edges are 1 px: light (248,248,224) top/left, dark (0,32,0) bottom/right. Panel text is white (248,248,248) |
| Choice menu | green panel. Width = widest option + 24, height = 16·n + 16, bottom edge at y 303, x = 292 − w/2 (one sample; rule provisional). Text MS Gothic **12 px regular**, origin (+13, +10), 16-px lines. The message that asked the question stays visible underneath. No highlight shows while the mouse is away. Client matches 99.7 % of the panel's pixels |
| Camp menu | green panel 96×129 at (16,16): 次のステージ, ユニット編成, アイテム購入, アイテム売却, セーブする, ロードする, ゲーム終了 (WORD.DAT 24–27, 21–23). The background is a full-screen picture (CAMP_nnn) |
| Save / load list | green panel 96×177: header ("セーブします" / "ロードします", WORD 53/54), a separator line, then ファイル１–８ (WORD 81–88). Empty slots are greyed in the load list |
| EXP message | green panel 240×33 at (200,64): "{n}の経験値を獲得" (WORD 60) |
| Acted unit | "END" badge from SYSTEM.BMP drawn on the unit |
| Battle screen | BACK_nnn background in the top 320 px, attacker/defender sprites from CA sheets. Bottom half: red panel (enemy, left, x 0–319) and blue panel (ally, right, x 320–639), each with name, HP and TP bars ("21／21"), 攻撃力/攻撃精度/物理防御/特殊防御/特殊補正値/地形効果 |
| Unit hover | name and "HP／max" in small text above the unit, white square cursor on its tile (screenshot 5; to be implemented) |
| Status window | blue panel with WORD.DAT labels: LV (EXP), HP, TP, Attack/Accuracy/Defense/Magic Defense, the 4 base stats, equipment, and a face (screenshot 6; to be implemented) |

Open: the camera position when a map starts. On map 4 the original showed camera tile (3,4), which isn't the map centre or a start position. It may depend on how the map was entered (editor test play?). To revisit with the battle-start logic.

## 14. Save files — `SAVEn.DAT` (114,400 B, fixed)

Written to the game folder, one file per save slot. There are no timestamps: saving the same state twice gives identical files (T16). Little-endian int32 throughout.

| Off | Size | Content |
|---|---|---|
| 0x00000 | 2000 | header |
| 0x007D0 | 100 × 640 | unit state of each **map object slot** (record i = SMAP object i) |
| 0x101D0 | 50 × 640 | **roster**: the player's army between maps (map_object = −1) |
| 0x17ED0 | 100 × 164 | copy of the current map's object table (SMAP §11.1 layout) |

**Header** (known fields; everything else zero in all 5 test saves):

| Off | Field | Conf. | Evidence |
|---|---|---|---|
| 0x000, 0x004 | 1 at camp, else 0 | unknown | SAVE4 only |
| 0x000 | 1 when saved at a camp, 0 during a battle | inferred | SAVE4/6/7/8/5 at camp = 1, SAVE1/3/5-old mid-battle = 0 |
| 0x004 | camps reached: 1, 2, 3, 4 | inferred | one more each chapter (T18b) |
| 0x008 | 7 / 1 / 2 across saves | unknown | |
| 0x00C | 0, 6, 4, 5 across saves | unknown | |
| 0x010 | map number (1-based) | inferred | map 1 battle = 1, camp before map 2 = 2, camp before map 3 = 3 |
| 0x014 | **switches**: 500 bytes, one per switch, 1 = ON | confirmed | the prologue's switch 79 「ＯＰ」 = byte 0x63 in every save; map 2's switches 0 and 1 = bytes 0x14, 0x15 (T18) |
| 0x20C | phase counter (ally turn t = 2t−1) | inferred | 1 at start, 7 mid-battle, 13 at the end |
| 0x210 | **inventory**: 100 × (i32 item, i32 count) | confirmed | buying one ポーション wrote (40, 1) (T18); a second one made it (40, 2), not a new entry (T18b) |
| 0x530 | inventory entries used | inferred | 0 → 1 after the purchase |
| 0x534 | gold | confirmed | 20 = in-game gold (T16) |
| 0x538 | party size | inferred | 2 (ルーティ, ジュナ) |
| 0x53C | current BGM file name (48 B) | confirmed | コミカル.MID in battle, 安らきﾞ.MID at camp |
| 0x56C | screen tone R, G, B, vividness | inferred | 100,100,100,100 |
| 0x57C | unknown (5, 0, 1 across saves) | unknown | was guessed as the object count, but it's 1 on map 4 |
| 0x5DC | **variables 0–249**, int16 each | inferred | T18c: choosing the option that adds 1 to variable 19 set byte 0x602 = 0x5DC + 19·2, and nothing else in 0x580–0x7CF. Scripts only use variables 0–33 (names exist up to 233), so 250 slots is plenty. Unresolved: 8-bit storage at 0x5EF would also put var 19 at 0x602; a save with variable 0 set (11話) settles it |

**Variables** were located with T18c: 16-bit values at 0x5DC (see the header table).

**Unit record** (640 B, int32 index [n] = byte offset 4n):

| Index | Field | Conf. | Evidence (T16 + status screenshot) |
|---|---|---|---|
| [0] | in use | inferred | |
| [1] | present (on the map, alive) | inferred | defeated enemies → 0 |
| [2] | acted this turn | confirmed | 0 → 1 after ジュナ moved |
| [3] | computer-controlled | inferred | 1 for enemies |
| [4] | unknown_010, always −1 | unknown | |
| [5], [6] | x, y | confirmed | ジュナ y 12 → 11 |
| [7] | map object index (−1 in the roster) | confirmed | |
| [8] | unit id | confirmed | 242 ルーティ, 243 ジュナ |
| [9] | class id | inferred | ルーティ 0, ジュナ 1, ナス 2, minions 3 |
| [10] | level | confirmed | ルーティ 1; ジュナ 2 → 3 over map 1 |
| [11] | EXP | confirmed | ルーティ 49 |
| [19] | movement | inferred | class movement 4 / 6 |
| [20]–[25] | base stats: max HP, max MP, Str, Mag, Skl, Sta (no equipment) | confirmed | ジュナ 22, 6, 9, 3, 8, 2 |
| [26]–[31] | current HP, current MP, Str, Mag, Skl, Sta incl. accessory | confirmed | ジュナ Mag 3 + イヤリング 2 = 5. Defeated → HP 0 |
| [32]–[35] | Attack, Accuracy, Defense, Magic Defense | confirmed | ジュナ 11/17/3/2 = her status window |
| [40]–[42] | weapon, armor, accessory (ITEM ids) | confirmed | ジュナ 銀のスプーン / 普通の服 / イヤリング |
| [43], [44] | weapon range min, max | inferred | ルーティ 1–2, ジュナ 1–1 |
| [50] | movement left this turn | confirmed | 6 → 5 after a 1-tile move |
| [54], [55] | position before moving this turn | confirmed | (7,12) |
| [52] | unknown battle leftover (2 on ジュリア in SAVE5) | unknown | |
| [56] | unknown (set on defeated units) | unknown | |
| [60]–[129] | movement cost per terrain (70): the class override, else the terrain's cost for the class's move type | confirmed | rebuilt exactly for every roster unit of every camp save |
| [130]–[149] | known spells (MAGIC ids, in the class skill list's order) | confirmed | グレイ 8 (活性波); ジュリア 1 (剣の舞), then 1, 2 (虎伏演舞) at level 4 |
| [150] | number of known spells | confirmed | 1 → 2 for ジュリア |
| [13] | 1 on roster copies | inferred | every roster record |

**Battle saves** (header 0x000 = 0; SAVE1, SAVE3):
- Map-unit records exist only for units that have appeared, starting at index 1: units deployed from the army first, then map objects in order. [7] is the object index (−1 for army units) and [4] the army unit's deployment slot (ルーティ 0, ジュナ 1), else −1.
- Computer units' [50] (movement left) is 0 during the player's phase.
- The object-table copy at 0x17ED0 is the map's own, except that a spawned object's in-use word is 0, and **+124 + 2·(slot−1) is a 16-bit 'this event slot has fired' flag**. ルーティ's death and first-attack slots and ジュナ's first-attack slot on map 1 were set.
- The client writes battle saves the same way. Re-saving SAVE3 differs only in leftovers it can't know: an army unit's movement left and position before moving from earlier play, and ジュリア's [52] = 2 (maybe facing). Loading restores the units, phase, switches, inventory and fired events. Status effects and buffs aren't known to be in the save and restart clear.

**Writing saves (client):** a camp save sets header 0x000 = 1, 0x004 (camps), 0x010 (next map, 1-based), switches, inventory and its count, gold, party size, BGM name, tone and variables. Each roster record is rebuilt from unit, class, level, EXP, base stats and equipment: in-use flags, stats with the accessory, Attack/Accuracy/Defense/MDef, range, movement, terrain costs and known spells. Fields that are leftovers of the last battle ([5], [6], [50], [52], [54]–[56]) and unknown header fields are kept from the slot's previous file or the last save loaded. Checks (`SaveFileTests`):
- Re-saving SAVE6 at its own camp reproduces the original file byte for byte.
- Every camp save's roster records rebuild from nothing exactly, apart from those leftovers.

Python: `parse_save()` in `re/srpg95_data.py`.

## 15. Battle runtime (client behaviour, mostly provisional)

What the client does on a battle map (`client/src/Srpg95.Data/Rules/`, `client/src/Erumani/BattleController.cs`). "Data" rows follow from the files or the help text; T19 confirmed several against the original; T20 covers the rest.

| Behaviour | Client | Basis |
|---|---|---|
| Stat formulas (Attack, Accuracy, Defense, MDef) | §14 formulas | confirmed on 172 save records |
| Hit, crit, damage, double attack, EXP, level-up | help "Battle Algorithm" formulas | help text, not yet checked in play |
| Movement range | Dijkstra over the class's terrain costs; enemies block, allies can be passed but not stopped on | inferred |
| Undo a move | cancel at the command menu returns the unit to where it started | confirmed (T19b) |
| End of player turn | only through ターン終了, from the menu opened by clicking an empty tile, even when every ally has acted | confirmed (T19a) |
| Range overlay | SYSTEM.BMP range tile (16-px two-colour checker) drawn on the tile's **odd pixel rows only**, opaque; even rows show the map. Move = cyan tile (x 0), attack = salmon tile (x 64), covering the weapon's whole range, not just the enemies. Units are drawn on top | confirmed, pixel-matched (T19c) |
| Strike results | one-line popups (damage, miss, 撃破, EXP, level-up) until the battle-scene milestone | placeholder |
| Death events vs victory | a unit's "dies" events play first, then the victory events, then COMPLETE!! | confirmed on map 1 (T19e) |
| Victory | `TELOP3.BMP` "COMPLETE!!" (512×96, index 0 transparent) with the victory fanfare (map BGM slot 6), then the camp menu | confirmed (T19e, T20d): it slides in from the right like the turn captions and stays about 9 s (the fanfare) in the middle. The client also lets a click skip it. The camp is milestone 6, so the client goes straight to the next map |
| Turn captions | `TELOP1.BMP` "PLAYER TURN" / `TELOP2.BMP` "ENEMY TURN" at each phase start: slide in from the right, pause about a second in the middle, leave | confirmed (T20a). The client slides for 16 frames, holds 60 and slides out left; speeds and exit direction are guesses. PLAYER TURN after the opening is also a guess |
| Turn music | player-turn BGM (map slot 1) / enemy-turn BGM (slot 2) start with each turn caption, replacing whatever an event played; the music also changes when a battle starts | confirmed (T20b). Battle music comes with the battle-scene milestone |
| Game over | `GAMEOVER.BMP` full screen | confirmed (T20c) |
| Losing | every ally down, or a unit from the map's character list down. Map 1 goes on without ルーティ: EV000101 is just her remark | confirmed for map 1 (T19d) |
| Winning without a "Next map" command | goes on to the next map number | guess |
| Spells known | the class's skill list: every spell whose level the unit has reached. Reaching one on level-up shows 「〜を習得した」 (WORD 73) | data + inferred |
| Spell targeting | the range grid around the caster, or the whole map. The aim must catch at least one unit the spell affects. The effect grid around the aim, or the whole map, is hit. Target 1 = enemies only for Damage/Degrade, own side for Raise; target 0 = everyone in the area. A harmful spell never hits its caster | grids confirmed (§5). The rest is a guess (T21) |
| Spell effects | Damage: magic hit/damage formulas, no counter. Degrade: hit roll; class resist or accessory "Safe Against" blocks it. Poison 3 turns; sleep loses 2 of the unit's turns and it acts on the 3rd; Atk/Def Down = −(Value + Mag·Relation/10). Raise: HP heal (capped), Atk/Def Up = Value + Mag·Relation/10, cure poison, cleanse, extra turn (target may act again). Buffs decay 1 per own turn. A spell whose effect grid has a hollow centre doesn't touch the aim tile (剣の舞: 「術者は含まない」) | 剣の舞 +3 and the 1-per-turn decay confirmed (T21c, T21d). The rest is help text |
| Spell EXP | Damage: like an attack (kill or hit, against the highest-level unit caught). Degrade/Raise: the spell's Exp Value per cast. From an item: none | Raise Exp Value confirmed (剣の舞 25, T21c). The rest is a guess |
| Battle items | ITEM types Medicine and Magic, if the user's class may use them. Medicine targets the user's side within its range (0 = self): HP/MP recovery, Def/MDef Up (resets the counter, cures Def Down), cures. A magic item casts its spell with no MP cost. Using one ends the action and uses up one | help text. Stat-up and class-change items are camp-only (guess) |
| Spell list | green panel at x 352, top at the caster's tile (clamped), 228 wide, 16 px per row + 16. Row: name at +14, ＴＰ label at +146, "cost／current TP" right-aligned at +220, 12 px text. The hovered row has a 16-px bar flashing light green (160,248,96) / white with dark text (0,32,0) | measured from T21 screenshots (the client matches to within the text rendering). Flash rate is a guess |
| Item list | fixed green panel (352,224) 180×240, 12 rows per page from y 232, "× n" right-aligned. Footer: a white line, "ページ  1／ 1", and SYSTEM.BMP arrow icons (16×16 at y 304, x 0 = ◁, x 16 = ▷) | measured (T21) |
| Description window | green panel (80,32) 480×48 while a list is open: "name：" at (+16,+10), the description at (+16,+26), white 12 px | measured (T21) |
| Aiming overlays | spell range and the area around the aim tile in stripes (odd rows), each affected unit's tile solid (all rows): blue (SYSTEM range tile 3) for own-side spells, orange/yellow (tile 2) for attack spells. Medicine range: salmon stripes (tile 1). Every range overlay's checker scrolls up-left, 2 px diagonally every 4 frames, wrapping every 16 px | confirmed: blue from T21b/e, orange and the scroll from the T22 spell recording (phase measured frame by frame) |
| Result messages | no name, full-width digits: 「ＨＰが８ポイント回復した」 (WORD 2+64+n+65+66). The client builds 攻撃力/防御 changes the same way with 上昇した/下降した. The 「〜は〜を使います」 line before an item is a guess | heal message confirmed (T21e). The popup sat at x 160 there and the EXP popup at x 200, so its position rule is unknown |
| Battle scene: sides | allies on the right, enemies on the left, whoever attacks. Animations are authored with the actor on the right facing left; an actor on the left plays them mirrored. A counter-attack happens in the same scene | confirmed (T22 recording) |
| Battle scene: timing | the game runs at 30 Hz; one animation frame per 1/30 s. The map darkens in 4 steps (~0.1 s) to ~20 % and holds ~0.27 s. Then black, the panels fade in (4 ticks), and the stage wipes in left to right (5 ticks, 128 px each). Then a 0.33 s pause and the animation, and a 0.5 s hold. Between strikes the stage wipes out, stays black ~0.4 s and wipes back in, while the panels stay. At the end the stage wipes out, the panels stay ~0.45 s, and the map brightens back in 4 steps | measured (T22; `hit_frame` 15 of ジュナ's attack lands exactly 15 ticks in, so hit_frame is 0-based) |
| Battle scene: layout | stage = `BACK_{terrain.battle_background:000}` (640×320) at y 0. Two 320×160 SYSTEM-textured panels at y 320 (enemy red left, ally blue right; edges from the SYSTEM.BMP swatches at y 290). Each has: name 16 px bold at (12, 328); an etched rule at y 348/349 (x 12–307); ＨＰ at (14, 356) and "cur／max" right-aligned to x 307; a 16-row gradient bar at y 368–383 (x 12–307; the empty part turns red, with a dark line above and a light one below); ＴＰ at y 396 with a green bar at 408–423; stats rows at y 438 and 454: 攻撃力 / 物理防御 / 特殊補正値 and 攻撃精度 / 特殊防御 / 地形効果 (terrain defense of the unit's tile), labels at x 13/119/225 | measured from the recording. Colours are approximate (lossy video) |
| First Attack (Before/After) slots | fire for the first fight between the slot's unit and its target unit, in either direction. Most maps put them on the enemy, targeting an ally (SMAP_005 …); map 1 puts them on ルーティ and ジュナ, targeting ナス (their EV000102 / EV000301 dialogue). 「Before」 events play before the battle scene starts | orientation from the data (both kinds exist); timing from the user |
| Battle scene: knock-back | while the hit flash lasts, the target is pushed away from the attacker: 0 px on the first tick, 11 px on the second, then ~8 px until the flash ends, then back. It isn't in any data file (engine behaviour). The offsets were read frame by frame from the recording, a left-side target (minion) and a right-side one (ジュナ). Healed units don't move | measured (T22 recording) |
| Battle scene: hit | the target flickers as a white silhouette for `flash_time` frames centred on `hit_frame`. At `hit_frame` the damage number appears in SYSTEM.BMP's purple digits (y 128, 24 px apart, centred on the target's x), drops onto the ground line (cell top y 268), bounces once and stays until the stage wipes out. The actor freezes on its last frame | measured (T22) |
| Battle scene: defeat | 1 s after a killing blow, the defeated unit's UNIT.DAT death message replaces the panels: bottom message layout with its face, waiting for a click. After the click the dead unit blinks out (hidden 1 tick, shown 1 tick, 4 times), is gone, and the stage wipes out ~11 ticks later; the message stays up until then | measured (T22 recording, and a death recording sampled at 60 fps) |
| Battle scene: after | the EXP message appears back on the map, at the top, with no name and full-width digits: 「１７の経験値を獲得」 | confirmed (T22) |
| Battle scene: miss | a purple 0 like a damage number, with the animation's miss sound instead of the hit sound | confirmed (user, T22) |
| Battle scene: background | `BACK_nnn` of the terrain under the unit being attacked or targeted | confirmed (user) |
| Battle scene: spells on one's own side | two halves, each with a single unit on the right and only its (blue) panel; the left half of the panels stays black. First the caster alone plays its class magic-to-ally animation: 11-tick pause, the animation, then straight into the wipe-out with no hold. The stage stays black ~0.45 s while the panel switches to the target. Then the target alone: 8-tick pause, the spell animation (authored landing on the left, so mirrored to the right), a 10-tick hold. The target flashes white like a hit. The recovery number uses the red digits (y 160) and bounces the same way, centred at x 511 on the right (the left-side damage number centres at 159; one sample each). TP drops when the cast animation starts. EXP afterwards = the spell's Exp Value (活性波: 「１０の経験値を獲得」) | measured from the T22 heal recording (グレイ's 活性波 on ジュリア) |
| Battle scene: spells on enemies | same split as own-side spells: the caster alone on its side (only its panel) plays the cast animation (magic-to-enemy), about 7 ticks after the wipe-in, and goes straight into the wipe-out. Then each target in turn alone on its side (enemies left, red panel only, the right half black): ~14 ticks of black, wipe-in, a 10-tick pause, the spell animation as authored, number, drain, and a death message if it dies. Then the next target, all in one scene with a single cast | confirmed (T22 spell recordings: ルーティ's ルーチェ＝フォルツァ on two minions) |
| Battle scene: HP drain | the HP bar doesn't jump: from the hit it drains 1 HP per tick (weapon, ナス on ジュナ: 5 HP in 5 ticks) or 1 HP per 2 ticks (spell: 18 HP in ~34 ticks), stopping at 0. The number shows the full damage even when it exceeds the HP left (37 on an 18-HP minion) | measured, two samples; the rule behind the two rates (weapon vs spell?) is a guess |
| Battle music | map BGM slot 3 (player-battle) when an ally starts the fight, 4 (enemy-battle) when an enemy does, 5 (boss-battle) when an enemy leader takes part; the phase music returns afterwards | battle music confirmed to start (T20b); slot choice inferred from the map header's slot names |
| Enemy spells | an enemy casts a known, affordable damage spell when its predicted total damage beats its best weapon attack, and a heal when it restores ≥ ⅓ of someone's HP | guess |
| Camp | CAMP_nnn full screen, camp BGM (map slot 0). Menu 96×129 at (16,16); the hovered entry gets the flashing light-green/white bar. Shown before a map whose camp_before_battle is set, after the previous map's COMPLETE!!. Sub-screens replace the menu | confirmed (T17b, T23 recordings) |
| Camp: ユニット編成 | unit list 180×400 at (88,40), each row "name … 出撃" (or ――). Commands 武器を替える / 防具を替える / 装飾品を替える / 道具を使う / 技を見る in a panel at (288,40), greyed when there's nothing to choose. 「出撃ユニット数 n／max」 (max = the next map's start positions) at (432,40). The status window 264×288 at (288,152) | confirmed (T23). How 出撃 is toggled wasn't recorded: the client uses left/right or a click on the 出撃 column |
| Camp: equipment / skills | the list replaces the unit list: 180×336 at (80,104), 「（装備をはずす）」 first, then matching items the class may use (the old item returns to the inventory). Skills: 204 wide, "name ＴＰ cost". The description window 480×48 at (80,32); status at (288,152) | confirmed (T23) |
| Camp: 道具を使う | not recorded. Client guess: the same list with stat-up and class-change items the class may use. A stat-up adds to the base stats; class change needs the item's level and a matching from→to pair 「〜に昇格」 | guess (user couldn't test yet) |
| Camp: shop | list 264×336 at (80,96): a 「アイテム ／ 値段 (アイテム売却: 下取り額) ／ 所持数」 header row with a rule, then rows. Gold in a 192×32 panel at (368,96), shown literally as 「130,肉」 (WORD 31 includes the comma). Description at the top. Sale price = half the price | confirmed (T23: ルーティのマント 10 → 5, ポーション 10 → 5) |
| Camp: 次のステージ | 「キャンプを終了して／進撃を開始します」, a rule and はい／いいえ in a panel at (16,16). No separate sortie screen (sortie is set in ユニット編成) | confirmed (T23) |
| Title | TITLE.BMP full screen; menu 108×64 at (266,320) with はじめませう (WORD 47) / ロードする / ゲーム終了; the load list replaces it at (272,281). New game starts at GAME.DAT [0] | confirmed (T24 recording) |
| System sounds | the runtime plays CLICK.WAV on every confirm (menus, map selections, advancing text), CANCEL.WAV on cancel, BUZZER.WAV on an unavailable choice, DOWN.WAV when a unit falls (with its death message) and LEVELUP.WAV on a level-up. SRPGEXEC.EXE names exactly these five files | confirmed: audio cross-correlation against the recordings (match ≥ 0.82) |
| Action menu | the original's order is 行動終了 / 攻撃 / 必殺技 / 装備 / アイテム (T23 recording). The client puts 行動終了 last (player request). 装備 switches the weapon without ending the action | order confirmed; 装備's behaviour is a guess |
| Fallen allies | units that died on the previous map come back at the camp | confirmed (user, T23d) |
| Enemy phase | each enemy moves in map-object order. Before it moves, the screen scrolls smoothly (16 px/frame) to centre it unless it is already 2+ tiles inside the view, then follows it to its destination | scrolling confirmed (T19f). Speed and margins are guesses |

Every scripted run is reproducible: in screenshot mode the client seeds the battle RNG with 1, ignores the real keyboard and mouse, and `--keys` feeds input. The client writes the state and a token trace next to the PNG as `.txt`.

## Test log (completed, oldest first)

All tests are done in `えるまに - Copy/` and diffed byte-for-byte against the untouched `えるまに/`. Unit/item/etc. numbers in instructions are the editor's 1-based numbers.

### Phase 1, round 1 — instructions (T1–T9)

- **T1 — UNIT.DAT.** Unit 004 (ナス): set Face Graphic to portrait #10. In Death Message, paste `0123456789` repeatedly until the editor stops accepting text. Set MP growth to 111. Tells us: face offset/base, max message length, growth order.
- **T2 — CLASS.DAT.** Class 003 (ナス):
  - Move Type = Special.
  - Animations: Weapon A = 005, Weapon B = 006, Weapon C = 007, No Weapon = 008, Magic To Enemy = 009, Magic To Ally = 010.
  - Weakness: Attribute B only. Resistance: Sleep only.
  - Move Rule → Special Terrain Case: give terrain #4 (雪原) cost 4.
  - Skill Progression: add a skill at level 13.
  - Please also screenshot the Move Type and Move Rule dialogs.
- **T3 — ITEM.DAT.** Screenshot the open weapon **Range** dropdown (its order defines the enum). Then:
  - Item 041 (ポーション): Range 2, Defense Up 11, Magic Defense Up 12. In the Heal group, tick **Sleep and Defense Down only** (leave Poison and Attack Down unticked).
  - Item 033 (スピードバンド, accessory): Exp Growth 37.
  - Item 001: untick class usability for class 002 only.
- **T4 — MAGIC.DAT.** Screenshot the **Target** dropdown. Then:
  - Spell 001 (ハウリングプラズマ, currently "Enemies Only"): Target = the last option.
  - Spell 012 (ヒール, Raise Status; Exp Value only appears on Degrade/Raise spells): Exp Value 77.
  - In the Attack Range tab, clear the grid and paint one cell 3 squares **right** of centre and one 1 square **up**.
  - In the Effect Range tab, paint only the centre.
- **T5 — GEOLOGY.DAT.** Screenshot the Terrain Editor, especially the labels of the three Movement boxes. Set terrain 001 (草原) Animation Background to 12.
- **T6 — ANIME.DAT** (optional). Animation 001: in frame 1, move one cell exactly 16 px right (Grid Use on). Then set Hit Sound Frame 5 and Miss Sound Frame 6.
- **T7 — WORD.DAT.** Screenshot the vocab editor (toolbar: the speech-bubble icon, second after the ½ zoom button) so the 100 entries can be labelled.
- **T8 — SMAP_001.DAT.** First screenshot Map Properties for map 1 (１話) as it is. Then:
  - Win Condition = Defeat Enemy Leader.
  - Toggle Camp Before Battle and Disable Party Changes.
  - Camp Background = 3.
  - Shop list = exactly item 001 then item 005.
  - Save and send SMAP_001.DAT.
- **T9 — GAME.DAT.** Open Game Properties (toolbar: the first icon right after the ½ zoom button, or the Game (G) menu) and screenshot it with its current values. No edits needed.

### Phase 1, round 2 — follow-ups (T8 redo, T7b, T9b, T10)

- **T8 redo:** T8 again, this time pressing **Save current map** before switching maps (the first attempt was never written to disk).
- **T7b:** screenshot the vocab editor's Term 2, Term 3 and Term 4 tabs.
- **T9b:** untick Game → "Launch In Full Screen" and check whether GAME.DAT changes.
- **T10:** on map 1 with Camp Before Battle = Yes, screenshot Map Properties, toggle "Disable Party Changes", Save current map.

### Phase 1 — results

Every change landed in exactly the field the parser expected, apart from the corrections already folded into the tables above:
- the grid column direction
- 0x9C really is Target
- T1's growth edit landed in slot 3, the 4th row

| Test | Result |
|---|---|
| T1 UNIT | face, death-message limit/wrapping confirmed. Growth edit landed in slot 3 (Mag) |
| T2 CLASS | move_type 2, terrain override, all 6 animation slots, skill level, weakness B, resist Sleep all confirmed |
| T3 ITEM | range enum, class_usable, acc_growth_bonus, med_def_up/mdef_up, med_cure order, med_range confirmed |
| T4 MAGIC | target, exp_value confirmed. Grid columns were mirrored in the parser (fixed) |
| T5 GEOLOGY | move_cost column labels, battle_background confirmed |
| T6 ANIME | frame layout start, x position, 0-based sound frames confirmed |
| T7 / T7b WORD | all four tabs: 76 entries labelled and text-matched (§8). 9 text entries have no editor box |
| T8 SMAP | first attempt not saved. **Redo**: win_condition 1, camp_before_battle 1, shop list [0, 4] with count 2 at 0x344, camp_background 3, all confirmed |
| T9 / T9b GAME | no Game Properties dialog in this editor version. The full-screen toggle doesn't touch GAME.DAT (§12) |
| T10 SMAP | Disable Party Changes = 0x450, confirmed. EDITOR.DAT confirmed as last-opened map |

### Phase 2, round 1 — instructions (T11–T13)

Open each dialog from the map's EVENT mode by double-clicking an object. Click **Save current map** before switching maps, then send `MAP/SMAP_001.DAT` plus the screenshots.

- **T11 — event start conditions.**
  1. Screenshot the Event Start Conditions dialog. This editor version may list more options than the help screenshot.
  2. On map 1, open object ナス at (7, 0) (object 2) and set:
     - No. 2 = Switch 007.
     - No. 3 = Turn, Enemy, at turn 3.
     - No. 4 = the first Unit Condition, Target Unit = ルーティ.
     - No. 5 = the last Unit Condition (When Unit Dies?).

  Leave No. 1 as is.
- **T12 — one of each command.**
  1. On map 1, create a new event on an empty tile: Type = Other, No. 1 condition = Switch 001.
  2. In its Event Contents, add these commands in this order, with exactly these values, and **screenshot each command's dialog** (the field names matter as much as the bytes):
     1. Message, two lines: `AAA` / `BBB`
     2. Face Graphics Setup: face #12 (note any position/side options)
     3. Switch Operations: switch 007 ON
     4. Switch Operations: switch 008 OFF
     5. Item Management: add item 005 ×3
     6. Gold Management: remove 1234
     7. Screen Display: 2nd effect option
     8. Erase Screen: 2nd effect option
     9. Picture Display: `BACK_002.BMP` (note all fields)
     10. Erase Picture
     11. Scroll Screen to (5, 9)
     12. Flash/Tint Screen: distinct values in every field (e.g. 10, 20, 30, 40, 50)
     13. Shake Screen: distinct values
     14. Change Ally Unit: add unit 005
     15. Unit Class Change: unit 005 → class 007
     16. Unit Equipment Change: unit 005 weapon → item 002
     17. Move Unit: unit 005 to (3, 4)
     18. Execute Movement
     19. BGM: `戦闘.MID`
     20. Sound Effect: `BOMB.WAV`
     21. Variable Operations: variable 010 = 77
     22. Variable Operations: variable 010 += 5
     23. Conditional Branch: variable 010 ≥ 50, with a `YES` message inside
     24. Character Choice: options `X` / `Y` / `Z`, each with a one-line message
     25. Button Input (into variable 011, if it asks)
     26. Wait 25
     27. Select Map 7
     28. Map Complete
     29. Return to Title
     30. Game Over
     31. End Event

     Finally screenshot the whole command list.
- **T13 — damaged events in the original game.** Map 56 (３２話) ships with three broken scripts: the enemy ルシフィール at (12, 22) event 1, and the "Other" event at (20, 26) events 1–2. If you can reach that chapter (or a save near it), note what happens when those events should fire: dialogue shown, garbage, nothing, or a crash. The new client should match whatever the original does.

### Phase 2, round 1 — results

- **T11:** condition codes 1/2/4/5 and the turn encoding confirmed (§11.1).
- **T12:** all opcode names and most payload fields confirmed (§11.3). The test event is object 6 / `EV000601` in the copy's SMAP_001. Notes from the session:
  - Face Graphics has left/right and a "flip horizontally" toggle.
  - Screen Display and Erase Screen have "From Black"/"From White".
  - Move Unit is a sequence of direction buttons, not a target tile.
  - Button Input doesn't ask for a variable.
  - Map Complete has a Win/Lose toggle.
  - There's no Play Movie test.
- **T13:** recorded as "dialogue plays", a provisional assumption to revisit (§11.2).

### Phase 2, round 2 — instructions (T14)

Same procedure: map 1 in the copy, add commands to a new "Other" event, Save current map, send SMAP_001.DAT.
1. **Move Unit steps:** report the button sequence used for T12's Move Unit, then add a Move Unit with exactly Up, Up, Left, Right, Down.
2. **Face Graphics:** (a) face #3 Right; (b) face #3 Left + Flip; (c) face #3 Right + Flip.
3. **Picture Display:** `BACK_003.BMP` at Upper Left with the 2nd Display Method, then at Lower Right with "Remove Transparent Color" ticked.
4. **Screen Display:** "From White" with the 1st effect.
5. **Flash/Tint:** screenshot the Screen FX dialog.
6. **Variable Operations:** screenshot the dialog, then one command per operation type, then one per operand source, on variable 012.
7. **Conditional Branch:** screenshot the dialog, then one branch per condition type/option, with variable 013 ≥ 44 and variable 014 = 55 among them.

### Phase 2, round 2 — results

The test commands are in `EV000701` (object 7) in the copy's SMAP_001. They landed in event slot 1, whose condition is "Do Not Perform", which is fine for a test.
- **Move Unit:** T12's sequence was move up/right/down/left, face up/right/down/left → steps 0–7. T14 was split into a move series `[0,0,3,1,2]` and a facing series `[4,4,7,5,6]`. Step codes confirmed.
- **Face:** position/flip bytes confirmed.
- **Picture:** keypad position layout, method index and remove-transparent byte confirmed.
- **Screen Display:** From White = color 1.
- **Flash/Tint:** the dialog names the four values Red, Green, Blue (0–200 %) and Vividness (0 = monochrome).
- **Variable Operations:** operations 0–4 confirmed. The dialog's sources are Numerical Value / Variable / Random Number Value. The second test series used source 1 (Variable) with the default variable 001. The 8-byte value area turned out to be three per-source fields.
- **Conditional Branch:** the dialog has Switch (ON…), Turn (Ally/… – n – After…), Item (Party Possesses…), Unit (Alive…), Variable (value + "= OR >"…). The record keeps one field per kind, and the parser decodes them all by name. Option codes are still unnamed (T15).

### Phase 2, round 3 — instructions (T15)

Screenshot the Conditional Branch dropdowns (Switch ON/…, Turn Ally/… and After/…, Item Party Possesses…, Unit Alive…, Variable "= OR >"), and report what T14's three Turn branches were set to.

### Phase 2, round 3 — results

All option lists captured (codes in §11.3, row 2A). T14's Turn branches were Ally turn 1 After, turn 2 Before, turn 3 Exact. They were stored as turn 1/3/5 with mode 0/1/2, which confirms the branch turn field uses the same phase encoding as event slots. All 15 T14 branches decode exactly as set. Every branch in the game's scripts maps to a named option:

| Condition | Count |
|---|---|
| variable ≥ | 373 |
| variable ≤ | 257 |
| variable = | 175 |
| switch ON | 76 |
| switch OFF | 36 |
| unit alive | 21 |
| item possessed | 13 |
| unit dead | 10 |
| unit not present | 3 |

### Phase 3, round 1 — instructions (T16, T17)

- **T16 — save files.** In a fresh copy of the game (`えるまに - Save testplay`):
  1. New game, then save to File 1.
  2. Save the same state again to File 2.
  3. Move one unit one tile, end its action, and save to File 3.
  4. Play to the first camp, save to File 4, and note the gold and one unit's level/HP/EXP/equipment.
- **T17 — reference screenshots** of the original runtime:
  - map 1 opening (narration, ルーティ, ナス);
  - map 4 opening (picture + グレイ);
  - a choice menu;
  - a battle-map screen with a unit hovered;
  - fade timing, and whether text is typed out.

### Phase 3, round 1 — results

- **T16:** 5 saves.
  - SAVE1 = SAVE2 byte-for-byte, so there's no timestamp.
  - SAVE2 → SAVE3 changed 5 bytes: ジュナ (7,12) → (7,11). That changed her y, the acted flag, movement left 6 → 5, and the position before the move.
  - SAVE4 (camp) shows gold 20 and ルーティ Lv 1 with 49 EXP, matching the notes.

  Format in §14. Switches, variables and inventory are still unlocated.
- **T17:** 7 screenshots. The client now matches them pixel-for-pixel in layout and font (§13). Text appears all at once. The choice-menu screenshot and fade timing weren't captured.

### Phase 3, round 2 — instructions (T17b)

Screenshots of: a choice menu (11話 camp menu), the camp screen, the save/load file lists, a battle in progress and the screen after it, plus fade timing.

### Phase 3, round 2 — results

All delivered (camp menu, save list, load list, 11話 choice menu, a battle screen, an EXP message); fades take about half a second. Findings are in §13. The choice menu was implemented and matched against its screenshot: 12 px text pixels 98 % identical (the rest is the mouse cursor drawn over it), panel 99.7 %.

### Phase 3, round 3 — instructions (T18)

Buy or receive an item at camp and save to File 6; play to the end of map 2 and save at the next camp to File 7.

### Phase 3, round 3 — results

- **SAVE4 → SAVE6** (bought one ポーション, 10 gold) changed 4 int32s:
  - 0x210 = 40 (item id) and 0x214 = 1;
  - 0x530 = 1 (entry count);
  - gold 20 → 10.

  So the inventory is (item, count) pairs.
- **SAVE6 → SAVE7** (map 2 played):
  - switch bytes 0x14/0x15 = 1, matching map 2's only two switch commands (switches 0, 1);
  - map number 2 → 3, gold 130, party 4, with グレイ (245) and ジュリア (247) added to the roster;
  - no variable changes, because maps 99/1/2 have no variable commands.
- **Bonus screenshot** (map 2 mid-opening narration): the client's text matches pixel-for-pixel. The trailing 「＿」 is part of the script text, not a UI marker.

### Phase 3, round 4 — instructions (T18b)

Buy a second ポーション and save; play maps 3 and 4 and save at the camp after map 4. A `wintool.py weaken` helper was added for quick wins.

### Phase 3, round 4 — results

| Save | When |
|---|---|
| SAVE8 | camp before map 4 |
| SAVE1 | mid-map 4, after the opening and the second Potion |
| SAVE5 | camp after map 4 (party 6) |

- **Inventory:** a second ポーション increments the count to (40, 2) rather than adding an entry, so it's confirmed as (item, count) pairs. ロボットアーム (41) arrived after map 3.
- **Header:** 0x004 counts camps (1→4); 0x000 is 1 only at a camp.
- **Variables:** still not seen. Map 4's only variable command is in the choice case the player didn't pick (see §14).

### Phase 3, round 5 — instructions (T18c)

From SAVE8, replay map 4's opening choosing 「ジュリアの選んだ服を買う」 (which adds 1 to variable 19), then save to File 2 as soon as the map starts.

### Phase 3, round 5 — results

The only non-zero byte in 0x580–0x7CF of the new SAVE2 is **0x602 = 1** = variable 19 in a 16-bit table at 0x5DC. SAVE1 (other choice) has none, and the switch and inventory fields agree. The other differences were unit movement, because SAVE1 had been saved later in that battle. `parse_save()` now reports `variables_set`.

### Phase 4, round 1 — instructions (T19: battle flow on map 1)

In `えるまに - Save testplay` with the original runtime, play map 1 and report:
- **T19a:** the end of the turn;
- **T19b:** whether a move can be undone;
- **T19c:** the movement and target overlays (screenshots);
- **T19d:** what happens when ルーティ dies;
- **T19e:** what follows beating ナス;
- **T19f:** whether the screen follows the enemies.

### Phase 4, round 1 — results

- **T19a:** the turn never ends by itself. Click a blank tile and pick ターン終了.
- **T19b:** cancelling at the command menu undoes the move.
- **T19c:** two screenshots, pixel-analysed (§15 "Range overlay"):
  - odd pixel rows show the SYSTEM.BMP range tile's checker (cyan (128,176,176)/(176,216,248) for movement, salmon (248,120,104)/(248,200,168) for attack);
  - even rows are the untouched map.
- **T19d:** the map continues. Her line is only her talking about herself.
- **T19e:** in order:
  1. ナス's death-event lines;
  2. a cutscene;
  3. "COMPLETE!!" on screen (`TELOP3.BMP`) with a fanfare;
  4. the camp menu.
- **T19f:** the screen scrolls to each moving enemy.

### Phase 4, round 2 — instructions (T20)

On map 1, check:
- **T20a:** the PLAYER TURN / ENEMY TURN captions;
- **T20b:** whether the music changes per turn;
- **T20c:** the game-over screen;
- **T20d:** COMPLETE!! position and timing.

### Phase 4, round 2 — results

- **T20a:** both captions appear. They slide in from the right and pause about a second in the middle.
- **T20b:** the music changes with each turn, and also when a battle starts.
- **T20c:** the game-over screen is just `GAMEOVER.BMP`.
- **T20d:** COMPLETE!! slides in at the same speed as the turn captions and stays about 9 s while the fanfare plays, then the camp menu.
- **Aside:** the runtime once reported `CAMP_003.BMP` missing. That file only exists in the editor's RTP folder (`srpg maker 95/RTP/BMP`), which the runtime finds through `HKCU\Software\Enterbrain\SRPG95\MyPath`. Every map's camp uses background 7, which the game folder has, so it was a one-off; nothing had changed on disk.

### Phase 4, round 3 — instructions (T21: magic and items on map 2)

Map 2 (２話) in `えるまに - Save testplay`: load save slot 6: the camp before map 2, with ルーティ, ジュナ and 1 ポーション. ジュリア knows 剣の舞, a Raise Attack spell for her own side, at level 2. Screenshots help wherever something appears on screen.

- **T21a — spell list.** Move ジュリア next to another ally and pick 必殺技. Screenshot the spell list.
- **T21b — aiming.** Pick 剣の舞. Screenshot the range display, with the cursor on an ally. Can the cursor go on an empty tile, or only onto units?
- **T21c — result.** Cast it on the ally. What messages appear, in order? Then:
  - Right-click the ally: what is its 攻撃力 now, compared with before? The data predicts +3 (Value 3, Relation 0).
  - How much EXP did ジュリア get? The data says 25.
- **T21d — decay.** On your next turn, check the ally's 攻撃力 again; it should be 1 lower.
- **T21e — items.** If you have a ポーション, use it from someone next to a hurt ally. Screenshot the item list and the target choice. What messages appear?
- **T21f — healing.** If グレイ joins during the map, have him cast 活性波 on a hurt ally. How much HP did it restore? The data predicts 7: Value 6 + his Magic 3 × Relation 5 / 10, rounded down, without an accessory.

### Phase 4, round 3 — results

From save slot 6, on map 2 (screenshots 37–43; layout numbers in §15):
- **T21a — spell list:** to the right of ジュリア, one row 「剣の舞　ＴＰ 4／13」. The hovered row flashes yellow/white, and a description window appears at the top: 「剣の舞：範囲内の味方の攻撃力を一時的に上げる、術者は含まない。」
- **T21b — aiming:** 剣の舞 aims only at the caster. Its effect grid around her shows as blue stripes, and the tile of the ally it will boost is solid blue. Whether the cursor can sit on an empty tile wasn't checked.
- **T21c — result:** the ally's 攻撃力 rose by 3, and ジュリア got 25 EXP. Both match the data.
- **T21d — decay:** a turn later it was 1 lower.
- **T21e — items:** a 12-row item list with a page footer, salmon range stripes around the user, and the message 「ＨＰが８ポイント回復した」.
- **T21f — healing:** taken as 7 (not observed).

### Phase 5, round 1 — instructions (T22: the battle scene)

The client now has a first battle scene, built from the data alone. These checks fix its layout and timing. Map 1 in `えるまに - Save testplay` is enough: slot 3, or a new game. Screenshots are the most useful thing. A short screen recording works even better, e.g. from ShareX, which is already installed, or Win+Alt+R. Drop it into the `recordings/` folder at the workspace root. `re/tools/frames.py` extracts the frames (using ShareX's ffmpeg).

- **T22a — the first moment.** Attack a minion with ジュナ. Screenshot the scene as soon as it appears, before anyone moves:
  - Which side is ジュナ on?
  - Is there any transition into the scene (fade, flash, wipe)?
- **T22b — the hit.** Screenshot the moment the damage number appears. Then:
  - Where does it appear?
  - What colour is it?
  - Does it move?
- **T22c — the counter.** If the minion survives and hits back, does that happen inside the same scene? Does the minion stay on its side, or do they swap?
- **T22d — timing.** Roughly how long is ジュナ's whole attack, in seconds, from her first move until she's standing still again? (Her animation is 23 frames.)
- **T22e — enemy attacks.** In the enemy turn, when a minion attacks ジュナ or ルーティ, which side is the attacker on?
- **T22f — the bottom of the screen.** Screenshot the area under the background during a battle (names, HP, anything else).
- **T22g — after the battle.** Does the EXP message appear inside the battle scene or back on the map?

### Phase 5, round 1 — results

From a 2-minute 60 fps recording of map 1, `recordings/` (frames via `re/tools/frames.py`); measurements in §15:
- **T22a — the first moment:** ジュナ is on the right and the minion on the left. The map darkens, the panels fade in, and the stage wipes in left to right.
- **T22b — the hit:** the number uses the purple digits, centred on the target, dropping and bouncing onto the ground. The target flashes white.
- **T22c — the counter:** in the same scene, after a stage-only wipe-out and wipe-in. The sides don't swap.
- **T22d — timing:** 30 Hz; ジュナ's 23 frames take 0.77 s.
- **T22e — enemy attacks:** the enemy stays on the left and its animation is mirrored.
- **T22f — the panels:** as in §15.
- **T22g — after the battle:** the EXP message appears on the map. A death message appears inside the scene first.
- **Follow-ups from the user:** the background is the attacked unit's terrain. A miss is a 0 with a different sound effect.
- **Heal recording** (グレイ's 活性波 on ジュリア): as in §15 "spells on one's own side".
- **Spell recordings** (ルーティ's ルーチェ＝フォルツァ on two minions; a second cast): as in §15 "spells on enemies" and "HP drain".

### Phase 6, round 1 — instructions (T23: the camp)

A screen recording of the camp before map 3 or 4 (save slot 7 or 8 in `えるまに - Save testplay`) would settle most of the camp layout. Please go through:
- **T23a:** ユニット編成: pick a unit, then each of 武器を替える, 防具を替える, 装飾品を替える, 道具を使う and 技を見る, and back out.
- **T23b:** アイテム購入: buy something cheap. Then アイテム売却: hover a couple of items (to show the sale price) and sell one.
- **T23c:** 次のステージ: everything up to the map starting. If a unit-selection screen appears, toggle someone.
- **T23d:** does a unit that died in the previous map come back at the camp? Say if you remember; no need to test specially.

### Phase 6, round 1 — results

Two recordings: one of the camp before map 3 (ユニット編成, equipment, 技を見る, shop, 次のステージ) and one of buying and selling. Layout in §15. The user couldn't try 道具を使う yet, so the client's version is a guess (noted in §15). Units that die come back at the camp.

### Phase 6, round 2 — instructions (T24: the title screen)

The client's title menu is a guess (「はじめませう」 / ロードする / ゲーム終了 in a small panel low in the middle). A screenshot of the original's title screen with its menu, and of what loading from it looks like, would fix it. Also: does any music play on the title?

### Phase 6, round 2 — results

A recording of the title: TITLE.BMP with a menu 108×64 at (266,320): はじめませう / ロードする / ゲーム終了. ロードする replaces it with the ロードします list, 96×177 at (272,281); empty files are grey. Loading darkens the screen in steps, goes black, and starts the saved map. Title music not checked (the recording has no audio analysis).

## Open items with no test planned

- Phase 1 leftovers with no known runtime effect: ANIME +0x020, SMAP 0x444 (always 1), 0x448 and 0x44C (3–4 maps each), GAME.DAT [1]..[4] (and [0] = start map is only inferred). Best chased later by watching the running game.
- The camera position at map start (§13).
- Save variables: 16-bit at 0x5DC vs 8-bit at 0x5EF. Any later save with another variable set (e.g. at the 11話 camp, variable 0) settles it.
- Save header fields 0x008, 0x00C, 0x57C, and unit-record fields [4], [13], [36], [52], [56], [130], [131], [150].
- Battle scene: why HP drains at 1/tick in one recording and 1/2 ticks in the other.
- Camp: 道具を使う (the user will record it when possible), and how a unit's 出撃 is toggled. A later recording of a spell, a miss and a heal would settle them.
- Event-side details that need the running game:
  - the order of event-slot condition codes 6–8 and 9–11 (§11.1);
  - picture display methods and screen effects beyond the first two;
  - the T13 damaged-script behaviour.

## Active tests

None right now.

**Tip: quick wins in the test copy.** At a camp (before the next map starts), run:

```bash
python re/tools/wintool.py weaken
```

Every enemy-only unit then has 1 HP and no attack or defense, so one hit beats each one, while all story events still run. Party members, anyone who ever fights on your side, and neutral NPCs keep their real stats. It only edits `えるまに - Save testplay/UNIT.DAT`, keeps the original in `_wintool_backup/`, and refuses to touch the original game. Undo with:

```bash
python re/tools/wintool.py restore
```
