# Interface design

GamepadEnhancer's window is a dark-first, single-accent desktop utility: a persistent sidebar, one page at a time, and a status bar.

## Structure
| Page | What it holds |
|---|---|
| Dashboard | Virtual controller, output path (ViGEm or another PC), physical controller, Network API status; the active game and profile; the state of mouse look, paddles, blocking and input capture, each with an "Open" shortcut |
| Controller | The interactive diagram (Xbox 360 / PS4 switch above it), the editor for the selected control, and the list view of all bindings |
| Games | Configs list (Default, Apex Legends, Warzone), built-in versus saved-changes state, source of the defaults, the game's native layout, Restore defaults |
| Rear paddles | A large rear view with the four paddles, the same editor, a four-row overview with live "held" dots and the hardware paddle each slot reads |
| Mouse look | Enable, movement (sensitivity, curve, invert) and right-stick output (dead zone, release) |
| Profiles (Games page) | The loaded profile with an unsaved-changes marker and its file details, plus the Profile menu button; the same menu is in the header |
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

## Design tokens (aligned with the ControllerMeta frontend)
The interface uses the same design tokens as the open frontend of https://github.com/HK560/ControllerMeta (values only: no code, assets, text or branding were copied; the repository carries no licence file, and design values are not code).

| | Dark | Light |
|---|---|---|
| Page | `#0F1117` | `#F8FAFC` |
| Card / rail / header | `#1A1D27` | `#FFFFFF` |
| Card hover | `#22263A` | `#F1F5F9` |
| Border | `#2A2E3E` | `#E2E8F0` |
| Text / secondary | `#E4E6EF` / `#8B8FA3` | `#1E293B` / `#64748B` |
| Blue / green / red / orange / purple | `#4F8CFF` `#34D399` `#F43F5E` `#FB923C` `#A78BFA` | `#3B82F6` `#10B981` `#EF4444` `#F59E0B` `#8B5CF6` |

Shapes: cards 12 px radius, 16 px padding, accent border on hover; inset tiles 8 px; buttons, inputs and tags 4 px; rail items 44 px with 12 px radius; segmented container 8 px with 6 px segments; underline tabs. Sizes: rail 64 px, header 56 px, page padding 16 px, card gap 12 px; type scale 10 / 11 / 12 / 14 / 18 / 24 px; card headings 12 px bold capitals. Fonts: Inter and JetBrains Mono, bundled with the app (SIL OFL, see `THIRD_PARTY_FONTS.md`).

## Layout and content
- **Dashboard**: a grid of compact cards made of metric tiles: virtual controller (state, output rate, type, path), inputs and network, active configuration, triggers and sticks (live bars and values on the final output), the controller picture, and a button event stream. All values are real engine data; zero while nothing is connected.
- **Navigation**: icon rail, a header with the app version, a state dot and bordered protocol tags (VIGEM, HOOKS, NET API), and underline tabs inside pages (Controller: Mapping / All bindings; Network and input: Receive / Send / Input and blocking / Appearance and files).
- **Controls**: every numeric setting on the mouse and monitor pages is a slider paired with a number box (the number box stays the source of truth).
- **Measurement views** (`Views/Components/Instruments.cs`): the mouse response curve uses the engine's own `MouseLook.Shape`; the stick scope shows the final right-stick value and the game dead-zone ring; bar meters show trigger travel.
- **Layout tests**: `UiSmokeTests.NoPageNeedsHorizontalScrolling` checks every page at four window sizes.

Not adopted: the split-colour wordmark (the app keeps its own plain name), hover glow, glass blur and background-image theming.

## Profiles and games (vocabulary)
- **Game**: what you are playing (Apex, Warzone, ...). Each game keeps its own changes automatically; built-in defaults are never overwritten.
- **Profile**: a saved copy of your whole setup (controller type, bindings, paddles, mouse look) in `profiles/<name>.json`.
- **Controller**: the virtual pad type (Xbox 360 or DualShock 4).
- The header shows `Game · Profile`, with a dot while the setup differs from the loaded profile. Clicking it opens the Profile menu: load, Save, Save as, Duplicate, Rename, Delete, Import, Export, Open profiles folder, Reset game to its defaults. The menu is also on the Games page (always available when the window is too narrow for the header).
- Loading a profile with unsaved changes asks first. `Default` cannot be renamed or deleted. Switching game clears the loaded profile.
