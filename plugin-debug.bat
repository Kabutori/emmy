@echo off
setlocal
cd /d "%~dp0"
call debug-build.bat
if errorlevel 1 exit /b %ERRORLEVEL%
powershell -NoProfile -ExecutionPolicy Bypass -File "artifacts\package\Install-Plugin.ps1"
exit /b %ERRORLEVEL%
