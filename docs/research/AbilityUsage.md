# Ability usage — does the bar earn its place? (GG-T246, 2026-11-05)

Owner: "the Abilities need to be utilized in the gameplay demos, to see if they are useful or not."

**How it was measured.** `AbilityUsageSoak` (`GAIDEN_ABILITY_RECORDS=<dir>`, 2 rounds) plays every hero class with its default bar, in rotating parties of five, across Green Valley I to Veshker Ruins III at each stage's recommended level. That's 26 battles with the greedy `HeroBot`. `python tools/records.py --abilities <dir>` credits each resolution with what followed it, up to the next action, turn, pincer, attack or wave.

- **impact/use** = damage + healing + 15 per kill + 40 per revive + 6 per status, tempo or movement effect.
- It's a rough yardstick and doesn't credit damage *prevented*, so taunts and wards read low by construction.
- Each class appears in only about 2 of the 26 battles, so treat a single class's numbers as anecdotes and the patterns as findings.

## Before → after the bot fix

| | before | after |
|---|---|---|
| victories | 16 / 26 | 16 / 26 |
| hero ability resolutions | 3344 | 3120 |
| distinct spells cast | 10 | 14 |
| orbs spent per battle | 6.5 | 11.9 |
| orbs overflowed (bank full) per battle | 46.8 | 21.0 |
| **minted orbs wasted** | **77%** | **58%** |
| abilities on a bar that never fired | 68 | 59 |

**Why the bot skipped its bar before:**
- Every spell paid a flat 1.5 per orb however full the bank was, so hoarding looked free even though every new orb overflowed.
- Most utility effects scored 0: knockback, pull, swap, step-back, ally haste, revive, ethers, scan, single cures, defensive and regen buffs, and how strong a debuff is. So they never became candidates.

**What changed** (`HeroBot.cs`, BIBLE §4.3):
- The price per orb now falls as the bank fills (0.2 when full).
- Every effect is valued from its own BuffDef and AbilityDef numbers.
- A clean pincer still outranks a spell of equal value.

## Most useful per use (after)

| ability | kind | cost | uses/battle | dmg/use | heal/use | impact/use |
|---|---|---|---|---|---|---|
| hexweaver_bloodharvest | Spell | 3 | 0.12 | 32 | 36 | 88 |
| sister_benediction | Skill | 0 | 0.92 | 0 | 42 | 86 |
| item:x_potion | Item | 0 | 15.73 | 0 | 59 | 60 |
| berserker_wildswing | Skill | 0 | 0.46 | 46 | 0 | 54 |
| berserker_lastmanswinging | Skill | 0 | 0.50 | 39 | 0 | 54 |
| fieldcaster_resupplydrone | Skill | 0 | 2.19 | 0 | 31 | 51 |
| skirmisher_runningriot | Skill | 0 | 9.38 | 35 | 0 | 49 |
| brawler_guard_break | Skill | 0 | 0.27 | 34 | 0 | 48 |
| aegiswardens_sanctuaryrite | Spell | 2 | 0.27 | 0 | 15 | 42 |

## Least useful per use (after)

| ability | uses/battle | impact/use | note |
|---|---|---|---|
| shadowstep_fade_step | 0.38 | 6 | a reposition with no follow-up value |
| warlord_marked_for_warband | 0.08 | 6 | a slow only, for a mana cost |
| primalpack_challenge / aegisguard_sacred_challenge / taunt | 0.5–4.2 | 6–7 | taunts: the yardstick can't see the damage they redirect |
| shieldmaiden_valkyrie | 0.08 | 9 | |
| deathguard_mana_siphon | **8.19** | 14 | cast constantly, and it's a mana *generator* (see below) |
| trapper_snare_net, warlord_battle_cry, greenninja_venomfang, ripper_rend | ≤ 0.12 | 13–16 | small numbers for a turn spent |

## Never fired with the chance (59, after)

- **Free melee finishers beaten by Cleave and similar skills** (AdjacentOnly, 6–16 dmg, cooldown 3–4): assassain_killingblow, basher_skullcrack, bruiser_haymaker, chromaninja_prismfang, combobrawler_knockoutcombo/openingjab, duelist_riposte/cut_the_casting_hand, fencer_fleche, fighter_combo, martialartist_palm, myrmidon_phalanx, opportunist_exploit, pandagirl_slam, pugilist_jabcross, raider_plunder, red/blue/black/yellowninja fangs, sellsword_contract, slasher_flurry, spellblade_quickstrike, streetfighter_dirtytrick, striker_finishingblow, swordmaster_perfectcut, thief_snatch. Cleave (about 29 per battle) out-damages them from the same tile, so each needs a reason to exist (bigger number, a rider, or no cooldown) or Cleave needs reining in.
- **Protection-buff skills**: cleric_grace, knight_oath, defender_wall, dervish_whirl, aegiswardens_preemptiveward. `protection` is −15% damage for **1 turn**, too small to ever beat a pincer.
- **Tempo/haste:** quicken, tempoweave_shortcut/swap, speedster_blitz, courier_relay. Haste 0.15–0.3 of a bar for a turn spent rarely wins against a pincer.
- **Self buffs and cures:** bluelion_roar, spellblade_followthrough, engineer_overclock, monk_peace, whiteninja_moonlit, alchemist_transmute, tinkerer_juryrig.
- **Spells:** blackwitch_shadowbolt, drifter_frostnova, harbinger_omen, hexmenders_mirrorcurse, operative_shock, redmage_fury, regen. They cost 1–2 orbs for 6–14 damage plus a two-turn debuff.

## For GG-T247 (mana tempering)

- **Even with the bot spending freely, 58% of minted mana overflows.** The bank fills faster than a five-hero party can spend it, which confirms the owner's "the bar is full almost every battle". The gain side has to come down, not just the bot's habits.
- **Mana Siphon** (`deathguard_mana_siphon`, 8.2 casts per battle) is a free, spammable mana source. Check it first.
- **Cheap spells are underpowered for their cost.** If mana becomes scarcer, the 1–2 orb spells above need more punch, or they'll stay unused.
- Re-run this soak after tuning to compare: `GAIDEN_ABILITY_RECORDS=<dir> dotnet test Tests/Core.Tests/Core.Tests.csproj --filter Ability_usage_soak`, then `python tools/records.py --abilities <dir>`.
