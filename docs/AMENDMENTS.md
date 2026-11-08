---
codex: 1
project: GridGameGaiden
code: GG
layer: amendments
status: living
updated: 2026-10-09
---

# GridGame Gaiden — Amendments to the GridGame2026 bible

> Every deliberate deviation of GridGame Gaiden from the GridGame2026 `docs/BIBLE.md`, one bullet
> each, with rationale. The [BIBLE](BIBLE.md) marks each inline as **Amended 2026-10-09:**. Items
> the new code simply has not built yet are not amendments; they stay as spec marked ⬜ in the bible
> and [USER_STORIES](USER_STORIES.md). "Owner" = confirmed by the project owner.

## Architecture & process

- **Pure simulation + event playback** (owner). `Gaiden.Core` is engine-free and deterministic;
  `BattleEngine.Apply(command) → events` is the only way a battle changes; Unity only plays events
  back (`BattleScreen.Play`). *Why:* the old Unity-managers-and-coroutines game could not be tested
  or reasoned about and was declared unplayable; a pure core runs under `dotnet test` in seconds.
- **One empty scene, everything built in code; UI Toolkit + `Theme.uss`; no prefabs, no scene
  builders** (owner). *Why:* removes the builder/`.unity` drift class of bugs entirely.
- **GG-LAW-3 (builders) and GG-LAW-4 (SerializeField/Resources/Instantiate guardrails) retired**
  (owner). *Why:* nothing left to guard; `Resources.Load` is now the sanctioned loader.
- **New laws GG-LAW-9..12**: Core has no UnityEngine; content is JSON validated by
  `ContentLoader.Validate`; glyph coverage (`tools/check_glyphs.py`); "done" requires harness
  evidence (dotnet tests + bot soak + automation screenshots) (owner). *Why:* make every claim checkable.
- **GG-LAW-7: harness evidence replaces `DebugManager.Demo_*` buttons.** *Why:* a script that plays
  the game is a better demo than a button and can run unattended.
- **GG-LAW-8: `GameApp.FitStage` replaces `AspectGuard`.** Any aspect fits the fixed 1170×2532 stage
  with bars; no snap list. *Why:* UI Toolkit scales one panel; the snap table and its camera rects are unnecessary.
- **Content is data** (owner): `Resources/Content/*.json` (classes, abilities, buffs, items, recipes,
  drop tables, stages, campaign, traps, bounties, summon, enchants) replaces the C# `*Library` /
  `*Data` classes. *Why:* content edits without code, validated at load and exercised by catalog tests.
- **Fade 0.2 s** (old 125 ms). *Why:* one router-wide transition that also hides screen rebuilds.
- **No Addressables, no chiptune fallback, no particle VFX yet.** Art/audio load from `Resources`;
  a missing clip is silent; feedback is code-drawn. *Why:* fewer moving parts for the rebuild; VFX stay ⬜.
- **AnnouncementWindow + combat feed → one action banner.** *Why:* the banner is an awaited beat in
  the event player; the feed stays ⬜.
- ~~**Settings are per save slot** (no profile-level settings). *Why:* profiles are gone.~~ **Reversed
  2026-11-02 (owner, GG-T99):** settings belong to the player profile again — see the 2026-11-02 entry below.

## Timeline & time

- **Act-advanced clock** (owner): the clock only runs when the player commits an action; each action
  = `ActionTicks` 20 ticks = 2.0 s; Shield runs to the next enemy turn; refusals and no-op drags cost
  nothing. Rewrites §0.2 "thinking time is game-clock time" and §2. *Why:* playable on a phone in any
  rhythm; decisions, not reflexes; deterministic replay.
- **Integer ticks (10/s), 1000-IP bar, Zone fixed at the rightmost 35%** (owner). *Why:* exact,
  reproducible math (old: floats, ZoneU 0.25–0.35).
- **Speed-derived open-track rate (`Speed × 1.6` IP/tick) + shared Zone crawl (30-tick crossing)** (owner).
- **Pushback/stun/hasten math as implemented** (owner): push `min(0.40, 0.18·(0.5+depth)·strMult)`,
  `strMult = max(0.5, 1+(STR−10)/40)`, no 0.55 floor; stun `max(1, round(8/(1+AGI/50)))` ticks;
  Hasten +u·1000 IP and clears stun. *Why:* integer-tick port with a cap instead of a floor.
- **Only enemies have turn icons.** Heroes act on any action. *Why:* the act-advanced clock makes
  hero icons meaningless.
- **Spawn stagger = random 0–250 IP head start** (replaces u=0 + queue delay; `Queued/PushedBack/
  Resolving` icon modes dropped). *Why:* same visual spread with no extra state.
- **Arrival ties: casts before turns, then lower entry id.** *Why:* deterministic ordering.
- **Hasten on a casting hero rushes the cast.** *Why:* heroes have no turn icon to push.
- **Shield never auto-presses.** *Why:* there is no real-time window to expire.
- **Optional Terra Battle drag timer (4 s, Settings, off by default)** (owner). *Why:* restores hand
  pressure for players who want it without making it the default.
- **Time-banked orbs come from drag-timer seconds left: 1 Blue per 1.25 s, max 3**, minted at the
  next enemy turn in the same action (old: seconds before the next enemy trigger, 1 per 3 s) (owner).
  Current consequence: no enemy turn in that action → nothing banked.
- **Countdown badge ("acts in N moves")** on every enemy. *Why:* the act-advanced clock needs a
  Terra Battle–style read of "how many moves do I have".
- **All content spells have a 1.5 s base cast** (WIS+INT scaled), so they usually resolve inside the
  action that casts them. *Why:* keeps casts on the timeline/interruptible without stalling play;
  open question §29.

## Combat rules

- **Supporters: any ally adjacent to an attacker; strike at 50%.** No line-of-sight/axis test. *Why:*
  readable at a glance; 50% keeps supporters from out-damaging the pincer.
- **Pincer order: dropped hero's pair first, then chains, then nearest to the last link.** *Why:* the
  player's own move always resolves first.
- **Respite spin dropped**; dead-line pairs are skipped silently.
- **Humanoid enemies resolve enemy-team pincers on their turn** (no orbs). *Why:* the old bible had
  enemies seek pincers but left resolution implicit.
- **Enemy melee targets the adjacent hero with the lowest current HP.** *Why:* makes wounded heroes
  feel threatened; deterministic.
- **Boss shove sends each hero to the tile the body vacated on its lane** (no cascade; aborts only on
  a non-hero blocker). *Why:* a 2×2 step can never push a hero off-board, so the abort case vanishes.
- **Boss phases trigger at the start of the boss's own turn** and are data (`ClassDef.BossPhases`).
  *Why:* phase changes land as a readable beat, not mid-combo.
- **Charges: fixed 2.5 s (not stat-scaled); single-target charges follow the hero; a charging enemy
  still moves/attacks on later turns.** *Why:* telegraphs read the same every time; dodging is by
  interrupting.
- ~~**Charge cancel orb color = first cost letter, else damage-type color.**~~ (moot since generic mana, 2026-10-10)
- **Fire on a Frozen target thaws it and steams off the resulting Wet in the same hit.** *Why:*
  falls out of running the expire chain before the Wet check; reads as "melted and evaporated".
- **Frozen (timeline-halting) ticks at the shared turn boundary; hero turn buffs and skill cooldowns
  tick once per action.** *Why:* a halted icon never reaches its own turn; heroes have no turns.
- **Traps are triggered only by heroes**, by any way of entering the tile; excluded from teleport,
  revive and spawn. *Why:* enemy-laid traps are a hero hazard.
- **Default coin drop ≈ ½ XP ±20%** when a drop table authors no coins (none do). *Why:* one curve
  instead of hand-tuning 16 tables.
- **Coins are gold**, credited on victory only; `GoldTracker` removed. *Why:* one number to reason about.
- **Every party hero (alive or fallen) receives the battle's full XP total** (not split). *Why:*
  reserves of the active party never fall behind; simple.
- **Wounds: survivors keep HP, fallen → 1 HP, a level-up → full HP; defeat/retreat → full; escape
  keeps wounds.** *Why:* level-ups feel like a reward; escape is a cost.
- **Consumed items are deducted at commit on every outcome** (victory, defeat, retreat, escape).
- **Potion = single-ally heal (PickActor)**; damage items hit one enemy with a bonus vs a tag; new
  item knobs: GrantsOrbs (Ether), CuresBuff/CuresAll, Revives (Revival Charm), Escape (Smoke Bomb),
  OutOfBattleOnly (Tent). *Why:* every consumable in content now does something.
- **Double Attack / Counter Attack are passives** (`AbilityKind.Passive` on enemy classes): second
  pincer strike at ½; adjacent counter at ½, no chains.
- **Equipment durability resets on unequip** (per-instance durability not tracked). *Why:* inventory
  is id → count; known simplification, open question.
- **Weapon BaseDamage adds to physical offense; armor rating unused; armor never wears in battle.**
- **Effective Speed = SPD + gear SPD + ½ gear AGI.**
- **A hero class placed in a stage wave fights as an enemy** (the Vampire Lord is the recruitable
  `vampire` class). *Why:* one class table.
- **Mug currently hits twice** (fixed Mug hit + its row's BaseDamage). Recorded as current truth and
  a defect (US-110), not a design change.
- **Silence currently affects hero slots only** (enemy charges ignore it). Recorded as current truth (US-109).

## Meta & Camp

- **Merged Camp hub** (owner): the FF-style `HubScreen` replaces the 7 vendor scenes, `VendorNavBar`
  and StageSelect-as-home. *Why:* the old §25.9 goal; one menu is faster than scene hops.
- **Inn added** (owner): hub row + Alchemist Heal tab; 0.5 G per missing HP across the whole roster.
- **Learn abilities for gold** (owner) on the Abilities screen: class list + general spell catalog;
  price 120 + 80 per orb, skills 300; the bar accepts only Known abilities. *Why:* a gold sink and
  build expression beyond fixed class presets.
- **Three save slots replace profiles** (owner); fixed party name "Lightbearer"; JSON with
  migrations; dictionaries/sets serialize natively. *Why:* simpler and testable. **Amended 2026-11-02
  (GG-T99):** profiles are back, each holding its own three slots (2026-11-02 entry below).
- **Stage list shows unlocked stages newest-first and collapses locked ones into one teaser row**
  (owner). *Why:* the frontier is always on top; no wall of locked rows.
- **Stage names are "<Theme> I/II/III"** (owner).
- **Bounties get their own Camp screen** (plus a progress strip on Adventure).
- **Blacksmith Upgrade tab** (the old unused upgrade recipes now have a UI); **Enchant runs through
  `Station: Enchant` recipes**.
- **Summon recruits arrive at the roster's average XP and auto-join the party when there is room.**
  *Why:* a new recruit is usable immediately.
- **Party must keep at least one member.**
- **Camp item use** (potions on a hero, Tent on everyone). *Why:* Tent had no battle use.
- **Retreat (pause menu) and Escape (Smoke Bomb)** as battle exits. *Why:* a way out without
  waiting for a wipe.
- **Story crawl marks the theme seen when it starts**, scrolls at 95 px/s, and also plays when
  entering via "Next stage".
- **Persist after every successful Camp action** (old: commit on vendor exit). *Why:* no lost purchases.
- **First-battles hint** (until the third stage is cleared): after 5 s idle the bot's best pincer
  move is shown. *Why:* answers part of the old tutorial question without a tutorial system.

## Presentation

- **Debuff icons are up to 3 letter pips** (no radial ring, no cycling).
- **Projectiles are a moving dot, straight or arced** (other motions pending).
- **Ability info shows in the actor panel while targeting** (long-press tooltips pending).
- **HUD is absolute design-pixel layout in `BattleScreen`** (old `HudLayout` row constants gone).

## 2026-10-10 — parity batch

- **Black and Green orbs now have uses.** The old catalog had no spell costing B or G, so Black/Green
  heroes minted dead orbs. *Change:* Poison costs (B)(B); new Green spells **Regen** (G — heal 4/s for
  6 s via the new `regen` buff, `BuffDef.HealPerTick`) and **Rejuvenate** (GG — heal 30 + cleanse).
- **Distinct kits for the 55 default-kit hero classes.** Each gets a color identity (from its name/role)
  and a color-matched preset + learnable pool; ninjas carry Teleport. The bible's seven seeded loadouts
  (§23.2) are unchanged.
- **Ranged archetype built** (§14.2): `ClassDef.AttackRange`; hags and the Swamp Mistress shoot the
  lowest-HP hero in a clear straight lane within 3 tiles and keep their distance.
- **Mechanical archetype built**: `ClassDef.ImmuneBuffs` (Mechanical/Construct: poisoned, sleep → "Immune!")
  plus Poison resistance 0.
- **Buff stacking policies** (§8.6): `BuffDef.Stacking` = Refresh (default) / Stack / Ignore / Upgrade.
- **Armor wears in battle**: −1 durability per landing enemy hit; at 0 it breaks and its bonuses vanish
  (no shatter damage — that stays weapon-only). Repair works as for weapons.
- **Relic passives** (§24.1.1 named passives): `PincerDamageBonus`, `RegenPerTurn`, `StartsProtected`,
  `BankCapacityBonus` (the §3.1.4 knob, max +4 slots), authored on Bloodstone, Jade, Sunfire, Phantom, Stars.
- **Ability-bar weapon swap** (old US feature): a bar slot can hold `weapon:<id>` from the bag; using it
  swaps weapons (costs a turn; incoming weapon at factory durability); the save follows the swap.
- **Battle end is checked after every action**, not only after kills/turns (robustness).
- **UI**: mute toggles; distinct projectile curves; long-press ability card; radial buff timers with
  >3-buff rotation; idle hint also suggests spells/GUARD.
- **WebGL build** (§11.4): uncompressed Minimal template; saves flush to IndexedDB via `GaidenSync.jslib`
  (`FS.syncfs`), and `SaveStore` falls back from `File.Replace` where unsupported.
- **Bosses are tagged `Boss`** (cyclops, naga, vampire) so their frame and timeline icon read purple
  (heroes never take boss styling).
- **Respite** restored (§13.1.1): a later link whose whole line already fell spins and shows "Respite".

## 2026-10-10 — design decisions taken (owner may overrule; each is one constant or flag)

- **Spells never miss but can crit** (§13.1.2 backlog closed): crit chance = ½ the caster's physical crit %,
  ×1.6 damage, a hero spell crit mints a bonus orb like a pincer crit. `BattleConfig.SpellCrit*`.
- **Inventory: no global cap** (§29 #19 closed): items stack to `MaxStack`; battle loot beyond it is
  auto-sold at the merchant price and reported on the results screen.
- **Per-instance durability** (§24.5 closed): unequipped worn gear keeps its durability and repair count
  (`SaveData.Worn`); equipping takes a fresh copy first, else the least-worn.
- **Guided first battle** (§29 #6 closed): the first battle of a new save shows a 4-step coach card
  (drag → pincer → spell → timeline) with immediate move hints; skippable; `SaveData.TutorialDone`.
- **No permadeath; New Game+** (§29 #1 closed): after clearing the final stage, camp offers New Game+:
  progress resets, roster/gear/gold kept, enemies +10 levels and coins +50% per cycle.
- **Audio is fully licensed**: every SFX is original (synthesized by `tools/make_sfx.py`); all music is
  Kevin MacLeod CC-BY 4.0 ("Heroic Age" replaces the Pixabay victory track, "Heartbreaking" replaces the
  unsourced defeat track). `tools/check_assets.py` fails the gate if any cue or track is missing.
- **VFX are procedural UI Toolkit effects per element** (fire, ice, lightning, poison, holy, dark, arcane,
  heal) plus status auras, replacing the old prefab VFX library.

## 2026-10-10 — generic mana (owner direction)

- **Owner:** "The colored mana system doesn't add anything, just make them generic mana orbs that can be
  globally spent on actions."
- `ManaBank` is now a capped count; every orb is identical; spell costs are integers (the former letter
  counts, 1–3). Removed: `ManaColor`, `ClassDef.ColorAffinity`, color-matched spending, the Colorless
  wild orb (crits now mint a plain bonus orb), per-source orb colors, orb letters and the colorblind
  orb tint. GG-LAW-6 (one shared, visible, capped pool) still holds. Bible §3.1 rewritten.

## 2026-10-10 — pincer close-up cut-in (owner request)

- **Owner:** GridGame2026 "had a technique for zooming in on the characters so it was like a close up
  shot with some shifting like a hand held camera." Ported from `PortraitManager.SpawnPair2DRoutine` /
  `Portrait2DInstance` (same slide curve, speed ×1.75, four entry patterns, lanes flanking the line) and
  extended with hand-held Perlin sway, a slow portrait push-in, a world dim and a board push-in. Plays on
  the first pincer of an action only, so chains stay quick. Settings → "Pincer close-ups" (default on).

## 2026-10-10 — battle unit frames match the menus (owner request)

- **Owner:** "make the borders around the Heros and Enemies in the battle match the menu system that
  exists outside the battle; same UI for all." Unit frames now use the menu portrait frame (white
  rounded border, window gradient). The old gold/red/purple outer borders became a thin inner keyline
  so team and boss still read at a glance.

## 2026-10-10 — pixel-art enemy timeline icons (owner request)

- **Owner:** "use imagemagick to reduce and enhance the enemy icons so that they stand out and look like
  pixel art versions of the full enemy … so the user can tell from a glance who's the next attacker."
  Turn icons now show the whole enemy as a 36 px, 16-colour sprite with a sticker outline; the next
  attacker's icon gets a gold ring and a slight lift.

## 2026-10-10 — timeline readability, enemy personalities, cast countdowns (owner requests)

- "The enemies don't need turn numbers on them because the timeline is there" → board countdown badges removed.
- "a very fast enemy [can] move past a slow enemy on the windup" / "set up the fast enemies to bypass enemies on
  the line" → enemy speed tiers 8 / 11 / 15 / 18 and an overtake lane on the timeline.
- "smarter enemies will attack the weakest enemy, dumb enemies will attack the closest" → targeting
  personalities (Closest / Weakest / Threat / Squishy / Erratic), per family in classes.json. Replaces the old
  blended score (distance + HP fraction − INT × threat). Bot balance moved slightly easier mid-campaign.
- Timeline icons centred on a thinner line, opaque, masked to the circle; debuff badges on circles + tiles.
- "casting spells requires a casting period which should also be on the timeline" → casts travel 0 → 1 over
  their own cast time on the same line (was: constant reference pace below the line).
- "use the enemy border as a radial countdown … each spell has a different color … cycle" → `CastRing`.
- Enemy icon method recorded in docs/PIXEL_ICONS.md (owner: "save and record the method").

## 2026-10-10 — story text removed (owner)

- **Owner:** "get rid of all this story about the missing sun and the UNDEARTH, its terrible, just put in lorem
  ipsum for now." All five theme crawls are lorem ipsum; the default profile name is "Player". The replacement
  story is the caravan premise (US-348).

## 2026-10-10 — cast time on the caster; spell VFX arc; biome tiles (owner)

- "instead of putting casting time on the timeline have it show on the caster" → cast icons removed from the
  line; the countdown ring moves to the caster's frame; the target keeps a spell chip.
- "always a particle effect … Hero1 'drawing energy' then cast burst, spawns an electrical particle that curves
  to the space above an enemy where it spawns the lightning strike" → `SpellVfx` (gather → burst → curving mote
  → element strike from above the target).
- "each area should use a different tile style" → `tools/make_tiles.py` recolours the original tile per biome.

## 2026-10-10 — overlap beats, one status slot, burn stacks, party of six (owner)

- Timeline circles stay on the centre line; overlapping circles take turns on top (no lane lift).
- One status slot per actor that flashes through buffs/debuffs; the card explains each.
- "fire has multiple stacks … not sure if they should tick when it's the user's turn and they are idle; it kills
  the wait-and-decide component" → burning stacks to 3; DoT ticks only on the act-advanced clock, so idle
  thinking is free (decision: keep the clock act-advanced; recorded here so the owner can overrule).
- "Up to six party members need to be visible" → `MaxPartySize` 4 → 6; Camp party is a 2-column grid.


## 2026-10-11 — synergy, staging, press feel, scarcer mana (owner)

- "it all needs to synergize … a healer should need to heal, a magician should have ranged spells, a tank should be
  able to taunt enemies, powerups should sometimes drop, money should accumulate on kills" → hero **roles** with
  matching kits (§25.13), **Taunt** (§14.1.1), **caster power** — INT/STR/WIS now scale hero spells, skills and heals
  (§13.1.5), **Cleave / War Cry** for strikers, **pickups** (§13.3.1). Gold on kills already existed (coin burst).
  Calls the owner may overrule: spells stay unlimited-range (the mage's identity is INT damage, not reach); existing
  saves keep their bars, only new heroes get the role kits.
- "button presses need feedback like depress, or confirmation screens when deciding to cast a spell … standardized"
  → `Ui.Pressable` on everything tappable (§11.5); select → confirm → act with a confirm card (§4.4.1), switchable
  in Settings.
- "if I clicked poison and an icon just appeared on an enemy it would feel meaningless … particle effects around the
  actor, a loading bar fills (while game is in motion), cast splash, a projectile on a curve, a splash attack, then a
  bubbly green poison effect which coincides with the poison debuff" → staging (§6.2). **Deviation:** the 2026-10-10
  ring that *shrinks* on the caster stays; a charging bar that *fills* is added beside it.
- "mana needs to be a more rare resource … the number of participants in the attack increases the multiplier needed
  to earn a mana bubble" → the mana charge meter (§3.1.9). Tuned with `BalanceReport`: greedy bot clears the
  campaign 4/4; casual+camp bot 2/4 (was 1/4 at the start of the day). To keep Mireholt Fens II fair under scarcer
  mana, its second wave drops the second lurker (hag + naga only). Mana Shard pickup gives 1 orb.
- "portrait slides … action packed … supporters pop in and out" → action cut-ins (§9).
- "the tiles transparency contrast is too high … the background needs to be clearly visible" → lighter, lower-contrast
  tiles; the stage background is dimmed less.
- "take screenshots and then examine them for artistic integrity" → `tools/artreview.py` + the automation `record`
  endpoint (fixed-step frame recorder: `Tween.FixedStep`), producing per-scenario contact sheets. First review pass
  fixed: flat cut-in band → feathered band + bust window + dimmed stage; opaque area slabs → soft bloom; dot
  projectile → glowing orb with comet tail; status text in the status colour; bolder taunt rings; item icon on the
  confirm card; billboard dodges top-row action.

## 2026-10-11 — full tutorial, five-wave first battle (owner)

- "there needs to be a full tutorial with explanations and examples, the first battle needs 5 waves of increasing
  difficulty" → Green Valley I is five waves (levels ramp 1 → 2; test `The_first_battle_has_five_waves_of_rising_difficulty`)
  and the coach card became ten lessons (§9). The first battle is now ~43 actions for a strong bot — above the 15–40
  pacing target, accepted for the teaching battle.

## 2026-10-11 — a brain for every enemy family (owner)

- "come up with ai for every single enemy unit, they are all different in their own unique ways" → 20 brains (§14.6)
  over the 41 families; with targeting and kit no two families play alike (test). The toad's tongue now slows (it
  matched the frog exactly). Smarter foes made the campaign harder, so enemy strikes carry ×0.9
  (`EnemyStrikeMultiplier`) and the finale's second wave drops a bat and its reaper is level 5; result: greedy
  bot 4/4, casual+camp bot 2/4, finale 18–33 % for the casual bot (it was 9 % this morning).

## 2026-10-11 — 20-wave first battle, battle records, potion stacks, party of five (owner)

- "make the first battle 20 rounds with increasing difficulty and record everything … keep the records of all moves
  as JSON" → §22.7, §22.6. Records drove fixes: skirmishers dart one tile (×0.8 glancing strike) instead of two;
  evasive brains make a last stand when they're the only fighters left (no stalls); the bot values Taunt by the
  allies it covers; late waves stay at level 4–5 and grow in numbers instead.
- "give each character 2 stacks of full life potions and unlock the slots … two stacks of 5x" → X-Potion ×5 twice per
  hero; all slots open from the start (§4.7). **Deviation:** progressive slot unlocks retired.
- "add a fourth and fifth hero … a 5 player team seems about right for max" → party cap 6 → 5; Red Mage + Engineer
  join the starting party (§25.14).
- Call the owner may overrule: **a defeat keeps its XP** (§22.2) — without it a learning player could never get past a
  20-wave first battle.
- Harness: `tools/harness.py` now uses port 7787 (`GAIDEN_PORT`); GridGameCaravan's player also listens on 7777.

## 2026-10-11 — armor plates and corpses (owner)

- "some enemies are armored and need to take damage from that side to break it off" → directional plates (§13.5).
  Calls the owner may overrule: spells bypass plates (a mage's answer to armor); the plate turns to face the enemy's
  target each turn.
- "add in the corpses for some things so they get in the way like Darkest Dungeon" → corpses (§14.7). Calls: corpses
  don't hold up a wave and rot after 6 actions (a 20-wave battle would otherwise silt up the 6×8 board).
- Balance after both: greedy bot 4/4 campaign and 6/6 first battle (~348 actions); casual+camp 1/4.
- Art review fixes: plates sit on the tile rim (they covered the HP bar); the billboard dodges upward over the banner
  (it landed on the heroes), and also while a spell charges on a top-row target. UI gate 70/70.

## 2026-10-11 — final art review pass

- Every art-review scenario re-recorded on the final build (caster, sweep, confirm, poison, fireball, potion, taunt,
  armor, corpses) and reviewed. Fix: the first-link pincer sweep now uses the standard cut-in band (slim, tilted,
  feathered, white core) along each portrait's lane, so every cut-in shares one look. UI gate 70/70 (the long-press
  check now recognises the "Charges …s on the timeline" wording).

## 2026-10-15 — Merchant Bargain shelf (owner direction, US-246, GG-T38)

- "a rotating 'Used / Bargain' shelf selling weapons and armor at 35–60% price but with only 10–40% of max
  durability left, clearly labelled with a durability bar before purchase — cheap power now, real risk of
  shattering mid-combat later, a deliberate early-game gamble." A third `ShopScreen` tab (`Meta.Bargains`, §25.1)
  rolls `ShelfSize` = 4 eligible weapons/armor deterministically from `SaveData.BargainRotation`; the shelf is
  stable until the rotation advances (once per finished battle, win or lose) and replays identically from the
  same save. Buying files the rolled durability into the existing per-instance `SaveData.Worn` tracking (§24.5,
  US-246 — already in place from the 2026-10-10 batch), so the piece equips worn rather than factory-fresh.
- Not yet built (tracked under GG-T54): teaching `MetaBot`/`CampRoutine` to shop the shelf and avoid gear close to
  shattering.

## 2026-10-15 — Recipe discovery (GG-T36)

- "recipes start hidden and are learned from blueprint/recipe-scroll drops, from first pickup of a recipe's
  rarest input, or from station level-ups; the Forge/Brew lists show discovered recipes plus '???' silhouettes
  for ones you're one input away from. Gives drops a reason to matter beyond raw mats." `RecipeDiscovery`
  (§24.6.2) treats a recipe as discovered the moment its station reaches its tier (§24.6.1) — no separate
  level-up event needed, since `Crafting.IsUnlocked` already derives that from `SaveData.StationXp` — or
  earlier via `SaveData.DiscoveredRecipes`, filled by reading a new `ItemType.RecipeScroll` item (`Items`
  screen "Learn recipe") or the first-ever pickup of the recipe's single rarest-rarity input
  (`BattleFactory.Commit`'s loot grant). Discovery only gates visibility in the Blacksmith/Alchemist recipe
  lists — `Crafting.Craft`'s own gold/inputs/tier checks are unchanged, since a tier-reached recipe is
  discovered by definition and a scroll-taught recipe ahead of its tier already fails the existing tier gate.
  Three scrolls ship in content (`scroll_recipe_sword_tempering`, `scroll_recipe_holy_water`,
  `scroll_recipe_holy_enchant`) as rare drops off `goblinthug00`/`ghost`/`demonlord`.

## 2026-10-15 — Brew instead of buy (GG-T44)

- "every healing/cure consumable the Merchant/Alchemist sells gets an Alchemy recipe made purely from field drops,
  and the brewed version is meaningfully cheaper than buying." Every Alchemy brew already existed for these items
  (§24.6) — the gap was that only Healing Potion was `SoldByVendor`. Hi-Potion, X-Potion, Revival Charm, Antidote,
  Eye Drops, Remedy and Tent are now sold too (§25.1), so there's an actual convenient-vs-efficient choice instead
  of brew-or-nothing.
- "raise shop potion prices … so gathering and mixing your own is the efficient path while buying stays the
  convenient one." Shop prices for X-Potion (250→500), Revival Charm (300→450), Remedy (150→250) and Tent (200→300)
  rose so every one of the eight brews before per-unit gold cost sits at 2.5–5× cheaper than its shop price;
  Hi-Potion/Antidote/Eye Drops/Healing Potion already cleared that bar unchanged. Recipe gold costs, tiers and
  reagents (§24.6.1) were left untouched. *(Test:
  `Every_sold_healing_or_cure_potion_is_meaningfully_cheaper_brewed_than_bought`.)*
- `CampRoutine`'s potion restock now brews the Healing Potion recipe before buying, falling back to the Merchant
  only once reagents run out (§25.3.3). *(Test: `CampRoutine_brews_potions_instead_of_buying_when_it_has_the_reagents`.)*

## 2026-10-15 — Bot & balance support for builds (GG-T54)

- "teach MetaBot/CampRoutine to … salvage worn-out gear and avoid bargain gear near shatter." `CampRoutine.Run`
  now scraps every `Broken` fragment on sight and salvages (instead of repairing) any equipped weapon/armor whose
  repair has crossed the existing "uneconomical" line (§24.5, cost ≥ `BaseCost`) — the same trade the Blacksmith's
  "Salvage instead" row already offers a player (GG-T42), just taken automatically. The gear-upgrade pass also now
  shops the Merchant's Bargain shelf (§25.1, GG-T38) for a slot upgrade once owned gear and Forge recipes come up
  short, but skips any offer below `CampRoutine.MinSafeBargainDurabilityFraction` (25%) — a player can weigh the
  shelf's 10-40%-durability shatter gamble, an automated buyer can't, so it only takes the safer half of the range.
  *(Tests: `CampRoutine_salvages_broken_fragments_on_every_visit`, `CampRoutine_salvages_an_equipped_piece_instead_of_an_uneconomical_repair`,
  `CampRoutine_never_buys_a_bargain_offer_too_close_to_shattering`, `CampRoutine_buys_a_safe_bargain_offer_for_an_empty_slot`.)*
- "brew instead of buy when it has reagents" was already built for the one consumable the bot stocks (Healing
  Potion, GG-T44) — left unchanged here since there's nothing else in `CampRoutine`'s restock loop to generalize it to.
- **Not done — blocked on GG-T50:** "teach MetaBot/CampRoutine to spend skill points (home tree first, sensible
  off-tree picks)." §25.6's GG-T49 amendment already says it plainly: "Until GG-T50 lands there is no tree to spend
  points on yet." `HeroSave.SkillPoints` accrues but nothing in content or code consumes it — no `skilltrees.json`,
  no `ClassDef` home tree, no spend API — so there is nothing yet for a bot to spend into. GG-T54's own dependency
  list (GG-T38, GG-T44, GG-T49) doesn't include GG-T50, but the ticket text assumes the tree exists; it doesn't.
  This piece is left for whoever picks up GG-T50 (and the bot-spend logic can follow as a small addition once it
  lands) rather than building a parallel/placeholder tree here that would conflict with that ticket's content schema.
- **Retuning:** reran `BalanceReport` (`Campaign_pacing_table`) after the above. `greedy+camp` still clears the
  whole campaign (4/4 seeds) with a healthy difficulty gradient intact (postgame stages mostly 44-80% win rate, not
  trivialized), and win rate/actions/HP-left improved modestly over plain `greedy` from the salvage/bargain gear
  bump — no stalls, no trivialization. Since the only consumer of skill points, XP and reagent drops that actually
  changed this round is the (unaffected) potion restock loop, XP rates, skill-point rates, reagent drop chances and
  shop prices were left as-is; a real retune of skill-point income/spend belongs with GG-T50, once there's a sink
  to tune it against.

## 2026-10-19 — Death-portrait dissolve restored (owner, GG-T96)

- **Reverts a silent drop.** GridGame2026 answered every death with `PortraitManager.Dissolve`: the actor
  hid and its full portrait sprite, at 196/255 alpha, shook around the tile while shrinking ×0.99 and
  fading 1% a frame. Gaiden never ported it; its `DeathAnim` only faded, shrank and spun the board token
  for 0.3 s, so the uncropped enemy art was never seen in battle. Owner 2026-10-19: "the only way to see
  the complete artwork of these enemies... a quintessential reward for killing an enemy" — and, the same
  day, "only supposed to show the portrait as a single image, not the entire actor prefab tile". Restored
  as a single image of the bare portrait on the board's fx layer (§6.2), non-blocking like the original.
- **Deviations from the original, each deliberate:** the motion is time-based (same curves as 60 fps,
  scaled by Battle speed) rather than per-frame; it starts at 196/255 as the ticket specifies (the
  original `DissolveRoutine` actually reset alpha to 1 on its first frame and ran ≈1.7 s); near a board
  edge the portrait is nudged inside the board instead of hanging under the HUD; Reduce Motion drops the
  jitter but keeps the fade. The old token fade/shrink/spin is gone (the token just disappears).


## 2026-10-19 — The Crucible: a defeat that keeps its rewards (owner direction, GG-T95)

- "a way to gain xp and loot for the saved campaign … a waiting for the bus kind of mode." New Camp row and
  battle mode, §22.10: endless escalating waves of families the save has met, fought from camp in the normal
  save, never mid-expedition.
- **Deviation from §22.2 (failure path):** everywhere else a defeat keeps only hard-won XP and a retreat keeps
  `RetreatKeepFraction` (50%) of the gold and loot. In the Crucible a **wipe, a retreat (Leave the Crucible) or
  a Smoke Bomb escape keeps everything earned so far** — all gold, every item drop, the full XP total and its
  level-ups — and the party is still restored to full HP like a defeat. That is the point of the mode: it is a
  reward faucet you leave whenever the bus comes, not a stage you can fail. It cannot be abused to skip the
  campaign's losses because it never changes campaign progress, unlocks, `LastStage` or the expedition, and is
  closed while an expedition is under way (so its full-HP reset can't heal the road). Gear wear from the run
  is kept too (the wear happened). *(Tests: `A_crucible_wipe_or_retreat_keeps_every_reward_and_restores_the_party`,
  `A_crucible_run_never_touches_campaign_progress_unlocks_or_the_expedition`.)*
- Bounty kills count in the Crucible (on any ending), unlike §22.4's victory-only rule for stages — same
  reasoning: every kill in the Crucible is kept.

## 2026-10-19 — One actor per tile, every path (owner direction, GG-T97)

- Owner: "there can never be two actors occupying the same tile after sliding is over." The 2026-10-11 self-heal
  (`EnforceTileInvariant`) stays only as a release-build last resort; every position write now goes through one
  chokepoint (`Relocate` / `MoveMovable` / `AddUnit`) and the test suite runs with `BattleEngine.StrictTiles`, which
  throws instead of healing. Rule and per-path guarantee: [BIBLE §1.1.3](BIBLE.md).
- **Delayed reinforcements wait for room** (§14.8 said nothing about a full board; the code silently dropped the
  spawn): a reinforcement with no free footprint stays queued and retries every tick, giving up only when no other
  enemy is left to free a tile, so the wave can still end.
- **A Revival Charm with nowhere to land is refused before it's spent** ("No room!"), instead of consuming the charm
  and announcing "No room!" afterwards.
- A party member with no free start tile anywhere (only possible on a stage walled with blocks) sits the battle out
  off the board instead of being stacked on the taken start tile.

## 2026-10-19 — Dialogue engine (owner direction, GG-T117)

- The bible's "no dialog system" ([§3](BIBLE.md), [§27](BIBLE.md)) is withdrawn: portrait dialogue with branching choices
  plays at story beats between battles and from Camp's Talk row ([BIBLE §27.1](BIBLE.md)). Still no world map and no
  cutscenes. *Why:* owner — the game is four kinds of screen: fighting, town, reading a crawl, or talking with people.

## 2026-10-19 — Nobody is lost for good (owner direction, GG-T114)

- Owner: "no one dies permanently, that's lame." The 2026-10-10 guest rule ("fall at any point → lost forever",
  [BIBLE §25.11](BIBLE.md)) is withdrawn: a fallen guest is revivable and announced "X has fallen!"; standing at a victory
  (including after a revive) recruits them; down at the end, the offer stays open and they reappear on that stage.
  Older saves' `LostGuests` entries are ignored, so guests lost before this change come back.
- **One permadeath rule, off.** Every "unit is lost" decision (guest record, battle exclusion, Summon Circle pool, the
  fall announcement) reads `Permadeath.Enabled`, which is always `false`. *Why:* a future opt-in Ironman mode is planned
  ([BIBLE §29.1](BIBLE.md)) and should only have to flip that one rule. Audit found no other permanent loss: defeated
  heroes recover at full HP, heroes who fell in a victory return at 1 HP, recruits and dialogue-joined heroes are never
  removed, and in-battle summons are per-battle by design.

## 2026-10-19 — Small starting party, story recruits (owner direction, GG-T118)

- **A new game starts with three heroes, not five** ([BIBLE §25.14–§25.15](BIBLE.md), [§25.17](BIBLE.md)). The party
  cap stays five; the rest join at story beats through a new dialogue effect, `JoinHero`, which always brings a hero
  the player doesn't have yet (named class, else the same role, else a missing role), on top of guest allies and the
  Summon Circle. *Why:* owner — the party should grow along the road instead of arriving complete.
- **Green Valley I lightened for the trio** ([BIBLE §22.7](BIBLE.md)): one reinforcing soldier instead of two, and a
  level-4 finale instead of level 5. *Why:* the casual bot's win rate there fell from 66% to 30% with three heroes;
  later stages already match the five-hero start because the fourth and fifth heroes join before Green Valley II and
  at Sandsea Reaches.
- **The bots play story beats headlessly** (Test Mode, `BalanceReport`): they still never show story, but the beats'
  effects apply, so the story recruits join their parties too.

## 2026-10-19 — Camp NPCs join along the way (owner direction, GG-T119)

- The Camp's services were always there ([BIBLE §25.0](BIBLE.md)); now the Merchant, Blacksmith, Alchemist, a new
  Enchantress and the Innkeeper are people met on the journey, and each one's Camp row is hidden until they join at a
  story beat (dialogue `JoinNpc`, flag `npc:<id>`). Schedule and rules: [BIBLE §25.0.1](BIBLE.md). *Why:* owner — the
  camp should fill up with the people you meet.
- **Enchanting moved out of the Alchemist** into the Enchantress's own station ([§25.3.4](BIBLE.md)). Deviation: her
  recipes still unlock with the Alchemist's station level rather than a ladder of her own, because every Enchant recipe
  is Master tier and a ladder fed only by enchanting could never be climbed.
- **The Alchemist's Heal tab is gone** — it was the Inn under another name, and resting now belongs to the Innkeeper.
- `CampRoutine` and the balance simulation use only joined services; the simulation meets NPCs by playing each launch's
  story beat. Test Mode saves auto-join everyone (no story plays there). Save version 4 joins everyone on older saves.
- **Every pending story beat plays at a launch**, one after another, instead of only the first ([BIBLE §27.1](BIBLE.md)):
  with GG-T118's recruit beats sharing stages with the NPC beats, one-per-launch pushed the Blacksmith back a whole
  expedition, and the bots (`PlayPendingBeatsHeadless`) already played them all.
- The GG-T117 sample beat after Green Valley I lost its `NpcJoined merchant` condition (it is now the Innkeeper's join
  beat, and a join beat must always fire).

## 2026-10-20 — Overworld withdrawn; traversal lab spun off as TownWalker (owner direction, GG-T115)

- **Owner:** "no world map - it takes away from a quick sit-down game." The overworld/terrain-gate pitch
  ([`docs/design-draft-overworld-and-dialogue.md`](design-draft-overworld-and-dialogue.md) §2/§2.1) is
  withdrawn, not deferred: [BIBLE §28 "No Overworld"](BIBLE.md) stays permanently, and is no longer
  contingent on GridGameCaravan settling on a traversal demo. [BIBLE §3](BIBLE.md) is confirmed
  unchanged on "no world map" (only "no dialog system" was ever struck, by GG-T117).
- **The journey is already told without one:** the party is travelling, and the story is carried by the
  per-theme text crawl ([§27](BIBLE.md)) and the dialogue engine ([§27.1](BIBLE.md), GG-T117–120). *Why:*
  owner — the game is four kinds of screen: fighting, in-town buying/upgrading, reading a text crawl, or
  talking with people (NPCs, party); a fifth, overworld kind of screen isn't wanted.
- **GridGameCaravan is spun off as its own project, "TownWalker,"** independent of Gaiden — not a
  prototype feeding a future Gaiden port. Nothing from `Lab.Core`/`WalkerRig2D` moves into
  `Gaiden.Meta`/`Gaiden.App`; the design draft's "porting follows Gaiden's laws" section is withdrawn
  along with the overworld it described.
- **Unaffected:** §1 (the biomechanical-plague/Longmother narrative merge, already landed GG-T120), §3
  (the dialogue engine, already landed GG-T117–120) and §4 (mid-battle recruitment, still open) — none of
  the three ever depended on an overworld.

## 2026-11-02 — Player profiles return, on Unity.Shared (owner, GG-T99)

- **Reverses "Settings are per save slot"** and amends "Three save slots replace profiles". A player
  **profile** (`Profiles/<Name>/`, the shared `com.mindattic.unity.shared` package's `ProfileStore`) owns one
  `settings.json` and its own three slots (`Saves/slot1-3.json`). Settings left `SaveData`: the shared
  `common` block holds volumes, mutes, reduce motion and language; Gaiden-only settings (battle speed, drag
  timer, colorblind palette, pincer close-ups, confirm-before-acting, menu theme, font, difficulty) live under
  `game`. Changing a setting writes the profile's settings.json, not the loaded slot, and the settings apply on
  the Title too. *Why:* the owner wants several players (or several runs with different preferences) on one
  device, the same way every MindAttic Unity game keeps profiles. [BIBLE §15.1, §15.2, §15.4](BIBLE.md).
- **Migration:** on first launch the old `persistentDataPath/Saves/slot*.json` are copied into a `Default`
  profile, the most recently played slot's settings seed its settings.json, and the old folder is kept as
  `Saves.migrated`. Nothing is deleted; deleting a profile moves it to `Profiles/.trash`.
- **Automation:** `-saveRoot DIR` now holds `DIR/Profiles`; `-profile NAME` picks (or makes) a profile;
  `-demo`/`-testmode` play under a throwaway profile in the temp cache, never the real ones.

## 2026-10-22 — 100+ distinct enemy groups (owner direction, epic W, GG-T184)

- **Added:** `encounters.json`, 182 authored enemy groups (one family's mix + a named formation of fixed
  enemy-half tiles + a level). [BIBLE §4 content table, §22.3 campaign, §22.10 Crucible](BIBLE.md).
- **Campaign:** the 19 waves that repeated an earlier wave now name a group of the same family; content
  validation fails if two campaign waves are ever the same foes, levels and tiles again.
- **Crucible / endless:** each wave is an authored group rather than a random handful of the family, so a
  run shows its formations and no group repeats until its family's same-size groups are used up. Wave
  sizes, levels and the one-family-per-wave rule are unchanged.

## 2026-10-21 — The timeline runs while a hero is held (owner direction, GG-T188)

- **Held turn replaces the act-advanced "2.0 s per action".** The clock still stands still while the player
  looks, but picking up a hero runs the timeline in real time; the turn lasts until the next enemy icon
  reaches ACT, when the hero is dropped where it is (forced drop, even with no step taken) and that enemy
  acts. Dropping earlier ends the turn early: every accepted command now runs the clock to the next enemy
  arrival (`AdvanceUntilEnemyTurn`), except an action that clears a wave (the player opens the new wave)
  and a board where no icon can arrive (fixed `ActionTicks`). *Why:* the owner wants Terra Battle's
  hand pressure back as the core rhythm — one move per enemy turn, with the remaining time shown as the
  next icon's distance to ACT and a drain bar on the held hero. Real-time floor `MinTurnSeconds` = 2 s;
  bots drag at `BotSecondsPerStep` = 0.25 s a tile. Countdown badges now count turns, not 20-tick
  actions. Tuning (and whether a slot use should end the turn) is the GG-T191 balance pass; Guard banking
  the unused time is GG-T189. [BIBLE §0, §0.1 pillar 2, §0.2, §2.0, §2.10](BIBLE.md).

## 2026-11-02 — Held-turn / Guard / support balance instrument (GG-T191, partial)

- **Every tuning number is now in `BattleConfig`:** the held turn (`MinTurnSeconds` 2, `BotSecondsPerStep` 0.25), the
  Guard bank (`GuardChargePerSecond` 10, `GuardMaxCharge` 40) and the bot's Guard habit (`BotGuardBelowScore` 0.5,
  `BotGuardChance` 0.2, previously literals in `HeroBot`). Class support magnitudes are content data (each class's
  `support` block), not code constants.
- **Soak metrics:** `HeldTurnSoakReport` (Tests/Core.Tests) plays the campaign with greedy and casual bots and prints,
  per stage, win rate, share of the turn window used, Guard share of turns, forced-drop share, mana charge from Guard
  vs pincers vs other sources, support heal / buff / haste / cleanse totals.
- **Values are unchanged.** No numbers were retuned: the soak has not been run (agents do not run tests) and the
  targets still need to be agreed with the owner, so there is nothing yet to justify a change. Next step: run the
  report, propose targets (e.g. turn used 50-80 %, Guard 5-20 % of turns, Guard mana a minority of pincer mana, no
  stage under the §0.3 win band), agree them, tune, and record the chosen values and why in the BIBLE (§2.0, §2.5,
  §29.2 item 5). The BIBLE edit was held back because `docs/BIBLE.md` carries another agent's uncommitted changes.

## 2026-11-03 — Nothing spawns on a pickup; pickups move like actors (owner direction, GG-T227, GG-T228)

Owner: "no two units or powerups can share the same tile, make sure nothing spawns on a tile thats already occupied
and make sure powerups and spawns get moved the same way actors get moved."

- **Was (§1.1.3, §13.3.1, GG-T129):** pickups didn't block — a hero collected one on entry, an enemy could stand on
  it, and `FreeForSpawn` ignored pickups, so a new wave, reinforcement, guest, recruitable, egg, snake segment,
  blink, revive, trap or trail could appear on one.
- **Now (GG-T227):** `FreeForSpawn` also requires `ItemFree`: nothing appears on a pickup (footprint-aware).
- **Now (GG-T228):** a pickup moves like a movable tile. Heroes still collect on entry; any other unit entering its
  tile shoves it onto the tile it just left (lane-aware for 2×2s, behind a snake's tail), else the nearest open
  item-free tile, else it fades — `PickupMovedEvent`, animated as a slide with a cue. A unit on a pickup is a
  `FindTileConflict` conflict. Enemy AI never targeted pickups, so it is unchanged.

## 2026-11-05 — The mana economy, tempered (owner direction, GG-T247)

Owner: "gaining mana is too generous since I see the whole bar filled almost in every battle … the conditions under which you can gain mana, and the cost of certain abilities need to be tempered." §3.1.7/§3.1.9 amended.

- **Fullness damping (new rule):** earned charge × `1 − 0.7 × bank fullness`. Explicit grants (items, pickups, Steal, gear, time bank, carried) are untouched. *Why:* it targets a bar that tops out without making the first spell of a wave harder to reach.
- **`PincerChargePerHp` 120 → 105, `CritCharge` 12 → 10, Mana supporters × 0.5** (`SupportManaFactor`; they were the second-biggest source). Interrupt, amplifier and Guard are unchanged, as they reward skill. The cap stays 12, since a lower cap would show a full bar *more* often.
- **Costs:** Mass Heal 1 → 2, Cleansing Vigil 1 → 2, Flowing Current 1 → 2 (party-wide or AoE heals for one orb), Red Mage Fury 2 → 3 (an AoE cross for two). Single-target heals and utility stay at 1.
- **Mana Siphon** (`deathguard_mana_siphon`), named by GG-T246's report as a mana generator, makes no mana. It is a free 8-damage Skill with a guaranteed Silence. It was the most-cast Skill, so its cooldown goes 3 → 4.
- **Measured** with `ManaEconomyReport` (GG-T246's bot, every campaign stage once per seed, 6 seeds, 150 battles per bot):

| | Before | After |
|---|---|---|
| Greedy: full bank, per-battle average of turns | 6.6% | 4.0% |
| Greedy: minted orbs faded at the cap | 24.0% | 10.3% |
| Greedy: battles that filled the bar | 16.7% | 8.7% |
| Greedy: wins | 40 | 45 |
| Casual: wins | 7 | 8 |
| Casual: turns holding 2+ orbs (pooled) | 26.0% | 21.2% |

  The casual bot spends the moment it can, so it rarely holds 2 orbs, before or after. Its 30% affordability target was never met, and this change lowers it by about 5 points.

## 2026-11-05 — Rested and the paywall hooks (owner direction, GG-T232)

- **Was:** stage launches were free; the Summon Circle cost 250 + 250 × recruits so far and the recruit arrived at once.
- **Now:** a party-wide Rested pool (max 10, back 1 point per 10 minutes) pays for every launch; refilling it, summoning and (GG-T235) the
  Diamond lucky craft are **paywall hooks**, priced and timed by `Paywall.Cost`/`Duration` — 1 g and 1 s today — and played out as a timed
  job. *Why:* the owner wants the monetization slots wired but inert ("until I call it monetize, it's not"); every other price stays as it
  was. Bots, Test Mode, `-demo` and harness runs are exempt from Rested. See BIBLE §25.18.

## 2026-11-05 — Mercenaries on contract (owner direction, GG-T233)

- **Was:** nobody ever leaves the roster (GG-T114, §25.17/§27.1): recruits, guests and dialogue joins are permanent.
- **Now:** a hero hired on the Mercenaries board is on a **contract**: paid upfront for a negotiated number of battles, counting down on
  every battle fought whether they were used or not; with one battle left they reoffer at Camp (renew at a loyalty discount, finish the
  contract, or part ways), and at zero they **leave the roster** (gear handed back to the bag) until hired again. *Why:* owner: "they stay
  for a negotiated amount of battles at a price; whether you use them or not; they will reoffer their services when they are about to leave
  and you can choose to renegotiate or part ways." Scoped as its own contract system: the dialogue vocabulary still has no effect that
  removes a hero, and no story recruit, guest or summon is ever a merc. Ordinary gold, not a paywall hook. See BIBLE §25.10.1.

## 2026-11-05 — Remains count down, and the campaign curve is retuned (GG-T261)

- **Was:** remains (gravestone, bone pile, §14.5) spawned at their owner's level with the full stat formula and raised it on their
  **first** turn; every body segment of a snake left its own marker.
- **Now:** remains have 25 % of the slain owner's MaxHP (`BattleConfig.RemainsHpFraction`), rise on their **third** turn
  (`RemainsTurnsToRise`; the first two announce "stirs… N"), and a snake leaves one marker, not one per segment. *Why:* since GG-T188 each
  action runs the clock to the next enemy turn (§2.0), so a marker left by the last foe standing rose inside the very action that made it
  (battle records: median 0 player actions between `RemainsLeft` and `RoseAgain`, 27 rises in one Green Valley III wave) and the party never
  had a turn to break it; at the owner's level a gravestone also had ~110 HP. The casual bot won 0 % on Green Valley III and every Frostmaw
  Caverns and Veshker Ruins stage. The rule is unchanged in spirit: destroy the remains or the undead comes back.
- **Content:** a dozen wave edits smooth the curve (BIBLE §22.3 lists them); the GG-T234 rule-breaking bosses are untouched.
  Casual-bot wins, before (6 per stage) → after (8 per stage, rotating parties): Green Valley I–III 67/100/0 → 75/88/100 %, Sandsea Reaches
  I–III 67/83/83 → 50/88/75 %, Mireholt Fens I–III 33/0/17 → 88/38/62 %, Frostmaw Caverns I–III 0/0/0 → 62/100/88 %, Veshker Ruins I–III
  0/0/0 → 75/62/25 % (Veshker Ruins III's second wave was trimmed after this run). Polish POL-G1 17.4 → 31.0, POL-G2 71.4 → 78.6.

## 2026-11-05 — Faster waves: enemy HP scale, ambush patience (GG-T262)

- **Was:** enemies spawned with the full `Formulas.MaxHp` and armor plate, struck at `EnemyStrikeMultiplier` 0.9, and an Ambusher waited
  for a hero within 2 tiles indefinitely.
- **Now:** enemies spawn with `BattleConfig.EnemyHpScale` **0.45** of the MaxHP formula and of their plate, strike at **1.2**, and an
  Ambusher lies in wait for at most `AmbushPatienceTurns` (3) of its own turns. *Why:* POL-G6 wants 6–20 player actions per wave; the
  records had a median of 34 (30 for the casual bot alone, cave-03 wave 1 at 68, a lone ambushing naga holding Mireholt Fens II's last wave to
  116). Scaling HP down and strikes up keeps the threat while shortening the fight; the greedy bot now clears a wave in a median of ~10
  actions, the casual bot (half its moves random) in ~19. Polish POL-G6 41.4 → 56.3; dimension G 66.3 → 67.4. POL-G4 (ability use share)
  fell 65.3 → 53.2 since shorter fights fire fewer abilities — that is GG-T263's to answer.

## 2026-11-05 — Ability impact is measured, not flat (GG-T264)

- **Was:** `records.impact` credited every buff, debuff, haste, delay or move a flat 6 per application, so a taunt, a ward or an
  empower read as dead weight (POL-G5) whatever it did.
- **Now:** `records.credit_utility` follows each buff/debuff after it lands and credits the ability that applied it, in HP:
  - **damage-up:** the extra share of the buffed ally's damage
  - **guard or taunt:** damage the multiplier prevented, plus every hit a taunter absorbed
  - **ticks:** damage and heal ticks at face value
  - **frozen or sleep:** each skipped enemy turn counts as one enemy turn's average damage
  - **slow:** the enemy turns it took away
  - **blind:** expected misses
  - **silence:** half an enemy turn per turn it held

  Repositioning, traps, cleanses and orbs keep the flat 6.
- **Tuned on the measured numbers:**
  - **Blood Harvest:** 3 → 2 orbs so it gets cast, and its drain trimmed 10/14 → 7/10. At 10/14 for 2 orbs it measured 73 impact/use against a Spell median of 28.
  - **`empowered`:** 2 → 3 Turns. Its skills measured about 0 because the buff ran out before the hero struck again.
- **Result:** G5 74.7 → 78.0 (206 battles). The remaining lows are pure utility buffs that genuinely do little against 45%-HP foes. They show as measured values now, not as metric artifacts.

## 2026-11-05 — Every ability earns its slot (GG-T263)

- **Was:** 58 of 124 equipped abilities fired in under 25% of their battles, 27 never.
  - **Cleave** (the striker baseline, 24 damage, 2-turn cooldown) beat every bespoke 8–16-damage finisher.
  - **`protection`** (×0.85 for 1 Turn) was too light to spend an action on.
  - **Some spells were priced above their effect.**
  - **Hero haste-only skills did nothing**, because heroes have no turn icons (GG-T268).
- **Now:** see BIBLE §7.2.
  - **Cleave** 20.
  - **Finishers** raised to 12–26 damage.
  - **Four spells** cost 2 → 1.
  - **Weak spells and supports** buffed.
  - **Haste-only tempo skills** also empower.
  - **New `ward` buff** (×0.70 for 2 Turns) for every hero guard skill. Guard and the enemy rally keep `protection`. It gets a status icon and a status style.
- **Result:** G 69.3 → 74.9 (12 casual battles per stage plus the greedy sweep, same seeds): G1 50.0 → 66.7, G4 59.7 → 73.4, G7 61.5 → 76.9.
  - **G2 85.7 → 78.6:** swamp-02's lone-hag wave still deals about 1000 damage to a casual party, and the stage drops from 42% to 17%. That's stage tuning.
  - **G6 53.5 → 50.4.**
  - **A Cleave of 16 was tried first:** striker parties lost their main damage and swamp-02 fell to 0/12.

## 2026-11-05 — The late-campaign mana flood is gone; the 2+ orbs target is retired (GG-T265)

- **Was:** GG-T255 measured the greedy bot's bank full on 94% of cityruins-03 turns (87% wasted), and the casual bot holding 2+ orbs on only 21% of turns against GG-T247's ≥ 30% target.
- **Now (measured, no rule change):** after GG-T247's tempering and GG-T262's 45% enemy HP, the bank is full on 0% of turns on every stage, cityruins-03 included, with 0% wasted.
  - The bots bank about 0.09 (greedy) and 0.05 (casual) orbs per player action.
  - The casual bot holds 2+ orbs on 22% of turns.
- **Retired:** the "2+ orbs held ≥ 30%" target. It measures hoarding, and a bot that spends the moment it can rarely holds two whatever it earns.
  - **Tried:** `PincerChargePerHp` 105 → 200 to buy a spell-per-wave earn rate.
  - **Result:** that raised the casual bot only to 0.08 orbs/action (the fullness damping caps it), and flooded the Green Valley stages, where that bot doesn't spend (full on 36–55% of turns). G fell 74.0 → 73.5.
  - **Outcome:** the charge stays at 105, and POL-G3 (no flood) remains the gate.

## 2026-11-05 — Hero haste is party tempo, and it stacks (GG-T268)

- **Was:** since the held turn (GG-T188) heroes have no turn icons, so haste on a hero only rushed a cast in flight. Quicken, Shortcut, Relay, Blitz, Follow-through and Fade Step did nothing but the `empowered` stopgap (GG-T263).
- **Now (owner):** hero haste "slows enemy motion down the timeline". See BIBLE §2.11.
  - Each hero haste adds signed **party tempo** points for some turns. The points **stack**.
  - Net tempo scales every enemy turn icon by 1.25^(−net), clamped ×0.5–×2.
  - A buff with `Tempo` on a hero subtracts: `slowed` is −1, ready for the Chronomancer (GG-T275). No enemy slows heroes yet.
  - One cast is one effect.
  - Shown as "Tempo ±N" on the timeline, with a track tint and a cue.

## 2026-11-05 — Mireholt Fens II back in band (GG-T274)

- **Was:** after GG-T263 the casual bot won swamp-02 3 of 12. The lone kiting hag (wave 2) bled the party, and the snake Naga closer (wave 5) finished it.
- **Now:**
  - **Wave 2:** frog + marsh shambler instead of the lone hag. Hags still appear in Mireholt Fens III's finale.
  - **Naga** (only on swamp-02): Strength 15 → 10, Vitality 12 → 10.
  - **Result:** casual wins 3/12 → 9/12 on the same seeds and parties. Neighbouring stages are untouched.

## 2026-11-05 — Effect prefabs come back (GG-T269)

- **Was:** "no prefabs" (repo law #3, BIBLE §11.1). Since GG-T253 every effect has been drawn in code from `vfxstyle.json`.
- **Now:** the owner chose premade particle prefabs that play once or loop, normalized, as GridGame2026 had: "it works".
  - **Where they live:** effect prefabs only, in `Resources/Vfx`, spawned and driven by code with no Inspector wiring. The source is the owned GridGame2026 set; any additions must be free/CC0.
  - **How they're set up:** `vfx.json` normalizes each prefab's size (in tiles), its duration and once/loop. `vfxstyle.json` maps elements, tiers and statuses to them.
  - **Rendering and time:** an effects camera feeds a RenderTexture overlay, driven by pause and battle speed.
  - **Fallback:** the code-drawn effects remain wherever no prefab is mapped, and under Reduce Motion.
- **Also changed:** a status no longer inherits its element's prefab (a burning unit doesn't replay the Fire impact explosion).

## 2026-11-05 — The Chronomancer: a foe that slows the party (GG-T275)

- **Was:** nothing put Slow on heroes. `slowed`'s −1 tempo (GG-T268) only came from traps (Shock Mine, Mire, Quicksand), and a cleanse removed the debuff but not the tempo it cost.
- **Now (owner: "Stack haste, while a Chronomancer casts slow on you"):** see BIBLE §14.10.
  - New enemy `chronomancer` charges **Temporal Drag**: party-wide, 4 Arcane plus Slowed, −1 tempo once. It holds Drag while the party is already behind on tempo, and rewinds once at 40% HP.
  - It is placed in Frostmaw Caverns II, wave 2. Casual wins there are 9/12 with it and 8/12 without it on the same seeds, in band. The wave was already long (the knights' tough hide).
  - **Cleansing** a tempo debuff off the last hero carrying it now lifts its tempo effect too (BIBLE §2.11).

## 2026-11-05 — Enemy lifesteal heals a quarter, not half (GG-T282, balance loop GG-T276)

- **Was:** the `lifesteal` enemy passive healed half of every landed hit. That was tuned when enemies struck at 0.9 with their full stat HP.
- **Why it broke:** GG-T262 raised the strike to 1.2 and cut enemy HP to 45%, so half a hit healed about three times as much of the pool. In casual-bot losses on Veshker Ruins III, the wave-1 undead healed back 75–85% of all the damage dealt to them, and the wave ran a median 76 actions.
- **Now:** `BattleConfig.EnemyLifestealFraction` = 0.25. On the same seeds (8 casual battles per stage): Veshker Ruins II 4/8 → 6/8, Veshker Ruins III 3/8 → 6/8; POL-G 70.1 → 71.8, G7 69.2 → 84.6.

## 2026-11-05 — Enemy charges can rest between casts; Smite does (GG-T283, balance loop GG-T276)

- **Was:** a caster charged its spell on every turn no hero stood next to it, with no cooldown. Templars are melee Guardians carrying `charge_smite`; from the board edge they charged Smite every turn and never closed in. In Veshker Ruins I wave 2 the casual bot took up to 154 Smites and dealt the two templars 41 damage in 102 actions.
- **Now:** enemy charges honour the ability's `CooldownTurns`, counted down at the start of the caster's own turn; `charge_smite` = 2. Same seeds: Veshker Ruins I 2/8 → 4/8, its wave 2 a median 66 → 48 actions, Smites per wave at most 24; POL-G 71.8 → 71.8, G2 71.4 → 78.6, G6 51.8 → 52.4.
- **Still open:** the casual bot lands only 1–5 pincers on edge-hugging Guardians; that is bot positioning, left to the loop.

## 2026-11-05 — One shared bag for the party's item slots (GG-T285, balance loop GG-T276)

- **Was:** `BattleFactory.Build` copied the whole inventory into every hero's spec, and a slot's stack split only within that hero. Owning 5 X-Potions put a full stack of 5 on every hero's bar, 25 full heals a battle, and the overdraw clamped to 0 afterwards, so it was free. Casual-bot victories ended with 60–75% of the party's HP left (POL-G1 wants 20–60%), on a median 15 potions.
- **Now:** one shared pool, drawn in party order; 5 owned is 5 charges across the party (`SharedInventoryTests`). A fresh game is unchanged (5 heroes × a stack of 5).
- **Measurement note:** the balance soak starts every battle from a fresh save, so it still carries 25 potions and its HP-left numbers don't move; a soak that carries a campaign's dwindling bag is the next instrument fix for POL-G1.

## 2026-11-06 — One timeline, no Zone: icons flow left and act at the edge (GG-T286, GG-T287)

- **Was:** enemy turn icons loaded left→right to ACT at the right end, at Speed × 1.6 IP/tick on the open track and then a shared 30-tick crawl through the rightmost 35% "Pushback Zone". Casts were shown only as a ring around the caster (the bible's summary still said "below the line", a contradiction).
- **Now (owner, inspired by Final Fantasy Resonance):** see BIBLE §2, §2.1, §2.6.
  - **One bar, one rate.** Every icon moves left at Speed × 1.6 IP/tick × tempo × status and acts when it reaches the left edge. It is drawn at `secondsUntilAct / BarSeconds` (`BattleConfig.BarSeconds` = 10 s). `ZoneStart`, `ZoneU` and the crawl are gone.
  - **Casts race turns.** A cast is born at `castSeconds / BarSeconds` on the same axis; ties go casts first, then the lower entry id. Slow, party Haste and Frozen change only the rate, so a Slowed foe can be overtaken by a cast.
  - **Diamonds (GG-T287).** One track with a small diamond every second and a larger one every 5 s. Every icon is a diamond-masked portrait (turns) or ability icon (casts). Borders: enemy red, boss purple, hero cast teal (`Palette.CastHero`), enemy charge amber (`Palette.CastEnemy`), the next actor gold (with a gold border on its tile). A large **prominence diamond** at the far left shows the next actor and its seconds ("NOW" while it acts). Inside `ApproachSeconds` (3 s) an icon and its owner's tile glow together in the icon's colour. GUARD sits at the right end.
  - **Guard is unchanged:** Protection, bank the unused seconds as orb mana, then the next actor reaches the left edge.
- *(Tests: `TimelineResonanceTests`, `Timeline_rate_is_linear_in_speed_with_no_zone_crawl`.)*

## 2026-11-06 — Pushback is a keyword in seconds (GG-T286)

- **Was:** every hero hit on an enemy in the Zone pushed its icon back by a depth × STR formula (`PushbackBaseU` 0.07, `PushbackMaxU` 0.15 after the 2026-10-11 tuning) and stunned it by AGI; `DelayU` was a separate bar-fraction field.
- **Now (owner, inspired by Final Fantasy Resonance):** see BIBLE §2.3, §2.7, §6.7.
  - **Only the keyword pushes.** `AbilityDef.Pushback` (seconds, was `DelayU`) on abilities and items; plain hits never push.
  - **A hero pincer pushes** every foe it damages `PincerPushbackSeconds` = 1 s.
  - **Anywhere on the bar,** capped at `PushbackMaxSeconds` = 2 s (validated at load), one push per foe per action. A held cast slips instead and is never cancelled by it.
  - **Content:** knockback and disruption skills carry 0.5–1.25 s; Hinder 2 s.
  - **Cast stagger** (the interrupt path, §13.4) is separate and unchanged.
- *(Tests: `A_plain_hit_never_pushes`, `The_Pushback_keyword_pushes_its_seconds_anywhere_on_the_bar`, `One_push_is_capped`, `Validate_rejects_a_pushback_past_the_cap`, `Pincer_pushback_is_the_same_seconds_anywhere_on_the_bar_whatever_the_strength`.)*

## 2026-11-06 — The window is the party's: every hero acts once before the next enemy (owner, GG-T308)

Owner: "Pushing an enemy back gives you more time to move your selected hero … The pushback happens after the drop (the hero acts) or as the result of an Ability, Item or Spell, all three of which consume that hero's turn."

- **Was (GG-T188):** every committed command ran the clock until one enemy turn resolved, so each hero action was exactly one enemy turn and Pushback/Slow never bought an action (the GG-T292 balance pass found reading the bar barely mattered).
- **Now:** see BIBLE §2.0, §2.5.
  - The time until the next enemy reaches the left of the bar is a **window shared by the party**; each able hero acts **once** in it (`Unit.ActedThisWindow`).
  - A drag spends the share of the window it was held; an Ability, Item or Spell spends `HeroActionSeconds` = 1 s. Pushback/Slow landed by the action lengthens the window first, so it buys the heroes still to act more time.
  - Control returns to the party while time is left and someone hasn't acted (`PlayerControlEvent.NewWindow` false). The enemy acts when the time runs out; when everyone has acted the clock runs on to it (leftover time is not banked); Guard gives the rest up and banks it.
  - Party turns (buff/cooldown/tempo/pickup/corpse counts, TurnStart/TurnEnd triggers) are windows, not hero actions.
  - The smart bot plans each window as a budget (pushback on the next actor is worth the hero actions it makes room for); the casual bot stays naive.
- *(Tests: `HeroWindowTests`.)*

## 2026-11-06 — Only Sentient enemies pincer (GG-T289, GG-T294)

- **Was:** any enemy tagged `Humanoid` could be a pincer attacker (`Unit.IsHumanoid`), so body shape decided it.
- **Now (owner):** see BIBLE GG-LAW-2, §1.2, §1.3.
  - Heroes always pincer. An enemy pincers only if tagged **Sentient**: it has a society or plans ahead, however rudimentary (soldiers, mecha and robots, goblins, orcs, frog folk). Intelligent undead such as vampires and mummies qualify too.
  - Beasts, slimes, yetis, scorpions and mindless undead never pincer. It's about sentience, not undeath. GG-T289 first barred every undead; GG-T294 lifts that so the rule is sentience alone.
  - `Unit.CanPincer` drives pincer detection, the enemy turn's pincer, pincer-seeking and `Auto` Threat targeting.
- *(Tests: `PincerEligibilityTests`.)*

## 2026-11-06 — Bestiary knowledge is earned by defeats; Scan is retired (GG-T290)

- **Was:** the Scan spell revealed a class's HP and stats and marked it Scanned; the Bestiary showed stats only after a scan.
- **Now (owner):** see BIBLE §25.11, §15.3, §9.
  - `BestiaryKnowledge.Tier` from defeats alone. Seen: name, description, lore, drops. 1 defeat: Speed. 3: HP and base stats. 6: elemental weaknesses and resistances. 10: abilities, brain and targeting.
  - Locked rows read "???" with "Defeat N more to learn …". The battle actor panel and the inspect card use the same tier.
  - Scan is gone. Save migration drops it from Known and the bar, and kits swapped it (healers → Quicken, supports → Silence, tanks → Cleave). An old `Scanned` entry keeps at least the stats tier.
- *(Tests: `BestiaryKnowledgeTests`.)*

## 2026-11-06 — Party cap six (GG-T291)

- **Was:** `MaxPartySize` 5; the Frostmaw Caverns and Veshker Ruins recruits both waited in reserve.
- **Now (owner: "two attackers and two supporters for each attacker"):** see BIBLE §25.14, §25.17.
  - `MaxPartySize` = 6 and the start stays 3. The story recruits fill slot 4 after Green Valley I, slot 5 at Sandsea Reaches and slot 6 at Frostmaw Caverns; the Veshker Ruins healer waits in reserve.
  - Six heroes spawn in rows 6–7 on every campaign stage and share one bag. Camp shows six in its two-column grid.
- *(Tests: `PartyCapTests`, `StoryRecruitTests`.)*

## 2026-11-06 — Text wraps itself; every overflowing line is a marquee (owner, GG-T305)

Owner: "Perfect text wrapping on all things (hyphenated or word wrap as an option), and one-line things like buttons have a restarting marquee that scrolls the info, flashes 3 times, then resets to left-most and begins again."

- **Every** single-line text that overflows its box (buttons, tabs, menu values, names, chips, badges, headers) is the one shared marquee, found automatically by `TextFit`; nothing opts in. The cycle ends in **three flashes** before the snap back (was: fade out, snap, fade in), and it no longer speeds up with Battle speed. Reduce Motion still shows an ellipsis.
- Wrapped text is laid out by our own line breaker (no lone last word, no line ending in "a"/"I"/a dash, overlong words hyphenated instead of cut mid-glyph) and is left exactly as given when the engine's wrap is already clean.
- **Settings → Text wrapping: Word wrap (default) / Hyphenate**, per profile, live. Hyphenation is a small deterministic English rule set (suffixes, a few prefixes, consonant-cluster onsets), not the full TeX pattern set: it only breaks where it is sure, and never in proper nouns, [keywords], numbers or rich-text spans. *Why:* a no-download, testable helper; a wrong break reads worse than a ragged line.

## 2026-11-06 — Active, Passive and Reactive abilities on one trigger engine (GG-T296)

- **Was:** `AbilityKind` had only `Passive` next to the Active kinds. Each of the ten battle passives was its own C# path (`HasPassive`, `MaybeCounter`, `MaybeThorns`, Berserk inside the enemy strike multiplier, and so on), several for enemies only. Passives learned from skill-tree nodes never reached the battle.
- **Now (owner):** see BIBLE §4.1, §7, §25.6.1.
  - Three kinds: Active (you take it), Passive (just happens), Reactive (fires after something happens to you). `AbilityDef.Trigger` carries the event, chance, condition, effects and guards as data, validated at load.
  - One engine fires every passive and reactive for heroes and enemies. Evaluation order is fixed and only a chance below 1 draws from the Rng. A reaction never provokes a reaction.
  - All ten battle passives are data now; Counter Attack and Thorns are Reactive. Learned passives apply in battle.
  - Behaviour changes: Lifesteal, Berserk, Tough Hide and Double Attack also work for heroes (the old enemy-only gates kept skill-tree grants from doing anything). An enemy's Double Attack second swing can be countered, as a hero's already could. A counter blow no longer sets off Thorns. Lifesteal and Regenerate stay silent at full HP instead of logging a 0 heal.
- *(Tests: `TriggerEngineTests`.)*

## 2026-11-06 — Every supporter brings its class's edge (owner, GG-T298)

Owner: "Each supporter gives some kind of passive or active effect when they are a supporter: a Cleric sends a heal to the attacker; a ninja might increase your crit %; a wizard might randomly cast a spell on enemies in the line — so some characters are just plain better for support because of their passive benefits."

- **Was (GG-T187):** six support effects; 34 of the hero classes (every ninja, duelist, brawler, hex archer…) only struck at 50 %, and Haste was a fraction of the old bar.
- **Now:** see BIBLE §1.2.3, §1.2.4.
  - Four new `SupportEffect`s: **Crit** (lends the attacker crit chance for this pincer, before its blows), **Cast** (echoes a random damaging spell from the supporter's own kit on the trapped foes at a power fraction, no mana), **Status** (a debuff on each trapped foe by chance) and **Pushback** (seconds added to the pincer's single push per foe, capped at 2 s).
  - **Haste is seconds now** (≤ 3 s), the mirror of Pushback on the BRAVE timeline.
  - The 34 strike-only classes are reassigned by archetype (10 Crit, 9 Status, 8 Pushback, 1 Haste, 6 keep Strike as their identity); Black Witch and Harbinger trade Mana for Cast.
  - A generated "As a [Supporter]: …" sentence shows on the InspectCard, hero page, Abilities screen, mercenary offers and party pick; the cut-in float names the effect.
  - The effects are plain `SupportDef` data, so enemy classes can carry them too (GG-T307).
- *(Tests: `SupportEffectTests`.)*

## 2026-11-06 — Every monster has its own support (owner, GG-T307)

Owner: "Every monster needs support abilities."

- **Was:** every enemy class had no `Support`, so an enemy in a Sentient pincer's chain only struck at 50 %.
- **Now:** see BIBLE §1.2.3 (Enemy supports).
  - All 115 enemy classes carry a named, flavour-fit `Support`, each matched to its family. The effects are Status (poison, blind, slow, silence, burn), Buff, Heal, Cleanse, Crit, Haste, Pushback, and Cast for the hags. None of them is a Strike.
  - Bosses reach 2 and hit harder: Naga *Coil Venom*, the Cyclops bosses' *Boulder Toss*, Captain *Battle Cry*, Reaper *Death Mark*, Demon Lord *Hellbrand*, Chronomancer *Time Skip*.
  - Enemy Pushback slips a trapped hero's pending cast, because heroes have no turn icon. Enemy Haste moves enemy icons left.
  - A Turns buff lent to the enemy whose own turn closed the pincer gets one extra turn, so it is still up when the heroes act.
  - Mana supports are hero-only, and the validator enforces it.
  - A "Party" support now reads "all its allies".
  - The Bestiary and a foe's InspectCard show the named support sentence at the abilities tier (10 defeats).
- *(Tests: `EnemySupportTests`.)*

## 2026-11-06 — Tap a timeline icon to select its unit (GG-T288)

- **Was:** timeline icons only answered a hold (the InspectCard); selecting a unit needed a board tap.
- **Now (owner, inspired by Final Fantasy Resonance):** see BIBLE §2.6, §9.
  - Before a move, a tap on any timeline icon or on the prominence diamond selects its owner, exactly like a board tap.
  - The selected unit gets a white ring on each of its diamonds and a white outline on its tile (`BoardView.SetSelection`).
  - A hold still opens the InspectCard. Both stay gated by the Bestiary knowledge tier.

- **Bracketed terms are tap tooltips** (owner 2026-11-06, GG-T300: "anything in brackets is a tap tooltip"). Content marks keywords as `[term]`, each validated against `glossary.json`; tapping one opens a definition card, alongside (not replacing) hold-to-read. Unquantified odds use five fixed chance words ([slight]/[small]/[fair]/[good]/[likely]). *Why:* descriptions stay short while every mechanic word explains itself on demand.

## 2026-11-06 — Difficulty is one 1–10 dial, starting at 10, and pays more the higher it is (owner, GG-T309)

Owner: "This is a difficulty question, and the higher the difficulty the greater the rewards. On a 1-10 scale you would start on 10 and let players adjust the game if they feel it is too easy or hard."

- **Was:** §22.9's scaffold — an Easy/Normal/Hard/Custom preset plus six multipliers, all pinned at 1.0 and read by nothing but telemetry.
- **Now:** see BIBLE §22.9.
  - One profile setting, `Difficulty.Level` 1..10, **10 by default**; Settings shows − / level / + with a one-line description and the reward multiplier, and it applies from the next battle.
  - `DifficultyTable` (validated, monotonic) scales enemy HP and plates ×0.55→×1.0, enemy damage to heroes ×0.60→×1.0 and enemy level −2→0 from 1 to 10. Level 10 is the designed game: its enemy side is exactly as authored, so every balance number still holds there.
  - Rewards rise with the dial: coins, XP and drop chance ×0.6 at 1 → ×1.0 at 5 → **×1.5 at 10**. *Effect on the default game:* a player at 10 now earns 1.5× the gold, XP and drops the economy paid before; the curve is one table row per level if that needs retuning.
  - Old settings migrate: Easy → 5, a below-×1 Custom → the nearest level, everything else → 10.
  - Soaks fight at `GAIDEN_DIFFICULTY` (default 10); `polish.py --balance --difficulty N`.
- *(Tests: `DifficultyTests`.)*

## 2026-11-06 — Defenders evade: Evasion from AGI and Luck, dodge synergies (owner, GG-T297)

Owner: characters can be built to benefit from dodging hits.

- **Was:** the hit roll (§13.1.1) read only the attacker's stats; a defender had no evasion, and `OnDodge` fired only on the attacker's own miss.
- **Now:** see BIBLE §13.1.0.
  - Every unit has **Evasion** = `min(25, 0.2·AGI + 0.1·LCK)` + buff and gear `EvasionBonus`, capped at 40 points. It takes the top points off the attacker's hit band on the same single Rng draw, so a seed only plays out differently where Evasion bites. Typical values are 3–12, so base balance barely moves.
  - **Pincer and supporter blows can't be dodged** (the pincer is the core payoff). Enemy melee swings and shots, counterstrikes and charged enemy spells can. Hero spells still never miss.
  - A dodge emits `DodgedEvent` (a "Dodge!" float, a sidestep and a whoosh, POL-D4) and fires `OnDodge` for the defender. A charged spell's miss or dodge now fires `OnDodge` too.
  - New data: `BuffDef.EvasionBonus` / `ItemDef.EvasionBonus` (validated), the `blurred` status (+15 for 2 turns), and three Reactives on `OnDodge`: Evasive Riposte (a [Counterstrike]), Catch Breath (heal 6%) and Blur (gain [Blurred]). Only the Duelist carries one (Evasive Riposte); wider assignment is GG-T302's.
  - Shown: `EVA n%` on the InspectCard and the battle unit panel at the Bestiary Stats tier, and on the Bestiary's Stats fact; glossary [Evasion] and [Blurred]; the [Dodge] entry mentions Evasion.
- *(Tests: `EvasionTests`.)*

## 2026-11-06 — Passives as rewards: Tomes, chests, milestones and a passive cap (owner, GG-T301)

- **Was:** a hero could only gain a Passive or Reactive from its class kit, a skill-tree node or an armor enchant, and every learned one fought. Quests, bounties and drops paid gold and consumables. There were no chests and no campaign milestone rewards.
- **Now (owner):** see BIBLE §25.6.4, §22.4, §22.11, §24.1.
  - A **Tome** item teaches one battle passive to a hero the player picks. Quests (3), bounties (2), each theme's first full clear, boss drop tables and chests grant them as ordinary items.
  - **Chests** are items that drop from every enemy family (1.5 % Wooden; bosses 20 % Iron) and open into their own drop table. Postgame themes' milestones are Gilded Chests.
  - Bounty rewards changed: Wolf Pack paid a Hi-Potion and now pays a Tome of Berserk; Cave Dweller paid Flame Oil and now pays a Tome of Tough Hide.
  - **Cap:** at most 2 learned passives (Tomes and skill-tree grants, not the class kit) are on, +1 at Lv 10 and at Lv 20. The Abilities screen's Passives tab switches them. Before this, every learned passive fought.
- *(Tests: `PassiveRewardTests`.)*

## 2026-11-06 — Signature passives for every hero class and most enemy families (owner, GG-T302)

Owner: Active = you take it; Passive = it just happens; Reactive = after something happens to you. "You could design a character that benefits from being hit, or from dodging hits, or from being surrounded by enemies." Enemies use the same system.

- **Was:** after GG-T296 the trigger engine shipped ten generic passives; only the Duelist (Evasive Riposte) had one in a hero kit, and many enemy families had none.
- **Now:** see BIBLE §4.1.2.
  - 44 new `abilities.json` Passive/Reactive rows. Every one of the 65 hero classes carries 1–2 (Vampire's Kiss, Shield Wall, Unbowed, Grudge, Guard Counter, the ninja edges, Mending Touch, Arcane Harvest, …), and 73 enemy classes gained 1–2 (Toxic Skin on frogs, Vengeance on templars/wolves/gangers, Mirror Plating on mecha armor, Poison Ward on the undead, two per boss).
  - Stale text fixed: Guard Stance promised a counter it never made, so Guard Counter (a Reactive while [Warded]) is now on all five Guard Stance classes; the Training Rapier +3's 3% lifesteal passive was named "Riposte" and is now *Bloodied Point* (items.json + tools/weapons.py); Counter Attack's text names [attacked directly]; the Mirrorcurse skill-tree node said "reflect a curse" but the spell blinds.
  - Glossary gains [Lifesteal], [Reflect], [mana charge], [Physical] and the alias "attacked directly"; `ContentLoader.Validate` now also checks the [terms] of every generated `TriggerText` sentence.
  - Removed the unread `BattleConfig.RegeneratePercent`, `ThornsFraction`, `ManaSiphonCharge` and `EnemyLifestealFraction`; the rows' Amounts are the tuning (tests pin 0.25/0.25/5).
  - *Balance (provisional, casual bot, difficulty 10, same seeds, 4 battles × 15 stages):* wins 60/60 → 60/60, party HP left on victories 90% → 87%, actions per battle 85.8 → 86.1; at 12 × 15 stages 180/180 → 180/180, HP left 89% → 90%, actions 88.9 → 88.1. Neutral within noise (the casual bot already wins every stage at the soak's levels, so the win band can't move).
- *(Tests: `SignaturePassiveTests`.)*

## 2026-11-06 — Taming: the Monster Tamer, the Dog Whistle, bond and domestication (owner, GG-T303)

Owner: a Monster Tamer tames an enemy monster so it fights for you on its own, like Umaro in FF VI. Only non-Sentient beasts, never bosses. Tamed, it gains the Pincer keyword. A full bond at victory lets it join for good, with limited growth.

- **Was:** only heroes and Sentient enemies could pincer; every hero-team unit was player-commanded; the roster held only hero classes.
- **Now:** see BIBLE §25.11.3 and §1.3.
  - `Unit.CanPincer` adds the **Pincer** keyword (tamed or domesticated, or a class tagged `Pincer`).
  - A hero-team unit can be **independent** (tamed or domesticated): never commanded, it acts on its own turn icon with a simple charge-the-nearest-foe AI that walks through friend and foe. Its turn doesn't end the party's window, and it alone doesn't prevent a defeat.
  - Domesticated monsters are roster members with an enemy class (`HeroSave.Domesticated`): no gear, skills, Tomes or skill tree, half XP, level cap 30, one per class.
  - Choices made here (the owner may revise them): a **full bond stops the tame's countdown** (otherwise a monster could rarely still be tamed at the end with a full bond). The explicit eligibility rule is followed literally, so mindless undead, cyber-zombies, tree golems, slimes and the ghost are tameable as well as beasts. The Dog Whistle is shop-only for now (adding it to drop tables would shift every seeded loot roll).
- *(Tests: `TamingTests`.)*

## 2026-11-06 — Minimum action time: heroes act only in a gap wide enough, else they Guard (owner, GG-T311)

Owner: "Heroes go first in the round, but unless they can get in a pincer between the intervals between enemies hitting left they have to sacrifice their turn, and that could lead to several enemy attacks in a row before a window wide enough for the player to act becomes available." After GG-T308 both bots won every battle: every gap, however short, still gave the party an action (a 0.1 s gap played back as a 2 s drag).

- A hero action starts only if the gap to the next enemy turn is at least its minimum: a drag 0.85 s (`DragPickupSeconds` 0.5 + `DragSecondsPerTile` 0.35 × a tile), an Ability/Item/Spell/Talk `HeroActionSeconds` 1 s. The `MinTurnSeconds` 2 s floor is gone; a drag spends at least its minimum.
- No action fits → the party can only Guard (forced sacrifice): Protection on every hero and the sliver banked as mana charge, then the enemy acts; clustered foes mean several enemy turns in a row.
- Every wave opens on the party (`OpeningWindowSeconds` 3 s, or a full lap for a foe too fast for that).
- Rally: each forced Guard slows every enemy icon a tempo step (stacking to 5, ≈ ×0.33, outside the tempo clamp) until the next hero action — so a lock of too-short gaps always opens (found by the Crucible soak: foes lapping so fast that every gap was under 0.85 s held the party out for 6000 commands). The bots hurry (no think time) after 2 Guards in a row, so a window a human could use never locks a bot out either.
- Bots drag on the human model plus think time (smart 0.4 s, casual 1 s); `BotSecondsPerStep` 0.25 s/tile is gone.
- `IpPerTickPerSpeed` 1.6 → 4.2 and the difficulty dial gains an `EnemySpeed` column (×0.60 at 1 … ×1.00 at 10). Soak (per-stage 8): difficulty 10 smart/casual 100/100% → 79/33%, 5 → 99/85%, 1 → 100/98%. Bots drag on think + 0.5 s + 0.35 s a tile (a hop of 1–2 tiles thinks half as long). The campaign pacing report now requires only the camp-managing greedy bot to finish.

## 2026-11-06 — Pincer-keyword units engineer pincers: the shared pincer planner (owner, GG-T306)

Owner: "the heroes and enemies with 'Pincer' keyword not only line themselves and complete pincers they actively try to make pincers by swapping with monsters or heroes to setup a beneficial situation with enemies, themselves, and their supporters - so every monster needs support abilities."

- **Was:** a Sentient foe stepped one free tile at a time (`PlanStep`, +50 for a tile that formed a pincer, +25 for a supporter tile) and never swapped. A tamed monster charged the nearest foe and was "deliberately not clever". The smart bot previewed only straight L-drags.
- **Now:** see BIBLE §14.1.3. `PincerPlanner` is one shared, pure, deterministic planner. It searches up to 3-step walks that swap through one-tile friends and foes like a hero drag, and scores what each walk leaves: Complete, Setup (an end an ally can reach), or Support (joining a chain). Supporters are priced by their own `SupportDef` effect, and walks that leave allies trapped are penalised.
  - Sentient foes use it within their `MoveRange`. A completing walk beats a charge. Evasive brains take only completing walks. A swap announces "X maneuvers!".
  - Tamed monsters use it on the hero side before falling back to their charge.
  - The smart bot adds the planner's walk to its drag candidates (inside the GG-T311 drag limit) and weighs supporters by effect.
  - `ContentLoader.Validate` now requires every enemy class to carry its own `Support`.
- **Rebalance:** `PincerPushbackSeconds` 1 → 1.5 s. Soak, smart/casual, difficulty 10: 79/33% → 76/31%; difficulty 5: 99/85% → 99/78%; difficulty 1: 100/98% → 100/98%. Without the push change, difficulty 10 was 72/22%.
- **Stall fixes found by the soak:** a fleeing foe avoids corners (−3; a corner can't be pincered). The smart bot hurries after 4 windows without damage, even when its bar offers buffs.
- *(Tests: `PincerPlannerTests`.)*

## 2026-11-06 — Speed sets where a foe re-enters; a recovery beat spaces the foes (owner, GG-T318 – GG-T322)

Owner: "I want the 'average' time to move to be 4 seconds … when an enemy restarts on the bar they start 4 seconds away on the far right but their speed is calculated so that they start a little more to the left the faster they are, that's the main point of the speed stat for enemies; there would also need to be a 'respawn interval' which takes place after the enemy attacks but before they appear back on the bar, the idea is to distribute enemies so that the player can fight back." And: "this is the lifeblood of the entire game, this is what sets it apart from Terra Battle's fixed 4 seconds … it would be affected by the Difficulty multiplier which also adjusts the rewards multiplier so it's an intricately interconnected system."

- **Was (GG-T286/GG-T311):** after acting, an icon reloaded from IP 0, and Speed set how fast it travelled (`IpPerTickPerSpeed` 4.2, so Speed 10 lapped in 2.4 s). Nothing spread foes apart. For the real 3-hero start at difficulty 10, 35–50% of the party's actions were forced Guards (GG-T313/GG-T316).
- **Now:** see BIBLE §2.1 and §2.12.
  - One rate for every icon (`BaseIpPerTick` 20 against `BarLength` 2000). `BarSeconds` is 6 s.
  - Speed sets the entry: 4 s × (ReferenceEnemySpeed(level) / Speed)^0.6, clamped 2–6 s.
  - After acting, a foe recovers off the bar for 1 s + its turn's `RecoverSeconds` keyword. A spacer on the difficulty dial (MinGap/MaxSpread 1.6/2.0 s at level 1 down to 1.0/1.0 s at 10, ±0.25 s jitter) holds it longer so its turn lands clear of the others'. Then it slides on at its entry.
  - Heavy charges recover 0.5–1.5 s longer.
  - The recovery tray shows it all (GG-T319).
- **Why no Grandia-style ACT zone:** heroes have no icons, enemy charges already are the visible, cancellable commitment, and a band would break "position = seconds until acting". Grandia's action-dependent cost lives on as the recovery. A target telegraph in an enemy's last seconds is a candidate follow-up.
- **Measured (GG-T321, `docs/research/TimelineTuning.md`):**
  - At difficulty 10 with campaign parties: smart 100%, casual 90%; forced Guards 14% (smart) / 22% (casual); median gap 2.1 s.
  - Green Valley I with the real three heroes: smart 6% → 4/4, casual 0/4.
  - At this pace no timeline setting moves the smart bot below 97%. Challenge at the top of the dial is an open owner decision (enemy strength, a human ceiling for the smart bot, or encounter design).
- *(Tests: `TimelineSpacingTests`; old-model tests rewritten.)*

## 2026-11-07 — Foes grow with the party fielded (GG-T321, decision delegated by the owner)

Owner: "you decide how difficulty 10 gets its challenge back (GG-T321)."

- **Was:** after GG-T318 (Speed sets re-entry, recovery spacing) difficulty 10 was smart 100%, casual 90%. No timeline setting moved the smart bot below 97%.
- **Now:** enemy strike and MaxHP (and plates) grow ×(1 + `EnemyScalePerExtraHero` 0.30 × heroes beyond the starting three), via `BattleEngine.PartyScale`.
  - *Why party size:* on the 4 s clock three heroes get about 1.3 actions per enemy turn and six get about 2.4. Party size, not the clock, is what made the game easy.
  - A global strength raise reached the same overall rates but walled the first battle (Green Valley I, three heroes: 0–50% even for the smart bot).
- **Skilled-human probe:** `HeroBot.Skilled` (SkillLevel 80, 0.8 s think) joins the tuning soak. It scored the same as the smart bot, so a real skilled player's rate needs a human playtest.
- **Measured** at difficulty 10, campaign parties: smart/skilled/casual 100/100/45%; six heroes 100/98/38%. Difficulty 5: 100/100/97%. Difficulty 1: 100% for every bot. Green Valley I unchanged. Full runs: `docs/research/TimelineTuning.md`.
- *(Tests: `Foes_grow_with_every_hero_fielded_beyond_the_starting_three`.)*

## 2026-11-07 — The Still: a sixth main theme between the Anchor Wastes and Halcyon (owner, GG-T344)

Owner, with six new landscapes of a white Concord dome: *"I will generate more landscapes from which you can extrapolate more story, settings levels, etc"*.

- **Was:** five main themes (Tithe Moors, Shardfall, Arkgrave, Anchor Wastes, Halcyon), 15 main stages, Halcyon at levels 13–15; every theme had exactly four backgrounds and `Art.Background` wrapped any index at 4.
- **Now:** **The Still** (`still`, three stages) sits between the Anchor Wastes and Halcyon ([BIBLE §22.3.1](BIBLE.md), [STORY.md](STORY.md) §5.5): the great Still at the foot of Halcyon's spire, where the tanks hold the tithe — and the Hollow, stored. The Still runs at 13–15 and Halcyon moves to 16–18.
  - A theme shows as many backgrounds as it has (`Backdrops.Resolve`; the Still has six). Owner rules for background files: kept at the art's original resolution (the Still's are the native 768×1536; the old 720×1440 downscale was an import choice, GG-T353 re-imports those), named after the story region and numbered from 01 (`still_01` … `still_06`). A campaign theme can name its set (`"Backgrounds": "still"`); the legacy `<theme>_0` sets keep loading until GG-T353 renames them. A postgame theme with none borrows its base theme's art (`cave2` → `cave`) instead of falling to the Tithe Moors' first image.
  - The Still III boss is the Chronomancer with **TwinTurn**, so two finales now share a rule-break (§14.9 said "each a different one"). The owner made no new boss art, and the time-mage is the Still's timekeeper.
  - Save v5 shifts `HighestClearedStage` up by three for saves already past the Anchor Wastes, so nobody's frontier moves.
- *(Tests: `StillTests`.)*

## 2026-11-07 — The story is reinvented: *Under Morrow* (owner, GG-T341)

Owner: *"flush out all story in the entire game … completely reinvent the story based on the characters and artwork of the world that exist in the repo; it's distinctive enough that all these different groups and their styles means there is a story already built into the artwork and different clothing and environments."*

- **Deviation:** the GG-T120 story (the walking spider-town, its soured ground and its oath) is retired in full. [`STORY.md`](STORY.md) is the new single source: Saltmoss runaways in a stolen Concord caravan, under a moon (Morrow) that hangs too low, against a Pale Concord whose aether tithe feeds the Hierarch.
- **Phase 1 (this change):** `campaign.json` (prologue "Tithe Day", ending "Still Here", every theme's name and crawl), all 111 `dialogues.json` dialogues (join scenes, party talks, boss intros, the Lv 10 "Why We Ran" interlude, quest offers, Camp chats, and every Advice quiz with a fresh faction voice and blind spot), quest, bounty, mercenary and caravan-bark text, the credits coda, and the Frostmaw armor set's display name (now Anchorwatch). Ids, triggers, conditions, effects, flags and node graphs are unchanged; the Merchant's speaker name is now "Lantern Merchant".
- **Retired docs:** `docs/story-draft-spider-town.md`, `docs/ppp-universe/` and `docs/design-draft-overworld-and-dialogue.md` are deleted (git keeps them).
- **Still to come (phase 2):** stage display names, unit `Description`/`Lore`, and a separate `hierarch` boss class (the finale still fields the hero class `vampire`).

## 2026-11-07 — Random rescues become wanderers you win over (owner, GG-T342)

Owner: *"so you will sometimes meet a hero while fighting a monster and they will fight the monsters separately but you are able to complete pincer moves with them so you fill a trust gauge by pincering, or healing them at the end of the stage and the loot you and the team member you choose has a conversation with them, if they can come to an understanding they join, if not they go back into the pool of possible recruits"*; and *"the point is to gain their trust, tell them what they want to hear, and then you have a follower — another party member."*

- **Was (GG-T236):** the 25% roll put a player-controlled **captive** guest on the board, who joined automatically if standing at a victory, followed by a rescue scene (`Trigger.Kind = Rescue`).
- **Now:** the same seeded roll and pool bring a **wanderer**: an independent ally like a tamed beast (own turn icon, own AI, never commanded, pincers with the party). Hero pincers that include it (+2) and heals from heroes (+1) fill a **trust** gauge of 6. Standing at a victory, the player picks a party member to talk with them; one of three answers says what they want to hear. They join (a full, normal party member) only with that answer **and** full trust, or half trust with a companion of their own STORY.md faction (`factions.json`); otherwise they go back to the pool. A deterministic rule, not a dice roll.
- *Why deterministic:* the owner wants a readable loop ("earn trust in battle, say the right thing, and they join"); the results screen states the rule before the talk.
- Retired: `Rescues`, `CaptiveSpec`, `GuestJoinedEvent.Captive`, `BattleReport.Captive/Rescued`, the `Rescue` dialogue trigger and `rescue_freed`. A wanderer standing alone never staves off defeat; a fallen one is down, never lost.
- *(Tests: `WanderTests`.)*

## 2026-11-07 — New heroes from the art are recruited as wanderers (owner, GG-T333)

Owner 2026-11-07: the Herald, Aetherist and Tactician from the owner's art are recruited through the wanderer system — "met mid-battle, earn their trust, say what they want to hear, and they join".

- **Was:** a new hero joined through the Summon Circle's gold pool (the Monster Tamer, GG-T303), a story beat, an authored guest or recruitable, or a mercenary contract.
- **Now:** these heroes have no other way in, so they are only met as wanderers ([BIBLE §25.11.2.1](BIBLE.md)) or drawn for a Diamond. The Herald belongs to the Bloom, which has no heroes of its own in `factions.json`, so she is filed with **the Wild**, the closest faction.
- **Essence (new mechanic):** the Herald's Lifegift is the first heal paid in the caster's own HP instead of mana or a cooldown alone (`AbilityDef.LifeCost`); it never takes her last HP.
- **Orbs that ride a heal (GG-T337):** an orb grant used to be a whole ability on its own (Meditate, ethers). The Aetherist's Aether Pour heals an ally and then pours one orb into the bank (`AbilityDef.PoursOrbs`); she is filed with the Fen-dark, where the Choir's runaways go.
- *(Tests: `WandererHeroesTests`.)*

## 2026-11-07 — A random summon for Diamonds (owner, GG-T343)

Owner: *"another way is to simply summon them randomly with Diamonds monetized currency"*.

- **Was:** "Not a gacha" (BIBLE §3): heroes were only recruited deliberately for gold at the Summon Circle.
- **Now:** the Summon Circle also offers one random draw for **1 Diamond** (`DiamondSummon.Price`, the one tuning constant) from the wanderer pool (unowned heroes with no other way in, the pool a parted wanderer returns to). It rides the Summon paywall job (1 s) and ends in a portrait reveal. The gold, deliberate summon stays.
- **Not monetized yet:** Diamonds still come only from the LuckyDiamond paywall hook (1 g · 1 s); there is no store and no IAP UI ("until I call it monetize, it's not"). Bots, `CampRoutine` and Test Mode never spend Diamonds.
- *(Tests: `SummonTests`.)*

## 2026-11-07 — Portraits are 1024×1024 canvases, never trimmed (owner, GG-T352)

Owner: *"all characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the assets are."*

- **Was:** `tools/import_art.py` alpha-trimmed every portrait to its figure and capped it at 640 px (BIBLE §12.1 "≤ 640 px", POLISH POL-H1 "a person's canvas is 640 px tall"), so all 211 files were smaller crops of the art.
- **Now:** every portrait is the art's own 1024×1024 RGBA canvas ([BIBLE §12.1.2](BIBLE.md)); a source of another size is fitted onto that canvas whole. Every class's `Thumbnail` was converted to frame the canvas exactly as it framed the trimmed image, and a new `Figure` box (the alpha bounds, written at import) lets whole-figure views (dialogue, cut-ins, level-up, cards, death dissolve) draw just the figure, at the size it had before. POL-H1 now checks the 1024 canvas instead of the 640 fill rules. The 18 originals that were cut by hand earlier at full canvas, and the vulture, which still had its solid background, now draw from their figure box like every other figure, so in dialogue they stand as tall as everyone else instead of carrying their old margins.
- *(Tests: `PortraitCanvasTests`.)*

## 2026-11-07 — Backgrounds at the art's resolution, named after their region (owner, GG-T353)

Owner: *"make sure the source files for the Backgrounds all get proper naming to identify them to their world lore counterparts"*.

- **Was:** `tools/import_art.py` downscaled the GridGame2026 biome backdrops (1536×3072) to 720×1440 JPEG and kept only the first four per biome, named by theme id (`greenvalley_0` … `cityruins_3`); `GaidenImport` capped Backgrounds at 2048.
- **Now:** every original is imported at full size (JPEG q95, 4:4:4) and named by its STORY.md region, numbered from 01: `tithemoors_01..05` (Moors), `shardfall_01..05` (RedThorns), `arkgrave_01..05` (ElectricWasteland), `anchorwastes_01..04` (UnderTheBridge), `halcyon_01..14` (CyberNecropolis). Each main theme names its set in campaign.json (`"Backgrounds": "tithemoors"`); theme ids are unchanged and the postgame themes borrow their base theme's set. The 4096 cap keeps them unshrunk ([BIBLE §12.1](BIBLE.md)).
  - **Fourteen Halcyon images, not sixteen:** CyberNecropolis `02.png` is byte-identical to `00.png` and `15.png` to `01.png`, so the duplicates are skipped (the old import showed `00` twice, as `cityruins_0` and `cityruins_2`).
  - **Memory:** a 1536×3072 backdrop is about 4.5 MB as a CompressedHQ texture (no mips) against about 1 MB before. Counting a set no longer keeps its images loaded (`Art.BackgroundSet` unloads what it probes).
  - The 19 files that existed were renamed with their `.meta`, so they keep their GUIDs; the other 14 are new.
  - The owner's queued region art (GG-T345 to GG-T350) is copied under each ticket's attachment folder as `<region>_NN.png` (`lanternroad_`, `freeholds_`, `bloom_`, `morrowsshadow_`, `underspire_`, `cathedral_`), at its native 768×1536.
- *(Tests: `BackgroundsTests`, `StillTests`.)*

## 2026-11-07 — The Lantern Road: a seventh main theme between the Tithe Moors and the Shardfall (owner, GG-T345)

Owner, with four new landscapes of a lantern-lit mountain pilgrim road: *"I will generate more landscapes from which you can extrapolate more story, settings levels, etc"*.

- **Was:** six main themes, the Shardfall at levels 4–6 right after the Tithe Moors, 18 main stages (28 in all), Halcyon at 16–18, armor sets up to level 28.
- **Now:** **The Lantern Road** (`lanternroad`, three stages) sits between the Tithe Moors and the Shardfall ([BIBLE §22.3.2](BIBLE.md)): the crew climbs the pilgrim road out of the moors toward the desert. It runs at 4–6, and every later chapter moves three levels (the Shardfall 7–9 … Halcyon 19–21); armor sets from level 4 up move three (Dreadwyrm 31).
  - Existing units only: oni road-raiders, the gorge's moon-changed Wild (wolves, a werewolf, bats, a flying monkey, steppin razors), a little Redline and militia. The finale reuses **Undying** on an oni (`oni02`), so it shares its rule with the Hierarch, as the Still III shares TwinTurn with the Captain.
  - **Story text is placeholder draft:** story is authored in Prose, not in this repo (Prose RFC 0017, GG-T355). The crawl, the party talk, the boss intro and the side quest (`q_masks_off`) are short and functional; STORY.md has a structural entry only.
  - Save v6 shifts `HighestClearedStage` up by three for saves already past the Tithe Moors, so nobody's frontier moves.
  - Balance (24 seeds, campaign parties, difficulty 10): the road measures casual 19, 16 and 15/24, smart 24/24. Two light trims where the three-level shift pushed a later stage lower: Shardfall III wave 5 (−1 soldier, casual 11 → 14/24) and Arkgrave II wave 4 (−1 toad, 1 → 5/24).
  - Background luminance: three of the four landscapes measure a mean luminance of 0.22–0.23, just under POL-H3's 0.25 floor (a soft check). They are kept as the owner made them, at native resolution.
- *(Tests: `LanternRoadTests`.)*
