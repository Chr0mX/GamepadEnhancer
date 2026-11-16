## Beta 1.9

Update on top of Beta 1.8 (Avalonia UI, aim/fire triggers, Circle preset and Loop, RC-Style and Low-Pass filtering with a Filter rate option, Input capture dropdown, lag fix for the filtering loop).

### Changed: Base drift pull direction Left / Right is now sideways only
- **Before:** Pull direction **Left** or **Right** was applied together with the downward drift, so the stick went from the center to the **bottom-left or bottom-right** corner.
- **Now:**
  - **Center**: straight down (as before).
  - **Left**: straight **left** from the center by the drift %, with no downward part.
  - **Right**: straight **right** from the center by the drift %, with no downward part.
- The Base drift slider (0-100) still sets how far the stick moves. At 25% on PS4, Left gives X 96 / Y 128, Right gives X 160 / Y 128 and Center gives X 128 / Y 160 (Xbox 360: -8192 / +8192 on the active axis, 0 on the other).
- The recoil pattern still replaces the drift while it fires, and the RC-Style / Low-Pass filtering still rides on top (with Left or Right the Y swing is around neutral).
- **Profiles:** saved profiles keep their `BasePullDirection` (Left / Center / Right), so any profile using Left or Right will now move sideways instead of diagonally. If you want the old diagonal drift, say so and I can add it back as separate options.
- The status text and the bottom label of the Base drift card follow the direction: for Left/Right they read "Base drift: N% to the left/right" and "More drift"; for Center they are unchanged.

### Unchanged
- Filter algorithms and maths, Filter rate, aim then fire, Hold/Toggle, Loop, Circle preset, Center Radius, Input capture (Low-Level Hooks / Raw Input), key bindings and turbo, and the lag fixes from Beta 1.7.
- No new profile fields; existing profiles and `patterns/` files load as before.

### Notes
- Tested with simulated controller output (DS4 and Xbox values for each direction, drift 0 and 100, filtering on, recoil override); please check how it looks in the Windows controller test screen (`joy.cpl`) and in game.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks.
