using GridGame.Core.Content;
using System.Linq;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>
    /// Only the happy path (real content validates clean, ContentAndSoakTests) was covered before this
    /// file: a regression in ContentLoader.Validate's cross-reference checks would go unnoticed because
    /// nothing exercised the broken-reference paths. Each test here takes a small, fully valid fixture
    /// and breaks exactly one reference, asserting Validate reports exactly that problem.
    /// </summary>
    public class ContentValidationTests
    {
        private static ContentDb ValidDb()
        {
            var db = new ContentDb();

            db.Buffs["burning"] = new BuffDef { Id = "burning", Name = "Burning" };

            db.Abilities["slash"] = new AbilityDef { Id = "slash", Name = "Slash", Kind = AbilityKind.Skill };
            db.Abilities["bolt"] = new AbilityDef { Id = "bolt", Name = "Bolt", Kind = AbilityKind.Spell, ApplyBuff = "burning" };

            db.Items["potion"] = new ItemDef { Id = "potion", Name = "Potion", Type = ItemType.Consumable };
            db.Items["ore"] = new ItemDef { Id = "ore", Name = "Ore", Type = ItemType.CraftingMaterial };
            db.Items["blade"] = new ItemDef { Id = "blade", Name = "Blade", Type = ItemType.Equipment };

            db.DropTables["knightdrop"] = new DropTableDef
            {
                Id = "knightdrop",
                Entries = { new DropEntry { Item = "potion", Chance = 1f } },
            };

            db.Recipes["r1"] = new RecipeDef
            {
                Id = "r1",
                Station = CraftStation.Forge,
                BaseItem = "ore",
                Result = "blade",
                Inputs = { new ItemCount { Item = "ore", Count = 1 } },
            };

            db.Traps["spike"] = new TrapDef { Id = "spike", Name = "Spike" };

            db.Pickups["p1"] = new PickupDef { Id = "p1", Name = "Pickup", Weight = 1 };

            db.Classes["goblin"] = new ClassDef { Id = "goblin", Name = "Goblin", Team = Team.Enemy };

            db.Classes["boss"] = new ClassDef
            {
                Id = "boss",
                Name = "Boss",
                Team = Team.Enemy,
                ChargeAbility = "bolt",
                Trap = "spike",
                Trail = "spike",
                BossPhases = { new BossPhaseDef { HpThreshold = 0.5f, OnEnterAbility = "bolt" } },
            };

            db.SkillTrees["tanktree"] = new SkillTreeDef { Id = "tanktree", Name = "Tank Tree" };

            db.Classes["knight"] = new ClassDef
            {
                Id = "knight",
                Name = "Knight",
                Team = Team.Hero,
                Role = "Tank",
                DropTable = "knightdrop",
                Abilities = { "slash" },
                DefaultLoadout = { "slash", "item:potion" },
                HomeTree = "tanktree",
            };

            db.Stages["s1"] = new StageDef
            {
                Id = "s1",
                Name = "S1",
                Waves =
                {
                    new WaveDef
                    {
                        Enemies = { new SpawnDef { Class = "goblin" } },
                        Guests = { new SpawnDef { Class = "goblin" } },
                        SurpriseSpeaker = "goblin",
                    },
                },
            };
            db.StageOrder.Add(db.Stages["s1"]);

            db.Campaign.Themes.Add(new ThemeDef { Id = "t1", Name = "T1", Stages = { "s1" } });

            return db;
        }

        /// <summary>Validates the fixture and asserts it reports exactly one problem, returning it.</summary>
        private static string OneProblem(ContentDb db)
        {
            var problems = ContentLoader.Validate(db);
            Assert.That(problems, Has.Exactly(1).Items);
            return problems.Single();
        }

        [Test]
        public void Baseline_fixture_validates_clean()
        {
            Assert.That(ContentLoader.Validate(ValidDb()), Is.Empty);
        }

        [Test]
        public void Unknown_class_in_stage_wave_enemy_is_reported()
        {
            var db = ValidDb();
            db.Stages["s1"].Waves[0].Enemies[0].Class = "missing";
            Assert.That(OneProblem(db), Does.Contain("unknown class 'missing'"));
        }

        [Test]
        public void Unknown_class_in_stage_wave_guest_is_reported()
        {
            var db = ValidDb();
            db.Stages["s1"].Waves[0].Guests[0].Class = "missing";
            Assert.That(OneProblem(db), Does.Contain("unknown class 'missing'"));
        }

        [Test]
        public void Unknown_class_as_surprise_speaker_is_reported()
        {
            var db = ValidDb();
            db.Stages["s1"].Waves[0].SurpriseSpeaker = "missing";
            Assert.That(OneProblem(db), Does.Contain("surprise speaker"));
        }

        [Test]
        public void Unknown_ability_in_loadout_is_reported()
        {
            var db = ValidDb();
            db.Classes["knight"].DefaultLoadout[0] = "missing";
            Assert.That(OneProblem(db), Does.Contain("loadout"));
        }

        [Test]
        public void Unknown_ability_as_charge_ability_is_reported()
        {
            var db = ValidDb();
            db.Classes["boss"].ChargeAbility = "missing";
            Assert.That(OneProblem(db), Does.Contain("charge"));
        }

        [Test]
        public void Unknown_ability_in_boss_phase_is_reported()
        {
            var db = ValidDb();
            db.Classes["boss"].BossPhases[0].OnEnterAbility = "missing";
            Assert.That(OneProblem(db), Does.Contain("boss phase"));
        }

        [Test]
        public void Null_loadout_entry_is_reported()
        {
            var db = ValidDb();
            db.Classes["knight"].DefaultLoadout.Add(null);
            Assert.That(OneProblem(db), Does.Contain("null entry"));
        }

        [Test]
        public void Unknown_item_in_recipe_is_reported()
        {
            var db = ValidDb();
            db.Recipes["r1"].Result = "missing";
            Assert.That(OneProblem(db), Does.Contain("recipe r1: unknown item 'missing'"));
        }

        [Test]
        public void Unknown_item_in_droptable_is_reported()
        {
            var db = ValidDb();
            db.DropTables["knightdrop"].Entries[0].Item = "missing";
            Assert.That(OneProblem(db), Does.Contain("droptable knightdrop: unknown item 'missing'"));
        }

        [Test]
        public void Unknown_buff_is_reported()
        {
            var db = ValidDb();
            db.Abilities["bolt"].ApplyBuff = "missing";
            Assert.That(OneProblem(db), Does.Contain("unknown buff 'missing'"));
        }

        [Test]
        public void Unknown_trap_is_reported()
        {
            var db = ValidDb();
            db.Classes["boss"].Trap = "missing";
            Assert.That(OneProblem(db), Does.Contain("unknown trap 'missing'"));
        }

        [Test]
        public void Unknown_trail_is_reported()
        {
            var db = ValidDb();
            db.Classes["boss"].Trail = "missing";
            Assert.That(OneProblem(db), Does.Contain("unknown trail 'missing'"));
        }

        [Test]
        public void Theme_referencing_unknown_stage_is_reported()
        {
            var db = ValidDb();
            db.Campaign.Themes[0].Stages[0] = "missing";
            Assert.That(OneProblem(db), Does.Contain("theme t1: unknown stage 'missing'"));
        }
        [TestCase(0)]
        [TestCase(-1)]
        public void Pickup_with_nonpositive_weight_is_reported(int weight)
        {
            var db = ValidDb();
            db.Pickups["p1"].Weight = weight;
            Assert.That(OneProblem(db), Does.Contain("weight must be positive"));
        }
        [TestCase(null)]
        [TestCase("Bogus")]
        public void Invalid_role_is_reported(string role)
        {
            var db = ValidDb();
            db.Classes["knight"].Role = role;
            Assert.That(OneProblem(db), Does.Contain("needs a Role"));
        }

        [Test]
        public void Hero_missing_home_tree_is_reported()
        {
            var db = ValidDb();
            db.Classes["knight"].HomeTree = null;
            Assert.That(OneProblem(db), Does.Contain("needs a HomeTree"));
        }

        [Test]
        public void Unknown_home_tree_is_reported()
        {
            var db = ValidDb();
            db.Classes["knight"].HomeTree = "missing";
            Assert.That(OneProblem(db), Does.Contain("unknown home tree 'missing'"));
        }

        [Test]
        public void Skill_tree_node_with_unknown_grant_is_reported()
        {
            var db = ValidDb();
            db.SkillTrees["tanktree"].Nodes.Add(new SkillNodeDef { Id = "n1", Grants = "missing" });
            Assert.That(OneProblem(db), Does.Contain("unknown ability 'missing'"));
        }

        [Test]
        public void Skill_tree_node_with_unknown_prerequisite_is_reported()
        {
            var db = ValidDb();
            db.SkillTrees["tanktree"].Nodes.Add(new SkillNodeDef { Id = "n1", Grants = "slash", Prerequisites = { "missing" } });
            Assert.That(OneProblem(db), Does.Contain("unknown prerequisite 'missing'"));
        }

        [Test]
        public void Skill_tree_node_prerequisite_cycle_is_reported()
        {
            var db = ValidDb();
            db.SkillTrees["tanktree"].Nodes.Add(new SkillNodeDef { Id = "n1", Grants = "slash", Prerequisites = { "n2" } });
            db.SkillTrees["tanktree"].Nodes.Add(new SkillNodeDef { Id = "n2", Grants = "slash", Prerequisites = { "n1" } });
            Assert.That(string.Join(";", ContentLoader.Validate(db)), Does.Contain("prerequisite cycle"));
        }

        /// <summary>Two branches that both trace back to the same ancestor (e.g. a finale node requiring two
        /// capstones that each descend from a shared root) is a valid diamond, not a cycle — a regression here
        /// would make every real archetype tree's finale node (GG-T74) fail validation.</summary>
        [Test]
        public void Skill_tree_diamond_shaped_prerequisites_are_not_a_cycle()
        {
            var db = ValidDb();
            var tree = db.SkillTrees["tanktree"];
            tree.Nodes.Add(new SkillNodeDef { Id = "root", Grants = "slash", Cost = 1 });
            tree.Nodes.Add(new SkillNodeDef { Id = "branch_a", Grants = "slash", Cost = 1, Prerequisites = { "root" } });
            tree.Nodes.Add(new SkillNodeDef { Id = "branch_b", Grants = "slash", Cost = 1, Prerequisites = { "root" } });
            tree.Nodes.Add(new SkillNodeDef { Id = "finale", Grants = "slash", Cost = 1, Prerequisites = { "branch_a", "branch_b" } });
            Assert.That(ContentLoader.Validate(db), Is.Empty);
        }
    }
}
