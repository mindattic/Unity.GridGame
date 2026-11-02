using System.Collections.Generic;
using System.Linq;
using System.Text;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    public class ContentAndSoakTests
    {

        [Test]
        public void Real_content_loads_and_cross_references_resolve()
        {
            var db = Real;
            Assert.That(db.Classes, Is.Not.Empty);
            Assert.That(db.Stages, Is.Not.Empty);
            var problems = ContentLoader.Validate(db);
            Assert.That(problems.Count == 0, Is.True, string.Join("\n", problems));
        }

        [Test]
        public void Every_enemy_family_has_a_drop_table_with_a_signature_material()
        {
            var db = Real;
            var enemies = db.Classes.Values.Where(c => c.Team == Team.Enemy).ToList();

            // Remains (gravestones/bone piles) and eggs give no loot by design (§14.5/§14.8) and
            // must stay undroppable rather than silently losing a table in a future content pass.
            var lootless = enemies.Where(c => c.Tags.Contains("Remains") || c.Tags.Contains("Egg")).ToList();
            Assert.That(lootless, Is.Not.Empty);
            foreach (var c in lootless) Assert.That(c.DropTable, Is.Null);

            var lootable = enemies.Except(lootless).ToList();
            Assert.That(lootable, Is.Not.Empty);
            var missing = lootable.Where(c => c.DropTable == null || !db.DropTables.ContainsKey(c.DropTable) || db.DropTables[c.DropTable].Entries.Count == 0).Select(c => c.Id).ToList();
            Assert.That(missing.Count == 0, Is.True, "enemy classes with no drop table: " + string.Join(", ", missing));

            // GG-T43's own new materials must stay each family's exclusive ingredient — reused
            // across tables, they'd stop telling the player "farm this stage for this reagent".
            var newMaterials = new[]
            {
                "mat_frog_gland", "mat_goblin_scrap_iron", "mat_bandit_trophy", "mat_military_insignia",
                "mat_razor_wire", "mat_bat_wing", "mat_vulture_feather", "mat_scorpion_stinger", "mat_sand_chitin",
                "mat_scrap_circuit", "mat_gremlin_cog", "mat_mecha_plating", "mat_swamp_ichor", "mat_marsh_moss",
                "mat_heartwood", "mat_hag_claw", "mat_mistress_veil", "mat_bone_dust", "mat_ceramic_shard",
                "mat_cyclops_eye", "mat_frost_core", "mat_oni_horn", "mat_yeti_fur", "mat_templar_seal",
                "mat_cyber_core", "mat_mannequin_joint", "mat_phantom_wisp", "mat_reaper_shroud", "mat_vampire_fang",
            };
            foreach (var item in newMaterials)
            {
                var tablesWithIt = db.DropTables.Values.Where(t => t.Entries.Any(e => e.Item == item)).Select(t => t.Id).ToList();
                Assert.That(tablesWithIt.Count == 1, Is.True, $"{item} should belong to exactly one family's drop table, found in: {string.Join(", ", tablesWithIt)}");
            }

            // Spot-check the families the ticket names explicitly (GG-T43).
            Assert.That(db.DropTables["frog"].Entries.Any(e => e.Item == "mat_frog_gland"), Is.True);
            Assert.That(db.DropTables["goblinthug00"].Entries.Any(e => e.Item == "mat_goblin_scrap_iron"), Is.True);
            Assert.That(db.DropTables["slime"].Entries.Any(e => e.Item == "mat_slime_gel"), Is.True);
            Assert.That(db.DropTables["undead"].Entries.Any(e => e.Item == "mat_undead_bone"), Is.True);
        }

        public static List<HeroSpec> StarterParty(ContentDb db, int level)
        {
            var party = new List<HeroSpec>();
            foreach (var id in new[] { "paladin", "barbarian", "cleric" })
            {
                var def = db.Class(id);
                var spec = new HeroSpec { ClassId = id, Level = level, Bar = new List<string>(def.DefaultLoadout) };
                foreach (var b in spec.Bar.Where(b => b.StartsWith("item:"))) spec.ItemCounts[b.Substring(5).Split(':')[0]] = 3;
                party.Add(spec);
            }
            return party;
        }
        [TestCase(0f)]
        [TestCase(0.6f)]
        public void Bots_play_every_stage_without_breaking_invariants(float randomness)
        {
            var db = Real;
            var report = new StringBuilder();
            int total = 0, wins = 0, stalls = 0;
            var stages = db.StageOrder;
            int index = 0;
            foreach (var stage in stages)
            {
                index++;
                int stageWins = 0, stageRuns = 0, actions = 0, forced = 0;
                long ticks = 0;
                for (int seed = 1; seed <= 12; seed++)
                {
                    int level = stage.IsTest ? 5 : System.Math.Max(1, index);
                    var setup = new BattleSetup { Stage = stage, Seed = seed * 7919 + index, UnlockedSlots = 5, RecommendedLevel = stage.IsTest ? 1 : index, Party = StarterParty(db, level) };
                    var bot = new HeroBot(seed, randomness);
                    var s = BotRunner.Play(db, setup, bot, 300, (cmd, ev, st) =>
                    {
                        AssertInvariants(st); AssertEventInvariants(ev);
                        // GG-T188 (§2.0): a bot drag is a held turn — it fits its window, and a forced drop is reported.
                        if (cmd is MoveCommand m) Assert.That(m.HeldSeconds, Is.GreaterThan(0f), $"{m}: a drag holds the hero for some time");
                        forced += ev.Count(e => e is TurnTimeUpEvent);
                    });
                    stageRuns++; total++;
                    if (s.Phase == BattlePhase.Victory) { stageWins++; wins++; }
                    if (!s.IsOver) stalls++;
                    actions += s.Results.ActionsTaken;
                    ticks += s.Results.TicksElapsed;
                }
                report.AppendLine($"{stage.Id,-22} win {stageWins,2}/{stageRuns}  avg actions {actions / (float)stageRuns:F1}  avg turn {ticks / (float)System.Math.Max(1, actions) / BattleConfig.TicksPerSecond:F2}s  forced drops {forced}");
            }
            report.AppendLine($"TOTAL win {wins}/{total}, unfinished {stalls}");
            TestContext.Out.WriteLine(report.ToString());
        }

        [Test]
        public void Battles_replay_identically_from_seed()
        {
            var db = Real;
            var stage = db.StageOrder.First(s => !s.IsTest);
            string Run()
            {
                var sb = new StringBuilder();
                var setup = new BattleSetup { Stage = stage, Seed = 42, UnlockedSlots = 5, Party = StarterParty(db, 3) };
                BotRunner.Play(db, setup, new HeroBot(5), 120, (cmd, ev, st) => { sb.AppendLine(cmd?.ToString()); foreach (var e in ev) sb.AppendLine(e.ToString()); });
                return sb.ToString();
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }
    }
}
