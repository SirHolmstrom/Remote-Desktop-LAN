@echo off
setlocal
cd /d "%~dp0"

REM Registers a Task Scheduler task that runs run.bat at logon, in your normal
REM (non-elevated) user session - required because screen capture and input
REM injection only work inside the interactive desktop session. A console window
REM will appear at logon: that's intentional (visible status, no hidden access).

schtasks /create /tn "RemoteDesktopLAN" /tr "\"%~dp0run.bat\"" /sc onlogon /rl limited /f
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to create the scheduled task.
    pause
    exit /b 1
)

echo.
echo Autostart enabled. Remote Desktop LAN will launch at next logon.
echo To remove it, run uninstall-autostart.bat.
pause
endlocal
