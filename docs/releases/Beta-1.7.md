## Beta 1.7

Update on top of Beta 1.6 (Avalonia UI, aim/fire triggers, Circle preset and Loop, RC-Style and Low-Pass filtering, Input capture dropdown).

### Fix: PC lag with the filtering on
Turning on the right stick filtering (RC-Style or Low-Pass, slider below 0) could lag the whole PC. Two causes in the output loop:
- The fast loop (1 ms) busy-waited on an above-normal-priority thread, using about **one full CPU core**, and held the system timer at 1 ms.
- Each report was sent to the controller driver while holding the lock that the mouse and keyboard hooks need, so a click or key press could stall behind the driver call. The hooks sit in the system-wide input path.

What changed:
- The loop now **sleeps** between ticks instead of spinning, on a normal-priority thread.
- While filtering is active the loop runs every **2 ms (about 500 reports per second)** instead of 1 ms.
- The driver call now happens **after the lock is released**, so the hooks never wait on it.
- A report is sent **only when something changed** (stick, buttons or triggers), plus a heartbeat about every 250 ms. A fixed Base drift with no filter goes from about 100 reports per second to about 4.

Measured in a simulation with a 0.5 ms driver call (not yet on a real PC): filtering CPU use went from about 102% to about 30% of one core (about 25 points of that is the simulated driver delay), a hook call's wait from about 131-178 microseconds to about 3-4, and the worst-case wait from 1.8-15 ms to under 0.5 ms.

### Behavior notes
- RC-Style now flips about 500 times per second (250 full swings) instead of about 1000. It is still "hundreds per second"; the rate is one constant if you want it changed.
- The Low-Pass soft wiggle timing is unchanged (it is based on the clock, not the tick rate).
- **Game switching between controller and mouse/keyboard:** this can't be fully fixed. While the filtering is on, the virtual stick changes constantly, so a game with automatic input detection sees controller activity whenever you also use the mouse. Use the game's input-device lock (Controller or Keyboard & Mouse instead of Automatic), or set the **Filter Button** to Hold on Left/Right/Middle so the filtering only runs while you aim or fire.

### Unchanged
- Filter algorithms and maths, aim then fire, Hold/Toggle, Loop, Circle preset, Center Radius, Input capture (Low-Level Hooks / Raw Input), key bindings and turbo.
- Profiles: no changes; existing profiles and `patterns/` files load as before.

### Notes
- Please tell me how the lag and the input switching feel on your PC with the filtering on.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks.
- Details: `docs/AIM_TRIGGER.md` (section "Output rate, CPU and lag").
