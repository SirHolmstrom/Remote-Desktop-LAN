@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "New-NetFirewallRule -DisplayName 'RemoteDesktopLAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8443 -RemoteAddress LocalSubnet"
pause