@echo off
setlocal
schtasks /delete /tn "RemoteDesktopLAN" /f
if errorlevel 1 (
    echo.
    echo [INFO] No autostart task found (or it was already removed).
) else (
    echo.
    echo Autostart disabled.
)
pause
endlocal
