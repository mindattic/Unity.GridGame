# Thumbnail Studio

A standalone Windows tool (WPF, .NET 10) for framing each hero's and enemy's thumbnail: the square the game
cuts out of the real portrait for board tokens, timeline icons and menu cards. It exports no images. It edits
one small `"Thumbnail"` block per class in `Assets/Gaiden/Resources/Content/classes.json`, and the game zooms
and offsets the existing portrait from those numbers.

It lives outside `Assets/` so Unity never imports it, and it references nothing from the game.

## Run

```powershell
powershell -File tools\thumbnail-studio.ps1                     # builds (Release) when needed, then starts
powershell -File tools\thumbnail-studio.ps1 --select alchemist  # open on a class
```

Or `dotnet run --project tools\ThumbnailStudio`. Arguments:

| Argument | Meaning |
|---|---|
| `--repo <path>` | The game checkout. Default: walk up from the exe (then the working directory) until `Assets/Gaiden/Resources/Content/classes.json` exists. |
| `--classes <file>` | Edit this classes.json instead, for example a scratch copy. Portraits still come from the repo. |
| `--select <id>` | Open on this class. |
| `--demo` | Frames, saves and steps to the next unit hands-free, for recordings. It saves, so pair it with `--classes <scratch copy>`. |

## Use

- **Left:** every class. Filter Heroes / Enemies / All, search by name or id. Each row shows its current thumbnail.
  A gold dot marks an unsaved edit, and `default` marks a class with no saved Thumbnail yet. A class whose
  portrait png is missing is greyed out.
- **Centre:** the portrait on a checkerboard under a fixed gold square. What's inside the square is the
  thumbnail. The thin circle is the timeline icon's mask.
  - Mouse wheel zooms about the cursor (hold Ctrl for fine steps).
  - Left-drag moves the portrait under the square. Arrow keys nudge it 1 px (Shift: 10 px). `+` / `-` zoom
    about the square's centre.
  - The overview (bottom-left) shows the whole portrait with the cut-out marked. Click or drag there to aim.
  - Zoom / X / Y fields stay in sync. Type a value and press Enter.
  - Reset returns to the saved value.
- **Right:** live previews using the game's math: the board token (180 px tile, art inset 6 px, blue / red
  frame), the timeline icon (88 px navy disc, ring gold for heroes, red for enemies, purple for bosses and
  multi-tile units), and a 150 px menu-card thumb.
- **Save:** the Save button or Ctrl+S saves the current unit, and Ctrl+Shift+S saves every unit you've edited.
  PgUp / PgDn step through the filtered list. Leaving a unit keeps its unsaved edit in memory. Closing the
  window asks before it drops anything.

## Data contract

Each class object in `classes.json` can carry:

```json
"Thumbnail": {
  "Zoom": 2.4,
  "X": 0.52,
  "Y": 0.21
}
```

- `X`, `Y`: the portrait point (fractions of the portrait's width / height, top-left origin) that sits at the
  thumbnail's centre. Range -0.5 to 1.5.
- `Zoom`: portrait width divided by the thumbnail side, so 1 means the whole portrait width fills the square.
  Range 0.25 to 20. Aspect is always preserved.
- No Thumbnail yet: the tool shows Zoom 1.6, X 0.5, Y 0.3 and marks the class `default`.
- Portrait: `Assets/Gaiden/Resources/Portraits/<id>.png` (lowercase id).
- Rendering: in a square of side S, the portrait is drawn at width `W = Zoom*S`, height `H = W*imgH/imgW`, at
  `left = S/2 - X*W`, `top = S/2 - Y*H`, clipped to the square.

Saving is a targeted byte edit, not a re-serialize. The file is re-read, then only that class's `Thumbnail`
value changes: an existing block is replaced in place, in the same single-line or multi-line style. Otherwise
a new block goes on the line after `"Portrait"`, with that property's indentation and the file's line endings.
Numbers are written with up to 4 decimals in invariant culture. The result is re-parsed before it replaces the
file, so a broken write never lands. Every other byte stays identical, including the unrelated `"Crop"` blocks.
