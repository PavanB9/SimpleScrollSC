@echo off
setlocal

REM Runs the PowerShell helper that publishes and starts ScrollShot.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
