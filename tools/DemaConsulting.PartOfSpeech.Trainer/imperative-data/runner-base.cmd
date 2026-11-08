@echo off
rem Imperative-data experiment (pre-registered, frozen by the hash in RUN-LOG.md): runner for the arms without the imperative source.
rem Starts dotnet at Normal priority: the machine is saturated by other sessions and BelowNormal, the default of run_arms.ps1, starves.
rem Use as: pwsh ..\context-features\run_arms.ps1 -Out <folder on C:> -Arms F2 -Runner <this file>
start "" /normal /wait /b dotnet %*
exit /b %ERRORLEVEL%
