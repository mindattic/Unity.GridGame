# GridGame Gaiden: story (a pointer, not a source)

**The story lives in Prose, not in this repo** (GG-T355, owner 2026-11-07: *"all dialog/conversations, descriptions,
screen crawls, everything is sourced from prose ... the Game handles gameplay, and Prose handles narrative"*).

- **Canon** (the premise, factions, places, characters and their relations) is the Prose universe **`gridgame`**
  ("GridGame") in the Prose narrative engine (`D:\Projects\MindAttic\Prose`). The premise is the owner's *A War in
  Heaven* (v4, DECIDED). *Under Morrow* (v1 to v3), which this file used to hold, is RETIRED and kept in Prose as history.
- **Every word a player reads as story** (dialogue lines and choices, crawls, class/item/armor-set/skill-tree/NPC
  descriptions, quest briefs and turn-ins, glossary definitions, barks) is a Prose text. The game reads it from
  `Assets/Gaiden/Resources/Content/Text/*.json`, which the build pulls from Prose and nobody edits by hand.
- **To change the story,** draft in Prose (`prose --texts-draft gridgame <dir>`; agents only draft, RFC 0009) and the
  owner approves and publishes it there. The next build applies it.

How it flows, the key scheme and the commands: [PROSE.md](PROSE.md). The rules: [BIBLE §27.2](BIBLE.md). The old text of
this file is in git history (before GG-T355 Phase 2) and in Prose.
