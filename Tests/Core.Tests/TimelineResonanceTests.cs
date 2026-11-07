using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T286 (BIBLE §2): the Resonance-style bar. One linear rate, no Zone; an icon sits at seconds-until-acting over
    /// BarSeconds (0 = the left edge, acts now). Casts ride the same axis from their cast time; Pushback is a keyword.
    /// </summary>
    public class TimelineResonanceTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            db.Abilities["zap"] = new AbilityDef { Id = "zap", Name = "Zap", Kind = AbilityKind.Spell, BaseDamage = 1, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Arcane };
            db.Abilities["jab"] = new AbilityDef { Id = "jab", Name = "Jab", Kind = AbilityKind.Skill, BaseDamage = 1, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            db.Abilities["shove_time"] = new AbilityDef { Id = "shove_time", Name = "Shove Time", Kind = AbilityKind.Skill, BaseDamage = 1, Pushback = 1.5f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            db.Abilities["big_shove"] = new AbilityDef { Id = "big_shove", Name = "Big Shove", Kind = AbilityKind.Skill, Pushback = 9f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            db.Buffs["slowed"] = new BuffDef { Id = "slowed", Name = "Slowed", Unit = DurationUnit.Turns, Duration = 2, TimelineSpeedMultiplier = 0.5f };
            return db;
        }

        private static Unit Wolf(BattleState s) => s.Units.First(u => u.Def.Id == "wolf" && u.Alive);

        private static System.Collections.Generic.List<BattleEvent> UseSkill(BattleState s, Unit hero, string id, Unit target)
        {
            hero.Bar[0] = new AbilitySlot { AbilityId = id };
            hero.Cooldowns.Clear();
            return BattleEngine.Apply(s, new UseSlotCommand { HeroId = hero.Id, Slot = 0, Target = target.Pos });
        }

        [Test]
        public void An_icon_sits_at_its_seconds_until_acting_over_the_bar()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var turn = s.TurnEntryOf(Wolf(s).Id);
            turn.Ip = BattleConfig.BarLength - BattleEngine.TurnRatePerTick(s, Wolf(s)) * 30; // 30 ticks out at the wolf's rate
            Assert.That(BattleEngine.SecondsUntilAct(s, turn), Is.EqualTo(3f).Within(1e-4));
            Assert.That(BattleEngine.BarU(s, turn), Is.EqualTo(3f / BattleConfig.BarSeconds).Within(1e-4));
            turn.Ip = BattleConfig.BarLength;
            Assert.That(BattleEngine.BarU(s, turn), Is.EqualTo(0f), "the left edge: acting now");
        }

        [Test]
        public void A_cast_is_born_at_its_cast_time_on_the_bar()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var mage = Hero(s, "mage");
            var heal4s = new TimelineEntry { Id = 600, Kind = TimelineKind.Cast, OwnerId = mage.Id, CasterTeam = Team.Hero, AbilityId = "heal", TotalCastTicks = 40, RemainingTicks = 40 };
            Assert.That(BattleEngine.SecondsUntilAct(s, heal4s), Is.EqualTo(4f).Within(1e-4));
            Assert.That(BattleEngine.BarU(s, heal4s), Is.EqualTo(4f / BattleConfig.BarSeconds).Within(1e-4), "Heal with a 4 s cast spawns at the 4 s mark");

            // A real cast: the entry the engine creates is placed by its cast time and then runs down with the clock.
            Wolf(s).Buffs.Add(new BuffInstance { Def = s.Content.Buffs["frozen"], Remaining = 1 }); // keep the wolf from acting
            mage.Bar[0] = new AbilitySlot { AbilityId = "meteor" };
            s.Bank.Add(2);
            long c0 = s.Clock;
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = mage.Id, Slot = 0, Target = new Coord(2, 0) });
            var queued = ev.OfType<CastQueuedEvent>().Single();
            var cast = s.Timeline.SingleOrDefault(e => e.Id == queued.EntryId);
            Assert.That(cast, Is.Not.Null, "the meteor is still in flight");
            Assert.That(cast.TotalCastTicks, Is.EqualTo(queued.Ticks));
            int elapsed = (int)(s.Clock - c0);
            Assert.That(BattleEngine.BarU(s, cast), Is.EqualTo((queued.Ticks - elapsed) / (BattleConfig.TicksPerSecond * BattleConfig.BarSeconds)).Within(1e-4));
        }

        [Test]
        public void Slow_on_an_enemy_lets_an_in_flight_cast_overtake_it()
        {
            int CastIndexMinusTurnIndex(bool slow)
            {
                var (s, _) = Battle(Db(), Stage(("wolf", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
                var wolf = Wolf(s);
                s.TurnEntryOf(wolf.Id).Ip = BattleConfig.BarLength - BattleEngine.TurnRatePerTick(s, wolf) * 35; // acts in 3.5 s
                s.Timeline.Add(new TimelineEntry { Id = 700, Kind = TimelineKind.Cast, OwnerId = Hero(s, "mage").Id, CasterTeam = Team.Hero, AbilityId = "zap", RemainingTicks = 40, TotalCastTicks = 40, TargetUnitId = wolf.Id, TargetTile = wolf.Pos });
                if (slow) wolf.Buffs.Add(new BuffInstance { Def = s.Content.Buffs["slowed"], Remaining = 2 });
                var turnEntry = s.TurnEntryOf(wolf.Id);
                var castEntry = s.Timeline.Single(e => e.Id == 700);
                Assert.That(BattleEngine.SecondsUntilAct(s, castEntry) < BattleEngine.SecondsUntilAct(s, turnEntry), Is.EqualTo(slow));
                var ev = BattleEngine.Apply(s, new ShieldCommand());
                int cast = ev.FindIndex(e => e is AbilityResolvedEvent r && r.AbilityId == "zap");
                int turn = ev.FindIndex(e => e is TurnStartedEvent);
                Assert.That(turn, Is.GreaterThanOrEqualTo(0));
                return cast < 0 ? int.MaxValue : cast - turn;
            }
            Assert.That(CastIndexMinusTurnIndex(slow: false), Is.GreaterThan(0), "unslowed, the wolf (3.5 s) beats the 4 s cast");
            Assert.That(CastIndexMinusTurnIndex(slow: true), Is.LessThan(0), "Slowed, the cast overtakes it");
        }

        [Test]
        public void A_plain_hit_never_pushes()
        {
            var (s, _) = Battle(Db(), Stage(("tank", 2, 3)), ("knight", 2, 4), ("cleric", 5, 7));
            ActsAfterAction(s, s.Units.Single(u => u.Def.Id == "tank")); // about to act: the old Zone's sweet spot (GG-T311: a skill still fits)
            var ev = UseSkill(s, Hero(s, "knight"), "jab", s.Units.Single(u => u.Def.Id == "tank"));
            Assert.That(ev.OfType<DamagedEvent>().Any(d => d.Amount > 0 || d.Hit != HitKind.Miss), Is.True);
            Assert.That(ev.OfType<PushedBackEvent>(), Is.Empty);
        }

        [Test]
        public void The_Pushback_keyword_pushes_its_seconds_anywhere_on_the_bar()
        {
            // GG-T311: ticks until it acts — a skill's use time (as close as an action fits), mid-bar, and far out (4 s, so a
            // 1.5 s push still lands on the 6 s bar, GG-T318).
            foreach (int ticksOut in new[] { 10, 20, 40 })
            {
                var db = Db();
                db.Classes["tank"].BaseStats.Speed = 5;
                var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 2, 4), ("cleric", 5, 7));
                var tank = s.Units.Single(u => u.Def.Id == "tank");
                s.TurnEntryOf(tank.Id).Ip = BattleConfig.BarLength - BattleEngine.TurnRatePerTick(s, tank) * ticksOut;
                int ip = s.TurnEntryOf(tank.Id).Ip;
                var push = UseSkill(s, Hero(s, "knight"), "shove_time", tank).OfType<PushedBackEvent>().Single();
                Assert.That(push.ToU - push.FromU, Is.EqualTo(1.5f / BattleConfig.BarSeconds).Within(1e-4), "ip " + ip);
            }
        }

        [Test]
        public void One_push_is_capped()
        {
            var db = Db();
            db.Classes["tank"].BaseStats.Speed = 5; // GG-T311: slow enough that a 2 s push from a skill's use time out doesn't clamp
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 2, 4), ("cleric", 5, 7));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            ActsAfterAction(s, tank);
            var push = UseSkill(s, Hero(s, "knight"), "big_shove", tank).OfType<PushedBackEvent>().Single();
            Assert.That(push.ToU - push.FromU, Is.EqualTo(BattleConfig.PushbackMaxSeconds / BattleConfig.BarSeconds).Within(1e-4));
        }

        [Test]
        public void Validate_rejects_a_pushback_past_the_cap()
        {
            var db = Db();
            var problems = ContentLoader.Validate(db);
            Assert.That(problems.Any(p => p.Contains("big_shove") && p.Contains("Pushback")), Is.True);
            Assert.That(problems.Any(p => p.Contains("shove_time")), Is.False);
        }

        [Test]
        public void Guard_still_banks_the_time_left_to_the_next_arrival()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            s.TurnEntryOf(Wolf(s).Id).Ip = 0; // a full linear lap: ceil(1000 / rate) ticks
            int rate = BattleEngine.TurnRatePerTick(s, Wolf(s));
            float window = (BattleConfig.BarLength + rate - 1) / rate / (float)BattleConfig.TicksPerSecond;
            Assert.That(BattleEngine.TurnWindowSeconds(s), Is.EqualTo(window).Within(1e-4));
            Assert.That(BattleEngine.GuardCharge(s, 1f), Is.EqualTo(BattleEngine.GuardChargeFor(window - 1f)));
        }
    }
}
