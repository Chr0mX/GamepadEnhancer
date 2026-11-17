# Bindings and input conditions

The **Bindings** page holds advanced bindings: one input, one condition, an optional toggle and one or more outputs.
Your normal controller mappings (Controller page) are the **automatic bindings** and keep working as before.

## Running versus enabled
- **Enabled** = eligible to run. **Running** = executing right now (its outputs are held).
- A row is highlighted only from the engine's own state (`BindingRuntime.Snapshot`), refreshed about every 33 ms, never from the setting.
  Blue = running; green with a thick border = a toggled binding latched on. With no virtual controller connected nothing can run and
  every enabled row says "Not connected".
- The physical input (held or not) is tracked separately from the binding: a toggled binding keeps running with the input released.
- States: Disabled, Waiting for input, Running, Toggled active, Waiting for hold, Waiting for double press.
- Analog inputs (Mouse movement) are always running while enabled and are never put through a condition. In this version they only
  report that state; the mouse still drives the right stick through Mouse look, and analog outputs are not wired yet.

## Conditions
| Condition | Execution | Runs |
|---|---|---|
| Held | repeats | while the input is held |
| Unheld | repeats | while the input is not held |
| Press | once | when the input is pressed |
| Release | once | when the input is released |
| Double | once | on the second press inside the window (default 300 ms), confirmed at the second press |
| Tap | once | when it is pressed and released within the tap time (default 200 ms) |
| Hold | once | when held for the hold time (default 500 ms); not repeated while still held |

A run-once binding holds its outputs for a short pulse (60 ms) so the game can see it. Held and Unheld never fire "once" events.
Change the condition with the selector in the row. Timing values are under **Details**.

## Toggle
Available for the run-once conditions (Press, Release, Double, Tap, Hold). With Toggle on, the binding becomes a repeating latch: the first
qualifying activation starts it, it keeps running after the input is released, the next activation stops it. One press can never toggle
twice (Press fires on the down edge only, Hold fires once per press). Turning Toggle off, disabling, editing the condition or input,
deleting, disconnecting or shutting down stops a latched binding and releases what it held. Held / Unheld ignore Toggle.

## Outputs and ownership
Outputs: virtual pad buttons (triggers take a percent), keyboard keys, mouse buttons. An output is held by the set of bindings that own
it and is released only when the last owner lets go, so one binding stopping never releases something another still holds.
Key and mouse outputs are sent with the same injector the rear paddles use; the app ignores them as input, so a binding cannot trigger
itself. An output that presses the binding's own input is refused. An output the active pad does not have (for example the DualShock
touchpad on Xbox 360) shows a warning on the row.

A manual binding on an input turns off the automatic mapping of that same input (shown under Automatic bindings), so one input never does both.

## Architecture
`Core/Bindings/`: `BindingModel` (definitions), `ConditionEvaluator` (one state machine per binding, pure), `OutputOwnership` (who holds
what), `BindingRuntime` (edge queue, tick update, outputs, status snapshot), `BindingEntries` (profile file form, validation).
`ControllerEngine` only forwards timestamped press / release edges, calls `Update` once per 10 ms tick, ORs `PadMask` and the trigger
levels into the report it already writes, and calls `Flush` (key / mouse injection) outside its lock. Nothing in the runtime sleeps or
owns a timer; time is passed in, which is how the unit tests drive it. Timed conditions therefore resolve to about 10 ms.

## Files
Bindings are saved in the profile as an optional `AdvancedBindings` list (absent when there are none, so existing profiles and older
apps are unaffected). Invalid inputs, outputs and times are cleaned on load and import.

## Status
Written and covered by unit tests (`BindingTests.cs`, a UI test in `UiSmokeTests.cs`). Not yet run: there is no .NET SDK where this was
written, so the tests run only on CI. Not verified against real hardware.
