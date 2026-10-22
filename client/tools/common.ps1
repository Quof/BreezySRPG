# Shared by smoke.ps1, events.ps1 and loads.ps1: the client (Debug build) and the scanner, built on first use.
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$client = Join-Path $repo "client\src\BreezySRPG95\bin\Debug\net8.0\BreezySRPG95.exe"
$scanner = Join-Path $PSScriptRoot "Srpg95Scan\bin\Release\net8.0\Srpg95Scan.exe"
if (-not (Test-Path $client)) { dotnet build (Join-Path $repo "client\src\BreezySRPG95") -c Debug -v q --nologo | Out-Null }
if (-not (Test-Path $scanner)) { dotnet build (Join-Path $PSScriptRoot "Srpg95Scan") -c Release -v q --nologo | Out-Null }

# Runs jobs (hashtables with Args, Png, Key, and optionally Env: environment variables for that run) through the
# client, $Parallel at a time; returns one result per job. Runs that settle leave no files behind, unless -Keep.
function Invoke-ClientRuns($Jobs, [int]$Parallel, [int]$TimeoutS, [int]$Frames, [switch]$Keep) {
    $queue = [System.Collections.Generic.Queue[object]]::new(); $Jobs | ForEach-Object { $queue.Enqueue($_) }
    $running = @{}; $results = [System.Collections.Generic.List[object]]::new(); $id = 0
    while ($queue.Count -gt 0 -or $running.Count -gt 0) {
        while ($queue.Count -gt 0 -and $running.Count -lt $Parallel) {
            $j = $queue.Dequeue(); $id++
            foreach ($ext in ".png", ".txt", ".log", ".error.txt") { $f = [IO.Path]::ChangeExtension($j.Png, $ext); if (Test-Path $f) { Remove-Item $f } }
            $a = $j.Args + @('--frames', "$Frames", '--screenshot', "`"$($j.Png)`"")
            # a started process copies the environment as it is: set the run's own variables around the start only
            $saved = @{}
            if ($j.Env) { foreach ($e in $j.Env.GetEnumerator()) { $saved[$e.Key] = [Environment]::GetEnvironmentVariable($e.Key); [Environment]::SetEnvironmentVariable($e.Key, $e.Value) } }
            $running[$id] = @{ P = (Start-Process -FilePath $client -ArgumentList $a -PassThru -WindowStyle Minimized); Start = Get-Date; Job = $j }
            foreach ($e in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($e.Key, $e.Value) }
        }
        Start-Sleep -Milliseconds 200
        foreach ($k in @($running.Keys)) {
            $r = $running[$k]; $timedOut = ((Get-Date) - $r.Start).TotalSeconds -gt $TimeoutS
            if (-not ($r.P.HasExited -or $timedOut)) { continue }
            if (-not $r.P.HasExited) { try { $r.P.Kill() } catch {} }
            $png = $r.Job.Png; $txt = [IO.Path]::ChangeExtension($png, ".txt"); $err = [IO.Path]::ChangeExtension($png, ".error.txt"); $log = [IO.Path]::ChangeExtension($png, ".log")
            $state = if (Test-Path $txt) { Get-Content $txt -Encoding utf8 -TotalCount 1 } else { "" }
            $frame = if ($state -match "^frame=(\d+)") { [int]$Matches[1] } else { -1 }
            $e = if (Test-Path $err) { (Get-Content $err -Encoding utf8 | Select-Object -First 3) -join ' | ' } else { "" }
            $status = if ($e) { "CRASH" } elseif ($timedOut) { "TIMEOUT" } elseif (-not (Test-Path $png)) { "NOSHOT" } elseif ($frame -gt $Frames) { "CAPPED" } else { "ok" }
            # why a run never settled, when it's one of the usual reasons a script run out of its story gets stuck
            $why = ""
            if ($status -eq "CAPPED") {
                $maps = @(if (Test-Path $log) { Get-Content $log -Encoding utf8 | Where-Object { $_ -match ': map SMAP' } | ForEach-Object { $_ -replace '^\d+: ', '' } })
                $events = if ($state -match 'events=(\d+)') { [int]$Matches[1] } else { 0 }
                $why = if ($maps.Count -gt 0 -and @($maps | Where-Object { $_ -eq $maps[-1] }).Count -ge 20) { "next map keeps restarting (a deploy check, with no army)" }
                       elseif ($events -ge 50) { "a switch event fires again and again" }
                       else { "?" }
            }
            $results.Add([pscustomobject]@{ Key = $r.Job.Key; Status = $status; Why = $why; Frames = $frame; Secs = [int]((Get-Date) - $r.Start).TotalSeconds; Error = $e; State = $state.Substring(0, [Math]::Min(220, $state.Length)) })
            if ($status -eq "ok" -and -not $Keep) { foreach ($f in $png, $txt, $log) { if (Test-Path $f) { Remove-Item $f } } }
            $running.Remove($k)
        }
    }
    $results
}

function Write-Summary($Results, [string]$Out) {
    $Results | Sort-Object Key | ConvertTo-Json -Depth 3 | Out-File (Join-Path $Out "results.json") -Encoding utf8
    $Results | Group-Object Status, Why | Sort-Object Name | ForEach-Object { "{0,5}  {1}" -f $_.Count, $_.Name }
    $Results | Where-Object { $_.Status -ne "ok" -and $_.Why -notmatch "deploy|switch event" } | Sort-Object Key |
        ForEach-Object { "{0} {1} {2}f {3}s :: {4} :: {5}" -f $_.Key, $_.Status, $_.Frames, $_.Secs, $_.Error, $_.State }
}
