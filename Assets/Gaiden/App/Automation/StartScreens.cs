using System;
using System.Linq;

namespace GridGame.App.Automation
{
    /// <summary>Screen ids → factories, for `-start <id>` and the automation `goto` endpoint (snapshot sweeps).</summary>
    public static class StartScreens
    {
        public static readonly string[] All =
        {
            "title", "hub", "stages", "items", "status", "equip", "abilities", "talents", "respec", "party", "shop", "blacksmith",
            "alchemist", "enchantress", "talk", "summon", "bounties", "crucible", "bestiary", "settings", "credits", "newgame", "load",
            "profiles", "newprofile",
        };

        public static bool TryGet(string id, out GameScreen screen)
        {
            screen = null;
            var app = GameApp.I;
            bool needsSave = id != "title" && id != "credits" && id != "newgame" && id != "load" && id != "profiles" && id != "newprofile";
            if (needsSave && app.Save == null)
            {
                app.Save = Meta.Progression.NewGame(app.Content, "Preview");
                Meta.CampNpcs.JoinAll(app.Save); // the sweep shows every Camp station (GG-T119)
                app.Slot = -1;
            }
            string hero = app.Save?.Party.FirstOrDefault();
            switch (id)
            {
                case "title": screen = new TitleScreen(); break;
                case "hub": screen = new HubScreen(); break;
                case "stages": screen = new StageSelectScreen(); break;
                case "items": screen = new ItemsScreen(); break;
                case "status": screen = new StatusScreen(hero); break;
                case "equip": screen = new EquipScreen(hero); break;
                case "abilities": screen = new AbilitiesScreen(hero); break;
                case "talents": screen = new SkillTreeScreen(hero); break;
                case "respec": screen = new RespecScreen(); break;
                case "party": screen = new PartyScreen(); break;
                case "shop": screen = new ShopScreen(); break;
                case "blacksmith": screen = new BlacksmithScreen(); break;
                case "alchemist": screen = new AlchemistScreen(); break;
                case "enchantress": screen = new EnchantressScreen(); break;
                case "talk": screen = new TalkScreen(); break;
                case "summon": screen = new SummonScreen(); break;
                case "mercenaries": screen = new MercenaryScreen(); break;
                case "bounties": screen = new BountyScreen(); break;
                case "quests": screen = new QuestScreen(); break;
                case "crucible": screen = new CrucibleScreen(); break;
                case "bestiary": screen = new BestiaryScreen(); break;
                case "settings": screen = new SettingsScreen(); break;
                case "credits": screen = new CreditsScreen(); break;
                case "newgame": screen = new SaveSlotScreen(true); break;
                case "load": screen = new SaveSlotScreen(false); break;
                case "profiles": screen = new ProfileSelectScreen(); break; // GG-T99
                case "newprofile": screen = new ProfileNameScreen(null); break;
            }
            return screen != null;
        }
    }
}
