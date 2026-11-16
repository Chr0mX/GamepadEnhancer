# Input blocking and mouse look

GamepadEnhancer can swallow the physical keyboard and mouse input that is mapped to the virtual controller while a game is focused, so the game sees only the pad. Mouse movement can also drive the right stick. Both live on the **Block & Mouse** tab.

## What gets blocked

Only inputs that are *mapped*:

- keys bound to a pad action on the **PC to Controller** tab,
- mouse buttons (`Mouse Left/Right/Middle/X1/X2`) and wheel notches (`Wheel Up/Down`) bound to a pad action,
- mouse movement, when **Mouse look** is on.

Everything else passes through, so you can still type, use other keys, and so on. The "disengage" keys (Tab, E, ...) and the aim/fire/filter trigger buttons are **not** blocked unless you also bind them to a pad action.

Blocking is active only while all of these are true: blocking is enabled, it is toggled on, the virtual controller is connected, Input capture is **Low-Level Hooks**, and the focused program is in your list. GamepadEnhancer's own window never counts, so you can always edit settings.

## Safety rules

- The **toggle / panic hotkey** (default Scroll Lock) pauses and resumes blocking and is itself never blocked.
- Input injected by other programs (SendInput, MAKCU, etc.) is never blocked.
- A key or button is released to the game exactly as it was pressed: if its press was swallowed, its release is swallowed too; a key that was already down when blocking began is released normally. No stuck keys.
- Closing the app removes the hooks; if it is killed, Windows removes them.
- Blocking is off by default. Raw Input can't swallow input, so selecting it is refused while blocking is on.

## Mouse look

Mouse movement is read with Raw Input (relative counts), so it works with either capture method. The output is added to the right stick on top of drift, filtering and recoil:

- **Sensitivity X/Y**: percent of stick per (mouse count per millisecond). A higher number reaches full tilt with a slower mouse.
- **Curve**: 1 = linear; above 1 gives finer control at low speed.
- **Game dead zone**: set it to the game's inner stick dead zone so small moves still register.
- **Release**: how long the last value is held with no movement before the stick recenters (smooths low-polling mice).
- **Invert Y**; Y is handled for you on Xbox (stick up is positive there).

Settings are stored per profile.

## Known risks / not verified on hardware

- The design relies on Raw Input still delivering movement while the low-level hook swallows the legacy mouse-move messages. This is believed to work, but it has not been confirmed on Windows from this repository's CI. If a setup stops delivering deltas, the fallback would be to recenter the cursor from the hook instead.
- Low-level hook callbacks must return quickly; Windows silently drops a hook that stalls. If input stops being blocked after a long pause, toggle blocking off and on.
- Games with anti-cheat may object to any input hook or virtual controller.
