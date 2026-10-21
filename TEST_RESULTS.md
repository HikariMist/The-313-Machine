# Test results

Session 1 (cloud, .NET 8, no Unity):
- `Tools/CoreTests` — **36 passed, 0 failed.** Includes: content reference validation, save round-trip/tamper/garbage,
  inventory & upgrades, mimic-water death, knock/panel bond, 3:13 margin timing, **all 15 endings reached by scripted
  7-night playthroughs**, endless unlock + 5 escalating rounds survived, endless death, campaign cycle loop.
- Unity scripts — compile check against Unity 2021.3 reference assemblies: **succeeded**. Not executed in Unity.
- Browser playtest — headless Chromium: title → new game → phone call → served first customer → monitor/handbook,
  and a full Night 1 to sunrise with **no page errors**.

Not yet tested: Unity Play mode, Windows build, launcher, installer, human playthrough length, audio by ear.
