# Compatibility tools

For checking that the client plays another SRPG Maker 95 game. They run from PowerShell, and build the client
(Debug) and the scanner on first use. Output goes wherever `-Out` points, so keep it outside the repo. A scratch
folder is fine.

Run these in order for a new game:

```powershell
client\tools\scan.ps1   -Out C:\temp\check "D:\Games\SomeGame"
client\tools\smoke.ps1  -Game "D:\Games\SomeGame" -Out C:\temp\check\smoke
client\tools\events.ps1 -Game "D:\Games\SomeGame" -Out C:\temp\check\events
```

## scan.ps1: read everything, run nothing

This runs the client's own loaders over every file. It writes `<Out>\<game folder>.txt` with:

- **GAME.DAT values that aren't zero.** For example, `[5]` means fallen units are lost and `[4]` slows battle
  animations. FORMAT_NOTES lists them all.
- **Histograms of every feature the game uses.** These cover command options, screen effects, picture positions,
  move patterns, slot conditions and so on. Compare one game's report with another's: a value that only the new
  game uses is a part of the client no other game has exercised yet.
- **Missing files** and what needs them.
- **Issues.** These are files the client can't read, values outside what it knows, start positions or objects
  off the map, and next maps that don't exist.

The scanner can also show single maps:

```powershell
$scan = "client\tools\Srpg95Scan\bin\Release\net8.0\Srpg95Scan.exe"
& $scan dump   "D:\Games\SomeGame" 4 5           # header, start positions, character list, objects
& $scan script "D:\Games\SomeGame" 4 EV000101    # one event script as a tree, plus every object's slots
& $scan script "D:\Games\SomeGame" 4 "*"         # all of the map's scripts
```

Event file names are `EVoooooss`: `oooo` is the object number and `ss` the slot (1–5). `OPENING` is the map's opening.

## smoke.ps1: every map, a few turns each

Each map loads on its own. Its opening plays, with messages confirmed and choices picked at random, then
`-Turns` turns are ended (3 by default). This exercises map loading, openings, turn events and the enemy AI.

## events.ps1: every event script, each by itself

Each event runs on its map in place of the opening. This reaches cutscenes, recruitments, endings and hub
menus without playing up to them.

## Reading the results

Each run gets one line in `results.json`, and the console shows a summary. Runs that went fine leave no files
behind; the others keep a screenshot, `.txt` (the state when it stopped), `.log` (map and camp changes) and
`.error.txt` (a crash).

| Status | Meaning |
|---|---|
| `ok` | It settled: the map was waiting for the player. |
| `CRASH` | A client bug. `.error.txt` has the stack trace. |
| `CAPPED` | It hadn't settled by `-Frames`. The `Why` column gives the reason when it is one of the harmless kinds below. |
| `TIMEOUT` / `NOSHOT` | The process hung or quit without a screenshot. Always worth a look. |

A script run out of its story often can't settle, and that isn't a client bug:

- **next map keeps restarting.** The event ended its map, and the next map's opening sends the player back to deploy
  certain units. These runs start with no army, so it never passes.
- **a switch event fires again and again.** An event whose condition is a switch runs again after every action
  while the switch is on. Some only stop once a story variable is set, which never happens when run alone.
- **Won, then stuck on a dark screen.** The `.log` has `missing MAP/SMAP_nnn.DAT`: the map's victory leads to a
  map that doesn't exist. Check whether any script selects that map (`script ... "*"`, look for `SelectMap`).
  If none does, it's leftover content no player reaches. The original would stop with an error there too.

Anything else that's `CAPPED`, shown with `Why` = `?` and listed after the summary, deserves a look. Open its
`.txt` and `.log` and dump the map's scripts.

What these can't tell you is whether the client looks and behaves *exactly* like the original. For anything new a
game exercises, compare the two by eye.
