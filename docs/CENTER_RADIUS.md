# Center Radius (Recoil Pattern tab)

Center Radius (%) has **two behaviors**. Pick one with **Center Radius Mode**, next to it on the
Recoil Pattern tab. Both only apply while the recoil pattern is running (after the aim trigger is
active **and** Start Delay has passed). With radius `0` nothing happens in either mode.

| Mode (UI label)         | Profile value | Radius range | Default |
|-------------------------|---------------|--------------|---------|
| **New: dead-zone lift** | `Lift`        | 0-100 %      | selected |
| **Old: Shake wobble**   | `Shake`       | 0-45 %       |         |
| **Old: Circle wobble**  | `Circle`      | 0-45 %       |         |

Profile fields (in `profiles/*.json`): `RecoilCenterRadiusPercent` (the number) and
`RecoilCenterRadiusMode` (`Lift`, `Shake` or `Circle`). Profiles saved before this existed have no
mode field and load as `Lift`.

## New: dead-zone lift (`Lift`)

Fixes recoil steps that are too small for the game to notice because of its inner dead zone.

- A pattern step whose length `sqrt(x^2 + y^2)` is **below** the radius is stretched, in the same
  direction, to exactly the radius. Example: step `(0, 5)` with radius 20 becomes `(0, 20)`.
- Steps already at or above the radius are unchanged.
- A `(0, 0)` step stays at the center.
- Code: `StickMath.ApplyCenterRadius` (`src/GamepadEnhancer/Core/StickMath.cs`).
- It is invisible when your steps are already larger than the radius (typical recoil steps with
  Y of 18-50 are unaffected by a radius up to about 18).

## Old: centered wobble (`Shake` / `Circle`)

This is what "Centered radius %" did in the earlier jitter feature (before it was removed in
commit `b69b6c1`). The radius is the **size of a small repeating wobble** added on top of each
recoil step, so the stick keeps shaking around where the pattern wants it.

- **Shake** - 4 steps repeating: `(-wide, 0)`, `(+wide, 0)`, `(-tight, +tight)`, `(+tight, -tight)`
  with `wide = max(4, r)` and `tight = max(2, round(0.45 * r))`.
- **Circle** - 16 points around a circle of radius `r`.
- The wobble is **centered**: the average of its steps is subtracted (so it adds no net drift), then
  it is scaled so the largest x or y component equals the radius.
- It advances one step every 10 ms (the old 10 ms timer), regardless of the output rate, and
  restarts each time the trigger activates.
- The result is added to the recoil step and clamped to -100..100.
- Radius is limited to 45 (values above are treated as 45).
- Code: `WobblePattern` (`src/GamepadEnhancer/Core/WobblePattern.cs`), ported unchanged from the old
  `BuildShakePattern`, `BuildCirclePattern` and `CenterAndScalePattern`.

### Differences from the old jitter feature

- Old trigger: a hold key plus its own Start delay (default 120 ms). Now the wobble uses the Aim
  Trigger (Left / Right / Middle, Hold or Toggle) and the pattern's **Start Delay**.
- Old default radius was 18. The default now is 0 (off) so existing recoil feel is unchanged.
- The old Off / Custom pattern types are not ported; use Shake or Circle.
- The old "extra drift while the hold key is pressed" is not part of this.

## Where the original source is

```bash
git show b69b6c1^:src/GamepadEnhancer/MainForm.cs
```

Look for `GetPatternSteps`, `CenterAndScalePattern`, `BuildShakePattern`, `BuildCirclePattern`
(about lines 1471-1570) and the "Centered radius %" field (about line 363).
