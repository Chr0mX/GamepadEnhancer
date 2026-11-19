# Third-party notices

Fonts are listed in [THIRD_PARTY_FONTS.md](THIRD_PARTY_FONTS.md). The NuGet packages the app uses carry their own licences in the
packages themselves.

## libvirtualgamepad (VHF output backend)

The optional VHF output backend talks to the driver of **libvirtualgamepad**. This repository does **not** contain or redistribute
that project's driver, setup tool or source files. What it contains is a C# client written for the driver's control protocol, whose
constants and binary layouts (IOCTL codes, interface GUID, request and response structures, profile ids, button bits) are transcribed
from the protocol header of that project. Those parts keep the notice below
(`src/GamepadEnhancer/Output/Vhf/VhfProtocol.cs` carries it in its header).

- Project: https://github.com/Nonary/libvirtualgamepad
- Pinned version: tag `v0.1.0-beta.11`, commit `93dc007a608b341aa998378221f8958f045ee7f2`, protocol version 2
- Source of the transcribed material: `include/libvirtualgamepad/protocol.h`
- Licence: MIT

```
MIT License

Copyright (c) 2026 Chase Payne

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

If a driver package of that project is distributed together with this app in the future, its own licence file and the notices of
anything it bundles (for example the descriptors it derives from other projects) must be shipped with it and audited first; that has
not been done, because no driver is distributed.

The other VHF driver project that was evaluated, LizardByte/libvirtualhid, is **not** used: its Windows driver is under the
source-available "LizardByte Source-Available License 1.0" (not an open source licence) and needs a paid, online-validated licence.

## System.Security.Cryptography.Pkcs

Microsoft / .NET Foundation, MIT licence. Used to read the signer of a driver catalog file when a VHF driver package is checked.
