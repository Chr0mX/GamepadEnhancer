## Beta 2.3

### Added: mouse button UDP stream for Axiom
- New card on the **Network** tab: *Send mouse buttons (UDP)*. It streams this PC's physical mouse buttons (left, right, middle, side 1, side 2) over UDP to the PC running Axiom, so Axiom can use a button as its aim trigger when the mouse is attached to this PC.
- Off by default. Set Axiom's address and port (default 5602) and an optional matching token. Packets go out on every change plus a 100 ms heartbeat; Axiom releases the buttons if the stream stops.
- Works with either input capture method (it reads the buttons directly, so side buttons work too).
- Packet format: `docs/NETWORK_API.md` (section "Mouse button UDP stream").

### Notes
- Not yet compiled or run on Windows when written; please check the stream against Axiom's remote button settings.
