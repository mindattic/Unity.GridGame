# Heroes — all 64 `Team.Hero` classes

Grouped by `Role` (`docs/BIBLE.md` §25.13), then by the sub-archetype cluster each class's
`DefaultLoadout` actually walks (identified by its `<cluster>_<move>` ability-id prefix, the same method
§25.6.1's GG-T80 audit used). A cluster entry gives the shared voice and the shared thing that role/cluster
needs advice about; each class then gets its own line naming how *that* class specifically gets it wrong,
built off its own unique signature move (slot 3 of its bar — the one ability nobody else shares).

Every "Needs advice about" line is written to be gradeable: in a GG-T133 Talk quiz, the choice that
names this hero's specific blind spot (not just "this role's" in general) is the one that should earn
the skill point.

---

## Tank — 9 classes, home tree `guardian`

**Job:** draw the enemies, protect the casters, never let every bar slot go non-damage at once
(`docs/BIBLE.md` §25.13 — the lone-tank soft-lock GG-T14 found and fixed). **Voice register:** short
declarative sentences, body language over feeling — a Tank says what it's doing, not what it's thinking.
**What the role needs advice about, in general:** that standing in front is a job, not a sacrifice — a
Tank who talks like a martyr has already lost the plot the role is built on.

### Aegis Guard — pure mitigation + cleanse (3)
Wards, judgments, sacred oaths: this cluster fights like it's defending something specific, not just
occupying a tile. Its blind spot is treating the ward as the whole job and forgetting a Guardian still
needs one damage option in the bar.

- **Defender** (`defender`, signature *Wall*) — Bio: the plainest name in the roster and proud of it; a
  wall doesn't need a better name than Wall. Voice: flat, repetitive, almost soothing — says the same
  thing every fight because the same thing keeps working. Needs advice about: that "I didn't move" is
  not the same as "I won."
- **Knight** (`knight`, signature *Oath*) — Bio: took a vow to something bigger than the caravan, long
  before the Longmother ever needed defending; keeps defending out of habit now, even unsure who the
  vow was to. Voice: formal, a little stiff, lands jokes a beat too late. Needs advice about: that an
  oath made to no one in particular can still be kept, and keeping it *is* the point.
- **Paladin** (`paladin`, signature *Bulwark*) — Bio: body-blocks and patches itself in the same breath;
  the starting trio's anchor (`docs/BIBLE.md` §23.4). Voice: warm, a little bossy, checks on everyone
  else's HP before its own. Needs advice about: that out-damaging the Barbarian was never the job — the
  bar's `item:x_potion` slot is there because self-sufficiency is the whole point (§23.2.0).

### Juggernaut — knockback aggression (2)
Less wall, more battering ram: this cluster doesn't just hold ground, it takes it, shoving the fight
where it wants the pincer to land. Its blind spot is confusing momentum with control.

- **Myrmidon** (`myrmidon`, signature *Phalanx*) — Bio: fights in formation even alone, like the rest of
  the line is still there; War Stomp and Overrun both want company it doesn't always have. Voice: terse
  commands, counts cadence under its breath. Needs advice about: learning to hold a formation of one
  without it reading as loneliness dressed up as discipline.
- **Tank** (`tank`, signature *Juggernaut* — the generic class whose own signature move shares the
  cluster's name) — Bio: the archetype with the training wheels left on, exactly as advertised; new
  players start here because there's nothing to misread. Voice: plain to the point of funny, says
  "Tank" when asked what it does, as if that settles it. Needs advice about: that being the obvious
  choice doesn't mean having nothing left to learn about itself.

### Warlord — party buffs (2)
Less about the Warlord's own hit points than everyone else's morale; this cluster leads by being loud at
the right moment. Its blind spot is believing the buff matters more than the person receiving it.

- **Shield Maiden** (`shieldmaiden`, signature *Valkyrie*) — Bio: marks a target "for the warband" before
  the warband has agreed to one; leads from a half-step ahead, not from the back. Voice: rallying,
  second-person-plural ("we," "us," "ours") even when talking to one hero. Needs advice about: that
  marking the fight's priority isn't the same as everyone actually following it there.
- **War Chief** (`warchief`, signature *Banner*) — Bio: plants a banner and dares the fight to come to
  it; older than most of the roster and never lets anyone forget it. Voice: gravel, long pauses, stories
  that circle before they land. Needs advice about: that the banner only means something while someone's
  still standing under it — leading is a renewable promise, not a one-time plant.

### Deathguard — drain/poison (2, Guardian's 4th branch since GG-T82)
Fights by taking something from the enemy, not just absorbing what's thrown; the last Tank cluster the
skill tree caught up to (it was an orphan until GG-T82 gave it the Guardian tree's 4th branch,
`docs/BIBLE.md` §25.6.1). It still half-expects not to be understood.

- **Dark Templar** (`darktemplar`, signature *Rite*) — Bio: a Knight's vow gone a shade darker — still
  defends the caravan, just by means the Knight wouldn't use. Voice: quiet, matter-of-fact about things
  that should sound worse than they do. Needs advice about: that the caravan judging the method less
  than the result doesn't mean the method costs nothing.
- **Jade Knight** (`jadeknight`, signature *Resolve*) — Bio: the Dark Templar's steadier cousin, drains
  slower and admits it's uncomfortable about it. Voice: apologetic, explains itself more than it needs
  to. Needs advice about: that discomfort with a tactic is not the same as doing it wrong.

---

## Healer — 8 classes, home tree `cleric`

**Job:** mend and cleanse, playing off WIS (`docs/BIBLE.md` §25.13). **Voice register:** economical with
sympathy — a Healer who talks too much about feelings and not enough about HP totals isn't doing the
job. **What the role needs advice about, in general:** that sustain is offense on a longer clock, not a
consolation prize for staying out of the fight.

### Aegis Wardens — preemptive wards (3)
Casts the ward before the hit lands, not after; this cluster's whole identity is refusing to be
reactive. Its blind spot is wearing out the party's patience for being told what's about to happen to
them.

- **Alchemist** (`alchemist`, signature *Transmute*) — Bio: the caravan's actual Alchemist-the-NPC's
  battlefield counterpart in spirit, if not in person — never runs out, because it turns what it has
  into what it needs. Voice: precise, a little clinical, narrates its own math out loud. Needs advice
  about: that out-damaging the mages was never the ask (§23.2.0) — "I never run out" is the whole brag.
- **Cleric** (`cleric`, signature *Grace*) — Bio: the starting trio's healer, keeps the party alive "when
  it shouldn't be" (§23.2.0) and has made peace with how often that phrase applies. Voice: calm under
  pressure to the point of eerie, never raises its voice even mid-Clutch. Needs advice about: that
  getting stuck on the front line chasing a kill is the one way to actually lose the sustain war.
- **Doctor** (`doctor`, signature *Triage*) — Bio: ranks the wounded by need, not by rank or
  friendship, and has had to defend that choice before. Voice: brisk, interrupts to ask who's bleeding
  worse. Needs advice about: that triage done right still looks cold to the person triaged last.

### Hexmenders — damage-for-heal (2)
Trades pain for healing, literally — a mirror-curse logic that turns an enemy's own hit into the
party's sustain. Its blind spot is enjoying the trade a little more than a Healer probably should.

- **Sage** (`sage`, signature *Insight*) — Bio: reads a fight like a text, sees the mirror-curse coming
  three turns out. Voice: measured, fond of "as I expected." Needs advice about: that being right about
  the fight isn't the same as having warned anyone in time to use it.
- **White Witch** (`whitewitch`, signature *Ward*) — Bio: the gentlest name doing the sharpest trick —
  her Sanctuary Rite ("Sanctuary Rite" wards the whole party, §25.6.1's own worked example) is as kind as
  Hexmenders get. Voice: soft-spoken, undercuts her own menace with politeness. Needs advice about: that
  a ward meant for everyone still has to be cast before the hit, not admired after.

### Skirmish-Menders — mobile support (3)
Heals on the move, never plants its feet; the cluster least likely to still be standing where it started
the turn. Its blind spot is mistaking motion for safety.

- **Monk** (`monk`, signature *Peace*) — Bio: the "flexible fighter with no single specialty" template
  made literal — heals by not needing to be anywhere in particular. Voice: koan-adjacent, answers
  questions with slightly different questions. Needs advice about: that peace it hands the party has to
  be somewhere they can actually stand on, not just a feeling.
- **Sister** (`sister`, signature *Benediction*) — Bio: blesses before a fight starts, out of habit more
  than ritual at this point. Voice: warm, quotes something half-remembered, never says where from.
  Needs advice about: that a blessing said once doesn't cover the whole expedition — it's a line item,
  not a shield.
- **White Ninja** (`whiteninja`, signature *Moonlit* — a Skirmish-Menders kit riding a ninja's reflexes)
  — Bio: proof the cluster doesn't need a robe to work; heals the way the Shadowstep ninjas fight, fast
  and from an angle. Voice: clipped, economical, the quietest Healer in the roster. Needs advice about:
  that speed covers for a lot, but not for skipping the hero who's actually dying.

---

## Mage — 6 classes, home tree `mage`

**Job:** ranged elemental AoE, playing off INT (`docs/BIBLE.md` §25.13). **Voice register:** precise
vocabulary, enjoys being the one who knows the word for the thing. **What the role needs advice about,
in general:** that the whole campaign's magic is fraying near the worst of the blight
(`docs/BIBLE.md` §27.1) — a Mage's confidence in its own craft is no longer something it can take for
granted the way it used to.

### Spellblade — melee-infused casting (2)
Casts with a blade in hand, not behind the line; this cluster never fully committed to staying ranged.
Its blind spot is not admitting that's a hedge, not a hybrid.

- **Harbinger** (`harbinger`, signature *Omen*) — Bio: reads bad news in the weather before the fight
  starts, and is rarely wrong, which hasn't made it popular. Voice: ominous by default, has to be asked
  twice to say something reassuring. Needs advice about: that naming the omen out loud doesn't discharge
  it — the party still has to act on the warning, not just hear it.
- **Red Mage** (`redmage`, signature *Fury*) — Bio: the jack-of-both-trades, blade and fire in the same
  turn, never quite resting in either. Voice: quick, slightly breathless, talks like it's always mid-cast.
  Needs advice about: that trying to be the best at two things at once is how it ends up merely good at
  both.

### Field Caster — traps/wards/support (2)
Casts ahead of where the fight is going to be, not where it is; sets the table before the enemy sits
down. Its blind spot is over-preparing for a fight that moves faster than the setup.

- **Drifter** (`drifter`, signature *Frost Nova*) — Bio: never stays with one caravan long, drifted into
  this one and hasn't drifted back out — yet. Voice: noncommittal, hedges every promise with "for now."
  Needs advice about: that staying this long already *is* the commitment it keeps saying it hasn't made.
- **Operative** (`operative`, signature *Shock*) — Bio: treats the battlefield like a job with a brief,
  reports in afterward whether anyone asked or not. Voice: clipped, debriefing cadence even in casual
  talk. Needs advice about: that the caravan isn't a client — it doesn't need a report, it needs a
  teammate.

### Hexweaver — curse/DoT damage (2)
Wins the long game, not the opener; a hit that gets worse the longer it's left alone. Its blind spot is
patience read as indifference by a party that wants the fight over now.

- **Black Witch** (`blackwitch`, signature *Shadow Bolt*) — Bio: ink-and-flame discipline (the archetype
  blurb classes.json still carries, §13) turned into something genuinely unsettling to fight beside, not
  just against. Voice: dry, deadpan, lets silences do the threatening. Needs advice about: that a party
  it's scaring a little is a party that won't tell it when something's wrong.
- **Ritualist** (`ritualist`, signature *Blood Rite*) — Bio: the Hexweaver who still thinks of it as
  ritual, not combat math — every DoT is a working, not just a debuff. Voice: liturgical, numbers given
  weight they don't technically need. Needs advice about: that calling it a rite doesn't make the cost
  to the target any less literal.

---

## Striker — 31 classes, home tree `warrior`

**Job:** pincer damage, melee skills, playing off STR (`docs/BIBLE.md` §25.13) — by far the largest
role, and the skill tree knows it: 8 real clusters share 3 branches, leaving 14 classes (45%) without a
branch that matches their own kit (`docs/BIBLE.md` §25.6.1, GG-T80/81). **Voice register:** this is the
one role where voice genuinely splits by cluster — there is no single "Striker sound." **What the role
needs advice about, in general:** that being unmatched to a tree branch isn't the same as being
unimportant to the party; half the roster living in this gap is a reason to write them well, not less.

### Berserker — rage/bloodlust (4)
Hits first, hits hardest, thinks about it never. This cluster's blind spot is exactly what §23.2.0 warns
against for its own Barbarian: being slow to set up reads as a failure of the whole identity.

- **Barbarian** (`barbarian`, signature *Reckless*) — Bio: the starting trio's striker, no single
  specialty and proud of the lack (§23.1's own "walks many roads" blurb, worn as a badge here). Voice:
  blunt, enthusiastic, counts to three before anyone else has decided to fight. Needs advice about: that
  "I delete the front rank" (§23.2.0) is a promise that has to be kept turn one, not eventually.
- **Basher** (`basher`, signature *Skull Crack*) — Bio: Barbarian's less complicated sibling — if there's
  a simpler way to hit something, Basher has already found it. Voice: monosyllabic by choice, not by
  limitation. Needs advice about: that simple and thoughtless aren't the same word, and it's earned the
  distinction more than it gets credit for.
- **Bruiser** (`bruiser`, signature *Haymaker*) — Bio: swings for the finish every time, win or lose;
  has lost more fights than it's won this way and hasn't changed a thing. Voice: cheerful about its own
  losses in a way that worries the rest of the party. Needs advice about: that a haymaker that misses
  still costs the same turn as one that lands — recklessness has a clock, too.
- **Panda Girl** (`pandagirl`, signature *Slam*) — Bio: the feral-instinct archetype (§13's "born to the
  deep places" blurb) wearing the roster's most disarming name; the contrast is the whole character.
  Voice: cheerful right up until the Slam connects. Needs advice about: that being underestimated is a
  weapon exactly once per stranger — the party already knows, and needs something else from her now.

### Duelist — precision/counter (7)
Wins the exchange, not the brawl; reads an opening and takes it clean. This cluster's blind spot is
assuming every fight is a duel, even the ones that are six-on-one.

- **Assassin** (`assassain`, signature *Killing Blow*) — Bio: a flanker built on quiet training and
  patience that outruns thought (§13); every action should hit and harvest (§23.2.0). Voice: economical
  to the point of unsettling — three words where another class would use ten. Needs advice about: that
  "too tanky" is the one way to lose the identity (§23.2.0) — gearing for survival is gearing away from
  the point.
- **Duelist** (`duelist`, signature *Riposte*) — Bio: the cluster's namesake, scars as lessons, lessons
  as openings (§13); the one who actually enjoys the one-on-one the whole cluster is built around.
  Voice: formal, almost courtly, treats an opponent with genuine respect mid-fight. Needs advice about:
  that respecting an opponent is not the same as waiting for them to be ready.
- **Fencer** (`fencer`, signature *Fleche*) — Bio: all forward motion, no retreat in the kit or the
  temperament. Voice: fast-talking, finishes other people's sentences. Needs advice about: that a lunge
  that can't reverse is a commitment the rest of the party has to plan around, not be surprised by.
- **Ronin** (`ronin`, signature *Masterless*) — Bio: answers to the caravan because it chose to, not
  because anyone assigned it there; "masterless" is a fact, not a complaint. Voice: unhurried, treats
  every instruction as a suggestion worth considering. Needs advice about: that choosing to stay still
  means choosing to actually take direction sometimes, or the choice stops meaning anything.
- **Sellsword** (`sellsword`, signature *Contract*) — Bio: rode in on the Wandering Merchant's own
  recruitment line (`dialogues.json camp_merchant_chat`'s 300 G guard) and still thinks in terms of the
  deal that got it here. Voice: transactional, narrates loyalty like an invoice line. Needs advice
  about: that the contract ran out a long time ago and it's still here anyway — that's not a line item,
  that's a choice it hasn't said out loud.
- **Sword Master** (`swordmaster`, signature *Perfect Cut*) — Bio: chases the one flawless exchange over
  and over, win or lose, because the cut mattered more than the outcome did. Voice: reverent about
  technique, almost monastic. Needs advice about: that a perfect cut on a fight already lost is still a
  loss — the party needs the win more than it needs the form.
- **Thief** (`thief`, signature *Snatch*) — Bio: the Assassin's looser cousin, same patience, loosened
  grip — takes the opening and whatever's in the enemy's pocket in the same motion. Voice: light,
  quick-fingered in conversation as in combat, changes the subject fast. Needs advice about: that
  harvesting from every fight is not the same as having anything to show the party for it.

### Shadowstep — mobility/rogue flavor (6)
Teleports into position a move sooner than anyone else can manage; the cluster that most needs the
GreenNinja/RedNinja anti-pattern warning (§23.2.0: a "consequence-free teleport" is the failure state).

- **Black Ninja** (`blackninja`, signature *Shadowfang*) — Bio: the baseline of the color-coded six, the
  one the others are all variations on. Voice: terse, near-silent, the least talkative hero in the
  roster by design. Needs advice about: that being the template doesn't mean having nothing distinct to
  say — it just means saying less of it.
- **Blue Ninja** (`blueninja`, signature *Frostfang*) — Bio: the cold one, literally and temperamentally;
  teleports to the coldest angle on the board out of instinct. Voice: clipped, a little superior about
  staying calm. Needs advice about: that calm read as distance is still distance — the party can't tell
  if it's composed or just checked out.
- **Chroma Ninja** (`chromaninja`, signature *Prismfang*) — Bio: refuses to pick a single color or a
  single specialty within the cluster — Prismfang hits however the moment calls for it. Voice:
  mercurial, answers the same question differently each time it's asked. Needs advice about: that
  flexibility without a throughline reads as unreliability, not range.
- **Green Ninja** (`greenninja`, signature *Venomfang*) — Bio: the mobility-thief archetype named
  outright in §23.2.0 as the cluster's own cautionary tale. Voice: playful, treats every teleport like a
  trick it's showing off. Needs advice about: the exact line §23.2.0 writes for it — "a sooner position"
  has to mean something tactically, not just arrive somewhere flashy.
- **Red Ninja** (`redninja`, signature *Flamefang*) — Bio: teleport plus Mug, the same combo as Assassin
  wearing a different color (§23.2.0 calls this out directly: "same as Assassain"). Voice: competitive,
  keeps score against the Assassin without saying so. Needs advice about: finding what it does that
  Assassin doesn't, instead of racing to the enemy backline first and calling that a personality.
- **Yellow Ninja** (`yellowninja`, signature *Shockfang*) — Bio: the quickest trigger in the cluster,
  teleports before it's sure it needs to. Voice: jumpy, fills silence with chatter. Needs advice about:
  that not every opening needs taking the instant it appears — some are worth waiting one more turn on.

### Skirmisher — mobility + covering fire (5, Striker's own — distinct from Ranger's tree branch of the same name)
Keeps distance and tempo at once, running riot rather than standing and trading. Its blind spot is
treating repositioning as a substitute for actually closing a fight out.

- **Lancer** (`lancer`, signature *Chargeline*) — Bio: commits hardest of the cluster, a straight line
  instead of a dodge — the one Skirmisher who sometimes forgets it has an exit. Voice: direct, states
  intent before acting, almost courteous about it. Needs advice about: that telegraphing the charge is
  fine, but only if there's still a plan for after it lands.
- **Raider** (`raider`, signature *Plunder*) — Bio: hits and takes, moves on; the loot-first instinct the
  whole caravan economy quietly depends on and nobody quite thanks it for. Voice: quick, transactional,
  already thinking about the next stage mid-fight. Needs advice about: that plunder taken from a fight
  the party barely survived isn't a win just because the bag got heavier.
- **Speedster** (`speedster`, signature *Blitz*) — Bio: the fastest hero in any given lineup, and visibly
  bored whenever the pace drops. Voice: rapid-fire, finishes thoughts before anyone's ready. Needs advice
  about: that speed isn't a plan by itself — it's a resource, and it runs out exactly like mana does.
- **Street Fighter** (`streetfighter`, signature *Dirty Trick*) — Bio: never learned a formal style and
  doesn't miss it; wins by doing the thing a Duelist would call unfair. Voice: casual, mocking, genuinely
  warm underneath the mockery. Needs advice about: that a trick that works once stops being a trick the
  second time an enemy remembers it.
- **Striker** (`striker`, signature *Finishing Blow*) — Bio: the role's own namesake class, built to
  close out what the rest of the pincer started. Voice: businesslike, allergic to a fight that overruns
  its expected length. Needs advice about: that not every fight is owed a finishing blow from this
  particular hero — sometimes the Finish belongs to whoever actually has the opening.

### Combo Brawler — chain-hit specialists (4)
Builds damage across a sequence, not a single swing; a whole cluster that's more dangerous the longer a
fight runs, which cuts against the pincer game's usual rhythm.

- **Fighter** (`fighter`, signature *Combo*) — Bio: the plainest Combo Brawler, builds a chain because
  that's simply how it was taught to fight. Voice: steady, counts its own hits under its breath. Needs
  advice about: that a chain broken early isn't a failure to restart from — it's a signal to change the
  plan for that fight.
- **Martial Artist** (`martialartist`, signature *Palm Strike*) — Bio: treats every fight as a lesson
  first and a battle second, same discipline whether the enemy is a goblin or a boss. Voice:
  instructional, narrates technique mid-combat whether anyone asked for the lecture or not. Needs advice
  about: that the lesson can wait until after the enemy's dead — narrating form mid-fight is a liability,
  not a teaching moment.
- **Prizefighter** (`prizefighter`, signature *Knockout*) — Bio: fights like there's a purse on the line
  even when there very much isn't; showboats for a crowd that's just the rest of the party. Voice: brash,
  performs confidence it mostly actually has. Needs advice about: that the party isn't an audience —
  playing to them instead of coordinating with them costs the pincer timing.
- **Pugilist** (`pugilist`, signature *Jab-Cross*) — Bio: the most economical Combo Brawler, no flourish,
  just the next hit in the sequence. Voice: quiet, almost bored, saves the intensity for the fight
  itself. Needs advice about: that economy of motion doesn't require economy of warmth — the party reads
  its silence as disinterest, not focus.

### Opportunist — punishes weakness (2)
Waits for an opening someone else's hit created, then collects on it; never throws the first strike if
it can help it. Its blind spot is a reputation for only showing up once the hard part's over.

- **Night Hunter** (`nighthunter`, signature *Nightstalk*) — Bio: tracks a target across a whole fight
  before committing, patient past what most of the roster could stand. Voice: low, deliberate, speaks
  mostly in observations about the enemy. Needs advice about: that the party can't always afford to wait
  as long as it does — sometimes the opening it's tracking has to be made, not found.
- **Ripper** (`ripper`, signature *Rend*) — Bio: the blunter Opportunist, less tracking, more simply
  there the moment a target is already hurt. Voice: short, a little gleeful about timing. Needs advice
  about: that collecting the debt (its own cluster's language) only works if the party trusts it's
  collecting, not just circling.

### Brawler — grapple-leaning strikers (2)
Fights close and ugly, breaks a guard rather than going around it. Its blind spot is confusing a
willingness to take damage with actually being built to tank it.

- **Odachi** (`odachi`, signature *Iaijutsu*) — Bio: one draw, one cut, everything riding on the single
  motion; the whole kit is built around a moment the rest of the cluster doesn't share. Voice: silent
  until it isn't, then exactly one sentence. Needs advice about: that the one perfect draw is still only
  one action — the fight doesn't end just because the Odachi's turn did.
- **Slasher** (`slasher`, signature *Flurry*) — Bio: Odachi's opposite number, many small cuts instead of
  one, wears a guard down instead of breaking it outright. Voice: rapid, slightly manic, narrates its own
  flurry as it lands. Needs advice about: that volume of hits isn't the same as the guard actually being
  broken — check the result, not just the count.

### Attrition — endurance/grim resilience (1, the roster's only overlap with `Team.Enemy`)
The one Striker cluster of exactly one class, and the one place the bible's own spawn-side rule
(`docs/BIBLE.md` §23) lives in the hero list: the same class fights as an enemy boss depending which side
spawned it.

- **Vampire** (`vampire`, signature *Enervate*) — Bio: life ended, purpose remained (§23's own lore line
  for the class) — as a hero, that purpose points at the caravan instead of away from it; as an enemy, it
  is the Vampire Lord of Veshker Ruins (`locations.md`). The two are the same class, never explained to
  the party which one they've met until it's already fighting beside or against them. Voice: unhurried,
  speaks like time has stopped mattering to it, because it mostly has. Needs advice about: the Vampire
  Lord's own unresolved question — whether enduring past when something should have killed you is
  survival or just a longer way of refusing to stop.

---

## Support — 8 classes, home tree `alchemist`

**Job:** tempo — Quicken allies, Slow foes, rally the party (`docs/BIBLE.md` §25.13). **Voice register:**
talks in terms of timing and resource math, the least emotionally direct role in the roster by design.
**What the role needs advice about, in general:** that tempo won by the Support is invisible to everyone
who isn't watching the clock — it needs naming out loud, or the party credits the wrong hero for the win.

### Primal Pack — reagent/consumable mastery (2)
Feral rather than mechanical, despite sharing a tree with Field Engineers; leans on instinct and the
den, not the workshop.

- **Blue Lion** (`bluelion`, signature *Roar*) — Bio: challenges on behalf of the party before anyone
  asks it to, the way a den-leader would. Voice: proud, protective, slightly performative about the
  protecting. Needs advice about: that a roar meant to draw attention away from the party still has to
  be backed up by actually surviving the attention it drew.
- **Wild Child** (`wildchild`, signature *Howl*) — Bio: raised by instinct more than instruction, the
  Primal Pack's rawest edge; Denmother's own care for the pack runs through it even when it doesn't say
  so. Voice: sparse, direct, trusts actions over explanations. Needs advice about: that the pack it
  protects includes humans who need the plan said out loud, not just felt.

### Tempo Weavers — haste/slow control (2)
Bends the clock itself — Quicken an ally, Slow an enemy, swap who acts next. The literal tempo-control
cluster the role's whole job description is named for.

- **Courier** (`courier`, signature *Relay*) — Bio: built around getting something — a message, a buff,
  an opening — from one place to where it's needed, fast. Voice: breathless, always mid-sentence about
  what's coming next. Needs advice about: that relaying information isn't the same as making sure
  someone actually acted on it.
- **Dervish** (`dervish`, signature *Whirl*) — Bio: one of the two guest allies the caravan can actually
  lose for good mid-battle before recruiting (`docs/BIBLE.md` §25.11, Sandsea Reaches II) — spins through
  a fight like it's already decided to join before anyone's asked. Voice: dizzying, talks in circles that
  somehow land somewhere useful. Needs advice about: that the caravan has to actually catch it, mid-spin,
  before it can trust the spin is going anywhere good.

### Field Engineers — traps/amplifier synergy (4)
Builds the thing that does the work instead of doing it personally; the cluster most likely to be
talking about a device instead of itself.

- **Engineer** (`engineer`, signature *Overclock*) — Bio: pushes every device past its rated limit on
  principle, including its own patience for being told not to. Voice: fast, technical, drops jargon
  without translating it. Needs advice about: that overclocking a mine is a battlefield trick; overclocking
  itself, every expedition, is just burnout with better branding.
- **Machinist** (`machinist`, signature *Turret*) — Bio: the guest-ally counterpart met in Mireholt Fens
  II (`docs/BIBLE.md` §25.11) and later recruitable outright; builds something that outlasts its own turn
  on the board. Voice: methodical, proud of its turret the way some heroes are proud of a weapon. Needs
  advice about: that a turret left running after the fight it was built for is just scrap waiting to
  jam — know when to stop building and start fighting directly.
- **Technician** (`technician`, signature *Diagnostic*) — Bio: runs the numbers before committing to
  anything, including whether a fight is worth joining the way the party wants to join it. Voice:
  careful, qualifies every statement with a probability. Needs advice about: that a diagnostic run mid-
  fight, out loud, costs the party time it may not have — some things have to be guessed at speed.
- **Tinkerer** (`tinkerer`, signature *Jury-Rig*) — Bio: the least polished Field Engineer and the
  proudest of it; a jury-rigged fix held together by spite and tape, same as half the caravan's actual
  gear. Voice: scattered, trails off mid-explanation to go fix something else. Needs advice about: that
  "it works" isn't the same as "it'll keep working" — the caravan has learned that the hard way with
  actual equipment (§24.5's shatter mechanic) and the Tinkerer should know it better than anyone.

---

## Ranged — 3 classes, home tree `ranger`

**Job:** precision single-target/line damage that never needs to be adjacent (`docs/BIBLE.md` §25.13) —
the newest role (added GG-T11), split since GG-T18 into one class per Ranger branch: Marksman
(`hexsniper`), Trapper (`hexwarden`) and Skirmisher (`hexstalker`) (`docs/BIBLE.md` §25.13).
**Voice register:** distance made literal in how they talk — all three keep people at arm's length, one
coldly, one territorially, one by simply never being where you left her. **What the role needs advice
about, in general:** that three classes, all reused from the same hag-family portraits (`hag00`/`hag01`/
`hag02`, `docs/BIBLE.md` §25.13), is a small enough roster that each one has to work twice as hard to not
read as the same person three times.

- **Hex Sniper** (`hexsniper`, signature *Piercing Shot*) — Bio: a swamp hag who traded curses for coin,
  picks off the toughest target in a fight from well outside its reach (its own classes.json lore line,
  kept verbatim because it's already good). Voice: cold, transactional, treats a kill like a line item.
  Needs advice about: that being paid for the shot doesn't mean the shot is owed no explanation to the
  rest of the party — mercenary and silent are two different postures, and it's defaulted to both.
- **Hex Warden** (`hexwarden`, signature *Snare Shot*) — Bio: the fen's own warden rather than a hired
  gun, snares anything that strays too near with a barbed little curse (also kept from its own lore line)
  — guards a place, not a contract. Voice: proprietary, a little territorial, warmer about the fen than
  about the people in it. Needs advice about: that the fens it's warding are the same ground the
  caravan's crossing out of necessity, not malice — the warden's suspicion needs a reason to stand down.
- **Hex Stalker** (`hexstalker`, signature *Fading Shot*) — Bio: a swamp hag who never stays where she
  fired from, drifting through the reeds a step ahead of whatever is chasing her (its own classes.json
  lore line). Voice: light, evasive, answers a question with a question and is gone before the reply.
  Needs advice about: that keeping her distance from every fight is a tactic, but keeping it from every
  person in the caravan is just running — someone has to know where she'll be.
