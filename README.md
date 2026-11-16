# GamepadEnhancer

Windows desktop app that creates a virtual Xbox 360 or PS4 controller with ViGEm and applies base stick drift, right stick filtering, a recoil pattern, and PC-key-to-controller bindings. It only reads your mouse and keyboard; it never moves them.

**Documentation site:** https://chr0mx.github.io/GamepadEnhancer/ (overview, how it works with live labs, how it was built, reference). Source is in [`site/`](site/); it is deployed by `.github/workflows/pages.yml` (Settings > Pages > Source: GitHub Actions).

**Downloads:** see [Releases](https://github.com/Chr0mX/GamepadEnhancer/releases). Release notes for each beta are in [`docs/releases/`](docs/releases/).

## Beta 1.1 highlights

- UI migrated from WinForms to **Avalonia UI** (same ViGEmBus integration, profiles and pattern files)
- Responsive layout: header wraps, cards re-flow to one or two columns, no horizontal scrollbars
- **PC to Controller** has its own tab (Controller / PC to Controller / Recoil Pattern)
- **Right stick filtering** (separate from base drift, RS only). Slide it below zero (e.g. `-7`, `-10`); the
  **Filter Algorithm** dropdown picks the effect: RC-Style Micro-Oscillation flips the stick value symmetrically
  around its target on every report (`-10` on PS4: 118 and 138 around 128), hundreds of times per second on its own
  ~2 ms thread (about 500 reports per second); Exponential Low-Pass Filter makes a soft rounded wiggle and eases changes on X and Y instead. The wiggle always goes on the axis across the drift (Y for Left/Right, X for Up/Down). Base drift is unchanged (0..100).

## Features

- Choose **Xbox 360** or **PS4** virtual controller
- Base drift (0..100) with pull direction **Down / Up / Left / Right**, each a straight pull along one axis from the center, built the same way: Left `(-d, 0)`, Right `(d, 0)`, Up `(0, -d)`, Down `(0, d)` (on Xbox 360 positive Y is up, so Down pulls up there)
- Bind **PC keyboard keys** to virtual controller inputs, with optional per-action **turbo** (1-30 Hz)
- **Right stick filtering** (slider 0 to -50) with a **Filter Algorithm** dropdown: RC-Style Micro-Oscillation (symmetric +/-N flip around the target) or Exponential Low-Pass Filter (a soft rounded wiggle, and eases changes on X and Y), both on the axis across the drift. Each has its own Filter Button (Off = always on, or Left/Right/Middle) and Hold/Toggle mode; see [docs/AIM_TRIGGER.md](docs/AIM_TRIGGER.md)
- **Aim, then fire:** Aim Trigger Key (**Right / Middle / Off**, or Left) plus **Hold** (aim counts only while pressed) or **Toggle** (press to switch aiming on/off); the pattern then plays while **Left click** (fire) is held. Off = Left click alone fires. See [docs/AIM_TRIGGER.md](docs/AIM_TRIGGER.md)
- Pattern **Start Delay (ms)** (wait before the first step) and **Center Radius (%)** (non-zero steps are lifted out to at least this much stick travel) (see [docs/CENTER_RADIUS.md](docs/CENTER_RADIUS.md) for the two Center Radius modes: new dead-zone lift, or the old Shake/Circle wobble)
- **Loop pattern while firing** option (restart from step 1 after the last step instead of holding it) and a **Circle** preset (tight 16-step circle, radius 8 %, replaces the old S-Pattern preset); use them together for a continuous small circular motion
- Recoil pattern editor (steps grid, live preview, text format, `.config` pattern files in `patterns/`)
- **Input capture** dropdown: Low-Level Hooks (default) or Raw Input (background capture with `RIDEV_INPUTSINK`); one active at a time, held inputs are released when switching; saved in `settings.json`. See [docs/INPUT_CAPTURE.md](docs/INPUT_CAPTURE.md)
- Mouse wheel / selected keys temporarily neutralize the stick
- **Starting settings:** the default profile ships inside the app (`src/GamepadEnhancer/Defaults/Default.json`: PS4, Base drift 32% Down, filtering -10 with Exponential Low-Pass on Right click Hold, recoil off, wheel disengage 500 ms with Tab and E). On first run it is also written to `profiles/Default.json`; the app starts with `profiles/Default.json` when it exists and loads (so whatever you save as "Default" is what you get on launch), otherwise with the shipped one. Older profile files that lack a field still load with the original behavior (no drift or filtering added).
- **Network API:** accept MAKCU-style text commands (`km.controller(id,value[,hold_ms])`, `km.controller_clear()`) over TCP from another computer and play them on the virtual pad, with a token and off by default; the same app can also **send** its pad to another PC. See [docs/NETWORK_API.md](docs/NETWORK_API.md)
- **Mouse button UDP stream:** send this PC's mouse buttons over UDP to another PC that wants a remote trigger button (off by default). See [docs/NETWORK_API.md](docs/NETWORK_API.md)
- Profiles (JSON in `profiles/`) save everything above; files from the WinForms build load unchanged

## Install and first run

1. Install **ViGEmBus** on the PC that runs GamepadEnhancer (https://github.com/nefarius/ViGEmBus/releases). Without it the virtual controller cannot be created.
2. Download the latest `GamepadEnhancer-win-x64-<tag>.zip` from [Releases](https://github.com/Chr0mX/GamepadEnhancer/releases) and unzip it anywhere you can write to (settings, `profiles/` and `patterns/` are created next to the exe).
3. Install the **.NET 8 Desktop Runtime** if Windows asks for it (the release is framework-dependent).
4. Run `GamepadEnhancer.exe`, pick Xbox 360 or PS4, and load or edit a profile.

Build the zip yourself with `package.bat` (see below).

## MAKCU requirements

GamepadEnhancer does not talk to MAKCU hardware directly. It speaks the **MAKCU-style text command protocol** (`km.controller(id,value[,hold_ms])`, `km.controller_clear()`) over TCP, so a MAKCU-compatible sender or another GamepadEnhancer on a second PC can drive the virtual pad:

- Enable the **Network API** on the Network tab on the PC with ViGEmBus, set a port and a token, and allow that port through the firewall.
- The sender connects to that address and port, sends `auth <token>` first, then the commands. Full protocol: [docs/NETWORK_API.md](docs/NETWORK_API.md).
- Both PCs must be on the same network. The token is stored in plain text in `settings.json`; use it only on networks you trust.

## Requirements

- Windows 10/11
- ViGEmBus installed on the PC that runs the app
- .NET 8 SDK for local builds (the .NET 8 Desktop Runtime is enough to run a release)

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

## Origin

GamepadEnhancer started as a copy of the `vigem-for-a-game` project and is developed independently of it and of Axiom; neither is needed to build or run it.
