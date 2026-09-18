@echo off
setlocal
title ProjectHub Commit ^& Push
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub\bin\ProjectHub_Commit_Push.ps1" -ProjectPath "%~dp0." %*
set "EXITCODE=%ERRORLEVEL%"
echo.
echo ProjectHub Commit ^& Push exited with code %EXITCODE%.
pause
endlocal & exit /b %EXITCODE%
