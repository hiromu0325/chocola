@echo off
rem ============================================================
rem  RENASCITA ROM builder (Windows x64 standalone)
rem    double-click             : release build -> Build\Windows\RENASCITA.exe
rem    build_rom.bat dev        : development build (script debugging on)
rem    build_rom.bat zip        : release build + Build\RENASCITA_<version>_<date>.zip
rem    build_rom.bat dev zip    : both
rem    set VERSION=0.2.0 before running to stamp PlayerSettings.bundleVersion
rem  NOTE: keep this file ASCII-only (cmd.exe reads batch files in the
rem        system code page; UTF-8 Japanese breaks the parser).
rem ============================================================
setlocal enabledelayedexpansion

set "UNITY=E:\Unity\Unity_6_3_16\Editor\Unity.exe"
set "ROOT=%~dp0"
set "PROJECT=%ROOT%project"
set "OUT=%ROOT%Build\Windows"
set "LOG=%ROOT%Build\build.log"
set "DEV="
set "ZIP="
for %%A in (%*) do (
    if /i "%%A"=="dev" set "DEV=-dev"
    if /i "%%A"=="zip" set "ZIP=1"
)
set "VERARG="
if not "%VERSION%"=="" set "VERARG=-version %VERSION%"

if not exist "%UNITY%" (
    echo [ERROR] Unity not found: %UNITY%
    pause
    exit /b 1
)
rem A stale lockfile is left behind after a crash, so only block when a Unity editor is actually running.
if exist "%PROJECT%\Temp\UnityLockfile" (
    tasklist /fi "imagename eq Unity.exe" 2>nul | find /i "Unity.exe" >nul
    if not errorlevel 1 (
        echo [ERROR] The project is open in the Unity Editor.
        echo         Close the editor first, or use the menu in the editor:
        echo         Tools ^> EscapePrototype ^> Build ^> "Windows ROM"
        pause
        exit /b 2
    )
)

if not exist "%ROOT%Build" mkdir "%ROOT%Build"
echo.
echo  Building RENASCITA ROM  (dev=%DEV%  zip=%ZIP%  %VERARG%)
echo  output : %OUT%
echo  log    : %LOG%
echo  This takes a few minutes. Unity runs in batch mode (no window).
echo.

"%UNITY%" -batchmode -nographics -quit ^
  -projectPath "%PROJECT%" ^
  -executeMethod EscapeProto.EditorTools.BuildScript.BuildWindows ^
  -buildTarget Win64 ^
  -buildPath "%OUT%" %DEV% %VERARG% ^
  -logFile "%LOG%"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
    echo.
    echo [ERROR] Build failed ^(exit code %RC%^). Last errors from the log:
    findstr /i /c:"error" "%LOG%" | findstr /v /i "0 errors" | more
    pause
    exit /b %RC%
)

echo.
echo [OK] Build succeeded: %OUT%\RENASCITA.exe
type "%OUT%\BUILD_INFO.txt"

if "%ZIP%"=="1" (
    for /f "tokens=2" %%V in ('findstr /b "version:" "%OUT%\BUILD_INFO.txt"') do set "VER=%%V"
    for /f "tokens=1-3 delims=/ " %%a in ("%DATE%") do set "STAMP=%%a%%b%%c"
    set "ZIPFILE=%ROOT%Build\RENASCITA_!VER!_!STAMP!.zip"
    echo.
    echo  Zipping -> !ZIPFILE!
    powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '!ZIPFILE!' -Force"
    if exist "!ZIPFILE!" (echo [OK] !ZIPFILE!) else (echo [WARN] zip failed)
)

start "" "%OUT%"
pause
