# Development status

_Last updated: session 1._

| Area | Status |
|---|---|
| Core simulation (shift, encounters, handbook, cameras, panel, knock/blackout/wrong-time events, horror director, instability) | Done, unit tested |
| Content: 7 nights, 52 campaign encounters, 14 endless templates, 26 clues, handbook A–G with forged pages | Done (needs human playtest for pacing) |
| 15 endings + achievements, campaign loop (cycles), Endless Mode unlocked by all 15 | Done, every ending reached by automated playthrough |
| Save system (checksum, backup, corrupt-save fallback), profile, settings | Done in Core; Unity file IO written, not run |
| Browser playtest (Blazor WASM) | Internal testing tool only — not distributed (players use the launcher) |
| Unity runtime (IMGUI renderer, audio, bootstrap, Steam hooks, build script) | Written and compile-checked; **never run in Unity** |
| Art | Studio art for title, Drenched Man, Smiling Woman, Hat Stranger (Collector), Child (Lily), Corrupted Worker (Mara/mimic), Faceless Passenger (the Attendant). Everything else is procedural placeholder |
| Music (15 tracks) / SFX (74) | Procedural originals, generated; not listened to by a human yet |
| Launcher | Built and run on real Windows (owner PC) |
| Studio intro (launcher) | `Launcher/StudioIntroPlayer.cs`: plays `Assets/StreamingAssets/Hikari_Forge_Studios_Startup_Intro.mp4` (linked into `Release/launcher/Intro`) borderless, skippable (any key / click, not Alt/Win), fades to black into the launcher; setting "Play Studio Intro on Startup"; missing/corrupt video logs to `%APPDATA%\Hikari Forge Studios\The 313 Machine\launcher.log` and opens the launcher. Tested on Windows: natural end, Esc/Enter/Space/click skip, Alt+Tab, disabled/re-enabled, missing and corrupt file. Launcher passes `-hf-studio-intro-shown` so the game skips its own copy |
| Studio intro (game, direct launch) | Unity plays the same MP4 from StreamingAssets, then the title; verified in a Windows build |
| Installer | Script compiles; install/uninstall verified under Wine with a stand-in game |
| Store kits, marketing, licenses | Written (`STEAM_RELEASE.md`, `ITCH_RELEASE.md`, `Docs/Marketing`, `LICENSES.md`) |

| Night pacing | Adaptive clock: nights land at ~10 real minutes; verified with simulated fast/typical/slow players |
| Title screen | Animated rain, splashes, mist and rare lightning (Unity) |
