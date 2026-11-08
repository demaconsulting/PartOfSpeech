#requires -Version 7
<#
Context-feature experiment: resumable sequential driver (pre-registered; frozen with the RUN-LOG, hash recorded there).

usage:
    pwsh run_arms.ps1 -Out <folder on C:> [-Arms BASE,F1,...] [-Seeds 20260101,11,...] [-NoBuild] [-DryRun]

What it does, for every arm of arms.json (in file order, BASE first) and every seed (in the order of arms.json), one process at a
time, each started at BelowNormal priority with DOTNET_PROCESSOR_COUNT capped at 8:

  1. stops with exit code 3 when drive C: has less than 6 GB free (checked before every run);
  2. skips the run when <Out>\<arm>-s<seed>.json exists (that file is the atomic completion marker, so re-running the same
     command after any interruption resumes where it stopped; a run without its JSON is redone from scratch);
  3. train:     dotnet <trainer.dll> train --seed S --features <csv|none> [--preview-folds K] --calibrate --output <arm>-s<seed>.bin
     stdout -> <arm>-s<seed>.train.txt, stderr -> <arm>-s<seed>.train.err.txt;
  4. eval-arm:  dotnet <trainer.dll> eval-arm --weights <arm>-s<seed>.bin --label <arm>-s<seed> --json <arm>-s<seed>.json.eval
  5. adds the driver fields to that JSON (below), checks that the result parses, and renames it to <arm>-s<seed>.json;
  6. appends one line to <Out>\progress.log.
A failing command stops the driver (exit 1) and deletes nothing of other runs. Only paths on C: are used (the repository, -Out and
the Python cache); no network or UNC path is touched and no drive is enumerated. The trainer is built once in Release first
(-NoBuild skips that); the repository commit and a dirty-tree flag are recorded in every result.

Python is not run by this script, but PYTHONPYCACHEPREFIX and PYTHONDONTWRITEBYTECODE=1 are exported (the default -PyCache lies in
the session folder) so that nothing in a child process writes a __pycache__ into the repository.

INTERFACE CONTRACT with the trainer (implemented in the feature stage; this script is written against it)

  train options:   --seed <int>                  run seed (shuffle); every other hyperparameter is the shipped training.json
                   --features <csv|none>         feature groups: positionCues (F1), clauseCues (F2), rightPreview (F3),
                                                 inflection (F4); `none` = BASE (must be byte-identical to the shipped model)
                   --preview-folds <int>         jackknife folds of rightPreview (by document hash); passed only with that group
                   --calibrate --output <file>   refit the temperature on open dev as usual, write the weights file
       stdout:     a line `Trained <N> steps in <X> s` (the driver parses X as the trainer's own training seconds)
  eval-arm options: --weights <file> --label <text> --json <file>
       dev-only: reads the shapes development set (dev.tsv, hard.tsv), open dev, gold v2 dev and the UD-EWT dev file only; no
       held-out file; writes only the JSON file; exit 0 on success.

  Per-run result <arm>-s<seed>.json (schema 1). Accuracies are PERCENT (0-100); "per1k" rates are per 1,000 noun-verb
  decisions; harmful = a swapped noun/verb class at confidence >= 0.45; every number is a JSON number.
    written by eval-arm:
      schema 1, label "<arm>-s<seed>",
      groups [group names read from the weights header], temperature <number>,
      config {features [..], previewFolds, epochs, hashBits, quantScale, prune},
      weights {sha256, bytesDeflated, bytesRaw, rows, weights},
      throughput {tokensPerSecond (median of 5 timed passes after 1 warm-up, default tagger, single thread, open dev + UD dev
                  tokens), tokens, passes},
      openDev  {tokens, accuracy, ambiguousAccuracy, nounVerbDecisions, harmful, harmfulPer1k, nll, ece},
      goldDev  {tokens, accuracy, nounVerbDecisions, harmful, harmfulPer1k, nll, ece},
      udDev    {tokens, accuracy, decisions, harmful, harmfulPer1k},
      shapes   {standard, hard}; each is {shapes {"<shape id>": S, ...}, pooled: S} with
                  S = {targetItems, targetCorrect, targetAccuracy, decisions, harmful, harmfulPer1k,
                       controlItems, controlCorrect, controlAccuracy}
                  and the hard tranche has the shapes n-compound-head, n-subject-initial, v-imperative-initial, v-after-modal,
      items    {hard: {"<item id>": {d (1 = noun-verb decision), c (1 = correct), h (1 = harmful)}}, standard: {same}},
      inputs   {devSha256 (dev.tsv), hardSha256 (hard.tsv), udDevSha256, openDevSha256, goldDevSha256, commit, dirty}
    added by this driver:
      arm, seed, features [..], trainSeconds (parsed from stdout, else null), trainWallSeconds (wall clock of the train process,
      includes the jackknife and the calibration), evalWallSeconds, driver {commit, dirty, utc, processorCount}
  analyze_arms.py reads: shapes.{standard,hard}.pooled.{harmfulPer1k,targetAccuracy,controlAccuracy},
  shapes.hard.shapes.<shape>.{targetAccuracy,harmfulPer1k}, openDev.*, goldDev.*, udDev.{accuracy,harmfulPer1k},
  temperature, weights.{rows,bytesRaw,bytesDeflated}, throughput.tokensPerSecond, trainWallSeconds, items.hard (optional),
  inputs.*Sha256 (every run must agree) and the file names.

Exit codes: 0 all requested runs complete, 1 a command failed, 2 usage or path error, 3 less than 6 GB free on C:.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Out,
    [string[]]$Arms,
    [string[]]$Seeds,
    [string]$Repo,
    [string]$Runner = "dotnet",
    [string]$Dll,
    [int]$Threads = 8,
    [double]$MinFreeGB = 6,
    [string]$PyCache = "C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\context-features\pycache",
    [switch]$NoBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Stop-Driver([int]$Code, [string]$Message) {
    [Console]::Error.WriteLine("run_arms: $Message")
    exit $Code
}

function Assert-LocalC([string]$Path, [string]$What) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^[Cc]:\\') { Stop-Driver 2 "$What must be on drive C: (got $full)" }
    return $full
}

function Get-FreeGB { return ([System.IO.DriveInfo]::new("C")).AvailableFreeSpace / 1GB }

function Split-List([string[]]$Items) {
    return @($Items | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Repo) { $Repo = Join-Path $here "..\..\.." }
$Repo = Assert-LocalC $Repo "-Repo"
$Out = Assert-LocalC $Out "-Out"
$PyCache = Assert-LocalC $PyCache "-PyCache"
if (-not $Dll) { $Dll = Join-Path $Repo "tools\DemaConsulting.PartOfSpeech.Trainer\bin\Release\net10.0\DemaConsulting.PartOfSpeech.Trainer.dll" }
if ($Threads -lt 1) { $Threads = 1 }
$processors = [Math]::Min($Threads, 8)

$spec = Get-Content -Raw (Join-Path $here "arms.json") | ConvertFrom-Json
$armList = @($spec.arms)
if ($Arms) {
    $wanted = Split-List $Arms
    foreach ($w in $wanted) { if (-not ($armList | Where-Object { $_.id -eq $w })) { Stop-Driver 2 "unknown arm $w" } }
    $armList = @($armList | Where-Object { $wanted -contains $_.id })
}
$seedList = if ($Seeds) { Split-List $Seeds } else { @($spec.seeds | ForEach-Object { "$_" }) }

if ((Get-FreeGB) -lt $MinFreeGB) { Stop-Driver 3 ("only {0:N1} GB free on C: (need {1})" -f (Get-FreeGB), $MinFreeGB) }

if (-not $DryRun) {
    New-Item -ItemType Directory -Force $Out | Out-Null
    New-Item -ItemType Directory -Force $PyCache | Out-Null
}
$env:PYTHONPYCACHEPREFIX = $PyCache
$env:PYTHONDONTWRITEBYTECODE = "1"
$env:DOTNET_PROCESSOR_COUNT = "$processors"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

function Invoke-Logged([string[]]$ArgList, [string]$StdOut, [string]$StdErr) {
    $quoted = ($ArgList | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join " "
    if ($DryRun) {
        Write-Host "DRY RUN: $Runner $quoted"
        return [pscustomobject]@{ ExitCode = 0; Seconds = 0.0 }
    }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Runner -ArgumentList $quoted -WorkingDirectory $Repo -NoNewWindow -PassThru `
        -RedirectStandardOutput $StdOut -RedirectStandardError $StdErr
    try { $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal } catch { Write-Warning "could not lower the priority: $_" }
    $null = $p.Handle   # keeps the handle so that ExitCode is available after the exit
    $p.WaitForExit()
    $watch.Stop()
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Seconds = $watch.Elapsed.TotalSeconds }
}

Push-Location $Repo
try {
    $commit = (git rev-parse HEAD).Trim()
    $dirty = [bool](git status --porcelain --untracked-files=no)
} finally { Pop-Location }
if ($dirty) { Write-Warning "the working tree has uncommitted changes (recorded as dirty in every result)" }

if (-not $NoBuild -and -not $DryRun) {
    $csproj = Join-Path $Repo "tools\DemaConsulting.PartOfSpeech.Trainer\DemaConsulting.PartOfSpeech.Trainer.csproj"
    $build = Invoke-Logged @("build", $csproj, "-c", "Release", "--nologo", "-v", "q") (Join-Path $Out "build.txt") (Join-Path $Out "build.err.txt")
    if ($build.ExitCode -ne 0) { Stop-Driver 1 "the Release build failed (see $Out\build.txt)" }
}
if (-not $DryRun -and $Runner -eq "dotnet" -and -not (Test-Path $Dll)) { Stop-Driver 2 "trainer not found: $Dll" }

$done = 0
$skipped = 0
foreach ($arm in $armList) {
    foreach ($seed in $seedList) {
        $label = "$($arm.id)-s$seed"
        $final = Join-Path $Out "$label.json"
        if (Test-Path $final) { Write-Output "skip $label (result exists)"; $skipped++; continue }

        $free = Get-FreeGB
        if ($free -lt $MinFreeGB) { Stop-Driver 3 ("only {0:N1} GB free on C: before $label (need {1})" -f $free, $MinFreeGB) }

        $bin = Join-Path $Out "$label.bin"
        $trainTxt = Join-Path $Out "$label.train.txt"
        $trainErr = Join-Path $Out "$label.train.err.txt"
        $evalTmp = Join-Path $Out "$label.json.eval"
        $part = Join-Path $Out "$label.json.part"
        if (-not $DryRun) { foreach ($f in @($bin, $evalTmp, $part)) { if (Test-Path $f) { Remove-Item $f -Force } } }

        $features = if (@($arm.features).Count -gt 0) { @($arm.features) -join "," } else { "none" }
        $trainArgs = @($Dll, "train", "--seed", "$seed", "--features", $features)
        if (@($arm.features) -contains "rightPreview") { $trainArgs += @("--preview-folds", "$($spec.previewFolds)") }
        $trainArgs += @("--calibrate", "--output", $bin)
        Write-Output "train $label (features: $features)"
        $train = Invoke-Logged $trainArgs $trainTxt $trainErr
        if ($train.ExitCode -ne 0) { Stop-Driver 1 "train failed for $label (exit $($train.ExitCode); see $trainTxt and $trainErr)" }

        $trainSeconds = $null
        if (-not $DryRun) {
            $m = [regex]::Match((Get-Content -Raw $trainTxt), "Trained\s+[\d,]+\s+steps\s+in\s+([0-9][0-9.,]*)\s*s")
            if ($m.Success) { $trainSeconds = [double]::Parse($m.Groups[1].Value.Replace(",", ""), [System.Globalization.CultureInfo]::InvariantCulture) }
            else { Write-Warning "no 'Trained N steps in X s' line in $trainTxt (trainSeconds recorded as null)" }
        }

        Write-Output "eval-arm $label"
        $eval = Invoke-Logged @($Dll, "eval-arm", "--weights", $bin, "--label", $label, "--json", $evalTmp) (Join-Path $Out "$label.eval.txt") (Join-Path $Out "$label.eval.err.txt")
        if ($eval.ExitCode -ne 0) { Stop-Driver 1 "eval-arm failed for $label (exit $($eval.ExitCode); see $Out\$label.eval.txt)" }

        if ($DryRun) { continue }

        # Append the driver fields to the JSON text (no re-serialization of the trainer's numbers).
        $text = (Get-Content -Raw $evalTmp).TrimEnd()
        if (-not $text.StartsWith("{") -or -not $text.EndsWith("}")) { Stop-Driver 1 "the eval-arm result of $label is not a JSON object" }
        $extra = [ordered]@{
            arm = $arm.id; seed = [long]$seed; features = @($arm.features)
            trainSeconds = $trainSeconds
            trainWallSeconds = [Math]::Round($train.Seconds, 2); evalWallSeconds = [Math]::Round($eval.Seconds, 2)
            driver = [ordered]@{ commit = $commit; dirty = $dirty; utc = [DateTime]::UtcNow.ToString("o"); processorCount = $processors }
        }
        $json = ConvertTo-Json $extra -Compress -Depth 5
        $combined = $text.Substring(0, $text.Length - 1).TrimEnd() + "," + $json.Substring(1)
        try { $null = $combined | ConvertFrom-Json } catch { Stop-Driver 1 "the combined result of $label is not valid JSON: $_" }
        [System.IO.File]::WriteAllText($part, $combined + "`n", [System.Text.UTF8Encoding]::new($false))
        Move-Item $part $final
        Remove-Item $evalTmp -Force

        $line = "{0} {1} train {2:N1}s eval {3:N1}s free {4:N1}GB" -f [DateTime]::UtcNow.ToString("s"), $label, $train.Seconds, $eval.Seconds, (Get-FreeGB)
        Add-Content -Path (Join-Path $Out "progress.log") -Value $line
        Write-Output $line
        $done++
    }
}
Write-Output "done: $done run(s) completed, $skipped skipped"
exit 0
