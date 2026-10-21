# The cutscene skip, against playing the cutscene through: each map is started twice, once with every event played
# (messages confirmed, choices taking their first option) and once with each event skipped as soon as it shows a
# message (Space, はい), choices still answered the same. Both runs go on to the same frame, long after the map waits
# for the player; then their states are compared — units (where, shown, HP, levels), music, screen, pictures, camera,
# gold, items, switches and variables. A skip has to leave the map exactly as the played cutscene does.
# -At start skips each event the moment it runs instead, whatever it is doing (a move, a pan, a fade, a wait).
# -Choose last answers every choice with its last option (menus whose first option never leaves them usually end on
# 戻る); anything else --choose takes works too.
#   skips.ps1 -Game DIR -Out DIR [-Maps 1,2,5] [-At message|start] [-Choose last] [-Parallel 8] [-TimeoutS 240] [-Frames 9000] [-Skips 8]
param([Parameter(Mandatory)][string]$Game, [Parameter(Mandatory)][string]$Out, [int[]]$Maps, [ValidateSet("message", "start")][string]$At = "message",
      [string]$Choose, [int]$Parallel = 8, [int]$TimeoutS = 240, [int]$Frames = 9000, [int]$Skips = 8)
. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
if (-not $Maps) {
    $Maps = @(Get-ChildItem -LiteralPath $Game -Recurse -File | Where-Object Name -match '^SMAP_(\d{3})\.DAT$' |
        ForEach-Object { [int]($_.Name.Substring(5, 3)) } | Sort-Object -Unique)
}

# a token that never fires keeps the played run going to the same frame as the skipped one (its spare skips wait too)
$played = "skipyes"
$skipped = (@($(if ($At -eq "start") { "skipany" } else { "skip" }), "skipyes") * $Skips) -join ","
# --choose answers the choices in turn and the first option once its list runs out: "last" goes on for every choice
$choices = if ($Choose -eq "last") { (@("last") * 400) -join "," } else { $Choose }
$jobs = foreach ($n in $Maps) {
    foreach ($way in "played", "skipped") {
        $dir = Join-Path $Out ("{0:000}" -f $n)
        New-Item -ItemType Directory -Force $dir | Out-Null
        @{ Key = "{0:000} {1}" -f $n, $way; Png = Join-Path $dir "$way.png"; Map = $n; Way = $way
           Args = @('--game', "`"$Game`"", '--saves', "`"$dir`"", '--map', "$n", '--advance', '-1', '--fast', '1',
                    '--keys', $(if ($way -eq "played") { $played } else { $skipped })) + $(if ($Choose) { @('--choose', $choices) } else { @() }) }
    }
}
"maps: $($Maps.Count), runs: $(@($jobs).Count)"
$results = Invoke-ClientRuns $jobs $Parallel $TimeoutS $Frames -Keep

# the state, without what only says how the run got there: on a map the first line (frame, script, the runner's own
# counters), elsewhere (a camp, the title, game over) only its frame; the trace lines ("123: ...") and the units' step
# counters
function State([string]$png) {
    $txt = [IO.Path]::ChangeExtension($png, ".txt")
    if (-not (Test-Path -LiteralPath $txt)) { return ,@() }
    $lines = @(Get-Content -LiteralPath $txt -Encoding utf8)
    $first = if ($lines.Count -gt 0 -and $lines[0] -notmatch ' phase=\d' -and $lines[0] -match '^frame=\d+ script-left=\d+ (.*)$') { @($Matches[1]) } else { @() }
    ,@($first + @($lines | Select-Object -Skip 1 | Where-Object { $_ -notmatch '^\d+: ' } | ForEach-Object { $_ -replace ' run=\d+/\d+/\d+', '' }))
}
function Stood($r) { if ($r.State -match '^frame=\d+ script-left=\d+ (\S+)') { $Matches[1] } else { "?" } }
$byKey = @{}; foreach ($r in $results) { $byKey[$r.Key] = $r }
$verdicts = foreach ($n in $Maps) {
    $dir = Join-Path $Out ("{0:000}" -f $n)
    $p = $byKey["{0:000} played" -f $n]; $s = $byKey["{0:000} skipped" -f $n]
    $why = ""
    foreach ($r in $p, $s) { if ($r.Status -notin "ok", "CAPPED") { $why = "$($r.Key): $($r.Status) $($r.Error)" } }
    if (-not $why) {
        $a = State (Join-Path $dir "played.png"); $b = State (Join-Path $dir "skipped.png")
        if ($a.Count -eq 0 -or $b.Count -eq 0) { $why = "no state" }
        else {
            $d = Compare-Object $a $b
            if ($d) { $why = (@($d | ForEach-Object { "{0} {1}" -f $(if ($_.SideIndicator -eq '<=') { "played: " } else { "skipped:" }), $_.InputObject }) -join "`n      ") }
        }
    }
    # the first line of each run says where it stood
    $where = "played [{0}] / skipped [{1}]" -f ($p.State -replace '^frame=\d+ script-left=\d+ (\S+ phase=\d+).*', '$1'), ($s.State -replace '^frame=\d+ script-left=\d+ (\S+ phase=\d+).*', '$1')
    if (-not $why -and $p.State -match '^frame=\d+ script-left=\d+ (\S+ phase=\d+)' -and $s.State -match '^frame=\d+ script-left=\d+ (\S+ phase=\d+)') {
        $pa = ($p.State -split ' ')[2..3] -join ' '; $sa = ($s.State -split ' ')[2..3] -join ' '
        if ($pa -ne $sa) { $why = "ended apart: $where" }
    }
    # a menu event that first options never leave (a shop, a hiring list) runs again and again in both: no end to compare
    $events = @($p, $s | ForEach-Object { if ($_.State -match 'events=(\d+)') { [int]$Matches[1] } else { 0 } })
    $result = if (-not $why) { "same" } elseif (($events | Measure-Object -Maximum).Maximum -ge 50) { "LOOP" } elseif ((Stood $p) -eq "Events") { "OPEN" } else { "DIFF" }
    if ($result -eq "LOOP") { $why = "an event runs again and again ($($events -join ' / ') events): not compared" }
    # the played run still in an event at the last frame (a long or repeating one): nothing settled to compare yet
    if ($result -eq "OPEN") { $why = "the played run is still in an event at frame $Frames ($(Stood $s) when skipped): not compared" }
    [pscustomobject]@{ Map = $n; Result = $result; Why = $why }
}
$verdicts | ConvertTo-Json -Depth 3 | Out-File (Join-Path $Out "results.json") -Encoding utf8
$verdicts | ForEach-Object { "{0,-4} SMAP_{1:000}{2}" -f $_.Result, $_.Map, $(if ($_.Why) { "`n      " + $_.Why } else { "" }) }
"{0} the same, {1} differ, {2} loop, {3} open" -f @($verdicts | Where-Object Result -eq "same").Count, @($verdicts | Where-Object Result -eq "DIFF").Count,
    @($verdicts | Where-Object Result -eq "LOOP").Count, @($verdicts | Where-Object Result -eq "OPEN").Count
