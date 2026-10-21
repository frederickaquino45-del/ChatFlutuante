@echo off
(
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0desinstalar.ps1"
  echo.
  pause
)
