# Beta readiness report — BETA v0.1.0 (2026-10-21)

## Verdict
**Content and systems are beta-ready and tested in simulation and in the browser. The Windows build compiled and
built successfully on the owner's PC (Unity 6000.6.3f1, first batch build, no errors) before tonight's balance
changes.** Release is blocked only on: rebuilding with tonight's changes, a short hands-on play test of the .exe, and
the owner's itch.io steps (ITCH_BETA.md).

## Playable in the beta
All 7 nights (52 encounters), 15 endings + achievements, cycle loop, Endless Mode. Nothing was cut.

## Implemented and verified (automated, 49 tests + browser runs)
| Area | Evidence |
|---|---|
| All 15 endings reachable | scripted 7-night playthroughs, one per ending |
| No softlocks in the campaign | every night of every ending route reaches sunrise/ending |
| Upgrades: 3 levels each, real effects, configurable prices (`Resources/Content/upgrades.json`) | tests: prices rise, effects change per level, cannot overspend/exceed max |
| Economy | careful/average/poor players × spread/focus/no-upgrade strategies all finish; first upgrade affordable after Night 1; nobody can max everything |
| Scare pacing | per-night budgets (1/1/2/2/3/3/4), spacing, no random scares during conversations, minimal setting removes sudden scares — tested on all 7 nights |
| Saves | round trip, tamper/garbage rejection, backup fallback (Unity), **v1→v2 migration keeps upgrades** |
| Browser playtest | Night 1 full run; Night 7 → Truth ending + achievement in UI, no page errors |
| Installer script | compiles (with and without launcher) under Wine; install/uninstall verified earlier (saves kept) |
| Unity build | **succeeded on owner's PC** (pre-balance build). Tonight's code compile-checked against Unity reference assemblies |

## Upgrade table (final beta values)
| Upgrade | Lv1 | Lv2 | Lv3 |
|---|---|---|---|
| Expanded Storage | $15 — slots 5 (base 4) | $35 — 6 | $65 — 8 |
| Faster Restocking | $15 — 8 min/slot (base 12) | $35 — 4 min, works while a customer waits | $65 — 2 min |
| Improved Lighting | $20 — reflection details, nerves −15% | $40 — −30% | $70 — −40%, power cuts don't hurt nerves |
| Enhanced Monitor (N2+) | $25 — CAM 4 + observations | $45 — anomaly alert on CAM 2 | $80 — cameras work in power cuts |
| Customer ID Tool (N3+) | $30 — basic scan | $50 — detailed scan | $85 — names relevant handbook sections |
| Emergency Power (N3+) | $25 — machine stays on, cuts −40% | $40 — −65% | $70 — −80% |
Wages $20→$30 per night plus sales. Typical: one upgrade level per night; focusing reaches Lv3 by Night 5–7.

## Not verified yet (needs the owner's PC, ~20 minutes)
- Hands-on play of the .exe after rebuilding (title, a night, save/continue, settings, intro video).
- Intro video: in the repo (`Assets/StreamingAssets/`), plays in the launcher and on direct game launches —
  verified on the owner's PC. Audio track detected, not yet listened to by a human.
- Launcher: run on the owner's PC (intro, skip, settings, fallbacks). PLAY from an installed copy not yet clicked.
- Human campaign length (2–3 h target) and scare frequency feel at human speed.

## Owner actions, in order
1. In the local Claude session (or by hand): pull the branch, run **`PACKAGE_BETA.bat`**.
2. Play 10 minutes of the built game; quit; Continue; check it resumes.
3. Follow **ITCH_BETA.md** (restricted project + download keys).
4. Optional: set `BuildInfo.FeedbackUrl` to a Google Form link and rebuild.
