# Recovery notes — BRAVE branch (Resonance timeline rework)

Written 2026-11-06 by the session that discovered a `docs/USER_STORIES.md` regression after a
CLI crash interrupted earlier work. Read this first if you're picking up BRAVE cold, or if a
future session is resumed with `/recover` (see `mindattic-agent-standard`'s new `recover` ability —
if that command doesn't exist yet where you are, see "Building /recover" at the bottom).

## Where things stand

Branch **BRAVE** (owner-approved exception to main-only — see the `brave-branch` project memory).
Nothing on BRAVE has merged to main, and nothing is pushed. Plan file:
`~/.claude-accounts/shampoo1232@mindattic.com/plans/inherited-giggling-music.md`.

Landed, in order: GG-T286 through GG-T310 (timeline model, UI, inspect, pincer eligibility,
bestiary, party cap 6, balance pass, docs), then:
- **GG-T303** (`a5710e3` + `4e1ba4c`) — Monster Tamer class, Tame ability, Dog Whistle item,
  domestication.
- **GG-T311** (`86317ed`) — minimum action time: a hero action only starts if the gap to the next
  enemy's turn is long enough (drag 0.85 s, skill 1 s); otherwise forced Guard + Rally stacks to
  break deadlocks. Bots drag with a human-like think time.
- **GG-T306** (`bc2a671` + `5b82979`) — `PincerPlanner`: Sentient foes and tamed monsters actively
  swap through units to complete/set up pincers and slot supporters; smart `HeroBot` uses the same
  planner. Rebalanced `PincerPushbackSeconds` 1 → 1.5 s.
- **GG-T312** (this session) — fixed a ticket-DB gap (below) and added this file.

Current win rates (8 battles/stage, smart / casual bot): d10 76%/31%, d5 99%/78%, d1 100%/98%.
No stage is unwinnable for the smart bot.

## The USER_STORIES.md regeneration gotcha (now fixed, but know this)

`docs/USER_STORIES.md`'s Epic rows are **regenerated from the shared ticket DB**
(`MindAttic.Tickets/data/Tickets.db`, shared across every repo on this machine) any time
`tickets story`/`tickets ticket` commands touch it — which can be triggered by a `ticket close` in
a completely unrelated repo, since the DB is workspace-wide.

GG-T303 and GG-T306's agents added `GG-US-R8` and `GG-US-R9` (and updated `GG-US-R2`'s wording from
"1 s" to "1.5 s") **by hand-editing the markdown** in their commits, but never ran `tickets story
new`/`tickets story edit` to put that text in the DB. Every regeneration since then reverted the
file to the DB's stale version — twice, silently, as uncommitted working-tree changes — because the
DB had no record of R8/R9 at all and still had R2's old wording.

**Fixed this session** by writing the real content into the DB:
```
tickets story edit GG-US-R2 --repo Unity.GridGame --text "..."   # 1.5 s wording
tickets story new --repo Unity.GridGame --epic R --status done --text "..."  # -> GG-US-R8
tickets story new --repo Unity.GridGame --epic R --status done --text "..."  # -> GG-US-R9
```
`docs/USER_STORIES.md` is now clean against HEAD and regenerates correctly. **Lesson for every
future BRAVE commit that adds a new `GG-US-R*` row or changes one's wording: always pair the
markdown edit with the matching `tickets story new`/`tickets story edit` call in the same step**, or
it will silently vanish the next time anyone anywhere closes a ticket.

## Open questions for the owner (not yet answered)

From GG-T311 (turn-gap rule):
1. Keep Rally (forced-Guard stacking that slows enemies to break deadlocks), or break deadlocks
   another way?
2. Is the ~2.4 s enemy bar-crossing at difficulty 10 too frantic? Slower felt better but pushed the
   smart bot to 92–97%.
3. Should a charging enemy spell also close/end the gap, not just enemy turns?
4. Do you want enemies' randomized wave-start bar positions back (lost when every wave started
   holding them back for the party's opening window)?

From GG-T306 (pincer planner):
1. Sentient foes swap with heroes as well as monsters to set up pincers — keep that on? (Turning it
   off barely moved win rates.) Knobs exist: `EnemyPincerSwapsHeroes`, `EnemyPincerSetups`.
2. Rebalancing used a stronger hero pincer pushback (1 s → 1.5 s) rather than a weaker enemy AI —
   is that the right lever, or would you rather tone the enemy side down instead?
3. Should hang-back foe brains (Kiter, Commander, Trapper, Medic) also move in to *set up* a
   pincer, not just complete one?
4. Should Sentient foes get extra move range specifically when setting up a pincer? They currently
   plan only within their normal 1–2 tile move range.

From GG-T303 (Monster Tamer), relayed but not yet confirmed:
- Mindless undead, slimes, golems and the ghost are tameable under the literal "non-Sentient,
  non-boss" rule — fine, or should taming be limited to living beasts only?
- A full bond currently just keeps the monster for the rest of that battle. Is that the intended
  behavior?
- The Dog Whistle is shop-only (150 G Merchant); it's not in any loot table yet.
- The battle bot doesn't use Tame yet.

## Still pending from the BRAVE plan

- **GG-T304** — Monster Meat (FF Legend 2) transformation: explore/design spike only, no build yet.
- **GG-T299** — Gambits: ON HOLD per the owner (too complex for "bus stop/toilet" sessions). Don't
  build or design around them unless revived.
- Visual check of the 6-hero layout across 9:23–3:4 aspect ratios hasn't been done on-screen yet.
- Re-run the balance/endless soak with the real starting party size now that GG-T311 and GG-T306
  have both landed (GG-T292's last pass predates both and found Green Valley I weak for the casual
  bot with the real 3-hero start — needs re-checking).
- BRAVE stays unmerged until the owner says so.

## Building `/recover` (if it doesn't exist yet)

The owner asked for a new portable global command, `/recover`, for exactly this situation: picking
up after a crash with no clean `/quicksave`. It's distinct from `/do` (resumes from the last turn or
an injected quicksave transcript) and `/quickload` (restores an explicit saved handoff) — `/recover`
should proactively diagnose a possibly-interrupted session with **no** prior deliberate save:

1. Survey git state: `git status --porcelain=v2 --branch`, check for `.git/MERGE_HEAD`,
   `.git/rebase-merge`/`rebase-apply`, `.git/CHERRY_PICK_HEAD` (an interrupted operation — report,
   never auto-resolve destructively), `git stash list`, and any unpushed commits.
2. Look for `.agent/RECOVERY.md` in the current repo (this file's convention) — if present, read it
   as the authoritative continuation note, then archive it the same one-shot way
   `quickload-on-do.ps1` archives `quicksave.md` (rename to `.001`, shifting older archives up —
   never delete).
3. Fall back to `/quickload`'s own handoff (`prose-agent.ps1 load`) if no `RECOVERY.md` exists.
4. Summarize findings in one short block, then continue exactly like `/do` — don't re-ask anything
   already decided, unless the git state itself is ambiguous or mid-conflict, in which case ask
   narrowly about *that* only.
5. Never force-push, hard-reset, or drop a stash automatically — recovery must not be destructive.

This session built this file as the `.agent/RECOVERY.md` convention it describes but — per this
session's `/crossrepo` policy (off, and not self-grantable) — did **not** cross into
`MindAttic.Agency` (the canonical `shared/abilities/*.md` source) to register it as a real portable
command. That still needs doing: either the owner runs `/crossrepo MindAttic.Agency` themselves in a
window on this repo and asks for `shared/abilities/recover.md` to be added (following the pattern of
`shared/abilities/dock.md`/`quickload.md`), or does it from a session already open on
`MindAttic.Agency`.
