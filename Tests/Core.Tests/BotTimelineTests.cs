using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T292: the smart HeroBot reads the bar (BIBLE §2). With a lethal hero cast in flight that lands just after a foe
    /// acts, it spends the turn on a Pushback or a Slow so the cast fires first and the foe never acts; an unthinking
    /// (casual) bot doesn't price that.
    /// </summary>
    public class BotTimelineTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            db.Abilities["bolt"] = new AbilityDef { Id = "bolt", Name = "Bolt", Kind = AbilityKind.Spell, BaseDamage = 999, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Arcane };
            db.Abilities["shove_time"] = new AbilityDef { Id = "shove_time", Name = "Shove Time", Kind = AbilityKind.Skill, BaseDamage = 1, Pushback = 1f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            db.Abilities["hobble"] = new AbilityDef { Id = "hobble", Name = "Hobble", Kind = AbilityKind.Skill, ApplyBuff = "slowed", ApplyBuffChance = 1f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            db.Buffs["slowed"] = new BuffDef { Id = "slowed", Name = "Slowed", Unit = DurationUnit.Turns, Duration = 2, TimelineSpeedMultiplier = 0.5f };
            return db;
        }

        /// <summary>A wolf that acts in 1.0 s, a lethal bolt landing in 1.5 s, and the knight holding <paramref name="skill"/>.</summary>
        private static (BattleState s, Unit wolf, Unit knight) Race(string skill)
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 2, 0)), ("knight", 5, 7), ("mage", 4, 7));
            var wolf = s.Units.First(u => u.Def.Id == "wolf");
            s.TurnEntryOf(wolf.Id).Ip = BattleConfig.BarLength - BattleEngine.TurnRatePerTick(s, wolf) * 10; // acts in 1.0 s
            s.Timeline.Add(new TimelineEntry { Id = 900, Kind = TimelineKind.Cast, OwnerId = Hero(s, "mage").Id, CasterTeam = Team.Hero, AbilityId = "bolt", TotalCastTicks = 15, RemainingTicks = 15, TargetUnitId = wolf.Id, TargetTile = wolf.Pos });
            var knight = Hero(s, "knight");
            knight.Bar[0] = new AbilitySlot { AbilityId = skill };
            return (s, wolf, knight);
        }

        [TestCase("shove_time")]
        [TestCase("hobble")]
        public void The_smart_bot_delays_a_foe_so_a_lethal_cast_lands_first(string skill)
        {
            var (s, wolf, knight) = Race(skill);
            Assert.That(BattleEngine.SecondsUntilAct(s, s.TurnEntryOf(wolf.Id)), Is.LessThan(1.5f), "unaided, the wolf acts before the bolt lands");

            var cmd = new HeroBot(1).Decide(s);
            Assert.That(cmd, Is.TypeOf<UseSlotCommand>(), "the smart bot spends its turn on the bar, not a move");
            var use = (UseSlotCommand)cmd;
            Assert.That(use.HeroId, Is.EqualTo(knight.Id));
            Assert.That(use.Target, Is.EqualTo(wolf.Pos));

            var ev = BattleEngine.Apply(s, cmd);
            int landed = ev.FindIndex(e => e is AbilityResolvedEvent r && r.AbilityId == "bolt");
            Assert.That(landed, Is.GreaterThanOrEqualTo(0), "the bolt landed");
            Assert.That(wolf.Alive, Is.False, "the bolt killed the wolf");
            Assert.That(ev.Take(landed).OfType<TurnStartedEvent>().Any(t => t.UnitId == wolf.Id), Is.False, "the wolf never acted first");
        }

        [Test]
        public void Only_the_smart_bot_reads_the_bar()
        {
            Assert.That(new HeroBot(1).TimelineAware, Is.True);
            Assert.That(new HeroBot(1, randomness: 0.5f).TimelineAware, Is.False, "the casual bot stays naive");
        }
    }
}
