## Beta 3.1

### Selectable configs
- The top bar now has a **Config** list: **Default**, **Apex Legends** and **Call of Duty: Warzone** (not verified yet). Picking one loads it; each keeps its own saved changes, and **Restore config defaults** resets only the selected one.
- The Apex layout is now named **Apex Legends**.

### New Default config (PS4 to keyboard + mouse)
Left stick = `W A S D`, right stick = mouse movement, L3 = `Shift`, R3 = `V`, R2 = Left Mouse, L2 = Right Mouse, R1 = `G`, L1 = `Q`, Cross = `Space`, Circle = `C`, Square = `R`, Triangle = mouse wheel up, D-Pad = `1 2 3 4`, Touchpad = `M`, Options = `Esc`, Share = `F12`. Where a binding had alternatives the first was used. Full table: [docs/MAPPINGS.md](../MAPPINGS.md).
- It is also the layout for any game that has none of its own.

### Upgrading
`profiles/Default.json` (the plain profile file) is replaced once with the new default; your previous file is kept as `Default.json.bak` (or `.bak2`, ...). Customizations you saved per game are not touched.

### Known
- Not yet run on Windows hardware; the build and unit tests run on CI. Warzone's native layout is still not verified.
- The Action / RPG trigger and bumper variant is not shipped as a separate config.
