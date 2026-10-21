# Next steps (exact continuation)

1. Owner: open the project in Unity 6.3 LTS. Run **Hikari Forge → Configure Project**, press Play, report errors.
   Then **Hikari Forge → Build Windows (x64)** → `Builds/Windows/The313Machine.exe`. Commit the generated `.meta`
   files and `ProjectSettings/` after first open.
2. Fix whatever the first Unity run reveals (it has only been compile-checked).
3. Launcher: `Launcher/` WinForms .NET 8 app (PLAY / Check for Updates / Settings / Support / Exit), self-contained
   win-x64 publish — can be built in the cloud container.
4. Installer: `Installer/The313Machine.iss` (Inno Setup) packaging the Unity build + launcher.
5. Replace remaining placeholder portraits as the studio supplies art (drop sheets in `Art/Source`, extend
   `SHEET_BUSTS` in `Tools/ArtGen/import_user_art.py`).
6. Docs still to write: README, CHANGELOG, LICENSES, RELEASE_CHECKLIST, STEAM_RELEASE, ITCH_RELEASE, marketing.
