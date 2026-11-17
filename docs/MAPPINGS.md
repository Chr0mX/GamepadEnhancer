# Input mappings and profiles

A profile maps **PC input to a virtual PS4 / Xbox 360 pad**. Configs are selectable from the top bar (see [GAMES.md](GAMES.md)): **Default** (the PS4 to keyboard + mouse layout below), **Apex Legends** (its own layout, further down) and, once verified, other games. Rear paddles are described in [PADDLES.md](PADDLES.md) and are stored in a `Paddles` block of the same profile file.

Three kinds of PC input are kept clearly apart, in the picker groups, in the small tag next to each picker, and in the file:

| Kind | What it is | Where it goes |
|---|---|---|
| **Keyboard** | A key (`Space`, `Ctrl`, `R`, `1`, `Home`, `F5`, ...) | Any button, and the four left-stick directions |
| **Mouse button** | `Mouse Left`, `Mouse Right`, `Mouse Middle`, `Mouse X1` (Mouse 4, side back), `Mouse X2` (Mouse 5, side forward) | Any button (not the left-stick directions) |
| **Mouse wheel** | `Wheel Up`, `Wheel Down` (one notch = one press) | Any button (not the left-stick directions) |
| **Mouse movement** | Moving the mouse | The **right stick**, not a binding: turn on *Mouse movement drives the right stick* |

## Default config (PS4 to keyboard + mouse)

Where a binding had alternatives, the first one is used and the rest stay one click away in the picker.

| Pad control | Keyboard / mouse | Role |
|---|---|---|
| Left stick | `W` `A` `S` `D` | Move |
| Right stick | Mouse movement | Look / aim |
| L3 | `Shift` | Sprint / run |
| R3 | `V` (or Middle Mouse) | Melee / lock-on |
| R2 | Left Mouse | Fire |
| L2 | Right Mouse | Aim down sights |
| R1 | `G` (or `E`) | Tactical / lethal / ability |
| L1 | `Q` | Ability / grenade / weapon wheel |
| Cross | `Space` | Jump / confirm / dodge |
| Circle | `C` (or Left Ctrl) | Crouch / slide |
| Square | `R` (or `E` / `F`) | Reload / interact |
| Triangle | Mouse wheel up (or `1` `2` `3`) | Switch weapon |
| D-Pad Up / Down / Left / Right | `1` / `2` / `3` / `4` | Weapons / gadgets |
| Touchpad click | `M` (or `Tab`) | Map / inventory |
| Options | `Esc` | Pause / menu |
| Share | `F12` (or PrtSc) | Screenshot / clip |

The Action / RPG variant of the triggers and bumpers (Left/Right click on R1/L1 and so on) is not shipped as its own config; pick those keys in the editor. PrtSc is not a bindable key.

## Default (Xbox 360) config

Same keys as the PS4 Default (A = Cross, B = Circle, X = Square, Y = Triangle, LB / RB = L1 / R1, LT / RT = L2 / R2, LS / RS = L3 / R3, right stick = mouse, D-Pad `1 2 3 4`), with two differences: **View (Back) = `M`**, and **Share / Touchpad are unbound** because the virtual Xbox 360 pad has neither (F12 and PrtSc are therefore not used; `Tab` is the other View option). Picking it selects the Xbox 360 output type when no pad is connected.

## Apex Legends config (built in)

| Pad action (PS4 / Xbox) | Input | Kind |
|---|---|---|
| Cross / A | `Space` | Keyboard |
| Circle / B | `Ctrl` | Keyboard |
| Square / X | `R` | Keyboard |
| Triangle / Y | `E` | Keyboard |
| L1 / LB | `Q` | Keyboard |
| R1 / RB | `Mouse X1` (Mouse 4, side) | Mouse button |
| L2 / LT | `Mouse Right` | Mouse button |
| R2 / RT | `Mouse Left` | Mouse button |
| L3 / left stick click | `Shift` | Keyboard |
| R3 / right stick click | `Mouse Middle` (Mouse 3) | Mouse button |
| D-Pad Up / Down / Left / Right | `4` / `F1` / `B` / `G` | Keyboard |
| Options / Start | `Escape` | Keyboard |
| Share / Back | `Tab` | Keyboard |
| PS / Guide | `Home` | Keyboard |
| Touchpad click | not bound | |
| Left stick Up / Down / Left / Right | `W` / `S` / `A` / `D` | Keyboard |
| Right stick Up / Down / Left / Right | Mouse movement Up / Down / Left / Right | Mouse movement |

Nothing else is bound (the Touchpad click is left empty), and turbo is off everywhere.

### What each button does in Apex Legends
The game only sees a controller, so what matters is the pad button; in-game PC keybinds are irrelevant. This is Apex's **default controller preset** (the app also shows it under each button):

| Pad button | Apex action | Hold |
|---|---|---|
| Cross / A | Jump | |
| Circle / B | Crouch (toggle) | |
| Square / X | Interact / Pickup / Reload | Interact / Pickup |
| Triangle / Y | Cycle Weapon / Holster | Holster |
| L1 / LB | Tactical Ability | |
| R1 / RB | Ping | Ping Wheel |
| L1 + R1 | Ultimate Ability (both together: Q + Mouse 4 by default) | |
| L2 / LT | Aim Down Sight | |
| R2 / RT | Attack | |
| L3 | Sprint / Toggle Zoom | |
| R3 | Melee | |
| D-Pad Up | Use Health/Shield Kit | Health Wheel |
| D-Pad Right | Toggle Fire Mode | Equip Survival Item |
| D-Pad Down | Equip Grenade | Grenade Wheel |
| D-Pad Left | Extra Character Action | Emote Wheel |
| Share / Back | Map (toggle) | |
| Options / Start | Inventory (toggle) | |

PS / Guide and the touchpad click are not on the preset. The Apex names are display hints only; they do not change any mapping.

Left stick keys: opposite keys cancel, and diagonals are scaled so they are no faster than a straight direction. A left stick value sent over the [Network API](NETWORK_API.md) replaces the keys while it is set (the right stick works the same way against mouse movement).

Mouse Left / Mouse Right are bound to the triggers by default, so while a pad is connected every click on the PC also registers as R2 / L2. Input blocking ([BLOCKING.md](BLOCKING.md)) can swallow them while the game is focused.

## The profile file

Profiles are plain JSON in `profiles/` next to the exe. Bindings are in `Mappings`, keyed by the action name; the right stick is the `MouseLook*` fields.

```json
{
  "Name": "Default",
  "ControllerType": "PS4",
  "MouseLookEnabled": true,
  "MouseSensitivityX": 8.0,
  "MouseSensitivityY": 8.0,
  "MouseCurve": 1.0,
  "MouseGameDeadZonePercent": 0,
  "MouseReleaseMs": 30,
  "MouseInvertY": false,
  "Mappings": {
    "A / Cross":      { "Input": "Space",   "Kind": "Keyboard",    "Turbo": false, "TurboHz": 12 },
    "RB / R1":        { "Input": "Mouse X1", "Kind": "MouseButton", "Turbo": false, "TurboHz": 12 },
    "Left Stick Up":  { "Input": "W",       "Kind": "Keyboard",    "Turbo": false, "TurboHz": 12 }
  }
}
```

- **Action names** are fixed: `A / Cross`, `B / Circle`, `X / Square`, `Y / Triangle`, `LB / L1`, `RB / R1`, `LT / L2`, `RT / R2`, `Back / Share`, `Start / Options`, `LS / L3`, `RS / R3`, `DPad Up`, `DPad Down`, `DPad Left`, `DPad Right`, `Guide / PS`, `Touchpad Click`, `Left Stick Up`, `Left Stick Down`, `Left Stick Left`, `Left Stick Right`.
- **`Input`** is one of the names above, or `None`. Names are not case sensitive. Unknown names, and mouse inputs on a left stick direction, are cleared to `None` (an import tells you which).
- **`Kind`** is there for people reading the file. It is worked out from `Input` when the file is loaded, so a wrong `Kind` does no harm.
- **`TurboHz`** is 1 to 30. Left-stick directions have no turbo.
- Missing actions are `None`. Missing fields take their defaults.
- Profiles from earlier builds (`KeyBindings`, `TurboEnabled`, `TurboHz` dictionaries, and the removed drift / recoil fields) still load; they are saved in the new layout from then on. An older build cannot read bindings from a profile saved by this one.

## Import, export and edit

On the top bar, next to the profile box:

- **Save / Load / Delete** work on `profiles/`.
- **Export...** writes the current screen (not only what was saved) to a `.json` file anywhere, to share or edit by hand.
- **Import...** reads such a file (max 256 KB), clears anything invalid, adds it to `profiles/` under a unique name (never overwriting) and applies it. If something was cleared the status line and a message say what.
- **Folder** opens `profiles/`, where every file can also be edited in a text editor and then loaded.

## The shipped default and upgrades

`Default.json` (PS4, nothing bound) ships inside the app and is written to `profiles/Default.json` on first run; Apex is read from the app itself and never written to `profiles/`. When a release changes the shipped Default, the existing `profiles/Default.json` is replaced **once** (the old file is kept as `Default.json.bak`, or `.bak2`, ... so a backup is never overwritten); `settings.json` remembers that the upgrade was applied (`DefaultMappingVersion`). Your own profiles are never touched. To get the empty Default back later, delete `profiles/Default.json` and restart.
