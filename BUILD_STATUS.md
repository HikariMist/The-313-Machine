# Build status

| Build | Status |
|---|---|
| Browser playtest | Built and published (private artifact) |
| Unity Windows .exe | **Built successfully on owner's PC** (Unity 6000.6.3f1). Rebuild for beta with `PACKAGE_BETA.bat` |
| Beta package | `PACKAGE_BETA.bat` → portable ZIP + `The313Machine_Beta_Setup_v0.1.0.exe` (installer compiles with/without launcher) |
| Launcher `The313MachineLauncher.exe` | Builds in the cloud (Avalonia, self-contained win-x64, ~45 MB): `cd Launcher && dotnet publish -c Release -o ../Release/launcher`. UI verified by screenshot (Linux build under Xvfb); not run on real Windows |
| Installer `The313Machine_Setup.exe` | `Installer/The313Machine.iss` compiles with Inno Setup 7.1 (under Wine). Install + uninstall verified under Wine with a stand-in game: per-user install without admin, Start menu + desktop shortcuts, uninstall removes files and shortcuts and keeps saves. Release installer needs the real Unity build |
| Steam package | Docs ready (STEAM_RELEASE.md); needs App ID and Unity build |
| itch.io package | Docs ready (ITCH_RELEASE.md); needs Unity build |
