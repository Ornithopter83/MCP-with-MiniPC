@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ProjectHub_Setup.ps1" -ProjectRoot "%~dp0." -ProjectHubSource "%~dp0." %*
if errorlevel 1 (
    echo SETUP_FAILED. Review the error above.
) else (
    echo SETUP_FINISHED. ProjectHub files are ready under bin.
)
pause
endlocal
