@echo off
cd /d "%~dp0"
docker compose up -d --build
if errorlevel 1 (
  echo Could not start Movie Night. Make sure Docker Desktop is running.
  pause
  exit /b 1
)
start "" "http://localhost:3000"
echo Movie Night is starting. The invite link will appear in your browser.
pause
