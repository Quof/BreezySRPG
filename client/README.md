# Erumani client

A modern replacement client for えるまに, a game made with Sim RPG Maker 95 (シミュレーションRPGツクール95). It reads the original game folder directly (`SRPGEXEC.EXE`'s data files, BMP graphics, MIDI/WAV audio) and never modifies it. Format documentation lives in [`../re/FORMAT_NOTES.md`](../re/FORMAT_NOTES.md).

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
- `--screenshot FILE.png`: render one frame at 640×480, save it and exit.

Set `SRPG95_REQUIRE_DATA=1` to make the tests fail instead of silently passing when the game data or `re/json` is missing.

Controls (milestone 1):

| Keys | Action |
|---|---|
| Arrows / WASD | scroll |
| PageUp / PageDown | previous / next map |
| F4 or Alt+Enter | fullscreen (F4 was the original's window toggle) |
| F5 | integer scaling on/off |
| F2 | show deleted map objects |
| Tab (hold) | fast-forward |
| Esc | quit |

## Roadmap

1. ✅ **Map rendering:** tilesets, units from class sprites, 640×480 virtual screen scaled to any window.
2. **Text and UI:** font, SYSTEM.BMP window skin, message window (top/centre/bottom), faces (left/right, flip), screen frame graphics.
3. **Script interpreter:** run OPENING and object events. Covers messages, choices, branches, switches and variables, BGM/SE, pictures, screen effects, waits, unit moves and map flow.
4. **Tactics gameplay:** turn flow, movement ranges from terrain/class costs, attacks and magic with range/effect grids, AI movement patterns, event triggers, win/lose conditions, level-ups.
5. **Battle scenes:** ANIME.DAT frame playback over BACK_nnn backgrounds, hit/miss sounds, damage per the help's Battle Algorithm.
6. **Camp and saving:** sortie, equipment, shop, and save/load in the original `SAVEn.DAT` format (pending save-file research, test T16).
7. **Title, game over, map transitions.**
8. **Modern features:** fast-forward (basic version done), rebindable controls with a config file, resolution and scaling options, SoundFont music, and a self-contained `dotnet publish` build.

Faithfulness rule: replicate the original's behaviour, including its bugs (see FORMAT_NOTES §11.4), by default. Any fixes are opt-in.
