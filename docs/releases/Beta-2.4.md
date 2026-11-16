## Beta 2.4

### Added: input blocking and mouse look
- New **Block & Mouse** tab. While a listed program is focused and the pad is connected, the keys and mouse buttons you mapped to pad actions (and mouse movement, when mouse look is on) are swallowed, so only the virtual controller reaches the game. Off by default; a hotkey (default Scroll Lock) pauses it and is never blocked.
- **Mouse buttons and the wheel** (`Mouse Left/Right/Middle/X1/X2`, `Wheel Up/Down`) can now be bound to pad actions on the PC to Controller tab.
- **Mouse look:** mouse movement drives the right stick, with sensitivity, response curve, game dead zone and a release time. Saved per profile.
- Details, safety rules and risks: [docs/BLOCKING.md](../BLOCKING.md).

### Technical
- Hooks now read the injected flags (injected input is never blocked), X buttons and wheel direction; Raw Input reads X1/X2, wheel direction and relative movement.
- First unit tests (`tests/GamepadEnhancer.Tests`), run by CI.

### Known
- Not yet run on Windows hardware; the build and unit tests pass on CI. See "Known risks" in docs/BLOCKING.md.
