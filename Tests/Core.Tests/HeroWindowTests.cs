using System.Collections.Generic;
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
    /// GG-T308 (§2.0): the party's window. The time until the next enemy reaches the left of the bar is a budget the
    /// party shares: each able hero acts once in it, a drag spends the share of the window it was held and an
    /// Ability/Item/Spell spends HeroActionSeconds, and a Pushback or Slow lengthens it — buying the others time.
    /// </summary>
    public class HeroWindowTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            db.Abilities["jab"] = new AbilityDef { Id = "jab", Name = "Jab", Kind = AbilityKind.Skill, BaseDamage = 1, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            db.Abilities["shove_time"] = new AbilityDef { Id = "shove_time", Name = "Shove Time", Kind = AbilityKind.Skill, BaseDamage = 1, Pushback = 1.5f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            return db;
        }

        private static Unit Wolf(BattleState s) => s.Units.First(u => u.Def.Id == "wolf" && u.Alive);

        /// <summary>Puts the wolf <paramref name="ticks"/> clock ticks from acting (Speed 10 = 16 IP a tick).</summary>
        private static void WolfIn(BattleState s, int ticks) => s.TurnEntryOf(Wolf(s).Id).Ip = BattleConfig.BarLength - BattleEngine.TurnRatePerTick(s, Wolf(s)) * ticks;

        /// <summary>A one-tile drag held for <paramref name="held"/> real seconds.</summary>
        private static List<BattleEvent> Step(BattleState s, Unit h, float held)
        {
            var to = new Coord(h.Pos.X, h.Pos.Y == 7 ? 6 : h.Pos.Y + 1);
            return BattleEngine.Apply(s, new MoveCommand { HeroId = h.Id, Path = new List<Coord> { to }, HeldSeconds = held });
        }

        private static List<BattleEvent> Use(BattleState s, Unit hero, string id, Unit target)
        {
            hero.Bar[0] = new AbilitySlot { AbilityId = id };
            hero.Cooldowns.Clear();
            return BattleEngine.Apply(s, new UseSlotCommand { HeroId = hero.Id, Slot = 0, Target = target.Pos });
        }

        private static bool SameWindow(List<BattleEvent> ev) => ev.Last() is PlayerControlEvent pc && !pc.NewWindow;

        [Test]
        public void Two_heroes_both_act_before_a_slow_enemy()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            WolfIn(s, 30); // a 3 s window
            long c0 = s.Clock;
            var k = Hero(s, "knight");
            var c = Hero(s, "cleric");

            var first = Step(s, k, 1f); // a third of the window: 10 ticks
            Assert.That(first.OfType<TurnStartedEvent>(), Is.Empty, "the wolf hasn't reached the left edge yet");
            Assert.That(SameWindow(first), Is.True);
            Assert.That(s.Clock, Is.EqualTo(c0 + 10));
            Assert.That(BattleEngine.TurnTicks(s), Is.EqualTo(20), "the rest of the window is the cleric's");
            Assert.That(k.ActedThisWindow, Is.True);
            Assert.That(BattleEngine.CanMove(s, k.Id), Is.False);
            Assert.That(BattleEngine.CanMove(s, c.Id), Is.True);
            Assert.That(BattleEngine.HeroesLeftToAct(s).Select(h => h.Id), Is.EqualTo(new[] { c.Id }));

            // The last hero to act ends the window: the clock runs on to the wolf, and everyone may act again.
            var second = Step(s, c, 0.5f);
            Assert.That(second.OfType<TurnStartedEvent>().Single().UnitId, Is.EqualTo(Wolf(s).Id));
            Assert.That(second.Last() is PlayerControlEvent pc && pc.NewWindow, Is.True);
            Assert.That(s.Clock, Is.EqualTo(c0 + 30));
            Assert.That(k.ActedThisWindow || c.ActedThisWindow, Is.False);
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(2));
            AssertInvariants(s);
        }

        [Test]
        public void Pushback_by_one_hero_lets_another_act_before_the_enemy()
        {
            // A plain skill spends HeroActionSeconds (1 s) of a 1 s window: the wolf acts before the cleric can.
            var (plain, _) = Battle(Db(), Stage(("wolf", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            WolfIn(plain, 10);
            var jab = Use(plain, Hero(plain, "knight"), "jab", Wolf(plain));
            Assert.That(jab.OfType<TurnStartedEvent>().Any(e => e.UnitId == Wolf(plain).Id), Is.True);
            Assert.That(jab.Last() is PlayerControlEvent pj && pj.NewWindow, Is.True);

            // The same skill with 1.5 s of Pushback: the window grows to 2.5 s, the skill spends 1 s, the cleric moves first.
            var (s, _) = Battle(Db(), Stage(("wolf", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            WolfIn(s, 10);
            var shove = Use(s, Hero(s, "knight"), "shove_time", Wolf(s));
            Assert.That(shove.OfType<PushedBackEvent>().Any(), Is.True);
            Assert.That(shove.OfType<TurnStartedEvent>(), Is.Empty, "the push bought the party time");
            Assert.That(SameWindow(shove), Is.True);
            Assert.That(BattleEngine.TurnTicks(s), Is.InRange(10 + 15 - 10, 10 + 15 - 10 + 1)); // a push past IP 0 rounds its stun up a tick
            var c = Hero(s, "cleric");
            var move = Step(s, c, 0.9f);
            Assert.That(move.OfType<AbilityRefusedEvent>(), Is.Empty);
            int wolfTurn = move.FindIndex(e => e is TurnStartedEvent t && t.UnitId == Wolf(s).Id);
            int cleric = move.FindIndex(e => e is ActionBeganEvent a && a.ActorId == c.Id);
            Assert.That(cleric, Is.GreaterThanOrEqualTo(0));
            Assert.That(wolfTurn, Is.GreaterThan(cleric), "the cleric acted before the wolf");
        }

        [Test]
        public void A_hero_cannot_act_twice_in_a_window()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            WolfIn(s, 30);
            var k = Hero(s, "knight");
            Step(s, k, 0.3f);
            long clock = s.Clock;
            int actions = s.Results.ActionsTaken;

            var again = Step(s, k, 0.3f);
            Assert.That(again.OfType<AbilityRefusedEvent>().Single().Reason, Is.EqualTo("Already acted"));
            Assert.That(again.OfType<PlayerControlEvent>(), Is.Empty);
            k.Bar[0] = new AbilitySlot { AbilityId = "jab" };
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 0), Is.EqualTo(SlotStatus.Acted));
            var skill = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = Wolf(s).Pos });
            Assert.That(skill.OfType<AbilityRefusedEvent>().Any(), Is.True);
            Assert.That(s.Clock, Is.EqualTo(clock), "a refused command costs nothing");
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(actions));
        }

        [Test]
        public void Guard_ends_the_window_and_banks_the_time_left()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 3, 7), ("mage", 1, 7));
            WolfIn(s, 30);
            s.ManaCharge = 0;
            Step(s, Hero(s, "knight"), 1f); // 10 of 30 ticks spent: 2 s left
            Assert.That(BattleEngine.GuardCharge(s), Is.EqualTo(BattleEngine.GuardChargeFor(2f)));
            int expected = BattleEngine.DampedCharge(s, BattleEngine.GuardChargeFor(2f));

            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "guard").Amount, Is.EqualTo(expected));
            Assert.That(ev.OfType<TurnStartedEvent>().Any(), Is.True, "the cleric and mage gave up their actions: the wolf acts");
            Assert.That(ev.Last() is PlayerControlEvent pc && pc.NewWindow, Is.True);
            Assert.That(s.Units.Any(u => u.IsHero && u.ActedThisWindow), Is.False);
        }

        [Test]
        public void A_forced_drop_mid_window_spends_the_rest_of_it()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 3, 7), ("mage", 1, 7));
            WolfIn(s, 30);
            long c0 = s.Clock;
            Step(s, Hero(s, "knight"), 1f); // 10 ticks
            var c = Hero(s, "cleric");
            var start = c.Pos;
            float left = BattleEngine.TurnWindowSeconds(s);
            Assert.That(left, Is.EqualTo(2f).Within(1e-4));

            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = c.Id, HeldSeconds = left });
            Assert.That(ev.OfType<TurnTimeUpEvent>().Single().HeroId, Is.EqualTo(c.Id));
            Assert.That(c.Pos, Is.EqualTo(start), "dropped where it stood");
            Assert.That(ev.OfType<TurnStartedEvent>().Any(), Is.True, "the window ran out: the wolf acts though the mage never did");
            Assert.That(s.Clock, Is.EqualTo(c0 + 30));
            Assert.That(ev.Last() is PlayerControlEvent pc && pc.NewWindow, Is.True);
        }

        [Test]
        public void A_drag_spends_its_share_of_the_window_and_a_skill_spends_HeroActionSeconds()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            WolfIn(s, 30);
            Assert.That(BattleEngine.HoldTicks(s, 1.5f), Is.EqualTo(15));
            Assert.That(BattleEngine.HoldTicks(s, 3f), Is.EqualTo(30), "a forced drop spends it all");
            Assert.That(BattleEngine.HoldTicks(s, 0f), Is.EqualTo(1), "a drag always costs a tick");
            Assert.That(BattleEngine.CommandTicks(s, new UseSlotCommand()), Is.EqualTo((int)System.Math.Round(BattleConfig.HeroActionSeconds * BattleConfig.TicksPerSecond)));
            Assert.That(BattleEngine.CommandTicks(s, new ShieldCommand()), Is.EqualTo(0));

            // GG-T311: no real-time floor — a 0.4 s gap is 0.4 s of hold; a drag never spends less than pickup + a tile.
            WolfIn(s, 4);
            Assert.That(BattleEngine.TurnWindowSeconds(s), Is.EqualTo(0.4f).Within(1e-4));
            Assert.That(BattleEngine.HoldTicks(s, 0.2f), Is.EqualTo(2));
            WolfIn(s, 30);
            Assert.That(BattleEngine.CommandTicks(s, new MoveCommand { HeldSeconds = 0.1f }), Is.EqualTo(BattleEngine.NeedTicks(BattleEngine.MinDragSeconds)));
        }

        [Test]
        public void Windows_replay_identically_from_the_same_commands()
        {
            List<string> Run()
            {
                var (s, _) = Battle(Db(), Stage(("wolf", 0, 0), ("goblin", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7), ("mage", 1, 7));
                var bot = new HeroBot(11);
                var log = new List<string>();
                for (int i = 0; i < 40 && !s.IsOver; i++)
                {
                    var cmd = bot.Decide(s);
                    var ev = BattleEngine.Apply(s, cmd);
                    log.Add(cmd + " @" + s.Clock + " | " + string.Join(";", ev));
                }
                return log;
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }

        [Test]
        public void The_smart_bot_takes_several_actions_in_one_window()
        {
            int sameWindow = 0, windows = 0;
            var (s, _) = Battle(Db(), Stage(("goblin", 2, 3), ("tank", 5, 0)), ("knight", 1, 3), ("cleric", 3, 5), ("mage", 5, 5));
            var bot = new HeroBot(5);
            Assert.That(bot.TimelineAware, Is.True);
            for (int i = 0; i < 30 && !s.IsOver; i++)
            {
                var ev = BattleEngine.Apply(s, bot.Decide(s));
                foreach (var pc in ev.OfType<PlayerControlEvent>()) { if (pc.NewWindow) windows++; else sameWindow++; }
                AssertInvariants(s);
            }
            Assert.That(sameWindow, Is.GreaterThan(0), "more than one hero acted before an enemy did");
            Assert.That(windows, Is.GreaterThan(0));

            // The casual bot still plays (naive about the bar, but it never stalls on a spent hero).
            var (c, _) = Battle(Db(), Stage(("wolf", 0, 0), ("goblin", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            var casual = new HeroBot(9, 0.5f);
            for (int i = 0; i < 30 && !c.IsOver; i++)
            {
                var ev = BattleEngine.Apply(c, casual.Decide(c));
                Assert.That(ev.Exists(e => e is PlayerControlEvent || e is BattleEndedEvent), Is.True, string.Join(";", ev));
            }
        }
    }
}
