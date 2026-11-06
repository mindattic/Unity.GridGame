"""
The weapon roster (GG-T181): 87 base weapons on top of the 13 hand-authored originals, for 100 unique bases
across every weapon class (Simple/Martial/Arcane, GG-T52) and hero role, each with its own stat profile or
trait, a source (Merchant shop, Forge recipe or enemy drop), a named +3 passive and an icon palette.

    python tools/weapons.py            # (re)write the roster into items/recipes/droptables + icons.json
    python tools/make_icons.py --missing

The +1/+2/+3 upgrade line and the six enchant variants are generated exactly like the originals':
  +N      Strength and Intelligence +2/+4/+7, BaseCost +15/+30/+50, same rarity; "+3" carries the named passive.
  enchant enchants.json's Bonus added to the base stats, BaseCost +120, rarity one step up.
  recipes upgrade_<id>[_plusN] (Forge, T2/T3/T3) and enchant_<id>_<enchant> (Enchant, T3) with the originals' inputs.

Idempotent: every row this script owns (by id) is removed and re-appended, so re-tuning a weapon here and
re-running rewrites it; nothing else in the content files is touched.
"""
import copy
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTENT = os.path.join(ROOT, "Assets", "Gaiden", "Resources", "Content")
MANIFEST = os.path.join(ROOT, "tools", "icons.json")

STAT = {"STR": "Strength", "VIT": "Vitality", "AGI": "Agility", "SPD": "Speed", "STA": "Stamina",
        "INT": "Intelligence", "WIS": "Wisdom", "LCK": "Luck"}
STAT_ORDER = ["Strength", "Vitality", "Agility", "Speed", "Stamina", "Intelligence", "Wisdom", "Luck"]
RARITIES = ["Common", "Uncommon", "Rare", "Epic", "Legendary"]
ROLES = {"Tank", "Healer", "Mage", "Striker", "Support", "Ranged"}
# Durability by weapon type (light blades and foci wear fastest, heavy hafted weapons slowest).
DURABILITY = {"Club": 160, "Sickle": 100, "Sling": 90, "Quarterstaff": 130, "Knife": 90, "Javelin": 110, "Rod": 100,
              "Pitchfork": 140, "Dart": 80, "Lantern": 90, "Bolas": 90, "Chakram": 110, "Knuckles": 120,
              "Mace": 200, "Hammer": 220, "Spear": 160, "Rapier": 120, "Hatchet": 140, "Crossbow": 120,
              "Flail": 180, "Halberd": 220, "Whip": 100, "Glaive": 170, "Trident": 170, "Katana": 150, "Bow": 110,
              "Scythe": 160, "Morningstar": 190, "Axe": 220, "Wand": 80, "Tome": 90, "Orb": 80, "Totem": 110,
              "Scepter": 110, "Staff": 120, "Skullstaff": 120}
BUFF_TEXT = {"burning": "[Burn] the target", "frozen": "[Freeze] the target", "wet": "[Soak] the target",
             "sleep": "put the target to [Sleep]", "poisoned": "[Poison] the target", "slowed": "[Slow] the target",
             "silenced": "[Silence] the target", "blinded": "[Blind] the target"}
DROP_CHANCE = {"Common": 0.06, "Uncommon": 0.04, "Rare": 0.03, "Epic": 0.015, "Legendary": 0.008}
FORGE_TIER = {"Common": 1, "Uncommon": 1, "Rare": 2, "Epic": 3, "Legendary": 3}

# (id suffix, name, WeaponType, class, rarity, role, stats, traits, +3 passive, sources, palette, description)
#   traits (";"-separated):  pincer=0.05 (pincer damage), regen=2, protect, orbs=1 (start orbs), bank=1 (orb slots),
#            resist=Fire:0.8, onhit=poisoned:0.1:Venom Tip, steal=0.03:Name (lifesteal from the base up)
#   passive: "Name|buff|burning|0.2", "Name|steal|0.04", "Name|guard|0.08"  (+3 only; an on-hit/steal base trait's
#            passive is the same mechanic, stronger)
#   sources: shop | forge:mat_a*2,mat_b*1 | drop:<table>[@chance]
W = [
    # ── Simple: anyone can wield these (Healers and Supports above all) ──
    ("club_oak", "Oak Club", "Club", "Simple", "Common", "Tank", "STR2 VIT2", "", "Knotted Guard|guard|0.06", "shop", "wood", "A knotted oak cudgel; crude but dependable."),
    ("sickle_farm", "Farmer's Sickle", "Sickle", "Simple", "Common", "Striker", "STR2 AGI2 LCK1", "", "Reaping Arc|steal|0.03", "shop", "iron", "A field sickle honed for harder harvests."),
    ("sling_leather", "Leather Sling", "Sling", "Simple", "Common", "Ranged", "AGI3 LCK2", "", "Grit in the Eye|buff|blinded|0.15", "shop", "leather", "A strip of leather and a pouch of river stones."),
    ("staff_quarter", "Quarterstaff", "Quarterstaff", "Simple", "Common", "Support", "STR1 VIT1 AGI1 SPD2", "", "Sweeping Ward|guard|0.07", "shop", "wood", "A plain ash pole, quick in the hand."),
    ("knife_bone", "Bone Knife", "Knife", "Simple", "Common", "Striker", "AGI3 LCK2 STR1", "", "Marrow Drink|steal|0.04", "forge:mat_undead_bone*3,mat_cracked_fang*1", "bone", "Carved from a long bone, wickedly sharp."),
    ("javelin_hunting", "Hunting Javelin", "Javelin", "Simple", "Common", "Ranged", "STR2 AGI3", "", "Pinning Throw|buff|slowed|0.2", "shop", "wood", "A light throwing spear for game and goblins alike."),
    ("rod_willow", "Willow Rod", "Rod", "Simple", "Common", "Healer", "WIS3 STA1", "regen=1", "Willow Shelter|guard|0.06", "forge:mat_wood_plank*2,mat_marsh_moss*2", "green", "A supple willow wand that hums with slow green life."),
    ("pitchfork_rustic", "Rustic Pitchfork", "Pitchfork", "Simple", "Common", "Tank", "STR2 VIT1 STA2", "", "Tangling Tines|buff|slowed|0.15", "shop", "rust", "Three iron tines on a stout handle; keeps trouble at arm's length."),
    ("dart_frog", "Frog-Venom Darts", "Dart", "Simple", "Common", "Ranged", "AGI2 LCK2", "onhit=poisoned:0.1:Venom Tip", "Toxic Volley|buff|poisoned|0.25", "forge:mat_frog_gland*2,mat_wood_plank*1", "poison", "Reed darts dipped in marsh-frog venom."),
    ("lantern_tallow", "Tallow Lantern", "Lantern", "Simple", "Common", "Support", "WIS2 LCK2", "bank=1", "Glare|buff|blinded|0.15", "shop", "sun", "A swinging tin lantern; mana gathers in its glow."),
    ("knuckles_copper", "Copper Knuckles", "Knuckles", "Simple", "Common", "Striker", "STR3 SPD2", "", "Rabbit Punch|steal|0.03", "shop", "bronze", "Four copper rings welded into a fist."),
    ("club_ironbound", "Ironbound Cudgel", "Club", "Simple", "Uncommon", "Tank", "STR4 VIT3 STA2", "", "Iron Hoop|guard|0.08", "forge:mat_wood_plank*2,mat_iron_ore*3,mat_goblin_scrap_iron*1", "iron", "An oak cudgel banded with scrap iron."),
    ("staff_pilgrim", "Pilgrim's Staff", "Quarterstaff", "Simple", "Uncommon", "Healer", "WIS4 VIT2 STA2", "regen=2", "Wayfarer's Blessing|guard|0.08", "forge:mat_wood_plank*3,mat_templar_seal*1", "holy", "A walking staff worn smooth by a hundred shrines."),
    ("knife_bandit", "Bandit's Shiv", "Knife", "Simple", "Uncommon", "Striker", "AGI4 LCK4", "onhit=blinded:0.1:Dirty Trick", "Cutpurse's Feint|buff|blinded|0.2", "drop:ganger", "crimson", "A short blade that fights dirty."),
    ("sling_wolfbone", "Wolfbone Sling", "Sling", "Simple", "Uncommon", "Ranged", "AGI5 LCK2 SPD2", "", "Howling Stone|buff|slowed|0.2", "forge:mat_wolf_pelt*2,mat_cracked_fang*2", "bone", "A wolf-hide sling that hurls fang-tipped shot."),
    ("sickle_marsh", "Marsh Sickle", "Sickle", "Simple", "Uncommon", "Striker", "STR3 AGI3", "onhit=poisoned:0.1:Bog Rot", "Mire Reaper|buff|poisoned|0.22", "forge:mat_iron_ore*2,mat_marsh_moss*2,mat_swamp_ichor*1", "poison", "Its curve never quite dries of swamp ichor."),
    ("bolas_courier", "Courier's Bolas", "Bolas", "Simple", "Uncommon", "Support", "SPD4 AGI3 LCK1", "", "Hobbling Throw|buff|slowed|0.25", "forge:mat_leather*2,mat_vulture_feather*2", "leather", "Weighted cords that wrap a runner's legs."),
    ("rod_hearthstone", "Hearthstone Rod", "Rod", "Simple", "Uncommon", "Healer", "WIS5 INT2 VIT1", "resist=Ice:0.8", "Hearthglow|guard|0.08", "forge:mat_wood_plank*2,mat_yeti_fur*2", "sun", "A warm stone set in a rod; the cold can't reach its bearer."),
    ("knife_sandmaw", "Sandmaw Fang", "Knife", "Simple", "Rare", "Striker", "STR4 AGI5 LCK3", "", "Burrowing Bite|steal|0.05", "forge:mat_sand_chitin*3,mat_iron_ore*2", "earth", "A sandmaw's tooth set in a chitin grip."),
    ("mace_templar", "Templar's Mace", "Mace", "Simple", "Rare", "Tank", "STR5 VIT4 STA3", "protect", "Oathkeeper|guard|0.1", "forge:mat_iron_ore*4,mat_templar_seal*2", "silver", "A flanged mace stamped with the templar seal."),
    ("hammer_tinker", "Tinker's Mallet", "Hammer", "Simple", "Rare", "Support", "STR3 SPD4 LCK3 INT2", "bank=1", "Overclock|buff|slowed|0.2", "forge:mat_iron_ore*3,mat_gremlin_cog*3", "bronze", "A gremlin-built mallet, all springs and cogs."),
    ("lantern_mistveil", "Mistveil Lantern", "Lantern", "Simple", "Rare", "Support", "WIS5 INT3 LCK3", "regen=3", "Will-o'-Wisp|buff|sleep|0.12", "drop:swampmistress", "teal", "Its pale flame drifts like marsh mist."),
    ("chakram_ceramic", "Ceramic Chakram", "Chakram", "Simple", "Rare", "Ranged", "AGI6 SPD3 LCK3", "", "Shattering Ring|buff|slowed|0.2", "forge:mat_ceramic_shard*3,mat_leather*1", "pink", "A glazed throwing ring that always comes back."),
    ("knife_sanguine", "Sanguine Stiletto", "Knife", "Simple", "Epic", "Striker", "STR4 AGI7 LCK5", "steal=0.03:Thirst", "Crimson Thirst|steal|0.07", "drop:vampire", "crimson", "A needle-thin blade that drinks what it draws."),
    ("club_glacier", "Glacier Club", "Club", "Simple", "Epic", "Tank", "STR7 VIT6 STA4", "resist=Ice:0.7", "Permafrost|buff|frozen|0.15", "forge:mat_yeti_fur*3,mat_frost_core*2,mat_iron_ore*2", "ice", "A club of ice that never melts."),
    ("lantern_seraph", "Seraph's Censer", "Lantern", "Simple", "Epic", "Healer", "WIS8 VIT4 STA3", "regen=4", "Choir of Wings|guard|0.12", "forge:mat_templar_seal*2,mat_enchanted_feather*2,mat_essence_holy*1", "holy", "A swinging censer whose smoke mends wounds."),
    ("staff_worldroot", "Worldroot Staff", "Quarterstaff", "Simple", "Legendary", "Healer", "WIS9 VIT6 STA5 INT2", "regen=5", "Deep Roots|guard|0.14", "forge:mat_heartwood*4,mat_dragon_scale*1,mat_essence_holy*1", "jade", "Cut from the oldest tree in the world; it still grows."),
    ("chakram_eclipse", "Eclipse Chakram", "Chakram", "Simple", "Legendary", "Ranged", "AGI9 SPD6 LCK6", "pincer=0.1", "Totality|buff|blinded|0.25", "drop:demonlord@0.04", "shadow", "A black ring rimmed in sunfire."),

    # ── Martial: fighters' weapons (Strikers, Tanks, Ranged) ──
    ("spear_militia", "Militia Spear", "Spear", "Martial", "Common", "Tank", "STR2 VIT2 STA1", "", "Shield Wall|guard|0.07", "shop", "iron", "The town watch's spear: long, plain, reliable."),
    ("rapier_training", "Training Rapier", "Rapier", "Martial", "Common", "Striker", "AGI3 SPD2", "", "Riposte|steal|0.03", "shop", "steel", "A blunted practice blade, still quick enough."),
    ("hatchet_hand", "Hand Axe", "Hatchet", "Martial", "Common", "Striker", "STR4 LCK1", "", "Hewing Blow|buff|slowed|0.15", "shop", "wood", "A woodcutter's hatchet turned to war."),
    ("crossbow_light", "Light Crossbow", "Crossbow", "Martial", "Common", "Ranged", "AGI3 LCK1 STR1", "", "Bolt Through|buff|slowed|0.15", "shop", "wood", "A hand-cranked crossbow; anyone can aim it."),
    ("flail_scrap", "Scrap-Iron Flail", "Flail", "Martial", "Common", "Striker", "STR3 LCK2", "", "Wild Swing|steal|0.03", "forge:mat_goblin_scrap_iron*3,mat_iron_ore*1", "rust", "Goblin scrap on a chain: unpredictable at both ends."),
    ("halberd_soldier", "Soldier's Halberd", "Halberd", "Martial", "Uncommon", "Tank", "STR5 VIT3 STA2", "", "Hold the Line|guard|0.09", "forge:mat_iron_ore*4,mat_military_insignia*2", "steel", "Standard issue for the front rank."),
    ("whip_razorwire", "Razorwire Whip", "Whip", "Martial", "Uncommon", "Striker", "AGI5 STR2 SPD1", "onhit=slowed:0.1:Snare", "Barbed Coil|buff|slowed|0.25", "forge:mat_razor_wire*3,mat_leather*1", "gray", "A coil of razor wire on a leather grip."),
    ("knuckles_fang", "Fang Knuckles", "Knuckles", "Martial", "Uncommon", "Striker", "STR4 SPD4", "", "Feral Flurry|steal|0.04", "forge:mat_werewolf_fang*1,mat_iron_ore*2", "bone", "Werewolf fangs set over the knuckles."),
    ("rapier_duelist", "Duelist's Rapier", "Rapier", "Martial", "Uncommon", "Striker", "AGI5 SPD3 LCK1", "", "Disarming Thrust|buff|blinded|0.15", "forge:mat_broken_blade*3,mat_bandit_trophy*2", "silver", "Reforged from blades its owner bested."),
    ("glaive_batwing", "Batwing Glaive", "Glaive", "Martial", "Uncommon", "Striker", "STR4 AGI4", "", "Night Swoop|buff|blinded|0.15", "forge:mat_iron_ore*3,mat_bat_wing*3", "shadow", "A crescent blade shaped like a spread wing."),
    ("trident_naga", "Naga Trident", "Trident", "Martial", "Uncommon", "Tank", "STR4 VIT2 AGI2", "onhit=wet:0.12:Tidecall", "Riptide|buff|wet|0.3", "drop:naga00", "water", "A three-pronged fork from the naga reefs."),
    ("bow_vulture", "Vulture Longbow", "Bow", "Martial", "Uncommon", "Ranged", "AGI6 LCK3", "", "Carrion Shot|steal|0.04", "forge:mat_wood_plank*3,mat_vulture_feather*3", "earth", "Fletched with vulture feathers; patient, like its namesake."),
    ("katana_iron", "Iron Katana", "Katana", "Martial", "Uncommon", "Striker", "STR5 AGI3", "", "Iaijutsu|steal|0.04", "forge:mat_iron_ore*5,mat_leather*1", "iron", "A single-edged blade folded from plain iron."),
    ("morningstar_spiked", "Spiked Morningstar", "Morningstar", "Martial", "Uncommon", "Striker", "STR6 LCK2", "", "Bone Breaker|buff|slowed|0.2", "forge:mat_goblin_scrap_iron*2,mat_broken_blade*2,mat_iron_ore*2", "iron", "A spiked iron head on a short haft."),
    ("hammer_cyclops", "Cyclops Maul", "Hammer", "Martial", "Rare", "Tank", "STR8 VIT4", "", "Earthshaker|guard|0.11", "drop:cyclops", "earth", "A cyclops's maul, cut down to human size."),
    ("flail_troll", "Troll-Hide Flail", "Flail", "Martial", "Rare", "Tank", "STR6 VIT4 STA2", "regen=2", "Troll's Blood|steal|0.05", "forge:mat_troll_hide*2,mat_iron_ore*3", "leather", "Bound in troll hide that knits itself back together."),
    ("scythe_chitin", "Chitin Scythe", "Scythe", "Martial", "Rare", "Striker", "STR7 LCK5", "", "Stinging Harvest|buff|poisoned|0.2", "forge:mat_sand_chitin*3,mat_scorpion_stinger*2", "earth", "A desert-insect scythe with a stinger at the heel."),
    ("halberd_mecha", "Mecha Pile Halberd", "Halberd", "Martial", "Rare", "Tank", "STR6 VIT5", "protect", "Piston Guard|guard|0.12", "forge:mat_mecha_plating*3,mat_iron_ore*3", "steel", "A halberd with a piston-driven spike."),
    ("glaive_werewolf", "Moonfang Glaive", "Glaive", "Martial", "Rare", "Striker", "STR6 AGI4 SPD2", "steal=0.02:Bloodmoon", "Lunar Frenzy|steal|0.06", "forge:mat_werewolf_fang*2,mat_wolf_pelt*2,mat_iron_ore*2", "silver", "A silvered glaive that hungers under the moon."),
    ("crossbow_hex", "Hexbolt Crossbow", "Crossbow", "Martial", "Rare", "Ranged", "AGI7 LCK3 INT2", "", "Hexed Bolt|buff|silenced|0.15", "forge:mat_hag_claw*2,mat_wood_plank*2,mat_arcane_dust*1", "arcane", "Bolts scratched with a hag's curses."),
    ("katana_oni", "Oni Katana", "Katana", "Martial", "Rare", "Striker", "STR7 AGI3 SPD2", "onhit=burning:0.1:Oni Fire", "Demon's Edge|buff|burning|0.22", "drop:oni", "fire", "An oni's blade, still warm from the forge of its making."),
    ("trident_storm", "Storm Trident", "Trident", "Martial", "Rare", "Striker", "STR5 AGI4 INT3", "resist=Lightning:0.8", "Thunderclap|buff|slowed|0.2", "forge:mat_scrap_circuit*2,mat_essence_spark*1,mat_iron_ore*2", "spark", "Its tines arc with captured lightning."),
    ("scythe_reaper", "Reaper's Scythe", "Scythe", "Martial", "Epic", "Striker", "STR9 LCK6 AGI2", "pincer=0.08", "Final Harvest|steal|0.07", "drop:reaper", "shadow", "Death's own tool, misplaced."),
    ("halberd_dragonscale", "Dragonscale Halberd", "Halberd", "Martial", "Epic", "Tank", "STR8 VIT6 STA3", "resist=Fire:0.7", "Dragon Wall|guard|0.14", "forge:mat_dragon_scale*1,mat_iron_ore*5,mat_troll_hide*2", "crimson", "Its haft is wrapped in dragon scale."),
    ("glaive_frostbite", "Frostbite Glaive", "Glaive", "Martial", "Epic", "Striker", "STR8 AGI6 INT2", "onhit=frozen:0.08:Rime", "Deep Freeze|buff|frozen|0.18", "forge:mat_frost_core*2,mat_essence_frost*1,mat_iron_ore*4", "ice", "A glaive of blue steel that frosts the air it cuts."),
    ("crossbow_arc", "Arc Repeater", "Crossbow", "Martial", "Epic", "Ranged", "AGI9 SPD5 LCK3", "", "Chain Bolt|buff|slowed|0.25", "forge:mat_cyber_core*2,mat_scrap_circuit*2,mat_iron_ore*3", "spark", "A clockwork crossbow that reloads itself."),
    ("whip_vampire", "Vampire Lash", "Whip", "Martial", "Epic", "Striker", "AGI8 STR5 LCK3", "", "Bloodletting|steal|0.08", "drop:vampire", "crimson", "A blood-red lash; every welt feeds its wielder."),
    ("rapier_phantom", "Phantom Rapier", "Rapier", "Martial", "Epic", "Striker", "AGI9 SPD5 LCK4", "onhit=blinded:0.1:Ghost Step", "Vanishing Point|buff|blinded|0.22", "forge:mat_phantom_wisp*3,mat_essence_shadow*1,mat_iron_ore*3", "shadow", "Half there, half not; its point is always where you aren't looking."),
    ("axe_hellforged", "Hellforged Greataxe", "Axe", "Martial", "Legendary", "Striker", "STR13 VIT4 LCK3", "pincer=0.1", "Infernal Cleave|buff|burning|0.25", "drop:demonlord@0.05", "fire", "Forged in the demon lord's own furnace."),
    ("spear_skypiercer", "Sky Piercer", "Spear", "Martial", "Legendary", "Ranged", "STR10 AGI8 SPD4", "", "Falling Star|steal|0.06", "forge:mat_dragon_scale*1,mat_enchanted_feather*3,mat_iron_ore*5", "gold", "A spear thrown so high it is said to return with starlight."),
    ("katana_moonlit", "Moonlit Katana", "Katana", "Martial", "Legendary", "Striker", "STR11 AGI7 LCK4", "steal=0.03:Moon Drinker", "Pale Crescent|steal|0.08", "forge:mat_reaper_shroud*2,mat_phantom_wisp*2,mat_dragon_scale*1", "silver", "Its blade shows the moon even at noon."),

    # ── Arcane: casters' foci (Mages, and anyone who learns arcane focus) ──
    ("wand_apprentice", "Apprentice's Wand", "Wand", "Arcane", "Common", "Mage", "INT3 WIS1", "", "First Lesson|buff|silenced|0.12", "shop", "wood", "Every mage's first wand; slightly singed."),
    ("tome_primer", "Primer Tome", "Tome", "Arcane", "Common", "Mage", "INT2 WIS3", "", "Footnote|guard|0.06", "shop", "leather", "A beginner's grimoire, heavily annotated."),
    ("orb_glass", "Glass Orb", "Orb", "Arcane", "Common", "Mage", "INT3 LCK2", "", "Refraction|buff|blinded|0.12", "shop", "ice", "A clear glass sphere that bends light and luck."),
    ("totem_batwing", "Batwing Totem", "Totem", "Arcane", "Common", "Support", "INT2 SPD2 LCK1", "", "Echolocation|buff|blinded|0.15", "forge:mat_bat_wing*3,mat_wood_plank*1", "shadow", "A carved post hung with leathery wings."),
    ("orb_slime", "Slime Orb", "Orb", "Arcane", "Common", "Mage", "INT2 VIT2 WIS1", "resist=Poison:0.85", "Gelatinous|buff|slowed|0.15", "forge:mat_slime_gel*3,mat_arcane_dust*1", "green", "A wobbling orb of set slime gel."),
    ("tome_ritual", "Ritual Tome", "Tome", "Arcane", "Uncommon", "Mage", "INT5 WIS3", "orbs=1", "Binding Rite|buff|silenced|0.15", "forge:mat_cloth*2,mat_arcane_dust*2,mat_hag_claw*1", "crimson", "Bound in hide; its pages already hold a spell half-cast."),
    ("staff_moss", "Mossgrown Staff", "Staff", "Arcane", "Uncommon", "Healer", "INT4 WIS4", "regen=2", "Overgrowth|buff|slowed|0.2", "forge:mat_wood_plank*2,mat_marsh_moss*3", "green", "A staff left in the marsh so long it took root."),
    ("orb_bonedust", "Bonedust Orb", "Orb", "Arcane", "Uncommon", "Mage", "INT6 LCK3", "", "Grave Chill|buff|frozen|0.1", "forge:mat_bone_dust*3,mat_arcane_dust*1", "bone", "Packed bone dust, pressed into a sphere."),
    ("scepter_bone", "Bone Scepter", "Scepter", "Arcane", "Uncommon", "Mage", "INT5 VIT2 WIS2", "", "Ossuary Ward|guard|0.08", "forge:mat_undead_bone*4,mat_arcane_dust*1", "bone", "A scepter of fused vertebrae."),
    ("wand_cog", "Cogwork Wand", "Wand", "Arcane", "Uncommon", "Support", "INT5 SPD3", "bank=1", "Short Circuit|buff|silenced|0.15", "forge:mat_gremlin_cog*3,mat_iron_ore*1", "bronze", "Clicks and whirs when charged."),
    ("wand_feather", "Featherfall Wand", "Wand", "Arcane", "Uncommon", "Mage", "INT4 AGI2 SPD3", "", "Updraft|buff|slowed|0.2", "forge:mat_enchanted_feather*2,mat_wood_plank*1", "teal", "Light as the feather at its tip."),
    ("skullstaff_grave", "Grave Skullstaff", "Skullstaff", "Arcane", "Uncommon", "Mage", "INT6 STA2", "onhit=sleep:0.08:Grave Hush", "Eternal Rest|buff|sleep|0.16", "drop:undead", "bone", "A grinning skull on a blackened pole."),
    ("wand_puppeteer", "Puppeteer's Wand", "Wand", "Arcane", "Uncommon", "Support", "INT4 AGI3 LCK2", "", "Cut Strings|buff|slowed|0.2", "forge:mat_mannequin_joint*3,mat_cloth*1", "pink", "Fine threads trail from its tip."),
    ("orb_circuit", "Circuit Orb", "Orb", "Arcane", "Rare", "Support", "INT7 SPD3 LCK2", "bank=1", "Feedback Loop|buff|silenced|0.15", "forge:mat_scrap_circuit*3,mat_arcane_dust*2", "spark", "A sphere of etched copper traces."),
    ("scepter_naga", "Naga Scepter", "Scepter", "Arcane", "Rare", "Mage", "INT6 WIS4", "onhit=wet:0.12:Undertow", "Drowning Tide|buff|wet|0.3", "forge:mat_naga_scale*2,mat_arcane_dust*2", "water", "A coral scepter crowned with naga scales."),
    ("totem_hag", "Hag's Totem", "Totem", "Arcane", "Rare", "Support", "INT6 WIS3 LCK3", "onhit=poisoned:0.1:Hex Rot", "Witch's Brew|buff|poisoned|0.22", "drop:hag", "poison", "Twigs, claws and something that still twitches."),
    ("tome_frostbound", "Frostbound Tome", "Tome", "Arcane", "Rare", "Mage", "INT8 WIS2 STA2", "resist=Ice:0.75", "Glacial Verse|buff|frozen|0.15", "forge:mat_frost_core*2,mat_cloth*2,mat_arcane_dust*1", "ice", "Its pages are rimed with frost."),
    ("staff_ghostlight", "Ghostlight Staff", "Staff", "Arcane", "Rare", "Mage", "INT7 WIS5", "orbs=1", "Spirit Drain|steal|0.05", "forge:mat_ghost_ectoplasm*1,mat_wood_plank*2,mat_arcane_dust*2", "teal", "A pale flame burns at its crook without fuel."),
    ("orb_cyclops", "Cyclops-Eye Orb", "Orb", "Arcane", "Rare", "Mage", "INT8 LCK4", "", "Evil Eye|buff|blinded|0.18", "forge:mat_cyclops_eye*1,mat_arcane_dust*2", "gold", "It still blinks."),
    ("wand_pyre", "Pyre Wand", "Wand", "Arcane", "Rare", "Mage", "INT8 STR2", "onhit=burning:0.1:Kindle", "Funeral Pyre|buff|burning|0.22", "forge:mat_oni_horn*2,mat_essence_flame*1", "fire", "A charred horn that smoulders in the hand."),
    ("scepter_veilwind", "Veilwind Scepter", "Scepter", "Arcane", "Epic", "Healer", "INT9 WIS6 SPD2", "regen=3", "Mistress's Mercy|guard|0.12", "forge:mat_mistress_veil*2,mat_enchanted_feather*2,mat_arcane_dust*2", "teal", "Veils of the swamp mistress stream from its head."),
    ("tome_ash", "Grimoire of Ash", "Tome", "Arcane", "Epic", "Mage", "INT11 WIS4", "orbs=2", "Ashfall|buff|burning|0.2", "drop:demonlord@0.03", "fire", "Its pages crumble to cinders and re-form."),
    ("orb_neural", "Neural Orb", "Orb", "Arcane", "Epic", "Support", "INT10 SPD4 LCK3", "bank=1", "Mind Spike|buff|silenced|0.2", "forge:mat_cyber_core*2,mat_scrap_circuit*2,mat_arcane_dust*2", "spark", "A glass brain in a glass ball, thinking."),
    ("totem_phantom", "Phantom Totem", "Totem", "Arcane", "Epic", "Support", "INT9 AGI4 LCK4", "onhit=blinded:0.1:Haunt", "Possession|buff|blinded|0.22", "drop:phantom", "shadow", "A totem nobody carved; it was simply there."),
    ("staff_thunderhead", "Thunderhead Staff", "Staff", "Arcane", "Epic", "Mage", "INT10 WIS4", "resist=Lightning:0.7", "Stormcall|buff|slowed|0.25", "forge:mat_essence_spark*2,mat_heartwood*2,mat_arcane_dust*2", "spark", "Clouds gather wherever it is raised."),
    ("tome_starlit", "Starlit Codex", "Tome", "Arcane", "Legendary", "Mage", "INT13 WIS7", "orbs=2; bank=1", "Constellation|buff|silenced|0.25", "forge:mat_dragon_scale*1,mat_mistress_veil*2,mat_essence_holy*2", "holy", "A codex whose pages chart stars that haven't risen yet."),
    ("scepter_abyssal", "Abyssal Scepter", "Scepter", "Arcane", "Legendary", "Mage", "INT14 LCK5 STA3", "pincer=0.1", "Void Hunger|steal|0.08", "drop:reaper@0.008", "shadow", "A scepter carved from the dark between stars."),
    ("orb_dragoneye", "Dragon's Eye Orb", "Orb", "Arcane", "Legendary", "Mage", "INT12 VIT5 WIS4", "resist=Fire:0.7", "Wyrmgaze|buff|burning|0.25", "forge:mat_dragon_scale*2,mat_essence_flame*2,mat_arcane_dust*3", "crimson", "A dragon's eye, polished; it watches back."),
]


def parse_stats(spec):
    s = {k: 0 for k in STAT_ORDER}
    for tok in spec.split():
        m = re.fullmatch(r"([A-Z]{3})(\d+)", tok)
        s[STAT[m.group(1)]] += int(m.group(2))
    return s


def parse_traits(spec):
    t = {}
    for tok in (t.strip() for t in spec.split(";")):
        if not tok:
            continue
        k, _, v = tok.partition("=")
        if k == "pincer": t["PincerDamageBonus"] = float(v)
        elif k == "regen": t["RegenPerTurn"] = int(v)
        elif k == "protect": t["StartsProtected"] = True
        elif k == "orbs": t["BattleStartManaOrbs"] = int(v)
        elif k == "bank": t["BankCapacityBonus"] = int(v)
        elif k == "resist":
            dt, mult = v.split(":"); t.setdefault("Resistances", {})[dt] = float(mult)
        elif k == "onhit":
            buff, chance, name = v.split(":", 2)
            t["PincerApplyBuff"], t["PincerApplyBuffChance"] = buff, float(chance)
            t["PassiveName"] = name
            t["PassiveDescription"] = f"[Pincer] strikes have a {round(float(chance) * 100)}% chance to {BUFF_TEXT[buff]}."
        elif k == "steal":
            frac, name = v.split(":", 1)
            t["LifestealFraction"] = float(frac)
            t["PassiveName"] = name
            t["PassiveDescription"] = f"Heals the wielder {round(float(frac) * 100)}% of damage dealt."
        else:
            raise ValueError(f"unknown trait {tok}")
    return t


def parse_passive(spec):
    parts = spec.split("|")
    name, kind = parts[0], parts[1]
    if kind == "buff":
        buff, chance = parts[2], float(parts[3])
        return {"PassiveName": name, "PassiveDescription": f"[Pincer] strikes have a {round(chance * 100)}% chance to {BUFF_TEXT[buff]}.",
                "PincerApplyBuff": buff, "PincerApplyBuffChance": chance}
    if kind == "steal":
        frac = float(parts[2])
        return {"PassiveName": name, "PassiveDescription": f"Heals the wielder {round(frac * 100)}% of damage dealt.", "LifestealFraction": frac}
    if kind == "guard":
        frac = float(parts[2])
        return {"PassiveName": name, "PassiveDescription": f"[Adjacent] ally heroes take {round(frac * 100)}% less damage.", "AdjacentAllyDamageReduction": frac}
    raise ValueError(spec)


def base_cost(rarity, total, traits):
    per = {"Common": (30, 10), "Uncommon": (100, 12), "Rare": (150, 20), "Epic": (300, 22), "Legendary": (600, 28)}[rarity]
    cost = per[0] + per[1] * total
    if traits: cost *= 1.1
    return int(round(cost / 5.0)) * 5


def item_row(wid, name, desc, rarity, wtype, wclass, cost, dur, stats, vendor):
    return {"Id": wid, "Name": name, "Description": desc, "Type": "Equipment", "Rarity": rarity, "Slot": "Weapon",
            "WeaponType": wtype, "WeaponClass": wclass, "BaseCost": cost, "SellValue": -1, "MaxStack": 1, "Durability": dur,
            "BaseHealing": 0, "BaseDamage": 0, "Stats": stats, "BattleStartManaOrbs": 0, "OnUseAbility": None,
            "Resistances": {}, "SoldByVendor": vendor, "Icon": None}


def apply_traits(row, traits):
    for k, v in traits.items():
        row[k] = copy.deepcopy(v)


def upgrade_inputs(step):
    return [[{"Item": "mat_iron_ore", "Count": 2}, {"Item": "mat_leather", "Count": 1}],
            [{"Item": "mat_iron_ore", "Count": 4}, {"Item": "mat_leather", "Count": 2}, {"Item": "mat_arcane_dust", "Count": 1}],
            [{"Item": "mat_iron_ore", "Count": 6}, {"Item": "mat_leather", "Count": 3}, {"Item": "mat_arcane_dust", "Count": 2}]][step]


def build(enchants, known_items, drop_tables):
    items, forge, upgrades, enchant_recipes, drops, palettes = [], [], [], [], [], {}
    for (key, name, wtype, wclass, rarity, role, stats_s, traits_s, passive_s, sources, palette, desc) in W:
        wid = "eq_" + key
        assert role in ROLES and rarity in RARITIES and wclass in ("Simple", "Martial", "Arcane"), wid
        stats = parse_stats(stats_s)
        traits = parse_traits(traits_s)
        passive = parse_passive(passive_s)
        for mech in ("PincerApplyBuff", "LifestealFraction"):
            if mech in traits:
                assert mech in passive, f"{wid}: an on-hit/steal base trait's +3 passive must be the same mechanic, stronger"
        total = sum(stats.values())
        cost = base_cost(rarity, total, traits)
        dur = int(round(DURABILITY[wtype] * (1 + 0.1 * RARITIES.index(rarity)) / 10.0)) * 10
        srcs = sources.split()
        vendor = "shop" in srcs
        assert not vendor or rarity == "Common", f"{wid}: only Common gear is vendor-sold"
        base = item_row(wid, name, desc, rarity, wtype, wclass, cost, dur, stats, vendor)
        apply_traits(base, traits)
        items.append(base)
        palettes[wid] = palette

        # upgrade line +1..+3 (the originals' rule: STR and INT +2/+4/+7, cost +15/+30/+50)
        prev = wid
        for n, (bump, extra) in enumerate(((2, 15), (4, 30), (7, 50)), start=1):
            st = dict(stats); st["Strength"] += bump; st["Intelligence"] += bump
            row = item_row(f"{wid}_plus{n}", f"{name} +{n}", f"{desc} (upgraded +{n})", rarity, wtype, wclass, cost + extra, dur, st, False)
            apply_traits(row, traits)
            if n == 3: apply_traits(row, passive)
            items.append(row)
            upgrades.append({"Id": f"upgrade_{prev}", "Station": "Forge", "Inputs": upgrade_inputs(n - 1),
                             "Gold": [30, 80, 180][n - 1], "BaseItem": prev, "Result": f"{wid}_plus{n}", "ResultCount": 1,
                             "Tier": [2, 3, 3][n - 1]})
            prev = f"{wid}_plus{n}"

        # enchant variants (the originals' rule: enchant Bonus on top, cost +120, rarity one step up)
        for e in enchants:
            st = {k: stats[k] + e["Bonus"].get(k, 0) for k in STAT_ORDER}
            erar = RARITIES[min(len(RARITIES) - 1, RARITIES.index(rarity) + 1)]
            row = item_row(f"{wid}_ench_{e['Id']}", f"{name} ({e['Name']})", f"{desc}\nImbued with the essence of {e['Name']}.",
                           erar, wtype, wclass, cost + 120, dur, st, False)
            apply_traits(row, traits)
            items.append(row)
            enchant_recipes.append({"Id": f"enchant_{wid}_{e['Id']}", "Station": "Enchant",
                                    "Inputs": [{"Item": e["Essence"], "Count": 1}, {"Item": "mat_arcane_dust", "Count": e["ArcaneDust"]}],
                                    "Gold": e["Gold"], "BaseItem": wid, "Result": f"{wid}_ench_{e['Id']}", "ResultCount": 1, "Tier": 3})

        for src in srcs:
            if src == "shop":
                continue
            kind, _, arg = src.partition(":")
            if kind == "forge":
                inputs = []
                for part in arg.split(","):
                    mat, _, n = part.partition("*")
                    assert mat in known_items, f"{wid}: unknown material {mat}"
                    inputs.append({"Item": mat, "Count": int(n)})
                gold = max(10, int(round(cost * 0.4 / 5.0)) * 5)
                forge.append({"Id": f"recipe_{key}", "Station": "Forge", "Inputs": inputs, "Gold": gold, "BaseItem": None,
                              "Result": wid, "ResultCount": 1, "Tier": FORGE_TIER[rarity]})
            elif kind == "drop":
                table, _, chance = arg.partition("@")
                assert table in drop_tables, f"{wid}: unknown drop table {table}"
                drops.append((table, {"Item": wid, "Chance": float(chance) if chance else DROP_CHANCE[rarity], "Min": 1, "Max": 1}))
            else:
                raise ValueError(src)
    return items, forge + upgrades + enchant_recipes, drops, palettes


def load(name):
    return json.load(open(os.path.join(CONTENT, name), encoding="utf-8"))


def save(path, data):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(data, indent=2, ensure_ascii=True) + "\n")


def main():
    items, recipes, tables = load("items.json"), load("recipes.json"), load("droptables.json")
    enchants = load("enchants.json")
    owned_ids = {"eq_" + w[0] for w in W}

    def owned(iid):
        return iid is not None and re.sub(r"_(plus\d|ench_[a-z]+)$", "", iid) in owned_ids

    items = [i for i in items if not owned(i["Id"])]
    recipes = [r for r in recipes if not owned(r["Result"])]
    for t in tables:
        t["Entries"] = [e for e in t["Entries"] if not owned(e["Item"])]

    new_items, new_recipes, drops, palettes = build(enchants, {i["Id"] for i in items}, {t["Id"] for t in tables})
    clash = {i["Id"] for i in items} & {i["Id"] for i in new_items}
    assert not clash, clash
    by_table = {t["Id"]: t for t in tables}
    for table, entry in drops:
        by_table[table]["Entries"].append(entry)

    save(os.path.join(CONTENT, "items.json"), items + new_items)
    save(os.path.join(CONTENT, "recipes.json"), recipes + new_recipes)
    save(os.path.join(CONTENT, "droptables.json"), tables)

    manifest = json.load(open(MANIFEST, encoding="utf-8"))
    manifest["weapon_palette"] = palettes
    for p in set(palettes.values()):
        assert p in manifest["palettes"], f"unknown palette {p}"
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(manifest, indent=1, ensure_ascii=False) + "\n")

    weapons = [i for i in items + new_items if i["Slot"] == "Weapon" and not re.search(r"_(plus\d|ench_[a-z]+)$", i["Id"])]
    print(f"weapons: {len(W)} generated bases, {len(weapons)} base weapons total, {len(new_items)} item rows, "
          f"{len(new_recipes)} recipes, {len(drops)} drop entries")


if __name__ == "__main__":
    main()
