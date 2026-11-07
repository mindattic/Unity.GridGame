# Timeline tuning — re-entry model (GG-T318 → GG-T321)

Measured with `Tests/Core.Tests/TimelineTuningSoak.cs`: the first 15 campaign stages, 4 battles per stage per bot,
smart (`HeroBot`, randomness 0) and casual (randomness 0.5), heroes at each stage's recommended level. Each row covers
60 battles per bot. To reproduce: set `GAIDEN_TIMELINE_SOAK=<out.md>` (plus the knobs in the soak's header) and run
`--filter Name=Timeline_tuning_soak`.

**Columns:**
- **gap:** time between consecutive enemy turns.
- **clusters:** the share of gaps shorter than a minimum drag (0.85 s).
- **forced Guard:** the share of hero actions that were forced Guards.

## Before (old model, for reference)

| Source | Smart wins | Casual wins | Forced Guards |
|---|---|---|---|
| GG-T311, difficulty 10, rotating 5-hero parties | 79% | 33% | — |
| GG-T313, Green Valley I, the real 3-hero start, difficulty 10 | 6% | 0% | 35–50% of the party's actions |

## Shipped defaults (GG-T318)

Entry 4 s × (RefSpeed/Speed)^0.6, clamped 2–6 s. Recovery 1 s. Difficulty 10: MinGap 1.0 s, MaxSpread 1.0 s, jitter ±0.25 s.

| Difficulty, party | Smart wins | Casual wins | Median gap | Clusters | Forced Guard (smart / casual) | Actions/window (smart / casual) |
|---|---|---|---|---|---|---|
| 10, campaign | 100% | 90% | 2.1 s | 22% | 14% / 22% | 2.33 / 1.43 |
| 10, six | 100% | 98% | 1.9 s | 23% | 13% / 22% | 2.45 / 1.56 |
| 5, campaign | 100% | 97% | 1.7 s | 20% | 11% / 18% | 2.75 / 1.81 |
| 1, campaign | 100% | 100% | 1.8 s | 13% | 5% / 14% | 3.35 / 2.24 |

Green Valley I, the real 3-hero start:

| Difficulty | Smart | Casual | Forced Guard (smart / casual) |
|---|---|---|---|
| 10 | 4/4 | 0/4 | 18% / 24% |
| 5 | 4/4 | 2/4 | — |
| 1 | 4/4 | 4/4 | — |

## Sweeps at difficulty 10, campaign parties

| Variant | Smart | Casual | Clusters | Forced Guard (smart / casual) |
|---|---|---|---|---|
| Recovery 0.5 s | 100% | 87% | 18% | 15% / 25% |
| Recovery 0 | 97% | 87% | 23% | 18% / 25% |
| MinGap 0.85 s / MaxSpread 0.5 s | 100% | 87% | 33% | 16% / 26% |
| Recovery 0.5 s + MinGap 0.85 s / MaxSpread 0.5 s | 100% | 82% | 31% | 18% / 28% |
| No spacing, no recovery, no jitter | 98% | 83% | 38% | 14% / 25% |
| Speed exponent 1.0, entry min 1.5 s | 100% | 90% | 22% | 14% / 22% |
| Exponent 1.0 + no recovery + no spacing | 100% | 72% | 36% | 18% / 30% |
| Entry base 3 s (not the owner's 4 s) | 100% | 87% | 21% | 18% / 26% |
| Enemy strike ×1.5 (from 1.2) | 97% | 87% | 25% | 15% / 21% |
| Enemy strike ×1.8 | 98% | 75% | 23% | 13% / 21% |
| Enemy HP scale 0.6 (from 0.45) | 100% | 88% | 21% | 14% / 22% |
| Strike ×1.5 + HP 0.55 | 98% | 90% | 24% | 14% / 21% |

## What it shows

1. **The re-entry model fixes GG-T316.** On Green Valley I with three heroes at difficulty 10, forced Guards fall from
   35–50% of the party's actions to 18% (smart) and 24% (casual). The smart bot goes from 6% to 100% wins.
2. **At an average 4 s, the timeline no longer decides who wins; it decides how a battle feels.** No timeline setting moves the
   smart bot below 97%. GG-T311's 79/33 came from 2.4 s laps that buried even a perfect player in forced Guards.
3. **The smart bot is no longer a useful difficulty probe.** With 2.3+ actions per window it plays near-optimally, and
   even 1.8× enemy damage leaves it at 98%. The casual bot is the sensitive one: that run cost it 15 points.
4. **Skill still shows where it should.** Casual wins none of Green Valley I at difficulty 10 while smart wins all of it,
   and the casual bot is forced to Guard about 1.6× as often across the board.
5. **Clusters sit at 20–23% of gaps with the shipped spacing.** That's above the plan's 5–15% target. They come from big
   waves, eggs and remains that the spacer can't separate inside MaxSpread. Widening MaxSpread would cut them, but it also
   flattens the "clusters are the danger" moments the owner asked for.

## Decision (GG-T321, delegated by the owner 2026-11-07): enemies scale with the party fielded

**1. A skilled-human probe (`HeroBot.Skilled`).** It reads the board and the bar like the smart bot, takes 0.8 s to
plan a route (the smart bot takes 0.4 s), and picks by softmax at `SkillLevel` 80. It scored the same as the smart
bot everywhere (97–100%), so the soak keeps it as a third column. Board reading, not reflexes, is what wins on the 4 s
timeline.

**2. Global strength was the wrong lever.**

| Variant | Smart | Skilled | Casual |
|---|---|---|---|
| Strike ×2.5 | 90% | 97% | 48% |
| Strike ×1.8, HP 0.7 | 95% | 93% | 52% |
| Strike ×2.2, HP 0.75 | 85% | 93% | 38% |
| Strike ×2.0, HP 0.7 | 93% | 95% | 45% |
| Strike ×2.5, HP 0.7 | 92% | 82% | 32% |

The overall rates land in band, but each of these turned Green Valley I (the first battle, three heroes) back into a
wall: 0–50% even for the smart bot. Raising hero action costs (1.5 s skill, 0.7 + 0.45 s/tile drag) only added
forced Guards (26–30%) without separating skill.

**3. The cause is party size.** The starting three manage about 1.3 hero actions per enemy turn; six heroes manage
about 2.4. So enemy strike and MaxHP now grow with the heroes fielded (`BattleEngine.PartyScale`):
×(1 + `EnemyScalePerExtraHero` × heroes beyond 3). Test stages are unscaled.

| Per extra hero | Smart | Skilled | Casual |
|---|---|---|---|
| 0.25 | 100% | 98% | 57% |
| **0.30 (shipped)** | **100%** | **100%** | **45%** |
| 0.35 | 100% | 98% | 33% |
| 0.45 | 100% | 97% | 20% |

**Shipped, 0.30:**

| Difficulty, party | Smart | Skilled | Casual |
|---|---|---|---|
| 10, campaign | 100% | 100% | 45% |
| 10, six | 100% | 98% | 38% |
| 5, campaign | 100% | 100% | 97% |
| 1, campaign | 100% | 100% | 100% |

Green Valley I is unchanged: smart and skilled 4/4, casual 0/4.

**Read:** at difficulty 10, informed play wins and careless play loses about half the time, the same shape as the
79/33 the owner accepted for GG-T311, but now from party size rather than a frantic clock. The plan's "skilled
75–85%" target is not met. Over these 60 battles the probe bots are near-perfect board solvers, so only a human
playtest can say where a real skilled player lands. Difficulties 6–9 interpolate and haven't been measured. The step
from 45% casual at 10 to 97% at 5 is steep.
