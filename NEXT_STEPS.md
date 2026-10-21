1. **Beta tonight:** follow BETA_READINESS.md → run `PACKAGE_BETA.bat`, smoke-test, then ITCH_BETA.md.
# Next steps (exact continuation)

1. Owner: double-click `BUILD_AND_PLAY.bat` (finds Unity 6, builds, launches; errors → `Builds/build_log.txt`).
   Or open the project in Unity 6.3 LTS. Run **Hikari Forge → Configure Project**, press Play, report errors.
   Then **Hikari Forge → Build Windows (x64)** → `Builds/Windows/The313Machine.exe`. Commit the generated `.meta`
   files and `ProjectSettings/` after first open.
2. Fix whatever the first Unity run reveals (it has only been compile-checked).
3. Package: publish the launcher, put the Unity build in `Builds/Windows/`, compile `Installer/The313Machine.iss`
   (Inno Setup 6.4+/7 on Windows, or `wine64 ISCC.exe` in the cloud). Test install/launch/uninstall on real Windows.
5. Replace remaining placeholder portraits as the studio supplies art (drop sheets in `Art/Source`, extend
   `SHEET_BUSTS` in `Tools/ArtGen/import_user_art.py`).
6. Work through RELEASE_CHECKLIST.md before publishing anything.
