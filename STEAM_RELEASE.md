# Steam release kit

Steam installs and launches `The313Machine.exe` directly; the custom launcher and installer are **not** used on Steam.

## App setup (owner)
1. Steamworks partner account + app credit ($100) → you receive an App ID. Do not commit it; put it in `steam_appid.txt` locally only.
2. Achievements (Steamworks → Stats & Achievements), API names must match exactly:
   `ENDING_TRUTH, ENDING_ESCAPE, ENDING_REPLACEMENT, ENDING_DEBT, ENDING_EMPTY_SEAT, ENDING_LAST_PASSENGER, ENDING_DRIVER,
   ENDING_NAMELESS, ENDING_WRONG_MINUTE, ENDING_PAID_IN_FULL, ENDING_MARAS_SHIFT, ENDING_COMPANY_MAN, ENDING_TOGETHER,
   ENDING_ONES_WHO_LEFT, ENDING_OFF_THE_CLOCK, ENDLESS_UNLOCKED`
3. Steam Cloud: simplest is **Auto-Cloud**, root `WinAppDataLocalLow`, path `Hikari Forge Studios/The 313 Machine/Saves`, pattern `*.json`.
4. Optional code integration: add Steamworks.NET to the Unity project and the scripting define `STEAMWORKS_NET`;
   `SteamService` then unlocks achievements and mirrors saves. Without it the game runs fine, no Steam required.

## SteamPipe upload
```
steamcmd +login <builder_account> +run_app_build ..\scripts\app_build_<APPID>.vdf +quit
```
`app_build` content root = `Builds/Windows/`, launch option = `The313Machine.exe`. Never commit credentials or `config.vdf`.

## Store page
- **Name:** The 3:13 Machine
- **Short description (≤300 chars):** You run a lonely roadside vending machine on the night shift. At exactly 3:13 AM, customers arrive who shouldn't exist. Follow the handbook, watch the cameras, check every reflection — and decide who gets served, who gets refused, and who gets to go home.
- **Long description:** see `Docs/Marketing/steam_description.md`.
- **Tags:** Psychological Horror, Horror, Mystery, Management, Atmospheric, Story Rich, Multiple Endings, Choices Matter, Dark, Singleplayer, 2D, Indie
- **Price:** $5.99 USD (launch week: 15% off → $5.09; Steam requires the base price to be live 30 days before later discounts)
- **System requirements (minimum, to be confirmed after the Unity build is profiled):** Windows 10 64-bit; 2 GHz dual core; 4 GB RAM; DirectX 11 GPU with 1 GB; 500 MB disk. No internet required.
- **Capsules (Steam sizes):** header 920×430, small 462×174, main 1232×706, vertical 748×896, library hero 3840×1240, library logo 1280×720 (transparent). Build from the title key art; keep "3:13" legible at small sizes.
- **Screenshots:** at least 5, 1920×1080, from the real game only (see `Docs/Marketing/screenshot_guide.md`).
- **Trailer:** see `Docs/Marketing/trailer_storyboard.md`.
- **Content descriptors:** frequent violence/gore: none shown; horror themes, death of characters (off-screen), jump scares (with reduction options).
