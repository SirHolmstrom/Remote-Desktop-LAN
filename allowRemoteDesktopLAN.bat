@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-NetFirewallRule -DisplayName 'RemoteDesktopLAN LAN' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; New-NetFirewallRule -DisplayName 'RemoteDesktopLAN LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8443 -RemoteAddress LocalSubnet"
pause
