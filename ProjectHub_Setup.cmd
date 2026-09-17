@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_Setup.ps1" %*
endlocal
