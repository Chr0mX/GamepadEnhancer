# Aim Trigger Key, Trigger Mode and firing (Recoil Pattern tab)

The recoil pattern plays in two steps: **aim first, then fire**.

- **Fire button = Left click.** The pattern only plays while Left click is held.
- **Aim Trigger Key** = the button that means "I'm aiming" (usually Right click). The pattern
  does nothing until aiming is on.
- **Trigger Mode** says how the aim button counts:
  - **Hold**: aiming is on only while the aim button is held down.
  - **Toggle**: press the aim button once to turn aiming on, again to turn it off (releasing does nothing).

| Aim Trigger Key | What happens |
|-----------------|--------------|
| **Right** / **Middle** | Needs aiming on (per Trigger Mode), then Left click fires the pattern. |
| **Off** | No aim needed: Left click alone fires the pattern. Trigger Mode has no effect. |
| **Left** | Left click is both aim and fire, so Left click alone fires. Trigger Mode has no effect. |

## Details

- Pressing Left click while aiming is off does nothing; turning aiming on while Left click is
  already held starts the pattern at that moment.
- **Start Delay (ms)** counts from the moment both conditions are true (aiming on and Left click held).
- Releasing Left click, or (Hold) releasing the aim button, stops the pattern and resets it, so the
  next shot starts from step 1. In Toggle mode, aiming stays on until you press the aim button again.
- Changing the Aim Trigger Key or Trigger Mode clears any toggled-on aiming. So does unticking
  **Enabled** in Toggle mode.
- Safety net: if a button release is ever missed, the pattern stops after about 30 ms once the
  physical button is seen up (swapped left/right mouse buttons are handled).
- Keyboard trigger keys from older versions are not offered; profiles that used one load as Off.

Profile fields: `RecoilTriggerKey` (`Mouse Left` / `Mouse Right` / `Mouse Middle` / `None`),
`RecoilTriggerMode` (`Hold` / `Toggle`). Code: `Core/ControllerEngine.cs` (`OnMouseButton`, `Reevaluate`).

## Right stick filtering: algorithm, Filter Button and Filter Mode

The slider (0 to -50) sets the strength; **Filter Algorithm** picks what it does.

**Wiggle axis:** the wiggle always shakes the axis **across** the drift, the reverse of the drift axis:
- **Left / Right** drift on X (the stick sits at X -d or +d), so the wiggle is on **Y**, around neutral.
- **Up / Down** drift on Y (the stick sits at Y -d or +d), so the wiggle is on **X**, around neutral.
(The old "Center" counts as Down.) The recoil pattern replaces the drift while it fires, but the wiggle axis still follows the Pull
direction setting. The smoothing of the Low-Pass algorithm always applies to both X and Y.


- **RC-Style Micro-Oscillation** (default): one axis only (see "Wiggle axis" below). The output alternates every report between
  `target + N` and `target - N` (N = the slider magnitude, in DS4 counts; Xbox uses the same size scaled to 16 bits),
  so it is symmetric around the target with no directional bias. On DS4 the target is the real value (128 at
  rest): `-10` gives 118 / 138. Near the stick limit the swing shrinks so both sides stay equal. Runs on the ~2 ms
  output loop (about 500 reports per second, so 250 full swings per second). (Earlier betas flipped between the target and `target - N`, which was biased; that is gone.)
- **Exponential Low-Pass Filter**: smooths and wiggles. Two things happen with one filter (`out += alpha * (target - out)`,
  `alpha = 1 - exp(-dt / tau)`, `tau = 4 ms x N`, so `-10` = 40 ms, `-50` = 200 ms):
  1. **Soft wiggle (one axis, see "Wiggle axis" below):** a symmetric square wave is added to that axis and the same low-pass rounds it, so it sways
     smoothly around the target by `+/- N` counts (`-12` = +/-12 on PS4). One wave takes `16 ms x N` (`-12` = 192 ms,
     about 5 per second; `-50` = 0.8 s): larger N is a bigger, slower, rounder wave. Centered on the target, no bias.
     (A pure low-pass can't change a constant value, so without this a fixed Base drift showed no effect at all.)
  2. **Smoothing of changes (X and Y):** recoil steps, pattern start/stop and Base drift moves glide to their new value
     instead of jumping, with the same time constant.
  It is frame-rate independent, kept in floating point so the 8-bit DS4 scaling never stalls it, and it runs on the same
  ~2 ms loop as RC-style. Constants: `StickMath.SoftWigglePeriodMsPerStep` (16) and `SmoothingTauMs` (4 ms per step).
- Switching algorithm resets the filter state, and so does closing the gate, a disengage, or reconnecting; the
  low-pass seeds from the first target after a reset, so it never jumps from stale values.

The Filter Button / Filter Mode below gate whichever algorithm is selected:

The filtering has its own button and mode, separate from aim and fire:

- **Filter Button**: Off, Left, Right or Middle. **Off = always on** (the default), whenever the slider is below 0.
- **Filter Mode**: **Hold** = filtering only while that button is held; **Toggle** = press once to turn it on, again to turn it off.
- It does not depend on aim, fire or the recoil pattern; you can use the same button for both (for example
  Right click for aim and for filtering). While the pattern plays, the filtering wobbles around the current step.
- Changing the Filter Button or Filter Mode clears a toggled-on filter. The same ~30 ms stuck-button safety
  net as aiming applies in Hold mode.

Profile fields: `RightStickJitter` (slider value), `RightStickJitterAlgorithm` (`Oscillation` / `LowPass`), `RightStickJitterButton` (`None` / `Mouse Left` /
`Mouse Right` / `Mouse Middle`) and `RightStickJitterMode` (`Hold` / `Toggle`). Profiles without the new
fields load as Off / Hold, which is the old always-on behavior.

## Loop and the Circle preset (Recoil Pattern tab)

- Normally the pattern plays its steps once and then **holds the last step** while you keep firing.
- Tick **Loop pattern while firing** to restart from step 1 after the last step and keep going until you
  release Left click. Start Delay only applies before the first pass. Saved in profiles as `RecoilLoop`
  (default off, so older profiles behave as before).
- The **Circle** preset (replaces the old S-Pattern button) loads a tight circle: 16 steps, radius 8 % around
  the stick center, 17.3 ms per step. Combine it with Loop for a continuous circular motion. Edit the steps or
  the pattern text to change the size or speed.

## Output rate, CPU and lag
- Normally the output loop runs every 10 ms. While filtering is active (RC-style or the low-pass wiggle) it runs every **2 ms**
  (about 500 reports per second). The loop **sleeps** between ticks (no busy-waiting) on a normal-priority thread.
- A report is only sent to the virtual controller when something changed (stick, buttons or triggers), plus a heartbeat about
  every 250 ms. A fixed drift with the filter off sends about 4 reports per second instead of 100.
- The driver call (`SubmitReport`) is made **outside** the engine lock, so the mouse/keyboard hooks never wait on it.
- Earlier betas ran the filtering loop at 1 ms with a spin-wait on an above-normal thread, which used about one CPU core and could
  lag the PC; measured against a simulated 0.5 ms driver call it now uses roughly a quarter of that and hooks wait microseconds.

### Filter rate (experimental)
The **Filter rate** dropdown in the filtering card sets how often the output runs while filtering is active: **2 ms** (500 reports per
second, the default), **4 ms** (250), **8 ms** (125) or **16 ms** (62). RC-style flips once per report, so its swings per second
follow the rate. The Low-Pass wiggle is time-based, so its wave is the same at any rate; for a small N, whose wave is only
16 ms x N long, the rate is automatically kept at or below one eighth of the wave so it is still sampled properly. A lower
rate means fewer controller events for the game to see and less CPU. Saved per profile as `RightStickJitterRateMs` (profiles
without it load as 2 ms).

### Game switching between controller and mouse/keyboard
Many games auto-detect the input device. While the filtering is on, the virtual controller's stick is changing constantly, so such
a game sees controller activity while you also use the mouse, and may flip its prompts/input mode. This app cannot prevent that.
With a very small slider value, the Xbox 360 type, or Filter Button Hold the flipping can still happen, because many games switch
on *any* event from a device, not only on stick movement past the dead zone. Options: use the game's input-device lock (Controller or Keyboard & Mouse instead of Automatic), or set the **Filter Button** to
Hold on Left/Right/Middle so the filtering only runs while you aim or fire.

## Base drift pull direction
The Base drift slider (0-100) moves the right stick by that percent along the axis chosen by **Pull direction**. All four directions
use the same form: one straight axis, the plain sign of the drift percent, on every controller type:
```
Left  => (-d, 0)    Right => (d, 0)
Up    => (0, -d)    Down  => (0, d)
```
- **Down** is the old "Center". Old profiles that say `Center` load as Down; new saves store `Down` / `Up` / `Left` / `Right`.
- There is no controller-specific adjustment. X is the same on PS4 and Xbox 360. Y is the controller's native Y: on **PS4** positive
  Y is down, so Down pulls down; on **Xbox 360** positive Y is up, so Down pulls **up** (and Up pulls down). Pick the other direction
  on Xbox 360 if you want the opposite. (Beta 2.0 flipped the Xbox Y sign; this build removes that so Up/Down are built exactly like
  Left/Right.)
- The recoil pattern replaces the drift while it fires, and the filtering is applied on top.

## Default profile (Default.json)
The default profile is a JSON file that ships inside the app (`Defaults/Default.json`, embedded, so it can't be edited or lost).
- First run: it is written to `profiles/Default.json`, so "Default" appears in the profile list. An existing file is never overwritten.
- Launch: the app starts with `profiles/Default.json` if it exists and is valid; if it is missing or corrupt, with the shipped default.
  Save a profile named "Default" to change what the app starts with. Delete `profiles/Default.json` to go back to the shipped one.
- Old profile files missing a field keep the original behavior for it (no drift or filtering added).
