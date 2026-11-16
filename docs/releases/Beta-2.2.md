## Beta 2.2

Update on top of Beta 2.1 (Avalonia UI, aim/fire triggers, Circle preset and Loop, RC-Style and Low-Pass filtering with a Filter rate option, Input capture dropdown, lag fix for the filtering loop, Down/Up/Left/Right pull direction built the same way, shipped Default.json).

### Changed: the filtering wiggle goes on the axis across the drift
- The right stick filtering (RC-Style Micro-Oscillation and the Low-Pass soft wiggle) now always shakes the axis **across** the drift, the reverse of the drift axis:

  | Pull direction | Where the stick sits | Where the wiggle is |
  |---|---|---|
  | Left / Right | X -d / +d | **Y**, around neutral |
  | Up / Down | Y -d / +d | **X**, around neutral |

- **Left and Right are unchanged** (drift on X, wiggle on Y). **Up and Down are the reverse:** the stick sits at Y ±d and shakes on X.
- At 25% drift on PS4 with filtering -12: Down = Y steady at 160 with X swinging 116 to 140 around 128; Left = X steady at 96 with Y swinging 116 to 140. On Xbox 360 the same applies (±N counts scaled to 16 bits).
- Both algorithms follow the rule. RC-Style swings by exactly ±N around the target; the Low-Pass wiggle is the soft rounded wave (one wave per 16 ms x N). The Low-Pass **smoothing** (time constant 4 ms x N) still applies to both X and Y.
- While the recoil pattern fires, the wiggle axis still follows the Pull direction setting. The wiggle stays inside the stick range near the limits.
- The old `Center` still counts as Down.
- **What changes for you:** the shipped default (Down drift 32%, Low-Pass -10, Filter Button Right) now wiggles on **X** instead of Y. If you preferred the old behavior, use Left or Right for the drift.
- The note under the filtering card describes the axis rule.

### Unchanged
- Pull direction values (Left `(-d, 0)`, Right `(d, 0)`, Up `(0, -d)`, Down `(0, d)`; on Xbox 360 positive Y is up, so Down pulls up there), filter algorithms and maths, Filter rate, aim then fire, Hold/Toggle, Loop, Circle preset, Center Radius, Input capture (Low-Level Hooks / Raw Input), key bindings and turbo, the lag fixes, and Default.json handling.

### Notes
- Tested with simulated controller output (all five direction names x both algorithms on DS4 and Xbox, recoil firing, the stick limit); please check the wiggle axis in the Windows controller test screen (`joy.cpl`) and in game.
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks.
- Details: `docs/AIM_TRIGGER.md` (section "Wiggle axis").
