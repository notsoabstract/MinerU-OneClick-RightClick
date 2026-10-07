@echo off
setlocal
if "%~1" neq "__inner" (
  start "Install MinerU One Click" cmd /k ""%~f0" __inner"
  exit /b
)
cd /d "%~dp0"
echo Working directory:
cd
echo.
echo Installing dependencies...
where npm >nul 2>nul
if errorlevel 1 (
  echo npm was not found. Please install Node.js first, then run this file again.
  exit /b 1
)
echo Node:
node -v
echo npm:
call npm -v
echo.
echo Using China-friendly mirrors for npm and Electron...
set npm_config_registry=https://registry.npmmirror.com
set ELECTRON_MIRROR=https://npmmirror.com/mirrors/electron/
call npm install --loglevel=info --progress=true
if errorlevel 1 (
  echo Install failed. Please copy the error text and send it to Codex.
  exit /b 1
)
echo Install finished.
