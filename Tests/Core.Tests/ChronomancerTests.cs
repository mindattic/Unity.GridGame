using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>The Chronomancer (GG-T275, BIBLE §14.10): Temporal Drag fights the party's stacked haste on the net-tempo timeline.</summary>
    public class ChronomancerTests
    {
        [Test]
        public void The_chronomancer_validates_and_its_charge_is_a_negative_tempo_drag()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("chronomancer") || p.Contains("temporal_drag") || p.Contains("chrono_rewind")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var c = db.Classes["chronomancer"];
            var drag = db.Abilities[c.ChargeAbility];
            Assert.That(drag.Shape, Is.EqualTo(TargetShape.AllEnemies));
            Assert.That(db.Buffs[drag.ApplyBuff].Tempo, Is.LessThan(0));
            Assert.That(db.Stages.Values.Any(s => s.Waves.Any(w => w.Enemies.Any(e => e.Class == "chronomancer"))), "placed in the campaign");
        }

        [Test]
        public void Temporal_drag_on_the_whole_party_costs_one_tempo_point_not_one_per_hero()
        {
            var (s, _) = Battle(Real, Stage(("chronomancer", 5, 0)), ("paladin", 1, 7), ("cleric", 3, 7));
            for (int i = 0; i < 12 && s.NetTempo == 0 && !s.IsOver; i++) BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(s.NetTempo, Is.EqualTo(-1), "the charge landed once for the party");
            Assert.That(s.Units.Where(u => u.IsHero && u.Alive).All(h => h.HasBuff("slowed")), "every hero is Slowed");
        }

        [Test]
        public void Cleansing_slowed_off_the_last_hero_gives_the_party_its_tempo_back()
        {
            var db = Mini();
            db.Buffs["slowed"] = new BuffDef { Id = "slowed", Name = "Slowed", Unit = DurationUnit.Turns, Duration = 3, Tempo = -1 };
            db.Abilities["drag"] = new AbilityDef { Id = "drag", Name = "Drag", Kind = AbilityKind.Spell, ApplyBuff = "slowed", ApplyBuffChance = 1f, Shape = TargetShape.AllAllies, Mode = TargetMode.Auto, Filter = TargetFilter.Any };
            db.Abilities["cure"] = new AbilityDef { Id = "cure", Name = "Cure", Kind = AbilityKind.Spell, RemovesBuff = "slowed", Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.Any };
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var knight = Hero(s, "knight"); var mage = Hero(s, "mage");
            knight.Bar[0] = new AbilitySlot { AbilityId = "drag" };
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = knight.Pos });
            Assert.That(s.NetTempo, Is.EqualTo(-1));
            mage.Bar[1] = new AbilitySlot { AbilityId = "cure" };
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = mage.Id, Slot = 1, Target = knight.Pos });
            Assert.That(s.NetTempo, Is.EqualTo(-1), "one hero is still Slowed");
            mage.Cooldowns.Clear();
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = mage.Id, Slot = 1, Target = mage.Pos });
            Assert.That(s.NetTempo, Is.EqualTo(0), "the last Slowed hero is cleansed");
        }

        [Test]
        public void The_chronomancer_holds_its_drag_while_the_party_is_already_behind()
        {
            var (s, _) = Battle(Real, Stage(("chronomancer", 5, 0)), ("paladin", 1, 7), ("cleric", 3, 7));
            var chrono = s.Units.Single(u => u.Def.Id == "chronomancer");
            Assert.That(EnemyPlanner.PlanCast(s, chrono, false), Is.Not.Null, "neutral tempo: it charges");
            s.Tempo.Add(new TempoEffect { Source = "buff:slowed", Amount = -1, TurnsLeft = 2, Fresh = false });
            Assert.That(EnemyPlanner.PlanCast(s, chrono, false), Is.Null, "already behind: it saves the charge");
            s.Tempo.Clear();
            s.Tempo.Add(new TempoEffect { Source = "haste", Amount = 2, TurnsLeft = 2, Fresh = false });
            Assert.That(EnemyPlanner.PlanCast(s, chrono, false), Is.Not.Null, "the party hastes: it drags");
        }

        [Test]
        public void Rewind_heals_and_hastens_only_its_own_icon_once()
        {
            var (s, _) = Battle(Real, Stage(("chronomancer", 5, 0)), ("paladin", 1, 7), ("cleric", 3, 7));
            var chrono = s.Units.Single(u => u.Def.Id == "chronomancer");
            Assert.That(chrono.Def.BossPhases.Single().OnEnterAbility, Is.EqualTo("chrono_rewind"));
            var rewind = Real.Abilities["chrono_rewind"];
            Assert.That(rewind.HastenU, Is.GreaterThan(0f));
            Assert.That(rewind.Shape, Is.EqualTo(TargetShape.Self));
            Assert.That(s.NetTempo, Is.EqualTo(0), "an enemy's haste never moves the party's tempo");
        }
    }
}
