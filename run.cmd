@echo off
setlocal

REM Runs the PowerShell helper that publishes and starts SimpleScrollSC.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
