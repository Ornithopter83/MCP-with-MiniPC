@echo off
setlocal EnableExtensions
title ProjectHub Fetch ^& Pull

for %%I in ("%~dp0.") do set "PROJECT_ROOT=%%~fI"
set "SCRIPT=%PROJECT_ROOT%\scripts\ProjectHub_Fetch_Pull.ps1"

if not exist "%SCRIPT%" (
    echo ERROR: Fetch/Pull engine was not found:
    echo   %SCRIPT%
    set "EXITCODE=2"
    goto :finish
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo ERROR: powershell.exe was not found.
    set "EXITCODE=9009"
    goto :finish
)

pushd "%PROJECT_ROOT%" >nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -ProjectPath "%PROJECT_ROOT%" %*
set "EXITCODE=%ERRORLEVEL%"
popd

:finish
echo.
if "%EXITCODE%"=="0" (
    echo ProjectHub Fetch ^& Pull completed successfully.
) else (
    echo ProjectHub Fetch ^& Pull failed with code %EXITCODE%.
)
pause
endlocal & exit /b %EXITCODE%
