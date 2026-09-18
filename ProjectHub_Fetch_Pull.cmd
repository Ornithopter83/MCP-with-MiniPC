@echo off
setlocal
title ProjectHub Fetch ^& Pull
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bin\ProjectHub_Fetch_Pull.ps1" -ProjectPath "%~dp0." %*
set "EXITCODE=%ERRORLEVEL%"
echo.
echo ProjectHub Fetch ^& Pull exited with code %EXITCODE%.
pause
endlocal & exit /b %EXITCODE%
