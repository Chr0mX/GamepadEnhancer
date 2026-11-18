# GamepadEnhancer

Windows desktop app that creates a virtual Xbox 360 or PS4 controller with ViGEm and maps PC keys and mouse buttons to it, can turn mouse movement into right-stick movement, and lets another program such as Axiom drive its sticks over the Network API. It reads your mouse and keyboard and never moves them; with the optional input blocking it can also swallow the mapped keys and buttons while a chosen game is focused (see [docs/BLOCKING.md](docs/BLOCKING.md)).

**Documentation site:** https://chr0mx.github.io/GamepadEnhancer/ (overview, how it works, how it was built, reference). Source is in [`site/`](site/); it is deployed by `.github/workflows/pages.yml` (Settings > Pages > Source: GitHub Actions).

**Downloads:** see [Releases](https://github.com/Chr0mX/GamepadEnhancer/releases). Release notes for each beta are in [`docs/releases/`](docs/releases/).

## Features

- Choose **Xbox 360** or **PS4** virtual controller
- Bind **PC keyboard keys, mouse buttons and the wheel** to virtual controller inputs, with optional per-action **turbo** (1-30 Hz)
- **Input capture** dropdown: Low-Level Hooks (default) or Raw Input (background capture with `RIDEV_INPUTSINK`); one active at a time, held inputs are released when switching; saved in `settings.json`. See [docs/INPUT_CAPTURE.md](docs/INPUT_CAPTURE.md)
- **Selectable configs:** pick **Default** (PS4 to keyboard + mouse: WASD, mouse look, Space, Shift, ...) or a game from the **Games** page (the current game and profile are shown in the header). Apex Legends ships its own layout and native controller labels; Warzone is listed but its native layout is not verified yet, so nothing is invented for it. Each config keeps its own saved changes, built-in layouts stay separate, and **Restore config defaults** resets only the selected one. See [docs/GAMES.md](docs/GAMES.md).
- **Interactive controller diagram:** a drawn DualShock 4 / Xbox 360 with every button, stick direction, the right stick (mouse) and the rear paddles; click a control to see and change its mapping, controls light up while pressed, and the list view is still there under *All bindings*.
- **Bindings:** advanced bindings (one input to one or more outputs) with seven conditions (Held, Unheld, Press, Release, Double, Tap, Hold), a Toggle latch, turbo, layers (shift) and un-mapping, a live "running" highlight, and safe output ownership. See [docs/BINDINGS.md](docs/BINDINGS.md)
- **Profiles menu:** load, save, save as, duplicate, rename, delete, import and export profiles from one menu in the header (also on the Games page)
- **Rear paddles:** four independently mapped paddles (virtual button, key or mouse button) read from a physical controller through Windows.Gaming.Input (hidden for the PS4 layout, which has none); see [docs/PADDLES.md](docs/PADDLES.md) for how it works and its limits.
- Profiles can be imported, exported and edited by hand ([docs/MAPPINGS.md](docs/MAPPINGS.md)). Older profile files load unchanged.
- **Network API:** accept MAKCU-style text commands (`km.controller(id,value[,hold_ms])`, `km.controller_clear()`) over TCP from another computer and play them on the virtual pad, with a token and off by default; the same app can also **send** its pad to another PC. See [docs/NETWORK_API.md](docs/NETWORK_API.md)
- **Mouse buttons over the same link:** a client that subscribes with `km.mouse_stream(1)` gets this PC's mouse buttons pushed back as `km.mouse(mask)` on the connection it already uses for commands (one address, port and token), for example to give Axiom a remote aim button. See [docs/NETWORK_API.md](docs/NETWORK_API.md)
- **Input blocking and mouse look:** while a chosen game is focused, swallow the keyboard/mouse input you mapped to pad actions so only the controller reaches the game, bind mouse buttons and the wheel to pad actions, and let mouse movement drive the right stick (off by default). See [docs/BLOCKING.md](docs/BLOCKING.md)
- **Live controller monitor:** an Xbox 360 or DualShock 4 picture of the pad, with a view of the physical input, the processed input, or the final output sent to ViGEm; sticks, analog triggers, button history, pop-out window (always on top or transparent overlay). See [docs/MONITOR.md](docs/MONITOR.md)
- **Switch the output type while connected** (Xbox 360 / PS4) without restarting; a **Guide / PS** button action
- Profiles (JSON in `profiles/`) save the controller type, bindings, turbo and mouse look, and can be **imported, exported and edited** by hand; keyboard keys, mouse buttons and mouse movement are kept clearly apart. Files from earlier builds load unchanged
- **Aiming, recoil and stick shaping are done by the client that drives the pad** (Axiom); this app no longer has base drift, stick filtering or recoil patterns

## Requirements

- Windows 10/11
- ViGEmBus installed on the PC that runs the app
- .NET 8 SDK for local builds (the .NET 8 Desktop Runtime is enough to run a release)

## Install and first run

1. Install **ViGEmBus** on the PC that runs GamepadEnhancer (https://github.com/nefarius/ViGEmBus/releases). Without it the virtual controller cannot be created.
2. Download the latest `GamepadEnhancer-win-x64-<tag>.zip` from [Releases](https://github.com/Chr0mX/GamepadEnhancer/releases) and unzip it anywhere you can write to (settings, `profiles/` and `patterns/` are created next to the exe).
3. Install the **.NET 8 Desktop Runtime** if Windows asks for it (the release is framework-dependent).
4. Run `GamepadEnhancer.exe`, pick Xbox 360 or PS4, and load or edit a profile.

Build the zip yourself with `package.bat` (see [Package a release zip](#package-a-release-zip)).

## MAKCU requirements

GamepadEnhancer does not talk to MAKCU hardware directly. It speaks the **MAKCU-style text command protocol** (`km.controller(id,value[,hold_ms])`, `km.controller_clear()`) over TCP, so a MAKCU-compatible sender or another GamepadEnhancer on a second PC can drive the virtual pad:

- Enable the **Network API** on the Network tab on the PC with ViGEmBus, set a port and a token, and allow that port through the firewall.
- The sender connects to that address and port, sends `auth <token>` first, then the commands. Full protocol: [docs/NETWORK_API.md](docs/NETWORK_API.md).
- Both PCs must be on the same network. The token is stored in plain text in `settings.json`; use it only on networks you trust.

## Run locally

```powershell
dotnet restore .\GamepadEnhancer.sln
dotnet build .\GamepadEnhancer.sln -c Release
dotnet run --project .\src\GamepadEnhancer\GamepadEnhancer.csproj
```

## Publish locally

```powershell
dotnet publish .\src\GamepadEnhancer\GamepadEnhancer.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

## Package a release zip

```powershell
.\package.bat
```

This publishes a win-x64 build and writes `artifacts\GamepadEnhancer-win-x64.zip`. GitHub Actions does the same on every push (`ci.yml`) and creates a release from the manual `build.yml` workflow.

## Tests

```powershell
dotnet test .\GamepadEnhancer.sln -c Release
```

The pure logic (input block policy, mouse look, process matching, key names) is covered by xUnit tests in `tests/GamepadEnhancer.Tests`; CI (`ci.yml`) builds, tests and publishes on every push.

## Project layout

| Path | What is in it |
|---|---|
| `src/GamepadEnhancer/Core` | Controller engine, stick maths, block policy, mouse look, pad snapshots |
| `src/GamepadEnhancer/Input` | Low-level hooks, Raw Input, input state, settings, foreground window |
| `src/GamepadEnhancer/Network` | Network API server, remote link, MAKCU command parser |
| `src/GamepadEnhancer/Views`, `MainWindow.axaml` | Avalonia UI (controller monitor in `ControllerView` / `MonitorPanel` / `MonitorWindow`) |
| `src/GamepadEnhancer/Defaults/` | `Default.json` (empty PS4), `Apex.json` and `Games/*.json` (one file per supported game), shipped inside the app |
| `tests/GamepadEnhancer.Tests` | Unit tests |
| `docs/`, `site/` | Markdown docs, release notes, documentation site |

## Documentation

- [docs/GAMES.md](docs/GAMES.md): game profiles, defaults and how to add a game
- [docs/PADDLES.md](docs/PADDLES.md): rear paddles and their limits
- [docs/MAPPINGS.md](docs/MAPPINGS.md): Apex mapping, profile format, import / export
- [docs/INPUT_CAPTURE.md](docs/INPUT_CAPTURE.md): Low-Level Hooks vs Raw Input
- [docs/NETWORK_API.md](docs/NETWORK_API.md): Network API and `km.mouse` mouse buttons
- [docs/BLOCKING.md](docs/BLOCKING.md): input blocking and mouse look
- [docs/MONITOR.md](docs/MONITOR.md): controller monitor and output types
- [docs/releases/](docs/releases/): release notes

## Origin

GamepadEnhancer started as a copy of the `vigem-for-a-game` project and is developed independently of it and of Axiom; neither is needed to build or run it.
