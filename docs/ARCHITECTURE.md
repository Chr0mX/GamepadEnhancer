# Input naming and game-action architecture

Four layers, each ignorant of the others' vocabulary:

| Layer | Answers | Where |
|---|---|---|
| 1. Physical input ids | "which control on the pad?" | `Core/Canonical/Ids.cs` (`Inputs`) |
| 2. Controller layouts | "what is that control called on *this* pad?" | `Defaults/Controllers/*.json`, `ControllerLayouts` |
| 3. Game actions + native layouts | "what does the game do, and on which physical input?" | `Defaults/Games/<id>.json`, `GameDefinition` |
| 4. Output bindings | "what PC key / mouse / virtual-pad input does it drive?" | profile `Mappings` (the automatic bindings), `Paddles`, `AdvancedBindings` ([BINDINGS.md](BINDINGS.md)) |

## 1. Physical ids (neutral, never brand names)
`FACE_SOUTH/EAST/WEST/NORTH`, `LB RB LT RT`, `SELECT START HOME TOUCHPAD`, `LS_CLICK RS_CLICK`, `DPAD_UP/DOWN/LEFT/RIGHT`,
`LS_UP/DOWN/LEFT/RIGHT`, `RS_MOVE` (the right stick), `PADDLE_LEFT_UPPER/LOWER`, `PADDLE_RIGHT_UPPER/LOWER`.
The first 22 (`Inputs.Buttons`) are the engine's bit order and must never be reordered (the Network API uses indices).
Pre-2.x names ("A / Cross", "LeftUpper", "Paddle:...") are migrated by `LegacyIds` whenever a file is read.

## 2. Controller layouts
A layout JSON holds only display data: `Id`, `Name` (short, used in lists), `Title`, `Aliases`, `Device` (`Xbox360` / `DualShock4`,
the virtual pad ViGEm exposes), `Labels` (physical id to text), `Unsupported`, `FaceStyle`/`FaceColors` (diagram look).
`FACE_SOUTH` is `Cross` on `dualshock4` and `A` on `xbox360`. An id with no label falls back to a neutral name
(`Left Bumper`, `D-Pad Up`). **Add a controller type = add one JSON file** (embedded via the `Defaults/Controllers` glob).
Brand names exist only in these files.

## 3. Games
A game JSON has `Actions` (`{Id, Name}`, ids like `JUMP`, `RELOAD`, `TACTICAL`) and `NativeLayouts` (preset, action id to
`{Input|Inputs, Gesture}`; `Inputs` with several entries is a combo such as Apex Ultimate = `LB`+`RB`). The "what the game does
on this button" labels are derived from these, not hand-written. Unverified games (Warzone) expose nothing.
`GameDefinition.ResolvePhysical(id)` maps an action id (or a physical id) to the single physical input that triggers it.

## 4. Profiles (schema 2)
`Mappings` keys may be physical ids or, when the profile's `Game` is set, that game's action ids. Resolution
(`ProfileMappings.Resolve`) always returns a complete dictionary keyed by physical id; a physical entry beats an action entry,
two actions landing on one input produce a warning, unknown ids are dropped with a warning. `ControllerType` is a layout id.
Paddles are keyed by their canonical ids. Older profiles (legacy keys, `"PS4"`, `"Xbox 360"`, flat bindings) load unchanged and are
written back as schema 2.

## Not covered
Combos and hold/toggle gestures are informational (the virtual pad cannot tell them apart); the editor UI still picks physical inputs,
with labels resolved for the current controller. DualShock/DualSense paddles are not detected (Xbox-class Windows.Gaming.Input only).

## Diagram
`Core/ControllerLayout.cs` holds the marker geometry per virtual device (DualShock 4 and Xbox 360 each have their own silhouette and control positions, drawn in `Views/ControllerConfigView.cs`). Face-button style/colours and an optional centre glyph (the Xbox logo on Guide) come from the controller layout JSON (`FaceStyle`, `FaceColors`, `Glyphs`).

## Advanced bindings
`Core/Bindings/` holds a second, optional binding layer on top of `Mappings`: `BindingModel` (definitions, conditions), `ConditionEvaluator`
(one state machine per binding), `OutputOwnership` (who holds which output), `BindingRuntime` (edge queue, tick update, layers, turbo, status
snapshot) and `BindingEntries` (file form and validation). `ControllerEngine` forwards timestamped input edges, calls `Update` once per tick
and ORs the result into the report it already writes; it remains the only writer to the pad. Details and rules: [BINDINGS.md](BINDINGS.md).
