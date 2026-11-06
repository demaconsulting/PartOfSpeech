# Corpus cleanup C, `prenominal-ing` follow-up: train one arm with each seed (shipped config, --calibrate), then dump the
# predictions of the runtime decoder on open dev and UD-EWT dev (forms only are read: no held-out file).
# usage: pwsh run_arms.ps1 -Scratch <folder> -UdDev <ud_dev.tsv> -Arms full,noing,noctrl -Seeds 20260101,11,12,13,14
# layout under -Scratch: wt-<arm> (worktree of the arm, trainer built in Release net10.0), dump-<arm>\out\Dump.exe (built against the
# arm's own library), runs\ (output: <arm>-s<seed>.bin, <arm>-s<seed>-open.tsv, <arm>-s<seed>-ud.tsv, <arm>-s<seed>.train.txt)
param([string]$Scratch, [string]$UdDev, [string]$Arms, [string]$Seeds)
$ErrorActionPreference = "Stop"
$runs = Join-Path $Scratch "runs"
New-Item -ItemType Directory -Force $runs | Out-Null
foreach ($arm in ($Arms -split ",")) {
    $wt = Join-Path $Scratch "wt-$arm"
    $trainer = Join-Path $wt "tools\DemaConsulting.PartOfSpeech.Trainer\bin\Release\net10.0\DemaConsulting.PartOfSpeech.Trainer.dll"
    $dump = Join-Path $Scratch "dump-$arm\out\Dump.exe"
    $openDev = Join-Path $Scratch "wt-full\data\open\dev.tsv"   # FULL's relabeled dev; Dump reads the forms only
    foreach ($s in ($Seeds -split ",")) {
        $w = Join-Path $runs "$arm-s$s.bin"
        if (-not (Test-Path $w)) {
            Push-Location $wt   # the trainer resolves training.json and the data paths from the repository root it finds
            & dotnet $trainer train --seed "$s" --calibrate --output $w 2>&1 | Out-File (Join-Path $runs "$arm-s$s.train.txt") -Encoding utf8
            Pop-Location
        }
        & $dump $w $openDev (Join-Path $runs "$arm-s$s-open.tsv") | Out-Null
        & $dump $w $UdDev (Join-Path $runs "$arm-s$s-ud.tsv") | Out-Null
        Write-Output "$arm seed $s hash $((Get-FileHash $w).Hash)"
    }
}
