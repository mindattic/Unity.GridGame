# Thumbnail Studio

A standalone Windows tool (WPF, .NET 10) for framing each hero's and enemy's thumbnail: the square the game
cuts out of the real portrait for board tokens, timeline icons and menu cards. It exports no images. It edits
one small `"Thumbnail"` block per class in `Assets/Gaiden/Resources/Content/classes.json`, and the game zooms
and offsets the existing portrait from those numbers.

It lives outside `Assets/` so Unity never imports it, and it references nothing from the game.

## Run

Double-click **Thumbnail Studio** on the desktop. Or:

```powershell
powershell -File tools\thumbnail-studio.ps1                     # builds (Release) when needed, then starts
powershell -File tools\thumbnail-studio.ps1 --select alchemist  # open on a class
powershell -File tools\thumbnail-studio.ps1 -Shortcut           # build if needed, (re)create the desktop shortcut only
```

The launcher refreshes `%Desktop%\Thumbnail Studio.lnk` on every run. The shortcut points straight at the built
exe, so no console window opens. The exe carries its own icon (`ThumbnailStudio.ico`, drawn by `make_icon.py`).
When the exe starts and finds its sources (`*.cs`, `*.xaml`, `*.csproj`, `*.ico`) newer than the last launcher
build (`build.stamp` beside the exe), it hands off to `thumbnail-studio.ps1` in a hidden PowerShell, which waits
for it to exit, rebuilds and starts the new build. If that build fails, the previous build opens and says so;
the output is in `%LOCALAPPDATA%\MindAttic\ThumbnailStudio\build.log`.

Or `dotnet run --project tools\ThumbnailStudio`. Arguments:

| Argument | Meaning |
|---|---|
| `--repo <path>` | The game checkout. Default: walk up from the exe (then the working directory) until `Assets/Gaiden/Resources/Content/classes.json` exists. |
| `--classes <file>` | Edit this classes.json instead, for example a scratch copy. Portraits still come from the repo. Turns git sync and deploy off. |
| `--select <id>` | Open on this class. |
| `--no-deploy` | Never start `tools\run.ps1` on close. Use it for every test run. |
| `--commit-quiet <s>` | Seconds without a save before the studio commits. Default 20. |
| `--demo [N]` | Frames N units hands-free (default 1). Autosave, commit and push run as they do for a person, so point `--repo` at a scratch clone and add `--no-deploy`. |
| `--demo-exit` | Close the window when the demo is done (this runs the close flush). |
| `--no-rebuild-check` | Don't relaunch through the launcher when the sources are newer than this build. |

## Use

- **Left:** every class. Filter Heroes / Enemies / All, search by name or id. Each row shows its current thumbnail.
  The right edge shows the unit's sync state, and `default` marks a class with no saved Thumbnail yet. A class whose
  portrait png is missing is greyed out.
- **Centre:** the portrait on a checkerboard under a fixed gold square. What's inside the square is the
  thumbnail. The thin circle is the timeline icon's mask.
  - Mouse wheel zooms about the cursor (hold Ctrl for fine steps).
  - Left-drag moves the portrait under the square. Arrow keys nudge it 1 px (Shift: 10 px). `+` / `-` zoom
    about the square's centre.
  - The overview (bottom-left) shows the whole portrait with the cut-out marked. Click or drag there to aim.
  - Zoom / X / Y fields stay in sync. Type a value and press Enter.
- **Right:** live previews using the game's math: the board token (180 px tile, art inset 6 px, blue / red
  frame), the timeline icon (88 px navy disc, ring gold for heroes, red for enemies, purple for bosses and
  multi-tile units), and a 150 px menu-card thumb.
- **Revert** returns the unit to the framing it had when the studio opened. The revert autosaves too.
  PgUp / PgDn step through the filtered list.

## Set once and forget (GG-T223)

There is no Save step. Each unit's state shows in the list and next to its name:
**editing → saved → committed → pushed → deploying**. The status bar under the editor says what the studio is doing.

1. **Autosave.** Every framing change writes that unit's `Thumbnail` block to `classes.json`, 600 ms after the
   last change. Ctrl+S writes it immediately. Only that block changes (see below).
2. **Commit.** After 20 s without a save, and on close, the studio commits every unit it saved since its last
   commit as one commit: `Thumbnail Studio: reframe <names, up to 6, then +N more> (GG-T223)`.
   The checkout is shared, so other people may have unstaged hunks (even in `classes.json`) and staged files.
   The commit is built with git plumbing so it can never include them:
   - Read `HEAD:classes.json`, and apply the same targeted Thumbnail edits to it.
   - `git hash-object -w` the result.
   - In a temporary index (`GIT_INDEX_FILE`): `read-tree HEAD`, then `update-index --cacheinfo`, then `write-tree`.
   - `git commit-tree -p HEAD`.
   - `git update-ref HEAD <new> <old>`. This is a compare-and-swap: if HEAD moved, the commit is rebuilt on the
     new HEAD.

   If the real index's `classes.json` entry was HEAD's old blob, it is moved to the new blob, so `git status`
   shows no phantom change. If someone had staged changes in that file, the index is left alone and the log
   says so. The working tree already holds the edits, so the studio's change stops showing as modified. The
   commit-msg hook doesn't run for `commit-tree`; the subject carries `(GG-T223)` anyway.
3. **Push.** `git push origin HEAD:main`, never forced. If origin moved, the studio fetches and pushes only if
   HEAD still contains `origin/main`. Otherwise it shows "saved and committed locally; push pending - remote
   moved" and retries every minute. It never rebases, merges, stashes or resets. A push left pending at close
   is retried by the next session.
4. **Deploy on close.** Closing flushes the autosave, the commit and the push. Then, if anything was committed
   this session, it starts `tools\run.ps1` in its own minimized PowerShell, which outlives the studio and
   rebuilds and publishes `C:\Apps\GridGameGaiden`. The log goes to
   `%LOCALAPPDATA%\MindAttic\ThumbnailStudio\deploy-<time>.log`. If GridGameGaiden (installed, or from this
   repo's `Build\`) or the Unity Editor is running, the studio skips the deploy and says so. The next
   Deploy & Run picks the change up, because run.ps1 rebuilds whenever Assets changed.

Git runs in the background with no window and no prompts. Errors show in the status bar, and everything is
logged to `%LOCALAPPDATA%\MindAttic\ThumbnailStudio\studio.log`.

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

Saving is a targeted byte edit, not a re-serialize. The same edit is applied to `HEAD`'s copy for the commit. The file is re-read, then only that class's `Thumbnail`
value changes: an existing block is replaced in place, in the same single-line or multi-line style. Otherwise
a new block goes on the line after `"Portrait"`, with that property's indentation and the file's line endings.
Numbers are written with up to 4 decimals in invariant culture. The result is re-parsed before it replaces the
file, so a broken write never lands. Every other byte stays identical, including the unrelated `"Crop"` blocks.
