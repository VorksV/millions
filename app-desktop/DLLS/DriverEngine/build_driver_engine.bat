@echo off
REM ============================================================
REM  build_driver_engine.bat
REM  Voltris Driver Engine — Build x64 + x86 native DLLs
REM  Requires: Visual Studio 2022 (Community or higher) + CMake
REM ============================================================

setlocal EnableDelayedExpansion

set ROOT=%~dp0
set DLLS_DIR=%ROOT%

REM ── Find VS vswhere ──────────────────────────────────────────
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [ERROR] vswhere.exe not found. Install Visual Studio 2022.
    pause & exit /b 1
)

for /f "usebackq tokens=*" %%i in (
    `"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`
) do set VS_PATH=%%i

if "%VS_PATH%"=="" (
    echo [ERROR] Visual Studio with C++ tools not found.
    pause & exit /b 1
)

set "VCVARS64=%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat"
set "VCVARS32=%VS_PATH%\VC\Auxiliary\Build\vcvars32.bat"

echo [INFO] Visual Studio: %VS_PATH%
echo.

REM ── Find CMake ───────────────────────────────────────────────
where cmake >nul 2>&1
if errorlevel 1 (
    set "CMAKE=%VS_PATH%\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    if not exist "!CMAKE!" (
        echo [ERROR] CMake not found. Install CMake or VS C++ CMake tools.
        pause & exit /b 1
    )
) else (
    set CMAKE=cmake
)
echo [INFO] CMake: %CMAKE%
echo.

REM ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REM  BUILD x64
REM ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo ┌──────────────────────────────────────────────────────────┐
echo │  Building x64 DLL                                        │
echo └──────────────────────────────────────────────────────────┘

call "%VCVARS64%"

set BUILD64=%ROOT%build\x64
mkdir "%BUILD64%" 2>nul
pushd "%BUILD64%"

"%CMAKE%" "%ROOT%" -G "Visual Studio 17 2022" -A x64 ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%ROOT%bin"
if errorlevel 1 (echo [ERROR] CMake configure x64 failed. & popd & exit /b 1)

"%CMAKE%" --build . --config Release
if errorlevel 1 (echo [ERROR] Build x64 failed. & popd & exit /b 1)

popd

REM Copy x64 DLL to bin/
mkdir "%ROOT%bin" 2>nul
copy /Y "%BUILD64%\Release\driver_engine_x64.dll" "%ROOT%bin\" >nul
echo [OK] x64 DLL → %ROOT%bin\driver_engine_x64.dll
echo.

REM ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REM  BUILD x86
REM ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo ┌──────────────────────────────────────────────────────────┐
echo │  Building x86 DLL                                        │
echo └──────────────────────────────────────────────────────────┘

call "%VCVARS32%"

set BUILD32=%ROOT%build\x86
mkdir "%BUILD32%" 2>nul
pushd "%BUILD32%"

"%CMAKE%" "%ROOT%" -G "Visual Studio 17 2022" -A Win32 ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%ROOT%bin"
if errorlevel 1 (echo [ERROR] CMake configure x86 failed. & popd & exit /b 1)

"%CMAKE%" --build . --config Release
if errorlevel 1 (echo [ERROR] Build x86 failed. & popd & exit /b 1)

popd

copy /Y "%BUILD32%\Release\driver_engine_x86.dll" "%ROOT%bin\" >nul
echo [OK] x86 DLL → %ROOT%bin\driver_engine_x86.dll
echo.

REM ── Copy DLLs to project output ──────────────────────────────
set DEST=%ROOT%..\..\DLLS\DriverEngine\bin
mkdir "%DEST%" 2>nul
copy /Y "%ROOT%bin\driver_engine_x64.dll" "%DEST%\" >nul
copy /Y "%ROOT%bin\driver_engine_x86.dll" "%DEST%\" >nul

echo.
echo ╔══════════════════════════════════════════════════════════╗
echo ║  BUILD COMPLETE                                          ║
echo ║  DLLs copied to:                                         ║
echo ║    %DEST%
echo ╚══════════════════════════════════════════════════════════╝
pause
