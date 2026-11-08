# GridGame and Prose: the story conduit (GG-T355)

Owner, 2026-11-07: *"the Game handles gameplay, and Prose handles narrative"*; *"all dialog/conversations,
descriptions, screen crawls, everything is sourced from prose"*; *"everything will be assigned guids and retrieved at
compile time … its not hard coded into the game its pulled from a dedicated literary system."*

GridGame is a consumer of the Prose narrative engine (`D:\Projects\MindAttic\Prose`), universe **`gridgame`**
("GridGame"). The standard is Prose's [RFC 0017](../../Prose/docs/rfc/0017-game-data-conduit.md) and
[docs/CONDUIT.md](../../Prose/docs/CONDUIT.md). Gameplay (numbers, rules, stages, AI, balance) lives in this repo.
Story (canon entities and every word a player reads as story) lives in Prose. This repo never authors story.

## The flow

```text
content JSON ──tools/prose_map.py──> Data/Prose/gridgame.universe.json ──kit push──> Prose 'gridgame' (entities, edges)
                                                                                         │
                         the author writes and approves texts in Prose; agents only draft (RFC 0009)
                                                                                         │
Assets/Gaiden/Resources/Content/Text/*.json <──tools/prose_apply.py── pending/ <──kit pull── published texts
                └──────────────── Data/Prose/applied.jsonl {id, version, hash, key, kind} ────────────────┘
```

| Piece | File | Notes |
|---|---|---|
| Config | `Data/Prose/prose.config.json` | consumer and universe `gridgame`, kinds, choice tags; committed |
| Key scheme | `tools/prose_keys.py` | shared by the mapper, the seed and the applier |
| Mapper | `tools/prose_map.py` | content JSON (read only) → interchange file (gitignored), deterministic, no balance numbers |
| Seed | `tools/prose_seed.py` | today's in-repo text → `prose-text/1` drafts (the first seed, RFC 0017 §7) |
| Applier | `tools/prose_apply.py` (+ `tools/test_prose_apply.py`, fixture `tools/fixtures/prose/`) | pending texts → `Content/Text/<kind>.json` |
| Build-time pull | `tools/prose-sync.ps1`, `tools/build.ps1`, `GaidenBuild.SyncProse` | kit pull + apply before every build |

Gitignored (local): `Data/Prose/*.universe.json`, `pending/`, `outbox.jsonl`, `.kit/`. Committed: the config,
`applied.jsonl` and the applied texts under `Content/Text`.

## Commands

The kit runs by path from the Prose checkout (one copy, never vendored):

```powershell
$kit = 'D:\Projects\MindAttic\Prose\consumer-kit\kit.ps1'
& $kit validate                 # the universe file and any pending texts
& $kit push --map               # map, import (replace mode: retires game entities the content no longer has), round trip
& $kit pull --status published  # published texts newer than applied.jsonl -> Data/Prose/pending/
& $kit apply                    # tools/prose_apply.py writes Content/Text, the kit records applied.jsonl
& $kit status                   # entity and text counts on the Hub

python tools/prose_seed.py <dir>          # re-seed in-repo text as drafts (only while text still lives in gameplay JSON)
prose --texts-draft gridgame <dir>        # drafts; identical content changes nothing
python -m unittest tools/test_prose_apply.py   # the applier's tests (run only when the owner asks)
```

**Approval is the owner's** (RFC 0009; agents never approve or publish). One kind at a time, dry run first:

```powershell
prose --texts-approve-all gridgame --kind crawl --by <name> --dry-run
prose --texts-approve-all gridgame --kind crawl --by <name> --publish
# likewise --kind conversation | description | quest | glossary | bark; narrow with --prefix item: or --about class-lich
```

## Keys

A text's `key` is the game's lookup handle; it keeps the game's own ids verbatim. The text id is the kind plus those
ids slugged. A key exists exactly when the gameplay record it names exists (`prose_keys.game_keys`), which is how the
applier tells an orphan from a text to apply.

| Kind | Key | Text id | Source today |
|---|---|---|---|
| conversation | `dialogue:<dialogueId>` | `conversation-<id>` | dialogues.json, every dialogue (title, nodes, choices) |
| crawl | `crawl:prologue`, `crawl:ending` | `crawl-prologue`, `crawl-ending` | campaign.json Prologue/Ending |
| crawl | `crawl:<themeId>` | `crawl-<themeId>` | campaign.json Themes[].Crawl |
| crawl | `crawl:dialogue/<dialogueId>` | `crawl-dialogue-<id>` | a dialogue's Crawl (the level-10 interlude) |
| description | `class:<classId>` | `description-class-<id>` | classes.json Description (short) and Lore (long): hero blurbs, enemy and Bestiary text |
| description | `item:<itemId>` | `description-item-<id>` | items.json Description, every item |
| description | `armorset:<setId>` | `description-armorset-<id>` | armorsets.json Description |
| description | `skilltree:<treeId>`, `skillnode:<nodeId>` | `description-skilltree-…`, `description-skillnode-…` | skilltrees.json |
| description | `npc:<npcId>` | `description-npc-<id>` | the camp NPC's blurb (innkeeper, blacksmith, alchemist, enchantress, merchant) |
| quest | `quest:<questId>` | `quest-<id>` | quests.json Name, Giver, Summary, TurnIn |
| glossary | `glossary:<glossaryId>` | `glossary-<id>` | glossary.json Term, Aliases, Text |
| bark | `bark:caravan/<upgradeId>` | `bark-caravan-<id>` | caravan.json Barks (the wagon's crew) |
| bark | `bark:merc/<classId>/pitch`, `…/renewal` | `bark-merc-<class>-pitch` | mercenaries.json Pitch, Renewal |
| bark | `bark:bossphase/<classId>/<n>` | `bark-bossphase-<class>-<n>` | classes.json BossPhases[n].Announce |
| bark | `bark:surprise/<stageId>/<wave>` | `bark-surprise-<stage>-<wave>` | stages.json Waves[wave].SurpriseLine |

**Stays in the game** (UI and mechanics, not story): unit, item, ability, theme and stage names (Prose holds them as
entity names; the game owns the ids), ability Description and Trigger.Callout, class Card and Support.Label, item
PassiveName/PassiveDescription, caravan Description and level text, pickups, bounties, encounters, the battle
tutorial and Credits.txt.

**Conversations carry words and shape only.** Node ids, `next`, and choice order are kept so the game can join the text
back onto its own records. Triggers, conditions, effects, speaker classes and portraits stay in dialogues.json.
Speakers are `$placeholders` (`$hero`, `$with`, `$lead`, `$recruit`) or entity ids (`class-<id>`, `character-<npc>`).
Choice **tags** carry the meaning the game's logic keys on:

- `right-answer`: a wanderer's `heard` branch (`Wanderers.RightAnswer`), or the Advice-quiz answer that grants a skill
  point.
- `conditional`: a choice the game shows only when its Conditions hold.

## Entities

`tools/prose_map.py` writes the game-owned entities. Ids are stable and type-prefixed:

- `class-<id>` for every class: team, role, boss and traits. A hero is `member-of` its faction, an enemy is
  `found-in` the regions whose stages field it, and a boss `guards` its finale stage.
- `faction-<id>` for each hero faction in factions.json.
- `place-<theme>` for each region, and `place-<stage>` for each stage, which is `part-of` its region.
  - **Travel geography:** each region is `road-to` the next in campaign order (the main route, then into the
    postgame) and `contains` its stages.
  - A postgame region `extends` the main region its stages belong to.
  - The stated facts in `ADJACENCY`: the Still is `foot-of` Halcyon, the Cathedral `top-of` it, and the Underspire
    `beneath` it.
  - All of this is derived from campaign order, so it stays right as chapters are added.
- `character-<id>` for the camp NPCs and the caravan crew.
- `concept-<id>` for each glossary term.
- `armorset-<id>` for each armor set, and `item-<id>` for every item (a set piece is `part-of` its set).

The universe record's setting, logline and rules are the owner's DECIDED premise, "A War in Heaven" (v4, 2026-11-07),
which retires docs/STORY.md's "Under Morrow".

**Canon is Prose-authored.** The Bright Host, the Pale Concord and its orders, Morrow, essence, the tithe, the falls,
the Anchors, the Hierarch (a celestial deserter), the Herald, the Bloom, the crew, Saltmoss, Morrow's Shadow, and the
retired premises (v1 to v3, kept as history) were created in Prose with its own authoring tools, not the interchange
file, so a pruning push never retires them. They are tagged `DECIDED`, `PROPOSED` or `RETIRED`, and linked to the game's
entities: the Hierarch `appears-as` `class-hierarch`, and the Pale Concord `serves` the Bright Host.

Because the universe now holds entities the game does not own, `kit push`'s round-trip check reports them as "only in
theirs" and as a leak. The import itself is unaffected (`retired 0`, `relations invalidated 0`). Teaching the kit to set
Prose-authored entities aside is Prose's fix. Until then, read the diff as clean when it lists only canon entities.

## GUIDs

The owner wants every text and entity to carry its Prose GUID through the conduit.

- **What the applier does.** It writes `guid` beside the readable key in every `Content/Text` entry
  (`{"key": "crawl:swamp", "id": "crawl-swamp", "guid": …, "version": n, "hash": …}`). It takes the value from the
  pulled document's `guid` (or `entityId`) field.
- **The gap, as of 2026-11-07:** `prose-text/1` documents expose only the slug `id`, and interchange entities only
  their slug. Prose stores both as entity rows with GUIDs, but neither pull nor export returns them, so `guid` is null
  today.
- **Also in the kit:** it writes `applied.jsonl` itself (id, version, hash, key, kind), so it has to record `guid` as
  well.
- **The fix belongs in Prose.** Add `guid` to `prose-text/1` and to the interchange export, and have the kit log it.
  The applier needs no change.

## Build-time pull

Every build ships the latest **published** Prose text.

- **Where the pull runs:**
  - `tools/build.ps1` runs `tools/prose-sync.ps1` (kit pull with `--status published`, then kit apply) before Unity.
  - `GaidenBuild.Build` calls `SyncProse()`, which does the same for menu and batch builds.
  - `GaidenProsePreBuild` (an `IPreprocessBuildWithReport`) covers File > Build.
  - Each pull runs once per build. After `build.ps1` has pulled, Unity does not pull again (`GG_PROSE_SYNCED=1`).
- **When Prose can't be reached** (the Hub is offline, the kit is missing, or a kit step fails), the step logs a warning
  and the build uses the committed `Content/Text` snapshot. A build never fails because Prose is down.
- **To skip the pull** (offline or CI builds), use `tools/build.ps1 -SkipProse`, `GG_SKIP_PROSE=1`, or `-skipProse` on
  Unity's command line.
- **Defaults** can be overridden with `PROSE_KIT` (the path to `kit.ps1`) and `PROSE_HUB` (the Hub URL).

## Phase 2 (still to do, after the content agents land)

1. **Move the text out.**
   - Strip the narrative fields from gameplay JSON: class Description/Lore; dialogue Title, node Text and choice Label;
     crawls; quest Name/Summary/TurnIn; glossary Text; item, armor set and skill-tree descriptions; barks;
     mercenary lines; boss announcements; surprise lines.
   - Gameplay records keep only their ids (the keys derive from them).
2. **Apply the approved text.** After the owner approves the seeded drafts, run `kit pull` and `kit apply` so
   `Content/Text/*.json` holds every text, and commit it with `applied.jsonl`.
3. **The loader join.** `ContentLoader` reads `Content/Text/<kind>.json` and joins each text onto its record by key.
   Conversations join by node id and choice order.
4. **Validation (tests):**
   - every gameplay key resolves to a text;
   - no narrative field is left in gameplay JSON;
   - the `right-answer` tags match `Wanderers.RightAnswer` and the quiz rewards.
5. **docs/STORY.md** becomes a pointer to the `gridgame` universe.
