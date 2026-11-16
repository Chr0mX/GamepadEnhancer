# Input Capture Method

Header dropdown **Input capture**: how the app sees your physical mouse and keyboard. **Exactly one method is
active at a time.** The choice is saved app-wide in `settings.json` next to the exe (not per profile, so loading a
profile never changes it). Default: Low-Level Hooks.

| Method | How it works | Notes |
|--------|--------------|-------|
| **Low-Level Hooks** | `SetWindowsHookEx` `WH_MOUSE_LL` + `WH_KEYBOARD_LL` (`GlobalInputHook.cs`), the original method | Runs on the UI thread. Events are passed through, never blocked. |
| **Raw Input** | `RegisterRawInputDevices` (generic mouse 1/2 and keyboard 1/6) with `RIDEV_INPUTSINK` (background capture) + `RIDEV_DEVNOTIFY`, `WM_INPUT` on a hidden window (`Input/RawInputService.cs`) | Own thread. **No `RIDEV_NOLEGACY`**: normal mouse/keyboard messages still reach every other window. Nothing is intercepted, blocked or injected; no driver is used. |

## Structure (kept apart from the engine)
```
LowLevelHookCapture  \                                   /-> ControllerEngine (IInputSink)
                      >-> InputStateManager (held state) -
RawInputService      /                                   \-> OnKey / OnMouseButton / OnWheel
```
- `Input/InputStateManager.cs` is the unified input-state manager: tracks held keys/buttons (and the device that holds
  them), accepts events only from the active method, converts virtual keys to the existing key names, and forwards to the engine.
- `Core/ControllerEngine.cs` contains no capture code; it only implements `IInputSink`. All mappings, Hold/Toggle,
  turbo, aim/fire, the filter gate and ViGEm output are unchanged.

## Switching and cleanup
- Switching stops the old method first, then **releases everything that was held** (keys and mouse buttons are sent as "up"),
  then starts the new one. Events from a stopped method are dropped, so nothing is duplicated and nothing stays stuck.
- A Toggle that was switched on (aim, filter) is not a held state and stays on across a switch; press it again to turn it off.
- If you keep holding a button while switching, the new method only sees its release; that stale "up" is ignored.
- If a method fails to start, the previous method is restored and the status bar says why. If the saved method (Raw Input)
  can't start at launch, the app falls back to Low-Level Hooks for that session (the saved choice is only changed when you pick one).
- If Raw Input fails while running, held inputs are released and the app switches to Low-Level Hooks.
- **Device disconnection** (Raw Input): `WM_INPUT_DEVICE_CHANGE` / `GIDC_REMOVAL` releases whatever the unplugged mouse or keyboard was holding.
- On stop the service unregisters its devices (`RIDEV_REMOVE`), destroys the hidden window, unregisters the window class and joins its thread.

## Raw Input details
- Key events: `RAWKEYBOARD.VKey` + `RI_KEY_BREAK`. Ignored: VKey `0xFF`/`0`, and the "fake shift" (scan code `0x2A` with the E0 prefix) that
  Windows inserts around some NumLock/arrow combinations. Mouse: `RI_MOUSE_*_BUTTON_DOWN/UP` for left/right/middle and `RI_MOUSE_WHEEL`.
  Raw movement is not used.
- Only one window per process can receive raw input for a device class; this registration replaces any earlier one in the process.
- Key auto-repeat downs are forwarded in both methods (same as the original hooks).

## Are synthetic (injected) inputs visible to Raw Input?
- **This app injects no mouse/keyboard input.** A search of the source finds no `SendInput`, `keybd_event`, `mouse_event` or
  `SetCursorPos`. Its only output is the ViGEm virtual gamepad. Raw Input is registered for the generic mouse and keyboard only
  (not the gamepad usage 1/5), so the virtual controller can never feed back into capture.
- **Input injected by other software** with `SendInput` (AutoHotkey, macro tools, remote-desktop tools): per Windows' documented
  behavior it produces `WM_INPUT` with `hDevice == NULL`, so Raw Input does receive it and it can be recognised by the NULL handle.
  Low-level hooks also see it (flagged `LLMHF_INJECTED` / `LLKHF_INJECTED`). The app does not filter either flag, so injected input
  maps exactly like physical input in both methods. `RawInputService.SyntheticEventCount` counts these events.
  Drivers that create a real virtual HID device appear as a normal device with a non-NULL handle.
- **What was and wasn't verified:** the NULL-handle case is covered by the parser tests (a buffer with `hDevice = 0` is delivered and
  flagged synthetic), but this was developed on Linux, so the live Windows behavior above comes from documentation and was **not**
  tested against a real `SendInput`. To confirm on Windows: select Raw Input, bind a key, run an AutoHotkey script that `Send`s it,
  and check the mapped button reacts; repeat with Low-Level Hooks.
- If the game runs as administrator, run this app as administrator for either method (whether Raw Input is affected the same
  way was not tested).
