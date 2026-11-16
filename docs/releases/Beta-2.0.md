## Beta 2.0

Update on top of Beta 1.9 (Avalonia UI, aim/fire triggers, Circle preset and Loop, RC-Style and Low-Pass filtering with a Filter rate option, Input capture dropdown, lag fix for the filtering loop, sideways Left/Right drift).

### Base drift: Down / Up / Left / Right
- **Center is now Down**, and **Up** is new. Pull direction has four options: **Down, Up, Left, Right**.
- All four use the same method: a straight pull from the center along **one axis**, with no second axis. The Base drift slider (0-100) sets how far. At 25% on PS4: Down = X 128 / Y 160, Up = X 128 / Y 96, Left = X 96 / Y 128, Right = X 160 / Y 128.
- **Xbox 360 fix:** Down now means screen-down on both controller types. Xbox 360 treats positive Y as up, so earlier betas pulled **up** when Xbox 360 was selected; the drift's Y sign is now flipped for Xbox so Down is really down. (The recoil pattern's Y values are unchanged.)
- The status text and the bottom label of the Base drift card follow the direction ("Base downward drift", "Base upward drift", "Base drift: N% to the left/right").
- **Old profiles:** a profile that says `Center` loads as **Down**; new saves store `Down` / `Up` / `Left` / `Right`.

### Default.json now ships inside the app
- The default profile is a JSON file embedded in the app (`Defaults/Default.json`): PS4, Base drift 32% Down, right stick filtering -10 with the Exponential Low-Pass algorithm on Right click (Hold), recoil pattern off, wheel disengage 500 ms with Tab and E.
- **First run:** the app writes it to `profiles/Default.json`, so "Default" appears in the profile list. An existing file is never overwritten.
- **On launch** the app starts with `profiles/Default.json` if it exists and is valid, otherwise with the shipped default (a missing or corrupt file falls back to it). So whatever you save as "Default" is what you get when you open the app. Delete `profiles/Default.json` to go back to the shipped one.
- Older profile files that lack a field still load with the original behavior; they don't gain drift or filtering.

### Unchanged
- Filter algorithms and maths, Filter rate, aim then fire, Hold/Toggle, Loop, Circle preset, Center Radius, Input capture (Low-Level Hooks / Raw Input), key bindings and turbo, and the lag fixes from Beta 1.7.

### Notes
- Tested with simulated controller output (every direction on DS4 and Xbox, drift 0 and 100, filtering and recoil on top, the shipped default against your saved profile, first-run seeding, startup and corrupt-file cases); please check the drift directions in the Windows controller test screen (`joy.cpl`) and in game, especially with Xbox 360 selected.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks.
- Details: `docs/AIM_TRIGGER.md` (sections "Base drift pull direction" and "Default profile").
