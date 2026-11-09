@echo off
rem Imperative-data experiment (pre-registered, frozen by the hash in RUN-LOG.md): runner for the arms with the imperative source.
rem Starts dotnet at Normal priority (see runner-base.cmd) and appends "--source imperative:1" when the second argument is "train"
rem (the driver calls: <runner> <trainer.dll> train ...); every other command (build, eval-arm) is passed through unchanged.
rem Use as: pwsh ..\context-features\run_arms.ps1 -Out <folder on C:> -Arms F2 -Runner <this file>
if /i "%~2"=="train" goto train
start "" /normal /wait /b dotnet %*
exit /b %ERRORLEVEL%
:train
start "" /normal /wait /b dotnet %* --source imperative:1
exit /b %ERRORLEVEL%
