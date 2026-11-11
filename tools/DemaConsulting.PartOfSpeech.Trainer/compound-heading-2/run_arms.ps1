#requires -Version 7
<#
Compound-head and heading follow-up experiment (PR-B): resumable sequential driver (pre-registered; frozen with the RUN-LOG, hash recorded there).

This is the driver of the compound-head and heading data experiment (compound-heading/run_arms.ps1) with these changes and nothing else:
  1. it reads the arms.json that lies next to it (the arms BASE, CMP2, CMP2-F5, CMP2-HDG2 and CMP2-HDG2-F5 of the follow-up, eight seeds);
  2. the default -PyCache lies in the folder of the follow-up (session folder, files\compound-heading-followup\pycache);
  3. this documentation (the pre-registered text of the experiment is in RUN-LOG.md, section "Follow-up: pre-registered re-test (E1)").
The sources of an arm are appended as `--source <name>:1` after `--calibrate --output <file>` (BASE has none), so the only differences
between the arms are these options and --features. The interface contract with the trainer (train and eval-arm options, the per-run
result schema) is that of context-features/run_arms.ps1; eval-arm additionally writes the members `shapesNew` and `shapesFollowup`
(the four compound and heading tranches, the pooled primary metric, the imperative headings) and the hashes of the development files
in `inputs`. The PAUSE-file check before the build and before every run, the 6 GB floor and the thread cap are those of the earlier driver.

usage:
    pwsh run_arms.ps1 -Out <folder on C:> [-Arms BASE,CMP2,...] [-Seeds 20260101,11,...] [-Runner <runner-base.cmd>] [-NoBuild] [-DryRun]

What it does, for every arm of arms.json (in file order, BASE first) and every seed (in the order of arms.json), one process at a
time, each started at BelowNormal priority (or by the -Runner, which the experiment sets to imperative-data\runner-base.cmd:
Normal priority, see the deviation record in RUN-LOG.md) with DOTNET_PROCESSOR_COUNT capped at 8:

  1. stops with exit code 4 and a resume record when the PAUSE file exists (checked before the build and before every run);
  2. stops with exit code 3 when drive C: has less than 6 GB free (checked before every run);
  3. skips the run when <Out>\<arm>-s<seed>.json exists (that file is the atomic completion marker, so re-running the same
     command after any interruption resumes where it stopped; a run without its JSON is redone from scratch);
  4. train:     dotnet <trainer.dll> train --seed S --features <csv|none> [--preview-folds K] --calibrate --output <arm>-s<seed>.bin
                [--source <name>:1 ...]
     stdout -> <arm>-s<seed>.train.txt, stderr -> <arm>-s<seed>.train.err.txt;
  5. eval-arm:  dotnet <trainer.dll> eval-arm --weights <arm>-s<seed>.bin --label <arm>-s<seed> --json <arm>-s<seed>.json.eval
  6. adds the driver fields to that JSON (arm, seed, features, sources, trainSeconds, trainWallSeconds, evalWallSeconds, driver),
     checks that the result parses, and renames it to <arm>-s<seed>.json;
  7. appends one line to <Out>\progress.log.
A failing command stops the driver (exit 1) and deletes nothing of other runs. Only paths on C: are used (the repository, -Out, the
PAUSE file and the Python cache); no network or UNC path is touched and no drive is enumerated. The trainer is built once in Release
first (-NoBuild skips that); the repository commit and a dirty-tree flag are recorded in every result. -DryRun prints the commands
and runs nothing (no train, no eval-arm, no build, nothing is written).

Python is not run by this script, but PYTHONPYCACHEPREFIX and PYTHONDONTWRITEBYTECODE=1 are exported (the default -PyCache lies in
the session folder) so that nothing in a child process writes a __pycache__ into the repository.

Exit codes: 0 all requested runs complete, 1 a command failed, 2 usage or path error, 3 less than 6 GB free on C:, 4 paused (the
PAUSE file exists; nothing was started, <Out>\resume.txt names the next run).
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
    [string]$PyCache = "C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\compound-heading-followup\pycache",
    [string]$PauseFile = "C:\Users\mnixon\.copilot\session-state\217fb8fb-495d-4da7-89a1-b33f6a7805ad\files\PAUSE",
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

# Stops cleanly when the PAUSE file exists: nothing is started, the resume record names the next step.
function Stop-IfPaused([string]$Next) {
    if (-not (Test-Path -LiteralPath $PauseFile)) { return }
    $resume = Join-Path $Out "resume.txt"
    $text = "paused at {0}Z: the PAUSE file exists ({1}); next: {2}; resume with the same command line (finished runs are skipped)" -f
        [DateTime]::UtcNow.ToString("s"), $PauseFile, $Next
    if (-not $DryRun) {
        New-Item -ItemType Directory -Force $Out | Out-Null
        [System.IO.File]::WriteAllText($resume, $text + "`n", [System.Text.UTF8Encoding]::new($false))
    }
    Stop-Driver 4 $text
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Repo) { $Repo = Join-Path $here "..\..\.." }
$Repo = Assert-LocalC $Repo "-Repo"
$Out = Assert-LocalC $Out "-Out"
$PyCache = Assert-LocalC $PyCache "-PyCache"
$PauseFile = Assert-LocalC $PauseFile "-PauseFile"
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
    Stop-IfPaused "the Release build, then the first run without a result"
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

        Stop-IfPaused "$label (arm $($arm.id), seed $seed)"

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
        foreach ($source in @($arm.sources)) { $trainArgs += @("--source", "${source}:1") }
        Write-Output "train $label (features: $features; sources: $(@($arm.sources) -join ',' ))"
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
            arm = $arm.id; seed = [long]$seed; features = @($arm.features); sources = @($arm.sources)
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
