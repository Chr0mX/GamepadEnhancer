# Aim Settings and Advanced Bindings: audit and plan

Status: audit and plan. **Phase 1 (binding foundation, conditions, toggle, running state) is now written** (see BINDINGS.md); Phases 2-5 are not started. Branch `MIX`.

## 1. What the repository actually is (audit)

GamepadEnhancer turns **PC keyboard and mouse into a virtual PS4 / Xbox 360 pad** (ViGEm, or the pad is sent to another PC over the
Network API). It is not an aim assist and has no game-side vision. Findings, by the questions in the brief:

| Question | Finding |
|---|---|
| Aim-processing pipeline | None as such. The only aim path is **mouse look**: `Core/MouseLook.cs` turns mouse counts per ms into a right-stick vector (`Step()`): velocity = counts / dt (dt clamped 1-50 ms), times sensitivity percent, power curve, lift past the game's inner dead zone, hold for `ReleaseMs`, then recentre. Called from `ControllerEngine.Tick()` (10 ms loop, `Core/ControllerEngine.cs`). |
| Mouse sensitivity / smoothing | Sensitivity is "percent of stick per (count/ms)", per axis (`EngineSettings.MouseSensitivityX/Y`), one exponent curve, one release hold. There is **no real smoothing** (only the release hold), no DPI, no cm/360. |
| Existing stick filters | **None in this repository.** `ControllerEngine` says drift, filtering and recoil "are Axiom's job now and no longer exist here"; the old fields still load from old profiles and are ignored (`InputProfile`). The brief's "existing stick-filtering algorithms" therefore do not exist here to preserve. They live in the separate Axiom project. |
| Physical input events | `Input/` captures keyboard and mouse with a low-level hook or Raw Input (`InputStateManager` -> `IInputSink.OnKey/OnMouseButton/OnWheel/OnMouseMove`). Engine keeps only a **level set** (`activeKeys`); wheel notches are 60 ms pulses. Physical gamepads are **not read**, except rear paddles via Windows.Gaming.Input (`Input/WgiPaddleSource.cs`). No gyro, no DPI. |
| Output handling | `ControllerEngine` builds one report per 10 ms tick (`Tick()`), sets ViGEm / network pad buttons, sliders and axes, submits when changed (+250 ms heartbeat). Triggers are digital 0/255 from bindings; the network client can override triggers and sticks (`RemoteControlState`). |
| MAKCU | Only as the **Network API** (`Network/MakcuCommandParser.cs`, `NetworkApiServer`): a remote client (for example Axiom) drives the pad with `km.controller(...)` and gets this PC's mouse buttons pushed (`km.mouse_stream`). There is no serial MAKCU device code here. |
| Existing binding system | **Profile `Mappings`**: one PC input (key, mouse button, wheel notch) -> one pad button, optional turbo (`BindingSetting`, `PressedMask`). Game profiles key them by game action (`Core/Games.cs`). Paddles map to a pad button, key or mouse button (`Core/Paddles.cs`, injection through `IInputInjector`, ownership by `IsInjected` + pending queue). No conditions, no toggle, no multi-output, no analog bindings. |
| Persistence | `InputProfile` JSON (schema 2, `Mappings`, `Paddles`, mouse fields), `GameProfileStore` (per-game saved changes), `ProfileImport` (validation), `AppSettings` (`settings.json`). `GameProfiles.Equivalent` decides "modified". |
| UI | Avalonia: rail pages `dashboard, controller, games, paddles, mouse, monitor, network` (`MainWindow.axaml`, `.Shell.cs`), `SettingRow`/`Section` components, `Instruments.cs` (curve plot, stick scope). Windowed-size tests exist (header fit, diagram scale, no sideways scroll). |
| Tests | 199 headless tests (xUnit + Avalonia.Headless). **No .NET SDK in this sandbox**: CI (GitHub Actions) is the only way to build and run them. |

## 2. Feature gap analysis

| Requested | Exists | Gap |
|---|---|---|
| Aiming sources: mouse | yes (mouse look) | enable/disable flag, combining |
| Aiming sources: thumbstick | **no physical stick is read** | only the **remote (Network API) right stick** exists as a stick source |
| Aiming sources: gyro | no | **Not implementable** (no pipeline). Left out, as the brief allows. |
| Hip / ADS sets, inheritance, ADS input, delays, transition | no | all new. ADS input can be a PC input or the game's `ADS` action (`GameDefinition.ResolvePhysical`) |
| cm/360, DPI | no | needs DPI **and** the game's full-stick turn speed (deg/s). See 6. |
| Smoothing (Standard, Classic) | no | new |
| Curves (multi-point editor) | single exponent | new `AimCurve` + editor control |
| Deadzones, axial, offset, rotation, inversion | game dead-zone lift + invert Y only | new (for the remote stick source) |
| Quantization (magnitude, angle) | no | new |
| Velocity mapping | no | Limit / Reduce implementable; Standard / Uniform need calibration. See 6. |
| Binding conditions (7), toggle, multi-output, ownership, copy/paste, learn, automatic bindings, states | no (only turbo mappings) | all new |

## 3. Safest integration strategy

Principles: **additive and off by default**. A profile or install that has not touched the new features behaves **bit for bit as
today**.

1. **New pure-C# modules, no Win32 and no ViGEm references**, each unit-tested on CI: `AimCurve`, `AimPipeline` (+ stage classes),
   `BindingModel`, `ConditionEvaluator`, `OutputOwnership`, `BindingScheduler`.
2. **The engine stays the single output writer.** `ControllerEngine.Tick()` calls the new modules at two well-defined points; nothing
   else writes to the pad.
3. **Legacy path preserved.** If `AimSettings.Enabled` is false (default) `MouseLook.Step()` runs exactly as now. When enabled, the new
   pipeline **reuses `MouseLook` for the counts-per-ms measurement and release hold** (no second velocity calculation) and replaces only
   what comes after it. The game dead-zone lift and curve are applied once, in one place (see 4).
4. **Existing `Mappings` remain the "automatic bindings".** They keep working and are shown as such. A manual binding on a PC input
   suppresses the automatic mapping of that same input (so no double output).
5. **Profile additions are optional blocks** (`Aim`, `AdvancedBindings`) on the existing profile. Absent block = defaults. Old files
   load unchanged, old apps ignore the blocks. `Equivalent()`, `ProfileImport` validation and presets are extended, not replaced.
6. **No change** to MAKCU / Network API parsing, `RemoteControlState`, the ViGEm and network pad wrappers, or the capture backends.
   One additive hook: `IInputSink` gets timestamped **edge events** (see 5) next to the existing level set.

## 4. Aim processing order (single, documented)

Per 10 ms tick, for each enabled source, then combined:

```
1  Source sample        mouse: counts since last tick (MouseLook measurement)      remote stick: RemoteFrame right stick
2  Normalize            counts/ms (mouse) or -1..1 (stick)
3  Source shaping       mouse: DPI + sensitivity (percent, or cm/360 if calibrated) + Y scale
                        stick: offset -> rotation -> axial / inner / outer dead zone -> invert -> sensitivity X/Y
4  Curve                ONE curve per source (Native = skip, Custom = AimCurve). Never stacked on the game's response
5  Hip/ADS blend        pick Hip, ADS, or the transition mix (applies to steps 3-4 parameters, 6, 7)
6  Combine              vector sum of enabled sources, clamp to unit disc
7  Smoothing            Standard OR Classic (mutually exclusive modes, separate parameters)
8  Quantization         magnitude, then angle (each optional)
9  Velocity mapping     Limit / Reduce (and Standard / Uniform when calibrated)
10 Game dead-zone lift  existing MouseGameDeadZonePercent, applied once, here only
11 Existing output      clamp, Xbox/DS4 axis conversion, remote override, report
```

Rules that prevent double processing: the game dead-zone lift happens only in step 10 (the legacy path keeps it inside
`MouseLook.Shape`, the new path calls the same function with curve = 1); each source has exactly one curve; smoothing runs after
combination, never per source; the remote stick keeps its **current pass-through** unless `Aiming Sources > Thumbstick` is enabled
(default off), so Axiom's own filtering is never silently re-filtered.

## 5. Binding execution architecture

Responsibilities, as requested, each its own unit:

1. **Detection**: hook / Raw Input (unchanged) -> `IInputSink`.
2. **Normalization**: `InputId` (device + name), `InputKind` (Digital / Analog). Wheel notch = digital pulse (as today).
3. **State tracking**: new thread-safe **edge queue** `(inputId, isDown, timestampMs)` filled on the hook thread, drained in `Tick()`.
   Needed because the engine only keeps levels; a press shorter than 10 ms would otherwise be lost, and Tap / Double need real
   timestamps. Level set stays for existing mappings.
4. **Condition evaluation**: `ConditionEvaluator` (pure state machines per binding): Held, Unheld, Press, Release, Double, Tap, Hold,
   plus Toggle for the run-once conditions. Time comes from a monotonic `now` passed in; **no sleeps, no timers on the UI**.
5. **Execution**: `BindingScheduler` runs from the engine tick (10 ms resolution). Pending timers (Hold threshold, Double window) are
   deadlines compared against `now`.
6. **Output ownership**: `OutputOwnership` is a ref-count / owner-set per output (virtual pad button, trigger, key, mouse button).
   An output is physically released only when its last owner releases. Toggled-off, disabled, deleted, edited, disconnected and
   shutdown all go through `ReleaseOwnedBy(binding)`; `Disconnect()` also calls `ReleaseAll()`.
7. **Virtual output**: advanced outputs OR into the same `mask` / trigger values the engine already writes (one writer). Keyboard / mouse
   outputs reuse `IInputInjector` (`SendInputInjector`) and the `IsInjected` guard so injected keys never re-trigger bindings.

Analog inputs (mouse movement, remote sticks, triggers) are **not** put through digital conditions; they feed the aim pipeline or a
continuous output with a magnitude. Digital bindings can use a thresholded analog value only through an explicit threshold setting.

## 6. Things the brief asks for that need a decision or cannot be done as written

1. **Thumbstick source.** No physical stick is read. Options: (a) apply the thumbstick settings to the **remote (Network API) right
   stick** only; (b) additionally read a physical pad through Windows.Gaming.Input (new capture code, new hardware coverage);
   (c) skip. Recommended: (a), default off.
2. **Gyro / motion**: not implementable. Omitted.
3. **cm/360 and DPI.** Converting mouse movement to a real turn speed needs the game's **full-stick turn speed in deg/s**. The app
   cannot measure it. Proposal: an explicit "Full-stick turn speed (deg/s)" calibration field (default blank). Blank = existing
   "percent per count/ms" sensitivity stays the only mode; filled = cm/360 + DPI mode becomes available. No guessed constants.
4. **Velocity mapping.** *Limit* and *Reduce* are pure output scaling and will be implemented. *Standard* is identity. *Uniform*
   (equal speed in all directions) needs per-direction max speeds, which cannot be measured: offered only with a **manual
   per-direction table**, default off.
5. **Quantization / smoothing for mouse**: fully implementable. Standard and Classic parameters are defined in
   `docs/AIM_SMOOTHING.md` (to be written with Phase 2) as concrete formulas before code, so "Precision / Response / Easing /
   Stability" and "Smooth / Decay / Synchronization" each map to one named term.
6. **Automatic bindings by category.** Implemented on top of the existing `Mappings` (keyboard / mouse buttons / wheel / sticks).
   Categories are switches that hide or disable that group's automatic mappings.

## 7. Phased implementation plan

Every phase: pure logic + unit tests first, wiring second, CI green before the next phase. Nothing is called complete until its
tests have **run on CI**.

- **Phase 1 Binding foundation.** `BindingModel`, `ConditionEvaluator` (7 conditions + toggle), `OutputOwnership`, edge queue,
  `BindingScheduler`, engine wiring, profile `AdvancedBindings` block + validation, copy / paste model, automatic-vs-manual
  suppression. Tests: each condition with fake clock, double / tap / hold edge cases and overlaps, toggle lifecycle, simultaneous
  bindings sharing one output, disconnect and shutdown release.
- **Phase 2 Aim settings core.** `AimCurve` (validation, linear / accel / decel / multi-point, monotonic and bounded), `AimPipeline`
  stages 2-8, Hip/ADS sets with the three inheritance modes, profile `Aim` block. Tests: pipeline order, no double dead-zone,
  legacy path identical when disabled (golden values from today's `MouseLook`), smoothing stability, quantization.
- **Phase 3 Advanced aim.** ADS activation (PC input or game `ADS` action), delays, smooth transition, stick rotation, velocity
  mapping (as in 6), cm/360 (if calibrated).
- **Phase 4 UI.** Two new rail pages, **Aim** and **Bindings**; curve editor control (add / move / remove points, reset, straighten
  segment, preview); live previews on `Instruments`; binding list with status. Rail grows from 7 to 9 buttons: windowed-size and
  no-sideways-scroll tests extended to cover them.
- **Phase 5 Verification.** The remaining items of the testing list (ADS transitions, device disconnect, shutdown), docs
  (`ARCHITECTURE.md`, new `AIM.md`, `BINDINGS.md`), release notes.

## 8. Constraints and risks

- **No local compiler or test runner.** Every build and test run is on GitHub Actions, which only runs on pushed branches. `MIX` is
  local only until pushing it is approved; without a push nothing here can be verified.
- Runtime behaviour that needs real hardware (hook timing, ViGEm output, real mouse feel) cannot be tested headless; the plan keeps
  that logic thin and tests the pure parts, and the docs will say which parts were hardware-verified (none until someone runs it).
- Timed conditions run at the engine's 10 ms tick: Tap / Double / Hold timing resolution is 10 ms, which is stated in the UI help.
- Out of scope by instruction: recoil, Simulate Analog Behavior, unrelated UI redesign, replacing any existing system.
