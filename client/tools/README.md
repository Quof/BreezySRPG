# Compatibility tools

For checking that the client plays another SRPG Maker 95 game. They run from PowerShell, and build the client
(Debug) and the scanner on first use. Output goes wherever `-Out` points, so keep it outside the repo. A scratch
folder is fine.

Run these in order for a new game:

```powershell
client\tools\scan.ps1   -Out C:\temp\check "D:\Games\SomeGame"
client\tools\smoke.ps1  -Game "D:\Games\SomeGame" -Out C:\temp\check\smoke
client\tools\events.ps1 -Game "D:\Games\SomeGame" -Out C:\temp\check\events
client\tools\loads.ps1  -Game "D:\Games\SomeGame" -Out C:\temp\check\loads
client\tools\skips.ps1  -Game "D:\Games\SomeGame" -Out C:\temp\check\skips
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

## loads.ps1: every way of loading, every kind of save

This one checks the client rather than the game. It finds the game's first camp save, battle save, and save made
in the middle of an event (read from the save files and the EasySRPG95 notes beside them). It then loads each of
them every way there is:

- `--load`;
- the title and a camp, with the 100-file save screen and with the original's 8-file list;
- a map's turn menu, both screens again;
- the right-click menu during an event.

Each run has to end in that camp, on that map with the player's phase waiting, or back in that event. Every run
works on its own copy of the saves (`-Saves` picks another folder than the game's), so the game's own saves are
never written. Runs that need something the saves don't have are skipped and said so: the turn menu needs a
battle save to start from, the camp a camp save, the right-click menu a save made in an event. The 8-file lists
reach files 1–8 only.

It prints a line per run, `ok` or `FAIL` with what went wrong (`ended in Title, not Camp`, `went to map SMAP_003`,
`the event didn't go on`, a crash), then the counts. A failed run keeps its folder: its saves, screenshot, `.txt`
and `.log`. Run it after any change to saving, loading or the screens between them.

## skips.ps1: the cutscene skip leaves the map as playing it does

This one checks the client too. Each map starts twice. One run plays every event through, confirming messages and
taking the first option at choices. The other skips each event as soon as it shows a message (Space, はい) and
answers its choices the same way. Both go on to the same frame, long after the map waits for the player, and their
states are compared: units (where, shown, HP, level), music, screen, pictures, camera, gold, items, switches and
variables. A skip must not change any of it. A story map that ends in its opening has to go on to the same next map,
too. `-At start` skips each event the moment it runs instead, whatever it's in the middle of (a move, a pan, a fade,
a wait).

It prints `same`, `DIFF` with the lines that differ, `LOOP` for a map whose menu event runs again and again under
first options (a shop, a hiring list), or `OPEN` when even the played run is still in an event at the last frame (a
long or repeating one); those two can't be compared. Run it after any change to the skip, to events or to
what they act on (moves, pans, appearing units, screen effects, music).

## Reading the results

For smoke and events: each run gets one line in `results.json`, and the console shows a summary. Runs that went fine leave no files
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
