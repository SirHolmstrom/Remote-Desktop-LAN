@echo off
setlocal
title Remote Desktop LAN
cd /d "%~dp0"

REM --- Require the .NET 8 SDK ---
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK not found on PATH.
    echo Install the .NET 8 SDK from:
    echo     https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo ============================================================
echo  Remote Desktop LAN - starting in Release
echo  First run will restore packages and build (may take a bit).
echo  The access URL prints below once it is listening.
echo  Press Ctrl+C in this window to stop the server.
echo ============================================================
echo.

dotnet run --project "src\Core" -c Release

echo.
echo Server stopped.
pause
endlocal
