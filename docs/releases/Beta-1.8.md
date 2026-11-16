## Beta 1.8

Update on top of Beta 1.7 (Avalonia UI, aim/fire triggers, Circle preset and Loop, RC-Style and Low-Pass filtering, Input capture dropdown, lag fix for the filtering loop).

### New: Filter rate (experimental)
- A **Filter rate** dropdown in the right stick filtering card sets how often the output runs while the filtering is active:
  - **2 ms (500/s)**: the default, same as Beta 1.7
  - **4 ms (250/s)**, **8 ms (125/s)**, **16 ms (62/s)**
- **Why:** some games switch between controller and mouse/keyboard whenever the virtual controller produces events, and that flipping only happens while the filtering is on (even with a very small slider value, the Xbox 360 type or Filter Button Hold). A lower rate means fewer controller events for the game to see, and less CPU. This is a test knob: it may not stop the flipping in every game, so please tell me what you see.
- RC-Style flips once per report, so its swings per second follow the rate (500, 250, 125 or 62 reports per second). The Low-Pass soft wiggle is time-based, so its wave is the same at any rate; for a small N, whose wave is only 16 ms x N long, the rate is automatically kept fast enough to sample it properly.
- The choice is saved per profile (`RightStickJitterRateMs`); profiles without it load as 2 ms.

### If the game keeps switching between controller and mouse
- The most reliable fix is in the game: set its input device to **Controller** (or Keyboard & Mouse) instead of Automatic.
- Otherwise try a lower filter rate, and set the **Filter Button** to Hold so the filtering only runs while you aim or fire.
- Details and why it happens: `docs/AIM_TRIGGER.md` (sections "Filter rate" and "Game switching between controller and mouse/keyboard").

### Unchanged
- The filter algorithms and maths, aim then fire, Hold/Toggle, Loop, Circle preset, Center Radius, Input capture (Low-Level Hooks / Raw Input), key bindings and turbo, and the lag fixes from Beta 1.7.
- Profiles: one new optional field; existing profiles and `patterns/` files load as before.

### Notes
- Tested with simulated controller output (report counts at each rate, filter shape at 16 ms, defaults and profile round-trip); not yet confirmed in a real game.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks.
