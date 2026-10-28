---
codex: 1
project: GridGameGaiden
code: GG
layer: bible
status: living
updated: 2026-10-21
---

# GridGame Gaiden — Project Bible

> Single source of truth for what GridGame Gaiden IS, is NOT, and the rules that keep it coherent. `README.md` says how to build/run; this says how to think about the system.
>
> GridGame Gaiden is the from-scratch rebuild of GridGame2026 (declared unplayable). The old bible's mechanics are the target ("full bible parity"); the architecture is new and several owner decisions amend the old rules. Every amendment is marked **Amended 2026-10-09:** inline and summarized in [`AMENDMENTS.md`](AMENDMENTS.md). Anything the old bible specifies that the new code does not do yet is kept as spec and marked **⬜ Not yet built** — nothing is silently claimed. The parity backlog with per-story evidence is [`USER_STORIES.md`](USER_STORIES.md).
>
> The nine sections below are the L0 outline. The detailed design canon lives in [Appendix A — Design Canon](#gg-appendix-a) and is cited by `§`-anchor. Structured canon (classes, abilities, buffs, items, recipes, drops, stages, campaign, traps, bounties, summon, enchants) lives in [`Assets/Gaiden/Resources/Content/*.json`](../Assets/Gaiden/Resources/Content/) — prose cites entities by `Id` rather than restating every field.

---

## 1. The one sentence {#GG-§1}

A tactical grid RPG where **movement deals zero damage** — you drag heroes one tile at a time across a 6×8 grid, **sliding** displaced actors aside, to trap enemies in **pincers** (two heroes flanking an unbroken line) on an **act-advanced Timeline**, built as a pure-C# deterministic simulation that a code-built Unity 6 UI Toolkit front end plays back.

The feel target: *Terra Battle pincers + Grandia II timeline + Final Fantasy camp menu + FF8 draw economy.* See [§0 North Star](#0-north-star) for the design pillars.

## 2. The product promise {#GG-§2}

- **Position is the only weapon.** Damage is dealt by the *pincer* a new position completes, never by the move itself. Every drop asks "does this finish the line?" ([§1.2](#12-pincer-attacks))
- **The slide.** Dragging a hero onto an occupied tile displaces that actor — friend or foe — into the tile you just left. Set up flanks, eject allies, feed enemies into kill zones. ([§1.1](#11-sliding-and-displacement))
- **The Timeline is a clock you advance.** Enemy icons load left→right, but the clock only runs when you commit an action (2.0 s of game time each). Hit a foe whose icon sits in the rightmost **Pushback Zone** and its turn is shoved back. ([§2](#2-the-timeline))
- **Shared, visible mana.** A 12-orb `ManaBank` of generic orbs is the whole party's spell budget; pincers, crits, steals and enemy-cast interrupts refill it. ([§3.1](#31-mana-the-orb-economy))
- **Casts ride the Timeline.** A spell with a cast time rides the same axis as a small icon below the line and fires when it arrives; every hit on the caster staggers it, enough stagger cancels it, and a rare LCK Clutch lets a dying healer let off one last miracle. ([§13.4](#134-the-interrupt-path))
- **Beyond the battlefield.** Five themed campaigns of hand-built stages with a skippable story crawl per theme, a Final Fantasy–style **Camp** menu (merchant, blacksmith, alchemist, inn, abilities, party, summon circle, bounties, bestiary), weapon durability with shatter rebound. **Amended 2026-10-19 (GG-T120):** the caravan story has landed — the town rides a titan, "the Longmother," who can never stop walking because standing ground sours into a biomechanical blight; crawl text, NPC/hero join scenes and party banter are real narrative, not lorem ipsum. ([§22](#22-the-macro-loop), [§25](#25-the-camp-merged-hub), [§27](#27-story-crawl-no-dialog))

Target pacing: a battle is 15–40 committed actions (~2–5 minutes), a stage ~3–4 min, a session 20–30 min. ([§0.3](#03-session-shape-target-pacing))

## 3. What it is NOT {#GG-§3}

GridGame Gaiden is **NOT**:

- **Not real-time, and not alternating turns.** The clock runs only when you commit an action, so thinking time is free; but enemies act when their icons arrive, which may be zero, one or several times per action. **Amended 2026-10-09:** replaces "thinking time is game-clock time" ([§0.2](#02-non-goals-what-this-game-is-not)).
- **Not a deck-builder** — AbilityBars are deliberate loadouts, not randomized hands.
- **Not a roguelike** — no permadeath, no procedural runs; stages are authored, saves persist.
- **Not a gacha / live service** — no pulls, no energy, no premium currency. Heroes are recruited deliberately for gold at the Summon Circle.
- **Not multiplayer** — solo offline; no co-op, PvP or leaderboards.
- **Not a grid-puzzle** — no match-3 piece-color matching; tile state is "who's standing there".
- **Not free-form movement** — one tile at a time, cardinal only, no pathfinding or diagonals.
- **Not a stat-spreadsheet** — if a player needs a wiki to build, the design failed.
- **No world map.** Narrative is a skippable per-theme text crawl ([§27](#27-story-crawl-no-dialog)) plus portrait dialogue at story beats between battles and from Camp's Talk row ([§27.1](#271-dialogue-gg-t117)); stage navigation is the Adventure list ([§28](#28-no-overworld)). **Amended 2026-10-19 (owner, GG-T117):** "no dialog system" withdrawn. **Confirmed 2026-10-20 (owner, GG-T115):** the separate overworld pitch ([design-draft-overworld-and-dialogue.md §2](design-draft-overworld-and-dialogue.md)) is withdrawn — a world map "takes away from a quick sit-down game." The game is four kinds of screen: fighting, in-town buying/upgrading, reading a text crawl, or talking with people (NPCs, party).

## 4. Architecture canon {#GG-§4}

**Amended 2026-10-09:** the builder/scene/manager architecture of GridGame2026 is gone. The game is a pure-C# simulation with an event-playback front end.

```
                    Unity 6000.4.3f1  ·  one near-empty scene: Assets/Gaiden/Scenes/Main.unity
                                         (GameApp boots itself; nothing is authored in the scene)
 ┌───────────────────────────────────────────────────────────────────────────────────────────────┐
 │ Gaiden.App (Assets/Gaiden/App)  — Unity presentation only                                      │
 │   Boot/GameApp ─ builds camera, UIDocument + PanelSettings (1170×2532), letterbox, router      │
 │   UI/ScreenRouter, Ui, Tween      Screens/*Screen (UI Toolkit C# element trees + Theme.uss)     │
 │   Battle/BattleScreen(.Input/.Playback), BoardView   Services/Art, Audio, Fonts, SaveStore     │
 │   Automation/AutomationServer (dev builds), StartScreens                                       │
 └──────────────▲───────────────────────────────────────────────▲────────────────────────────────┘
                │ commands ↓  events ↑                          │ services
 ┌──────────────┴──────────────────────────┐   ┌────────────────┴──────────────────────────────┐
 │ Gaiden.Core (noEngineReferences)        │   │ Gaiden.Meta (noEngineReferences)              │
 │  Battle/BattleEngine.Apply(cmd)→events  │◀──│  SaveData, SaveMigrations                     │
 │  BattleRunner.* (rules), BattleState,   │   │  Progression, BattleFactory (save↔battle),    │
 │  Timeline, ManaBank, Unit, BattleConfig │   │  Shop, Crafting, Repair, Inn, Camp, Equipment,│
 │  Rules/PincerDetector, TargetResolver,  │   │  Abilities, Party, Summon, Bounties           │
 │        EnemyPlanner, Formulas           │   └───────────────────────────────────────────────┘
 │  Bot/HeroBot, BotRunner   Util/Rng,Coord│
 │  Content/ContentLoader, ContentDb, ContentModels ◀── Resources/Content/*.json              │
 └─────────────────────────────────────────┘
          ▲ compiled and run outside Unity by Tests/Core.Tests (NUnit, dotnet test)
```

### 4.1 Projects / assemblies

- `Gaiden.Core` (`Assets/Gaiden/Core/Gaiden.Core.asmdef`, `noEngineReferences: true`) — the battle simulation, rules, content model, bot. Only dependency: `Newtonsoft.Json.dll`.
- `Gaiden.Meta` (`noEngineReferences: true`, references Core) — save model and every Camp service.
- `Gaiden.App` (references Core, Meta, `Unity.InputSystem`) — presentation, input, audio, automation.
- `Gaiden.Editor` (Editor only) — `Editor/GaidenBuild.cs`: batchmode entry points (`EnsureScene`, `BuildWindowsDev`, `BuildWindowsRelease`, `BuildAndroid`, `BuildWebGL`, `CompileCheck`, `ReimportArt`). This file is the only "scene authoring" the project has.
- `Tests/Gaiden.Core/Gaiden.Core.csproj` — compiles the Unity source of Core + Meta as a plain .NET Standard 2.1 library (proving [GG-LAW-9](#GG-LAW-9) on every build); `Tests/Core.Tests/Core.Tests.csproj` — the NUnit suite on top of it, run with `dotnet test` (no Unity needed).

### 4.2 Domain model — the NOUNS

- **Unit** — `Core/Battle/Unit.cs`: hero or enemy (`Team`), `Def` (`ClassDef`), level, `LevelStats` + `GearStats` → `Effective` stats, HP, footprint (`W`,`H`), buffs, cooldowns, `Bar` of 5 `AbilitySlot`s, weapon state, snake-chain links, boss phase, `DamageDealt` (threat). ([§3.2](#32-hp-and-stats))
- **ClassDef** — one entry of `classes.json` (172: 62 hero classes, 110 enemy classes). ([§23](#23-character-classes))
- **AbilityDef** — `abilities.json` (25: 15 spells, 6 skills, 4 enemy charges). ([§7](#7-the-spell-catalog))
- **BuffDef / BuffInstance** — `buffs.json` (10). ([§8](#8-buffs-and-debuffs))
- **ItemDef** — `items.json` (225: 155 equipment, 54 materials, 16 consumables). ([§24](#24-equipment-items-materials-currency))
- **ManaBank** — `Core/Battle/ManaBank.cs`: a capped count of 12 generic orbs. ([§3.1](#31-mana-the-orb-economy))
- **TimelineEntry** — `Core/Battle/Timeline.cs`: a Turn icon (enemy, integer IP 0..1000) or a Cast icon (remaining ticks). ([§2](#2-the-timeline))
- **BattleState** — `Core/Battle/BattleState.cs`: units, timeline, traps, bank, wave, phase, clock, seeded `Rng`, accumulated `BattleResults`. Cloneable (previews, bot look-ahead).
- **SaveData** — `Meta/SaveData.cs`: one save slot. ([§15](#15-save))

### 4.3 Key services — the VERBS

- `BattleEngine.Create(content, setup, events)` — builds a battle from a `BattleSetup` (stage, party `HeroSpec`s, seed, unlocked slots, recommended level).
- `BattleEngine.Apply(state, command)` → `List<BattleEvent>` — **the only way a battle changes.** One committed command resolves, the clock advances, arrivals act, control returns. ([§4.5](#45-the-command--event-contract))
- `BattleEngine.PreviewMove / ValidTargets / GetSlotStatus / TicksUntilTurn / ActionsUntilTurn` — read-only queries the UI and bot use (preview runs on a cloned state).
- `PincerDetector.Detect(state, team, dropped)` — board-wide pincer scan, chain ordering, supporters. ([§1.2](#12-pincer-attacks))
- `TargetResolver.Tiles / Collect / IsValidPick` — the (shape × mode × filter) triad. ([§5](#5-targeting))
- `BattleRunner.ResolveAbility` — the dispatcher; adding a spell never touches it. ([§6](#6-the-spell-dispatcher))
- `EnemyPlanner.PickTarget / PlanStep / PlanCast / LineToward / FootprintStepLegal` — positional AI. ([§14](#14-ai))
- `Formulas` — stats-at-level, HP, hit/crit, physical/magic/spell damage, cast time, XP curve. ([§13.1](#131-damage-formulas))
- `BattleFactory.Build / Commit` — save → battle setup, finished battle → save. ([§22.1](#221-per-transition-state-contract))
- `HeroBot.Decide` / `BotRunner.Play` — a player stand-in that drives the real command API (soak tests, in-game Auto battle, first-battles hints).
- `CampRoutine.Run` (Meta) + `MetaHeuristics` (Core/Bot/MetaBot.cs) — "what a sensible player does at Camp" (repair, rest, claim/accept bounties, recruit into an open party slot, forge/equip better gear, learn a spell, fill empty slots, restock potions) through the same services the screens call; drives the campaign balance simulation.

### 4.4 Content is data {#GG-§4.4}

**Amended 2026-10-09:** every class, ability, buff, item, recipe, drop table, stage and campaign is JSON under `Assets/Gaiden/Resources/Content/`, loaded by `ContentLoader.Load` (Newtonsoft, `MissingMemberHandling.Error`, string enums) and cross-checked by `ContentLoader.Validate` at boot (problems are logged by `GameApp.LoadContent`; the test suite fails on any).

| File | Holds |
|---|---|
| `classes.json` | 172 `ClassDef`s — stats, growth, milestones, tags, resistances, learnable abilities, default loadout, drop table, move range, XP reward, boss phases, charge ability/shape, trap, snake segments, footprint, lore/card text, portrait crop |
| `abilities.json` | 25 `AbilityDef`s — cost letters, cast time, cooldown, shape/radius/mode/filter, motion, damage type/base, heal, buff + chance, cleanse, steal, hasten, teleport, scan, VFX/SFX keys |
| `buffs.json` | 10 `BuffDef`s — kind, unit (Turns/Ticks), duration, DOT, immobile, halts timeline, breaks on damage/move, incoming-damage / timeline-speed / accuracy multipliers, blocks spells, expire chain, letter, color |
| `items.json` | 231 `ItemDef`s (155 equipment, 55 materials, 18 consumables, 3 recipe scrolls; GG-T45 added `mat_pure_slime_gel` + Greater Healing Potion/Antidote) — type, rarity, slot, cost, stack, durability, healing/damage, stats, battle-start orbs, on-use ability, orb grants, cures, revive, escape, camp-only, bonus-vs-tag, resistances, vendor flag |
| `recipes.json` | 142 `RecipeDef`s — station (Forge / Alchemy / Enchant), inputs, gold, optional base item, result; some Alchemy recipes also carry a potency variant ([§24.6.4](#2464-reagent-potency-gg-t45-owner-direction)) |
| `droptables.json` | 37 `DropTableDef`s — optional coin range, weighted item entries (chance, min, max); one per enemy family, each carrying that family's own signature material (added 2026-10-15, GG-T43) |
| `stages.json` | 19 `StageDef`s — 15 campaign + 4 test (`IsTest`), theme, waves of `SpawnDef` (class, level, optional fixed tile); a wave may instead name an encounter group (`WaveDef.Group`) |
| `encounters.json` | 182 `EncounterGroupDef`s (GG-T184) — one enemy family's mix on fixed enemy-half tiles (a named formation: Pincer, Wedge, Crown ...) at a level; 2-5 foes, every Crucible family has a group of every wave size. `EncounterCatalog.Validate`: no two groups are the same foes on the same tiles, no two campaign waves are the same foes, levels and tiles, a group backs one campaign wave at most |
| `campaign.json` | 5 main themes × 3 stages + 5 postgame themes × 2 bonus stages (added 2026-10-13, GG-T7/T10; each bonus stage carries a `StageDef.LevelOverride` instead of relying on flattened-index `RecommendedLevel`), in play order, with each theme's story-crawl text |
| `traps.json` | 1 `TrapDef` (`venom_snare`: 6 dmg + Poisoned, lay chance 0.45) |
| `bounties.json` | 5 `BountyDef`s — target class, kill count, gold + item reward |
| `summon.json` | Summon Circle pool (7 classes), base price 250, step 250 |
| `enchants.json` | 6 `EnchantDef`s (Flame/Frost/Spark/Shadow/Poison/Holy, the last 2 added 2026-10-13) — reference data; the actual enchant crafts are `Station: Enchant` recipes |

### 4.5 The command → event contract

A **command** (`Core/Battle/Commands.cs`) is a player decision: `MoveCommand` (hero id + path of cardinal steps + drag-timer seconds left), `UseSlotCommand` (hero, slot, target tile) or `ShieldCommand`. `BattleEngine.Apply` resolves it synchronously and returns an ordered list of `BattleEvent`s (`Core/Battle/BattleEvents.cs`: `Stepped`, `Displaced`, `PincerFormed`, `Attack`, `Damaged`, `Healed`, `Died`, `Loot`, `OrbMinted`, `OrbsSpent`, `TimelineAdvanced`, `PushedBack`, `Hastened`, `TurnStarted`, `TurnSkipped`, `CastQueued`, `CastStaggered`, `CastCancelled`, `Clutch`, `AbilityResolved`, `AbilityRefused`, `ItemUsed`, `BuffApplied/Ticked/Removed`, `Announce`, `Scanned`, `TrapPlaced/Triggered`, `WeaponWorn/Shattered`, `BossPhase`, `WaveStarted/Cleared`, `Revived`, `BattleEnded`, `PlayerControl`). The simulation has already finished when the view sees them; the view only plays them back (`BattleScreen.Play` in `App/Battle/BattleScreen.Playback.cs`, the event player). A refused command emits `AbilityRefusedEvent` and changes nothing — no clock, no cost. The same seed + content + command list always reproduces the same event list.

## 5. The Laws {#GG-§5}

This project **inherits the org-wide house rules** in [`../../MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) — do not restate them here. The most load-bearing for this repo: [`HOUSE-LAW-1`](../../MindAttic.HouseRules.md#HOUSE-LAW-1) whole-number versioning (`VERSION` → `PlayerSettings.bundleVersion`), [`HOUSE-LAW-2`](../../MindAttic.HouseRules.md#HOUSE-LAW-2) soft-disable over delete, [`HOUSE-LAW-8`](../../MindAttic.HouseRules.md#HOUSE-LAW-8) verified-not-asserted done. GridGame Gaiden ships **zero LLM features** — nothing AI lands in `Assets/`.

Project-specific laws:

### {#GG-LAW-1} Movement never deals damage
Sliding a hero through or onto an enemy is **not** an attack. Damage flows ONLY through pincers ([§1.2](#12-pincer-attacks)), enemy attacks, abilities/items ([§6](#6-the-spell-dispatcher)) and environmental sources (DOT, traps, weapon shatter). *(Source: `BattleRunner.WalkPath`; test `Movement_never_deals_damage`.)*

### {#GG-LAW-2} A pincer is two allies flanking an unbroken, ally-free line
Same row OR column, every tile strictly between them covered by opponents — no gaps, no allies, at least one opponent. No diagonal pincers. A 2×2 boss covering several line tiles counts as one opponent. *(Source: `PincerDetector.TryPair`.)*

### {#GG-LAW-3} ~~Code is the source of truth; the `.unity` is the print-out~~ — RETIRED
**Amended 2026-10-09:** there are no scene builders, no `BuilderAutoRebuild`, no `BuilderDriftChecker`. The one scene is an empty shell; `GameApp` builds everything at runtime. Superseded by [GG-LAW-9](#GG-LAW-9).

### {#GG-LAW-4} ~~Guardrails are enforced at pre-push~~ — RETIRED
**Amended 2026-10-09:** the `SerializedFieldBan`, `ResourcesLoadBan` and `InstantiateBan` guardrails are gone. There are no prefabs and no Inspector wiring to guard; `Resources.Load` is the sanctioned loader for content JSON, art, audio, fonts and the UI theme. Superseded by [GG-LAW-9](#GG-LAW-9)–[GG-LAW-12](#GG-LAW-12).

### {#GG-LAW-5} Every spell is a (shape × mode × filter) triad
Adding a spell is a new `abilities.json` row picking targeting axes and effect knobs; it never touches `BattleRunner.ResolveAbility`. *(Source: `TargetResolver.cs`, `BattleRunner.Abilities.cs`; test `Every_hero_ability_resolves_against_a_live_board`.)*

### {#GG-LAW-6} Mana is one shared, visible, capped pool
A single 12-orb `ManaBank` for the whole party — no per-hero MP. Over-minting past 12 fades out (felt as "leaving value on the table"). *(Source: `ManaBank.Add`; test `Bank_overflow_fades_extra_orbs`.)*

### {#GG-LAW-7} Verify-then-checkpoint; the bible is the brief
Land a feature end-to-end and verify it before committing — no mid-phase commits. If code and bible disagree, reconcile in writing. **Amended 2026-10-09:** the old "every system ships a `DebugManager.Demo_*` button" rule is replaced by harness evidence ([GG-LAW-12](#GG-LAW-12)) — a scripted automation step or a test is the demo.

### {#GG-LAW-8} Portrait-mobile is locked; never stretch
The UI is a fixed 1170×2532 design stage. **Amended 2026-10-09:** `GameApp.FitStage` (not the old `AspectGuard`) scales the stage to the limiting axis and centers it; the rest of the screen is black bars. The game never stretches or squashes. **Amended 2026-10-21 (GG-T169, owner: "on-device experience is king"):** 1170×2532 is now the *minimum* stage, not a fixed one — phones from 9:21 to 9:16 fill the whole screen (the stage grows taller or wider to match), and only shapes outside that range (tablets, ultra-tall) get bars, with an outline frame around the stage ([§26.3](#263-adaptive-stage-gg-t169)). Still never stretched or squashed. *(Source: `App/Boot/GameApp.cs`.)*

### {#GG-LAW-9} Core has no UnityEngine
`Gaiden.Core` and `Gaiden.Meta` are `noEngineReferences: true` and must compile and run under plain `dotnet test`. All randomness goes through the seeded `Core/Util/Rng` (xoshiro128\*\*); no `System.Random`, no time reads, no static mutable battle state. The presentation never decides a rule; it sends commands and plays back events. *(Source: asmdefs; test `Battles_replay_identically_from_seed`.)*

### {#GG-LAW-10} Content is JSON validated at load
Game content lives only in `Resources/Content/*.json`. `ContentLoader.Load` rejects unknown fields, empty or duplicate ids; `ContentLoader.Validate` resolves every cross-reference (classes, items, abilities, buffs, drop tables, traps, stages, themes, summon pool, enchant essences). A content change is done only when `Real_content_loads_and_cross_references_resolve` is green.

### {#GG-LAW-11} Every glyph the UI prints must exist in the bundled fonts
`tools/check_glyphs.py` scans every non-comment line of `App/**/*.cs`, every content JSON and `Credits.txt` for non-ASCII characters and fails if any is missing from Outfit → Noto Sans → Noto Sans Symbols 2. It runs inside `tools/test.ps1`. No tofu boxes ship.

### {#GG-LAW-12} "Done" requires harness evidence
A story is ✅ only with evidence a script produced: a named test in `Tests/Core.Tests` (rules, meta, catalog, bot soak), and for anything visible, an automation-harness step (`tools/harness.py smoke|sweep`, screenshots + layout audit in `artifacts/<run>/`). Asserting "it works" without a test or a harness run is not done ([HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8)).

## 6. Verified state {#GG-§6}

> Status legend: ✅ done (verified) · 🟡 partial · ⬜ planned · living.

**Build/test environment:**

- **Engine:** Unity `6000.4.3f1` (`ProjectSettings/ProjectVersion.txt`).
- **Rules + meta suite:** `pwsh tools/test.ps1` → `dotnet test Tests/Core.Tests` then `tools/check_glyphs.py`. `-Compile` adds a Unity batchmode compile (`GaidenBuild.CompileCheck` via `tools/unity.ps1`, which refuses to run while the project is open in the Editor). Last run (2026-10-09): **84 tests, 84 passed, ~36 s.**
- **Suites** (`Tests/Core.Tests/`): `BoardRulesTests` (slide, pincer geometry, chains, supporters, 2×2 boss, waves, victory/loot), `TimelineAndAbilityTests` (act-advanced clock, enemy turns, shield, pushback, spells, cast queue + cap, Frozen/Wet/Steam/Shock, potion, teleport, Quicken, enemy charge, Sleep, weapon wear, Double Attack / Counter passives), `CatalogTests` (every hero ability, every enemy charge, every consumable's effect, every recipe, every class through the real engine; Smoke Bomb escape; Camp potion/Tent), `ContentAndSoakTests` (content validation, bot soak over every stage, deterministic replay), `MetaLoopTests` (new game, save round-trip, full loop through every Camp service, defeat), `BalanceReport` (plays the whole campaign with natural progression for greedy / casual / button-masher bots, with and without `CampRoutine`, and prints a win% / actions / HP-left table per stage; asserts a greedy bot can finish), shared fixtures in `TestKit.cs` (`Mini()` content, `AssertInvariants`).
- **Bot soak:** `Bots_play_every_stage_without_breaking_invariants` plays all 19 stages × 12 seeds at bot randomness 0 and 0.6 (300-action cap) asserting structural invariants after every command (HP in range, one unit per tile, in bounds, bank ≤ 12, no timeline entry owned by a dead unit, both teams alive while the battle runs). `BotRunner` throws on a soft-lock (6 refusals in a row).
- **Automation harness:** dev players compile in `AutomationServer` (localhost HTTP, `-automation <port>`); taps/drags/presses are dispatched straight into the UI Toolkit panel, so they land whether or not the player window has OS focus, and reply `ok:false` with an error when they hit nothing or never reach the panel (GG-T121); `tools/harness.py smoke` plays title → new game → camp → Adventure → crawl → battle on Auto → results; `sweep` screenshots and layout-audits 15 Camp screens. Artifacts: `artifacts/smoke1/`, `artifacts/sweep/`, `artifacts/run1/` (ad-hoc drag/pincer captures).
- **`-demo`:** boots straight into a scripted self-play loop with no harness attached — character select (auto-picks the starting three at random, §25.15; amended 2026-10-19, GG-T118) → crawl (auto-skipped) → battle (autoplayed) → results (auto-advances to the next stage, or retries on defeat) → …, restarting with a fresh save and a fresh random party when the campaign ends or nothing is launchable (`GameApp.DemoMode`). Formalizes what `tools/harness.py campaign` drives over HTTP as a startup flag, for unattended soak-play or a demo reel.
- **`-testmode` (GG-T93, owner 2026-10-19, bot-only):** self-plays the whole *mechanical* loop end to end with no story, crawl, overworld or tutorial — a throwaway save on slot -1 (never written; `TestLoop.NewSave`: the small starting party, tutorial done, every crawl seen, every Camp NPC joined (GG-T119: the headless beats would bring them too, but cycle 1 shouldn't wait for them); **amended 2026-10-19, GG-T118:** no story is shown, but `TestCycle.Begin` plays each stage's pending story beats headlessly (`DialogueRunner.PlayPendingBeatsHeadless`, first choice) so the story recruits still join, and the cycle line reports them as `story: …`) → launch a stage → battle (autoplayed) → results → **back to the Hub every fight** → `CampRoutine` (salvage/repair, Inn, forge/buy/equip, learn + assign, potions) → launch again, forever (`GameApp.TestMode`). Unlike `-demo` it visits camp and spends between every battle. Difficulty follows the party: on a `SaveData.TestMode` save `Progression.StageRecommendedLevel` returns `Progression.PartyLevel` (rounded average hero level) instead of `RecommendedLevel(campaignIndex)`/`LevelOverride`, and spawns stay max(authored, recommended), so enemies climb as the party levels. Stage pick (`TestLoop.PickStage`): the active expedition's next battle, else battle 1 of the highest unlocked theme, stepping back one theme per 3 losses in a row (a lost expedition restarts its theme). One `[TestMode] cycle N | stage … | party Lv a->b | enemy Lv … | result | +XP | +gold | mats … | camp gold x->y: <every CampRoutine action>` line per cycle; the automation `state` dump carries `testmode.cycles`. *(Verified: `TestModeTests` — enemy level rises with party level in test mode and is unchanged otherwise, stage picking, a 4-cycle headless loop; `tools/harness.py testmode N` runs the real player and screenshots each cycle's battle, level-up, results and camp into `artifacts/testmode/` plus `testmode.log`, checking every cycle logged, battle/results/hub seen per cycle, party level never falling and zero errors.)*
- **Tile-invariant safety net (added 2026-10-11; root causes closed 2026-10-19, GG-T97):** two actors never share a tile once a slide is over — see [§1.1.3](#113-one-actor-per-tile--the-occupancy-guarantee-added-2026-10-19-owner-direction-gg-t97) for the rule and the guarantee per path. Every position write goes through `Relocate` / `MoveMovable` / `AddUnit`; `EnforceTileInvariant` (after every command and `BattleEngine.Create`) is only the last-resort net: in release it self-heals and logs a `TileConflictEvent` (error log, dev-build banner, `board_audit.tile_conflicts`); under `BattleEngine.StrictTiles` — on for the whole test suite — it throws, and the check also runs after every enemy turn, cast and reinforcement inside a command. Root causes closed by GG-T97: a snake head stepping onto the amplifier left it under segment 1; a Hopper leap didn't check movables; a Revival Charm on a full board was spent for nothing; a reinforcement with no room was silently dropped (it now waits); an egg's event reported its intended tile even when it spawned elsewhere; a hero start with no free tile fell back onto the taken one; and the view placed spawned/displaced tokens at their end-of-command tiles mid-playback. The strict soak (every stage × 24 seeds × 2 bots, 1500-action cap, plus 2500-action Crucible runs) and the 191 battle records on disk (2026-10-19: none contains a `TileConflict` event or an overlapping state) show no remaining occurrence.

**Proven working** (evidence per story in [`USER_STORIES.md`](USER_STORIES.md)):

- ✅ Board rules: slide/displacement, edge stop, no-op drag cancel, pincer geometry, chains, supporters, 2×2 immovable + width pincer, waves, victory/defeat (`BoardRulesTests`).
- ✅ Act-advanced timeline: clock only on committed actions, enemy turns on arrival, Zone pushback, Quicken, Shield, Frozen halt (`TimelineAndAbilityTests`).
- ✅ Mana: shared capped bank of generic orbs, pincer mint, supporter mint, crit bonus orb, overflow.
- ✅ Every hero ability, enemy charge, recipe and class resolves through the real engine (`CatalogTests`).
- ✅ Determinism and bot soak across all content (`ContentAndSoakTests`).
- ✅ Camp loop: new game, save round-trip, battle commit, merchant, forge, equip, repair, inn, learn + assign, summon, party cap, bounty (`MetaLoopTests`); every Camp screen renders (harness sweep).

- ✅ Every consumable has a working battle effect or is camp-only; Smoke Bomb escape; Camp item use.

**Built but not yet asserted** (🟡 — see USER_STORIES): boss phases, snake chain, traps, enemy pincers and AI weights (soak-only), Clutch, time bank, battle-start orbs, buff tick timing details, wounds carry-over, first-battles hints, colorblind/reduce-motion.

**Verification gates (2026-10-10):** `tools/test.ps1` (152 dotnet tests + glyph + audio checks; `-Ui` adds 45 player checks), `tools/webcheck.mjs` (WebGL, 4 checks), `tools/androidcheck.py` (Android emulator, 7 checks); earlier `tools/test.ps1` (148+ dotnet tests incl. every ability/charge/item/recipe/class, bot soak, balance report), `tools/uicheck.py` (35 assertions against the real player: screens, music, HUD, drag preview, targeting, long-press, pause, hints, settings, letterbox, fps, zero errors), `tools/harness.py campaign` (consecutive autoplayed stages). See [AMENDMENTS](AMENDMENTS.md) "parity batch" for the systems added in that pass (Ranged/Mechanical archetypes, stacking policies, armor wear, relic passives, weapon swap, Black/Green spells, 55 class kits).

**Amended 2026-10-21 (GG-T17):** `tools/harness.py campaign 25` cleared the full 25-stage campaign (15 main + 10 postgame, GG-T10) end to end in one unattended live run for the first time, including the Frostmaw Caverns expedition (stages 9–11, 0-based) that had been the automation frontier in earlier sessions. Four single-attempt losses (stages 8, 13, 17, 19) each recovered on the harness's first scripted retry; no stage hit `DIFFICULTY_WALL`, no stall/timeout, and `errors: []` — zero engine errors across the whole run, with real gold/level progression through all 25 stages (`dotnet tests` 1024/1024 the same session). Confirms GG-T20's orphaned-confirm-veil fix (closing GG-T16) also cleared the click-reliability risk that had made a full unbroken run unachievable before.

## 7. Active frontier {#GG-§7}

- **Parity backlog** — [`USER_STORIES.md`](USER_STORIES.md): every 🟡 needs a targeted test; every ⬜ is unbuilt spec (full 7-line combat feed with icons, long-press tooltips, armor wear, relic passives, Ranged archetype). **Amended 2026-10-13:** "distinct kits for the 55 default-kit classes" is done — see §23.2.2; ability-level `CastVfx/ProjectileVfx/ImpactVfx/LingerVfx` name wiring is partially done (§6.2: Scan/Cleanse/Hasten utility spells) but still open for damage/buff spells, which read by `DamageType`/buff id instead.
- **Defects fixed 2026-10-09** — Mug now hits once (its row's BaseDamage); Silence also stops enemy charges (`EnemyPlanner.PlanCast`); Frozen skips the boundary of the action that applied it, so it costs the enemy one full cycle; the time bank carries until the next enemy turn instead of clearing each action. Each has a regression test in `AiAndSystemsTests`.
- **V2 direction** (not in this build window): GridGame2026 `docs/rfc/0002-v2-vision.md` — random summoning, branching story, HD art, content breadth.
- **Open design questions** — [§29](#29-open-design-questions).

## 8. Quality bar {#GG-§8}

Definition of done for a feature (also the per-story DoD in [`USER_STORIES.md`](USER_STORIES.md)):

1. **Compiles** — `tools/test.ps1` green (dotnet) and, for App changes, `tools/test.ps1 -Compile` shows zero `error CS`.
2. **Bible section updated** — no silent drift ([GG-LAW-7](#GG-LAW-7)).
3. **Rules are tested** — a named test in `Tests/Core.Tests` for every new rule; content changes keep `Real_content_loads_and_cross_references_resolve` and the bot soak green.
4. **Visible things are harnessed** — a `tools/harness.py` step (or a new one) reaches it and the layout audit is clean; screenshots land in `artifacts/<run>/`.
5. **Glyph check green** ([GG-LAW-11](#GG-LAW-11)).
6. **One commit at the end** — verify-then-checkpoint, no mid-phase commits.

A story is ✅ only when 1–6 hold; otherwise 🟡/⬜ ([GG-LAW-12](#GG-LAW-12)).

## 9. Glossary {#GG-§9}

- **Slide / displacement** — dragging a hero onto an occupied tile moves the displaced actor (ally or foe) into the tile the hero just left. ([§1.1](#11-sliding-and-displacement))
- **Pincer** — two allies in the same row/column with a contiguous, ally-free, ≥1-opponent line between them; the only source of hero physical damage. ([§1.2](#12-pincer-attacks))
- **Supporter** — an ally cardinally adjacent to a pincer attacker; strikes every trapped opponent at 50% and mints its own orb. ([§1.2.3](#123-supporters))
- **Action** — one committed command (move, slot use, shield); costs `BattleConfig.ActionTicks` = 20 ticks = 2.0 s of game clock (Shield instead runs to the next enemy turn). ([§2.0](#20-the-act-advanced-clock))
- **Tick / IP** — the clock unit (10 per second); turn icons hold Initiative Points 0..1000 (`u = IP/1000`).
- **Pushback Zone** — the rightmost 35% of the bar (IP ≥ 650); hitting an enemy there shoves its icon back and stuns it. ([§2.3](#23-pushback-the-interrupt-by-hitting-mechanic))
- **Hasten / Quicken** — the inverse: slide an icon forward toward the trigger. ([§2.7.1](#271-hasten--quicken--the-inverse))
- **Cast icon** — a spell with a cast time rides the timeline below the line and resolves when its remaining ticks reach 0. ([§2.6](#26-one-timeline-two-lanes--turn-icons-above-cast-icons-below))
- **Clutch** — rare LCK interrupt outcome on hero casts: the hit is shrugged and the cast resolves on the spot. ([§13.4](#134-the-interrupt-path))
- **ManaBank / orb** — shared 12-orb pool of identical orbs; any orbs pay any spell. ([§3.1](#31-mana-the-orb-economy))
- **Drag timer** — optional Terra Battle–style 4 s limit per drag (Settings). ([§2.9](#29-drag-timer-optional))
- **Time-banked orbs** — Blue orbs minted from drag-timer seconds left at the drop. ([§3.1.8](#318-time-banked-orbs))
- **Countdown badge** — the number on an enemy: actions you get before it acts. ([§2.10](#210-countdown-badge))
- **AbilityBar** — the 5-slot Skill / Spell / Item bar of the selected hero; 2 usable on a fresh save, up to 5 by campaign progress. ([§4](#4-the-abilitybar))
- **Camp** — the Final Fantasy–style hub menu (`HubScreen`). ([§25](#25-the-camp-merged-hub))
- **Event player** — `BattleScreen.Play`, which animates an event list one awaited beat at a time.
- ~~**Undearth**~~ — struck 2026-10-10 (owner): the missing-sun / Undearth lore is removed in favor of the Longmother/blight premise that landed in GG-T120 (US-348).
- **The Longmother** — the titanic, unnamed-age spider the town is built on; she never stops walking because standing ground sours into the blight. ([§27](#27-story-crawl-no-dialog))
- **The blight** — the biomechanical plague souring the ground behind the Longmother's path and fusing the living with the grown-not-mined/built-not-buried; not a single named villain. ([§27](#27-story-crawl-no-dialog))
- **Story crawl** — the skippable per-theme intro text shown on first entry into a theme. ([§27](#27-story-crawl-no-dialog))
- **Summon Circle** — the Camp service where gold recruits a hero class. ([§25.10](#2510-summon-circle))
- **Bounty** — a posted kill contract: accept one, track kills, claim gold + an item. ([§22.4](#224-bounty-board))

For combat/targeting/VFX sub-vocabulary see [Appendix §18 Glossary](#18-glossary).

---
<a id="gg-appendix-a"></a>

# Appendix A — Design Canon

> The detailed reference behind the nine sections above; the L0 sections and `USER_STORIES.md` cite it by `§` number (numbering follows the GridGame2026 bible; §16 and §21 are intentionally absent).

## 0. North Star

A tactical-RPG hybrid where the player **drags heroes one tile at a time across a 6×8 grid** to flank enemies and trigger **pincer attacks**. Combat runs on a **timeline** of enemy icons that "load" left → right and act when they arrive; the clock advances only when the player commits an action. Magic, items and class skills are managed through the **AbilityBar**. Between battles the party rests at a Final Fantasy–style **Camp** menu.

The game is a **deterministic pure-C# simulation** (`Gaiden.Core`) with a **code-built UI Toolkit front end** (`Gaiden.App`) that plays the simulation's events back. There are no prefabs, no hand-built scenes and no Inspector wiring; art, audio and content load from `Resources`.

### 0.1 Design pillars

1. **The verb is "slide".** Heroes never "land on" enemies. They flank into tiles; damage comes from the geometry that forms, not the move itself.
2. **Time is the resource the player manages.** Every decision trades against "whose icon lands first?" — but the clock waits for you. **Amended 2026-10-09:** time is spent per action, not per real second.
3. **Mana is a shared, visible pool.** The 12-slot ManaBank is the whole party's spell budget.
4. **Every spell is a (shape × mode × filter) triad.** Adding a spell is a data row.
5. **The simulation is the truth.** **Amended 2026-10-09:** replaces "Code is the source of truth; the builder is the spec". Core decides; the view only animates what Core already resolved.
6. **Verify-then-checkpoint.** Land a feature end-to-end with test + harness evidence before committing.
7. **The bible is the brief.** If code and bible disagree, fix the disagreement in writing.

### 0.2 Non-goals (what this game is NOT)

- **Not real-time, and not "press attack, wait for enemy to press attack."** **Amended 2026-10-09:** the old bible said "the timeline is continuous; thinking time is also game-clock time." In GridGame Gaiden the clock is **act-advanced**: it does not move while you look, drag or pick a target. Each committed action spends 2.0 s of game clock, during which any number of enemy icons and casts may arrive and resolve in arrival order. There is no hero turn order — any living, mobile hero may act each time. The pressure comes from the countdown badges and the Zone, not from a ticking wall clock (an optional drag timer restores Terra Battle's hand pressure, §2.9).
- Every other non-goal is unchanged from [§3](#GG-§3): not a deck-builder, roguelike, gacha / live service, multiplayer game, grid-puzzle, free-form mover or stat-spreadsheet.

### 0.3 Session shape (target pacing)

| Beat | Target | Felt as |
|---|---|---|
| One **battle** | 15–40 committed actions (~2–5 min at default battle speed) | A single decisive engagement |
| One **Camp visit** | 30–60 s | Quick tweak, not a menu trawl |
| One **stage** (battle + results + Camp) | 3–4 min | A "round" |
| One **session** (5–8 stages) | 20–30 min | A commute, a couch break |
| Full **playthrough** (15 campaign stages + farming) | 6–10 h | A weekend campaign |

**Amended 2026-10-09:** battle length is measured in actions because wall-clock time now depends on the player and on Settings → Battle speed. `BalanceReport.Campaign_pacing_table` is the tuning dashboard: it plays the campaign (15 main + 10 postgame stages, as of 2026-10-13) in order with natural progression (XP, carried wounds, Inn, retries) for bots of three skill levels, with and without `CampRoutine`, and prints win %, average actions and HP left per stage.

---

## 1. The Board

- **6 columns × 8 rows** (`BattleConfig.Cols/Rows`). **Amended 2026-10-09:** coordinates are 0-based `Coord(X, Y)`, X = column left→right, Y = row top→bottom; (0,0) is top-left.
- Each tile holds at most **one actor**; occupancy is footprint-aware via `BattleState.UnitAt(tile)` / `Unit.Occupies(tile)`.
- Heroes start at fixed tiles (1,6), (4,6), (2,7), (3,7), (0,7), (5,7) unless a `HeroSpec.Start` overrides. Enemies use their `SpawnDef` tile if the footprint fits, else a random free tile in rows 0–4 (then anywhere). **Amended 2026-10-09** (spawn placement is now explicit rule, not scene layout).
- On screen the board is drawn by `App/Battle/BoardView.cs`: 180-px tiles at design (45, 450).

### 1.1 Sliding and displacement

The player drags a hero **one tile at a time, cardinal only**. The UI turns the finger position into cardinal steps (`BattleScreen.ExtendPath`: dominant axis first; if that step is blocked, try the other axis once, else hold position) and the committed `MoveCommand.Path` is a list of one-tile steps.

When a step enters a tile **already occupied** by any actor (ally **or** enemy):
- The dragging hero takes that tile.
- The occupant is moved to the tile the dragging hero **just left**.
- This cascades step by step as the hero passes through several occupants.

```
before:              H A E E . .
drag H east 1 tile:  A H E E . .    (A moved into H's old tile)
drag H east again:   A E H E . .    (the first E moved into H's old tile)
```

**Refused steps** (`StepBlockedEvent`; the rest of the path is dropped): a non-adjacent step (diagonal), a step off the board ("edge"), or a step into a multi-tile enemy or snake-chain member ("immovable"). **Amended 2026-10-09:** a drag that ends with nothing changed (no position, HP or trap change) is a **cancel**, not an action — no clock cost (`Noop_drag_costs_no_time`).

Side effects of a step: Sleep (and any `BreaksOnMove` buff) is removed from a displaced actor and from the dragger; a hero that steps or is displaced onto a trap triggers it ([§14.2](#142-enemy-archetypes-design-palette)).

**Damage rule**: movement never deals damage ([GG-LAW-1](#GG-LAW-1)).

### 1.1.1 Physical blocks (added 2026-10-10, owner direction)

Owner: "the grid can have physical blocks that disallow entrance … nobody moving through or landing or spawning on a physical block."

- `StageDef.Blocks` lists block tiles (rocks, pillars, dead trees); `BattleState.Blocks` holds them for the battle. **`BattleState.Walkable(c)`** (on the board and not a block) is the single collider check every rule uses: hero steps (`StepBlockedEvent` "block"), enemy steps and 2×2 footprints, snake moves, teleports, spawns (initial and reinforcements), trap laying and amplifier spawns/shoves.
- Displacement can never push a unit into a block — the displaced unit always takes the tile the mover just left.
- A block is not an opponent, so a block inside a line **breaks the pincer**. Blocks also stop ranged line of sight and line charges.
- Content: Mireholt Fens III, all three Frostmaw Caverns and all three city-ruin stages carry 2–3 blocks, kept off the hero start rows (6–7). The bot soak plays every stage with them.
- View: a raised, bevelled stone block (the biome tile, lit top edge, dark base, drop shadow).
- *(Tests: `A_hero_cannot_step_onto_a_block`, `A_block_in_the_line_breaks_the_pincer`, `Nobody_spawns_or_walks_onto_blocks`, `Real_stages_carry_their_blocks`.)*

### 1.1.2 Movable tiles (added 2026-10-10, owner direction)

Owner: "nothing can spawn on top of the Power Amplifier but they can push it around like any other actor … a MovableTile as opposed to a stone which can't be moved."

- `MovableTile` (`BattleState.Movables`, kind `Amplifier` for now) occupies its tile like an actor: `BattleState.FreeForSpawn(c)` (walkable, no unit, no movable) gates every spawn, reinforcement, trap, trail and teleport landing (the amplifier spawn uses the stricter `FreeForItem`, which also keeps it off pickups — GG-T129).
- It is **displaced exactly like an actor**: a hero stepping into it swaps it into the tile just left; an enemy stepping into it does the same; a 2×2 drags it to its vacated lane tile; knockback pushes it ahead if there's room. It is never an attacker, a target or a pincer opponent, so it breaks a pincer line like any gap.
- Stones (§1.1.1) are the immovable kind — colliders nothing enters or lands on.
- **One item per tile (added 2026-10-19, owner: "power ups can't share a space either", GG-T129):** a movable is a board *item* as well as an actor, so it never shares a tile with a pickup (§13.3.1) either. It never spawns on a pickup (`BattleState.FreeForItem`), and a shove never lands it on one: a hero swap or enemy step whose "tile just left" holds a pickup sends it to the nearest open, item-free tile instead (`ShoveMovable` → `NearestMovableSpot`; the pickup stays where it lies), and knockback treats a pickup beyond it as a wall (Crash). See §1.1.3.
- **Amplifier alignment (decision, owner may overrule):** since heroes can't stand on a movable tile, a pincer attacker is *aligned* with the amplifier when it is cardinally adjacent to it.
- *(Test: `The_amplifier_is_a_movable_tile_heroes_push_and_nothing_spawns_on`.)*

### 1.1.3 One actor per tile — the occupancy guarantee (added 2026-10-19, owner direction, GG-T97)

Owner: "there can never be two actors occupying the same tile after sliding is over."

**The rule.** Once a slide (any step, shove, swap or appearance) is over, every tile holds at most one actor — a living unit (corpses, remains and eggs included; the dead are off the board) *or* a movable tile — and every actor stands on walkable tiles (§1.1.1), footprint-aware for 2×2s. The only in-between is the two halves of one swap (the stepper arrives, then its partner takes the tile just left), which never survives to the end of a beat.

**Board items too (added 2026-10-19, owner direction, GG-T129).** Owner: "power ups can't share a space either; I see multiple pickups sitting on top of each other." Every tile holds **at most one board item — a pickup (§13.3.1) or a movable tile (§1.1.2) — ever**, and every item lies on a walkable tile. Items are separate from units: a unit may stand on a pickup (pickups don't block; a hero collects it on entry, an enemy just stands there), but never on a movable. One check gates every item placement, `BattleState.FreeForItem` (walkable, no unit, no trap, `ItemFree`: no pickup and no movable) — pickup drops and the amplifier spawn both use it — and `MoveMovable` refuses a landing tile that holds a pickup. (Only a hero-path *preview* ignores pickups there: the preview doesn't collect them, so the tiles the walk crossed still list pickups the real move picks up first.)

**The chokepoint.** Every position write in Core goes through one of three `BattleRunner` methods, which refuse a footprint that isn't open (`FootprintOpen`: walkable, no other living unit, no movable — with the step's own swap partner / shoved heroes / shoved movable named as yielding):
- `Relocate(unit, to)` — every move of a unit already on the board;
- `MoveMovable(movable, to)` — every shove of the Power Amplifier;
- `AddUnit(unit)` — every unit that appears.
A refused write moves nothing (release) or throws (`BattleEngine.StrictTiles`). New units are placed with `FootprintFree` (`FreeForSpawn` + no trap) / `RandomFreeTile` first, so a refusal there is always a bug.

**The guarantee, path by path:**

| Path | How the footprint is guaranteed free | When there's no room |
|---|---|---|
| Hero drag step (`WalkPath`) | Enters an empty tile, or swaps: the occupant (or movable) takes the tile the hero just left — the one tile the step frees. Multi-tile, snake and remains occupants refuse the step ("immovable"); a diagonal slip only enters open space. | Step refused (`StepBlockedEvent`), rest of the path dropped. |
| Enemy 1×1 step (`StepEnemy`) / skirmisher retreat | Destination must hold no unit; a movable there is shoved into the tile just left. | No step. |
| Hopper leap (`LeapEnemy`) | Landing tile must be fully open (no unit, **no movable**). | No leap. |
| 2×2 step + `VacatedTile` shoves | `FootprintStepLegal` refuses any entered tile with an enemy (incl. corpses/remains/eggs); each hero/movable on an entered tile goes to the tile vacated **on its own lane** — one entered and one vacated tile per lane, so two shoves can never meet. | No step. |
| Snake chain (`FollowSnake`) | Each segment slides into the tile the one ahead just left; a movable the head walked onto goes behind the **tail** (it used to land under segment 1 — fixed in GG-T97). | The chain stops at the first segment that can't follow. |
| Knockback (`Knockback`) | Each tile of the push must be walkable and unit-free; a movable ahead is pushed on only if the tile beyond it is free. | Crash (unit / movable that can't move) or Slam (edge / block): damage, no move. |
| Teleport ability, delayed or instant | Landing footprint must be open at resolution time. | "Blocked", caster stays. |
| Blink / burrow (`SurfaceBeside`) | Only a free, trap-free tile beside the prey. | Normal turn instead. |
| Movable shoves (`ShoveMovable`, hero swaps included) | Only onto a walkable tile nobody stands on that holds no pickup and no other movable (GG-T129). | Nearest open, item-free tile (`NearestMovableSpot`); never left under a unit or on a pickup. |
| Power Amplifier spawn | `FreeForItem`: no unit, no trap, **no pickup** (it used to ignore pickups — GG-T129). | Not spawned this time. |
| Pickup drop (`MaybeDropPickup`) | `FreeForItem` on the fallen enemy's tile, else a cardinal neighbour. | No drop. |
| Party at battle start | `HeroSpec.Start`/default start if free, else a random free tile in rows 4–7, else anywhere. | The hero sits the battle out off the board (only possible on a stage walled with blocks). |
| Wave spawns (`SpawnWave`, incl. snakes) | `SpawnDef` tile if the footprint fits, else random free rows 0–4, else anywhere; segments on free adjacent tiles. | That spawn is skipped. |
| Delayed reinforcements (§14.8) | As wave spawns. | **Waits** in the queue and retries every tick until a tile frees; gives up only when no other enemy is left to free one (so the wave can still end). |
| Eggs (`LayEgg`) | A free, trap-free tile beside the layer's footprint; the event reports the egg's real tile. | No egg this turn. |
| Remains / corpses (`LeaveRemains`), rising and hatching | On the tile the unit just vacated by dying (the remains/egg is removed before its successor spawns). | Not left / successor placed by the spawn rules. |
| Guests (`SpawnGuests`) | Their tile if free, else random free rows 3–7, else anywhere. | Guest doesn't join. |
| Revival Charm (`Revive`) | The free, trap-free tile nearest the user — never back onto its old tile if someone stands there now. | Refused before the charm is spent ("No room!"). |

**The net.** `BattleRunner.EnforceTileInvariant` still runs after every command (and after `BattleEngine.Create`) as a last resort, and since GG-T129 `FindTileConflict` covers items as well (two pickups on a tile, a pickup under a movable, a pickup off the board): in a release build it nudges the newer actor to a free tile (a movable on a pickup slides to the nearest item-free tile; the newer of two stacked pickups fades with a `PickupExpiredEvent`, so its token goes too) and emits a `TileConflictEvent`, which the App logs as an error, shows on screen in dev builds and counts in `/state → board_audit`. Under `BattleEngine.StrictTiles` (the whole test suite turns it on; `BattleState.StrictTilesOverride` lets one test opt out) it throws `TileConflictException` instead — and the same check also runs after every enemy turn, cast and reinforcement *inside* a command, so a conflict is caught where it happens.

**The view.** Playback places every token where the event says it was at that moment — spawn/reinforcement tiles (`WaveStartedEvent.SpawnedAt`, `ReinforcementsArrivedEvent.SpawnedAt`, GG-T98), remains/eggs/hatchlings/risen/guests at their event `At`, and the drag prefix snaps each unit to its last prefix step, not its end-of-command tile. `BoardAudit` checks after every beat that no two tokens are drawn on one tile (a swap's two halves excepted), as playback settles that every token sits exactly on its unit's `State` tile (before `SyncAll` could hide a miss as a jump), and after `SyncAll` that tokens are unique and rendered on their tiles. Since GG-T129 it audits **item tokens** the same way (`AuditItemTokens`): after every beat no two item tokens (pickups, the amplifier diamond) are drawn on one tile, and against the state no stale token — every drawn pickup still lies where the state has it, every pickup in the state is drawn, and the amplifier is drawn exactly where (and only while) it stands. Collected and faded pickups remove their tokens (`PickupCollectAnim` / `PickupExpiredAnim`, with `SyncPickups` as the net), a used amplifier hides its diamond.

*(Tests: `TileOccupancyTests` — strict throw, release heal (unit and amplifier), long drag through a crowd, knockback into unit/edge/block/amplifier, 2×2 shoving a crowded lane (two heroes, hero + amplifier) and blocked by an enemy body, snake head onto the amplifier, delayed teleport onto a tile taken meanwhile, blink with no free side, colliding party/enemy spawn tiles, a party bigger than the board, guest onto a taken tile, reinforcement onto its own taken tile and onto a full board, egg laying, remains/rising, revive onto an occupied tile and on a full board; GG-T129 board items: the amplifier never spawns on a pickup, an enemy leaving a pickup shoves the amplifier aside, a hero swap never sends it onto a pickup, knockback stops at a pickup behind the amplifier, strict throw on a pickup under the amplifier, release heal of two stacked pickups, and a strict soak with drops and amplifier shoves (`Strict_soak_with_drops_and_amplifier_shoves_never_stacks_items`); `TestKit.AssertInvariants` asserts items apart in every soak; and the strict bot soak: every stage × 24 seeds × 2 bot temperaments at a 1500-action cap plus long Crucible runs. Harness: `python tools/harness.py boardaudit N` plays N autoplayed battles in the real player and fails on any `board_audit` problem, item tokens included.)*

### 1.2 Pincer attacks

A pincer is **two allies in the same row OR column, with every tile between them covered by opponents** — no gaps, no allies in the line, at least one opponent ([GG-LAW-2](#GG-LAW-2)).

```
Valid horizontal pincer (3 enemies):     Valid vertical (2 enemies):
H E E E H .                              . H . . . .
                                         . E . . . .
INVALID — gap:   H E . E H .             . E . . . .
INVALID — ally:  H E A E H .             . H . . . .
INVALID — diagonal: no.
```

#### 1.2.1 Trigger

`BattleRunner.ResolvePincers(Team.Hero, hero)` runs after:
- a committed hero drag (`DoMove`), and
- a Teleport landing (`ResolveAbility`, [§5.3](#53-special-targeting-flows)).

The scan (`PincerDetector.Detect`) is **board-wide** — every valid pair resolves, not only pairs involving the dropped hero. Enemies run the same scan for their own team on their turn ([§14](#14-ai)).

**Amended 2026-10-11** (owner: a manual drag out and back into the start tile wasn't pincering): a drag that ends with no position/HP/trap/pickup change is normally a free cancel ([§1.1](#11-movement), `Noop_drag_costs_no_time`) — but if the hero's final tile (even its own start tile) completes a pincer, that always counts as the turn; `DoMove` checks `PincerDetector.Detect` before deciding a drag was a no-op. *(Test: `Dragging_out_and_back_to_the_start_tile_still_pincers_if_it_lands_back_in_position`.)*

#### 1.2.2 Ordering of multiple pincers

**Amended 2026-10-09** (`PincerDetector.Order`):
1. The first pair is one **including the dropped hero** (if any), oriented so it is attacker A.
2. Next, a pair sharing the previous link's second attacker (**chain**), oriented so the shared hero leads.
3. Otherwise the pair **nearest** (Manhattan) to the previous link's end.

`PincerFormedEvent.ChainIndex` numbers the links; the view shows "PINCER!" then "CHAIN ×N!".

#### 1.2.3 Supporters

**Amended 2026-10-09:** any **living ally cardinally adjacent to a pincer attacker** (other than its partner) is a supporter — there is no line-of-sight/axis requirement (`PincerDetector.FindSupporters`). Supporters of both attackers are merged and de-duplicated. Each supporter strikes **every trapped opponent** with a physical attack at **`BattleConfig.SupporterDamageFraction` = 50%** damage, then mints one orb of its own color ([§3.1.2](#312-harvesting)). Supporters need not be Humanoid.

#### 1.2.4 Resolution order inside one pincer

1. `PincerFormedEvent` (attackers, opponents, supporters).
2. Attacker A strikes each living opponent in line order; then attacker B does the same. **Both endpoints hit the whole line.** A hero whose weapon is on its last durability point strikes at ×1.5 and then shatters ([§24.5](#245-weapon-durability)). An attacker with the **Double Attack** passive strikes each opponent a second time at ×0.5; any struck defender with **Counter Attack** that survives a landing hit strikes back at ×0.5 ([§7](#7-the-spell-catalog)).
3. Each supporter strikes each living opponent at 50%.
4. (Hero team only) each living attacker, then each living supporter, mints one orb.

A pair whose opponents are all already dead (killed by an earlier link) is skipped. **Amended 2026-10-09:** the old cosmetic "Respite" spin is not built.

#### 1.2.5 No pincer = legal

A drop with no valid pincer is a legal action: the hero stays, the clock advances one action.

#### 1.2.6 Power Amplifier (added 2026-10-10, owner request)

Owner: "Add in the Power Amplifier from the original Terra Battle … if you align attacker1 or attacker2 to it it enhances the attack; it spawns on a random schedule modified by the cumulative luck of the Hero team; the Enemy team cannot utilize it but can move through it to reposition it, with the intent of breaking enhanced pincer combos."

- **Spawn:** when control returns to the player and no amplifier is on the board, it appears on a random free tile (`FreeForItem`: no unit, trap or pickup — two items never share a tile, §1.1.3) with chance `clamp(0.12 + 0.003 × party Luck, 0, 0.45)` (`Formulas.AmplifierChance`; `AmplifierSpawnedEvent`). At most one at a time. It is a movable tile (§1.1.2): it occupies its tile and is pushed around like an actor.
- **Use:** a hero **pincer attacker cardinally adjacent to it** (aligned, §1.1.2) when its pincer resolves strikes at **×1.75** and mints a bonus orb ("amplifier"); the amplifier is used up (`AmplifierUsedEvent`). Supporters don't trigger it.
- **Enemies:** never use it. An enemy stepping into it **pushes it to the tile the enemy just left** (a 2×2 drags it along its lane; `AmplifierMovedEvent`). Clever personalities (Weakest, Threat) seek it out to break an amplified setup (+4 step score).
- **View:** a pulsing gold diamond on its tile; pops in, slides when shoved, flashes a ring and "AMPLIFIED!" when used.
- *(Tests: `Amplifier_chance_grows_with_party_luck_and_caps`, `An_attacker_on_the_amplifier_strikes_harder_adds_mana_charge_and_uses_it_up`, `Enemies_cannot_use_the_amplifier_but_stepping_on_it_shoves_it_back`.)*

#### 1.2.7 Outline & view layering (GG-T65 investigation, 2026-10-21)

The pincer outline (`board.Lines.Outline`, `BoardView.GroupRect`) is drawn **exactly one tile thick along the pincer's axis**, widened only by the same multi-tile-opponent allowance [GG-LAW-2](#GG-LAW-2) already states (`PincerGeometry.Error`'s guard: one big opponent may widen it to its own width; two opposed big opponents, `2×widest−1`).

GG-T65 was opened against a screenshot of a ~2×3 gold loop wrapping a "SUPPORT" cut-in card, two hero portraits and a "MISS" float — wider than this battle's geometry should ever draw. Investigating it found two separate, since-fixed view defects, not a hole in `PincerDetector`/`PincerGeometry`:

- **GG-T98** (closed): the outline read live `State` positions, already end-of-command by playback time — a unit that moved later in the same command (an enemy turn, knockback, a later chain link) stretched the box across wherever it ended up. Fixed by snapshotting every member's tile onto `PincerFormedEvent`/`UnitFootprint` at formation time, guarded by `PincerGeometry.Error` in dev builds. *(Test: `PincerGeometryTests.Pincer_reports_formation_time_tiles_when_the_foe_darts_away_in_the_same_command`.)*
- **GG-T128** (closed): an earlier pass at this same ticket's z-order need (cut-in cards must never cover the outline) reparented `board.Lines` out of the board and onto `App.Overlay`, so it stopped inheriting `boardHost`'s camera twist/zoom/shake — it then sat still while the board (and anything riding it) tilted or shook, reading as a mismatched box around whatever UI shared the frame. Fixed by putting the line layer back inside the board's transformed tree and meeting the z-order need with a board-local `cutInLayer` sitting just below `board.Lines` instead.

Current layering (verified against both fixes, `BattleScreen.cs`'s board-build comment: "tiles, units, marks, fx, cut-in cards, lines"): `board.Lines` (outline + drag path) and `cutInLayer` (`ActionCard` pop-ups — `SupporterPop`, chained `PopUpCards`, `CasterSlide`) both live inside `board.Root`/`boardHost`, so the outline and these cards ride every camera twist, zoom, drift and shake together, and the cards never draw over the outline. The full-screen `PincerCutIn` portrait sweep (first link of a chain only) is deliberately left outside that tree — a cinematic overlay unrelated to tile alignment, same as the GridGame2026 cut-in it ports (US-145) — and GG-T128's own fix evidence recorded that this split still meets GG-T65's original z-order need. GG-T126 separately confirmed the Pause veil draws above every one of these battle layers, including mid-chain.

No change was needed in `PincerDetector`/`PincerGeometry`/`BoardView.GroupRect`: the shape rule they enforce (one tile thick, widened only by a multi-tile opponent) is correct and tested; the ~2×3 box this ticket reported was these two now-fixed view bugs.

### 1.3 Humanoid restriction

Only **Humanoid** actors can be pincer attackers. Heroes are always Humanoid (`Unit.IsHumanoid`); enemies need the `Humanoid` tag (71 of 110 enemy classes). Multi-tile actors are never attackers. Humanoid 1×1 enemies **seek** pincers (+50 move score, [§14.1.2](#1412-score-weight-cheat-sheet)) and, **Amended 2026-10-09**, **resolve** them on their turn: after moving, if the enemy is one end of an enemy-team pincer, every such pincer resolves against the trapped heroes (no orbs are minted for enemies); otherwise it melees ([§14.1](#141-enemy-planning)).

### 1.4 A full action, end-to-end (worked example)

Cleric `[C]` at (2,3) and Knight `[K]` at (5,3); two goblins `[g]` at (3,3) and (4,3).

```
Step 0:   col: 0  1  2   3   4   5          Step 1 (Knight drags west one tile):
  row 3:      .  . [C] [g] [g] [K]            row 3:  .  . [C] [g] [K] [g]
```

1. The Knight enters (4,3); the goblin there is displaced into (5,3) (`DisplacedEvent`).
2. `DoMove` commits; `ResolvePincers` finds C(2,3) — g(3,3) — K(4,3): one opponent, valid.
3. The Knight (the dropped hero, attacker A) strikes, then the Cleric; no supporters.
4. Two orbs mint, one per attacker.
5. If the goblin's icon was in the Zone, each landing hit pushes it back and stuns it ([§2.3](#23-pushback-the-interrupt-by-hitting-mechanic)).
6. The clock advances 20 ticks; any enemy whose icon reaches 1000 in that window takes its turn; any cast that reaches 0 resolves.
7. Control returns: hero turn-unit buffs and skill cooldowns tick, enemy Frozen ticks, `PlayerControlEvent`.

That is the full beat: **drag → slide cascade → pincer → strikes → orbs → pushback → clock → enemy turns/casts → control returns.** *(Test: `Worked_example_1_4_slide_then_pincer`.)*

### 1.5 Multi-tile actors (2×2 bosses)

Enemies may occupy a rectangle (`ClassDef.Footprint`, e.g. `cyclops00` = 2×2); **heroes are always 1×1**. `Unit.Pos` is the anchor (top-left); the actor covers `Pos + (dx,dy)` for `dx<W, dy<H`. Occupancy, adjacency (`Unit.IsAdjacentTo`) and distance (`Unit.DistanceTo`) are footprint-aware. Spawn needs the whole rectangle free (`FootprintFree`, which also refuses trap tiles).

**The four rules:**
1. **Immovable anchor.** A hero step into any boss tile is refused ("immovable"); the boss is never displaced. Pincer hits still push its **timeline icon**.
2. **Pincer by flanking its width.** Every tile between the two attackers must be covered by opponents; the boss's tiles count as **one** opponent. *(Test: `Two_by_two_boss_blocks_slides_and_is_pincered_across_its_width`.)*
3. **Boss shoves heroes (on its turn).** **Amended 2026-10-09:** the boss steps its footprint one cardinal tile (`EnemyPlanner.FootprintStepLegal`: in bounds, no other enemy in the way); every hero in a newly entered tile is moved directly to **the tile the body vacated on that hero's lane** (`BattleRunner.VacatedTile`) — no cascade, never off-board. The step aborts only if a non-hero occupies an entered tile. Shoved heroes wake (BreaksOnMove) and trigger traps.
4. **One timeline icon** regardless of size; the boss melees via footprint adjacency; it never pincers (multi-tile units are not attackers).

---

## 2. The Timeline

A horizontal bar under the top HUD. Enemy turn icons load left→right from IP 0 to the trigger at IP 1000; cast icons count down to 0 on the same u-axis.

```
u = 0.0 (IP 0)                         u = 0.65 (IP 650)             u = 1.0 (IP 1000)
   ┃═════════════════════════════════════║════════════════════════════╣ ACT
   ┃   "Open Track" (65%)                ║   Pushback Zone (35%)      ║
   ┃   icons advance at Speed × 1.6 IP/t ║   every icon crawls at the ║
   ┃                                     ║   same rate (30-tick cross)║
   ┃═════════════════════════════════════║════════════════════════════╣
```

### 2.0 The act-advanced clock

**Amended 2026-10-09 (owner decision):** the clock is **act-advanced**.

- Time is integer **ticks**, `BattleConfig.TicksPerSecond` = 10. The bar is `BarLength` = 1000 IP; `u = IP / 1000`.
- The clock advances **only** inside `BattleEngine.Apply`, after a command is accepted:
  - **Move** or **slot use** → `AdvanceClock(ActionTicks)` = **20 ticks (2.0 s)**.
  - **Shield** → `AdvanceUntilEnemyTurn` ([§2.5](#25-auto-skip--shield)).
- Refused commands and no-op drags cost **nothing** (no ticks, `ActionsTaken` unchanged).
- Each tick: every turn icon advances (or burns a stun tick), every cast icon counts down, tick-unit buffs accumulate ([§8.3](#83-ticking)), then arrivals resolve ([§2.4](#24-turn-order--arrival-order)).
- After the advance, **control returns** (`ReturnControl`): hero turn-unit buffs and skill cooldowns tick, enemy timeline-halting buffs (Frozen) tick — skipping the boundary of the action that applied them — and `PlayerControlEvent`. The pending time bank is kept until the next enemy turn mints it.
- **Only enemies have turn icons.** **Amended 2026-10-09:** heroes have no icons; any living, mobile hero may act on any action. (The old bible drew hero portraits above the line.)

*(Tests: `Clock_only_advances_on_committed_actions`, `Unaffordable_or_silenced_spell_is_refused_without_cost`, `Enemy_acts_when_its_icon_reaches_the_trigger`.)*

### 2.1 Speed and pace

- **Open track** (IP < `ZoneStart` = 650): an icon gains `round(Speed × OpenRatePerSpeed)` IP per tick, `OpenRatePerSpeed` = 1.6 (Speed 10 → 16 IP/tick → ~41 ticks ≈ 2 actions to reach the Zone).
- **Zone** (IP ≥ 650, the rightmost `ZoneU` = **35%**): every icon crawls at the same `(1000 − 650) / ZoneCrossingTicks` = 11 IP/tick (`ZoneCrossingTicks` = 30, so ~32 ticks ≈ 1.6 actions to cross), regardless of stats — a uniform coordination window.
- Rates are multiplied by `Unit.TimelineSpeedMultiplier` (Slowed ×0.5), minimum 1 IP/tick.
- `Speed` is the unit's effective SPD: level SPD + gear SPD + ½ gear AGI ([§3.2](#32-hp-and-stats)).
- A Speed-10 enemy starting at IP 0 acts after ~73 ticks — during your 4th action. **Amended 2026-10-09:** the old ZoneU range 0.25–0.35 is fixed at 0.35.

### 2.2 Icon state

**Amended 2026-10-09:** the old per-icon modes (`Queued / Approaching / PushedBack / Stunned / Resolving`) collapse to data on `TimelineEntry`: `Ip` (turn icons, ≥ 1000 = arrived), `StunTicks` (ticks it will not advance; set by pushback, cleared by Hasten), `RemainingTicks` (cast icons, 0 = arrived) and `AccumulatedDelayTicks` (stagger, [§13.4](#134-the-interrupt-path)). An owner with a **timeline-halting** buff (Frozen) advances neither its turn icon nor its cast icon (`Unit.IsTimelineHalted`).

### 2.3 Pushback (the "interrupt by hitting" mechanic)

When a **hero** damages an **enemy** whose turn icon is in the Zone (`BattleRunner.Pushback`, called from `Damage` for every non-miss hit with a hero source — pincer strikes, supporter strikes, spells, Mug, damage items):
- **Damage is always applied**; pushback only moves the icon.
- `frac = (IP − 650) / 350` (how deep in the Zone).
- `strMult = max(0.5, 1 + (attacker STR − 10) / 40)`.
- `push = min(PushbackMaxU 0.15, PushbackBaseU 0.07 × (0.5 + frac) × strMult)` (in u); IP drops by `round(push × 1000)`, floored at 0.
- `StunTicks = max(1, round(BaseStunTicks 4 / (1 + target AGI / 50)))`.
- Environmental damage (DOT, traps, shatter) never pushes. Every landing hit re-checks, so a second hit on an icon already pushed out of the Zone does nothing — but a big pincer's supporters each land their own hit, so one busy commit can still re-trigger pushback several times on the same target before it finally drops below the Zone.

**Amended 2026-10-09:** the old floor "newU ≥ 0.55" is gone; the cap is 0.40 u. *(Tests: `Pincer_on_in_zone_enemy_pushes_its_icon_back`, `Pincer_outside_zone_has_no_pushback`.)*

**Amended 2026-10-11 (owner: "the pushback on monsters needs to be less; they almost never get to take a turn"):** `PushbackBaseU` 0.18→0.07 and `PushbackMaxU` 0.40→0.15 (both ~60% weaker), `BaseStunTicks` 8→4. A single large party could otherwise re-trigger pushback on the same in-Zone enemy several times in one pincer (once per attacker/supporter hit) before it fell out of the Zone, effectively denying it a turn for most of a battle — this is a magnitude tuning, not a rule change; the mechanic and its relative ordering (deeper in the Zone / higher STR pushes harder) are unchanged.

### 2.4 Turn order = arrival order

Icons advance independently. Within a tick, arrivals resolve one at a time: **cast icons before turn icons, then lower entry id first** (`BattleRunner.Earlier`). **Amended 2026-10-09** (explicit tie rule). A resolving turn resets that icon to IP 0. Pushback, Hasten, Slowed and Frozen reorder icons only by changing IP/rate.

### 2.5 Auto-skip / Shield

The **GUARD** button (right of the timeline) issues `ShieldCommand`:
- Applies **Protection** (`buffs.json` `protection`: incoming damage ×0.85, 1 Turn) to every hero.
- Runs the clock until **one enemy turn has resolved** (cap 2000 ticks), then returns control — Protection expires at that control return, so it covers exactly the skipped stretch.
- Counts as an action; clears the pending time bank.

**Amended 2026-10-09:** it never auto-presses (there is no real-time window to run out). *(Test: `Shield_protects_heroes_and_skips_to_next_enemy_turn`.)*

### 2.6 One timeline, two lanes — turn icons above, cast icons below

**Amended 2026-10-10 (owner requests): one line, every icon on it.** The track is a thin 6 px line; every icon is centred on it.
- **Enemy turn icons (84 px circles):** an opaque dark disc (the line never shows through) masking a **pixel-art sprite of the whole enemy** with enhanced eyes (`Resources/Pixel/<id>.png`; method in [docs/PIXEL_ICONS.md](PIXEL_ICONS.md)); red ring, bosses purple. The turn closest to ACT — **the next attacker** — gets a gold ring and a 1.18× lift. A unit that isn't moving (Frozen, Asleep, pushback stun) shows a **status badge** on its circle and its sprite dims; the same debuff shows as a pip on its tile (stun as a gold "!"). There are no turn-number badges on the board — the timeline is the turn order.
- **Casts are not on the timeline (amended again 2026-10-10, owner: "instead of putting casting time on the timeline have it show on the caster").** A pending cast traces a **radial countdown around its caster's frame** (`CastRing`, clockwise from top-centre; the lit length is the time left, `1 − u` with `u = 1 − Remaining / (Total + stagger delay)`) in the spell's own colour (`Art.SpellColor`: golden-ratio hue per ability, so every spell differs). Its **target** carries a chip with the spell's icon (or initial) in that colour. A unit casting — or targeted by — several spells cycles through them every 1.2 s. Banners name caster, spell, target and landing time ("Witch charges Ice on Paladin — lands in 3s"; "Paladin: Fireball → Slime").
- **Overlaps (amended 2026-10-10, owner: "all enemies on the timeline share the same center alignment … let each one have its own beat of being on top"):** circles never leave the centre line. Overlapping circles form a cluster and take turns on top, one per 1.2 s beat (`LayoutIcons`), so a fast enemy passing a slow one reads as the two trading places.
- **Spell VFX (owner direction):** every spell plays one readable arc (`SpellVfx`): the caster *draws energy* (motes spiral in, its frame glows in the spell colour), a *cast burst* ring, then a mote per target *curves to the space above it*, from where the element impact plays — lightning strikes down from that point, flames burst, frost rings, heals sparkle.
- Icons that would overlap step into extra lanes (`BattleScreen.StaggerIcons`).

**Shared clock — casts fire "off-turn" by design.** Both lanes advance on the same ticks; whatever arrives first resolves. Lifecycle: up to **4** hero casts may load at once (`MaxHeroCastsInFlight`; enemy charges don't count) — a fifth cast-time spell is refused before any orbs are spent; orbs are spent at cast start; the icon spawns with `ticks = SecondsToTicks(Formulas.CastTime(base, caster))`; on arrival it resolves (`ResolveCast`) and is removed. A spell with `CastTime` 0 resolves immediately.

**Amended 2026-10-09:** every hero spell in `abilities.json` has base `CastTime` 1.5 s; after WIS+INT scaling (12–15 ticks for the starting heroes) it normally resolves **inside the same action's 20-tick advance**, unless an enemy turn earlier in that advance staggers it into later actions. *(Tests: `Cast_time_spell_rides_timeline_then_resolves`, `Fifth_hero_cast_is_refused_by_cast_cap`.)*

### 2.7 Worked example: pushback math

A goblin (AGI 8) sits at IP 920 (u 0.92). A Knight with STR 18 lands a pincer hit.

```
frac      = (920 − 650) / 350           = 0.771
strMult   = max(0.5, 1 + (18 − 10)/40)  = 1.20
push      = min(0.40, 0.18 × (0.5 + 0.771) × 1.20) = 0.275 u
new IP    = 920 − round(274.6)          = 645   (u 0.645 — just outside the Zone)
stun      = max(1, round(8 / (1 + 8/50))) = round(6.9) = 7 ticks (0.7 s)
```

Net: one deep-Zone hit buys 275 IP (~1.2 actions at Speed 10, since it now re-crosses the Zone) plus 7 frozen ticks. A hit at the Zone edge (IP 650) buys 108 IP; a hit outside buys nothing.

#### 2.7.1 Hasten / Quicken — the inverse

`BattleRunner.Hasten(target, u)` (Quicken `HastenU` 0.30; the boss `enrage` skill 0.35):
- On a unit with a **turn icon**: clears its stun and adds `round(u × 1000)` IP, clamped to 1000 (so it may fire on the next tick and can overtake icons ahead of it).
- **Amended 2026-10-09:** on a unit with **no turn icon but a cast in flight** (i.e. a casting hero): subtracts `round(u × 1000 / 16)` ticks from the cast (Quicken rushes an ally's spell by ~19 ticks). *(Test: `Quicken_hastens_an_enemy_icon`.)*

### 2.8 Spawn rules

| Trigger | Icon |
|---|---|
| Wave start (incl. battle start) | each enemy's turn icon at a random **IP 0–250** (`SpawnStaggerIp`) — **Amended 2026-10-09** (replaces u=0 + queue delay) |
| Snake segments | no icon (the head owns the chain's turn) |
| Hero spell with cast time | cast icon below the line |
| Enemy charge | cast icon below the line (2.5 s = 25 ticks, not stat-scaled) |
| Pushback / Hasten | existing icon's IP changes |

### 2.9 Drag timer (optional)

**Amended 2026-10-09 (owner decision):** Settings → "Drag timer (4 s)" (`SettingsSave.DragTimer`, off by default) restores Terra Battle's hand pressure: once a drag starts, `DragTimerSeconds` = 4 s counts down on screen (real time, unscaled by battle speed) and the drag auto-drops at 0. The seconds left at the drop travel in `MoveCommand.TimeLeftSeconds` and feed time-banked orbs ([§3.1.8](#318-time-banked-orbs)). The timer never affects the game clock (an action is always 20 ticks).

### 2.10 Countdown badge

**Amended 2026-10-09 (new, Terra Battle–style):** every enemy shows a number on its tile — `BattleEngine.ActionsUntilTurn` = `(TicksUntilTurn − 1) / 20`: the actions you get before it acts (**0** = it acts during your next action, drawn red). `TicksUntilTurn` simulates its icon with stun and the Zone crawl but no future pushback/hasten; Frozen enemies show no number. The actor panel says "Acts in N moves", "Acts after your next move!", "Frozen in time" or "Charging X!".

---

## 3. Resources

### 3.1 Mana (the orb economy)

**Amended 2026-10-10 (owner decision): mana is generic.** Every orb is identical and any spell can be paid from any orbs. `ManaBank` is a capped count (capacity **12**) shared by the party ([GG-LAW-6](#GG-LAW-6)). The HUD draws 12 orb slots; filled slots glow mana-blue, empty slots are dim.

#### 3.1.1 Costs

A spell's cost is a whole number of orbs (`AbilityDef.Cost`, an int in `abilities.json`); the UI renders it as that many orb pips (`Ui.CostPips`). Content costs are 1–3 (the former letter counts: Fireball 2, Bolt 3, Heal 1, …). Skills and items cost 0.

#### 3.1.2 Harvesting

**Amended 2026-10-11 (owner: "mana needs to be a more rare resource … the number of participants in the attack increases the multiplier needed to earn a mana bubble"):** pincers, crits, interrupts and the amplifier no longer mint whole orbs — they fill the **mana charge** meter ([§3.1.9](#319-mana-charge)). Orbs still mint whole through `BattleRunner.MintOrb` (event `OrbMintedEvent`, reason string) from:
- **Mana charge** rolling over 100 (reason = what filled it: `pincer`, `critical`, `interrupt`, `amplifier`).
- **Steal / Mug** (`steal`): per target, chance `clamp01((LCK + 0.5 × AGI) / 50)` → 1 orb.
- **Battle-start gear** (`battle start`): `ItemDef.BattleStartManaOrbs` summed over the party's equipped items before wave 1 (Mage Robes 2, Wizard Robe 3).
- **Time bank** (`time bank`): see [§3.1.8](#318-time-banked-orbs).
- **Mana items** (`item`): Mana Potion 2, Ether 3, Hi-Ether 6.

The view flies each orb from its source to its slot in a 0.22 s arc (overflowing orbs fade in flight).

#### 3.1.3 Spending

`ManaBank.Spend(cost)` removes `cost` orbs or fails without spending. Orbs are spent at cast start; an interrupted cast refunds nothing. *(Test: `Any_orbs_pay_for_any_spell`.)*

#### 3.1.4 Bank-full rules (overflow)

| State | Mint |
|---|---|
| `Count + n ≤ cap` | all land |
| `Count == cap` | orb flies and fades; bank unchanged (`OrbMintedEvent.Landed = false`) |
| `Count + n > cap` | first `cap − Count` land, rest fade |

`ItemDef.BankCapacityBonus` gear raises the cap (summed over the party, max +4).

#### 3.1.5–3.1.6 (struck 2026-10-10)

~~Spend ordering by color~~ and ~~the Colorless wildcard~~ no longer exist — there are no colors to order or substitute.

#### 3.1.7 At-a-glance mint cadence

| Event | Mana |
|---|---|
| 2-hero pincer that takes half a foe's HP | 60 charge (×1) |
| The same damage with 2 supporters joining | 30 charge (÷2) |
| Third link of a chain, same damage | 90 charge (×1.5) |
| Critical hit (physical or spell) | +12 charge |
| Steal/Mug, 3 adjacent enemies, 2 rolls succeed | 2 orbs |
| Cancel an enemy charge | +25 charge |
| Battle start, one Mage Robes equipped | 2 |
| Drop with ≥ 3.75 s left on the drag timer, then an enemy turn | 3 (cap) |

#### 3.1.8 Time-banked orbs

**Amended 2026-10-09 (owner decision):** the bank is fed by the **optional drag timer**, not by real-time seconds before an enemy trigger.
- A committed move stores `PendingTimeBankSeconds = clamp(TimeLeftSeconds, 0, 4)` (0 when the timer is off).
- At the start of the **first enemy turn** during that action's clock advance (`MintTimeBank` in `RunEnemyTurn`), it converts to **1 orb per `SecondsPerTimeBankOrb` = 1.25 s**, max `MaxTimeBankOrbs` = **3**, with a "Banked N mana" announcement.
- It is cleared at control return and by Shield. Consequence (current truth, tuning watch): a quick drop that is **not** followed by an enemy turn within the same action banks nothing.

#### 3.1.9 Mana charge

Added 2026-10-11 (owner direction). `BattleState.ManaCharge` (0–99) is the forming bubble; the HUD draws it filling the next empty orb from the centre. `AddCharge` emits `ManaChargedEvent` and every full `ManaChargePerOrb` = **100** becomes an orb.

- **Pincer:** `charge = PincerChargePerHp (120) × share × (1 + 0.25 × chainIndex) ÷ (1 + 0.5 × (participants − 2))`, minimum 2 when it lands. `share` = Σ over the trapped foes of `min(1, HP removed by this pincer ÷ MaxHP)`; `participants` = the two attackers + every supporter. So a lean two-hero pincer earns at full rate, a four-hero dog-pile at half, and chaining is the way to farm.
- **Critical** +12, **interrupt** +25, **amplifier** +30. Kills alone earn nothing.
- The billboard shows the arithmetic ("Mana +34 · 3 heroes ÷1.5 · chain ×1.25").

*(Tests: `More_heroes_in_a_pincer_earn_less_mana_per_hp_taken`, `Charge_rolls_over_into_orbs_and_keeps_the_remainder`, `A_killing_pincer_earns_mana_by_hp_taken_not_per_hero`, `Supporters_join_but_dilute_the_mana_charge`.)*

### 3.2 HP and stats

`Unit.Effective` = `LevelStats` + `GearStats`, with **Speed = level SPD + gear SPD + ½ gear AGI** (**Amended 2026-10-09**); STR, VIT and Speed are floored at 1.

| Stat | Drives | Read in |
|---|---|---|
| HP / MaxHP | life | `Formulas.MaxHp = floor(12 + 5·VIT + level)` |
| STR | physical offense; pushback; stagger push | `Formulas.Physical`, `Pushback`, `StaggerCasts` |
| VIT | HP + physical defense | `MaxHp`, `Physical` |
| AGI | pushback stun recovery; Steal; ½ into Speed from gear | `Pushback`, Steal roll |
| SPD | timeline rate; hit chance | `RateFor`, `Formulas.HitPercent` |
| STA | reserved | — |
| INT | enemy magic offense; cast time; threat weighting | `Formulas.Magic`, `CastTime`, `PickTarget` |
| WIS | magic defense; cast time; cast poise | `Magic`, `CastTime`, `StaggerCasts` |
| LCK | crit; hit; Steal; Clutch; damage variance tilt | `CritPercent`, `HitPercent`, Steal, Clutch |

Level stats: `Formulas.StatsAtLevel` = base + growth per level from 2 + milestone bonuses at their level, each stat floored. Spell damage with a non-zero base always does ≥ 1 unless the target is immune (resistance 0).

#### 3.2.1 Stat-to-effect cheat sheet

Tank: VIT, STR (not INT) · glass cannon: INT, WIS, LCK (not VIT) · skirmisher: SPD, AGI, LCK · heavy hitter: STR, VIT (not SPD/AGI) · rogue: LCK, AGI · mystic support: WIS, INT (not STR).

### 3.3 Elemental resistance

`Unit.ResistanceMultiplier(type)` = `ClassDef.Resistances[type]` × each equipped item's `Resistances[type]` (multiplicative; missing = 1.0; 0 = immune). `DamageType`: `Physical, Fire, Ice, Lightning, Poison, Holy, Dark, Arcane`. Spell damage composes ([§13.1.2](#1312-spell-damage-dispatcher)):

```
final = round(base × resistance × (Lightning on Wet ? 1.5 : 1) × incoming-damage multiplier)
```

Current content authors no class resistances; the only resistance gear is the Sunfire Amulet (Fire ×0.7).

---

## 4. The AbilityBar

The battle HUD shows **5 slot buttons** for the **selected hero** (`BattleRunner.MaxSlots` = 5); campaign progress makes **2 to 5** usable ([§4.7](#47-progressive-slot-unlock)). Each slot is an `AbilitySlot`: an ability id, or an item stack.

### 4.1 The three kinds

| Kind | Cost | Cast time | Frame |
|---|---|---|---|
| **Skill** | free; costs the action; per-skill cooldown | instant | green |
| **Spell** | mana orbs | cast icon on the timeline (all content spells: 1.5 s base) | blue + cost pips |
| **Item** | one charge of the slot's stack | instant | warm leather; "name n/max" |

#### 4.1.1 Skill cooldowns

A skill locks for `AbilityDef.CooldownTurns` after use: **Steal 3, Mug 2, Teleport 3**. Countdowns live per hero in `Unit.Cooldowns`. **Amended 2026-10-09:** they tick **once per committed action** at control return (the old "turn-cycle" = hero window is now one action). A cooling slot shows "CD n" and refuses ("Recharging"). *(Test: `Teleport_relocates_and_fires_pincer_then_goes_on_cooldown`.)*

### 4.2 Per-hero loadouts

The bar follows the selected hero. Its slots come from the hero's saved bar, `HeroSave.Bar` (5 entries: ability id, `"item:<id>"`, or null), edited on the Abilities screen ([§25.6](#256-abilities)). A new hero's bar is its class `DefaultLoadout` (`Progression.NewHero`), whose abilities become **Known** ([§25.6](#256-abilities)). **Amended 2026-10-09:** there is no separate `HeroLoadouts` preset layer or `CombatLoadouts` cache — the saved bar is the bar.

Seeded loadouts (`classes.json`):

| Class | DefaultLoadout |
|---|---|
| Cleric | heal, heal, frost, item:healing_potion_basic |
| Paladin | heal, fireball, item:healing_potion_basic |
| Barbarian | fireball, bolt, item:healing_potion_basic |
| Alchemist | frost, item:healing_potion_basic, steal, item:cons_sleep_dart (duplicate potion entry dropped by `NewHero`) |
| Assassin | steal, mug, bolt, item:healing_potion_basic |
| GreenNinja | teleport, steal, fireball, item:healing_potion_basic |
| RedNinja | teleport, mug, bolt, item:healing_potion_basic |
| all 55 other hero classes | heal, fireball, frost, bolt, item:healing_potion_basic |

On a fresh save (2 slots) the starting trio's potion sits in locked slot 3 until the first clear.

### 4.3 Item stacks and effects

- `"item:<id>"` draws its charges from the **inventory**: `min(owned, MaxStack)`; a second slot of the same item takes from what remains (stacks split, never duplicate). Each use spends a charge and is recorded in `BattleResults.ItemsConsumed`, deducted from the save at commit — **Amended 2026-10-09:** on victory, defeat, retreat and escape alike.
- `"item:<id>:<n>"` is a preset stack of n charges not drawn from inventory (available for test/setup).
- A consumable's battle effect (`BattleRunner.ItemAbility`) is its `OnUseAbility` (Sleep Dart → `sleep`), else synthesized from item knobs. **Amended 2026-10-09:**

| Knob | Effect | Items |
|---|---|---|
| `BaseHealing` | single-ally heal (PickActor, AllyOnly) | Healing Potion 50, Greater Healing Potion 90, Hi-Potion 150, X-Potion 500 |
| `BaseDamage` (+`DamageType`, `BonusVsTag` ×`BonusMultiplier`) | single-enemy hit | Holy Water 40 Holy (×2 vs Undead), Flame Oil 45 Fire (×1.5 vs Beast) |
| `GrantsOrbs` | orbs (Auto) | Mana Potion 2, Ether 3, Hi-Ether 6 |
| `CuresBuff` / `CuresAll` | remove one debuff id / all debuffs from an ally | Antidote (poisoned), Greater Antidote (all), Eye Drops (blinded), Remedy (all) |
| `Revives` | first fallen hero returns on the free tile nearest the user with `BaseHealing` HP; refused if nobody has fallen | Revival Charm (50) |
| `Escape` | battle ends as **Escaped** ([§22.2](#222-failure-path-detail)) | Smoke Bomb |
| `OutOfBattleOnly` | no battle effect; Camp only | Tent |

### 4.4 Click flow per kind

All three go through `UseSlotCommand` → `BattleRunner.DoUseSlot`, which re-checks `GetSlotStatus` and the target ([§4.5](#45-slot-visual-states)); a refusal costs nothing.
- **Item** — spend a charge → resolve its ability immediately.
- **Skill** — start its cooldown → resolve immediately.
- **Spell** — `Bank.Spend(cost)` → if `CastTime > 0` queue a cast icon (target unit remembered for PickActor, tile for PickTile), else resolve immediately. Every accepted use is an action (20 ticks).

#### 4.4.1 Select → confirm → act

Added 2026-10-11 (owner: "confirmation screens when deciding to cast a spell"). With Settings → **Confirm before acting** on (default):
- A pick ability: tap the slot → legal anchors glow gold → tap a tile → the pick **locks**: its area and every unit it would touch glow (red harmful / green helpful / blue teleport), the banner asks "Poison on Wolf? Confirm below", and the actor panel becomes the **confirm card** (icon, name, cost pips, timing, "On Wolf", **Cast/Use/Go** and **Cancel**). Tapping another legal tile re-aims; tapping the same tile again confirms.
- An Auto ability (Taunt, War Cry, Mass Heal …) goes straight to the confirm card.
- Nothing is spent until Confirm (`BattleScreen.ConfirmPending`, chime `ui.confirm`); Cancel / slot re-tap / Back play `ui.cancel`.
- Autoplay and the setting turned off commit on the pick, as before.

### 4.5 Slot visual states

`GetSlotStatus` (in precedence order) and how `BattleScreen.SyncBar` draws it:

| Status | When | Visual |
|---|---|---|
| CannotAct | hero dead, or immobile (Frozen/Sleep) | 45% opacity; refused "Can't act" |
| Locked | slot ≥ unlocked count | "Locked", 35% opacity |
| Empty | no entry / unknown ability id | "—", 50% (an item with no battle effect, e.g. a slotted Tent, reports Ready but is refused "Empty" on use) |
| OutOfCharges | item, 0 charges | name + "0/n", 45% |
| Cooldown | skill counting down | "Name\nCD n", 45% |
| Silenced | spell while caster Silenced | red border + "Silenced" |
| Unaffordable | spell the bank can't pay | 45% |
| CastCapReached | cast-time spell with 4 hero casts in flight | 45%; "Too many spells!" |
| Ready | otherwise | full opacity |

The slot being targeted gets a gold border and 1.06× scale, and **while targeting, the actor panel shows the ability's card** (icon, name, cost pips, "Cast time 1.5s — rides the timeline" / skill recharge, description, "Tap a highlighted tile. Tap the slot again to cancel."). **Amended 2026-10-09.** ⬜ Not yet built: hover/long-press tooltips on idle slots and the radial cooldown sweep.

### 4.6 Cancel paths

Targeting is cancelled by tapping the same slot again, the hardware/Escape **Back** action (`BattleScreen.OnBack`), or committing any other command. Cancelling spends nothing.

### 4.7 Progressive slot unlock

**Amended 2026-10-11 (owner: "give each character 2 stacks of full life potions and unlock the slots to put them in … the 2 healing potion is two stacks of 5x"):** all five slots are open from the start (`Progression.StartingSlots`). Every hero's default bar is two abilities, **two X-Potion stacks (5 each, full heal, usable on any ally)**, then more abilities; a new game stocks a full stack per potion slot (5 heroes × 2 × 5). The threshold rule below is kept for reference but no longer gates anything.

**Amended 2026-10-11 (later the same day, owner direction) — one X-Potion stack, one new skill per class:** the two X-Potion slots collapsed into **one** (still a full stack of 5; a new game now stocks 5 heroes × 1 × 5), freeing the 5th bar slot for a **brand-new, genuinely unique skill per hero class** — all 62 classes, even ones that otherwise share an identical kit (e.g. the 19 "war_cry bruiser" strikers), each got their own ability authored to that class's own name/flavor rather than just its Role. All 62 are purely data-driven (`abilities.json`, `Kind: Skill` for non-casters / `Kind: Spell` for the 6 Mage-role classes), reusing the existing generic resolver and the 13 existing buffs — no new engine code. Every one has a real pixel icon (`tools/icons.json` + `tools/make_icons.py`, reusing already-cached silhouettes), not the orb fallback. *(Tests: `New_game_has_starting_trio_gold_and_loadouts`, `A_recorded_battle_round_trips_as_json_with_every_turn`; every new ability is also auto-exercised by `CatalogTests.Every_hero_ability_resolves_against_a_live_board`.)*

`Progression.UnlockedSlots(save)`: **2** on a fresh save, +1 when `HighestClearedStage` reaches **0, 2 and 5** (`UnlockThresholds`), max 5. The battle reads it via `BattleSetup.UnlockedSlots`; the Abilities screen renders locked cells and `Abilities.Assign` refuses them. The post-battle screen announces "Ability slot unlocked!". *(Tests: `New_game_has_starting_trio_gold_and_loadouts`, `Full_loop_battle_commit_then_every_vendor`.)*

---

## 5. Targeting

Three orthogonal enums (`ContentModels.cs`):

```
TargetShape  : Self / SingleActor / SingleTile / Square / Diamond / Cross / Plus / Row / Column / AllEnemies / AllAllies
TargetMode   : Auto / PickActor / PickTile
TargetFilter : Any / EnemyOnly / AllyOnly / EmptyOnly
```

plus `int Radius`.

### 5.1 Resolver

`TargetResolver.Tiles(anchor, shape, radius)` → tiles (clipped to the board); `Collect(state, ability, caster, anchor)` → distinct living units on those tiles that pass the filter (or the whole team for All*, or the caster for Self).

```
Square(r=1)     Diamond(r=2)      Cross(r=1)      Plus             Row          Column
· · · · ·       · · █ · ·         · · · · ·       · · █ · ·        · · · · ·    · · █ · ·
· █ █ █ ·       · █ █ █ ·         · · █ · ·       · · █ · ·        █ █ ★ █ █    · · ★ · ·
· █ ★ █ ·       █ █ ★ █ █         · █ ★ █ ·       █ █ ★ █ █        · · · · ·    · · █ · ·
· █ █ █ ·       · █ █ █ ·         · · █ · ·       · · █ · ·
· · · · ·       · · █ · ·         · · · · ·       · · █ · ·
```

| Shape | Tiles (anchor `(ax,ay)`) |
|---|---|
| Self, SingleActor, SingleTile | the anchor |
| Square(r) | Chebyshev ≤ r |
| Diamond(r) | Manhattan ≤ r |
| Cross(r) | anchor + r tiles along each cardinal arm |
| Plus | the anchor's whole row ∪ whole column |
| Row / Column | the anchor's whole row / column |
| AllEnemies / AllAllies | no tiles; the caster's opposing / own team |

Filters: EnemyOnly = other team; AllyOnly = caster's team **including the caster**; Any = everyone; EmptyOnly = no units (used by Teleport). AoE shapes never hit units their filter excludes.

### 5.2 Picker flow

`BattleScreen.OnSlot` / `BattleScreen.Input`:
- **Auto** (Self, All*, Steal/Mug's Cross around the caster) → commits immediately with the caster's tile as anchor; refused "No target" if nothing would be hit.
- **PickActor / PickTile** → `BattleEngine.ValidTargets` marks every legal anchor gold; pressing a tile previews its area (red if harmful, green if helpful, blue for Teleport); releasing on a legal tile commits `UseSlotCommand`. Legal anchors (`TargetResolver.IsValidPick`): PickActor = an occupied tile whose unit passes the filter; PickTile = any tile whose shape catches ≥ 1 valid unit; EmptyOnly = an empty, trap-free tile.
- No legal anchor → banner "No valid target", nothing spent.

### 5.3 Special targeting flows

- **Teleport** (`IsTeleport`): EmptyOnly PickTile → the caster moves there (`TeleportedEvent`), triggers any trap, then a board-wide hero pincer scan resolves. Cooldown 3.
- **Revive / Escape / orb items** resolve on the user without a pick ([§4.3](#43-item-stacks-and-effects)).

### 5.4 Targeting session lifecycle

```
tap slot ──▶ GetSlotStatus ≠ Ready ─▶ banner reason (no state change)
   │ Ready
   ├─ Auto ──────────────────────────────▶ Commit(UseSlotCommand)
   └─ Pick* ─▶ TargetingSlot = i; mark ValidTargets
                 │ press/drag over tiles ─▶ PreviewTarget(tile)
                 │ release on legal tile ─▶ Commit(UseSlotCommand) ─▶ CancelTargeting
                 │ release elsewhere ─────▶ error sfx, keep targeting
                 └ tap slot again / Back ─▶ CancelTargeting (nothing spent)
```

Invariant: while the event player is busy (`BattleScreen.Busy`) no command can be committed (`CanCommand`), so two uses can never overlap. A 20 s playback watchdog resyncs the view if an animation ever stalls.

---

## 6. The Spell Dispatcher

`BattleRunner.ResolveAbility(caster, ability, anchor, pickedUnit)` — the single resolution path for spells, skills, items and boss phase abilities. **Amended 2026-10-09:** it is pure simulation; VFX are presentation and run later from events. Order:

1. **Teleport** → relocate + trap + pincer scan, done.
2. **Escape** → `BattlePhase.Escaped`, done. **Revive** → revive first fallen hero, done. **GrantsOrbs** → mint orbs, done.
3. Collect targets; emit `AbilityResolvedEvent` (anchor, tiles, targets). No targets → "Missed!".
4. Per living target, in order:
   1. **Scan** → `ScannedEvent` (stats, HP), mark the class Scanned + Seen.
   2. **Cleanse** (`RemovesDebuffs`: all debuffs) / **cure** (`RemovesBuff`: one id).
   3. **Fire × Frozen / Wet**: a damaging Fire hit force-expires Frozen (its chain applies Wet), then strips Wet ("Steam!").
   4. **Steal** (`StealsMana`) roll → random orb or "Missed!"; **Mug** adds a fixed physical hit (`MugDamage` 10 × physical resistance × incoming multiplier).
   5. **Damage** (`BaseDamage > 0`) → `Formulas.SpellDamage` (Lightning on Wet ×1.5, note "Shock!"), × `BonusMultiplier` vs `BonusVsTag`; then **Lightning blind roll** 30% → Blinded.
   6. **Knockback** (`Knockback`, [§6.6](#66-knockback-added-2026-10-10-owner-direction)), then **Pull** (`Pull`) and **Swap** (`SwapsPlaces`) ([§6.7](#67-movement-timeline-and-drain-gg-t183)).
   7. **Heal** (`BaseHeal`). **Amended 2026-10-22 (GG-T183):** a heal only ever lands on the caster's side. On a foe it is a **drain** when the ability's filter is `EnemyOnly` (the caster is mended instead), and nothing when the filter is `Any`; and an ability that both harms and heals never harms a friend (before this, Soul Feast, Collect the Debt, Bloodletting and Blood Harvest healed the foe they hit, and Harm into Hope scorched allies and mended enemies).
   8. **Hasten** (`HastenU`), then **Delay** (`DelayU`).
   9. **Buff** (`ApplyBuff`, chance `ApplyBuffChance`): Sleep on a Warm target lasts ×1.5 (ceil).
5. **Step back** (`StepBack`): the caster backs away from the nearest foe.
6. Battle-end check.

**Amended 2026-10-22 (GG-T183):** a teleport with `HastenU` (Fade Step) also hastens the caster when it lands.

`Damage()` is the single damage choke-point: armor (snake), Sleep-break, HP, threat, death + loot, then (hero source, non-miss) pushback and cast stagger ([§13](#13-combat-resolution)).

### 6.1 Projectile motions

`AbilityDef.Motion`: `None / Straight / Bezier / Homing / Spiral / Twist / Strike`. **Amended 2026-10-09, corrected 2026-10-13 (was stale):** `BattleScreen.Playback.ProjectilePoint` gives every motion its own curve — `Bezier` arcs, `Homing` curls in late from the side, `Spiral` corkscrews in on a shrinking radius, `Twist` oscillates side to side, `Strike` drops from directly overhead — `Straight`/`None` is the only flat line, and Reduce Motion flattens everything to it.

### 6.2 VFX pipeline

**Amended 2026-10-11 — staging (owner: "there is a rhythm and tempo to gameplay that does require pauses and transitions … if I clicked poison and an icon just appeared on an enemy it would feel meaningless"):** every action plays as readable beats (`BattleScreen.Fx.cs`, `BattleScreen.CutIns.cs`):
1. **Wind-up:** motes spiral into the caster in the spell's element colour (`CastColor`: fire orange, poison green, heals green, status spells their status colour).
2. **Charging:** a channel halo pulses on the caster and motes orbit it; a **charging bar** fills on its tile (plus the existing ring) while the timeline glides. Hero casts queue and land inside one action, so playback tracks them as *cast ghosts* from `CastQueuedEvent` to the resolve.
3. **Cast splash:** the caster's action cut-in ([§9](#9-the-hud)) — a sequential beat, never covering the flight.
4. **Projectile:** a glowing orb with a white-hot core and a comet tail of embers on a curve.
5. **Impact:** a splash ring at every target (status-only spells included); area spells wash over their tiles as a soft bloom wave from the anchor.
6. **Status lands, then its icon:** each status has its own take-hold effect (bubbling poison, converging frost, falling droplets, rising Z's, slow spiral, blinding smoke, red taunt shockwave with threat lines, rising embers …); only after it does the pip pop in and the status-coloured text float.
7. **Linger:** while a status lasts the unit keeps showing it (poison bubbles, embers, frost glitter and blue tint, drips, snores, buff sparks); damage-over-time ticks play the element (poison bubbles), not generic sparks.
- **Items:** the user produces the bottle (rises out of the frame); a potion for self is uncorked, tipped and drunk (`glug`); for someone else, or a damage item, it spins on an arc and shatters (`glass`) into a splash.
- **Skills:** melee skills (Cleave, Shield Bash) lunge with a slash arc; self/party skills (Taunt, War Cry) rear up with a shockwave.
- The billboard steps to the foot of the board when the action is in the top two rows.
- Reduce Motion keeps the beats but drops particles, shakes and camera moves.

Earlier note (2026-10-09, superseded): **Amended 2026-10-09:** there are no particle prefabs, no `VisualEffectLibrary` and no Addressables. Feedback today is code-drawn in UI Toolkit: tile marks colored by damage type, the projectile dot, floating text, shakes, pops, glows and a full-screen flash for Clutch. The `CastVfx / ProjectileVfx / ImpactVfx / LingerVfx` keys remain in `abilities.json` for a future pass.

**Amended 2026-10-13:** damage and buff-tagged spells still read their look from `DamageType`/the applied buff id (`ElementBurst`, `StatusBurst`) — already rich and per-type (Fire/Ice/Lightning/Poison/Holy/Dark each have a bespoke burst, and poisoned/burning/frozen/sleep/slowed/silenced/blinded/taunting/empowered/protection/regen each have a bespoke take-hold animation). What used to fall through to one generic purple ring — Scan, pure cleanse/dispel (`RemovesDebuffs`/`RemovesBuff`), and hasten-only spells (no damage, no heal, no buff) — now get their own bespoke gold bursts (`ScanBurst`/`CleanseBurst`/`HastenBurst`, `BattleScreen.Playback.cs`) and read gold during the cast wind-up too (`CastColor`). The named `CastVfx/ProjectileVfx/ImpactVfx/LingerVfx` strings on individual abilities (e.g. distinguishing Fireball from Meteor) are still not read by any code — still ⬜ for a future pass, still code-drawn UI Toolkit if it happens, never particle prefabs (law #3).

**Amended 2026-10-19 — death-portrait dissolve restored (owner: "the only way to see the complete artwork of these enemies... a quintessential reward for killing an enemy", GG-T96):** on `DiedEvent` the board token vanishes at once and a fresh single image of the unit's **full, uncropped portrait** (`Art.Portrait`, never the token's `Thumbs/` crop, and no frame, HP bar, pips, glow or any other part of the `UnitView`) appears on the board's fx layer (below floating numbers and the coin burst, above every token, mark and line), centred on the fallen unit's footprint (2×2 bosses on their whole footprint) and nudged back inside the board at its edges so the whole art reads. It is sized to fit a box of (2 + footprint) tiles, starts at 196/255 opacity and, frame-rate independently and scaled by Battle speed, jitters up to ±1/12 tile around its start point, shrinks as ×0.99 per 60 fps frame and fades 0.6/s until gone (≈1.3 s), exactly GridGame2026's `PortraitManager.Dissolve` + `Portrait3DInstance.DissolveRoutine`. It is non-blocking: playback waits only a 0.12 s beat, so the loot burst and the next kill in a chain play while it dissolves, and several deaths each get their own. Every dying unit gets one (corpses show the fallen class's art; units with no portrait skip it). The death sfx and the "★ X is defeated" feed line are unchanged. Reduce Motion drops the jitter; the shrink and fade still play. (`BattleScreen.Playback.cs` `DeathAnim`/`DeathDissolve`.)

### 6.3 Edge cases the dispatcher handles

| Case | Handling |
|---|---|
| Target dies before its turn in the target list | skipped (`!t.Alive`) |
| PickActor cast whose target moved | anchor follows the unit's current tile at resolution |
| PickActor cast whose target died | resolves at the original tile (usually "Missed!") |
| Caster dies before its cast resolves | cast removed with its owner (`Kill` clears the owner's entries) |
| AoE overlapping the same unit twice | `Collect` de-duplicates units (a 2×2 boss is hit once) |
| Fire on Wet | Wet stripped before damage, so no Lightning ×1.5 from that Wet later |
| Lightning on Wet | ×1.5, Wet stays, then the 30% blind roll |
| Steal on any target | success always mints (stolen "from the world") |
| Battle ends mid-list | the loop stops (`S.IsOver`) |

---

### 6.6 Knockback (added 2026-10-10, owner direction)

Owner: "certain abilities would have pushbacks like a shield bash that pushes an enemy away and it hurts more if it runs into another enemy, and hurts a lot if it runs into a solid tile."

`AbilityDef.Knockback` = tiles the target is shoved straight away from the caster (dominant axis); `AdjacentOnly` limits the pick to a cardinally adjacent foe. Per tile: into stone or the board edge → **Slam** (`18 + blow` to the target, stop); into another unit → **Crash** (`8 + ½ blow` to the target and, if it's a foe, to the unit hit; stop); into a movable tile → it is pushed ahead if there's room, else a crash; otherwise the target slides (`DisplacedEvent`). 2×2 bosses and snake segments don't budge. **Shield Bash** (Skill, cooldown 2, 12 physical, knockback 2, adjacent only) is learnable by Paladin, Knight and Defender and sits in the Paladin's third bar slot. *(Tests: `Shield_bash_shoves_the_target_two_tiles_away`, `Bashing_into_a_unit_crashes_both_and_into_stone_slams_hard`, `Shield_bash_needs_an_adjacent_target`.)*

### 6.7 Movement, timeline and drain (GG-T183)

Added 2026-10-22 so abilities can differ in more than damage and status ([§7.1](#71-reskin-audit-gg-t183)). Four `AbilityDef` fields, each resolved per target by the dispatcher above:

| Field | Mechanic | Rule | Event |
|---|---|---|---|
| `Pull` (tiles) | movement | The target is dragged toward the caster one tile at a time along the longer axis. It stops beside the caster, or before any unit, block, movable or board edge. A hero dragged onto a trap springs it. | `DisplacedEvent` per tile |
| `SwapsPlaces` | movement | The caster and the target trade tiles. Both are one-tile units, so the board stays one actor per tile ([§1.1.3](#113-one-actor-per-tile--the-occupancy-guarantee-added-2026-10-19-owner-direction-gg-t97)). A hero moved onto a trap springs it. | two `DisplacedEvent`s |
| `StepBack` (tiles) | movement | After every target resolves, the caster backs straight away from the nearest foe onto free, trap-free tiles. | `DisplacedEvent` per tile |
| `DelayU` (bar fraction) | timeline | The inverse of Hasten ([§2.7.1](#271-hasten--quicken--the-inverse)): the target's turn icon slides back by `DelayU × BarLength` (to no less than 0). A target that is holding a cast has the cast slip by the same amount in ticks instead. That counts as a stagger, but it never cancels the cast. | `PushedBackEvent` / `CastStaggeredEvent` |

2×2 bosses, snake segments and remains don't budge ("Won't budge"). None of the moves is a hero step, so none of them forms a pincer: movement never deals damage ([GG-LAW-1](#GG-LAW-1)). *(Tests: `AbilityAuditTests`: `Pull_reels_a_foe_in_toward_the_caster`, `Pull_stops_beside_the_caster`, `Swap_trades_tiles_with_the_target`, `Step_back_puts_distance_between_the_caster_and_the_nearest_foe`, `Delay_slides_the_foes_turn_icon_back`, `A_heal_aimed_at_a_foe_drains_into_the_caster`, `Harm_and_heal_in_one_burst_hurts_only_foes_and_mends_only_friends`.)*

## 7. The Spell Catalog

All entries in `abilities.json`. Every spell has base cast time **1.5 s** (scaled by WIS+INT, [§13.1.4](#1314-cast-time)).

| Spell | Cost | Shape / Mode / Filter | Motion | Effect | Role |
|---|---|---|---|---|---|
| **Heal** | 1 | SingleActor / PickActor / AllyOnly | None | +25 HP | spot heal |
| **Mass Heal** | 1 | AllAllies / Auto / AllyOnly | None | +12 HP each | emergency top-up |
| **Antidote** | 1 | SingleActor / PickActor / AllyOnly | None | removes all debuffs | cleanse |
| **Scan** | 1 | SingleActor / PickActor / EnemyOnly | Straight | reveals HP + stats; bestiary Scanned | recon |
| **Fireball** | 2 | SingleActor / PickActor / EnemyOnly | Twist | 18 Fire + Burning | burst + DOT |
| **Frost** | 2 | Square(1) / PickTile / EnemyOnly | Bezier | 10 Ice + Frozen | AoE control |
| **Bolt** | 3 | Row / PickActor / EnemyOnly | Strike | 14 Lightning (×1.5 Wet), 30% Blind | row clear |
| **Poison** | 2 | Cross(1) / PickTile / EnemyOnly | Bezier | 6 Poison + Poisoned | long tail |
| **Sleep** | 1 | SingleActor / PickActor / EnemyOnly | Homing | Asleep (breaks on damage/move) | hard CC |
| **Slow** | 2 | Row / PickActor / EnemyOnly | Bezier | Slowed (icon ×0.5) | row tempo |
| **Quicken** | 1 | SingleActor / PickActor / Any | Straight | +0.30 u (or rush a cast) | tempo |
| **Silence** | 1 | SingleActor / PickActor / EnemyOnly | Straight | Silenced | see note |
| **Meteor** | 2 | Diamond(2) / PickTile / EnemyOnly | Strike | 22 Fire + Burning | big AoE |
| **ShockWave** | 3 | Column / PickActor / EnemyOnly | Strike | 10 Lightning | column cleave |
| **CrossHit** | 3 | Plus / PickTile / EnemyOnly | Strike | 8 Lightning | row + column |
| **Steal** (Skill, CD 3) | free | Cross(1) / Auto / EnemyOnly | Homing | per adjacent enemy: LCK+AGI roll → orb | resource grab |
| **Mug** (Skill, CD 2) | free | Cross(1) / Auto / EnemyOnly | Straight | Steal roll + 10 Physical — see note | steal + hit |
| **Teleport** (Skill, CD 3) | free | SingleTile / PickTile / EmptyOnly | None | relocate + pincer scan | mobility |

Enemy-only entries: `charge_fireball` (Fire), `charge_ice` (Ice), `charge_thunder` (Lightning), `charge_smite` (Holy) — 2.5 s charges with base 0 (damage comes from the INT-vs-WIS roll, [§13.4](#134-the-interrupt-path)); `enrage` (self Hasten 0.35, Cyclops phase 2).

**Passives** (`Kind: "Passive"`, active when listed in a class's `Abilities`; never on the bar, never learnable) — **Amended 2026-10-09:**
- **Double Attack** (`double_attack`): as a pincer attacker, strikes each trapped opponent a second time at ×0.5.
- **Counter Attack** (`counter_attack`): when hit by a landing physical strike (pincer, supporter or melee) from an adjacent attacker and still alive, strikes back once at ×0.5 ("Counter!"); counters never chain.
- Carried by `bat01`, `bat02`, `scorpion` (enemies). *(Test: `Double_attack_passive_strikes_twice_and_counter_strikes_back`.)*

**Notes (current truth, Amended 2026-10-09):**
- **Mug** = Steal's roll + its row's `BaseDamage` 10 Physical, one hit per target (fixed 2026-10-09; `Mug_steals_and_hits_exactly_once_per_target`).
- **Silence** blocks hero spell slots and stops a silenced enemy from starting a charge (`EnemyPlanner.PlanCast`; `Silenced_caster_does_not_start_a_charge`).
- Spell damage never misses or crits ([§13.1.2](#1312-spell-damage-dispatcher)).

### 7.1 Reskin audit (GG-T183)

Owner direction (2026-10-21, epic W): there should be **at least 100 hero abilities that differ in mechanic, each learnable at the Learn screen**. A reskin is an ability with the same shape and effect as another under a different name.

**How "mechanic" is measured.** `Abilities.MechanicKey` (`Meta/MetaServices.cs`) removes the name, the numbers (damage, heal, cost, cooldown, cast time, hasten/delay amount, buff chance) and the art. What remains is the key:
- **Payment and timing:** a skill on a cooldown, a spell cast on the timeline, or an instant spell.
- **Footprint:** shape (with its radius for Square/Diamond/Cross), auto or pick mode, filter, and whether it is adjacent-only. Mode and filter are ignored for Self and All* shapes, where they change nothing.
- **Effects:** damage, with Fire and Lightning counted apart because they thaw/steam and shock/blind, while every other element only meets a different resistance. Then heal or drain, the status it applies, cleanse/cure, steal, orbs, scan, revive, escape, bonus-vs-tag, movement (teleport, knockback, pull, swap, step back) and timeline (hasten, delay).

**Learnable** = `Abilities.LearnableByAnyHero`: a hero-usable Spell or Skill (never a passive, an enemy charge or `enrage`) that is a spell, a universal skill, or on a hero class's `Abilities` list. That is the union of what `Abilities.Learnable` offers across all hero classes.

**Before (197 rows):** 169 learnable hero abilities, **95 distinct keys**. Five of them did nothing when they resolved (only 92 of the 95 keys had an effect at all), and seven more were wired wrong:
- **No-ops:** `sprint`, `shake_it_off`, `tempoweave_shortcut`, `tempoweave_swap`, `shadowstep_fade_step` (a "teleport" that wasn't one).
- **`disengage`:** a self knockback, which the dispatcher skips.
- **Drains that healed the foe:** `attrition_soulfeast`, `opportunist_collect_the_debt`, `attrition_bloodletting`, `hexweaver_bloodharvest`.
- **`hexmenders_harmintohope`:** burned allies and healed enemies.
- **Unlearnable:** nine ranger skills (`marksman_*`, `trapper_*`, `skirmisher_covering_volley/retreat_shot/rapid_barrage`) were only granted by a skill-tree node, so no Learn screen offered them.

The biggest reskin family was the plain adjacent strike: 21 rows (Cleave, Killing Blow, Iaijutsu, Flurry, Riposte, Fleche, Knockout Punch, Wild Swing, Hit and Run…).

**What GG-T183 changed:**

| Change | Abilities |
|---|---|
| No-ops made real, using the mechanic each one's text already promised | Sprint (self hasten), Shake It Off (self cleanse), Shortcut (instant ally hasten), Swift Swap (swap places with an ally + cleanse), Fade Step (teleport + hasten), Disengage (step back 2) |
| Text the data didn't do | Followthrough now hastens as it says; Cleansing Vigil now cleanses as it says |
| Drain / harm-and-heal fixed in the dispatcher ([§6](#6-the-spell-dispatcher)) | Soul Feast, Collect the Debt, Bloodletting, Blood Harvest, Harm into Hope |
| Reskins given the mechanic their name describes | Fleche (lunge through: swap with the foe), Riposte (+ delay), Hit and Run (+ step back), Wild Swing (all 8 tiles around you), Knockout Punch (50% sleep), Retreat Shot (+ step back), Covering Volley (column + delay) |
| Skill-tree-only ranger skills put on the ranger class lists | Hexsniper: Deadeye Shot, Piercing Volley, Execution Shot, Covering Volley, Retreat Shot, Rapid Barrage. Hexwarden: Binding Shot, Pitfall Volley, Snare Net |
| **12 new abilities** | Spells, which every hero can learn: **Hinder** (delay 0.3), **Stasis** (all foes, delay 0.12), **Haste Field** (all allies, hasten), **Drain Life** (cast drain), **Undertow** (column, Ice + Wet + pull 2), **Drench** (instant 3×3 Wet, setting up Lightning ×1.5), **Flame Lance** (row, Fire + burning). Class skills: **Grappling Hook** (pull 3: Lancer, Raider, Panda Girl, Hexwarden), **Meditate** (+1 orb: Monk, Sage, Martial Artist, White Ninja), **Shove** (adjacent knockback, no blade: Defender, Knight, Tank, Myrmidon), **Disrupt** (silence + delay: Sage, Operative, Drifter, Harbinger), **Pinning Shot** (ranged hit + delay: Hexsniper, Hexwarden) |

**After (209 rows):** 190 learnable hero abilities, **120 distinct mechanics**, and none of them is a no-op. The remaining reskin families are left on purpose, because each class keeps its own flavour of a staple move. The largest are the plain adjacent strike (16), adjacent strike + slowed (8), adjacent strike + knockback (8), adjacent strike + blinded (6), adjacent strike + poisoned (5), ranged strike (5) and self-empower (4). *(Tests: `AbilityAuditTests`: `At_least_100_learnable_hero_abilities_differ_in_mechanic` prints every remaining family, `Every_learnable_hero_ability_does_something`, `Every_counted_ability_is_offered_at_some_heros_Learn_screen`, `Reskins_share_a_key_and_new_mechanics_do_not`; `CatalogTests.Every_hero_ability_resolves_against_a_live_board` runs all 12 new rows through the engine.)*

---

## 8. Buffs and Debuffs

`BuffDef` (`buffs.json`) + `BuffInstance` (`Remaining`, `TickAccumulator`) on `Unit.Buffs`, applied and removed only by `BattleRunner.ApplyBuff / RemoveBuffsWhere / Expire`.

### 8.1 Catalog

| Id | Kind | Duration | Knobs | On expire |
|---|---|---|---|---|
| `protection` | Buff | 1 Turn | incoming damage ×0.85 | — |
| `burning` | Debuff | 5 Ticks | 4 dmg/tick | → `warm` |
| `frozen` | Debuff | 1 Turn | immobile, halts timeline | → `wet` |
| `wet` | Debuff | 6 Ticks | Lightning ×1.5; Fire strips it | — |
| `warm` | Debuff | 3 Ticks | Sleep ×1.5 duration | — |
| `sleep` ("Asleep") | Debuff | 3 Turns | immobile, breaks on damage / move | — |
| `poisoned` | Debuff | 6 Ticks | 3 dmg/tick | — |
| `slowed` | Debuff | 2 Turns | timeline rate ×0.5 | — |
| `silenced` | Debuff | 2 Turns | blocks spell slots | — |
| `blinded` | Debuff | 2 Turns | hit chance ×0.5 | — |

### 8.2 Cross-effect multipliers

`BattleConfig.LightningWhenWetMultiplier` = 1.5, `SleepWhenWarmMultiplier` = 1.5, `LightningBlindChance` = 0.30.

#### 8.2.1 Interaction matrix

| On target ↓ \ hit → | **Fire** | **Ice** | **Lightning** | **Physical / other** |
|---|---|---|---|---|
| (clean) | damage + Burning (Fireball/Meteor) | damage + Frozen (Frost) | damage; 30% Blinded | damage |
| **Burning** | refresh Burning | Frozen applies; Burning keeps ticking | normal | normal |
| **Frozen** | **thaw**: Frozen expires → Wet → Wet steamed off; damage | refresh Frozen | normal; still halted | damage; still halted |
| **Wet** | **"Steam!"** Wet stripped; damage | Frozen (on expire refreshes Wet) | **×1.5 "Shock!"**; Wet stays | normal |
| **Warm** | Burning | Frozen | normal | normal |
| **Asleep** | damage **wakes** | damage wakes | damage wakes | damage wakes (a miss does not) |
| **Protection** | ×0.85 | ×0.85 | ×0.85 | ×0.85 |

**Amended 2026-10-09:** Fire on a Frozen target now nets a clean target (thaw + steam) in one hit.

#### 8.2.2 Expire chains

`BuffDef.OnExpireApply`: Burning → Warm, Frozen → Wet. A chain fires on natural expiry and on `ForceExpire` (Fire thaw) — never on cleanse or death.

#### 8.2.3 Damage- and move-breaks

`BreaksOnDamage` (Sleep): removed by any non-miss hit with amount > 0, before HP changes. `BreaksOnMove` (Sleep): removed when the bearer steps, is displaced by a slide, or is shoved by a boss.

### 8.3 Ticking

- **Tick-unit buffs** (Burning, Wet, Warm, Poisoned): one duration step — and one damage tick for DOTs — per `BuffTickInterval` = 10 ticks (1.0 s) of game clock, i.e. **2 steps per action**. DOT damage is environmental (no pushback, no stagger) and can kill (loot still drops).
- **Turn-unit buffs** — **Amended 2026-10-09:**
  - **Enemies:** non-halting turn buffs tick at the **end of the bearer's own turn** (so "2 Turns" = two of its turns).
  - **Enemies, timeline-halting (Frozen):** tick at the **shared turn boundary** — each control return — because a halted icon never reaches its own turn.
  - **Heroes:** every turn buff ticks at each control return, i.e. once per committed action.
- Consequence (current truth, tuning watch): Frost (1.5 s cast) lands mid-advance and Frozen (1 Turn) expires at that action's control return — the freeze holds only for the remainder of the action in which it lands. *(Test: `Frozen_halts_the_enemy_icon_and_expires_into_wet`.)*

### 8.4 Immobility hook

`Unit.IsImmobile` (Frozen, Sleep): a hero can't move or use slots (CannotAct); an enemy skips its turn (`TurnSkippedEvent` "asleep"/"immobile", its end-of-turn ticks still run) and plans no step or charge. `Unit.IsTimelineHalted` (Frozen) freezes its turn and cast icons.

### 8.5 Debuff icons (per actor)

**Amended 2026-10-10 (owner): one status slot per actor.** `UnitView.SetBuffs` shows a single letter pip on the tile (colour `BuffDef.Color`, radial ring = time left) that **flashes through every buff, debuff and pushback stun** on the shared 1.2 s beat, with a small count when there are several. The selected actor's card lists each one with its pip, name and a plain-language effect built from its mechanics (`Ui.BuffEffect`: "4 damage each tick", "can't move or act, timeline halted", …) and time left, so the player learns what each pip means. A unit that isn't moving also shows the badge on its timeline circle.

### 8.6 Stacking rules

`BuffDef.Stacking`: **Refresh** (default — re-applying replaces the old instance at full duration), **Ignore**, **Upgrade** (keep the longer), or **Stack** (each stack ticks and applies separately, up to `MaxStacks`; at the cap the oldest stack is replaced). **Burning stacks to 3** (amended 2026-10-10, owner: "fire has multiple stacks"). Damage-over-time ticks on the battle clock, which only advances when the player commits an action or Guards — thinking time never costs HP, so "wait and decide" stays free while every move under fire costs a tick of burn. *(Test: `Burning_stacks_up_to_its_cap_and_each_stack_burns`.)*

### 8.7 Buff lifecycle

Source (spell, item, charge, trap, expire chain, Shield) → `ApplyBuff` (refresh, `BuffAppliedEvent`) → ticks per [§8.3](#83-ticking) (`BuffTickedEvent`) or breaks per [§8.2.3](#823-damage--and-move-breaks) → `Remaining ≤ 0` → `Expire` (`BuffRemovedEvent` "expired") → `OnExpireApply` chain.

### 8.8 Mass-cleanse rules

- **Antidote spell / Remedy item**: remove all debuffs (no chains); buffs (Protection) stay. Antidote/Eye Drops items cure one id.
- **Death**: all buffs dropped silently.
- **Between battles**: buffs live on battle units only; nothing carries over.

---

## 9. The HUD

**Amended 2026-10-09:** the battle HUD is built in code by `BattleScreen.Build` on the 1170×2532 design stage (absolute design-pixel layout; the old `HudLayout` row constants are gone).

```
┌─────────────────────────────────────┐ y=30
│ [II]     Stage name · Wave n/m   N G│  pause · title/wave · coins this battle
├─────────────────────────────────────┤ y=150
│ ─ enemy portraits ──────────────    │  turn icons above the line
│ ━━━━━━━━━━━━━━━━━━━[Zone 35%] ACT  [GUARD]
│ ─ cast icons ───────────────────    │  casts below the line
├─────────────────────────────────────┤ y=368
│        Action banner          3.2   │  banner · drag-timer seconds
├─────────────────────────────────────┤ y=450
│     6 × 8 board (180-px tiles)      │  units: art, HP bar, ≤3 buff pips,
│        heroes, enemies, traps       │  countdown badge, glow
├─────────────────────────────────────┤ y=1910
│ [slot][slot][slot][slot][slot]      │  AbilityBar 5 × 216×196
├─────────────────────────────────────┤ y=2118
│ ● ● ● ● ● ● ● ● ○ ○ ○ ○             │  12 mana orbs with letters
├─────────────────────────────────────┤ y=2250
│ Actor panel: portrait · name · Lv · │  selected hero, or tapped enemy
│ HP (??? until scanned) · stats ·    │
│ "Acts in N moves" · buffs           │
└─────────────────────────────────────┘
```

- **Banner** (`ShowBanner`) carries announcements, refusal reasons, "PINCER!", "X's turn", "Wave n/m", "VICTORY!". **Amended 2026-10-09:** it replaces the old AnnouncementWindow + combat feed. ⬜ Not yet built: the 7-line combat feed with inline icons.
- **Pincer group outline (amended 2026-10-10, owner):** instead of a line across the character art, a rounded outline *draws itself* around the whole group — Hero, foe…, Hero (e.g. a 4-tile-long box) — in 0.24 s, in gold (red for enemy pincers); each supporter is joined to its partner by a short blue link that bridges the gap between their frames, edge to edge. The drag preview shows the same outline. **Amended 2026-10-11** (owner: the shape needs a beat of realization before the cut-in, not to appear alongside it): the outline draw is now *awaited* in full (it used to fire detached and overlap the cut-in) and is followed by a short 0.18 s pause — "caught" reads before the portraits slam in — before any cut-in or pop-up card starts (`BattleScreen.PincerAnim`). **Fixed 2026-10-19 (GG-T98):** the outline is drawn from the formation-time footprints `PincerFormedEvent` now carries (`Attacker1At`/`Attacker2At`/`OpponentsAt`/`SupportersAt`), never from end-of-command state — a member that moved later in the same command (the foe's own turn, knockback, a later chain link) used to stretch it into a multi-tile box. It is always one tile thick across its axis (a 2×2 foe may widen it to its own size); `PincerGeometry.Error` guards that in tests and logs an error in dev builds. Every other playback beat reads a unit's position from its board view (`UnitView.Tile`, advanced event by event), not from `State`. **Fixed 2026-10-19 (GG-T128):** the outline layer (`board.Lines`) lives inside the board again, so it rides every camera twist, zoom, drift and screen shake and always hugs its tiles (GG-T65 had moved it onto the global Overlay). Cut-in cards (`ActionCard`: caster slides, chain pop-ups, supporter pops) and their dimming veil now sit in a stage-sized card layer inside the board just below the outline (board order: tiles, units, marks, fx, cards, outline), so a card still never covers the outline, and the cards move with the board camera; a second veil sheet dims the HUD below the board. Nothing of the battle is on the Overlay under Pause any more except the full-screen pincer cut-in, billboard and coin/orb flights, which the pause veil stays above (GG-T126).
- **Combat presentation (amended 2026-10-10, owner: "keep the frenzy and chaos of multiple attackers … each attack layered up like an accumulating math problem before exploding into the final damage number, and a clear announcement billboard … like Final Fantasy VI"):**
  - **Billboard:** an FF VI–style blue window at the top of the board names everything as it happens — "Paladin & Barbarian — Pincer", "Chain ×2", "Support: Cleric", "Power Amplifier ×1.75", each spell/skill/item with its pixel icon ("Fireball → Slime"), enemy charges ("Witch: Poison Cloud on Paladin"). Up to 3 lines; each fades after 1.6 s (÷ battle speed).
  - **Damage tally:** inside a pincer, every hit on a target adds a term to a tally above it ("10 + 5", crits as "8!", "×AMP" when amplified); when the pincer ends (its orbs mint, the next link starts, or time advances) each tally collapses and **bursts into the total** at 120 px with a screen shake. Single hits keep their normal floating number.
  - **Cut-in modes:** the full sweep on the first link (supporters' portraits slide up in a row from the bottom edge alongside it); chained links get **pop-up portrait cards** beside each attacker (bounce in, drop away); a **spell** slides its caster's portrait in from its side (heroes left, enemies right). All honour Settings → Pincer close-ups and Reduce motion.
- **Action cut-ins (amended 2026-10-11, owner: "portrait slides … should be action packed just like the board that twists and zooms, and have supporters pop in and out to show their participation bonus"):** every portrait arrives on a feathered team-colour band (bright core, racing light streaks) as a head-and-shoulders bust in a tilted white window, twisting in from an oversized tilt with a flash and an impact shake, then whipping out; the board camera punches toward the action and settles. Spells dim the stage behind the caster's cut-in and print the spell name; chained links pop both attackers with "CHAIN ×n"; each **supporter pops in at the moment it strikes** tagged "SUPPORT" and pops back out. The first-link sweep opens on a white flash with a harder push-in and twist.
- **Pincer close-up cut-in** (ported from GridGame2026's portrait slide-in, US-145/US-333): on the first pincer of an action, both attackers' portraits sweep past in lanes flanking the attacked line on the old slide curve (fast in → slight overshoot → drift back → fast out, ~1.14 s at ×1), in one of four entry patterns (counter sweep, mirrored, same-side chase, staggered one-two), with Perlin hand-held sway and a slow push-in. The world dims and the board "camera" pushes in 8.5 % on the pincer with its own sway, then settles back over 0.3 s. Chained links keep the quick beat. Off with Settings → "Pincer close-ups" or Reduce motion (`BattleScreen.PincerCutIn`).
- **Floating text** over units: damage (crit bigger + shake), heals, "Steam!", "Stagger!", "Steady!", "Interrupted!", "CLUTCH!", "SHATTER!", "Snare!", "Trap!", "Zzz…", "Revived!".
- **Actor panel**: enemy HP and stats show only if the class has been Scanned (this battle or ever); while targeting it shows the ability card ([§4.5](#45-slot-visual-states)).
- **Guided first battle (amended 2026-10-11, owner: "a full tutorial with explanations and examples, the first battle needs 5 waves of increasing difficulty"):** Green Valley I has **five waves** — slime; two slimes; bat + level-2 slime; level-2 wolf + goblin thug; level-2 wolf + goblin + slime — and ten lessons, each an explanation plus a worked example: Moving, Pincers, Mana, Chains, Spells (with the confirm step and charge bar), The timeline, Roles & Taunt, Pickups, Interrupting, Ready. A lesson completes when the player does it (or after a few actions, or Next); lessons that need particular foes wait for their wave ("Next lesson arrives in wave N"); moving lessons light up a demo move; Spells and Taunt select the hero and pulse the slot. Skip ends it for good (`SaveData.TutorialDone`).
- **First-battles hint** — **Amended 2026-10-09 (new):** until the third campaign stage is cleared (`HighestClearedStage < 2`), after 5 s of idle input (not on Auto, not targeting) the board shows the move `HeroBot` would make — the hero glows, its path is drawn to a glowing tile, banner "Hint: drag X to the glowing tile" — or "Tip: trap an enemy between two heroes" when no pincer is available. Any input clears it.
- Tile highlights and pincer lines draw **over** the units so previews stay readable.
- Long banner text steps its font down (48 → 38 → 32) so it never runs off the stage.

---

## 10. Equipment (summary — see §24)

A hero has five equipment slots (`EquipmentSlot`: Weapon, Armor, Relic1–3) stored on `HeroSave`. Equipped items' `Stats` add into `Unit.GearStats`; the weapon's `BaseDamage` is added to physical offense (`Unit.WeaponPower`); resistances fold into damage ([§3.3](#33-elemental-resistance)).

---

## 11. Screen Architecture

### 11.1 Code-only boot

**Amended 2026-10-09:** `Assets/Gaiden/Scenes/Main.unity` is an empty scene (the only one in the build, ensured by `GaidenBuild.EnsureScene`). `GameApp.Boot` (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`) creates a `DontDestroyOnLoad` `GameApp` that builds, in code: a UI-only camera (black clear), a `UIDocument` with runtime `PanelSettings` (ScaleWithScreenSize, 1170×2532, theme `UI/GaidenTheme` + style sheet `UI/Theme.uss`), the letterboxed stage, the screens host, an overlay layer and a fade layer; then loads content, the save store and audio, and shows the Title (or `-start <screen>`). There are no prefabs, no `[SerializeField]`s and no scene builders; every screen is a `GameScreen` subclass whose `Build(root)` composes UI Toolkit elements via `App/UI/Ui.cs` helpers and USS classes.

### 11.2 Screen list

`TitleScreen`, `SaveSlotScreen` (new/load), `HubScreen` (Camp), `StageSelectScreen` ("Adventure"), `StoryCrawlScreen`, `BattleScreen`, `LevelUpScreen`, `PostBattleScreen`, `ItemsScreen`, `StatusScreen`, `EquipScreen`, `AbilitiesScreen`, `PartyScreen`, `ShopScreen` ("Merchant"), `BlacksmithScreen`, `AlchemistScreen`, `SummonScreen`, `BountyScreen`, `BestiaryScreen`, `SettingsScreen`, `CreditsScreen`, plus the `Confirm` modal. `Automation/StartScreens` maps ids → screens for `-start` and the `goto` endpoint.

#### 11.2.1 Screen transition graph

```
Title ──▶ SaveSlot (New / Load) ──▶ Camp (HubScreen) ◀──────────────────────────────┐
  │ Continue ─────────────────────▶   │ menu rows push sub-screens; Back pops      │
  └─ Credits                          ├─ Adventure ─▶ [StoryCrawl, first entry] ─▶ Battle
                                      ├─ Items · Status · Equip · Abilities · Party │    │
                                      ├─ Merchant · Blacksmith · Alchemist · Inn    │    ▼
                                      ├─ Summon Circle · Bounties · Bestiary        │ PostBattle
                                      ├─ Settings                                   │ (Continue → Camp,
                                      └─ Save & Quit ─▶ Title                       │  Retry / Next stage
                                                                                    └── → Battle)
```

### 11.3 Screen navigation

`ScreenRouter` is a stack: `Push`, `Replace`, `ResetTo`, `Back`. Every transition fades the stage to black for **0.2 s** and rebuilds the top screen. **Amended 2026-10-09** (old fade 125 ms). Escape / hardware back calls `Router.Back`; a screen can swallow it (`OnBack`: the battle cancels targeting, then opens the pause menu). **Fixed 2026-10-19 (GG-T126): Pause pauses.** Opening the battle's pause menu sets `Tween.Paused`, so `Tween.Dt` reads 0 and every playback beat, cut-in, outline, float, coin burst, dissolve, banner scroll and ambient pulse (`Tween.Clock`) holds its exact frame; the event player and `Safe()` start no beat, the watchdog, drag timer, hints and auto battle stop, and ringing sound effects pause mid-clip (music plays on). Resume (or Back) carries on from the same frame. The pause veil is kept the topmost Overlay child, above `board.Lines`, cut-ins, the billboard and coin/orb flights. Verified by `tools/harness.py pause` (pauses mid-chain, two identical frames 2 s apart, zero beats while paused, resume finishes the chain). Screens rebuild on every show and on `Refresh()`, so they always reflect current state. UI callbacks are ordinary C# lambdas (the old "no lambdas in persistent listeners" rule is moot).

### 11.4 Build settings

`GaidenBuild.EnsureScene` writes `EditorBuildSettings.scenes = [Main.unity]`, portrait orientation, no splash, `bundleVersion` from `VERSION`. New screens need no build-settings change.

### 11.5 The visual language

**Amended 2026-10-11 — one press feel (owner: "all button presses need feedback like depress … a standardized look and feel"):** every tappable thing goes through `Ui.Pressable` (buttons, menu rows, tabs, cards, bar cells, battle slots). On touch it sinks — buttons stand on a darker lip (thick bottom border) and drop onto it, everything shrinks to 95 % and darkens — with a soft `ui.press` tick; on release it springs back (`ease-out-back`), then the action plays its own cue (`ui.click` / `ui.select` / `ui.confirm` / `ui.cancel`). Disabled things don't sink. Battle tiles are lighter and lower-contrast so the stage background reads through the board.

One Final Fantasy–style language (`Resources/UI/Theme.uss`): deep blue gradient windows (`Art.Window`, generated) with white rounded frames, pointing-cursor menu rows (`Ui.MenuRow`, selected row highlighted), gold numbers, rarity-colored item names, gauges, tabs, primary/danger buttons, toasts on the overlay. **Amended 2026-10-10 (owner):** battle units wear the same frame as menu portraits (`.thumb` → `.unit-frame`: white 4 px rounded border over the window gradient); team reads from a thin inner keyline (hero blue, foe red, boss purple `.unit-keyline--*`). Fonts (`App/Services/Fonts.cs`): **Outfit** body, **Cinzel** display (titles), both with Noto Sans → Noto Sans Symbols 2 fallbacks ([GG-LAW-11](#GG-LAW-11)). **Amended 2026-10-09:** replaces HubTheme / UiFonts / UiKit (Attic + Outfit).

---

## 12. Asset Pipeline

### 12.0 Audio

**Amended 2026-10-09:** every cue is an authored clip under `Resources/Audio`; there is no chiptune fallback (a missing clip is silent).
- **Music** (`App/Services/Audio.Music`, per screen via `GameScreen.Music`): Title and Story crawl "TellerOfTheTales", Camp "MinstrelGuild" (sub-pages keep it), Battle "Crusade", Victory "Triumph", Defeat/Escape "MelancholyLull".
- **SFX** (`Audio.Sfx(key)`): game code speaks semantic cues (`ui.click`, `ui.select`, `ui.buy`, `ui.error`, `move`, `slide`, `pickup`, `pincer`, `hit`, `crit`, `miss`, `death`, `heal`, `orb`, `turn`, `fire`, `ice`, `lightning`, `poison`, `magic`, `cast`, `explosion`, `shatter`, `debuff`, `dash`, `trap`, `victory`, `defeat`) mapped to files in `Audio.Cue`; rapid repeats within 40 ms are thinned; 10 pooled sources.
- **Attribution** lives in `Resources/Credits.txt` (Credits screen): Kevin MacLeod CC BY 4.0 tracks, Pixabay "Triumph"; "Melancholy Lull" and the SFX packs are flagged **origin unverified — confirm or replace before commercial release**.
- `tools/import_art.py` reproducibly imports audio (and art) from the GridGame2026 reference project.

#### 12.0.1 Announcements — cadenced callouts

Announcements (`AnnounceEvent`) are played by the event player as an awaited 0.25 s banner beat, one at a time, never an instant swap. Wired for refusals, charges ("X is charging Y!"), boss phases, "Clutch!", "Interrupted!", "Steam!", "Steal!", "Banked N mana", "Escaped!", enemy pincers.

#### 12.0.2 Pacing

**Amended 2026-10-09:** every beat duration lives in `BattleScreen.Playback.cs` (no `PacingConfig`): step 0.16 s, displacement 0.14 s, pincer 0.32 s, orb flight 0.22 s, timeline glide 0.18–0.45 s, turn pop 0.2 s, projectile 0.22 s, victory/defeat banner 1.0 s + 0.6 s before results. All durations divide by `Tween.Speed` = Settings → Battle speed (×1 / ×1.5 / ×2 / ×3). Nothing the player must read resolves in a single frame.

### 12.1 Art

**Amended 2026-10-09:** textures load with `Resources.Load` through `App/Services/Art.cs` (no Addressables, no `SpriteLibrary`):

| Key | Path | Source |
|---|---|---|
| Board/timeline thumb | `Resources/Thumbs/<classId>.png` (256²) | face crop by `import_art.py` |
| Portrait | `Resources/Portraits/<classId>.png` | full body, ≤ 640 px |
| Backdrop | `Resources/Backgrounds/<theme>_<n>.jpg` (720×1440, n 0–3) | biome art |
| Ability icon | `Resources/Icons/<Key>.png` (64²) | `AbilityDef.Icon` / `ItemDef.Icon` |

Every class has a thumb (`Every_class_can_be_instantiated_and_has_art_reference`).

### 12.2 Procedural pieces and placeholders

`Art` generates the window gradients, cursor, glow and orb textures at runtime. **Board tiles (amended 2026-10-10, owner):** GridGame2026's own tile art (`Resources/Tiles/tile.png`, its `tile3`, imported by `tools/import_art.py`), recoloured per campaign biome by `tools/make_tiles.py` (moss, sandstone, murky teal, rust-rose; the caverns keep the original slate) and tinted light/dark per square to keep the checkerboard (`Art.BoardTile(theme)`). A missing texture falls back to a purple placeholder and logs once — a gap is visible, never fatal.

### 12.3 VFX

⬜ Not yet built (see [§6.2](#62-vfx-pipeline)).

---

## 13. Combat Resolution

### 13.1 Damage formulas

All in `Core/Rules/Formulas.cs` (ported from the old `Formulas` with engine types removed).

#### 13.1.1 Physical (pincer, supporter, enemy melee)

```
off   = attacker STR + weapon BaseDamage           def = defender VIT (+ armor rating, unused = 0)
raw   = max(1, max(off − max(0, def − 0.25·off)·0.35, 0.20·off))
raw  ×= defender physical resistance
var   = raw × U(0.9, 1.1) × LCK tilt (0.99..1.01)
hit%  = clamp(80 + 0.5·SPD + 0.2·LCK + 3·(attacker lv − defender lv), 5, 95) × Blind accuracy (×0.5)
crit% = clamp(8 + 0.4·LCK, 0, 50)        roll ≤ crit% → Critical; ≤ hit% → Normal; else Miss
dmg   = max(1, floor(var × (crit ? 1.6 : 1) × 1.07^(Δlevel) × multiplier))
dmg   = max(1, floor(dmg × incoming multiplier))     (immune if physical resistance ≤ 0)
```

`multiplier` = 0.5 for supporters, 1.5 for a shattering weapon. A Miss deals 0 and never pushes, staggers or wakes.

#### 13.1.2 Spell damage (dispatcher)

`Formulas.SpellDamage`: `round(base × resistance × (Lightning on Wet ? 1.5 : 1) × incoming)`, min 1 unless immune; then × `BonusMultiplier` vs `BonusVsTag` (items). No crit, no miss, no Blind. ⬜ Not yet built: unifying spells with the physical roll (old backlog).

#### 13.1.3 Healing

`Heal`: `HP = min(MaxHP, HP + BaseHeal)`; the event reports the amount actually restored. No resistance, no crit, no overheal.

#### 13.1.4 Cast time

`Formulas.CastTime(base, stats) = max(0.25·base, base × 50 / (50 + 0.5·(WIS + INT)))`, converted to ticks (`round(s × 10)`, min 1). Hero casts only; enemy charges use their authored 2.5 s.

#### 13.1.5 Enemy magic (charge resolution)

`Formulas.Magic`: same mitigation shape with INT vs WIS, element resistance, variance, the shared hit/crit roll and level scaling; plus the charge's flat `SpellDamage` (0 in content). Can miss.

#### 13.1.5 Caster power (role identity)

Added 2026-10-11. A hero's spell damage scales with **INT** (physical skills with **STR**), heals with **WIS**: `1 + (stat − 12) × 0.035`, clamped 0.7–1.6 (`Formulas.CasterPower / HealPower`). A Red Mage (INT 20) hits ×1.28, a Fighter's Fireball ×0.9. Items and enemy abilities keep their tuned numbers. **Empowered** (War Cry, Might Rune) multiplies all damage the bearer deals by 1.3. *(Test: `Caster_power_makes_a_mage_out_hit_a_fighter_and_a_sage_out_heal`.)*

### 13.2 HP delta

`BattleRunner.Damage` clamps HP at 0, emits `DamagedEvent` (amount, hit kind, type, HP after, note), credits hero threat (`Unit.DamageDealt`), kills at 0, then applies pushback and cast stagger.

### 13.3 Death and loot

At 0 HP (`Kill`): off board, buffs cleared, every timeline entry it owns removed (pending casts vanish), `DiedEvent`; enemies award loot (`AwardLoot`):
- **XP** `Formulas.XpReward = round(XpReward + 2·level + 0.10·(STR+VIT) + 0.08·(INT+WIS))`, min 1.
- **Coins**: the drop table's `CoinsMin..CoinsMax` if authored; **Amended 2026-10-09:** otherwise **≈ half the XP, ×U(0.8, 1.2)**, min 1 (no table in content authors coins). Snake segments drop no coins.
- **Items**: each drop-table entry rolls its `Chance` for `Min..Max`.
- **Kills** count by class (bounties, bestiary); snake segments don't count.

The view floats "+N G" and item names from the corpse. Totals accumulate in `BattleResults` and are committed by `BattleFactory.Commit` ([§22.1](#221-per-transition-state-contract)).

#### 13.3.1 Pickups

Added 2026-10-11 (owner: "powerups should sometimes drop"). A fallen enemy leaves a pickup with chance `0.20 + 0.004 × killer LCK` (max 0.40), on its tile or a free one beside it, never more than 3 on the board (`pickups.json`, weighted):

| Pickup | Effect |
|---|---|
| Heart Tonic | heals the collector 25 % of max HP |
| Mana Shard | 1 orb |
| Quicksilver | hastens the collector 0.3 |
| Might Rune | Empowered (×1.3 damage) for 2 turns |

The first hero to enter the tile collects it (walking, being displaced, or teleporting). Enemies don't collect; one may stand on a pickup. **One item per tile, ever (GG-T129, §1.1.3):** a pickup only drops on a tile that holds no other pickup and no movable (`FreeForItem`), the Power Amplifier never spawns or is shoved onto a pickup, and the view never draws two item tokens on one tile or keeps the token of a collected or faded pickup. A pickup fades after **4** player actions (a ring on the token counts down). It pops out of the fallen enemy with a bounce; collecting flies it to the hero. *(Tests: `Stepping_on_a_pickup_collects_it_and_heals`, `A_round_trip_drag_that_only_grabs_a_pickup_still_counts_as_a_move`, `Pickups_fade_after_their_lifetime`, `Fallen_enemies_sometimes_leave_pickups`; items apart: `The_amplifier_never_spawns_on_a_pickup`, `Strict_soak_with_drops_and_amplifier_shoves_never_stacks_items` and the rest of §1.1.3's GG-T129 tests.)*

### 13.5 Armor plates (added 2026-10-11, owner: "some enemies are armored and need to take damage from that side to break it off")

`ClassDef.ArmorPlate` gives a family a plate on the side it **faces** (`Unit.Facing`, turned toward the hero it wants at spawn and at the end of each of its turns — `FacingChangedEvent`). Plate HP = `ArmorPlate × (1 + 0.15 × (level − 1))`. A **physical** blow (pincer strike, supporter, Cleave, Shield Bash) from the plated side (`SideOf`, dominant axis) dents the plate instead of the body (`PlateHitEvent`, "CLANG! −n"); at 0 the plate breaks off for good (`PlateBrokenEvent`, shards, "ARMOR BROKEN!"). Blows from the flanks or behind land normally; **spells go around the plate**. Families: ceramic knights 30, templars 30, mecha armour 40, soldiers 18, the captain 24. Tactics: pincer it across its line of sight, break the plate head-on, or let the mage burn it. The plate is a riveted steel bar on the tile's rim; the enemy card says where it faces and how much is left. The bot avoids lining an attacker up on the plated side. *(Tests: `A_plate_takes_the_blow_from_its_facing_side_and_the_flank_hits_flesh`, `A_pincer_across_its_line_of_sight_skips_the_plate_and_a_broken_plate_stops_blocking`, `Spells_go_around_the_plate`.)*

### 13.4 The interrupt path

Any landing hit (source not null, not a miss) on a unit with a cast in flight runs `BattleRunner.StaggerCasts(caster, attacker)` — hero casts hit by enemies, enemy charges hit by heroes.

Order per hit:
1. **Clutch** (hero casts only): chance `min(20%, LCK / 200)` → `ClutchEvent`, "Clutch!", the cast resolves immediately (`ResolveCast`). The view plays a white flash + "CLUTCH!".
2. **WIS shrug**: chance `min(60%, 1.5% × WIS)` → `CastStaggeredEvent(Resisted)` "Steady!", no delay.
3. **Stagger**: `delay = 0.6 s × (1 + attacker STR / 20) / (1 + caster WIS / 15)` → ticks, added to both `RemainingTicks` and `AccumulatedDelayTicks` ("Stagger!").
4. **Cancel**: once accumulated delay ≥ the cast's original ticks, the cast is removed (`CastCancelledEvent` "interrupted", "Interrupted!"); orbs stay spent. Cancelling an **enemy charge** mints one orb ([§3.1.2](#312-harvesting)).

**Enemy charges** ([§14.2](#142-enemy-archetypes-design-palette)): `EnemyPlanner.PlanCast` lets a unit with a `ChargeAbility`, not immobile and **not adjacent to any hero** (unless its boss phase `PrefersCharge`), queue a charge on the nearest hero instead of moving. **Amended 2026-10-09:**
- Single-target charges **lock onto the hero**, wherever it moves — dodge only by interrupting (or killing).
- Line charges (`ChargeShape.Line`) lock the cardinal line from beside the caster to the edge on the dominant axis toward the target (X on ties); at resolution every hero still on those tiles is hit ("Dodged!" if none). Edge-pinned lines fall back to single-target. 🟡 Built in Core, but no enemy in `classes.json` uses `ChargeShape: Line` yet (only hero-class rows carry it).
- A charging enemy that gets another turn before its charge lands moves/attacks normally (no second charge).

*(Tests: `Enemy_charge_is_telegraphed_and_can_be_interrupted`, `Every_enemy_charge_telegraphs_and_resolves`.)*

---

## 14. AI

### 14.1 Enemy planning

`BattleRunner.RunEnemyTurn(enemy)`, when its icon arrives:
1. Mint the pending time bank (first enemy turn of the action) and emit `TurnStartedEvent`.
2. **Boss phases** advance ([§14.3](#143-ai-hooks)).
3. **Immobile** → skip the turn (end-of-turn ticks still run).
4. **Charge**: if not already casting, `PlanCast` → queue a charge and end the turn.
5. **Trap**: a trap-layer with no hero adjacent rolls `LayChance` (0.45) to arm a free, trap-free cardinal tile, ending the turn.
6. **Move** up to `MoveRange` steps (all content: 1), re-planning each step with `PlanStep`; a multi-tile step shoves heroes ([§1.5](#15-multi-tile-actors-22-bosses)); a snake head drags its chain.
7. **Attack**: a Humanoid 1×1 enemy that is now an end of an enemy pincer resolves those pincers ("X pincers!"); otherwise it melees the adjacent hero its **targeting personality** picks (below). No adjacent hero → no attack.
8. End of turn: its non-halting turn buffs tick; battle-end check.

**Targeting personalities (amended 2026-10-10, owner direction: "smarter enemies attack the weakest, dumb enemies the closest").** `ClassDef.Targeting` drives movement (`PlanStep`), melee choice, ranged choice and charge targets (`EnemyPlanner.PickFrom`); ties fall back to distance, then HP, then id:

| Personality | Picks | Families |
|---|---|---|
| Closest | nearest hero | slimes, undead, cyber-zombies, skelepedes, tree golems, mannequin, frogs, toad, sandmaw, cyclopes, troll, yeti, shamblers, ceramic knights, templars, naga |
| Weakest | lowest current HP, even across the board | wolves, werewolf, vulture, hags, swamp mistress, reaper, phantom, lurkers, tech gremlins, purple prototypes |
| Threat | hero who has dealt the most damage (Closest until someone has) | captain, soldiers, demon lord, oni, mecha armor, ice mauler |
| Squishy | lowest VIT (then MaxHP) | gangers, steppin' razors, scorpion, goblin thug |
| Erratic | a different hero each turn (hash of clock + id; no RNG draw) | bats, flying monkey, ghost |

`Auto` (unset) derives Weakest for INT ≥ 14, Threat for Humanoids, else Closest. The enemy card shows it ("Hunts the weakest hero").

**Speed tiers (amended 2026-10-10):** enemy Speed is 18 (swift: bats, wolves, flyers, werewolf, steppin' razors, phantom), 15 (quick), 11 (steady) or 8 (slow: slimes, undead, zombies, golems, mannequin, ghost). Open-track rate scales with Speed and the Zone is a shared crawl, so a fast enemy can overtake a slow one during the windup but not inside the Zone.

`EnemyPlanner` is pure: it reads state (temporarily moving the enemy's `Pos` to score a candidate, then restoring it) and never commits.

#### 14.1.1 Decision tree

```
PlanStep(state, enemy)
  immobile or snake segment? ─ yes ─▶ stay
  target = PickTarget: argmin( distance + 8·HPfrac − threat·INT·0.8 )
  candidates = {stay} ∪ {4 cardinals that are in bounds & free (1×1) | FootprintStepLegal (2×2)}
  for each c:   wounded (HP < 30%) ? score = +dist : score = −dist
                not wounded & adjacent to target here      +2
                1×1 & would be sandwiched by two heroes    −100
                c == stay                                  −0.5
                not wounded & Humanoid 1×1 & forms pincer  +50
                not wounded & becomes ally-pincer supporter +25
  return argmax(score)
```

The −100 sandwich avoidance (two heroes directly on opposite sides) outweighs the +50 pincer-seek: enemies pick safe pincers, not suicide pincers.

#### 14.1.2 Score weight cheat sheet

| Factor | Weight | Notes |
|---|---|---|
| Distance to target | −1 per tile (+1 when wounded) | footprint-aware Manhattan |
| Adjacent to target | +2 | dropped when wounded |
| Sandwiched by heroes | −100 | immediate neighbors on one axis; 1×1 only |
| Stay put | −0.5 | mild advance bias |
| Forms own Humanoid pincer | +50 | Humanoid 1×1, not wounded |
| Supports an ally's pincer | +25 | any enemy, not wounded |
| Threat (target choice) | −(DamageDealt / max) × INT × 0.8 | smart enemies hunt the top damage-dealer |
| Wounded retreat | below `RetreatHpThreshold` 0.30 | flees; flank-avoid still applies; never against a lone hero (GG-T123, [§22.10](#2210-the-crucible)) |

Constants live in `EnemyPlanner` and `BattleConfig.RetreatHpThreshold`.

#### 14.1.1 Taunt

Added 2026-10-11 (owner: "a tank should be able to taunt enemies"). While any hero carries a buff with `Taunts` (Taunting, 2 turns, −25 % damage taken), `EnemyPlanner.PickFrom` only considers taunting heroes among those in reach — so walking, charging and ranged foes all go for the tank, and a taunter out of reach doesn't save an adjacent ally. Synergy: a hit staggers a hero's cast, so a taunting tank keeps the mages' spells charging. *(Tests: `A_taunting_hero_draws_every_enemy_that_can_choose`, `Taunt_only_overrides_among_heroes_in_reach`.)*

### 14.2 Enemy archetypes (design palette)

| Archetype | Lean | Tags | Behavior today |
|---|---|---|---|
| Rusher | high SPD, low VIT | Beast | loads fast; closes and melees |
| Bruiser | STR + VIT | Humanoid | slow, tough; best pincer target |
| Flanker | mid STR, AGI | Humanoid | seeks own pincers (+50), supports (+25) |
| Ranged | AGI, INT | Humanoid | ⬜ Not yet built: keep-distance behavior and ranged attacks |
| Caster | INT, low VIT | Magic (+ affinity) | charges when no hero is adjacent; `icemauler` (`charge_ice`) is the live caster (cave-02, cave-03) |
| Beast | varies | Beast | never pincers; rushes |
| Mechanical | VIT, immunities | — | 🟡 resistances supported; no class authors them yet |
| Boss | everything | 2×2 + `BossPhases` | `cyclops00`: phases + enrage ([§14.3](#143-ai-hooks)) |
| Line caster | as Caster | `ChargeShape: Line` | 🟡 built in Core, no enemy authored ([§13.4](#134-the-interrupt-path)) |
| Trap-layer | varies | `Trap` | `scorpion` lays `venom_snare` (6 Poison + Poisoned) |
| Segmented snake | boss | `SnakeSegments` | `naga00` (3 segments) — see below |

**Traps** (`BattleState.Traps`, `traps.json`) — **Amended 2026-10-09:** visible tiles armed by a trap-layer; triggered by a **hero** entering the tile in any way — stepping, being displaced by a slide, being shoved by a boss, or teleporting — once, then removed (damage is environmental; buff applied). Enemies never trigger traps. Trap tiles are excluded from Teleport, Revive and spawn.

**Segmented snake** — a class with `SnakeSegments` n grows n body segments on free adjacent tiles at spawn (`GrowSnake`, preferring up/left/right/down); each segment has half the head's MaxHP, no timeline icon and no plan, and follows the head's vacated trail front-to-back (`FollowSnake`). **Tail-first:** any member is **armored** (damage set to 0, "Armored!") while a later chain member lives. Chain members are immovable to drags. Segments award XP but no coins or kills.

### 14.3 AI hooks

- **Casting enemies** — [§13.4](#134-the-interrupt-path).
- **Supporter positioning** — `WouldSupportAllyPincer` (+25), reusing `PincerDetector` supporters.
- **Boss scripted phases** — **Amended 2026-10-09:** `ClassDef.BossPhases` is data (`HpThreshold`, `PrefersCharge`, `OnEnterAbility`, `Announce`). At the **start of the boss's own turn**, every phase whose threshold the HP fraction has dropped to is entered in order: `BossPhaseEvent`, its announcement, and its on-enter ability resolved on itself. `cyclops00`: phase 0 at ≤ 100% ("Cyclops", on its first turn), phase 1 at ≤ 50% ("ENRAGED!" + `enrage`, self-Hasten 0.35). `PrefersCharge` lets a phase charge even in melee range (no content uses it yet).
- **Threat tracking** — `Unit.DamageDealt` (hero damage dealt this battle) feeds `PickTarget`.
- **Retreat** — below 30% HP.

---

### 14.4 Enemy kits (added 2026-10-10, owner direction: "every enemy with its own unique behaviours and spells and whether they lay traps")

Generic, data-driven mechanics (`ClassDef`), so each family's kit is content, not code:

| Mechanic | Field | Effect |
|---|---|---|
| Trail | `Trail` (trap id) | the tile it steps off becomes that trap (max 3 per enemy, oldest removed) |
| On-hit | `OnHitBuff`, `OnHitChance` | a landed attack (melee, ranged, pincer) may inflict the buff |
| Death burst | `DeathBurst` | on death, damages every adjacent hero ("self-destructs!") |
| Passives | `Abilities` | `lifesteal` (heals half the damage dealt), `tough_hide` (physical blows land at 70%), `berserk` (+35% damage below half HP), `regenerate` (6% MaxHP at the end of its turn), `double_attack`, `counter_attack` |
| Support charges | `ChargeAbility` with `Filter: AllyOnly` | Howl (allies in 2 tiles hasten 0.25u — only when another ally in range is already at a hero, GG-T123), Rally (allies gain Protection), Mend (heal the most wounded ally 30 — only when one is below 70%) |
| Area charge | `ChargeAbility` with an area `Shape` | Poison Cloud: 3×3, 6 poison + Poisoned |
| Traps | `Trap` | Poison Cloud (miasma), Shock Mine (10 lightning + Slowed), Caltrops (8), plus the trail traps Slime Trail / Mire (Slowed) and Quicksand (4 + Slowed) |

Family kits: slimes leave a Slime Trail · bats move 2, lifesteal · wolves Howl · werewolf moves 2, berserk, lifesteal · vulture & lurkers move 2 (lurkers' bites poison) · flying monkey moves 2, blinds · frogs/toad/skelepedes/scorpion/naga poison on hit · hags cast Poison Cloud and lay it as a trap · swamp mistress Mends · marsh shamblers leave Mire, tough hide · undead & reaper lifesteal · cyber-zombies self-destruct (14) · ceramic knights, templars (Smite), mecha armor (Thunder) and cyclopes have tough hide · tree golems regenerate · purple prototypes & oni go berserk · tech gremlins lay Shock Mines · gangers blind · steppin' razors move 2 and strike twice · goblin thug lays Caltrops · soldiers & mannequin counter · the captain Rallies · demon lord casts Fireball, lifesteals · phantom moves 2, silences · ghost slows · ice mauler & yeti may freeze · mountain troll regenerates and goes berserk · sandmaw leaves Quicksand, berserk. The enemy card and the bestiary list each kit (`Ui.KitTraits`).

### 14.5 Remains — gravestones and bone piles (added 2026-10-10, owner direction)

Owner: "some undead enemies should spawn a solid grave stone like in Darkest Dungeon where you have to delete the gravestone / pile of bones to be able to finally defeat the enemy, or after a while they will resurrect."

- `ClassDef.Remains` names the marker an enemy leaves: hooded **undead → Gravestone**, **skelepedes → Bone Pile**. On death (after its loot), the remains spawn on the same tile as an enemy unit tagged `Remains` (`RemainsLeftEvent`).
- Remains are **solid** (can't be dragged through or knocked back), never move or attack, are immune to status effects, give no loot or XP and never appear in the bestiary. They have their own slow timeline icon (Speed 5) — the countdown to resurrection.
- **Destroy them** (pincer or spell) and the undead is laid to rest for good. A wave is not cleared while remains stand.
- **Leave them** and when their icon reaches ACT the undead **rises again** on that tile at half HP and drops nothing a second time (`RoseAgainEvent`); kill it again and it leaves fresh remains.
- Art: procedurally drawn gravestone and bone pile (ImageMagick), pixel sprites via `tools/make_pixel_icons.py`.
- *(Tests: `A_slain_undead_leaves_solid_remains_that_block_the_wave_from_clearing`, `Remains_left_alone_raise_their_owner_at_half_hp_without_loot`, `Remains_are_solid_and_real_undead_leave_them`.)*

### 14.6 Brains — every family thinks differently (added 2026-10-11, owner: "come up with ai for every single enemy unit, they are all different in their own unique ways")

`ClassDef.Brain` sets how a family moves and decides, on top of its targeting personality (§14.1) and kit (§14.4). `EnemyPlanner.PlanStep` scores tiles with the brain's taste (`BrainScore`); `BattleRunner.BrainTurn` handles brains that change the shape of a turn. The enemy card and bestiary describe it.

| Brain | Behaviour | Families |
|---|---|---|
| Hunter | closes on its prey (the classic) | purple prototype, yeti |
| Pack | +1.5 per kin beside it — moves as a group | wolves, gangers, skelepedes |
| Ambusher | holds still until a hero is within 2 tiles | lurkers, naga, scorpion |
| Skirmisher | adjacent at turn start → strikes first, then darts away | bats, flying monkey, steppin' razors |
| Kiter | holds ~3 tiles from heroes (melee kiters keep charging spells) | hags, mecha armour |
| Guardian | stays beside its most valuable ally (boss, caster, commander, medic) | ceramic knights, templars, ice mauler |
| Berserker | charges, ignores flanking danger, never retreats | cyclops, oni, werewolf |
| Scavenger | hunts the wounded, retreats at 50 % HP | vulture, reaper |
| Trapper | keeps ~3 tiles off to lay traps on the approach | goblin thug, tech gremlins |
| Sluggish | moves every other turn | slimes, marsh shamblers, tree golems |
| Hopper | can leap two tiles, over units or blocks | frogs, toad (toad's tongue slows) |
| Blinker | every 2nd turn appears beside its prey and strikes | ghost |
| Burrower | every 3rd turn tunnels under and bursts up beside its target | sandmaw |
| Bomber | dives where it touches the most heroes (it explodes) | cyber zombies |
| Medic | stays one tile from its most wounded ally, away from heroes | swamp mistress |
| Commander | stays ~3 tiles back with an ally between it and the heroes | captain, demon lord |
| Duelist | wants exactly one hero adjacent; avoids being outnumbered | soldiers |
| Stalker | targets casters (new `Caster` targeting), avoids crowds | phantom |
| Sentinel | never moves | mannequin, remains |
| Relentless | never retreats | undead, mountain troll |

No two families share brain + targeting + kit (`Every_enemy_family_plays_differently`). Enemy physical strikes carry `EnemyStrikeMultiplier` 0.9 (retuned after the brains with `BalanceReport`). *(Tests: `An_ambusher_waits_until_a_hero_comes_close`, `A_sentinel_never_moves_and_a_sluggish_foe_moves_every_other_turn`, `A_hopper_leaps_two_tiles_over_a_unit`, `A_bomber_dives_where_it_touches_the_most_heroes`, `A_skirmisher_strikes_then_darts_away`, `A_burrower_surfaces_beside_its_target`, `A_guardian_stays_by_its_ward`.)*

### 14.7 Corpses (added 2026-10-11, owner: "add in the corpses for some things so they get in the way like Darkest Dungeon")

Beasts and humanoids of several families (wolves, gangers, soldiers, oni, werewolf, mountain troll, yeti, goblin thug, toad, captain, lurkers, scorpion) leave a **corpse** (`Remains: "corpse"`, tags Remains + Corpse) on their tile: it blocks movement like any remains, sits in pincer lines (a pincer through it clears it — "Cleared away"), takes area spells, has no turn and never rises, **does not hold up a wave clear**, earns no mana or loot, and **rots away after 6 player actions**. It's drawn as the fallen unit's own art, upright and greyed (amended 2026-10-19, owner: "don't turn monsters sideways" — it was rotated 90°); its card says whose body it is and when it rots. *(Tests: `A_fallen_foe_leaves_a_corpse_that_blocks_the_way_but_not_the_win`, `A_corpse_blocks_movement_and_rots_away`.)*

### 14.8 Reinforcements and eggs (added 2026-10-12, owner direction)

Owner: "sometimes enemies can lay eggs that hatch, sometimes new enemies don't spawn on the first turn... every wave can offer a surprise like fighting monsters then suddenly soldiers show up."

**Delayed mid-wave spawns.** `SpawnDef.DelayTicks` (default 0 = today's immediate spawn, unchanged for every untouched wave) queues an entry into `BattleState.PendingSpawns` instead of spawning it at wave start; `BattleRunner.Tick` resolves any entry whose `TriggerClock` has arrived via `ResolvePendingSpawns`, spawning it exactly like a normal wave arrival. A wave with an unresolved pending entry **holds the wave open** even once the board is clear (`CheckEnd` checks `PendingSpawns` alongside living units), so the wave never advances out from under a reinforcement still inbound.

**The surprise beat.** `WaveDef.SurpriseSpeaker`/`SurpriseLine` (optional, wave-scoped) fire once — on the *first* delayed entry a wave triggers, not per enemy — as a `ReinforcementsArrivedEvent`. The App layer plays a quick, per-class entrance (burst-up for `Brain.Burrower`, drop-from-above for `Flying`-tagged classes, fade-in for `Undead`-tagged classes, the ordinary wave fade+drop otherwise — derived entirely from existing `Tags`/`Brain`, no new content fields needed), then, if a line is set, a held narrative card: the speaker's portrait and name beside the line (reusing the same 360×400 bordered-frame convention as the character-select reveal and the level-up fanfare), dismissed by a tap or ~2.6s, same dual-exit idiom as the story crawl. Green Valley I's wave 4 is the flagship example: a goblin/bandit skirmish that a captain's soldier squad reinforces mid-fight. Automation: `/demosurprise?speaker=&line=` plays the beat on demand for testing.

**Eggs.** An egg-laying class sets `ClassDef.LaysEgg` (the egg's class id) and `EggInterval` (lay every Nth own turn, default 4); `BattleRunner.Enemy.LayEgg` drops the egg on a free adjacent tile, mirroring how `LeaveRemains` drops a grave marker. The egg class is tagged `Egg` (`Unit.IsEgg`, the `IsRemains` pattern's sibling) and is otherwise built exactly like a gravestone/bone pile (`Brain.Sentinel`, immobile, no turn except its own). When the egg's timeline turn arrives, `Hatch` spawns its `RisesAs`/`RisesLevel` class on the same tile **at full HP** (a fresh enemy, unlike a risen undead's half-HP second life) and leaves normal loot. `naga00` (Mireholt Fens II's solo finale) is the one showcase of this so far. *(Tests: `A_delayed_spawn_arrives_partway_through_its_wave_not_at_wave_start`, `A_waves_reinforcement_dialogue_fires_once_even_with_multiple_delayed_entries`, `An_egg_hatches_into_its_real_enemy_at_full_hp_on_schedule`.)*

## 15. Save

### 15.1 Data model

**Amended 2026-10-09:** **three save slots** replace profiles and save-state lists.

```
SaveData                       (Meta/SaveData.cs; one per slot)
├─ Version (1), Name ("Player"), CreatedUtc, UpdatedUtc, PlaySeconds
├─ Gold (wallet), LifetimeCoins (ticker)
├─ HighestClearedStage (campaign index, −1 = none), ClearedStages{}, SeenCrawls{}, LastStage
├─ Roster[]  HeroSave: ClassId, TotalXp, Hp (0 = full), Known[], Bar[5], SkillPoints,
│                       WeaponId/Durability/Repairs, ArmorId/Durability/Repairs, Relics[3]
├─ Party[]   class ids of the active squad (≤ 4)
├─ Inventory { itemId → count }, Recruits
├─ Bounty    ActiveId, Progress, Claimed{}
├─ Bestiary  { classId → Seen, Scanned, Defeated }
└─ Settings  MusicVolume, SfxVolume, MuteMusic, MuteSfx, ColorblindMode, ReduceMotion, DragTimer, BattleSpeed
```

Level is **derived** from `TotalXp` (`Formulas.LevelFromTotalXp`, `XpToNext(L) = 50 + 10·L²`), so the curve can change without migration. `SaveMigrations.Migrate` upgrades in place (v1 is the first shape; it backfills missing sections and pads Bar/Relics) and rejects saves newer than the build. Dictionaries and sets serialize natively (Newtonsoft) — the old "lists only" limit is gone.

**Amended 2026-10-15 (GG-T49):** every level gained now also earns **skill points** — `HeroSave.SkillPoints`, an unspent balance spent on skill-tree nodes (content and UI land in GG-T50). `Formulas.SkillPointsForLevelUp(from, to)` awards `SkillPointsPerLevel` (1) for each level crossed, plus a `SkillPointMilestoneBonus` (1) on every `SkillPointMilestoneInterval`th level (5, 10, 15…) — summed per level so a multi-level jump still hits every milestone in between. It's applied everywhere a hero's level can move: a battle-end level-up, hard-won XP kept on a defeat/retreat, a guest who survives joining the roster, and a Summon Circle recruit arriving above level 1. Save shape bumped to **v2**: `SaveMigrations` backfills each existing hero once with the points they'd already have earned up to their current level, so no save is retroactively shorted by the mechanic not existing yet; re-migrating a current-version save does not grant them twice. *(Tests: `Leveling_up_in_battle_grants_skill_points_including_the_milestone_bonus`, `Hard_won_xp_on_defeat_still_grants_skill_points_when_it_crosses_a_level`, `Migrating_a_v1_save_backfills_skill_points_once_for_heroes_already_past_level_1`, `A_guest_who_survives_a_victory_joins_the_roster`, `Full_loop_battle_commit_then_every_vendor`.)*

### 15.2 Persistence flow

`App/Services/SaveStore`: `slot1.json`…`slot3.json` under `persistentDataPath/Saves` (or `-saveRoot`), written atomically (temp file + `File.Replace`). `GameApp.Persist` writes:

| Trigger | Saved? |
|---|---|
| New game in a slot | yes |
| Every successful Camp action (buy, sell, craft, repair, equip, learn, assign, party, summon, bounty, inn, camp item) | yes, immediately |
| Settings change | yes |
| Story crawl first shown | yes |
| Battle end (victory, defeat, retreat, escape) | yes, after `BattleFactory.Commit` |
| Save & Quit | yes |
| Mid-battle | no (battle state is not saved; quitting mid-battle loses the battle only) |

**Amended 2026-10-20 (GG-T146):** `SaveStore.Write` never throws. A failure at any step (disk full, a locked file, permissions) is caught, logged as `[Save] slot N write failed: …`, best-effort-cleans the `.tmp` file, and is reported to the caller via a `bool` return instead of losing progress silently — mirroring how `Load` reports a bad read with a log plus a `null` sentinel rather than an exception. `GameApp.Persist` shows the "Couldn't save!" toast whenever `Write` returns `false`. *(Tests: `SaveStoreTests.Write_WhenTheTempFileCannotBeWritten_ReturnsFalseInsteadOfThrowing`, `Write_WhenTheReplaceStepFails_ReturnsFalseAndLeavesThePreviousSaveIntact`.)*

### 15.3 Ability bar and Bestiary

- `HeroSave.Known` lists learned ability ids; `HeroSave.Bar` the five bar entries. The battle reads the bar through `BattleFactory.SpecFor`. *(Tests: `Save_round_trips_through_json`, `Full_loop_battle_commit_then_every_vendor`.)*
- Bestiary: **Seen** for every class spawned or scanned, **Scanned** for Scan targets — recorded at commit even on defeat; **Defeated** counts kills on victory.

---

## 17. Workflow Discipline

**Amended 2026-10-09:** "Code-Only Workflow" is now literal — there is no editor-side authoring at all.

- Never ask the user to open the Unity Editor to place, drag or wire anything. A screen is C# in `App/Screens` or `App/Battle`; a rule is C# in `Core`; content is JSON.
- Rules change in `Core` first, with a test in `Tests/Core.Tests`; the view adapts to events.
- Never force-close Unity; `tools/unity.ps1` refuses to run while the project is open — ask the user to quit the Editor cleanly.

### 17.1 Common pitfalls

1. **Deciding rules in the view.** If `BattleScreen` needs to know an outcome, ask `BattleEngine` (`PreviewMove`, `GetSlotStatus`, `ValidTargets`, `ActionsUntilTurn`) — never re-implement.
2. **Non-determinism in Core.** No `System.Random`, `DateTime`, `UnityEngine.Random` or dictionary-order dependence in rules; the replay test catches it.
3. **Mutating state in a preview.** Previews run on `BattleState.Clone()` with `PreviewOnly` (no events, no traps/wake-ups); keep it that way.
4. **Missing glyphs.** A new symbol in UI text must pass `check_glyphs.py`.
5. **Unknown JSON fields.** `MissingMemberHandling.Error` fails the load — add the field to `ContentModels.cs` first.
6. **PowerShell 5.1 encoding.** Never round-trip `.cs`/`.json` through `Get-Content`/`Set-Content` (BOM-less UTF-8 mojibakes); use an editor or .NET `WriteAllText` with `UTF8Encoding(false)`.
7. **Playback stalls.** Every animation runs through `Safe()`, the final `SyncAll()` snaps views to truth, and a 20 s watchdog resyncs — don't bypass them.
8. **Worktree package resolution (GG-T159).** `Packages/manifest.json` pulls `com.mindattic.capture`/`com.mindattic.telemetry` via `file:../../MindAttic.Capture(/Telemetry)/unity/...`, two levels above the project root. In the main checkout that lands on the real sibling repo; in a per-ticket worktree (`<WorkspaceRoot>.worktrees\Unity.GridGame\<Ticket>\`) the same climb instead lands on `<WorkspaceRoot>.worktrees\Unity.GridGame\<Repo>`, which has no repo of its own. `tools/unity.ps1` now auto-creates a directory junction there (pointing at the real sibling repo, found by walking up to the nearest `*.worktrees` ancestor) the first time it's missing, so `tools/build.ps1`/`tools/test.ps1 -Compile` work in a fresh worktree with no manual setup; junctions, not symlinks, because creating a symlink needs admin/Developer-Mode privilege on Windows and junctions don't.

### 17.2 Cadence

- Keep going until the feature works end-to-end before committing.
- `pwsh tools/test.ps1` after every rules/content change; `-Compile` after App changes (Editor closed).
- `pwsh tools/build.ps1` then `python tools/harness.py smoke` / `sweep` for anything visible.

### 17.3 Checkpoint recipe

```
1. pwsh tools/test.ps1              → all tests + glyph check green?
2. App touched? pwsh tools/test.ps1 -Compile → zero "error CS"?
3. New rule? docs/BIBLE.md updated (§32) and a test named for it?
4. Visible change? build.ps1 + harness.py smoke/sweep → screenshots in artifacts/, layout audit clean?
5. USER_STORIES.md status + evidence updated?
6. Commit once.
```

### 17.4 Naming conventions

`*Def` = a JSON content row · `*Event` = something that happened · `*Command` = a player decision · `BattleRunner.<Concern>.cs` = one rules concern per partial file · static classes in `Meta` = Camp services returning `Result` · `*Screen` / `HubPage` = a UI Toolkit page · `*View` = presentation of a Core object (`BoardView`, `UnitView`).

### 17.5 File-edit cookbook

| Goal | File(s) | Remember |
|---|---|---|
| Tune a battle number | `Core/Battle/BattleConfig.cs` | update the bible table; run soak |
| Change a formula | `Core/Rules/Formulas.cs` | §13.1 |
| Add a spell / skill | `abilities.json` (+ a class's `Abilities`/`DefaultLoadout`) | §7 row; `CatalogTests` covers it automatically |
| Add a buff | `buffs.json` (+ a new knob in `BuffDef` and its hook in `BattleRunner` if needed) | §8 |
| Add an enemy | `classes.json` + a `stages.json` wave (+ `droptables.json`) | thumb in `Resources/Thumbs` |
| Add an item / recipe | `items.json` / `recipes.json` | `Every_recipe_is_craftable_with_its_inputs` |
| Tweak AI weights | `Core/Rules/EnemyPlanner.cs` | §14.1.2 |
| Add a Camp service | `Meta/MetaServices.cs` + a screen in `App/Screens/HubScreens.cs` + hub row + `StartScreens` id | `MetaLoopTests`, harness sweep list |
| Add a save field | `Meta/SaveData.cs` + `SaveMigrations` (bump `CurrentVersion` when shape changes) | round-trip test |
| Add a battle animation | `BattleScreen.Playback.cs` `Animate` switch | awaited beat, `Fx` for motion |
| Add a sound | `Resources/Audio/Sfx` + `Audio.Cue` | Credits.txt attribution |

---

## 18. Glossary

The L0 [§9 Glossary](#GG-§9) is canonical; additional terms: **Armored** (snake member taking 0 while a later member lives) · **Escape** (Smoke Bomb exit; wounds kept) · **Control return** (end of an action: hero buffs/cooldowns and enemy Frozen tick) · **Interrupt outcomes** (*Clutch* → resolve now, *Shrug* → no delay, *Stagger* → delay accumulates, *Cancel* → delay ≥ cast time, orbs lost) · **Passive** (always-on class ability: Double Attack, Counter Attack) · **Humanoid** (may be a pincer attacker; heroes always) · **Coins** (gold picked up in battle, banked on victory) · **Materials** (crafting inputs) · targeting axes **Shape / Mode / Filter** ([§5](#5-targeting)).

---

## 19. How to Add a New Spell — Checklist

1. Add a row to `abilities.json`: `Id`, `Name`, `Description`, `Kind: "Spell"`, `Cost` letters, `CastTime`, `Shape`/`Radius`/`Mode`/`Filter`, `Motion`, `DamageType`/`BaseDamage` or `BaseHeal`, optional `ApplyBuff`/`ApplyBuffChance`/`RemovesDebuffs`/`HastenU`, `Icon`.
2. Every hero can now **learn** it at Camp (general spell catalog, [§25.6](#256-abilities)); add it to a class's `Abilities`/`DefaultLoadout` to make it signature or starting.
3. New debuff? Add it to `buffs.json` (letter + color).
4. Add `Resources/Icons/<Icon>.png`.
5. `pwsh tools/test.ps1` — `CatalogTests.Every_hero_ability_resolves_against_a_live_board` exercises it.
6. Add the §7 row.

### 19.1 Worked example: "Quake"

Brief: pick a tile, hit a 3×3 square of enemies for 16 Physical, no debuff.

```json
{ "Id": "quake", "Name": "Quake", "Description": "The ground heaves under a 3×3 area.",
  "Kind": "Spell", "Cost": "RB", "CastTime": 1.5,
  "Shape": "Square", "Radius": 1, "Mode": "PickTile", "Filter": "EnemyOnly",
  "Motion": "None", "DamageType": "Physical", "BaseDamage": 16, "Icon": "Quake" }
```

Learn price: 120 + 80 × 2 orbs = 280 G. What you don't touch: the dispatcher, targeting UI, cast icons, bank, HUD — data only ([GG-LAW-5](#GG-LAW-5)).

---

## 20. How to Add a New Enemy — Checklist

1. Add a `ClassDef` to `classes.json` with `Team: "Enemy"`, `Tags` (`Humanoid` if it may pincer), `BaseStats`, `Growth`, `Resistances`, `XpReward`, `MoveRange`, optional `DropTable`, `ChargeAbility`/`ChargeShape`, `Trap`, `BossPhases`, `Footprint`, `SnakeSegments`, `Lore`.
2. Add `Resources/Thumbs/<id>.png` and `Resources/Portraits/<id>.png` (or run `tools/import_art.py`).
3. Put it in a `stages.json` wave; optionally add a `droptables.json` table.
4. `pwsh tools/test.ps1` — validation, catalog and soak include it automatically. The Bestiary lists it (silhouette until seen).

### 20.1 Worked example: "FrostWolf"

A Beast `{"Id":"frostwolf","Team":"Enemy","Tags":["Beast"],"Resistances":{"Ice":0.0,"Fire":2.0}, "XpReward":25,"MoveRange":1,"DropTable":"wolf00", …stats}` never pincers; `EnemyPlanner` closes distance and melees; coins default to ~½ XP; the Bestiary shows it as a silhouette until seen.

---

## 22. The Macro Loop

```
Title ─▶ (Continue | New Game → slot → CharacterSelect (pick 5) | Load Game → slot)
   ▼
Camp (HubScreen)  ◀══════════════════════════════════════════╗
   │  Adventure ─▶ stage list (newest-first)                 ║
   │  Crucible ─▶ endless waves, keeps everything (§22.10)   ║
   ▼                                                         ║
StoryCrawl (first entry into a theme only)                   ║
   ▼                                                         ║
Battle — clear every wave                                    ║
   ▼                                                         ║
LevelUp — one hero at a time, only when someone leveled ═════╣
   ▼                                                         ║
PostBattle — gold, items, unlocks, bounty ════════════════════╝
             (Continue → Camp · Next stage › → Battle · Retry → Battle)
```

- **Stage = waves.** Clearing a wave spawns the next immediately inside the same action; clearing the last wave is victory.
- **Reward beat.** `BattleFactory.Commit` writes XP, gold, items, kills, unlocks and bounty progress, then the save is persisted and `PostBattleScreen.RouteFrom` opens `LevelUpScreen` first if anyone leveled, else `PostBattleScreen` directly.
- **No Overworld** ([§28](#28-no-overworld)).
- **Test Mode loop (GG-T93):** `-testmode` runs this same loop with the bot at every seat — Camp → Battle → LevelUp → PostBattle → **Camp, where `CampRoutine` spends before every launch** — skipping Title/CharacterSelect/StoryCrawl/tutorial, on a party-scaled save whose enemy level tracks the party's level (see [§6](#GG-§6)).

### 22.1 Per-transition state contract

| From → To | Commit before | Hydrate on arrival |
|---|---|---|
| Camp → Battle | nothing (every Camp action already persisted) | `BattleFactory.Build`: party specs (level from XP, carried HP, weapon + durability, equipped items, bar), inventory counts for item stacks, unlocked slots, recommended level = campaign index + 1 |
| Battle → LevelUp → PostBattle | `BattleFactory.Commit` + `Persist` | `LevelUpScreen` (only if `BattleReport.LevelUps` is non-empty) then `PostBattleScreen` render the `BattleReport` |
| PostBattle → Camp / Battle | already saved | screens rebuild from `App.Save` |

**Commit rules** (`BattleFactory.Commit`) — **Amended 2026-10-09** where marked, **Amended 2026-10-11** (Retreat) where marked:
- Always: deduct consumed items; record Seen/Scanned.
- **Victory**: gold += coins (also `LifetimeCoins`); add item drops; add kills to Bestiary; **every party hero — alive or fallen — gains the battle's full XP total** (not split, *Amended*); HP: level-up → full, survivor keeps wounds, fallen → 1 HP; weapon durability written back (a shattered weapon is removed); stage marked cleared, `HighestClearedStage` raised, next stage named as unlocked; bounty kills recorded.
- **Defeat**: nothing gained but hard-won XP (below); party HP reset to full.
- **Retreat** (*Amended* — no longer a Defeat): keeps hard-won XP plus `BattleConfig.RetreatKeepFraction` (50%) of the battle's gold and item counts so far, floored; party HP reset to full, same as Defeat.
- **Escaped** (*Amended*): nothing gained; wounds kept (fallen → 1 HP).
- **Crucible** (*Amended 2026-10-19*, [§22.10](#2210-the-crucible)): however the run ends — wipe, leave, escape or (in theory) clearing every wave — it **keeps everything**: gold, item drops, the full XP total (level-ups and skill points), kills, bounty progress and gear wear; party HP reset to full. Never touches cleared stages, `HighestClearedStage`, unlocks, `LastStage` or the expedition. `BattleFactory.Commit` hands any `crucible-*` stage to `Crucible.Commit`.

### 22.2 Failure path detail

**Amended 2026-10-11 — hard-won experience:** a defeat now keeps the XP of every foe felled (gold and loot are still lost); the results screen shows "Hard-won experience +N each" and any level-ups. Found with the battle records: with a 20-wave first battle and no reward for losing, a learning player could never get stronger.

- **Defeat** (all heroes down): "DEFEAT", no rewards but hard-won XP, party restored to full, PostBattle offers **Retry** and **Continue**.
- **Retreat** (pause menu → Retreat, confirmed) — **Amended 2026-10-11: FFIV-style partial loss, no longer a Defeat.** Its own `BattlePhase.Retreated` (mirrors how `Escaped` already gets its own phase); `BattleFactory.Commit` keeps hard-won XP plus `RetreatKeepFraction` (50%) of the battle's `Results.Coins`/`Results.Items` accumulated so far, lost otherwise. PostBattle shows "RETREATED" and the partial haul. The pause-menu row and confirm dialog quote the exact kept percentage. Mid-expedition, a retreat still ends the expedition via the normal non-victory path ([§22.5](#225-expeditions)).
- **Escape** (Smoke Bomb): "ESCAPED", no rewards, wounds remain. **Amended 2026-10-09** (new). No permadeath ([§29](#29-open-design-questions)).
- **The Crucible is the exception** (*Amended 2026-10-19*): a wipe, Leave or escape there keeps the whole haul and all XP, party restored to full ([§22.10](#2210-the-crucible)).

### 22.3 Adventure — the stage list

**Amended 2026-10-09 (owner decision):**
- **Unlocked stages only, newest first.** The list shows every unlocked campaign stage in reverse play order, so the frontier is on top; **all locked stages collapse into one teaser row** ("◇ N more stages ahead…", disabled).
- **Stage names are "<Theme> I / II / III"** (e.g. "Green Valley I", "Sandsea Reaches III").
- **Row content:** ★ cleared / ☆ not yet, the name, "Lv N" (recommended level), and a second line "<Theme> · N waves · drops: X, Y, Z" from the wave enemies' drop tables (first 3 distinct items) — the farming hint.
- **Unlock gating:** stage index i is unlocked when `i ≤ HighestClearedStage + 1` (`Progression.IsStageUnlocked`): linear forward, fully open backward; every unlocked stage is replayable.
- **Farming is the point:** cleared stages stay replayable and drop tables differ per enemy.
- A strip above the list shows the active bounty's progress.

**Difficulty curve:** `Progression.RecommendedLevel(i) = i + 1`; spawned enemy levels are `max(authored, recommended)`; test stages are exempt. XP and coins scale with enemy level.

Campaign (`campaign.json`): Green Valley, Sandsea Reaches, Mireholt Fens, Frostmaw Caverns, Veshker Ruins — 3 stages each, **5 hand-authored waves per stage** ([§22.7](#227-the-first-battle), amended 2026-10-12 — every stage normalized to 5, including Green Valley I, which had been 20), generally grouped by enemy family (frogs with frogs, goblins with goblins) rather than mixed. **No two campaign waves are the same encounter** (*added 2026-10-22, GG-T184*): the 19 waves that repeated an earlier wave's foes now name an `encounters.json` group of the same family instead (`WaveDef.Group`), and `ContentLoader.Validate` fails on any repeat. *(Tests: `EncounterCatalogTests`.)* Final boss: the Vampire Lord (`vampire`, level 10, Veshker Ruins III).

### 22.4 Bounty board

**Amended 2026-10-09:** a Camp screen (`BountyScreen`, "Bounties"), not a StageSelect strip. Posted = every `bounties.json` contract not yet claimed (claimed ones never repost). One active slot (`Bounties.Accept` refuses while one is active); kills of the target class count **on victory only** (in the Crucible, on any ending — [§22.10](#2210-the-crucible)), capped at the goal; **Claim** pays gold + item; **Abandon** resets. *(Test: `Full_loop_battle_commit_then_every_vendor`.)*

| Bounty | Target | Kills | Reward |
|---|---|---|---|
| Slime Culling | slime00 | 5 | 200 G + Healing Potion |
| Wolf Pack | wolf00 | 3 | 300 G + Hi-Potion |
| Restless Dead | undead00 | 4 | 400 G + Holy Water |
| Cave Dweller | cyclops00 | 2 | 500 G + Flame Oil |
| The Vampire Lord | vampire | 1 | 2000 G + Revival Charm |

---


### 22.6 Battle records (added 2026-10-11, owner: "record everything so you can learn how to balance and synergize, keep the records of all moves as JSON so that we can build ML around it in the future")

`BattleRecorder` (Core) writes one JSON document per battle, schema v3 (v2 added per-turn skill level / perfect command; v3, GG-T98, added formation-time footprints on PincerFormed and `SpawnedAt` on wave/reinforcement events): `source` (player / bot:greedy / bot:casual / bot:autoplay / player+autoplay), stage, seed, waves, the party (class, role, level, stats, gear, bar), the opening state and events, then every turn — the full state **before** (units with position, HP, buffs, cooldowns, bar, brain, timeline position, casts; pickups, traps, blocks, amplifier, bank, mana charge), the **command**, whether it was accepted, **every event** it produced (type + fields), and a summary after — and finally the outcome (phase, wave reached, actions, coins, XP, kills, items used, heroes standing).
- The game saves every battle (victory, defeat, retreat) to `persistentDataPath/battle-records/` (or `<artifacts>/records` under the harness, or `-records <dir>`).
- `RecordTests.Record_soak_of_the_first_battle` with `GAIDEN_RECORDS=<dir>` writes bot-played battles; `tools/records.py` prints per-wave tables (turns, damage taken, deaths, potions, pincers, chains, mana, pickups, taunts), each role's damage/healing, damage taken by enemy brain, and the most-used abilities. The original 20-wave first battle (since compressed to 5, [§22.7](#227-the-first-battle)) and several rules (skirmisher dart, last stand, taunt valuation, hard-won experience) were tuned from these tables.

### 22.7 The first battle (amended 2026-10-12, owner: superseding "make the first battle 20 rounds" — every stage, including this one, normalized to 5 waves)

Green Valley I is **5 waves**, levels 1 → 5, each wave outweighing the one before it (test `The_first_battle_has_five_waves_of_rising_difficulty`): an ooze opener (slimes), a beast wave (wolves, bat), a frog/toad wave, a goblin-and-bandit skirmish that a captain's soldier squad **reinforces mid-fight** — the flagship example of the delayed-spawn/surprise mechanic ([§14.8](#148-reinforcements-and-eggs-added-2026-10-12-owner-direction)) — and a captain's-squad finale.

**Amended 2026-10-19 (GG-T118, rebalanced for the three-hero start):** the reinforcement is one soldier, not two (wave 4 is five foes, still heavier than the frog wave), and the finale's captain and two soldiers are level **4**, not 5, so levels now run 1 → 4. Measured on Green Valley I alone, 24 seeds, single attempt: the casual bot (randomness 0.5) won 41% with 22% HP left with the old five-hero start; the trio on the old waves won 16% / 11%; probes of the trio with a Lv 4 finale gave 45% / 31%, and with a four-foe wave 4 as well 54% / 37%; this cut sits between them, back at the old five-hero difficulty or a little kinder. The greedy bot wins 100% either way. In `BalanceReport` (up to 4 attempts) the casual bot now clears it 80% of attempts with 61% HP left (five-hero start before: 66% / 48%; trio on the old waves: 30% / 26%).

### 22.5 Expeditions (added 2026-10-10, owner direction)

Owner: "the game should be grouped in sets of battles, where a battle has X battles to fight and each battle has Y waves, so there is a survival element and a reason to hold onto consumables and use mana sparingly because the challenge is going to get bigger."

- Each campaign **theme is an expedition**: its stages are the battles (3 each), fought **in order**; each battle has its waves. `SaveData.Expedition` (`ExpeditionSave`: theme, next battle, carried orbs, coins) tracks the one under way.
- **Starting:** an expedition starts on its theme's first battle (`Expeditions.CanLaunch` / `Begin`); only one at a time; battles 2+ open only as the next step.
- **Between battles:** wounds carry (as always), **leftover mana orbs carry** into the next battle's bank (`BattleSetup.StartOrbs`, minted as "carried"), consumables spent are gone. The caravan's merchant, blacksmith and alchemist stay open — that is why the shops are always there — but the **inn is closed on the road** (`Inn.Rest` refuses; Tents still work).
- **Finishing:** the last battle won completes the expedition: **+50% of its coins as a bonus**, the caravan makes camp and the party rests (full HP), the next expedition unlocks.
- **Failing:** a defeat ends the expedition (the party falls back and recovers); it restarts from battle 1. **Abandon** (Adventure), a voluntary Retreat (in-fight, or the "Retreat early" button below) or escaping also ends it, wounds kept.
- **Amended 2026-10-11 — bailing out early costs nothing already earned:** every battle won in an expedition is paid out immediately by that battle's own `Commit` call — `s.Expedition.Coins`/`CarriedOrbs` are bookkeeping for the completion bonus only, not an escrow. So mid-expedition, `PostBattleScreen`'s button row offers **"Retreat early"** next to **Next battle ›** (shown whenever a next battle is pending): it's a plain `Expeditions.Abandon` with no partial-loss math, forfeiting only the *not-yet-earned* completion bonus. An in-fight Retreat (pause menu) is different — it DOES apply the FFIV-style partial-loss formula ([§22.2](#222-failure-path-detail)), but only to that one battle's own in-progress `Results`; gold from earlier battles in the same expedition is untouched either way.
- UI: Adventure groups battles under "<Theme> Expedition" headers with status ("Battle 2/3 · 4 orbs carried", "★ Completed"); results show "Expedition: battle 1 of 3 won — 4 orbs carry over" and **Next battle ›**; camp shows the expedition strip and "Inn — On the road".
- *(Tests: `An_expedition_runs_its_battles_in_order_carrying_mana_with_the_inn_closed`, `Finishing_an_expedition_pays_a_bonus_and_rests_the_party_while_defeat_restarts_it`, `Retreating_mid_expedition_ends_it_without_touching_already_banked_gold`.)*

### 22.8 Level-up fanfare (added 2026-10-11, owner: "pomp and circumstance... the same tempo and pause that makes video games enjoyable")

`LevelUpScreen` sits between `BattleScreen` and `PostBattleScreen` whenever `BattleReport.LevelUps` is
non-empty (`PostBattleScreen.RouteFrom` decides; skipped entirely otherwise). One hero at a time:
- The portrait slams into place (`Art.Portrait`, the same bombastic reveal `CharacterSelectScreen` uses).
- The XP bar ticks fully through every level crossed in this one battle (fill → "Lv N" pop → reset)
  before a true partial fill for the level actually landed on — a hero who jumped two levels visibly
  climbs through both, not one smooth lerp.
- Stat deltas (`Formulas.StatsAtLevel` before vs. after) reveal one at a time, staggered.
- Auto-advances after a hold, but a tap anywhere or "Skip ›" jumps straight ahead — the same dual-input
  idiom `StoryCrawlScreen` already uses, not a new one.

### 22.9 Difficulty settings (scaffolding, added 2026-10-11, owner direction)

Owner: a Difficulty selection with presets and a Custom mode "so when you read the telemetry you can ML
it and learn what multipliers affect what; for right now just keep everything at 1.0."

- `SettingsSave.Difficulty` (`DifficultySettings`): a `Preset` (`"Easy"`/`"Normal"`/`"Hard"`/`"Custom"`,
  default `"Normal"`) plus six multipliers — `EnemyHp`, `EnemyDamage`, `EnemySpeed`, `RewardGold`,
  `RewardXp`, `ItemDropRate` — all defaulting to, and today permanently pinned at, **1.0 for every
  built-in preset**. Landing on Easy/Normal/Hard resets all six to 1.0; only **Custom** lets the player
  move them (Settings → Difficulty → Custom reveals six more rows, each cycling ×0.5/×0.75/×1/×1.25/×1.5/×2).
- **None of the six multipliers are consumed by `BattleRunner`/`Formulas`/`BattleFactory` yet.** This is
  intentionally pure scaffolding: the selection UI, persistence and telemetry tagging exist now so that
  (a) later balance work has a lever already wired to Settings instead of needing new UI, and (b)
  telemetry sessions are already tagged with the active preset and every multiplier
  (`difficultyPreset`/`diffEnemyHp`/`diffEnemyDamage`/`diffEnemySpeed`/`diffRewardGold`/`diffRewardXp`/
  `diffItemDropRate` tags on every `TelemetryUnity.Begin`, `BattleScreen.Build`), so once the multipliers
  do something, there's already a history of battles to learn from.
- **Future direction, not yet built:** higher difficulty should mean better rewards (`RewardGold`/
  `RewardXp`/`ItemDropRate` scaling up as `EnemyHp`/`EnemyDamage`/`EnemySpeed` scale up) — this amendment
  only lands once the six multipliers are actually wired into combat and loot math.

### 22.10 The Crucible (added 2026-10-19, owner direction, GG-T95)

Owner: "a way to gain xp and loot for the saved campaign … a waiting for the bus kind of mode."

A quick, endless survival battle fought from camp **inside a normal saved campaign** — a Camp menu row, not a separate game mode (`Meta/Crucible.cs`, `CrucibleScreen`, `CrucibleLauncher`).

- **Entry.** Camp → **Crucible** row (right under Adventure; its value reads "Endless waves" before the first run, then "Best: N waves") → `CrucibleScreen` (what it is, first-wave level, the families you'll face, the record) → **Enter the Crucible** (`crucible_enter` cue). **Closed mid-expedition**: the row is disabled and reads "Closed on an expedition" (`Crucible.CanLaunch`) — the run's full-HP reset would otherwise heal the party between an expedition's battles for free ([§22.5](#225-expeditions)). It also refuses with no party or no foes met.
- **One battle, many waves.** A run is one generated `StageDef` (`crucible-<seed>`, registered in the in-memory `ContentDb` only, earlier runs' stages pruned) of `Crucible.MaxWaves` (50) waves, fought by the ordinary `BattleEngine` wave loop — no new battle loop. Because it is one battle, **wounds, mana and consumables carry wave to wave** and there is no camp in between. Effectively endless: levels climb every wave, so the party falls (or leaves) long before wave 50; clearing all 50 would be a victory, committed the same way.
- **Escalation.** Wave *i* (0-based) spawns at **`PartyLevel + i × LevelStep`** (`LevelStep` 1; `PartyLevel` = the active party's rounded mean level), with `2 + i/3` foes capped at 5. The levels are authored per spawn, so `BattleFactory.Build` gives a Crucible stage `RecommendedLevel` 1 and no New Game+ step — neither the campaign curve nor NG+ lifts them further.
- **Encounter groups** (*added 2026-10-22, GG-T184*). Each wave is one of that family's **authored groups** from `encounters.json` (its mix on its formation tiles), the largest that fits the wave's foe count; no group repeats in a run until every same-size group of its family has had its turn (`EncounterCatalog.Pick`). A family with no group that size tops up with random foes of the family. The endless generator (`EncounterGenerator`, GG-T55) builds its waves the same way.
- **Families.** Each wave is **one enemy family** (`ClassDef.DropTable`, house convention), never the same family twice in a row, cycling through a fresh shuffle of every family the save knows: enemies it has **met** (Bestiary seen/scanned/defeated) or that fight in **the campaign so far** (cleared stages plus the next one). Bosses (`BossPhases`/`Boss` tag) and multi-tile foes stay in their own stages. So the drops are the mats this save is already crafting with. The arena (background, music, blocks) is one of the campaign-so-far stages.
- **Seeded and deterministic.** `Crucible.SeedFor(save)` mixes the save's creation time with `CrucibleSave.Runs`: the same save replays the same gauntlet (and the same battle RNG) until it fights it, and each run after is new. `Crucible.Generate(content, save, seed)` is pure.
- **Leaving.** The pause menu's Retreat row becomes **Leave the Crucible (keep all)**, confirmed with the wave count; a Smoke Bomb escape counts the same.
- **Rewards — the exception to [§22.2](#222-failure-path-detail).** Unlike any other non-victory end, a Crucible wipe or retreat **keeps all the gold, drops and XP earned so far** (that is the point of the mode), then restores the party to full HP like a defeat. Commit rules in [§22.1](#221-per-transition-state-contract); AMENDMENTS 2026-10-19.
- **Record.** `SaveData.Crucible` (`CrucibleSave`: `Best`, `Runs`, `TotalWaves`). Waves survived = waves fully cleared (the index of the wave the party fell or left on). The Camp row shows the best; a better run sets it, a worse one never lowers it.
- **Presentation.** HUD reads "Crucible wave N" (no total); each wave's banner reads "Crucible wave N · Lv L" with a rising-pitch `crucible_wave` sting. Results: **CRUCIBLE**, "Survived N waves", "New best!" (with the `crucible_best` fanfare) or the standing best, then the whole haul (gold, XP each, items, bounty); **Continue** → camp, **Again ›** → the next run. Level-ups play first as after any battle.
- **No endless stalemates** (*added 2026-10-19, GG-T123*). A run must always end by itself, for the bot and for a player alike. Two stalls were found by the bot soak and fixed in the rules (they apply to every battle, not only the Crucible):
  - **A lone hero is never fled.** A pincer needs two heroes, so one hero left standing can hurt foes only with its bar. Against a lone hero (`EnemyPlanner.LoneHero`) no foe makes a wounded retreat and every evasive brain (Commander, Trapper, Kiter, Medic) makes its last stand and an Ambusher stops lying in wait (§14.6) — they close in, so the hero either wins with spells, items and skills or falls. (Before: a wounded Flying Monkey kited the last hero round the board forever; a Scorpion ambusher waited forever for a lone hero that never came within 2 tiles.)
  - **Howl only hurries a striker.** A pure-haste support charge (Howl: no buff, no heal) is cast only when another ally in its square is already adjacent to a hero (or in a ranged foe's reach); otherwise the wolf moves. (Before: a scattered pack howled every turn, each howl hastening only the howler into its next howl, and never closed in.)
  - **The bot keeps a lone hero productive** (`HeroBot`): it spends mana and items on damage freely (cost penalty cut), closes in on the foes to bring its bar to bear, and guards (Shield) rather than wandering when nothing is worth doing.
  A player who would rather not finish a long fight still has **Leave the Crucible (keep all)**. *(Tests: `Bot_soak_over_a_crucible_run` — now deterministic, its save's creation time pinned since `Crucible.SeedFor` mixes it in — and `Crucible_bot_soak_never_stalls_across_many_saves`, 48 gauntlets fought to a wipe under the action cap.)*
- Bounty kills count on any Crucible ending (it keeps every kill), unlike a stage's victory-only rule ([§22.4](#224-bounty-board)).
- The first-battle tutorial never runs in the Crucible.
- *(Tests: `CrucibleTests` — `Crucible_waves_escalate_one_family_at_a_time_from_the_party_level`, `Crucible_draws_only_families_the_save_has_met_or_reached`, `Crucible_is_deterministic_per_seed_and_per_save`, `Crucible_spawns_at_the_authored_level_even_on_new_game_plus`, `Crucible_cannot_start_mid_expedition_and_says_why`, `A_crucible_wipe_or_retreat_keeps_every_reward_and_restores_the_party`, `A_worse_run_never_lowers_the_best_record`, `A_crucible_run_never_touches_campaign_progress_unlocks_or_the_expedition`, `A_crucible_wipe_keeps_the_gear_wear_it_caused`, bot soak `Bot_soak_over_a_crucible_run`. Harness: `python tools/harness.py crucible [WAVES [RUN]]` — camp → Crucible → Enter → Auto → leave after WAVES (99 = fight to a wipe) → level-ups → results → camp, asserting the haul, XP, full HP, the record and the untouched campaign.)*

## 23. Character Classes

All classes share `classes.json`; `Team` separates heroes (62) from enemies (110). **Amended 2026-10-09:** the spawn side decides the team — a hero class placed in a stage wave fights as an enemy (the Vampire Lord is the `vampire` class, which is also recruitable).

### 23.1 Class identity

Base stats, per-level growth, milestone bonuses, tags, resistances, learnable `Abilities`, `DefaultLoadout`, lore/card text, portrait crop. No dialogue.

### 23.2 Hero classes with seeded kits

| Class | Identity | Stat lean | Color | Kit |
|---|---|---|---|---|
| **Cleric** | white-magic healer | INT/WIS | **W** | Heal, Heal, Frost, Potion |
| **Paladin** | tank with heals | STR/VIT | **W** | Heal, Fireball, Potion |
| **Barbarian** | brute front line | even 12s | **R** | Fireball, Bolt, Potion |
| **Alchemist** | consumables + control | INT/WIS | **G** | Frost, Potion, Steal, Sleep Dart |
| **Assassin** (id `assassain`) | flanker / rogue | AGI/SPD/LCK | **B** | Steal, Mug, Bolt, Potion |
| **GreenNinja** | mobility thief | AGI/SPD/LCK | **G** | Teleport, Steal, Fireball, Potion |
| **RedNinja** | mobility striker | AGI/SPD/LCK | **R** | Teleport, Mug, Bolt, Potion |

Every other hero class has color **U** (Blue, the default) and the default kit (Heal, Fireball, Frost, Bolt, Potion). A new save's roster is **Paladin, Barbarian, Cleric** (`Progression.StartingRoster`).

#### 23.2.0 Signature moves + design rationale

| Class | Signature | Should feel like | Anti-pattern |
|---|---|---|---|
| Cleric | sustain (Heal/Mass Heal/Antidote) | "I keep the party alive when it shouldn't be." | stuck front-line |
| Paladin | tank + self-patch | "I body-block and patch myself." | out-damaging the Barbarian |
| Barbarian | Fireball + Bolt | "I delete the front rank." | slow to set up |
| Alchemist | stacks + Steal | "I never run out." | out-damaging mages |
| Assassain | Mug | "Every action I hit and harvest." | too tanky |
| GreenNinja | Teleport into a flank | "I'm in position a move sooner." | consequence-free teleport |
| RedNinja | Teleport + Mug | "I'm in their backline first." | same as Assassain |

#### 23.2.1 Class identity rules of thumb

One thing it does best plus a secondary cope; lopsided stats; colors shape the bank (W/W/W vs W/B/R); abilities reference identity.

#### 23.2.2 Classes without distinct kits

**Amended 2026-10-13 (superseded, was already stale):** this table predates a kit pass that gave every one of the 62 hero classes a bespoke signature move (`<classid>_<move>` in slot 3, e.g. `paladin_bulwark`, `myrmidon_phalanx`, `whitewitch_ward`) — verified against `classes.json`, 0 classes run the flat default kit described above. A further pass the same day grouped each role into 3-5 sub-archetypes (e.g. Tank: Aegis Guard / Juggernaut / Warlord / Deathguard; Healer: Aegis Wardens / Hexmenders / Skirmish-Menders; see `tools/icons.json`'s `ability` map for the full id list) and gave each cluster two more bespoke moves (slots 1 and 4), so every class now has 3 of its 5 slots archetype-specific — only slot 0 (the role's baseline action, e.g. `taunt`/`heal`/`cleave`) and slot 2 (`item:x_potion`) stay shared, by design (`SynergyTests.Every_hero_has_a_role_and_kits_match_it` asserts the baseline survives). A parallel `Universal` flag on `AbilityDef` (12 new Skills) lets any hero of any role learn a class-agnostic move via `Abilities.Learnable`, alongside the pre-existing rule that every Spell is already universally learnable.

### 23.3 Enemy classes

110 enemy classes, 47 used in the campaign. Enemies use only `ChargeAbility`, `Trap`, `BossPhases`, `SnakeSegments`, `Footprint`, `MoveRange` and stats; their `Abilities` list is not used.

### 23.4 Roster + party composition

`SaveData.Roster` holds every recruited hero; `Party` the active squad, **max 6** (`Progression.MaxPartySize`; **amended 2026-10-10**, owner: "up to six party members" — the board has six hero start tiles; Camp shows them in a 2-column grid). `Party.Toggle` moves a hero between party and reserve, refusing a full party and refusing to remove the last member (**Amended 2026-10-09**). XP lives on the hero, so it survives moves.

---

## 24. Equipment, Items, Materials, Currency

### 24.1 Item types

`Equipment` (weapon, armor, relic), `Consumable`, `CraftingMaterial`, `QuestItem` (never sold). Gold is not an item.

#### 24.1.1 Rarity tiers

`ItemRarity`: Junk, Common, Uncommon, Rare, Epic, Legendary (content: 3 / 38 / 45 / 37 / 24 / 20). Names render rarity-colored (`Ui.RarityClass`).

| Rarity | Cost guide | Stat range | Source |
|---|---|---|---|
| Common | 1.0× | +1–3 | most enemies |
| Uncommon | 2.5× | +3–6 | mid enemies, forge |
| Rare | 6× | +5–10 | elites, upgrades |
| Epic | 15× | +8–15 | bosses, enchant |
| Legendary | 40× | +12–25 | end-game |

Relic/Epic+ named passives (`PincerDamageBonus`, `RegenPerTurn`, `StartsProtected`) and weapon-upgrade-line named passives ([§24.6.1](#2461-tier-ladder-gg-t35-amended-2026-10-15), GG-T47) are both built.

### 24.2 Equipment slots

Weapon, Armor, Relic I–III. `Equipment.Fits` (weapon/armor by slot; any relic in any relic slot), `Equip` (takes one from the bag, returns the old piece), `Unequip`, `Compare` (stat preview).

#### 24.2.1 Proficiency gating (GG-T52)

Every weapon carries a `WeaponClass` (`Simple`/`Martial`/`Arcane`) and every Head/Torso/Legs/Gloves/Boots piece an `ArmorWeight` (`Light`/`Medium`/`Heavy`); every hero can always use Simple weapons and wear Light/Medium armor regardless of class. `ClassDef.WeaponProficiencies` (which non-Simple classes a class starts trained in) and `ClassDef.ArmorProficiency` (the heaviest armor it starts able to wear, default Medium) set each class's out-of-the-box reach: the Tank archetype (Guardian tree) starts at Heavy armor; Warrior/Guardian/Ranger start with Martial weapons; Mage starts with Arcane; Cleric and Alchemist start Simple-weapons-only (daggers, maces) until they earn more.

`Progression.EffectiveWeaponProficiencies`/`EffectiveArmorProficiency` layer the class default together with any proficiency-unlock passive already in `HeroSave.Known` — `martial_weapons_proficiency` ("Martial Weapons"), `arcane_focus_proficiency` ("Arcane Focus") and `heavy_armor_proficiency` ("Heavy Armor Proficiency"), the same three passives §25.6's Build screen already reads via `Progression.KnownProficiencies` (staged by GG-T74). They're real skill-tree nodes today: **Mage** can unlock Heavy Armor Proficiency (so a mage can earn the right to wear armor — the ticket's own example), **Cleric** can unlock Martial Weapons, and **Alchemist** can unlock Arcane Focus, each a late node (`RequiredPointsInTree: 8`) in its tree. Actually spending a point on *any* tree node (these three included) is still the deferred follow-up noted in §25.6/§25.6.1 — until that lands, a passive only enters `Known` via a class's starting kit (none do yet) or the migration grandfather clause below.

`Equipment.ProficiencyGate(c, h, item)` returns null if `h` can equip `item`, else the unlocking passive's name; `Equipment.Fits(c, h, item, slot)` (an overload of the shape-only `Fits(item, slot)`) folds that check in, and `Equipment.Equip` refuses with `"Not proficient — needs {gate}"` without consuming the item. The Equip screen's bag listing still calls the shape-only `Fits`/`Candidates` so a piece the hero owns but can't wear stays visible — shown disabled with a 🔒 and the gate's name in place of its count — instead of silently vanishing; `Equipment.Candidates(c, s, slot, h)` (the hero-aware overload) is what the camp bot and shop-browsing code use instead, so it never buys or crafts toward gear it can't equip. A save written before this landed may already have a hero wearing something ungated at the time — `SaveMigrations.Migrate` auto-grants the matching proficiency for anything already equipped, so nobody's gear is stripped out from under them. *(Tests: `LoadoutAndProgressionTests` — effective proficiencies by class default and by passive, `ProficiencyGate`/`Fits`/`Equip`/`Candidates` behavior, the migration grandfather clause.)*

#### 24.2.2 Weapon roster (GG-T181)

**100 unique base weapons** — the 13 originals plus 87 more, authored as one table in `tools/weapons.py` (re-run it to re-tune, then `python tools/make_icons.py --missing` for any new icon). 30 Simple (clubs, sickles, slings, quarterstaves, knives, javelins, rods, lanterns, chakrams… — the Healer/Support kit, since those classes start Simple-only), 40 Martial (halberds, rapiers, katanas, glaives, whips, crossbows, flails, scythes, tridents… — Striker/Tank/Ranged) and 30 Arcane (wands, tomes, orbs, scepters, totems, staves — Mage/Support/Healer), Common through Legendary. Each has its own stat profile, and many a base trait from fields the engine already applies to any equipped item: `PincerDamageBonus`, `RegenPerTurn`, `StartsProtected`, `BattleStartManaOrbs`, `BankCapacityBonus`, `Resistances`, or an **on-hit trait** — a low-chance `PincerApplyBuff` or a small `LifestealFraction` named on every step via `PassiveName` (whose `+3` passive is then the same mechanic, stronger). Every weapon has a source: Common basics are Merchant stock (Common only, GG-T84), most are Forge recipes (gold ≈ 40% of base cost, Apprentice for Common/Uncommon, Journeyman for Rare, Master for Epic/Legendary, inputs themed on the enemy family whose trophies they use), and some are enemy drops (`droptables.json`, 0.8–6% by rarity — the Demon Lord's are the Legendary ones). Every base gets the originals' generated variants: `+1/+2/+3` (Strength and Intelligence +2/+4/+7, cost +15/+30/+50, a named passive at `+3`) and one per enchant (enchant `Bonus` added, cost +120, rarity one step up). Each weapon type has its own game-icons.net silhouette and each base its own palette (`tools/icons.json` `weapon_palette`, shared by its variants). *(Tests: `WeaponRosterTests` — 100 bases across all three classes and every rarity, distinct profiles, a shop/forge/drop source for each, a full upgrade line and enchant set for each, an icon for every weapon row; `EconomyLoopTests`; `Every_weapon_upgrade_lines_top_step_has_a_named_passive`.)*

#### 24.2.3 Armor sets (GG-T182)

`armorsets.json` holds **20 named five-piece sets — 100 armor pieces** (one Head/Torso/Legs/Gloves/Boots each, all of the set's `ArmorWeight`): 7 Light, 6 Medium, 7 Heavy, pitched from campaign level 1 (Wayfarer's, Light) to 25 (Dreadwyrm, Heavy), a new set at most every five levels, rarity Common → Legendary with the level. `ArmorSetDef{Id, Name, Weight, Level, Description, Pieces[5], Bonuses[]}`; each `SetBonusDef{Pieces, Stats, Resistances, PincerDamageBonus, RegenPerTurn, BattleStartManaOrbs, StartsProtected}` is active while at least `Pieces` distinct pieces of the set are worn. Every set has a **2-piece** bonus (its signature stat) and a **5-piece** bonus (two stats plus a perk: an element resistance, Pincer damage, Regen, battle-start orbs or starting Protected). `ContentDb.SetByItem`/`TryArmorSetOf` maps a piece to its set; `ContentLoader.Validate` rejects a set without exactly one piece per armor slot, a piece of the wrong weight or in two sets, or a bonus tier outside 2–5.

Resolution is pure Core (`Core/Rules/ArmorSets`: `Progress`, `ActiveBonuses`, `BonusStats`, `Describe`). `BattleRunner.SpawnHero` folds the active tiers into the hero's `GearStats`/`GearResist`/`PincerDamageBonus`/`RegenPerTurn` (and battle-start orbs/Protection), tracking them in `Unit.SetBonuses`; when a piece **breaks** mid-battle (§24.5) the tiers it was holding up are taken back out and the feed reads "<Set> set bonus lost". Because `Progression.Preview` runs the same spawn, the Status totals and the Equip compare grid include set bonuses automatically.

Sources — every piece has at least one: **Merchant** (only Common pieces: Wayfarer's head/gloves/boots, Militia head/torso/boots, Bramblehide and Gutterscrap boots), **Forge** recipes (base leather/iron/cloth plus the trophies of the enemies the set is themed on; Apprentice up to level 6, Journeyman to 14, Master above; gold ≈ 40% of cost), and **drops** (the remaining pieces, 4–5% from the themed enemy families' tables, 10% off the Demon Lord). The 23 older unsetted armor pieces stay as they were.

UI: the **Status** screen's "Armor Sets" window lists each set the hero wears a piece of ("Wayfarer's set 3/5") with every tier, ✔ and green once reached; the **Equip** screen's compare panel shows the set of the current and previewed piece as one line ("Wayfarer's set 4/5 → 5/5 · ✔ 2 pieces: … · ✔ 5 pieces: …"); item details (Items, Shop, Forge) name the set, its weight and level, and both tiers. *(Tests: `Tests/Core.Tests/ArmorSetTests.cs`.)*

### 24.3 ItemDef fields

```
Id, Name, Description, Type, Rarity, Slot, WeaponType, WeaponClass, ArmorWeight, BaseCost,
SellValue (−1 = BaseCost/2), MaxStack, Durability, BaseHealing, BaseDamage, DamageType, Stats{8},
BattleStartManaOrbs, OnUseAbility, GrantsOrbs, CuresBuff, CuresAll, Revives, Escape, OutOfBattleOnly,
BonusVsTag, BonusMultiplier, Resistances{type→mult}, SoldByVendor, Icon
```

### 24.4 Inventory

`SaveData.Inventory` — item id → count, per save slot; `AddItem` removes zero entries. **Amended 2026-10-10:** durability *is* per inventory instance — `SaveData.Worn` (item id → its worn copies, each a `WornGear{Durability, Repairs}`) remembers the wear and repair count of a piece that leaves a hero's slot, instead of resetting it; see §24.5.

### 24.5 Weapon durability

- A hero's equipped weapon loses **1 durability per pincer it attacks in** (not as a supporter); items with `Durability` 0 never wear. Content weapons have 80–250 durability.
- When a pincer starts with the weapon at 1, that attacker's strikes in that pincer deal **×1.5**; then it wears to 0 and **shatters**: the wielder takes `round(15% MaxHP)` (min 1, "Shatter!", environmental), the weapon's stats leave `GearStats`, and **(amended 2026-10-15, GG-T41)** the weapon becomes a Broken fragment in the save at commit instead of being removed outright — see Broken fragments below.
- **Repair** (`Meta.Repair`): restores to `EffectiveMax = max(1, Durability − repairs)`, then `repairs++` (so each repair lowers the next ceiling by 1); cost `ceil(missing × BaseCost / Durability × 0.30 × 1.6^repairs)`, min 1; flagged uneconomical when cost ≥ BaseCost. *(Tests: `Weapon_wears_per_pincer_and_shatters_at_zero`, `Full_loop_battle_commit_then_every_vendor`.)*
- **Per-instance durability** (closed 2026-10-10, US-246): unequipping a piece that isn't factory-fresh (worn below max, or repaired) files it into `SaveData.Worn` instead of resetting it; `Equipment.Equip` hands out a fresh copy first, else the **least-worn** tracked copy. *(Test: `Unequipped_gear_remembers_its_wear_and_fresh_copies_are_used_first`.)*
- **Armor wears in battle** (closed 2026-10-10): a hero's equipped armor loses **1 durability per landing hit taken** (not DOT/environmental); at 0 it breaks mid-fight — stats and resistances leave `GearStats` immediately with an "Armor broken!" feed line and crack VFX; **(amended 2026-10-15, GG-T41)** it becomes a Broken fragment at commit like a shattered weapon (no shatter damage — that stays weapon-only). Repair works as for weapons. *(Test: `Armor_wears_per_enemy_hit_and_breaks_at_zero`.)*
- **Ability-bar weapon swap** (closed 2026-10-10): a bar slot can hold `weapon:<id>` from the bag; using it swaps weapons (costs a turn, no mana; the incoming weapon enters at factory durability); the save follows the swap.
- **Shatter auto-equip** (GG-T40): if a hero's weapon shatters and a backup is slotted on their bar, it equips for free the moment that hero is next given a command — no turn cost, no player input. *(Test: `Shattered_weapon_auto_equips_the_bar_backup_on_the_hero_s_next_action`.)*
- **Broken fragments (GG-T41):** a shattered weapon or broken armor doesn't vanish — `BattleResults.BrokenWeapons`/`BrokenArmors` (hero → item ids, recorded the instant each shatters/breaks, even if a bar backup is auto-equipped afterward) turns each into a `Broken <Name>` fragment at a Victory commit (`SaveData.Broken`, item id → count), not a whole item returned to the bag. Broken pieces can't be equipped (`Equipment.Candidates` only reads `Inventory`, never `Broken`) or repaired (`Repair` only ever touches a hero's equipped gear); they're listed on the Items screen (All/Gear tabs, "can't be equipped or repaired" note) and the Blacksmith's Salvage tab, and `Crafting.SalvageBroken` scraps one for `SalvageYield(c, item, 0)` materials ([§24.6](#246-crafting-recipes)) plus Blacksmith XP. *(Tests: `A_shattered_weapon_is_recorded_in_results_so_commit_can_turn_it_into_a_broken_fragment`, `A_shattered_weapon_becomes_a_broken_fragment_instead_of_vanishing`, `A_shattered_armor_becomes_a_broken_fragment_instead_of_vanishing`, `A_weapon_that_shattered_and_had_a_bar_backup_auto_equipped_does_not_also_return_whole_to_the_bag`, `Broken_fragments_cannot_be_equipped_and_salvaging_one_grants_materials_and_blacksmith_xp`.)*
- **Telegraph (added 2026-10-15, GG-T39):** each hero's battle card shows a thin weapon/armor wear bar (`BoardView.UnitView.SetGear`); at ≤10% remaining it pulses amber, and at the weapon's last-strike durability of 1 it glows red with a cracked-weapon badge instead. The drag preview calls out an imminent shatter ("This pincer will shatter your Iron Sword!") before the drop commits, for any attacker the previewed pincer would wear to 0 — not just the dragged hero. *(Harness: `python tools/harness.py durability`.)*

### 24.6 Crafting recipes

`RecipeDef` (`recipes.json`): station, inputs, gold, optional `BaseItem` consumed, result. `Crafting.Craft` checks gold, inputs, base item and result stack room atomically.

| Station | Count | Where |
|---|---|---|
| Forge (no base) | 11 | Blacksmith → Forge |
| Forge with base item (upgrades) | 39 | Blacksmith → Upgrade — **Amended 2026-10-09:** the old unused `UpgradeLibrary` now has a UI |
| Alchemy | 14 | Alchemist → Brew |
| Enchant (base weapon + essence + 2 Arcane Dust + gold) | 52 | Alchemist → Enchant |

*(Test: `Every_recipe_is_craftable_with_its_inputs`.)* **Salvage** (`Crafting.Salvage`): break one equipment piece into 50% of its base Forge recipe's inputs (floor, min 1). **Amended 2026-10-15 (GG-T42):** every equipment piece is now salvageable — pieces without a base recipe fall back to a flat materials-by-type-and-rarity table (`Crafting.FallbackSalvage`, keyed by Weapon/Armor/Relic × rarity). Yield also scales with the piece's remaining durability (`Crafting.SalvageYield(c, item, durability)`): a near-dead or Broken piece returns proportionally less (floor), but never less than 1 of the cheapest input. Bag salvage (`Crafting.Salvage`) always eats the worst (most-worn) tracked copy first; `Crafting.SalvageEquipped` lets a hero's currently-equipped weapon/armor be scrapped directly instead of unequipping it first. **Amended 2026-10-15 (GG-T41):** `Crafting.SalvageBroken` scraps one `SaveData.Broken` fragment the same way, at durability 0 ([§24.5](#245-weapon-durability)). *(Tests: `Every_equipment_item_has_a_salvage_yield_via_recipe_or_fallback_table`, `Salvage_of_equipment_without_a_recipe_uses_the_type_and_rarity_fallback_table`, `Salvage_yield_scales_down_with_remaining_durability_but_keeps_at_least_one_of_the_cheapest_input`, `SalvageEquipped_clears_the_heros_slot_and_grants_durability_scaled_materials_instead_of_returning_it`, `Broken_fragments_cannot_be_equipped_and_salvaging_one_grants_materials_and_blacksmith_xp`.)*

#### 24.6.1 Tier ladder (GG-T35, amended 2026-10-15)

Each crafting station has its own level — **Apprentice → Journeyman → Master** — raised by XP from every craft, brew, repair and salvage done there (`StationProgress`, `SaveData.StationXp`; 150/450 total XP for Journeyman/Master). `RecipeDef.Tier` (1–3, default 1 when absent from content) gates `Crafting.Craft` to the station's current level, failing with "Requires Journeyman"/"Requires Master" below it; Repair and Salvage aren't recipes and are always available, but still feed the Blacksmith's XP. Deliberately independent of §25.12 caravan upgrades (gold-bought stat bonuses, never recipe gates) so the two never double-gate the same thing — tested by `Caravan_upgrades_and_station_tiers_never_double_gate_each_other`.

Content assignment: Forge's 11 base (no-`BaseItem`) recipes are all Apprentice; its 39 Upgrade recipes split Journeyman (the first upgrade off a base item) / Master (the deeper `+1 → +2` chains); Alchemy's 14 brews split Apprentice/Journeyman/Master by gold cost; all 78 Enchant recipes are Master (Alchemist-side, alongside Brew). The original "Iron Sword → Steel Sword → Mythril Blade → +1" ladder sketch predates this data shape; upgrades exist as data and UI. *(Tests: `A_station_starts_at_apprentice_and_levels_up_from_real_crafting_actions`, `Repair_and_salvage_grant_blacksmith_xp_but_never_alchemist`.)*

**Named upgrade passives (added 2026-10-15, GG-T47):** each weapon upgrade line's top step (13 originally; all 100 since GG-T181, §24.2.2) (its `+3`, the deepest Master-tier rung) carries a named passive — `ItemDef.PassiveName`/`PassiveDescription` (shown on the Equip compare panel and in `ItemUi.StatsText`) plus one of three data-driven mechanics, all keyed off the wielder's equipped `WeaponId` at combat time (no new Unit fields):
- `PincerApplyBuff`/`PincerApplyBuffChance`: a landed **pincer** strike (not a supporter or counter hit) has this chance to apply the named buff to the target — Flame Tongue's **Flamebrand** (20% Burn), Serpent Spear's **Serpent's Kiss** (25% Poison), Rune Axe's **Rune Frost** (15% Freeze), Shadow Blade's **Duskfang** (20% Blind), Crystal Wand's **Silencing Shard** (20% Silence).
- `LifestealFraction`: heals the wielder this fraction of any damage they land, pincer or not — Bronze Dagger's **Vampiric Edge** (5%), Rusty Sword's **Scrapper's Edge** (3%), Mystic Staff's **Soul Siphon** (5%), Hunter's Bow's **Bloodhunt** (4%).
- `AdjacentAllyDamageReduction`: while this hero stands adjacent to an ally hero, that ally takes this much less damage from any landed hit (the strongest adjacent guardian wins — doesn't stack) — War Hammer's **Guardian's Bulwark** (12%), Starfall Mace's **Celestial Aegis** (10%), Steel Sword's **Steel Bulwark** (10%), Iron Sword's **Ironwall** (8%).

*(Tests: `Flamebrand_applies_burning_on_a_landed_pincer_strike`, `Vampiric_edge_heals_the_wielder_a_fraction_of_damage_dealt`, `Guardian_passive_reduces_damage_to_an_adjacent_ally`, `Every_weapon_upgrade_lines_top_step_has_a_named_passive`.)*

#### 24.6.2 Recipe discovery (GG-T36, amended 2026-10-15)

Recipes start hidden; `RecipeDiscovery.IsDiscovered` reveals one once its station's level reaches its `Tier` (§24.6.1 — `Crafting.IsUnlocked` already derives that from `SaveData.StationXp`, so a tier-reached recipe is discovered by definition with no separate level-up event) or earlier, via `SaveData.DiscoveredRecipes`:
- **Read a recipe scroll** — a new `ItemType.RecipeScroll` item (`ItemDef.TeachesRecipe`) dropped like any other loot; the Items screen's detail pane offers "Learn recipe" (`RecipeDiscovery.Learn`), which consumes the scroll and discovers its recipe outright even if the station isn't high enough level to craft it yet (it then shows 🔒-locked, same as any other discovered-but-locked recipe).
- **First-ever pickup of the recipe's rarest input** — `RecipeDiscovery.RarestInput` picks the input with the highest `ItemRarity` among a recipe's `Inputs` (ties: higher `BaseCost`, then id); `BattleFactory.Commit`'s loot grant calls `RecipeDiscovery.OnItemAcquired` the moment an item's owned count goes from 0 to positive, discovering every still-hidden recipe whose rarest input is that item.

The Blacksmith/Alchemist recipe lists (`HubScreens.RecipeList`) show discovered recipes as before, plus a `???` row for an undiscovered recipe missing exactly one input (`RecipeDiscovery.OneAwayMissing`) naming that missing material — everything else undiscovered is hidden outright. Selecting a `???` row shows the input checklist (materials aren't secret) but not the recipe's name, result or price. Discovery only gates visibility: `Crafting.Craft`'s gold/inputs/tier checks are unchanged. Three scrolls ship in content as rare drops (`scroll_recipe_sword_tempering` off `goblinthug00`, `scroll_recipe_holy_water` off `ghost`, `scroll_recipe_holy_enchant` off `demonlord`). *(Tests: `Tests/Core.Tests/RecipeDiscoveryTests.cs`.)*

#### 24.6.3 Masterwork variants (GG-T46)

Every Forge craft and Upgrade (never Alchemy/Enchant) rolls a chance (`Crafting.MasterworkChance`) at a **Masterwork** result on a Weapon- or Armor-slot item: base 5%, +0.2% per point of the active party's combined Luck (`Formulas.StatsAtLevel` summed over `SaveData.Party`, levels included), +5% per Blacksmith station level above Apprentice ([§24.6.1](#2461-tier-ladder-gg-t35-amended-2026-10-15)), capped at 35%. A hit rolls a random **+10–20% stats** multiplier (`WornGear.Mastery`/`HeroSave.WeaponMastery`/`ArmorMastery`) and a fixed **+25% max durability** (`BattleConfig.MasterworkDurabilityMultiplier`), shown with a ★ in front of the item's name everywhere it's listed (bag, Equip, Status, Repair) and in the craft toast. The roll travels with the specific piece like per-instance durability ([§24.5](#245-weapon-durability)): unequipping remembers it, equipping restores it, and it's gone for good if the piece shatters/breaks or is swapped out for an ability-bar backup (backups always enter at factory spec). *(Tests: `Masterwork_chance_grows_with_party_luck_and_blacksmith_level_and_caps`, `Forge_craft_can_roll_a_masterwork_variant_with_boosted_stats_and_durability`, `Unequipping_a_masterwork_weapon_remembers_its_roll_and_requipping_restores_it`, `Enchant_crafts_never_roll_masterwork_even_though_they_also_touch_weapons`.)*

#### 24.6.4 Reagent potency (GG-T45, owner direction)

A handful of Alchemy recipes carry a `RecipeDef.PotencyInputs`/`PotencyResult`/`PotencyResultCount` on top of their normal `Inputs`/`Result`: brewing with a rarer reagent swapped in for the recipe's key material — e.g. a **Pure Slime Gel** (Rare, a low-chance bonus drop from the slime family's own table, [§24.7](#247-drops)) instead of a plain Slime Gel — yields a stronger version of the same potion instead of the base one, for the same gold cost. `Crafting.HasPotencyInputs`/`Crafting.Craft(c, s, r, potency: true)` check and consume the potency set instead of the normal one; `AlchemistScreen`'s Brew detail shows the greater result and its own reagent checklist and Brew button beneath the normal one whenever a recipe has one. Today: **Greater Healing Potion** (90 HP vs. 50) off `recipe_potion_healing`, and **Greater Antidote** (cures everything, not just poison) off `recipe_antidote` — both gated on the same Pure Slime Gel, so a lucky rare drop has a use beyond unlocking new recipes. Adding another potion's potency variant is a content-only change (no code): give its recipe `PotencyInputs`/`PotencyResult`. *(Test: `Brewing_with_the_potency_reagent_yields_the_greater_result_instead`.)*

### 24.7 Drops

Per enemy `DropTable` ([§13.3](#133-death-and-loot)); 37 tables in content, one per enemy family. The Adventure list hints each stage's drops.

**Amended 2026-10-15 (GG-T43, owner direction):** every enemy family carries its own signature crafting/alchemy material (frogs → Frog Gland, goblins → Goblin Scrap Iron, slimes → Slime Gel, undead → Undead Bone, cyclopes → Cyclops Eye, vampire → Vampire Fang, etc.), matching the house convention that a wave clusters one family ([§22.7](#227-the-first-battle)) — so a player can pick a stage and farm it for a specific ingredient. Remains (gravestones, bone piles) and eggs still drop nothing ([§14.5](#145-remains--gravestones-and-bone-piles-added-2026-10-10-owner-direction), [§14.8](#148-reinforcements-and-eggs-added-2026-10-12-owner-direction)). The Bestiary lists a family's drops as soon as it's **Seen** ([§25.11](#2511-bestiary)). *(Test: `Every_enemy_family_has_a_drop_table_with_a_signature_material`.)*

#### 24.7.1 Material economy

```
ENEMIES ─▶ drop tables ─▶ Inventory ─┬▶ Blacksmith (forge / upgrade / salvage / repair) ─▶ gear
                                     ├▶ Alchemist (brew / enchant) ─▶ consumables, enchanted gear
                                     └▶ Merchant (sell 50%) ─▶ Gold ─▶ buy, craft, learn, summon, inn
```

### 24.8 Specific items

- **Mage Robes** (`eq_armor_mage`, Uncommon): +2 orbs at battle start; **Wizard Robe** (`eq_armor_wizard`, Rare): +3; stacks across the party, capped by the bank.
- **Sleep Dart** (`cons_sleep_dart`, stack 5): casts `sleep` (PickActor) with no mana — the item-casts-a-spell path.
- **Sunfire Amulet** (`eq_amulet_sunfire`, relic): Fire damage taken ×0.7.
- Consumables table: [§4.3](#43-item-stacks-and-effects).

### 24.9 Currency

**Amended 2026-10-09:** coins collected in battle *are* gold. `BattleResults.Coins` is credited to `SaveData.Gold` (and the `LifetimeCoins` ticker) at a **victorious** commit; coins seen in a lost or escaped battle are lost. The old `GoldTracker` bridge is gone. Gold sinks: Merchant, crafting, repair, Inn, learning abilities, Summon Circle. New saves start with **200 G**.

---

## 25. The Camp (merged hub)

**Amended 2026-10-09 (owner decision):** the FF-style **Camp** menu (`HubScreen`) replaces the seven vendor scenes, the `VendorNavBar` and StageSelect-as-home. It is the "merged hub" the old §25.9 deferred.

### 25.0 Camp navigation

`HubScreen`: a party window (portrait, name, level, HP gauge per active hero; tap → Status), a menu, and a side window (Gold, play time, campaign stages cleared). Menu rows, in order: **Adventure** ("Stage N"), **Items**, **Status**, **Equip**, **Abilities**, **Party**, **Merchant**, **Blacksmith**, **Alchemist**, **Inn** (cost or "Rested"), **Summon Circle**, **Bounties** (progress), **Bestiary**, **Settings**, **Save & Quit**. Each sub-screen is a `HubPage` with a header (back, title, gold) over a themed backdrop; Back returns to Camp. Every successful action toasts, plays a cue, persists and refreshes (`HubPage.Done`).

**Amended 2026-10-19 (owner, GG-T119):** the service rows — **Merchant**, **Blacksmith**, **Alchemist**, **Enchantress** (new, §25.3.4) and **Inn** — appear only once that Camp NPC has joined the caravan (§25.0.1); **Talk** (§27.1) sits after Party. A new save's Camp has none of them.

### 25.0.1 Camp NPCs join along the way (GG-T119)

The Merchant, Blacksmith, Alchemist, Enchantress and Innkeeper are people the party meets on the journey, not fixtures of the camp. Each joins at a story beat (§27.1) through a dialogue `JoinNpc` effect, which sets the save flag `npc:<id>` (`CampNpcs.Joined`); until then their Camp service is hidden. Every NPC also has a Camp **Talk** dialogue — from Camp's Talk row, and from a **Talk** button in the header of their own station (`HubPage.Npc`).

| NPC | Service | Joins (story beat) | First launch it plays before |
|---|---|---|---|
| Merchant | Merchant (§25.1) | `greenvalley_road_merchant` — ThemeStart Green Valley | Green Valley I |
| Innkeeper | Inn (§25.3.1), incl. the Medic Wagon | `greenvalley_after_first` — StageCleared Green Valley I | Green Valley II |
| Blacksmith | Blacksmith (§25.2) | `greenvalley_smith` — GearWorn 60%, fallback Sandsea Reaches (§25.0.4) | the first launch after a hero's gear wears below 60% (at the latest Sandsea Reaches I) |
| Alchemist | Alchemist / Brew (§25.3) | `desert_alchemist` — CanBrew, fallback Sandsea Reaches (§25.0.5) | the first launch once the bag holds the loot for a first bottle brew (at the latest Sandsea Reaches I) |
| Enchantress | Enchantress / Enchant (§25.3.4) | `swamp_enchantress` — FirstArmor, fallback Mireholt Fens (§25.0.8) | the first launch after the first armor piece is forged (at the latest Mireholt Fens I) |

- **Always met, early:** a join beat has no conditions and every branch of it joins (the Blacksmith's fires on worn gear, with a theme fallback so a party that never wears gear still meets him) (nobody can be refused away), and everyone is met by the third expedition — the services the campaign leans on (shop, rest, repair) inside the first one. Every beat pending at a launch plays, one after another (§27.1), after Camp and before the battle, so a service opens at the next visit to Camp. *(Tests: `CampNpcTests` — `Every_npc_joins_on_every_branch_of_a_story_beat`, `Playing_the_campaign_in_order_meets_everyone_by_the_third_expedition`, `Every_npc_has_a_camp_talk`.)*
- **The bots meet them the same way:** `CampRoutine` (§25.3.3) uses only joined services, and `BalanceReport`'s story rows play each launch's pending beats headlessly (`DialogueRunner.PlayPendingBeatsHeadless`, GG-T118); a finished run asserts everyone was met. Its no-story yardstick rows join every NPC up front, like a pre-GG-T119 save.
- **Test Mode** (§6, GG-T93) never plays story, so `TestLoop.NewSave` **auto-joins every NPC** up front — the loop exercises repair, rest, buy, brew and learn from cycle 1. The automation sweep's preview save and `newgame npcs=1` do the same.
- **Old saves:** save version 4's migration joins every NPC on any older save (they had been using every service). New Game+ keeps flags, so nobody leaves.
- Abilities/Learn, the Summon Circle, Bounties, the Caravan and the Crucible (§22.10) have no keeper and are always open.

### 25.0.3 Stations open one at a time (GG-T174)

A new player meets the Camp one system at a time. Every station is always **listed** on the Camp menu, but a closed one is a dimmed row saying when it arrives (`CampUnlocks`):

| Station | Opens | Row while closed |
|---|---|---|
| Merchant, Inn, Blacksmith, Alchemist, Enchantress | when their keeper joins (§25.0.1) | "Joins at Mireholt Fens" / "Joins after Green Valley II" — read from the join beat in content |
| Bounties | 2 campaign stages cleared | "After Green Valley II" |
| Summon Circle, Crucible | 5 cleared (the first expedition) | "After …" |
| Caravan | 10 cleared | "After …" |

So after the first win the Merchant is the one shop open (the Inn opens alongside it — rest is upkeep, not a system to learn), and its camp tip (§25.0.2) points at the Healing Potion, scrolled into view, to spend the first battle gold on. A station the save has already used — an active bounty, a recruit, a Crucible run, a caravan upgrade — never locks again, so older saves are unaffected; Test Mode and New Game+ saves see everything. *(Tests: `CampUnlocksTests`; harness `fullloop` asserts Bounties starts locked and is open by battle 4.)* Long menu values wrap instead of clipping.

### 25.0.4 Onboarding step 2: the Blacksmith repairs (GG-T175)

The Blacksmith arrives when he is first needed: his join beat (`greenvalley_smith`) has the story trigger **`GearWorn`** (`Percent` 60) — it plays at the next launch once any roster hero's equipped weapon or armor piece is below 60% of its max durability (`Repair.AnyBelow`). Its optional `Id` names a fallback theme (`desert`): launching any stage of that theme or a later one fires it anyway, so a party that never equips wearable gear still meets him before the workshop matters. The Camp row reads "Joins when gear wears below 60%" while he is away.

- **Repair first:** `BlacksmithScreen` opens on **Repair**, and Repair is his only open tab at first — Forge, Upgrade and Salvage are dimmed, and tapping one toasts "Opens once the Alchemist joins" (`CampUnlocks.SmithWorkshopOpen`: the Alchemist has joined, or Test Mode / New Game+; old saves joined everyone in the v4 migration, so they keep it). Repair's "Salvage instead" offer (GG-T42) also waits for the workshop. The opened Forge starts with a guided first forge (§25.0.6).
- **Guided prompt:** his camp tip (§25.0.2) is resolved for the save (`CampTips.Resolve`): it names the most worn piece ("Paladin's Iron Sword is worn down to 50%. Tap it to repair it before it shatters in battle.") and rings that piece's repair row, named `repair-<class>-<slot>` (`Repair.RowNameFor`). With nothing worn it reads as written and points at the first repair row.
- **Bots:** `CampRoutine` repairs once he has joined but forges and salvages only once the workshop is open, like the player.
- *(Tests: `CampNpcTests.The_blacksmith_joins_once_gear_first_wears_below_sixty_percent`, `CampUnlocksTests.The_blacksmith_opens_with_only_repair_until_the_alchemist_joins`, `CampTipsTests.The_blacksmith_tip_names_and_points_at_the_most_worn_piece`; harness `smithrepair` — tip rings the worn row, Forge tap stays on Repair, tapping the row repairs it.)*

### 25.0.5 Onboarding step 3: Alchemy potions from loot (GG-T176)

The Merchant sells an **Empty Bottle** (`empty_bottle`, 4 G) and the Alchemist brews **bottle recipes**: one Empty Bottle plus monster trophies looted from enemies makes one potion — Healing Potion (Wolf Pelt + Tattered Cloth), Mana Potion (2 Bat Wing), Antidote (Frog Gland), Eye Drops (Vulture Feather), Greater Antidote (Swamp Ichor + Marsh Moss, 10 G), and at Journeyman tier Hi-Potion (2 Goblin Ear + Scorpion Stinger, 15 G) and Ether (Hag Claw + Bat Wing, 5 G). A bottle recipe always costs less than buying what it makes, even with every input bought at the Merchant (`Brewing.ShopCost` < `Brewing.BuyCost`); with loot it is just the bottle. The older slime-gel/arcane-dust recipes stay.

- **The Alchemist joins at the first brew:** his join beat (`desert_alchemist`) has the story trigger **`CanBrew`** — it plays at the next launch once the bag holds every looted input (all but the bottle) of some Apprentice-tier bottle recipe (`Brewing.LootedForFirstBrew`). Its `Id` is a fallback theme (`desert`), as with GearWorn (§25.0.4), so a party that never loots still meets him at the Sandsea. He hands over one Empty Bottle on joining, so the first brew needs no shopping. The Camp row reads "Joins once you've looted enough to brew" while he is away. Joining him also opens the Blacksmith's workshop (§25.0.4).
- **Guided prompt:** on the first visit `AlchemistScreen` opens with the first brewable bottle recipe selected (`Brewing.FirstBrew`) and his camp tip (`CampTips.Resolve`) names it and what goes in, says what the Merchant charges for the potion, and rings **Brew** (`craft`). With only the bottle missing it rings the recipe row and sends the player to the Merchant for one.
- **Bots:** `CampRoutine`'s potion stock brews any recipe that makes its stopgap potion, buying a single bottle from the Merchant when a bottle recipe is short only of that.
- *(Tests: `EconomyLoopTests` — `Every_dropped_crafting_material_feeds_at_least_one_recipe` (no looted material is a dead end), `Bottle_recipes_brew_from_loot_for_less_than_buying_the_potion`, `The_camp_routine_buys_a_bottle_to_brew_its_loot`; `CampNpcTests.The_alchemist_joins_once_the_loot_for_a_first_brew_is_in_the_bag`; `CampTipsTests.The_alchemist_tip_walks_the_first_bottle_brew`.)*

### 25.0.6 Onboarding step 4: forge the first weapon (GG-T177)

Once the workshop opens (§25.0.4), the Forge offers three **starter weapon recipes** (`Starter: true` in `recipes.json`): Apprentice Forge recipes that cost **no gold** and take only materials Green Valley's enemies drop — **Oak Club** (Wolf Pelt + 2 Cracked Fang), **Training Rapier** (2 Broken Blade + Iron Ore) and **Tallow Lantern** (2 Slime Gel + Tattered Cloth). Between them they suit every starting hero (Paladin, Barbarian, Cleric). Content validation holds a starter recipe to one weapon, Apprentice tier, no gold.

- **The hero it suits** (`Forging.SuitedHero`): a party hero who can wield it and would be better off than with what they hold, best stat match first — the weapon's stat bonuses weighted by the class's growth (`Forging.Affinity`), so the Wisdom lantern goes to the Cleric — then the bigger gain.
- **Guided prompt:** with the repair tip behind him, `BlacksmithScreen` opens on **Forge** with the first-forge recipe selected (`Forging.First`: a carryable starter recipe that suits a party hero, ready ones first). The Forge tab has its own camp tip (`blacksmith-forge`; `HubPage.TipStation`), resolved for the save: it names the weapon, its inputs and the hero, and rings **Craft**; with materials still missing it rings the recipe row and says how many of which to bring back. The recipe detail reads "First forge: free, and the Cleric equips it".
- **Forge and equip:** tapping Craft runs `Forging.Forge` — the weapon is made and equipped on the hero it suits ("Made Tallow Lantern: the Cleric equips it"), and the save flag `onboard:first-forge` ends the guidance; later forges go to the bag as usual. Test Mode and New Game+ saves skip the guidance.
- **Bots:** `CampRoutine` forges starter weapons like any other recipe, once the workshop is open.
- *(Tests: `EconomyLoopTests.Starter_weapon_recipes_cost_no_gold_and_take_only_first_theme_drops`, `EconomyLoopTests.The_first_forge_equips_the_starter_weapon_on_the_hero_it_suits`, `CampTipsTests.The_forge_tip_walks_the_first_forge`; harness `firstforge` — Forge tip rings Craft, tapping it forges the lantern for no gold and the Cleric equips it.)*

### 25.0.7 Onboarding step 5: forge armor (GG-T178)

The armor-set recipes (GG-T182's twenty named sets, `armorsets.json`) **open at the Forge after the first weapon** (§25.0.6): until `onboard:first-forge` is set, any Forge recipe whose result is a set piece (`Forging.IsSetArmor`) is hidden from the Forge list and `Crafting.Craft` refuses it ("Armor recipes open after your first forge"; `Forging.ArmorOpen`). Plain armor and jewelry recipes are not gated. Test Mode and New Game+ saves skip the gate, and `CampRoutine` only picks open recipes. Four Apprentice set recipes take only Green Valley drops — **Wayfarer's Jerkin/Trousers** (Leather + Wolf Pelt, Light) and **Militia Chausses/Gauntlets** (Iron Ore + Leather + Military Insignia, Medium) — so a first piece is forgeable before the desert.

- **The hero it suits** (`Forging.SuitedWearer`): a party hero proficient in its weight who gains over what they wear in that slot, best stat match first (`Forging.Affinity`), then the bigger gain.
- **Guided prompt:** once the first weapon is made the Forge tab's tip becomes `blacksmith-armor` ("Forge Armor"; `BlacksmithScreen.TipStation`), so it appears right after the weapon is forged. `Forging.FirstArmor` picks the piece: an open Apprentice set recipe that suits a party hero and can be carried, ready ones first, then stat match, fewest missing materials, cheapest. The Forge preselects it (moving off the forged starter weapon), the tip names the piece, its inputs and the hero and rings **Craft**; short of materials or gold it rings the recipe row and says what to bring back. The recipe detail reads "First armor: the Barbarian wears it".
- **Forge and equip:** Craft runs `Forging.Forge` — the piece costs its usual gold, is made and equipped on that hero ("Made Wayfarer's Trousers: the Barbarian wears it"), and `onboard:first-armor` ends the step (the Enchantress's join, GG-T179, follows it). Later forges go to the bag.
- *(Tests: `EconomyLoopTests.Armor_recipes_open_after_the_first_weapon_and_the_first_piece_is_worn`, `EconomyLoopTests.A_first_armor_piece_is_forgeable_from_first_theme_drops`, `CampTipsTests.The_armor_tip_walks_the_first_armor_piece`; harness `firstarmor` — no set recipes before the first forge, then the armor tip rings Craft and the Barbarian wears the trousers.)*

### 25.0.8 Onboarding step 6: enchant armor for passives (GG-T179)

Alongside the six weapon-only elemental enchants (`enchants.json`, which turn a weapon into its "(Element)" variant), the Enchantress binds **armor enchants** (`armorenchants.json`, `ArmorEnchantDef`: `Id`, `Name`, `Passive`, `Inputs`, `Gold`, `Tier`) into a worn armor piece (Head/Torso/Legs/Gloves/Boots). Each grants a real `Passive` ability (abilities.json) to the wearer in battle while the piece is intact:

| Enchant | Passive | Effect | Tier | Cost |
|---|---|---|---|---|
| Regeneration | `regenerate` (the enemy kit) | heals 6% MaxHP each time control returns | Apprentice | Arcane Dust + 2 Frog Gland, 40 G |
| Poison Ward | `poison_ward` | immune to Poisoned | Apprentice | Arcane Dust + 2 Slime Gel, 40 G |
| Thorns | `thorns` | an enemy whose hit lands takes 25% of it back ("Thorns") | Apprentice | Arcane Dust + 2 Cracked Fang, 40 G |
| Steadfast | `steadfast` | immune to Frozen and Sleep — always free to move (heroes already drag freely, so this stands in for "+move") | Apprentice | Arcane Dust + 2 Wolf Pelt, 40 G |
| Riposte | `counter_attack` (the enemy kit) | an adjacent attacker is struck back at half strength | Master | 2 Arcane Dust + Shadow Essence + 2 Broken Blade, 150 G |
| Mana Siphon | `mana_siphon` | every hit the wearer lands adds 5 mana charge | Master | 2 Arcane Dust + Spark Essence + 2 Goblin Ear, 150 G |

- **Where it lives:** the enchant id is on the piece (`EquippedArmor.Enchant`), not a new item, so set membership, wear, repairs and Masterwork rolls are untouched. One enchant per piece — a new one replaces it. Unequipping keeps it on that copy (`WornGear.Enchant`; an enchanted copy is put back on first), and it goes into battle on `ArmorPieceSpec.Enchant` / `EquippedPiece.Enchant`. `BattleRunner.HasPassive` checks the class kit, then every intact enchanted piece the unit wears; a broken piece leaves `Unit.Gear`, taking its passive with it, and a piece that breaks for good loses the enchant. Warded buffs announce "Warded!". Tuning: `BattleConfig.RegeneratePercent`, `ThornsFraction`, `ManaSiphonCharge`.
- **Tier:** like her weapon enchants, armor enchant tiers unlock with the Alchemist's station level, and each enchant feeds it 15 XP. The Apprentice ones take Arcane Dust and Green Valley trophies only.
- **The Enchantress joins after the first armor:** her join beat (`swamp_enchantress`) has the story trigger **`FirstArmor`** — it plays at the next launch once `onboard:first-armor` is set (§25.0.7). Its `Id` is a fallback theme (`swamp`), so a party that never forges armor still meets her at the Fens. She hands over an Arcane Dust on joining. The Camp row reads "Joins once you've forged your first armor" while she is away.
- **Guided prompt:** `ArmorEnchanting.First` picks the first enchant: an unenchanted worn piece (set pieces first, so the guided first armor), and an open enchant whose materials are in the bag, else the fewest still to find, else file order. Her **Armor** tab (`EnchantressScreen`) lists worn pieces (`piece-<class>-<slot>`) and enchants (`aenchant-<id>`) and preselects both; her camp tip (`enchantress`, `CampTips.Resolve`) names the enchant, the passive, the hero and the piece and rings **Enchant** (`craft`); short of materials it rings the enchant row and says what to bring back. The **first enchant is free** of gold (`ArmorEnchanting.Gold`), and `onboard:first-enchant` ends the guidance. Test Mode and New Game+ saves skip it.
- **Shown:** the Status screen's gear list adds "✦ Regenerate" after an enchanted piece; the Equip screen marks the slot "✦" and names the enchant in its compare panel.
- *(Tests: `EconomyLoopTests.Armor_enchants_grant_passives_and_the_first_ones_take_first_theme_drops`, `EconomyLoopTests.An_armor_enchant_binds_into_the_worn_piece_and_stays_with_it`, `CampNpcTests.The_enchantress_joins_after_the_first_armor_piece`, `CampTipsTests.The_enchantress_tip_walks_the_first_armor_enchant`, `ArmorEnchantTests` (poison ward, thorns, regeneration in battle); harness `firstenchant` — forge the lantern and trousers, then the Enchantress tip rings Enchant and Regeneration binds into the trousers for no gold.)*

### 25.0.2 Camp tips (GG-T173)

The first time the player opens a Camp station, a one-time **camp tip** points at the thing to do there: the screen dims, a gold outline and a bouncing pointing hand (`Art.Cursor`) mark the key control, and a card names the station and says what to do in one sentence, with **Got it**. The card sits at the bottom, or at the top when the control is in the lower half, so it never covers what it points at. Combined with the NPC join order above, stations — and their tips — arrive one at a time.

- **Table:** `Meta/CampTips.cs` — station (the page's `Id`), title, sentence, target control name (a trailing `-` matches a prefix, e.g. `recipe-` = the first recipe row). Today: Merchant, Blacksmith (the most worn piece's repair row, §25.0.4), the Blacksmith's Forge tab (the first forge's Craft button, §25.0.6, then the first armor piece's, §25.0.7), Alchemist (the first bottle brew's Brew button, §25.0.5), Enchantress (the first armor enchant's Enchant button, §25.0.8), Abilities, Bounties. The onboarding chain (GG-T174…T180) refines wording and targets here.
- **Once per save:** dismissing records the station in `SaveData.SeenCampTips` (migrated to empty on older saves). Bot-played saves (`TestMode`) never get tips.
- **Replay:** Settings › How to play › **Replay camp tips** clears the set.
- **Automation:** demo, test mode, `-autobattle` and harness runs suppress tips so the UI drivers see the plain screens; `-campTips` turns them back on. `python tools/harness.py camptips` checks show / dismiss / never-again / replay and screenshots each tip. *(Tests: `CampTipsTests`.)*
- Tab buttons are named `tab-<label>` (`Ui.Tabs`) so tips and harnesses can target them.

### 25.1 Merchant

`ShopScreen` / `Meta.Shop`. Buy / Sell / Bargain tabs; Buy and Sell use separate carts with rows showing name (rarity color), unit price, owned count and a − n + stepper; the footer shows "Pay" (red if unaffordable) or "Receive" and commits the whole cart.
- **Buy stock:** every `SoldByVendor` item, sorted by type, price, name; stepper capped at `MaxStack − owned`.
- **Sell:** every owned non-quest item with a cost, at `max(1, floor(BaseCost × 0.5))`.
- **Potion economy (amended 2026-10-15, GG-T44):** every healing/cure consumable (Healing Potion, Hi-Potion, X-Potion, Revival Charm, Antidote, Eye Drops, Remedy, Tent) is now `SoldByVendor`, not just Healing Potion — the Merchant is the convenient, always-available path. Each one's Alchemy brew (§25.3) costs **2.5–5×** less gold per unit, because its reagents come from field drops instead of a gold price; gathering and brewing is the efficient path, buying stays the convenient one. *(Test: `Every_sold_healing_or_cure_potion_is_meaningfully_cheaper_brewed_than_bought`.)*
- **Fresh baseline gear (amended 2026-10-19, GG-T84):** the 16 unenhanced **Common** weapons, armor and relics (Rusty Sword, Bronze Dagger, Iron Sword, Hunter's Bow; Padded Vest, Leather Armor, Chain Mail, Iron Helm, Leather Boots, Iron Sabatons, Leather Leggings, Leather Gloves; Iron Talisman, Bone Amulet, Copper Ring, Copper Earring) are now `SoldByVendor`, bought new at full `BaseCost` and full durability -- before this, 0 of 155 equipment defs were sold and the worn Bargain shelf was the only gear buy path, so "buy replacements" at camp (owner, 2026-10-19) was impossible. Uncommon-and-up and every enhanced (+N) tier stay drop/forge-only. Where the Forge can make a sold piece, its gold cost stays under 60% of the buy price (materials come from drops), mirroring the potion rule: forging is the efficient path, buying the convenient one. *(Test: `The_merchant_sells_fresh_baseline_gear_and_only_baseline_gear`.)*
- **Bargain (added 2026-10-15, owner direction, US-246, GG-T38):** a rotating "Used / Bargain" shelf of `Meta.Bargains.ShelfSize` = 4 worn weapons/armor, picked from every equipment `ItemDef` with `Durability > 0` and `Slot` Weapon or Armor, at **35–60%** of `BaseCost` but only **10–40%** of `Durability` left — cheap power now, a real risk of shattering mid-combat before it can be repaired. Rolled deterministically from `SaveData.BargainRotation` (so reopening the Merchant never rerolls it, and the same save always replays the same shelf) and turns over once per finished battle, win or lose (`Bargains.Rotate`, called from `BattleFactory.Commit`). Each row shows a durability gauge before purchase; buying files the rolled durability straight into `SaveData.Worn` (§24.5) so equipping it carries that wear, not factory-fresh. A piece already owned (every weapon/armor is `MaxStack` 1) can't be bought again until it leaves the bag. *(Tests: `Tests/Core.Tests/BargainShelfTests.cs`.)*

### 25.2 Blacksmith

`BlacksmithScreen`: **Forge**, **Upgrade**, **Salvage**, **Repair** tabs (only Repair is open until the workshop opens, §25.0.4; the opened Forge starts with the starter weapons of §25.0.6, and its armor-set recipes open after the first weapon, §25.0.7) ([§24.5](#245-weapon-durability), [§24.6](#246-crafting-recipes)). The header shows the Blacksmith's station level and XP to its next tier ([§24.6.1](#2461-tier-ladder-gg-t35-amended-2026-10-15)); a tier-locked recipe shows a 🔒 and the level required instead of its price; an undiscovered recipe ([§24.6.2](#2462-recipe-discovery-gg-t36-amended-2026-10-15)) one input short shows as `???` instead, everything else undiscovered stays hidden. The recipe detail shows ✔/✘ per input with owned counts. Repair lists every roster hero's durable weapon/armor ("Paladin — Iron Sword 3/9", cost or "OK"), warning when uneconomical. **Amended 2026-10-15 (GG-T42):** when a repair is flagged uneconomical, an indented "Salvage instead" row appears directly beneath it showing the scrap yield for that piece's current durability, so the repair cost and the salvage yield sit side by side — confirming scraps the piece (`Crafting.SalvageEquipped`) instead of repairing it. **Amended 2026-10-15 (GG-T41):** the Salvage tab also lists any `Broken <Name>` fragments alongside spare bag gear, dimmed, salvaging them via `Crafting.SalvageBroken`; the Items screen's All/Gear tabs list them too (also dimmed), with a detail note that they can't be equipped or repaired.

### 25.3 Alchemist

`AlchemistScreen`: **Brew** (Alchemy recipes, including the Empty Bottle recipes of §25.0.5). ~~**Enchant** (Enchant recipes), **Heal** (same service as the Inn).~~ **Amended 2026-10-19 (GG-T119):** enchanting moved to the Enchantress's own station (§25.3.4) and the Heal tab is gone — it was the Inn under another name, and resting is now the Innkeeper's (§25.0.1). The header shows the Alchemist's station level and XP to its next tier ([§24.6.1](#2461-tier-ladder-gg-t35-amended-2026-10-15)); a tier-locked recipe shows a 🔒 and the level required instead of its price; an undiscovered recipe shows as `???` or stays hidden, same convention as the Blacksmith ([§24.6.2](#2462-recipe-discovery-gg-t36-amended-2026-10-15)).

### 25.3.1 Inn

**Amended 2026-10-09 (new):** `Meta.Inn` — the Camp's rest service (hub "Inn" row; ~~and the Alchemist Heal tab~~ — removed 2026-10-19, GG-T119: the Inn is the Innkeeper's, shown once she joins, §25.0.1). Cost = `ceil(total missing HP × 0.5 G)` across the **whole roster** (reserves included); resting sets every hero to full. "Everyone is healthy" when nothing is missing.

### 25.3.2 Using items at Camp

**Amended 2026-10-09 (new):** `Meta.Camp.Use` — from the **Items** screen's detail pane, a healing consumable (not Revival Charm) can be used on a party hero (one button per hero), and a Tent (`OutOfBattleOnly`) has "Use on party", resting the whole roster. *(Test: `Camp_potion_heals_a_wounded_hero_and_tent_heals_everyone`.)*

### 25.3.3 Camp routine (auto-manage)

`Meta.CampRoutine.Run` performs a sensible Camp visit through the normal services: repair worn gear, rest if affordable, forge and equip better weapon/armor (gear score = sum of stats + ½ attack), learn one affordable spell per hero and fill empty unlocked slots, keep ≥ 6 Healing Potions. **Amended 2026-10-15 (GG-T44):** restocking now brews the Healing Potion recipe (cheap, from field-drop reagents) before falling back to buying at the Merchant once the reagents run out. Used by `BalanceReport`. 🟡 No in-game entry point yet. *(Test: `CampRoutine_brews_potions_instead_of_buying_when_it_has_the_reagents`.)*

**Amended 2026-10-15 (GG-T54):** before repairing, every `Broken` fragment is scrapped on sight (it can't be equipped or repaired anyway, §24.6.2's salvage-only fate), and a weapon/armor whose repair has become uneconomical (§24.5, cost ≥ `BaseCost`) is scrapped via `Crafting.SalvageEquipped` instead of repaired — the same trade the "Salvage instead" row offers a player. The gear-upgrade pass now also shops the Merchant's Bargain shelf (§25.1) for an affordable slot upgrade once owned gear and Forge recipes come up short, but skips any offer below `CampRoutine.MinSafeBargainDurabilityFraction` (25%) — the shelf's 10-40% durability gamble is a deliberate player choice, and an automated buyer can't weigh that risk the way a player can. Spending skill points (`HeroSave.SkillPoints`, GG-T49) is deliberately **not** part of this routine yet: there is still no skill tree to spend them into until GG-T50 lands (§25.6's amendment), so they're left to bank exactly as Learn already does. *(Tests: `CampRoutine_salvages_broken_fragments_on_every_visit`, `CampRoutine_salvages_an_equipped_piece_instead_of_an_uneconomical_repair`, `CampRoutine_never_buys_a_bargain_offer_too_close_to_shattering`, `CampRoutine_buys_a_safe_bargain_offer_for_an_empty_slot`.)*

**Amended 2026-10-19 (GG-T87):** the routine now works the two Camp services it used to skip. **Bounty Board** (§22.4): a finished contract is claimed; an active one the uncleared campaign stages can no longer finish is abandoned; with none active it accepts the posted contract the stages ahead would complete soonest (cheapest reward breaks ties) — a playthrough meets each stage once and this player doesn't grind replays, so a contract whose kill count outnumbers every remaining spawn of its target is never taken. (Content note: Slime Culling wants 5 `slime00` kills but the whole campaign spawns only 3, so it is completable only by replaying stages; Wolf Pack's 3 kills need all three `wolf00` spawns, the last at stage 15.) **Summon Circle** (§25.10): when a party slot is open and `Summon.Price` is affordable, it recruits the first pool class not yet in the roster, before the gear pass so the newcomer is equipped the same visit; a full party never spends on a reserve recruit. Every real new game starts with a full five, so `BalanceReport.Campaign_pacing_table` gained a `greedy+camp trio-start` row (three starters) to put recruiting in the loop; the table header now reports recruits and bounties claimed, and the `+camp` rows assert a bounty is claimed whenever a run finishes (and the trio row that someone is recruited). Greedy+camp and trio-start still finish 4/4. **Amended 2026-10-19 (GG-T118):** every new game now starts with three ([§25.17](#2517-small-start-story-recruits-gg-t118)), so the default rows are trio starts that gain story recruits; the old trio-start row became `greedy+camp trio no-story` (it still asserts a Summon Circle recruit) and a `greedy five-start (pre-GG-T118)` row keeps the old start as the yardstick. *(Tests: `CampRoutine_recruits_into_an_open_party_slot_when_affordable`, `CampRoutine_does_not_recruit_with_a_full_party`, `CampRoutine_claims_a_finished_bounty_and_accepts_only_an_achievable_one`, `Campaign_pacing_table`.)*

**Amended 2026-10-19 (GG-T119):** the routine uses only the services whose Camp NPC has joined (§25.0.1) — no Blacksmith: no salvage, repair or forging; no Innkeeper: no rest; no Merchant: no Bargains or potion buying; no Alchemist: no brewing. Equipping owned gear, learning, bounties and recruiting are always available. *(Tests: `Camp_routine_uses_no_service_whose_npc_has_not_joined`, `Camp_routine_uses_each_service_once_its_npc_joins`.)*

### 25.3.4 Enchantress (GG-T119)

`EnchantressScreen` (hub row **Enchantress**, once she joins, §25.0.1): the Enchant recipes that used to be the Alchemist's second tab, with the same recipe list and detail. Every enchant is Master tier and she has no recipe ladder of her own to climb (a ladder that only enchanting fed could never leave Apprentice), so her recipes still unlock with the **Alchemist's** station level (§24.6.1) and enchant crafts still feed it; her header reads "Unlocks with the Alchemist: …". **Amended 2026-10-27 (GG-T179):** her station has two tabs — **Armor** (first; the armor enchants of §25.0.8) and **Weapons** (the elemental Enchant recipes above).

### 25.4 Equip

`EquipScreen`: hero switcher (‹ portrait ›), the five slots, a stat-delta grid (STR VIT AGI SPD INT WIS LCK, current → preview), and the bag's candidates for the selected slot (tap to preview, tap again to equip; "Remove" unequips). For an armor slot the compare panel also shows armor set progress for the current and previewed piece (GG-T182, [§24.2.3](#2423-armor-sets-gg-t182)).

### 25.5 Party

`PartyScreen`: every roster hero as a card (thumb, name, level, MaxHP, Active/Reserve, class card text); tap toggles ([§23.4](#234-roster--party-composition)).

### 25.6 Abilities

`AbilitiesScreen` / `Meta.Abilities`: hero switcher, the 5-cell battle bar (locked cells shown), and three tabs:
- **Known** — the hero's learned abilities (name, kind/cast time, cost pips, description); tap to put it in the selected slot.
- **Items** — owned consumables; tap to put `"item:<id>"` in the slot.
- **Learn** — **Amended 2026-10-09 (owner decision):** buy new abilities with gold. Learnable = the class's own `Abilities` list plus **every hero spell in the general catalog**, minus what is known (enemy charges, `enrage` and passives excluded). Price: **spells 120 + 80 per orb of cost, skills 300** (`Abilities.Price`). Confirm dialog, then the ability is Known.

`Abilities.Assign` accepts only Known abilities or consumables, refuses locked slots, and moves an entry already on the bar to the new slot (no duplicates via assignment). "Clear slot" empties it.

**Amended 2026-10-15 (GG-T49, owner decision):** heroes now also earn skill points on level-up ([§15.1](#151-data-model)) to spend on skill-tree nodes. Decision: once the tree exists, its nodes **replace gold-bought Learn for a hero's own class/off-class abilities and passives** — gold Learn above stays only for the **general spell catalog**. **GG-T50 (2026-10-15) landed the tree's content** (§25.6.1) — the Learn screen is deliberately left unchanged for now (still gold-for-class-abilities), and so is `BattleRunner`'s enemy-kit passive gating (§14.4): a tree-granted passive sits in a hero's `Known` list but has no in-battle effect yet. Spending points against a tree, a Skills tab on `AbilitiesScreen`, wiring passive effects to heroes, and the Learn-screen migration itself are follow-up work; `StatusScreen` continues to show the running skill-point balance (`HeroSave.SkillPoints`) so the currency is visible before there's anywhere to spend it.

**Amended 2026-10-21 (GG-T48, owner direction — "Hero character sheet"):** `StatusScreen` is the per-hero **Build screen** the ticket asked for. On top of its existing portrait/HP/level/XP/gear-with-durability, it now shows:
- **Base vs. gear stats** — each stat as Base (`Unit.LevelStats`) / Gear (the delta to `Unit.Effective`, colored like an Equip-screen preview) / Total, instead of only the effective sum.
- **Known Abilities** — every id in `HeroSave.Known`, name + kind, in a scrollable list (the hero's full kit can run past default-loadout size once Learn has bought a few).
- **Skill Trees** — `Progression.TreeInvestment(c, h)`: for each tree, the sum of every node's `Cost` whose `Grants` the hero already has Known, highest first. This is a read-only reading of the tree map against what the hero already knows (its default loadout plus anything gold-Learned) — **not** a spend ledger, since spending points against a tree is still GG-T49/50's deferred follow-up (previous paragraph); a hero with nothing matching a tree's nodes just shows none.
- **Proficiencies** — `Progression.KnownProficiencies`: any of the three proficiency-unlock passives (`heavy_armor_proficiency`, `martial_weapons_proficiency`, `arcane_focus_proficiency`, staged by GG-T74 for §25.6.1) already in `Known`. **Amended 2026-10-21 (GG-T52):** these now actually gate gear ([§24.2.1](#2421-proficiency-gating-gg-t52)) and are real nodes in the Mage/Cleric/Alchemist trees — still shows "None yet" for a fresh hero today, since spending a tree point on any node (these included) remains the deferred follow-up §25.6 already notes; a hero picks one up via the migration grandfather clause until then.
- **Build summary** — `Progression.BuildSummary`: `"{Class} · {tree A} {pts}[ / {tree B} {pts}…] — {role}[ who {cross-tree verb}]"` (e.g. "Paladin · Guardian 7 — a tank"), reading only off the class, its tree investment and its `Role` — a cross-tree verb ("…who heals") only appears once a non-home tree's matched nodes reach 3 points, so the line never claims a build the hero's Known list doesn't back up.

*(Tests: `BuildScreenTests` — tree investment sums only nodes whose grant is Known, a cross-tree ability counts toward its own tree and not the home tree, an empty Known list yields no tree investment, proficiencies report only what's actually known, the build summary names class/trees/role and gains its cross-tree tail only at the 3-point threshold, every real hero class produces a summary without throwing.)*

### 25.6.1 Skill trees (content only, GG-T50)

`Assets/Gaiden/Resources/Content/skilltrees.json` → `ContentDb.SkillTrees` (`SkillTreeDef`/`SkillNodeDef`, `Core/Content/ContentModels.cs`): one tree per hero archetype, named by a `ClassDef.HomeTree` id every hero class must set (`ContentLoader.Validate`, same pattern as §25.13's `Role` check). A tree is a small DAG of nodes:
- `Grants` — the ability or passive id the node would unlock (must resolve in `ContentDb.Abilities`).
- `Cost` — skill points to spend on it.
- `RequiredPointsInTree` — total points already spent anywhere in the tree before it can unlock.
- `Prerequisites` — sibling node ids (same tree) that must already be unlocked.

`ContentLoader.Validate` resolves every grant and prerequisite, rejects duplicate node ids within a tree and prerequisite cycles, and requires every `Team.Hero` class to name a tree that exists. Six trees ship today, one per `Role` (§25.13): **Guardian** (Tank), **Warrior** (Striker), **Ranger** (Ranged), **Mage** (Mage), **Cleric** (Healer), **Alchemist** (Support) — a deliberate simplification of the Guardian/Warrior/Rogue/Ranger/Mage/Cleric/Alchemist archetype list down to one tree per already-`Role`-validated archetype, leaving a Rogue split out of Warrior for a later pass once rogue-specific content exists.

**Amended 2026-10-16 (GG-T74):** each tree deepened from a flat 5-node list to **2 shared nucleus nodes + 3 variant branches (Opener → Core → Branch Capstone) + 1 cross-branch Finale** — **72 nodes total**, every tree 12 deep. The 3 branches per tree are the bible's own §25.13 sub-archetype clusters, now actually spendable instead of only shaping a class's starting kit:

**Amended 2026-10-21 (GG-T52):** Mage, Cleric and Alchemist each picked up a 13th node (`RequiredPointsInTree: 8`, no prerequisite) granting one of the three proficiency-unlock passives ([§24.2.1](#2421-proficiency-gating-gg-t52)) — Mage → Heavy Armor Proficiency, Cleric → Martial Weapons, Alchemist → Arcane Focus. Guardian/Warrior/Ranger don't need one: their classes already start with Martial weapons (and Guardian with Heavy armor) by `ClassDef` default.

| Tree | Branch 1 | Branch 2 | Branch 3 |
|---|---|---|---|
| Guardian (Tank) | Aegis Guard — mitigation + cleanse | Juggernaut — knockback aggression | Warlord — party buffs |
| Warrior (Striker) | Berserker — rage/bloodlust | Duelist — precision/counter | Shadowstep — mobility/rogue flavor |
| Ranger (Ranged) | Marksman — crit/precision | Trapper — snares/control | Skirmisher — mobility + covering fire |
| Mage | Spellblade — melee-infused casting | Field Caster — traps/wards/support | Hexweaver — curse/DoT damage |
| Cleric (Healer) | Aegis Wardens — preemptive wards | Hexmenders — damage-for-heal | Skirmish-Menders — mobile support |
| Alchemist (Support) | Tempo Weavers — haste/slow control | Field Engineers — traps/amplifier synergy | Primal Pack — reagent/consumable mastery |

A branch's Capstone is reachable in 3 points (Opener 1 + Core 2); the tree's Finale needs 10 points in the tree and 2 branch Capstones unlocked (deliberately the same 10-point figure §25.6.2's cross-tree gating uses to open a second tree — finishing two branches of your home tree and being ready to dip into another tree land at roughly the same investment). Ranger had no reusable sub-archetype content (GG-T18), so its 3 branches (9 nodes) are new `AbilityDef`s; every other tree's 9 branch nodes reuse the already-shipped GG-T3/§25.13 cluster kits verbatim (`aegisguard_*`, `juggernaut_*`, `warlord_*`, `berserker_*`, `duelist_probingthrust`/`guardstance`/`decisivecut`, `shadowstep_*`, `spellblade_*`, `fieldcaster_*`, `hexweaver_*`, `aegiswardens_*`, `hexmenders_*`, `skirmishmenders_*`, `tempoweave_*`, `fieldeng_*`, `primalpack_*`). No new ability was needed for a "Barrier"-equivalent spell — Cleric's `aegiswardens_sanctuaryrite` ("Sanctuary Rite") already wards the whole party and is the node a cross-specced non-healer grinds toward (§25.6.2's worked example). Node grants otherwise still reuse existing content, including the six "enemy kit" passives (§14.4) pre-dating this amendment; this pass adds 12 new `AbilityDef`s (9 Ranger branch skills + 3 proficiency-unlock passives staged for §24.2's GG-T52 gating, granted here but not yet wired to any battle effect).

A hero's starting bar is unaffected: `Progression.NewHero` already makes every `DefaultLoadout` entry Known at creation regardless of the tree (§15.1), so "the hero starts in its home tree with its default abilities pre-unlocked" holds without any new linking.

*(Tests: `SkillTreeTests` — every hero class names a real tree, every node's grant/prerequisites resolve, `ContentLoader.Validate` catches an unknown grant, an unknown prerequisite, a duplicate node id, a non-positive cost and a prerequisite cycle; `CatalogTests` resolves every new `AbilityDef` on a live board; full suite 873/873, glyph + asset checks clean.)*

**Amended 2026-10-20 (GG-T80, docs-only class/region audit):** every `Team.Hero` class's `DefaultLoadout` was matched against its own `HomeTree`'s 3 branches above, by `<cluster>_<move>` ability-id prefix (e.g. an `aegisguard_opener` sibling kit uses `aegisguard_*` ids) — how well each tree's 3 chosen branches actually cover the classes that live in that region. Requested to inform where bridge nodes and new classes would be needed on a future unified map (GG-T79's own hex-wedge sketch didn't survive — see §25.6.2's neighbor note below and `docs/USER_STORIES.md` GG-T83 — but this gap data stands on its own regardless of topology). Findings:

- **Guardian — full branch coverage, 2 orphans.** Aegis Guard (`defender`, `knight`, `paladin`), Juggernaut (`myrmidon`, `tank`) and Warlord (`shieldmaiden`, `warchief`) between them cover 7 of Guardian's 9 classes. The other 2 — `darktemplar` and `jadeknight` — both run a `deathguard_*` kit (`deathguard_mana_siphon`, `deathguard_dark_communion`) that matches none of the tree's 3 branches; §25.13's own Tank sub-archetype list already names Deathguard as a 4th cluster, it just never got a branch (follow-up: GG-T82).
- **Warrior — structurally overloaded.** Striker's 31 classes split across **8** real `<cluster>_` kits, but the tree kept only 3 as branches. Per-cluster counts: duelist ×7 (`assassain`, `duelist`, `fencer`, `ronin`, `sellsword`, `swordmaster`, `thief`), shadowstep ×6 (`blackninja`, `blueninja`, `chromaninja`, `greenninja`, `redninja`, `yellowninja`), skirmisher ×5 (`lancer`, `raider`, `speedster`, `streetfighter`, `striker`), berserker ×4 (`barbarian`, `basher`, `bruiser`, `pandagirl`), combobrawler ×4 (`fighter`, `martialartist`, `prizefighter`, `pugilist`), opportunist ×2 (`nighthunter`, `ripper`), brawler ×2 (`odachi`, `slasher`), attrition ×1 (`vampire`). Only Berserker, Duelist and Shadowstep became branches; the other 5 clusters — skirmisher, combobrawler, opportunist, brawler, attrition, **14 classes (45%)** — match no branch in their own home tree (follow-up: GG-T81, carve a Rogue region out of the excess).
- **Ranger — branches nobody is assigned to.** Marksman, Trapper and Skirmisher (GG-T74's new, Ranger-only content) have zero classes walking them. Ranger's only 2 classes, `hexsniper` and `hexwarden`, still run the pre-branch shared `ranged_*` baseline kit (`ranged_quickshot`, `ranged_calledshot`, `ranged_suppressingfire`) — neither was retrofitted onto a branch (follow-up: GG-T82).
- **Mage, Cleric, Alchemist — no gap.** Every class in these 3 regions matches one of its tree's 3 branches: Mage — Spellblade (`harbinger`, `redmage`), Field Caster (`drifter`, `operative`), Hexweaver (`blackwitch`, `ritualist`); Cleric — Aegis Wardens (`alchemist`, `cleric`, `doctor`), Hexmenders (`sage`, `whitewitch`), Skirmish-Menders (`monk`, `sister`, `whiteninja`); Alchemist — Tempo Weavers (`courier`, `dervish`), Field Engineers (`engineer`, `machinist`, `technician`, `tinkerer`), Primal Pack (`bluelion`, `wildchild`).

This audit is read-only — no `ClassDef`, `SkillTreeDef` or other content changed, no new tests (none apply to a docs-only pass). *(Source: `Assets/Gaiden/Resources/Content/classes.json` × `skilltrees.json`, matched by script against every `Team.Hero` entry's `HomeTree` and `DefaultLoadout`.)*

### 25.6.2 Cross-tree specialization: topology open (GG-T83)

**Not decided; nothing here ships.** How a hero crosses from its home tree into another archetype's is still an open design question. The six trees in §25.6.1 have no bridge, toll or adjacency rule between them yet. The 10-point "open a second tree" figure referenced above comes from GG-T51's original toll-gate draft (option A below), which the owner rejected as a tax. It stays only as the rough investment §25.6.1's Finale was tuned against.

Topologies tried and rejected so far: (A) six trees plus a toll-gated bridge (GG-T51/GG-T76); (B) one hex map of six wedges, cost = path length (GG-T79, which also got its own opposite-pair arithmetic wrong); (C) an octagon of the 8 stats; (D) the owner's own archetype wheel of 8 pure plus 8 hybrid archetypes, the closest so far but "not quite right". The two references are FF12's Zodiac License Boards (pick 2 of 12 boards) and FFX's Sphere Grid (one shared lattice, different start points). The full record, including why each was rejected and the later GG-T166/167/168 attempts (GG-T168, stat-threshold unlocks with no board, is awaiting owner review), is in [`docs/research-skill-map-topology.md`](research-skill-map-topology.md). The GG-T80 audit findings above don't depend on this decision.

### 25.7 Shared Camp utilities

`App/UI/Ui.cs` (windows, menu rows, lists, tabs, gauges, cost pips, buttons, gold formatting), `ItemUi` (item rows, glyphs, detail pane), `Confirm` (modal), `GameApp.Toast`.

### 25.8 Per-screen sketches

Every Camp screen follows the same skeleton — header (‹ back, title, gold), body windows, a primary action — and is captured by `tools/harness.py sweep` (`artifacts/sweep/sweep-<screen>.png`), the living replacement for the old ASCII sketches.

### 25.9 The merged hub

✅ Done — this section's old "deliberately not yet" is resolved by §25.0 (**Amended 2026-10-09**).

### 25.10 Summon Circle

`SummonScreen` / `Meta.Summon`: recruit a hero class for gold — deliberate, not a pull. Pool (`summon.json`): GreenNinja, RedNinja, Pugilist, Ronin, Sellsword, Thief, Vampire. Price **250 + 250 × recruits so far**. Owned classes show "Joined"; unaffordable rows are disabled. **Amended 2026-10-09:** a recruit **arrives at the roster's average TotalXp** (so it is not stranded at level 1) and **joins the party automatically when there is room**. *(Test: `Full_loop_battle_commit_then_every_vendor`.)*

### 25.11 Bestiary

`BestiaryScreen`: a grid of every enemy class's thumb (darkened silhouette until Seen), "Seen n/110"; the detail shows name, Defeated count, lore and its family's drops (rarity-colored item names, highest chance first) once **Seen**, then base stats only once Scanned (added 2026-10-15, GG-T43).

---


### 25.11 Guest allies (added 2026-10-10, owner direction)

Owner: "guest characters that you meet in battle that act as allies — you rescue them and get them to join you, unless they die in the fight, then you've lost them forever."

- `WaveDef.Guests` places a hero class into a wave (`GuestJoinedEvent`, billboard "X joins the fight! Keep them alive to recruit them."). The guest is hero-team and **player-controlled** for that battle — drag it, pincer and support with it — at the stage's recommended level, with its class loadout. A green keyline and a GUEST tag mark it.
**Amended 2026-10-19 (GG-T114, owner: "no one dies permanently, that's lame"):** a guest who falls is *down*, not lost.

- **Standing at a victory** → the guest joins the roster (and the party if there's room) at its battle level; results: "X joins the caravan!". A guest who fell and was brought back (Revival Charm, revive spell) and is standing at the end counts.
- **Falls mid-battle** → billboard "X has fallen!"; revivable like any hero (the Revival Charm picks up a fallen guest too).
- **Down at battle's end** → **not lost**: nothing is recorded, the offer stays open and the guest turns up again when that stage is replayed; results: "X fell in battle — Will return".
- Guests already recruited are skipped (`BattleSetup.ExcludedGuests`). A guest still standing keeps the battle going if the party falls.
- Permanent loss exists only behind the single off-by-default rule `Permadeath.Enabled` (§29.1): with it on (a future Ironman mode) a guest down at the end goes into `SaveData.LostGuests`, is excluded from later battles and the Summon Circle's pool, and falls are announced "for good". In the normal game it is always off and any `LostGuests` entries from older saves are ignored — those guests come back.
- Content: Dervish (Sandsea Reaches II), Drifter (Mireholt Fens II), Lancer (Frostmaw Caverns II), Machinist (first city ruin).
- *(Tests: `A_guest_who_survives_a_victory_joins_the_roster`, `A_guest_revived_mid_battle_is_recruited`, `A_guest_down_at_battle_end_can_still_be_recruited_later`, `Permadeath_is_off_by_default_and_old_lost_guests_come_back`.)*

### 25.12 Caravan upgrades (added 2026-10-10, owner direction: "upgrades … a sense of progression")

The caravan is the party's moving base — its wagons are the Longmother's own shed plates, re-decked, and the shops between battles (US-348 framing; real story text, GG-T120, §27.1). `caravan.json` defines upgrades bought with gold at **Camp → Caravan** (`CaravanUpgrades.Buy`, levels in `SaveData.Caravan`):

| Upgrade | Levels | Effect |
|---|---|---|
| Mana Casks | 400 / 900 / 1800 | every battle starts with +1 / +2 / +3 orbs (`BattleSetup.StartOrbs`) |
| Medic Wagon | 600 / 1400 | the inn opens **on the road** mid-expedition at ×2 / ×1.5 price |
| Armory Wagon | 350 / 800 / 1500 | repairs −20 / −35 / −50 % (`Repair.Cost(c, s, …)`) |
| Strongbox | 500 / 1200 | expedition completion bonus +25 / +50 % |
| Scouts | 300 | Adventure lists each battle's foes |

*(Test: `Caravan_upgrades_cost_gold_and_change_how_expeditions_play`.)*
### 25.14 The party of five (added 2026-10-11, owner: "add a fourth and fifth hero onto the team … a 5 player team seems about right for max")

`MaxPartySize` = **5**; a new game's five are picked on `CharacterSelectScreen` (§25.15) rather than fixed. **Amended 2026-10-19 (owner, GG-T118):** the cap stays 5 but a new game starts with **three**; the other two join at story beats ([§25.17](#2517-small-start-story-recruits-gg-t118)). Recruits and rescued guests wait in reserve when the party is full. Camp shows the five in a two-column grid; class blurbs render their [keywords] as highlights.

### 25.15 Character select (added 2026-10-11, owner: "unlock all character and select a random cross section of heros, make character selection like a fighting came where you have a grid at the bottom then you select a tile then the full artwork appears on top and you lock in that character")

`New Game → slot` now pushes `CharacterSelectScreen` instead of calling `Progression.NewGame` with the fixed five directly. Every hero-team class in content (62, not just `StartingRoster`) is an open tile in a scrollable grid at the bottom — tiles are 164px with an even 10px margin on all sides and the row centred (`Justify.Center`), so the gutter reads the same left, right and between tiles regardless of how many columns fit or the window's width. Tapping a tile previews it — full `Art.Portrait` art, name, role badge, description — in a reveal panel above, framed in that role's colour, which **slams** into place (oversized and tilted, a `Tween.Back` overshoot down to rest plus a small landing shake and an impact cue) rather than popping in cold, matching the cut-ins' bombastic feel (bible law: no single-frame swaps). Tapping the same tile again (or "Lock In") commits it to the party strip at the top. **Amended 2026-10-19 (GG-T118):** three locks (`Progression.StartingPartySize`; a hint under the header says more heroes join along the road), not five ([§25.17](#2517-small-start-story-recruits-gg-t118)); the last lock → `Progression.NewGame(c, name, party)` (an overload taking the chosen roster; the old 2-arg form still exists for automation/tests and defaults to `StartingRoster`). `-demo` drives the identical screen itself (`auto: true`): it previews a random not-yet-picked class, pauses, locks it in, once per starting hero, so every attract-mode run shows a different squad. *(Verified: `tools/harness.py` launch, screenshot of the live grid showing even gutters, and a frame-by-frame `record` capture of the reveal panel mid-slam — tilted/oversized mid-flight, not instant.)*

### 25.16 Diagonal slip (added 2026-10-11, owner: Terra Battle's fast-swipe diagonal move — "if you moved quickly and accurately you could move diagonally between two squares… it takes skill… allow the game to skip the collision detection")

Player hero drags only (never AI — enemies/bots use the separate `StepEnemy`/`EnemyPlanner` path and never submit a diagonal step). Gated by `BattleConfig.DiagonalSlipEnabled` (mutable, default **on**; `-noDiagonalSlip` at the command line turns it off). A diagonal step is legal, destination tile fully free (no unit, no movable — never a swap, never onto anything: a glide into open space, same "two units never share a tile" invariant §(tile-conflict safety net) protects), with no softened "at most one corner" rule — the two flanking tiles' occupancy is never consulted, matching the owner's framing of the original as skipping a check rather than a designed exception. `WalkPath` (`BattleRunner.Combat.cs`, the single path both the committed move and the live drag-ghost preview run through) accepts `Coord.IsDiagonalNeighbor` alongside the existing cardinal check when the flag is on. The client (`BattleScreen.Input.ExtendPath`) only ever offers a diagonal candidate when the raw drag gesture between polled pointer-move frames is both fast (> `Tile×6` px/s) and close to a true 45° (within 12°) — a careful/slow drag always still resolves as today's safe two-cardinal-step route, so the skill gate is pointer-timing, not a toggle the player can rely on deliberately. *(Tests: `Diagonal_is_not_a_pincer_and_diagonal_step_is_refused_when_slip_is_disabled`, `Diagonal_slip_glides_onto_a_free_tile_when_enabled`, `Diagonal_slip_is_still_refused_onto_an_occupied_tile_even_when_enabled`. The engine contract is machine-verified; the client's speed/angle gesture gate is pointer-timing and was confirmed by hand, not by the automation harness — its synthetic `drag` endpoint deliberately smooths input into ~30px sub-steps for clean recordings, which never reproduces a real fast flick, so a drag through it always falls back to the safe two-step route exactly as before, with zero regression.)*

### 25.17 Small start, story recruits (GG-T118)

Owner 2026-10-19: "New Game picks a small party (2-3 heroes) instead of 5; further heroes join at story beats through dialogue." The party cap stays **5** (§25.14).

- **Start:** `Progression.StartingPartySize` = **3**. Character select locks in three (§25.15); the 2-arg `Progression.NewGame` (automation, tests, Test Mode) uses `StartingRoster` = Paladin, Barbarian, Cleric (Tank, Striker, Healer). `-demo` auto-picks three.
- **Story recruits:** a dialogue effect `JoinHero` (§27.1) — `Id` (preferred class) and/or `Role`. `Summon.StoryPick` resolves it so it **always brings someone new**: the named class if the player doesn't have it, else the first unowned hero of that `Role`, else one of a role the roster lacks (Healer, Tank, Mage, Support, Striker, Ranged), else anyone unowned (none left → no-op). The fallbacks skip the Summon Circle's pool and every stage guest, so those stay worth having; name order makes it deterministic, and the dialogue's `"$recruit"` speaker is bound to the same pick when it opens, so the hero who speaks is the hero who joins. The recruit arrives free at the roster's average experience (`Summon.Join`, like a Summon Circle recruit) and takes an open party slot, else waits in reserve. Separate from `RecruitHero`, which hires exactly one class and does nothing if it's owned.
- **The beats (`dialogues.json`, real narrative as of GG-T120):** after Green Valley I (before the next launch) — a Mage (Red Mage); at the start of Sandsea Reaches — a Support (Engineer), making five; at the start of Frostmaw Caverns — a Tank (Knight); at the start of Veshker Ruins — a Healer (Sage); the last two go to the reserve when the party is full. Every path through each beat ends in the join (the join is on the last node, not a choice). They sit on top of the guest allies (§25.11) and the Summon Circle (§25.10).
- **Nobody is lost for good:** nothing in the dialogue vocabulary removes a hero.
- **Bots:** Test Mode and the balance sim never show story, but play every pending beat headlessly before a stage (`DialogueRunner.PlayPendingBeatsHeadless`, first choice) so the recruits still join; the Test Mode cycle line reports them as `story: …`.
- **Balance (`BalanceReport.Campaign_pacing_table`, 4 seeds, up to 4 attempts a stage):** the default rows now start with the trio and play story beats. Greedy finishes 4/4 (as before); greedy+camp 3/4 (was 2/4); Green Valley II onward plays like the five-hero start because the fourth hero arrives before it and the fifth at Sandsea Reaches. The only early stage the trio found harder was Green Valley I for the casual bot (30% of attempts vs 66% with five), so it was lightened (§22.7) and is now 80% with 61% HP left. The table keeps a `greedy+camp trio no-story` row (GG-T87's trio-start: no story recruits, so the Summon Circle step always has a slot to fill) and a `greedy five-start (pre-GG-T118)` row as the yardstick.
- *(Tests: `StoryRecruitTests` — the small start, JoinHero's named/role/missing-role fallbacks, the summon pool and guests left alone, a full party sends the recruit to the reserve, the `$recruit` face is the joiner, every path through every recruit beat adds exactly one hero, story beats grow the trio to five by Sandsea Reaches, recruits arrive at the roster's experience, Test Mode's headless join, JoinHero validation. Harness: `python tools/harness.py recruits` picks three on the real party select, clears Green Valley I and checks the recruit dialogue plays with the joining hero's portrait and the party grows to four; `uicheck` checks a new game starts with three.)*

### 25.13 Roles (added 2026-10-11, owner direction: "a healer should need to heal, a magician should have ranged spells, a tank should be able to taunt")

Every hero class has a `Role` (validated): **Tank** (9), **Healer** (8), **Mage** (6), **Striker** (31), **Support** (8), **Ranged** (2). The role sets the default bar and the learn list; caster power (§13.1.5) makes the stats matter.

**Amended 2026-10-13 — archetype sub-kits:** each role (except the 2-member Ranged, not yet subdivided) splits into 3-5 WoW-style sub-archetypes that play differently within the same role (owner: "every combination of tanking type... find Paladin, Warrior, and Druid do things differently"). Every class keeps slot 0 (the role's shared baseline, e.g. Tank's `taunt`) and slot 2 (`item:x_potion`) in common; slots 1 and 4 carry two bespoke abilities per sub-archetype, and slot 3 is the class's own unique signature (unchanged from the original per-class kit pass). A fourth bespoke ability per cluster ("Core") is learnable but not in the starting bar, so there's room to customize without disturbing the default. Sub-archetypes: Tank — Aegis Guard (pure mitigation + cleanse), Juggernaut (knockback aggression), Warlord (party buffs), Deathguard (drain/poison); Healer — Aegis Wardens (preemptive wards), Hexmenders (damage-for-heal), Skirmish-Menders (mobile support); Support — Primal Pack, Tempo Weavers, Field Engineers; Mage — Spellblades, Field Casters; Striker — Berserkers, Duelists, Skirmishers, Combo Brawlers, Attrition, Shadowsteps, Opportunists, Brawlers. Full id list: `tools/icons.json`'s `ability` map, or `docs/USER_STORIES.md` ticket GG-T3.

**Amended 2026-10-13 — a role-wide invariant, found by live play (GG-T14):** every Tank's default bar must keep at least one damage-dealing ability. A lone tank (party wiped around it) can't pincer by itself, so if every bar slot is non-damage or on cooldown/unaffordable, it can soft-lock against a weak enemy neither side can finish — this actually happened to the Aegis Guard cluster's first cut (fixed by swapping its Capstone for its own damage-dealing Core ability). Asserted by `SynergyTests.Every_hero_has_a_role_and_kits_match_it`.

**Amended 2026-10-13 — Ranged (new 6th role):** a precision-attacker archetype distinct from Striker (melee, `AdjacentOnly`) and Mage (AoE elemental) — no ability in a Ranged hero's bar may be `AdjacentOnly`. Two classes so far, `hexsniper` and `hexwarden`, reusing the portraits of the existing `Ranged`-tagged enemies `hag00`/`hag01` (`AttackRange` 3) rather than new art — see GG-T11.

Universal cross-class pool (added 2026-10-13, GG-T4): `AbilityDef.Universal` — any hero can learn a `Universal`-flagged Skill regardless of role, extending the pre-existing rule that every `Spell` is already learnable by any class (`Abilities.Learnable`).

| Role | Job |
|---|---|
| Tank | draw the enemies, protect casters, at least one damage option always in the bar |
| Healer | mend and cleanse (WIS) |
| Mage | ranged elemental AoE damage (INT) |
| Striker | pincer damage, melee skills (STR) |
| Support | tempo — Quicken allies, Slow foes, rally the party |
| Ranged | precision single-target/line damage that never needs to be adjacent |

The starting trio is a full triangle: Paladin (Tank), Barbarian (Striker), Cleric (Healer). Role badges show on the Camp cards, Party, Status (with a one-line "what it's for") and the battle panel. The Abilities screen can **move** a slot along the bar (`Abilities.Swap`). *(Tests: `Every_hero_has_a_role_and_kits_match_it`, `The_bar_can_be_reordered_within_unlocked_slots`.)*

## 26. Responsive Design & Aspect Ratio

### 26.1 Reference resolution

**1170 × 2532** portrait (`GameApp.DesignWidth/Height`) is the **minimum** stage: every screen must fit in it. The live size is `GameApp.StageWidth/StageHeight` (design pixels), between 1170×2532 and 1424 wide (`MaxStageWidth`, 9:16) or 2730 tall (`MaxStageHeight`, 9:21). Screens lay out vertically from the top in fixed design pixels and horizontally against the live width (left/right insets, or centred on `StageWidth`), so extra width or height is absorbed, never cropped.

### 26.2 Panel scaling

`PanelSettings`: ScaleWithScreenSize, MatchWidthOrHeight; `GameApp.FitStage` sets the reference resolution to the live stage size and `match` to the limiting axis whenever the screen size changes, then raises `GameApp.StageResized`.

### 26.3 Adaptive stage (GG-T169)

**Amended 2026-10-21 (owner: "on-device experience is king — fill every phone, frame tablets, never warp"):** `GameApp.StageSizeFor(aspect)` picks the stage: on a screen wider than 19.5:9 the height stays 2532 and the width grows to match, up to 1424 (9:16); on a taller one the width stays 1170 and the height grows, up to 2730 (9:21). Inside 9:21…9:16 — every mainstream phone — the stage fills the screen edge to edge with no bars. Outside it (tablets 3:4/2:3/10:16, ultra-tall shapes) the stage clamps to the nearest supported shape, is centred, and a `stage-frame` outline (the same white rounded frame every window uses) sits 10 px outside it over a near-black bezel, so the bars read as deliberate. Scaling is always uniform.

- **Battle:** the board is centred on the stage width (`BoardLeft = (StageWidth − 1080)/2`, 45 at the minimum), the timeline stretches (`StageWidth − 230`), cut-in anchors mirror against `StageWidth`; the bottom info panel is pinned top and bottom, so it absorbs extra height. `BattleScreen.ApplyStageLayout` re-applies these on `StageResized`.
- **Automation:** design-pixel taps map through the live stage width; `stagebounds` reports `design` (live stage size) and `framed`.
- **Verification:** `python tools/harness.py aspects` launches at 9:23, 9:21, 9:20, 9:19.5, 9:18, 9:16, 2:3 and 3:4, shoots title/hub/battle, runs the layout audit, and fails if the stage drops below the minimum or the frame state doesn't match the rule.

### 26.4 Camera

One UI-only camera (`cullingMask = 0`, black clear); the board is UI Toolkit, so there is no orthographic framing.

### 26.5 Safe area

`GameApp.ApplySafeArea` insets the `screens` container where `Screen.safeArea` (notch, home bar) overlaps the stage — which on a phone that fills the screen is every time. `-safeArea <topPx>,<bottomPx>` simulates one on desktop.

---

## 27. Story crawl (no dialog)

`StoryCrawlScreen`: the theme name (Cinzel) and its `campaign.json` `Crawl` text scroll upward at 95 design-px/s over a dimmed theme backdrop; **Skip ›** or a tap anywhere ends it and replaces it with the battle. It plays on the **first** launch of any stage in a theme — from Adventure or from the results screen's "Next stage ›" (both go through `StageLauncher.Launch`); the theme is recorded in `SaveData.SeenCrawls` (and saved) when the crawl starts. **Amended 2026-10-09** (timing by scroll speed instead of a 26 s duration). Writers edit `campaign.json` only.

~~No dialog system: no character dialogue, no branching, no cutscenes.~~ **Amended 2026-10-19 (owner, GG-T117):** character dialogue now exists ([§27.1](#271-dialogue-gg-t117)); there are still no cutscenes. The in-battle "surprise" reinforcement beat ([§14.8](#148-reinforcements-and-eggs-added-2026-10-12-owner-direction)) stays a lightweight one-line announcement card.

### 27.1 Dialogue (GG-T117)

- **Content:** `dialogues.json` — a list of `DialogueDef` (id, `Title`, `Trigger`, optional `Conditions`, `Once`, `Backdrop`, `Speakers`, `Start`, `Nodes`). A speaker is a class (its name and portrait), `"$lead"` (the first party member), `"$with"` (the party member a Talk is with), `"$recruit"` (the hero the dialogue's first `JoinHero` brings — fixed when it opens, so the face that speaks is the hero who joins; GG-T118), or a `Name` + `Portrait` (`Resources/Portraits/<id>`). A node is a speaker and a line, then either `Next` (null ends it) or `Choices` (label, `Next`, `Conditions`, `Effects`); a node may carry `Effects` applied on arrival.
- **Vocabulary (the save's, not a script):** conditions `FlagSet`, `FlagNotSet`, `HasItem`, `GoldAtLeast`, `PartyHas`, `RosterHas`, `RosterLacks`, `NpcJoined`; effects `SetFlag`, `ClearFlag`, `GrantGold` (negative = pay, never below 0), `GrantItem`, `RecruitHero` (joins like a Summon Circle recruit, free; no-op if owned), `JoinHero` (GG-T118, story recruits: `Id` and/or `Role`; always brings someone new — `Summon.StoryPick`, [§25.17](#2517-small-start-story-recruits-gg-t118)), `JoinNpc` (sets the flag `npc:<id>`, which `ClearFlag` can't touch; NPCs: blacksmith, alchemist, enchantress, merchant, innkeeper). Nothing removes a hero — nobody dies permanently. Flags live in `SaveData.Flags`; finished dialogues in `SaveData.SeenDialogues` (cleared by New Game+, like `SeenCrawls`).
- **Validation (`ContentLoader.Validate`, same severity as a dangling class id):** unknown speakers, classes, items, NPCs, themes, stages or node ids; a missing portrait (the app and dotnet tests pass a portrait lookup); a node unreachable from the start; a node with both `Next` and `Choices`; a choice list with no unconditional choice (so a player can never be stranded); a `JoinHero` with neither `Id` nor `Role`, a non-hero `Id` or an unknown `Role`; a `"$recruit"` speaker in a dialogue with no `JoinHero`.
- **Engine:** `Gaiden.Meta.DialogueRunner` walks nodes, offers the choices whose conditions hold, applies effects, and marks the dialogue seen when it ends (dotnet-tested, `DialogueTests`). `DialogueRunner.PlayHeadless` plays one to the end taking the first choice each time, and `PlayPendingBeatsHeadless` plays every beat pending before a stage — how the bots (Test Mode, `BalanceReport`) live through the story they never see (GG-T118).
- **Triggers:** story beats — `ThemeStart` (theme id) and `StageCleared` (stage id) — play once, in file order, through `StageLauncher.Launch`: after the theme's crawl, before the battle. **Amended 2026-10-19 (GG-T119):** every beat pending at a launch plays, one after another (each picked as the last ends), not just the first — a story recruit and a Camp NPC can share a stage, and the app now plays the same set the bots play headlessly. Never on a Test Mode save. **Talk** — Camp's Talk row (`TalkScreen`) lists the party and every joined NPC; each offers its own `Talk` dialogue (`"party"`, a hero class id, or `"npc:<id>"`), unseen ones first and marked **New!**; Talk dialogues repeat unless `Once`.
- **Screen (`DialogueScreen`):** the speakers' portraits overlap across the top — the one talking slides in full-bright and in front, the other dims and shrinks behind; the conversation stacks below as overlapping boxes that slide in from the speaker's side, older ones dimmed, the newest typing out; the choices (or **Next ›**) sit at the bottom. A tap finishes the line or advances; **Skip ›** rushes to the next choice — choices are never skipped. Effects toast as they land. `-demo` plays it through on its own.
- Sample content: a merchant met at the start of Green Valley (every branch ends with him joining), four story-recruit beats (GG-T118, [§25.17](#2517-small-start-story-recruits-gg-t118)), a Talk with the merchant (hires a sellsword for 300 G) and a generic party chat. **Amended 2026-10-19 (GG-T119):** the beat after Green Valley I now meets the Innkeeper, and beats after Green Valley II, at the start of the Sandsea Reaches and of the Mireholt Fens meet the Blacksmith, Alchemist and Enchantress (§25.0.1); each NPC has a Camp Talk.
- **The real story (GG-T120, 2026-10-19):** every theme's `Crawl` is rewritten narrative, merging the Longmother draft (`docs/story-draft-spider-town.md`) with the biomechanical-plague pitch (`docs/design-draft-overworld-and-dialogue.md` §1) — the town rides the Longmother, an ageless spider-colossus who can never stop walking because standing ground sours into a blight that fuses the living with the grown-and-built; she can't recross ground she's already woken, so her route curls rather than backtracks, which is why the five postgame themes ("Deeper In", "Further Out", "Past the Mire", "Lower Depths", "Inner Wards") revisit each biome's *kind* of ground, never the same ground twice. Magic fraying near the worst of the blight, machines not caring, is threaded through the join scenes and party talk (why the Alchemist/Enchantress/Blacksmith matter) without adding a new mechanic yet (open question in the design draft). Every NPC and story-recruit join scene (§25.0.1, §25.17) is rewritten in-voice, and a new `*_party_talk` `ThemeStart` dialogue per theme (10 total, one per `campaign.json` theme) gives the party a short beat of banter that carries a piece of the plot forward — placed after that theme's existing beats in `dialogues.json` so trigger order (tested by `DialogueTests`) is unaffected. The Vampire Lord (Veshker Ruins III, the campaign's climax) is reframed as the city's ruler who refused to stop and found a way to keep "stopped" going past when it should have killed him; the five postgame themes are the road continuing past that climax, deliberately left open (no cure, no final answer) per the design draft's own open questions.
- **PPP prose universe seed (GG-T137, 2026-10-20):** `docs/ppp-universe/` gives every hero class, enemy family, Camp NPC and theme/location a short bio, voice and "what they need advice about," grounded in the content above plus the shipped `dialogues.json`/`campaign.json` text. It is prose source material only — nothing in it is loaded or validated by the game — written to feed a future GG-T133 Talk-quiz pass and further GG-T120-style party-talk beats, and flagged as the seed for a later authoring pass in the Prose repo once that repo's shape for a game-tie-in universe is confirmed.

## 28. No Overworld

There is no world map. Stage navigation is the Adventure list ([§22.3](#223-adventure--the-stage-list)). **Confirmed 2026-10-20 (owner, GG-T115):** permanent, not contingent on GridGameCaravan's traversal prototype picking a demo — the earlier pitch for a navigable overworld and terrain gates ([design-draft-overworld-and-dialogue.md §2/§2.1](design-draft-overworld-and-dialogue.md), withdrawn) is not coming back. The journey is understood without one: the party is travelling, and the story is told by text crawls ([§27](#27-story-crawl-no-dialog)) and dialogue ([§27.1](#271-dialogue-gg-t117)). GridGameCaravan, the traversal lab that would have fed this, is spun off as its own project ("TownWalker"), not a Gaiden prototype.

## 29. Open Design Questions

Resolve a question → write the answer into the right section → delete it here.

### 29.1 Macro loop / run structure
1. **Permadeath / NG+** — **Answered 2026-10-19 (GG-T114, owner):** never in the normal game — "no one dies permanently, that's lame". Defeat returns to Camp at full HP; fallen heroes recover; a fallen guest returns (§25.11); recruits and dialogue-joined heroes are never removed. **Ironman** is a *planned, opt-in* mode ("leave the door open but not right now") and is **not built**: every "unit is lost" decision already routes through the one rule `Permadeath.Enabled` (`Gaiden.Meta`, always `false`; fed to battles as `BattleSetup.Permadeath`), so Ironman only has to switch that on. Still open: what else Ironman changes (fallen heroes too? one save slot?).
2. **Tutorial / onboarding** — a guided first battle? Partly answered: Settings has "How to play" and the first two campaign stages show idle-time move hints (§9). A scripted tutorial is still open.

### 29.2 Timeline feel (new with the act-advanced clock)
3. **Frozen length** — should Frozen survive into the next action (Duration 2, or tick at the bearer's own boundary)? Today it ends with the action it lands in (§8.3).
4. **Time bank** — should banked seconds persist until the next enemy turn instead of clearing at control return (§3.1.8)?
5. **ActionTicks** — 2.0 s per action makes every 1.5 s spell resolve in-action; keep, or lengthen cast times so casts visibly ride the timeline?

### 29.3 Content economy
6. **Inventory cap** — unbounded apart from `MaxStack`; is a total cap wanted?
7. **Durability per instance** — keep the reset-on-unequip simplification (§24.5)?
8. **Item naming** — Hi-Potion, X-Potion and Ether use classic Final Fantasy item names, the same
   copyright concern that prompted renaming Phoenix Down → Revival Charm (2026-10-18); revisit
   whether these should also get original names.

## 30. Performance Budgets

Target: **60 fps** on a 4-year-old mid-tier phone; `GameApp.Awake` pins `targetFrameRate = 60`, `vSyncCount = 0`, and the automation `state` endpoint reports fps.

- **Simulation** costs nothing per frame — it runs only inside `Apply`, one burst per action. `PreviewMove` clones the whole `BattleState` per drag step and `HeroBot` previews every L-path for every hero: fine on cold paths, the first thing to profile on device.
- **Presentation** plays one awaited beat at a time (budget ≤ 3 ms); **UI Toolkit** layout/render ≤ 8 ms — rebuild a screen only on change (`Refresh`), never per frame; keep ≥ 2 ms slack.
- **Hygiene:** all playback runs through `Safe()` with a 20 s watchdog and a final `SyncAll`. **Amended 2026-10-09:** there is no per-battle static state to clear — everything lives on `BattleState`, which dies with the screen.
- **Assets:** `Resources` loading is sanctioned (backdrops 720×1440 JPEG, thumbs 256², icons 64²; music mp3, SFX ogg/wav).
- ⬜ Not yet built: the on-device profiling pass.

---

## 31. Accessibility

- **Color-blindness:** mana has one color by design (filled vs empty reads by brightness); buff pips carry letters. **Colorblind palette** (`SettingsSave.ColorblindMode`) remaps buff pip colors.
- **Motion:** 🟡 **Reduce motion** (`SettingsSave.ReduceMotion`) zeroes lunges, shakes and the death-portrait jitter and dims the Clutch flash (`BattleScreen.Fx`); **Battle speed** ×1/1.5/2/3 shortens every beat.
- **Touch targets:** slots 216×196 and tiles 180² design px clear 44 dp at every supported scale; a dragged hero lifts above the finger.
- **Readability:** 40-px body text, white on deep-blue windows, HP as gauge + numbers on the panel.
- **Audio:** every important event also has text. 🟡 Music/SFX volume cycles in Settings; `MuteMusic`/`MuteSfx` exist in the save and are honored by `Audio.Apply` but have no toggle.
- **Menu themes (added 2026-10-11, owner: "the gradient on all the menus is generated... make multiple Themes... updated in Settings and remembers it"):** `Art.MenuTheme` is two chosen base colors (`ColorA`/`ColorB`); the accent used for every window's border (`Ui.Window`) is never hand-picked — it's the complement of the two base colors' circular-mean hue, pushed toward full saturation/brightness since it frames thin borders, not large fills (`Art.MenuTheme`'s constructor). Presets (`Art.Themes`): **Classic** (the original deep blue, now just the default preset), **Cyberpunk** (magenta → neon cyan, gold-green accent), **Ember** (mindattic.com's coral → teal), **Verdant** (MindAttic.Launcher's green → near-black). `SettingsSave.MenuTheme` persists the pick; "Menu theme" in Settings cycles it (`Art.ApplyTheme`, called from `GameApp.ApplySettings` on every load and every Settings change) and repaints the shared `Art.Window`/`Art.WindowDark` gradient textures **in place** — every already-open window updates with no rebuild, since they all hold the same `Texture2D` reference.
- **Theme USS classes (GG-T185, epic W):** a preset may also name a root USS class (`Art.MenuTheme.UssClass`). `Ui.ApplyMenuTheme` (called from `GameApp.ApplySettings` right after `Art.ApplyTheme`) puts the active theme's class on the UI root and removes every other theme's, and re-tints every open `.window`'s frame; Theme.uss scopes each such theme's frame, tab, button, header and toast rules under that class at the end of the file, so the whole look switches live with no rebuild. A theme with a class owns its window frame in USS (`Ui.Window` sets no inline accent border for it). Adding a theme is one row in `Art.Themes` plus one `.theme-<name>` block in Theme.uss. **Slate** (`theme-slate`) is modeled on Chrono Trigger's default window style: a gray-blue slate gradient body, a light bevelled frame (light top/left edge, darker bottom/right), white text, slate tabs and buttons with the same bevel; primary (gold) and danger (red) buttons keep their colors. The harness sets it with `call setting key=menutheme value=Slate`. **Dark** (`theme-dark`, GG-T186) is the DocFX modern dark docs / mindattic.com look on MindAttic.Components' `web/themes/Dark/dark.css` tokens: flat `#13161b` windows (both gradient stops the same) on a `#0d0f12` stage, `#2a2f3a` hairline frames on windows, tabs, header, buttons and toasts, one `#6ba3e8` accent (selected rows, active tab's top edge, primary buttons with dark text), `#e8eaf0`/`#9aa0b0` text and no text shadows; gold, good/bad and rarity colors stay semantic. `python tools/harness.py sweep RUN Dark` sweeps every menu screen in it.
- **Fonts (added 2026-10-11, owner: "allow a selection of different Fonts to change look and feel"):** `Fonts.Presets` (`Assets/Gaiden/App/Services/Fonts.cs`) — **Classic** (Outfit, the original/default), **Lato**, **Roboto**, **Play**, **Montserrat**, **Inter**. Calibri and Aptos were named but are Microsoft-proprietary (not freely bundleable); Inter fills that "clean modern UI sans" role instead. "Attic" isn't a real distributable family; Montserrat fills that slot (its Google Fonts build is a variable font whose default instance renders a bit lighter than Regular — a known, purely cosmetic quirk, not a bug). Only the **body** font is selectable; the Display font (Cinzel, used on ~7 headline/name labels) stays fixed across every preset. `SettingsSave.FontName` persists the pick; "Font" in Settings cycles it the same way "Menu theme" does (`Fonts.ApplyFont`, called from `GameApp.ApplySettings`). Unlike themes, no texture-repaint trick is needed: `unityFontDefinition` is an inherited UI Toolkit style property, so re-applying the body font to the UI root (`Fonts.UseBody`) cascades to every element that doesn't explicitly override it, instantly, with no screen rebuild. Roboto/Montserrat/Inter are Google's current variable-font TTFs (no static cut is published any more); Unity's dynamic SDF pipeline (`FontAsset.CreateFontAsset`) renders their default instance correctly.
- Status: colorblind 🟡 · reduce motion 🟡 · volume 🟡 · menu themes 🟡 · fonts 🟡 · safe area ⬜ · tooltips ⬜.

---

## 32. Document Discipline

- Every gameplay-affecting change updates this bible (add, amend or verify); complex mechanics are written here first, then coded. Numbers recorded here follow `BattleConfig` / content changes.
- Removed mechanics are deleted (or marked RETIRED with a pointer, as GG-LAW-3/4). Git is the history.
- A deviation from the GridGame2026 bible gets an inline **Amended YYYY-MM-DD:** note and a bullet in [`AMENDMENTS.md`](AMENDMENTS.md); status changes go to [`USER_STORIES.md`](USER_STORIES.md) with evidence.
- What the game IS lives here; why we deviated lives in AMENDMENTS; what is pending lives in USER_STORIES + §29; what a session debugged lives only in that conversation.
- Readers: the owner (sanity check), future sessions, unattended agents (the spec), new contributors. A section one of them can't read without context is a bug.

— end of document —
