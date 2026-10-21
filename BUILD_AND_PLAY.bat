@echo off
setlocal EnableDelayedExpansion
rem ============================================================================
rem  THE 3:13 MACHINE - one-click Windows build
rem  Double-click this file. It finds Unity, builds the game, and starts it.
rem  The first run imports every asset and can take 5-15 minutes.
rem ============================================================================
title The 3:13 Machine - building...
cd /d "%~dp0"

rem ---- Find a Unity editor installed through Unity Hub (newest Unity 6 first) ----
set "UNITY="
for %%R in ("%ProgramFiles%\Unity\Hub\Editor" "%ProgramW6432%\Unity\Hub\Editor" "C:\Program Files\Unity\Hub\Editor" "D:\Program Files\Unity\Hub\Editor" "%LOCALAPPDATA%\Programs\Unity\Hub\Editor") do (
    if exist "%%~R" (
        for /f "delims=" %%V in ('dir /b /ad /o-n "%%~R\6000*" 2^>nul') do (
            if not defined UNITY if exist "%%~R\%%V\Editor\Unity.exe" set "UNITY=%%~R\%%V\Editor\Unity.exe"
        )
    )
)
if not defined UNITY (
    echo.
    echo  Could not find Unity 6 ^(6000.x^) installed through Unity Hub.
    echo  Install "Unity 6.3 LTS" with "Windows Build Support (Mono)" from Unity Hub, then run this again.
    echo  If Unity is installed somewhere else, set UNITY to the full path of Unity.exe at the top of this file.
    echo.
    pause
    exit /b 1
)
echo Using Unity: "%UNITY%"

rem ---- Close a running copy of the game so the build can overwrite it ----
taskkill /im The313Machine.exe /f >nul 2>&1

if not exist "Builds" mkdir "Builds"
set "LOG=%~dp0Builds\build_log.txt"
echo Building... (log: Builds\build_log.txt)
echo This window will stay open until the build finishes.
"%UNITY%" -batchmode -quit -projectPath "%~dp0." -executeMethod HikariForge.M313.EditorTools.BuildScript.BuildWindows -logFile "%LOG%"
set "RESULT=%ERRORLEVEL%"

if exist "Builds\Windows\The313Machine.exe" if "%RESULT%"=="0" (
    echo.
    echo  Build succeeded: Builds\Windows\The313Machine.exe
    echo  Starting the game...
    start "" "Builds\Windows\The313Machine.exe"
    timeout /t 5 >nul
    exit /b 0
)

echo.
echo  ===================== BUILD FAILED =====================
echo  The errors below come from Builds\build_log.txt.
echo  Copy them (or send the whole log file) to Claude.
echo  ========================================================
echo.
findstr /i /c:"error CS" /c:"Exception" /c:"Build Failed" /c:"BuildResult" /c:"license" /c:"Scripts have compiler errors" "%LOG%"
echo.
echo  Common fixes:
echo   - "No valid Unity Editor license": open Unity Hub once, sign in, then run this again.
echo   - Package errors: open Packages\manifest.json and delete the "render-pipelines.universal" line.
echo.
pause
exit /b 1
