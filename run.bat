@echo off
title WordFormatter Launcher
setlocal enabledelayedexpansion

set "ROOT=%~dp0"

echo ========================================
echo   Word Formatter Launcher
echo ========================================
echo.

rem -- Kill stale backend processes (old code keeps serving otherwise) --
rem 只按进程名清理，避免误杀占用 8765 端口的无关程序
echo [0/2] Cleaning up stale backend processes...
taskkill /F /IM backend.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul

rem -- Backend --
echo [1/2] Starting backend server...
start "WordFormatter-Backend" cmd /k "title Backend && cd /d %ROOT% && set PYTHONPATH=%ROOT% && echo Backend: http://127.0.0.1:8765 && python -m uvicorn backend.server:app --host 127.0.0.1 --port 8765 --reload"

rem Wait for backend port
echo Waiting for backend to be ready...
set /a TRIES=0
:waitloop
set /a TRIES+=1
ping -n 2 127.0.0.1 >nul
curl -s -o nul http://127.0.0.1:8765/api/health 2>nul && goto backend_ready
if !TRIES! lss 10 goto waitloop

:backend_ready
echo Backend is ready.

rem -- Frontend --
echo [2/2] Starting frontend...
cd /d "%ROOT%frontend"
start "WordFormatter-Frontend" cmd /k "title Frontend && dotnet run"

echo.
echo ========================================
echo   Backend  : http://127.0.0.1:8765
echo   Frontend : should appear as a window
echo ========================================
echo.
echo Close the two launched windows to stop.
pause
