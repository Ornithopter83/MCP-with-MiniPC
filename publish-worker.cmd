@echo off
setlocal
set "SCRIPT=%~dp0scripts\worker-publish\publish-worker.ps1"

if not exist "%SCRIPT%" (
  echo.
  echo ERROR: Worker publish script was not found:
  echo   %SCRIPT%
  echo.
  echo Make sure this file is inside the ProjectHub repository root.
  pause
  exit /b 2
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "exitCode=%ERRORLEVEL%"

if not "%exitCode%"=="0" (
  echo.
  echo Worker publish failed with exit code %exitCode%.
  pause
)
exit /b %exitCode%
