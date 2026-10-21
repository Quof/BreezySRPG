# Every way of loading a save, with every kind of save there is: --load; the title and a camp, each with the
# 100-file save screen and with the original's 8 files; a map's turn menu, both screens again; and the right-click
# menu during an event. Each loads the game's first camp save, battle save and save made in the middle of an event
# (those its saves have), and has to end up in that camp, on that map waiting for the player, or in that event.
# Everything runs on copies of the saves: the game's own are never written.
#   loads.ps1 -Game DIR -Out DIR [-Saves DIR] [-Parallel 8] [-TimeoutS 120] [-Frames 4000]
param([Parameter(Mandatory)][string]$Game, [Parameter(Mandatory)][string]$Out, [string]$Saves,
      [int]$Parallel = 8, [int]$TimeoutS = 120, [int]$Frames = 4000)
. "$PSScriptRoot\common.ps1"
if (-not $Saves) { $Saves = $Game }
New-Item -ItemType Directory -Force $Out | Out-Null

# the saves, by kind: a camp save (header 0x000 = 1), a battle save, or one made in an event (a battle save whose
# EasySRPG95 note, for this very file, holds the event); and the map each is on (header 0x004, 0-based)
$notes = $null
$index = Join-Path $Saves "EasySRPG95\saves.json"
if (Test-Path -LiteralPath $index) { $notes = Get-Content -LiteralPath $index -Raw -Encoding utf8 | ConvertFrom-Json }
$found = foreach ($n in 1..100) {
    $f = Join-Path $Saves "SAVE$n.DAT"
    if (-not (Test-Path -LiteralPath $f) -or (Get-Item -LiteralPath $f).Length -ne 114400) { continue }
    $b = [IO.File]::ReadAllBytes($f)
    $note = if ($notes) { $notes.PSObject.Properties["$n"] } else { $null }
    $hash = (Get-FileHash -Algorithm SHA1 -LiteralPath $f).Hash.Substring(0, 20)
    $kind = if ([BitConverter]::ToInt32($b, 0) -eq 1) { "camp" }
            elseif ($note -and $note.Value.Hash -eq $hash -and $note.Value.Scene) { "event" } else { "battle" }
    [pscustomobject]@{ Slot = $n; Kind = $kind; Map = [BitConverter]::ToInt32($b, 4) + 1 }
}
$first = @{}
foreach ($k in "camp", "battle", "event") { $first[$k] = $found | Where-Object Kind -eq $k | Sort-Object Slot | Select-Object -First 1 }
foreach ($k in "camp", "battle", "event") {
    if ($first[$k]) { "{0,-6} save: file {1}, map {2}" -f $k, $first[$k].Slot, $first[$k].Map } else { "{0,-6} save: none (not tested)" -f $k }
}

function Down([int]$n) { if ($n -gt 0) { @("down") * $n } else { @() } }
function Keys([object[]]$k) { (@($k | ForEach-Object { $_ }) | Where-Object { $_ }) -join "," }
# where each way of loading starts (a save to --load first, none for the title) and the keys that load file $n: the
# title's and the camp's Load is their row 1 and row 5, the turn menu's its row 3; the 8-file lists pick
# at once, the 100-file screen opens on the newest file (the one tested) and asks first
$ways = @(
    @{ Name = "direct";       Screen = "original"; From = $null;    Keys = { param($n) Keys @("shot+200") } }
    @{ Name = "title-8";      Screen = "original"; From = $null;    Keys = { param($n) Keys @("down", "enter", (Down ($n - 1)), "enter", "shot+200") } }
    @{ Name = "title-100";    Screen = "extended"; From = $null;    Keys = { param($n) Keys @("down", "enter", "enter", "enter", "shot+200") } }
    @{ Name = "camp-8";       Screen = "original"; From = "camp";   Keys = { param($n) Keys @((Down 5), "enter", (Down ($n - 1)), "enter", "shot+200") } }
    @{ Name = "camp-100";     Screen = "extended"; From = "camp";   Keys = { param($n) Keys @((Down 5), "enter", "enter", "enter", "shot+200") } }
    @{ Name = "turnmenu-8";   Screen = "original"; From = "battle"; Keys = { param($n) Keys @("turnmenu", (Down 3), "enter", (Down ($n - 1)), "enter", "shot+200") } }
    @{ Name = "turnmenu-100"; Screen = "extended"; From = "battle"; Keys = { param($n) Keys @("turnmenu", (Down 3), "enter", "enter", "enter", "shot+200") } }
    @{ Name = "rightclick";   Screen = "extended"; From = "event";  Keys = { param($n) Keys @("rclick", "down", "enter", "enter", "enter", "shot+200") } }
)

$jobs = [System.Collections.Generic.List[object]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()
foreach ($target in @($first.Values | Where-Object { $_ } | Sort-Object Slot)) {
    foreach ($w in $ways) {
        $key = "{0} file {1} ({2})" -f $w.Name, $target.Slot, $target.Kind
        if ($w.From -and -not $first[$w.From]) { $skipped.Add("$key -- no $($w.From) save to start from"); continue }
        if ($w.Screen -eq "original" -and $w.Name -ne "direct" -and $target.Slot -gt 8) { $skipped.Add("$key -- the 8-file list ends at file 8"); continue }
        # the run's own copy of the saves, the file tested the newest (where the 100-file screen opens)
        $dir = Join-Path $Out ("{0}_{1}" -f $w.Name, $target.Slot)
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        New-Item -ItemType Directory -Force $dir | Out-Null
        foreach ($s in $found) {
            $copy = Join-Path $dir "SAVE$($s.Slot).DAT"
            Copy-Item -LiteralPath (Join-Path $Saves "SAVE$($s.Slot).DAT") $copy
            (Get-Item -LiteralPath $copy).LastWriteTime = [datetime]"2000-01-01"
        }
        if (Test-Path -LiteralPath (Join-Path $Saves "EasySRPG95")) { Copy-Item -LiteralPath (Join-Path $Saves "EasySRPG95") $dir -Recurse }
        (Get-Item -LiteralPath (Join-Path $dir "SAVE$($target.Slot).DAT")).LastWriteTime = Get-Date
        $load = if ($w.Name -eq "direct") { $target.Slot } elseif ($w.From) { $first[$w.From].Slot } else { $null }
        $jobs.Add(@{ Key = $key; Png = Join-Path $dir "end.png"; Way = $w.Name; Target = $target; Dir = $dir
            Env = @{ ERUMANI_SAVESCREEN = $w.Screen }
            Args = @('--game', "`"$Game`"", '--saves', "`"$dir`"", '--fast', '1', '--keys', (& $w.Keys $target.Slot)) +
                   $(if ($load) { @('--load', "$load") } else { @() }) })
    }
}
"runs: $($jobs.Count)"
$results = Invoke-ClientRuns $jobs $Parallel $TimeoutS $Frames -Keep

# each run has to end where its file leads: the last map or camp the log names is the file's (after the start, for
# all but --load), the client in that camp, on that map with the player's phase waiting, or in the saved event
$byKey = @{}; foreach ($j in $jobs) { $byKey[$j.Key] = $j }
$verdicts = foreach ($r in $results) {
    $j = $byKey[$r.Key]; $t = $j.Target
    $log = @(if (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($j.Png, ".log"))) { Get-Content -LiteralPath ([IO.Path]::ChangeExtension($j.Png, ".log")) -Encoding utf8 })
    $arrivals = @($log | Where-Object { $_ -match '^(\d+): (camp before SMAP_|map SMAP_)(\d+)' } | ForEach-Object {
        $null = $_ -match '^(\d+): (camp before SMAP_|map SMAP_)(\d+)'
        [pscustomobject]@{ Frame = [int]$Matches[1]; Camp = $Matches[2] -like "camp*"; Map = [int]$Matches[3] } })
    $last = if ($arrivals.Count) { $arrivals[-1] } else { $null }
    $resumed = @($log | Where-Object { $_ -match "^(\d+): resumed in event" -and $last -and [int]$Matches[1] -eq $last.Frame }).Count -gt 0
    $state = if ($r.State -match '^frame=\d+ script-left=\d+ (\S+)') { $Matches[1] } else { "?" }
    $want = switch ($t.Kind) { "camp" { "Camp" } "battle" { "Idle" } default { "Events" } }
    $why = if ($r.Status -ne "ok") { "$($r.Status) $($r.Error)" }
           elseif (-not $last) { "nothing was loaded" }
           elseif ($j.Way -ne "direct" -and $last.Frame -eq 0) { "the file wasn't loaded (only the start)" }
           elseif ($last.Map -ne $t.Map -or $last.Camp -ne ($t.Kind -eq "camp")) { "went to {0} SMAP_{1:000}" -f $(if ($last.Camp) { "the camp before" } else { "map" }), $last.Map }
           elseif ($state -ne $want) { "ended in $state, not $want" }
           elseif ($t.Kind -eq "event" -and -not $resumed) { "the event didn't go on" }
           else { "" }
    if (-not $why) { Remove-Item -LiteralPath $j.Dir -Recurse -Force }
    [pscustomobject]@{ Key = $r.Key; Result = $(if ($why) { "FAIL" } else { "ok" }); Why = $why }
}
$verdicts | Sort-Object Key | ConvertTo-Json -Depth 3 | Out-File (Join-Path $Out "results.json") -Encoding utf8
$verdicts | Sort-Object Key | ForEach-Object { "{0,-4} {1}{2}" -f $_.Result, $_.Key, $(if ($_.Why) { " :: " + $_.Why } else { "" }) }
$skipped | ForEach-Object { "skip $_" }
"{0} ok, {1} failed, {2} skipped" -f @($verdicts | Where-Object Result -eq "ok").Count, @($verdicts | Where-Object Result -eq "FAIL").Count, $skipped.Count
