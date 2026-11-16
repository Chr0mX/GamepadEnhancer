## Beta 2.1

Update on top of Beta 2.0 (Avalonia UI, aim/fire triggers, Circle preset and Loop, RC-Style and Low-Pass filtering with a Filter rate option, Input capture dropdown, lag fix for the filtering loop, Down/Up/Left/Right pull direction, shipped Default.json).

### Fixed: the Xbox 360 flip in Base drift is removed
- **What Beta 2.0 did:** to make Down point down on Xbox 360 too, it flipped the sign of the drift's Y value when Xbox 360 was selected. That made Up and Down work differently from Left and Right.
- **Now:** Up and Down use exactly the same form as Left and Right, with the plain sign of the drift percent on every controller type:

  | Pull direction | Stick value (d = Base drift %) |
  |---|---|
  | Left | (-d, 0) |
  | Right | (d, 0) |
  | Up | (0, -d) |
  | Down | (0, d) |

- At 25% on PS4: Down = X 128 / Y 160, Up = X 128 / Y 96, Left = X 96 / Y 128, Right = X 160 / Y 128. On Xbox 360 the values are +8192 (Down), -8192 (Up), -8192 (Left) and +8192 (Right) on the active axis, 0 on the other.
- **What this means on Xbox 360:** Y is the controller's native Y, and Xbox 360 treats positive Y as **up**. So with the Xbox 360 controller type, **Down pulls up and Up pulls down**. (This is how it behaved before Beta 2.0.) If you use Xbox 360, choose the opposite direction. PS4 is unaffected.
- Left, Right, the recoil pattern, the filtering, Default.json and old profiles (`Center` still loads as Down) are unchanged.

### Unchanged
- Filter algorithms and maths, Filter rate, aim then fire, Hold/Toggle, Loop, Circle preset, Center Radius, Input capture (Low-Level Hooks / Raw Input), key bindings and turbo, the lag fixes from Beta 1.7, and the Default.json behavior from Beta 2.0.

### Notes
- Tested with simulated controller output (all four directions on DS4 and Xbox, drift 0 and 100, filtering and recoil on top); please check the drift directions in the Windows controller test screen (`joy.cpl`) and in game, especially with Xbox 360 selected.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks.
- Details: `docs/AIM_TRIGGER.md` (section "Base drift pull direction").
