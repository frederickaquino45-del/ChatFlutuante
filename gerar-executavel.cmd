@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0gerar-executavel.ps1"
set "build_result=%ERRORLEVEL%"
echo.
pause
exit /b %build_result%
