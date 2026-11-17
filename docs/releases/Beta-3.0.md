## Beta 3.0

### Game profiles
- New **Game** selector on the top bar. Each game has its own built-in defaults and its own saved customizations; switching games saves the current one and loads the other, and one game's changes never touch another's.
- **Apex Legends** loads the Apex keyboard + mouse layout and shows what Apex's default controller preset does on every button.
- **Call of Duty: Warzone** is listed, but its native layout could not be verified from a real source yet, so it ships no labels and no bindings (it uses your global default) until it is transcribed.
- Unsupported games use your global `profiles/Default.json`. **Restore game defaults** resets only the selected game. Priority: your saved customization, then the built-in default, then the global fallback. See [docs/GAMES.md](../GAMES.md).
- The Apex on/off toggle is replaced by the game selector; installs from before this start on Apex, as they did.

### Interactive controller diagram
- The PC to Controller tab opens with a drawn DualShock 4 or Xbox 360 (it follows the selected output type): every button, bumper, trigger, stick click, stick direction, the right stick (mouse movement) and a rear view with four paddles. Each control shows its current mapping; click it (or use the arrow keys) to change it. Pressed controls light up, remapped ones flash, and it scales with the window. The previous list view is kept under *All bindings*.

### Rear paddles
- Four independently mapped paddles (Left/Right Upper/Lower): each can be enabled or disabled and press a virtual controller button, a keyboard key or a mouse button. Saved in profiles (and so per game).
- Read from a physical controller through Windows.Gaming.Input once per engine tick, with disconnect/reconnect handling, no repeated activation and duplicate-target protection. Hardware capability is detected (Xbox Elite pads are known to have four; others only when a paddle is seen). **There was no earlier paddle code to restore**; this is a new input source. Limits: [docs/PADDLES.md](../PADDLES.md).

### Under the hood
- The app now targets `net8.0-windows10.0.19041.0` (needed for Windows.Gaming.Input).
- Profiles gained an optional `Paddles` block; older profiles load unchanged with paddles off.

### Known
- Not yet run on Windows hardware: the paddle reading, the Windows API calls and the drawing are untested on a real controller. Whether paddle readings arrive while a game has focus is not verified.
- Warzone's native layout is not verified; Apex's other presets and stick layouts are not modelled.
