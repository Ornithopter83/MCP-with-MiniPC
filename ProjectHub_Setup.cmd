@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_Setup.ps1" -ProjectRoot "%~dp0." -ProjectHubSource "%~dp0." %*
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
    echo SETUP_FAILED. Review the error above.
) else (
    echo SETUP_FINISHED. ProjectHub files are ready under bin.
)
echo.
echo ProjectHub Setup exited with code %EXITCODE%.
pause
endlocal & exit /b %EXITCODE%
