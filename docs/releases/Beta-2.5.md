## Beta 2.5

### Added: live controller monitor
- New **Monitor** tab: an Xbox 360 or DualShock 4 drawn with that pad's own layout, with analog sticks, analog triggers, button lighting and a button history. A view switch shows the **physical input**, the **processed input**, or the **final output** sent to the virtual pad. Size, refresh rate (30-144 Hz), compact mode, pop-out window, always on top and a transparent overlay mode are saved in `settings.json`.
- It draws on its own timer from lock-free snapshots; the controller output never waits on it, and snapshots are only built while a monitor is showing. Details: [docs/MONITOR.md](../MONITOR.md).

### Added: output type switching
- Changing Xbox 360 / PS4 while connected replaces the virtual pad in place (old device disconnected first); the header shows the active output. Loading a profile no longer touches a live pad's type.
- New **Guide / PS** action (Xbox Guide, PS button).

### Added: input blocking
- Separate **Block keyboard** / **Block mouse** switches (on by default) and a **Restore input now** button.

### Known
- Written without a compiler or Windows hardware; build, run the unit tests and try both pad types in a game before releasing. The Guide / PS button uses `Xbox360Button.Guide` and `DualShock4SpecialButton.Ps` from `Nefarius.ViGEm.Client` 1.21.
