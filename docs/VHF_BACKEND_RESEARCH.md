# Research: a Microsoft VHF virtual gamepad backend for GamepadEnhancer

Status: this is the research that preceded the implementation, which is described in [VHF_BACKEND.md](VHF_BACKEND.md). When it was written no source code had been changed. Nothing here was built, installed or run: the sandbox this was written in is Linux, has no .NET SDK and no Windows, so there are **no benchmarks and no hardware results** in this document. Every statement is marked with where it comes from: **[code]** read in a cloned repository, **[docs]** read in Microsoft's published documentation sources, **[claim]** stated by a project but not verified by me, **[unverified]** not checked.

## 0. Corrections to the task's assumptions

- GamepadEnhancer is **C# / .NET 8 / Avalonia 11**. There is no Rust, TypeScript or JavaScript in the repository (searched for `*.rs`, `Cargo.toml`, `package.json`, `*.ts`) [code]. "Rust integration" therefore means **C# integration** below.
- It does **not capture a physical controller**. It turns keyboard and mouse input (low-level hooks or Raw Input) into a virtual Xbox 360 / DualShock 4 pad. Only the rear paddles are read from hardware, through Windows.Gaming.Input [code].
- Stick curves, deadzones, sensitivity, dynamic/adaptive/Anchor FOV and recoil handling are **not in this app**. They belong to Axiom (which reaches GamepadEnhancer through the "ViGEm Network" TCP connection). In this app the "processing" is the engine in `Core/ControllerEngine.cs`: key-to-button mapping, WASD-to-stick, mouse look, turbo, advanced bindings and layers. A VHF backend only has to carry the engine's final pad state.

## 1. Existing project architecture (relevant part)

| Piece | Where | Notes |
|---|---|---|
| Output thread | `Core/ControllerEngine.cs`, `Loop()` | One thread named "ViGEm output"; one report per tick (10 ms, 100 Hz), `timeBeginPeriod(1)` + `Thread.Sleep(1)` pacing. It is the only writer to the pad. |
| Pad abstraction | `Core/PadInterfaces.cs` | `IXboxPad` and `IDs4Pad` (Connect, Disconnect, SetAxisValue, SetButtonState, SetSliderValue, SubmitReport). Implemented by `ViGEmXboxPad`, `ViGEmDs4Pad` and by `Network/NetworkPad.cs` (sends `km.controller(...)` to another PC). |
| Coupling | same file | The interfaces use **Nefarius.ViGEm.Client types** (`Xbox360Axis`, `Xbox360Button`, `DualShock4Button`, …) in their signatures. A third backend would have to speak those types or the interfaces must be made neutral. |
| Selection | `ControllerEngine.Connect(bool xbox360, RemoteTarget remote)` | Chooses ViGEm vs. network. Profile → layout is data-driven (`Core/Canonical/ControllerLayouts.cs`, `PadFamily` = Xbox360 or DualShock4). |
| Feedback | none | The engine does not read rumble or LED output from ViGEm today [code: no matches]. |
| Package | `GamepadEnhancer.csproj` | `Nefarius.ViGEm.Client 1.21.256` is the only driver-facing dependency. |

Conclusion: there is already a clean seam. The smallest change is a third implementation of the two pad interfaces plus a backend choice in `Connect`, with the Nefarius enums replaced by neutral ones.

## 2. Candidate comparison

Repository metadata (stars, issues, releases pages) could not be read: GitHub's API and web pages were blocked in this sandbox, only `git clone`/`ls-remote` worked. Activity below comes from git history.

| | **Nonary/libvirtualgamepad** | **LizardByte/libvirtualhid** | **SYMBaiEX/OpenController** |
|---|---|---|---|
| Uses Microsoft VHF | Yes: UMDF2 source driver linking `VhfUm.lib`, ships no custom kernel driver [code: README, `driver/`] | Yes for non-Xbox-360 profiles (UMDF2 + VHF); Xbox 360 uses a separate XUSB software-device personality [code: README, `docs/windows-driver.md`] | No driver. Generates VHF driver *source* (KMDF) for you to build and sign; "does not install that driver yet" [code: `docs/windows-virtual-gamepad.md`] |
| Language | C++ driver + C++ client | C++ | TypeScript SDK |
| Activity | 96 commits; last 2026-11-14; tags `v0.1.0-beta.*` (up to at least beta.9) [code] | 50 commits in the clone (depth-limited); last 2026-11-12; bot-assisted [code] | 120 commits; last 2026-10-05 [code] |
| License | **MIT**, whole repo [code: `LICENSE`] | Library MIT, but **the Windows driver, broker and MSI are the "LizardByte Source-Available License 1.0", explicitly "not an open source license"**: non-commercial evaluation only, no redistribution of modified copies, anti-circumvention clauses [code: `LICENSES/license-map.md`, `LicenseRef-LizardByte-SAL-1.0.md`] | MIT |
| Cost / gating | Free | Paid machine license, activated online through a license server, 24 h revalidation, device limits when offline [code: `docs/windows-driver.md`] | Free |
| Profiles | Xbox One, Xbox Series, DualShock 4, DualSense, Switch Pro, Steam Controller (2026). Xbox 360 refused; generic HID disabled pending a public VID/PID allocation [code: README] | Generic, Xbox 360, One, Series, DS4, DualSense, Switch Pro [claim] | Report helpers only |
| Signing story | Producer publishes an **unsigned** package on purpose; downstream consumers sign the catalog and setup tool with their own SignPath request [code: README, `docs/signpath/`] | Production-signed MSI by the vendor, behind the paid license [claim] | None |
| Windows | Win10+ x64/ARM64 build baseline, UMDF 2.15+ [code: README "Build baseline"] | Win11 21H2+ AMD64 supported; Win10 2004+ best effort; ARM64 not available [code: `docs/windows-driver.md`] | n/a |
| Suitable for reuse here | **Yes, with a fork** (see §9) | **No**: license | **No**: no driver |

Not found: another open-source VHF gamepad project that is better. I did not get to run a general GitHub search (blocked), so this is "none that I saw", not "none exists".

### What libvirtualgamepad actually is [code]
- Control interface: a private WDF device interface (GUID in `include/libvirtualgamepad/protocol.h`) on a root-enumerated node `ROOT\VIBESHINEVIRTUALGAMEPAD`. Fixed-size, versioned, `METHOD_BUFFERED` IOCTLs (`CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800…0x808, …)`). Controllers are owned by the control handle: closing it releases every slot, so a crashed app cannot leave a pad behind.
- Input: `input_state_request` = `{header, controller_id, buttons (u32), left_x/y, right_x/y (i16), left/right trigger (u8)}`, plus touch, motion and battery requests for the PlayStation pads. The driver converts to each profile's native report.
- Output: HID output reports are parsed in the driver and returned through a bounded, last-event-wins `poll_feedback()` (rumble, Xbox four-motor rumble, PlayStation lightbar/LEDs/adaptive triggers). Error codes are documented per call in `client.h`.
- Lifecycle: the pad is deleted on sleep/hibernate/shutdown; the next call returns `ERROR_DEVICE_REMOVED` and the caller recreates it. `create` can return `ERROR_BUSY` until PnP removes the previous child (needs bounded retry).
- Tests: descriptor and contract unit tests, a Windows CI workflow (`windows-2022`, ctest) and hardware probes (HID/DirectInput/GameInput/SDL identity, DS4/DS5 USB feature reports) [code]. CI results were not looked at.
- **Maturity: beta.** Version numbers are `0.1.0-beta.N`; the README says a profile becomes available "only after" descriptor, mapping and application behaviour are tested, and the generic profile is disabled. "It builds" is not what I am relying on, but I also have not run it.

## 3. How VHF works [docs]

Source: `MicrosoftDocs/windows-driver-docs`, `windows-driver-docs-pr/hid/virtual-hid-framework--vhf-.md` (page dated 2025-04-22). learn.microsoft.com itself was blocked, so this is the published markdown source of the same pages.

- Windows 10+. You write a **HID source driver**; the in-box `Vhf.sys` is loaded as a lower filter and **enumerates child HID devices**; `Hidclass.sys`/`Mshidkmdf.sys` expose the top-level collections to applications as for real hardware.
- The driver creates a device with `VHF_CONFIG_INIT` + `VhfCreate`, then `VhfStart`; it submits input reports with `VhfReadReportSubmit`. VHF handles descriptor/attribute IOCTLs itself; `WriteReport`, `GetFeature`, `SetFeature` and `GetInputReport` reach the source driver only through callbacks it registers (otherwise `STATUS_NOT_SUPPORTED`). That callback path is where **rumble/output reports** come from.
- The VHF page I read says "In this release, VHF supports a HID source driver only in kernel mode", while the page text describes KMDF/WDM. libvirtualgamepad nevertheless links the user-mode import library `VhfUm.lib` and runs as UMDF2 [code]. The two do not agree in the sources I could read; I could not read the DDI reference pages (`ns-vhf-_vhf_config`) to settle it. The upstream project's CI and probes are the evidence that UMDF works, not Microsoft's page.
- The VID/PID, `HardwareIDs`, version and string identity of the child come from `VHF_CONFIG`. libvirtualgamepad sets `config.HardwareIDs` at runtime [code: `driver/src/driver.cpp`].

**VHF does not make an Xbox 360 device.** A real 360 pad is an XUSB device on a USB bus; VHF creates HID children, not bus children. libvirtualgamepad refuses the profile for exactly this reason [code: `docs/PROFILE_CONTRACT.md`].

## 4. Controller profiles and what is actually known

"Visible in Windows" is not "recognized by a game". For each profile, what the sources say:

| Profile | How it is exposed | XInput | DirectInput / HID | Rumble / LEDs | Evidence level |
|---|---|---|---|---|---|
| **Xbox Series** (VID 045E, PID 0B12 / 02FF) | HID child with a native Series report layout and hardware IDs `HID\VID_045E&PID_xxxx&IG_00`, which makes the inbox `xinputhid.sys` filter attach [code: `driver/src/xbox_series.cpp`, README "XInput"] | Via `xinputhid.sys`, according to the project | Visible as HID | Four-motor rumble payload decoded [code] | **[claim]** by the repo; its docs/tests describe SDL/GameInput identity probes. I have not seen a game or `XInputGetState` result. |
| **Xbox One** | Same route, PID 02EA | Same | Same | Same | Same |
| **Xbox 360** | Not possible with VHF | — | — | — | Keep **ViGEm** for this |
| **DualShock 4** | Native report shape, touchpad, motion, battery, lightbar; USB feature reports [code: `docs/DS4_USB_COMPATIBILITY.md`] | No (HID only) | Yes | Rumble + lightbar | Contract tests + hardware probes in repo [claim] |
| **DualSense** | As DS4 plus adaptive triggers, player LEDs, mic LED; USB feature reports that some games' native libraries require [code: `docs/DS5_USB_COMPATIBILITY.md`] | No | Yes | Rumble, triggers, LEDs; **no HD haptics** (needs a USB audio function VHF cannot provide) | Same |
| **Generic HID gamepad** | Descriptor exists but the public driver **refuses it** until the project gets a public VID/PID; pid.codes `1209:0001` is test-only and must not ship [code: `docs/PID_ALLOCATION.md`] | No | Would be | PID force feedback encoder exists, unverified | Not available |

Compatibility limits that matter for this app:
- A pad that only appears in `joy.cpl` / HID does **not** work in XInput-only games. For those, the Xbox Series route depends on `xinputhid.sys` attaching, which is a Microsoft inbox driver behaviour the project relies on.
- **Windows 10**: LizardByte's docs say Xbox Series virtual controllers can cause progressive CPU/FPS loss while `GameInputSvc` runs, because of an inbox `xinputhid.sys` bug, and suggest the Xbox 360 profile as a workaround [claim: `libvirtualhid/docs/windows-driver.md`, issue #194, nefarius research]. It concerns the inbox driver path, so it likely applies to any Xbox Series-via-VHF device, including libvirtualgamepad's. **Not verified by me.**
- Anything using Raw Input/HID directly will see the real vendor/product IDs the profile declares.

## 5. Production signing [docs]

Sources: `windows-driver-docs-pr/dashboard/*.md` and `install/*.md` in MicrosoftDocs/windows-driver-docs (dates in the pages: 2025–2026).

1. **Test signing** (self-signed cert): the test machine needs test-signing mode, which on a Secure Boot machine means disabling Secure Boot. libvirtualgamepad's local build does exactly this and its script "deliberately does not alter" that setting [code]. **Development only; never a release.**
2. **Third-party release certificate (OV/EV Authenticode, e.g. via SignPath)**: Microsoft's PnP installation rules say a package counts as signed if the **catalog is signed by WHQL or by a third-party release certificate**; the kernel-mode code-signing policy additionally requires Microsoft signing only for *kernel-mode* drivers [`pnp-device-installation-signing-requirements…`, `signing-drivers-for-public-release…`]. Caveats in the same sources: Windows 10 in S mode and non-x86/amd64 editions (**ARM64**) require a catalog signed by WHQL; and a third-party-signed driver needs the signer in the machine's **Trusted Publishers** store, otherwise the user is prompted [`deprecation-of-software-publisher-certificates…`, `digital-signatures-and-pnp-device-installation…`]. This is the route libvirtualgamepad's pipeline assumes (it signs the `.cat` and the setup tool downstream). Whether Windows 11 with Secure Boot/HVCI installs this particular UMDF+VHF package with only that prompt is **[unverified]**: it must be tested on a real machine before committing to this route.
3. **Attestation signing (Microsoft)**: needs a Hardware Developer Program account (Microsoft Entra global admin sign-in, company details or D-U-N-S, a legal contact who signs agreements, four agreements to accept) and an **EV code-signing certificate** registered to the account; you submit a CAB of INF/DLL/etc.; Microsoft regenerates and signs the catalog. Supports user-mode and kernel-mode, Windows 10+. The docs frame it as signing "for testing scenarios", say attestation-signed drivers are **not Windows Certified** and **cannot be published to Windows Update for retail**; for that you need WHCP [`driver-signing-offerings.md`, `code-signing-attestation.md`, `hardware-program-register.md`]. The wording makes it unclear how Microsoft views attestation for self-distribution; I would confirm with the Hardware Dev Center before relying on it.
4. **WHCP / HLK certification**: full Hardware Lab Kit run per Windows version, submitted through the dashboard; this is the only route Microsoft describes for retail/Windows Update distribution and for S mode/ARM64. Cost and calendar time are **[unverified]**.

What needs something external, whichever route: an organisation-grade code-signing certificate (EV for options 3 and 4; an OV/EV or a signing service for option 2) and, for 3 and 4, a Microsoft Partner Center hardware account tied to a legal entity. A solo developer without a company may find the registration step a blocker; I did not verify that.

Version management: the INF `DriverVer` must increase for Windows to pick the new package. libvirtualgamepad derives it from commit date and count and its setup tool reports when a restart is needed [code]. The consumer is responsible for the signing of every update.

Secure Boot: no step in the recommended path disables it. Only the test-signing route does.

## 6. Security and anti-cheat considerations

- **How Windows identifies the device**: a root-enumerated source node (`ROOT\VIBESHINEVIRTUALGAMEPAD` upstream) with a VHF-created HID child carrying the profile's hardware IDs and VID/PID [code]. The keyboard/mouse source keeps VHF's default `0000:0000` [code].
- **Real vendor IDs are used** for Xbox (045E) and PlayStation (054C) profiles because the XInput filter and games' native libraries match on them [code: `PID_ALLOCATION.md`]. That is how every virtual gamepad driver works, including ViGEm. It is a compatibility mechanism, not concealment, and this project must say so plainly in its documentation. Whether shipping those IDs has trademark/USB-IF implications for a redistributed product is **[unverified]** and should be checked.
- **Anti-cheat**: I found no evidence either way about any specific anti-cheat and made no tests. A signed driver does not change what a game or anti-cheat can enumerate (the device is visible as a virtual HID device in the normal device tree). **No claim of compatibility or safety is made.** Nothing in this task involves hiding, renaming or spoofing to avoid detection, and I recommend the implementation does not add any.
- Authorization: control handles own their controller slots; a different handle gets `ERROR_ACCESS_DENIED` [code]. The consumer app should still restrict who can open the control interface (it is a local, admin-installed interface; the default ACL was not checked).
- Licensing and provenance: libvirtualgamepad is MIT (`Copyright (c) 2026 Chase Payne`), so redistribution needs the notice kept. The DS5 notes reference Inputtino's `ds5_usb.hpp` as the source of some protocol knowledge, and the profile contract mentions a provenance review of descriptors [code]. A **third-party attribution audit of the descriptors and protocol data is required before shipping**; no `NOTICE`/third-party file was seen at the repo root.

## 7. C# integration approach

No Rust; a typed C# client is the right shape.

- `Output/Vhf/VhfClient.cs`, internal, mirrors `client.h`: finds the interface GUID with `SetupDi*`, `CreateFile` with the access the protocol needs, then `DeviceIoControl` with blittable `[StructLayout(LayoutKind.Sequential, Pack = 1)]` structs copied from `protocol.h` (fixed size, no pointers, versioned). One handle per process, serialized by the caller (the library documents it as not thread-safe).
- A thin `VhfPad : IXboxPad/IDs4Pad` adapter on the existing seam, which keeps the engine's single output thread (no extra threads; `DeviceIoControl` is synchronous). A separate small poller for `poll_feedback()` only if rumble is wanted (the engine has none today).
- Error mapping: map the documented Win32 codes (`ERROR_DEVICE_REMOVED` → recreate, `ERROR_BUSY` → bounded retry, `ERROR_REVISION_MISMATCH` → driver/app version mismatch message, `ERROR_NO_MORE_ITEMS` → no feedback) to the existing status text. Never swallow errors silently.
- Driver version check on connect (`query_info`: protocol version and available profile mask) so an old driver gives a clear message.
- Prerequisite refactor: make `IXboxPad`/`IDs4Pad` use app-neutral enums so the engine no longer depends on `Nefarius.ViGEm.Client` types; `ViGEm*Pad` and `NetworkPad` adapt to them. This is the only change inside the engine and is mechanical.
- Packaging: the signed INF/DLL/CAT plus the setup tool installed by a separate, explicit "Install virtual gamepad driver" step with admin consent; never auto-installed.

Polling rates: input reports are submitted by the app, so the rate is whatever the output loop sends. The engine ticks at 100 Hz [code]; 125–1000 Hz would require changing the tick, which is separate work and is not implied by choosing VHF. **No latency or CPU figures exist yet.** Plan: a benchmark harness that sends N reports through each backend and timestamps arrival with a HID reader on the same machine; compare ViGEm vs. VHF only on those measurements.

## 8. Recommended approach (decision)

**Option B: fork libvirtualgamepad and keep upstream changes small and visible.**

- Reuse, not rewrite: the driver (UMDF/VHF), the protocol and the profile encoders already exist, are MIT-licensed, and use Microsoft's official framework.
- Why a fork rather than using it unchanged: upstream is built for another product ("Vibeshine"). The device node, INF, interface GUID and package names are Vibeshine's. Two products that ship the same hardware ID and GUID would collide on one machine, and the producer deliberately publishes an **unsigned** package for each consumer to sign. So we need to (1) rename the identifiers, (2) build and sign our own package, and (3) track upstream.
- Maintenance burden: beta software with a small contributor set; the protocol is versioned but still moving (`k_protocol_version = 2`). Keep our patches to a single rename/branding commit set so upstream merges stay mechanical; pin to a tagged upstream release.
- Option A (reuse unchanged) is not possible without Vibeshine's signing. Option C (custom driver) is not justified.

Out of scope and unchanged: ViGEm remains the default backend and the only Xbox 360 (XUSB) path; the Network backend stays; profiles and presets are unaffected; Axiom is unaffected.

## 9. Complexity and roadmap

| Step | Work | Rough size |
|---|---|---|
| 0 | Hardware spike on a clean Windows 11 machine: build upstream, test-sign, install, create a Series and a DS4 pad, confirm XInput / DirectInput / a real game; decide the signing route (§5) | days; needs a Windows machine |
| 1 | Provenance and licence audit of upstream descriptors; rename identifiers in a fork | small |
| 2 | Neutral pad enums + `IPadOutput` refactor (engine unchanged otherwise); ViGEm and Network adapted | small-medium, fully testable in CI |
| 3 | `VhfClient` (P/Invoke) + `VhfPad`, fake driver in tests that implements the same IOCTL contract | medium |
| 4 | UI: backend choice per layout, driver status, explicit install/uninstall, clear "Xbox 360 stays on ViGEm" text | small-medium |
| 5 | Optional rumble/LED feedback | medium |
| 6 | Signing and release pipeline (certificate, Partner Center account if attestation/WHCP, CI signing, driver version bumps) | **the long pole**; depends on the route chosen |
| 7 | Benchmarks vs. ViGEm; compatibility notes per game, with what was actually tested | medium |

Hard dependency: steps 3–7 cannot be validated without Windows hardware; CI can only compile and run unit tests against a fake driver.

## 10. What I could not verify

- Microsoft's DDI reference pages (`VHF_CONFIG`) and learn.microsoft.com were unreachable; I read the published markdown source instead. The kernel-only vs. UMDF wording in the VHF article conflicts with the upstream project's working UMDF design and is unresolved.
- Stars, issue counts, releases and open PRs for all three repositories.
- Whether a third-party-signed (SignPath/OV) UMDF+VHF package installs on Windows 11 with Secure Boot and what prompt appears.
- Whether Xbox Series-via-`xinputhid.sys` is recognized by real games, and the Windows 10 `GameInputSvc` problem.
- Anti-cheat behaviour of any kind.
- Any latency, CPU or stability figure.
- GamepadEnhancer's own licence compatibility with MIT redistribution (check the repository licence before bundling).
