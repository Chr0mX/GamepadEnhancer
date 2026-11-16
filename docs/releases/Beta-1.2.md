## Beta 1.2

First release built on the new **Avalonia UI** (replaces WinForms), with new aim/fire and stick filtering controls.
Same ViGEmBus integration; profiles and pattern files from earlier versions still load.

### UI
- Migrated from WinForms to Avalonia. Layout adapts to window size and Windows scaling: the header wraps, cards re-flow into one or two columns, no horizontal scrollbars.
- Three tabs: **Controller**, **PC to Controller** (now its own tab) and **Recoil Pattern**.
- Controller tab: Base drift is unchanged (0-100 with Left/Center/Right pull); **Right stick filtering** has its own vertical slider card next to it; Disengage is on the right.

### Right stick filtering (new)
- Slider from 0 to -50. Negative values flip the right-stick Y value on every report, hundreds of times per second (for example -10 on PS4 alternates 127 and 117). Right stick only; base drift is not affected.
- Runs on its own ~1 ms output thread while active (10 ms otherwise).
- **Filter Button** (Off / Left / Right / Middle) and **Filter Mode** (Hold / Toggle). Off = always on, as before.

### Recoil pattern
- **Aim, then fire:** **Aim Trigger Key** (Right / Middle, or Off / Left) with **Trigger Mode** Hold or Toggle sets aiming; the pattern plays only while **Left click** is held. Off or Left = Left click alone fires.
- **Start Delay (ms):** wait before the first step, counted from when aiming and firing are both on.
- **Center Radius (%)** with a **Center Radius Mode** switch: *New: dead-zone lift* (default), or the *Old* centered **Shake** / **Circle** wobble from the previous jitter feature (radius 0-45).
- Safety net: a missed mouse-button release stops the pattern after about 30 ms.

### Profiles
- New fields: `RightStickJitter`, `RightStickJitterButton`, `RightStickJitterMode`, `RecoilTriggerMode`, `RecoilStartDelayMs`, `RecoilCenterRadiusPercent`, `RecoilCenterRadiusMode`. Missing fields fall back to the old behavior.
- A profile from an early 1.1 beta that stored a negative base drift loads it as right stick filtering.
- Keyboard keys as the recoil trigger are no longer offered; such profiles load as Off.

### Notes
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks (Hold/Toggle buttons won't trigger).
- Details: `docs/AIM_TRIGGER.md` and `docs/CENTER_RADIUS.md`.
