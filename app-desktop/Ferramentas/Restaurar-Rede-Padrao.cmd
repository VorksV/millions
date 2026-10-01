@echo off
setlocal
title Restaurar Rede - Padrao do Windows

rem ---------------------------------------------------------------
rem  Guarda anti-loop: se ja fomos reabertos como elevados,
rem  executa direto e NUNCA tenta elevar de novo.
rem ---------------------------------------------------------------
if /i "%~1"=="elevated" goto run

rem fltmc exige admin e nao depende do servico Server (LanmanServer),
rem diferente de "net session" que falha se o servico estiver parado.
fltmc >nul 2>&1
if %errorlevel% neq 0 (
    echo ============================================================
    echo   Solicitando privilegios de Administrador...
    echo ============================================================
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList 'elevated' -Verb RunAs"
    exit /b
)

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restaurar-Rede-Padrao-Windows.ps1"
echo.
echo ============================================================
pause
