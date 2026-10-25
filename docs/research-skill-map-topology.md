---
status: paused, unresolved (research spike, GG-T83)
date: 2026-10-16 (recorded 2026-10-25)
purpose: Keep every topology tried for the unified build-a-character skill map, why each one was
  rejected, and the two real-game references that came up, so a later session can pick the question
  up without working it all out again. Nothing here is decided. BIBLE.md §25.6.1 still describes
  what ships (six separate archetype trees, GG-T50/GG-T74).
---

# Skill map topology: research spike (GG-T83)

## The question

GG-T50/GG-T74 shipped six separate archetype trees in `skilltrees.json`, one per `Role` (Guardian,
Warrior, Ranger, Mage, Cleric, Alchemist). The owner's direction (2026-10-15/16) is that a hero should
be able to build toward a cross-archetype character, such as a tank that casts Heal, a rogue that
throws Fireball, or a mage in plate, and that this should feel organic rather than taxed. The open
question is what shape the board behind that should have. The owner turned down three sketches in a
row (A, B, C below). A fourth, the owner's own sketch (D), came closest but is still "not quite right".

## Approaches tried

### A. Six independent trees plus a toll-gated bridge (original GG-T51 draft)

- A hero's home tree costs its listed price. Once the hero has spent enough points at home (10 to
  open a 2nd tree, 20 for a 3rd), off-tree nodes become available at +1 point each, and higher-tier
  off-tree nodes need more points in that tree. GG-T76 later added a choice of paying the opening
  toll in gold instead.
- **Rejected:** it felt like a tax, not organic. The cross-spec cost was a flat surcharge with no
  sense of distance, so a near neighbor cost the same as a far-off archetype. (GG-T76 was closed
  wont-do along with it. GG-T51 is still open in the ticket list but describes this rejected model.)

### B. One connected hex map, six wedges meeting at a center (GG-T79, closed wont-do)

- The 6 trees merge into one graph with cross-region bridge nodes. Each class starts at a different
  spawn point. There is no toll and no opening step: the cost of a node is the sum of node costs on
  the path to it. Ring order by role kinship:
  Guardian - Warrior - Ranger - Alchemist - Mage - Cleric - (back to Guardian).
- **Rejected, for two reasons:**
  1. It still read as "trees by archetype", just relabeled as wedges.
  2. **Math error:** the ticket said Guardian<->Mage, Warrior<->Alchemist and Ranger<->Cleric
     were each other's longest walk. On a 6-ring, the longest walk is between positions 3 apart.
     With Guardian=0, Warrior=1, Ranger=2, Alchemist=3, Mage=4, Cleric=5, the real opposite pairs are
     **Guardian<->Alchemist, Warrior<->Mage, Ranger<->Cleric**. Guardian<->Mage and
     Warrior<->Alchemist are only 2 steps apart. Only Ranger<->Cleric was right. This was caught
     during the session. It is a mistake in the sketch's own arithmetic, separate from the design
     disagreement, so check any future ring's distance claims against its ring order.

### C. Octagon of the game's 8 real stats

- The 8 stats (STR / VIT / AGI / SPD / STA / INT / WIS / LCK) sit on the corners of an octagon.
  Each archetype cluster is placed by its primary/secondary stat pair, with no walls between
  clusters.
- **Rejected:**
  1. It borrows Path of Exile's shape without its logic. PoE's passive tree has only **3**
     attributes (Strength/Dexterity/Intelligence), and its starting classes sit at those 3 points
     and between them. Putting 8 stats on that kind of shape does not reproduce how PoE's tree
     works.
  2. The layout came out lopsided, with Warrior isolated. Nobody intended that. It fell out of the
     stat-pair placement.

### D. Archetype wheel (owner's own sketch): closest so far

- 8 "pure" archetypes in a ring: **Warrior, Tank, Healer, Mage, Debuffer, Buffer, Ranger, Thief**,
  with 8 named hybrid nodes at the seams between neighbors (Warrior x Tank, Tank x Healer, and so on
  around the ring).
- **Mage was added by Claude and the owner has not confirmed it.** Where Mage goes, and whether it
  belongs on the wheel at all, is the main open point.
- Following the ring order above, the seams would be Warrior x Tank, Tank x Healer, Healer x Mage,
  Mage x Debuffer, Debuffer x Buffer, Buffer x Ranger, Ranger x Thief, Thief x Warrior. Only the
  first two were named in the session, and the hybrid names were not settled.
- Owner verdict: "not quite right". It is closer to the intent than A to C, but it was not accepted
  as drawn.
- Observation (not discussed with the owner): D's 8 pures differ from the 6 shipped `Role` trees in
  two places. Thief corresponds to the Rogue region GG-T81 proposes carving out of Warrior. Buffer
  and Debuffer split today's single Alchemist (Support) tree in two. If D is refined, those two
  splits are where new content would be needed.

## Real-game references (surfaced, not yet applied)

### FF12 Zodiac Job System: discrete boards, pick two

- There are 12 jobs, and each has its own License Board. Each character dual-classes by choosing
  exactly **2 of the 12 whole boards**.
- A character's identity is **which two boards they hold**, not where they walked. Cross-archetype
  builds come from the combination, not from distance on a shared map.

### FFX Sphere Grid: one shared board, different starting points

- There is one shared lattice that every character walks on, each starting at a different point.
  There are no drawn archetype regions. A character's identity is the path they took.
- Structurally this is **the closest real precedent** to what the owner described ("a tank that
  eventually casts a healer's ward" is just a long walk).
- Caveat to check before relying on it: FFX's standard grid does gate the routes between
  characters' home areas with lock nodes (Level 1-4 Key Spheres), so it is not literally wall-free.
  The Expert grid in the International/HD release puts everyone near the center and is the more
  open variant.

## Open question for the next session

Pick one of these families before drawing anything else:

1. **One shared walkable board with no drawn regions** (Sphere Grid style).
2. **Discrete per-archetype boards, combined by picking two** (Zodiac style).
3. **A named ring of pure and hybrid archetypes** (option D, refined: settle Mage, the 8 pures and
   the seam names).

## What happened after this spike (later tickets)

The question did not stay paused. Later attempts, in order (see `docs/USER_STORIES.md` for each
ticket's full text):

- **GG-T166 (wont-do):** fractal branching. Every tine forks into 3, with stat nodes interleaved
  along each tine. Superseded by GG-T167 once the owner said the intent was one shared map.
  Mockup: `artifacts/Warrior-Fractal-Skill-Tree-Example.html`.
- **GG-T167 (wont-do):** one shared ring, Ranger - Warrior - Cleric - Guardian - Alchemist - Mage,
  ordered so each seam matches existing sub-archetype content. Every node is a stat pick and
  abilities unlock by stat combination. Superseded by GG-T168 when the owner questioned whether a
  shared map is the right model at all. Mockup: `artifacts/Unified-Stat-Ring-Example.html`.
- **GG-T168 (open, awaiting owner review):** drop the topology entirely. Abilities get a
  stat-threshold `Requires` vector, each level grants a free stat point, and an ability becomes
  learnable once the hero's stats meet it. If adopted, it answers this spike by removing the
  premise: there is no board to draw. Mockup: `artifacts/Trail-Example.html`.

GG-T168's text says GG-T83 should probably close once the owner confirms GG-T168 is the final answer.
This record is kept either way, so a future "draw a board after all" session starts from A to D and
the two references instead of from nothing.

## Independent of the topology decision

The GG-T80 class/region audit (BIBLE.md §25.6.1, "Amended 2026-10-20") still holds and can be acted
on under any of these topologies. Its follow-ups do not need to wait for this decision:

- **Warrior is overloaded:** 8 real clusters across 31 classes but only 3 branches, so 14 classes
  (45%) match nothing (GG-T81: carve a Rogue region out of the excess).
- **Guardian's Deathguard is orphaned:** `darktemplar` and `jadeknight` run `deathguard_*` kits with
  no branch (GG-T82).
- **Ranger has branches but no classes:** Marksman/Trapper/Skirmisher have zero classes walking
  them, and `hexsniper`/`hexwarden` still run the `ranged_*` baseline kit (GG-T82).

GG-T168, if adopted, would reframe these region audits (see its ticket text). Even then, the
underlying content gaps (missing Deathguard and Rogue content, Ranger kits not retrofitted) are real.
