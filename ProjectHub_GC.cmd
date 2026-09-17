@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_GC.ps1" %*
endlocal
