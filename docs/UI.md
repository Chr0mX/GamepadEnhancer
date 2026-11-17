# Interface design

GamepadEnhancer's window is a dark-first, single-accent desktop utility: a persistent sidebar, one page at a time, and a status bar.

## Structure
| Page | What it holds |
|---|---|
| Dashboard | Virtual controller, output path (ViGEm or another PC), physical controller, Network API status; the active game config and profile; the state of mouse look, paddles, blocking and input capture, each with an "Open" shortcut |
| Controller | The interactive diagram (Xbox 360 / PS4 switch above it), the editor for the selected control, and the list view of all bindings |
| Games | Configs list (Default, Apex Legends, Warzone), built-in versus saved-changes state, source of the defaults, the game's native layout, Restore defaults |
| Rear paddles | A large rear view with the four paddles, the same editor, a four-row overview with live "held" dots and the hardware paddle each slot reads |
| Mouse look | Enable, movement (sensitivity, curve, invert) and right-stick output (dead zone, release) |
| Profiles | Saved profiles list, name/save/load/delete, import/export/folder, active profile, per-file details (controller, game, size, date) |
| Monitor | Physical / processed / final-output views, display options, pop-out (always on top, transparent overlay) |
| Network | Two separate sections: Receive (Network API server) and Send (control a PC that has ViGEmBus) |
| Input & settings | Capture method, input blocking and game programs, appearance, data folders |

The virtual controller type and Connect/Disconnect stay in the sidebar footer on every page. Below about 900 px the sidebar collapses to icons and two-column pages stack.

## Design system
- **Tokens** (`App.axaml`): surfaces, borders, text, one accent plus its tint, status colours, for Dark and Light. Text/background pairs keep at least 4.5:1 contrast. The Fluent control resources (button, text box, combo box, list item, tooltip states) are pointed at these tokens, so hover, pressed, focus and disabled states come from the same place.
- **Styles** (`Styles/Theme.axaml`): 32 px control height, 6 px control and 8 px panel radius, type scale (20 title, 14 section, 13 body, 12 caption), button variants (`primary`, `ghost`, `danger`, `icon`), `nav` and `seg` radio items, panel and divider, status dot, one keyboard focus ring.
- **Components** (`Views/Components`): `SettingRow` (title, explanation, control), `Section` (titled panel), `StatusBadge` (dot + text), `Icon` + `Icons` (stroke icons in the Lucide style, 24 px grid, entered as SVG path data; no icon package is referenced).
- **Controller visuals** keep their own drawing code (`ControllerConfigView`, `ControllerView`); face-button colours and glyphs come from the controller layout files.

## What did not change
Every control still calls the same handlers and writes the same settings and profile files. The controller engine, ViGEm, Network API, paddles, mouse look, blocking and profile/preset formats are untouched.

## Testing
`UiSmokeTests` creates the real application and window headless (Avalonia.Headless), shows every page at three window sizes and checks the diagram follows the page and the layout switch. It catches missing resources, bad styles/templates and constructor exceptions in CI. It cannot judge how the window looks; that needs eyes on a real screen.

## Limits
- Not checked visually. Spacing, icon legibility and the redrawn diagrams need a look on a real display.
- The monitor panel's drawing is the previous implementation (stick positions, trigger values and recent events were already there); only its page, controls and layout changed.
- Number entry uses numeric boxes; there are no paired sliders yet.
- Remote command counters are not shown on the Network page: the engine does not expose them without disturbing its timing, and no placeholder values are shown.
