## Beta 2.6

### Removed: stick drift, right-stick filtering, recoil and disengage
Axiom now owns aiming, recoil and stick behaviour and drives the pad over the Network API, so these features are gone from GamepadEnhancer:

- Base drift and the pull direction.
- Right stick filtering (RC-style oscillation and exponential low-pass), its button, mode and rate.
- The Recoil Pattern tab, pattern files (`patterns/`), Center Radius, Start Delay, Loop and the aim / fire trigger settings.
- Wheel and key "disengage" (it only existed to neutralize the drift).

The right stick is now mouse look (when on) or the value a Network API client sets; the left stick and triggers come from the client; buttons come from key, mouse and wheel bindings (with turbo) and the client. The Controller and Recoil Pattern tabs and the drift readout are removed, and the documentation site no longer has the drift, recoil and filtering sections or their labs.

### Profiles
Old profile files still load. Their drift, filtering, recoil and disengage fields are ignored and are not written back on the next save. The shipped `Default.json` now holds only the controller type, bindings and turbo.

### Also
- Mouse buttons are pushed to a Network API client that sends `km.mouse_stream(1)` on the same connection (see [docs/NETWORK_API.md](../NETWORK_API.md)).

### Known
- Not yet run on Windows hardware; the build and unit tests run on CI.
