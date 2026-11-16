## Beta 2.7

### New default mapping (PS4, keyboard + mouse)
The default profile is now a ready-to-play layout instead of "nothing bound":

- Cross = Space, Circle = Ctrl, Square = R, Triangle = E, L1 = Q, R1 = Mouse 4 (side), L2 = Right Mouse Button, R2 = Left Mouse Button, L3 = Shift, R3 = Mouse 3 (middle), D-Pad Up / Down / Left / Right = 1 / 2 / 3 / 4, Options = Esc, Share = Tab, PS = Home, Touchpad click = G.
- Left stick Up / Down / Left / Right = W / S / A / D.
- Right stick = mouse movement (mouse look is on by default).

Nothing else is bound. The whole table, the file format and the import / export workflow are in [docs/MAPPINGS.md](../MAPPINGS.md).

### New actions
- **Touchpad click** (DS4 only; ignored on Xbox 360) and four **left stick direction** keys. Opposite keys cancel, diagonals are normalized, and a left stick value from the Network API replaces the keys while it is set. Both appear in the controller monitor.
- **Home** is a named key.

### Profiles
- Easier to edit: bindings are in a `Mappings` object (`Input`, `Kind`, `Turbo`, `TurboHz` per action). `Kind` says whether an input is a keyboard key, a mouse button or a wheel notch.
- **Import...**, **Export...** and **Folder** buttons next to the profile box. Import checks the file (size, JSON, unknown inputs) and reports what it cleared.
- The PC to Controller tab is now three cards: Buttons (typed pickers grouped Keyboard / Mouse buttons / Mouse wheel, with a tag showing the kind), Left stick (keyboard only) and Right stick (mouse movement, with the mouse-look settings moved here).
- Older profiles still load (their `KeyBindings` / `TurboEnabled` / `TurboHz` are read), and are written in the new layout when saved. An older build cannot read bindings from a profile saved by this one.

### Upgrading
`profiles/Default.json` is replaced once with the new default; your previous file is kept as `Default.json.bak` (or `.bak2`, ...). Your own profiles are not touched.

### Known
- Not yet run on Windows hardware; the build and unit tests run on CI.
- Mouse Left / Right are bound to the triggers by default, so every click registers on the pad while it is connected; input blocking can swallow them in the game.
