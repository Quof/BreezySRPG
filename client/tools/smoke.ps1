# Every map of a game through the client: its opening, then N turns ended (endturn), several maps at once.
#   smoke.ps1 -Game DIR -Out DIR [-Turns 3] [-Maps 1,2,3] [-Parallel 8] [-TimeoutS 150] [-Frames 30000]
param([Parameter(Mandatory)][string]$Game, [Parameter(Mandatory)][string]$Out, [int]$Turns = 3, [int[]]$Maps,
      [int]$Parallel = 8, [int]$TimeoutS = 150, [int]$Frames = 30000)
. "$PSScriptRoot\common.ps1"
New-Item -ItemType Directory -Force (Join-Path $Out "saves") | Out-Null
if (-not $Maps) { $Maps = Get-ChildItem -LiteralPath (Join-Path $Game "MAP") -Filter "SMAP_*.DAT" | ForEach-Object { [int]$_.BaseName.Substring(5) } | Sort-Object }
$keys = (@("endturn") * $Turns) -join ","
$jobs = foreach ($m in $Maps) {
    @{ Key = "SMAP_{0:000}" -f $m; Png = Join-Path $Out ("m{0:000}.png" -f $m)
       Args = @('--game', "`"$Game`"", '--map', "$m", '--advance', '-1', '--fast', '1', '--choose', 'random',
                '--saves', "`"$(Join-Path $Out 'saves')`"") + $(if ($Turns -gt 0) { @('--keys', $keys) } else { @() }) }
}
"maps: $($jobs.Count)"
Write-Summary (Invoke-ClientRuns $jobs $Parallel $TimeoutS $Frames) $Out
