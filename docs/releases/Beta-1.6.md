## Beta 1.6

Update on top of Beta 1.5 (Avalonia UI, aim/fire triggers, Circle preset and Loop, filter algorithms, Input capture dropdown).

### Right stick filtering: Exponential Low-Pass now wiggles
- **Why:** a pure low-pass can only ease a value that is changing. With a fixed Base drift and the recoil pattern off, its input never changes, so on Windows it showed no effect while RC-Style visibly shook the stick.
- **Soft wiggle (right stick Y):** Exponential Low-Pass now also sways Y smoothly around its target by **+/- N counts** (the filtering slider value; `-12` = +/-12 on PS4, scaled to 16 bits on Xbox 360). It is a rounded wave rather than RC-Style's hard flip, centered on the target with no directional bias. One wave takes **16 ms x N**: `-12` is 192 ms (about 5 per second) and `-50` is 0.8 s, so a larger N gives a bigger, slower, rounder wave. It stays inside the stick range near the limits.
- **Smoothing is still there (X and Y):** recoil steps, the pattern starting and stopping, and Base drift changes glide to their new value instead of jumping, with time constant **4 ms x N** (`-50` = 200 ms).
- It runs on the same fast ~1 ms output loop as RC-Style, so it stays accurate even at small N.
- Gating, reset behavior and everything else about the filter are unchanged: the Filter Button and Hold/Toggle still turn it on and off, and the filter state still resets when you switch algorithm, close the gate, disengage or reconnect.

### Unchanged
- RC-Style Micro-Oscillation, the Input capture dropdown (Low-Level Hooks / Raw Input), aim then fire, Loop and the Circle preset, Center Radius, key bindings and turbo.
- Profiles: no new fields; existing profiles and `patterns/` files load as before.

### Notes
- This was checked with simulated tests (wiggle stays within +/-N, no bias, about one wave per 16 ms x N, changes ease in and out) but not yet on every controller; please tell me if the size or speed of the wave feels off. The constants are `SoftWigglePeriodMsPerStep` and `SmoothingTauMs` in `StickMath`.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks (aim, fire and filter buttons won't trigger).
- Details: `docs/AIM_TRIGGER.md`.
