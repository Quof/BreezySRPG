# EasySRPG95

A modern replacement client for games made with Sim RPG Maker 95 (シミュレーションRPGツクール95), started for えるまに and checked against ＯtoＺ, 昴の騎士 and 闇鍋企画 as well. It reads the original game folder directly (`SRPGEXEC.EXE`'s data files, BMP graphics, MIDI/WAV audio) and never modifies it. Format documentation lives in [`../re/FORMAT_NOTES.md`](../re/FORMAT_NOTES.md).

## Stack

| Piece | Choice | Why |
|---|---|---|
| Language/runtime | C# / .NET 8 | Ships as a self-contained Windows build (no runtime install for players). Fast, with good tooling and first-class Shift-JIS support via `System.Text.Encoding.CodePages` |
| Engine | MonoGame 3.8 (DesktopGL) | A code-first 2D framework. The game is fully data-driven from the original files, so a scene editor (Godot/Unity) would add weight without helping. Gives pixel-perfect scaling and borderless fullscreen |
| Music (planned) | MeltySynth + a General MIDI SoundFont | Pure-C# SoundFont synthesizer: consistent, good-sounding MIDI on every PC instead of the Windows GS wavetable, with user-replaceable SoundFonts |
| Text (planned) | FontStashSharp | Runtime TTF rendering for Japanese. Uses the system's MS Gothic (the original's font) when available, else a bundled OFL font |

Alternatives considered:
- **Godot 4:** no native MIDI, and its scene editor isn't needed.
- **Python/pygame:** distribution and performance.
- **Rust:** slower iteration for little gain here.

## Layout

```
client/
  src/Srpg95.Data/          format loaders (C# port of re/srpg95_data.py)
  src/Erumani/              the game (MonoGame)
  tests/Srpg95.Data.Tests/  cross-checks every loader against the Python reference JSON
```

## Build and run

Needs the .NET 8 SDK.

```bash
dotnet build client/Erumani.sln
```

```bash
dotnet run --project client/src/Erumani -- --map 1
```

```bash
dotnet test client/tests/Srpg95.Data.Tests
```

Options:
- `--game DIR`: the game folder. By default it searches next to and above the executable for a folder containing `SRPGEXEC.EXE`.
- `--rtp DIR`: the RTP folder used as a fallback for graphics and audio. Default `srpg maker 95/RTP`.
- `--map N`: the map to open (1-based, as in `SMAP_nnn`).
- `--screenshot FILE.png`: render one frame at 640×480, save it and exit. It also writes `FILE.txt` with the battle state (units, HP, EXP) and a trace of the scripted input. In this mode the real keyboard and mouse are ignored and the battle RNG is seeded, so runs are reproducible.
- `--scale N`: used with `--screenshot`, renders and saves the frame at N × 640×480, as a window N times the size draws it.
- `--fast 1`: used with `--screenshot`, runs frames as fast as the machine allows instead of 60 a second. The game advances by frames, so the outcome is the same, only sooner.
- `--frames N`: used with `--screenshot`, captures at frame N at the latest (default 30000), so a run that never settles still ends. A scripted run also appends map, camp, title and game-over lines to `FILE.log` as they happen, and a crash leaves `FILE.error.txt`.
- `--choose "a,b,..."`: used with `--advance`, the option each character choice takes in turn (0-based, or `last`); `random` picks every choice from then on at random (seeded, so a run repeats).
- `--advance N`: used with `--screenshot`, auto-confirms N event messages or choices before capturing. `-1` confirms all of them.
- `--keys "a,b,..."`: used with `--screenshot`, scripted input fed one token every 10 frames while the map is idle:
  - `up`, `down`, `left`, `right`, `enter`, `esc`;
  - `at:X.Y` puts the cursor on tile (X,Y);
  - `wait` skips a beat;
  - `shot+N` captures N frames later, even mid-animation.
  - `telop+N` / `telopK+N` captures N frames after the next caption (any, or `TELOPK.BMP`: 1 PLAYER TURN, 2 ENEMY TURN, 3 COMPLETE!!) appears.
  - `choice+N` / `choiceK+N` captures N frames after the next (the K-th) character choice comes up, leaving it unanswered.
  - `endturn` ends the player's turn, as ターン終了 does.

  Example: select ジュナ on map 1 and move her 3 tiles up:

  ```bash
  dotnet run --project client/src/Erumani -- --map 1 --advance -1 --keys "at:7.12,enter,at:7.9,enter" --screenshot shot.png
  ```
- `--event NAME`: run this script on load instead of the first map's `OPENING`, e.g. `--map 4 --event EV000101` (object 1's first event slot). With `--advance -1 --choose random --fast 1 --screenshot` this runs any event of any map by itself.
- `--load N`: load `SAVEn.DAT` at start (a camp save opens its camp).
- `--saves DIR`: where `SAVEn.DAT` files are read and written. The default is the game folder, as in the original; point it elsewhere to keep the game folder untouched.
- `--camp N` (developer): open the camp before map N.
- `--level N` (developer): allies start the map at level N or higher, e.g. `--level 12` gives ルーティ her spells on map 1.
- `--place "UNIT:X.Y[.HP],..."` (developer): move a unit (UNIT.DAT id) to a tile, keep it shown, and optionally set its HP. For example, `--map 2 --place 245:15.13,247:16.13.9` puts グレイ next to a hurt ジュリア.
- `--give "ID:N,..."` (developer): add N of item ID (0-based ITEM.DAT index) to the inventory, e.g. `--give 40:2` for two ポーション.

Set `SRPG95_REQUIRE_DATA=1` to make the tests fail instead of silently passing when the game data or `re/json` is missing.

## Playing

Put `EasySRPG95.exe` (one file, from a published build) in the game folder, next to `SRPGEXEC.EXE`, and run it; or run it from anywhere with `--game "<game folder>"`. No .NET install is needed: the runtime, SDL2 and OpenAL are inside the exe (the two native libraries are unpacked to `%TEMP%\.net\EasySRPG95` on the first start). Nothing is written next to the exe: settings live in `%APPDATA%\EasySRPG95\settings.json`, shared by every game, and saves go where the original keeps them (the game folder). A `settings.json` placed beside the exe is used instead, for a setup that keeps its own.

Music plays through **GeneralUser GS** (v2.0.3, by S. Christian Collins, [schristiancollins.com](http://www.schristiancollins.com)), a GS SoundFont embedded in the exe, so everyone hears the same soundtrack; its licence ([`src/Erumani/soundfont/GeneralUser-GS-LICENSE.txt`](src/Erumani/soundfont/GeneralUser-GS-LICENSE.txt)) allows bundling it. Under Options you can switch to another `.sf2` put in a `soundfonts` folder next to the exe or in the game folder, or to Windows MIDI as in the original. The first build downloads the SoundFont from the author's repository (hash-checked); it isn't kept in git.

```bash
dotnet publish client/src/Erumani -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o client/publish
```

The project keeps its working name (`src/Erumani`, namespace `Erumani`); only the program is called EasySRPG95.

## Controls

Default keys (SRPG Studio style); all can be rebound under Options → Controls.

| Keys | Action |
|---|---|
| Enter / Z, left click | confirm. On a unit: select it, or show its status if it can't act. On an empty tile: the turn menu |
| Esc / X / Backspace, right click | cancel or back (at the command menu: undo the move). During a battle scene: skip it |
| Arrows | move the map cursor (hold to repeat); move in menus |
| W A S D | scroll the map camera; W/S also move in menus |
| C / Left Shift, right click on a unit | unit status |
| Tab / Left Ctrl | fast-forward (hold, or toggle; see Options) |
| Mouse wheel up / L | text backlog (wheel down or click to close) |
| Space | during an event: skip the rest of it (asks first) |
| R | enemy ranges: every enemy's reach (moves and attacks, spells included) as one red area; press again to hide |
| F1 | options (anywhere) |
| F4 or Alt+Enter | fullscreen |
| F5 | switch pixel-perfect / fit scaling |
| F3 | debug overlay (map, BGM, event log) |
| PageUp / PageDown | previous / next map (developer) |

## Options (modern features)

| Section | Settings |
|---|---|
| Display | interface style (original / modern / gilded), window size (1×–4×), fullscreen (borderless), scaling (pixel-perfect whole multiples / fit 4:3 / stretch), filter (sharp / smooth) |

The frame is drawn at the window's resolution: the pixel art is scaled up whole, and the modern and gilded styles' text and gold are drawn at the window's own pixels, so they stay crisp at any size. The filter only matters when the window isn't a whole multiple of 640×480.
| Audio | music and sound volume, MIDI playback (SoundFont via MeltySynth, or Windows MIDI like the original), SoundFont file, text advance sound (the CLICK "thunk" when advancing text) |
| Gameplay | battle animations on/off, fast-forward key (hold / toggle), speed (×2–×8), fast-forward skips battle scenes, fast-forward advances text |
| Controls | rebind every action; reset to defaults |

## Roadmap

1. ✅ **Map rendering:** tilesets, units from class sprites, 640×480 virtual screen scaled to any window.
2. 🟡 **Text and UI:** done:
   - Japanese text (MS Gothic, falling back to Yu Gothic or Meiryo);
   - message window at top/centre/bottom with faces (left/right, flip);
   - choice menu.

   Still provisional: the look (window style, text speed). That needs reference screenshots of the original (T17) and the SYSTEM.BMP skin.
3. 🟡 **Script interpreter:** done: every map's OPENING runs on load, covering every command the game uses:
   - messages, choices, branches, switches, variables, items, gold;
   - pictures, fades, flash/tint, shake, waits;
   - unit moves, camera scroll;
   - BGM (MCI MIDI, looping) and sound effects;
   - map flow.

   Object events triggered during battle came with milestone 4.
4. 🟡 **Tactics gameplay:** done:
   - player turn: cursor, unit selection, movement range from class terrain costs, walking, command menu (攻撃 / 行動終了), move undo, turn menu (ターン終了), status window;
   - attacks: target choice, hit/crit/damage, counter-attacks, double attacks, EXP and level-ups (help formulas);
   - enemy phase: AI move patterns (rush, wait, go to a point);
   - event triggers: turn, switch, contact, death, pass/stop on a tile, before/after attack, victory;
   - PLAYER TURN / ENEMY TURN / COMPLETE!! captions with the victory fanfare, GAMEOVER screen;
   - the camera follows each enemy;
   - win/lose, then on to the next map.

   - magic (必殺技): spells learned by class level, range/effect grids, damage/degrade/raise effects, MP; enemies cast damage and healing spells;
   - items (アイテム): medicine and magic items from the shared inventory.

   Still to do: the other turn-menu entries, and checking the provisional rules against the original (FORMAT_NOTES §15, test T21).
5. 🟡 **Battle scenes:** done in a first version: ANIME.DAT playback from the CA/MAGA sprite strips over BACK_nnn backgrounds, counters mirrored, hit/miss sounds, damage digits, HP panels, battle music. Sides, timing, transitions, panels, hit flash, damage numbers and death messages are matched to a recording of the original (T22). Spell scenes (heals and attack spells, one cast then each target in turn), misses and HP drain are matched too.
6. 🟡 **Camp and saving:** done in a first version:
   - camp menu, army screen (equipment, items, skills), shop (buy/sell) and sortie;
   - save and load in the original `SAVEn.DAT` format. Re-saving one of the original's camp saves reproduces it byte for byte, and its saves load in the client.

   - battle saves from the turn menu (セーブする / ロードする): units, phase and fired events. Re-saving the original's SAVE3 differs only in a few leftovers from earlier play.

   Still to do: checking 道具を使う against the original (the other camp screens are matched to recordings, T23).
7. 🟡 **Title, game over, map transitions:** done:
   - title screen (TITLE.BMP; new game from GAME.DAT's start map, load, quit);
   - win → COMPLETE!! → camp → next map;
   - game over (GAMEOVER.BMP) → title.

   The title menu is matched to a recording (T24).
8. ✅ **Modern features:** done:
   - options menu (title and F1), rebindable keyboard controls, display options;
   - SoundFont MIDI, volumes, text-advance sound toggle;
   - battle animation skip;
   - fast-forward options;
   - mouse-wheel text backlog;
   - interface styles: the original's, a modern dark one, and a gilded one (thin gold borders, name boxes over the text box, a backlog laid out by speaker);
   - enemy ranges at a key press;
   - drawing at the window's resolution, with crisp text;
   - self-contained build.

Faithfulness rule: replicate the original's behaviour, including its bugs (see FORMAT_NOTES §11.4), by default. Any fixes are opt-in.
