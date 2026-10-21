# itch.io closed beta — THE 3:13 MACHINE BETA v0.1.0

Use a **new, separate project** (not the ONE MORE BAG page). Nothing here requires charging testers.

## Exact steps for the owner (about 10 minutes)
1. itch.io → **Upload new project**.
2. Title: `The 3:13 Machine (Closed Beta)` · URL: `the-313-machine` · Kind: **Downloadable**.
3. Pricing: **$0 or donate → "No payments"**.
4. Uploads: `Release/The313Machine_Beta_v0.1.0_Windows.zip` (tick **Windows**) and, if built,
   `Release/The313Machine_Beta_Setup_v0.1.0.exe` (tick **Windows**). Set the version to `0.1.0`.
5. Description: paste the block below. Cover image: crop `Art/Source/title_keyart.png` to 630×500.
6. **Visibility & access → Restricted** ("Only owners & people with download keys can access").
   Save as **Draft** first, check the page, then switch to Restricted. **Do not choose Public.**
7. **Distribute → Download keys → Generate** one key per tester (Distribute tab). Send each tester their own
   key link. Never share your account login.
8. Feedback: create a free Google Form with the 10 questions in
   `BuildInfo.FeedbackQuestions` (also listed below), then put its link in
   `Assets/_Project/Scripts/Core/GameSession.cs` → `BuildInfo.FeedbackUrl` before the next build,
   and in the itch description. Until then testers reply via itch comments / your Discord.

I (Claude) cannot log in to itch.io from here; steps 1–7 need your account.

## Page description
**THE 3:13 MACHINE — Closed Beta v0.1.0**

You work the night shift at a lonely roadside vending machine. At exactly 3:13 AM, customers arrive who shouldn't exist. Follow the handbook, check the cameras, watch every reflection, and decide who gets served.

**This beta includes:** a full week of 7 night shifts (about 10 minutes each), 15 different endings, 3-level upgrades, and Endless Mode, unlocked once you find all 15 endings. Expect placeholder art for some characters and backgrounds.

**Install:** run the Setup .exe, or unzip the portable ZIP and run `The313MachineLauncher.exe`, then press PLAY. If SmartScreen warns you, click *More info → Run anyway*.
**Controls:** mouse · Space/Enter next line · H handbook · M cameras · E evidence · Esc pause.
**Saves:** `%USERPROFILE%\AppData\LocalLow\Hikari Forge Studios\The 313 Machine\Saves` (kept on uninstall).

**Please tell us:** did it start; how long you played; which night you reached; were the controls clear; were upgrade prices fair; were jumpscares too frequent, too rare or balanced; were they effective; did anything break; what you enjoyed most; what to improve. If something broke, attach `...\The 313 Machine\Player.log`.

## Update notes (v0.1.0)
First closed beta. Seven nights, 15 endings, endless mode, upgrade levels, paced scares, pause menu, feedback screen.
