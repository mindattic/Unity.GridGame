using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T289: only enemies with a society (tag Sentient) can pincer; heroes always can. A frog folk
    /// or a soldier closes a pincer on a hero line; a yeti or an undead never does, Humanoid body or not.
    /// </summary>
    public class PincerEligibilityTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            var st = new Stats { Strength = 5, Vitality = 2, Agility = 10, Speed = 10, Intelligence = 10 };
            db.Classes["frog"] = new ClassDef { Id = "frog", Name = "Frog", Team = Team.Enemy, Tags = { "Aquatic", "Sentient" }, BaseStats = st, MoveRange = 0 };
            db.Classes["soldier"] = new ClassDef { Id = "soldier", Name = "Soldier", Team = Team.Enemy, Tags = { "Humanoid", "Sentient" }, BaseStats = st, MoveRange = 0 };
            db.Classes["yeti"] = new ClassDef { Id = "yeti", Name = "Yeti", Team = Team.Enemy, Tags = { "Beast", "Humanoid" }, BaseStats = st, MoveRange = 0 };
            db.Classes["zombie"] = new ClassDef { Id = "zombie", Name = "Zombie", Team = Team.Enemy, Tags = { "Humanoid", "Undead" }, BaseStats = st, MoveRange = 0 };
            return db;
        }

        /// <summary>Two of <paramref name="cls"/> flank the knight on row 3: (0,3) enemy, (1,3) knight, (2,3) enemy.</summary>
        private static BattleState Flank(string cls) =>
            Battle(Db(), Stage((cls, 0, 3), (cls, 2, 3)), ("knight", 1, 3), ("cleric", 5, 7)).state;

        [TestCase("frog")]
        [TestCase("soldier")]
        public void Sentient_enemies_pincer_a_hero_line(string cls)
        {
            var s = Flank(cls);
            Assert.That(s.Units.Where(u => u.Def.Id == cls).All(u => u.CanPincer), Is.True);
            var pincers = PincerDetector.Detect(s, Team.Enemy, null);
            Assert.That(pincers, Has.Count.EqualTo(1));
            Assert.That(pincers[0].A.Def.Id, Is.EqualTo(cls));
        }

        [TestCase("yeti")]
        [TestCase("zombie")]
        public void Enemies_without_a_society_never_pincer(string cls)
        {
            var s = Flank(cls);
            Assert.That(s.Units.Where(u => u.Def.Id == cls).Any(u => u.CanPincer), Is.False);
            Assert.That(PincerDetector.Detect(s, Team.Enemy, null), Is.Empty);
        }

        [Test]
        public void A_frog_closes_the_pincer_on_its_own_turn()
        {
            var s = Flank("frog");
            s.TurnEntryOf(s.Units.First(u => u.Def.Id == "frog").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is PincerFormedEvent p && p.Team == Team.Enemy), Is.True);
        }

        [Test]
        public void A_yeti_flanking_a_hero_only_melees()
        {
            var s = Flank("yeti");
            s.TurnEntryOf(s.Units.First(u => u.Def.Id == "yeti").Id).Ip = 999;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is PincerFormedEvent), Is.False);
        }

        [Test]
        public void Heroes_always_pincer()
        {
            var s = Flank("yeti");
            Assert.That(s.Units.Where(u => u.IsHero).All(u => u.CanPincer), Is.True);
        }

        [Test]
        public void Real_content_tags_society_not_body()
        {
            var c = Real.Classes;
            foreach (var id in new[] { "frog00", "toad00", "soldier00", "goblinthug00", "mechaarmor00", "naga00", "lurker00" })
                Assert.That(c[id].HasTag("Sentient"), Is.True, id);
            foreach (var id in new[] { "yeti", "bat00", "slime00", "werewolf00", "scorpion", "phantom", "reaper", "undead00", "cyberzombie00", "treegolem00", "nagaegg" })
                Assert.That(c[id].HasTag("Sentient"), Is.False, id);
            Assert.That(c.Values.Where(d => d.HasTag("Sentient")).All(d => d.Team == Team.Enemy && !d.HasTag("Undead")), Is.True);
        }
    }
}
