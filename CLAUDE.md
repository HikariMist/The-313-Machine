# THE 3:13 MACHINE — working notes for Claude sessions

Hikari Forge Studios psychological horror game. Unity 6.3 LTS (C#), Windows target, plus a browser playtest.
At session start read: DEVELOPMENT_STATUS.md, NEXT_STEPS.md, TASK_QUEUE.md, KNOWN_ISSUES.md.

## Architecture
- `Assets/_Project/Scripts/Core/` — all game logic, **no UnityEngine references** (asmdef `noEngineReferences`).
  `ShiftContext` (one night's simulation), `GameSession` (screen/dialogue flow shared by Unity and browser),
  `ContentDatabase` (JSON content), `Conditions`/`Effects` (content scripting), `HorrorDirector`, `Profile`
  (endings/achievements), `Endless`, `AutoPlayer` (scripted player used by tests and playtest "skip to night").
- `Assets/_Project/Resources/Content/*.json` — all story content: nights, encounters (n1–n7 + endless templates),
  handbook, clues, products, upgrades, characters, 15 endings. Condition/effect syntax is documented in `Core/Script.cs`.
- `Assets/_Project/Scripts/Runtime/` — Unity layer: `GameBootstrap` (spawns everything at startup, no scene wiring),
  `GameView` (IMGUI renderer for every screen, virtual 1920x1080), `AudioDirector`, `UnityPlatform` (saves in
  persistentDataPath with .bak), `SteamService` (no-op unless STEAMWORKS_NET defined).
- `Assets/_Project/Scripts/Editor/BuildScript.cs` — menu **Hikari Forge → Build Windows (x64)**.
- `Playtest/` — Blazor WebAssembly front end over the same Core + content, **internal testing only** (players get the
  game only through the launcher/installer). `Tools/publish_playtest.sh` builds it.

## Commands (cloud container, no Unity)
- Install .NET: `apt-get install -y dotnet-sdk-8.0`
- Tests: `cd Tools/CoreTests && dotnet test` (content validation, saves, all 15 endings via AutoPlayer, endless).
- Unity compile check: needs Unity reference DLLs in a folder → `cd Tools/CompileCheck && dotnet build -p:UnityRefs=<dir>`
  (NuGet `unityengine.modules` 2021.3.33 + `unity3d.sdk` UnityEditor.dll were used; not committed).
- Playtest: `Tools/publish_playtest.sh <outdir>`; artifact host reserves `_` paths, so `_framework` → `framework`.
- Art: `python3 Tools/ArtGen/environments.py Assets/_Project/Resources/Art` (placeholders) then
  `python3 Tools/ArtGen/import_user_art.py .` (studio art from `Art/Source`, overrides placeholders).
- Audio: `python3 Tools/AudioGen/audiogen.py Assets/_Project/Resources/Audio [music|sfx]` (music takes ~7 min).

## Rules
- Nights must take about 10 real minutes (adaptive clock in `ShiftContext.PacedIdleRate`; `NightLengthTests` guard it).
- 7 nights per week, 15 endings, Endless Mode unlocks only after all 15.
- Studio art in `Art/Source` is approved by the owner and wins over generated art.
- Do not recreate the Hikari Forge logos; intro uses `Resources/Branding/hf_emblem.png` / `hf_wordmark.png` when supplied.
- Never claim Unity builds/tests ran unless they did — this container has no Unity.
- Branch: `claude/new-game-project-rscf4z`.
