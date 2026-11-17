# Game profiles

GamepadEnhancer is game-aware: choose a game on the top bar and it loads that game's own layout. Every game keeps its own saved customization, so changing one game never touches another.

## What a "game profile" is
Two different things are kept apart:

- **What this app does:** which PC input (keyboard key, mouse button) presses which pad button, the paddles, and mouse look. This is an ordinary profile (see [MAPPINGS.md](MAPPINGS.md)) and is what you edit.
- **What the game does on that pad button** (its own default controller layout, for example *Cross = Jump*). The game owns this; the app only shows it next to each button as a reminder. It is display only and never changes a mapping.

## Which profile loads (priority)
1. **Your saved customization** for the selected game (`profiles/games/<game>.json`).
2. **The game's built-in default** (shipped inside the app).
3. **The global fallback**: your `profiles/Default.json` (PS4, nothing bound unless you remapped it).

An unsupported game ("Other game") simply uses level 3. A game with no built-in default (Warzone, until verified) also falls to level 3. It never borrows another game's mappings.

## How customizations are kept
- Built-in defaults live in the app and are never written to `profiles/`, so your edits cannot overwrite them and an update can improve them.
- Whatever is on screen for the selected game is saved automatically (about a second after you stop changing things, when you switch games, and when the app closes). If it is identical to the built-in default, no file is kept.
- Switching games saves the current game first, then loads the other; nothing is lost either way.
- **Restore game defaults** (top bar) deletes only that game's saved customization and reloads its built-in default.
- Named profiles (Save / Load / Import / Export) still work: loading one puts it on screen as the selected game's working configuration.

## Supported games and where their data comes from
| Game | Built-in PC-input defaults | Native controller labels | Source |
|---|---|---|---|
| Apex Legends | the Apex layout in [MAPPINGS.md](MAPPINGS.md) | verified | Apex's in-game *Controller Presets* screen, preset **Default**, stick layout **Default** (screenshot supplied by the project owner; it shows the Xbox One pad with PlayStation icons). Other presets and stick layouts are not modelled. |
| Call of Duty: Warzone | none yet | **not verified yet, nothing is shown** | The native layout could not be read from an authoritative source while building this. Guides suggest Circle = crouch / prone / slide, R3 = melee, touchpad = map, Options = pause menu, but the rest is unconfirmed, so no label or binding is shipped. |
| Other game | your `profiles/Default.json` | none | global fallback |

## Adding or updating a game
Each game is one JSON file in `src/GamepadEnhancer/Defaults/Games/<id>.json`, embedded automatically:

```json
{
  "Id": "apex",
  "Name": "Apex Legends",
  "Verified": true,
  "Source": "where the labels come from",
  "Layout": "Default preset, Default sticks",
  "Notes": "free text",
  "ProfileResource": "GamepadEnhancer.Apex.json",
  "Native": { "A / Cross": "Jump", "RT / R2": "Attack" }
}
```
- `Native` maps a pad action name (see [MAPPINGS.md](MAPPINGS.md)) to what the game does. Labels are only shown when `Verified` is `true`.
- `ProfileResource` (optional) names an embedded profile with the game's default PC-input bindings; leave it `null` to use the global fallback.
- The `Id` is lowercase letters, digits, `-` or `_` and is also the saved file name. Settings remember the selected game by it (`settings.json`, `ActiveGame`).

## Upgrading
- The app starts on the game stored in `settings.json` (`ActiveGame`); installs from before game profiles start on **Apex**, as before.
- Existing named profiles and `profiles/Default.json` are untouched. Old profiles without paddles load with every paddle off.
