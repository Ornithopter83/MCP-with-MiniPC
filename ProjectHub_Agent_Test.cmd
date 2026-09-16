@echo off
setlocal
title ProjectHub Agent E2E Test

set "SCRIPT=%~dp0ProjectHub_Agent_Test.ps1"

if not exist "%SCRIPT%" (
    echo ERROR: ProjectHub_Agent_Test.ps1 was not found.
    echo Put this CMD and the PS1 file in the same folder.
    echo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"

echo.
pause
endlocal
