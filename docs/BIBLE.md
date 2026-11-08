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
- **The Timeline runs while you hold a hero.** Every icon flows **right→left** along one bar of seconds and acts when it reaches the left edge, where a large diamond shows who acts next (**Amended 2026-11-06**, GG-T286/GG-T287, Final Fantasy Resonance–style: no Zone). The clock stands still while you look; picking up a hero starts it in real time, and your turn lasts until the next enemy icon reaches the left edge — then the hero drops where it is and that enemy acts (dropping earlier ends the turn early). A pincer pushes every foe it hits **1 s** back, and abilities with the **Pushback** keyword push up to 2 s, anywhere on the bar. ([§2](#2-the-timeline), GG-T188)
- **Shared, visible mana.** A 12-orb `ManaBank` of generic orbs is the whole party's spell budget; pincers, crits, steals and enemy-cast interrupts refill it. ([§3.1](#31-mana-the-orb-economy))
- **Casts ride the Timeline.** A spell with a cast time is a diamond on the same bar as the turns, born at its cast time, and fires when it reaches the left edge; every hit on the caster staggers it, enough stagger cancels it, and a rare LCK Clutch lets a dying healer let off one last miracle. ([§13.4](#134-the-interrupt-path))
- **Beyond the battlefield.** Five themed campaigns of hand-built stages with a skippable story crawl per theme, a Final Fantasy–style **Camp** menu (merchant, blacksmith, alchemist, inn, abilities, party, summon circle, bounties, bestiary), weapon durability with shatter rebound. **Amended 2026-11-07 (GG-T341):** the story is *Under Morrow* ([STORY.md](STORY.md)) — a crew of tithe-runaways in a stolen Concord caravan, under a moon that hangs too low and a Concord that drinks the holds dry; crawl text, NPC/hero join scenes, party talk and Advice quizzes are written from it (the GG-T120 story is retired). ([§22](#22-the-macro-loop), [§25](#25-the-camp-merged-hub), [§27](#27-story-crawl-no-dialog))

Target pacing: a battle is 15–40 committed actions (~2–5 minutes), a stage ~3–4 min, a session 20–30 min. ([§0.3](#03-session-shape-target-pacing))

## 3. What it is NOT {#GG-§3}

GridGame Gaiden is **NOT**:

- **Not real-time, and not alternating turns.** The clock runs only when you commit an action, so thinking time is free; but enemies act when their icons arrive, which may be zero, one or several times per action. **Amended 2026-10-09:** replaces "thinking time is game-clock time" ([§0.2](#02-non-goals-what-this-game-is-not)).
- **Not a deck-builder** — AbilityBars are deliberate loadouts, not randomized hands.
- **Not a roguelike** — no permadeath, no procedural runs; stages are authored, saves persist.
- **Not a gacha / live service** — no pulls, no energy, no premium currency. Heroes are recruited deliberately for gold at the Summon Circle. **Amended 2026-11-07 (owner, GG-T343):** *"another way is to simply summon them randomly with Diamonds monetized currency"* — the Summon Circle also offers one **random draw** for a Diamond ([§25.10.2](#25102-random-summon-for-diamonds-added-2026-11-07-owner-gg-t343)) beside the deliberate gold summon. Diamonds still come only from the existing LuckyDiamond paywall hook (1 g · 1 s, [§25.18](#2518-rested-and-the-paywall-hooks-gg-t232)); there is no store, no real-money purchase and no pity system ("until I call it monetize, it's not").
- **Not multiplayer** — solo offline; no co-op, PvP or leaderboards.
- **Not a grid-puzzle** — no match-3 piece-color matching; tile state is "who's standing there".
- **Not free-form movement** — one tile at a time, cardinal only, no pathfinding or diagonals.
- **Not a stat-spreadsheet** — if a player needs a wiki to build, the design failed.
- **No world map.** Narrative is a skippable per-theme text crawl ([§27](#27-story-crawl-no-dialog)) plus portrait dialogue at story beats between battles and from Camp's Talk row ([§27.1](#271-dialogue-gg-t117)); stage navigation is the Adventure list ([§28](#28-no-overworld)). **Amended 2026-10-19 (owner, GG-T117):** "no dialog system" withdrawn. **Confirmed 2026-10-20 (owner, GG-T115):** the separate overworld pitch ([design-draft-overworld-and-dialogue.md §2](design-draft-overworld-and-dialogue.md)) is withdrawn — a world map "takes away from a quick sit-down game." The game is four kinds of screen: fighting, in-town buying/upgrading, reading a text crawl, or talking with people (NPCs, party). **Amended 2026-11-02 (GG-T75):** the caravan crew's one-line barks on Camp and the Caravan screen ([§25.12.1](#25121-the-caravan-crew-and-their-barks-gg-t75-added-2026-11-02-owner-direction-2026-10-16)) are flavor text, not a fifth kind of screen and not dialogue: no branching, no choices, no overworld.

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
  - **It uses the whole bar (GG-T246, 2026-11-05, owner: "the Abilities need to be utilized in the gameplay demos, to see if they are useful or not"):** a spell's mana price falls as the shared bank fills (`OrbPenalty`: 1.5 per orb when empty, down to 0.3 near full and 0.2 when full, since every orb minted into a full bank is lost). Every effect is valued from its own numbers:
    - foe debuffs by what the BuffDef does (halt/immobile, slow, vulnerability, blind, silence, weakness, damage over time)
    - ally buffs by damage-up, damage-taken-down on an exposed ally, regen on a hurt one, haste
    - knockback (more when it peels a foe off a hurt hero), pull, swap, step-back when hurt and engaged
    - revive with someone down, ethers only into a bank with room, single-buff cures (the first-scan-per-class score went with Scan, GG-T290)

    A clean pincer stays the strongest move, so abilities win only on real value. Measured with `AbilityUsageSoak` + `records.py --abilities` (§22.6, `docs/research/AbilityUsage.md`).
- `CampRoutine.Run` (Meta) + `MetaHeuristics` (Core/Bot/MetaBot.cs) — "what a sensible player does at Camp" (repair, rest, claim/accept bounties, recruit into an open party slot, forge/equip better gear, learn a spell, fill empty slots, restock potions) through the same services the screens call; drives the campaign balance simulation.

### 4.4 Content is data {#GG-§4.4}

**Amended 2026-10-09:** every class, ability, buff, item, recipe, drop table, stage and campaign is JSON under `Assets/Gaiden/Resources/Content/`, loaded by `ContentLoader.Load` (Newtonsoft, `MissingMemberHandling.Error`, string enums) and cross-checked by `ContentLoader.Validate` at boot (problems are logged by `GameApp.LoadContent`; the test suite fails on any).

| File | Holds |
|---|---|
| `classes.json` | 172 `ClassDef`s — stats, growth, milestones, tags, resistances, learnable abilities, default loadout, drop table, move range, XP reward, boss phases, charge ability/shape, trap, snake segments, footprint, lore/card text, portrait crop |
| `abilities.json` | 25 `AbilityDef`s — cost letters, cast time, cooldown, shape/radius/mode/filter, motion, damage type/base, heal, buff + chance, cleanse, steal, hasten, Pushback (seconds), teleport, VFX/SFX keys (Scan retired, GG-T290) |
| `buffs.json` | 10 `BuffDef`s — kind, unit (Turns/Ticks), duration, DOT, immobile, halts timeline, breaks on damage/move, incoming-damage / timeline-speed / accuracy multipliers, blocks spells, expire chain, letter, color |
| `items.json` | 231 `ItemDef`s (155 equipment, 55 materials, 18 consumables, 3 recipe scrolls; GG-T45 added `mat_pure_slime_gel` + Greater Healing Potion/Antidote) — type, rarity, slot, cost, stack, durability, healing/damage, stats, battle-start orbs, on-use ability, orb grants, cures, revive, escape, camp-only, bonus-vs-tag, resistances, vendor flag |
| `recipes.json` | 142 `RecipeDef`s — station (Forge / Alchemy / Enchant), inputs, gold, optional base item, result; some Alchemy recipes also carry a potency variant ([§24.6.4](#2464-reagent-potency-gg-t45-owner-direction)) |
| `droptables.json` | 37 `DropTableDef`s — optional coin range, weighted item entries (chance, min, max); one per enemy family, each carrying that family's own signature material (added 2026-10-15, GG-T43) |
| `stages.json` | 19 `StageDef`s — 15 campaign + 4 test (`IsTest`), theme, waves of `SpawnDef` (class, level, optional fixed tile); a wave may instead name an encounter group (`WaveDef.Group`) |
| `encounters.json` | 182 `EncounterGroupDef`s (GG-T184) — one enemy family's mix on fixed enemy-half tiles (a named formation: Pincer, Wedge, Crown ...) at a level; 2-5 foes, every Crucible family has a group of every wave size. `EncounterCatalog.Validate`: no two groups are the same foes on the same tiles, no two campaign waves are the same foes, levels and tiles, a group backs one campaign wave at most |
| `campaign.json` | 5 main themes × 3 stages + 5 postgame themes × 2 bonus stages (added 2026-10-13, GG-T7/T10; each bonus stage carries a `StageDef.LevelOverride` instead of relying on flattened-index `RecommendedLevel`), in play order, with each theme's story-crawl text |
| `traps.json` | 1 `TrapDef` (`venom_snare`: 6 dmg + Poisoned, lay chance 0.45) |
| `bounties.json` | 5 `BountyDef`s — target class, kill count, gold + item reward, optional skill points (GG-T134) |
| `summon.json` | Summon Circle pool (7 classes), base price 250, step 250 |
| `enchants.json` | 6 `EnchantDef`s (Flame/Frost/Spark/Shadow/Poison/Holy, the last 2 added 2026-10-13) — reference data; the actual enchant crafts are `Station: Enchant` recipes |

### 4.5 The command → event contract

A **command** (`Core/Battle/Commands.cs`) is a player decision: `MoveCommand` (hero id + path of cardinal steps + drag-timer seconds left), `UseSlotCommand` (hero, slot, target tile) or `ShieldCommand`. `BattleEngine.Apply` resolves it synchronously and returns an ordered list of `BattleEvent`s (`Core/Battle/BattleEvents.cs`: `Stepped`, `Displaced`, `PincerFormed`, `Attack`, `Damaged`, `Healed`, `Died`, `Loot`, `OrbMinted`, `OrbsSpent`, `TimelineAdvanced`, `PushedBack`, `Hastened`, `TurnStarted`, `TurnSkipped`, `CastQueued`, `CastStaggered`, `CastCancelled`, `Clutch`, `AbilityResolved`, `AbilityRefused`, `ItemUsed`, `BuffApplied/Ticked/Removed`, `Announce`, `TrapPlaced/Triggered`, `WeaponWorn/Shattered`, `BossPhase`, `WaveStarted/Cleared`, `Revived`, `BattleEnded`, `PlayerControl`). The simulation has already finished when the view sees them; the view only plays them back (`BattleScreen.Play` in `App/Battle/BattleScreen.Playback.cs`, the event player). A refused command emits `AbilityRefusedEvent` and changes nothing — no clock, no cost. The same seed + content + command list always reproduces the same event list.

## 5. The Laws {#GG-§5}

This project **inherits the org-wide house rules** in [`../../MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) — do not restate them here. The most load-bearing for this repo: [`HOUSE-LAW-1`](../../MindAttic.HouseRules.md#HOUSE-LAW-1) whole-number versioning (`VERSION` → `PlayerSettings.bundleVersion`), [`HOUSE-LAW-2`](../../MindAttic.HouseRules.md#HOUSE-LAW-2) soft-disable over delete, [`HOUSE-LAW-8`](../../MindAttic.HouseRules.md#HOUSE-LAW-8) verified-not-asserted done. GridGame Gaiden ships **zero LLM features** — nothing AI lands in `Assets/`.

Project-specific laws:

### {#GG-LAW-1} Movement never deals damage
Sliding a hero through or onto an enemy is **not** an attack. Damage flows ONLY through pincers ([§1.2](#12-pincer-attacks)), enemy attacks, abilities/items ([§6](#6-the-spell-dispatcher)) and environmental sources (DOT, traps, weapon shatter). *(Source: `BattleRunner.WalkPath`; test `Movement_never_deals_damage`.)*

### {#GG-LAW-2} A pincer is two allies flanking an unbroken, ally-free line
Same row OR column, every tile strictly between them covered by opponents — no gaps, no allies, at least one opponent. No diagonal pincers. A 2×2 boss covering several line tiles counts as one opponent. **Amended 2026-11-06 (owner, GG-T289/GG-T294):** the two allies must be **pincer-eligible** (`Unit.CanPincer`): heroes always; an enemy only if it is **Sentient** — it has a society or plans ahead, however rudimentary. Beasts, slimes, yetis, scorpions and mindless undead never pincer; it is about sentience, not undeath ([§1.3](#13-sentient-restriction)). *(Source: `PincerDetector.TryPair`.)*

### {#GG-LAW-3} ~~Code is the source of truth; the `.unity` is the print-out~~ — RETIRED
**Amended 2026-10-09:** there are no scene builders, no `BuilderAutoRebuild`, no `BuilderDriftChecker`. The one scene is an empty shell; `GameApp` builds everything at runtime. Superseded by [GG-LAW-9](#GG-LAW-9).

### {#GG-LAW-4} ~~Guardrails are enforced at pre-push~~ — RETIRED
**Amended 2026-10-09:** the `SerializedFieldBan`, `ResourcesLoadBan` and `InstantiateBan` guardrails are gone. There are no prefabs and no Inspector wiring to guard; `Resources.Load` is the sanctioned loader for content JSON, art, audio, fonts and the UI theme. Superseded by [GG-LAW-9](#GG-LAW-9)–[GG-LAW-12](#GG-LAW-12).

### {#GG-LAW-5} Every spell is a (shape × mode × filter) triad
Adding a spell is a new `abilities.json` row picking targeting axes and effect knobs; it never touches `BattleRunner.ResolveAbility`. *(Source: `TargetResolver.cs`, `BattleRunner.Abilities.cs`; test `Every_hero_ability_resolves_against_a_live_board`.)*

### {#GG-LAW-6} Mana is one shared, visible, capped pool
A single 12-orb `ManaBank` for the whole party — no per-hero MP. Over-minting past 12 fades out (felt as "leaving value on the table"). **Amended 2026-11-02 (GG-T189):** orbs have fractional progress — the one forming bubble (`BattleState.ManaCharge`, [§3.1.9](#319-mana-charge)) fills toward the next orb; a bubble that rolls over while the bank holds 12 mints an orb that fades like any other, keeping the remainder. *(Source: `ManaBank.Add`, `BattleRunner.AddCharge`; tests `Bank_overflow_fades_extra_orbs`, `Guard_bubble_rolls_into_an_orb_and_fades_when_the_bank_holds_12`.)*

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

### {#GG-LAW-13} Multiplayer-ready
This game is built so co-op or PvP can be added without re-architecting, per Unity.Multiplayer's [Multiplayer-ready standard](https://mindattic.github.io/Unity.Multiplayer/articles/multiplayer-ready.html) ([UM-LAW-4](https://github.com/mindattic/Unity.Multiplayer/blob/main/docs/BIBLE.md#um-law-4)): every player intent is a serializable command stamped with a player id and submitted through `ISession`; one authority owns game state and presentation only reads state and events; there is no global "the player", only player ids and `ISession.LocalPlayer`; rules are deterministic (seeded RNG, fixed tick, no `UnityEngine.Random` or frame time); entities have stable ids; state and commands serialize; the game runs on a `LocalSession` behind `ISession`; and a test plays commands from two player ids. A change that breaks any of these is a bug. No netcode package is in the project (owner 2026-10-21, GG-T192: groundwork only).

How Gaiden meets it (audit 2026-11-02, GG-T192): **commands** — `MoveCommand` / `UseSlotCommand` / `ShieldCommand` are plain data (hero id, tiles, slot) carrying `BattleCommand.Player` (a `PlayerId` seat), round-tripped by `CommandCodec`; **pipeline** — `BattleScreen.Commit` stamps `ISession.LocalPlayer` and submits to `LocalSession`, which applies one queued command per session tick through `BattleEngine.Apply` and raises its events; **authority** — `BattleState` + `BattleEngine` in engine-free Core ([GG-LAW-9](#GG-LAW-9)); each hero has an `Owner` seat and the engine refuses (`AbilityRefusedEvent`, "Not your hero") a command for a hero another seat owns, while party-wide Guard is open to every seat; **no global player** — the single-player game is seat 0 everywhere, and only presentation asks `LocalPlayer`; **determinism** — `Rng` (xoshiro128\*\*) seeded in the state, the act-advanced clock ([§2.0](#20-the-act-advanced-clock)) is the rules' fixed tick, no frame time in rules; **stable ids** — `Unit.Id` assigned by the authority in setup order; **serialization** — setup + seed + `LocalSession.CommandLog` replays a battle exactly (`LocalSession.Replay`), and `BattleRecorder.Snapshot` is the state's JSON view. *(Tests: `MultiplayerReadyTests` — two seats, wrong-seat refusal, command JSON round-trip, replay equality, one-seat session identical to the engine.)* Known gaps, out of scope here: the meta/camp layer is one save profile (fine for battle co-op, not for split campaigns), and `BattleState` has no full load-from-snapshot yet (replay from the command log is the restore path).

### {#GG-LAW-14} Polish never regresses
"Polish" has one official, measured definition: [`docs/POLISH.md`](POLISH.md) — eight dimensions (type, color, layout/touch, information/feedback, motion/VFX cohesion, audio, balance/pacing, art cohesion), each rule with an ID (`POL-A1`…), a numeric threshold and a source (Apple HIG, Material 3, WCAG 2.2, Nielsen, Fitts/Hick, the 12 principles of animation, EBU R128). Every test cycle runs the polish pass (`tools/polish.py`) and a review checklist against the recorded clip; **no dimension score may drop** below `docs/polish/baseline.json`, a drop is a `fix` ticket worked first, and the three lowest rule groups become the next cycle's tickets. Thresholds only tighten; loosening one is an owner decision in `docs/AMENDMENTS.md` (owner 2026-11-05, GG-T249).

## 6. Verified state {#GG-§6}

> Status legend: ✅ done (verified) · 🟡 partial · ⬜ planned · living.

**Build/test environment:**

- **Engine:** Unity `6000.4.3f1` (`ProjectSettings/ProjectVersion.txt`).
- **Rules + meta suite:** `pwsh tools/test.ps1` → `dotnet test Tests/Core.Tests` then `tools/check_glyphs.py`. `-Compile` adds a Unity batchmode compile (`GaidenBuild.CompileCheck` via `tools/unity.ps1`, which refuses to run while the project is open in the Editor). Last run (2026-10-09): **84 tests, 84 passed, ~36 s.**
- **Suites** (`Tests/Core.Tests/`): `BoardRulesTests` (slide, pincer geometry, chains, supporters, 2×2 boss, waves, victory/loot), `TimelineAndAbilityTests` (act-advanced clock, enemy turns, shield, pushback, spells, cast queue + cap, Frozen/Wet/Steam/Shock, potion, teleport, Quicken, enemy charge, Sleep, weapon wear, Double Attack / Counter passives), `CatalogTests` (every hero ability, every enemy charge, every consumable's effect, every recipe, every class through the real engine; Smoke Bomb escape; Camp potion/Tent), `ContentAndSoakTests` (content validation, bot soak over every stage, deterministic replay), `MetaLoopTests` (new game, save round-trip, full loop through every Camp service, defeat), `BalanceReport` (plays the whole campaign with natural progression for greedy / casual / button-masher bots, with and without `CampRoutine`, and prints a win% / actions / HP-left table per stage; asserts a greedy bot can finish), shared fixtures in `TestKit.cs` (`Mini()` content, `AssertInvariants`).
- **Bot soak:** `Bots_play_every_stage_without_breaking_invariants` plays all 19 stages × 12 seeds at bot randomness 0 and 0.6 (300-action cap) asserting structural invariants after every command (HP in range, one unit per tile, in bounds, bank ≤ 12, no timeline entry owned by a dead unit, both teams alive while the battle runs). `BotRunner` throws on a soft-lock (6 refusals in a row).
- **Automation harness:** dev players compile in `AutomationServer` (localhost HTTP, `-automation <port>`); taps/drags/presses are dispatched straight into the UI Toolkit panel, so they land whether or not the player window has OS focus, and reply `ok:false` with an error when they hit nothing or never reach the panel (GG-T121); `tools/harness.py smoke` plays title → new game → camp → Adventure → crawl → battle on Auto → results; `sweep` screenshots and layout-audits 15 Camp screens. Artifacts: `artifacts/smoke1/`, `artifacts/sweep/`, `artifacts/run1/` (ad-hoc drag/pincer captures).
- **`-demo`:** boots straight into a scripted self-play loop with no harness attached — character select (auto-picks the starting three at random, §25.15; amended 2026-10-19, GG-T118) → crawl (auto-skipped) → battle (autoplayed) → results (auto-advances to the next stage, or retries on defeat) → …, restarting with a fresh save and a fresh random party when the campaign ends or nothing is launchable (`GameApp.DemoMode`). Formalizes what `tools/harness.py campaign` drives over HTTP as a startup flag, for unattended soak-play or a demo reel.
  - **`-demo -endless` (added 2026-11-02, GG-T57):** no Title, Hub, Camp or crawl — boots straight into generate encounter (`EncounterGenerator`, GG-T55: a synthetic 5-wave `endless-` stage) + a level-appropriate geared party for it (`EncounterParty`, GG-T56) → battle (autoplayed) → results (auto-advance, win or lose) → the next fresh encounter, forever (`GameApp.EndlessDemo`). `-seed S` sets the base seed (default: the clock) and encounter *i* (0-based) uses seed **S+i**, so any encounter of a run can be reproduced on its own; `-count N` stops cleanly (quits the player) after N encounters. Each encounter runs on its own throwaway in-memory save on slot -1 (`Persist` never writes it — no real save slot is touched; the battle is still committed to it so the results screen shows), and the previous encounter's synthetic stage is pruned from the live `ContentDb` before the next is registered, and the last one on stop (the GG-T23 rule). One `[EndlessDemo] encounter N seed S: …` line per launch and one result line per encounter.
    - **Reproduce one encounter by seed (added 2026-11-02, GG-T64):** a battle record's `seed` (and the seed in the launch line) *is* the encounter seed, so `-demo -endless -seed S -count 1` replays exactly the encounter a record or report flagged: the same location, waves, levels, party and gear (`Meta.EndlessEncounter.Build(content, S)` — the one pure seed → setup build both the player and Core use; it prunes any earlier `endless-` stage first, because the generator borrows its location from any non-test stage), the same battle RNG (`BattleSetup.Seed` = S), and the autoplay bot's RNG seeded from S (`BattleScreen.BotSeed` = S, i.e. `EndlessEncounter.Bot(S)`; the view's `UnityEngine.Random` is also seeded with S at launch). Headless, `EndlessEncounter.Replay(content, S)` plays the same encounter in Core with that bot and returns outcome, turns (commands), wave and clock — deterministic per seed, so a flagged outlier becomes a debuggable repro in a test. The player's simulated cursor still runs on frame timing, so a sub-100 `-skill` (drag overshoots, slip attempts) can diverge from the headless replay; at the default skill 100 the decisions are the same. *(Verified: `EndlessEncounterTests` — two builds from one seed are identical even with another encounter in between; two headless replays of a seed give the same outcome, turn count, wave and clock.)*
- **`-testmode` (GG-T93, owner 2026-10-19, bot-only):** self-plays the whole *mechanical* loop end to end with no story, crawl, overworld or tutorial — a throwaway save on slot -1 (never written; `TestLoop.NewSave`: the small starting party, tutorial done, every crawl seen, every Camp NPC joined (GG-T119: the headless beats would bring them too, but cycle 1 shouldn't wait for them); **amended 2026-10-19, GG-T118:** no story is shown, but `TestCycle.Begin` plays each stage's pending story beats headlessly (`DialogueRunner.PlayPendingBeatsHeadless`, first choice) so the story recruits still join, and the cycle line reports them as `story: …`) → launch a stage → battle (autoplayed) → results → **back to the Hub every fight** → `CampRoutine` (salvage/repair, Inn, forge/buy/equip, learn + assign, potions) → launch again, forever (`GameApp.TestMode`). Unlike `-demo` it visits camp and spends between every battle. Difficulty follows the party: on a `SaveData.TestMode` save `Progression.StageRecommendedLevel` returns `Progression.PartyLevel` (rounded average hero level) instead of `RecommendedLevel(campaignIndex)`/`LevelOverride`, and spawns stay max(authored, recommended), so enemies climb as the party levels. Stage pick (`TestLoop.PickStage`): the active expedition's next battle, else battle 1 of the highest unlocked theme, stepping back one theme per 3 losses in a row (a lost expedition restarts its theme). One `[TestMode] cycle N | stage … | party Lv a->b | enemy Lv … | result | +XP | +gold | mats … | camp gold x->y: <every CampRoutine action>` line per cycle; the automation `state` dump carries `testmode.cycles`. *(Verified: `TestModeTests` — enemy level rises with party level in test mode and is unchanged otherwise, stage picking, a 4-cycle headless loop; `tools/harness.py testmode N` runs the real player and screenshots each cycle's battle, level-up, results and camp into `artifacts/testmode/` plus `testmode.log`, checking every cycle logged, battle/results/hub seen per cycle, party level never falling and zero errors.)*
- **Tile-invariant safety net (added 2026-10-11; root causes closed 2026-10-19, GG-T97):** two actors never share a tile once a slide is over — see [§1.1.3](#113-one-actor-per-tile--the-occupancy-guarantee-added-2026-10-19-owner-direction-gg-t97) for the rule and the guarantee per path. Every position write goes through `Relocate` / `MoveMovable` / `AddUnit`; `EnforceTileInvariant` (after every command and `BattleEngine.Create`) is only the last-resort net: in release it self-heals and logs a `TileConflictEvent` (error log, dev-build banner, `board_audit.tile_conflicts`); under `BattleEngine.StrictTiles` — on for the whole test suite — it throws, and the check also runs after every enemy turn, cast and reinforcement inside a command. Root causes closed by GG-T97: a snake head stepping onto the amplifier left it under segment 1; a Hopper leap didn't check movables; a Revival Charm on a full board was spent for nothing; a reinforcement with no room was silently dropped (it now waits); an egg's event reported its intended tile even when it spawned elsewhere; a hero start with no free tile fell back onto the taken one; and the view placed spawned/displaced tokens at their end-of-command tiles mid-playback. The strict soak (every stage × 24 seeds × 2 bots, 1500-action cap, plus 2500-action Crucible runs) and the 191 battle records on disk (2026-10-19: none contains a `TileConflict` event or an overlapping state) show no remaining occurrence.

**Proven working** (evidence per story in [`USER_STORIES.md`](USER_STORIES.md)):

- ✅ Board rules: slide/displacement, edge stop, no-op drag cancel, pincer geometry, chains, supporters, 2×2 immovable + width pincer, waves, victory/defeat (`BoardRulesTests`).
- ✅ Act-advanced timeline: clock only on committed actions, enemy turns on arrival, Pushback keyword (no Zone since GG-T286), Quicken, Shield, Frozen halt (`TimelineAndAbilityTests`, `TimelineResonanceTests`).
- ✅ Mana: shared capped bank of generic orbs, pincer mint, supporter mint, crit bonus orb, overflow.
- ✅ Every hero ability, enemy charge, recipe and class resolves through the real engine (`CatalogTests`).
- ✅ Determinism and bot soak across all content (`ContentAndSoakTests`).
- ✅ Camp loop: new game, save round-trip, battle commit, merchant, forge, equip, repair, inn, learn + assign, summon, party cap, bounty (`MetaLoopTests`); every Camp screen renders (harness sweep).

- ✅ Every consumable has a working battle effect or is camp-only; Smoke Bomb escape; Camp item use.

**Built but not yet asserted** (🟡 — see USER_STORIES): boss phases, snake chain, traps, enemy pincers and AI weights (soak-only), Clutch, time bank, battle-start orbs, buff tick timing details, wounds carry-over, first-battles hints, colorblind/reduce-motion.

**Verification gates (2026-10-10):** `tools/test.ps1` (152 dotnet tests + glyph + audio checks; `-Ui` adds 45 player checks), `tools/webcheck.mjs` (WebGL, 4 checks), `tools/androidcheck.py` (Android emulator, 7 checks); earlier `tools/test.ps1` (148+ dotnet tests incl. every ability/charge/item/recipe/class, bot soak, balance report), `tools/uicheck.py` (35 assertions against the real player: screens, music, HUD, drag preview, targeting, long-press, pause, hints, settings, letterbox, fps, zero errors), `tools/harness.py campaign` (consecutive autoplayed stages). See [AMENDMENTS](AMENDMENTS.md) "parity batch" for the systems added in that pass (Ranged/Mechanical archetypes, stacking policies, armor wear, relic passives, weapon swap, Black/Green spells, 55 class kits).

**Amended 2026-10-21 (GG-T17):** `tools/harness.py campaign 25` cleared the full 25-stage campaign (15 main + 10 postgame, GG-T10) end to end in one unattended live run for the first time, including the Anchor Wastes expedition (stages 9–11, 0-based) that had been the automation frontier in earlier sessions. Four single-attempt losses (stages 8, 13, 17, 19) each recovered on the harness's first scripted retry; no stage hit `DIFFICULTY_WALL`, no stall/timeout, and `errors: []` — zero engine errors across the whole run, with real gold/level progression through all 25 stages (`dotnet tests` 1024/1024 the same session). Confirms GG-T20's orphaned-confirm-veil fix (closing GG-T16) also cleared the click-reliability risk that had made a full unbroken run unachievable before.

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
7. **Polish doesn't regress** — for visible or balance-affecting work, the polish pass ([`POLISH.md`](POLISH.md)) scores no dimension below the baseline ([GG-LAW-14](#GG-LAW-14)).

A story is ✅ only when 1–7 hold; otherwise 🟡/⬜ ([GG-LAW-12](#GG-LAW-12)).

## 9. Glossary {#GG-§9}

- **Slide / displacement** — dragging a hero onto an occupied tile moves the displaced actor (ally or foe) into the tile the hero just left. ([§1.1](#11-sliding-and-displacement))
- **Pincer** — two allies in the same row/column with a contiguous, ally-free, ≥1-opponent line between them; the only source of hero physical damage. ([§1.2](#12-pincer-attacks))
- **Supporter** — an ally connected to a pincer attacker by an unbroken cardinal chain of allies, within its class's support range; performs its class's **support action** (strike the trapped line at 50%, heal, buff, haste, mana, cleanse, lend crit, echo a spell, inflict a status, shove…). ([§1.2.3](#123-supporters))
- **Support action** (`SupportDef`) — a class's effect as a supporter: Effect, Magnitude, Target (Attacker / Chain / Party / Self), Range along the chain. Enemies and classes without one strike at 50% from beside the attacker. ([§1.2.3](#123-supporters))
- **Action** — one committed command (move, slot use, shield); it ends the player's turn: the clock runs to the next enemy arrival (`BattleConfig.ActionTicks` = 20 ticks only when no enemy icon can arrive). ([§2.0](#20-the-held-turn), GG-T188)
- **Held turn** — the real-time window from picking a hero up until the next enemy icon reaches the left edge; when it runs out the hero is dropped where it is (forced drop). ([§2.0](#20-the-held-turn))
- **Tick / IP** — the clock unit (10 per second); turn icons load Initiative Points 0..1000 internally, but every icon is drawn at `u = secondsUntilAct / BarSeconds` (10 s; 0 = the left edge, acting now). ([§2](#2-the-timeline))
- ~~**Pushback Zone**~~ — retired 2026-11-06 (GG-T286). **Pushback** — an ability/item keyword in seconds (and 1 s on every foe a hero pincer hits): the foe acts that much later, anywhere on the bar, at most 2 s and once per action. ([§2.3](#23-pushback-a-keyword-in-seconds))
- **Hasten / Quicken** — the inverse: slide an icon forward toward the left edge. ([§2.7.1](#271-hasten--quicken--the-inverse))
- **Cast icon** — a spell with a cast time is a diamond on the same bar as the turns, born at `castSeconds / BarSeconds`, and resolves when it reaches the left edge. ([§2.6](#26-one-track-diamond-icons-the-prominence-diamond))
- **Prominence diamond** — the large diamond at the timeline's left end showing who acts next, with its seconds ("NOW" while acting). ([§2.6](#26-one-track-diamond-icons-the-prominence-diamond))
- **Sentient** — the enemy tag for a foe with a society or that plans ahead; only Sentient enemies (and every hero) may be pincer attackers. ([§1.3](#13-sentient-restriction))
- **Clutch** — rare LCK interrupt outcome on hero casts: the hit is shrugged and the cast resolves on the spot. ([§13.4](#134-the-interrupt-path))
- **ManaBank / orb** — shared 12-orb pool of identical orbs; any orbs pay any spell. ([§3.1](#31-mana-the-orb-economy))
- **Drag timer** — optional Terra Battle–style 4 s limit per drag (Settings). ([§2.9](#29-drag-timer-optional))
- **Time-banked orbs** — Blue orbs minted from drag-timer seconds left at the drop. ([§3.1.8](#318-time-banked-orbs))
- **Countdown badge** — the number on an enemy: actions you get before it acts. ([§2.10](#210-countdown-badge))
- **AbilityBar** — the 5-slot Skill / Spell / Item bar of the selected hero; 2 usable on a fresh save, up to 5 by campaign progress. ([§4](#4-the-abilitybar))
- **Camp** — the Final Fantasy–style hub menu (`HubScreen`). ([§25](#25-the-camp-merged-hub))
- **Event player** — `BattleScreen.Play`, which animates an event list one awaited beat at a time.
- ~~**Undearth**~~ — struck 2026-10-10 (owner): the missing-sun / Undearth lore is removed in favor of the story premise that landed in GG-T120 (US-348) — itself retired in GG-T341 for *Under Morrow*.
- **Morrow** — the moon, hanging so low it fills a third of the sky and "nearing" every lifetime; it pulls at **aether**, the current that lets a body heal and a spell catch. Never explained in game. ([STORY.md](STORY.md) §1)
- **The tithe** — the aether every hold pays the Pale Concord at its white domes (**Stills**) to feed the Anchors; pay too much and you come back **Hollow**. The surplus feeds the Hierarch. ([STORY.md](STORY.md) §2)
- **Story crawl** — the skippable per-theme intro text shown on first entry into a theme. ([§27](#27-story-crawl-no-dialog))
- **Summon Circle** — the Camp service where gold recruits a hero class. ([§25.10](#2510-summon-circle))
- **Bounty** — a posted kill contract: accept one, track kills, claim gold + an item. ([§22.4](#224-bounty-board))
- **Tap term / `[term]`** (*added 2026-11-06, GG-T300, owner: "anything in brackets is a tap tooltip"*) — content text marks a keyword in square brackets; the player sees it highlighted and underlined (light blue `Palette.Term`, brackets hidden) and a **tap** on it opens a small definition card. Every bracketed term must resolve, case-insensitively, to an `Id`, `Term` or `Aliases` entry of `Resources/Content/glossary.json` (`GlossaryEntry`: `Id`, `Term`, `Aliases[]`, `Text`, optional `LinkKind` Buff/Ability/Stat/Item + `LinkId` for live numbers on the card); `ContentLoader.Validate` (`Glossary.Validate`) fails on an unknown term in any ability, item (incl. relic passive), class or skill-tree text, and requires an entry for every status name. Text without brackets is always valid. The glossary is seeded with every status, the stats, the timeline words (Pushback, Guard, Cast time, Haste, Slow, Stun, Sentient, Pincer, Supporter, Prominence, Tempo, Interrupt), the reach/reaction words ([directly], [adjacent], [surrounded], [Counterstrike], [Reactive], [Passive], [Active], [Dodge]) and the class-card archetype words.
- **Chance words** (*GG-T300*) — a description that doesn't print a number says how likely with one word, one band each (`Glossary.ChanceBands`, `Glossary.ChanceWord(p)`): **[slight]** ~10% (≤15%) · **[small]** ~20% (16–27%) · **[fair]** ~35% (28–42%) · **[good]** ~50% (43–62%) · **[likely]** ~75% (63%+).

For combat/targeting/VFX sub-vocabulary see [Appendix §18 Glossary](#18-glossary).

---
<a id="gg-appendix-a"></a>

# Appendix A — Design Canon

> The detailed reference behind the nine sections above; the L0 sections and `USER_STORIES.md` cite it by `§` number (numbering follows the GridGame2026 bible; §16 and §21 are intentionally absent).

## 0. North Star

A tactical-RPG hybrid where the player **drags heroes one tile at a time across a 6×8 grid** to flank enemies and trigger **pincer attacks**. Combat runs on a **timeline** of enemy icons that "load" left → right and act when they arrive; the clock advances only when the player commits an action. Magic, items and class skills are managed through the **AbilityBar**. Between battles the party rests at a Final Fantasy–style **Camp** menu.

The game is a **deterministic pure-C# simulation** (`Gaiden.Core`) with a **code-built UI Toolkit front end** (`Gaiden.App`) that plays the simulation's events back. There are no prefabs, no hand-built scenes and no Inspector wiring; art, audio and content load from `Resources`. **One exception (amended 2026-11-05, owner, GG-T269):** premade particle-effect prefabs under `Resources/Vfx/`, spawned and driven by code ([§6.2](#62-vfx-pipeline)).

### 0.1 Design pillars

1. **The verb is "slide".** Heroes never "land on" enemies. They flank into tiles; damage comes from the geometry that forms, not the move itself.
2. **Time is the resource the player manages.** Every decision trades against "whose icon lands first?" — but the clock waits for you. **Amended 2026-10-09:** time is spent per action, not per real second. **Amended again 2026-10-21 (owner, GG-T188 — reverses the act-advanced clock):** the clock waits while you *look*, but runs in real time while you *hold* a hero; your turn is the time from pickup until the next enemy icon reaches ACT.
3. **Mana is a shared, visible pool.** The 12-slot ManaBank is the whole party's spell budget.
4. **Every spell is a (shape × mode × filter) triad.** Adding a spell is a data row.
5. **The simulation is the truth.** **Amended 2026-10-09:** replaces "Code is the source of truth; the builder is the spec". Core decides; the view only animates what Core already resolved.
6. **Verify-then-checkpoint.** Land a feature end-to-end with test + harness evidence before committing.
7. **The bible is the brief.** If code and bible disagree, fix the disagreement in writing.

### 0.2 Non-goals (what this game is NOT)

- **Not real-time, and not "press attack, wait for enemy to press attack."** **Amended 2026-10-09:** the old bible said "the timeline is continuous; thinking time is also game-clock time." In GridGame Gaiden the clock is **act-advanced**: it does not move while you look, drag or pick a target. Each committed action spends 2.0 s of game clock, during which any number of enemy icons and casts may arrive and resolve in arrival order. There is no hero turn order — any living, mobile hero may act each time. The pressure comes from the countdown badges and the Zone, not from a ticking wall clock (an optional drag timer restores Terra Battle's hand pressure, §2.9). **Amended 2026-10-21 (owner, GG-T188):** still not real-time while you think — but a held hero runs the clock in real time, and each committed action ends your turn at the next enemy arrival ([§2.0](#20-the-held-turn)).
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

**The core strategy (owner, 2026-11-03, GG-T219).** "One of the most important strategies is to slide through allies and opponents to set up even more devastating pincer attacks … moving opponents into lines to execute the best pincer attack." The slide is not travel; it is how a turn *builds* the pincer. Every occupant a hero passes through lands on the tile the hero just left, so a walk drags a column of foes back along its own route — straightening a scattered pack into one unbroken line — while allies passed through are dropped behind it as attackers or supporter-chain links. The best turn walks a route that ends with the most foes in a single line between two heroes, with the longest supporter chains behind them. Features that show or score moves (the shove preview, GG-T190; the drag trail below; bot and hint scoring) serve this play first.

**The drag trail (GG-T219).** Like Terra Battle and Friends & Dragons, a moving unit leaves a trail. Owner: "the trail should just be a trail renderer … just a fast wide trail that fades out in about 1.5-2 squares worth of distance … not anchored to anything … moving the heroes and units can be a diagonal or free hand thing, not perfectly right angled changes." `TrailLayer` is that trail renderer: while a token moves, its real drawn position (the finger's free-hand path for the held hero, the tween for a walking unit) is sampled every frame (4 px minimum vertex distance) into a polyline and drawn **under** the token as a triangle-strip streak as wide as the token (a soft full-width band over a brighter core), so it shows just behind it. It is never anchored and its length is time, not distance: each point lives 0.15 s (owner: "it needs to be fading quick fast"), with alpha falling off as the square of its remaining life, so a faster, longer move draws a longer streak and it is gone a moment after the unit stops. Owner: "it gets longer the more quickly and distant the movement is but always catches up very quickly instead of fading out to a point, the whole end fades out so that the end is still almost as thick as the start" — the tail keeps ~88% of the token's width and fades out by age. Gold for heroes, red for foes; a displaced actor shows its shove arrow instead (GG-T190).

### 1.1.1 Physical blocks (added 2026-10-10, owner direction)

Owner: "the grid can have physical blocks that disallow entrance … nobody moving through or landing or spawning on a physical block."

- `StageDef.Blocks` lists block tiles (rocks, pillars, dead trees); `BattleState.Blocks` holds them for the battle. **`BattleState.Walkable(c)`** (on the board and not a block) is the single collider check every rule uses: hero steps (`StepBlockedEvent` "block"), enemy steps and 2×2 footprints, snake moves, teleports, spawns (initial and reinforcements), trap laying and amplifier spawns/shoves.
- Displacement can never push a unit into a block — the displaced unit always takes the tile the mover just left.
- A block is not an opponent, so a block inside a line **breaks the pincer**. Blocks also stop ranged line of sight and line charges.
- Content: Arkgrave III, all three Anchor Wastes and all three city-ruin stages carry 2–3 blocks, kept off the hero start rows (6–7). The bot soak plays every stage with them.
- View: a raised, bevelled stone block (the biome tile, lit top edge, dark base, drop shadow).
- *(Tests: `A_hero_cannot_step_onto_a_block`, `A_block_in_the_line_breaks_the_pincer`, `Nobody_spawns_or_walks_onto_blocks`, `Real_stages_carry_their_blocks`.)*

### 1.1.2 Movable tiles (added 2026-10-10, owner direction)

Owner: "nothing can spawn on top of the Power Amplifier but they can push it around like any other actor … a MovableTile as opposed to a stone which can't be moved."

- `MovableTile` (`BattleState.Movables`, kind `Amplifier` for now) occupies its tile like an actor: `BattleState.FreeForSpawn(c)` (walkable, no unit, no board item — no movable and, since GG-T227, no pickup) gates every spawn, reinforcement, egg, guest, recruitable, snake segment, blink/burrow surfacing, revive, trap and trail (the amplifier spawn uses the stricter `FreeForItem`, which also keeps it off traps — GG-T129). Units already on the board that are pulled, step back or are knocked on use `FreeToEnter` (no unit, no movable): they may enter a pickup's tile.
- It is **displaced exactly like an actor**: a hero stepping into it swaps it into the tile just left; an enemy stepping into it does the same; a 2×2 drags it to its vacated lane tile; knockback pushes it ahead if there's room. It is never an attacker, a target or a pincer opponent, so it breaks a pincer line like any gap.
- Stones (§1.1.1) are the immovable kind — colliders nothing enters or lands on.
- **One item per tile (added 2026-10-19, owner: "power ups can't share a space either", GG-T129):** a movable is a board *item* as well as an actor, so it never shares a tile with a pickup (§13.3.1) either. It never spawns on a pickup (`BattleState.FreeForItem`), and a shove never lands it on one: a hero swap or enemy step whose "tile just left" holds a pickup sends it to the nearest open, item-free tile instead (`ShoveMovable` → `NearestMovableSpot`; the pickup stays where it lies), and knockback treats a pickup beyond it as a wall (Crash). See §1.1.3.
- **Amplifier alignment (decision, owner may overrule):** since heroes can't stand on a movable tile, a pincer attacker is *aligned* with the amplifier when it is cardinally adjacent to it.
- *(Test: `The_amplifier_is_a_movable_tile_heroes_push_and_nothing_spawns_on`.)*

### 1.1.3 One actor per tile — the occupancy guarantee (added 2026-10-19, owner direction, GG-T97)

Owner: "there can never be two actors occupying the same tile after sliding is over."

**The rule.** Once a slide (any step, shove, swap or appearance) is over, every tile holds at most one actor — a living unit (corpses, remains and eggs included; the dead are off the board) *or* a movable tile — and every actor stands on walkable tiles (§1.1.1), footprint-aware for 2×2s. The only in-between is the two halves of one swap (the stepper arrives, then its partner takes the tile just left), which never survives to the end of a beat.

**Board items too (added 2026-10-19, owner direction, GG-T129).** Owner: "power ups can't share a space either; I see multiple pickups sitting on top of each other." Every tile holds **at most one board item — a pickup (§13.3.1) or a movable tile (§1.1.2) — ever**, and every item lies on a walkable tile. Items are separate from units: ~~a unit may stand on a pickup (pickups don't block; a hero collects it on entry, an enemy just stands there), but never on a movable.~~ **Amended 2026-11-03 (owner direction, GG-T228):** no unit ever stands on a pickup either — a hero entering a pickup's tile collects it, and any other unit that enters one shoves it exactly like a movable (see *Pickups move like actors* below). One check gates every item placement, `BattleState.FreeForItem` (walkable, no unit, no trap, `ItemFree`: no pickup and no movable) — pickup drops and the amplifier spawn both use it — and `MoveMovable` refuses a landing tile that holds a pickup. (Only a hero-path *preview* ignores pickups there: the preview doesn't collect them, so the tiles the walk crossed still list pickups the real move picks up first.)

**The chokepoint.** Every position write in Core goes through one of three `BattleRunner` methods, which refuse a footprint that isn't open (`FootprintOpen`: walkable, no other living unit, no movable — with the step's own swap partner / shoved heroes / shoved movable named as yielding):
- `Relocate(unit, to)` — every move of a unit already on the board;
- `MoveMovable(movable, to)` — every shove of the Power Amplifier;
- `AddUnit(unit)` — every unit that appears.
A refused write moves nothing (release) or throws (`BattleEngine.StrictTiles`). New units are placed with `FootprintFree` (`FreeForSpawn` + no trap) / `RandomFreeTile` first, so a refusal there is always a bug.

**Nothing appears on a pickup (added 2026-11-03, owner direction, GG-T227).** Owner: "no two units or powerups can share the same tile, make sure nothing spawns on a tile thats already occupied." `FreeForSpawn` used to check units and movables only, so a new wave (pickups outlive the wave that dropped them), a reinforcement, a guest, a recruitable, a snake segment, an egg, a blinker/burrower surfacing, a revived hero, a laid trap or a trail could all appear on a pickup — the unit-on-powerup stacks the owner saw. Since GG-T227 it also requires `ItemFree` (no pickup), so every appearance path in the table below skips pickup tiles footprint-aware (a 2×2 needs all four tiles pickup-free) and falls back exactly as it does for an occupied tile.

**Pickups move like actors (added 2026-11-03, owner direction, GG-T228).** Owner: "no two units or powerups can share the same tile … make sure powerups and spawns get moved the same way actors get moved." Heroes still collect a pickup by entering its tile (walking, displaced, teleporting, stepping back). Any other unit that enters one — an enemy step or skirmisher retreat, a hopper leap, a 2×2 lane step, a snake head, a blink, a teleport, a non-hero knocked back, pulled or displaced — shoves it with `ShovePickupsUnder` → `ShovePickup`, the `ShoveMovable` rules: onto the tile the mover just left (a 2×2's vacated tile on that lane; a snake's tail tile) when it is walkable with no unit, no movable and no other pickup, else the nearest such tile (`NearestSpot`); with no such tile anywhere the pickup fades (`PickupExpiredEvent`) rather than stay under a unit. Each shove emits `PickupMovedEvent` (From, To, ByUnitId), which the view slides like the amplifier with a slide cue. Spawns never need it: they never appear on a pickup (GG-T227). `FindTileConflict` now reports a unit on a pickup (strict throws; release slides the pickup off); `TestKit.AssertInvariants` checks it in every soak. Enemy AI never sought pickups, so no planner change.

**The guarantee, path by path:**

| Path | How the footprint is guaranteed free | When there's no room |
|---|---|---|
| Hero drag step (`WalkPath`) | Enters an empty tile, or swaps: the occupant (or movable) takes the tile the hero just left — the one tile the step frees. Multi-tile, snake and remains occupants refuse the step ("immovable"); a diagonal slip only enters open space. | Step refused (`StepBlockedEvent`), rest of the path dropped. |
| Enemy 1×1 step (`StepEnemy`) / skirmisher retreat | Destination must hold no unit; a movable there is shoved into the tile just left. | No step. |
| Hopper leap (`LeapEnemy`) | Landing tile must be fully open (no unit, **no movable**). | No leap. |
| 2×2 step + `VacatedTile` shoves | `FootprintStepLegal` refuses any entered tile with an enemy (incl. corpses/remains/eggs); each hero/movable on an entered tile goes to the tile vacated **on its own lane** — one entered and one vacated tile per lane, so two shoves can never meet. | No step. |
| Snake chain (`FollowSnake`) | Each segment slides into the tile the one ahead just left; a movable the head walked onto goes behind the **tail** (it used to land under segment 1 — fixed in GG-T97). | The chain stops at the first segment that can't follow. |
| Knockback (`Knockback`) | Each tile of the push must be walkable and unit-free; a movable ahead is pushed on only if the tile beyond it is free; a pickup it lands on is shoved back to the tile it left (GG-T228). | Crash (unit / movable that can't move) or Slam (edge / block): damage, no move. |
| Teleport ability, delayed or instant | Landing footprint must be open at resolution time. | "Blocked", caster stays. |
| Blink / burrow (`SurfaceBeside`) | Only a free, trap-free tile beside the prey. | Normal turn instead. |
| Movable shoves (`ShoveMovable`, hero swaps included) | Only onto a walkable tile nobody stands on that holds no pickup and no other movable (GG-T129). | Nearest open, item-free tile (`NearestMovableSpot`); never left under a unit or on a pickup. |
| Power Amplifier spawn | `FreeForItem`: no unit, no trap, **no pickup** (it used to ignore pickups — GG-T129). | Not spawned this time. |
| Pickup shoves (`ShovePickup`, GG-T228) | A non-hero entering a pickup's tile sends it to the tile it just left (lane-aware for 2×2s, behind the tail for snakes) if no unit, movable or other pickup is there. | Nearest open, item-free tile; none at all → it fades. Never left under a unit. |
| Pickup drop (`MaybeDropPickup`) | `FreeForItem` on the fallen enemy's tile, else a cardinal neighbour. | No drop. |
| Party at battle start | `HeroSpec.Start`/default start if free, else a random free tile in rows 4–7, else anywhere. | The hero sits the battle out off the board (only possible on a stage walled with blocks). |
| Wave spawns (`SpawnWave`, incl. snakes) | `SpawnDef` tile if the footprint fits, else random free rows 0–4, else anywhere; segments on free adjacent tiles. | That spawn is skipped. |
| Delayed reinforcements (§14.8) | As wave spawns. | **Waits** in the queue and retries every tick until a tile frees; gives up only when no other enemy is left to free one (so the wave can still end). |
| Eggs (`LayEgg`) | A free, trap-free tile beside the layer's footprint; the event reports the egg's real tile. | No egg this turn. |
| Remains / corpses (`LeaveRemains`), rising and hatching | On the tile the unit just vacated by dying (the remains/egg is removed before its successor spawns). | Not left / successor placed by the spawn rules. |
| Guests (`SpawnGuests`) | Their tile if free, else random free rows 3–7, else anywhere. | Guest doesn't join. |
| Revival Charm (`Revive`) | The free, trap-free tile nearest the user — never back onto its old tile if someone stands there now. | Refused before the charm is spent ("No room!"). |

**The net.** `BattleRunner.EnforceTileInvariant` still runs after every command (and after `BattleEngine.Create`) as a last resort, and since GG-T129 `FindTileConflict` covers items as well (two pickups on a tile, a pickup under a movable, a pickup off the board): in a release build it nudges the newer actor to a free tile (a movable on a pickup slides to the nearest item-free tile; the newer of two stacked pickups fades with a `PickupExpiredEvent`, so its token goes too) and emits a `TileConflictEvent`, which the App logs as an error, shows on screen in dev builds and counts in `/state → board_audit`. Under `BattleEngine.StrictTiles` (the whole test suite turns it on; `BattleState.StrictTilesOverride` lets one test opt out) it throws `TileConflictException` instead — and the same check also runs after every enemy turn, cast and reinforcement *inside* a command, so a conflict is caught where it happens.

**The view.** Playback places every token where the event says it was at that moment — spawn/reinforcement tiles (`WaveStartedEvent.SpawnedAt`, `ReinforcementsArrivedEvent.SpawnedAt`, GG-T98), remains/eggs/hatchlings/risen/guests at their event `At`, and the drag prefix snaps each unit to its last prefix step, not its end-of-command tile. `BoardAudit` checks after every beat that no two tokens are drawn on one tile (a swap's two halves excepted), as playback settles that every token sits exactly on its unit's `State` tile (before `SyncAll` could hide a miss as a jump), and after `SyncAll` that tokens are unique and rendered on their tiles. Since GG-T228 pickup tokens move (`PickupMoveAnim`, `BoardView.PlacePickup` keeps the drawn tile in step, `SyncPickups` re-places them, and a drop is drawn on its event tile, not the end-of-command one), and the audit also fails a unit token drawn on a pickup token — per beat for non-hero tokens once no move beat is next, and against the state for every unit. Since GG-T129 it audits **item tokens** the same way (`AuditItemTokens`): after every beat no two item tokens (pickups, the amplifier diamond) are drawn on one tile, and against the state no stale token — every drawn pickup still lies where the state has it, every pickup in the state is drawn, and the amplifier is drawn exactly where (and only while) it stands. Collected and faded pickups remove their tokens (`PickupCollectAnim` / `PickupExpiredAnim`, with `SyncPickups` as the net), a used amplifier hides its diamond.

*(Tests: `TileOccupancyTests` — strict throw, release heal (unit and amplifier), long drag through a crowd, knockback into unit/edge/block/amplifier, 2×2 shoving a crowded lane (two heroes, hero + amplifier) and blocked by an enemy body, snake head onto the amplifier, delayed teleport onto a tile taken meanwhile, blink with no free side, colliding party/enemy spawn tiles, a party bigger than the board, guest onto a taken tile, reinforcement onto its own taken tile and onto a full board, egg laying, remains/rising, revive onto an occupied tile and on a full board; GG-T129 board items: the amplifier never spawns on a pickup, an enemy leaving a pickup shoves the amplifier aside, a hero swap never sends it onto a pickup, knockback stops at a pickup behind the amplifier, strict throw on a pickup under the amplifier, release heal of two stacked pickups, and a strict soak with drops and amplifier shoves (`Strict_soak_with_drops_and_amplifier_shoves_never_stacks_items`); GG-T227 spawn sources aimed at a pickup: a new wave (1×1 and 2×2) on pickups left from the last, a reinforcement, an egg, a guest, a recruitable and snake segments, a blinker's only surface tile, a revive whose nearest tiles all hold pickups; `TestKit.AssertInvariants` asserts items apart in every soak; and the strict bot soak: every stage × 24 seeds × 2 bot temperaments at a 1500-action cap plus long Crucible runs. Harness: `python tools/harness.py boardaudit N` plays N autoplayed battles in the real player and fails on any `board_audit` problem, item tokens included.)*

### 1.2 Pincer attacks

A pincer is **two allies in the same row OR column, with every tile between them covered by opponents** — no gaps, no allies in the line, at least one opponent ([GG-LAW-2](#GG-LAW-2)). **Amended 2026-11-06 (owner, GG-T289/GG-T294):** both allies must be pincer-eligible — every hero, and only **Sentient** enemies ([§1.3](#13-sentient-restriction)); a trapped hero line flanked by two wolves is just two melee neighbours.

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

~~**Amended 2026-10-09:** any living ally cardinally adjacent to a pincer attacker is a supporter and strikes every trapped opponent at 50%.~~

**Amended 2026-10-21 (GG-T187, owner — "Terra Battle's best idea"): every class gets a support action.** A living ally (other than the partner) **connected to a pincer attacker by an uninterrupted cardinal chain of allies** is a supporter when its distance along that chain (1 = touching the attacker) is within its class's support **Range** (`PincerDetector.FindSupporters`, a breadth-first walk over allies only). Any empty tile, foe or obstacle **breaks the chain**; every ally is a link whatever its own range, so a striker standing beside the attacker carries a healer two tiles back. There is no line-of-sight/axis requirement. Supporters of both attackers are merged and de-duplicated; one reached from both sides supports attacker A. Supporters need not be pincer-eligible (an enemy supporter need not be Sentient, [§1.3](#13-sentient-restriction)).

Each supporter then performs its class's **`SupportDef`** (`classes.json` → `Support`: `Effect`, `Magnitude`, `Target`, `Range`, optional `Buff` and cut-in `Label`) — `BattleRunner.PerformSupport`, announced by a `SupportActionEvent`:

| Effect | What it does | Magnitude |
|---|---|---|
| **Strike** | a physical strike on **every trapped opponent** (the old supporter hit — now one support type among many) | damage fraction (0.5 = `BattleConfig.SupporterDamageFraction`) |
| **Heal** | heals each target | fraction of the target's max HP |
| **Buff** | applies `Buff` to each target — ATK up (`empowered`), shield (`protection`), `regen`, taunt (`taunting` on Self) | — (the buff's own duration) |
| **Haste** | each target acts sooner: its turn icon gains that many seconds of IP at its own rate, or its cast is rushed by that many seconds; a hasted hero also lifts party Tempo by `round(s / BarSeconds / 0.15)` (min 1). **Amended 2026-11-06 (GG-T298):** seconds, not a bar fraction | seconds (≤ 3) |
| **Mana** | fills the mana meter (100 charge = one orb, [§3.1.9](#319-mana-charge)); hero team only | charge |
| **Cleanse** | strips every debuff from each target | — |
| **Crit** *(GG-T298)* | the attacker it is chained to gains that much crit chance on its blows **in this pincer** (Double Attack follow-ups included). Resolves **before** the attackers strike; several stack; the attacker's crit is capped at 75 % while supported | crit fraction (0.2 = +20 points, ≤ 0.5) |
| **Cast** *(GG-T298)* | echoes a random **damaging spell from its own kit** (class kit, then a hero's bar: Spell kind, BaseDamage > 0, not ally-only; one battle-Rng draw when it has two or more) on **every trapped opponent** at reduced power: the spell's own damage × resistances × caster INT power × Magnitude. No mana, no cast time, no crit, no rider status. A silenced supporter, or one with no damaging spell, does nothing | power fraction (≤ 1) |
| **Status** *(GG-T298)* | each trapped opponent gains `Buff` (a debuff: poison, burn, blind, slow, silence…) with that chance — one Rng draw per foe below 1, none at 1; immunities and wards still apply | chance 0..1 |
| **Pushback** *(GG-T298)* | that many seconds of Pushback on every trapped opponent, **added to the pincer's own push** (hero pincers: `PincerPushbackSeconds` on a foe they hurt), so each foe is still pushed **once per action**, capped at `PushbackMaxSeconds` (2 s). Works for either team | seconds (≤ 2) |

**Target** is **Attacker** (the attacker the chain reaches), **Chain** (every ally on the chain from the supporter to that attacker, both ends), **Party** (every living ally) or **Self**; Strike, Cast, Status and Pushback always hit the trapped line and Crit always goes to the attacker. A class with no `Support` uses the default: Strike at 50% from range 1, exactly the pre-amendment rule — since GG-T307 no shipped enemy relies on it ([Enemy supports](#enemy-supports-gg-t307) below). Hero defaults by role: Strikers strike (range 1), Ranged strike from range 2, Healers heal (attacker, chain or party, range 2–3), Tanks guard/taunt/rally (range 2), Mages give mana or haste (range 2), Supports haste, buff, shield, regen, cleanse or give mana (range 2–3). Supporters still count as participants for the mana charge divisor ([§3.1.9](#319-mana-charge)). The pincer outline links each supporter to the unit its chain runs through (`PincerFormedEvent.SupporterLinks`), and each supporter's cut-in card and float text name its action. *(Tests: `Supporters_join_through_an_unbroken_chain_of_allies_within_their_range`, `A_gap_in_the_chain_breaks_support`, `A_healer_supporter_heals_the_attacker_it_is_chained_to`, `Every_hero_class_performs_its_own_support_action`.)*

**Amended 2026-11-06 (GG-T298, owner — "Each supporter gives some kind of passive or active effect when they are a supporter: a Cleric sends a heal to the attacker; a ninja might increase your crit %; a wizard might randomly cast a spell on enemies in the line — so some characters are just plain better for support because of their passive benefits."):** the four effects above join the list, and the 34 hero classes that only struck now each bring an archetype edge:

- **Crit** (+25–35 %): Assassin *Killing Intent*, Black/Blue/Chroma Ninja *Shadow Edge* / *Still Water* / *Prism Edge*, Duelist *Read the Opening*, Fencer *Riposte Eye*, Swordmaster *Perfect Form*, Ronin *Iaido*, Ripper *Find the Vein*, Night Hunter *Hunter's Mark*.
- **Status** (35–50 %): poison — Green Ninja *Venom Dart*, Slasher *Rusty Blade*, Hexstalker *Venom Rain*; burn — Red Ninja *Fire Shuriken*; blind — Yellow Ninja *Flash Powder*, Thief *Pocket Sand*; slow — Vampire *Mesmerize*, Hexwarden *Snare Shot*; silence — Hexsniper *Hex Round*.
- **Pushback** (+0.5 s; Panda Girl +0.75 s): Basher *Bash*, Bruiser *Shove*, Pugilist *Haymaker*, Prizefighter *Uppercut*, Street Fighter *Shoulder Check*, Martial Artist *Palm Strike*, Panda Girl *Belly Bump*, Lancer *Lance Drive*.
- **Haste**: Speedster *Slipstream* (1 s).
- **Strike** stays the identity of the pure melee: Barbarian *Rampage* (60 %), Fighter *Follow Through*, Striker *Flurry*, Sellsword *Paid Blade*, Raider *Plunder*, Odachi *Great Arc*.
- **Cast** (40 % power) goes to the witch-type mages, which trade Mana for it: Black Witch *Hex Echo*, Harbinger *Arcane Echo*.
- The existing Haste supports convert to seconds: Courier 1.5 s, Operative 1.25 s, Drifter 1 s, Dervish 0.75 s (chain).

`ContentLoader.Validate` checks each effect's range, that a Status names a known debuff, that a Cast class has a damaging spell in its kit, and that the generated sentence's [terms] are in the glossary. Every effect is plain `SupportDef` data, so an enemy class given a `Support` uses them unchanged (GG-T307).

<a id="enemy-supports-gg-t307"></a>**Enemy supports. Amended 2026-11-06 (GG-T307, owner — "Every monster needs support abilities."):** every one of the 115 enemy classes carries its own named `Support`. None of them uses the default strike. A monster stands as a supporter whenever its chain reaches the attacker of a Sentient enemy pincer, and it does not need to be Sentient itself, because only the two attackers must be. A tamed monster with the Pincer keyword brings the same block. Bosses reach 2 and hit harder:

| Family | Effect (Label) |
|---|---|
| Bats, vultures, flying monkey, sand maw | **Status** blinded 30–40 % (*Screech*, *Eye Peck*, *Flung Grit*, *Sandblast*); bat01 **Heal** 8 % (*Blood Share*) |
| Wolves, werewolf | **Crit** +20–25 % (*Pack Instinct*), +30 % (*Blood Frenzy*) |
| Frogs, toad, naga | **Status** poisoned 40–50 % (*Toxic Skin*, *Wart Venom*) or **Pushback** 0.5 s (*Tongue Lash*); Naga (boss) poisons every trapped hero, reach 2 (*Coil Venom*) |
| Slimes, marsh shamblers, ice mauler, tree golem 06 | **Status** slowed 35–50 % (*Ooze*, *Bog Grip*, *Rime Grip*, *Root Snare*) |
| Scorpion, skelepedes, corpse | **Status** poisoned 30–40 % (*Venom Sting*, *Rot Bite*, *Rot Miasma*) |
| Soldiers, ceramic knights, captain | **Buff** protection (*Close Ranks*, *Shield Wall*), ward (*Kiln Ward*), empowered (*Press the Attack*); Captain empowers the whole chain, reach 2 (*Battle Cry*) |
| Templars | **Heal** 12 % (*Battle Hymn*) or **Cleanse** the chain (*Purify*) |
| Gangers, goblin thug, lurkers, steppin razors, prototypes | **Status** blinded 35 % (*Pocket Sand*) or **Crit** +20–25 % (*Gang Up*, *Cheap Shot*, *Ambush*, *Razor Rhythm*, *Overclock*) |
| Oni, cyclopes, troll, mecha armor, yeti, bone pile | **Pushback** 0.5–0.75 s (*Club Sweep*, *Stomp*, *Piston Ram*, *Snow Shove*, *Rattle* 0.25 s); Cyclops bosses 1 s at reach 2 (*Boulder Toss*), Mountain Troll 1 s (*Landslide Shove*) |
| Tech gremlins, cyber zombies, phantom | **Status** silenced 25–30 % (*Short Circuit*, *Signal Jam*, *Wail*); gremlin01 **Haste** 0.75 s (*Tinker Boost*) |
| Mannequin | **Haste** its attacker 0.75 s (*Puppet Strings*) |
| Undead, ghost, gravestone | **Status** slowed 25–40 % (*Grave Grasp*, *Grave Chill*) |
| Tree golems | **Buff** regen (*Verdant Sap*) |
| Hags | **Cast** their Miasma at 50 % (*Hex Echo*) |
| Swamp Mistress | **Heal** the chain 12 %, reach 2 (*Fen Mending*) |
| Reaper | **Crit** +40 %, reach 2 (*Death Mark*) |
| Demon Lord | **Status** burning 75 %, reach 2 (*Hellbrand*) |
| Chronomancer | **Haste** all its allies 1 s, reach 2 (*Time Skip*) |
| Naga egg | **Heal** 5 % (*Brood Bond*) |

How each effect resolves from the enemy side:

- **Heal, Buff, Cleanse** land on enemy allies exactly as on heroes. A **Turns** buff lent to the enemy whose own turn closed the pincer gets one extra turn (`BattleRunner.supportTurnOwner`). An enemy's turn buffs tick down as its turn ends, so without that extra turn the buff would expire before any hero could hit it.
- **Haste** moves enemy turn icons left by that many seconds, or rushes an enemy cast. It lends no party tempo, because tempo belongs to the heroes.
- **Pushback** adds to the enemy pincer's own push, which is 0 s because only hero pincers carry `PincerPushbackSeconds`. A hero has no turn icon, so the push has nothing to delay on its turn. What it does hit is a pending hero **cast**: the cast slips by that many seconds, is never cancelled, is pushed once per action and is capped at 2 s.
- **Status, Cast and Crit** hit the trapped heroes, or lend the enemy attacker crit, through the same code as the hero side.
- **Mana** is hero-only. `ContentLoader.Validate` rejects a Mana support on any non-hero class.

The Bestiary shows the named sentence at the abilities tier (10 defeats), for example "As a [Supporter] (reach 1), Tongue Lash: adds 0.5 s of [Pushback] to each trapped foe." A foe's InspectCard shows the same sentence once it reaches that tier. *(Tests: `EnemySupportTests`.)*

*Presentation:* the supporter's cut-in card names the `Label`; its float and feed line add the effect ("Shadow Edge! +30% Crit", "Hex Echo! Fireball", "Venom Rain! Poisoned", "Shove! Pushback +0.5 s"), one sound per effect (POL-D4, reused ids: heal, orb, crit, status.debuff, whoosh, powerup, cast). `SupportText.Describe` renders the rule as one sentence with glossary [terms] — "As a [Supporter] (reach 1): gives the attacker +30% [Crit] chance on its pincer blows." — on the hero InspectCard (a foe's once its abilities are known), the hero page, the Abilities screen, the mercenary offers and the new-game party pick. *(Tests: `SupportEffectTests`, `Every_hero_class_performs_its_own_support_action`.)*

#### 1.2.4 Resolution order inside one pincer

1. `PincerFormedEvent` (attackers, opponents, supporters).
2. Attacker A strikes each living opponent in line order; then attacker B does the same. **Both endpoints hit the whole line.** A hero whose weapon is on its last durability point strikes at ×1.5 and then shatters ([§24.5](#245-weapon-durability)). An attacker with the **Double Attack** passive strikes each opponent a second time at ×0.5; any struck defender with **Counter Attack** that survives a landing hit strikes back at ×0.5 ([§7](#7-the-spell-catalog)).
3. Each supporter performs its support action (`SupportActionEvent`, then its effect): a Strike supporter strikes each living opponent at 50%; the others heal, buff, haste, charge mana, cleanse, echo a spell, inflict a status or add Pushback ([§1.2.3](#123-supporters)). **Amended 2026-11-06 (GG-T298):** a **Crit** supporter acts first, between steps 1 and 2 (its `SupportActionEvent` precedes the blows and its loan ends after step 2); the pincer's Pushback (one push per foe: its own seconds plus every Pushback supporter's, capped) lands after step 3.
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

<a id="13-humanoid-restriction"></a>

### 1.3 Sentient restriction

**Amended 2026-11-06 (owner, GG-T289, refined by GG-T294): a pincer takes a society, not a body shape.** ~~Only **Humanoid** actors can be pincer attackers (`Unit.IsHumanoid`; enemies need the `Humanoid` tag).~~ Heroes are always pincer-eligible. An enemy is eligible only if its class is tagged **`Sentient`** (`Unit.CanPincer`): it has a society or plans ahead, however rudimentary — soldiers, templars, captains, mecha and robots, goblins, gremlins, orcs and trolls, frog folk, hags, naga, and **intelligent undead** such as vampires and mummies. **Beasts, slimes, yetis, scorpions, tree golems, remains and mindless undead** (the shambling undead, cyber-zombies, skelepedes) never pincer. It is about sentience, not undeath (owner 2026-11-06). `CanPincer` replaces `IsHumanoid` everywhere it was read: `PincerDetector`, the enemy turn's pincer resolve, `EnemyPlanner`'s pincer-seeking and its Threat-vs-Closest `Auto` targeting, and the Bestiary's targeting label. *(Tests: `PincerEligibilityTests` — `Sentient_enemies_pincer_a_hero_line`, `Enemies_without_a_society_never_pincer`, `A_frog_closes_the_pincer_on_its_own_turn`, `A_yeti_flanking_a_hero_only_melees`, `Heroes_always_pincer`, `Real_content_tags_society_not_body`.)*

**Amended 2026-11-06 (owner, GG-T303): the Pincer keyword.** `Unit.CanPincer` = hero team **or** `Sentient` **or** the **Pincer** keyword (`Unit.HasPincerKeyword`: a tamed or domesticated monster, or a class tagged `Pincer`). A tamed beast is on the hero team, so it ends and supports *hero* pincers with the party; once its tame wears off it is a mindless enemy again and never pincers ([§25.11.3](#25113-taming-and-domestication-added-2026-11-06-owner-gg-t303)).

Multi-tile actors are never attackers. Sentient 1×1 enemies **seek** pincers (+50 move score, [§14.1.2](#1412-score-weight-cheat-sheet)) — **Amended 2026-11-06 (owner, GG-T306):** and actively **engineer** them, swapping places with monsters and heroes to complete a pincer, set up an end or slot a supporter into the chain ([§14.1.3](#1413-pincer-planning-gg-t306)) — and, **Amended 2026-10-09**, **resolve** them on their turn: after moving, if the enemy is one end of an enemy-team pincer, every such pincer resolves against the trapped heroes (no orbs are minted for enemies); otherwise it melees ([§14.1](#141-enemy-planning)).

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
5. The pincer carries Pushback: the goblin it hit acts 1 s later, wherever its icon was ([§2.3](#23-pushback-a-keyword-in-seconds), **amended 2026-11-06**, GG-T286).
6. The clock runs until the next enemy icon reaches the left edge and that enemy takes its turn (GG-T188, [§2.0](#20-the-held-turn)); any cast that reaches 0 on the way resolves.
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

**Amended 2026-11-06 (owner, GG-T286/GG-T287, inspired by Final Fantasy Resonance): one bar, no Zone, icons flow LEFT.** A horizontal track under the top HUD. Every icon (enemy turns and every cast, hero or enemy) moves left at its own steady rate, and **reaching the left edge is acting**. Where an icon sits is simply how long until it acts: `u = secondsUntilAct / BarSeconds` (`BattleEngine.SecondsUntilAct`, `BattleEngine.BarU`), with `BattleConfig.BarSeconds` = **10 s**; anything further off than 10 s waits at the right end. There is no ACT zone, no Pushback Zone and no crawl: the old `ZoneStart`/`ZoneU`/`ZoneCrossingTicks` are gone.

**Amended 2026-11-06 (owner, GG-T318): Speed sets where a foe re-enters, not how fast it travels; a recovery beat
spaces the foes.** Owner: "I want the 'average' time to move to be 4 seconds … when an enemy restarts on the bar they
start 4 seconds away on the far right but their speed is calculated so that they start a little more to the left the
faster they are, that's the main point of the speed stat for enemies; there would also need to be a 'respawn interval'
which takes place after the enemy attacks but before they appear back on the bar, the idea is to distribute enemies so
that the player can fight back." And: "this is the lifeblood of the entire game, this is what sets it apart from Terra
Battle's fixed 4 seconds."
- Every icon now drains its seconds at **one rate** ([§2.1](#21-speed-and-pace)). `BarSeconds` = **6 s**, the latest a
  foe re-enters.
- After acting, a foe leaves the bar for its **recovery**, then re-enters at its **entry**, set by its Speed: an
  average foe 4 s out, a faster one further left ([§2.12](#212-recovery-and-spacing-gg-t318)).
- *Why no Grandia-style ACT zone* (the owner asked for this to be weighed without preconceptions): Grandia 2's ACT
  stretch made commitment visible, aimed counterplay at it, and gave actions different time costs.
  - Heroes have no icons here (the window is the party's), so a hero-side COM/ACT stretch has nowhere to live.
  - Enemy charges already are that committed, cancellable stretch for spells (§13.4).
  - A band would break "position = seconds until acting".
  - So the action-dependent cost became the recovery (`RecoverSeconds`). Telegraphing a physical attack's target in an
    enemy's last seconds is a candidate follow-up.

```
 prominence     u = secondsUntilAct / BarSeconds (BarSeconds = 6 s)          recovery
  diamond   0 s  1    2    3    4    5    6 s                                 tray
   ◆◆◆      ◆━━━◇━━━━◇━━━━◇━━━━◇━━━━◆━━━━◇                                  ◇◇◇   [GUARD]
  ◆next◆    ◇ = 1 s delimiter, ◆ = 5 s delimiter (no Zone, no ACT)          ↑ just acted: off the bar,
   ◆◆◆      ◀── every icon (turn or cast) moves left at ONE rate              counting down its recovery,
 "NOW"/2.4s     (20 IP/tick × tempo × status × dial); at 0 s it acts          then slides on at its entry
                fast foe re-enters here ─┘    └─ an average foe, 4 s out
```

Turns and casts share the one axis, so a cast is a racer like any turn: it is born at `castSeconds / BarSeconds` and acts when it reaches the left edge. *(Tests: `TimelineResonanceTests` — `An_icon_sits_at_its_seconds_until_acting_over_the_bar`, `A_cast_is_born_at_its_cast_time_on_the_bar`, `Slow_on_an_enemy_lets_an_in_flight_cast_overtake_it`.)*

<a id="20-the-act-advanced-clock"></a>

### 2.0 The held turn

**Amended 2026-10-09 (owner decision):** the clock is **act-advanced**. **Amended again 2026-10-21 (owner, GG-T188 — reverses the act-advanced clock's "an action is 2.0 s"):** the timeline **runs while a hero is held**.

- Time is integer **ticks**, `BattleConfig.TicksPerSecond` = 10. A turn icon still loads `BarLength` = 1000 IP internally, but it is **drawn** at its seconds until acting (`u = secondsUntilAct / BarSeconds`, GG-T286).
- **Looking is free.** Nothing moves while you inspect, select or pick a target.
- **Picking up a hero starts the turn.** The timeline then runs in real time: the player's turn is the time from pickup until the **next enemy icon reaches the left edge** (acts) — `BattleEngine.TurnTicks` (the soonest `TicksUntilTurn`, no future pushback assumed), shown as `TurnWindowSeconds` = ticks ÷ 10 real seconds. ~~max(`MinTurnSeconds` 2 s, …) (the floor slowed the playback on a very short turn)~~ — **GG-T311:** no floor; a gap too short for a drag can't start one (below).
- **Forced drop.** When the window runs out the hero is dropped **where it is** — the path walked so far is the move — and that enemy acts. `MoveCommand.HeldSeconds` carries the seconds held; at or past the window (`BattleEngine.IsTimeUp`) the drop is forced (`TurnTimeUpEvent`) and the turn ends **even if the hero never left its tile**.
- ~~**Dropping earlier ends the turn early.** Every accepted command ends the player's turn: the clock runs until an enemy turn resolves.~~ **Amended 2026-11-06 (owner, GG-T308): the window is the party's, and every hero acts once in it.** Owner: "Pushing an enemy back gives you more time to move your selected hero — slide through heroes and enemies alike and create more alignments … The pushback happens after the drop (the hero acts) or as the result of an Ability, Item or Spell, all three of which consume that hero's turn."
  - **The window** is the time until the next enemy (or enemy charge's owner) reaches the left of the bar — `BattleEngine.TurnTicks`, the same gold hold lane and seconds readout. It is a **budget shared by the party**.
  - **Each living, able hero acts once per window** (`Unit.ActedThisWindow`; `BattleEngine.HeroesLeftToAct`): one drag, or one Ability/Item/Spell (or Talk). A hero who has acted can't be picked up or use its bar (`CanMove` false, `SlotStatus.Acted`; the engine refuses "Already acted").
  - **What an action spends** (`BattleEngine.CommandTicks`, run after the action resolves, measured against the window as it stood at pickup): a **drag** spends the share of the window it was held (`HoldTicks` = ⌊held ÷ `TurnWindowSeconds` × `TurnTicks`⌋, at least 1 tick; a forced drop spends all of it) — the same rate the view plays the bar at; an **Ability, Item or Spell** spends a fixed `BattleConfig.HeroActionSeconds` = **1 s** (a spell's cast time still rides the bar separately).
  - **Pushback and Slow buy time.** A pincer's pushback or a Pushback keyword lands with the drop/ability, *before* the time is spent, so pushing the foe that ends the window lengthens it for the heroes still to act; a Slow on it does the same through its rate.
  - **When the window ends:** if the time runs out during an action, that enemy acts there and then (the rest of that action's cost is not carried over); when **every** able hero has acted, the clock runs on to the next enemy turn — leftover time is **not** banked (only Guard banks it); Guard gives the rest of the window up ([§2.5](#25-auto-skip--shield)). After the enemy turn a **new window** begins and every hero may act again (`PlayerControlEvent.NewWindow`). Clearing a wave also opens a fresh window (no advance); with no enemy icon able to arrive (none left, all Frozen) a spent window is the fixed `ActionTicks` = 20 ticks.
  - **Turn boundaries are windows.** Mid-window control returns with `PlayerControlEvent { NewWindow = false }` and nothing else; hero buffs and skill cooldowns, Frozen on foes, tempo effects, pickups, corpses, the Amplifier roll, Regenerate and the TurnStart/TurnEnd triggers all count **windows** (party turns), not hero actions. ActionEnd triggers still fire after each hero's action.
  - **Seen:** a hero that has acted greys out with a ✓ on its token until the window ends (`UnitView.SetActed`, eased); mid-window the selection moves to a hero who can still act and a banner says "N heroes can still act · 2.3s left"; the next enemy keeps its gold ring; the hold lane shows what is left of the window.
- **Amended 2026-11-06 (owner, GG-T311): minimum action time — heroes go first, but only act in a gap wide enough.** Owner: "Heroes go first in the round, but unless they can get in a pincer between the intervals between enemies hitting left they have to sacrifice their turn, and that could lead to several enemy attacks in a row before a window wide enough for the player to act becomes available."
  - **The gap** is the window as it stands: seconds until the next enemy turn icon reaches the left edge (`BattleEngine.TurnTicks`; an enemy charge landing does not end it).
  - **An action starts only if the gap is at least its minimum** (`BattleEngine.MinActionSeconds`, `ActionFits`, compared in whole ticks rounded up — `NeedTicks`): a **drag** needs `DragPickupSeconds` **0.5 s** + `DragSecondsPerTile` **0.35 s** × one tile = **0.85 s** (`MinDragSeconds`; the human drag model is `DragSeconds(tiles)` = 0.5 + 0.35 × tiles); an **Ability, Item, Spell or Talk** needs its use time, `HeroActionSeconds` **1 s**. Too short: `CanMove` is false, the slot reads `SlotStatus.TooLittleTime`, Talk is unavailable, and the engine refuses the command with "Too little time" at no cost.
  - **A drag spends at least its minimum** (`CommandTicks` = max(held share, 0.85 s), never more than the window): a flick is still a pickup and a step. During the drag real time is spent as before, and the forced drop still applies.
  - **Forced sacrifice.** When heroes are left to act but no action of theirs fits (`BattleEngine.TooLittleTime`), the party can only **Guard**: it gives the sliver up, the enemy acts, and the next gap opens. If that one is too short as well, the party Guards again — a tight cluster of foes is several enemy attacks in a row. The forced Guard **keeps Guard's Protection on every hero and banks the time left as mana charge** (`GuardChargeFor`, so a 0.3 s sliver is 3 charge): it softens the blow without replacing an action.
  - **Rally** (`BattleState.Rally`): each forced Guard slows every enemy icon one tempo step (×0.8, stacking to `RallyMaxStacks` **5** ≈ ×0.33, on top of — outside the clamp of — the party's tempo), and the next hero action resets it (a new wave too). Without it, foes whose turns all fall closer together than an action (three wolves lapping in 2 s) would lock the party out for ever; with it, "several enemy attacks in a row" always end in a window. The banner adds "Rally N: foes slowed".
  - **Heroes act first.** A wave's enemy turn icons are held back at spawn so none reaches the left edge sooner than `OpeningWindowSeconds` = **3 s** (`BattleRunner.OpenPartyWindow`) — the party always opens the battle and every wave. ~~A foe too fast for that started from the beginning of the bar, a full lap.~~ **GG-T318:** the wave is scheduled one foe at a time through the spacer ([§2.12](#212-recovery-and-spacing-gg-t318)), each from its entry with recovery raised to the 3 s floor.
  - **GG-T318:** with recovery and spacing a lock of gaps all too short is rare (a crowded wave, eggs, remains), and Rally stays as its safety valve.
  - **Opening gaps.** Clusters are the danger; a pincer's pushback, a Pushback keyword or a Slow on the foe at the front of a cluster opens a gap behind it (pushing the *second* foe of a tight pair before the first acts turns the next gap from a forced Guard into a window).
  - **Seen:** the bar draws the current gap as a soft lane from the left edge to the next enemy icon with its seconds — **green** "move or skill", **amber** "move only", **red** "Guard" (`BattleScreen.SyncGap`); with no room, a banner says "Too little time — Guard (0.4s gap, a move needs 0.85s)" and GUARD is lit gold (`SyncGuardCall`); a slot reads "Too little time — needs 1.0s". Automation `/state` carries `too_little_time`.
  - **Bots model a human** (`HeroBot.ThinkSeconds`: smart **0.4 s**, casual **1.0 s** of hesitation while holding; a hop of `QuickDragTiles` 2 or fewer thinks half as long, and after `HurryAfterGuards` 2 Guards in a row it hurries with no think time — a player locked out of a usable window eventually just moves): a bot drag holds think + `DragSeconds(tiles)` (`MaxDragSteps(s, think)` tiles fit; a longer path is a forced drop), so a gap shorter than think + pickup + a tile offers it no drag; it never acts in a gap too short and Guards when forced; the smart bot still prices pushback/Slow by the hero actions the gap they open makes room for.
  - **Tuning (GG-T311):** `IpPerTickPerSpeed` 1.6 → **4.2** (Speed 10 laps in 2.4 s) and the difficulty dial gains an `EnemySpeed` column ([§22.9](#229-difficulty-settings-scaffolding-added-2026-10-11-owner-direction)). Per-stage 8 soak (15 stages), smart / casual wins: difficulty 10 **100% / 100% → 79% / 33%** (greenvalley 83/25, desert 91/41, swamp 66/25, cave 87/58, cityruins 66/16; no stage unwinnable for the smart bot), difficulty 5 100/100 → 99/85, difficulty 1 100/100 → 100/98. A campaign bot that never manages its camp no longer finishes at 10; the greedy+camp bot finishes 3/4 seeds (the trio no-story run 2/4).
  *(Tests: GG-T311 `MinActionTimeTests` — `Heroes_act_first_in_every_wave`, `An_action_is_refused_when_the_gap_is_shorter_than_its_minimum`, `Two_enemies_close_together_force_two_sacrifices_in_a_row`, `Pushback_opens_a_gap_that_then_allows_an_action`, `Bots_never_act_in_a_gap_too_short_and_replay_identically`.)*
- **The view, not Core, runs the held clock.** Core never ticks during a hold; `BattleEngine.TimelineUAfter(ticks)` gives the view where every icon would be, so the icons glide left in real time. The remaining time shows twice: a gold **lane** from the left end of the track to the next enemy icon (reddening as it runs out; GG-T286), and a **drain bar** under the held hero (green → red). A paused battle stops the hold.
- **Bot driver.** `HeroBot` drags ~~at `BotSecondsPerStep` = 0.25 s a tile~~ like a human (GG-T311): think time + 0.5 s pickup + 0.35 s a tile; a path longer than `MaxDragSteps` is cut where the time runs out (a forced drop), and every bot move carries its `HeldSeconds`. Since GG-T308 it is asked once per hero action, so it plays several actions a window; the smart bot also weighs the window as a budget (`TimeBalance`: a pushback on the foe that ends the window is worth the extra hero actions it makes room for — `ActionWorth` 4 each — and a long drag or a forced drop costs the actions it denies), prefers a setup an ally still to act can close this window, and counts a stall per window. The soak (`Bots_play_every_stage_without_breaking_invariants`) reports average turn length and forced drops per stage.
- Refused commands and no-op drags (let go early without moving) cost **nothing** (no ticks, `ActionsTaken` unchanged).
- **Cancelling a drag** (*added 2026-11-05, GG-T252*): releasing the held hero **off the board**, or back on **its own tile with nothing shoved** (the shove preview is empty), is a cancel — no command, no clock, `ui.cancel` and "Move cancelled" (`BattleScreen.DropCancels`/`CancelDrag`). A walk that returns home after shoving someone still commits (shoving is a tactic, GG-T190). A forced drop (the window ran out) is never cancelled.
- Each tick: every turn icon advances (or burns a stun tick), every cast icon counts down, tick-unit buffs accumulate ([§8.3](#83-ticking)), then arrivals resolve ([§2.4](#24-turn-order--arrival-order)).
- After the advance that ends a window (GG-T308), **control returns** (`ReturnControl`): hero turn-unit buffs and skill cooldowns tick, enemy timeline-halting buffs (Frozen) tick — skipping the boundary of the action that applied them — and `PlayerControlEvent`. The pending time bank is kept until the next enemy turn mints it.
- **Only enemies have turn icons.** **Amended 2026-10-09:** heroes have no icons; any living, mobile hero may act on any action. (The old bible drew hero portraits above the line.)

*(Tests: `Clock_only_advances_on_committed_actions`, `Unaffordable_or_silenced_spell_is_refused_without_cost`, `Enemy_acts_when_its_icon_reaches_the_trigger`, `Turn_length_is_the_time_until_the_next_enemy_icon_reaches_act`, `Forced_drop_ends_the_turn_where_the_hero_stands`, `Early_drop_ends_the_turn_and_the_next_enemy_acts_on_arrival`, `Countdown_badge_counts_turns_not_ticks`, `Bot_drags_fit_inside_the_turn_window`; GG-T308 `HeroWindowTests` — `Two_heroes_both_act_before_a_slow_enemy`, `Pushback_by_one_hero_lets_another_act_before_the_enemy`, `A_hero_cannot_act_twice_in_a_window`, `Guard_ends_the_window_and_banks_the_time_left`, `A_forced_drop_mid_window_spends_the_rest_of_it`, `A_drag_spends_its_share_of_the_window_and_a_skill_spends_HeroActionSeconds`, `Windows_replay_identically_from_the_same_commands`, `The_smart_bot_takes_several_actions_in_one_window`.)*

### 2.1 Speed and pace

**Amended 2026-11-06 (owner, GG-T286): one linear rate, the whole way.** ~~Open track at Speed × 1.6 IP/tick, then a shared 30-tick Zone crawl.~~
**Amended again 2026-11-06 (owner, GG-T318): one rate for every icon; Speed is where it re-enters.**
- ~~A turn icon gains `round(Speed × IpPerTickPerSpeed × tempo × status)` IP per tick, `IpPerTickPerSpeed` 4.2: Speed 10 laps in 2.4 s.~~
- Every turn icon gains `round(BaseIpPerTick × tempo × status)` IP per tick (`BattleEngine.TurnRatePerTick`). `BaseIpPerTick` = **20** against `BarLength` = **2000** IP, so a full load is 10 s. Enemies are also multiplied by the difficulty dial's `EnemySpeed`. The minimum is 1 IP/tick. Speed is not in the rate.
- **Speed sets the entry.** After its recovery ([§2.12](#212-recovery-and-spacing-gg-t318)) a foe re-enters the bar
  `EntrySeconds` from acting (`BattleEngine.EntrySeconds`, `EntryIp`):
  `EntryBaseSeconds × (ReferenceEnemySpeed(level) / Speed)^EntrySpeedExponent`, clamped to `EntryMin`..`EntryMax`
  (**4 s**, exponent **0.6**, **2–6 s**). `Formulas.ReferenceEnemySpeed(level)` = 11 + 0.75 × (level − 1) is the median
  enemy SPD at that level in classes.json, so a foe is judged against its peers and stays fast or slow at every level.

  | Lv1 foe | SPD | Re-enters |
  |---|---|---|
  | Wolf | 18 | ≈3.0 s |
  | Average | 11 | 4.0 s |
  | Slime | 8 | ≈4.8 s |
  | Bone pile | 5 | 6 s (cap) |
- **tempo** is the party tempo — hero Haste slows every foe, Slow on the party speeds them ([§2.11](#211-party-tempo-gg-t268)); **status** is the owner's own buffs (Slowed ×0.5; Frozen halts it entirely, `Unit.IsTimelineHalted`) — `BattleEngine.TimelineScale`. Slow, Haste and Frozen change only the **rate**, never the icon's place: a Slowed foe simply drifts left more slowly, so a cast or a faster foe behind it can **overtake** it. (A Hasten/Quicken landing *on an enemy* is the one exception: it jumps the icon forward, [§2.7.1](#271-hasten--quicken--the-inverse).)
- A cast icon counts its remaining ticks down one per tick; its `secondsUntilAct` is those ticks ÷ 10.
- `Speed` is the unit's effective SPD: level SPD + gear SPD + ½ gear AGI ([§3.2](#32-hp-and-stats)).
- *(Tests: `Timeline_rate_ignores_speed_and_speed_moves_the_entry_instead`, `Slow_on_an_enemy_lets_an_in_flight_cast_overtake_it`; GG-T318 `TimelineSpacingTests` — `An_average_foe_re_enters_four_seconds_out_and_a_faster_one_further_left`, `Entry_is_judged_against_the_level_so_a_fast_foe_stays_fast_at_every_level`, `Every_icon_flows_at_the_same_rate_whatever_its_Speed`.)*

### 2.2 Icon state

**Amended 2026-10-09:** the old per-icon modes (`Queued / Approaching / PushedBack / Stunned / Resolving`) collapse to data on `TimelineEntry`: `Ip` (turn icons, ≥ 1000 = arrived), `StunTicks` (ticks it will not advance; since GG-T286 only the part of a Pushback that IP 0 can't absorb, cleared by Hasten), `RemainingTicks` (cast icons, 0 = arrived) and `AccumulatedDelayTicks` (stagger, [§13.4](#134-the-interrupt-path)). An owner with a **timeline-halting** buff (Frozen) advances neither its turn icon nor its cast icon (`Unit.IsTimelineHalted`).

### 2.3 Pushback (a keyword, in seconds)

**Amended 2026-11-06 (owner, GG-T286, inspired by Final Fantasy Resonance): Pushback is a keyword, not a property of every hit.** ~~Any hero hit on an enemy in the Pushback Zone shoved its icon back by a depth × STR formula (`PushbackBaseU`/`PushbackMaxU`, 2026-10-11 tuning) and stunned it by AGI.~~ The Zone, the STR/AGI formula and the AGI stun are gone.
- **Only the keyword pushes.** An ability or item carrying `Pushback` (`AbilityDef.Pushback`, seconds) pushes each foe it reaches; **plain hits never push** — pincer strikes aside (below), supporter strikes, spells without the keyword, Mug, damage items, DOT, traps and shatter move no icon.
- **A hero pincer carries it.** Every trapped foe a hero pincer lands a blow on is pushed `BattleConfig.PincerPushbackSeconds` = ~~**1 s**~~ **1.5 s** (GG-T306 rebalance, [§14.1.3](#1413-pincer-planning-gg-t306)) (once, after the supporters act). Enemy pincers push nothing.
- **Anywhere on the bar.** The foe acts that many seconds later wherever its icon sits (`BattleRunner.Pushback`): it loses `ticks × its current rate` IP; whatever IP 0 can't absorb (an icon that just acted) becomes stun ticks, so the delay is always the full amount.
- **Capped and once.** One push is at most `PushbackMaxSeconds` = **2 s** (`ContentLoader.Validate` rejects a larger keyword), and a foe is pushed **at most once per action**, so a chained pincer or a multi-hit skill doesn't stack it.
- **A recovering foe recovers longer** (*GG-T318*): a foe still off the bar ([§2.12](#212-recovery-and-spacing-gg-t318)) has the push added to its recovery. It re-enters later but still at its Speed's entry, and the tray's ring grows.
- **A held cast slips instead.** A target with no turn icon but a cast in flight has the cast's remaining ticks raised by the same amount (`CastStaggeredEvent`); Pushback never cancels a cast.
- **Content:** the knockback and disruption skills carry 0.5–1.25 s (Shield Bash, Shove, Bamboo Slam, Juggernaut Charge, Covering Volley, Stasis 0.75; War Stomp, Haymaker, Phalanx Strike, Guard Break, Wide Cleave 0.5; Riposte, Overrun, Disrupt 1; Pinning Shot 1.25) and the Hinder spell the full 2 s. The old `DelayU` field is this keyword now ([§6.7](#67-movement-timeline-and-drain-gg-t183)).
- **Cast stagger is separate and unchanged** ([§13.4](#134-the-interrupt-path)): any landing hit on a caster still staggers (and can cancel) its cast.

*(Tests: `Pincer_pushes_its_foe_back_near_the_left_edge`, `Pincer_pushback_works_far_from_the_left_edge_too`, `Pincer_pushback_is_the_same_seconds_anywhere_on_the_bar_whatever_the_strength`, `TimelineResonanceTests` — `A_plain_hit_never_pushes`, `The_Pushback_keyword_pushes_its_seconds_anywhere_on_the_bar`, `One_push_is_capped`, `Validate_rejects_a_pushback_past_the_cap`.)*

### 2.4 Turn order = arrival order

Icons advance independently. Within a tick, arrivals resolve one at a time: **cast icons before turn icons, then lower entry id first** (`BattleRunner.Earlier`). **Amended 2026-10-09** (explicit tie rule). A resolving turn resets that icon to IP 0. Pushback, Hasten, Slowed and Frozen reorder icons only by changing IP/rate. **Amended 2026-11-06 (GG-T286):** turns and casts race on the one axis (`secondsUntilAct`), so the order on screen, left to right, *is* the order they act in; ties at the left edge still go casts first, then the lower entry id.

### 2.5 Auto-skip / Shield

**Unchanged by GG-T286/GG-T287** except where it sits: the **GUARD** button (the right end of the timeline, on the right gutter) issues `ShieldCommand`:
- Applies **Protection** (`buffs.json` `protection`: incoming damage ×0.85, 1 Turn) to every hero.
- Runs the clock until **one enemy turn has resolved** — the next actor reaches the left edge (cap 2000 ticks), then returns control — Protection expires at that control return, so it covers exactly the skipped stretch. **Amended 2026-11-06 (GG-T308):** Guard is the party's, not a hero's: pressed mid-window it gives up the rest of the window, so the heroes who haven't acted yet lose their actions; it banks only the seconds still left of the window (what the heroes before it spent is gone).
- Counts as an action; clears the pending time bank.

**Amended 2026-10-09:** it never auto-presses (there is no real-time window to run out). *(Test: `Shield_protects_heroes_and_skips_to_next_enemy_turn`.)*

**Amended 2026-11-02 (GG-T189, owner 2026-10-21): Guard banks unused turn time into mana.** Guard ends the turn early, and the turn time it gives up fills the forming mana bubble ([§3.1.9](#319-mana-charge)) before the clock runs:
- `charge = min(GuardMaxCharge, round(secondsLeft × GuardChargePerSecond))` — `BattleEngine.GuardCharge` / `GuardChargeFor`, reason `guard` (`ManaChargedEvent`; a rollover mints an orb with the same reason).
- `secondsLeft` = the turn window ([§2.0](#20-the-held-turn), `TurnWindowSeconds`) minus `ShieldCommand.HeldSeconds` already used (0 for the GUARD button); with no enemy icon able to arrive, the window is the `ActionTicks` turn it spends (2 s).
- Tuning in `BattleConfig`: `GuardChargePerSecond` = **10**, `GuardMaxCharge` = **40** (under half an orb). Guard ends the turn, so one conversion per turn is the per-turn limit by construction.
- Protection is kept as Guard's defensive effect. The drag-timer time bank ([§3.1.8](#318-time-banked-orbs)) is fed only by moves; Guard still clears any pending drag remainder, so the two never stack.
- The HUD shows it as the filling bubble plus a "Guard · Mana +N · Xs banked" billboard.

*(Tests: `GuardBankTests` — conversion maths, time-left proportionality, overflow at 12 orbs, Guard ending the turn after banking; `Guard_still_banks_the_time_left_to_the_next_arrival`.)*

<a id="26-one-timeline-two-lanes--turn-icons-above-cast-icons-below"></a>

### 2.6 One track, diamond icons, the prominence diamond

**Amended 2026-11-06 (owner, GG-T287, inspired by Final Fantasy Resonance): the timeline is redrawn as one track of diamonds flowing left** (`BattleScreen.Timeline.cs`). ~~Enemy turn circles on a line with the next attacker nearest ACT on the right; casts shown only as a ring around the caster (2026-10-10, "casts are not on the timeline").~~
- **One track, no labels.** A thin line from the prominence diamond (left) to the GUARD button (right), with a **small diamond delimiter every second** and a **larger one every 5 s** across the `BarSeconds` = 10 s span. No Zone overlay, no "ACT".
- **Every icon is a diamond.** A 45° bordered square whose content is clipped to the diamond (overflow hidden, stencil-masked when rotated) around a counter-rotated image: an enemy **portrait** for a turn, the **ability icon** for a cast. Nothing shows outside the diamond.
- **Borders say whose it is:** enemy turn **red**, boss **purple**, hero cast **teal** (`Palette.CastHero`), enemy charge **amber** (`Palette.CastEnemy`). The **next actor** rings **gold** with a lift on the bar, and its board tile gets a gold border too.
- **Casts ride the bar** (fixes the old line-32 split between "below the line" and "not on the timeline"): every cast in flight, hero or enemy (in-playback cast ghosts included), is a diamond on the same track, born at `castSeconds / BarSeconds`; it joins the bar only when its `CastQueuedEvent` beat plays, and a stagger or a Pushback visibly slips it right. The caster's radial `CastRing`, the target's spell chip and the "lands in 3s" banners stay.
- **The prominence diamond.** A large diamond at the far left (the left gutter) shows **whoever acts next** — a portrait for a turn, the ability icon for a cast — with its seconds left under it ("2.4s"), and **"NOW"** while that actor is acting. A new actor taking the slot plays a whoosh.
- **Approach glow.** Inside `ApproachSeconds` = **3 s** of acting, an icon's halo **and its owner's tile** (the caster's tile for a cast; `BoardView` cue layer) pulse together in the icon's own colour, ramping to full at 0 s, so icon and unit read as one; the glow starts with a channel cue. A halted (Frozen) icon doesn't glow.
- **Overlaps:** icons stay centred on the track; overlapping diamonds form a cluster and take turns on top, one per 1.2 s beat (`LayoutIcons`). A unit that isn't moving (Frozen, Asleep, stunned) shows a status badge and dims; the same debuff is a pip on its tile.
- **GUARD** sits at the right end; the hold lane ([§2.0](#20-the-held-turn)) and the tempo readout ([§2.11](#211-party-tempo-gg-t268)) anchor to the track's left end.
- **Spell VFX (owner direction):** every spell plays one readable arc (`SpellVfx`): the caster *draws energy* (motes spiral in, its frame glows in the spell colour), a *cast burst* ring, then a mote per target *curves to the space above it*, from where the element impact plays — lightning strikes down from that point, flames burst, frost rings, heals sparkle.
- **Tap to select, hold to read** (*added 2026-11-06, owner, GG-T288*): before a move — no drag, playback or aiming under way — a **tap** on any timeline icon or on the prominence diamond selects its owner exactly as tapping it on the board does (a hero becomes the selected hero, a foe fills the actor panel). The selected unit is cross-highlighted: a **white ring** on every one of its diamonds (turn, cast, the prominence slot) and a **white outline** on its board tile (`BoardView.SetSelection`). A **hold** still opens the InspectCard ([§9](#9-the-hud)). What either shows is gated by the Bestiary knowledge tier ([§25.11](#2511-bestiary)).

**Shared clock — casts fire "off-turn" by design.** Turns and casts advance on the same ticks; whatever arrives first resolves. Lifecycle: up to **4** hero casts may load at once (`MaxHeroCastsInFlight`; enemy charges don't count) — a fifth cast-time spell is refused before any orbs are spent; orbs are spent at cast start; the icon spawns with `ticks = SecondsToTicks(Formulas.CastTime(base, caster))`; on arrival it resolves (`ResolveCast`) and is removed. A spell with `CastTime` 0 resolves immediately.

**Amended 2026-10-09:** every hero spell in `abilities.json` has base `CastTime` 1.5 s; after WIS+INT scaling (12–15 ticks for the starting heroes) it normally resolves **inside the same action's 20-tick advance**, unless an enemy turn earlier in that advance staggers it into later actions. *(Tests: `Cast_time_spell_rides_timeline_then_resolves`, `Fifth_hero_cast_is_refused_by_cast_cap`.)*

### 2.7 Worked example: pushback math

**Amended 2026-11-06 (GG-T286):** ~~the depth × STR example (IP 920, STR 18 → 0.275 u plus a 7-tick AGI stun)~~ is replaced. *(The example below keeps the GG-T286 numbers: 1.6 IP/tick per Speed and a 1 s pincer push. With 4.2 IP/tick (GG-T311) and 1.5 s (GG-T306) the method is the same: seconds × the foe's current rate.)*

A goblin (Speed 10 → 16 IP/tick) sits at IP 920: 80 IP to go → 5 ticks → **0.5 s** from acting (u 0.05, nearly at the left edge). Two heroes pincer it.

```
push      = PincerPushbackSeconds              = 1 s = 10 ticks
IP lost   = 10 ticks × 16 IP/tick              = 160
new IP    = 920 − 160                          = 760   (240 IP to go = 15 ticks = 1.5 s, u 0.15)
```

Net: the pincer bought exactly 1 s, the same at IP 920 or IP 100 and whatever the attackers' STR. A goblin that has just acted (IP 0) can't lose IP, so it stands still for those 10 ticks instead (`StunTicks` 10). A Shield Bash (`Pushback` 0.75) in the same action adds nothing: one push per foe per action.

#### 2.7.1 Hasten / Quicken — the inverse

`BattleRunner.Hasten(target, u)` (Quicken `HastenU` 0.30; the boss `enrage` skill 0.35):
- On a unit with a **turn icon**: clears its stun and adds `round(u × 1000)` IP, clamped to 1000 (so it may fire on the next tick and can overtake icons ahead of it).
- **Amended 2026-10-09:** on a unit with **no turn icon but a cast in flight** (i.e. a casting hero): subtracts `round(u × 1000 / 16)` ticks from the cast (Quicken rushes an ally's spell by ~19 ticks). *(Test: `Quicken_hastens_an_enemy_icon`.)*
- **Amended 2026-11-05 (GG-T268):** on a **hero**, haste also raises the **party tempo** ([§2.11](#211-party-tempo-gg-t268)). Since the held turn (§2.0) heroes have no turn icons, so this is what hero haste does.

### 2.8 Spawn rules

| Trigger | Icon |
|---|---|
| Wave start (incl. battle start) | each enemy's turn icon at a random **IP 0–250** (`SpawnStaggerIp`) — **Amended 2026-10-09** (replaces u=0 + queue delay) |
| Snake segments | no icon (the head owns the chain's turn) |
| Hero spell with cast time | teal cast diamond on the bar at `castSeconds / BarSeconds` (GG-T286/GG-T287) |
| Enemy charge | amber cast diamond on the bar at its charge's `CastTime` (default `DefaultChargeCastSeconds` 2.5 s → u 0.25; not stat-scaled) |
| Pushback / Hasten | existing icon's IP changes (a held cast's remaining ticks) |

### 2.9 Drag timer (optional)

**Amended 2026-10-09 (owner decision):** Settings → "Drag timer (4 s)" (`SettingsSave.DragTimer`, off by default) restores Terra Battle's hand pressure: once a drag starts, `DragTimerSeconds` = 4 s counts down on screen (real time, unscaled by battle speed) and the drag auto-drops at 0. The seconds left at the drop travel in `MoveCommand.TimeLeftSeconds` and feed time-banked orbs ([§3.1.8](#318-time-banked-orbs)). The timer never affects the game clock. **Amended 2026-10-21 (GG-T188):** the held turn ([§2.0](#20-the-held-turn)) now gives every drag a real-time limit of its own; the optional 4 s drag timer is kept as a stricter cap and still feeds the time bank (Guard's banking of unused turn time is GG-T189).

### 2.11 Party tempo (GG-T268)

Owner 2026-11-05: hero haste "slows enemy motion down the timeline, thus you are moving hastily". Several hastes **stack**, and Slow on the party pushes the other way.

- **One signed number.** `BattleState.Tempo` is a list of `TempoEffect`s (source, signed amount, turns left). Their sum is the **net tempo** (`NetTempo`).
- **What it does.** Every turn icon not on the hero team advances at its normal rate × `TempoStep^(−net)` (`TempoStep` 1.25), clamped to **×0.5–×2** (`TempoMin/MaxMultiplier`), so neither side ever freezes or runs away. Net +1 is ×0.8, +2 ×0.64, −1 ×1.25. `BattleEngine.TimelineScale` applies it everywhere an icon's rate is read: the clock, `TicksUntilTurn`, the countdown badges (§2.10), the held-turn window (§2.0) and the icon glide.
- **Sources.**
  - A haste (`HastenU`) landing on a hero (an ability, Fade Step's landing, a pickup) adds `max(1, round(HastenU / 0.15))` points for `AbilityDef.TempoTurns` (default `DefaultTempoTurns`, 2). Quicken 0.30 is +2; Relay, Follow-through and Fade Step are +1.
  - A buff with `BuffDef.Tempo ≠ 0` applied **to a hero** adds that many points for its duration. `slowed` carries −1, so a Slow cast on the party (the Chronomancer's Temporal Drag, §14.10) cancels haste and then speeds the foes up.
  - **Cleansing gives it back** (*added 2026-11-05, GG-T275*): when a tempo buff is removed from the **last** hero carrying it (a cleanse, a cure), its tempo effect lifts with it (`BattleRunner.DropBuffTempo`). Expiry still follows the tempo effect's own turn count.
  - Haste on an enemy still slides its own icon (§2.7.1) and doesn't touch tempo.
- **Per cast.** One cast is one effect, however many allies an area haste touches.
- **Timing.** Effects count down at the party's turn boundary (`ReturnControl`), and skip the boundary of the command that applied them, so "2 turns" means the next two turns.
- **Seen and heard.** `TempoChangedEvent` updates a "Tempo +2" / "Tempo −1" readout above the timeline's left end. The track tints cool (Haste) while foes are slowed and red while the party is, with a rising or falling cue and a feed line giving the multiplier. The InspectCard of a haste ability states the tempo it grants.
- *(Tests: `TempoTests`.)*

### 2.12 Recovery and spacing (GG-T318)

Owner 2026-11-06: "a 'respawn interval' which takes place after the enemy attacks but before they appear back on the
bar, the idea is to distribute enemies so that the player can fight back and have time to push back dangerous enemies
or to cast spells that will provide healing or shielding … there would be instances when enemies would cluster, and in
those cases the player would either try to pull off a really quick move, or sacrifice their move for a defensive bonus
and convert the remaining time into a portion of mana orb." The cluster fallback is GG-T311's forced Guard ([§2.0](#20-the-held-turn)).

- **Recovery.** When a foe's turn resolves (`BattleRunner.ScheduleReentry`), its icon leaves the bar.
  - It sits in recovery for `RecoverBaseSeconds` **1 s**, plus the largest `AbilityDef.RecoverSeconds` keyword it used
    that turn (0–3 s, validated), plus the spacer's delay, ± the difficulty row's jitter.
  - Then it re-enters at its entry ([§2.1](#21-speed-and-pace)).
  - Recovery is **not** scaled by Slow, Haste, Rally or the dial. Frozen holds it. It counts in `TicksUntilAct`, so
    the window, the badge and the bots all see it.
  - Heavy, telegraphed charges recover longer:
    - **1.5 s:** fireball, temporal drag.
    - **1.0 s:** miasma, thunder, ice, enrage.
    - **0.5 s:** smite, rally, mend.
    - A wolf pack's howl recovers 0, so a pack keeps its pressure.
- **The spacer.** `TimelineSpacing.Delay` (pure) takes the foe's natural next turn and the turns of every other foe and
  every enemy charge in flight.
  - It holds the foe up to `MaxSpreadSeconds` longer, so its turn lands at least `MinGapSeconds` from all of them.
  - When nothing in that range fits, it takes the widest gap on offer. A crowded bar still clusters: big waves, eggs,
    remains, and a pushback or Slow that reshuffles the order.
  - The extra time goes into recovery, never into the entry, so the bar always reads Speed honestly.
  - The columns live on the difficulty dial ([§22.9](#229-difficulty-settings-scaffolding-added-2026-10-11-owner-direction)):
    1.6/2.0 s at level 1 down to **1.0/1.0 s** at 10, where a spaced gap always fits one skill. Every row adds ±0.25 s
    of seeded jitter.
  - The neutral row (rules tests) has no spacing and no jitter. Test stages use the neutral row.
- **Spawns.** A wave is scheduled one foe at a time in entry order (`OpenPartyWindow`), with the 3 s opening floor.
  Reinforcements, eggs and risen remains are spaced when they arrive. Tamed allies enter at their entry and are never
  spaced.
- **Pushback and haste.** A push on a recovering foe adds to its recovery (§2.3). A Hasten/Quicken on one eats its
  recovery first, then moves it along the bar.
- **Seen.** A recovering foe waits in the **recovery tray** between the track's right end and GUARD, dimmed, with a
  countdown ring, then slides onto the bar at its entry (GG-T319).
- **Tuning (GG-T321)** — `Tests/Core.Tests/TimelineTuningSoak.cs`, all runs in `docs/research/TimelineTuning.md`.
  - At difficulty 10 with campaign parties:

    | | Smart | Casual |
    |---|---|---|
    | Wins | 100% | 90% |
    | Forced Guard share | 14% | 22% |

    The median gap is 2.1 s and 22% of gaps are under a minimum drag.
  - This fixes GG-T316. Tithe Moors I with the real three heroes was 6%/0% with 35–50% forced Guards; it is now 4/4
    smart, 0/4 casual, with 18–24% forced Guards.
  - At an average 4 s, no timeline setting moves the smart bot below 97%.
  - **Amended 2026-11-07 (GG-T321, decision delegated by the owner): challenge comes from party size.** On this clock
    the starting three manage about 1.3 actions per enemy turn and six heroes about 2.4. So enemy strike and MaxHP
    (and plates) grow by `EnemyScalePerExtraHero` **0.30** for each hero fielded beyond `PartyScaleBaseHeroes` 3
    (`BattleEngine.PartyScale`, `BattleState.PartySize`). Test stages are unscaled.

    | Difficulty, party | Smart | Skilled | Casual |
    |---|---|---|---|
    | 10, campaign | 100% | 100% | 45% |
    | 10, six | 100% | 98% | 38% |
    | 5, campaign | 100% | 100% | 97% |
    | 1, campaign | 100% | 100% | 100% |

    Tithe Moors I is unchanged. The skilled column is `HeroBot.Skilled`: SkillLevel 80, 0.8 s think. A global
    strength raise was rejected because it walled the first battle; see `docs/research/TimelineTuning.md`.
- *(Tests: `TimelineSpacingTests`.)*

### 2.10 Countdown badge

**Amended 2026-10-09 (new, Terra Battle–style):** every enemy shows a number on its tile — `BattleEngine.ActionsUntilTurn` = `(TicksUntilTurn − 1) / 20`: the actions you get before it acts (**0** = it acts during your next action, drawn red). **Amended 2026-10-21 (GG-T188):** a turn now ends at each enemy arrival, so the number counts the distinct arrival ticks of the *other* enemies (each re-loading from IP 0) before this one's — the turns you get before it acts (**0** = it acts at the end of your next turn). Since GG-T308 a "turn" is the party's window (every hero acts once in it), so the number counts windows, not hero actions. **GG-T318:** each other foe's next lap is estimated as base recovery plus the trip from its entry, not a reload from IP 0. `TicksUntilTurn` simulates its icon with stun at its one linear rate (no Zone crawl since GG-T286) but no future pushback/hasten; Frozen enemies show no number. The actor panel says "Acts in N moves", "Acts after your next move!", "Frozen in time" or "Charging X!".

---

## 3. Resources

### 3.1 Mana (the orb economy)

**Amended 2026-10-10 (owner decision): mana is generic.** Every orb is identical and any spell can be paid from any orbs. `ManaBank` is a capped count (capacity **12**) shared by the party ([GG-LAW-6](#GG-LAW-6)). The HUD draws 12 orb slots; filled slots glow mana-blue, empty slots are dim.

#### 3.1.1 Costs

A spell's cost is a whole number of orbs (`AbilityDef.Cost`, an int in `abilities.json`); the UI renders it as that many orb pips (`Ui.CostPips`). Content costs are 1–3 (the former letter counts: Fireball 2, Bolt 3, Heal 1, …). Skills and items cost 0.

#### 3.1.2 Harvesting

**Amended 2026-10-11 (owner: "mana needs to be a more rare resource … the number of participants in the attack increases the multiplier needed to earn a mana bubble"):** pincers, crits, interrupts and the amplifier no longer mint whole orbs — they fill the **mana charge** meter ([§3.1.9](#319-mana-charge)). Orbs still mint whole through `BattleRunner.MintOrb` (event `OrbMintedEvent`, reason string) from:
- **Mana charge** rolling over 100 (reason = what filled it: `pincer`, `critical`, `interrupt`, `amplifier`, `guard`).
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
| 2-hero pincer that takes half a foe's HP | 52 charge (×1) |
| The same damage with 2 supporters joining | 26 charge (÷2) |
| Third link of a chain, same damage | 79 charge (×1.5) |
| Critical hit (physical or spell) | +10 charge |
| A Mana supporter (Red Mage, Ritualist, …) joining a pincer | its charge × 0.5 (Red Mage 30 → 15) |
| Any of the above with the bank at 6/12 · at 12/12 | × 0.65 · × 0.30 (fullness damping, [§3.1.9](#319-mana-charge)) |
| Steal/Mug, 3 adjacent enemies, 2 rolls succeed | 2 orbs |
| Cancel an enemy charge | +25 charge |
| Battle start, one Mage Robes equipped | 2 |
| Drop with ≥ 3.75 s left on the drag timer, then an enemy turn | 3 (cap) |
| Guard with 2 s of turn left (GG-T189) | 20 charge (cap 40) |

#### 3.1.8 Time-banked orbs

**Amended 2026-10-09 (owner decision):** the bank is fed by the **optional drag timer**, not by real-time seconds before an enemy trigger.
- A committed move stores `PendingTimeBankSeconds = clamp(TimeLeftSeconds, 0, 4)` (0 when the timer is off).
- At the start of the **first enemy turn** during that action's clock advance (`MintTimeBank` in `RunEnemyTurn`), it converts to **1 orb per `SecondsPerTimeBankOrb` = 1.25 s**, max `MaxTimeBankOrbs` = **3**, with a "Banked N mana" announcement.
- It is cleared at control return and by Shield. Consequence (current truth, tuning watch): a quick drop that is **not** followed by an enemy turn within the same action banks nothing.

#### 3.1.9 Mana charge

Added 2026-10-11 (owner direction). `BattleState.ManaCharge` (0–99) is the forming bubble; the HUD draws it filling the next empty orb from the centre. `AddCharge` emits `ManaChargedEvent` and every full `ManaChargePerOrb` = **100** becomes an orb.

- **Pincer:** `charge = PincerChargePerHp (105) × share × (1 + 0.25 × chainIndex) ÷ (1 + 0.5 × (participants − 2))`, minimum 2 when it lands. `share` = Σ over the trapped foes of `min(1, HP removed by this pincer ÷ MaxHP)`; `participants` = the two attackers + every supporter. So a lean two-hero pincer earns at full rate, a four-hero dog-pile at half, and chaining is the way to farm.
- **Critical** +10, **interrupt** +25, **amplifier** +30. Kills alone earn nothing.
- **Mana supporters** (`SupportEffect.Mana`: Black Witch, Harbinger, Machinist, Red Mage, Ritualist, Tinkerer) charge their `Magnitude × SupportManaFactor` (0.5).
- **Fullness damping** (*Amended 2026-11-05, GG-T247*): every earned charge above (pincer, crit, interrupt, amplifier, support, Guard, Mana Siphon armor) is multiplied by `1 − ChargeFullnessDamping (0.7) × Bank.Count ÷ Bank.Capacity` before it lands (`BattleEngine.DampedCharge`; a positive charge never rounds below 1). An empty bank fills at the full rate and a full one at 30%, so mana stays easy to start and hard to max out. Explicit grants are whole orbs and skip it: items, pickups, Steal/Mug, battle-start gear, the time bank and carried orbs.
- **Guard** (GG-T189): the unused turn time at 10 charge per second, max 40 ([§2.5](#25-auto-skip--shield)). This is the "fractional orb": the bubble is progress toward the next orb, and a rollover with the bank at 12 fades ([§3.1.4](#314-bank-full-rules-overflow)) while the remainder stays in the bubble.
- The billboard shows the arithmetic ("Mana +34 · 3 heroes ÷1.5 · chain ×1.25").
- **Where the economy sits** (*measured 2026-11-05, GG-T265*, after GG-T247's tempering and GG-T262's 45% enemy HP; 206 bot battles, 12 per stage): the late-campaign flood is gone (the bank is full on 0% of greedy turns and 1–3% of casual turns, 0–7% of orbs fade, where cityruins-03 had been full on 94%). Bots bank about 0.11 orbs per player action (greedy) and 0.05 (casual), and a casual bot holds 2+ orbs on about 23% of turns. GG-T247's "casual holds 2+ orbs on ≥ 30% of turns" target is **retired**: it measures hoarding, and a bot that spends the moment it can rarely holds two whatever it earns. Raising `PincerChargePerHp` to 200 to buy a spell-per-wave earn rate lifted the casual bot only to 0.08, and flooded the Tithe Moors stages (full on 36–55% of turns) because that bot doesn't spend there, so the charge stays at 105. POLISH G3 stays the gate (no flood, ≤ 20% wasted).

*(Tests: `More_heroes_in_a_pincer_earn_less_mana_per_hp_taken`, `Charge_rolls_over_into_orbs_and_keeps_the_remainder`, `A_killing_pincer_earns_mana_by_hp_taken_not_per_hero`, `Supporters_join_but_dilute_the_mana_charge`, `ManaEconomyTests`. Instrument: `ManaEconomyReport` — `[Explicit]`, run with `--filter ManaEconomyReport`, `GAIDEN_MANA_SEEDS=N` — bots play every campaign stage and report how full the bank runs.)*

### 3.2 HP and stats

`Unit.Effective` = `LevelStats` + `GearStats`, with **Speed = level SPD + gear SPD + ½ gear AGI** (**Amended 2026-10-09**); STR, VIT and Speed are floored at 1.

| Stat | Drives | Read in |
|---|---|---|
| HP / MaxHP | life | `Formulas.MaxHp = floor(12 + 5·VIT + level)`; an enemy spawns with `× EnemyHpScale` (0.45, GG-T262) |
| STR | physical offense; pushback; stagger push | `Formulas.Physical`, `Pushback`, `StaggerCasts` |
| VIT | HP + physical defense | `MaxHp`, `Physical` |
| AGI | Evasion; pushback stun recovery; Steal; ½ into Speed from gear | `Formulas.Evasion`, `Pushback`, Steal roll |
| SPD | timeline rate; hit chance | `RateFor`, `Formulas.HitPercent` |
| STA | reserved | — |
| INT | enemy magic offense; cast time; threat weighting | `Formulas.Magic`, `CastTime`, `PickTarget` |
| WIS | magic defense; cast time; cast poise | `Magic`, `CastTime`, `StaggerCasts` |
| LCK | crit; hit; Evasion; Steal; Clutch; damage variance tilt | `CritPercent`, `HitPercent`, `Evasion`, Steal, Clutch |

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

**Amended 2026-11-06 (GG-T296, owner): Active, Passive, Reactive.** Skills, spells and items are the *Active* kind: you take them. Two more kinds never sit on the bar. A **Passive** just happens (Vampire's Kiss heals when you attack; Tough Hide always softens blows). A **Reactive** fires after something happens *to* you, usually on a chance ("when [attacked directly], a [slight] chance to [Counterstrike]"). Heroes and enemies use the same system, so a character can be built to benefit from being hit, dodging or being surrounded.

- **Data:** `AbilityDef.Trigger` (`TriggerDef`): `On` (the event), `Chance` (0..1; 1 never touches the Rng; optional `ChancePerLuck`), event parameters `Count` (OnSurrounded), `Threshold` (OnLowHp) and `Status` (OnStatusGained), an optional `Condition` (owner HP below/above, has/lacks a status, adjacent foes ≥ n, the other unit's tag, the damage type), a list of `Effects`, and the guards `Cooldown` (own turns) and `OncePerAction`. `Callout` overrides the float text. Everything is checked by `ContentLoader.ValidateTrigger`.
- **Events (`TriggerOn`):** `Always` (modifiers only), `OnAttack` (a primary weapon attack, hit or miss), `OnLandedHit`, `OnDamaged`, `OnAttackedDirect` (an adjacent foe's blow lands), `OnAttackedAny` (any foe's blow or ability lands), `OnDodge` (a foe's blow or charged spell misses or is slipped by Evasion, §13.1.0), `OnSurrounded` (at turn start), `OnStatusGained`, `OnLowHp` (a hit crosses the threshold), `OnAllyKO`, `OnKill`, `OnPincerAsAttacker`, `OnPincerAsSupporter`, `TurnStart`, `TurnEnd`, `ActionEnd`. A hero's turn starts and ends each time control returns to the party; an enemy's is its own turn.
- **Effects (`TriggerEffectKind`):** `Heal` (fraction of max HP), `Lifesteal` and `Reflect` (fraction of the context's damage), `ApplyBuff`, `Counterstrike` (a blow at ×Amount on a foe in reach), `FollowUp` (strike the target again at ×Amount of the blow), `CastAbility` (an Active ability resolved instantly and free through the normal dispatcher), `Cleanse`, `ManaCharge` (heroes), `Pushback` (seconds, foes). The `Always` modifiers are `DamageMultiplier` (the owner's weapon blows), `ReductionMultiplier` (damage taken) and `Immunity` (statuses). `Who` (Self/Target/Attacker) overrides each effect's default target.
- **Engine (`BattleRunner.Triggers.cs` + pure `Rules/Triggers.cs`):** `Triggers.Sources(unit)` lists a unit's passives in a fixed order: class kit, learned passives (`Unit.Passives` from `HeroSpec.Passives` from `HeroSave.Known`), enchants on intact armor, then the weapon's named lifesteal. The funnels fire it: `Damage` (OnDamaged, OnAttackedAny, OnLowHp), `Strike` (OnDodge, OnLandedHit, OnAttackedDirect, OnAttack), `Kill` (OnKill, OnAllyKO), `ApplyBuff` (OnStatusGained), pincer resolve, turn starts and ends, and the end of a command. Owners go in unit-id order, abilities in source order, and only a chance below 1 draws from the battle Rng. **A reaction never provokes a reaction:** a Reactive can't fire from inside another Reactive's effects, and no ability re-enters itself. A Passive such as Lifesteal still rides a counter blow.
- **Presentation:** every firing emits `TriggeredEvent` ahead of its effects. The view floats the callout over the unit with a sound: a clang for Counterstrike/Reflect, a war cry for a free cast, a power-up otherwise (POL-D4). The InspectCard and the bestiary kit line show **Passive** or **Reactive** with a sentence generated from the data (`TriggerText.Describe`), keywords in [brackets].
- *(Tests: `TriggerEngineTests`, `ArmorEnchantTests`, `Double_attack_passive_strikes_twice_and_counter_strikes_back`.)*

#### 4.1.1 Skill cooldowns

A skill locks for `AbilityDef.CooldownTurns` after use: **Steal 3, Mug 2, Teleport 3**. Countdowns live per hero in `Unit.Cooldowns`. **Amended 2026-10-09:** they tick **once per committed action** at control return (the old "turn-cycle" = hero window is now one action). A cooling slot shows "CD n" and refuses ("Recharging"). *(Test: `Teleport_relocates_and_fires_pincer_then_goes_on_cooldown`.)*

#### 4.1.2 Signature passives (GG-T302, added 2026-11-06)

Owner: "You could design a character that benefits from being hit, or from dodging hits, or from being surrounded by enemies." Every hero class carries **one or two** Passive/Reactive rows in its `classes.json` `Abilities` (they never reach the bar, `MetaServices.IsHeroUsable`), and most enemy families carry 0–2 so fights read differently. All are `abilities.json` rows on the §4.1 engine; numbers stay modest.

| Archetype | Classes | Signature (id) | Rule |
|---|---|---|---|
| Vampire | vampire | Vampire's Kiss (`vampires_kiss`), Blood Feast (`blood_feast`) | landed blow → [Lifesteal] 15%; a kill heals 10% |
| Guard Stance duelists | duelist, fencer, ronin, sellsword, swordmaster | Guard Counter (`guard_counter`) | while [Warded] (Guard Stance's ward), every direct blow is answered with a [Counterstrike] ×0.6 |
| Dodge fighters | duelist, swordmaster, dervish, martial artist (Evasive Riposte); fencer, chroma ninja, speedster, courier, hexstalker (Blur); thief, white ninja (Catch Breath) | GG-T297 rows | OnDodge synergies |
| Shield tanks | knight, defender, paladin, jade knight | Shield Wall (`shield_wall`) | [surrounded] by 2+ at turn start → [Ward] |
| Brawler tanks | myrmidon, tank, panda girl | Unbowed (`unbowed`) | [surrounded] by 2+ → [Empowered] |
| Hit-builders | warchief, shieldmaiden, prizefighter | Grudge (`grudge`) | damaged → 20% [Empowered], rests 2 turns |
| | fighter, defender, sellsword | Battle Hardened (`battle_hardened`) | attacked directly → 20% [Protection] |
| | dark templar | Dark Retribution (`dark_retribution`) | attacked directly → 35% [Reflect] 30% |
| Berserkers | barbarian (Berserk); bruiser, wild child (Blood Rage `blood_rage`); odachi, ronin (Grim Resolve `grim_resolve`) | | below 50% ×1.35; crossing 50% → [Empowered]; crossing 35% → heal 15% |
| Ninjas / rogues | green ninja, slasher (Venom Edge); red ninja (Ember Edge); yellow ninja (Static Edge); blue ninja (Rime Edge 10%); black ninja (Shadow Edge); street fighter (Dirty Fighting 20%) | `*_edge`, `dirty_fighting` | landed blow → 15% status on the target |
| Finishers | assassin, night hunter (Vanish → [Blurred]); striker, ripper, harbinger, hexsniper (Bloodlust → [Empowered]); raider (Blood Feast) | | on a kill |
| Counter-punchers | pugilist (Counterpunch 20% ×0.6), lancer (Set Spear 25% ×0.75), basher (Rattle: 15% [Pushback] 0.5 s on a landed blow) | | |
| Healers | cleric, sister, doctor, alchemist, white ninja, white witch (Mending Touch: after supporting a pincer, heal the backed attacker 5%); cleric, sister, sage (Vigil: an ally falls → [Regen]); monk (Inner Calm: 3% each turn start); alchemist (Poison Ward); white witch (Pure Spirit: can't be [Silenced]) | | |
| Mages | black witch, red mage (Arcane Harvest: a kill → 10 [mana charge]); ritualist (Blood Price: damaged → 4, once per action); sage, tinkerer (Arcane Insight: supporting → 5); drifter (Chill Aura: 25% [Slow] the direct attacker) | | |
| Engineers / rangers | machinist, operative (Shock Plating: 25% [Pushback] 0.75 s on the direct attacker); engineer, technician (Self-Repair: below half HP, 5% each turn start); hexwarden (Keep Away 35%, 1 s); blue lion (Vengeance) | | |

**Enemies** (same rows, readable at the Bestiary's abilities tier): frogs, toad, Swamp Mistress — **Toxic Skin** (attacked directly → 35% [Poison] the attacker); gangers, wolves, templars, captain — **Vengeance** (an ally falls → [Empowered]); cyber zombies — Poison Ward + **Rotting Bite** (20% poison); undead, skelepedes, marsh shamblers — Poison Ward; ghost — **Ethereal** ([Physical] ×0.8) + Poison Ward; phantom — Ethereal; mecha armor — **Mirror Plating** (a landed attack → 50% [Reflect] 20%); slimes — **Gelatinous** (damaged → 25% [Protection]); hags — **Spiteful Hex** (25% [Blind] the direct attacker); lurkers — Evasive Riposte; tech gremlins — Shock Plating; flying monkey — Blur; goblin thug — Dirty Fighting; Demon Lord, oni — **Infernal Hide** (can't [Burn]); Ice Mauler, yeti — **Frostborn** (can't [Freeze]) + Chill Aura; vulture — **Carrion Feast** (an ally falls → heal 15%); reaper — Blood Feast. Bosses carry two: Cyclops 00–02 Tough Hide + Blood Rage, Naga **Serpent's Retort** (35% [Counterstrike] ×0.6) + Poison Ward, Chronomancer **Time Slip** (35% [Pushback] 1 s) + Steadfast, Demon Lord Lifesteal + Infernal Hide. Bats, soldiers, mannequin, ceramic knights, steppin' razors, prototypes, oni, troll, werewolf, tree golems and sand maw keep their GG-T296 rows.

- **Text is honest:** every chance in a description uses its band word ([slight] ≤15%, [small] ≤27%, [fair] ≤42%, [good] ≤62%); `ContentLoader.Validate` also fails a `TriggerText` sentence whose [terms] miss the glossary (new entries [Lifesteal], [Reflect], [mana charge], [Physical]; "attacked directly" is an alias of [directly]). Guard Stance now says what it does (Ward, and Guard Counter while it holds); the Training Rapier +3 lifesteal passive is *Bloodied Point*, not "Riposte"; Counter Attack's text names [attacked directly]; Mirrorcurse's node says it blinds.
- *(Tests: `SignaturePassiveTests`.)*

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
- **Spell** — `Bank.Spend(cost)` → if `CastTime > 0` queue a cast icon (target unit remembered for PickActor, tile for PickTile), else resolve immediately. Every accepted use is an action (it ends the turn at the next enemy arrival, GG-T188).

#### 4.4.1 Select → confirm → act

Added 2026-10-11 (owner: "confirmation screens when deciding to cast a spell"). With Settings → **Confirm before acting** on (default):
- A pick ability: tap the slot → legal anchors glow gold → tap a tile → the pick **locks**: its area and every unit it would touch glow (red harmful / green helpful / blue teleport), the banner asks "Poison on Wolf? Confirm below", and the actor panel becomes the **confirm card** (icon, name, cost pips, timing, "On Wolf", **Cast/Use/Go** and **Cancel**). Tapping another legal tile re-aims; tapping the same tile again confirms.
- An Auto ability (Taunt, War Cry, Mass Heal …) goes straight to the confirm card.
- Nothing is spent until Confirm (`BattleScreen.ConfirmPending`, chime `ui.confirm`); Cancel / slot re-tap / Back play `ui.cancel`.
- **Outcome preview** (*added 2026-11-05, GG-T252, POL-D1*): the confirm card lists up to three touched units with their damage (red) or heal (green) **range**, **KO** chance, statuses and their chance, and the orbs the action would mint. The numbers come from `OutcomePreviewer.Preview` (Core, pure): it runs the command on six `BattleState.Clone`s, the first with the battle's own dice and the rest re-seeded, and reads only the actor's resolution window (until the next enemy turn starts; reopened when the actor's own charged cast resolves). The real battle and its RNG are never touched (`OutcomePreviewTests`). The confirm column is 28% of the card (≥ 264 px), not a fixed 330.
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

**Amended 2026-11-05 (GG-T285) — one shared bag:** the party's item slots draw their charges from **one** shared inventory, in party order (`BattleFactory.Build`). Before, every hero's spec got a full copy of the inventory, so 5 owned X-Potions put a stack of 5 on every bar (25 heals) and the overdraw was clamped away after the battle. A fresh game still stocks and carries 5 × 5.

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
- **Drag preview numbers** (*added 2026-11-05, GG-T252*): while a held hero's path makes a pincer, every foe it would hit wears a small tag with the damage range and KO chance (`OutcomePreviewer`, three samples per step), cleared on drop or cancel.

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
   1. ~~**Scan** → `ScannedEvent` (stats, HP), mark the class Scanned + Seen.~~ Retired 2026-11-06 (owner, GG-T290): Bestiary knowledge is earned by defeats ([§25.11](#2511-bestiary)); `IsScan` and `ScannedEvent` are gone.
   2. **Cleanse** (`RemovesDebuffs`: all debuffs) / **cure** (`RemovesBuff`: one id).
   3. **Fire × Frozen / Wet**: a damaging Fire hit force-expires Frozen (its chain applies Wet), then strips Wet ("Steam!").
   4. **Steal** (`StealsMana`) roll → random orb or "Missed!"; **Mug** adds a fixed physical hit (`MugDamage` 10 × physical resistance × incoming multiplier).
   5. **Damage** (`BaseDamage > 0`) → `Formulas.SpellDamage` (Lightning on Wet ×1.5, note "Shock!"), × `BonusMultiplier` vs `BonusVsTag`; then **Lightning blind roll** 30% → Blinded.
   6. **Knockback** (`Knockback`, [§6.6](#66-knockback-added-2026-10-10-owner-direction)), then **Pull** (`Pull`) and **Swap** (`SwapsPlaces`) ([§6.7](#67-movement-timeline-and-drain-gg-t183)).
   7. **Heal** (`BaseHeal`). **Amended 2026-10-22 (GG-T183):** a heal only ever lands on the caster's side. On a foe it is a **drain** when the ability's filter is `EnemyOnly` (the caster is mended instead), and nothing when the filter is `Any`; and an ability that both harms and heals never harms a friend (before this, Soul Feast, Collect the Debt, Bloodletting and Blood Harvest healed the foe they hit, and Harm into Hope scorched allies and mended enemies).
   8. **Hasten** (`HastenU`), then **Pushback** (`Pushback` seconds, foes only; was `DelayU` before GG-T286, [§2.3](#23-pushback-a-keyword-in-seconds)).
   9. **Buff** (`ApplyBuff`, chance `ApplyBuffChance`): Sleep on a Warm target lasts ×1.5 (ceil). **Amended 2026-11-05 (GG-T266):** a *beneficial* buff (`BuffKind.Buff`) never lands on a foe: on a foe-targeting ability it is the **caster's own**, granted once per cast however many foes are caught (War Stomp and Battle Cry make the caster taunt, Opening Jab empowers the caster's next strike; before this, all three buffed the enemies they hit). *(Test: `A_beneficial_buff_on_a_foe_targeting_ability_lands_on_the_caster_not_the_foe`.)*
5. **Step back** (`StepBack`): the caster backs away from the nearest foe.
6. Battle-end check.

**Amended 2026-10-22 (GG-T183):** a teleport with `HastenU` (Fade Step) also hastens the caster when it lands.

`Damage()` is the single damage choke-point: armor (snake), Sleep-break, HP, threat, death + loot, then (non-miss, with a source) cast stagger ([§13](#13-combat-resolution)). **Amended 2026-11-06 (GG-T286):** `Damage()` no longer pushes; only the Pushback keyword and a hero pincer do ([§2.3](#23-pushback-a-keyword-in-seconds)).

### 6.1 Projectile motions

`AbilityDef.Motion`: `None / Straight / Bezier / Homing / Spiral / Twist / Strike`. **Amended 2026-10-09, corrected 2026-10-13 (was stale):** `BattleScreen.Playback.ProjectilePoint` gives every motion its own curve — `Bezier` arcs, `Homing` curls in late from the side, `Spiral` corkscrews in on a shrinking radius, `Twist` oscillates side to side, `Strike` drops from directly overhead — `Straight`/`None` is the only flat line, and Reduce Motion flattens everything to it.

### 6.2 VFX pipeline

**Amended 2026-11-05 — effect prefabs (owner, GG-T269: "Prefabs that played either once or in a loop; they all might need to be normalized"):** where `vfxstyle.json` names an effect, a premade particle prefab plays instead of the code-drawn burst. The prefabs are the owned GridGame2026 set (54 prefabs plus their materials, textures and meshes, imported with their GUIDs into `Resources/Vfx` and `Assets/Gaiden/Vfx`, licence record `Assets/Gaiden/Vfx/SOURCES.md`).
- **Catalog:** `vfx.json` (validated) lists every effect: `Mode` Once or Loop, `Tiles` (on-screen size of its 80th-percentile particle reach), `Extent` (authored size, measured by `GaidenBuild`-style batch tool `VfxImport.Prepare` into `tools/vfx-measure.json`), `Duration` (a Once effect stops emitting then fades), `Tint`, `Cue`, `Source`.
- **Mapping:** an element lists `TierPrefabs` (Minor/Major/Ultimate). A tier it leaves out, an unmapped element (Ice and Arcane today) and Reduce Motion all keep the code-drawn effect, which stays the fallback. A status names its own `Prefab`; it never inherits its element's impact. A Loop status prefab (burning → flame, wet → bubble) stays on its unit exactly while the unit stands with that status, reconciled every frame, so death, cleanse, expiry and wave end all stop it.
- **Rendering:** an orthographic effects camera renders layer 31 into a RenderTexture, one world unit per board pixel. A composite pass rebuilds alpha from brightness, because additive legacy particles write none. A stage-sized UI Toolkit overlay inside the board's FX layer shows it, following resizes (§26), so harness screenshots and MindAttic.Capture include it.
- **Time:** pause sets every live system's `simulationSpeed` to 0 (frozen mid-burst), and battle speed scales it. Instances are pooled.
- **Imported materials:** URP-only ones moved to the legacy particle shaders, renderers with no material got `GaidenDefaultParticle`, and the old scripts and audio sources were stripped.
- **Checked:** harness `vfxprefabs` films every catalog effect into a contact sheet (only effects that read well there are mapped), pause freezes them pixel-for-pixel, 60 fps with a dozen heavy effects live, and they stay aligned at 9:23 and 3:4.

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

**Amended 2026-10-13:** damage and buff-tagged spells still read their look from `DamageType`/the applied buff id (`ElementBurst`, `StatusBurst`) — already rich and per-type (Fire/Ice/Lightning/Poison/Holy/Dark each have a bespoke burst, and poisoned/burning/frozen/sleep/slowed/silenced/blinded/taunting/empowered/protection/regen each have a bespoke take-hold animation). What used to fall through to one generic purple ring — Scan (retired 2026-11-06, GG-T290, with its `ScanBurst`), pure cleanse/dispel (`RemovesDebuffs`/`RemovesBuff`), and hasten-only spells (no damage, no heal, no buff) — now get their own bespoke gold bursts (`ScanBurst`/`CleanseBurst`/`HastenBurst`, `BattleScreen.Playback.cs`) and read gold during the cast wind-up too (`CastColor`). The named `CastVfx/ProjectileVfx/ImpactVfx/LingerVfx` strings on individual abilities (e.g. distinguishing Fireball from Meteor) are still not read by any code — still ⬜ for a future pass, still code-drawn UI Toolkit if it happens, never particle prefabs (law #3).

**Amended 2026-10-19 — death-portrait dissolve restored (owner: "the only way to see the complete artwork of these enemies... a quintessential reward for killing an enemy", GG-T96):** on `DiedEvent` the board token vanishes at once and a fresh single image of the unit's **full, uncropped portrait** (`Art.Portrait`, the same in-memory texture the token was framing through its Thumbnail ([§12.1.1](#1211-thumbnails-are-a-window-onto-the-portrait-added-2026-11-03-owner-direction-gg-t220)), shown whole, and no frame, HP bar, pips, glow or any other part of the `UnitView`) appears on the board's fx layer (below floating numbers and the coin burst, above every token, mark and line), centred on the fallen unit's footprint (2×2 bosses on their whole footprint) and nudged back inside the board at its edges so the whole art reads. It is sized to fit a box of (2 + footprint) tiles, starts at 196/255 opacity and, frame-rate independently and scaled by Battle speed, jitters up to ±1/12 tile around its start point, shrinks as ×0.99 per 60 fps frame and fades 0.6/s until gone (≈1.3 s), exactly GridGame2026's `PortraitManager.Dissolve` + `Portrait3DInstance.DissolveRoutine`. It is non-blocking: playback waits only a 0.12 s beat, so the loot burst and the next kill in a chain play while it dissolves, and several deaths each get their own. Every dying unit gets one (corpses show the fallen class's art; units with no portrait skip it). The death sfx and the "★ X is defeated" feed line are unchanged. Reduce Motion drops the jitter; the shrink and fade still play. (`BattleScreen.Playback.cs` `DeathAnim`/`DeathDissolve`.)

**Amended 2026-11-05 — one visual language (GG-T253, owner: "it doesn't look like a collection of random effects"):** the per-type bespoke bursts above are gone. `Resources/Content/vfxstyle.json` (`VfxStyleSheet`, validated by `ContentLoader.Validate`) is the one table every effect reads ([§12.3](#123-vfx)):
- **Elements** (every `DamageType`, plus the `Heal` and `Water` families): a palette (`Core`, `Glow`, `Accent`), a particle **shape** (spark, shard, ember, bubble, mote, smoke, rune, ray, glyph, droplet), a **motion** (burst, rise, fall, implode, spiral, pulse, contract, bolt, rays, drift, billow), a timing token, count, size, reach, an optional ring and the sound family (`Cue`). `Palette.Element` reads its hue from here.
- **Statuses** (every buff id) name the element they come from and inherit everything they leave empty: Burning reads as Fire, Frozen as Ice (converging, with a held frost tint), Wet as Water, Poisoned as Poison (with a passing green tint), Regen as Heal. The rest have their own look (Sleep's drifting "z" glyph, Slowed's spiral, Silenced's closing rune ring, Blinded's smoke, Protection's and Ward's pulse, Empowered's rising embers and swell). Taunt keeps its shockwave and threat lines (a readout of who is being pulled), drawn in its style's red. `LingerEvery` sets how often a lasting status sheds a wisp.
- **Routing:** `ElementBurst`, `StatusBurst`, `TickLinger`, `HealBurst`, the spell trail and the named `ImpactVfx` flourishes all draw through one emitter (`BattleScreen.VfxStyle.cs`, `Emit`). A named flourish family (Explosion, Cloud, Sparkle, Beam, Aura, Drift, Slash, Strike) is now only a *motion* played in the ability's own element style, so Fireball and Frost share a gesture but never a look.
- **Intensity tiers** (`Tiers`: Minor, Major, Ultimate) scale particle count, size, screen shake and hit-stop (0 / 40 / 90 ms). An ability's tier is its optional `Intensity`, else derived from its price (3+ orbs or a 5+ turn cooldown = Ultimate; 2 orbs or 3+ turns = Major; `VfxStyleSheet.TierOf`). A pincer's hits are Major; a crit, or a hit for a quarter of the target's HP, lands one tier higher.
- **Every hit is anticipation → impact → follow-through:** the target braces (an Instant squash), the element bursts while the frame holds for the tier's hit-stop, then the target recoils past rest and settles (`Ease.Back`). Durations come only from the timing tokens and easing only from the easing tokens (`Ease.Arc` replaces the hand-written `Sin(k·π)` curves); no duration is random.
- Reduce Motion halves particle counts and drops the anticipation, follow-through, hit-stop and shakes.
- **Prefab-ready (owner direction 2026-11-05):** every element style and every tier carries an optional `Prefab` id (null today; a status inherits its element's). A follow-up brings back real effect prefabs (GridGame2026's VisualEffects/Particles plus free/CC0 packs) on an effects-camera overlay: when set, the prefab replaces the code-drawn burst (a tier's `Prefab` overrides its element's), and the code-drawn burst stays the fallback. Nothing reads the field yet.
- **Contact sheets:** `python tools/harness.py vfxsheet [RUN] [LIMIT]` plays every non-passive ability's look (dev `devvfx` endpoint: cast, flight, impact at its tier, the status; state untouched) and saves a frame strip per ability (`artifacts/<run>/strips/`) and a grid per element (`artifacts/<run>/<Element>.png`).
*(Tests: `VfxStyleTests`.)*

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
| `Pushback` (seconds) | timeline | **Amended 2026-11-06 (GG-T286; was `DelayU`, a bar fraction):** the Pushback keyword ([§2.3](#23-pushback-a-keyword-in-seconds)). Each foe it reaches acts that many seconds later, wherever its icon sits; at most `PushbackMaxSeconds` 2 s (validated at load) and one push per foe per action. A target holding a cast has the cast slip by the same seconds instead; it never cancels the cast. Never lands on a friend. | `PushedBackEvent` / `CastStaggeredEvent` |

2×2 bosses, snake segments and remains don't budge ("Won't budge"). None of the moves is a hero step, so none of them forms a pincer: movement never deals damage ([GG-LAW-1](#GG-LAW-1)). *(Tests: `AbilityAuditTests`: `Pull_reels_a_foe_in_toward_the_caster`, `Pull_stops_beside_the_caster`, `Swap_trades_tiles_with_the_target`, `Step_back_puts_distance_between_the_caster_and_the_nearest_foe`, `Pushback_keyword_slides_the_foes_turn_icon_back`, `A_heal_aimed_at_a_foe_drains_into_the_caster`, `Harm_and_heal_in_one_burst_hurts_only_foes_and_mends_only_friends`.)*

## 7. The Spell Catalog

All entries in `abilities.json`. Every spell has base cast time **1.5 s** (scaled by WIS+INT, [§13.1.4](#1314-cast-time)).

| Spell | Cost | Shape / Mode / Filter | Motion | Effect | Role |
|---|---|---|---|---|---|
| **Heal** | 1 | SingleActor / PickActor / AllyOnly | None | +25 HP | spot heal |
| **Mass Heal** | 2 | AllAllies / Auto / AllyOnly | None | +12 HP each | emergency top-up (cost 1 → 2, GG-T247: a party-wide heal for one orb) |
| **Antidote** | 1 | SingleActor / PickActor / AllyOnly | None | removes all debuffs | cleanse |
| ~~**Scan**~~ | — | — | — | **Retired 2026-11-06 (owner, GG-T290):** Bestiary knowledge comes from defeats ([§25.11](#2511-bestiary)). Save migration drops Scan from every hero's Known list and bar; in class kits healers got Quicken instead, supports Silence, tanks Cleave | — |
| **Fireball** | 2 | SingleActor / PickActor / EnemyOnly | Twist | 18 Fire + Burning | burst + DOT |
| **Frost** | 2 | Square(1) / PickTile / EnemyOnly | Bezier | 10 Ice + Frozen | AoE control |
| **Bolt** | 3 | Row / PickActor / EnemyOnly | Strike | 14 Lightning (×1.5 Wet), 30% Blind | row clear |
| **Poison** | 2 | Cross(1) / PickTile / EnemyOnly | Bezier | 6 Poison + Poisoned | long tail |
| **Sleep** | 1 | SingleActor / PickActor / EnemyOnly | Homing | Asleep (breaks on damage/move) | hard CC |
| **Slow** | 2 | Row / PickActor / EnemyOnly | Bezier | Slowed (icon ×0.5) | row tempo |
| **Quicken** | 1 | SingleActor / PickActor / Any | Straight | +0.30 u (or rush a cast) + Empowered (GG-T263: heroes have no turn icons, so haste alone was inert; GG-T268) | tempo |
| **Silence** | 1 | SingleActor / PickActor / EnemyOnly | Straight | Silenced | see note |
| **Meteor** | 2 | Diamond(2) / PickTile / EnemyOnly | Strike | 22 Fire + Burning | big AoE |
| **ShockWave** | 3 | Column / PickActor / EnemyOnly | Strike | 10 Lightning | column cleave |
| **CrossHit** | 3 | Plus / PickTile / EnemyOnly | Strike | 8 Lightning | row + column |
| **Steal** (Skill, CD 3) | free | Cross(1) / Auto / EnemyOnly | Homing | per adjacent enemy: LCK+AGI roll → orb | resource grab |
| **Mug** (Skill, CD 2) | free | Cross(1) / Auto / EnemyOnly | Straight | Steal roll + 10 Physical — see note | steal + hit |
| **Teleport** (Skill, CD 3) | free | SingleTile / PickTile / EmptyOnly | None | relocate + pincer scan | mobility |

Enemy-only entries: `charge_fireball` (Fire), `charge_ice` (Ice), `charge_thunder` (Lightning), `charge_smite` (Holy) — 2.5 s charges with base 0 (damage comes from the INT-vs-WIS roll, [§13.4](#134-the-interrupt-path)); `enrage` (self Hasten 0.35, Cyclops phase 2). **Amended 2026-11-05 (GG-T283):** an enemy charge with `CooldownTurns` > 0 rests that many of the caster's own turns (counted at each turn start) before it can be charged again; `charge_smite` has 2, so a templar charges every other turn and closes in between.

**Passives and Reactives** (`Kind: "Passive"` / `"Reactive"`, active from a class's `Abilities`, a learned skill-tree node or an armor enchant; never on the bar, never bought). **Amended 2026-11-06 (GG-T296):** each is now a `Trigger` row on the engine of [§4.1](#41-the-three-kinds); no passive has its own code.

| Id | Kind | Trigger → effect |
|---|---|---|
| `double_attack` | Passive | OnAttack → FollowUp ×0.5 (a pincer attacker or a melee swing strikes the same foe again) |
| `counter_attack` | Reactive | OnAttackedDirect → Counterstrike ×0.5, callout "Counter!" |
| `lifesteal` | Passive | OnLandedHit → Lifesteal 25% of the damage |
| `tough_hide` | Passive | Always, physical damage → ReductionMultiplier 0.7 |
| `berserk` | Passive | Always, below 50% HP → DamageMultiplier 1.35 |
| `regenerate` | Passive | TurnEnd → Heal 6% max HP |
| `poison_ward` | Passive | Always → Immunity: poisoned ("Warded!") |
| `steadfast` | Passive | Always → Immunity: frozen, sleep |
| `thorns` | Reactive | OnAttackedAny → Reflect 25% of the damage |
| `mana_siphon` | Passive | OnLandedHit → ManaCharge 5 |

**GG-T302** adds 44 more rows (hero signatures and enemy family passives, listed in [§4.1.2](#412-signature-passives-gg-t302-added-2026-11-06)). The three proficiency passives carry no trigger; they only gate equipment ([§24.2.1](#2421-proficiency-gating-gg-t52)). Every one now works for heroes and enemies alike: a hero who learns Tough Hide, Lifesteal or Double Attack from a skill tree gets it in battle. Before, those were enemy-only code paths, and learned passives never reached the battle. *(Tests: `TriggerEngineTests`, `Double_attack_passive_strikes_twice_and_counter_strikes_back`.)*

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

### 7.2 Usefulness pass (GG-T263, GG-T264, 2026-11-05)

POLISH G4/G5 ("every ability earns its slot", "no dead or dominant abilities") read from `records.py --abilities` over the `polish.py --balance` soaks. Measured at 12 casual battles per stage plus the greedy class sweep (206 battles); the 4-per-stage default swung a stage ±25 points with party make-up alone.

- **What was wrong:**
  - **Cleave crowded out every finisher.** It is the striker baseline in every striker's slot 0, and at 24 damage on a 2-turn cooldown it beat the bespoke 8–16-damage adjacent finishers. 27 abilities never fired.
  - **`protection`** (×0.85 for 1 Turn) was too light to spend an action on.
  - **Several spells cost more than they did.**
  - **Hero haste is inert under the held turn** (heroes have no turn icons, §2.0; GG-T268).
- **Changes:**
  - **Cleave** 24 → 20.
  - **Plain finishers now out-hit it on their longer cooldowns:** Combo/Prism Fang 20, Jab-Cross 22, Contract 20, Flurry 18, Perfect Cut 24, Iaijutsu 26, Shock/Flame Fang 18, Finishing Blow 20, Reckless 24.
  - **Finishers with a rider move closer to Cleave:** blind, silence, delay, steal, knockback and swap finishers go to 12–18.
  - **Spells:** Shadow Bolt, Shock, Bloodrite and Swap cost 2 → 1. Mirror Curse 10, Overdrive 12, Frost Nova 14 and Fury 20 damage.
  - **Support:** Moonlit 22 heal. Transmute and Jury-rig also mend 10, and Howl and Insight deal 6 and 8.
  - **Haste-only tempo skills** (Quicken, Shortcut, Relay, Blitz) also empower their target.
  - **New `ward` buff** (×0.70 for 2 Turns) for every hero guard skill; Guard keeps `protection`.
  - **GG-T264 trims:** Blood Harvest is re-costed 3 → 2 but drains 7/10 (at 10/14 it measured 73 impact/use against a Spell median of 28), and `empowered` lasts 3 Turns.
- **Impact metric (GG-T264):** `records.impact` used to credit every buff, debuff and tempo effect a flat 6. It now credits what each one did after it landed (`records.credit_utility`), in HP:
  - **damage-up:** the extra share of the buffed ally's damage
  - **guard or taunt:** damage prevented, plus every hit a taunter absorbed
  - **ticks:** damage and heal ticks at face value
  - **frozen or sleep:** a skipped enemy turn counts as one enemy turn's damage
  - **slow:** the enemy turns it took away
  - **blind:** expected misses
  - **silence:** half an enemy turn per turn
- **Result (same seeds, before → after):** G 69.3 → 74.9.
  - G1 50.0 → 66.7, G4 59.7 → 73.4, G5 74.7 → 78.0, G7 61.5 → 76.9, G3 100 → 100.
  - G2 85.7 → 78.6: swamp-02's lone-hag wave deals about 1000 damage to a casual party and tips that stage, a stage-tuning matter.
    **Amended 2026-11-05 (GG-T274):** Arkgrave II's wave 2 is now a frog and a marsh shambler instead of the lone kiting hag (hags stay in Arkgrave III's finale), and its Naga closer (used on no other stage) drops to Strength 10 / Vitality 10 (was 15 / 12). Casual wins 3/12 → 9/12.
  - G6 53.5 → 50.4.
  - Remaining G5 lows are pure utility buffs (taunts, wards, empowers), which do little against 45%-HP foes. Trapline is high (78) because its row Slow takes real turns away.

## 8. Buffs and Debuffs

`BuffDef` (`buffs.json`) + `BuffInstance` (`Remaining`, `TickAccumulator`) on `Unit.Buffs`, applied and removed only by `BattleRunner.ApplyBuff / RemoveBuffsWhere / Expire`.

### 8.1 Catalog

| Id | Kind | Duration | Knobs | On expire |
|---|---|---|---|---|
| `protection` | Buff | 1 Turn | incoming damage ×0.85 — Guard's shield and the enemy rally | — |
| `ward` | Buff | 2 Turns | incoming damage ×0.70 — every hero guard/ward skill (GG-T263; it had used `protection`, too light to be worth an action) | — |
| `empowered` | Buff | 3 Turns | outgoing damage ×1.3 (GG-T264: 2 → 3 Turns; at 2 it mostly ran out before the buffed hero struck again) | — |
| `burning` | Debuff | 5 Ticks | 4 dmg/tick | → `warm` |
| `frozen` | Debuff | 1 Turn | immobile, halts timeline | → `wet` |
| `wet` | Debuff | 6 Ticks | Lightning ×1.5; Fire strips it | — |
| `warm` | Debuff | 3 Ticks | Sleep ×1.5 duration | — |
| `sleep` ("Asleep") | Debuff | 3 Turns | immobile, breaks on damage / move | — |
| `poisoned` | Debuff | 6 Ticks | 3 dmg/tick | — |
| `slowed` | Debuff | 2 Turns | timeline rate ×0.5; on a hero, party Tempo −1 ([§2.11](#211-party-tempo-gg-t268)) | — |
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

**Hold to read** (*added 2026-11-05, GG-T252, POL-D1 recognition over recall*): holding anything for `InspectCard.HoldSeconds` (0.45 s) opens one floating **InspectCard** (`App/UI/InspectCard.cs`) anchored above the finger (below when there's no room), clamped inside the stage and safe area, eased in over `Timing.Quick` with `ui.select`; releasing anywhere closes it, and the press that opened it never also acts. It works on: ability-bar slots (kind, cost pips, cooldown, reach, damage/heal, buff and its chance, movement effects, description, and why it can't be used now), units on the board (level, HP and stats or ??? until the Bestiary knows them — `BestiaryKnowledge.Tier`, GG-T290, [§25.11](#2511-bestiary) — when it acts, a charging foe's **whole spell**, its brain, plate, every status), status pips in the actor panel, **timeline icons** and the prominence diamond (turn icons → the unit; cast icons → the spell and how to push it back; a *tap* instead selects the owner, GG-T288, [§2.6](#26-one-track-diamond-icons-the-prominence-diamond)), and in menus: the Abilities screen's bar cells and Known/Learn rows, every `ItemUi.Row` (shop, equip, items — the full item detail), talent nodes, and the Bestiary's ability chips. The ability bar's five slots share the stage width with even 16-px gutters (they grow on a wide stage instead of the gaps).

**Tap a [term]** (*added 2026-11-06, GG-T300*): wherever a description renders (the hold cards above, the ability card/confirm card in the actor panel, enemy kit lines and status rows, the Abilities screen, every item detail and weapon passive, gear compare, enchants, talent nodes and trees, the Bestiary, class/party/summon cards) it is a `GlossaryText`: text with bracketed terms becomes a wrapping row of word labels in the same classes, terms highlighted and underlined. A **tap** on a term opens a term card (headword, Buff/Debuff/Stat/Chance/Keyword, the definition, and a linked status's effect and duration) with the InspectCard's anchoring, stage/safe-area clamp, ease-in and `ui.select`; it stays after release and the next press anywhere closes it (re-tapping the same word just closes it); the tap never also acts on the row or card around it. Holding the element still opens its InspectCard (where terms are drawn but inert). Each term's hit area is padded 24 px vertically and 16 px horizontally and pulled back by the same negative margin, so layout is unchanged (POL-C2). Plain labels that receive bracketed content still tint the terms instead of showing brackets. Long GlossaryText paragraphs follow the 60-character measure rule (`Typography.Paragraphs`).

```
┌─────────────────────────────────────┐ y=30
│ [II]     Stage name · Wave n/m   N G│  pause · title/wave · coins this battle
├─────────────────────────────────────┤ y=150
│  ◆◆                                 │  prominence diamond: next actor,
│ ◆NOW◆◀━◆━·━◇━·━·━◆━·━·━◇━·━◆ [GUARD]│  "NOW"/seconds · one 10 s track,
│  ◆◆   ◀ diamonds flow left (acts)   │  ◆ every 1 s (big every 5 s);
│                                     │  turns + casts on the same bar
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
│ HP/stats (??? until known) ·        │
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
- **Actor panel**: ~~enemy HP and stats show only if the class has been Scanned (this battle or ever)~~ **Amended 2026-11-06 (owner, GG-T290):** what it shows of an enemy follows the Bestiary knowledge tier (`BestiaryKnowledge.Tier`, [§25.11](#2511-bestiary)) — Speed after 1 defeat, HP and stats after 3, weaknesses after 6, abilities and brain after 10; anything not yet known reads "???". It fills from a board tap or, since GG-T288, a tap on the unit's timeline icon ([§2.6](#26-one-track-diamond-icons-the-prominence-diamond)). While targeting it shows the ability card ([§4.5](#45-slot-visual-states)).
- **Guided first battle (amended 2026-10-11, owner: "a full tutorial with explanations and examples, the first battle needs 5 waves of increasing difficulty"):** Tithe Moors I has **five waves** — slime; two slimes; bat + level-2 slime; level-2 wolf + goblin thug; level-2 wolf + goblin + slime — and ten lessons, each an explanation plus a worked example: Moving, Pincers, Mana, Chains, Spells (with the confirm step and charge bar), The timeline, Roles & Taunt, Pickups, Interrupting, Ready. **Amended 2026-11-05 (GG-T252, POL-D7):** four more — Hold to read (done the moment a card opens), The hold timer (and cancelling a drag), Buffs & debuffs, Armor plates — fifteen in all with Shoving (GG-T190). A lesson completes when the player does it (or after a few actions, or Next); lessons that need particular foes wait for their wave ("Next lesson arrives in wave N"); moving lessons light up a demo move; Spells and Taunt select the hero and pulse the slot. Skip ends it for good (`SaveData.TutorialDone`).
- **First-battles hint** — **Amended 2026-10-09 (new):** until the third campaign stage is cleared (`HighestClearedStage < 2`), after 5 s of idle input (not on Auto, not targeting) the board shows the move `HeroBot` would make — the hero glows, its path is drawn to a glowing tile, banner "Hint: drag X to the glowing tile" — or "Tip: trap an enemy between two heroes" when no pincer is available. Any input clears it.
- Tile highlights and pincer lines draw **over** the units so previews stay readable.
- Long banner text steps its font down (48 → 38 → 32) so it never runs off the stage.

---

## 10. Equipment (summary — see §24)

A hero has five equipment slots (`EquipmentSlot`: Weapon, Armor, Relic1–3) stored on `HeroSave`. Equipped items' `Stats` add into `Unit.GearStats`; the weapon's `BaseDamage` is added to physical offense (`Unit.WeaponPower`); resistances fold into damage ([§3.3](#33-elemental-resistance)).

---

## 11. Screen Architecture

### 11.1 Code-only boot

**Amended 2026-10-09:** `Assets/Gaiden/Scenes/Main.unity` is an empty scene (the only one in the build, ensured by `GaidenBuild.EnsureScene`). `GameApp.Boot` (`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`) creates a `DontDestroyOnLoad` `GameApp` that builds, in code: a UI-only camera (black clear), a `UIDocument` with runtime `PanelSettings` (ScaleWithScreenSize, 1170×2532, theme `UI/GaidenTheme` + style sheet `UI/Theme.uss`), the letterboxed stage, the screens host, an overlay layer and a fade layer; then loads content, the save store and audio, and shows the Title (or `-start <screen>`). There are no prefabs (except the code-spawned effect prefabs in `Resources/Vfx`, amended 2026-11-05, GG-T269, [§6.2](#62-vfx-pipeline)), no `[SerializeField]`s and no scene builders; every screen is a `GameScreen` subclass whose `Build(root)` composes UI Toolkit elements via `App/UI/Ui.cs` helpers and USS classes.

### 11.2 Screen list

`TitleScreen`, `SaveSlotScreen` (new/load), `HubScreen` (Camp), `StageSelectScreen` ("Adventure"), `StoryCrawlScreen`, `BattleScreen`, `LevelUpScreen`, `PostBattleScreen`, `ItemsScreen`, `StatusScreen`, `EquipScreen`, `AbilitiesScreen`, `PartyScreen`, `ShopScreen` ("Merchant"), `BlacksmithScreen`, `AlchemistScreen`, `SummonScreen`, `BountyScreen`, `QuestScreen` (GG-T237), `BestiaryScreen`, `SettingsScreen`, `CreditsScreen`, plus the `Confirm` modal. `Automation/StartScreens` maps ids → screens for `-start` and the `goto` endpoint.

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
New Game: SaveSlot → CharacterSelect → StoryCrawl (Prologue) → Camp.
Final victory (first time, §27.0): PostBattle → Continue → StoryCrawl (Ending) → Credits (rolling) → Title.
```

### 11.3 Screen navigation

`ScreenRouter` is a stack: `Push`, `Replace`, `ResetTo`, `Back`. Every transition fades the stage to black for **0.2 s** and rebuilds the top screen. **Amended 2026-10-09** (old fade 125 ms). Escape / hardware back calls `Router.Back`; a screen can swallow it (`OnBack`: the battle cancels targeting, then opens the pause menu). **Fixed 2026-10-19 (GG-T126): Pause pauses.** Opening the battle's pause menu sets `Tween.Paused`, so `Tween.Dt` reads 0 and every playback beat, cut-in, outline, float, coin burst, dissolve, banner scroll and ambient pulse (`Tween.Clock`) holds its exact frame; the event player and `Safe()` start no beat, the watchdog, drag timer, hints and auto battle stop, and ringing sound effects pause mid-clip (music plays on). Resume (or Back) carries on from the same frame. The pause veil is kept the topmost Overlay child, above `board.Lines`, cut-ins, the billboard and coin/orb flights. Verified by `tools/harness.py pause` (pauses mid-chain, two identical frames 2 s apart, zero beats while paused, resume finishes the chain). Screens rebuild on every show and on `Refresh()`, so they always reflect current state. UI callbacks are ordinary C# lambdas (the old "no lambdas in persistent listeners" rule is moot).

### 11.4 Build settings

`GaidenBuild.EnsureScene` writes `EditorBuildSettings.scenes = [Main.unity]`, portrait orientation, no splash, `bundleVersion` from `VERSION`. New screens need no build-settings change.

### 11.5 The visual language

**Polish rules (GG-T249):** type scale, color tokens/contrast, spacing, touch targets and motion tokens are defined and measured in [`POLISH.md`](POLISH.md) §A–§C, §E ([GG-LAW-14](#GG-LAW-14)).

**Design tokens (GG-T250):** the visual language lives in tokens, not literals. `Theme.uss` declares them as `:root` custom properties (`--color-*` semantic palette incl. teams, elements, rarity and gauges; `--type-*` on the 24…120 scale; `--space-*` on the 8-px scale) plus `.t-caption … .t-display`, and its base rules use `var()`. `App/UI/Tokens.cs` mirrors them for code: `Palette` (semantic colors, `Palette.Element(DamageType)` — the one element table, every element its own hue; Physical no longer shares Arcane's, Haste and "Pushback!" no longer borrow Ice blue), `TypeScale`, `Space`, `Timing` (instant 0.08 · quick 0.15 · standard 0.25 · emphasis 0.4 · dramatic 0.7 · cinematic 1.2+) and `Ease` (Out, InOut, Back, Snap). Battle and menu presentation take their colors and sub-second durations from them; no duration is random. **Weight is a font, not a style:** USS has no `-unity-font-style: bold`; the formerly bold classes (`title h1 h2 window-title header-title btn tab--active role-badge pip`, plus `t-heavy`) get the real Outfit Bold cut (`Fonts.UseHeavy`) from `Ui.AddClasses`/`Ui.Button` and a sweep after every screen build (`Ui.ApplyHeavy`), skipping text already set to Display.

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
- **Amended 2026-11-05 (GG-T254, POLISH.md §F):**
  - **Cue taxonomy:** every cue is `ui.* / battle.* / element.* / status.* / story.*` (e.g. `battle.hit`, `element.arcane` for the old `magic`, `element.water` for `splash`, `status.debuff`, `story.quest_complete`). A key's file is the key with dots as underscores (`battle.dash` → `battle_dash.ogg`); `battle.move`/`battle.hit` pick one of 6/7 variants, `ui.back` shares `ui_click`. `Audio.Canonical` keeps one alias table from the old flat names, so a stray old name still plays, and the cue log records the taxonomy key.
  - **Loudness:** `tools/loudness.py` measures BS.1770 integrated loudness (clips under 400 ms ungated) and 4×-oversampled true peak. Every SFX is normalized as `tools/make_sfx.py` synthesizes it to **−16 LUFS ± 2, ≤ −1 dBTP**, through a look-ahead limiter for percussive cues and one corrective re-encode for what Vorbis trims. Music is re-encoded once (`--music-apply`, two-pass loudnorm, 320 kbps) to **−18 LUFS**. Before: SFX −24.1 … −11.3 (16/57 on target), music −32.0 … −11.4 (0/5). After: SFX −16.5 … −14.7 (57/57), music −18.5 … −18.0 (5/5).
  - **Ducking:** `Audio.DuckFor(seconds)` dips the music **−8 dB** (in over `Timing.Standard`, out over `Timing.Dramatic`, eased in dB) under every cut-in card (`TwistCard`), each hero's level-up fanfare, and dialogue while a line types. It is time-boxed, not push/pop, so a stopped coroutine can never leave the music stuck quiet.

#### 12.0.1 Announcements — cadenced callouts

Announcements (`AnnounceEvent`) are played by the event player as an awaited 0.25 s banner beat, one at a time, never an instant swap. Wired for refusals, charges ("X is charging Y!"), boss phases, "Clutch!", "Interrupted!", "Steam!", "Steal!", "Banked N mana", "Escaped!", enemy pincers.

#### 12.0.2 Pacing

**Amended 2026-10-09:** every beat duration lives in `BattleScreen.Playback.cs` (no `PacingConfig`): step 0.16 s, displacement 0.14 s, pincer 0.32 s, orb flight 0.22 s, timeline glide 0.18–0.45 s, turn pop 0.2 s, projectile 0.22 s, victory/defeat banner 1.0 s + 0.6 s before results. All durations divide by `Tween.Speed` = Settings → Battle speed (×1 / ×1.5 / ×2 / ×3). Nothing the player must read resolves in a single frame.

### 12.1 Art

**Amended 2026-10-09:** textures load with `Resources.Load` through `App/Services/Art.cs` (no Addressables, no `SpriteLibrary`):

| Key | Path | Source |
|---|---|---|
| Portrait | `Resources/Portraits/<classId>.png` | full body on a **1024×1024 transparent canvas**, never trimmed or shrunk ([§12.1.2](#1212-portraits-are-1024-canvases-added-2026-11-07-owner-rule-gg-t352)); the unit's only image |
| Backdrop | `Resources/Backgrounds/<set>_<NN>.jpg` — owner rules 2026-11-07 (GG-T344, GG-T353): a set is named after its **story region** and numbered from **01** with zero padding, at the art's **original resolution** (JPEG q95; PNG only for art with transparency). Owner: *"make sure the source files for the Backgrounds all get proper naming to identify them to their world lore counterparts"*. The sets: `tithemoors_01..05`, `shardfall_01..05`, `arkgrave_01..05`, `anchorwastes_01..04`, `halcyon_01..14` (the GridGame2026 Moors, RedThorns, ElectricWasteland, UnderTheBridge and CyberNecropolis originals at 1536×3072, every one of them; CyberNecropolis's two byte-identical duplicates are skipped) and `still_01..06` (native 768×1536). Each main campaign theme names its set with `ThemeDef.Backgrounds` (`"tithemoors"`), so theme ids (`greenvalley` …) never change; a theme without one uses its id as the set. `Art.Background` wraps an index over the set's own count (`Backdrops.Resolve`); a postgame theme borrows its base theme's set (`cave2` → `anchorwastes`), then the default theme's, then `tithemoors_01`. Import: `tools/import_art.py` `import_backgrounds` (no resize); Unity caps Backgrounds at 4096 (`GaidenImport`), CompressedHQ, no mips, so a 1536×3072 backdrop is about 4.5 MB of texture memory (the old 720×1440 was about 1 MB) and only the one or two on screen are loaded | biome art |
| Ability icon | `Resources/Icons/<Key>.png` (64²) | `AbilityDef.Icon` / `ItemDef.Icon` |

Every class has a portrait and a `Thumbnail` (`Every_class_can_be_instantiated_and_has_art_reference`). **Amended 2026-11-03 (GG-T221):** there are no thumbnail images: every board token, timeline turn icon (heroes and enemies alike; the old enemy pixel sprites were deleted in GG-T224), menu/party/bestiary card and dialogue face is the portrait framed live by the class's Thumbnail ([§12.1.1](#1211-thumbnails-are-a-window-onto-the-portrait-added-2026-11-03-owner-direction-gg-t220)).

#### 12.1.1 Thumbnails are a window onto the portrait (added 2026-11-03, owner direction, GG-T220)

Owner: "these new coordinates and zoom are kept in memory in the character's Thumbnail properties; it always uses the real existing portrait and simply zooms in and offsets; that way the portrait is always in memory and when the enemy dies you can just spawn a portrait of the actual full portrait to shake and die; no duplicating assets."

- Every class carries `"Thumbnail": { "Zoom", "X", "Y" }` (`ClassDef.Thumbnail`). (`X`, `Y`) is the portrait point — fractions of the portrait's width and height from its top-left — that sits at the thumbnail's centre; `Zoom` is portrait width ÷ thumbnail side (1 = the whole portrait width fills the square). Aspect is always kept, and the numbers are resolution-independent, so one framing serves the board token, the timeline icon and every card.
- A square of side S shows the portrait at width W = Zoom × S and height H = W × (portrait h ÷ w), placed at left = S/2 − X·W, top = S/2 − Y·H, clipped to the square.
- `ContentLoader.Validate` requires one on every class, with Zoom in [0.25, 20] and X/Y in [−0.5, 1.5].
- The owner frames each unit in **Thumbnail Studio** (`tools/ThumbnailStudio`, GG-T222), a separate desktop app that edits these numbers in `classes.json`; nothing is exported. It is set-and-forget (GG-T223). It opens from its own "Thumbnail Studio" desktop shortcut (`tools/thumbnail-studio.ps1 -Shortcut`) with no console. Every framing change autosaves that unit's block. The studio commits only those blocks, built from `HEAD` in a temporary index so nobody else's staged or unstaged work rides along, as `Thumbnail Studio: reframe <names> (GG-T223)`, and pushes them, never forced. On close, if it committed anything, it starts `tools/run.ps1` in the background so the installed game shows the new thumbnails. It skips that when the game or the Unity Editor is running, because the next Deploy & Run rebuilds anyway.
- The first values (GG-T220) were recovered from the pre-cut `Resources/Thumbs` images by locating each one inside its portrait (multiscale template match, every result checked by re-rendering it), so every unit first looks exactly as before. `import_art.py` no longer cuts thumbs; a new class needs a Thumbnail before it validates.
- **Drawn live (GG-T221):** `Ui.PortraitThumb(host, classId)` puts the portrait in a clipped (`overflow: hidden`) square host as a child sized and placed in percent of it (width = Zoom×100%, height = Zoom×aspect×100%, left = 50% − X·width, top = 50% − Y·height), so it is right at any size with no layout pass. `Ui.Thumb` (cards), the board token (`UnitView.Art`; corpses frame their `CorpseOf` class), the timeline turn icon (`tl-art`, inside its circle mask) and dialogue faces all use it. A class with no Thumbnail shows its whole portrait contained; a dialogue speaker that isn't a class shows no face. Tints (bestiary silhouette, corpse grey, frost, hit flash) go on the portrait child (`thumb-art`). `Resources/Thumbs/` and `Art.Thumb` are gone; the death dissolve shows the same `Art.Portrait` texture whole.
- Separate from `Crop` (the cut-in bust's eye framing, `BattleScreen.CutIns.cs`), which is unchanged.
- **Amended 2026-11-07 (GG-T352):** the numbers now frame the whole 1024×1024 canvas ([§12.1.2](#1212-portraits-are-1024-canvases-added-2026-11-07-owner-rule-gg-t352)). Every class's old framing, made on the trimmed image, was converted so it shows exactly what it showed before.

#### 12.1.2 Portraits are 1024 canvases (added 2026-11-07, owner rule, GG-T352)

Owner: "all characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the assets are."

- **The file.** Every `Portraits/<classId>.png` is a 1024×1024 RGBA canvas with a transparent background, exactly as the art comes. `tools/import_art.py` `save_portrait_assets` writes it (and `tools/portrait_cut.py --import` and `tools/remove_bg.py --import` go through it). A 1024² image is kept pixel for pixel. Any other size is cut to its figure (the alpha bounds, so nothing of the figure is lost), scaled down only if that is still larger than 1024, and centred. Nothing is alpha-trimmed or capped at 640 any more. The existing `.meta` (GUID) is kept.
- **Figure.** Each class carries `"Figure": { "X", "Y", "W", "H" }` (`ClassDef.Figure`): the figure's box on its portrait (the alpha bounds), as fractions of the portrait's width and height from its top-left. The import writes it into `classes.json` with a targeted edit, like Thumbnail Studio. `ContentLoader.Validate` requires one on every class, with some area and inside the portrait.
- **Whole-figure views draw the figure, not the canvas.** `Art.PortraitFigure(classId)` is the portrait cut to its Figure box as a sprite (cached per texture). Dialogue speakers and NPC icons, the cut-in bust, the pincer cut-in shots, the boss intro, level-up, the hero sheet, the Mercenary renewal and the death dissolve draw it instead of the raw texture, so each figure reads at the size it had before the canvases grew margins. `Crop` still positions within that box. `Art.FigureSize` is the figure's layout size: its box (in 1024-canvas px) fitted inside a 640 square, never enlarged, the size portraits were imported at before. Dialogue draws 640 at `FigureHeight`, so a creature smaller than a person still stands smaller. The mannequin fallback (GG-T351) uses the mannequin's own box.
- **Thumbnails** (`Ui.PortraitThumb`, §12.1.1) frame the whole canvas, and are unchanged in code.
- **Re-import (2026-11-07).** Every portrait was re-imported at 1024. The GridGame2026 originals (`Assets/Sprites/Actor/Portraits`, by the class's `Portrait` else its `Name`) came in as they are; the 19 RGB originals with solid backgrounds went through `portrait_cut.py`'s smoke cut; the 6 off-size ones were fitted. The owner's pasted art (GG-T328 to GG-T340, in the tickets' attachments) was cut again with the smoke method at full canvas. The 7 portraits with no original anywhere (gravestone, bone pile, corpse, naga egg, hex sniper / warden / stalker) are their old image, unscaled, centred on a 1024 canvas. Each class's Thumbnail was converted from trimmed-image to canvas fractions: with the old image = the canvas cropped to the box (bx0, by0, bw, bh) and scaled uniformly, `X' = (bx0 + X·bw)/1024`, `Y' = (by0 + Y·bh)/1024`, `Zoom' = Zoom·1024/bw`. The box was found per class as whichever of the new alpha bounds or the whole canvas reproduces the old image (refined edge by edge where neither did). Every class was then checked by rendering old and new thumbnails with the game's maths.
- *(Tests: `PortraitCanvasTests`. Every portrait file is 1024×1024 8-bit RGBA with transparent top corners; every class has a Thumbnail in range whose centre lands on its figure; every Figure equals its PNG's alpha bounds. `tools/check_assets.py` fails a portrait that isn't a 1024² RGBA canvas (POL-H1).)*

### 12.2 Procedural pieces and placeholders

`Art` generates the window gradients, cursor, glow and orb textures at runtime. **Board tiles (amended 2026-10-10, owner):** GridGame2026's own tile art (`Resources/Tiles/tile.png`, its `tile3`, imported by `tools/import_art.py`), recoloured per campaign biome by `tools/make_tiles.py` (moss, sandstone, murky teal, rust-rose; the caverns keep the original slate) and tinted light/dark per square to keep the checkerboard (`Art.BoardTile(theme)`). A missing texture falls back to a purple placeholder and logs once — a gap is visible, never fatal.

### 12.3 VFX

✅ **GG-T253:** one element/status table (`Resources/Content/vfxstyle.json`) drives every effect, with intensity tiers and timing/easing tokens; code-drawn UI Toolkit, with premade effect prefabs layered on top where mapped (GG-T269, §6.2; the one exception to "no prefabs"). Details and the contact-sheet tool in [§6.2](#62-vfx-pipeline); rules in [`POLISH.md`](POLISH.md) §E (POL-E1–E8).

---

## 13. Combat Resolution

### 13.1 Damage formulas

All in `Core/Rules/Formulas.cs` (ported from the old `Formulas` with engine types removed).

#### 13.1.0 Evasion (defender, GG-T297)

*Added 2026-11-06 (owner: "characters can be built to benefit from dodging hits").* Every unit, hero or enemy, has **Evasion** in percentage points:

```
evasion = clamp( min(25, 0.2·AGI + 0.1·LCK) + Σ buff EvasionBonus + Σ gear EvasionBonus , 0, 40 )
```

A level-1 hero sits near 3.6 (AGI 12, LCK 12), a ninja near 5, a level-30 skirmisher near 9–12; buffs and gear lift it, never past 40. It takes the **top `evasion` points off the attacker's hit band** on the same single Rng draw (`roll ≤ crit% → Critical; ≤ hit% → Normal; else Miss`, then a would-be hit with `roll > hit% − evasion` is **dodged**), so a seed replays identically wherever Evasion doesn't bite. A dodge deals nothing, emits `DodgedEvent` (a "Dodge!" float, a sidestep and a whoosh) instead of the 0-damage Miss, and fires `OnDodge` for the defender.

- **Dodgeable:** enemy melee swings and shots, counterstrikes (Counter Attack, Evasive Riposte) and charged enemy spells (`Formulas.Magic`, §13.1.5).
- **Never dodged:** pincer blows, supporter blows and the follow-ups riding them (the pincer is the game's core payoff), and hero spells/skills, which already never miss (§13.1.2).
- **Sources:** `BuffDef.EvasionBonus` (validated −40..40; `blurred` +15 for 2 turns) and `ItemDef.EvasionBonus` (validated 0..20, lost when an armor piece breaks).
- **Dodge synergies** (Reactive rows in `abilities.json`, on `OnDodge`): **Evasive Riposte** (a [Counterstrike] at 0.5×; on the Duelist's kit), **Catch Breath** (heal 6% MaxHP, once per action), **Blur** (gain [Blurred], once per action). **Amended 2026-11-06 (GG-T302):** carried by the duelist, swordmaster, dervish, martial artist and lurkers (Evasive Riposte), fencer, chroma ninja, speedster, courier, hexstalker and flying monkey (Blur), and thief and white ninja (Catch Breath); see [§4.1.2](#412-signature-passives-gg-t302-added-2026-11-06).
- **Shown:** the InspectCard and battle unit panel stat line (`EVA n%`) at the Bestiary Stats tier, and the Bestiary's Stats fact (from base stats); buff and item text name their bonus; the glossary has [Evasion] and [Blurred].

#### 13.1.1 Physical (pincer, supporter, enemy melee)

```
off   = attacker STR + weapon BaseDamage           def = defender VIT (+ armor rating, unused = 0)
raw   = max(1, max(off − max(0, def − 0.25·off)·0.35, 0.20·off))
raw  ×= defender physical resistance
var   = raw × U(0.9, 1.1) × LCK tilt (0.99..1.01)
hit%  = clamp(80 + 0.5·SPD + 0.2·LCK + 3·(attacker lv − defender lv), 5, 95) × Blind accuracy (×0.5)
crit% = clamp(8 + 0.4·LCK, 0, 50)        roll ≤ crit% → Critical; ≤ hit% → Normal; else Miss
dodge = a non-pincer, non-supporter would-be hit with roll > hit% − defender Evasion (§13.1.0)
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

The first hero to enter the tile collects it (walking, being displaced, or teleporting). Enemies don't collect; ~~one may stand on a pickup.~~ **Amended 2026-11-03 (owner direction, GG-T228): a pickup moves like an actor** — any non-hero entering its tile (step, 2×2 lane step, snake, leap, blink, teleport, knockback/pull/displacement) shoves it like a movable tile onto the tile it just left, else the nearest open item-free tile (`PickupMovedEvent`, a slide with a cue; fades only on a full board), so no unit ever shares a tile with a pickup; nothing spawns on one (GG-T227, §1.1.3). **One item per tile, ever (GG-T129, §1.1.3):** a pickup only drops on a tile that holds no other pickup and no movable (`FreeForItem`), the Power Amplifier never spawns or is shoved onto a pickup, and the view never draws two item tokens on one tile or keeps the token of a collected or faded pickup. A pickup fades after **4** player actions (a ring on the token counts down). It pops out of the fallen enemy with a bounce; collecting flies it to the hero. *(Tests: `An_enemy_stepping_onto_a_pickup_shoves_it_into_the_tile_it_just_left`, `A_two_by_two_shoves_a_pickup_to_the_tile_vacated_on_its_lane`, `A_snake_head_stepping_onto_a_pickup_shoves_it_behind_its_tail`, `A_foe_knocked_onto_a_pickup_shoves_it_back_and_a_hero_still_collects`, `A_pickup_with_nowhere_to_go_fades_rather_than_stay_under_a_unit`, strict/release unit-on-pickup; `Stepping_on_a_pickup_collects_it_and_heals`, `A_round_trip_drag_that_only_grabs_a_pickup_still_counts_as_a_move`, `Pickups_fade_after_their_lifetime`, `Fallen_enemies_sometimes_leave_pickups`; items apart: `The_amplifier_never_spawns_on_a_pickup`, `Strict_soak_with_drops_and_amplifier_shoves_never_stacks_items` and the rest of §1.1.3's GG-T129 tests.)*

### 13.5 Armor plates (added 2026-10-11, owner: "some enemies are armored and need to take damage from that side to break it off")

`ClassDef.ArmorPlate` gives a family a plate on the side it **faces** (`Unit.Facing`, turned toward the hero it wants at spawn and at the end of each of its turns — `FacingChangedEvent`). Plate HP = `ArmorPlate × (1 + 0.15 × (level − 1)) × EnemyHpScale` (the scale added 2026-11-05, GG-T262). A **physical** blow (pincer strike, supporter, Cleave, Shield Bash) from the plated side (`SideOf`, dominant axis) dents the plate instead of the body (`PlateHitEvent`, "CLANG! −n"); at 0 the plate breaks off for good (`PlateBrokenEvent`, shards, "ARMOR BROKEN!"). Blows from the flanks or behind land normally; **spells go around the plate**. Families: ceramic knights 30, templars 30, mecha armour 40, soldiers 18, the captain 24. Tactics: pincer it across its line of sight, break the plate head-on, or let the mage burn it. The plate is a riveted steel bar on the tile's rim; the enemy card says where it faces and how much is left. The bot avoids lining an attacker up on the plated side. *(Tests: `A_plate_takes_the_blow_from_its_facing_side_and_the_flank_hits_flesh`, `A_pincer_across_its_line_of_sight_skips_the_plate_and_a_broken_plate_stops_blocking`, `Spells_go_around_the_plate`.)*

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
6. **Pincer plan** (GG-T306, [§14.1.3](#1413-pincer-planning-gg-t306)): a pincer-capable 1×1 foe first asks `EnemyPlanner.PlanPincer`. A walk that **completes** a pincer is taken at once, before step 4's charge; a walk that **sets up** an end or **joins** an ally pincer as a supporter is taken here, after the charge and trap checks. Either way it then goes straight to step 8's attack.
7. **Move** up to `MoveRange` steps (all content: 1 or 2), re-planning each step with `PlanStep`; a multi-tile step shoves heroes ([§1.5](#15-multi-tile-actors-22-bosses)); a snake head drags its chain.
8. **Attack**: a Sentient 1×1 enemy (`Unit.CanPincer`, [§1.3](#13-sentient-restriction); was Humanoid before GG-T289) that is now an end of an enemy pincer resolves those pincers ("X pincers!"); otherwise it melees the adjacent hero its **targeting personality** picks (below). No adjacent hero → no attack.
9. End of turn: its non-halting turn buffs tick; battle-end check.

**Targeting personalities (amended 2026-10-10, owner direction: "smarter enemies attack the weakest, dumb enemies the closest").** `ClassDef.Targeting` drives movement (`PlanStep`), melee choice, ranged choice and charge targets (`EnemyPlanner.PickFrom`); ties fall back to distance, then HP, then id:

| Personality | Picks | Families |
|---|---|---|
| Closest | nearest hero | slimes, undead, cyber-zombies, skelepedes, tree golems, mannequin, frogs, toad, sandmaw, cyclopes, troll, yeti, shamblers, ceramic knights, templars, naga |
| Weakest | lowest current HP, even across the board | wolves, werewolf, vulture, hags, swamp mistress, reaper, phantom, lurkers, tech gremlins, purple prototypes |
| Threat | hero who has dealt the most damage (Closest until someone has) | captain, soldiers, demon lord, oni, mecha armor, ice mauler |
| Squishy | lowest VIT (then MaxHP) | gangers, steppin' razors, scorpion, goblin thug |
| Erratic | a different hero each turn (hash of clock + id; no RNG draw) | bats, flying monkey, ghost |

`Auto` (unset) derives Weakest for INT ≥ 14, Threat for Sentient enemies (Humanoids before GG-T289), else Closest. The enemy card shows it ("Hunts the weakest hero").

**Speed tiers (amended 2026-10-10):** enemy Speed is 18 (swift: bats, wolves, flyers, werewolf, steppin' razors, phantom), 15 (quick), 11 (steady) or 8 (slow: slimes, undead, zombies, golems, mannequin, ghost). ~~Open-track rate scales with Speed and the Zone is a shared crawl.~~ **Amended 2026-11-06 (GG-T286):** the rate scales with Speed the whole way, so a fast enemy can overtake a slow one anywhere on the bar.

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
                not wounded & Sentient 1×1 & forms pincer  +50
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
| Forms own pincer | +50 | Sentient 1×1 (`CanPincer`, GG-T289), not wounded |
| Supports an ally's pincer | +25 | any enemy, not wounded |
| Threat (target choice) | −(DamageDealt / max) × INT × 0.8 | smart enemies hunt the top damage-dealer |
| Wounded retreat | below `RetreatHpThreshold` 0.30 | flees; flank-avoid still applies; never against a lone hero (GG-T123, [§22.10](#2210-the-crucible)) |

Constants live in `EnemyPlanner` and `BattleConfig.RetreatHpThreshold`.

#### 14.1.3 Pincer planning (GG-T306)

**Added 2026-11-06 (owner):** "The heroes and enemies with 'Pincer' keyword not only line themselves and complete pincers, they actively try to make pincers by swapping with monsters or heroes to set up a beneficial situation with enemies, themselves, and their supporters — so every monster needs support abilities."

`PincerPlanner` (Core/Rules) is one shared, pure planner for any unit that can pincer (`Unit.CanPincer`: heroes, Sentient foes, tamed or domesticated monsters).
- **Search.** It tries every walk of up to `min(steps, MaxSearchSteps = 3)` cardinal steps, never revisiting a tile. Each step either enters an empty walkable tile or **swaps** with the one-tile unit standing there, friend or foe. The occupant takes the tile just left, exactly like a hero drag ([§1.1](#11-sliding-and-displacement)). Units that can't be swapped: multi-tile actors, snake members, remains, corpses and neutrals. A hero-team walk never steps on a trap. That is at most 52 walks per unit, each scored on the board it leaves. The planner moves `Pos` to look and restores it, never draws from the Rng, and tries walks in `Coord.Cardinals` order; the first of equal scores wins, so it is deterministic.
- **Score** (`Evaluate`, the board after the walk):

| Outcome | Kind | Worth |
|---|---|---|
| The mover is an end of a pincer with live opponents (a commanded hero: any hero pincer, since its drop resolves them all) | **Complete** | `OpponentValue` 10 per live, unarmored opponent, +3 for one below 40% HP, plus each supporter's `SupportWorth` (so an ally swapped into the chain counts) |
| No pincer, but it stands at one end of an unbroken opponent line whose far tile is open (empty, or a non-pincer ally that can be swapped out) and a pincer-capable ally can reach that tile on its next action (a foe or tamed beast: its `MoveRange`; a commanded hero: the drag that fits the window) | **Setup** | `SetupBase` 6 + 1 per opponent in the line |
| It joins the chain of an ally pincer already on the board | **Support** | 0.6 × its `SupportWorth` (added to the other kinds too) |
| Each ally newly trapped in an opponent pincer | — | −12; the mover itself trapped −40 (a Berserker or Bomber foe ignores the −40) |
| Each step / each swap | — | −0.25 / −0.25 |

- **`SupportWorth`** prices a supporter by its class's `SupportDef` ([§1.2.3](#123-supporters)): Strike 2 + 4×magnitude, Heal 2 + 10×, Buff 3, Haste 2 + seconds, Mana 2 + charge/25, Cleanse 2, Crit 2 + 6×, Cast 2 + 4×, Status 2 + 3×chance, Pushback 2 + 2×seconds. This is why a healer behind an attacker can beat a second striker. Every enemy class must carry its own `Support`: `ContentLoader.Validate` rejects one that doesn't ("an enemy needs its own Support"), and all 115 do since GG-T307.
- **Foes** (`EnemyPlanner.PlanPincer`): a Sentient 1×1 foe that isn't immobile and isn't wounded enough to retreat plans with its `MoveRange`. A Sentinel, an Ambusher still lying in wait, or a Sluggish foe on its rest turn plans with 0 steps (it only pincers in place). An evasive brain (Kiter, Commander, Trapper, Medic) takes only a **Complete** walk. A walk is taken only when its score is positive. `BattleRunner.FollowPincerPlan` walks it with `SwapStepEnemy` (an empty tile is an ordinary step; an occupied one swaps, `DisplacedEvent` by the foe, and a hero shoved onto a trap springs it). With a swap it announces "X maneuvers!". Then it attacks, closing the pincer if it now stands at an end. Mindless foes (beasts, slimes, yetis…) keep the plain `PlanStep`: they never swap.
- **Tamed monsters** (`RunAllyTurn`, [§25.11.3](#25113-taming-and-domestication-added-2026-11-06-owner-gg-t303)) plan with the same planner on the hero side and walk the plan with the hero drag's `WalkPath`. They complete a hero pincer, stand at an end a hero can close, or slot into a chain. They still act on their own and shove heroes aside to do it, which can wreck a setup the player had in mind. With nothing to engineer, a tamed monster falls back to charging the nearest foe. A ranged one that already has a shot takes only a Complete walk.
- **The smart bot** (`HeroBot`, timeline-aware) adds the planner's walk to its drag candidates whenever it is not one of the straight L-drags it already previews. The walk is capped at the drag that fits the gap (`MaxDragSteps` with think time, GG-T311), and is previewed and scored like any other drag. The bot also weighs each supporter of a previewed pincer by 0.6 × `SupportWorth` instead of a flat 2.
- *(Tests: `PincerPlannerTests` — `A_sentient_enemy_swaps_with_an_ally_to_complete_a_pincer`, `A_sentient_enemy_swaps_to_set_up_an_end_an_ally_can_close`, `A_mindless_enemy_never_swaps_to_pincer`, `A_tamed_monster_moves_to_close_a_hero_pincer`, `The_hero_planner_finds_the_walk_that_closes_a_pincer`, `Planning_and_the_turns_it_drives_replay_identically`, `Every_real_enemy_class_brings_its_own_support`.)*

- **Balance (GG-T306).** Per-stage 8 soak (`AbilityUsageSoak`, 15 campaign stages, same seeds), smart / casual bot wins, before → after:

| Difficulty | Before (GG-T311) | Planner only (pincer push 1 s) | Shipped (pincer push 1.5 s) |
|---|---|---|---|
| 10 | 79% / 33% | 72% / 22% | **76% / 31%** |
| 5 | 99% / 85% | — | **99% / 78%** |
| 1 | 100% / 98% | — | **100% / 98%** |

  Engineered enemy pincers alone cost the casual bot 11 points at difficulty 10, which put it below the 30–45% band. `PincerPushbackSeconds` 1 → **1.5 s** brings it back: every hero pincer buys the party half a second more against foes that now set up their own pincers. Difficulty 10 by theme, smart bot: greenvalley 83 → 71, desert 92 → 92, swamp 67 → 62, cave 88 → 79, cityruins 67 → 75. The smart bot wins at least one battle on every stage. Casual, by theme: greenvalley 25, desert 42, swamp 25, cave 58 → 46, cityruins 17.
  The soak also exposed two stalls, fixed here. A wounded foe fleeing into a **corner** can't be pincered on either axis, so a buff-only pair could never finish it; a fleeing step now scores −`FleeCornerPenalty` (3) for a corner tile. And a smart bot whose bar held only buffs never counted as locked out, so with 1 s gaps it never dragged; it now also hurries (no think time) after `StallHurry` (4) windows without drawing blood. `BattleConfig.EnemyPincerSetups` and `EnemyPincerSwapsHeroes` (both on) are soak knobs (`GAIDEN_TUNE`). Turning both off measured casual 31% at difficulty 10 with a 1 s push.

#### 14.1.1 Taunt

Added 2026-10-11 (owner: "a tank should be able to taunt enemies"). While any hero carries a buff with `Taunts` (Taunting, 2 turns, −25 % damage taken), `EnemyPlanner.PickFrom` only considers taunting heroes among those in reach — so walking, charging and ranged foes all go for the tank, and a taunter out of reach doesn't save an adjacent ally. Synergy: a hit staggers a hero's cast, so a taunting tank keeps the mages' spells charging. *(Tests: `A_taunting_hero_draws_every_enemy_that_can_choose`, `Taunt_only_overrides_among_heroes_in_reach`.)*

### 14.2 Enemy archetypes (design palette)

| Archetype | Lean | Tags | Behavior today |
|---|---|---|---|
| Rusher | high SPD, low VIT | Beast | loads fast; closes and melees |
| Bruiser | STR + VIT | Sentient | slow, tough; best pincer target |
| Flanker | mid STR, AGI | Sentient | seeks own pincers (+50), supports (+25) |
| Ranged | AGI, INT | Sentient | ⬜ Not yet built: keep-distance behavior and ranged attacks |
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
- **Supporter positioning** — `WouldSupportAllyPincer` (+25), reusing `PincerDetector` supporters; since GG-T306 the pincer planner also swaps allies into a chain ([§14.1.3](#1413-pincer-planning-gg-t306)).
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
| Passives | `Abilities` | `lifesteal` (heals a quarter of the damage dealt, the row's `Lifesteal` Amount 0.25; it was half before GG-T282), `tough_hide` (physical blows land at 70%), `berserk` (+35% damage below half HP), `regenerate` (6% MaxHP at the end of its turn), `double_attack`, `counter_attack`; **GG-T302** adds signature rows per family ([§4.1.2](#412-signature-passives-gg-t302-added-2026-11-06)) |
| Support charges | `ChargeAbility` with `Filter: AllyOnly` | Howl (allies in 2 tiles hasten 0.25u — only when another ally in range is already at a hero, GG-T123), Rally (allies gain Protection), Mend (heal the most wounded ally 30 — only when one is below 70%) |
| Area charge | `ChargeAbility` with an area `Shape` | Poison Cloud: 3×3, 6 poison + Poisoned |
| Traps | `Trap` | Poison Cloud (miasma), Shock Mine (10 lightning + Slowed), Caltrops (8), plus the trail traps Slime Trail / Mire (Slowed) and Quicksand (4 + Slowed) |

Family kits: slimes leave a Slime Trail · bats move 2, lifesteal · wolves Howl · werewolf moves 2, berserk, lifesteal · vulture & lurkers move 2 (lurkers' bites poison) · flying monkey moves 2, blinds · frogs/toad/skelepedes/scorpion/naga poison on hit · hags cast Poison Cloud and lay it as a trap · swamp mistress Mends · marsh shamblers leave Mire, tough hide · undead & reaper lifesteal · cyber-zombies self-destruct (14) · ceramic knights, templars (Smite), mecha armor (Thunder) and cyclopes have tough hide · tree golems regenerate · purple prototypes & oni go berserk · tech gremlins lay Shock Mines · gangers blind · steppin' razors move 2 and strike twice · goblin thug lays Caltrops · soldiers & mannequin counter · the captain Rallies · demon lord casts Fireball, lifesteals · phantom moves 2, silences · ghost slows · ice mauler & yeti may freeze · mountain troll regenerates and goes berserk · sandmaw leaves Quicksand, berserk. The enemy card and the bestiary list each kit (`Ui.KitTraits`).

### 14.5 Remains — gravestones and bone piles (added 2026-10-10, owner direction)

Owner: "some undead enemies should spawn a solid grave stone like in Darkest Dungeon where you have to delete the gravestone / pile of bones to be able to finally defeat the enemy, or after a while they will resurrect."

- `ClassDef.Remains` names the marker an enemy leaves: hooded **undead → Gravestone**, **skelepedes → Bone Pile**. On death (after its loot), the remains spawn on the same tile as an enemy unit tagged `Remains` (`RemainsLeftEvent`).
- Remains are **solid** (can't be dragged through or knocked back), never move or attack, are immune to status effects, give no loot or XP and never appear in the bestiary. They have their own slow timeline icon (Speed 5) — the countdown to resurrection.
- **Destroy them** (pincer or spell) and the undead is laid to rest for good. A wave is not cleared while remains stand.
- **Leave them** and when their icon reaches the left edge the undead **rises again** on that tile at half HP and drops nothing a second time (`RoseAgainEvent`); kill it again and it leaves fresh remains.
- **Amended 2026-11-05 (GG-T261):** remains rise on their **third** turn (`BattleConfig.RemainsTurnsToRise` 3; the first two only announce "stirs… N"), with **25 %** of the slain owner's MaxHP (`RemainsHpFraction`), and a snake's body segments leave none (one marker per creature). Found in the battle records: since GG-T188 every action runs the clock to the next enemy turn (§2.0), so a marker left by the last foe standing rose **inside the killing action** (median 0 player actions between remains and rising), and at the owner's own level a gravestone had ~110 HP — the casual bot won 0 of 6 on every Anchor Wastes and Halcyon stage and on Tithe Moors III. See AMENDMENTS 2026-11-05.
- Art: procedurally drawn gravestone and bone pile (ImageMagick).
- *(Tests: `A_slain_undead_leaves_solid_remains_that_block_the_wave_from_clearing`, `Remains_left_alone_raise_their_owner_at_half_hp_without_loot`, `Remains_are_solid_and_real_undead_leave_them`.)*

### 14.6 Brains — every family thinks differently (added 2026-10-11, owner: "come up with ai for every single enemy unit, they are all different in their own unique ways")

`ClassDef.Brain` sets how a family moves and decides, on top of its targeting personality (§14.1) and kit (§14.4). `EnemyPlanner.PlanStep` scores tiles with the brain's taste (`BrainScore`); `BattleRunner.BrainTurn` handles brains that change the shape of a turn. The enemy card and bestiary describe it.

| Brain | Behaviour | Families |
|---|---|---|
| Hunter | closes on its prey (the classic) | purple prototype, yeti |
| Pack | +1.5 per kin beside it — moves as a group | wolves, gangers, skelepedes |
| Ambusher | holds still until a hero is within 2 tiles, for at most 3 of its own turns (`AmbushPatienceTurns`, GG-T262) | lurkers, naga, scorpion |
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

No two families share brain + targeting + kit (`Every_enemy_family_plays_differently`). Enemy physical strikes carry `EnemyStrikeMultiplier` 0.9 (retuned after the brains with `BalanceReport`). **Amended 2026-11-05 (GG-T262):** enemies spawn with `EnemyHpScale` **0.45** of the MaxHP formula (and plate, §13.5) and strike at **1.2** — faster fights, same threat (AMENDMENTS 2026-11-05). *(Tests: `An_ambusher_waits_until_a_hero_comes_close`, `A_sentinel_never_moves_and_a_sluggish_foe_moves_every_other_turn`, `A_hopper_leaps_two_tiles_over_a_unit`, `A_bomber_dives_where_it_touches_the_most_heroes`, `A_skirmisher_strikes_then_darts_away`, `A_burrower_surfaces_beside_its_target`, `A_guardian_stays_by_its_ward`.)*

### 14.7 Corpses (added 2026-10-11, owner: "add in the corpses for some things so they get in the way like Darkest Dungeon")

Beasts and humanoids of several families (wolves, gangers, soldiers, oni, werewolf, mountain troll, yeti, goblin thug, toad, captain, lurkers, scorpion) leave a **corpse** (`Remains: "corpse"`, tags Remains + Corpse) on their tile: it blocks movement like any remains, sits in pincer lines (a pincer through it clears it — "Cleared away"), takes area spells, has no turn and never rises, **does not hold up a wave clear**, earns no mana or loot, and **rots away after 6 player actions**. It's drawn as the fallen unit's own art, upright and greyed (amended 2026-10-19, owner: "don't turn monsters sideways" — it was rotated 90°); its card says whose body it is and when it rots. *(Tests: `A_fallen_foe_leaves_a_corpse_that_blocks_the_way_but_not_the_win`, `A_corpse_blocks_movement_and_rots_away`.)*

### 14.8 Reinforcements and eggs (added 2026-10-12, owner direction)

Owner: "sometimes enemies can lay eggs that hatch, sometimes new enemies don't spawn on the first turn... every wave can offer a surprise like fighting monsters then suddenly soldiers show up."

**Delayed mid-wave spawns.** `SpawnDef.DelayTicks` (default 0 = today's immediate spawn, unchanged for every untouched wave) queues an entry into `BattleState.PendingSpawns` instead of spawning it at wave start; `BattleRunner.Tick` resolves any entry whose `TriggerClock` has arrived via `ResolvePendingSpawns`, spawning it exactly like a normal wave arrival. A wave with an unresolved pending entry **holds the wave open** even once the board is clear (`CheckEnd` checks `PendingSpawns` alongside living units), so the wave never advances out from under a reinforcement still inbound.

**The surprise beat.** `WaveDef.SurpriseSpeaker`/`SurpriseLine` (optional, wave-scoped) fire once — on the *first* delayed entry a wave triggers, not per enemy — as a `ReinforcementsArrivedEvent`. The App layer plays a quick, per-class entrance (burst-up for `Brain.Burrower`, drop-from-above for `Flying`-tagged classes, fade-in for `Undead`-tagged classes, the ordinary wave fade+drop otherwise — derived entirely from existing `Tags`/`Brain`, no new content fields needed), then, if a line is set, a held narrative card: the speaker's portrait and name beside the line (reusing the same 360×400 bordered-frame convention as the character-select reveal and the level-up fanfare), dismissed by a tap or ~2.6s, same dual-exit idiom as the story crawl. Tithe Moors I's wave 4 is the flagship example: a goblin/bandit skirmish that a captain's soldier squad reinforces mid-fight. Automation: `/demosurprise?speaker=&line=` plays the beat on demand for testing.

**Eggs.** An egg-laying class sets `ClassDef.LaysEgg` (the egg's class id) and `EggInterval` (lay every Nth own turn, default 4); `BattleRunner.Enemy.LayEgg` drops the egg on a free adjacent tile, mirroring how `LeaveRemains` drops a grave marker. The egg class is tagged `Egg` (`Unit.IsEgg`, the `IsRemains` pattern's sibling) and is otherwise built exactly like a gravestone/bone pile (`Brain.Sentinel`, immobile, no turn except its own). When the egg's timeline turn arrives, `Hatch` spawns its `RisesAs`/`RisesLevel` class on the same tile **at full HP** (a fresh enemy, unlike a risen undead's half-HP second life) and leaves normal loot. `naga00` (Arkgrave II's solo finale) is the one showcase of this so far. *(Tests: `A_delayed_spawn_arrives_partway_through_its_wave_not_at_wave_start`, `A_waves_reinforcement_dialogue_fires_once_even_with_multiple_delayed_entries`, `An_egg_hatches_into_its_real_enemy_at_full_hp_on_schedule`.)*

### 14.9 Rule-breaking bosses (added 2026-11-05, owner direction, GG-T234)

Owner: "a boss who 'breaks the rules' as far as standard gameplay goes." Each main-biome finale (the third stage of Tithe Moors, The Lantern Road, Shardfall, Arkgrave, Anchor Wastes, The Still, Halcyon and The Choir's Cathedral) has one boss that breaks **one** standard rule, each a different one (but see the GG-T344 amendment below), each with a counter the player can read.

- **Data:** `SpawnDef.RuleBreak` (`RuleBreak` enum: `None`, `TwinTurn`, `AxisWard`, `MireSwap`, `GravityStamp`, `Undying`) on that one spawn in the finale wave, so the same class plays by the normal rules everywhere else (the Captain fights in many stages). `ContentLoader.Validate` (`RuleBreaks.Validate`) refuses a rule-break on a guest and a `MireSwap` on a multi-tile or snake body. Copied onto `Unit.RuleBreak` at spawn (immediate or delayed); `Unit.RuleBreakCount` keeps its bookkeeping.
- **Telegraph:** on arrival, a `RuleBrokenEvent` (`Arrived`) bannering the broken rule and the counter ("The Mecha Armor ignores the pincer rule: side-to-side pincers glance off. Pincer it top to bottom!"), with a war cry, a screen shake and a **RULE BREAKER** float. Every bite after that is another `RuleBrokenEvent` with its own cue and float (DOUBLE TIME! / WARDED! / SWAP! / STAMP! / UNDYING!). All of it runs through `BattleEngine.Apply` → events (`BattleRunner.RuleBreak.cs`).

| Finale | Boss | Rule it breaks | What happens | Counter |
|---|---|---|---|---|
| Tithe Moors III | Captain | the turn order (§2) | **TwinTurn:** after its own turn, if no hero stands beside it, it marches and strikes again at once (no second cast, trap or buff tick) | engage it: a hero beside it at the end of its turn holds it to one |
| The Lantern Road III | Oni (`oni02`) | a fallen foe stays down (§13.3) | **Undying** (*added 2026-11-07, GG-T345*), the Hierarch's rule on an oni chief whose mask gets back up: the first time it falls while another foe still stands, it rises at half HP | cut down its raiders first; it rises only once |
| Shardfall III | Mecha Armor | the pincer rule (§1.2) | **AxisWard:** a hero pincer along its row (both attackers on its row) glances off, with no attacker or supporter strike ("Warded!", no mana), and cracks the ward | pincer it top to bottom; the third glancing pincer (`RuleBreakRules.AxisWardCracks`) shatters the ward and every pincer lands after, so a foe hugging an edge can't stall the fight |
| Arkgrave III | Swamp Mistress | one tile at a time (§1.1) | **MireSwap:** at the start of its turn, with a hero beside it, it trades tiles with the ally standing farthest from any hero (one-tile allies only), then takes its turn | cut down its coven first; with no ally left it has nowhere to go |
| Anchor Wastes III | Cyclops | only you move your heroes | **GravityStamp:** every 2nd own turn it stamps and drags every hero within 3 tiles (not already beside it) 2 tiles toward itself, nearest first (`Pull`); the turn before, it warns "raises a foot: it stamps next turn!" | keep out of reach on the warning, or stand ready: the pull drops heroes beside it, which is where a pincer starts |
| The Still III | Chronomancer | the turn order (§2) | **TwinTurn** (*added 2026-11-07, GG-T344*), the Captain's rule on a time-mage: the keeper of the Still's clocks takes a second turn whenever no hero stands beside her | close in on her: a hero beside her at the end of her turn holds her to one |
| Halcyon III | Kiln Guard captain (`ceramicknight06`) | the pincer rule (§1.2) | **AxisWard** (*since 2026-11-07, GG-T350; the Hierarch moved up to the cathedral*), the Mecha Armor's rule on the porcelain knight holding the spire stair: row pincers glance off its glaze and crack it; the third shatters it | pincer it top to bottom, or crack the glaze three times |
| The Choir's Cathedral III | Hierarch (`hierarch`) | a fallen foe stays down (§13.3) | **Undying:** the first time it falls while any other foe still stands (graves, eggs and bodies don't count), it rises at half HP, shedding its debuffs. Its Choir (a Cantor and a Confessor) stands beside it | silence its Choir first; it rises only once |

*Amended 2026-11-07 (GG-T344):* The Still (§22.3) is a sixth main biome, so six finales now carry a rule-breaker and two of them break the same rule. The Still III reuses TwinTurn on the Chronomancer rather than adding a sixth engine rule: she is the Still's timekeeper and already fights the party's tempo, and the owner made no new art for a boss.
*Amended 2026-11-07 (GG-T345):* The Lantern Road (§22.3.2) is a seventh main biome. Its finale reuses **Undying** on an oni (`oni02`) rather than adding an engine rule: existing units only, and the oni's lore is a mask that grew on. So two pairs of finales now share a rule (TwinTurn, Undying).
*Amended 2026-11-07 (GG-T350):* The Choir's Cathedral (§22.3.3) is an eighth main biome and the new finale. The Hierarch keeps **Undying** and moves to the Cathedral III; Halcyon III gets a Kiln Guard captain with **AxisWard**, so each of TwinTurn, AxisWard and Undying is shared by two finales and MireSwap and GravityStamp stay unique.

A frozen or sleeping rule-breaker skips its rule along with its turn. The bot handles them without special-casing beyond pincer scoring: a warded row pincer is worth +3 (it cracks the ward) instead of +10 (`HeroBot.ScoreMove`), so it prefers a column pincer. *(Tests: `RuleBreakTests` — the arrival banner on the flagged spawn only; TwinTurn's second turn and its engaged counter; AxisWard's glancing row pincer, landing column pincer and shatter; MireSwap's swap and its no-ally counter; GravityStamp's warning then pull, out-of-reach heroes untouched; Undying's single rise with a thrall standing, and staying down with none or once risen; the eight main finales carry exactly these rule-breaks and no other stage does; validation of a guest rule-break and a big-body swap.)*

### 14.10 The Chronomancer (added 2026-11-05, owner direction, GG-T275)

Owner: "Stack haste, while a Chronomancer casts slow on you would be an interesting battle mechanic." A caster that fights the party's **tempo** (§2.11) instead of its health.

- **Class.** `chronomancer`, an enemy caster (Kiter brain, shoots 3 tiles, moves 1). Its portrait is the owner's own art (GG-T328, cut with `tools/portrait_cut.py`, a 1024 canvas since GG-T352); `"Portrait": "chronomancer"` keeps `tools/import_art.py` from overwriting it with the old `Hag03` stand-in. It is not a Crucible family: it has a boss phase, which the Crucible excludes.
- **Temporal Drag** (`charge_temporal_drag`). A telegraphed charge (2.5 s on the cast lane), `AllEnemies`: 4 Arcane to every hero plus **Slowed** (2 turns). That is one −1 tempo effect for the whole party, not one per hero, so every foe's icon runs ×1.25.
- **Rewind** (`chrono_rewind`). Once, at 40% HP (its boss phase), it heals 20 and hastens **its own** icon 0.5 u. That never touches party tempo (§2.7.1).
- **Brain.** It holds Drag while the party's net tempo is already negative (`EnemyPlanner.PlanCast`). It moves or shoots instead, and drags when the party is level or hasted, which is the tug-of-war. As a Kiter it keeps out of pincers.
- **Counterplay** (named on its kit line and in Drag's InspectCard text):
  - **interrupt**: hit it while it charges (§13.4 stagger and cancel)
  - **cleanse** Slowed off every hero (§2.11, the tempo lifts)
  - **out-haste** it: stacked haste outweighs one Drag
- **Bot.** `HeroBot` values pincering or damaging a foe that is charging a tempo drain (+6 / +5) and cleansing a negative-tempo debuff while net tempo is below zero (+4 per point).
- **Placement.** Anchor Wastes II, wave 2 (behind two ceramic knights). On the same 12 casual-bot seeds and parties it wins 9 of 12 (75%) with the Chronomancer and 8 of 12 without, inside the POL-G1 55–85% band. The wave was already long (36 actions without it, about 43 with it): the knights' tough hide is the slog, and that's left to the balance loop (GG-T276).
- *(Tests: `ChronomancerTests`. Content validates and is placed; one Drag on two heroes is −1 tempo with both Slowed; cleansing the last Slowed hero restores tempo; Drag is held while tempo is negative and cast when level or hasted; Rewind is self-only.)*

### 14.11 The Murder — harpies (added 2026-11-07, owner art, GG-T329)

The Murder (docs/STORY.md §3.2): Street kids spliced with black wings, rooftop smugglers. Three swift (Speed 18,
re-entering ≈3 s out), frail, `Sentient` flyers (`Flying`), one family (`harpy` drop table), three roles:

| Class | Role | Brain / targeting | Kit | Support |
|---|---|---|---|---|
| `harpy01` Harpy Raider | striker | Skirmisher (strike, dart a tile away) / Weakest, moves 2 | **Talon Rake** (`talon_rake`): a landed blow, 25%: [Slowed] on the target, so the party loses a point of tempo (§2.11) | Crit +25% (*Diving Strike*) |
| `harpy02` Harpy Shrieker | disruptor | Kiter / Weakest, moves 2 | **Gale Shriek** (`charge_gale_shriek`): 2 s charge, a 3×3 around one hero, 4 Arcane and `Pull` 2 — each hero caught is dragged toward the harpy, out of the formation the party built; rests a turn, recovers 0.5 s. Blur | Haste its attacker 1 s (*Tailwind*) |
| `harpy03` Harpy Spotter | ranged | Kiter / Caster, shoots 3, moves 2 | **Jammer Darts** (`jammer_darts`): a landed blow, 25%: [Silenced] | Silence 30%, reach 2 (*Jammer Drone*) |

- **Counterplay** (on their cards): they are frail, so [pincer] them when they land; cleanse the Slow; interrupt the
  shriek or keep heroes spread; close in on the spotter. Weak to Lightning (×1.5).
- **Design note.** The gale first knocked heroes *away* (Knockback 1). A hero blown into the board's edge takes the
  18-damage wall slam, and the casual bot fell from 8/12 to 1/12 on the trial stage, so the gale pulls instead: same
  "breaks your lines" identity, no slam.
- **Placement.** Tithe Moors II (`greenvalley-02`), wave 4: the three harpies replace two goblin thugs and a Redline
  ganger. Casual bot, 24 seeds, campaign parties: 11/24 with them and 11/24 without; smart bot 23/24 vs 24/24.
- *(Tests: `HarpyTests`.)*

### 14.12 The Void Sentinel (added 2026-11-07, owner art, GG-T330)

What forms where an Anchor leaks (docs/STORY.md §3.5): smoke given a body around a cyan heart of stolen aether. An
elite guardian, and the first enemy to use the Line charge built in §13.4.

- **Class.** `voidsentinel`: `Construct`, `Magic`, not `Sentient` (it never makes a pincer, but supports one). Speed 8
  (re-enters ≈4.8 s out), VIT 18, a 20-point front **armor plate** (§13.5), Guardian brain (stays beside its most
  valuable ally) with Threat targeting. Resists Dark (×0.5), weak to Holy (×1.5).
- **Void Beam** (`charge_void_beam`, `ChargeShape: Line`). A 2.5 s charge whose line, from beside it to the board's edge
  toward its target, is marked when it starts. On landing, every hero still on the line takes a Dark spell (INT vs WIS)
  and is [Silenced]. It rests a turn between beams and recovers 1.5 s.
- **Void Core** (`void_core`): the first hit that drops it below half HP flares a [Ward] on it.
- **Support:** *Void Aegis*, [Ward] on its attacker, reach 2.
- **Counterplay** (its card): step off the marked line or interrupt the charge; pincer it from the flanks, not the plate.
- **Placement.** Anchor Wastes I (`cave-01`) wave 5, added beside the two oni and the ice mauler it guards. Casual bot,
  24 seeds, campaign parties: 15/24 with it, 18/24 without; smart bot 24/24 both. Of 178 beams the casual bot let
  charge, 175 landed and caught 71 heroes: most beams hit an empty line because a hero had moved off it.
- *(Tests: `VoidSentinelTests`.)*

### 14.13 The Lich (added 2026-11-07, owner art, GG-T331)

The last captain of the Ark *Long Patience*, who refused to let his ship die and kept himself instead (docs/STORY.md
§3.5). A boss caster for the Arkgrave.

- **Class.** `lich`: `Undead`, `Sentient`, `Caster`, `Boss`. Speed 11, INT 18, WIS 16, frail body (VIT 14). Commander
  brain (keeps ~3 tiles back with an ally between it and the party), Weakest targeting, shoots 3. Resists Dark (×0.5),
  weak to Holy (×1.75); Poison Ward and Steadfast. Not tameable and not a Crucible family (it has boss phases).
- **Soulfire** (`charge_soulfire`): a 2.5 s charge on a 3×3 around its target: 6 Dark to every hero inside and a 50%
  [Burning]; recovers 1.5 s. Spread out, or interrupt it.
- **Soul Harvest** (`lich_soul_harvest`, boss phase at 50%): once, 5 Dark to every hero, and it heals 6 per hero touched.
  Enemy-only (`MetaServices` `EnemyOnly`), like Enrage and Rewind.
- **The grave.** `Remains: gravestone` (§14.5): when it falls it leaves a grave, and left standing the grave raises it on
  its third turn. That is the phylactery, with no new code. A risen Lich enters its harvest phase again.
- **Support:** *Soul Echo*, Cast its Soulfire at 50% power, reach 2.
- **Placement.** Arkgrave I (`swamp-01`) finale, wave 5: the Lich behind a marsh shambler, in place of a lurker. Casual
  bot, 24 seeds, campaign parties: 10/24 with it, 13/24 without. The waves before it are unchanged: 14 runs reached
  wave 5 both times, and the casual bot beat the Lich in 10 of its 14 tries (13 of 14 against the old wave). Smart bot
  24/24 both. The first cut (8 Dark, Burning every hit) took the stage to 8/24; Burning alone was more damage than the
  Lich's own spells. The casual bot let it rise from the grave 3 times in 24 battles.
- *(Tests: `LichTests`.)*

### 14.14 The Infernalists (added 2026-11-07, owner art, GG-T338)

The Ember's zealots (docs/STORY.md §3.6): black armour, long red capes, and the belief that the Nearing is a
homecoming. Four `Sentient` variants, one `infernalist` family. They resist Fire (×0.5), are weak to Ice (×1.5), and
three of them carry Infernal Hide (they can't be [Burned]). The family plays one combo: **burn, then execute.**

| Class | Role | Speed / brain / targeting | Kit | Support |
|---|---|---|---|---|
| `infernalist01` Zealot | brander | 15 / Hunter / Threat | **Cinder Claws** (`cinder_claws`): a landed blow, 35%: [Burning] on the target | Status burning 50% (*Cinder Brand*) |
| `infernalist02` Pyromancer | caster | 11 / Kiter / Squishy | **Hellfire** (`charge_hellfire`): 2.5 s charge, a Cross (radius 1) on a hero, 6 Fire and [Burning] on each hero caught; recovers 1.5 s | Cast at 50% (*Ember Echo*), reach 2 |
| `infernalist03` Bloodbinder | healer | 11 / Medic / Threat | **Blood Rite** (`charge_blood_rite`): 2 s charge on its most wounded ally (below 70%): heals 25 and [Empowers] it; rests a turn. Lifesteal | Heal the chain 10%, reach 2 (*Blood Pact*) |
| `infernalist04` Executioner | finisher | 11 / Duelist / Weakest | **Executioner's Verdict** (`executioners_verdict`): its blows land ×1.5 on a [Burning] foe | Crit +30% (*Mark for Death*) |

- **Engine (small).** The Verdict needed a trigger condition on the *other* unit's status, so `TriggerCondition` gains
  `OtherHasStatus` (§4.1): checked against the context's other unit (the attacker, else the target) in
  `Triggers.ConditionHolds`, written "the foe has [Burning]" by `TriggerText`, and validated as a known buff id.
- **Counterplay** (their cards): cleanse the burn before the Executioner reaches the hero, or keep burning heroes away
  from it; spread out against Hellfire; kill the Bloodbinder first or interrupt the rite; Ice spells.
- **Placement.** The Shardfall, where the Ember wakes. Shardfall I (`desert-01`) wave 3: Pyromancer and Bloodbinder in
  place of two sand maws. Shardfall II (`desert-02`) wave 2: Zealot and Executioner beside a sand maw, in place of a
  militia soldier, so the burn-then-execute combo plays out. Casual bot, 24 seeds, campaign parties: desert-01 18/24
  with them vs 19/24 without, desert-02 14/24 vs 15/24; smart bot 24/24 on both, with and without. The first
  Executioner (STR 17, VIT 15, healed by the Bloodbinder beside it) dealt twice a soldier's damage, so it was cut to
  STR 14 and VIT 13 and paired with the Zealot instead.
- *(Tests: `InfernalistTests`.)*

### 14.15 The Bulwark (added 2026-11-07, owner art, GG-T332)

The Pale Concord's soldiers (docs/STORY.md §3.1): white power armour, siege rifles, and an aether pack on every back,
each pack a portable Still. Four `Sentient` variants, one `bulwark` family, all slow or steady (Speed 8 or 11, re-entering
≈4.8 s or 4 s out). Sealed suits resist Poison (×0.5); the armour shorts out under Lightning (×1.5). The squad plays
**hold the line and tithe the party**: a shield-Still wards the soldiers, the rifles and the siege gun shoot from range,
and the Siphoner drinks the party's orbs.

| Class | Role | Speed / brain / targeting | Kit | Support |
|---|---|---|---|---|
| `heavytrooper01` Rifleman | ranged | 11 / Kiter / Squishy, shoots 3 | **Suppressing Fire** (`suppressing_fire`): a landed shot, 30%: [Blinded] | Blind 30%, reach 2 (*Covering Fire*) |
| `heavytrooper02` Shieldbearer | wall | 8 / Guardian / Threat, plate 30 | **Aegis Field** (`charge_aegis_field`): 2 s charge, [Ward] on every ally within 2 tiles; rests a turn, recovers 0.5 s | Protection on the chain (*Pack Shield*) |
| `heavytrooper03` Siphoner | tithe | 11 / Kiter / Caster | **Aether Siphon** (`charge_aether_siphon`): 2 s charge on a hero, 4 Arcane, and **an orb leaves the party's bank**; rests a turn, recovers 0.5 s | Haste its attacker 1 s, reach 2 (*Vent Aether*) |
| `heavytrooper04` Siege Gunner | artillery | 8 / Commander / Threat, plate 24 | **Siege Shot** (`charge_siege_shot`, `ChargeShape: Line`): 3 s charge down a marked line to the board's edge, a Lightning spell on every hero still on it; rests a turn, recovers 2 s | Pushback 0.75 s (*Rail Recoil*) |

- **Engine (small): the siphon.** `AbilityDef.DrainsOrbs` (0–3, validated, only on a `charge_` ability, so a hero can
  never learn it): when the charge lands on at least one hero (a single target that isn't dodged, or any hero inside an
  area), `BattleRunner.DrainOrbs` removes up to that many orbs from the party's `ManaBank`, emitting `OrbsSpentEvent`
  with `Drained` (the bank redraws and the cast cue plays; telemetry doesn't count it as mana spent) and "Siphoned an
  orb!". An empty bank announces "No aether to take". The smart bot treats a siphon charging while the bank holds orbs
  like a tempo drain (`HeroBot.ChargesTempoDrain`): worth staggering.
- **Counterplay** (their cards): spend orbs before a siphon lands, or interrupt it; flank the Shieldbearer and the Siege
  Gunner off their plates; step off the siege gun's marked line; close on the Rifleman and pincer it; Lightning.
- **Placement.** Halcyon, where the Concord holds the spire. Halcyon II (`cityruins-02`) wave 3: Shieldbearer, Rifleman
  and Siphoner in place of two Chromed Hollow. Halcyon III (`cityruins-03`) wave 2: the Siege Gunner beside the lone bat.
  Casual bot, 24 seeds, campaign parties: cityruins-02 15/24 with them vs 20/24 without (inside the 55–85% band; the
  losses still come at the reaper's wave 5, with the wounds the squad leaves), cityruins-03 17/24 vs 18/24; smart bot
  24/24 everywhere. Over the cityruins-02 casual runs the siphon landed 139 times but found orbs in the bank only 37
  times: the casual bot spends what it earns, which is exactly the counter. The first Shieldbearer also had Tough Hide;
  plate, Ward and Protection were already a slog, so it was dropped. A two-soldier wave (Shieldbearer and Siphoner, with
  the Rifleman moved beside the gun) measured 14/24 and cost cityruins-03 three wins.
- *(Tests: `BulwarkTests`.)*

### 14.16 The Anchor Watch — wraith knights (added 2026-11-07, owner art, GG-T334)

Knights sworn to guard the Anchors forever, kept going by the arches' own leaking aether long after they should have
died (docs/STORY.md §3.5): grey plate, cloaks the colour of old fire. Three `Undead`, `Sentient` variants, one
`anchorwatch` family. They resist Ice (×0.5) and Fire undoes them (×1.5). The family plays **undying**: every knight
leaves a gravestone that raises it again unless smashed (§14.5), and each role adds a different way of refusing to fall.

| Class | Role | Speed / brain / targeting | Kit | Support |
|---|---|---|---|---|
| `wraithknight01` Watch Sentry | the post | 8 / Sentinel (never moves) / Threat, plate 30 | **Rime Riposte** (`rime_riposte`, Reactive): attacked directly, 50%: a [Counterstrike] at 60% that also [Slows] the attacker. Steadfast | Ward on its attacker (*Oath Shield*) |
| `wraithknight02` Watch Reaver | the hunter | 15 / Hunter / Weakest | **Oathbound** (`oathbound`, Reactive): the first time a hit drops it below 35% HP it heals 25% and gains [Ward] (trigger `Cooldown` 99: once a battle) | Slow 35% (*Rime Edge*) |
| `wraithknight03` Watch Vigilant | the keeper | 11 / Commander / Weakest | **Ember Vigil** (`charge_ember_vigil`): 2 s charge, [Regen] on every ally within 2 tiles; rests a turn, recovers 0.5 s | Heal the chain 10%, reach 2 (*Old Fire*) |

- **No engine code.** Sentinel brain, Reactive triggers, a trigger cooldown, an AllyOnly area charge and gravestone
  remains are all existing data.
- **Counterplay** (their cards): strike the Sentry from range or accept the riposte, and flank its plate; finish the
  Reaver in one push once its oath has held; interrupt the Vigil or kill the Vigilant first; smash the graves; Fire.
- **Placement.** The Anchor Wastes. Anchor Wastes I (`cave-01`) wave 2: a Reaver beside a skelepede, in place of the
  second skelepede. Anchor Wastes II (`cave-02`) wave 3: Sentry and Vigilant beside the goblin thug, in place of an oni
  and a skelepede. Casual bot, 24 seeds, campaign parties: cave-01 14/24 with them vs 15/24 without; cave-02 6/24 vs
  7/24 (already below the band before them: most losses are still at the Chronomancer's wave 2); smart bot 24/24 on
  both. In the casual runs the Reaver's oath held 29 times and the graves stirred 134 times.
- *(Tests: `AnchorWatchTests`.)*

### 14.17 The Choir — cultists (added 2026-11-07, owner art, GG-T335)

The Pale Concord's engineer-priests (docs/STORY.md §3.1): faceless hoods, white ponchos printed with violet
circuit-glyphs. They run the Stills and sing the aether out of people; the glyphs are the hymn. Three `Sentient`
variants, one `choir` family, every one of them a singer: each role is a telegraphed charge the party can read on the
bar and interrupt. Their own song is aether, so they resist Arcane (×0.5); they are weak to Dark (×1.5).

| Class | Role | Speed / brain / targeting | Kit | Support |
|---|---|---|---|---|
| `cultist01` Choir Cantor | the hymn | 11 / Commander / Weakest | **Tithe Hymn** (`charge_tithe_hymn`): 2.5 s charge on the whole party, 3 Arcane to every hero and a 50% [Silenced] on each; rests a turn, recovers 1.5 s | Silence 30%, reach 2 (*Hush*) |
| `cultist02` Choir Psalmist | the buffer | 15 / Kiter / Squishy | **Psalm of Certainty** (`charge_psalm_of_certainty`): 2 s charge, [Empowered] on every ally within 2 tiles; rests a turn, recovers 0.5 s | Empower its attacker, reach 2 (*Certainty*) |
| `cultist03` Choir Confessor | the absolver | 11 / Medic / Threat | **Absolution** (`charge_absolution`): 2 s charge on its most wounded ally (below 70%): heals 15, [Cleanses] every debuff, gives [Ward]; rests a turn, recovers 0.5 s | Cleanse the chain (*Absolve*) |

- **No engine code.** An AllEnemies charge with a buff chance, an AllyOnly area buff and a single-ally heal that also
  cleanses and wards are all existing dispatcher data.
- **Counterplay** (their cards): cast your spells before the hymn lands, interrupt it, or cleanse the Silence after;
  pull the Concord away from the Psalmist or interrupt the psalm; kill the Confessor first, or interrupt the absolution.
- **Placement.** Halcyon III (`cityruins-03`) wave 3: a whole Choir (Cantor, Psalmist, Confessor) in place of two
  Chromed Hollow and the Choir's training mannequin. Casual bot, 24 seeds, campaign parties: 19/24 with them vs 17/24
  with the stage as it stood (the Bulwark's Siege Gunner already in wave 2; 18/24 before either); smart bot 24/24 both.
  The stage got no harder: the three frail singers stand in for a cyber-zombie pair whose death bursts (14 to every
  adjacent hero) hurt more than a hymn, and the casual bot reached wave 5 in 23 of 24 runs.
- *(Tests: `ChoirTests`.)*

### 14.18 The Herd — minotaurs (added 2026-11-07, owner art, GG-T339)

The Street's hired muscle (docs/STORY.md §3.2): kids spliced with horns and bull-mass in the underspire's back-room
clinics, in hoodies, cargo pants, platform sneakers and chrome. Four `Humanoid`, `Sentient` variants, one `herd` family
(`Remains: corpse`). The splice holds a body's aether in so the Stills can't sing it out, so they resist Arcane (×0.5);
the chrome grafts short out under Lightning (×1.5). The family plays **loyalty on the timeline**: they are loyal to the
crew that pays and feeds them, and every bull carries **Stampede** (`herd_stampede`, Reactive `OnAllyKO`): when any ally
falls (a bull or the crew that hired it), it casts the free `stampede_rush` on itself and its icon jumps 0.3 of the bar
(1.5 s) sooner (§2.7.1).

| Class | Role | Speed / brain / targeting | Kit | Support |
|---|---|---|---|---|
| `minotaur01` Herd Boss | the paymaster | 11 / Commander / Weakest | **Feed the Herd** (`charge_feed_the_herd`): 2 s charge when an ally is below 70%, heals every ally within 2 tiles 12 and rushes each 0.15 of the bar; rests a turn, recovers 0.5 s | Haste its attacker 1 s, reach 2 (*On the Payroll*) |
| `minotaur02` Herd Bouncer | the wall | 8 / Guardian / Threat, plate 24 | **Brass Knuckles** (`brass_knuckles`): a landed punch, 30%: [Slowed] (the party loses a point of tempo) | Ward on its attacker (*Bodyguard*) |
| `minotaur03` Herd Gorer | the charge | 11 / Hunter / Threat | **Bull Rush** (`charge_bull_rush`, `ChargeShape: Line`): 2.5 s charge down a marked line to the board's edge, 8 Physical plus its INT hit on every hero still on it; rests a turn, recovers 1.5 s | Pushback 0.75 s (*Trample*) |
| `minotaur04` Herd Breaker | the brawler | 15 / Berserker / Weakest | **Berserk**: ×1.35 below half HP; charges in, never retreats | Crit +25% (*Gore*) |

- **No engine code.** An `OnAllyKO` reactive that casts a free self-haste (`stampede_rush`, enemy-only in
  `MetaServices` `EnemyOnly` like Enrage), an AllyOnly heal-and-haste charge and a Line charge are all existing data.
- **Counterplay** (their cards): kill the Herd before the crew that hired it, or watch the bar when a ganger falls and
  [Pushback] the bull that lurches forward; kill the Boss first or interrupt the feeding; flank the Bouncer off its plate;
  step off the Gorer's line; finish the Breaker in one push; Lightning.
- **Placement.** Street territory. Tithe Moors III (`greenvalley-03`) wave 3: a Bouncer and a Gorer hired by two Redline
  gangers, in place of the other two gangers. Halcyon I (`cityruins-01`) wave 5: the Boss and the Breaker beside a
  Hollow, in place of the Choir's training mannequin and a bat. Casual bot, 24 seeds, campaign parties, difficulty 10:
  greenvalley-03 8/24 with them vs 10/24 without, cityruins-01 9/24 vs 9/24 (both stages were already below the 55–85%
  band); smart bot 24/24 on both, with and without. In the casual runs the herd stampeded 72 times and the
  Gorer rushed 134 times on greenvalley-03; on cityruins-01 the Boss fed its crew 118 times.
- *(Tests: `HerdTests`.)*

### 14.19 The Hellspawn (added 2026-11-07, owner art, GG-T340)

What the Ember makes of a beast or a person who drinks from the Shardfall's red rivers (docs/STORY.md §3.6): red scale,
black hooked claws, manes and tails of blood-light. Whatever they were is gone, so they are `Beast`s, not `Sentient` (they
never plan a pincer). Four variants, one `hellspawn` family. Born of the Ember, they resist Fire (×0.5), all carry Infernal
Hide (they can't be [Burned]) and are weak to Ice (×1.5). The family plays **the hunger**: every one carries
**Bloodscent** (`bloodscent`, Passive `Always`): its blows land ×1.5 on a foe below half HP.

| Class | Role | Speed / brain / targeting | Kit | Support |
|---|---|---|---|---|
| `hellspawn01` Ravager | the hunter | 15 / Hunter / Weakest | Bloodscent | Burning 50% (*Cinder Mane*) |
| `hellspawn02` Drinker | the runner | 18 / Skirmisher / Weakest | Bloodscent and [Lifesteal]: strikes, drinks and darts away | Haste its attacker 1 s (*Blood Rush*) |
| `hellspawn03` Emberheart | the tide | 11 / Stalker / Caster | **Red Tide** (`charge_red_tide`, Shape `Row`): 2.5 s charge, the red river floods its target's whole row, 5 Fire and [Burning] on every hero in it; rests a turn, recovers 1.5 s | Cast at 50%, reach 2 (*Red Spill*) |
| `hellspawn04` Scaleback | the wall | 8 / Relentless / Threat, plate 20 | **Searing Scales** (`searing_scales`, Reactive `OnAttackedDirect`, 35%): [Burning] on the attacker | Protection on the chain (*Scale Wall*) |

- **Engine (small).** Bloodscent needed a trigger condition on the *other* unit's HP, so `TriggerCondition` gains
  `OtherHpBelow` (§4.1, like GG-T338's `OtherHasStatus`): checked against the context's other unit in
  `Triggers.ConditionHolds` (strictly below the fraction), written "the foe is below 50% HP" by `TriggerText`, and
  validated to 0..1 by `ContentLoader.Validate`.
- **Red Tide** is the first enemy charge with the `Row` shape: it follows its target, so the hero it picked is always
  caught, and the rest of a row the party lined up for a pincer can step out of it.
- **Counterplay** (their cards): keep the party above half HP or keep the wounded out of reach; interrupt the tide or leave
  the row; [pincer] the Drinker when it lands; strike the Scaleback from range or cleanse the burn; Ice.
- **Placement.** Shardfall III (`desert-03`), the stage the Infernalists left untouched. Wave 2: Ravager and Drinker
  beside the tech gremlin, in place of two militia soldiers. Wave 4: Scaleback and Emberheart beside the gremlin and the
  vulture, in place of two more soldiers. Casual bot, 24 seeds, campaign parties, difficulty 10: 13/24 with them vs 8/24
  without; smart bot 24/24 both. The stage gets easier: the four soldiers they replace are plated, countering, `Sentient`
  Duelists that pincer, and the beasts are not; most losses are still at the Captain's wave 5 (7 of 11 with them, 11 of
  16 without). In the casual runs the tide landed 103 times and the scales seared 19 attackers.
- *(Tests: `HellspawnTests`.)*

## 15. Save

### 15.1 Data model

**Amended 2026-10-09:** **three save slots** replace profiles and save-state lists. **Amended 2026-11-02 (GG-T99):** profiles are back — each player profile owns its own three slots and its own settings ([§15.4](#154-player-profiles)); `Settings` is no longer part of `SaveData`.

```
SaveData                       (Meta/SaveData.cs; one per slot)
├─ Version (1), Name ("Player"), CreatedUtc, UpdatedUtc, PlaySeconds
├─ Gold (wallet), LifetimeCoins (ticker)
├─ HighestClearedStage (campaign index, −1 = none), ClearedStages{}, SeenCrawls{}, LastStage
├─ Roster[]  HeroSave: ClassId, TotalXp, Hp (0 = full), Known[], Bar[5], SkillPoints,
│                       WeaponId/Durability/Repairs, ArmorId/Durability/Repairs, Relics[3]
├─ Party[]   class ids of the active squad (≤ 6, `Progression.MaxPartySize`, GG-T291)
├─ Inventory { itemId → count }, Recruits
├─ Bounty    ActiveId, Progress, Claimed{}
├─ Bestiary  { classId → Seen, Defeated (+ legacy Scanned) }  knowledge tier = BestiaryKnowledge.Tier
└─ (Settings moved to the profile's settings.json, §15.4 — an old slot's Settings block is ignored on load)
```

Level is **derived** from `TotalXp` (`Formulas.LevelFromTotalXp`, `XpToNext(L) = 50 + 10·L²`), so the curve can change without migration. `SaveMigrations.Migrate` upgrades in place (v1 is the first shape; it backfills missing sections and pads Bar/Relics) and rejects saves newer than the build. Dictionaries and sets serialize natively (Newtonsoft) — the old "lists only" limit is gone.

**Amended 2026-10-15 (GG-T49):** every level gained now also earns **skill points** — `HeroSave.SkillPoints`, an unspent balance spent on skill-tree nodes (content and UI land in GG-T50). `Formulas.SkillPointsForLevelUp(from, to)` awards `SkillPointsPerLevel` (1) for each level crossed, plus a `SkillPointMilestoneBonus` (1) on every `SkillPointMilestoneInterval`th level (5, 10, 15…) — summed per level so a multi-level jump still hits every milestone in between. It's applied everywhere a hero's level can move: a battle-end level-up, hard-won XP kept on a defeat/retreat, a guest who survives joining the roster, and a Summon Circle recruit arriving above level 1. Save shape bumped to **v2**: `SaveMigrations` backfills each existing hero once with the points they'd already have earned up to their current level, so no save is retroactively shorted by the mechanic not existing yet; re-migrating a current-version save does not grant them twice. *(Tests: `Leveling_up_in_battle_grants_skill_points_including_the_milestone_bonus`, `Hard_won_xp_on_defeat_still_grants_skill_points_when_it_crosses_a_level`, `Migrating_a_v1_save_backfills_skill_points_once_for_heroes_already_past_level_1`, `A_guest_who_survives_a_victory_joins_the_roster`, `Full_loop_battle_commit_then_every_vendor`.)*

### 15.2 Persistence flow

`App/Services/SaveStore`: `slot1.json`…`slot3.json` under the active profile's `persistentDataPath/Profiles/<Name>/Saves` (**amended 2026-11-02, GG-T99**; was `persistentDataPath/Saves`; `-saveRoot DIR` puts the whole Profiles folder under `DIR/Profiles`), written atomically (temp file + `File.Replace`). `GameApp.Persist` writes the slot; a settings change is written by `GameApp.SaveSettings` to the profile's settings.json instead:

| Trigger | Saved? |
|---|---|
| New game in a slot | yes |
| Every successful Camp action (buy, sell, craft, repair, equip, learn, assign, party, summon, bounty, inn, camp item) | yes, immediately |
| Settings change | the profile's settings.json, not the slot (GG-T99) |
| Story crawl first shown | yes |
| Battle end (victory, defeat, retreat, escape) | yes, after `BattleFactory.Commit` |
| Save & Quit | yes |
| Mid-battle | no (battle state is not saved; quitting mid-battle loses the battle only) |

**Amended 2026-10-20 (GG-T146):** `SaveStore.Write` never throws. A failure at any step (disk full, a locked file, permissions) is caught, logged as `[Save] slot N write failed: …`, best-effort-cleans the `.tmp` file, and is reported to the caller via a `bool` return instead of losing progress silently — mirroring how `Load` reports a bad read with a log plus a `null` sentinel rather than an exception. `GameApp.Persist` shows the "Couldn't save!" toast whenever `Write` returns `false`. *(Tests: `SaveStoreTests.Write_WhenTheTempFileCannotBeWritten_ReturnsFalseInsteadOfThrowing`, `Write_WhenTheReplaceStepFails_ReturnsFalseAndLeavesThePreviousSaveIntact`.)*

### 15.3 Ability bar and Bestiary

- `HeroSave.Known` lists learned ability ids; `HeroSave.Bar` the five bar entries. The battle reads the bar through `BattleFactory.SpecFor`. *(Tests: `Save_round_trips_through_json`, `Full_loop_battle_commit_then_every_vendor`.)*
- Bestiary: **Seen** for every class spawned — recorded at commit even on defeat; **Defeated** counts kills on victory. **Amended 2026-11-06 (owner, GG-T290):** ~~**Scanned** for Scan targets~~ — Scan is retired; what the party knows of a class is `BestiaryKnowledge.Tier(entry)` (Meta, `SaveData.cs`), read from `Defeated` alone: Seen → name, description, lore and drops; **1** defeat → Speed; **3** → HP and base stats; **6** → elemental weaknesses and resistances; **10** → abilities, brain and targeting (`DefeatsForSpeed/Stats/Elements/Abilities`). An old save's `Scanned` flag is kept and counts as at least the stats tier. *(Tests: `BestiaryKnowledgeTests`.)*

### 15.4 Player profiles

**Added 2026-11-02 (owner, GG-T99; reverses the "settings are per save slot" amendment).** Profiles come from the shared `com.mindattic.unity.shared` package (`Packages/manifest.json` → `file:../../Unity.Shared/unity/com.mindattic.unity.shared`; its engine-free `Unity.Shared.Core` is referenced by `GridGame.Meta` and compiled into the dotnet test build):

```
persistentDataPath/Profiles/
├─ current.txt                 the profile in use
├─ <Name>/settings.json        {"format":"mindattic.settings","version":1,"common":{…},"game":{…}}
│   common: musicVolume, sfxVolume, muteMusic, muteSfx, reduceMotion, language   (every MindAttic game)
│   game:   battleSpeed, dragTimer, colorblindMode, pincerCutIns, confirmActions,
│           menuTheme, fontName, difficulty{level}   (1..10, default 10; §22.9)
├─ <Name>/Saves/slot1-3.json   that profile's save slots (§15.1)
└─ .trash/<Name>~<UTC>         deleted profiles, moved aside, never erased
```

- `Meta/PlayerProfiles.cs`: `GaidenSettings` maps `SettingsSave` ↔ settings.json (unknown fields are kept; the CLI `-muted` session mute is never written back); `ProfileSession` is the active profile + its settings (switch / create / rename / delete; switching reloads settings from the new profile's file); `SaveFiles` is the slot file shape shared by `SaveStore` and the tests.
- **Settings follow the profile, not the slot:** `GameApp.Settings` is the active profile's, applied at boot (so the Title already wears the player's theme, font and volumes) and the same whichever slot is loaded. The Settings screen (reachable from the Title as well as Camp) edits it and writes settings.json on every change.
- **Title:** a **Profile** row shows the current profile; it opens the picker (`ProfileSelectScreen`: tap to switch; Rename; Delete with a confirm; **+ New profile**), and `ProfileNameScreen` takes a name (create — which also switches to it — or rename), refusing names that can't be a folder on every platform or already exist. Deleting the last profile leaves a fresh `Default`. Code-built like every other screen.
- **Migration** (`LegacySaveMigration`, once, first launch): `persistentDataPath/Saves/slot*.json` are copied into the `Default` profile (a slot it already has is never overwritten), the most recently played slot's (by its `UpdatedUtc`) old `Settings` block seeds `Default/settings.json`, and the old folder is renamed `Saves.migrated`. Nothing is deleted.
- **Automation:** `-saveRoot DIR` keeps every profile under `DIR/Profiles` (the harness passes its run folder, so harness and tests never touch real profiles); Unity.Shared's `-profiles-root DIR` / `MINDATTIC_PROFILES_ROOT` name the folder outright; `-profile NAME` picks (or creates) a profile; `-demo` and `-testmode` play under a throwaway `Throwaway` profile in a temp-cache folder cleared at launch. The automation endpoint has `profile?op=list|switch|create|rename|delete`, `goto` knows `profiles` and `newprofile`, and `python tools/harness.py profiles` shoots the picker, the name screen and Settings and checks settings follow the profile.

*(Tests: `ProfileTests` — `Migration_moves_every_legacy_slot_into_the_Default_profile_and_keeps_the_old_folder`, `Migration_seeds_settings_from_the_most_recently_played_slot`, `Migration_runs_once_and_does_nothing_without_a_legacy_folder`, `Migration_never_overwrites_a_slot_the_profile_already_has`, `Each_profile_has_its_own_save_slots`, `Each_profile_has_its_own_settings`, `Switching_profiles_reloads_settings_and_remembers_the_choice`, `A_session_mute_never_reaches_settings_json`.)*

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
| Add an enemy | `classes.json` + a `stages.json` wave (+ `droptables.json`) | `Resources/Portraits/<id>.png` + a `Thumbnail` framed in Thumbnail Studio (§12.1.1) |
| Add an item / recipe | `items.json` / `recipes.json` | `Every_recipe_is_craftable_with_its_inputs` |
| Tweak AI weights | `Core/Rules/EnemyPlanner.cs` | §14.1.2 |
| Add a Camp service | `Meta/MetaServices.cs` + a screen in `App/Screens/HubScreens.cs` + hub row + `StartScreens` id | `MetaLoopTests`, harness sweep list |
| Add a save field | `Meta/SaveData.cs` + `SaveMigrations` (bump `CurrentVersion` when shape changes) | round-trip test |
| Add a battle animation | `BattleScreen.Playback.cs` `Animate` switch | awaited beat, `Fx` for motion |
| Add a sound | `Resources/Audio/Sfx` + `Audio.Cue` | Credits.txt attribution |

---

## 18. Glossary

The L0 [§9 Glossary](#GG-§9) is canonical; additional terms: **Armored** (snake member taking 0 while a later member lives) · **Escape** (Smoke Bomb exit; wounds kept) · **Control return** (end of an action: hero buffs/cooldowns and enemy Frozen tick) · **Interrupt outcomes** (*Clutch* → resolve now, *Shrug* → no delay, *Stagger* → delay accumulates, *Cancel* → delay ≥ cast time, orbs lost) · **Passive** (always-on class ability: Double Attack, Counter Attack) · **Sentient** (an enemy with a society or that plans ahead — may be a pincer attacker; heroes always; replaced Humanoid 2026-11-06, GG-T289) · **Coins** (gold picked up in battle, banked on victory) · **Materials** (crafting inputs) · targeting axes **Shape / Mode / Filter** ([§5](#5-targeting)).

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

1. Add a `ClassDef` to `classes.json` with `Team: "Enemy"`, `Tags` (`Sentient` if it has a society or plans ahead and so may pincer — vampires and mummies yes, mindless undead and beasts no, GG-T289/GG-T294), `BaseStats`, `Growth`, `Resistances`, `XpReward`, `MoveRange`, optional `DropTable`, `ChargeAbility`/`ChargeShape`, `Trap`, `BossPhases`, `Footprint`, `SnakeSegments`, `Lore`.
2. Add `Resources/Portraits/<id>.png` as a **1024×1024 transparent canvas**, never trimmed or shrunk (owner 2026-11-07: "all characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the assets are", [§12.1.2](#1212-portraits-are-1024-canvases-added-2026-11-07-owner-rule-gg-t352)). Run `tools/import_art.py`, or for owner art on a white background `python tools/portrait_cut.py <image> --import <id>`, which erases the background and keeps white cloth and translucent smoke (GG-T327). Either one writes the canvas and the class's `"Figure"` box (add the class first, or copy the box it prints). Give the class `"Portrait": "<id>"` so a later `import_art.py` run leaves it alone, and give it a `Thumbnail`: frame it in Thumbnail Studio (`tools/thumbnail-studio.ps1`, [§12.1.1](#1211-thumbnails-are-a-window-onto-the-portrait-added-2026-11-03-owner-direction-gg-t220)); there is no separate thumb image.
3. Put it in a `stages.json` wave; optionally add a `droptables.json` table.
4. `pwsh tools/test.ps1` — validation, catalog and soak include it automatically. The Bestiary lists it (silhouette until seen).

### 20.1 Worked example: "FrostWolf"

A Beast `{"Id":"frostwolf","Team":"Enemy","Tags":["Beast"],"Resistances":{"Ice":0.0,"Fire":2.0}, "XpReward":25,"MoveRange":1,"DropTable":"wolf00", …stats}` never pincers; `EnemyPlanner` closes distance and melees; coins default to ~½ XP; the Bestiary shows it as a silhouette until seen.

---

## 22. The Macro Loop

```
Title ─▶ (Continue | New Game → slot → CharacterSelect (pick 3) → Prologue crawl | Load Game → slot)
   ▼
Camp (HubScreen)  ◀══════════════════════════════════════════╗
   │  Adventure ─▶ stage list (newest-first)                 ║
   │  Crucible ─▶ endless waves, keeps everything (§22.10)   ║
   ▼                                                         ║
StoryCrawl (first entry into a theme only)                   ║
   ▼                                                         ║
Story beats (dialogue; a finale's boss intro; §27.1)         ║
   ▼                                                         ║
Battle — clear every wave                                    ║
   ▼                                                         ║
LevelUp — one hero at a time, only when someone leveled ═════╣
   ▼                                                         ║
PostBattle — gold, items, unlocks, bounty ════════════════════╝
             (Continue → [level interlude] → Camp · Next stage › → Battle · Retry → Battle)
             (first win over the Hierarch: Continue → Ending crawl → rolling Credits → Title)
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
- Always: deduct consumed items; record Seen (Scanned retired 2026-11-06, GG-T290).
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
- **Stage names are "<Theme> I / II / III"** (e.g. "Tithe Moors I", "Shardfall III").
- **Row content:** ★ cleared / ☆ not yet, the name, "Lv N" (recommended level), and a second line "<Theme> · N waves · drops: X, Y, Z" from the wave enemies' drop tables (first 3 distinct items) — the farming hint.
- **Unlock gating:** stage index i is unlocked when `i ≤ HighestClearedStage + 1` (`Progression.IsStageUnlocked`): linear forward, fully open backward; every unlocked stage is replayable.
- **Farming is the point:** cleared stages stay replayable and drop tables differ per enemy.
- A strip above the list shows the active bounty's progress.

**Difficulty curve:** `Progression.RecommendedLevel(i) = i + 1`; spawned enemy levels are `max(authored, recommended)`; test stages are exempt. XP and coins scale with enemy level. The player's difficulty dial ([§22.9](#229-difficulty-settings-scaffolding-added-2026-10-11-owner-direction)) then scales the enemy side down from the designed 10 and the rewards with it.

**Amended 2026-11-05 (GG-T261, POL-G1/G2):** the casual-bot curve is measured by `python tools/polish.py --balance` (casual bot, rotating five-hero parties at the stage's level). Before: Anchor Wastes I–III and Halcyon I–III won 0 %, Tithe Moors III 0 %, Tithe Moors II and Shardfall II 100 % — the zeros were the remains rule (§14.5), not the waves. After the remains fix, wave trims where losses clustered and additions where it was a walk-over — Shardfall I wave 5 (−1 Mecha Armor), II waves 1/4 (+Scorpion, +Soldier), III waves 2/4 (+Tech Gremlin, +Vulture); Arkgrave I wave 5 (Hag → Lurker); Tithe Moors III waves 2/3 (+Wolf, +Ganger); Anchor Wastes I waves 3/4 (a Goblin Thug for one Ceramic Knight and for the Troll), II wave 3 (+Goblin Thug), III waves 2/5 (a Goblin Thug for one Ceramic Knight, −Ice Mauler; the Cyclops' GravityStamp stays); Halcyon II wave 1 (−Bat), III waves 1/2/4 (the two-undead Flank group, one Bat, −Undead) — every main stage now wins 25–100 % with 8 rotating parties (most 50–88 %; party make-up swings a stage ±25 %). POL-G1 17.4 → 31.0, POL-G2 71.4 → 78.6.

Campaign (`campaign.json`): Tithe Moors, The Lantern Road (GG-T345, §22.3.2), Shardfall, Arkgrave, Anchor Wastes, The Still (GG-T344, §22.3.1), Halcyon, The Choir's Cathedral (GG-T350, §22.3.3) — 3 stages each, **5 hand-authored waves per stage** ([§22.7](#227-the-first-battle), amended 2026-10-12 — every stage normalized to 5, including Tithe Moors I, which had been 20), generally grouped by enemy family (frogs with frogs, goblins with goblins) rather than mixed. **No two campaign waves are the same encounter** (*added 2026-10-22, GG-T184*): the 19 waves that repeated an earlier wave's foes now name an `encounters.json` group of the same family instead (`WaveDef.Group`), and `ContentLoader.Validate` fails on any repeat. *(Tests: `EncounterCatalogTests`.)* Final boss: the Hierarch (its own enemy class `hierarch` since GG-T350, The Choir's Cathedral III; it spawns at the stage's recommended 24). ~~(`vampire`, level 10 authored, Halcyon III)~~ **Added 2026-11-05 (GG-T234):** every main-biome finale (each theme's third stage) is fought against a **rule-breaking boss** ([§14.9](#149-rule-breaking-bosses-added-2026-11-05-owner-direction-gg-t234)): the Captain (TwinTurn), an oni (Undying, The Lantern Road III, GG-T345), Mecha Armor (AxisWard), Swamp Mistress (MireSwap), Cyclops (GravityStamp), the Chronomancer (TwinTurn, The Still III, GG-T344), a Kiln Guard captain (AxisWard, Halcyon III, GG-T350) and the Hierarch (Undying, The Choir's Cathedral III, GG-T350).

#### 22.3.1 The Still (added 2026-11-07, owner direction, GG-T344)

Owner, with six new landscapes: *"I will generate more landscapes from which you can extrapolate more story, settings levels, etc"*. The six images (a vast white Concord dome inside: aether tanks glowing cyan, a moon-oculus, mirror-polished floors, rails and catwalks, arched gates) became **The Still** (`still`), a sixth main theme between the Anchor Wastes and Halcyon — the great Still at the foot of Halcyon's spire where every Anchor beam's tithe is collected into tanks before it climbs to the Hierarch ([STORY.md](STORY.md) §5.5).
- **Campaign:** `campaign.json` order is now Tithe Moors, Shardfall, Arkgrave, Anchor Wastes, **The Still**, Halcyon, then the five postgame themes. Milestone reward `tome_mana_siphon`. Backgrounds: the owner's six landscapes as `Backgrounds/still_01..06.jpg` (`"Backgrounds": "still"`). Three stages, five waves each, like every main theme: **The Still I** (`still-01`, the queues: three Hollow; the Bulwark gate guard, Shieldbearer and Rifleman; two Chromed Hollow and a Hollow carrying the cans; a Red Seal warden with a Chromed Hollow; a Choir Confessor and two Hollow at the head of the line), **II** (`still-02`, the tank hall: two Chromed Hollow and a Hollow; Cantor and Confessor; Siphoner, Shieldbearer and a Hollow; two vat-giants grown in the tanks, `cyclops03/04`; a Psalmist, a Chromed Hollow and a Hollow), **III** (`still-03`, under the oculus: two Hollow and a Chromed Hollow; the Siege Gunner and a Rifleman; Cantor and Psalmist; a vat-giant and a Hollow; then the **Chronomancer** with TwinTurn beside a Shieldbearer and a Confessor, §14.9). Board tile `Tiles/still.png` (`tools/make_tiles.py still`, one polished white slab with pooled aether); the tanks stand on the board as blocks.
- **Levels:** `RecommendedLevel(i) = i + 1` puts the Still at 13–15 (the Bulwark's and Choir's Crucible band) and moves Halcyon from 13–15 to 16–18 (the Hierarch fights at 18). *Since GG-T345 (§22.3.2) both are three higher: the Still 16–18, Halcyon 19–21.*
- **Story beats:** the crawl, `still_party_talk` (`ThemeStart`, the Enchantress on the queues), `boss_intro_still` (`StageStart still-03`: the Chronomancer shows them tank nine — a whole hold, Fennick's Rise, drowned and turning — and the crew learns the Hollow aren't empty, they're *stored*), and a side quest, **The Hymn Stops Here** (`q_hymn_stops`, the Enchantress: defeat three Choir Cantors).
- **Balance** (24 seeds, campaign parties of six, difficulty 10, `AbilityUsageSoak` with `GAIDEN_ABILITY_STAGE`): casual **14/24, 20/24, 15/24** (58–83 %), smart **24/24** on all three. The first cut had two Templar pairs and a Templar beside the Choir and measured casual 6, 7 and 5/24; the losses clustered at those waves, so the Red Seal thinned to one warden per wave. Halcyon at 16–18 instead of 13–15 measured casual 6, 10 and 20/24 against 9, 15 and 19 before, with losses at the same waves (Halcyon I wave 3, Halcyon II wave 5). One bat came off each of those waves, and Halcyon now measures **8, 19 and 20/24**, smart 24/24.
- **Old saves:** `HighestClearedStage` is a campaign index, so save **v5** moves any save already past the Anchor Wastes up by three (`SaveMigrations.StillInsertedAt` 12, `StillStageCount` 3): the same furthest clear, the Still open behind it.
- **Tools:** the ability-usage and timeline soaks play the main campaign (every campaign stage before the first `LevelOverride`) instead of "the first 15", and `GAIDEN_ABILITY_STAGE` takes a comma list.
- *(Tests: `StillTests`; `StoryArcTests` and `RuleBreakTests` list six finales; `QuestTests` wants a quest in every main theme.)*

#### 22.3.2 The Lantern Road (added 2026-11-07, owner direction, GG-T345)

Owner, with four new landscapes: *"I will generate more landscapes from which you can extrapolate more story, settings levels, etc"*. The four images (a lantern-lit pilgrim road climbing mountain gorges at night: stone stairs, shrines, torii, bamboo, rope lines of paper lanterns, a low full moon) became **The Lantern Road** (`lanternroad`), a seventh main theme between the Tithe Moors and the Shardfall: the crew climbs the pilgrim road out of the moors toward the desert ([STORY.md](STORY.md) §5, a structural entry only).
- **Campaign:** `campaign.json` order is now Tithe Moors, **The Lantern Road**, Shardfall, Arkgrave, Anchor Wastes, The Still, Halcyon, then the five postgame themes (main campaign 21 stages, 31 in all). Milestone reward `tome_steadfast`. Backgrounds: the owner's four landscapes as `Backgrounds/lanternroad_01..04.jpg` at their native 768×1536, JPEG q95 (`"Backgrounds": "lanternroad"`). Board tile `Tiles/lanternroad.png` (`tools/make_tiles.py lanternroad`: one moonlit blue-grey flagstone, moss, a warm lantern pool). Three stages, five waves each, existing units only (the oni road-raiders, the gorge's moon-changed Wild, a little Redline and militia on the caravan's trail): **The Lantern Road I** (`lanternroad-01`: two wolves; three bats; two oni; two steppin razors; an oni with a wolf and a bat), **II** (`lanternroad-02`, two stone lanterns as blocks: a flying monkey and a bat; two steppin razors and a wolf; two oni; a werewolf with two wolves; a Redline ganger and a militia soldier), **III** (`lanternroad-03`, two blocks: two bats and a flying monkey; two steppin razors; a wolf and a werewolf; an oni and a steppin razor; then the **oni chief** `oni02` with **Undying** beside an oni and a wolf, §14.9).
- **Levels:** `RecommendedLevel(i) = i + 1` puts the Lantern Road at 4–6 and moves every later chapter three levels: the Shardfall 7–9, the Arkgrave 10–12, the Anchor Wastes 13–15, the Still 16–18, Halcyon 19–21 (the Hierarch fights at 21). Postgame stages keep their `LevelOverride`.
- **Story text is placeholder draft.** The owner ruled that story is authored in Prose, not in this repo (Prose RFC 0017, GG-T355), so the chapter carries only short functional text: the crawl, `lanternroad_party_talk` (`ThemeStart`, the Lantern Merchant on the road), `boss_intro_lanternroad` (`StageStart lanternroad-03`) and a side quest, **Masks Off** (`q_masks_off`, the Lantern Merchant: defeat three oni, so `QuestTests` still finds a quest in every main theme). All of it is to be replaced by approved Prose text.
- **Balance** (24 seeds, campaign parties, difficulty 10, `AbilityUsageSoak` with `GAIDEN_ABILITY_STAGE`): the Lantern Road casual **19/24, 16/24, 15/24**, smart **24/24** on all three. The first cut measured casual 19, 12 and 6/24 with losses spread over the oni and steppin-razor waves, so Lantern Road II lost a wolf from its oni wave and a Redline ganger from wave 5, and III thinned waves 2–5 by one foe each. Every later main stage re-measured (casual wins of 24, before → after; smart stayed 23–24/24 everywhere): Shardfall 18/14/13 → 15/13/14, Arkgrave 10/4/6 → 10/5/4, Anchor Wastes 14/6/4 → 12/8/7, the Still 14/20/15 → 11/18/17, Halcyon 8/19/20 → 13/18/20. The insertion also moves every later stage's seeds and rotating parties, so about ±3 of 24 is noise. Two light trims where the shift pushed a stage below where it was: Shardfall III wave 5 lost a soldier (11 → **14**/24) and Arkgrave II wave 4 lost its toad (1 → **5**/24). Already low before this change and left to a balance pass: Arkgrave II–III and Anchor Wastes II–III (casual 4–8/24) and Tithe Moors I (6/24, smart 22/24).
- **Old saves:** save **v6** moves any save already past the Tithe Moors up by three (`SaveMigrations.LanternRoadInsertedAt` 3, `LanternRoadStageCount` 3), after v5's Still shift: the same furthest clear, the Lantern Road open behind it.
- **Armor sets:** every set from level 4 up moves three levels (§24.2.3); the top set, Dreadwyrm, tracks the 31-stage campaign.
- *(Tests: `LanternRoadTests`; `StoryArcTests` and `RuleBreakTests` list seven finales; `BackgroundsTests` expects the four-image `lanternroad` set; `StillTests` follows the Still's new levels and index.)*

#### 22.3.3 The Choir's Cathedral (added 2026-11-07, owner direction, GG-T350)

Four owner landscapes (a cathedral nave at night: an organ of aether pipes glowing cyan, rose windows, pews by the hundred, a moon in the vault) became **The Choir's Cathedral** (`cathedral`), the main campaign's eighth theme and **new finale**: the cathedral at the top of Halcyon's spire. The organ is the Hierarch's reservoir; the Choir sings the tithe up into it ([STORY.md](STORY.md) §5, a structural entry only).
- **Campaign:** `campaign.json` order is now Tithe Moors, The Lantern Road, Shardfall, Arkgrave, Anchor Wastes, The Still, Halcyon, **The Choir's Cathedral**, then the five postgame themes (main campaign 24 stages, 34 in all). Milestone reward `tome_counter_attack`. Backgrounds: `Backgrounds/cathedral_01..04.jpg` at their native 768×1536, JPEG q95, fresh-GUID metas (`"Backgrounds": "cathedral"`; mean luminance 0.29). Board tile `Tiles/cathedral.png` (`tools/make_tiles.py cathedral`: one dark marble slab, pale veining, a pool of cyan organ light); the pews stand on the board as blocks. Three stages, five waves each, the Choir and the Concord only (two foes a wave, a Chromed Hollow as the lighter half): **I** (`cathedral-01`: a Cantor and a Chromed Hollow; a Kiln Guard and a Chromed Hollow; a Red Seal templar and a Chromed Hollow; a Bulwark Rifleman and a Chromed Hollow; the Siege Gunner and a Psalmist), **II** (`cathedral-02`: a Psalmist and a Chromed Hollow; a Siphoner and a Chromed Hollow; a Kiln Guard and a Cantor; a templar and a Chromed Hollow; a Shieldbearer and a Confessor), **III** (`cathedral-03`: a Cantor and a Chromed Hollow; a Kiln Guard and a Chromed Hollow; the Siege Gunner and a Confessor; a templar and a Chromed Hollow; then the **Hierarch** with **Undying** beside a Cantor and a Confessor, §14.9).
- **The Hierarch** is its own enemy class, `hierarch` (STORY.md §3.1), so the playable `vampire` hero is no longer fielded as the boss. Kit, all existing systems: charge **Tithe Draught** (`charge_tithe_draught`, 2.5 s, rests a turn: 5 Dark to every hero, and it heals for each hero it touches — a drain through the organ; interrupt it), boss phase at half HP **Open the Reservoir** (`hierarch_open_reservoir`, free and enemy-only, in `Abilities.EnemyOnly`: heals 25, Empowered, a 0.35 haste on its own icon; it prefers its charge from then on), **Vampire's Kiss** lifesteal on every landed blow, and the **Undying** rule-break (it rises once at half HP while its Choir still stands — silencing the Choir is how the party breaks the reservoir). Mesmerize support (Slowed, reach 2 like every boss). Drop table `vampire`; the Hierarch bounty (`bounty_vampire_lord`) now targets `hierarch`.
- **No portrait yet** (owner 2026-11-07, GG-T351: missing art uses the mannequin). The class has no `Portraits/hierarch.png`; it declares `"Portrait": "mannequin"` and carries the mannequin's own `Thumbnail` and `Figure` values, so validation (every class needs both, GG-T352) passes and every view frames the fallback texture exactly as the mannequin's own class does (`Art.FigureRect` reads the box of the class the texture belongs to). `CatalogTests` accepts a class that declares the mannequin placeholder in place of its own portrait file, checks it copies the mannequin's framing, and fails once real art lands so the placeholder is dropped. Dialogue speakers name it with `"Class": "hierarch", "Portrait": "mannequin"`.
- **Halcyon III** keeps its waves; its finale now ends on a **Kiln Guard captain** (`ceramicknight06`, AxisWard) beside a Hollow, holding the spire stair (the Reaper that stood beside the Hierarch came off for balance). `boss_intro_cityruins` is his (placeholder draft); the Hierarch's own lines moved verbatim to `boss_intro_cathedral`. The `Ending` ("Still Here") follows `cathedral-03` and uses the cathedral backdrop.
- **Levels:** the Cathedral runs at 22–24 (the Hierarch fights at 24); nothing before it moves. Postgame stages keep their `LevelOverride`.
- **Story text is placeholder draft** (story is authored in Prose, GG-T355): the crawl, `cathedral_party_talk` (`ThemeStart`, the Enchantress on the organ), the Kiln Guard's intro and a side quest, **Quiet the Organ** (`q_organ_quiet`, the Enchantress: defeat three Choir Confessors).
- **Balance** (24 seeds, campaign parties, difficulty 10, `AbilityUsageSoak` with `GAIDEN_ABILITY_STAGE`, measured on top of the Herald, Aetherist and Tactician heroes, GG-T333/336/337): the Cathedral casual **17/24, 19/24, 16/24**, smart **24/24** on all three. The first cut (three-foe Concord waves, Kiln Guard pairs) measured casual 4, 0 and 3/24 (smart 24/24/23); every wave went to two foes with a Chromed Hollow as the lighter half, and Cathedral I's templar lost its Confessor. Halcyon re-measured, casual before → after: 9/17/15 → 9/17/16, smart 24/24 throughout. Halcyon I and II are unchanged content on unchanged seeds; III with the Kiln Guard captain beside the Reaper first measured 12/24 (losses at that wave), so the Reaper came off it. Halcyon I at 9/24 casual was low before this change (the new heroes moved the soak's party rotation) and is left to a balance pass.
- **Old saves:** save **v7** moves any save already into the postgame up by three (`SaveMigrations.CathedralInsertedAt` 21, `CathedralStageCount` 3); a save whose furthest clear is Halcyon III finds the Cathedral next. The `story:ending` flag is left alone, so a save that already saw the ending at Halcyon does not see it again at the Cathedral (New Game+ replays it as before).
- **Armor sets:** the campaign grows to 34 stages; Wispweave stays at 22 and the top four spread over the new range (Templar's 24 → 25, Reaper's 26 → 28, Sanguine 28 → 31, Dreadwyrm 31 → 34) so no stretch goes more than five levels without a set (§24.2.3).
- **Tools:** the harness's finale run (`tools/harness.py`) launches `cathedral-03` and checks `boss_intro_cathedral`.
- *(Tests: `CathedralTests`; `StoryArcTests` and `RuleBreakTests` list eight finales and the ending follows the Hierarch's stage; `BackgroundsTests` expects the four-image `cathedral` set; `CatalogTests` accepts the mannequin placeholder; `QuestTests` allows the eighth main-theme quest.)*

### 22.4 Bounty board

**Amended 2026-10-09:** a Camp screen (`BountyScreen`, "Bounties"), not a StageSelect strip. Posted = every `bounties.json` contract not yet claimed (claimed ones never repost). One active slot (`Bounties.Accept` refuses while one is active); kills of the target class count **on victory only** (in the Crucible, on any ending — [§22.10](#2210-the-crucible)), capped at the goal; **Claim** pays gold + item, plus an optional skill-point payout (`BountyDef.RewardSkillPoints`, *added 2026-11-02, GG-T134*) that every hero in the active party gains; **Abandon** resets. The camp routine prefers a skill-point contract when two finish equally soon, and the balance table reports the SP bounties paid. *(Test: `Full_loop_battle_commit_then_every_vendor`.)*

| Bounty | Target | Kills | Reward |
|---|---|---|---|
| Slime Culling | slime00 | 5 | 200 G + Healing Potion |
| Wolf Pack | wolf00 | 3 | 300 G + Tome of Berserk (GG-T301; was a Hi-Potion) |
| Shrouds on the Road | undead00 | 4 | 400 G + Holy Water |
| Vat-Giants | cyclops00 | 2 | 500 G + Tome of Tough Hide (GG-T301; was Flame Oil) + 1 SP each |
| The Hierarch | vampire | 1 | 2000 G + Revival Charm + 2 SP each |

### 22.11 Side quests (added 2026-11-05, owner direction, GG-T237)

Owner: "optional quests and bounty board … make it a complete experience." Optional story errands, separate from the bounty board: any number may run at once, none ever gates the campaign, and they live in Camp's **Quests** journal (`QuestScreen`, start id `quests`).

- **Content:** `quests.json` (`QuestDef`: `Id`, `Name`, `Giver`, `Theme`, `Unlock` stage, `Summary`, `TurnIn` line, `Objective`, rewards `RewardGold` / `RewardItem` × `RewardItemCount` / `RewardSkillPoints` (each active-party hero) / `RewardFlag`). Objectives reuse what the save already tracks: **ClearStage** (optionally `MaxHeroes` — at most N of your own heroes in the fight — and `NoneFallen`), **Defeat** N of an enemy class (counted after accepting), **Bring** N of an item (read from the bag, handed over on turn-in), **Flag** (a save flag). `ContentLoader.Validate` (`QuestCatalog.Validate`) checks every reference and that **some dialogue offers each quest** (a `StartQuest` effect).
- **Given in conversation:** each quest has a Camp Talk dialogue (`quest_offer_<id>`, with an NPC or `"party"`) gated on `QuestAvailable` — its `Unlock` stage is cleared and it is neither under way nor done — whose accept choice carries `StartQuest`. "Not now" loses nothing: once the offer has been heard the journal lists it under **Heard of** with an **Accept** button. Quests not heard yet are listed under **Ask around** with who to talk to. A `RewardFlag` opens a follow-up Talk (`quest_after_grown_not_forged`, the Blacksmith on what the grown cogs mean).
- **Progress** (`Meta/Quests.cs`): `Quests.RecordBattle` runs in `BattleFactory.Commit` next to the bounty, **on stage victories only** (like the bounty board; the Crucible doesn't count). The results screen shows a "Quest" row per quest it moved. Bring and Flag objectives read the save live.
- **Journal:** sections **Ready to hand in!** (Hand in), **Under way** (Set aside — progress lost, can be taken up again), **Heard of** (Accept), **Ask around**, **Done**. The Camp row reads "! N to hand in" when something is ready, else "N under way"; it appears once any quest is unlocked. Accepting plays `quest_accept`; handing in plays the `quest_complete` fanfare and slides in a card with the giver's thanks and the reward (`.quest-turnin`, eased in, no single-frame swap).
- **Bots:** Test Mode, the balance sim and `CampRoutine` never take quests (no Talk is played headlessly), so they never affect balance.
- **Save:** `SaveData.Quests` (`QuestSave`: `Active` id → progress, `Done`); older saves migrate to an empty journal.

| Quest | Giver | Biome · offered after | Objective | Reward |
|---|---|---|---|---|
| Wolves at the Wells | Innkeeper | Tithe Moors · stage 1 | Defeat 5 Wolf | 250 G + Hi-Potion |
| What the Frogs Know | Merchant | Tithe Moors · stage 1 | Bring 3 Frog Gland | 300 G + 2 Mana Potion |
| Travel Light | Party | Tithe Moors · stage 2 | Clear Tithe Moors III with ≤ 3 heroes | 200 G + Tome of Steadfast + 1 SP each |
| Grown, Not Forged | Blacksmith | Shardfall · stage 1 | Bring 4 Gremlin Cog | 350 G + Tome of Mana Siphon + follow-up talk |
| Antivenom | Alchemist | Shardfall · stage 2 | Defeat 4 Scorpion | 300 G + 2 Hi-Potion |
| The Hag's Hand | Alchemist (after Antivenom) | Arkgrave · stage 1 | Bring 3 Hag Claw | 400 G + Revival Charm |
| Every Voice | Enchantress | Arkgrave · stage 2 | Clear Arkgrave III, nobody falls | Hi-Ether + 1 SP each |
| Furs for the Long Cold | Innkeeper | Anchor Wastes · stage 1 | Bring 3 Yeti Fur | 500 G + 2 Tent |
| Cold Iron | Blacksmith | Anchor Wastes · stage 2 | Defeat 3 Ice Mauler | 600 G + Tome of Counter Attack + 1 SP each |
| The Ones Who Paid | Party | Halcyon · stage 1 | Defeat 5 Ghost | 700 G + 3 Holy Water + 1 SP each |

*(Tests: `QuestTests` — content valid and spread over the five biomes, an orphan quest rejected, offer gated on the unlock stage and started by the conversation, "Not now" kept in the journal, Defeat counts victories only and pays once, ClearStage's party-size and nobody-falls terms, Bring takes only the asked count and a reward flag opens the follow-up, old saves migrate.)*

---


### 22.6 Battle records (added 2026-10-11, owner: "record everything so you can learn how to balance and synergize, keep the records of all moves as JSON so that we can build ML around it in the future")

`BattleRecorder` (Core) writes one JSON document per battle, schema v3 (v2 added per-turn skill level / perfect command; v3, GG-T98, added formation-time footprints on PincerFormed and `SpawnedAt` on wave/reinforcement events): `source` (player / bot:greedy / bot:casual / bot:autoplay / player+autoplay), stage, seed, waves, the party (class, role, level, stats, gear, bar), the opening state and events, then every turn — the full state **before** (units with position, HP, buffs, cooldowns, bar, brain, timeline position, casts; pickups, traps, blocks, amplifier, bank, mana charge), the **command**, whether it was accepted, **every event** it produced (type + fields), and a summary after — and finally the outcome (phase, wave reached, actions, coins, XP, kills, items used, heroes standing).
- The game saves every battle (victory, defeat, retreat) to `persistentDataPath/battle-records/` (or `<artifacts>/records` under the harness, or `-records <dir>`).
- `RecordTests.Record_soak_of_the_first_battle` with `GAIDEN_RECORDS=<dir>` writes bot-played battles; `tools/records.py` prints per-wave tables (turns, damage taken, deaths, potions, pincers, chains, mana, pickups, taunts), each role's damage/healing, damage taken by enemy brain, and the most-used abilities. The original 20-wave first battle (since compressed to 5, [§22.7](#227-the-first-battle)) and several rules (skirmisher dart, last stand, taunt valuation, hard-won experience) were tuned from these tables.
- **Per-ability usefulness (GG-T246):** `python tools/records.py --abilities <dir>` credits each hero ability with what its resolutions did. Effects are attributed from an `AbilityResolved` (or the `ItemUsed` before it) up to the next action, turn, pincer, attack or wave, so a cast-time spell is credited when it lands. It reports uses, damage, healing, kills, status and tempo/movement effects and an impact score per use, the abilities that sat on a bar but never fired, the weak ones, and the mana flow (orbs banked, overflowed and spent). `AbilityUsageSoak` (env `GAIDEN_ABILITY_RECORDS=<dir>`, `GAIDEN_ABILITY_ROUNDS`) feeds it every hero class in rotating parties of five across the first 15 campaign stages at their recommended levels. Findings: `docs/research/AbilityUsage.md`.

### 22.7 The first battle (amended 2026-10-12, owner: superseding "make the first battle 20 rounds" — every stage, including this one, normalized to 5 waves)

Tithe Moors I is **5 waves**, levels 1 → 5, each wave outweighing the one before it (test `The_first_battle_has_five_waves_of_rising_difficulty`): an ooze opener (slimes), a beast wave (wolves, bat), a frog/toad wave, a goblin-and-bandit skirmish that a captain's soldier squad **reinforces mid-fight** — the flagship example of the delayed-spawn/surprise mechanic ([§14.8](#148-reinforcements-and-eggs-added-2026-10-12-owner-direction)) — and a captain's-squad finale.

**Amended 2026-10-19 (GG-T118, rebalanced for the three-hero start):** the reinforcement is one soldier, not two (wave 4 is five foes, still heavier than the frog wave), and the finale's captain and two soldiers are level **4**, not 5, so levels now run 1 → 4. Measured on Tithe Moors I alone, 24 seeds, single attempt: the casual bot (randomness 0.5) won 41% with 22% HP left with the old five-hero start; the trio on the old waves won 16% / 11%; probes of the trio with a Lv 4 finale gave 45% / 31%, and with a four-foe wave 4 as well 54% / 37%; this cut sits between them, back at the old five-hero difficulty or a little kinder. The greedy bot wins 100% either way. In `BalanceReport` (up to 4 attempts) the casual bot now clears it 80% of attempts with 61% HP left (five-hero start before: 66% / 48%; trio on the old waves: 30% / 26%).

**Amended 2026-11-05 (GG-T261/GG-T262):** after the remains countdown (§14.5), `EnemyHpScale` 0.45 and strikes at 1.2 (§14.6), Tithe Moors I is unchanged in content and the casual bot (`polish.py --balance`, 8 rotating five-hero parties) clears it 75 % of the time with 59 % HP left, waves a median ~20 player actions (was 38).

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

**Amended 2026-11-06 (owner, GG-T309): one dial, 1–10, starting at 10.** Owner: "This is a difficulty question, and
the higher the difficulty the greater the rewards. On a 1-10 scale you would start on 10 and let players adjust the
game if they feel it is too easy or hard." The Easy/Normal/Hard/Custom preset and its six inert multipliers (below,
superseded) are gone.

- **Where it lives.** `SettingsSave.Difficulty.Level` (`DifficultySettings`), a **profile** setting in settings.json
  (`game.difficulty = {"level": N}`, [§15.4](#154-player-profiles)), so every save slot of the profile plays it. New profiles
  start at **10**. It applies from the **next battle** (`BattleFactory.Build(…, difficulty)` copies it into
  `BattleSetup.Difficulty` → `BattleState.Difficulty`); a battle in progress keeps the level it began at.
- **One table.** `Core/Battle/DifficultyTable.cs` (`DifficultyTable.Rows`), validated by `ContentLoader.Validate`
  (`DifficultyTable.Problems`): ten rows in order, positive multipliers, every enemy column non-decreasing, every
  reward column strictly rising, and level 10's enemy side exactly 1.0 / offset 0. **Level 10 is the designed game**:
  every balance number in this bible is tuned at 10, and lowering the dial only ever softens it.

  | Level | Name | Enemy HP | Enemy damage | Enemy Lv | Enemy speed (GG-T311) | MinGap / MaxSpread (GG-T318) | Rewards (gold, XP, drop chance) |
  |---|---|---|---|---|---|---|---|
  | 1 | Gentle | ×0.55 | ×0.60 | −2 | ×0.60 | 1.60 / 2.00 s | ×0.6 |
  | 2 | Gentle | ×0.60 | ×0.65 | −2 | ×0.65 | 1.55 / 1.90 s | ×0.7 |
  | 3 | Easy | ×0.65 | ×0.70 | −1 | ×0.70 | 1.50 / 1.80 s | ×0.8 |
  | 4 | Easy | ×0.70 | ×0.75 | −1 | ×0.75 | 1.45 / 1.70 s | ×0.9 |
  | 5 | Moderate | ×0.75 | ×0.80 | −1 | ×0.80 | 1.40 / 1.60 s | ×1.0 |
  | 6 | Moderate | ×0.80 | ×0.85 | 0 | ×0.85 | 1.30 / 1.45 s | ×1.1 |
  | 7 | Hard | ×0.85 | ×0.90 | 0 | ×0.90 | 1.25 / 1.35 s | ×1.2 |
  | 8 | Hard | ×0.90 | ×0.93 | 0 | ×0.93 | 1.15 / 1.20 s | ×1.3 |
  | 9 | Very hard | ×0.95 | ×0.97 | 0 | ×0.97 | 1.10 / 1.10 s | ×1.4 |
  | 10 | Intended | ×1.00 | ×1.00 | 0 | ×1.00 | 1.00 / 1.00 s | ×1.5 |

  **Amended 2026-11-06 (GG-T318): the spacing columns.** `MinGapSeconds` / `MaxSpreadSeconds` drive the timeline spacer
  ([§2.12](#212-recovery-and-spacing-gg-t318)), and every row adds a ±0.25 s `JitterSeconds`. Less spacing is more
  pressure, so the validator lets these columns only fall as the level rises. The neutral row (level 0, rules tests)
  has no spacing and no jitter.

- **Party size** (*GG-T321*): independent of the dial, enemy strike and MaxHP also grow ×(1 + 0.30 × heroes beyond the
  starting three), §2.12.
- **What it scales.** Enemy MaxHP and armor plates at spawn (on top of `BattleConfig.EnemyHpScale`); the damage an
  enemy's blow deals a hero (after the hero's own reductions start, min 1); every enemy's turn-icon pace (`EnemySpeed`, GG-T311: slower foes leave wider gaps for the party, §2.0); how far apart foes are spaced on the timeline (`MinGap`/`MaxSpread`, GG-T318, §2.12); every non-test enemy spawn level
  (`EnemyLevelOffset`, never below 1, which also lowers that foe's stats and XP); and per kill the coins, the XP and
  every drop-table entry's chance (capped at 100%), after New Game+'s coin bonus. Test stages (`StageDef.IsTest`) keep
  the enemy side unscaled. Level 0 (`DifficultyTable.Neutral`) is the raw rules with no scaling at all — what a
  hand-built `BattleSetup` (the rules tests) gets.
- **Shown to the player.** Settings → **Difficulty**: − / the level / + (132-px targets) and one line underneath from
  `DifficultyTable.Describe`, e.g. "Intended: the designed fight. Rewards ×1.5." or "Gentle: foes 55% HP, 60% damage,
  60% speed, -2 Lv. Rewards ×0.6." The results screen names the dial under Experience ("Difficulty 10 — rewards ×1.5";
  `BattleReport.Difficulty`).
- **Migration.** A settings.json from before the dial holds the old preset: **Easy → 5**; **Custom** with enemy HP and
  damage averaging below ×1 → the level whose enemy side is nearest that average; **Normal, Hard, Custom at ×1 or more,
  and anything unknown → 10** (the old multipliers were inert, so those players were already fighting the designed
  game). It is rewritten as `{"level": N}` on the next save and the scaffold's keys are dropped. A legacy save slot's
  Settings block migrates the same way.
- **Telemetry** tags every battle with `difficulty` and the multipliers it applied (`diffEnemyHp`, `diffEnemyDamage`,
  `diffEnemyLevel`, `diffRewardGold`, `diffRewardXp`, `diffItemDropRate`); battle records carry `difficulty`.
- **Balance runs** fight at a chosen level: `GAIDEN_DIFFICULTY=1..10` (default 10) for `AbilityUsageSoak` and
  `BalanceReport`, and `python tools/polish.py --balance --difficulty N`.
- *(Tests: `DifficultyTests` — table valid and monotonic, 10 = unscaled enemies, the reward curve and its label, new
  settings at 10, preset migration, enemy HP/levels rising with the dial on a real stage, kill rewards scaling.)*

*Superseded 2026-11-06 (GG-T309) — the original scaffold:* Owner: a Difficulty selection with presets and a Custom
mode "so when you read the telemetry you can ML it and learn what multipliers affect what; for right now just keep
everything at 1.0." `SettingsSave.Difficulty` held a `Preset` (Easy/Normal/Hard/Custom, default Normal) and six
multipliers (`EnemyHp`, `EnemyDamage`, `EnemySpeed`, `RewardGold`, `RewardXp`, `ItemDropRate`) pinned at 1.0 for every
built-in preset, none of them consumed by the battle; telemetry was tagged with all of them. The stated future
direction — higher difficulty, better rewards — is what the dial above delivers.

### 22.10 The Crucible (added 2026-10-19, owner direction, GG-T95)

Owner: "a way to gain xp and loot for the saved campaign … a waiting for the bus kind of mode."

A quick, endless survival battle fought from camp **inside a normal saved campaign** — a Camp menu row, not a separate game mode (`Meta/Crucible.cs`, `CrucibleScreen`, `CrucibleLauncher`).

- **Entry.** Camp → **Crucible** row (right under Adventure; its value reads "Endless waves" before the first run, then "Best: N waves") → `CrucibleScreen` (what it is, first-wave level, the families you'll face, the record) → **Enter the Crucible** (`crucible_enter` cue). **Closed mid-expedition**: the row is disabled and reads "Closed on an expedition" (`Crucible.CanLaunch`) — the run's full-HP reset would otherwise heal the party between an expedition's battles for free ([§22.5](#225-expeditions)). It also refuses with no party or no foes met.
- **One battle, many waves.** A run is one generated `StageDef` (`crucible-<seed>`, registered in the in-memory `ContentDb` only, earlier runs' stages pruned) of `Crucible.MaxWaves` (50) waves, fought by the ordinary `BattleEngine` wave loop — no new battle loop. Because it is one battle, **wounds, mana and consumables carry wave to wave** and there is no camp in between. Effectively endless: levels climb every wave, so the party falls (or leaves) long before wave 50; clearing all 50 would be a victory, committed the same way.
- **Escalation.** Wave *i* (0-based) spawns at **`PartyLevel + i × LevelStep`** (`LevelStep` 1; `PartyLevel` = the active party's rounded mean level), with `2 + i/3` foes capped at 5. The levels are authored per spawn, so `BattleFactory.Build` gives a Crucible stage `RecommendedLevel` 1 and no New Game+ step — neither the campaign curve nor NG+ lifts them further.
- **Encounter groups** (*added 2026-10-22, GG-T184*). Each wave is one of that family's **authored groups** from `encounters.json` (its mix on its formation tiles), the largest that fits the wave's foe count; no group repeats in a run until every same-size group of its family has had its turn (`EncounterCatalog.Pick`). A family with no group that size tops up with random foes of the family. The endless generator (`EncounterGenerator`, GG-T55) builds its waves the same way.
- **Families.** Each wave is **one enemy family** (`ClassDef.DropTable`, house convention), never the same family twice in a row, cycling through a fresh shuffle of every family the save knows: enemies it has **met** (Bestiary seen or defeated; legacy scanned) or that fight in **the campaign so far** (cleared stages plus the next one). Bosses (`BossPhases`/`Boss` tag) and multi-tile foes stay in their own stages. So the drops are the mats this save is already crafting with. The arena (background, music, blocks) is one of the campaign-so-far stages.
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

All classes share `classes.json`; `Team` separates heroes (62) from enemies (110). **Amended 2026-10-09:** the spawn side decides the team — a hero class placed in a stage wave fights as an enemy (the Hierarch is the `vampire` class, which is also recruitable).

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

`Equipment` (weapon, armor, relic), `Consumable`, `CraftingMaterial`, `QuestItem` (never sold). Gold is not an item. **Added 2026-11-06 (GG-T301):** `RecipeScroll` (§24.6.2), `Tome` (teaches one Passive/Reactive, `TeachesAbility`) and `Chest` (rolls a droptables.json row when opened, `OpensTable`); see [§25.6.4](#2564-learnable-passive-rewards-tomes-and-chests-gg-t301).

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

`armorsets.json` holds **20 named five-piece sets — 100 armor pieces** (one Head/Torso/Legs/Gloves/Boots each, all of the set's `ArmorWeight`): 7 Light, 6 Medium, 7 Heavy, pitched from campaign level 1 (Wayfarer's, Light) to 34 (Dreadwyrm, Heavy; the top four sets spread over the Choir's Cathedral's 22–24 and the longer campaign, GG-T350; every set from 16 up moved three levels when The Still joined the campaign, GG-T344, and every set from 4 up moved three more when The Lantern Road did, GG-T345, so the Lantern Road's 4–6 has none of its own), a new set at most every five levels, rarity Common → Legendary with the level. `ArmorSetDef{Id, Name, Weight, Level, Description, Pieces[5], Bonuses[]}`; each `SetBonusDef{Pieces, Stats, Resistances, PincerDamageBonus, RegenPerTurn, BattleStartManaOrbs, StartsProtected}` is active while at least `Pieces` distinct pieces of the set are worn. Every set has a **2-piece** bonus (its signature stat) and a **5-piece** bonus (two stats plus a perk: an element resistance, Pincer damage, Regen, battle-start orbs or starting Protected). `ContentDb.SetByItem`/`TryArmorSetOf` maps a piece to its set; `ContentLoader.Validate` rejects a set without exactly one piece per armor slot, a piece of the wrong weight or in two sets, or a bonus tier outside 2–5.

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

#### 24.6.5 Lucky craft — the Diamond (GG-T235)

Owner 2026-11-05: "trying to do a lucky craft with a 'Diamond' to increase chances of a better item that's forged." The **Diamond** (`mat_diamond`, Epic crafting material, never sold by a vendor, never dropped) comes **only** from the LuckyDiamond paywall hook ([§25.18](#2518-rested-and-the-paywall-hooks-gg-t232)): the Forge detail's **Buy Diamond · 1g · 1s** button runs a timed job that lands one in the bag (a full stack refunds). Any Forge recipe whose result can roll Masterwork ([§24.6.3](#2463-masterwork-variants-gg-t46)) also shows **Lucky craft (use 1 Diamond) · ★ N%** while one is held: the same craft as **Craft** (`Forging.Forge(c, s, r, lucky: true)` → `Crafting.Craft(…, lucky: true)`, guided first forges included), but it spends one Diamond and rolls Masterwork at `MasterworkChance(c, s, lucky: true)` — the normal chance **+40%**, capped at **75%** instead of 35% — and a lucky Masterwork's stat bonus rolls **+15–20%** instead of +10–20%. Tuning lives in `Crafting.LuckyChanceBonus`/`LuckyMaxChance`/`LuckyStatBonusMin`. A refused lucky craft (no Diamond, potency brew, ineligible result) consumes nothing. The result plays out over time (`LuckyCraftUi`): the Diamond spins up in a veil with a `magic` cue, then the result lands with `powerup` (Masterwork) or `clang` ("the Diamond's luck didn't hold"). Bots and `CampRoutine` never buy or spend Diamonds. *(Tests: `LuckyCraftTests`.)*

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
| Merchant | Merchant (§25.1) | `greenvalley_road_merchant` — ThemeStart Tithe Moors | Tithe Moors I |
| Innkeeper | Inn (§25.3.1), incl. the Medic Wagon | `greenvalley_after_first` — StageCleared Tithe Moors I | Tithe Moors II |
| Blacksmith | Blacksmith (§25.2) | `greenvalley_smith` — GearWorn 60%, fallback Shardfall (§25.0.4) | the first launch after a hero's gear wears below 60% (at the latest Shardfall I) |
| Alchemist | Alchemist / Brew (§25.3) | `desert_alchemist` — CanBrew, fallback Shardfall (§25.0.5) | the first launch once the bag holds the loot for a first bottle brew (at the latest Shardfall I) |
| Enchantress | Enchantress / Enchant (§25.3.4) | `swamp_enchantress` — FirstArmor, fallback Arkgrave (§25.0.8) | the first launch after the first armor piece is forged (at the latest Arkgrave I) |

- **Always met, early:** a join beat has no conditions and every branch of it joins (the Blacksmith's fires on worn gear, with a theme fallback so a party that never wears gear still meets him) (nobody can be refused away), and everyone is met by the third expedition — the services the campaign leans on (shop, rest, repair) inside the first one. Every beat pending at a launch plays, one after another (§27.1), after Camp and before the battle, so a service opens at the next visit to Camp. *(Tests: `CampNpcTests` — `Every_npc_joins_on_every_branch_of_a_story_beat`, `Playing_the_campaign_in_order_meets_everyone_by_the_third_expedition`, `Every_npc_has_a_camp_talk`.)*
- **The bots meet them the same way:** `CampRoutine` (§25.3.3) uses only joined services, and `BalanceReport`'s story rows play each launch's pending beats headlessly (`DialogueRunner.PlayPendingBeatsHeadless`, GG-T118); a finished run asserts everyone was met. Its no-story yardstick rows join every NPC up front, like a pre-GG-T119 save.
- **Test Mode** (§6, GG-T93) never plays story, so `TestLoop.NewSave` **auto-joins every NPC** up front — the loop exercises repair, rest, buy, brew and learn from cycle 1. The automation sweep's preview save and `newgame npcs=1` do the same.
- **Old saves:** save version 4's migration joins every NPC on any older save (they had been using every service). New Game+ keeps flags, so nobody leaves.
- Abilities/Learn, the Summon Circle, Bounties, the Caravan and the Crucible (§22.10) have no keeper and are always open.

### 25.0.3 Stations open one at a time (GG-T174)

A new player meets the Camp one system at a time. Every station is always **listed** on the Camp menu, but a closed one is a dimmed row saying when it arrives (`CampUnlocks`):

| Station | Opens | Row while closed |
|---|---|---|
| Merchant, Inn, Blacksmith, Alchemist, Enchantress | when their keeper joins (§25.0.1) | "Joins at Arkgrave" / "Joins after Tithe Moors II" — read from the join beat in content |
| Bounties | 2 campaign stages cleared | "After Tithe Moors II" |
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

- **The Alchemist joins at the first brew:** his join beat (`desert_alchemist`) has the story trigger **`CanBrew`** — it plays at the next launch once the bag holds every looted input (all but the bottle) of some Apprentice-tier bottle recipe (`Brewing.LootedForFirstBrew`). Its `Id` is a fallback theme (`desert`), as with GearWorn (§25.0.4), so a party that never loots still meets him at the Shardfall. He hands over one Empty Bottle on joining, so the first brew needs no shopping. The Camp row reads "Joins once you've looted enough to brew" while he is away. Joining him also opens the Blacksmith's workshop (§25.0.4).
- **Guided prompt:** on the first visit `AlchemistScreen` opens with the first brewable bottle recipe selected (`Brewing.FirstBrew`) and his camp tip (`CampTips.Resolve`) names it and what goes in, says what the Merchant charges for the potion, and rings **Brew** (`craft`). With only the bottle missing it rings the recipe row and sends the player to the Merchant for one.
- **Bots:** `CampRoutine`'s potion stock brews any recipe that makes its stopgap potion, buying a single bottle from the Merchant when a bottle recipe is short only of that.
- *(Tests: `EconomyLoopTests` — `Every_dropped_crafting_material_feeds_at_least_one_recipe` (no looted material is a dead end), `Bottle_recipes_brew_from_loot_for_less_than_buying_the_potion`, `The_camp_routine_buys_a_bottle_to_brew_its_loot`; `CampNpcTests.The_alchemist_joins_once_the_loot_for_a_first_brew_is_in_the_bag`; `CampTipsTests.The_alchemist_tip_walks_the_first_bottle_brew`.)*

### 25.0.6 Onboarding step 4: forge the first weapon (GG-T177)

Once the workshop opens (§25.0.4), the Forge offers three **starter weapon recipes** (`Starter: true` in `recipes.json`): Apprentice Forge recipes that cost **no gold** and take only materials the Tithe Moors' enemies drop — **Oak Club** (Wolf Pelt + 2 Cracked Fang), **Training Rapier** (2 Broken Blade + Iron Ore) and **Tallow Lantern** (2 Slime Gel + Tattered Cloth). Between them they suit every starting hero (Paladin, Barbarian, Cleric). Content validation holds a starter recipe to one weapon, Apprentice tier, no gold.

- **The hero it suits** (`Forging.SuitedHero`): a party hero who can wield it and would be better off than with what they hold, best stat match first — the weapon's stat bonuses weighted by the class's growth (`Forging.Affinity`), so the Wisdom lantern goes to the Cleric — then the bigger gain.
- **Guided prompt:** with the repair tip behind him, `BlacksmithScreen` opens on **Forge** with the first-forge recipe selected (`Forging.First`: a carryable starter recipe that suits a party hero, ready ones first). The Forge tab has its own camp tip (`blacksmith-forge`; `HubPage.TipStation`), resolved for the save: it names the weapon, its inputs and the hero, and rings **Craft**; with materials still missing it rings the recipe row and says how many of which to bring back. The recipe detail reads "First forge: free, and the Cleric equips it".
- **Forge and equip:** tapping Craft runs `Forging.Forge` — the weapon is made and equipped on the hero it suits ("Made Tallow Lantern: the Cleric equips it"), and the save flag `onboard:first-forge` ends the guidance; later forges go to the bag as usual. Test Mode and New Game+ saves skip the guidance.
- **Bots:** `CampRoutine` forges starter weapons like any other recipe, once the workshop is open.
- *(Tests: `EconomyLoopTests.Starter_weapon_recipes_cost_no_gold_and_take_only_first_theme_drops`, `EconomyLoopTests.The_first_forge_equips_the_starter_weapon_on_the_hero_it_suits`, `CampTipsTests.The_forge_tip_walks_the_first_forge`; harness `firstforge` — Forge tip rings Craft, tapping it forges the lantern for no gold and the Cleric equips it.)*

### 25.0.7 Onboarding step 5: forge armor (GG-T178)

The armor-set recipes (GG-T182's twenty named sets, `armorsets.json`) **open at the Forge after the first weapon** (§25.0.6): until `onboard:first-forge` is set, any Forge recipe whose result is a set piece (`Forging.IsSetArmor`) is hidden from the Forge list and `Crafting.Craft` refuses it ("Armor recipes open after your first forge"; `Forging.ArmorOpen`). Plain armor and jewelry recipes are not gated. Test Mode and New Game+ saves skip the gate, and `CampRoutine` only picks open recipes. Four Apprentice set recipes take only Tithe Moors drops — **Wayfarer's Jerkin/Trousers** (Leather + Wolf Pelt, Light) and **Militia Chausses/Gauntlets** (Iron Ore + Leather + Military Insignia, Medium) — so a first piece is forgeable before the desert.

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

- **Where it lives:** the enchant id is on the piece (`EquippedArmor.Enchant`), not a new item, so set membership, wear, repairs and Masterwork rolls are untouched. One enchant per piece — a new one replaces it. Unequipping keeps it on that copy (`WornGear.Enchant`; an enchanted copy is put back on first), and it goes into battle on `ArmorPieceSpec.Enchant` / `EquippedPiece.Enchant`. `BattleRunner.HasPassive` checks the class kit, then every intact enchanted piece the unit wears; a broken piece leaves `Unit.Gear`, taking its passive with it, and a piece that breaks for good loses the enchant. Warded buffs announce "Warded!". Tuning: each passive's `abilities.json` trigger Amount (the old `BattleConfig.RegeneratePercent`/`ThornsFraction`/`ManaSiphonCharge`/`EnemyLifestealFraction` constants were removed in GG-T302, unread since GG-T296).
- **Tier:** like her weapon enchants, armor enchant tiers unlock with the Alchemist's station level, and each enchant feeds it 15 XP. The Apprentice ones take Arcane Dust and Tithe Moors trophies only.
- **The Enchantress joins after the first armor:** her join beat (`swamp_enchantress`) has the story trigger **`FirstArmor`** — it plays at the next launch once `onboard:first-armor` is set (§25.0.7). Its `Id` is a fallback theme (`swamp`), so a party that never forges armor still meets her at the Fens. She hands over an Arcane Dust on joining. The Camp row reads "Joins once you've forged your first armor" while she is away.
- **Guided prompt:** `ArmorEnchanting.First` picks the first enchant: an unenchanted worn piece (set pieces first, so the guided first armor), and an open enchant whose materials are in the bag, else the fewest still to find, else file order. Her **Armor** tab (`EnchantressScreen`) lists worn pieces (`piece-<class>-<slot>`) and enchants (`aenchant-<id>`) and preselects both; her camp tip (`enchantress`, `CampTips.Resolve`) names the enchant, the passive, the hero and the piece and rings **Enchant** (`craft`); short of materials it rings the enchant row and says what to bring back. The **first enchant is free** of gold (`ArmorEnchanting.Gold`), and `onboard:first-enchant` ends the guidance. Test Mode and New Game+ saves skip it.
- **Shown:** the Status screen's gear list adds "✦ Regenerate" after an enchanted piece; the Equip screen marks the slot "✦" and names the enchant in its compare panel.
- *(Tests: `EconomyLoopTests.Armor_enchants_grant_passives_and_the_first_ones_take_first_theme_drops`, `EconomyLoopTests.An_armor_enchant_binds_into_the_worn_piece_and_stays_with_it`, `CampNpcTests.The_enchantress_joins_after_the_first_armor_piece`, `CampTipsTests.The_enchantress_tip_walks_the_first_armor_enchant`, `ArmorEnchantTests` (poison ward, thorns, regeneration in battle); harness `firstenchant` — forge the lantern and trousers, then the Enchantress tip rings Enchant and Regeneration binds into the trousers for no gold.)*

### 25.0.9 Onboarding step 7: train a new skill (GG-T180)

The last step of the onboarding chain. Once the first armor enchant is bound (`onboard:first-enchant`, §25.0.8), the **Learn** tab of the Abilities screen (gold-bought abilities, §25.6, US-231) is introduced, and a guided prompt trains one new skill and slots it on the battle bar.

- **When:** `Training.Step` — the first enchant is done and `onboard:first-skill` isn't. Camp's **Abilities** row reads "Train a skill!" while it's ahead. Test Mode and New Game+ saves skip it (`Training.FirstDone`). The Learn tab itself was never locked and stays open throughout; this step is the introduction, not a gate.
- **Guided skill:** `Training.First` picks the party lead first (then the rest of the party, then the reserve) and, for that hero, the first not-yet-known skill on its own class list (else the first universal skill by name). The Abilities screen opens on **Learn**, on that hero, with a guided card at the top (`guided-lesson`: the skill, what it does, "First lesson free", the bar slot it lands in) and a **Learn** button (`craft`). Learn rows are named `learn-<abilityId>`.
- **Free and slotted:** while the step is ahead, any skill (not spell) learned is the guided one: `Abilities.Price(s, a)` is 0 for it, and `Abilities.Learn` puts it in the first empty unlocked bar slot (else the last unlocked slot, `Training.SlotFor`) and sets `onboard:first-skill`, which ends the chain. Later skills cost their usual 300 G and are slotted by hand.
- **Camp tip:** the Abilities screen shows the `abilities-learn` tip (its own, separate from the plain `abilities` tip, so a player who opened Abilities early still gets it): it names the hero and the skill, says the first lesson is free and which slot it goes in, and rings **Learn**.
- *(Tests: `EconomyLoopTests.The_first_skill_after_the_first_enchant_is_free_and_goes_on_the_bar`, `CampTipsTests.The_training_tip_walks_the_first_skill_after_the_first_enchant`; harness `firstskill` — after the first enchant the Abilities screen opens on Learn, the tip rings Learn, and the skill is learned for no gold and sits on the lead's bar.)*

### 25.0.2 Camp tips (GG-T173)

The first time the player opens a Camp station, a one-time **camp tip** points at the thing to do there: the screen dims, a gold outline and a bouncing pointing hand (`Art.Cursor`) mark the key control, and a card names the station and says what to do in one sentence, with **Got it**. The card sits at the bottom, or at the top when the control is in the lower half, so it never covers what it points at. Combined with the NPC join order above, stations — and their tips — arrive one at a time.

- **Table:** `Meta/CampTips.cs` — station (the page's `Id`), title, sentence, target control name (a trailing `-` matches a prefix, e.g. `recipe-` = the first recipe row). Today: Merchant, Blacksmith (the most worn piece's repair row, §25.0.4), the Blacksmith's Forge tab (the first forge's Craft button, §25.0.6, then the first armor piece's, §25.0.7), Alchemist (the first bottle brew's Brew button, §25.0.5), Enchantress (the first armor enchant's Enchant button, §25.0.8), Abilities (then, after the first enchant, its Learn tab's guided first skill, §25.0.9), Bounties. The onboarding chain (GG-T174…T180) refines wording and targets here.
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
- `Grants` — the ability or passive id the node would unlock (must resolve in `ContentDb.Abilities`). **Amended 2026-11-06 (GG-T296):** a granted Passive or Reactive (Last Bastion's Counter Attack, Lifesteal, Double Attack, Tough Hide, Regenerate) fights with the hero. `BattleFactory.SpecFor` copies every known Passive/Reactive into `HeroSpec.Passives`; it had dropped them, so these nodes did nothing in battle.
- `Cost` — skill points to spend on it.
- `RequiredPointsInTree` — total points already spent anywhere in the tree before it can unlock.
- `Prerequisites` — sibling node ids (same tree) that must already be unlocked.

`ContentLoader.Validate` resolves every grant and prerequisite, rejects duplicate node ids within a tree and prerequisite cycles, and requires every `Team.Hero` class to name a tree that exists. Six trees ship today, one per `Role` (§25.13): **Guardian** (Tank), **Warrior** (Striker), **Ranger** (Ranged), **Mage** (Mage), **Cleric** (Healer), **Alchemist** (Support) — a deliberate simplification of the Guardian/Warrior/Rogue/Ranger/Mage/Cleric/Alchemist archetype list down to one tree per already-`Role`-validated archetype, leaving a Rogue split out of Warrior for a later pass once rogue-specific content exists. *(That split landed 2026-11-02, GG-T81: seven trees now, Warrior and Rogue both serving `Striker`; see the GG-T81 amendment below.)*

**Amended 2026-10-16 (GG-T74):** each tree deepened from a flat 5-node list to **2 shared nucleus nodes + 3 variant branches (Opener → Core → Branch Capstone) + 1 cross-branch Finale** — **72 nodes total**, every tree 12 deep. The 3 branches per tree are the bible's own §25.13 sub-archetype clusters, now actually spendable instead of only shaping a class's starting kit:

**Amended 2026-10-21 (GG-T52):** Mage, Cleric and Alchemist each picked up a 13th node (`RequiredPointsInTree: 8`, no prerequisite) granting one of the three proficiency-unlock passives ([§24.2.1](#2421-proficiency-gating-gg-t52)) — Mage → Heavy Armor Proficiency, Cleric → Martial Weapons, Alchemist → Arcane Focus. Guardian/Warrior/Ranger don't need one: their classes already start with Martial weapons (and Guardian with Heavy armor) by `ClassDef` default.

**Amended 2026-11-02 (GG-T77, amends GG-T52): interleaved attribute-bonus nodes.** `SkillNodeDef` gained an optional `GrantsStat` (`StatGrantDef`: `Stat` — a real `Stats` field name, `Strength`/`Vitality`/`Agility`/`Speed`/`Stamina`/`Intelligence`/`Wisdom`/`Luck`, never D&D shorthand — plus a flat `Amount`). A node grants **either** an ability (`Grants`) **or** a stat bonus (`GrantsStat`), never both and never neither; `ContentLoader.Validate` rejects both/neither, an unknown stat name and a non-positive amount. Every branch now interleaves 2 stat nodes (1 point each): `<branch>_stat1` (+3 of the tree's primary stat; needs the Opener and 2 points in the tree) and `<branch>_stat2` (+2 of its secondary; needs the Core and 5 points). Primary/secondary per tree: **Guardian** Vitality/Stamina, **Warrior** Strength/Speed, **Ranger** Agility/Luck, **Mage** Intelligence/Wisdom, **Cleric** Wisdom/Vitality, **Alchemist** Luck/Stamina. 20 branches (Guardian's Deathguard and Mage's Arcane Warden make four each) → 40 new nodes, 121 total. `StatGrantDef.ToStats()` gives the bonus as a `Stats` block for the point-spending/stat pipeline to add; until that pipeline reads it, a bought stat node is content only. *(Tests: `SkillTreeTests.Every_branch_interleaves_its_trees_primary_and_secondary_stat_nodes`, `Validate_rejects_a_bad_stat_node`.)*

| Tree | Branch 1 | Branch 2 | Branch 3 |
|---|---|---|---|
| Guardian (Tank) | Aegis Guard — mitigation + cleanse | Juggernaut — knockback aggression | Warlord — party buffs |
| Warrior (Striker) | Berserker — rage/bloodlust | Duelist — precision/counter | Brawler — guard break/sweep · Attrition — drain/sustain (4 branches, GG-T81) |
| Rogue (Striker, GG-T81) | Shadowstep — mobility/vanish | Skirmisher — hit-and-run | Combo Brawler — chained strikes · Opportunist — feed on ailments (4 branches) |
| Ranger (Ranged) | Marksman — crit/precision | Trapper — snares/control | Skirmisher — mobility + covering fire |
| Mage | Spellblade — melee-infused casting | Field Caster — traps/wards/support | Hexweaver — curse/DoT damage |
| Cleric (Healer) | Aegis Wardens — preemptive wards | Hexmenders — damage-for-heal | Skirmish-Menders — mobile support |
| Alchemist (Support) | Tempo Weavers — haste/slow control | Field Engineers — traps/amplifier synergy | Primal Pack — reagent/consumable mastery |

A branch's Capstone is reachable in 3 points (Opener 1 + Core 2); the tree's Finale needs 10 points in the tree and 2 branch Capstones unlocked (deliberately the same 10-point figure §25.6.2's cross-tree gating uses to open a second tree — finishing two branches of your home tree and being ready to dip into another tree land at roughly the same investment). Ranger had no reusable sub-archetype content (GG-T18), so its 3 branches (9 nodes) are new `AbilityDef`s; every other tree's 9 branch nodes reuse the already-shipped GG-T3/§25.13 cluster kits verbatim (`aegisguard_*`, `juggernaut_*`, `warlord_*`, `berserker_*`, `duelist_probingthrust`/`guardstance`/`decisivecut`, `shadowstep_*`, `spellblade_*`, `fieldcaster_*`, `hexweaver_*`, `aegiswardens_*`, `hexmenders_*`, `skirmishmenders_*`, `tempoweave_*`, `fieldeng_*`, `primalpack_*`). **Amended 2026-11-02 (GG-T74, closing pass):** Mage gained a **4th branch, Arcane Warden** (the ticket's "Barrier spell on Mage's Warden branch") — 3 new bespoke Arcane spells, no class kit reused: Opener `warden_runeward` (*Rune Ward*, 1 orb, single ally `protection`), Core `warden_spellshield` (*Spellshield*, 2 orbs, single ally `protection` + strips debuffs), Capstone `warden_barrier` (*Barrier*, 2 orbs, `AllAllies` `protection`, no heal — the mage's pure-wall counterpart to Cleric's `aegiswardens_sanctuaryrite`, which still heals and stays the node a cross-specced non-healer grinds toward in §25.6.2's worked example). No Mage class starts on the Warden branch (no class kit changed); it's tree-only content, reachable by spending points. Mage's Finale prerequisites are unchanged (Spellblade + Field Caster capstones). Node grants otherwise still reuse existing content, including the six "enemy kit" passives (§14.4) pre-dating this amendment; this pass adds 12 new `AbilityDef`s (9 Ranger branch skills + 3 proficiency-unlock passives staged for §24.2's GG-T52 gating, granted here but not yet wired to any battle effect).

A hero's starting bar is unaffected: `Progression.NewHero` already makes every `DefaultLoadout` entry Known at creation regardless of the tree (§15.1), so "the hero starts in its home tree with its default abilities pre-unlocked" holds without any new linking.

*(Tests: `SkillTreeTests` — every hero class names a real tree, every node's grant/prerequisites resolve, `ContentLoader.Validate` catches an unknown grant, an unknown prerequisite, a duplicate node id, a non-positive cost and a prerequisite cycle; `CatalogTests` resolves every new `AbilityDef` on a live board; full suite 873/873, glyph + asset checks clean.)*

**Amended 2026-10-20 (GG-T80, docs-only class/region audit):** every `Team.Hero` class's `DefaultLoadout` was matched against its own `HomeTree`'s 3 branches above, by `<cluster>_<move>` ability-id prefix (e.g. an `aegisguard_opener` sibling kit uses `aegisguard_*` ids) — how well each tree's 3 chosen branches actually cover the classes that live in that region. Requested to inform where bridge nodes and new classes would be needed on a future unified map (GG-T79's own hex-wedge sketch didn't survive — see §25.6.2's neighbor note below and `docs/USER_STORIES.md` GG-T83 — but this gap data stands on its own regardless of topology). Findings:

- **Guardian — full branch coverage, 2 orphans.** Aegis Guard (`defender`, `knight`, `paladin`), Juggernaut (`myrmidon`, `tank`) and Warlord (`shieldmaiden`, `warchief`) between them cover 7 of Guardian's 9 classes. The other 2 — `darktemplar` and `jadeknight` — both run a `deathguard_*` kit (`deathguard_mana_siphon`, `deathguard_dark_communion`) that matches none of the tree's 3 branches; §25.13's own Tank sub-archetype list already names Deathguard as a 4th cluster, it just never got a branch (follow-up: GG-T82).
- **Warrior — structurally overloaded.** Striker's 31 classes split across **8** real `<cluster>_` kits, but the tree kept only 3 as branches. Per-cluster counts: duelist ×7 (`assassain`, `duelist`, `fencer`, `ronin`, `sellsword`, `swordmaster`, `thief`), shadowstep ×6 (`blackninja`, `blueninja`, `chromaninja`, `greenninja`, `redninja`, `yellowninja`), skirmisher ×5 (`lancer`, `raider`, `speedster`, `streetfighter`, `striker`), berserker ×4 (`barbarian`, `basher`, `bruiser`, `pandagirl`), combobrawler ×4 (`fighter`, `martialartist`, `prizefighter`, `pugilist`), opportunist ×2 (`nighthunter`, `ripper`), brawler ×2 (`odachi`, `slasher`), attrition ×1 (`vampire`). Only Berserker, Duelist and Shadowstep became branches; the other 5 clusters — skirmisher, combobrawler, opportunist, brawler, attrition, **14 classes (45%)** — match no branch in their own home tree (follow-up: GG-T81, carve a Rogue region out of the excess). **Resolved 2026-11-02 (GG-T81):** see the amendment below; all 31 now match.
- **Ranger — full branch coverage (closed 2026-11-02, GG-T18).** Marksman (`hexsniper`), Trapper (`hexwarden`) and Skirmisher (`hexstalker`, new) each have one class walking them; see §25.13's Ranged sub-archetype amendment. (Originally: zero classes on any of the three branches, both classes on the pre-branch `ranged_*` baseline kit — GG-T82's Ranger half.)
- **Mage, Cleric, Alchemist — no gap.** Every class in these 3 regions matches one of its tree's 3 branches: Mage — Spellblade (`harbinger`, `redmage`), Field Caster (`drifter`, `operative`), Hexweaver (`blackwitch`, `ritualist`); Cleric — Aegis Wardens (`alchemist`, `cleric`, `doctor`), Hexmenders (`sage`, `whitewitch`), Skirmish-Menders (`monk`, `sister`, `whiteninja`); Alchemist — Tempo Weavers (`courier`, `dervish`), Field Engineers (`engineer`, `machinist`, `technician`, `tinkerer`), Primal Pack (`bluelion`, `wildchild`).

This audit is read-only — no `ClassDef`, `SkillTreeDef` or other content changed, no new tests (none apply to a docs-only pass). *(Source: `Assets/Gaiden/Resources/Content/classes.json` × `skilltrees.json`, matched by script against every `Team.Hero` entry's `HomeTree` and `DefaultLoadout`.)*

**Amended 2026-11-02 (GG-T81, resolves the Warrior half of the GG-T80 audit above): Rogue split out of Warrior.** Warrior's 8 sub-archetype clusters are now divided between two regions by flavor, so every one of the 31 former Warrior classes walks a branch of its own home tree (was 17 of 31):

- **Warrior** keeps the brute-melee clusters: **Berserker** (`barbarian`, `basher`, `bruiser`, `pandagirl`), **Duelist** (`assassain`, `duelist`, `fencer`, `ronin`, `sellsword`, `swordmaster`, `thief`), plus two new branches built from the already-shipped kits, **Brawler** (`brawler_guard_break` → `brawler_wide_cleave` → `brawler_second_wind`; `odachi`, `slasher`) and **Attrition** (`attrition_bloodletting` → `attrition_dreadaura` → `attrition_soulfeast`; `vampire`). 14 classes. Nucleus (`cleave`, `war_cry`) and Finale (Undying Rampage, `lifesteal`, Berserker + Duelist capstones) unchanged.
- **Rogue** (new tree `rogue`, `HomeTree: "rogue"`) takes the mobility-and-combo clusters: **Shadowstep** (moved out of Warrior with its node ids unchanged; the six ninjas), **Skirmisher** (`skirmisher_blindside` → `skirmisher_hitandrun` → `skirmisher_runningriot`; `lancer`, `raider`, `speedster`, `streetfighter`, `striker`), **Combo Brawler** (`combobrawler_openingjab` → `combobrawler_onetwoflurry` → `combobrawler_knockoutcombo`; `fighter`, `martialartist`, `prizefighter`, `pugilist`) and **Opportunist** (`opportunist_exploit` → `opportunist_chain_the_opening` → `opportunist_collect_the_debt`; `nighthunter`, `ripper`). 17 classes. Nucleus: Light Fingers (`steal`) and Feint (`feint`); Finale: Flurry of Blades (`double_attack`, 10 points + Shadowstep and Skirmisher capstones). Rogue's Skirmisher is the melee `skirmisher_blindside` kit, a different cluster from Ranger's ranged Skirmisher branch (`skirmisher_covering_volley`); node ids are unique across all trees (`ContentLoader.Validate` rejects a clash, GG-T213), so Rogue's nodes are `rogue_skirmisher_opener/stat1/core/stat2/capstone`.
- Both trees have **4 branches** (same Opener 1 / Core 2 / Capstone 2 costs and 1/3/6 point gates as every other branch) rather than the original 3: the alternative, 3 + 5, would have left either Warrior or Rogue with a cluster and its classes still off-map. Every node grants an existing ability, so no new `AbilityDef`.
- Every new branch carries GG-T77's two interleaved stat nodes (`<branch>_stat1` +3 primary after the Opener, `<branch>_stat2` +2 secondary after the Core). Rogue's primary/secondary are **Agility/Speed**, so the moved Shadowstep branch's `shadowstep_stat1` is now +3 Agility (was Warrior's +3 Strength); Warrior's new Brawler and Attrition branches use Warrior's Strength/Speed.
- Both regions still serve `Role: "Striker"` (§25.13); no class's `Role` changed, only its `HomeTree`. The Build screen's cross-tree tail reads "…who skirmishes" for Rogue points (`Progression.BuildSummary`).
- **Place on the map:** Rogue sits between **Warrior and Ranger**, its two kin (Warrior shares its melee, Ranger its mobility and its Skirmisher name). `skilltrees.json` lists it between them. On GG-T79's role-kinship ring that order becomes Guardian - Warrior - Rogue - Ranger - Alchemist - Mage - Cleric (a 7-ring: the longest walk is 3 steps and no region has a single opposite), and on the owner's archetype wheel (§25.6.2, option D) Thief already sat between Ranger and Warrior. Which board shape ships is still GG-T83/GG-T168's call; this only fixes Rogue's neighbors.

### 25.6.2 Cross-tree specialization (GG-T51)

**Decided 2026-11-02 (owner, "do" on GG-T51): the home-tree threshold ships.** This replaces the earlier "not decided; nothing here ships" text and the notes on GG-T51, GG-T135 and GG-T167 that called this model undecided or rejected. It is how you get a tank that casts Heal, a rogue that throws Fireball, or a mage in plate. The rules (`Meta.Talents`, tuned in `Meta.TalentConfig`, both in `Assets/Gaiden/Meta/Talents.cs`):

- **Opening a second tree, then a third.** A hero can open its first off-tree once it has **10 points in its home tree** (`TalentConfig.OpenTreeAt[0]`) and a second off-tree at **20** (`OpenTreeAt[1]`). The array's length is the cap: two off-trees, three trees in all. "Points in the home tree" is `Talents.PointsIn`, the same sum the node gates use (every unlocked node's `Cost`, starting-kit Known nodes included). `Talents.OffTreeSlots` is how many of those thresholds the hero has met.
- **What opens a tree.** Buying its first node with skill points (`Talents.Opened`: the home tree, or any tree with a bought node in `HeroSave.TreeNodes`). A tree where the hero only *knows* a node's grant (a gold-Learned spell, §25.6) is not opened by that: buying a node there still needs a free slot. A closed tree's nodes are Locked, and the reason reads "Opening a second tree needs 10 pts in Guardian (7 now)" or, once both slots are used, "Already specialized in 2 other trees" (`Talents.WhyClosed`).
- **Off-tree premium.** Every off-tree node costs **+1 point** on top of its `Cost` (`TalentConfig.OffTreePremium`; `Talents.Cost`). The premium is a price, not investment: the tree's points (`PointsIn`) still count the node's own `Cost`, so it does not speed up that tree's gates.
- **Deeper off-tree tiers need more points in that tree.** An off-tree node's `RequiredPointsInTree` is multiplied by **1.5, rounded up** (`TalentConfig.OffTreeGateScale`; `Talents.Required`). Start-tier nodes (0) stay open; a branch Opener (1) needs 2, a stat node (2) needs 3, a Core (3) needs 5, a Capstone (6) needs 9 and a Finale (10) needs 15.
- **Respec** (§25.6.3) refunds what was paid, premiums included (`Talents.PointsSpent`), and since it clears every bought node it closes every off-tree too.
- **The camp routine** (GG-T54, `CampRoutine`) spends off-tree only in a tree the hero is already in (`Talents.HeldTrees`: opened, or holding a Known node) and pays the off-tree price and gates there. It never opens a tree the hero has no foot in; that stays the player's choice.

All three numbers are static fields, not consts, so a balance pass can retune them in one place. The 10-point figure is also the investment §25.6.1's Finale is tuned against, so finishing two home branches and being ready to open a second tree arrive at about the same time.

*(Tests: `TalentTests` — `A_second_tree_opens_only_once_the_home_tree_has_enough_points`, `Off_tree_nodes_cost_the_premium_and_home_nodes_do_not`, `Deeper_off_tree_tiers_need_more_points_in_that_tree`, `A_third_tree_needs_the_next_threshold_and_there_is_no_fourth`, `Respec_refunds_the_off_tree_premium_and_closes_the_off_tree`.)*

#### 25.6.2.1 Topology history (GG-T83)

Before the owner's 2026-11-02 decision above, the topology was left open. Topologies tried and set aside: (A) six trees plus a toll-gated bridge (GG-T51/GG-T76; GG-T51's rules above are this option, now adopted); (B) one hex map of six wedges, cost = path length (GG-T79, which also got its own opposite-pair arithmetic wrong); (C) an octagon of the 8 stats; (D) the owner's own archetype wheel of 8 pure plus 8 hybrid archetypes, the closest so far but "not quite right". The two references are FF12's Zodiac License Boards (pick 2 of 12 boards) and FFX's Sphere Grid (one shared lattice, different start points). The full record, including why each was rejected and the later GG-T166/167/168 attempts (GG-T168, stat-threshold unlocks with no board, was awaiting owner review when GG-T51 was adopted instead), is in [`docs/research-skill-map-topology.md`](research-skill-map-topology.md). The GG-T80 audit findings above don't depend on this decision.

#### 25.6.2.2 Archetype adjacency (GG-T135, added 2026-11-02)

Built on §25.6.2's rules, not beside them: the slot thresholds, the scaled gates and Respec are unchanged; adjacency only decides **which** tree a free slot may open and **how much** the flat premium grows. The board (`Meta.TreeAdjacency`, `Assets/Gaiden/Meta/TreeAdjacency.cs`) is GG-T79's role-kinship ring with Rogue between its kin (§25.6.1's Rogue note): **Guardian - Warrior - Rogue - Ranger - Alchemist - Mage - Cleric - (Guardian)**. Seven trees, so the farthest any tree is from another is 3 steps.

- **Distance is from the nearest tree the hero already holds** (the home tree plus every opened off-tree, `Talents.HeldTreeIds`), walking the ring through trees it doesn't hold (`TreeAdjacency.DistanceFrom`, `Talents.Distance`). An opened tree is measured from the *other* trees held, so it doesn't price itself at 0.
- **Premium grows with distance.** An off-tree node costs GG-T51's flat **+1** (`TalentConfig.OffTreePremium`) plus **+1 per step beyond the first** (`TreeAdjacency.PremiumPerExtraStep`; `Talents.Premium`): a neighbour +1, two steps +2. Respec refunds the recomputed price (`Talents.PointsSpent`), which matches what was paid when trees are opened outward from home; opening a farther tree before the nearer stepping stone can refund a point less.
- **Too far means route through.** A tree more than **2 steps** (`TreeAdjacency.MaxOpenDistance`) from every held tree can't be opened directly. Its nodes are Locked and the reason names the stepping stone: "Mage is 3 steps away: open Guardian first" (`TreeAdjacency.WhyTooFar`, checked in `Talents.WhyClosed` after the free-slot check). The tab still shows while a slot is free, so the player can see where the route leads.
- **Worked example (the owner's fireball barbarian in plate):** a Warrior can't open Mage at 10 home points (Warrior - Guardian - Cleric - Mage, 3 steps). It opens Guardian (a neighbour, +1 a node: plate). At 20 home points Mage is now 2 steps from Guardian, so the third tree can be Mage at +2 a node; or it opens Cleric instead (a neighbour of Guardian, +1). GG-T51's two-off-tree cap still holds, so Warrior → Guardian → Cleric → Mage as three off-trees isn't possible.
- **Tuning:** `Ring`, `ExtraEdges` (extra neighbour pairs, empty by default), `MaxOpenDistance` and `PremiumPerExtraStep` are static fields, as in `TalentConfig`. The Talents screen's off-tree note shows the distance ("neighbour" / "2 steps away") and the actual per-node premium.

*(Tests: `TalentTests` — `Every_skill_tree_sits_once_on_the_adjacency_ring`, `Ring_distance_routes_a_warrior_to_mage_through_guardian`, `A_distant_tree_must_be_routed_to_and_costs_more_per_step`. The GG-T51 tests pick the home tree's neighbours first, so they keep testing the flat premium.)*

### 25.6.3 Talents screen and Inn Respec (GG-T53)

Skill points (§15.1) are now spent on §25.6.1's tree nodes. `Meta.Talents` (`Assets/Gaiden/Meta/Talents.cs`) owns the rules; `HeroSave.TreeNodes` records each node bought with points as `"treeId/nodeId"` (an older save has none, so no migration step is needed).
- **Node states** — **Known** (green): the grant is already in `Known` (starting kit or gold-Learned). It counts toward the tree's points with no point spent, which is how GG-T50's "starts in its home tree with its default abilities pre-unlocked" works. **Learned** (gold): bought with points. **Available** (blue): every prerequisite node is unlocked and the tree's unlocked-node cost total (`Talents.PointsIn`, the same sum as the Build screen's `Progression.TreeInvestment`) has reached `RequiredPointsInTree`. **Locked** (dim): otherwise, and the detail panel says what is missing.
- **Spending** — `Talents.Unlock` takes `Cost` points from `HeroSave.SkillPoints`, records the node and adds its grant to `Known`. It is not auto-slotted on the bar: the Abilities screen does that. Gold Learn (§25.6) is unchanged; moving class abilities off Learn is still follow-up work.
- **Which trees show** — the home tree first, then any other tree the hero has opened or holds a Known node in, then, while an off-tree slot is free, every other tree (tab marked "(new)"; buying its first node opens it). Off-tree tabs show the premium, tier labels show the scaled points-in-tree gates, and node prices include the +1. The rules are §25.6.2's (GG-T51).
- **Screen** — Camp's **Talents** row (`SkillTreeScreen`, id `talents`; the row shows the party's unspent points and opens on the first hero who has some). It has the hero switcher, a point counter (unspent, plus spent so far), and a tab per tree when there is more than one. The tree is drawn as a node graph: one row per `RequiredPointsInTree` tier, nodes ordered by branch, and an elbow link from each prerequisite to the node it opens, coloured by progress. Tapping a node shows its detail (the tooltip): the granted ability's description, kind, cost and gate. **Learn** asks for confirmation (`Confirm`) before spending. The bought card pops in.
- **Respec at the Inn** — `Talents.Respec` costs `100 + 50 × level` gold. It refunds every point the hero spent on nodes, forgets those nodes' grants and clears them off the battle bar. Known-state abilities (the starting kit and anything gold-Learned) stay. It needs the Innkeeper (§25.0.1) and an open Inn (`Expeditions.InnOpen`), and does nothing when no points have been spent. It's offered from Camp's **Inn: Respec** row (`RespecScreen`, id `respec`: every roster hero with points spent and its price) and from the Respec button on the Talents screen.

GG-T78's pannable/zoomable canvas is still a separate follow-up; this graph scrolls vertically. *(Tests: `TalentTests`. Harness: `talents` and `respec` are in `tools/harness.py sweep`.)*

### 25.6.4 Learnable-passive rewards: Tomes and Chests (GG-T301)

Owner: heroes get passive/reactive abilities from class kits, skill-tree nodes, gear/enchants, **and quest completion, bounties, campaign progress, random chests and battle-finish loot**. One reward type carries all five: the **Tome**.

- **Tome** (`ItemType.Tome`, `ItemDef.TeachesAbility`): teaches one Passive or Reactive that has a `Trigger` (a battle passive) to a hero the player picks. `Tomes.Teach` uses up one Tome and adds the ability to `HeroSave.Known`; `BattleFactory.SpecFor` then sends it into battle (GG-T296's path). A hero that already knows it, or has it in its class kit, can't read it. `AbilityDef.LearnableBy` (hero class ids and/or Roles) restricts who may learn it; empty = any hero (all ten shipped passives). Ten Tomes ship, one per battle passive: Counter Attack, Thorns, Tough Hide, Regenerate, Poison Ward, Steadfast, Berserk, Mana Siphon (Uncommon/Rare), Lifesteal and Double Attack (Epic).
- **Sources.** Every source grants the Tome as an ordinary item, so the reward tables are plain data and a new signature passive (GG-T302) is one items.json row plus a reference:
  - **Quests** (`RewardItem`): Travel Light → Steadfast, Grown, Not Forged → Mana Siphon, Cold Iron → Counter Attack (§22.11).
  - **Bounties** (`RewardItem`): Wolf Pack → Berserk, Cave Dweller → Tough Hide (§22.4).
  - **Campaign milestones** (`ThemeDef.MilestoneReward`, campaign.json): paid once, the first time a theme's last stage is won (`Milestones.Pay` in `BattleFactory.Commit`, guarded by `SaveData.Milestones`, kept through New Game+). Tithe Moors → Regenerate, Shardfall → Thorns, Arkgrave → Poison Ward, Anchor Wastes → Lifesteal, Halcyon → Double Attack; each postgame theme → a Gilded Chest. The results screen shows "*Theme* cleared! · Milestone: …".
  - **Battle-finish loot** (droptables.json): boss families carry a Tome (Hierarch Lifesteal 35 %, Cyclops Tough Hide, Mecha Armor Steadfast, Swamp Mistress Poison Ward, Demon Lord Berserk 20 %, Mountain Troll Regenerate 10 %) and a 20 % Iron Chest.
  - **Random chests** (`ItemType.Chest`, `ItemDef.OpensTable`): every enemy family's table has a 1.5 % Wooden Chest. A chest is loot like any other, so the difficulty dial's drop multiplier (§22.9, GG-T309) scales it. Opening one (`Chests.Open`) rolls its own droptables.json row (`chest_wooden` / `chest_iron` / `chest_gilded`): each entry rolls its chance, coins roll CoinsMin..CoinsMax, and if nothing rolled it gives the likeliest entry, so a chest is never empty. Rolls are seeded by `SaveData.ChestsOpened`, so a save replays its chests. Wooden: potions, ore, 6 % each of the four defensive Tomes. Iron: potions, Ether, dust, 10–12 % each of six Tomes. Gilded: X-Potion, Revival Charm, 20–25 % each of six Tomes.
  - Dialogue `GrantItem` can grant a Tome too (no beat does yet; the story is on hold).
- **Validation** (`RewardCatalog.Validate`, part of `ContentLoader.Validate`): a Tome needs `TeachesAbility` naming a Passive/Reactive with a Trigger; a Chest needs `OpensTable` naming a non-empty table that holds no chest; only Tomes and Chests may set those fields; every drop entry needs 0 < Chance ≤ 1 and 1 ≤ Min ≤ Max; `LearnableBy` names hero classes or Roles, on Passive/Reactive abilities only; a milestone names a real item. Quests and bounties already check their reward item.
- **Passive slots** (`PassiveLoadout`): class-kit passives are innate, always on and never take a slot. Every other battle passive a hero knows (Tomes and skill-tree grants) is *learned*. At most **2** are on; a 3rd opens at **Lv 10** and a 4th at **Lv 20**. Until the player picks, the first ones learned are on, so old saves and tree grants keep working. Once they switch one (`HeroSave.PassivesChosen`, `HeroSave.ActivePassives`), the list is theirs, and a newly learned passive goes straight on if a slot is free. Meta-only passives (the proficiencies) never count. Respec drops forgotten grants from the list. *Why a cap:* with every Tome on one hero, Lifesteal + Double Attack + Berserk + Counter Attack would make one unkillable build. The cap makes each Tome a choice, and the choice is one switch, which suits a short session.
- **Screens:** the results screen lists a looted Tome with its generated rule sentence (`TriggerText.Describe`, terms tappable through GlossaryText) and a **Teach to…** row of hero buttons (party first; a hero who can't learn it is greyed). A looted Chest has **Open** in place, and its haul is revealed beneath it (Tomes with their own Teach row). The quest hand-in card and the bounty board (after **Claim**) offer the same Teach row, and a posted contract that pays a Tome shows what it teaches. The Items bag (All and Items tabs) shows a Tome's sentence with Teach to…, and a Chest's **Open**. **Abilities → Passives** (a 4th tab) lists the hero's learned passives with **On/Off** ("Learned passives on: 1/2 · another slot at Lv 10"), the class passives marked Innate, and any Tome in the bag this hero could **Read**. Teaching plays `battle.powerup`; opening a chest plays `battle.pickup`.
- **Bots:** `CampRoutine` opens every chest and reads each Tome to the first party hero with a free slot.
- *(Tests: `PassiveRewardTests`.)*

### 25.7 Shared Camp utilities

`App/UI/Ui.cs` (windows, menu rows, lists, tabs, gauges, cost pips, buttons, gold formatting), `ItemUi` (item rows, glyphs, detail pane), `Confirm` (modal), `GameApp.Toast`.

### 25.8 Per-screen sketches

Every Camp screen follows the same skeleton — header (‹ back, title, gold), body windows, a primary action — and is captured by `tools/harness.py sweep` (`artifacts/sweep/sweep-<screen>.png`), the living replacement for the old ASCII sketches.

### 25.9 The merged hub

✅ Done — this section's old "deliberately not yet" is resolved by §25.0 (**Amended 2026-10-09**).

### 25.10 Summon Circle

`SummonScreen` / `Meta.Summon`: recruit a hero class for gold — deliberate, not a pull. Pool (`summon.json`): GreenNinja, RedNinja, Pugilist, Ronin, Sellsword, Thief, Vampire. ~~Price **250 + 250 × recruits so far**.~~ **Amended 2026-11-05 (owner, GG-T232):** a paywall hook ([§25.18](#2518-rested-and-the-paywall-hooks-gg-t232)) — `Paywall.Cost(Summon)` (1 g today, flat) and the hero arrives when a `Paywall.Duration(Summon)` (1 s) summoning job finishes (`Summon.BeginRecruit` → `Paywall.Finish`); bots and tests keep the instant `Summon.Recruit` at the same price. `summon.json`'s `BasePrice`/`PriceStep` are gone. Owned classes show "Joined"; unaffordable rows are disabled. **Amended 2026-10-09:** a recruit **arrives at the roster's average TotalXp** (so it is not stranded at level 1) and **joins the party automatically when there is room**. *(Test: `Full_loop_battle_commit_then_every_vendor`.)*

#### 25.10.1 Mercenaries (added 2026-11-05, owner direction, GG-T233)

Owner: "they stay for a negotiated amount of battles at a price; whether you use them or not; they will reoffer their services when they are about to leave and you can choose to renegotiate or part ways."

- **Content:** `mercenaries.json` (`MercenaryDef`: hero `Class`, `Rate` gold per battle at the shortest term, `Terms` offered, `Temperament` Easygoing/Fair/Greedy, a board `Pitch` and a `Renewal` line), validated (hero class, listed once, positive rate, terms of 2+ battles, no duplicates, both lines). Six classes kept apart from the Summon pool, the guests, the recruitables and the story recruits: Myrmidon (Tank), Prizefighter and Raider (Striker), Ritualist (Mage), Doctor (Healer), Courier (Support).
- **Board:** Camp → **Mercenaries** (opens with the Bounty board, `Mercenaries.Open`) → `MercenaryScreen`. `Mercenaries.Board` shows **3** offers in an order seeded by the save and `MercenarySave.Refresh`, skipping heroes already in the roster; it is stable until the next battle, which turns it over.
- **Terms:** each longer term is **10% cheaper per battle** than the one before (`PerBattle`); the whole contract is **paid upfront** in ordinary gold (not a paywall hook). The merc joins at the roster's average experience and takes a party slot if one is open (`Summon.Join`).
- **Haggle:** once per merc per refresh, seeded (save + refresh + merc), odds by temperament (drop / hold / raise / walk: Easygoing 55/30/10/5, Fair 40/30/20/10, Greedy 25/25/30/20): **−20%**, unchanged, **+15%**, or the merc **walks off** until the next battle. The answer plays out over a beat ("…") with a cue per outcome.
- **Countdown:** `BattleFactory.Commit` calls `Mercenaries.CountBattle` on every battle, stage or Crucible, win or lose: every contract loses a battle **whether the merc fought or sat in reserve**. A spent contract settles (`Mercenaries.Settle`) before the next battle is built and at Camp, never mid-commit, so the last battle's XP and wear are written first: the merc's gear goes back to the bag, they leave the roster and party, and Camp announces it. They return to the board later and can be hired again.
- **Reoffer:** with **one battle left**, Camp opens `MercenaryRenewalScreen` (portrait steps in, their `Renewal` line types out): **Renew** any offered term on top of the battle left, at a loyalty discount of 10% per renewal (cap 30%); **Finish the contract** (they fight the last battle, then go; backing out means the same); or **Part ways now**.
- **Marked:** a `MERC · N left` tag on Camp's hero cards, the Party screen and the board's "Under contract" list.
- **Bots** (Test Mode, `CampRoutine`, `BalanceReport`) never hire. This is its own contract system: the dialogue vocabulary still cannot remove a hero (AMENDMENTS 2026-11-05).
- *(Tests: `MercenaryTests` — valid content apart from the summon pool, longer terms cheaper, seeded board that skips owned heroes and turns over, upfront payment at the roster's XP, countdown in reserve then departure with gear returned and rehire, a spent contract never fights, renewal at a loyalty discount and parting ways, seeded once-a-refresh haggle with walk-offs, save migration.)*

### 25.11 Bestiary

`BestiaryScreen`: a grid of every enemy class's thumb (darkened silhouette until Seen), "Seen n/110"; the detail shows name, Defeated count, lore and its family's drops (rarity-colored item names, highest chance first) once **Seen** (added 2026-10-15, GG-T43). ~~Then base stats only once Scanned.~~

**Amended 2026-11-06 (owner, GG-T290): knowledge is earned by defeats only; Scan is retired.** Each tier is `BestiaryKnowledge.Tier` (`Meta/SaveData.cs`), from the class's `Defeated` count:

| Tier | Needs | Reveals |
|---|---|---|
| Seen | spawned once | name, description, lore, drops |
| Speed | **1** defeat | SPD |
| Stats | **3** defeats | HP at Lv 1 and every base stat |
| Elements | **6** defeats | elemental weaknesses and resistances ("Immune to Poison · weak to Fire (×1.5)", `ElementLine`) |
| Abilities | **10** defeats | its abilities (as chips — hold one to read it), kit traits, brain and targeting personality |

#### 25.10.2 Random summon for Diamonds (added 2026-11-07, owner, GG-T343)

Owner: *"another way is to simply summon them randomly with Diamonds monetized currency"*.

- **The draw** (`Meta/DiamondSummon.cs`): a "Call a stranger" window at the top of the Summon Circle (`summon-random`) spends **`DiamondSummon.Price` = 1 Diamond** (`mat_diamond`; the one tuning constant, 1 for now like the other placeholder paywalls) and **no gold**. It brings one hero from the **wanderer pool** (`Wanderers.Pool`, [§25.11.2](#25112-random-rescues-added-2026-11-05-owner-the-random-character-recruitment-for-saving-them-gg-t236)) — unowned heroes with no other way in, the same pool a wanderer goes back to when it parts ways — never one of the gold summons.
- **Who:** `DiamondSummon.Pick`, seeded from the save's creation time and its recruit count, fixed when the summoning starts (the job's `Arg`), so asking again doesn't reroll.
- **Over time:** it rides the ordinary Summon paywall job (`PaywallJob.Kind = Summon`, `Diamonds` = what was spent, `Paid` = 0), one summoning at a time (gold or Diamond), `Paywall.Duration(Summon)` (1 s) long, through `TimedJobView` (coin cue, the filling bar, the confirm cue); then a **reveal** (`SummonReveal`, `summon-reveal`): the hero's portrait swells out of the light with a power-up cue, a fanfare and their name and role, and a tap closes it. The hero arrives exactly like a gold summon (`Summon.Arrive`: the roster at its average experience, the party if there's room). A summoning that fizzles (the hero joined some other way meanwhile) gives its Diamond back. A job left running survives a restart and lands at Camp like any other.
- **Diamonds** still come only from the LuckyDiamond hook ([§24.6.5](#2465-lucky-craft--the-diamond-gg-t235)); with none, the window says they're bought for the lucky craft at the forge. **No store, no IAP UI.**
- **Bots never draw:** `CampRoutine` only uses the gold summon, Test Mode saves are refused (`DiamondSummon.CanDraw`), and the soaks never touch it. The gold, deliberate summon (`Summon.Recruit`/`BeginRecruit`) is unchanged.
- *(Tests: `SummonTests` — a draw costs one Diamond and no gold, it draws from the wanderer pool and the hero arrives only when the summoning lands, the pick is seeded, no Diamond no draw and one summoning at a time, a fizzled draw refunds, a running draw survives a save round trip, Test Mode and the camp routine never spend Diamonds, the gold summon is unchanged.)*

- A locked row reads **"???"**, and a hint under the detail names the next one: **"Defeat N more to learn …"** (its speed / its HP and stats / its weaknesses / its abilities; `BestiaryKnowledge.Hint`).
- **One rule everywhere:** the battle actor panel ([§9](#9-the-hud)) and the hold-to-inspect card read the same tier, so a foe shows in battle exactly what the Bestiary knows.
- **Scan is gone:** the ability, `IsScan`, `ScannedEvent`, the battle report's scanned list, the bot's scan score and its VFX. Save migration drops Scan from every hero's Known list and bar; class kits swapped it out (healers → Quicken, supports → Silence, tanks → Cleave). An old entry marked `Scanned` keeps at least the Stats tier.
- *(Tests: `BestiaryKnowledgeTests` — `Tier_follows_the_defeat_thresholds`, `Thresholds_are_the_named_constants`, `DefeatsToNext_counts_down_to_each_tier_and_stops_at_the_last`, `An_old_scanned_entry_keeps_at_least_the_stats_tier`, `Element_line_reads_the_class_multipliers`, `Migration_drops_the_retired_scan_from_known_and_the_bar`, `No_content_references_scan`.)*

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
- Content: Dervish (Shardfall II), Drifter (Arkgrave II), Lancer (Anchor Wastes II), Machinist (first city ruin).
- *(Tests: `A_guest_who_survives_a_victory_joins_the_roster`, `A_guest_revived_mid_battle_is_recruited`, `A_guest_down_at_battle_end_can_still_be_recruited_later`, `Permadeath_is_off_by_default_and_old_lost_guests_come_back`.)*

#### 25.11.1 Mid-battle recruitment — "talk" (added 2026-11-02, GG-T132; from the since-deleted overworld/dialogue design draft §4)

Fire Emblem's talk on the real grid: some heroes aren't handed over — they have to be won.

- `WaveDef.Recruitables` (`RecruitableDef`: hero `Class`, `Level`, tile, `Dialogue`, `Conditions`) places a hero class on the board on the third team, **`Team.Neutral`**: amber keyline and a TALK tag, no timeline icon, never acts. Neither side fights it — enemies ignore it (no targeting, no pincer through it, a 2×2 won't shove it), hero damage and enemy-only abilities skip it, and it doesn't hold up a wave (a wave clears with it still standing). A hero dragged through it swaps places with it like any one-tile unit. A recruitable that wants healing enters at half HP (`HpPercent` overrides).
- **Conditions** (all must hold): `HealedBy` (any hero, or `Id` = that class, heals it — a hero ally-only ability reaches a neutral), `ParticipatedInPincerWith` (it stands beside a hero attacker — where a supporter would — when that hero's pincer strikes), `PayGold` (`Amount`, paid from the caravan's banked gold at the moment it joins — `BattleSetup.Purse`, deducted on commit whatever the outcome), `TalkedBy` (only that hero class can talk it round).
- **`TalkCommand`** (hero id, neutral id; legal only when cardinally adjacent, footprint-aware): tap the neutral with a hero beside it. Terms unmet → `RecruitOfferedEvent`: it speaks its Recruit dialogue up to the `join` node and says what it still wants — **no clock cost** (like a no-op drag). Terms met → one action: `RecruitJoinedEvent`, it speaks the `join` node, flips to `Team.Hero` as a **guest ally** (§25.11 from there on: player-controlled; standing at a victory → joins the roster; down at the end → it'll be back).
- **Dialogue:** a `DialogueDef` with `Trigger.Kind = Recruit`, `Trigger.Id` = the recruitable's class; linear nodes, no choices or effects, must have a `join` node; `$with` is the hero talking. Never played between battles.
- **Bot:** `HeroBot` talks a recruitable round the moment every term is met (`Recruitment.ReadyTalk`), values healing one that wants it, and never counts shuffling a neutral as progress.
- Content: Ronin (Tithe Moors III, wave 1 — heal me, fight beside me), Sellsword (Shardfall III, wave 1 — 150 G).
- Open (§6.1 of the draft): permanence — today a talked-round recruit joins the roster exactly like a guest.

#### 25.11.2 Random rescues (added 2026-11-05, owner: "the random character recruitment for saving them", GG-T236)

~~A battle the player launches may hold a **captive**: a random hero the player doesn't own, freed as a guest ally (§25.11 from there on).~~

**Amended 2026-11-07 (owner, GG-T342): the captive is now a wanderer.** Owner: *"so you will sometimes meet a hero while fighting a monster and they will fight the monsters separately but you are able to complete pincer moves with them so you fill a trust gauge by pincering, or healing them at the end of the stage and the loot you and the team member you choose has a conversation with them, if they can come to an understanding they join, if not they go back into the pool of possible recruits"* — clarified the same day: *"the point is to gain their trust, tell them what they want to hear, and then you have a follower — another party member."*

- **The roll** (`Meta/Wanderers.cs`, was `Rescues.cs`): `Wanderers.Chance` = **25%** per campaign battle, at most one wanderer, decided when `BattleScreen` builds the battle (`Wanderers.Apply` → `BattleSetup.Wanderer`, a `WandererSpec`). Seeded from the save's creation time, the stage id and the battle seed (`Wanderers.SeedFor`), so a battle replays the same roll. **Never** on a Test Mode save, a test stage, the Crucible, or a stage that already has an authored guest or recruitable; and never in the bots' balance runs or dotnet soaks, which build battles without `Wanderers.Apply`. Authored guests (§25.11) and talk-recruitables (§25.11.1) are unchanged.
- **Who** (`Wanderers.Pool`): an unowned, not-lost hero class that has no other way in — not in the Summon Circle's gold pool, not an authored guest or recruitable, not named by a dialogue's `JoinHero`/`RecruitHero`, not a mercenary (`mercenaries.json`, GG-T233), not a Camp NPC's face. Id order, so a seed always names the same hero. The same pool feeds the Summon Circle's Diamond draw ([§25.10](#2510-summon-circle)).
- ~~**On the board:** … as a player-controlled guest (`Unit.IsGuest`) … "A captive X! Keep them alive to free them."~~ **On the board:** the wanderer steps on in wave 1 or 2 (seeded) at the stage's recommended level with its class loadout on a free tile (`RandomFreeTile`, GG-T227) as an **independent ally** (`Unit.Wanderer`, `IsIndependent`), exactly like a tamed beast (§25.11.3): hero team, never commanded (no drag, no bar, not one of `HeroesLeftToAct`), its own turn icon, and on its turn the shared ally AI (`BattleRunner.RunAllyTurn`: the pincer planner, else charge the nearest foe through anyone one tile big, then close a pincer it stands at the end of, else strike the weakest adjacent foe, else shoot one in range). As a hero-team unit it pincers with the party both ways (`Unit.CanPincer`). `WandererJoinedEvent`; billboard **"A wandering X fights the monsters! Fight beside them to earn their trust."**
- **Trust** (`Unit.Trust`/`TrustNeeded`, `TrustGrewEvent`; tuning in `BattleConfig`): the gauge is full at **6** (`WandererTrustNeeded`). **+2** (`TrustPerPincer`) for each hero pincer it is part of, as one of the two ends or as a flanking supporter, alongside at least one commanded hero (a heroes' pincer it closes on its own turn counts). **+1** (`TrustPerHeal`) each time a commanded hero restores some of its HP (a heal at full HP gives nothing). Never above full. Floats "Trust n/m", and "Trusts you!" with a war-cry cue when full.
- **Look:** a rose keyline (`unit-keyline--wanderer`, `Palette.Wanderer`) with a WANDERER tag; its timeline icon has the same rose border; tapping it says "X is a wanderer: it fights on its own. Pincer beside them or heal them to earn their trust"; the InspectCard heads it "Wanderer · role" with a trust meter (`inspect-trust`) and how to earn it.
- **Defeat and falls:** a wanderer still standing does **not** keep the party in the fight (only commanded heroes do). If it falls it is down, never lost ("X has fallen!"; a revive brings it back with its turn icon); down at the end, it simply goes back to the road — results read "The wandering X goes on their way — May be met again", nothing is recorded and it stays in the pool.
- **The talk (the "loot"):** standing at a victory, the wanderer waits on the results screen (`BattleReport.WandererOffer`, `Wanderers.Offer`) — it never joins on its own. The block shows its trust meter, the rule as it stands (`Wanderers.Outlook`) and a button per party member (a tamed monster can't talk; a faction-mate is highlighted with their faction's name). The member the player picks talks with them: a `dialogues.json` conversation with `Trigger.Kind = Wanderer` (`Id` = the wanderer's hero class, else its `factions.json` faction, else none for the generic `wanderer_met`; `Wanderers.Scene`), `"$hero"` the wanderer and `"$with"` the companion. It has **one question with three answers**: exactly one says **what they want to hear** (goes to the `heard` node); the others go to `unheard`. Leaving the results without talking lets them go.
- **The understanding** (`Wanderers.Judge`, deterministic, no dice): they **join** only if the companion says what they want to hear **and** they trust the party: **full trust**, or **at least half trust with a companion of their own faction**. Below half, nothing said will do. Then both replies go on to `"$outcome"`, which the rule resolves to the scene's `join` or `part` line.
- **Join:** a full, normal party member exactly like a freed guest (`BattleFactory.JoinRoster`): the roster, the party if there's room, at their battle level and wounds; "X joins the caravan!". **Part:** they go their own way and back into the pool — nothing recorded; owning the hero is the only thing that takes them out.
- **Factions** (`factions.json`, `FactionDef`, `ContentDb.FactionOf`): the STORY.md factions with heroes — the Street, the Free Holds, the Lantern Road, the Fen-dark, the Ember, the Wild — and their hero classes; validated (hero classes only, each in at most one faction, unique ids that aren't class ids). Talks: one generic, one per faction, and the Dark Templar's own.
- **Validation** (`WandererContent.ValidateDialogue`): a Wanderer talk names a hero class, a faction or nothing; has no effects; exactly one question of three unconditional answers, one to `heard` and the rest to `unheard`; both replies go on to `"$outcome"`; and `join` and `part` nodes end it. `"$hero"` and `"$outcome"` fit a Wanderer talk only. The GG-T236 `Rescue` trigger and the `rescue_freed` scene are retired.
- **Bots and Test Mode:** the bots and soaks never roll a wanderer and Test Mode never does, so nothing changes for them; `Wanderers.Converse` plays a talk headlessly with a chosen answer (tests). Dev/automation: `devwanderer` (alias `devcaptive`) forces the next launch's wanderer.
- *(Tests: `WanderTests` — the deterministic roll near 25%, pool exclusions, no roll in Test Mode/the Crucible/a guest stage, the roll brings an independent ally with its own icon, it fights on its own turn, trust from a hero pincer and from a heal and its cap, a lone wanderer can't hold the field, a standing one waits to talk and a fallen one returns to the pool, the understanding rule, the right answer at full trust makes a normal party member, the wrong answer or too little trust sends them back, determinism, every pool hero has a faction and a talk, Wanderer-talk and faction validation.)*

~~**Freed:** … "X is freed and joins the caravan!" … the rescue conversation (`Trigger.Kind = Rescue`) …~~ ~~**Down at the end:** … "May be found again" …~~ (both superseded above).

#### 25.11.2.1 Wanderer heroes from the owner's art (added 2026-11-07, owner, GG-T333)

Owner 2026-11-07: the new heroes from the art are **recruited as wanderers** — "met mid-battle, earn their trust, say what they want to hear, and they join". Each is in no other pool (not the Summon Circle, no authored guest or recruitable, no story `JoinHero`, not a mercenary), so `Wanderers.Pool` holds them and so does the Diamond draw (§25.10.2). Each has a `factions.json` faction for the half-trust bridge, an Advice quiz (`talk_quiz_<id>`) and the faction's generic wanderer talk (no hero-specific scene: story text is placeholder until it is authored in Prose, GG-T355). Portraits are 1024 canvases with `Figure` and `Thumbnail` (§12.1.2).

- **Herald** (`herald`, Healer, Speed 12, home tree Cleric, faction **the Wild**). A flower-winged messenger of the Bloom (STORY.md §4) who heals by giving her own essence. The Bloom is a growth, not a faction with heroes, so she sits with the Wild, the closest faction (tide-changed growths). Bar: Heal, Guardian's Rush, potion, **Lifegift**, Covering Withdrawal (the Skirmish-Mender cluster). Signature **Lifegift** (`herald_lifegift`): a Skill, cooldown 2, instant, an ally mends 30 (scaled by WIS like every heal) and the Herald pays **Essence**, 12% of her max HP. Passive **Rebloom** (`herald_rebloom`): at her turn start below 60% HP she mends 5%. Support *Petalfall*: heals the chain 10%, reach 3. Stats lean VIT/WIS (9/14/12/12/12/13/15/12), so she can afford the price. Where the Cleric spends orbs and wards, the Herald spends herself: free, big heals that leave the bank for the casters.
  - **Essence** (`AbilityDef.LifeCost`, 0..0.5 of max HP; `Formulas.LifeCostHp`): once the heal lands the caster loses that share of her max HP, at least 1 and **never her last HP**. It is a price, not a blow: no attacker, no damage triggers, no cast stagger; a `DamagedEvent` with no source and the note "Essence" plays the -N float and the debuff cue. Validated: a Skill or Spell ally heal (BaseHeal > 0, AllyOnly). The bot never pays it below 40% HP and weighs the HP given. Glossary [Essence].
- **Aetherist** (`aetherist`, Healer, Speed 12, home tree Cleric, faction **the Fen-dark**; GG-T337). A Choir singer who reversed the hymn and pours aether in instead of drawing it out (STORY.md §4); the Fen-dark is where the Choir's runaways go (§3.5). Bar: Heal, Preemptive Ward, potion, **Aether Pour**, Sanctuary Rite (the Aegis Warden cluster). Signature **Aether Pour** (`aetherist_aetherpour`): a Skill, cooldown 4, instant, an ally mends 15 and **one orb flows into the party's bank** — the Bulwark's Siphon (§14.15) run backwards. Passives *Arcane Insight* (mana charge when she backs a pincer) and *Pure Spirit* (she can't be Silenced: the Choir's Hush doesn't take). Support *Cyan Current*: heals the attacker 10%, reach 3. Stats lean INT/WIS (7/9/11/12/12/18/18/12). Where the Herald spends HP to save orbs, the Aetherist feeds the bank so the casters can spend.
  - **An orb grant can ride a heal** (`AbilityDef.PoursOrbs`: `GrantsOrbs` on an ability that also heals, hits or buffs). It resolves on its targets like any ability, then mints its orbs ("aether"); a bare grant (Meditate, an ether) still resolves on its own with no target.
- **Tactician** (`tactician`, Support, Speed 12, home tree Alchemist, faction **the Free Holds**; GG-T336). A former Concord staff officer who planned tithe routes until she read the ledgers (STORY.md §4). The Concord has no hero faction; the Free Holds, with their militias and old oaths, are the closest fit for a defecting officer. Bar: Quicken, Stumbling Ground, potion, **Countermarch**, Swift Swap (Tempo Weaver tools for position and pace). Signature **Countermarch** (`tactician_countermarch`): a Skill, cooldown 4, no damage, on any foe. Passive *Keep Away* (a fair chance a foe that strikes her directly is pushed back 1 s). Support *Flank Order*: Pushback 0.75 s on the pincer's foes, reach 3. Stats 10/11/12/12/12/15/15/12. The other supports move the party's tempo; she rewrites the enemy's order.
  - **Countermarch** (`AbilityDef.SwapsTurns`, `Countermarch`): the foe's turn icon trades its time to act, exactly, with the foe whose turn is furthest off: the one about to act goes to the back of the line and the straggler steps up to its time. Both must be on the bar (not recovering in the tray, not Frozen) and on the enemy side; an independent ally is never moved. On the last foe, or one off the bar, it does nothing ("Already last" / "Not on the line"). It is a trade, not a push, so the Pushback cap (§2.3) doesn't bound it: the bar holds the same turns, only who strikes next changes (a charging caster or the foe beside a wounded hero for a harmless straggler). Played back as the existing Pushback glide on the foe and Haste glide on the straggler, with their cues, and a "Countermarch!" float. Validated: a Skill or Spell that picks one foe. The smart bot prices it as the push it buys on the foe less what the straggler brings forward (`HeroBot.CountermarchValue`); the casual bot sends a threatening foe to the back. Glossary [Countermarch].
- *(Tests: `WandererHeroesTests` — each hero's kit, the wanderer pool and faction, the quiz, the art; Lifegift heals and costs essence but never the last HP; a LifeCost must pay for an ally heal; Aether Pour heals and pours an orb; a bare orb grant is unchanged; Countermarch trades two turns exactly and does nothing on the last foe or one off the bar; a Countermarch must pick one foe.)*

#### 25.11.3 Taming and domestication (added 2026-11-06, owner, GG-T303)

Owner: a Monster Tamer can tame an enemy monster so it fights for the party for a few turns, "like Umaro the yeti berserker in FF VI": it acts on its own, keeps its own timeline icon and AI, and may run straight through a pincer you were setting up. Fight beside it long enough and it asks to join for good.

- **The Tame effect** (`AbilityDef.Tame` = base chance, `TameTurns` = duration in the monster's own turns) is one ability field, so any source plugs in: the Monster Tamer's **Tame** (Skill, 0.6, 4 turns, cooldown 2) and the **Dog Whistle** consumable (`dog_whistle_call`, 0.35, 2 turns). Validated: a Tame is a Skill or Item ability that picks one foe (SingleActor, PickActor, EnemyOnly), 0 < Tame <= 1, TameTurns >= 1.
- **Who** (`Taming.WhyNot`): a living enemy with no society: not `Sentient` (so frog folk, soldiers, hags never), not a boss (tag `Boss`, boss phases or a rule-break), not remains, an egg or a corpse, one tile and not a snake, not tagged `Untameable`. Today that is the bats, wolves, werewolf, yeti, flying monkey, vulture, scorpion, slimes, sandmaw, tree golems, skelepedes, the mindless undead, cyber-zombies and the ghost. `TargetResolver.IsValidPick` offers only these, so a refused pick spends nothing.
- **Odds** (`Taming.Chance`, one Rng draw): base x (1.5 - the monster's HP fraction, so x0.5 fresh to x1.5 nearly dead) x level factor (1 + 0.05 per level the user is above it, x0.5..x1.5) + 0.4% per point of the user's Luck and of its Wisdom, clamped to **5-95%**. A failure is a `TameFailedEvent` ("Resisted (n%)").
- **Tamed** (`TamedEvent`): the unit flips to `Team.Hero` with `Unit.Tamed` where it stands; any charge it was casting fizzles (`CastCancelledEvent` "tamed"); it keeps (or gets) its own turn icon. It is **independent** (`Unit.IsIndependent`): never commanded (no drag, no bar; `CanMove` and `GetSlotStatus` refuse; not one of `HeroesLeftToAct`). Its own turn **doesn't end the party's window** (only enemy turns do, §2.0), its buffs tick on its own turns, and party tempo doesn't move its icon. Enemies treat it as a hero: they target it and pincer it. A tamed beast alone doesn't keep the party in the fight: when every commanded hero is down, it is a defeat.
- **Its turn** (`BattleRunner.RunAllyTurn`): start-of-turn triggers. **Amended 2026-11-06 (GG-T306):** first it asks the shared pincer planner for a walk that completes a hero pincer, sets up an end a hero can close, or slots it into a chain ([§14.1.3](#1413-pincer-planning-gg-t306)), swapping through friend and foe. Otherwise it charges the nearest foe on the shortest cardinal path **through anyone one tile big**. Friend or foe swap into the tile it leaves, exactly like a hero drag, up to its `MoveRange`. ~~It is deliberately not clever about your setup.~~ It still acts on its own, so it can disrupt your setup. Then it closes any hero pincer it stands at the end of (it carries the **Pincer** keyword, §1.3), else hits the weakest adjacent foe, else shoots one in range. Its own class `Support` backs the party's pincers (§1.2.3). End-of-turn triggers and buff ticks follow.
- **Bond** (`Unit.Bond`/`BondNeeded`, `BondGrewEvent`): each own turn fought while tamed adds 1. `BondNeeded` is the class's `TameBond` (yeti and werewolf 6), else 3 + level/5. While the bond isn't full, each turn also spends one tame turn; at 0 it **breaks free** (`TameEndedEvent`): back on `Team.Enemy` where it stands, its bond kept for a re-tame. A **full bond** ("Bonded!") stops the countdown: it stays for the rest of the battle.
- **Domestication** (`Meta/Domestication.cs`): a tamed monster still standing at a victory with a full bond asks to join (`BattleReport.TameOffers`). The results screen shows "X wants to join you!" with **Accept** and **Decline**. Accepted, it becomes a roster member (`HeroSave.Domesticated`, keyed by its enemy class, so one of each) at its battle level: in the party if a slot is free, else the reserve. In battle it spawns with the party as an independent ally (`HeroSpec.Domesticated`) with the Pincer keyword. Drawbacks: no gear (`Equipment.Equip` refuses), no learned skills or Tomes, **no skill tree** (an enemy class has no `HomeTree`), half XP (`Domestication.XpFactor`) and no growth past **Lv 30** (`LevelCap`). Camp shows a TAMED badge in place of a role.
- **Look:** a tamed or domesticated unit wears a chartreuse keyline and a TAMED tag on the board, and its timeline icon has the same `Palette.Tamed` border. A "Tamed!" float with a power-up cue; "Bond n/m" each turn; "Bonded!" with a war-cry cue; "Breaks free!" with the debuff cue. The InspectCard shows a bond meter and the turns left.
- **Getting them:** the **Monster Tamer** (Support, Wild Child's portrait, home tree Alchemist) is in the **Summon Circle** pool. Kit: Tame, Feral Howl, Alpha's Challenge, Regen, Quicken, Antidote. Passives: *Pack Leader* (after it backs a pincer, a fair chance to turn the attacker it backed [Empowered]) and *Beast Sense* (a beast that attacks it directly is pushed back 1 s). Support: *Whip Crack*, Pushback 0.5 s. The **Dog Whistle** (150 G, stack 3) is sold by the Merchant.
- Not in scope: Monster Meat transformation (GG-T304). The bot never tames yet (no score for it).
- *(Tests: `TamingTests`.)*

### 25.12 Caravan upgrades (added 2026-10-10, owner direction: "upgrades … a sense of progression")

The caravan is the party's moving base — a Concord supply caravan the crew stole on tithe day ([STORY.md](STORY.md) §4), and the shops between battles (US-348 framing; story text §27.1). `caravan.json` defines upgrades bought with gold at **Camp → Caravan** (`CaravanUpgrades.Buy`, levels in `SaveData.Caravan`):

| Upgrade | Levels | Effect |
|---|---|---|
| Mana Casks | 400 / 900 / 1800 | every battle starts with +1 / +2 / +3 orbs (`BattleSetup.StartOrbs`) |
| Medic Wagon | 600 / 1400 | the inn opens **on the road** mid-expedition at ×2 / ×1.5 price |
| Armory Wagon | 350 / 800 / 1500 | repairs −20 / −35 / −50 % (`Repair.Cost(c, s, …)`) |
| Strongbox | 500 / 1200 | expedition completion bonus +25 / +50 % |
| Scouts | 300 | Adventure lists each battle's foes |
| Enchanter's Wagon | 700 / 1600 | armor enchants −20 / −40 % gold (`ArmorEnchanting.Gold(c, s, e)`; never below 1; the guided first rune stays free) — GG-T75 |
| Quartermaster's Ledger | 450 / 1100 | salvage (bag, equipped and Broken) returns +25 / +50 % of each material, rounded half up (`CaravanUpgrades.LedgerYield`) — GG-T75 |

*(Test: `Caravan_upgrades_cost_gold_and_change_how_expeditions_play`.)*

#### 25.12.1 The caravan crew and their barks (GG-T75, added 2026-11-02, owner direction 2026-10-16)

Each wagon has a crew member (`Crew`, `CrewRole` in `caravan.json`), shown on its Caravan card ("Crew: Brannoc, cask-keeper"): Brannoc the cask-keeper (Mana Casks), Sister Ilsabet the wagon medic (Medic Wagon), Old Hask the wagon farrier (Armory Wagon), Penny Tallow the purser (Strongbox), Wren and Dace the outriders (Scouts), Lio the rune apprentice (Enchanter's Wagon) and Osk the quartermaster (Quartermaster's Ledger). They are not Camp NPCs (§25.0.1): they have no station, no Talk and no join scene.

**Barks:** each crew member has a few one-line `Barks`. The Caravan screen's header and, once the Caravan station is open (`CampUnlocks.CaravanOpen`), a small "Around the wagons" window under Camp's gold/time panel each show one line with its speaker (`CaravanUpgrades.Bark(c, s, turn)`). The line changes every time the screen rebuilds. Lines come from the crews whose wagon this save has upgraded, or from every crew before the first upgrade. A bark is **flavor text, not dialogue** (§27): no portrait scene, no branching, no choices, no effects, no flags, nothing to tap. It is not an overworld either (§28).
<a id="2514-the-party-of-five-added-2026-10-11-owner-add-a-fourth-and-fifth-hero-onto-the-team--a-5-player-team-seems-about-right-for-max"></a>

### 25.14 The party of six — was five (added 2026-10-11, owner: "add a fourth and fifth hero onto the team … a 5 player team seems about right for max")

`MaxPartySize` = **5**; a new game's five are picked on `CharacterSelectScreen` (§25.15) rather than fixed. **Amended 2026-10-19 (owner, GG-T118):** the cap stays 5 but a new game starts with **three**; the other two join at story beats ([§25.17](#2517-small-start-story-recruits-gg-t118)). Recruits and rescued guests wait in reserve when the party is full. Camp shows the five in a two-column grid; class blurbs render their [keywords] as highlights.

**Amended 2026-11-06 (owner, GG-T291): the cap is six — "two attackers and two supporters for each attacker."** `Progression.MaxPartySize` = **6** (`StartingPartySize` stays **3**): one pincer's two attackers, each with two supporters chained to it ([§1.2.3](#123-supporters)). The slots fill at the existing story beats ([§25.17](#2517-small-start-story-recruits-gg-t118)): the 4th after Tithe Moors I, the 5th at Shardfall, the 6th at Anchor Wastes; the Halcyon healer (and any later recruit or rescued guest) waits in reserve. Six heroes spawn on the stage's `DefaultHeroStarts` in rows 6–7, below the enemy band, on every campaign stage, and share one item bag ([§24](#24-equipment-items-materials-currency), GG-T285). Camp shows six in its two-column grid (three rows); the Items "use on" row wraps a party of six into equal thirds. *(Tests: `PartyCapTests` — `The_cap_is_six_and_the_start_stays_three`, `A_seventh_hero_waits_in_reserve`, `Six_heroes_spawn_on_free_tiles_below_the_enemy_band_on_every_campaign_stage`, `Six_heroes_share_one_bag`; `StoryRecruitTests`.)*

### 25.15 Character select (added 2026-10-11, owner: "unlock all character and select a random cross section of heros, make character selection like a fighting came where you have a grid at the bottom then you select a tile then the full artwork appears on top and you lock in that character")

`New Game → slot` now pushes `CharacterSelectScreen` instead of calling `Progression.NewGame` with the fixed five directly. Every hero-team class in content (62, not just `StartingRoster`) is an open tile in a scrollable grid at the bottom — tiles are 164px with an even 10px margin on all sides and the row centred (`Justify.Center`), so the gutter reads the same left, right and between tiles regardless of how many columns fit or the window's width. Tapping a tile previews it — full `Art.Portrait` art, name, role badge, description — in a reveal panel above, framed in that role's colour, which **slams** into place (oversized and tilted, a `Tween.Back` overshoot down to rest plus a small landing shake and an impact cue) rather than popping in cold, matching the cut-ins' bombastic feel (bible law: no single-frame swaps). Tapping the same tile again (or "Lock In") commits it to the party strip at the top. **Amended 2026-10-19 (GG-T118):** three locks (`Progression.StartingPartySize`; a hint under the header says more heroes join along the road), not five ([§25.17](#2517-small-start-story-recruits-gg-t118)); the last lock → `Progression.NewGame(c, name, party)` (an overload taking the chosen roster; the old 2-arg form still exists for automation/tests and defaults to `StartingRoster`). `-demo` drives the identical screen itself (`auto: true`): it previews a random not-yet-picked class, pauses, locks it in, once per starting hero, so every attract-mode run shows a different squad. *(Verified: `tools/harness.py` launch, screenshot of the live grid showing even gutters, and a frame-by-frame `record` capture of the reveal panel mid-slam — tilted/oversized mid-flight, not instant.)*

### 25.16 Diagonal slip (added 2026-10-11, owner: Terra Battle's fast-swipe diagonal move — "if you moved quickly and accurately you could move diagonally between two squares… it takes skill… allow the game to skip the collision detection")

Player hero drags only (never AI — enemies/bots use the separate `StepEnemy`/`EnemyPlanner` path and never submit a diagonal step). Gated by `BattleConfig.DiagonalSlipEnabled` (mutable, default **on**; `-noDiagonalSlip` at the command line turns it off). A diagonal step is legal, destination tile fully free (no unit, no movable — never a swap, never onto anything: a glide into open space, same "two units never share a tile" invariant §(tile-conflict safety net) protects), with no softened "at most one corner" rule — the two flanking tiles' occupancy is never consulted, matching the owner's framing of the original as skipping a check rather than a designed exception. `WalkPath` (`BattleRunner.Combat.cs`, the single path both the committed move and the live drag-ghost preview run through) accepts `Coord.IsDiagonalNeighbor` alongside the existing cardinal check when the flag is on. The client (`BattleScreen.Input.ExtendPath`) only ever offers a diagonal candidate when the raw drag gesture between polled pointer-move frames is both fast (> `Tile×6` px/s) and close to a true 45° (within 12°) — a careful/slow drag always still resolves as today's safe two-cardinal-step route, so the skill gate is pointer-timing, not a toggle the player can rely on deliberately. *(Tests: `Diagonal_is_not_a_pincer_and_diagonal_step_is_refused_when_slip_is_disabled`, `Diagonal_slip_glides_onto_a_free_tile_when_enabled`, `Diagonal_slip_is_still_refused_onto_an_occupied_tile_even_when_enabled`. The engine contract is machine-verified; the client's speed/angle gesture gate is pointer-timing and was confirmed by hand, not by the automation harness — its synthetic `drag` endpoint deliberately smooths input into ~30px sub-steps for clean recordings, which never reproduces a real fast flick, so a drag through it always falls back to the safe two-step route exactly as before, with zero regression.)*

### 25.17 Small start, story recruits (GG-T118)

Owner 2026-10-19: "New Game picks a small party (2-3 heroes) instead of 5; further heroes join at story beats through dialogue." The party cap stays **5** (§25.14). **Amended 2026-11-06 (owner, GG-T291):** the cap is **6**, so the Anchor Wastes Knight now fills the sixth slot instead of waiting in reserve; only the Halcyon healer (Sage) goes to the reserve.

- **Start:** `Progression.StartingPartySize` = **3**. Character select locks in three (§25.15); the 2-arg `Progression.NewGame` (automation, tests, Test Mode) uses `StartingRoster` = Paladin, Barbarian, Cleric (Tank, Striker, Healer). `-demo` auto-picks three.
- **Story recruits:** a dialogue effect `JoinHero` (§27.1) — `Id` (preferred class) and/or `Role`. `Summon.StoryPick` resolves it so it **always brings someone new**: the named class if the player doesn't have it, else the first unowned hero of that `Role`, else one of a role the roster lacks (Healer, Tank, Mage, Support, Striker, Ranged), else anyone unowned (none left → no-op). The fallbacks skip the Summon Circle's pool and every stage guest, so those stay worth having; name order makes it deterministic, and the dialogue's `"$recruit"` speaker is bound to the same pick when it opens, so the hero who speaks is the hero who joins. The recruit arrives free at the roster's average experience (`Summon.Join`, like a Summon Circle recruit) and takes an open party slot, else waits in reserve. Separate from `RecruitHero`, which hires exactly one class and does nothing if it's owned.
- **The beats (`dialogues.json`, real narrative as of GG-T120):** after Tithe Moors I (before the next launch) — a Mage (Red Mage); at the start of Shardfall — a Support (Engineer), making five; at the start of Anchor Wastes — a Tank (Knight), making six (GG-T291); at the start of Halcyon — a Healer (Sage), who goes to the reserve because the party is full (before GG-T291 the last two did). Every path through each beat ends in the join (the join is on the last node, not a choice). They sit on top of the guest allies (§25.11) and the Summon Circle (§25.10).
- **Nobody is lost for good:** nothing in the dialogue vocabulary removes a hero.
- **Bots:** Test Mode and the balance sim never show story, but play every pending beat headlessly before a stage (`DialogueRunner.PlayPendingBeatsHeadless`, first choice) so the recruits still join; the Test Mode cycle line reports them as `story: …`.
- **Balance (`BalanceReport.Campaign_pacing_table`, 4 seeds, up to 4 attempts a stage):** the default rows now start with the trio and play story beats. Greedy finishes 4/4 (as before); greedy+camp 3/4 (was 2/4); Tithe Moors II onward plays like the five-hero start because the fourth hero arrives before it and the fifth at Shardfall. The only early stage the trio found harder was Tithe Moors I for the casual bot (30% of attempts vs 66% with five), so it was lightened (§22.7) and is now 80% with 61% HP left. The table keeps a `greedy+camp trio no-story` row (GG-T87's trio-start: no story recruits, so the Summon Circle step always has a slot to fill) and a `greedy five-start (pre-GG-T118)` row as the yardstick.
- *(Tests: `StoryRecruitTests` — the small start, JoinHero's named/role/missing-role fallbacks, the summon pool and guests left alone, a full party sends the recruit to the reserve, the `$recruit` face is the joiner, every path through every recruit beat adds exactly one hero, story beats grow the trio to five by Shardfall, recruits arrive at the roster's experience, Test Mode's headless join, JoinHero validation. Harness: `python tools/harness.py recruits` picks three on the real party select, clears Tithe Moors I and checks the recruit dialogue plays with the joining hero's portrait and the party grows to four; `uicheck` checks a new game starts with three.)*

### 25.18 Rested and the paywall hooks (GG-T232)

Owner 2026-11-05: "everything costs gold now but eventually there will need to be a 'monitization' angle that lets you recover your 'Rested' points … make everything cost 1g and take 1second to complete; but these would be scaling pay walls in the future". Clarified the same day: the hooks are **only** summoning, the Diamond lucky craft (GG-T235) and refilling Rested ("until I call it monetize, it's not"); Rested is **party-wide energy**. Every other price is unchanged. **Amended 2026-11-07 (owner, GG-T343):** Diamonds now also buy the Summon Circle's random draw ([§25.10.2](#25102-random-summon-for-diamonds-added-2026-11-07-owner-gg-t343)) — still through the Summon hook's timed job (`PaywallJob.Diamonds` records Diamonds spent instead of gold, refunded if it fizzles); Diamonds are still bought only through the LuckyDiamond hook, and there is still no store.

- **Paywall hooks (`Meta/Paywall.cs`):** `PaywallKind` { Summon, LuckyDiamond, RestedRefill }. `Paywall.Cost(kind, save)` (gold) and `Paywall.Duration(kind, save)` (seconds) read one tuning table — **1 g and 1 s for every kind today**; future scaling lands there and nowhere else. `Paywall.Tag` prints "1g · 1s" for buttons. There is no real-money store.
- **Timed jobs:** `Paywall.Start(save, kind, arg, now, out job)` takes the gold and records a `PaywallJob` (kind, arg, `StartedUtc`, `Seconds`, `Paid`) in `SaveData.Jobs` — one per kind at a time — so a long future job survives a restart. `Paywall.Progress`/`Ready`, then `Paywall.Finish(c, save, job, now)` lands the kind's effect (Summon → the hero arrives; RestedRefill → the pool fills; LuckyDiamond → one Diamond for the lucky craft, [§24.6.5](#2465-lucky-craft--the-diamond-gg-t235)) and removes the job; it refuses a job still running. `Paywall.FinishReady` lands every job whose time is up — Camp calls it on build unless one is playing on screen.
- **On screen (`App/UI/PaywallUi.cs`):** `TimedJobView.Begin(now => (result, job), label, onDone)` runs the paying start (a refusal toasts why), persists, then `TimedJobView.Run(job, label, onDone)` shows a modal (`paywall-job`) whose bar (`paywall-gauge`) fills over the job's real duration with a countdown, a coin cue (`ui.buy`) as it starts and `ui.confirm` as it lands, then toasts the result.
- **Rested (`Meta/RestedPool.cs`, `SaveData.Rested`):** one party-wide pool, `Max` **10**. Every stage launch — Adventure, Next stage, Retry, the Crucible — spends **1** (`StageLauncher`/`CrucibleLauncher` through `RestedGate`). It comes back **1 point per 10 real minutes** (`RegenEvery`), never above max; a clock that runs backwards restarts the interval. At 0 a launch is refused — "The party is exhausted — no Rested left. Next point in m:ss." — with a **Refill Rested — 1g · 1s** offer that fills the pool and then launches. Camp's side column shows a `camp-rested` window (points, meter, "+1 in m:ss", the `rested-refill` button); Adventure rows add "· 1 Rested" and the Crucible screen "Costs 1 Rested".
- **Exempt:** Test Mode saves (`SaveData.TestMode`), and `-demo`, `-testmode`, `-autobattle` and harness (`-automation`) runs set `RestedPool.Unlimited`, so bots and automation are never blocked (Camp shows "Unlimited"). A harness run opts in with `-rested`, or the dev endpoint `devrested?points=N&limit=1`.
- *(Tests: `PaywallRestedTests` — 1 g / 1 s per hook, spend and regen, refusal at 0 with the wait, Test Mode exemption, the refill job (gold up front, not before its time, fills, one at a time), no gold → refused, a running job surviving a save round trip, summoning through the job at the flat price.)*

### 25.13 Roles (added 2026-10-11, owner direction: "a healer should need to heal, a magician should have ranged spells, a tank should be able to taunt")

Every hero class has a `Role` (validated): **Tank** (9), **Healer** (8), **Mage** (6), **Striker** (31), **Support** (8), **Ranged** (2). The role sets the default bar and the learn list; caster power (§13.1.5) makes the stats matter.

**Amended 2026-10-13 — archetype sub-kits:** each role (Ranged too since 2026-11-02, GG-T18 — see below) splits into 3-5 WoW-style sub-archetypes that play differently within the same role (owner: "every combination of tanking type... find Paladin, Warrior, and Druid do things differently"). Every class keeps slot 0 (the role's shared baseline, e.g. Tank's `taunt`) and slot 2 (`item:x_potion`) in common; slots 1 and 4 carry two bespoke abilities per sub-archetype, and slot 3 is the class's own unique signature (unchanged from the original per-class kit pass). A fourth bespoke ability per cluster ("Core") is learnable but not in the starting bar, so there's room to customize without disturbing the default. Sub-archetypes: Tank — Aegis Guard (pure mitigation + cleanse), Juggernaut (knockback aggression), Warlord (party buffs), Deathguard (drain/poison); Healer — Aegis Wardens (preemptive wards), Hexmenders (damage-for-heal), Skirmish-Menders (mobile support); Support — Primal Pack, Tempo Weavers, Field Engineers; Mage — Spellblades, Field Casters; Striker — Berserkers, Duelists, Skirmishers, Combo Brawlers, Attrition, Shadowsteps, Opportunists, Brawlers. Full id list: `tools/icons.json`'s `ability` map, or `docs/USER_STORIES.md` ticket GG-T3.

**Amended 2026-10-13 — a role-wide invariant, found by live play (GG-T14):** every Tank's default bar must keep at least one damage-dealing ability. A lone tank (party wiped around it) can't pincer by itself, so if every bar slot is non-damage or on cooldown/unaffordable, it can soft-lock against a weak enemy neither side can finish — this actually happened to the Aegis Guard cluster's first cut (fixed by swapping its Capstone for its own damage-dealing Core ability). Asserted by `SynergyTests.Every_hero_has_a_role_and_kits_match_it`.

**Amended 2026-10-13 — Ranged (new 6th role):** a precision-attacker archetype distinct from Striker (melee, `AdjacentOnly`) and Mage (AoE elemental) — no ability in a Ranged hero's bar may be `AdjacentOnly`. Two classes so far, `hexsniper` and `hexwarden`, reusing the portraits of the existing `Ranged`-tagged enemies `hag00`/`hag01` (`AttackRange` 3) rather than new art — see GG-T11.

**Amended 2026-11-02 — Ranged sub-archetypes (GG-T18):** Ranged now splits into the Ranger tree's own three branches (§25.6.1, GG-T74), one class each, on the same slot convention as every other role: slot 0 the role baseline `ranged_quickshot`, slot 1 the branch Opener, slot 2 `item:x_potion`, slot 3 the class signature, slot 4 the branch Capstone (the branch Core stays learnable, off the starting bar). **Marksman** (patient single-target burst) — `hexsniper`: `marksman_deadeye_shot` / `hexsniper_piercingshot` / `marksman_execution_shot`. **Trapper** (slows, roots, silences) — `hexwarden`: `trapper_binding_shot` / `hexwarden_snareshot` / `trapper_snare_net`. **Skirmisher** (lane pressure + falling back) — new class `hexstalker` (Hex Stalker): `skirmisher_covering_volley` / `hexstalker_fadingshot` (*Fading Shot*: 14 Dark, Blinded, caster steps back 2) / `skirmisher_rapid_barrage`; added to the Summon Circle pool. The shared `ranged_calledshot`/`ranged_suppressingfire` stay in every Ranged class's learnable list (and the tree's nucleus). Art: `hexstalker` reuses the portrait/thumb of `hag02`, a common `Kiter`-brain hag mook that appears across many encounters — the same "defector" reuse as `hag00`/`hag01`, which is what this ticket was waiting for; the named miniboss `swampmistress00` is still deliberately not reused.

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

### 26.6 Units and overflow (GG-T272, GG-T271; owner 2026-11-05)

Owner: "measure things in agnostic units" and "all overflow should be avoided when possible, when not possible automatic marquee that prints out the rest and then resets back to left".

- **Units.** Design px already scale uniformly to every resolution (§26.2), so type (`TypeScale`), spacing (`Space`, 8-px scale with a 4-px half step) and fixed-size art and controls (`Size`) stay in design px. What design px don't absorb is aspect, so layout widths and vertical bands that should breathe use stage units: `Fit.W(%)` / `Fit.H(%)` (percent of the safe-area stage, `GameApp.SafeTop/SafeBottom`) or flex/`Length.Percent`, and every full-width panel sits `Fit.Edge` (24) from the stage edge. No App or USS margin, padding, size or position is a bare number any more (`tools/polish_lint.py` POL-C1). The battle board's 1080-px grid and its HUD rows stay fixed game geometry, named beside `BoardTop` (`BannerTop`, `InfoPanelTop`, `TopBarHeight`, `TimelineFrameHeight`).
- **Overflow.** Text wraps where it can. A single line that can't (a menu row's value — prices, counts, "Lv 1 · 5 waves" — or a fixed-width name) is a marquee (`Ui.Marquee`, `App/UI/Marquee.cs`): it sits in a clipping box; when the text is wider than the box it holds (`Timing.Cinematic`), scrolls left at 90 px/s with `Ease.InOut` until the end shows, holds, fades out, snaps back to the start, fades in and repeats, on pause-aware Tween time. With Reduce Motion it shows an ellipsis instead. Menu-row values never shrink below their text and never take more than 55% of the row, so the row's label wraps instead (before: prices broke as "30 / G" beside a two-line name). The layout audit counts a marquee label as handled, not as overflow, and measures wrapped text with a 1-px rounding allowance.
- **Amended 2026-11-06 (owner, GG-T305: "perfect text wrapping on all things (hyphenated or word wrap as an option), and one-line things like buttons have a restarting marquee that scrolls the info, flashes 3 times, then resets to left-most and begins again").** Nothing opts in any more: `TextFit` (`App/UI/TextFit.cs`, installed on the panel root by `GameApp`) passes over every TextElement every 120 ms.
  - **Single line** (`white-space: nowrap`) wider than its box — a button, tab, menu-row value, name, chip, badge or header — is a `Marquee` (`App/UI/Marquee.cs`, the one shared component; the party-card name and menu-row values use it like everything else). The element keeps its text and layout (its own glyphs drawn invisibly with a leading `<alpha=#01>`), and an absolute clipping overlay laid over its content box (a sibling: a TextElement with a child loses its text measure) draws the same text in the same font, size, colour and shadow. Cycle: hold at the left 1.2 s → scroll at 90 px/s (`Ease.InOut`, at least 0.7 s) until the end shows → hold 0.3 s → **flash three times** (opacity 1 → 0.2 → 1, 0.16 s each way) → snap back to the left → repeat. Only while the text overflows; text that fits is static and untouched. Pause-aware (`Tween.Dt`) but not sped up by Battle speed. Reduce Motion: an ellipsis. `Ui.Marquee(label)` now only lets a label shrink below its text; a menu-row value's 55% cap is in px from the row's width (a percent max-width on a text node resolved against the wrong base).
  - **Wrapped text** (`white-space: normal`) is laid out by `Meta/TextBreaking.cs` `LineBreaker` against the label's real width and font: greedy like the engine, then no lone word on the last line when the word before it can come down too (never stranding a lone dash or letter above it), no single-letter word ("a", "I", "&", a dash) ending a line, and an overlong word hyphenated at a break point (or split with a hyphen) instead of being cut mid-glyph. When the engine's own wrap is already clean the text is left exactly as given; otherwise the label gets explicit line breaks and a minimum width of its longest line + 2 px (so the engine never re-wraps a line by a rounding hair), re-laid when its width, its parent's width, its font size, the font or the setting changes, or code sets new text (which must hold still for one pass, so a typewriter is never re-flowed mid-line). A GlossaryText word wider than its whole row becomes a wrapped label so it hyphenates too.
  - **Settings → Text wrapping: Word wrap (default) / Hyphenate** (`SettingsSave.Hyphenate`, settings.json `game.hyphenate`), applied live. Hyphenate also breaks a word that would otherwise leave more than 20% of a line empty, at the largest `Hyphenation` point that fits.
  - **Hyphenation** (`Meta/TextBreaking.cs` `Hyphenation.Points/Hyphenate`, `Typography.Hyphenate` for text that lays itself out): deterministic English, words of 6+ letters, at least 2 letters before and 3 after a break, a vowel on both sides. Breaks only where it is sure: before known suffixes (-tion/-sion/-cian/-tial/-cial/-tious/-cious, -ment(s), -ness, -less, -ful, -able/-ible, -ing(s), -thing; a doubled consonant before -ing/-able/-ible splits between the doubles: run-ning, ter-rible, but fall-ing), after un-/dis-/inter- (before a consonant) and over-/under-/counter-/super-, and inside a consonant cluster between vowels at the first split whose right part is a legal onset, never inside ch/ck/gh/ph/sh/th/wh (con-trol, mon-ster, black-smith, lit-tle, sim-ple); a lone consonant between vowels is never split (le-mon vs lem-on is ambiguous). Never a capitalised word (proper nouns), a number, or anything inside a `[keyword]` or a rich-text colour/link/bold span. *(Tests: `TextBreakingTests`.)*
  - The layout audits treat a marquee host and its overlay as handled; automation (`clicktext`, `texts`) reads `TextFit.SourceOf`, the words as given; `textinfo?text=` reports how a text element is laid out and fitted; `setting key=hyphenate value=1` flips the mode.

---

## 27. Story crawl (no dialog)

`StoryCrawlScreen`: the theme name (Cinzel) and its `campaign.json` `Crawl` text scroll upward at 95 design-px/s over a dimmed theme backdrop; **Skip ›** or a tap anywhere ends it and replaces it with the battle. It plays on the **first** launch of any stage in a theme — from Adventure or from the results screen's "Next stage ›" (both go through `StageLauncher.Launch`); the theme is recorded in `SaveData.SeenCrawls` (and saved) when the crawl starts. **Amended 2026-10-09** (timing by scroll speed instead of a 26 s duration). Writers edit `campaign.json` only.

~~No dialog system: no character dialogue, no branching, no cutscenes.~~ **Amended 2026-10-19 (owner, GG-T117):** character dialogue now exists ([§27.1](#271-dialogue-gg-t117)); there are still no cutscenes. **Amended 2026-11-02 (GG-T75):** the caravan crew's rotating one-line barks ([§25.12.1](#25121-the-caravan-crew-and-their-barks-gg-t75-added-2026-11-02-owner-direction-2026-10-16)) are not part of the dialogue system: they are not in `dialogues.json`, have no speakers' portraits, branches, choices, effects or flags, and need no tap. The in-battle "surprise" reinforcement beat ([§14.8](#148-reinforcements-and-eggs-added-2026-10-12-owner-direction)) stays a lightweight one-line announcement card.

### 27.0 The story start to finish (GG-T231, owner 2026-11-05: "make the story from start to finish; title screen, new campaign, text scroll … a story crawl at a certain level … a boss … roll credits")

Owner clarification: a "cutscene" is a conversation ([§27.1](#271-dialogue-gg-t117)) or a text crawl — no new cinematic system. `StoryCrawlScreen` now takes any title, text and backdrop, not only a theme's.
- **Prologue.** `campaign.json` `Prologue` (`CrawlDef`: `Title`, `Crawl`, `Backdrop`) — "Tithe Day": tithe day at Saltmoss and the crew's run with a stolen Concord caravan. Plays once, right after the last lock on character select (`StoryBeats.NewCampaign`), then Camp. Never for automation, Test Mode or `-demo` saves (they don't come through New Game).
- **Level interlude.** Trigger `LevelReached` (`DialogueTrigger.Level`, 2+) fires once any roster hero is at that level. It plays on the way back to Camp from the results screen (`DialogueRunner.PendingInterlude` → `StoryBeats.Camp`), or at the next launch if that comes first (it's an ordinary beat to `PendingBeat`). A story beat may open with its own crawl (`DialogueDef.Crawl`, headed by its `Title`; story beats only, validated). Content: **Lv 10 — "Why We Ran"**, a crawl on how running turned into fighting, then a talk with the Innkeeper where the player names the crew's motive (flags `motive:coin|people|truth|crew`: coin, the holds, the truth about the tithe, each other).
- **Boss introductions.** Trigger `StageStart` (`Id` = stage) plays before that stage's battle, once. Each main-biome finale opens with its boss speaking: Tithe Moors III the Captain (who sold the holds), The Lantern Road III an oni chief on the pass (`oni02`, GG-T345; placeholder draft text until Prose, GG-T355), Shardfall III a shield-walker (`mechaarmor01`) guarding the Concord dig, Arkgrave III the Swamp Mistress (the drowned Ark's pilot), Anchor Wastes III a vat-giant (`cyclops02`) keeping the great Anchor, The Still III the Chronomancer keeping the Still's clocks (GG-T344), Halcyon III a Kiln Guard captain on the spire stair (`ceramicknight06`, GG-T350; placeholder draft text), The Choir's Cathedral III the Hierarch (its own `hierarch` class since GG-T350, speaking with the mannequin placeholder art). Each warns that "the rules don't hold here" in general terms; the mechanics themselves are GG-T234's.
- **Ending.** `campaign.json` `Ending` (`CrawlDef` + `Stage` = `cathedral-03` since GG-T350, was `cityruins-03`; backdrop `cathedral`). The **first victory** over that stage, after LevelUp and PostBattle as always, turns the results screen's Continue into: the ending crawl ("Still Here") → `CreditsScreen` in **rolling mode** (`Resources/Credits.txt` scrolls up over the ruins at 110 px/s, hold anywhere to fast-forward ×4, **Skip ›**, a coda line, `HeroicAge`) → **Title**. "Next expedition ›" is hidden on that one results screen. The save flag `story:ending` (`Story.EndingFlag`) records it; the postgame themes stay open after it and Continue from Title lands in Camp. New Game+ clears the flag, so the ending plays again each cycle. Never on a Test Mode save.
- **Bots** play `LevelReached`/`StageStart` beats headlessly like every other beat (`PlayPendingBeatsHeadless`); they never see the prologue, ending or credits.
- *(Tests: `StoryArcTests`.)*

### 27.1 Dialogue (GG-T117)

- **Content:** `dialogues.json` — a list of `DialogueDef` (id, `Title`, `Trigger`, optional `Conditions`, `Once`, `Backdrop`, `Speakers`, `Start`, `Nodes`). A speaker is a class (its name and portrait), `"$lead"` (the first party member), `"$with"` (the party member a Talk is with), `"$recruit"` (the hero the dialogue's first `JoinHero` brings — fixed when it opens, so the face that speaks is the hero who joins; GG-T118), or a `Name` + `Portrait` (`Resources/Portraits/<id>`). A node is a speaker and a line, then either `Next` (null ends it) or `Choices` (label, `Next`, `Conditions`, `Effects`); a node may carry `Effects` applied on arrival.
- **Vocabulary (the save's, not a script):** conditions `FlagSet`, `FlagNotSet`, `HasItem`, `GoldAtLeast`, `PartyHas`, `RosterHas`, `RosterLacks`, `NpcJoined`; effects `SetFlag`, `ClearFlag`, `GrantGold` (negative = pay, never below 0), `GrantItem`, `RecruitHero` (joins like a Summon Circle recruit, free; no-op if owned), `JoinHero` (GG-T118, story recruits: `Id` and/or `Role`; always brings someone new — `Summon.StoryPick`, [§25.17](#2517-small-start-story-recruits-gg-t118)), `JoinNpc` (sets the flag `npc:<id>`, which `ClearFlag` can't touch; NPCs: blacksmith, alchemist, enchantress, merchant, innkeeper), `GrantSkillPoint` (GG-T133: `Id` a hero class, or `"$with"`/omitted for the Talk partner; `Amount` default 1; paid once per dialogue per hero — the guard flag `sp:<dialogue>:<hero>` is set on the first payout and, like `npc:`, `ClearFlag` can't touch it, so a replayed Talk can't farm it); side quests (GG-T237, [§22.11](#2211-side-quests-added-2026-11-05-owner-direction-gg-t237)) add the conditions `QuestAvailable`, `QuestActive`, `QuestDone` and the effect `StartQuest`. Nothing removes a hero — nobody dies permanently. Flags live in `SaveData.Flags`; finished dialogues in `SaveData.SeenDialogues` (cleared by New Game+, like `SeenCrawls`).
- **Validation (`ContentLoader.Validate`, same severity as a dangling class id):** unknown speakers, classes, items, NPCs, themes, stages or node ids; a missing portrait (the app and dotnet tests pass a portrait lookup); a node unreachable from the start; a node with both `Next` and `Choices`; a choice list with no unconditional choice (so a player can never be stranded); a `JoinHero` with neither `Id` nor `Role`, a non-hero `Id` or an unknown `Role`; a `"$recruit"` speaker in a dialogue with no `JoinHero`.
- **Engine:** `Gaiden.Meta.DialogueRunner` walks nodes, offers the choices whose conditions hold, applies effects, and marks the dialogue seen when it ends (dotnet-tested, `DialogueTests`). `DialogueRunner.PlayHeadless` plays one to the end taking the first choice each time, and `PlayPendingBeatsHeadless` plays every beat pending before a stage — how the bots (Test Mode, `BalanceReport`) live through the story they never see (GG-T118).
- **Triggers:** story beats — `ThemeStart` (theme id) and `StageCleared` (stage id) — play once, in file order, through `StageLauncher.Launch`: after the theme's crawl, before the battle. **Amended 2026-10-19 (GG-T119):** every beat pending at a launch plays, one after another (each picked as the last ends), not just the first — a story recruit and a Camp NPC can share a stage, and the app now plays the same set the bots play headlessly. Never on a Test Mode save. **Talk** — Camp's Talk row (`TalkScreen`) lists the party and every joined NPC; each offers its own `Talk` dialogue (`"party"`, a hero class id, or `"npc:<id>"`), unseen ones first and marked **New!**; Talk dialogues repeat unless `Once`.
- **Screen (`DialogueScreen`):** ~~the speakers' portraits overlap across the top — the one talking slides in full-bright and in front, the other dims and shrinks behind; the conversation stacks below as overlapping boxes that slide in from the speaker's side, older ones dimmed, the newest typing out; the choices (or **Next ›**) sit at the bottom.~~ **Amended 2026-11-03 (owner, GG-T225: "one textbox and they keep trading whose talking by lighting up the name of the person talking"):** a conversation is **one shared text box** at the bottom (`dlg-box`); each line replaces the last in it and types out (never a stacked log), and its choices (or **Next ›**) appear inside that same box. The speakers stand on the left and right as their **whole, uncropped portraits** (`Art.Portrait`, contain-fit, feet behind the box — never the §12.1.1 Thumbnail framing), for class speakers and NPC speakers with a `Portrait` alike; a speaker with no art shows only its name plate. Each side has a **name plate** on the box's top edge. Whoever is talking has their plate lit (theme accent fill, bright border, glow) and their portrait full-bright, full-size and nudged toward the middle; the listener's plate greys and their portrait dims and steps back. The swap eases over 0.2 s with a soft slide cue; a speaker stepping on slides in from their edge. The first two speakers take left then right; a third takes the side of whoever is not talking (the one who just spoke stays put), the old occupant stepping off first. A tap finishes the line or advances; **Skip ›** rushes to the next choice — choices are never skipped. Effects toast as they land. `-demo` plays it through on its own. Camp's Talk list shows each NPC's whole portrait; heroes keep their Thumbnail face like every other hero list.
- Sample content: a Lantern Road merchant met at the start of the Tithe Moors (every branch ends with him joining), four story-recruit beats (GG-T118, [§25.17](#2517-small-start-story-recruits-gg-t118)), a Talk with the merchant (hires a sellsword for 300 G) and a generic party chat. **Amended 2026-10-19 (GG-T119):** the beat after Tithe Moors I now meets the Innkeeper, and beats after Tithe Moors II, at the start of the Shardfall and of the Arkgrave meet the Blacksmith, Alchemist and Enchantress (§25.0.1); each NPC has a Camp Talk. **Added 2026-11-02 (GG-T133, owner 2026-10-19 "talk to Heroes, answer right, gain a skill point"):** every hero class has an "Advice" Talk quiz (`talk_quiz_<class>`): the hero voices a worry that fits their faction ([STORY.md](STORY.md) §3; rewritten in GG-T341, each hero with a fresh blind spot), and of three answers only the one that names that hero's own blind spot carries `GrantSkillPoint` (the two wrong ones feed the blind spot or miss it, and earn nothing). Quizzes aren't `Once` — they replay, but the point pays out only the first time it's answered right. An unseen quiz outranks the generic party chat in the Talk row (hero-specific before `"party"`).
- **The story: *Under Morrow* (GG-T341, 2026-11-07; replaces GG-T120's story).** [docs/STORY.md](STORY.md) is the single source for every word of story — crawls, dialogue, quests, caravan barks, mercenary pitches, unit lore, credits. The crew are Saltmoss runaways who fled tithe day in a stolen Concord supply caravan; Morrow, the moon, hangs too low, the Pale Concord tithes every hold's aether to feed the Anchors, and the Hierarch drinks the surplus. The main themes are **The Tithe Moors** (`greenvalley`), **The Lantern Road** (`lanternroad`, added by GG-T345, §22.3.2), **The Shardfall** (`desert`), **The Arkgrave** (`swamp`), **The Anchor Wastes** (`cave`), **The Still** (`still`, added by GG-T344, §22.3.1), **Halcyon** (`cityruins`) and **The Choir's Cathedral** (`cathedral`, the finale, added by GG-T350, §22.3.3); the postgame themes are **The Moors Beyond the Stills**, **Deep Shardfall**, **The Drowned Arks**, **Under the Anchors** and **The Underspire** — the road after the Hierarch, open on purpose. Each `*_party_talk` `ThemeStart` beat carries one piece of the plot (the quota was a lie → the moon is made of what it pulls → the Concord grounded the Arks → the beams carry the surplus to Halcyon → the tanks hold the Hollow, stored → give it back). The Camp NPCs are the Saltmoss Innkeeper, a Free Hold Blacksmith who won't forge for the Captain, a Street splice-cutter Alchemist, a Choir-runaway Enchantress and a Lantern Road Merchant (speaker name "Lantern Merchant"). Ids, triggers, conditions, effects and node graphs are unchanged; only words and NPC speaker names changed. Stage display names, unit `Description`/`Lore` and a separate `hierarch` boss class are the next phase of GG-T341.
- **PPP prose universe seed (GG-T137, 2026-10-20) — retired (GG-T341).** `docs/ppp-universe/` and `docs/story-draft-spider-town.md` belonged to the retired GG-T120 story and are deleted (git keeps them); [STORY.md](STORY.md) replaces both as the prose source.

## 28. No Overworld

There is no world map. Stage navigation is the Adventure list ([§22.3](#223-adventure--the-stage-list)). **Confirmed 2026-10-20 (owner, GG-T115):** permanent, not contingent on GridGameCaravan's traversal prototype picking a demo — the earlier pitch for a navigable overworld and terrain gates (design-draft-overworld-and-dialogue.md §2/§2.1, withdrawn; the draft is deleted in GG-T341, git keeps it) is not coming back. The journey is understood without one: the party is travelling, and the story is told by text crawls ([§27](#27-story-crawl-no-dialog)) and dialogue ([§27.1](#271-dialogue-gg-t117)). GridGameCaravan, the traversal lab that would have fed this, is spun off as its own project ("TownWalker"), not a Gaiden prototype.

## 29. Open Design Questions

Resolve a question → write the answer into the right section → delete it here.

### 29.1 Macro loop / run structure
1. **Permadeath / NG+** — **Answered 2026-10-19 (GG-T114, owner):** never in the normal game — "no one dies permanently, that's lame". Defeat returns to Camp at full HP; fallen heroes recover; a fallen guest returns (§25.11); recruits and dialogue-joined heroes are never removed. **Ironman** is a *planned, opt-in* mode ("leave the door open but not right now") and is **not built**: every "unit is lost" decision already routes through the one rule `Permadeath.Enabled` (`Gaiden.Meta`, always `false`; fed to battles as `BattleSetup.Permadeath`), so Ironman only has to switch that on. Still open: what else Ironman changes (fallen heroes too? one save slot?).
2. **Tutorial / onboarding** — a guided first battle? Partly answered: Settings has "How to play" and the first two campaign stages show idle-time move hints (§9). A scripted tutorial is still open.

### 29.2 Timeline feel (new with the act-advanced clock)
3. **Frozen length** — should Frozen survive into the next action (Duration 2, or tick at the bearer's own boundary)? Today it ends with the action it lands in (§8.3).
4. **Time bank** — should banked seconds persist until the next enemy turn instead of clearing at control return (§3.1.8)?
5. ~~**ActionTicks**~~ — superseded by the held turn (GG-T188, §2.0): a turn now lasts until the next enemy arrival. Open instead: should a slot use (not just a drag) end the turn, and is `MinTurnSeconds` = 2 s the right floor? (balance pass GG-T191)

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
- **Assets:** `Resources` loading is sanctioned (backdrops at the art's resolution, 1536×3072 JPEG, ~4.5 MB each as textures (GG-T353); thumbs 256², icons 64²; music mp3, SFX ogg/wav).
- ⬜ Not yet built: the on-device profiling pass.

---

## 31. Accessibility

- **Color-blindness:** mana has one color by design (filled vs empty reads by brightness); buff pips carry letters. **Colorblind palette** (`SettingsSave.ColorblindMode`) remaps buff pip colors.
- **Motion:** 🟡 **Reduce motion** (`SettingsSave.ReduceMotion`) zeroes lunges, shakes and the death-portrait jitter and dims the Clutch flash (`BattleScreen.Fx`); **Battle speed** ×1/1.5/2/3 shortens every beat.
- **Touch targets:** slots (≥ 211×196, growing with the stage, GG-T252) and tiles 180² design px clear 44 dp at every supported scale; a dragged hero lifts above the finger.
- **Tooltips** (*GG-T252*): hold-to-read InspectCards on every icon, unit, slot, status, timeline icon and item/talent row ([§9](#9-the-hud)); taught by the tutorial's "Hold to read" lesson, and once by a tip for anyone who skipped it (`hint:inspect` flag).
- **Readability:** 40-px body text, white on deep-blue windows, HP as gauge + numbers on the panel. **Text wrapping** (*GG-T305*, Settings → Text wrapping: Word wrap / Hyphenate) and the shared one-line marquee (Reduce Motion: an ellipsis) — [§26.6](#266-units-and-overflow-gg-t272-gg-t271-owner-2026-11-05).
- **Audio:** every important event also has text. 🟡 Music/SFX volume cycles in Settings; `MuteMusic`/`MuteSfx` exist in the save and are honored by `Audio.Apply` but have no toggle.
- **Menu themes (added 2026-10-11, owner: "the gradient on all the menus is generated... make multiple Themes... updated in Settings and remembers it"):** `Art.MenuTheme` is two chosen base colors (`ColorA`/`ColorB`); the accent used for every window's border (`Ui.Window`) is never hand-picked — it's the complement of the two base colors' circular-mean hue, pushed toward full saturation/brightness since it frames thin borders, not large fills (`Art.MenuTheme`'s constructor). Presets (`Art.Themes`): **Classic** (the original deep blue, now just the default preset), **Cyberpunk** (magenta → neon cyan, gold-green accent), **Ember** (mindattic.com's coral → teal), **Verdant** (MindAttic.Launcher's green → near-black). `SettingsSave.MenuTheme` persists the pick; "Menu theme" in Settings cycles it (`Art.ApplyTheme`, called from `GameApp.ApplySettings` on every load and every Settings change) and repaints the shared `Art.Window`/`Art.WindowDark` gradient textures **in place** — every already-open window updates with no rebuild, since they all hold the same `Texture2D` reference.
- **Theme USS classes (GG-T185, epic W):** a preset may also name a root USS class (`Art.MenuTheme.UssClass`). `Ui.ApplyMenuTheme` (called from `GameApp.ApplySettings` right after `Art.ApplyTheme`) puts the active theme's class on the UI root and removes every other theme's, and re-tints every open `.window`'s frame; Theme.uss scopes each such theme's frame, tab, button, header and toast rules under that class at the end of the file, so the whole look switches live with no rebuild. A theme with a class owns its window frame in USS (`Ui.Window` sets no inline accent border for it). Adding a theme is one row in `Art.Themes` plus one `.theme-<name>` block in Theme.uss. **Slate** (`theme-slate`) is modeled on Chrono Trigger's default window style: a gray-blue slate gradient body, a light bevelled frame (light top/left edge, darker bottom/right), white text, slate tabs and buttons with the same bevel; primary (gold) and danger (red) buttons keep their colors. The harness sets it with `call setting key=menutheme value=Slate`. **Dark** (`theme-dark`, GG-T186) is the DocFX modern dark docs / mindattic.com look on MindAttic.Components' `web/themes/Dark/dark.css` tokens: flat `#13161b` windows (both gradient stops the same) on a `#0d0f12` stage, `#2a2f3a` hairline frames on windows, tabs, header, buttons and toasts, one `#6ba3e8` accent (selected rows, active tab's top edge, primary buttons with dark text), `#e8eaf0`/`#9aa0b0` text and no text shadows; gold, good/bad and rarity colors stay semantic. `python tools/harness.py sweep RUN Dark` sweeps every menu screen in it.
- **Fonts (added 2026-10-11, owner: "allow a selection of different Fonts to change look and feel"):** `Fonts.Presets` (`Assets/Gaiden/App/Services/Fonts.cs`) — **Classic** (Outfit, the original/default), **Lato**, **Roboto**, **Play**, **Montserrat**, **Inter**. Calibri and Aptos were named but are Microsoft-proprietary (not freely bundleable); Inter fills that "clean modern UI sans" role instead. "Attic" isn't a real distributable family; Montserrat fills that slot (its Google Fonts build is a variable font whose default instance renders a bit lighter than Regular — a known, purely cosmetic quirk, not a bug). Only the **body** font is selectable; the Display font (Cinzel, used on ~7 headline/name labels) stays fixed across every preset. `SettingsSave.FontName` persists the pick; "Font" in Settings cycles it the same way "Menu theme" does (`Fonts.ApplyFont`, called from `GameApp.ApplySettings`). Unlike themes, no texture-repaint trick is needed: `unityFontDefinition` is an inherited UI Toolkit style property, so re-applying the body font to the UI root (`Fonts.UseBody`) cascades to every element that doesn't explicitly override it, instantly, with no screen rebuild. Roboto/Montserrat/Inter are Google's current variable-font TTFs (no static cut is published any more); Unity's dynamic SDF pipeline (`FontAsset.CreateFontAsset`) renders their default instance correctly.
- Status: colorblind 🟡 · reduce motion 🟡 · volume 🟡 · menu themes 🟡 · fonts 🟡 · safe area ⬜ · tooltips ✅ (GG-T252).

---

## 32. Document Discipline

- Every gameplay-affecting change updates this bible (add, amend or verify); complex mechanics are written here first, then coded. Numbers recorded here follow `BattleConfig` / content changes.
- Removed mechanics are deleted (or marked RETIRED with a pointer, as GG-LAW-3/4). Git is the history.
- A deviation from the GridGame2026 bible gets an inline **Amended YYYY-MM-DD:** note and a bullet in [`AMENDMENTS.md`](AMENDMENTS.md); status changes go to [`USER_STORIES.md`](USER_STORIES.md) with evidence.
- What the game IS lives here; why we deviated lives in AMENDMENTS; what is pending lives in USER_STORIES + §29; what a session debugged lives only in that conversation.
- Readers: the owner (sanity check), future sessions, unattended agents (the spec), new contributors. A section one of them can't read without context is a bug.

— end of document —
