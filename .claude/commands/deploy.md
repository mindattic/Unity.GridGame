---
description: Build the Windows Release player and publish it to C:\Apps\GridGameGaiden — this repo's own /deploy, which overrides the generic MindAttic router (tools\run.ps1 lives under tools\, not at root, so the router wouldn't find it on its own).
argument-hint: "[-Launch]"
allowed-tools: Read, Bash, PowerShell
---

# /deploy — GridGameGaiden

`tools\run.ps1`: stops any running instance launched from
this repo or from `C:\Apps\GridGameGaiden\`, clears `Build\WindowsRelease` (build output only,
never `Library\`), rebuilds the Windows Release player via Unity batch mode, and publishes it to
`C:\Apps\GridGameGaiden\`. Requires the Unity Editor closed first — batchmode fails otherwise; ask
the owner to quit it, never kill it.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\run.ps1` from the repo root in a
single tool call, forwarding `$ARGUMENTS` (e.g. `-Launch` to also launch the published exe once it's
copied, `-Force` to rebuild even when the deployed commit is up to date). Never run the repo-root
`run.bat` from here: it opens a 1) Deploy & Run / 2) Run menu and waits for a keypress (MAT-T67). Don't invoke the `unthrottle` skill yourself:
the context gate only writes its exemption marker when the user types `/unthrottle`, so a
self-invoked one does nothing. If the user wants this to skip the pace hold, they type
`/unthrottle` first.

Report the script's own output; don't summarize away a failure.

The Desktop shortcut `GridGame Gaiden.lnk` points at the repo-root `run.bat`, whose menu offers
1) Deploy & Run (this script with `-Force -Launch`) or 2) Run (launch whatever is currently
published in `C:\Apps\GridGameGaiden\`, with no rebuild).
