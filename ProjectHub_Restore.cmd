@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bin\ProjectHub_Restore.ps1" -ProjectRoot "%~dp0." %*
endlocal
