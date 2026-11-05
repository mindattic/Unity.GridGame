using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Random rescues (§25.11.2, GG-T236).</summary>
    public class RescueTests
    {
        private static StageDef PlainStage(ContentDb c) =>
            c.CampaignStages().First(st => st.Waves.All(w => w.Guests.Count == 0 && w.Recruitables.Count == 0));

        [Test]
        public void The_roll_is_deterministic_and_lands_about_a_quarter_of_the_time()
        {
            var c = Real; var s = Progression.NewGame(c, "R1");
            var stage = PlainStage(c);
            int hits = 0;
            for (int seed = 1; seed <= 400; seed++)
            {
                var a = Rescues.Roll(c, s, stage, seed);
                var b = Rescues.Roll(c, s, stage, seed);
                Assert.That(b?.Class, Is.EqualTo(a?.Class));
                Assert.That(b?.Wave, Is.EqualTo(a?.Wave));
                if (a == null) continue;
                hits++;
                Assert.That(a.Wave, Is.InRange(0, stage.Waves.Count - 1));
            }
            Assert.That(hits, Is.InRange(400 * Rescues.Chance * 0.5f, 400 * Rescues.Chance * 1.5f));
        }

        [Test]
        public void A_captive_is_never_a_hero_promised_another_way_in()
        {
            var c = Real; var s = Progression.NewGame(c, "R2");
            var pool = Rescues.Pool(c, s);
            Assert.That(pool, Is.Not.Empty);
            var guests = c.Stages.Values.SelectMany(st => st.Waves).SelectMany(w => w.Guests.Select(g => g.Class).Concat(w.Recruitables.Select(r => r.Class)));
            var named = c.Dialogues.Values.SelectMany(d => d.AllEffects()).Where(e => e != null && (e.Type == DialogueEffectType.JoinHero || e.Type == DialogueEffectType.RecruitHero) && e.Id != null).Select(e => e.Id);
            foreach (var id in pool)
            {
                Assert.That(c.Class(id).Team, Is.EqualTo(Team.Hero), id);
                Assert.That(s.Hero(id), Is.Null, id);
                Assert.That(c.Summon.Pool, Does.Not.Contain(id));
                Assert.That(guests, Does.Not.Contain(id));
                Assert.That(named, Does.Not.Contain(id));
                Assert.That(c.Mercenaries.Select(m => m.Class), Does.Not.Contain(id));
            }
        }

        [Test]
        public void No_roll_in_test_mode_the_crucible_or_a_stage_with_its_own_guest()
        {
            var c = Real;
            var s = Progression.NewGame(c, "R3");
            var withGuest = c.CampaignStages().FirstOrDefault(st => st.Waves.Any(w => w.Guests.Count > 0 || w.Recruitables.Count > 0));
            var test = Progression.NewGame(c, "R3t"); test.TestMode = true;
            for (int seed = 1; seed <= 200; seed++)
            {
                Assert.That(Rescues.Roll(c, test, PlainStage(c), seed), Is.Null);
                if (withGuest != null) Assert.That(Rescues.Roll(c, s, withGuest, seed), Is.Null);
                Assert.That(Rescues.Roll(c, s, new StageDef { Id = Crucible.IdPrefix + seed, Name = "C", Waves = { new WaveDef() } }, seed), Is.Null);
            }
        }

        private static BattleState CaptiveBattle(ContentDb c, SaveData s, string cls, List<BattleEvent> ev)
        {
            var setup = BattleFactory.Build(c, s, PlainStage(c).Id, 1);
            setup.Captive = new CaptiveSpec { Class = cls, Wave = 0 };
            return BattleEngine.Create(c, setup, ev);
        }

        [Test]
        public void A_captive_standing_at_a_victory_is_freed_and_joins()
        {
            var c = Real; var s = Progression.NewGame(c, "R4");
            string cls = Rescues.Pool(c, s)[0];
            var ev = new List<BattleEvent>();
            var st = CaptiveBattle(c, s, cls, ev);
            var captive = st.Units.Single(u => u.IsGuest);
            Assert.That(captive.Def.Id, Is.EqualTo(cls));
            Assert.That(ev.OfType<GuestJoinedEvent>().Single().Captive, Is.True);
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text.StartsWith("A captive")), Is.True);
            st.Phase = BattlePhase.Victory;
            var rep = BattleFactory.Commit(c, s, st);
            Assert.That(rep.Captive, Is.EqualTo(cls));
            Assert.That(rep.Rescued, Is.True);
            Assert.That(rep.GuestsJoined, Does.Contain(cls));
            Assert.That(s.Hero(cls), Is.Not.Null);
            Assert.That(Rescues.Pool(c, s), Does.Not.Contain(cls));
            Assert.That(Rescues.Scene(c, cls), Is.Not.Null);
        }

        [Test]
        public void A_captive_down_at_the_end_is_not_lost()
        {
            var c = Real; var s = Progression.NewGame(c, "R5");
            string cls = Rescues.Pool(c, s)[0];
            var st = CaptiveBattle(c, s, cls, new List<BattleEvent>());
            var captive = st.Units.Single(u => u.IsGuest);
            captive.Hp = 0; captive.OnBoard = false;
            st.Phase = BattlePhase.Victory;
            var rep = BattleFactory.Commit(c, s, st);
            Assert.That(rep.Captive, Is.EqualTo(cls));
            Assert.That(rep.Rescued, Is.False);
            Assert.That(rep.GuestsFell, Does.Contain(cls));
            Assert.That(s.Hero(cls), Is.Null);
            Assert.That(s.LostGuests, Is.Empty);
            Assert.That(Rescues.Pool(c, s), Does.Contain(cls)); // still out there to be found again
        }

        [Test]
        public void A_rescue_scene_must_name_a_hero_or_nobody()
        {
            var d = new DialogueDef
            {
                Id = "rescue_x", Trigger = new DialogueTrigger { Kind = DialogueTriggerKind.Rescue, Id = "goblin" },
                Speakers = { new DialogueSpeaker { Id = "f", Class = "$with" } },
                Nodes = { new DialogueNode { Id = "a", Speaker = "f", Text = "Free!" } },
            };
            var db = new ContentDb();
            db.Classes["knight"] = new ClassDef { Id = "knight", Name = "Knight", Team = Team.Hero, Role = "Tank", HomeTree = "t" };
            db.Classes["goblin"] = new ClassDef { Id = "goblin", Name = "Goblin", Team = Team.Enemy };
            db.SkillTrees["t"] = new SkillTreeDef { Id = "t", Name = "T" };
            db.Dialogues[d.Id] = d;
            db.DialogueOrder.Add(d);
            Assert.That(ContentLoader.Validate(db, null), Has.Some.Contains("Rescue needs a hero class"));
            d.Trigger.Id = "knight";
            Assert.That(ContentLoader.Validate(db, null).Where(p => p.Contains("rescue_x")), Is.Empty); // "$with" fits a rescue scene
            d.Trigger.Id = null;
            Assert.That(ContentLoader.Validate(db, null).Where(p => p.Contains("rescue_x")), Is.Empty);
        }
    }
}
