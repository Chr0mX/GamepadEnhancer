## Beta 1.5

Update on top of Beta 1.4 (Avalonia UI, aim/fire triggers, Circle preset and Loop, filter algorithms).

### New: Input capture method
- **Input capture** dropdown in the header: **Low-Level Hooks** (the original method, default) or **Raw Input**.
  - **Raw Input** uses `RegisterRawInputDevices` with `RIDEV_INPUTSINK` (background capture) and device notifications on its own hidden-window thread. It does **not** use `RIDEV_NOLEGACY`, so normal mouse/keyboard input still reaches every other window; nothing is blocked, intercepted or injected, and no driver is used.
  - Only one method is active at a time. Both feed one input-state manager, so all mappings, Hold/Toggle, turbo, aim/fire, the filter button and ViGEm output behave the same with either method.
- **Switching is clean:** the old method is stopped first, then every held key/mouse button is released, then the new method starts, so nothing stays stuck or gets duplicated. A Toggle that was switched on stays on.
- **Device unplug** (Raw Input): whatever the removed mouse or keyboard was holding is released. The service unregisters its devices and destroys its window when stopped.
- **Failure handling:** if a method can't start, the previous one is restored and the status bar says why; if the saved Raw Input can't start at launch the app uses Low-Level Hooks for that session; if Raw Input fails while running the app switches to Low-Level Hooks.
- The choice is saved app-wide in **`settings.json`** next to the exe (not per profile, so loading a profile never changes it).
- This app injects no mouse/keyboard input of its own (its only output is the ViGEm virtual gamepad), so it can't feed back into capture. Input injected by other software (SendInput) is treated like physical input with both methods. Details and a test recipe: `docs/INPUT_CAPTURE.md`.

### Known limitations
- Raw Input has not been run on real Windows hardware yet; please report any differences from Low-Level Hooks (for example keys or buttons not registering). Switching back to Low-Level Hooks is instant.
- If your game runs as administrator, run this app as administrator too; whether Raw Input is affected the same way has not been tested.

### Profiles
- No profile changes; existing profiles and `patterns/` files load as before.
