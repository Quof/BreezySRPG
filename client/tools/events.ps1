# Every event script of a game run by itself: its map loads with the event in place of the opening (--event),
# messages and choices answered automatically (choices at random, seeded). Reports crashes and runs that never settle,
# and says why for the usual harmless reasons (see README.md).
#   events.ps1 -Game DIR -Out DIR [-Maps 1,2] [-Parallel 12] [-TimeoutS 120] [-Frames 20000]
param([Parameter(Mandatory)][string]$Game, [Parameter(Mandatory)][string]$Out, [int[]]$Maps,
      [int]$Parallel = 12, [int]$TimeoutS = 120, [int]$Frames = 20000)
. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force (Join-Path $Out "saves") | Out-Null
if (-not $Maps) { $Maps = Get-ChildItem -LiteralPath (Join-Path $Game "MAP") -Filter "SMAP_*.DAT" | ForEach-Object { [int]$_.BaseName.Substring(5) } | Sort-Object }
$jobs = foreach ($m in $Maps) {
    foreach ($line in (& $scanner script $Game $m "*" 2>$null)) {
        if ($line -match "^== SMAP_\d+ (\S+)$" -and $Matches[1] -ne "OPENING") {
            $ev = $Matches[1]
            @{ Key = "SMAP_{0:000} {1}" -f $m, $ev; Png = Join-Path $Out ("m{0:000}_{1}.png" -f $m, $ev)
               Args = @('--game', "`"$Game`"", '--map', "$m", '--event', $ev, '--advance', '-1', '--fast', '1', '--choose', 'random',
                        '--saves', "`"$(Join-Path $Out 'saves')`"") }
        }
    }
}
"events: $($jobs.Count)"
Write-Summary (Invoke-ClientRuns $jobs $Parallel $TimeoutS $Frames) $Out
