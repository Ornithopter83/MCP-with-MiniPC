@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_Restore.ps1" %*
endlocal
