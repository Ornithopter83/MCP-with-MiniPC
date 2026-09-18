@echo off
title ProjectHub Update ^& Run
mode con: cols=220 lines=50
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AI-Server\ProjectHub\ProjectHub_Update_Run.ps1"
set "EXITCODE=%ERRORLEVEL%"
echo.
echo ProjectHub exited with code %EXITCODE%.
pause
exit /b %EXITCODE%
