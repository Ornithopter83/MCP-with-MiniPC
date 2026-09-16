@echo off
title ProjectHub Update ^& Run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AI-Server\ProjectHub\ProjectHub_Update_Run.ps1"
if errorlevel 1 (
    echo.
    echo ProjectHub update/run failed.
    pause
)
