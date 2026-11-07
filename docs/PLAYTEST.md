# Play-test #1 — checklist (≈15 minutes)

Build: `pwsh tools/build.ps1`, then run `Build/Windows/GridGameGaiden.exe` (resize the window as you like —
the game letterboxes to portrait and never stretches). Or open the project in Unity 6000.4.3f1 and press
Play on `Assets/Gaiden/Scenes/Main.unity` (it's empty on purpose; everything is built from code).

Mouse = finger. `Esc` = back / pause.

## 1. First five minutes
- [ ] Title → **New Game** → Slot 1 → you land at the **Camp** menu (party, commands, gold/time).
- [ ] **Adventure** → **Tithe Moors I** → the story crawl plays (tap or Skip).

## 2. The battle (the part that matters most)
- [ ] Press and drag a hero. It follows your cursor; every tile you cross is one step. Anyone in the way
      **swaps** into the tile you just left, live, while you're still holding.
- [ ] Line up an enemy between two heroes in a row or column: a gold line + red tiles preview the
      **PINCER** before you let go. Release → both heroes strike everything in the line.
- [ ] The number on each enemy is how many of your moves until it acts (Terra Battle style). The
      timeline at the top shows the same thing; icons slide right only when you act.
- [ ] Hit an enemy whose icon is inside the orange zone → it gets **Pushback**.
- [ ] Pincers fill the orb line (bottom). Tap a hero, tap **Heal/Fireball** when lit, tap a target.
- [ ] **GUARD** (top right) skips to the next enemy turn and shields everyone.
- [ ] Pause (II) → **Auto battle** lets the bot play so you can watch.
- [ ] Win → results (gold, XP, loot, unlocks) → **Next stage** or **Continue** to camp.

## 3. Camp
- [ ] **Items**: tabs, rarity colors, item detail; use a potion on a wounded hero.
- [ ] **Merchant**: buy/sell carts with − / + steppers, Pay/Receive total, Buy/Sell commits.
- [ ] **Blacksmith**: Forge (needs materials from battles — farm the Tithe Moors), Upgrade, Salvage, Repair.
- [ ] **Equip**: pick a slot, tap an item once to preview stat changes, tap again to equip.
- [ ] **Abilities**: pick a bar slot, assign Known spells or Items; **Learn** new spells for gold.
- [ ] **Party / Summon Circle / Bounties / Bestiary / Inn / Settings** all open and do what they say.
- [ ] Settings → **Drag timer** on: you get 4 seconds per drag; quick drags bank blue orbs.

## Tell me
1. Does the drag feel good? (speed, the lift, the swap animation, too slow/fast?)
2. Is anything confusing in the first battle without reading this sheet?
3. Difficulty: too easy / too hard by Desert (stage 4–6)?
4. Anything that looks wrong or ugly — screenshot it.
