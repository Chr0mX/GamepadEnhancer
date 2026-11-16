## Beta 1.3

Small update on top of Beta 1.2 (Avalonia UI, aim/fire triggers, right stick filtering, Center Radius modes).

### Recoil pattern
- **Circle preset replaces S-Pattern.** The preset button now loads a tight circular motion: 16 steps, radius 8 % around the stick center, 17.3 ms per step. The old S-shaped preset is removed. Edit the steps or the pattern text to change the size or speed.
- **New option: Loop pattern while firing.** Normally the pattern plays its steps once and holds the last step. With Loop on it restarts from step 1 after the last step and keeps going until you release Left click. Combine it with the Circle preset for a continuous small circular motion. Start Delay still only applies before the first pass.

### Profiles
- New field `RecoilLoop` (default off), so existing profiles behave exactly as before.

### Notes
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks (aim, fire and filter buttons won't trigger).
- Details: `docs/AIM_TRIGGER.md` and `docs/CENTER_RADIUS.md`.
