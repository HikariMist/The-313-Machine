# Test results

Session 1 (cloud, .NET 8, no Unity):
- `Tools/CoreTests` — **36 passed, 0 failed.** Includes: content reference validation, save round-trip/tamper/garbage,
  inventory & upgrades, mimic-water death, knock/panel bond, 3:13 margin timing, **all 15 endings reached by scripted
  7-night playthroughs**, endless unlock + 5 escalating rounds survived, endless death, campaign cycle loop.
- Unity scripts — compile check against Unity 2021.3 reference assemblies: **succeeded**. Not executed in Unity.
- Browser playtest — headless Chromium: title → new game → phone call → served first customer → monitor/handbook,
  and a full Night 1 to sunrise with **no page errors**.

Not yet tested: Unity Play mode, Windows build, launcher, installer, human playthrough length, audio by ear.

Session 1, later:
- Launcher: Linux build of the same Avalonia code rendered under Xvfb; screenshot shows key art, buttons, version, and
  the correct "Game files not found" status when no game is present.
- Installer: compiled with Inno Setup 7.1 under Wine; silent install under Wine succeeded (per-user, no admin);
  silent uninstall removed install dir and shortcuts, **kept a planted save file**, removed launcher settings.
  This testing found and fixed a real bug: ":" in "The 3:13 Machine" is illegal in Windows paths, so all on-disk
  names (install folder, shortcuts, Unity productName/save folder, launcher settings) now use "The 313 Machine".
- Browser end-to-end: Playtest tools → Night 7 (careful route auto-played) → full night played through the UI →
  override keypad 1013 at the Collector → **THE TRUTH ending shown, "Clause Thirteen" achievement unlocked**, no page
  errors (278 s). Found and fixed: tall result/ending cards were clipped at the top.
