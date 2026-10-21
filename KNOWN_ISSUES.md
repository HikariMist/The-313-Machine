# Known issues

- Unity layer has never run; expect first-run fixes. URP is listed in the manifest but the game renders with IMGUI
  and does not depend on URP features.
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
