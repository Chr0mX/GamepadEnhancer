## Beta 1.4

Update on top of Beta 1.3 (Avalonia UI, aim/fire triggers, Circle preset and Loop).

### Right stick filtering
- **New Filter Algorithm dropdown** in the filtering card, next to the Filter Button and Filter Mode (both unchanged and still gate whichever algorithm is selected):
  - **RC-Style Micro-Oscillation** (default): right stick **Y only**. The output now alternates between `target + N` and `target - N` on every report, so it is symmetric around the target with no directional bias. On PS4, `-10` swings 118 / 138 around 128 (Xbox 360: the same size scaled to 16 bits). Near the stick limits the swing shrinks so both sides stay equal. Still runs on the ~1 ms output loop.
  - **Exponential Low-Pass Filter**: right stick **X and Y**. Smooths sudden changes in the final output (base drift, recoil steps, Center Radius). Time constant = 4 ms x N (`-10` = 40 ms, `-50` = 200 ms); frame-rate independent and settles exactly on the target.
- The slider (0 to -50) controls the amplitude for oscillation and the smoothing strength for low-pass; the caption and note under it follow the selected algorithm.
- The filter state resets when you switch algorithm, when the Filter Button gate closes, on a disengage and on reconnect.

### Changed behavior
- **Oscillation is no longer biased.** Earlier betas flipped between the target and `target - N` (centered N/2 below it). The new swing is `+/- N`, so for the same slider value the total swing is twice as large. Lower the slider if it feels too strong.

### Profiles
- New field `RightStickJitterAlgorithm` (`Oscillation` or `LowPass`). Profiles without it load as Oscillation.

### Notes
- If your game runs as administrator, run this app as administrator too, otherwise Windows blocks the mouse-button hooks (aim, fire and filter buttons won't trigger).
- Details: `docs/AIM_TRIGGER.md`.
