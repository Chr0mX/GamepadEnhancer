# Network API (MAKCU-style commands)

Lets another computer drive the virtual controller with MAKCU/MAKXD-style text commands over TCP. It works in two directions with the same app:

- **Receiver** (the PC with ViGEmBus and the game): turn on the **Network** tab, press **Connect** for the controller, give the other PC the address, port and token.
- **Sender** (optional, the PC with your mouse and keyboard): run this same app, on the **Network** tab turn on **Send to another PC**, enter the receiver's address, port and token, then press **Connect**. The sender runs your drift, recoil and filtering settings and relays the result. It does not need ViGEmBus.

Any MAKCU client that can open a TCP socket and send text lines can also talk to the receiver.

## Security
- **Off by default.** Nothing listens until you tick *Enable Network API*.
- A client must send `auth <token>` as its first line; a wrong token closes the connection. The token is a random 24-character secret created on first enable (stored in `settings.json`, never logged). *New token* replaces it.
- Anyone on your network who has the token controls the pad. Keep the port firewalled, or untick *Allow other computers* to listen on this PC only. The traffic is plain TCP (not encrypted); use a VPN or SSH tunnel across untrusted networks.
- One client at a time; a new authenticated connection replaces the old one. When the client disconnects, times out (30 s without any line) or the pad disconnects, everything it set is released.

## Protocol
One command per line (`\n` or `\r\n`), max 256 characters. Queries answer `result` then `\r\n>>> `; commands that change something are silent (unless `km.echo(1)`); anything wrong answers `ERR`.

```
auth <token>                      first line; answers OK
km.controller(id,value[,hold_ms]) set one control; hold_ms releases it after that many ms (0 = keep)
km.controller_clear()             release every remotely set control
km.controller(0|1)                accepted, no effect (input streaming is not applicable)
km.mouse(mask)                    the sender's mouse buttons (see below); km.mouse() queries
km.version()  km.api_version()    identification (api version 1)
km.echo(0|1)  km.echo()           echo commands back, or query
```

### Control ids
The MAKCU docs give sticks and triggers as ids 10 to 15. The button ids 0 to 9 and the D-pad ids 16 to 19 below are **this app's mapping** (not confirmed against MAKXD firmware); the table is in `Core/RemoteControlState.cs`.

| id | Control | Value |
|---|---|---|
| 0 / 1 / 2 / 3 | A (Cross) / B (Circle) / X (Square) / Y (Triangle) | 0 release, anything else press |
| 4 / 5 | LB (L1) / RB (R1) | 0 / 1 |
| 6 / 7 | Back (Share) / Start (Options) | 0 / 1 |
| 8 / 9 | Left / right stick click | 0 / 1 |
| 10 / 11 | Left / right trigger | 0 to 1023 |
| 12 / 13 | Left stick X / Y | -32768 to 32767 |
| 14 / 15 | Right stick X / Y | -32768 to 32767 |
| 16 / 17 / 18 / 19 | D-pad Up / Down / Left / Right | 0 / 1 |

Stick **+Y is down on every controller type** (the MAKXD convention); the app flips it for Xbox 360, where the raw Y axis counts up.

### How it merges with the local settings
- A remotely set **right stick** replaces the drift, recoil and filtering for that tick (set X and Y together; an unset axis counts as neutral). When the remote releases it, the local behavior resumes.
- **Buttons** are OR-ed with the local key bindings. **Triggers** set remotely override the local digital trigger bindings.
- The **left stick** is only driven while a remote value is set.

## Examples
```bash
# Linux/macOS/WSL
printf 'auth YOURTOKEN\nkm.controller(14,16384,500)\n' | nc 192.168.1.20 5555
```
```python
import socket
s = socket.create_connection(("192.168.1.20", 5555))
s.sendall(b"auth YOURTOKEN\n")
print(s.recv(64))                      # b'OK\r\n>>> '
s.sendall(b"km.controller(15,12000)\n") # right stick down
s.sendall(b"km.controller_clear()\n")
```

## Sender notes
- The sender sends only changed values and a keep-alive about every 5 seconds, and reconnects by itself if the link drops; the full state is sent again after a reconnect.
- On disconnect it sends `km.controller_clear()` so no stick or button stays held on the receiver.
- The sender's token, address and port are stored in plain text in its `settings.json`.
- With *Send to another PC* on, **Connect** reaches the other PC instead of ViGEm; the receiver must already be connected, otherwise it answers `ERR not connected`.

## Mouse buttons (`km.mouse`)
The same connection can carry this PC's physical mouse buttons, so a remote program can use one as a trigger button (for example Axiom's aim key). There is no separate stream, port or token any more: it uses the **Send to another PC** address, port and token, with **Also send this PC's mouse buttons** ticked, and one **Connect**.

```
km.mouse(mask)    the sender's buttons: bit0 left, bit1 right, bit2 middle, bit3 side 1 (X1), bit4 side 2 (X2)
km.mouse()        query the last mask
```

- The sender sends it on every change (polled about every 4 ms) plus a keep-alive every second, and again after a reconnect. Swapped primary/secondary buttons are honored.
- A receiver treats the mask as 0 (nothing held) when the connection closes, on `km.controller_clear()`, or after 3 seconds without any `km.mouse`.
- A GamepadEnhancer receiver accepts and remembers the mask; it does not act on it. Axiom listens on the same protocol (Keys & HW > Remote Mouse Buttons) and engages aim while its chosen button is held.
- To feed only Axiom, point Send to another PC at Axiom's address and port and press Connect: Axiom accepts the pad commands that also flow on the link and ignores them.
- The old UDP stream (`AXMB` datagrams, default port 5602) is gone from both programs. Update the sender's port to the one Axiom now listens on (default 5555).
