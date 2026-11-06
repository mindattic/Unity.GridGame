using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T302 (§4.1.2): every hero class and most enemy families carry signature Passive/Reactive abilities, written
    /// as data on the GG-T296 trigger engine, with honest text.</summary>
    public class SignaturePassiveTests
    {
        private static bool Triggered(ContentDb c, string id) => Triggers.IsTriggered(c.TryAbility(id));

        [Test]
        public void Content_validates()
        {
            Assert.That(ContentLoader.Validate(Real), Is.Empty);
        }

        [Test]
        public void Every_hero_class_has_a_signature_passive_or_reactive()
        {
            var c = Real;
            var heroes = c.Classes.Values.Where(k => k.Team == Team.Hero).ToList();
            Assert.That(heroes.Count, Is.GreaterThanOrEqualTo(65));
            foreach (var k in heroes)
            {
                int n = k.Abilities.Count(id => Triggered(c, id));
                Assert.That(n, Is.InRange(1, 2), $"{k.Id} carries 1-2 battle passives in its kit");
            }
        }

        [Test]
        public void Every_triggered_description_says_its_chance_with_the_right_band_word()
        {
            var c = Real;
            foreach (var a in c.Abilities.Values.Where(Triggers.IsTriggered))
            {
                if (a.Trigger.Chance >= 1f) continue;
                Assert.That(a.Description, Does.Contain(Glossary.ChanceWord(a.Trigger.Chance)), $"{a.Id} ({a.Trigger.Chance:P0})");
            }
        }

        [Test]
        public void Trigger_sentences_only_use_glossary_terms()
        {
            var c = Real;
            foreach (var a in c.Abilities.Values.Where(Triggers.IsTriggered))
                foreach (var t in Glossary.Terms(TriggerText.Describe(c, a)))
                    Assert.That(c.TryTerm(t), Is.Not.Null, $"{a.Id}: [{t}]");
        }

        [Test]
        public void Guard_stance_promises_a_counter_and_its_classes_deliver_one()
        {
            var c = Real;
            foreach (var k in c.Classes.Values.Where(k => k.Abilities.Contains("duelist_guardstance")))
                Assert.That(k.Abilities, Does.Contain("guard_counter"), k.Id);
            var g = c.Ability("guard_counter").Trigger;
            Assert.That(g.Condition.HasStatus, Is.EqualTo(c.Ability("duelist_guardstance").ApplyBuff));
            Assert.That(c.Items.Values.Any(i => i.PassiveName == "Riposte" && i.LifestealFraction > 0f), Is.False, "a lifesteal blade isn't named Riposte");
        }

        private static ContentDb WithReal(params string[] ids)
        {
            var db = Mini();
            foreach (var id in ids) db.Abilities[id] = Real.Ability(id);
            foreach (var b in new[] { "ward", "empowered", "poisoned", "protection" }) db.Buffs[b] = Real.Buff(b);
            return db;
        }

        [Test]
        public void A_surrounded_tank_raises_its_shield_wall()
        {
            var db = WithReal("shield_wall");
            db.Classes["knight"].Abilities.Add("shield_wall");
            db.Classes["wolf"].MoveRange = 0;
            BattleState Run(params (string, int, int)[] wolves)
            {
                var setup = new BattleSetup { Stage = Stage(wolves), Seed = 5, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(3, 4) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(6, 7) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                BattleEngine.Apply(s, new ShieldCommand());
                return s;
            }
            Assert.That(Hero(Run(("wolf", 2, 4), ("wolf", 4, 4)), "knight").HasBuff("ward"), Is.True, "two adjacent foes: Shield Wall");
            Assert.That(Hero(Run(("wolf", 2, 4), ("wolf", 0, 0)), "knight").HasBuff("ward"), Is.False, "one adjacent foe isn't enough");
        }

        [Test]
        public void Vampires_kiss_heals_the_vampire_a_share_of_each_landed_blow()
        {
            var db = WithReal("vampires_kiss");
            db.Classes["knight"].Abilities.Add("vampires_kiss");
            var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var k = Hero(s, "knight");
            k.Hp = k.MaxHp / 2;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var blow = ev.OfType<DamagedEvent>().First(d => d.SourceId == k.Id && d.Amount > 0 && d.Hit != HitKind.Miss);
            var heal = ev.OfType<HealedEvent>().First(h => h.TargetId == k.Id);
            Assert.That(heal.Amount, Is.EqualTo(System.Math.Max(1, (int)System.Math.Round(blow.Amount * 0.15f))));
            Assert.That(ev.OfType<TriggeredEvent>().Any(t => t.UnitId == k.Id && t.AbilityId == "vampires_kiss" && t.Effect == TriggerEffectKind.Lifesteal), Is.True);
            Assert.That(ev.OfType<HealedEvent>().Any(h => h.TargetId == Hero(s, "cleric").Id), Is.False, "only the vampire drinks");
        }

        [Test]
        public void A_frogs_toxic_skin_sometimes_poisons_the_hero_that_strikes_it()
        {
            int fired = 0, strikes = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var db = WithReal("toxic_skin");
                db.Classes["tank"].Abilities.Add("toxic_skin");
                var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = seed, UnlockedSlots = 5 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                var tank = s.Units.Single(u => u.Def.Id == "tank");
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                strikes += ev.OfType<DamagedEvent>().Count(d => d.TargetId == tank.Id && d.Amount > 0 && d.Hit != HitKind.Miss);
                foreach (var t in ev.OfType<TriggeredEvent>().Where(t => t.UnitId == tank.Id && t.AbilityId == "toxic_skin"))
                {
                    fired++;
                    Assert.That(t.Kind, Is.EqualTo(AbilityKind.Reactive));
                    int at = ev.IndexOf(t);
                    var poisoned = ev.Skip(at + 1).OfType<BuffAppliedEvent>().FirstOrDefault(b => b.BuffId == "poisoned");
                    Assert.That(poisoned, Is.Not.Null, $"seed {seed}: the cue precedes the poison");
                    Assert.That(s.Units.Single(u => u.Id == poisoned.UnitId).IsHero, Is.True, "the attacker is poisoned, not the frog");
                }
            }
            Assert.That(fired, Is.GreaterThan(0), "a 35% reactive fires on some seed");
            Assert.That(fired, Is.LessThan(strikes), "but not on every blow");
        }
    }
}
