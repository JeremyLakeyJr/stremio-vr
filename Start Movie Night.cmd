@echo off
cd /d "%~dp0"

if not exist .env (
  if exist .env.example (
    echo [Movie Night] Creating default .env configuration file...
    copy .env.example .env >nul
  )
)

set "COMPOSE_FILES=-f compose.yaml"
where nvidia-smi >nul 2>nul
if %errorlevel% equ 0 (
  echo [Movie Night] NVIDIA GPU detected -- enabling hardware transcoding override.
  set "COMPOSE_FILES=-f compose.yaml -f compose.gpu.yaml"
)

docker compose %COMPOSE_FILES% up -d --build
if errorlevel 1 (
  echo Could not start Movie Night. Make sure Docker Desktop is running.
  pause
  exit /b 1
)
start "" "http://localhost:3000"
echo Movie Night is starting. The invite link and QR code will appear in your browser.
pause
