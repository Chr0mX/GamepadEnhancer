# Rear paddles

Up to four rear paddles can be mapped independently: **Left Upper**, **Left Lower**, **Right Upper**, **Right Lower**. Each can be enabled or disabled and can press:

- a **virtual controller button** (any of the 18 pad buttons, including the Touchpad click),
- a **keyboard key**,
- a **mouse button** (Left, Right, Middle, Mouse 4, Mouse 5).

Paddles are saved in every profile (a `Paddles` block) and therefore per game. Older profiles have none and load with every paddle off.

## Where this came from
There was **no earlier paddle implementation** to restore. A search of this repository's full history, of the project it was seeded from (`vigem-for-a-game`) and of Axiom found none (the only "paddle" matches are PaddleOCR in Axiom). Until now GamepadEnhancer read only the keyboard and mouse and never a physical controller (HID devices are deliberately ignored by its Raw Input reader). Paddles are physical-controller inputs, so a new input source was added.

## How it works
- `WgiPaddleSource` reads the connected controller through **Windows.Gaming.Input** (`GamepadButtons.Paddle1..Paddle4`).
- The engine reads it **once per tick** (the existing 10 ms loop): no new thread and no new polling rate.
- `PaddleProcessor` turns readings into outputs: pad-button targets are merged into the same button mask as keys, turbo and the Network API; key / mouse targets are sent with `SendInput` after the engine lock is released.
- The controller monitor's Physical and Processed views include paddle-driven buttons.

## Reliability rules
- Edge based: one press = one output, one release = one output; holding never repeats.
- Every paddle is independent, so simultaneous paddles work.
- Changing, disabling or unmapping a paddle while it is held releases the old target at once; the new target needs a fresh press.
- **Disconnect** releases everything a paddle was holding. On **reconnect**, a paddle that is already held is ignored until it is released.
- Stopping the engine or closing the app releases every injected key and mouse button.
- A key a paddle is pressing itself is recognized and ignored by the engine's own input handling, so it cannot loop back as a user key press.
- Two paddles cannot be given the same target (the editor refuses and says which paddle has it); an imported profile with duplicates keeps the first and clears the rest.

## Hardware capability detection
Windows has no "how many paddles" query. The app therefore:
- treats **Xbox Elite Wireless Controller Series 1 and 2** (vendor `0x045E`, products `0x02E3`, `0x0B00`, `0x0B05`, `0x0B22`) as having four paddles,
- treats any other controller as having none **until a paddle is actually seen pressed** (it then becomes available),
- dims paddles the connected controller does not have and labels them *(not detected)*; you can still map them (the profile stays portable).

**Detect paddle...** (in the editor for a paddle) assigns the physical paddle you press to that slot, because Windows does not say which paddle is upper or lower. The result is stored in `settings.json` (`PaddleSourceIndex`).

## Limits you should know about
- **Only controllers Windows.Gaming.Input lists as a Gamepad are visible** (Xbox-class pads, including Elite). Pads that expose back buttons another way (for example the DualSense Edge) are not detected. Supporting one means adding another `IPaddleSource`.
- **Background input is not guaranteed.** The paddles are read by polling Windows.Gaming.Input from the engine thread, with no window needed. Whether Windows delivers readings while another window has focus depends on the Windows version and the device; this could not be tested here. If paddles stop responding while a game has focus, that is the cause, and a Raw Input or XInput based source would be the alternative.
- Hardware behaviour and the Windows API calls were **not run on real hardware** while writing this; CI compiles and runs the logic tests (processing, conflicts, capability rules, persistence) only.
