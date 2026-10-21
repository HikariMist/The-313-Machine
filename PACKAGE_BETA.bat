@echo off
setlocal EnableDelayedExpansion
rem ============================================================================
rem  THE 3:13 MACHINE - package the closed beta
rem  1. Builds the Windows game (Unity batch mode)
rem  2. Publishes the launcher if the .NET 8 SDK is installed (optional)
rem  3. Creates Release\The313Machine_Beta_v0.1.0_Windows.zip (portable)
rem  4. Compiles Release\The313Machine_Beta_Setup_v0.1.0.exe if Inno Setup 6+ is installed
rem ============================================================================
set "VERSION=0.1.0"
cd /d "%~dp0"
title The 3:13 Machine - packaging beta v%VERSION%

rem ---- 1. Unity build -------------------------------------------------------
set "UNITY="
for %%R in ("%ProgramFiles%\Unity\Hub\Editor" "C:\Program Files\Unity\Hub\Editor" "D:\Program Files\Unity\Hub\Editor" "%LOCALAPPDATA%\Programs\Unity\Hub\Editor") do (
    if exist "%%~R" for /f "delims=" %%V in ('dir /b /ad /o-n "%%~R\6000*" 2^>nul') do (
        if not defined UNITY if exist "%%~R\%%V\Editor\Unity.exe" set "UNITY=%%~R\%%V\Editor\Unity.exe"
    )
)
if not defined UNITY ( echo Unity 6 not found. Install it with Unity Hub. & pause & exit /b 1 )
taskkill /im The313Machine.exe /f >nul 2>&1
if not exist Builds mkdir Builds
echo [1/4] Building the game with "%UNITY%" ...
"%UNITY%" -batchmode -quit -projectPath "%~dp0." -executeMethod HikariForge.M313.EditorTools.BuildScript.BuildWindows -logFile "%~dp0Builds\build_log.txt"
if not exist "Builds\Windows\The313Machine.exe" (
    echo BUILD FAILED - see Builds\build_log.txt
    findstr /i /c:"error CS" /c:"Exception" /c:"Build Failed" "Builds\build_log.txt"
    pause & exit /b 1
)
> "Builds\Windows\version.txt" echo %VERSION%

rem ---- 2. Launcher (optional) ----------------------------------------------
echo [2/4] Launcher...
where dotnet >nul 2>&1 && (
    dotnet publish Launcher\The313MachineLauncher.csproj -c Release -o Release\launcher >nul && echo      launcher built
) || echo      .NET SDK not found - beta ships without the launcher (game starts directly)

rem ---- 3. Portable ZIP ------------------------------------------------------
echo [3/4] Portable ZIP...
set "STAGE=Release\The313Machine_Beta_v%VERSION%"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%\Game"
xcopy /e /i /q /y "Builds\Windows" "%STAGE%\Game" >nul
for /d %%D in ("%STAGE%\Game\*_DoNotShip" "%STAGE%\Game\*_BackUpThisFolder_ButDontShipItWithYourGame") do rmdir /s /q "%%D"
if exist "Release\launcher\The313MachineLauncher.exe" copy /y "Release\launcher\The313MachineLauncher.exe" "%STAGE%" >nul & copy /y "Release\launcher\launcher.json" "%STAGE%" >nul & xcopy /e /i /q /y "Release\launcher\Intro" "%STAGE%\Intro" >nul
copy /y "Installer\README_BETA.txt" "%STAGE%" >nul
if exist "Release\The313Machine_Beta_v%VERSION%_Windows.zip" del "Release\The313Machine_Beta_v%VERSION%_Windows.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath 'Release\The313Machine_Beta_v%VERSION%_Windows.zip'"
echo      Release\The313Machine_Beta_v%VERSION%_Windows.zip

rem ---- 4. Installer (optional) ---------------------------------------------
echo [4/4] Installer...
set "ISCC="
for %%I in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 7\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do if exist %%I set "ISCC=%%~I"
if defined ISCC (
    pushd Installer
    "%ISCC%" /Q /DAppVersion=%VERSION% /O"..\Release" The313Machine.iss && echo      Release\The313Machine_Beta_Setup_v%VERSION%.exe
    popd
) else (
    echo      Inno Setup not found - skipped. Install it from jrsoftware.org to build the installer.
)

echo.
echo Done. Test the ZIP or installer on this PC before sending it to testers.
explorer Release
pause
