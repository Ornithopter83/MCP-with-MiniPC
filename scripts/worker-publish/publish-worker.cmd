@echo off
setlocal
set "SCRIPT=%~dp0publish-worker.ps1"

if not exist "%SCRIPT%" (
  set "SCRIPT=%~dp0scripts\worker-publish\publish-worker.ps1"
)

if not exist "%SCRIPT%" (
  echo.
  echo ERROR: publish-worker.ps1 was not found.
  echo Expected one of:
  echo   %~dp0publish-worker.ps1
  echo   %~dp0scripts\worker-publish\publish-worker.ps1
  echo.
  echo Run this command from the ProjectHub repository root,
  echo or keep publish-worker.cmd and publish-worker.ps1 together.
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
