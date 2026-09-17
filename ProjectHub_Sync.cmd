@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_Sync.ps1" -ProjectPath "%~dp0" %*
endlocal
