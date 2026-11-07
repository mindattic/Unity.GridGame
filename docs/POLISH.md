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
| POL-C1 | Spacing comes only from the scale **8 / 16 / 24 / 32 / 48 / 64 / 96** (4 as the half step), via tokens; sizes from `Size`; layout widths and bands from stage units (`Fit`, GG-T272) | A rhythm the eye can feel | 8-pt grid (Material 3) | lint | ✅ every App/USS margin, padding, size and position is a token (GG-T272) |
| POL-C2 | Touch targets ≥ **132 × 132 px** (44 pt) | Finger accuracy | Apple HIG 44 pt; Material 48 dp; WCAG 2.2 SC 2.5.8 | layout | 🟡 slots 216×196 and tiles 180² pass; other elements unmeasured |
| POL-C3 | ≥ **24 px** between adjacent targets | Avoids mis-taps | Material 3 (8 dp spacing) | layout | ⬜ |
| POL-C4 | Primary actions sit in the **bottom 45%** of the stage | One-handed thumb reach | Hoober's thumb-zone research; Fitts's law | layout (primary-tagged elements) | ⬜ |
| POL-C5 | Nothing **off-stage, clipped, overlapping or inside an unsafe area**, on any supported aspect. Text wraps where it can, cleanly (GG-T305 `TextFit`: no lone last word, no line ending in a single letter, overlong words hyphenated; Settings → Text wrapping: Word wrap / Hyphenate); a single line that can't (a button, tab, name, chip, badge, header) is the shared **marquee** (GG-T271/T305), applied automatically whenever it overflows: it holds at the left, scrolls to show the rest, flashes three times, then resets to the left (Reduce Motion: an ellipsis) | Basic finish | Apple HIG safe areas | layout across `aspects` | 🟡 only horizontal NoWrap overflow and squashed tabs/buttons are checked |
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
| POL-E1 | Durations come only from the **timing tokens**: instant 0.08 · quick 0.15 · standard 0.25 · emphasis 0.4 · dramatic 0.7 · cinematic ≥ 1.2 s | A shared rhythm; values tuned by eye drift apart | Material 3 motion durations | lint | ✅ GG-T253: every Tween duration is a token |
| POL-E2 | Easing comes only from the **tokens** out · in-out · back · snap · arc (0 → 1 → 0); no linear UI motion | Natural motion | 12 principles of animation (slow in/slow out); Material 3 easing | lint | ✅ GG-T253: `Ease.Arc` replaced the hand-written `Sin(k·π)` curves |
| POL-E3 | **No random durations**; randomness only in particle position, rotation and scale | Timing is part of the feel and must be repeatable | Game-feel practice | lint | ✅ GG-T253: particle staggers are fixed fractions of a token |
| POL-E4 | One **element table** (`vfxstyle.json`) sets each element's color, shape language, motion verb and sound family, and every burst, status visual, projectile and impact reads from it | This is what makes effects look like one family instead of a collection | Art-direction practice; style guides | lint + vfxsheet | ✅ GG-T253: `vfxstyle.json` covers every element and status; no per-element or per-status switch left in the battle VFX code |
| POL-E5 | **Intensity tiers** minor / major / ultimate scale particle count, screen shake and hit-stop | Feedback proportional to impact | Game feel / juice (Swink; Jonasson & Purho) | lint + vfxsheet | ✅ GG-T253: `Tiers` in `vfxstyle.json`, read by the hit playback |
| POL-E6 | Hits go **anticipation → impact (hit-stop 50–120 ms) → follow-through** | Weight and readability | 12 principles of animation | vfxsheet + R4 | 🟡 GG-T253: brace → burst + tier hit-stop (0/40/90 ms) → `Ease.Back` recoil; judged on the contact sheets |
| POL-E7 | **Reduce Motion** turns down every shake, flash and large motion; never more than **3 flashes per second** | Comfort and photosensitivity | WCAG 2.2 SC 2.3.1; platform accessibility guidelines | lint + uicheck | 🟡 lunges, shakes and Clutch flash are gated; the rest unchecked |
| POL-E8 | Effects never cover numbers or the active unit for longer than **0.4 s**; at most **3 concurrent bursts** per tile | Readability beats spectacle | Game-feel practice | vfxsheet + R5 | ⬜ |

### F. Audio

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-F1 | Cue names follow the taxonomy **`ui.* / battle.* / element.* / status.* / story.*`** (old names aliased) | Findable, mixable, consistent | Game-audio practice | assets | ✅ every cue on the taxonomy, old names aliased (GG-T254) |
| POL-F2 | SFX loudness **−16 LUFS ± 2**, true peak ≤ **−1 dBTP** | Even mix, no clipping | EBU R128 / ITU-R BS.1770; mobile loudness norms | loud | ✅ 57/57 SFX −16.5 … −14.7 LUFS, ≤ −1.5 dBTP (GG-T254) |
| POL-F3 | Music sits at **−18 LUFS ± 2** and **ducks ≥ 6 dB** under cut-ins and dialogue | Speech and big moments stay clear | Broadcast/game mixing practice | loud + R6 | ✅ 5/5 tracks −18.5 … −18.0 LUFS; −8 dB duck under cut-ins, fanfares, dialogue (GG-T254) |
| POL-F4 | Cues fired often (move, hit) have **≥ 3 variants**, and repeats are throttled | Avoids machine-gun repetition | Game-audio practice | assets | ✅ move ×6, hit ×7, 40 ms throttle |

### G. Balance and pacing

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-G1 | Per campaign stage, the **casual bot wins 55–85%** with **20–60% HP left** | Hard enough to matter, fair enough to finish | Flow (Csikszentmihalyi); difficulty-curve practice | records (BalanceReport) | 🟡 Tithe Moors I 80% / 61%; rule-break finales unmeasured |
| POL-G2 | No stage's casual win rate is **> 20 points** below the stage before it | No difficulty spikes | Difficulty-curve practice | records | ⬜ |
| POL-G3 | The mana bank is full on ≤ **8%** of turns; ≤ **20%** of minted orbs are wasted | A full bar should be an earned peak (owner, GG-T247) | Resource-economy design | records | ✅ 0% full / 0% wasted on every campaign stage, cityruins-03 included (GG-T265, 206 bot battles). GG-T247's "casual bot holds 2+ orbs on ≥ 30% of turns" is **not** a rule: it measured hoarding, and lifting the earn rate to buy it flooded the stages where the casual bot doesn't spend (BIBLE §3.1.9) |
| POL-G4 | Every equipped ability fires in ≥ **25%** of the battles where it could be used | Every ability earns its slot | Content-usefulness balance practice | records `--abilities` | 🟡 73.4: 33 of 124 under 25%, none never-fired outside haste-only skills (GG-T263; BIBLE §7.2) |
| POL-G5 | Impact per use within **0.5× – 2×** of its role's median | No dead or dominant abilities | Balance practice (outlier bands) | records `--abilities`; utility is measured, not flat (GG-T264, `records.credit_utility`: damage-up share, damage prevented/absorbed, ticks, skipped/slowed enemy turns, expected misses) | 🟡 78.0: the lows are pure utility buffs, the one high is Trapline |
| POL-G6 | **6–20 player actions** per wave | Waves neither trivial nor a slog | Session-pacing practice (BIBLE A §0.3) | records | ⬜ |
| POL-G7 | No single party composition or strategy wins **> 90%** of all stages for the casual bot | No dominant strategy | Game-theory balance practice | records (party rotation) | ⬜ |

### H. Art cohesion

| ID | Rule | Why | Source | Measured by | Status |
|---|---|---|---|---|---|
| POL-H1 | **Portrait standard** (spec below; amended 2026-11-07, GG-T352): a 1024×1024 RGBA canvas with a transparent background, never trimmed or shrunk; the figure's box fitted in a 640 square is its drawn size (640 = one person, a smaller box = a smaller creature) | People must be the same scale side by side; the figure box *is* the scale | Art-direction practice | `check_assets.art_findings` | 🟡 92.7: 13 portraits float or under-fill (frogs, toad, undead, phantom, sage, swordmaster, monk, hag03, mannequin, technician) |
| POL-H2 | **Icon standard** (spec below): 88×88 RGBA on a 22×22 pixel grid ×4, ≤ 9 colours (8 + the dark outline); UI icons 64×64 | Icons from many sources must read as one set | Icon-system practice (Material 3 icons) | `check_assets.art_findings` | ✅ 100: all 1,514 icons |
| POL-H3 | **Backdrop standard** (spec below): a 1:2 JPEG at the art's own resolution named `<region>_NN`, mean luminance 0.25–0.50, one top-left key light; units stay ≥ 3 : 1 against the board | The board must always read | Value-structure / art-direction practice | `check_assets.art_findings` (size, value band); R10 (light) | ✅ 100 measured (means 0.29–0.45); light direction by review |
| POL-H4 | No asset drawn above its native resolution on the largest stage | No blur | Asset-pipeline practice | `check_assets.art_findings` | ❌ 0: dialogue draws 640-px portraits at 1200 px (1.9×); needs ≥ 1200-px sources or a smaller figure height |

#### H specs (GG-T256)

**Portraits** (`Resources/Portraits/<class>.png`; amended 2026-11-07, GG-T352, owner: "all characters are 1024x1024 with transparent backgrounds; that's the rule, that's what the assets are"):
- **Canvas.** A **1024×1024** RGBA PNG with a transparent background, exactly as the art comes: never alpha-trimmed, never shrunk (`tools/import_art.py` fits a source of another size onto the canvas whole). The old 640-px canvas, fill, foot-line and side-margin rules are retired.
- **Figure and scale.** The class's `Figure` box (the alpha bounds, written at import, BIBLE §12.1.2) is what whole-figure views draw. Its size is the box fitted inside a 640 square, never enlarged (`Art.FigureSize`): **640 = one person**, and a creature whose box is smaller draws that much smaller.
- **Rendering.** Dialogue draws a 640 figure at `DialogueScreen.FigureHeight` (1200) and a smaller one proportionally (GG-T256). Tokens, turn icons, cards and faces never use a separate image: they frame the whole canvas through the class `Thumbnail` (BIBLE §12.1.1).

**Thumbnails.** These are not files: `classes.json` `Thumbnail {Zoom, X, Y}` frames the portrait (BIBLE §12.1.1). The face sits in the upper third of the frame, with the eyes on the frame's upper-third line and the same zoom family for the same body type. Judged in review (R10).

**Icons** (`AbilityIcons`, `ItemIcons`, `StatusIcons`: generated by `tools/make_icons.py` from `tools/icons.json`):
- **Pipeline.** A game-icons.net silhouette, a three-stop vertical gradient from one `icons.json` palette (light top → base → dark bottom), a hard alpha edge, 8 colours, a 1-logical-px dark outline, centred on a 22×22 grid, then ×4 nearest-neighbour to **88×88**.
- **Rules.** The grid is exact (every 4×4 block is one colour). At most **9** colours. The palette comes from `icons.json` `palettes`, chosen by meaning: element palettes for spells and status (fire, ice and so on, matching `Tokens.Palette.Element`), rarity and material palettes for items.
- **UI icons** (`Icons/`) are **64×64** RGBA.
- **Hand-made icons** go through the same pipeline; nothing is drawn outside it.

**Backdrops** (`Backgrounds/<region>_<NN>.jpg`, GG-T353):
- **The art's own resolution**, never downscaled: 1536×3072 for the GridGame2026 biomes, 768×1536 for the owner's region art; JPEG (RGB, q95), 1:2 portrait. Named after the STORY.md region, numbered from 01.
- **Mean luminance 0.25–0.50**, so units and UI always read on top (today 0.29 for city ruins to 0.45 for desert).
- **One key light from the upper left**, with value falling off toward the bottom where the board sits. Saturation stays below the units' own.
- Checked by `check_assets` (size, format, value band) and review R10 (light direction, saturation).

**Enforcement.** `tools/check_assets.py` fails on hard breaks: a portrait that isn't a 1024×1024 RGBA canvas or is barely transparent (under 20% of it clear), an icon at the wrong size, or a backdrop that isn't a 1:2 JPEG/PNG at least 1536 tall named `<region>_NN`. Everything else is printed as an art warning. `tools/polish.py` scores POL-H1 to H4 from the same `art_findings()`.
| POL-H5 | Side-by-side test: a stranger can't pick out "the asset from somewhere else" | Cohesion | Art-direction practice | R10 | ❌ owner: "a collection of random effects" |


### Units (GG-T272, owner 2026-11-05: "measure things in agnostic units")

Three kinds of length, each with one job:

| Kind | Token | Scales with | Use for |
|---|---|---|---|
| Spacing | `Space.S0…S96` / `--space-*` | resolution only | margins, paddings, gaps, gutters |
| Element size | `Size.*` | resolution only | icons, pips, bars, portraits, table columns, tap targets |
| Stage unit | `Fit.W(%)`, `Fit.H(%)`, `Fit.Edge`, `Length.Percent` | resolution **and** aspect | panel widths, vertical bands, screen-edge gutters |

Design px are already resolution-agnostic: the panel scales uniformly from the 1170×2532 design stage to every device (§26.2). So type, spacing and fixed-size art stay in design px, and a 40-px label reads the same on every phone. What design px don't absorb is **aspect**: a wider or taller stage (up to 1424 wide or 2730 tall, §26.3) has extra room. Layout that should breathe uses `Fit` (percent of the safe-area stage, read when the screen builds; every screen rebuilds on `GameApp.StageResized`) or flex. `Fit.Edge` (24) is the one screen-edge gutter. The battle board's 1080-px grid and its HUD rows are fixed game geometry (`BoardTop`, `BannerTop`, `InfoPanelTop`), centered and padded per §26.3.

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
