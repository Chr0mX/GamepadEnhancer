# VHF output backend (optional)

GamepadEnhancer creates its virtual controller through **ViGEmBus** by default. This page describes the optional second output: a
virtual controller created through Microsoft's **Virtual HID Framework (VHF)** with the open-source driver of
[libvirtualgamepad](https://github.com/Nonary/libvirtualgamepad). **ViGEm stays the default and is not removed or changed.** Nothing
changes for anyone who does not pick VHF.

> Status: implemented and covered by automated tests that run against a fake driver. It has **not** been tried against the real
> driver, in a real game, or on real hardware by the author of this change, and no performance or anti-cheat measurement exists. The
> manual test list at the end is what still has to be done on a Windows PC.

## What it is and is not

- A second `IXboxPad` / `IDs4Pad` implementation (`Output/Vhf/VhfPad.cs`) next to the ViGEm pad and the Network pad. All processing
  (key-to-button mapping, WASD stick, mouse look, turbo, advanced bindings) stays in `ControllerEngine`; the driver only receives the
  final pad state and reports what a game wrote to the controller.
- **Not** a new driver. The app never builds, bundles, signs or silently installs a driver. It talks to an installed driver and can
  start that driver's own setup tool after checking the package (below).
- **Not** an Xbox 360 device. VHF creates HID devices; a real Xbox 360 pad is an XUSB device on a USB bus, which VHF cannot create.
  Xbox 360 output always uses ViGEm.

```
ControllerEngine (unchanged logic, one output thread)
  -> IXboxPad / IDs4Pad
       |- ViGEmXboxPad / ViGEmDs4Pad     ViGEm (default)
       |- NetworkPad                     send to another PC
       '- VhfPad -> VhfClient -> IVhfTransport -> the VHF driver
```

## Upstream version

| | |
|---|---|
| Project | https://github.com/Nonary/libvirtualgamepad |
| Version | tag `v0.1.0-beta.11` |
| Commit | `93dc007a608b341aa998378221f8958f045ee7f2` (2026-11-14) |
| Protocol | version 2 (`k_protocol_version`); a driver whose supported range excludes 2 is refused |
| Licence | MIT, Copyright (c) 2026 Chase Payne |

"Pinned and tested" means: the C# wire format is checked byte for byte against the sizes, offsets, IOCTL codes and GUID of that
commit's `protocol.h` in CI, and the client is exercised against a fake that behaves like the driver's documented contract. Upstream's
own test results were not inspected, and the upstream project itself calls these releases beta.

## Licence and redistribution

- Upstream is MIT: the transcribed protocol constants keep its notice (`THIRD_PARTY_NOTICES.md`, header of `VhfProtocol.cs`).
- No upstream driver binary, setup tool or source file is in this repository or in a GamepadEnhancer release. Users obtain a driver
  package from its publisher.
- The other candidate, LizardByte/libvirtualhid, was rejected: its Windows driver is "source-available", not open source, and needs a
  paid licence with online validation. OpenController was rejected: it ships no driver.
- The app adds one Microsoft NuGet package, `System.Security.Cryptography.Pkcs` (MIT), to read the signer of a driver catalog.

## Supported profiles

The profile follows the controller layout selected in the app (Xbox or PlayStation) and is set on the **Network and input > OUTPUT** tab.

| Layout | VHF profiles | Notes |
|---|---|---|
| Xbox | Xbox Series, Xbox One | HID devices with Microsoft's vendor id so Windows' inbox `xinputhid.sys` can attach for XInput. Reported by the project; not verified here. |
| PS4 | DualShock 4, DualSense | HID devices with Sony's vendor id; touchpad click and the PS button are mapped. DualSense adaptive triggers, player LEDs and microphone LED arrive as output events (display only); HD haptics are not possible through VHF. |
| Xbox 360 | none | Use ViGEm. |

Not offered by this app although the driver defines them: Switch Pro, Steam Controller, and the generic HID profiles (disabled upstream
until a public USB VID/PID is allocated).

Controller identity: the vendor/product ids come from the driver, not from this app. They are the real Microsoft and Sony ids because
Windows' Xbox filter driver and games' native libraries match on them; that is a compatibility mechanism, the same one every virtual
gamepad driver uses, not concealment. This app does nothing to hide, rename or disguise the device or the driver.

## Driver signing and installation

The signed driver is **not** part of this app. Upstream publishes its package unsigned on purpose; whoever distributes it has to sign it.

What signing involves (Microsoft documentation, `windows-driver-docs`; see also [VHF_BACKEND_RESEARCH.md](VHF_BACKEND_RESEARCH.md)):

| Route | Needs | Notes |
|---|---|---|
| Test signing | a self-made certificate and Windows in test-signing mode (Secure Boot off) | Development only. This app **refuses** such packages. |
| Third-party release certificate (OV/EV code signing, or a signing service) | a certificate; the signer ends up in the PC's Trusted Publishers store or the user accepts a prompt | Accepted by Windows for user-mode driver packages on normal x86/amd64 editions; not accepted on Windows 10 S mode or on ARM64, which need a Microsoft signature. Whether a given package installs cleanly with Secure Boot on has to be tested. |
| Microsoft attestation signing | a Partner Center Hardware account tied to a company (Entra admin, legal contact, agreements) and an EV certificate | Supports user-mode drivers on Windows 10+. Microsoft describes it for test scenarios and says such drivers are not Windows Certified and cannot be on Windows Update. |
| WHCP / HLK certification | HLK testing per Windows version, submitted through the dashboard | The route Microsoft describes for retail distribution, S mode and ARM64. |

### How the app installs it

1. **Choose package folder...** or **Choose package .zip...** on the OUTPUT tab. A .zip is unpacked into a new temporary folder with
   path-traversal, size and file-count limits.
2. The package is checked (`VhfPackageVerifier`): exactly the five expected files, a `manifest.json` for protocol 2 and this PC's
   processor, SHA-256 of every file equal to the manifest, and the **catalog and setup tool must carry a signature Windows trusts**. A
   package that is unsigned, self-signed, signed with a certificate whose name says test, tampered with or expired is refused, even if
   somebody made this PC trust the test certificate.
3. **Install driver...** checks the package again, shows what was found, asks for confirmation, then runs the package's own setup tool
   (`install --inf ...`) with the normal Windows administrator prompt (UAC), which also shows who signed the tool.
4. **Remove driver...** runs the same tool with `remove` after the same checks and confirmation. Closing the VHF controller first is required.

Limits of the check: it is a pre-check for the user's benefit. Windows' own driver installation checks (catalog membership of the
files, publisher trust, Secure Boot) are the enforcement and are never bypassed. The app never changes Secure Boot, test signing or any
signing policy. A folder can change between the check and the elevated run; the package is therefore checked again immediately before
running, and the UAC prompt shows the signer of the tool that is about to run.

## What the UI shows

OUTPUT tab: backend (ViGEm default / VHF), VHF profile (when VHF is chosen), a status badge (not installed, installed but not running,
ready, in use by another program, protocol mismatch, error), the driver version, date and provider, the signature of the installed
catalog (and whether it is a production signature), the controller connection and the last rumble / light output a game sent,
**Check driver**, package choice, **Install driver...**, **Remove driver...**. The header tag reads VIGEM or VHF; the dashboard and the
window title say which backend created the controller.

Errors are shown with what to do (driver missing, protocol mismatch, profile not offered, controller lost after sleep). If the VHF driver
fails, the app does **not** switch to ViGEm on its own; the output stays as chosen and one click on the OUTPUT tab switches it back.

## Lifecycle

- **Create:** open the driver's control interface, version handshake, create the controller slot (a "busy" answer while Windows is still
  removing the previous child is retried for about half a second).
- **Report:** the engine's report (on change, plus a heartbeat) becomes one `input_state_request`; sticks are signed with positive = up
  and right (DualShock 4 bytes are converted), triggers 0..255.
- **Feedback:** every engine tick the pad polls the driver's feedback slot (at most every 20 ms) and shows rumble / light output. It is
  **not forwarded** to a physical controller.
- **Lost controller:** after sleep or a driver restart the pad recreates the controller (every 500 ms until it works) and resends the held state.
- **Destroy / crash:** closing the control handle makes the driver release every slot it owns, so killing the app does not leave a
  controller behind.

## Known limits

- Not an Xbox 360 device; no generic HID, Switch Pro or Steam Controller profile in this app.
- Rumble and lights are shown, not forwarded to hardware.
- Reported by the LizardByte project: on Windows 10, Xbox Series virtual controllers can cause growing CPU use while `GameInputSvc`
  runs, because of an inbox `xinputhid.sys` bug; its suggested workaround is the Xbox 360 profile (ViGEm here). Not verified by this project.
- Whether a particular game recognises each profile is not established. A controller being visible in Windows does not mean a game uses it.
- Anti-cheat: nothing is known or promised. A virtual controller is visible to games and anti-cheat software as a virtual device, and
  using it may be treated differently from a physical controller, including a kick or a ban. There is no guarantee. Use it only where
  virtual controllers are allowed.
- The driver is a beta project with few contributors; its protocol may change (hence the strict version check).
- ARM64 needs a Microsoft-signed package.

## Automated tests

`tests/GamepadEnhancer.Tests/VhfTests.cs` (runs in CI, no driver needed):

- wire format: sizes, offsets, byte order, IOCTL numbers, GUID, profile bits, feedback decoding and rejection of malformed events;
- client: handshake, error mapping, slot and profile checks;
- pad: button, axis and trigger formatting (Xbox and DualShock 4/DualSense), heartbeat, busy retry, missing driver and profile messages,
  lost-controller recovery, feedback polling, error de-duplication;
- engine: creates and removes the VHF pad, polls feedback while idle, a missing driver fails the connect and leaves the engine stopped,
  ViGEm remains the default and old `settings.json` files keep it;
- package verifier: layout, manifest and hashes, protocol and processor, unsigned / self-signed / test / untrusted / tampered / expired
  rejected, safe .zip handling; the Windows trust check itself is exercised against a Microsoft-signed executable and catalog;
- inspector: not installed, not running, ready, protocol mismatch, in use, error, installed signature shown;
- window: OUTPUT tab, backend and profile choices follow the layout, connect through a fake VHF driver, header and labels, refused
  package keeps the install button off.

## Manual test instructions (Windows, with a real driver)

Record the result next to each line when you run it. Use a PC without the driver first.

1. **Not installed:** OUTPUT tab > Check driver: badge "Not installed". Choose VHF, press Connect: a clear error, the output stays VHF,
   one click on ViGEm and Connect works as before.
2. **Package refusal:** choose the unsigned upstream producer package: refused as "Not signed". Choose a locally test-signed package: refused.
3. **Install:** with a package signed by its publisher, Install driver...: confirmation text shows the signer, the Windows prompt shows
   the same publisher, then Check driver shows version, date, provider and a trusted signature. Secure Boot stays on.
4. **Enumeration:** connect an Xbox Series profile and a DualSense profile in turn: Device Manager (Human Interface Devices) and
   `joy.cpl` show a game controller; `pnputil /enum-devices /class HIDClass` lists the child.
5. **XInput:** with the Xbox Series profile, a tool that calls `XInputGetState` (or a game that uses XInput) sees the controller.
6. **DirectInput / GameInput / SDL:** the same for DirectInput (`joy.cpl` test tab) and a GameInput or SDL3 test tool; note the
   vendor/product ids each reports.
7. **Accuracy:** with the Monitor tab open, move the stick to its extremes and centre; check the test tool shows full deflection both
   ways with no inverted Y, triggers 0 to full, every button once.
8. **Rumble:** run a rumble test in the tool; the OUTPUT tab shows the last output, and shows the DualSense lightbar colour when set.
9. **Reconnect:** put the PC to sleep and wake it; the controller returns within a second or two and held buttons are sent again.
10. **Crash recovery:** end the app in Task Manager while connected; the controller disappears from `joy.cpl`, and the next start can
    connect again.
11. **Removal and reinstall:** Remove driver...; Check driver shows "Not installed"; install again.
12. **Latency and rates** (optional): compare ViGEm and VHF with the same test tool; the engine currently sends at most 100 reports a
    second plus a heartbeat.
