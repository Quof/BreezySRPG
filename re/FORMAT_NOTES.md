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
| 0x444 | 4 | i32 | whole_tile_grid: 1 = the tiles are stored as the whole 40×40 grid, 0 = as h rows of w chips (everything after them moves up) | confirmed (exe) | FUN_004048ac reads the grid either way; 1 in all 79 maps and Kikumaru's 7 |
| 0x448 | 4 | i32 | unknown_448 (rare: 74, 254, 318) | unknown | |
| 0x44C | 4 | i32 | unknown_44c (rare: 46, 94, 96, 250) | unknown | |
| 0x450 | 4 | i32 | disable_party_changes (1 = ticked) | confirmed | T10: screenshot shows it ticked on map 1 (stored 1). Unticking + Save current map stored 0. (The T8-redo untick didn't take; the box is greyed out while Camp Before Battle = No) |
| 0x454 | 92 | — | (always zero) | — | |

Lists keep stale entries past their count (e.g. 4 x-coords with count 1), so always honour the count field.

### Tile grid 0x4B0–0x112F (confirmed layout)

40×40 uint16, row-major, stride 40, independent of width. Value = chip number in MAPC_tileset.BMP (0–383). Single layer; no separate upper layer has turned up. The runtime's map loader (FUN_004048ac) reads the header (0x4B0 B), this grid, the 100 objects (0x4010 B) and the script container (FUN_0040289c) in sequence; with header 0x444 = 0 the grid would be just h rows of w chips (stored 2·w·h bytes, read into rows of 40) and every later offset would move up.

**Chip numbering (confirmed against an editor screenshot of map 1):** the 12 × 32-chip sheet is two 6-column halves, the editor's "A" and "B" tabs. Chip k < 192 is at column k % 6, row k / 6 of the left half. Chip k ≥ 192 is at column 6 + (k−192) % 6, row (k−192) / 6. An earlier straight 12-wide reading produced scrambled maps.

### Event area 0x1130–EOF

Decoded in phase 2. See **§11**. Layout summary (confirmed on all 79 maps):

| Off | Size | Content |
|---|---|---|
| 0x01130 | 100 × 164 | map objects (unit placements and "Other" events). Slot 0 is never used |
| 0x05140 | 5000 × i32 | block chain table (FAT): next block index, -2 = end of chain, -1 = free |
| 0x09F60 | 5000 × 16 | file directory: `char name[8]`, `i32 first_block`, `i32 size_bytes`. All-zero = empty |
| 0x1D7E0 | n × 100 | the data blocks **in use**, packed in block order |

File size is always `0x1D7E0 + 100 × (number of non-free FAT entries)` because only the blocks in use are stored: the editor and the runtime's save (FUN_00402908) write 100 bytes for each FAT entry that isn't −1, in index order, and the loader (FUN_0040289c) reads them back into `block × 100` of a zeroed buffer the same way. So block k sits at `0x1D7E0 + 100 × (number of used entries below k)`, not at `100 × k`: the two only agree while the FAT has no free holes (confirmed, exe; the misreading is what made SMAP_056 look damaged, §11.2). The parser still writes the raw area to `json/map/smap_nnn.events.bin` as a backup.

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

Files are read by following the FAT chain from `first_block` and truncating to `size` (FUN_0040262c), the blocks taken from the packed block area (§10). The event loaders (FUN_0040cfe0 `EV%04d%02d`, FUN_0040d0a4 `OPENING`) zero a 500,000-byte buffer, then read the 100-byte comment, the 4-byte length n and n bytes.

**No shipped script is damaged (T13, resolved from the exe).** SMAP_056's FAT has free entries 84–89 and 117, and Kikumaru's SMAP_006 one at 35. Reading block k at `100 × k`, as the parser first did, made three SMAP_056 scripts look cut off or pointing into other scripts (`EV001701` "missing 7 of its 12 blocks", `EV003601`/`EV003602` "the middle of other scripts") and garbled Kikumaru's `OPENING`. Read packed, as the runtime does, all four decode whole and re-encode byte-identically:
- `EV001701`: ルシフィール's dying line 「くっ・・・・・リューガ様！！／あとは頼みましたわ・・・。」, then the party's reactions.
- `EV003601`: グレイ's 「不気味だな。」 conversation.
- `EV003602`: the victory scene over BACK_009, ending with `button_input`, `act2.bmp` (a plain dark picture), 「次が最終マップです／全員出撃できます。」 and `map_complete` (win).

The original plays them all; the client does the same (it skips only a structurally invalid script, which no game has).

**File layout:**

| Off | Size | Field |
|---|---|---|
| 0 | 100 | comment (str). Editor-only label, e.g. `OPENING` = "オープニングイベント" |
| 100 | 4 | stream length n |
| 104 | n | command stream |

The stream is a sequence of commands followed by one `0x00` byte. The last command is always `00` (end_of_script, len 0). **Command framing** (confirmed: all 469 files of both games re-encode byte-identically):

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
| C9 | 18,783 | message_line | u16 window, str | confirmed: further lines of the preceding message. The interpreter takes at most four straight after it (FUN_0040d18c), so a message has five lines; a sixth (one message in each 闇鍋企画 game) or a line with no message before it is skipped unseen |
| 02 | 11,231 | face_graphic | u8 position, u8 flip, u16 face | confirmed: position 0 left / 1 right, flip 1 = mirrored (T14: Right → 1/0, Left+Flip → 0/1, Right+Flip → 1/1). The face lasts until the event ends: every event starts without one (the loaders FUN_0040cfe0 / FUN_0040d0a4 clear it, +0x5fbc) |
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

**Result on this game:** all 446 scripts build with **zero structure problems**. Every ID is in range and every BGM/picture/face file exists in the game or RTP folders, with these exceptions, all quirks of the shipped game:

| Where | Finding | Client implication |
|---|---|---|
| SMAP_067 EV000201 | sound `空湾曲1.wav` exists in neither EFS folder | play nothing (presumed original behaviour) |
| SMAP_041 OPENING | the "deploy ハンス and ゴーバック" guard: the ハンス branch restarts map 41 correctly, but the ゴーバック branch's Next Map is index 99 = **SMAP_100**, an empty map. An authoring bug | reproduce by default; candidate for an optional fix |
| SMAP_021 object 18, SMAP_037 object 1 | slot 1 has a start condition but no script | nothing happens |

Kikumaru: no findings (its SMAP_006 `OPENING` reads whole once the blocks are taken packed, §11.2).

## 12. Small files

| File | Layout | Findings |
|---|---|---|
| GAME.DAT (200 B) | i32[50] | Game options, read by the runtime (§16, confirmed from the exe): [0] the **start map**, 0-based (98 here → SMAP_099 "０話" prologue; 0 in Kikumaru); [1] = 1 battle background from both sides' terrain; [2] counter-attacks; [3] double attacks; [4] battle animations slower by [4] % (the tick is 33 + 33·[4]/100 ms); [5] fallen units are lost at the map's end. えるまに: 98, 0, 1, 0, 0, 0; Kikumaru: 0, 1, 1, 1, 50, 0. This editor version has no Game Properties dialog: the Game menu holds only Test Play Game (F9), Test Play Current Map (F8) and "Launch In Full Screen". GAME.DAT was not rewritten during any test session. T9b: toggling "Launch In Full Screen" and reopening the project does not change GAME.DAT, so that setting is stored outside the game data (registry or editor config, not investigated) |
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
| UNIT_nnn.BMP | **3 columns × 5 rows of 64×64 cells**, the 32-px character centred, so it overhangs its tile by 16 px each side. Columns are animation frames, cycled 0, 0, 1, 2, 2, 1. Rows: 0 standing, 1 walking up, 2 right, 3 down, 4 left (FUN_00413918; the exe keeps 30 characters a sheet). えるまに's sheets show the same front-facing head on every row | confirmed (exe) |
| SYSTEM.BMP | UI atlas (exe uses): message balloon nine-slice (0,0) 48×48 and tails (0,48) / (32,48); HP / TP bar pieces (48–63, rows 0 / 16); END (64,0); the dashed tile cursor (96,0), (64,32), (96,32); mouse arrow / hourglass (128,0) / (128,32); range blocks 64×64 at y 64 (cyan, salmon, orange, blue); damage digits y 128, recovery digits y 160; PSN / SLP four frames each at y 192; panel textures 64×64 at y 224 (blue, red, yellow, green); the colour swatches at y 288 (pixel x = 16·i, FUN_004035e4); page arrows 16×16 at y 304 | confirmed (exe) |
| Save files | see §14 | — |

**Screen layout of the runtime** (from the exe; T17's screenshots and the recordings match the client built from it, e.g. 99.03 % of the whole frame for map 3's opening, 99.67 % for the EXP panel):

| Element | Layout | Where |
|---|---|---|
| Screen frame | SFRAME_H/V strips, 16 px on each side, drawn opaque. Map viewport 608×448 at (16,16). Strip variants: rows/columns 0–15 = red arrow, 32–47 = grey. The red arrows show where the view can scroll, only in states 2/6/0x11/0x10, blinking (shown while (tick/6) % 4 ≠ 0). The map loop draws the tiles (FUN_0041371c), then the frame, then the units (FUN_00413918, unclipped: sprites reaching past the view cover it) and the picture (FUN_0041419c), so a full-screen cutscene picture hides the frame | FUN_00413768, map loop near FUN_0041371c |
| Erased screen | the viewport fills with (16,16,0); the frame stays | |
| Message | the window area 608×160 at (16, 16 + 144·window) (0 top, 1 centre, 2 bottom). The balloon is SYSTEM.BMP's 48×48 nine-slice in 16-px pieces, 26 × 8 of them (416×128), 16 px down: at x 112 without a face, 192 with a face on the left, 32 with one on the right; its tail (SYSTEM.BMP (0,48) left, (32,48) right) 16 px down its edge toward the face. The face 128×128 16 px in from the window's left (x 480 on the right) and 16 px down. Text **MS Gothic 18 px bold** (FUN_00407888), no anti-aliasing, colour (16,16,0), 18 px into the balloon and 28 px below the window's top, 22-px lines, all at once | FUN_004148a4, FUN_0040d18c |
| Mouse cursor | SYSTEM.BMP arrow (128,0)+n·32, 20×20, hotspot (0,0); hourglass (128,32)+n·32 while a message waits or a result shows; none during captions, battle scenes and the computer's phase | FUN_00409db8 (mode +0x198) |
| Fades (screen display / erase) | §16 "Screen display / erase" | |
| Textured panels | SYSTEM.BMP's 64×64 tiles at y 224 (blue x 0, red 64, yellow 128, green 192), repeated from the panel's top-left corner. 1-px edges from the swatches at y 288: light = swatch 2c, dark = 2c+1 for colour c — blue (248,216,192) / (0,0,48), red (248,216,192) / (64,0,0), yellow (248,248,224) / (40,40,8), green (248,248,224) / (0,32,0) | FUN_0041203c, FUN_004035e4 |
| Menus | a panel (width units·12, a 16-px row per header row and item plus one) with the 12-px text at the item x (white (248,248,248), disabled (152,152,152)); a header row's text at x + 12, or "-" for an etched rule (dark (0,32,0) at y + 15 + 16r, light (248,248,224) a row below). The hovered row (the pointer within [item x − 6, item x + 12·item width + 6)) gets a bar at (item x − 6, y + 8 + 16·row), 12·item width + 12 wide: green (160,248,96) with text (0,32,0) and white (248,248,248) with text (0,64,0) alternating every 6 ticks; a disabled row's bar is grey (122,122,122). A menu opening (or reopening on a row) warps the pointer to that row's centre | FUN_00413100, FUN_004144f4, FUN_00414344 |
| 12-px text | the exe's own bitmap font KANJIFONT (resource type "KANJI"): 12×12 one-bit glyphs, 24 bytes each. Half-width c (0x20–0xDF) is glyph c − 0x20, drawn at y + 3, 6 px wide; full-width lead 0x81–0x9F → c·0xC0 + trail − 0x6040, 0xE0–0xEF → −0x9040, drawn at y + 2, 12 px wide; 0xF0–0xFC blank | FUN_0040a284, FUN_00407698 |
| Choice menu | green panel ((longest option's bytes + 1)/2 + 2) units wide, a row per option plus one, at x 320 − units·8; on the message window if it's at the bottom, else centred on y 240 (a top or centre message's sum overshoots into the frame's bottom); kept inside the frame. The pointer starts on the first option; the message stays visible underneath | FUN_0042523c |
| Camp menu | green panel 96×128 at (16,16): 次のステージ, ユニット編成, アイテム購入, アイテム売却, セーブする, ロードする, ゲーム終了 (WORD.DAT 24–27, 21–23) over CAMP_nnn | FUN_0041f6d4 |
| Save / load list | green panel 176 high: header ("セーブします" / "ロードします", WORD 53/54), an etched rule, then ファイル１–８ (WORD 81–88) 60 px wide in the middle; empty slots greyed in the load list. Title (272,280), camp (16,16), map: by the turn menu | title / camp / map file lists |
| Panels on the map | green, units·12 wide at y 64, a 16-px row per line plus one, waiting for a click: rewards at x 200 (EXP 「ｎの経験値を獲得」), level-up at x 140 (with LEVELUP.WAV), item results at x 160 (240 wide) | FUN_00414b14, FUN_00414d40, FUN_004157a4 |
| Units | §15 "Map units" | FUN_00413918 |
| Unit hover | while the map waits: the hovered unit's name and 「%2d／%2d」 in 12-px text in its side's light colour with a 1-px outline in the dark one, the name centred 34 px above the tile and the numbers 20 px above it, kept inside the frame (below the tile near the top) | FUN_00413918 |
| Status window | 264×288 at (188,96) (camp: (288,152)) in the unit's side's colour: name 16 px bold in a raised box, class drawn twice a pixel apart, ＬＶ / EXP, ＨＰ / ＴＰ, the four derived and four base stats, equipment, the face in a sunken frame | FUN_00412448 |
| Battle screen | §15 "Battle scene" rows | FUN_00419574 |

The camera position when a map starts: §16 "Map start" (9 tiles left of and 6 above the first player unit, clamped).

## 14. Save files — `SAVEn.DAT` (114,400 B, fixed)

Written to the game folder, one file per save slot. There are no timestamps: saving the same state twice gives identical files (T16). Little-endian int32 throughout.

**Saving and loading (exe):** FUN_0040be6c writes the header (0x000 = whether the camp is up; the BGM name and the palette's tone copied in), the 100 map-unit records, the 50 roster records and the map's object table; a file that can't be opened plays BUZZER, else CLICK. FUN_0040bfbc reads the header, shows a black screen, stops the music and reads the rest; then it rebuilds a battle (FUN_0040e334, header 0x000 = 0) or the camp (FUN_0041db4c), reads the records again over what that set up, restores the tone, waits until 800 ms after it began and starts the saved BGM.

| Off | Size | Content |
|---|---|---|
| 0x00000 | 2000 | header |
| 0x007D0 | 100 × 640 | unit state of each **map object slot** (record i = SMAP object i) |
| 0x101D0 | 50 × 640 | **roster**: the player's army between maps (map_object = −1) |
| 0x17ED0 | 100 × 164 | copy of the current map's object table (SMAP §11.1 layout) |

**Header** (known fields; everything else zero in all 5 test saves):

| Off | Field | Conf. | Evidence |
|---|---|---|---|
| 0x000 | 1 when saved at a camp, 0 on a map | confirmed (exe) | FUN_0040be6c writes (top state == camp); SAVE4/6/7/8/5 at camp = 1, SAVE1/3/5-old mid-battle = 0 |
| 0x004 | the map being played, or the camp's map, 0-based (what loading opens) | confirmed (exe: FUN_004048ac loads it) | it looked like "camps reached" while every map from 2 on had a camp |
| 0x008 | the camera's left column, in tiles (the view's top-left tile) | confirmed (exe) | FUN_0040dee0 adds it to the pointer's tile (pointer x − 16 over 32); FUN_0040e4ec steps it toward the scroll target; a load turns scrolling off and leaves it as saved (FUN_0040e334). Was "7 / 1 / 2 across saves" |
| 0x00C | the camera's top row, in tiles | confirmed (exe) | as 0x008. Was "0, 6, 4, 5 across saves" |
| 0x010 | the next map, 0-based: 0x004 + 1 unless a "Next Map" command set it | confirmed (exe) | equal to the 1-based map number unless a Next Map ran |
| 0x014 | **switches**: 500 bytes, one per switch, 1 = ON | confirmed | the prologue's switch 79 「ＯＰ」 = byte 0x63 in every save; map 2's switches 0 and 1 = bytes 0x14, 0x15 (T18) |
| 0x20C | phase counter (ally turn t = 2t−1) | inferred | 1 at start, 7 mid-battle, 13 at the end |
| 0x210 | **inventory**: 100 × (i32 item, i32 count) | confirmed | buying one ポーション wrote (40, 1) (T18); a second one made it (40, 2), not a new entry (T18b) |
| 0x530 | inventory entries used | inferred | 0 → 1 after the purchase |
| 0x534 | gold | confirmed | 20 = in-game gold (T16) |
| 0x538 | party size | inferred | 2 (ルーティ, ジュナ) |
| 0x53C | current BGM file name (48 B) | confirmed | コミカル.MID in battle, 安らきﾞ.MID at camp |
| 0x56C | screen tone R, G, B, vividness | confirmed (exe) | FUN_0040be6c writes the palette's tone there; FUN_0040bfbc puts it back (FUN_004059b0) |
| 0x57C | unknown (5, 0, 1 across saves) | unknown | was guessed as the object count, but it's 1 on map 4 |
| 0x5DC | **variables 0–249**, int16 each | confirmed (exe) | T18c: choosing the option that adds 1 to variable 19 set byte 0x602 = 0x5DC + 19·2, and nothing else in 0x580–0x7CF. The exe reads and writes them as `short` at header + 0x5DC + 2·var (FUN_0040d18c's variable command, FUN_00424ee0's branch), which rules out 8-bit storage at 0x5EF. えるまに uses variables 0–33; 昴の騎士 the most of the other games, up to 199 |

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
| [13] | 出撃 (sorties on the next map) | confirmed (exe camp / deploy code) | every roster record that fought last |

**Battle saves** (header 0x000 = 0; SAVE1, SAVE3):
- Map-unit records exist only for units that have appeared, starting at index 1: units deployed from the army first, then map objects in order. [7] is the object index (−1 for army units) and [4] the army unit's deployment slot (ルーティ 0, ジュナ 1), else −1.
- Computer units' [50] (movement left) is 0 during the player's phase.
- The object-table copy at 0x17ED0 is the map's own, except that a spawned object's in-use word is 0, and **+124 + 2·(slot−1) is a 16-bit 'this event slot has fired' flag**. ルーティ's death and first-attack slots and ジュナ's first-attack slot on map 1 were set.
- The client writes battle saves the same way. Re-saving SAVE3 differs only in leftovers it can't know: an army unit's movement left and position before moving from earlier play, and ジュリア's [52] = 2 (maybe facing). Loading restores the units, phase, switches, inventory and fired events, and each unit's buffs ([36]–[38]) and poison / sleep counters ([45] / [46]), the records the exe's own code keeps them in (FUN_00413918, FUN_0041d5e4).

**Writing saves (client):** a camp save sets header 0x000 = 1, 0x004 (camps), 0x010 (next map, 1-based), switches, inventory and its count, gold, party size, BGM name, tone and variables. Each roster record is rebuilt from unit, class, level, EXP, base stats and equipment: in-use flags, stats with the accessory, Attack/Accuracy/Defense/MDef, range, movement, terrain costs and known spells. Fields that are leftovers of the last battle ([5], [6], [50], [52], [54]–[56]) and unknown header fields are kept from the slot's previous file or the last save loaded. Checks (`SaveFileTests`):
- Re-saving SAVE6 at its own camp reproduces the original file byte for byte.
- Every camp save's roster records rebuild from nothing exactly, apart from those leftovers.

Python: `parse_save()` in `re/srpg95_data.py`.

## 15. Battle runtime (client behaviour)

What the client does on a battle map, in the camp and on the title (`client/src/Srpg95.Data/Rules/`, `client/src/Erumani/`), and its basis: mostly the exe (§16), with the tests and recordings as cross-checks. The client's own additions (forecast and terrain panels, popups with the battle scenes off, keyboard control, skipping) are named as such.

| Behaviour | Client | Basis |
|---|---|---|
| Stat formulas (Attack, Accuracy, Defense, MDef) | §14 formulas | confirmed on 172 save records |
| Hit, crit, damage, double attack, EXP, level-up | the exe's formulas: §16 "Physical strike", "Exchange", "EXP / rewards", "Spells", "Level-up" | exe |
| Movement range | Dijkstra over the class's terrain costs; units of the same side can be passed but not stopped on, any other unit blocks (neutral ones too) | exe (FUN_004111cc) |
| Undo a move | cancel at the command menu returns the unit to where it started | confirmed (T19b) |
| End of player turn | only through ターン終了, from the menu opened by clicking an empty tile, even when every ally has acted | confirmed (T19a) |
| Overlays | a tile shows its area value (+0x848) if it has one, else its range value (+0x208), as SYSTEM.BMP's range block number value − 1: **1 cyan** a move's reach, or the actor of an action under way; **2 salmon** the reach of an attack, a spell or a medicine; **3 orange** the area of an attack or a harmful spell (type 0/1); **4 blue** that of a support spell or a medicine. Stripes: only the tile's odd pixel rows (FUN_00406c38), the map between them; a unit the action reaches stands on a solid tile (value + 4, every row). The pattern drifts up-left: the 32×32 window sits (tick/2)·2 px down and right in the block, wrapping at 64 (the blocks repeat every 32, and every 16 diagonally). The area follows the cursor over the reach: the tile under it for an attack (orange, solid on an enemy) or a medicine (blue, solid on someone it can be given to), the spell's effect grid around it (FUN_00411a48), everywhere for a spell covering the whole map. Units are drawn on top | exe (FUN_004132a8, FUN_00411d74 builds the sheet); T19c pixel-matched the move / attack stripes; T21b's blue and T22's orange are areas |
| Strike results | the battle scene (below). With the scenes turned off (an option) the client shows its own one-line popups | client option |
| Death events vs victory | a unit's "dies" events play first, then the victory events, then COMPLETE!! | confirmed on map 1 (T19e); order rules in §16 |
| Victory | `TELOP3.BMP` "COMPLETE!!" (512×96, index 0 transparent) comes in like a turn caption and stays; the fanfare (map BGM slot 6) starts at its tick 20; from tick 60 the fanfare's end, or a click, fades the screen out 5 % a tick; then the next map's camp or the map | exe (FUN_0041083c, FUN_0041421c); T19e, T20d |
| Turn captions | `TELOP1.BMP` "PLAYER TURN" / `TELOP2.BMP` "ENEMY TURN" at each phase start (after the opening too): the caption's counter goes up a tick at a time from 0; drawn at y 192, x 64 (centred) over the screen frame, coming in from the right 96 px a tick while the counter is under 8; it leaves to the left at the same speed from 33 and ends at 40; clicks do nothing meanwhile | exe (FUN_0040e5a8, FUN_0041421c); T20a |
| Turn music | player-turn BGM (map slot 1) / enemy-turn BGM (slot 2) from tick 20 of each turn caption, replacing whatever an event played; the same file isn't restarted | exe (FUN_0040e5a8, FUN_0040931c); T20b |
| Game over | `GAMEOVER.BMP` full screen | confirmed (T20c) |
| Losing | every ally down (neutral units don't count), or a player-side unit from the map's character list down → game over. Map 1 goes on without ルーティ: EV000101 is just her remark. An event's map_complete "lose" is not a defeat: see §16 | confirmed for map 1 (T19d), rule from the exe |
| Winning without a "Next map" command | goes on to the next map number | exe: header 0x010 = current + 1 at every map start (FUN_0041d5e4), "Next Map" overwrites it |
| Spells known | the class's skill list: every spell whose level the unit has reached. Reaching one on level-up shows 「〜を習得した」 (WORD 73) | exe (§16 "Level-up"); the saves' spell lists |
| Spell targeting | the range grid around the caster, or the whole map (FUN_004117d0). A click casts only when the aim is in the range and its area holds someone the spell affects; a spell whose effect covers the whole map ignores the aim and is cast by a click anywhere, the frame included. The effect grid around the aim, or the whole map, is hit. Who is affected: §16 "Spells' targets" | exe (FUN_00410050, FUN_00411a48); grids §5 |
| Spell effects | Damage: magic hit/damage formulas, no counter. Degrade: hit roll; class resist or accessory "Safe Against" blocks it. Poison 3 turns; sleep loses 2 of the unit's turns and it acts on the 3rd; Atk/Def Down = −(Value + Mag·Relation/10). Raise: HP heal (capped), Atk/Def Up = Value + Mag·Relation/10, cure poison, cleanse, extra turn (target may act again). Buffs decay 1 per own turn. A spell whose effect grid has a hollow centre doesn't touch the aim tile (剣の舞: 「術者は含まない」) | exe (§16 "Spells", "Buffs", "Phases"); 剣の舞 +3 and the 1-per-turn decay confirmed (T21c, T21d) |
| Spell EXP | Damage: the attack's EXP summed over every enemy it hurt (kill or hit); Degrade/Raise: the spell's Exp Value per cast; cast from a magic item: the same (no TP spent) | exe (FUN_0041ba0c, FUN_0041b6ec; FUN_0040fda8 → FUN_0041c8c0); 剣の舞 25 (T21c) |
| Battle items | ITEM types Medicine, Stat-up and Magic the user's class may use, and Class-change items (level and from → to pair). A medicine reaches the distances of its range type (+0x268) by FUN_00411ba0's tables (0x43323c / 0x43325c): 0 → 0 (the user), 1 → 0–1, 2 → 0–2, 3 → 1, 4 → 1–2, 5 → 1–3, 6 → 1–4, 7 → 2–9; it can be given to a unit of the user's side (the player's units and neutral ones together), not an unattackable neutral one (FUN_00411c90). Stat-up and class-change items work on the user. A magic item casts its spell with no MP cost, one less at the click. Using one ends the action and uses up one | exe (FUN_0040f930, FUN_0040f650's list, FUN_0040b074) |
| Spell list | green panel ((WORD 3's bytes + 1)/2 + 17) units wide (228 px for ＴＰ), its corner 80 px left of the caster's tile at the tile's top, kept inside the frame and not above y 96; a row per known spell plus one: 「%-22s」 name, ＴＰ, 「%4d／%2d」 cost and current TP, greyed when TP is short; the pointer on the first row | exe (FUN_00417548); T21 screenshots match |
| Item list | 180×240 (15 rows), its corner 48 px left of the unit's tile at its top, kept inside the frame and not above y 96; 12 rows a page, 「%-22s×%2d」 in the inventory's order, greyed when unusable; footer: an etched rule, then a row lower ページ at x 12 and 「%2d／%2d」 at x 60, and SYSTEM.BMP's page arrows 16×16 at y 304 (◁ x 0, ▷ x 32; 16 further right while pressed) 54 and 30 px from the right | exe (FUN_00417f54, FUN_00418108, FUN_00420ca8); T21 |
| Description window | green panel (80,32) 480×48 while a list is open: 「name：」 at (+12, +8), the description at (+12, +24), 12 px | exe (FUN_004142d4); T21 |
| Result messages | no name, full-width digits: 「ＨＰが８ポイント回復した」 (WORD 2+64+n+65+66), the item's nominal amounts (FUN_0040b188); in one panel at (160,64), 240 wide. Rewards at x 200, level-up at x 140 (§13 "Panels on the map") | exe (FUN_004157a4, FUN_00414b14, FUN_00414d40); T21e |
| Battle scene: lead-in | after the player's click (state 0x12's counter at 5) or the computer's decision (at 0) the map keeps the action's overlay — the reach cleared, the actor's tile cyan, the area in its colour, the units met solid — and the counter goes up a tick at a time while the view isn't scrolling: at 5 the "before" events get their turn (a tick after a click, 6 after a decision); from 10 the palette dims 20 % a tick (80, 60, 40, 20 %); after 14 the scene begins (that last frame stays a tick longer). From the click to the scene: 10 ticks (the computer's: 15) | exe (FUN_00410388; FUN_0040dee0's +0x91 dimming). The recordings' extra ~0.27 s at 20 % is the original loading the scene — its music and bitmaps at c 0 of FUN_00419574 — before it can show black |
| Battle scene: steps | an attack is a strike step per blow (FUN_0041a9c4 from FUN_0041b4b0: the attack, the counter, the double strike); a spell a cast step (the caster alone, its class's magic-to-enemy or magic-to-ally animation, FUN_0041b6ec) and a step per unit it lands on (that unit alone, the spell's animation, FUN_0041ba0c). The computer's units stand on the left, everyone else's on the right (by the acting unit, or for a landing the target: +0x60f0); animations are authored with the actor on the right and mirrored otherwise. A strike shows both units' panels, a cast and a landing only their unit's, the other half black | exe (FUN_00419574 and its step builders); T22 recordings |
| Battle scene: a step | a counter c from 0, a tick each, to the step's length: frames + 45 for a strike, + 35 for a damage or recovery spell, plus the drain beyond 5 ticks, + 25 when the target drops to 0 and + 30 more for its death message; a cast frames + 20, any other spell frames + 25. Brightness 0 for c 0–1, then 25·c (50, 75), 100, and down 25 a tick over the last 4. The stage (BACK_nnn of the target's terrain, the caster's for a cast) and the units slide in from the acting side 106 px a tick over c 0–9 and out the other way over the last 15. The animation runs from c 15, a frame a tick, holding its last frame; its hit or miss sound plays at that frame + 15 (hit_frame is 0-based). GAME.DAT [4] slows the ticks from c 15 to 15 before the end | exe (FUN_00419574); r1 at 60 fps: the 3-step brightening, the stage widening 110 → 534 px, the slide-out and the length (155 against ~154) |
| Battle scene: hit | from the hit frame + 15: a unit that is hit shows a white silhouette on even ticks for the animation's flash time and, if damaged, shakes 8 px away every other pair of ticks; a missed one sidesteps 4, 8, 8, 4 px. A critical sets the brightness for 10 ticks to 140, 180, 80, 160, 100, 180, 60, 90, 180, 200 (DAT_0043341c; the exe indexes it by the counter alone, reading stack garbage unless the hit frame is 24–33, so the client applies the evident intent). From 4 ticks later until 20 before the end the number — the damage as rolled, 0 for a miss, or the HP restored — stands at the unit's feet in SYSTEM.BMP's digits (damage y 128, recovery y 160, tens 24 px left), bouncing 72, 60, 40, 16, 0, 8, 20, 30, 30, 20, 8 px over 11 ticks, while the HP bar moves linearly over moved × 37 / max HP ticks; a cast's TP drops a tick after its hit | exe (FUN_00419574, FUN_0041a9c4); T22: purple 0 on a miss, red recovery digits |
| Battle scene: defeat | a unit brought to 0 blinks (hidden on even ticks) from 40 ticks before the end and is gone 30 before; DOWN.WAV plays 58 before the end (the exe also plays it after a spell landing without a number, whose "HP after" stays 0 — reproduced); its UNIT.DAT death message, if any, replaces the panels for the last 80 ticks: the face at (32,336), the balloon at (192,336) with its tail left, up to 5 lines from (210,348). The scene never waits for a click | exe (FUN_00419574, FUN_0041d24c); r1: the message 79 ticks before the end, 99.97 % of the strip |
| Battle scene: panels | two 320×160 panels at y 320 in the side's colour (computer red left, everyone else blue right), text in the light swatch: the name 16 px bold, an etched rule, ＨＰ / ＴＰ with 「%2d／%2d」 at 272 and bars of SYSTEM.BMP 8×16 pieces (x 48 full, 56 empty; row 0 HP, 16 TP) in a sunken frame, the stat rows (攻撃力 / 物理防御 / 特殊補正値 and 攻撃精度 / 特殊防御 / 地形効果) with 「%4d」 60 px after each label | exe (FUN_00419574); r1: identical text boxes |
| Battle scene: after | the EXP message back on the map at (200,64), no name, full-width digits: 「１７の経験値を獲得」; the level-up panel at (140,64) with LEVELUP.WAV | exe (FUN_00414b14, FUN_00410a88 → FUN_00414d40); T22 |
| Battle scene: background | `BACK_nnn` of the terrain under the unit attacked or landed on (the caster's for a cast). With GAME.DAT [1] = 1 a strike whose two units stand on terrains with different backgrounds shows the striker's own background on its half (the left half for a computer's unit, else the right) | exe (FUN_0041a9c4 keeps both, +0x6114 target / +0x6118 striker; the scene loader copies the half); user |
| First Attack (Before/After) slots | fire for the first fight between the slot's unit and its target unit, in either direction (the actor's object's slots, then the other's). Most maps put them on the enemy, targeting an ally (SMAP_005 …); map 1 puts them on ルーティ and ジュナ, targeting ナス (their EV000102 / EV000301 dialogue). 「Before」 events play in the lead-in, with the overlay still up; 「After」 ones after the EXP | exe (FUN_00410388, FUN_0040cd98); data |
| Fight rolls | an attack or a spell is rolled whole when it's chosen — hits, criticals, damage, each strike on the HP the earlier ones left, statuses, the caster's TP after, the EXP and rewards (FUN_0041b4b0 / FUN_0041c8c0, before state 0x12) — so the "before" events run with the outcome fixed: maps 49 and 69's EV005101 promotes unit 241 to class 15 before its first fight with unit 208, which is still fought with the old class's stats and animations. The scene then puts each step's outcome on the units at that step's end: a strike's or a damage / healing spell's HP as rolled (overwriting whatever happened in between), a cast's TP, poison / sleep (3 turns), buffs, cures, another action. After the scene the actor has acted; then the EXP rolled for a player unit still standing goes in, its level-ups rolled at that moment, with the rewards (FUN_0041093c) — so the scene still shows the old max HP and stats | exe (FUN_0041b4b0, FUN_0041c8c0, FUN_00419574, FUN_0041093c) |
| Spell landings | in the map's row order (top to bottom, each row left to right: FUN_0041c8c0 scans the area grid). Damage and healing get a number and a moving bar; a damage spell's miss a 0; status and support spells none — a status spell's miss shows no 0 either (FUN_0041ba0c leaves its number flag off) | exe |
| Acted (END) | a player's attack or spell: once its scene is over (FUN_00419574's end); a magic item or a medicine: at the click (FUN_0040fda8); 行動終了 at once. A computer unit: after its scene when it attacks or casts, at once when it only walks or has nothing to do (FUN_0041589c) | exe |
| Battle music | chosen at each step's start: map BGM slot 5 when a leader of the computer's takes part in that step (a strike's two units, a cast's caster, a landing's caster and target), else 3 in the player's phase and 4 in the computer's; the same file playing goes on. A player's spell on a boss thus starts on the normal battle theme for the cast and turns to the boss theme as it lands. The phase music returns afterwards | exe (FUN_00419574; +0x6122 from FUN_0041a9c4 / FUN_0041b6ec / FUN_0041ba0c; FUN_0040931c); T20b |
| Enemy AI | as the exe decides it, see §16 "Enemy AI" | exe |
| Camp | CAMP_nnn full screen, camp BGM (map slot 0). Menu 96×128 at (16,16): 次のステージ (once a unit is set to 出撃) / ユニット編成 (once there is an army) / アイテム購入 / アイテム売却 / セーブする / ロードする / ゲーム終了; its questions and file lists take its place. Every menu opens with the pointer on its first row and comes back to the row it was left from. Shown before a map whose camp_before_battle is set, after the previous map's COMPLETE!! | exe (FUN_0041f6d4, FUN_00413100); T17b, T23 |
| Camp: ユニット編成 | the army at (88,40), 180×400, 22 a page — names at x 100, 出撃 / ―― at x 232 — beside the commands at (288,40), 108×96 (武器 / 防具 / 装飾品を替える, 道具を使う, 技を見る; each there while its slot holds something or the inventory has something for it), the chosen unit's status window at (288,152) and 「出撃ユニット数 n／max」 (max = the next map's start positions) at (432,40), 120×48. The army and the commands answer the pointer together: a click on a name chooses the unit, one on its 出撃 part chooses and toggles it — on only while fewer than max are, never off for one of the map's characters, not at all when the map fixes the army | exe (FUN_004202dc, FUN_00420564, FUN_004208cc, FUN_0041e210); T23 |
| Camp: equipment / 道具を使う / skills | the command's list at (80,104) with the description at (80,32): equipment of the slot's kind the class may use, 「（装備をはずす）」 first when something is held; for 道具を使う stat-up items the class may use and class-change items for its level and class; 「%-22s×%2d」 in the inventory's order, 18 a page; for 技を見る the known spells, 「%-22sＴＰ%4d」. A pick equips, takes off or uses the item at once, without a message, and goes back to the commands. Stat-ups add positive values (max HP/MP to 60, the rest to 30); a class change starts the new class at level 1, moves Str/Mag/Skl/Sta by the classes' initial-stat difference and sends unusable equipment to the inventory | exe (FUN_004209e8, FUN_00420e60, FUN_0041e4b0, FUN_0040b074, FUN_0040b8f0, FUN_0040bb0c); T23 |
| Camp: shop | at (80,96), 264×336, 16 a page: 「  アイテム … 値段 (下取り額) … 所持数」, a rule, then 「%-22s%10d%8d」; the gold, 「%14d,肉」 (WORD 31 includes the comma), in a panel at (368,96). Buying needs the price and fewer than 99 held; an item sells for half its price, 1 for a price of 1, not at all without one | exe (FUN_004210f4, FUN_00421478, FUN_0041e94c, FUN_0041eba8); T23 (ルーティのマント 10 → 5) |
| Camp: 次のステージ | 「キャンプを終了して／進撃を開始します」, a rule and はい／いいえ in the menu's place at (16,16). No separate sortie screen | exe; T23 |
| Title | TITLE.BMP full screen; the menu at (266,320), 108 wide, with はじめませう (WORD 47) / ロードする / ゲーム終了 (the client adds 環境設定, so 80 high instead of 64); ロードする replaces it with the load list at (272,280), 96×176 (WORD 54, a rule, ファイル１–８), the pointer on its first file; a right click goes back to ロードする. A new game starts at GAME.DAT [0] | exe (FUN_00421bd4); T24 recording |
| System sounds | CLICK.WAV (confirm, map selections, advancing text), CANCEL.WAV, BUZZER.WAV (an unavailable choice), DOWN.WAV (a unit falls) — FUN_00409428's 0–3 — and LEVELUP.WAV when the level-up panel opens or a class changes | exe (FUN_00409428, FUN_00410a88, FUN_0040be00); the recordings' audio matches (≥ 0.82) |
| Action menu | 攻撃 / 必殺技 / 装備 / アイテム / 行動終了 — the original's order is 行動終了 / 攻撃 / 必殺技 / 装備 / アイテム; the client puts 行動終了 last (player request). ((longest + 1)/2 + 2) units wide (at least 4), 32 px right of the unit's tile at its top, moved left / up near the frame's edges (the exe checks against a 6-unit width). After a move with a target in reach the targets come at once instead; otherwise the menu opens on 行動終了, and coming back from a list or the targets reopens it on that entry (攻撃 from the targets only when they came from the menu). 装備 switches the weapon without ending the action | exe (FUN_004171e0, FUN_0040ecf0, FUN_004177e8); T23 for the order |
| Turn menu | a click on an empty tile: ターン終了 / 全体マップ / セーブする / ロードする / ゲーム終了, 7 units wide, 32 px left of the click, inside the frame; its questions (はい／いいえ 36 px wide under an etched rule) and file lists by it. Cancel puts the pointer back where the menu was opened | exe (FUN_00418614, FUN_004104e4, FUN_00418824) |
| Pointer | every menu or list opening (or reopening on a row) warps the pointer to that row's centre; picking a unit's action puts it on the unit's tile centre; the spot where the turn menu was opened and where a move ended are saved and restored on cancel. The map cursor follows the pointer only in states 2, 6, 0x11, 0x10 and 0xf | exe (FUN_00409d24, FUN_00409cac / FUN_00409cd4, FUN_00417188, FUN_0040dee0) |
| Edge scroll | holding the left button with the pointer on the frame scrolls the view a tile a tick that way, in the player's phase while the map waits or a unit, target, aim or medicine is chosen (states 2, 6, 0x11, 0x10, 0xf) and the view isn't already moving | exe (FUN_0040dee0, FUN_00416f38) |
| Map units | the standing units by their row on the screen, then the moving ones (sort key y + 1000), each a 64×64 cell centred on its tile. One that is stepping (walking, or an event's turn) shows its way's row — 1 up, 2 right, 3 down, 4 left — cycling 0, 0, 1, 2, 2, 1 every 2 ticks; the player's chosen unit (states 6/7) its way's row every 3 ticks; any other row 0 every 4 ticks, or, once it has acted (any side), frame 0 under SYSTEM.BMP's END. A poisoned unit shows PSN, a sleeping one SLP instead, a new frame every 4 ticks | exe (FUN_00413918) |
| Fallen allies | units that fell on the previous map come back at the camp (GAME.DAT [5] = 0). With [5] set (ＯtoＺ) they are lost: at the map's end a fallen unit holding a roster entry (record [4] ≥ 0: deployed from the army, or the first of its id an "add ally" took in) has the entry deleted, the later entries moving up, and a fallen one without doesn't join | confirmed (user, T23d) and exe (FUN_0041d5e4's first loop, FUN_004244d8 sets [4]) |
| Enemy phase | enemies by record number, then neutral units (§16 "Phases"). The camera "shows" each unit that acts (middle-box rule, a tile per tick) while it walks a tile per 8 ticks | exe; scrolling also seen in T19f |

Every scripted run is reproducible: in screenshot mode the client seeds the battle RNG with 1, ignores the real keyboard and mouse, and `--keys` feeds input. The client writes the state and a token trace next to the PNG as `.txt`.

## 16. From the executable (SRPGEXEC.EXE disassembly)

Decompiled with Ghidra 12.1.4 (`re/ghidra_scripts/ExportDecompiled.java`, output in `re/exe/`, not committed: it is derived from the game's executable). The program is Win32, probably Borland C++, 904 functions. It uses DirectDraw for graphics, `Harmony.dll` for MIDI/WAV, and MCI. Function names below are Ghidra's addresses. Unit records in memory have the same layout as the save files (§14, 640 B).

| Topic | What the code does | Where |
|---|---|---|
| GAME.DAT | the 200 bytes are game options: [0] start map; [1] = 1 → battle background from both sides' terrain; [2] counter-attacks on; [3] double attacks on; [4] battle animations slowed: from the scene's tick 15 to 15 before its end the tick is [4]·33/100 + 33 ms (FUN_00408530), else 33; [5] ≠ 0 → units that fell aren't carried into the roster at the map's end. えるまに: 98, 0, 1, 0, 0, 0 → **counters on, double attacks off, no slowdown, fallen units come back** | FUN_00403758 loads it; uses in FUN_0041b4b0, FUN_0041a5b0, FUN_00419574, FUN_0041d5e4 |
| Derived stats | Attack = Str(+acc) + weapon power + buff; Speed = Skl + usability/3 − armor weight; Defense = Sta + armor def + buff; MDef = Mag/2 + armor mdef + buff. **Each clamped to 0–99** | FUN_0040a68c |
| Physical strike | hit = 70 + 2·Skl + usability − (3·dSkl − weight) − terrain def; hit if rand()%100 < hit. Crit (only after a hit) = Skl + critical − (dSkl/2 + crit avoid). Attack ×3/2 if an attribute hits an unguarded weakness, else ×4/5 if the armor guards every attribute (help says 50 %). Damage = attack − Defense − terrain/10 (integer, truncating), clamped 0–99. Crit: damage×5/2, cap 99; no crit when damage is 0 | FUN_0041a9c4 |
| Exchange | strike; counter if GAME.DAT[2], defender alive and not disabled (record [46]/[47] = 0), attacker within the defender's range; double strike if GAME.DAT[3] and defender speed + 3 ≤ attacker speed | FUN_0041b4b0 |
| EXP / rewards | only for a player unit's strikes that did > 0 damage: kill 40 + 2·(dLv − Lv), else 15 + dLv − Lv, min 1; the exchange keeps the **largest** value. Damage spells **sum** it over targets. A kill adds the defeated object's reward gold (+0x50) and item (+0x4C) | FUN_0041a9c4, FUN_0041ba0c |
| Spells | damage: hit = 2·Skl + accuracy − (3·dSkl − weight) − terrain; damage = (Mag + damage, attributes as above) − MDef − terrain/10, 0–99, no criticals. Status spells hit on accuracy alone, blocked by class resist or accessory "safe" flags; power = value + relation·Mag/10. Raise/heal spells give their Exp Value | FUN_0041ba0c, FUN_0041b6ec |
| Level-up | while EXP ≥ 100: −100, level +1 (at 20: EXP 0). Each stat gains growth/100 + (rand()%100 < growth%100), growth = UNIT.DAT growth + accessory bonus; caps HP/MP 60, Str/Mag/Skl/Sta 30; current HP/MP rise too; a class skill whose level equals the new level is learned | FUN_0040a8.. (level-up loop) |
| Boss music | each step's +0x6122: set when a computer unit whose object is a leader takes part in it (a strike: either unit; a cast: the caster; a landing: the caster or the target); FUN_00419574 picks battle BGM 5 for it, else 3 / 4 by the phase, at the step's c 0 | FUN_0041a9c4, FUN_0041b6ec, FUN_0041ba0c (0x6122), FUN_00419574 |
| Battle scene timing | per step, a counter c: lengths, brightness, slide, animation from c 15, sounds at hit/miss frame + 15, the number, the drain, the blink and the death message as in §15 "Battle scene" | FUN_00419574 |
| Unit record [15], [16] | [15] = 1 for a neutral object (SMAP side 2), [16] = the object's "unattackable" flag (+0xA0); [3] = 1 only for enemy objects (side 0), so neutral units count as player-side in the AI's eyes | FUN_00406.. (unit spawn, line ~6719) |
| Buffs | Power/Defense Up only replace a lower value, Downs only a higher one (no stacking, no overwriting a stronger one). Cleanse also clears a third status ([47]) and a third buff ([38]) not used by these spells | battle-end code near FUN_00417.. |
| Enemy AI: pattern | the highest-numbered move pattern whose switch is ON (pattern 1 has none). Asleep ([46]): done | FUN_0041589c |
| Enemy AI: spells | skipped for Specific and for neutral units. Every known spell with MP cost ≤ MP is scored at every aim within range of a tile reachable this turn (anywhere for whole-map range); the best spell scoring > 0 is cast, from the reachable tile closest by walk cost (own tile for whole-map range; aim = own tile for whole-map effect). Spells come **before** weapon attacks | FUN_0041589c, FUN_00416478, FUN_00416c44 |
| Enemy AI: spell score | sum over every unit in the area, as they stand now (the caster only if the aim is in range from its tile). Player-side = allies and neutral units. Damage: player-side (dmg + Mag − MDef) × 5 + 80 if > 0, own side −150. Status: own side −150; poison/sleep rand(20) + 2·HP + 40 if not already; attack/defense down rand(40) + 10·power + 30 **only if the target's buff is already above power/2** (as written: effectively never). Support: player-side −150; heal min(power, missing) × 10 + 50; power up rand(20) + 10·power + 3·Attack + 30 if buff < power/2 (defense up: 2·Attack); cure poison rand(20) + 30·poison + 40; cleanse 20·poison + 40, +30·poison + 50 if asleep (sic), +10·abs(buff) + 60 per buff below −2; extra action: total := rand(20) + 95 for a unit that acted. Floor 0. power = value + relation·Mag/10 | FUN_0041664c |
| Enemy AI: attacks | skipped for Specific and neutral units. Targets: allies and attackable neutral units. For each, the tile to attack from is the free tile anywhere on the map, at weapon range, with the lowest walk cost − terrain recovery − terrain defense/10 (−4 if reachable this turn). Value = (Attack − Defense) × 3 + 100 (+200 instead if ≥ the target's HP) − walk cost; 1 if damage < 1. The highest value > 1 whose tile is reachable this turn is attacked | FUN_004160f0, FUN_004162c4 |
| Enemy AI: moving | Rush: toward the target with the highest value (neutral: the first ally unit); Specific: toward its location; Wait, None: stay. None units have 0 movement but still cast/attack from their tile. The path is traced back from the goal, each step to the first neighbour closer to the unit (x−1, x+1, y−1, y+1 if the unit is farther sideways, else y−1, y+1, x−1, x+1); the unit walks it as far as this turn allows and stops on the last free tile | FUN_0041589c, FUN_004113c4 |

| Event checkpoint (FUN_0040c24c) | runs whenever the game is idle between actions, and after every event. It starts **one** event, then looks again from the top once that event ends: (1) contact-after-movement slots (cond 9) of the unit that just acted, then of every unit; (2) each "Other" object whose switch condition holds, by object index; (3) each unit on the map by record number (the army first, then objects in the order they appeared), a fallen unit being taken off the map once its own slots had their chance; (4) lost? → game over; (5) won? → the victory slots of "Other" objects (index order), then COMPLETE!!. Every object's slots are tried from **slot 5 down to slot 1** | FUN_0040c24c, FUN_0040c584 |
| Fired flags | one 16-bit word per object slot (+0x7C, saved in the battle save's object table). Set for turn, contact, death, first-attack and victory slots. **Never set for switch (1), pass-by (6) and stop-on (7) slots**: those run again whenever their condition holds again. The game relies on it: 28話's colosseum menu and おまけ１'s hub menu are switch events that end without turning their switch off (they come back until a choice turns it off); 開発室's warp / comment tiles and トレーニング２'s exit are stop-on tiles with a はい／いいえ that can be refused and used again; treasure tiles guard themselves with a switch branch. A slot with a condition but no script is skipped | FUN_0040c584, FUN_0040cfe0 |
| Slot conditions | 1 switch ON (while ON); 2 phase counter equal to the slot's turn; 4 a unit next to the owner (target unit, or "Nothing" = the other side: enemies vs. everyone else, and neutral vs. player units); 5 the owner has fallen (a target = who defeated it); 6 / 7 a unit went over / stopped on the "Other" object's tile (target unit, or "Nothing" = a player unit that isn't neutral); 8 victory; 9 like 4, checked when an action is over; 10 / 11 first attack before / after (between the attacker and each unit of the other side it meets; the owner must be alive) | FUN_0040c584, FUN_0040cad8, FUN_0040cd98 |
| Moves | a unit's move (any side) marks the "Other" objects on every tile it walks onto (pass-by) and on its last tile (stop-on; standing still marks both); undoing the move clears every mark. Then contact slots (cond 4), the mover first: one that fires **ends the mover's action** (no action menu; an enemy doesn't attack). Pass-by / stop-on events themselves wait for the next checkpoint, i.e. after the unit's action | FUN_0040ecf0, FUN_0040f0ac |
| Units appearing and leaving | whenever a switch changes (event command, a Specific pattern's switch) and at map start: a unit whose object's switch condition is OFF leaves the map (its object waits on the tile it stood on); every waiting object whose condition holds comes on in index order if no living unit stands on its tile, with a fresh record from UNIT.DAT (a returning unit starts afresh). This happens at once, in the middle of a cutscene, so a later move command can walk a unit that has just appeared | FUN_00409658 |
| Map start | the army (roster units with 出撃, roster order) goes onto the first free start positions, then the objects come on; map objects always use UNIT.DAT stats (equipment only if the class may use it). The camera starts 9 tiles left of and 6 above the first player unit (clamped). OPENING runs; at its end the camera scrolls to the first player unit, then PLAYER TURN, then the checkpoint (turn-1 events). A picture still shown when any event ends is removed | FUN_0041dc34, FUN_0040d18c |
| Camera | "show tile" scrolls just enough to bring the tile into the view's middle box (columns 6–12, rows 5–8 of 19×14), a tile per 30 Hz tick; used by scroll_screen (which waits for it), the enemy phase, move commands and the end of the opening. Units walk a tile per 8 ticks (0.27 s) | FUN_00416fb0, FUN_00423870, FUN_0040ecf0 |
| Phases | ターン終了 → enemy phase: every enemy that hasn't acted, by record number, then every neutral unit (same phase counter), the checkpoint between units; then the camera goes to the first player unit and the player's phase starts. Upkeep at a side's phase start (allies **and neutral units** at the player's): poison −HP/4 (rounded down, never below 1), counters −1, buffs one step toward 0, terrain recovery (an "Other" object's graphic counts as the tile), HP kept within 1–max. Clicking an enemy or neutral unit shows its movement range (right click: status) | FUN_00410b90, FUN_0040e658, FUN_0040ebcc |
| Terrain under "Other" objects | an "Other" object whose switch condition holds and that has a graphic replaces the map's chip at its tile for every terrain purpose (movement, defense, recovery, battle background) | FUN_0040c1d4 |
| Win / lose | lose: a player-side unit (ally or neutral) from the character list has fallen, or no ally is alive. Win 0: no enemy on the map; win 1: no enemy leader on the map; win 2: only by event | FUN_0040c24c |
| Map end | map_complete "win": COMPLETE!! and the fanfare, then on; map_complete "lose": straight on without them (about 20 maps use it as a plain transition: 8話前半, 11話前半, 12話前編/中編, 13話, 19話, 20話, 21話, 分岐, 30話…). Either way: header 0x004 := 0x010 (the "Next Map" target, else the following map); every player unit on the map that isn't a guest or neutral (fallen ones too) is copied into the roster (new ones appended, 出撃 on, HP/MP full, statuses cleared); the roster is sorted by unit id; the next map's camp comes first if it has one and the roster isn't empty, where the character list is forced to sortie and the rest is trimmed to the start positions in roster order (GAME.DAT [5] = 1 would drop fallen units: off here) | FUN_0041083c, FUN_0041d5e4 |
| Party commands | add ally: units of that id on the map that the computer controls change sides (4話: ハンス and ゴーバック defect mid-battle); the first joins the roster as it is (出撃 on) unless already there; with none on the map it joins from UNIT.DAT (出撃 off). Remove ally: out of the roster, and such units on the map turn neutral. Class change: roster entry and units on the map, Str/Mag/Skl/Sta by the classes' initial-stat difference (1–30), unusable equipment to the inventory, level kept (a class-change item sets it to 1). Equipment change: the item's type slot, the old item to the inventory (once per record, so a deployed unit returns it twice) | FUN_004244d8, FUN_00424790, FUN_00424870, FUN_0042490c, FUN_0040bb0c |
| Branch conditions | turn "before" is inclusive (≤); unit "alive" = on the map (a fallen unit counts until the checkpoint takes it off), "dead" = taken off after falling, "not present" = no record at all (a fallen unit is still present), "player controlled" = an ally record or in the roster (the roster loop skips its first entry); "party has item" also counts items equipped by player-side units on the map | FUN_00424ee0, FUN_00424e70 |
| Commands | a message directly followed by a choice doesn't wait: the choice comes at once under it. Variables stay within ±1000; dividing by 0 gives 0. Items: at most 99 each and 100 kinds; gold 0–9,999,999. Move commands give the steps to every unit of that id; scripted steps onto an occupied or impassable tile are skipped (still taking their time). return_to_title erases the screen and goes to the title 2.5 s later | FUN_0040d18c, FUN_0040add8, FUN_00424a68 |
| Saves | header 0x004 = the map being played / the camp's map (what a load opens), 0x010 = the next map; roster record [13] = 出撃. Battle-save records sit at their record numbers; [14] guest, [15] neutral, [16] unattackable, [45]/[46] poison/sleep | FUN_0040e334, FUN_004048ac |
| What a save holds | FUN_0040be6c writes the 2,000-byte header (camp flag, map, camera, the music's name, the tone, switches, variables, …), the 100 unit records, the 50 roster records and the object table — no play time and no date (only the file's own). The game's files are SAVE1–8.DAT (WORD.DAT 81–88 name them ファイル１–８). A map's name for a list is MAPNAME.DAT's (100 × 32 bytes, the editor's map list), which matches every map file's own name in all five games | FUN_0040be6c, FUN_0040bfbc |
| EasySRPG95's additions (not the original's) | the extended save screen: SAVE1–100.DAT (the original sees 1–8), and beside the saves an EasySRPG95 folder with SAVEn.png (the screen when saved) and saves.json (play time, counted by the client, and the save's hash — a slot the original saved over since shows neither) | client |
| Saving in an event (EasySRPG95's, not the original's) | the original saves only between events (turn menu, camp). EasySRPG95's right-click menu during an event saves the map as an ordinary battle save in SAVE9–100.DAT (never 1–8, which the original would load with the rest of the event lost) and the rest in that save's saves.json note ("Scene"): the script's name and its stack of blocks (a branch body −1, a choice case n, each with its next command), the face, the message or choice up, a wait's time left; what follows the event (the opening's first turn, a fight held up by its "before" events as rolled, the rest of its "after" events, the enemy phase's group); pass-by and stop-on marks, killers of fallen units, unit facings and queued move steps; the camera in pixels, the brightness (an erase's 0 / 200), the pictures, the music and the tick. Running fades, pans and moves are jumped to their ends before it's written | client |
| Items in battle | medicine, stat-up and magic items the class may use, and class-change items (level and from → to pair), stat-up and class-change ones on the user itself. Stat-ups add positive values only (max HP/MP to 60, the rest to 30) | FUN_0040f650's list, FUN_0040b074, FUN_0040b8f0 |
| Spells' targets | an unattackable neutral unit is never affected; target 0 hits everyone in the area (the caster too); target 1 only the other side (Damage/Degrade) or the caster's side (Raise), enemies vs. everyone else | FUN_00411934 |
| Screen display / erase | effect 0 is instant (the most used: ~320 times), 6 is a brightness fade of 20 ticks (0.67 s; to white goes to 200 %), 1–5 are wipes (FUN_00422a00) taking about 0.66 s: 1 random 8×8 blocks (240 a tick, freshly shuffled), 2 one-pixel rows every 20 px, 3 one-pixel columns every 20 px, 4 8-px bands, the 1st/3rd of every 32 px top-down then the 2nd/4th bottom-up (30 × 22 ms), 5 the same with columns (40 × 16 ms). A display wipe starts from a screen filled with the colour. The script waits for all of them | FUN_00422ecc, FUN_004231c0, FUN_00422a00 |
| Pictures | method 0 instant, 1–5 the same wipes; a new picture is drawn over those still shown (one surface, cleared only by erase picture or an event's end); positions inside the 16-px frame | FUN_004233f0, FUN_004237c4 |
| Flash / tone | flashes set the screen tone (R/G/B/vividness in %) for a moment, not a colour overlay: Flash 1 one tick, Flash 2 one tick, a gap, two ticks, Flash 3 eight ticks moving toward the colour then back. Tone change 3 is instant, 4 takes 15 ticks, 5 30 ticks. The script waits | FUN_00423914 |
| Shake | the screen jumps 8 / 24 / 48 px left, up, right, down, 50 ms each (0.2 s), the script waiting | FUN_00423e70, table at 0x4338D8 |
| Music | loading a map stops the music (a camp's music carries on into the opening); the player-turn music starts 20 ticks into PLAYER TURN; the same file isn't restarted; return_to_title plays "". A name starting with `*` goes to Harmony as it is, any other as `BGM\name` (no game uses `*`) | FUN_0041d5e4, FUN_0040e5a8, FUN_0040931c |
| Missing files | music and sounds go through Harmony and a missing one is simply not heard. A missing bitmap — a battle animation sheet (CA / MAGA), a battle or camp background, a picture — stops the game: FUN_004030a8 stops the music, shows an error box naming the file and posts WM_CLOSE. The client shows its own box instead ("Error: Missing Game File" and the file, once per file) and plays on without it; a missing next map goes back to the title. Shipped games that would stop: ＯtoＺ (7 CA sheets), 闇鍋企画 前編 (2) and 完結編 (3 CA sheets, CAMP_048, 地図基礎12.bmp), 昴の騎士 (2), えるまに (CA01_025, class 透明) — each only when that class fights or that map is reached | FUN_004030a8; loads in FUN_0041a5b0 (battle scene), FUN_0041f034 (camp) |
| Map files | the loader reads the header (0x4B0), the tiles (the whole 40×40 grid when header 0x444 is set, else h rows of w), the 100 objects (0x4010) and the script container: the FAT and directory (5000 entries each), then 100 bytes for each FAT entry that isn't −1, in index order, into a zeroed block area. A file's read follows its chain from the directory's first block, capped at its size; nothing checks whether a block was actually in the file. The editor saves the same way (FUN_00402908), which is why the file holds exactly the used blocks | FUN_004048ac, FUN_00402848, FUN_0040289c, FUN_00402468, FUN_0040262c |
| Overlays | two byte grids of the map: +0x208 (reach: 1 move, 2 attack / spell / medicine reach) and +0x848 (area: 3 harmful, 4 support, + 4 where a unit is reached). FUN_00411050 clears both, FUN_00411094 the area. Drawn per tile after its chip and its "Other" object: the area if non-zero, else the reach, from a 384×256 sheet built from SYSTEM.BMP's range blocks (each tiled to 96×96, twice: the upper copy with its even rows cleared, the lower whole); values 1–4 in stripes (odd rows, FUN_00406c38), 5–8 solid (the lower copy) | FUN_004132a8, FUN_00411d74, FUN_00411050, FUN_00411094 |
| Spell aim | FUN_004117d0 marks the reach (the range grid around the caster, or every tile) and, for a spell covering the whole map, its area at once. Each tick FUN_00411a48 marks the area around the pointer's tile (nothing outside the reach) and says whether a unit there is affected; a click casts only then. A spell covering the whole map is computed at (0, 0) whatever the pointer does, and any click casts it | FUN_004117d0, FUN_00411a48, FUN_00411934, FUN_00410050, FUN_0040fda8 |
| Medicine | its reach: the distances of FUN_00411ba0's tables for its range type (+0x268): min 0, 0, 0, 1, 1, 1, 1, 2 / max 0, 1, 2, 1, 2, 3, 4, 9. The tile under the pointer is its area (blue, + 4 on a unit of the user's side that isn't an unattackable neutral); a click gives it there | FUN_00411ba0 (tables 0x43323c / 0x43325c), FUN_00411c90, FUN_0040fda8 |
| Action under way (state 0x12) | entered with the actor's tile cyan and the target(s) marked: on the player's click with counter +0x1a0 = 5 (FUN_00410190 attack, FUN_00410050 spell, FUN_0040fda8 magic item), on the computer's with 0 (FUN_00416e04 attack, FUN_00416e98 spell). Each tick: at 5 the "before" slots of each affected unit of the other side (FUN_0040cd98, one event at a time); at 10 the dimming flag (+0x91, brightness −20 a tick in FUN_0040dee0); the counter goes up while the view isn't scrolling; past 14 the overlays are cleared and the battle scene starts. The fight itself was already rolled before (FUN_0041b4b0 / FUN_0041c8c0 at the click); the scene writes each step's HP, TP and statuses into the records at the step's end, and state 0x1a (FUN_0041093c) adds the EXP, rolls the level-up (FUN_0040a9e4) and adds the rewards (FUN_0040aef0, FUN_0040add8) | FUN_00410388, FUN_00419574, FUN_0041093c |
| Map units | drawn after the tiles: the living units in view sorted by screen y (moving ones + 1000); row from the way (+0xd0: 8 up → row 1, 6 right → 2, 2 down → 3, 4 left → 4, 0 → 0) while moving (+0xd4) or chosen (states 6/7, in the player's phase only the player's unit), frames from {0, 0, 64, 128, 128, 64} by tick/2 moving, tick/3 chosen, tick/4 standing; acted (+8): frame 0 and END (SYSTEM.BMP (64,0) 32×32) on the tile; poisoned (+0xb4) PSN, asleep (+0xb8) SLP (SYSTEM.BMP y 192, x 32·((tick/4) & 3), + 128 for SLP). Event moves set +0xd0 / +0xd4 per step, 8 ticks each (4 px a tick); turn steps (4–7) only set the way | FUN_00413918, FUN_00424a68; tables 0x43328c / 0x4332a4 |
| System sounds | FUN_00409428(code): 0 `.\click.wav`, 1 `.\cancel.wav`, 2 `.\buzzer.wav`, 3 `.\down.wav`; `levelup.wav` from the level-up panel's state (0x1b) and from a class change | FUN_00409428, FUN_00410a88, FUN_0040be00 |
| Lists by the unit | spells: corner (tile x − 80, tile y), width ((WORD 3 bytes + 1)/2 + 17) units, rows 「%-22s」 + WORD 3 + 「%4d」 + 「／」 + 「%2d」, greyed when TP < cost; equipment slots 64 px left, placed as 15 units wide and then widened to 17 (so by the frame's right edge it reaches 24 px into it); items / equipment choices 48 px left, 15 units, 12 rows a page; all kept inside the frame and not above y 96 | FUN_00417548, FUN_004177e8, FUN_00417b08, FUN_00417f54 |
| Death message | drawn into a 640×160 surface for the scene's lower part: the face at (32,16), the balloon (26 × 8 pieces, tail left) at (192,16), 18-px text from (210,28), lines split at CR, at most 5, 22 px apart | FUN_0041d24c |
| Save / load | §14 "Saving and loading (exe)" | FUN_0040be6c, FUN_0040bfbc |

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
- **T13:** recorded as "dialogue plays", a provisional assumption to revisit (§11.2). Later settled from the exe without playing it: the scripts aren't damaged (the map file stores the used blocks packed), and the original plays them whole.

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
- **Heal recording** (グレイ's 活性波 on ジュリア): a cast step, then the landing (§15 "Battle scene: steps", "hit").
- **Spell recordings** (ルーティ's ルーチェ＝フォルツァ on two minions; a second cast): one cast, a landing per target (§15 "Battle scene: steps"); the two drain rates measured there are moved × 37 / max HP ticks (§15 "Battle scene: hit").

### Phase 6, round 1 — instructions (T23: the camp)

A screen recording of the camp before map 3 or 4 (save slot 7 or 8 in `えるまに - Save testplay`) would settle most of the camp layout. Please go through:
- **T23a:** ユニット編成: pick a unit, then each of 武器を替える, 防具を替える, 装飾品を替える, 道具を使う and 技を見る, and back out.
- **T23b:** アイテム購入: buy something cheap. Then アイテム売却: hover a couple of items (to show the sale price) and sell one.
- **T23c:** 次のステージ: everything up to the map starting. If a unit-selection screen appears, toggle someone.
- **T23d:** does a unit that died in the previous map come back at the camp? Say if you remember; no need to test specially.

### Phase 6, round 1 — results

Two recordings: one of the camp before map 3 (ユニット編成, equipment, 技を見る, shop, 次のステージ) and one of buying and selling. Layout in §15. The user couldn't try 道具を使う yet, so the client's version was a guess; it and the 出撃 toggle were later taken from the exe (§15). Units that die come back at the camp.

### Phase 6, round 2 — instructions (T24: the title screen)

The client's title menu is a guess (「はじめませう」 / ロードする / ゲーム終了 in a small panel low in the middle). A screenshot of the original's title screen with its menu, and of what loading from it looks like, would fix it. Also: does any music play on the title?

### Phase 6, round 2 — results

A recording of the title: TITLE.BMP with a menu 108×64 at (266,320): はじめませう / ロードする / ゲーム終了. ロードする replaces it with the ロードします list, 96×177 at (272,281); empty files are grey. Loading darkens the screen in steps, goes black, and starts the saved map. Title music not checked (the recording has no audio analysis).

## Open items with no test planned

- Phase 1 leftovers with no known runtime effect: ANIME +0x020, SMAP 0x448 and 0x44C (3–4 maps each). Best chased in the exe.
- Save header field 0x57C, and unit-record fields [4], [13], [36], [52], [56], [130], [131], [150].
- Event-side details that need the running game:
  - the order of event-slot condition codes 6–8 and 9–11 (§11.1);
  - picture display methods and screen effects beyond the first two.

## Active tests

**T25 — behaviours read from the exe (§16), quick checks in the original.** Everything below comes from the disassembly; a glance in the running game confirms the reading.
1. New game in the original: in 0話's 「オープニングを選択してください」 menu pick ＯＰ１, let it end. Does the menu come back? (Expected: yes; switch events repeat.)
2. 4話: when ハンス and ゴーバック's event has played (EV000101), can you select and move them like your own units? (Expected: yes; "add ally" turns them.)
3. Finish 8話前半: does COMPLETE!! appear before 8話後半's camp? (Expected: no; its map_complete is "lose", a plain transition.)
4. During any player phase, left-click an enemy. (Expected: its movement range shows; right click opens its status.)
5. If you see one, note what a screen fade-in with random squares or stripes looks like, and whether plain cuts happen instantly (expected: effect 0 is a cut, not a fade).

**Tip: quick wins in the test copy.** At a camp (before the next map starts), run:

```bash
python re/tools/wintool.py weaken
```

Every enemy-only unit then has 1 HP and no attack or defense, so one hit beats each one, while all story events still run. Party members, anyone who ever fights on your side, and neutral NPCs keep their real stats. It only edits `えるまに - Save testplay/UNIT.DAT`, keeps the original in `_wintool_backup/`, and refuses to touch the original game. Undo with:

```bash
python re/tools/wintool.py restore
```
