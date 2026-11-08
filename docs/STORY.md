# GridGame Gaiden — Story Bible (GG-T341)

Owner, 2026-11-07: *"flush out all story in the entire game … completely reinvent the story based on the characters and
artwork of the world that exist in the repo; it's distinctive enough that all these different groups and their styles
means there is a story already built into the artwork and different clothing and environments."*

This file is the single source for every word of story in the game: crawls (`campaign.json`), dialogue
(`dialogues.json`), quests, caravan barks, mercenaries, unit `Description`/`Lore`, credits. Every faction below was read
out of the art — what people wear, what they carry, and the ground they stand on. When a new unit arrives, find its
faction here first (or add one here first), then write its lore. The previous story (the Longmother, the blight, the
Footfall Oath, Veshker) is retired in full; nothing of it survives.

---

## 1. The world: Under Morrow

**Morrow** is the moon. It hangs so low it fills a third of the sky over the red desert, pale and cratered, close
enough to see weather on it. It has been *nearing* for longer than anyone has records — a hand's width closer every
lifetime. People call this the **Nearing**.

Morrow pulls at more than the sea. It pulls at **aether**: the quiet current in every living thing that lets a body
heal, a will push, a spell catch. Near Morrow's full face, wounds stay open longer and old people simply stop. Mages
learned to catch aether the way millers catch a river — that is all magic is.

The old world tried three answers to the Nearing, and each answer is now a place:

| Answer | What it was | What it is now | Theme |
|---|---|---|---|
| **Hold it back** | The **Anchors**: colossal stone arches raised across the northern ice, built to push Morrow away with gathered aether. | Still standing, still humming, half-frozen. They work, a little, if they are fed. | `cave` — **The Anchor Wastes** |
| **Leave** | The **Arks**: sky-ships meant to carry a city away from Morrow's pull. | They fell. Their skeletons lie rotting in a golden fog-fen. | `swamp` — **The Arkgrave** |
| **Do nothing** | Nobody chose it; it happened anyway. Pieces of Morrow broke off and came down. | The **Shardfall**: red sand, red rivers, colossal curved moon-shards jutting like tusks. Where the shards bleed, the **Ember** wakes. | `desert` — **The Shardfall** |

Between them lie the **Tithe Moors** (`greenvalley`): grey-green moss country of farm-holds and standing stones,
dotted with the white domes of the Concord's **Stills**. And at the end of every road is **Halcyon** (`cityruins`), the
Spire City, neon under a pink sky, its towers drinking light from beams that run north to the Anchors. Where the beams
come down, at the foot of the spire, stands the greatest of the domes: **the Still** (`still`), where all of it is
collected into tanks before it climbs.

## 2. The tithe

The **Pale Concord** rules Halcyon and claims every hold. Its doctrine is simple and partly true: *the Anchors hold
Morrow back, and the Anchors must be fed.* So every hold pays the **tithe** — not in coin but in aether. Tithe-takers
come with the white domes (Stills), and the people walk in, and walk out lighter.

Pay too much and you come back **Hollow**: walking, obedient, blank. The Hollow wear the white tithe-shroud they were
given at the door. Whole villages of them shuffle the roads in white hoods (the `undead` crowds), drifting toward the
nearest Still because it is the last thing they remember.

The part that is not true: the Anchors need far less than the Concord takes. The difference flows up the spire of the
**Hierarch**, who has drunk tithe for two hundred years and does not age. The Concord's own engineers half-know.
Nobody who says it aloud stays un-Hollow for long.

## 3. Factions (read from the art)

Colour and cut tell you who someone answers to. White and porcelain is the Concord. Neon streetwear is the Street.
Mail and tabards are the free holds. Robes, straw hats and masks are the Lantern Road. Rags and dark veils are the
fen-dark. Black armour and red capes are the Ember.

### 3.1 The Pale Concord — white, clean, certain
The power. Everything it owns is white: armour, robes, domes, flesh grown in its vats.
- **The Choir** (`cultist01–03`): white ponchos printed with violet circuit-glyphs, faceless hoods. Engineer-priests
  who run the Stills and sing the aether out of people. They never show faces; the glyphs are the hymn.
- **The Red Seal** (`templar00–05`): white robes, red cross-seal. Tithe-wardens; they collect from holds that don't
  come willingly.
- **The Bulwark** (`heavytrooper01–04`): white power armour, siege rifles, back-mounted aether packs. The Concord's
  soldiers; each pack is a portable Still.
- **Shield-walkers** (`mechaarmor00–02`): crewed walkers behind a tower shield bearing the Concord sun.
- **The Kiln Guard** (`ceramicknight00–06`): porcelain knights in white cloaks — fired, not born. The Hierarch's honour
  guard; they crack instead of bleeding.
- **Vat-giants** (`cyclops00–06`): pale, swollen labourers grown in the Stills from tithe-surplus, one red eye each.
  They tend the Anchors because nothing else survives the cold.
- **Prototypes and gremlins** (`purpleprototype00–04`, `techgremlin00–02`): failed weapons from the vats, and the
  little scrap-rats that maintain them.
- **The Chronomancer** (`chronomancer`): an Anchor engineer. Her clockwork rings are Anchor tech in miniature — she
  doesn't push Morrow back, she pushes *time* back, and makes everyone near her late.
- **The Hierarch** (`hierarch`, the final boss): the Concord's head, pale, beautiful, two hundred years old on
  other people's lives. Called "Hierarch" by the faithful and "the Leech" by everyone else. Its own enemy class since
  GG-T350, so the boss and the playable `vampire` hero are different people; it has no portrait yet and shows the
  mannequin placeholder until its art is made (owner 2026-11-07, GG-T351).
- **Chromed Hollow** (`cyberzombie00–04`): Hollow the Concord fitted with chrome harness and sent back to work.
- The `mannequin` is a Choir training dummy (Test stages only).

### 3.2 The Street — neon, layered, loud
The kids of Halcyon's underspire and the moor towns who grew up dodging the tithe. Hoodies, cargo pants, platform
sneakers, tape, chrome. The Street resists the tithe with **splicing**: black-market grafts cut from Ember-touched
flesh. A splice holds your aether in so the Stills can't sing it out — at the price of becoming something else.
- **Redline** (`ganger00–06`): red and black hoods, masked faces. Sells tithe-exemption tokens that sometimes work.
- **The Murder** (`harpy01–03`): spliced with black wings. Rooftop couriers and smugglers; they fly contraband (and
  people) past Concord checkpoints.
- **The Herd** (`minotaur01–04`): spliced with horns and bull-mass. Muscle for hire, loyal to the crew that pays and
  feeds them.
- Street heroes (the party's core): `courier`, `engineer`, `technician`, `speedster`, `basher`, `slasher`, `fencer`,
  `streetfighter`, `striker`, `fighter`, `prizefighter`, `pugilist`, `thief`, `chromaninja`, `yellowninja`,
  `machinist`, `tinkerer`, `ripper`, `reaper`, `raider`, `lancer`, `duelist`, `assassain`, `operative`, `nighthunter`.

### 3.3 The Free Holds — mail, tabards, old oaths
The moor-holds that still keep their own militias and remember the time before the tithe. Steel, shields, faded
heraldry.
- Militia (`soldier00–03`): green hoods over old mail; most now serve whoever pays the Concord's tithe on their behalf.
- The **Captain** (`captain`): the moor militia's commander, who sold the holds to the Concord for an exemption.
- Hold heroes: `knight`, `paladin`, `defender`, `shieldmaiden`, `tank`, `jadeknight`, `myrmidon`, `warchief`,
  `barbarian`, `bruiser`.

### 3.4 The Lantern Road — robes, straw hats, masks
Wandering sword-schools and hermit orders who walk between holds by lantern-light and owe nothing to anyone.
- Heroes: `ronin`, `odachi`, `swordmaster`, `sellsword`, `monk`, `martialartist`, `drifter`, `dervish`,
  `blackninja`, `blueninja`, `greenninja`, `redninja`, `whiteninja`, `pandagirl`, `sage`.
- **Oni** (`oni00–02`): masked road-raiders who wear the old demon masks to frighten pilgrims; some have worn them so
  long the masks grew on.

### 3.5 The Fen-dark — veils, rags, things that remember the Arks
What lives in the Arkgrave and the dark under the Anchors: the people the tithe missed and the dead it didn't finish.
- **Hags** (`hag00–02`) and the **Hexes** (`hexsniper`, `hexstalker`, `hexwarden`): Choir who fled into the fens and
  learned to sing aether the other way — into curses.
- **The Lich** (`lich`): the last captain of the Ark *Long Patience*, who refused to let his ship die and kept himself
  instead. Horned, robed, flame-lit.
- **The Swamp Mistress** (`swampmistress00`): the drowned Ark's pilot, grown into the fen.
- **Wraith Knights** (`wraithknight01–03`): the Anchor Watch — knights sworn to guard the arches forever, kept going by
  the arches' own leaking aether long after they should have died. Grey plate, cloaks the colour of old fire.
- **The Void Sentinel** (`voidsentinel`): what forms where an Anchor leaks — smoke given a body around a cyan heart of
  stolen aether.
- Hollow and remains (`undead00–04`, `ghost`, `phantom`, `skelepede00–02`, `bonepile`, `corpse`, `gravestone`).
- Fen-dark heroes: `blackwitch`, `whitewitch`, `ritualist`, `hexwarden`, `hexsniper`, `hexstalker`, `harbinger`,
  `redmage`, `alchemist`, `doctor`, `sister`, `cleric`, `vampire` (a hero who is *not* the Hierarch — a Concord
  noble's child who ran, and has the same hunger and refuses it).

### 3.6 The Ember — black armour, red capes, blood-light
Where Morrow's shards bleed into the sand, something wakes that wants the moon to finish falling. The Ember is
Morrow's hunger with a body.
- **Infernalists** (`infernalist01–04`): black-armoured zealots in long red capes. They believe the Nearing is a
  homecoming and want the Anchors broken.
- **Hellspawn** (`hellspawn01–04`): what the Ember makes of a beast or a person who drinks from the red rivers.
- The **Demon Lord** (`demonlord`) and the `darktemplar` (a hero — a Red Seal templar who drank Ember to survive a
  Hollowing and kept the red sword).

### 3.7 The Wild — changed by the tides
Animals and growths reshaped by Morrow's pull: `wolf00–03`, `werewolf00`, `frog00–03`, `toad00`, `slime00–03`,
`treegolem00–06` (moss-golems of the moors), `yeti`, `icemauler`, `mountaintroll`, `lurker00–02`, `naga00`, `nagaegg`,
`sandmaw`, `scorpion`, `bat00–02`, `vulture`, `flyingmonkey`, `marshshambler00–03`, `steppinrazor00–05`
(blade-legged fen-stalkers), `bluelion`, `goblinthug00`. Heroes from the Wild: `wildchild`, `monstertamer`, `bluelion`.

## 4. The crew and the camp

The party is a **crew**: a handful of Street kids and drifters from **Saltmoss**, a moor town, who ran on tithe day
with a stolen Concord supply caravan. The caravan is home; Camp is wherever it stops. Others join on the road from every
faction above — that mix *is* the point: the Concord only wins while everyone else stays apart.

Camp NPCs (roles unchanged, voices new):
- **Innkeeper** — Saltmoss's tavern-keeper, who hid the crew on tithe day and came along because there was nothing left
  to keep.
- **Blacksmith** — a Free Hold armourer who refuses to forge for the Captain.
- **Alchemist** — a Street splice-cutter; knows exactly what grafts cost.
- **Enchantress** — a Choir runaway who still hears the hymn and won't sing it.
- **Merchant** — a Lantern Road trader; sells to everyone, informs on no one.

New heroes from the owner's art:
- **Herald** (`herald`, healer): a flower-winged messenger of the **Bloom** — moss and red blossoms that grow wherever
  aether is *given* rather than taken. Heals by giving her own aether back, which is exactly what the Concord says is
  impossible.
- **Aetherist** (`aetherist`, healer): a Choir singer who learned to reverse the hymn — pouring aether in instead of
  drawing it out. White robes, cyan currents. The Choir wants her back very badly.
- **Tactician** (`tactician`): a former Concord staff officer in a black greatcoat who planned tithe routes until she
  read the ledgers and saw where the surplus went. She plans the crew's war now.

## 5. The campaign

Thirteen themes, in order: eight in the main campaign, five in the postgame. Each theme's crawl opens it; its party talk
carries one piece of the plot.

Owner, 2026-11-07 (GG-T344): *"I will generate more landscapes from which you can extrapolate more story, settings
levels, etc"* — new chapters are read out of the owner's landscapes the same way the factions are read out of the units.

1. **The Tithe Moors** (`greenvalley`). Tithe day at Saltmoss. The crew runs with a Concord caravan. Moor roads full of
   wolves, Redline, militia and the first Hollow. *Boss (III): the **Captain**, who sold the holds and has come to
   drag the runaways back.* What the crew learns: the tithe-takers took far more than the quota.
2. **The Lantern Road** (`lanternroad`, GG-T345, from four landscapes the owner made). *Structural entry only; the
   chapter's story is authored in Prose (GG-T355) and its in-game text is placeholder draft.* Where: the lantern-lit
   pilgrim road that climbs out of the Tithe Moors through mountain gorges (stone stairs, shrines, torii, bamboo, rope
   lines of paper lanterns, a low moon) toward the Shardfall. Who: the Lantern Road's sword-schools walk it (§3.4); the
   **oni** (`oni00–02`) raid it; the gorge's moon-changed Wild (wolves, a werewolf, bats, a flying monkey, steppin
   razors, §3.7) hunt it; a little Redline and militia still trail the caravan. Stages *I–III* climb the stairs to the
   pass. *Boss (III): an **oni** chief (`oni02`, Undying) whose mask gets back up once.*
3. **The Shardfall** (`desert`). South over the moors to where Morrow's pieces came down. Red rivers, moon-tusks, the
   Ember's first Infernalists and Hellspawn. A Concord dig is cutting aether out of the shards. *Boss: a **shield-walker**
   commanding the dig.* Learned: the shards are full of aether — the moon is *made* of what it pulls.
4. **The Arkgrave** (`swamp`). Into the fog-fens where the Arks fell. Hags, Hexes, shamblers, the Lich's crew. The
   ship logs say the Arks didn't fail on their own: the Concord grounded them so no one could leave the tithe.
   *Boss: the **Swamp Mistress**.*
5. **The Anchor Wastes** (`cave`). North across the ice to the arches. Wraith Knights, Void Sentinels, vat-giants. The
   crew sees the beams running from the Anchors to Halcyon — and how little of the light the Anchors keep. *Boss: a
   **vat-giant** keeping the great Anchor.* Learned: the surplus goes to the Hierarch.
6. **The Still** (`still`, GG-T344, from six landscapes the owner made). The crew follows the beams south to where they
   meet: the **great Still** at the foot of Halcyon's spire, a white dome bigger than any hold. Every Anchor beam's tithe
   is collected here, into the tanks, before it climbs to the Hierarch. The crew breaks in to see the tithe with their
   own eyes. Inside: floors polished to mirrors, a moon-oculus that frames Morrow overhead, catwalks and rails, arched
   gates, and rows of tall tanks glowing cyan. And the **queues** — Hollow from every hold, still in their white
   tithe-shrouds, standing in line at the tanks to give what they no longer have, because it is the last thing they
   remember. The Bulwark holds the gates, Red Seal wardens herd the queues, Chromed Hollow carry the cans, the Choir
   sings the tanks full, and the vat-giants grown here from tithe-surplus do the heavy lifting.
   - *I — The Queues.* Through the gate hall, past the lines. The crew sees faces they know from the moor roads.
   - *II — The Tank Hall.* Close enough to the tanks to look inside. The aether isn't empty light: there are shapes
     in it.
   - *III — Under the Oculus.* The meter room under the moon-window, where the Still's clocks time the flow up the
     spire.
   *Boss: the **Chronomancer**, the Anchor engineer who keeps the Still's clocks.* She times everything here, so she
   breaks the turn order (TwinTurn: two turns unless a hero stands beside her). She is one of the engineers who
   half-knows, and she says it aloud because she thinks the crew won't live to repeat it.
   **What the crew learns: what the tanks hold.** Not light, and not fuel. Look close and the aether has *shapes* in it:
   a hold's chimneys, a well, a garden wall, a whole street, small and drowned and turning. The tithe carries what people
   *were*. The Hollow are not empty — they are **stored**, here, in tanks, a hold to a tank, until the Hierarch drinks
   them. Which means it can be given back. (This is why, at the ending, the Hollow begin to remember.)
7. **Halcyon** (`cityruins`). Out of the Still's back gate and up into the Spire City. The Bulwark, the Choir, the Kiln
   Guard, the Chronomancer. Up the spire. *Boss (III): a **Kiln Guard captain** (`ceramicknight06`, AxisWard) holding
   the spire stair to the cathedral* (GG-T350; the Hierarch moved up to the cathedral).
8. **The Choir's Cathedral** (`cathedral`, GG-T350, from four landscapes the owner made; the main campaign's finale).
   *Structural entry only; the chapter's story is authored in Prose (GG-T355) and its in-game text is placeholder
   draft.* Where: the cathedral at the top of Halcyon's spire — rose windows, pews by the hundred, a moon in the vault,
   and an organ of aether pipes glowing cyan. The organ **is the Hierarch's reservoir**: the Choir sings the tithe up
   into the pipes, and the Hierarch drinks from them. Who: the Choir (`cultist01–03`) and the Concord — the Bulwark,
   the Kiln Guard, the Red Seal (§3.1). Stages *I–III* climb from the nave to the organ. *Boss (finale, III): the
   **Hierarch** (`hierarch`, Undying while its Choir still sings).* The crew breaks the Hierarch's reservoir; two
   centuries of stolen aether pour back down the beams into the holds.

**Ending** — *"Still Here."* The tithe stops. The Hollow begin, very slowly, to remember. Morrow still hangs exactly as
low as yesterday; nobody has solved the Nearing. But where the stolen aether came down, the Bloom is growing, and the
crew has a caravan, a road, and proof that giving works. Roll credits.

**Postgame** (the road after the Hierarch — open on purpose, no final answer):
9. **The Moors Beyond the Stills** (`greenvalley2`) — holds that never got their aether back, and Redline selling the
   Bloom as a drug. *Region: **the Free Holds** (GG-T346, from seven landscapes the owner made; structural entry only,
   story authored in Prose, GG-T355).* Where: walled hill-forts and wooden palisades over gold moorland under a pale
   Morrow — the holds that still keep their own walls (§3.3). Who: hold militia (`soldier00–03`) at the gates, the
   Captain's people still collecting for whoever pays, Redline, moor wolves, and a hold whose Hollow (`undead00–02`) never
   got their aether back. Stages *The Free Holds I–II*. *Boss (II): the **Captain** with his militia.*
10. **Deep Shardfall** (`desert2`) — the Ember grows bolder with the Concord broken; the Infernalists march on the Anchors.
11. **The Drowned Arks** (`swamp2`) — the *Long Patience* still flies, a little; the Lich offers passage to anyone.
12. **Under the Anchors** (`cave2`) — what the arches are actually built on.
13. **The Underspire** (`cityruins2`) — Halcyon without a Hierarch: the Choir, the Red Seal and the Street all want the
    spire. *Region: **the Street markets** (GG-T349, from nine landscapes the owner made; structural entry only, story
    authored in Prose, GG-T355).* Where: the underspire's night markets — the white spire behind tangled cables, pink
    neon signs, stalls and splice clinics in the rain (§3.2). Who: Redline, the Murder and the Herd on their home turf,
    Chromed Hollow still carrying cans for nobody, and the Choir come down to bargain. Stages *The Underspire I–II*.
    *Boss (II): the **Herd Boss** (`minotaur01`) beside a Choir Cantor and a Redline ganger — three claimants to the
    spire.*

**Prologue** — *"Tithe Day."* Saltmoss, the white dome on the hill, the crew's names in the ledger, the run.

**Level 10 interlude** — *"Why We Ran."* The Innkeeper asks the crew what they're actually fighting for; the player
names the motive (flags `motive:coin|people|truth|crew` keep their meaning: coin, the holds, the truth about the
tithe, each other).

## 6. Tone and voice

- Grounded and warm, not grimdark. People crack jokes on bad roads. The world is ending slowly enough that everyone
  still has to make breakfast.
- The Concord is not cartoon-evil: most of it sincerely believes the tithe holds the moon up. The villainy is the
  Hierarch's skim and everyone who looks away from it.
- The Street talks fast and short. The Free Holds talk in oaths. The Lantern Road talks in proverbs and then undercuts
  them. The Choir talks in hymn fragments. The Ember talks about "coming home."
- Never explain Morrow fully. Nobody in the world knows what it is.

## 7. Glossary of story words

**Morrow** (the moon) · **the Nearing** · **aether** · **the tithe** · **Stills** (white tithe-domes; **the Still**
is the great one at the foot of Halcyon's spire) · **the queues** (Hollow lined up at the tanks) · **Hollow**
(the over-tithed) · **the Anchors** · **the Arks** · **the Shardfall** · **the Ember** · **splicing** · **the Bloom** ·
**Saltmoss** (the crew's home town) · **Halcyon** (the Spire City) · **the Hierarch**.
