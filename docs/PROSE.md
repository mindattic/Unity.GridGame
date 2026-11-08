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
| Seed | `tools/prose_seed.py` | the game's words → `prose-text/1` drafts (the first seed, RFC 0017 §7); reads Content/Text since Phase 2 |
| Text tool | `tools/prose_text.py` | Phase 2: `check` (no story in gameplay JSON, every key has a text), the one-time `snapshot`, the Python join, `upsert_seed` for generators |
| Loader join | `Core/Content/ProseText.cs` | reads Content/Text into the models, placeholders and `TextRef` (id, GUID, version) |
| Applier | `tools/prose_apply.py` (+ `tools/test_prose_apply.py`, fixture `tools/fixtures/prose/`) | pending texts → `Content/Text/<kind>.json` |
| Build-time pull | `tools/prose-sync.ps1`, `tools/build.ps1`, `GaidenBuild.SyncProse` | kit pull + apply before every build |

Gitignored (local): `Data/Prose/*.universe.json`, `pending/`, `drafts/`, `outbox.jsonl`, `.kit/`. Committed: the config,
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

python tools/prose_seed.py <dir>          # draft the game's current words (Content/Text joined on) for Prose
python tools/prose_text.py check         # no story text in gameplay JSON, every key has a text, no orphans
python tools/prose_text.py seed-missing --drafts <dir>   # seed only keys with no text yet; drafts for Prose
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

| Kind | Key | Text id | Gameplay record (the words were here before Phase 2) |
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
| description | `place:<themeId>` | `description-place-<id>` | a campaign region (GG-T374), shown under its expedition header on the stage select; seeded from the crawl's first paragraph |
| description | `place:<stageId>` | `description-place-<id>` | a campaign stage (in a theme's Stages, not `IsTest`); seeded "A battlefield of <region>." |
| description | `faction:<factionId>` | `description-faction-<id>` | factions.json, every faction; shown in the post-battle wanderer box; seeded from the mapper's summary |
| description | `character:<crew>` | `description-character-<crew>` | caravan.json `Crew`, a named character with no class or npc key; `<crew>` is the crew name slugged (its entity id without `character-`); shown on the Caravan card |
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

**Every place, faction and character has a description** (GG-T374). Prose's verifier raises `TEXT-OBLIGATION
[undescribed]` when a story text names an entity that no `description` text is `about`. `prose_keys.description_keys`
gives each place, faction and character entity its key (`place:`, `faction:`, `npc:` for camp NPCs, `character:` for the
crew); the mapper refuses to write an entity without one, and `prose_text.py check` reports it. The game joins them into
`ContentDb.PlaceBlurbs`, `FactionBlurbs` and `CrewBlurbs` (a missing key shows the placeholder). New keys get their seed
with `python tools/prose_text.py seed-missing --drafts <dir>`, which only fills keys that have no text yet, then
`prose --texts-draft gridgame <dir> --batch <tag>`.

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
- **Also in the kit:** it writes `applied.jsonl` itself (id, version, hash, key, kind), and since Prose PRS-T923 it
  records a pulled text's `guid` there too.
- **In the game** (Phase 2): the GUID and version reach `ContentDb.TryText(key)` (`TextRef.Guid`, `TextRef.Version`).
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

## Phase 2 (done 2026-11-07): the game reads every story string from Content/Text

1. **The seed snapshot.** `python tools/prose_text.py snapshot` (one-time, refuses to run again) wrote
   `Content/Text/*.json` from the text the gameplay JSON held, *not* from Prose: nothing is approved yet and only the
   owner approves. Each entry is exactly what the applier writes (same key, text id and payload as the Prose draft),
   with `guid` null, `version` 0 and `source` `"seed"` (an applied text says `"prose"`). The seed is recorded in
   `Data/Prose/applied.jsonl` (`status` `"seed"`, `version` 0), which the kit's format allows: any published version is
   higher, so the next build-time pull replaces each seed as soon as the owner publishes its text. 1976 texts:
   conversation 132, crawl 18, description 1671, quest 13, bark 28, glossary 114. The game reads the same words as before.
2. **Stripped from gameplay JSON** (2802 fields; `ProseText.NarrativeFields` / `prose_text.NARRATIVE`):

   | File | Fields moved to Prose | Kept (gameplay, UI) |
   |---|---|---|
   | classes.json | `Description`, `Lore`, `BossPhases[].Announce` | Name, Card, Support.Label, everything else |
   | dialogues.json | node `Text`, choice `Label`, `Crawl` (now the `HasCrawl` marker) | Id, Title (the Talk row label), Trigger, Conditions, Effects, Speakers, node ids, Next, choice order |
   | campaign.json | `Prologue.Crawl`, `Ending.Crawl`, `Themes[].Crawl` | titles, theme names, backdrops, stages |
   | quests.json | `Summary`, `TurnIn` | Name, Giver, objective, rewards |
   | glossary.json | `Text` | Term, Aliases, link |
   | items.json, armorsets.json | `Description` | Name, PassiveName/PassiveDescription, stats |
   | skilltrees.json | tree and node `Description` | names, grants, costs |
   | caravan.json | `Barks` | Description and level text (mechanics), Crew |
   | mercenaries.json | `Pitch`, `Renewal` | rate, terms, temperament |
   | stages.json | `Waves[].SurpriseLine` | `SurpriseSpeaker` (the marker for its key) |

   A key exists exactly when its gameplay record does; the optional ones hang on a marker: a dialogue's crawl on
   `HasCrawl`, a surprise line on `SurpriseSpeaker`, caravan barks on `Crew`; every boss phase announces.
3. **The join** (`ProseText.Join`, called by `ContentLoader.Load`) reads `Content/Text/<kind>.json` and fills the same
   model fields consumers always read, so no screen changed. Conversations join by node id (lines) and choice order
   (choice texts); crawls are their paragraphs joined by a blank line; a bark's first line is a one-liner.
   - **A missing key** shows `(missing text: <key>)` (a dialogue node `(missing text: dialogue:<id>#<node>)`), is recorded
     in `ContentDb.TextProblems`, reported by `ContentLoader.Validate` and logged as a `[Content]` warning by `GameApp`.
     It never throws, nor does an unreadable text file.
   - **Debugging and telemetry:** `ContentDb.TryText(key)` gives a `TextRef` (key, kind, Prose text id, GUID, version,
     source, hash, missing). NPC blurbs (`npc:<id>`) are in `ContentDb.NpcBlurbs`.
   - A hand-built fixture with no Content/Text at all is not joined (`TextJoined` false) and keeps the words it was given.
4. **Validation** (`ContentLoader.Validate`, via the join): every key resolves; no orphan text; no story text in gameplay
   JSON (a field put back is reported and Prose's text wins); conversation shape agrees (nodes, choice count, each
   choice's `next`); the `right-answer` tag sits exactly on the answers the game rewards (`ProseText.RewardsRightAnswer`:
   a wanderer's `heard` reply, which is `Wanderers.RightAnswer`, or a choice that grants a skill point).
   *(Tests: `TextSourcingTests` (C#) and `TextSourcingTests` in `tools/test_prose_apply.py`; written, run when the owner
   asks.)*
5. **New content gets a key, its words go to Prose.** A new record's key exists as soon as the record does; until its
   text is approved it shows the placeholder and Validate names it. Draft the words in Prose
   (`prose --texts-draft gridgame <dir>`). Generators use `prose_text.upsert_seed`, which writes a seed entry (never over
   a Prose-applied text) and a draft in `Data/Prose/drafts/` (`tools/weapons.py` does this for weapon descriptions).
   BIBLE §20 has the new-unit steps.
6. **docs/STORY.md** is a pointer to the `gridgame` universe.

**Still open (Prose side):** GUIDs in `prose-text/1` pulls (the kit already records a pulled `guid`), and the owner's
approval, kind by kind (see Commands). Then `tools/prose-sync.ps1` replaces the seed text by text.
