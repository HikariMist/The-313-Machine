# Known issues

- Unity layer has never run; expect first-run fixes. The game renders with IMGUI on the built-in pipeline; URP was
  removed from the manifest because nothing uses it and a version mismatch could block the first import.
- `ProjectVersion.txt` says 6000.3.0f1; Unity Hub may offer to open with the installed 6.3 patch — accept.
- Approved Hikari Forge logo files not supplied: intro shows a plain text studio credit as a placeholder.
- Remaining portraits (Del, Crane, nurse, officer, Hale, etc.), backgrounds, product icons and scare plates are
  procedural placeholders.
- Hard stinger peaks at about -2 dBFS after Vorbis encoding (target -6); playback is scaled down in-game.
- Campaign length (2–3 h target) unverified by a human playthrough.
- Hosted playtest link not yet confirmed working by the owner.
- On-disk name is "The 313 Machine" (Windows forbids ":"); the window title bar therefore reads "The 313 Machine".
- Launcher and installer were tested under Wine/Xvfb, not on real Windows. The launcher is unsigned (SmartScreen may warn).
- Launcher update check needs a real HTTPS manifest URL in launcher.json; until then it opens the store page.
- Distribution is launcher-only (installer or portable ZIP). `Playtest/` (browser build) is an internal testing tool
  and is not shipped to players.
- Nights are paced to about 10 real minutes (owner's request), so a first full week takes roughly 65–80 minutes plus
  replays for other endings.
