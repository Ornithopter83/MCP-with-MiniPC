@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_GC.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
echo.
echo ProjectHub GC exited with code %EXITCODE%.
pause
endlocal & exit /b %EXITCODE%
