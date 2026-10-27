@echo off
rem The Desktop shortcut runs this. It asks first (MAT-T67):
rem   1) Deploy & Run: tools\deploy.ps1 -Force -Launch pulls latest, rebuilds Release, publishes to
rem      C:\Apps\GridGameGaiden, refreshes the Desktop shortcut and launches it.
rem   2) Run: launches the published C:\Apps\GridGameGaiden\GridGameGaiden.exe without deploying (deploys if missing).
rem Agents and /deploy call tools\deploy.ps1 directly; it never prompts.
rem No goto/labels on purpose: this file is LF, and cmd's label search is unreliable in LF files.
setlocal
set "APP_DIR=C:\Apps\GridGameGaiden"
set "APP_EXE=%APP_DIR%\GridGameGaiden.exe"
echo.
echo   GridGame Gaiden
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
set "PICK=%ERRORLEVEL%"
if "%PICK%"=="2" if not exist "%APP_EXE%" (
    echo   Nothing is published at %APP_DIR% yet - deploying first.
    set "PICK=1"
)
if "%PICK%"=="2" (
    start "" /d "%APP_DIR%" "%APP_EXE%"
    exit /b 0
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\deploy.ps1" -Force -Launch %*
exit /b %ERRORLEVEL%
