@echo off
setlocal
title ProjectHub Force Restore
mode con: cols=220 lines=50
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bin\ProjectHub_Force_Restore.ps1" -ProjectPath "%~dp0." %*
set "EXITCODE=%ERRORLEVEL%"
echo.
echo ProjectHub Force Restore exited with code %EXITCODE%.
pause
endlocal & exit /b %EXITCODE%
