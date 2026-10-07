@echo off
setlocal
if "%~1" neq "__inner" (
  start "MinerU One Click" cmd /k ""%~f0" __inner"
  exit /b
)
cd /d "%~dp0"
where npm >nul 2>nul
if errorlevel 1 (
  echo npm was not found. Please install Node.js first, then run this file again.
  exit /b 1
)
call npm start
