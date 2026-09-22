@echo off
setlocal
pushd "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-worker.ps1" %*
set "exitCode=%ERRORLEVEL%"

popd
if not "%exitCode%"=="0" (
  echo.
  echo Worker publish failed with exit code %exitCode%.
  pause
)
exit /b %exitCode%
