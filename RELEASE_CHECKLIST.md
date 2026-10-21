# Release checklist

## Game
- [ ] Opens in Unity 6.3 LTS without errors; Play mode runs title → night 1 → sunrise
- [ ] Windows build (`Hikari Forge → Build Windows (x64)`) runs outside Unity on a clean PC
- [ ] All 7 nights completed by a human; campaign time recorded (target 2–3 h)
- [ ] At least 3 different endings reached by hand; others spot-checked with Playtest tools
- [ ] Save, quit, Continue works; damaged save falls back to backup
- [ ] Settings persist; comfort options reduce flashing/shake/scare volume
- [ ] Alt-tab pauses the shift; no runaway memory over a full night
- [ ] Approved Hikari Forge logos in `Assets/_Project/Resources/Branding/` (hf_emblem.png, hf_wordmark.png)
- [ ] Remaining placeholder portraits replaced or approved as final

## Packaging
- [ ] `version.txt` (e.g. `1.0.0`) placed next to `The313Machine.exe`
- [ ] Launcher published (`cd Launcher && dotnet publish -c Release -o ../Release/launcher`)
- [ ] `launcher.json` store/support URLs point at the real pages
- [ ] Installer compiled (`Installer/The313Machine.iss`) and tested: install, desktop + Start menu shortcuts, launch, uninstall keeps saves
- [ ] Portable ZIP: `Game/` + launcher + launcher.json
- [ ] Windows SmartScreen: consider code-signing the launcher and installer

## Store and legal
- [ ] Trademark search on the title
- [ ] LICENSES.md reviewed; AI disclosure entered on Steam
- [ ] Store pages filled from STEAM_RELEASE.md / ITCH_RELEASE.md using real screenshots
- [ ] Owner approval to publish
