using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Party tempo (GG-T268, BIBLE §2.11): hero haste slows enemy icons, Slow on heroes speeds them; it stacks.</summary>
    public class TempoTests
    {
        private static (BattleState s, Unit knight, Unit mage, Unit tank) Setup(ContentDb db)
        {
            var (s, _) = Battle(db, Stage(("tank", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            return (s, Hero(s, "knight"), Hero(s, "mage"), tank);
        }

        private static void Quicken(BattleState s, Unit caster, Unit target)
        {
            caster.Bar[0] = new AbilitySlot { AbilityId = "quicken" };
            caster.Cooldowns.Clear();
            s.Bank.Add(1);
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = caster.Id, Slot = 0, Target = target.Pos });
        }

        [Test]
        public void Hasting_a_hero_raises_party_tempo_and_slows_every_enemy_icon()
        {
            var db = Mini();
            var (s, knight, mage, tank) = Setup(db);
            float before = BattleEngine.UPerTick(s, s.TurnEntryOf(tank.Id));
            Quicken(s, knight, mage);
            Assert.That(s.NetTempo, Is.EqualTo(2), "Quicken (0.3 u) is two tempo points");
            Assert.That(BattleEngine.EnemyTempoMultiplier(s), Is.EqualTo(0.64f).Within(1e-3));
            Assert.That(BattleEngine.UPerTick(s, s.TurnEntryOf(tank.Id)), Is.LessThan(before));
        }

        [Test]
        public void Haste_stacks_and_clamps_so_enemies_never_freeze()
        {
            var db = Mini();
            var (s, knight, mage, _) = Setup(db);
            Quicken(s, knight, mage);
            Quicken(s, mage, knight);
            Assert.That(s.NetTempo, Is.EqualTo(4), "two casts stack");
            for (int i = 0; i < 4; i++) s.Tempo.Add(new TempoEffect { Source = "t" + i, Amount = 2, TurnsLeft = 3 });
            Assert.That(BattleEngine.EnemyTempoMultiplier(s), Is.EqualTo(BattleConfig.TempoMinMultiplier).Within(1e-4));
        }

        [Test]
        public void Slow_on_a_hero_is_negative_tempo_that_cancels_haste_and_clamps_the_other_way()
        {
            var db = Mini();
            db.Buffs["slowed"] = new BuffDef { Id = "slowed", Name = "Slowed", Unit = DurationUnit.Turns, Duration = 2, TimelineSpeedMultiplier = 0.5f, Tempo = -2 };
            db.Abilities["slow"] = new AbilityDef { Id = "slow", Name = "Slow", Kind = AbilityKind.Spell, ApplyBuff = "slowed", ApplyBuffChance = 1f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.Any };
            var (s, knight, mage, _) = Setup(db);
            Quicken(s, knight, mage);
            mage.Bar[1] = new AbilitySlot { AbilityId = "slow" };
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = mage.Id, Slot = 1, Target = knight.Pos });
            Assert.That(s.NetTempo, Is.EqualTo(0), "Slow on a hero cancels the haste");
            Assert.That(BattleEngine.EnemyTempoMultiplier(s), Is.EqualTo(1f));
            for (int i = 0; i < 5; i++) s.Tempo.Add(new TempoEffect { Source = "s" + i, Amount = -2, TurnsLeft = 3 });
            Assert.That(BattleEngine.EnemyTempoMultiplier(s), Is.EqualTo(BattleConfig.TempoMaxMultiplier).Within(1e-4));
        }

        [Test]
        public void Tempo_wears_off_at_the_party_turn_boundary()
        {
            var db = Mini();
            var (s, knight, mage, _) = Setup(db);
            Quicken(s, knight, mage);
            int turns = BattleConfig.DefaultTempoTurns;
            for (int i = 0; i < turns + 2 && s.NetTempo != 0; i++) BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(s.NetTempo, Is.EqualTo(0));
        }

        [Test]
        public void An_area_haste_is_one_tempo_effect_per_cast_not_per_target()
        {
            var db = Mini();
            db.Abilities["haste_field"] = new AbilityDef { Id = "haste_field", Name = "Haste Field", Kind = AbilityKind.Spell, Cost = 1, HastenU = 0.15f, Shape = TargetShape.Square, Radius = 3, Mode = TargetMode.Auto, Filter = TargetFilter.AllyOnly };
            var (s, knight, _, _) = Setup(db);
            knight.Bar[0] = new AbilitySlot { AbilityId = "haste_field" };
            s.Bank.Add(1);
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = knight.Pos });
            Assert.That(s.Tempo.Count, Is.EqualTo(1));
            Assert.That(s.NetTempo, Is.EqualTo(1));
        }

        [Test]
        public void Hasting_an_enemy_still_slides_its_icon_and_leaves_tempo_alone()
        {
            var db = Mini();
            var (s, knight, _, tank) = Setup(db);
            s.TurnEntryOf(tank.Id).Ip = 0;
            Quicken(s, knight, tank);
            Assert.That(s.NetTempo, Is.EqualTo(0));
        }
    }
}
