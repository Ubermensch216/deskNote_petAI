@echo off
rem ---------------------------------------------------------------------------
rem  DeskNote - local AI installer (double-click me)
rem
rem  Installs Ollama and the two models DeskNote uses, then verifies the
rem  connection. Everything stays on this PC. Notes are never read or sent.
rem
rem  This file is only a launcher: the Korean-language logic lives in
rem  install-ai.ps1, which must sit in the same folder.
rem
rem  Options are passed straight through, for example:
rem      install-ai.bat -CheckOnly
rem      install-ai.bat -Yes
rem      install-ai.bat -Model llama3.2:3b
rem ---------------------------------------------------------------------------

setlocal
chcp 65001 >nul 2>nul
title DeskNote - local AI installer

set "SCRIPT=%~dp0install-ai.ps1"

if not exist "%SCRIPT%" (
    echo [ERROR] install-ai.ps1 was not found next to this file:
    echo         "%SCRIPT%"
    echo         Keep install-ai.bat and install-ai.ps1 together in one folder.
    echo.
    pause
    exit /b 1
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Windows PowerShell was not found on this PC.
    echo         Install Ollama manually instead: https://ollama.com/download/windows
    echo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "RESULT=%ERRORLEVEL%"

echo.
pause
exit /b %RESULT%
