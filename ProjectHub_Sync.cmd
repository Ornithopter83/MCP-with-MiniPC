@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0bin\ProjectHub_Sync.ps1" -ProjectPath "%~dp0." %*
set "EXITCODE=%ERRORLEVEL%"
echo.
echo ProjectHub Sync exited with code %EXITCODE%.
pause
endlocal & exit /b %EXITCODE%
