# Beta 3.3

Internal architecture change; no change to what the default configs do.

- Controls now have neutral canonical ids (`FACE_SOUTH`, `LB`, `PADDLE_LEFT_UPPER`, ...). Cross / A and the other names are looked up per
  controller from data files, so a new controller type is one JSON file. See `docs/ARCHITECTURE.md`.
- Games define actions (`JUMP`, `RELOAD`, `TACTICAL`, ...) and where their native layout puts them; profiles can reference those actions.
- Existing profiles, presets and saved paddles load unchanged and are saved in the new format.
- Binding titles and the diagram follow the selected controller (Cross on PS4, A on Xbox 360).
