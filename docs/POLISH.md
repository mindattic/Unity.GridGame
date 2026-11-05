# The Polish Standard

GridGame Gaiden's official definition of *polish*, its measurable rules, and the pass that runs each test cycle. Bible law: [GG-LAW-14](BIBLE.md#GG-LAW-14). Tickets: GG-T249 (this document), GG-T250 (tokens), GG-T251 (audits + scorecard), GG-T252–T256 (dimension work).

## 1. Definition

> **Polish** is the measured distance between "it works" and "a stranger would pay for it." A build is polished in a dimension when it passes that dimension's rules below. Every test cycle runs a **polish pass** that scores every dimension. **No score may go down** (the ratchet). Anything a machine can't measure gets a review-checklist item, scored against the recorded clip of that cycle.

Polish is not a phase at the end. Every cycle leaves the game measurably more polished than the last.

Units: design pixels at the 1170-px-wide minimum stage (BIBLE §26.1). **3 design px ≈ 1 pt** (1170 px ≈ 390 pt iPhone width).

## 2. Rules

Each rule has an ID (cite it in tickets, commits and audit output), a threshold, the reason, its source, how it's measured, and its status when this document was written (2026-11-05).

**Measured by** names an automated check or a checklist item:
- `layout`: the `LayoutAudit` automation endpoint (AutomationServer.cs), extended by GG-T251.
- `lint`: `tools/polish_lint.py`, a static scan of App/ code and Theme.uss (GG-T251).
- `assets`: `tools/check_assets.py`. `art`: `tools/artreview.py`.
- `records`: battle-record soaks via `tools/records.py` (GG-T246/T247/T255).
- `vfxsheet`: harness contact sheets (GG-T253). `loud`: `tools/loudness.py` (GG-T254).
- `uicheck`: `tools/uicheck.py`.
- `Rn`: an item in the review checklist (§5).

Status values: ❌ fails, 🟡 partly passes or partly measured, ✅ passes, ⬜ not yet measured.

### A. Type

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-A1 | Font sizes come only from the type scale **24 / 30 / 36 / 40 / 48 / 60 / 76 / 96 / 120** (ratio ≈ 1.25), via tokens | One scale gives hierarchy at a glance; ad hoc sizes read as noise | Modular-scale typography; Material 3 type scale | lint | ❌ 13 USS sizes + ~30 inline sizes + 11 `FloatText` sizes |
| POL-A2 | Body text ≥ **40 px** (13 pt); captions ≥ **30 px** (10 pt). 24 px only for non-essential badges that never carry information alone | Legible at arm's length on a phone | Apple HIG (body 17 pt, minimum 11 pt); Material 3 | layout | 🟡 body is 40 px; captions unmeasured |
| POL-A3 | Real font weights only: **no faux bold** (`FontStyle.Bold`, `-unity-font-style: bold`) | SDF faux bold dilates and rounds glyphs, which looks "bubbly" (owner, GG-T245) | Typographic practice; Unity SDF behavior | lint + layout | ❌ 8 C# sites + 9 USS rules |
| POL-A4 | At most **3 font roles** per screen: Body, Heavy, Display (`Fonts.cs`) | Too many faces looks like a ransom note | Material 3; general type practice | layout | 🟡 roles exist; unchecked per screen |
| POL-A5 | Wrapped text measure ≤ **60 characters** per line | Long lines are hard to track; mobile wants a shorter measure | Typographic measure (45–75 characters) | layout | ⬜ |
| POL-A6 | Long text **wraps or shrinks by one shared fit rule**, never per-site length checks (`Length>44?32:…`) | Same text behaves the same everywhere | Nielsen #4 (consistency) | lint | ❌ several per-site checks |

### B. Color

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-B1 | Every color comes from **tokens**: a semantic palette (team, element, status, rarity, gold/mana/HP) plus the menu-theme surfaces (`Art.MenuTheme`) | One palette is what makes a game look like one product | Design-token practice; Material 3 color roles | lint | ❌ ~187 USS literals + 345 `new Color()` literals in App/ |
| POL-B2 | Text contrast ≥ **4.5 : 1**; large text (≥ 54 px, or ≥ 42 px Heavy) ≥ **3 : 1** | Readable in sunlight, at low brightness, and by low-vision players | WCAG 2.2 SC 1.4.3 | layout | ⬜ |
| POL-B3 | Icons, gauges and UI boundaries ≥ **3 : 1** against what is behind them | Non-text UI must read too | WCAG 2.2 SC 1.4.11 | layout | ⬜ |
| POL-B4 | **One meaning per hue family.** Element colors are never reused for statuses or UI messages | Color must mean something; reuse makes it noise | Nielsen #4; semiotics of game UI | lint (palette table) | ❌ Ice blue = Haste = "Pushback!"; `ColorFor` is a private switch (Playback.cs) |
| POL-B5 | **Never color alone:** every color-coded state also has a shape, letter, icon or text | Color-blind players | WCAG 2.2 SC 1.4.1 | R9 | 🟡 buff pips have letters; elements unchecked |
| POL-B6 | Element palette pairs stay distinguishable under deuteranopia/protanopia simulation (ΔE₀₀ ≥ **15**) | About 8% of men have red-green color blindness | Color-vision deficiency research; CIEDE2000 | lint (palette sim) | ⬜ |

### C. Layout and touch

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-C1 | Spacing comes only from the scale **8 / 16 / 24 / 32 / 48 / 64 / 96**, via tokens | A rhythm the eye can feel | 8-pt grid (Material 3) | lint | ❌ 22 ad hoc margin/padding values; 82 literal widths/heights |
| POL-C2 | Touch targets ≥ **132 × 132 px** (44 pt) | Finger accuracy | Apple HIG 44 pt; Material 48 dp; WCAG 2.2 SC 2.5.8 | layout | 🟡 slots 216×196 and tiles 180² pass; other elements unmeasured |
| POL-C3 | ≥ **24 px** between adjacent targets | Avoids mis-taps | Material 3 (8 dp spacing) | layout | ⬜ |
| POL-C4 | Primary actions sit in the **bottom 45%** of the stage | One-handed thumb reach | Hoober's thumb-zone research; Fitts's law | layout (primary-tagged elements) | ⬜ |
| POL-C5 | Nothing **off-stage, clipped, overlapping or inside an unsafe area**, on any supported aspect | Basic finish | Apple HIG safe areas | layout across `aspects` | 🟡 only horizontal NoWrap overflow and squashed tabs/buttons are checked |
| POL-C6 | **Use the space:** on the widest stage (1424 px) content width grows ≥ **80%** of the extra width; no fixed widths that leave dead gutters | Wide phones should get more content, not more border | Responsive-layout practice | polish.py (content span at 1170 vs 1424) | 🟡 GG-T252 made the confirm column, pause, title menu, confirm dialog, PaywallUi, Camp side column, battle feed, ability-bar slots and Abilities bar cells proportional; other fixed widths remain |
| POL-C7 | ≤ **7 choices** per visible menu group (group or nest beyond that), and one visually primary action per screen | Decision time grows with choices | Hick–Hyman law; Miller 7±2 | layout (rows per group) | ⬜ Camp has many rows in one group |
| POL-C8 | **Even gutters:** in a row or wrapping grid of like-sized items, the left, right and between-item gaps are equal (±2 px) and the gap sits on the space scale. No ragged right edge from a fixed item width that doesn't divide the row | Even gutters are what make a grid look designed (owner 2026-11-05: "gutters and spacing are important") | 8-pt grid; Material 3 layout grids | layout | 🟡 Bestiary grid fixed by GG-T252 (`EvenGrid`: cells stretch so every gutter is 16 px); party cards still 28 px edges vs 22 px gaps |
| POL-C9 | **No dead bands:** at every aspect, each side margin ≤ **10%** of the stage width and no empty horizontal band > **15%** of its height (art-led screens such as the title, credits, crawls and dialogue are exempt from the band check) | Content grows or redistributes to fill the phone, never floats in a sea of backdrop (owner 2026-11-05: "scaling to fit those various aspect ratios and resolutions is important") | Responsive-layout practice | layout | ❌ first pass: short list screens (Adventure, Quests, Bounties, Talk, Party) leave 1100–2250 px empty |

### D. Information and feedback

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-D1 | **Press-and-hold inspects anything:** ability slots, item stacks, buff/debuff pips, unit tokens, timeline icons, a charging enemy's ability, gear and skill nodes. It opens an Inspect card with name, cost, cooldown, shape/range and numbers | Players must know what a thing does before they commit | Nielsen #6 (recognition over recall), #10 (help) | uicheck (hold probe per element kind) | ✅ GG-T252: `InspectCard` on slots, units, status pips, timeline icons (turn + cast), a charging foe's whole spell, Abilities bar/rows, every item row, talent nodes, Bestiary ability chips |
| POL-D2 | **Outcome preview before commit:** damage/heal range, kills, status chance and mana gain on the target confirm card and the pincer preview | Lets players plan; prevents "I didn't mean that" | Nielsen #1 (visibility of status), #5 (error prevention) | uicheck | ✅ GG-T252: `OutcomePreviewer` (Core, pure, sampled) on the confirm card and as tags on pincered foes during a drag |
| POL-D3 | Visible touch response within **100 ms** | Feels instantaneous | Nielsen response-time limits (0.1 s) | uicheck (tap → first changed frame) | 🟡 `Ui.Pressable` exists; unmeasured |
| POL-D4 | Every state change has **visual + audio** feedback | No silent changes (existing Law 5) | Game feel / juice practice | assets (event → cue map) | 🟡 |
| POL-D5 | Costly or destructive actions confirm, and every gesture can be **cancelled** before commit | User control and freedom | Nielsen #3 | uicheck + R7 | ✅ GG-T252: release off the board, or home with nothing shoved, cancels a drag |
| POL-D6 | **No dead taps:** anything styled as tappable responds; anything that responds looks tappable | Affordance | Norman (affordances/signifiers); Nielsen #4 | layout (clickable classes vs handlers) | ⬜ |
| POL-D7 | **Every mechanic the game uses is taught** in a tutorial lesson, hint or first-time tip | Nobody reads manuals | Nielsen #10; onboarding practice | R8 | 🟡 GG-T252 added lessons for holding to read, the hold timer/cancel, buffs/debuffs and armor plates; not yet re-reviewed against R8 |

### E. Motion and VFX cohesion

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-E1 | Durations come only from the **timing tokens**: instant 0.08 · quick 0.15 · standard 0.25 · emphasis 0.4 · dramatic 0.7 · cinematic ≥ 1.2 s | A shared rhythm; values tuned by eye drift apart | Material 3 motion durations | lint | ❌ 47 distinct duration literals |
| POL-E2 | Easing comes only from the **tokens** out · in-out · back · snap; no linear UI motion | Natural motion | 12 principles of animation (slow in/slow out); Material 3 easing | lint | ❌ 3 easings + 43 hand-written `Sin(k·π)` curves |
| POL-E3 | **No random durations**; randomness only in particle position, rotation and scale | Timing is part of the feel and must be repeatable | Game-feel practice | lint | ❌ `Random.Range` durations (e.g. ElementBurst Fire) |
| POL-E4 | One **element table** (`vfxstyle.json`) sets each element's color, shape language, motion verb and sound family, and every burst, status visual, projectile and impact reads from it | This is what makes effects look like one family instead of a collection | Art-direction practice; style guides | lint + vfxsheet | ❌ colors in `ColorFor`, shapes per VfxFamily, 12 hand-written status effects |
| POL-E5 | **Intensity tiers** minor / major / ultimate scale particle count, screen shake and hit-stop | Feedback proportional to impact | Game feel / juice (Swink; Jonasson & Purho) | vfxsheet | ❌ none |
| POL-E6 | Hits go **anticipation → impact (hit-stop 50–120 ms) → follow-through** | Weight and readability | 12 principles of animation | vfxsheet + R4 | ⬜ |
| POL-E7 | **Reduce Motion** turns down every shake, flash and large motion; never more than **3 flashes per second** | Comfort and photosensitivity | WCAG 2.2 SC 2.3.1; platform accessibility guidelines | lint + uicheck | 🟡 lunges, shakes and Clutch flash are gated; the rest unchecked |
| POL-E8 | Effects never cover numbers or the active unit for longer than **0.4 s**; at most **3 concurrent bursts** per tile | Readability beats spectacle | Game-feel practice | vfxsheet + R5 | ⬜ |

### F. Audio

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-F1 | Cue names follow the taxonomy **`ui.* / battle.* / element.* / status.* / story.*`** (old names aliased) | Findable, mixable, consistent | Game-audio practice | assets | 🟡 `ui.*` only; 33 flat battle cues |
| POL-F2 | SFX loudness **−16 LUFS ± 2**, true peak ≤ **−1 dBTP** | Even mix, no clipping | EBU R128 / ITU-R BS.1770; mobile loudness norms | loud | ⬜ |
| POL-F3 | Music sits at **−18 LUFS ± 2** and **ducks ≥ 6 dB** under cut-ins and dialogue | Speech and big moments stay clear | Broadcast/game mixing practice | loud + R6 | ❌ no ducking |
| POL-F4 | Cues fired often (move, hit) have **≥ 3 variants**, and repeats are throttled | Avoids machine-gun repetition | Game-audio practice | assets | ✅ move ×6, hit ×7, 40 ms throttle |

### G. Balance and pacing

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-G1 | Per campaign stage, the **casual bot wins 55–85%** with **20–60% HP left** | Hard enough to matter, fair enough to finish | Flow (Csikszentmihalyi); difficulty-curve practice | records (BalanceReport) | 🟡 Green Valley I 80% / 61%; rule-break finales unmeasured |
| POL-G2 | No stage's casual win rate is **> 20 points** below the stage before it | No difficulty spikes | Difficulty-curve practice | records | ⬜ |
| POL-G3 | The mana bank is full on ≤ **8%** of turns; ≤ **20%** of minted orbs are wasted | A full bar should be an earned peak (owner, GG-T247) | Resource-economy design | records | ❌ greedy bot 17.4% full / 45.6% wasted; ability soak 77% wasted |
| POL-G4 | Every equipped ability fires in ≥ **25%** of the battles where it could be used | Every ability earns its slot | Content-usefulness balance practice | records `--abilities` | ❌ 68 equipped abilities never fired (GG-T246 baseline) |
| POL-G5 | Impact per use within **0.5× – 2×** of its role's median | No dead or dominant abilities | Balance practice (outlier bands) | records `--abilities` | ⬜ |
| POL-G6 | **6–20 player actions** per wave | Waves neither trivial nor a slog | Session-pacing practice (BIBLE A §0.3) | records | ⬜ |
| POL-G7 | No single party composition or strategy wins **> 90%** of all stages for the casual bot | No dominant strategy | Game-theory balance practice | records (party rotation) | ⬜ |

### H. Art cohesion

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-H1 | **Portrait standard:** canvas 640 px tall, transparent background, figure fills ≥ 95% of the height (props exempt), rendered at one shared figure height | People must be the same scale side by side | Art-direction practice | assets | 🟡 figures fill the height; canvases vary 336–640 px wide; render normalized by GG-T248 |
| POL-H2 | **Icon standard:** one canvas size, one silhouette padding, palette from tokens, one stroke weight | Icons from many sources must read as one set | Icon-system practice (Material 3 icons) | assets + art | ⬜ |
| POL-H3 | **Backdrops** share one light direction and value range, and units stay ≥ 3 : 1 against the board | The board must always read | Value-structure / art-direction practice | art | ⬜ |
| POL-H4 | No asset drawn above its native resolution on the largest stage | No blur | Asset-pipeline practice | assets | ⬜ |
| POL-H5 | Side-by-side test: a stranger can't pick out "the asset from somewhere else" | Cohesion | Art-direction practice | R10 | ❌ owner: "a collection of random effects" |

## 3. Scoring

- **Automated rule:** score 0–100 = the share of its subjects that pass (screens, elements, abilities, cues, assets).
- **Checklist item:** 0–3, mapped to 0 / 33 / 67 / 100.
- **Dimension score:** the mean of its rules' scores, equally weighted.
- **Every aspect:** the full pass runs the layout audit on every screen at every `harness.py aspects` shape (9:23, 9:21, 9:20, 9:19.5, 9:18, 9:16, 2:3, 3:4). Each **POL-C rule scores at its worst aspect**, and the scorecard names that aspect. POL-C6 compares each screen's content span at 9:19.5 (1170 px wide) with 9:16 (1424 px wide). `--quick` audits the default window only and never writes the baseline.
- `tools/polish.py` (GG-T251) writes `artifacts/polish/<date>/scorecard.json` plus `report.html`: every dimension, every failing rule with its subject and screenshot, and the delta against the baseline.

## 4. The polish pass and the ratchet

Run every test cycle, before any release build:

1. **Build** (`tools/build.ps1`, zero `error CS`).
2. **Measure:** `python tools/polish.py`. It runs `layout` across `sweep` and `aspects`, `uicheck`, glyph and asset checks, `lint`, `vfxsheet`, `loud` and the `records` soak, all **recorded** with MindAttic.Capture.
3. **Review:** fill in the §5 checklist against the recorded clip, then add it to the scorecard.
4. **Ratchet:** compare with `docs/polish/baseline.json`.
   - Any **dimension score that drops fails the pass.** The regression gets a `fix` ticket and is worked first.
   - A rule-level drop inside a rising dimension is reported, not failed.
5. **Raise the bar:** when the pass is clean, commit the new scores as the baseline.
6. **Triage:** the **three lowest-scoring rule groups** become tickets for the next cycle, citing their rule IDs.

**Thresholds only tighten.** Loosening a threshold, or adding a rule exemption, needs an owner decision recorded in `docs/AMENDMENTS.md`.

## 5. Review checklist (score 0–3 each, against the recorded clip)

| ID | Item | Rules |
|---|---|---|
| R1 | Title screen and first 30 s feel like one product: type, color, motion, music | A, B, E, F |
| R2 | Every screen has a clear primary action and visual hierarchy; nothing feels cramped or empty | C4, C6, C7 |
| R3 | You can always tell whose turn it is, what's about to happen and why you took damage | D1, D2 |
| R4 | Hits have weight: anticipation, impact, follow-through; big moves feel bigger than small ones | E5, E6 |
| R5 | Effects never hide the numbers or the board state that matter | E8 |
| R6 | Audio mix: speech and cut-ins are clear, nothing is harsh or repetitive | F2–F4 |
| R7 | Mistakes are cheap: cancels exist and costly actions confirm | D5 |
| R8 | A new player would learn every mechanic they meet without leaving the game | D7 |
| R9 | Play it in grayscale: every state still reads | B5 |
| R10 | Side by side, all portraits, icons, backdrops and effects look like one art direction | H1–H5, E4 |
| R11 | Pacing: no stretch of waiting, no wave that drags, no screen you want to skip twice | G6 |
| R12 | Would a stranger pay for what's on screen? Name the one thing that most says "no" | all |
