# Controller monitor and output types

## Output types (Xbox 360 / DualShock 4)

Both virtual controllers come from the same driver and client: ViGEmBus with `Nefarius.ViGEm.Client` can create an Xbox 360 pad (`CreateXbox360Controller`) and a DualShock 4 pad (`CreateDualShock4Controller`). No second driver is needed, and the existing ViGEm code is unchanged. The output stays modular: the engine talks to `IXboxPad` / `IDs4Pad`, implemented by the ViGEm pads and by the network sender.

- Pick **Xbox 360** or **PS4** in the header dropdown. Changing it **while connected** replaces the pad in place: the old virtual device is disconnected and its ViGEm client disposed before the new one is created, so only the selected type is exposed. No restart.
- The header shows which output is active (for example `Output: PlayStation 4 DualShock 4 Controller (ViGEm)`), and so does the window title.
- Loading a profile never swaps a live pad; it only sets the dropdown while disconnected.
- A new **Guide / PS** action (bindable like any other) drives the Xbox Guide button or the PS button. It is not sent over the Network API (the protocol has no id for it).

| Action | Xbox 360 | DualShock 4 |
|---|---|---|
| A / Cross, B / Circle, X / Square, Y / Triangle | A, B, X, Y | Cross, Circle, Square, Triangle |
| LB / L1, RB / R1 | LB, RB | L1, R1 |
| LT / L2, RT / R2 | analog LT, RT | analog L2, R2 (and the L2/R2 buttons) |
| Back / Share, Start / Options | View, Menu | Share, Options |
| LS / L3, RS / R3 | L3, R3 | L3, R3 |
| D-pad | four direction bits | one hat switch (up+down or left+right cancel) |
| Guide / PS | Guide | PS |

Not implemented (not requested, and not needed by the current engine): Xbox One / Series, DualSense, generic HID, running two pads at once.

## Monitor tab

A live picture of the virtual controller: an Xbox-style pad or a DualShock 4, drawn with that pad's own layout (sticks, face buttons, D-pad, touchpad, share/options, PS or Guide), not one picture with swapped labels.

**View** chooses which stage you see:

| View | Meaning |
|---|---|
| Physical input | What your keyboard, mouse and network commands ask for, before any processing: bound keys held (no turbo), the mouse-look stick, remote controls. GamepadEnhancer does not read a physical gamepad, so there is no physical-pad connection status. |
| Processed input | After mouse look and turbo, before rounding into a report. |
| Final output | Exactly what is written to the virtual pad: rounding and the DualShock 4 hat rules. This is what the game receives. |

Sticks show position (the knob eases back to centre in about 45 ms; moving away from centre is never delayed), triggers fill with their analog value and show a percentage, and pressed buttons light up. Readouts show the connection (ViGEm, or "sending to the other PC"), stick and trigger values, the output rate in reports per second, and the last button presses of the chosen view.

Settings (saved in `settings.json`, not in profiles): Live visualization on/off, view, size 50-200 %, refresh 30-144 Hz, compact mode (no labels, no history), pop-out window, **always on top**, and **overlay mode** (frameless, transparent, only the controller; drag to move, Esc or right click closes).

## Performance

The engine publishes immutable snapshots with plain reference swaps; the UI reads them on its own timer. Nothing in the output thread waits on the UI. Snapshots are only built while a monitor is actually showing (the Monitor tab is selected and the window is not minimized, or a pop-out is open); otherwise the cost is one volatile read per tick. A redraw happens only when the state changed or a stick is still easing back. Button history is a fixed ring, so it cannot grow.
