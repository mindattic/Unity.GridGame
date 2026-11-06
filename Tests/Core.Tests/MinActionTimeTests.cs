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
    /// GG-T311 (§2.0), owner: "Heroes go first in the round, but unless they can get in a pincer between the intervals
    /// between enemies hitting left they have to sacrifice their turn, and that could lead to several enemy attacks in a
    /// row before a window wide enough for the player to act becomes available." An action starts only in a gap at least
    /// as long as it takes (a drag: pickup + a tile; a skill: its use time); with none, the party can only Guard.
    /// </summary>
    public class MinActionTimeTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            db.Classes["hare"] = new ClassDef { Id = "hare", Name = "Hare", Team = Team.Enemy, Tags = { "Beast" }, BaseStats = new Stats { Strength = 5, Vitality = 2, Agility = 10, Speed = 20, Intelligence = 10 }, MoveRange = 0 };
            db.Abilities["jab"] = new AbilityDef { Id = "jab", Name = "Jab", Kind = AbilityKind.Skill, BaseDamage = 1, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            db.Abilities["shove_time"] = new AbilityDef { Id = "shove_time", Name = "Shove Time", Kind = AbilityKind.Skill, BaseDamage = 1, Pushback = 1.5f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            return db;
        }

        /// <summary>Puts <paramref name="u"/> <paramref name="ticks"/> clock ticks from acting (Speed 10 = 16 IP a tick).</summary>
        private static void In(BattleState s, Unit u, int ticks) => s.TurnEntryOf(u.Id).Ip = BattleConfig.BarLength - BattleEngine.TurnRatePerTick(s, u) * ticks;

        private static List<Unit> Wolves(BattleState s) => s.Units.Where(u => u.Def.Id == "wolf" && u.Alive).OrderBy(u => u.Id).ToList();

        private static List<BattleEvent> Step(BattleState s, Unit h, float held) =>
            BattleEngine.Apply(s, new MoveCommand { HeroId = h.Id, Path = new List<Coord> { new Coord(h.Pos.X, h.Pos.Y == 7 ? 6 : h.Pos.Y + 1) }, HeldSeconds = held });

        private static List<BattleEvent> Use(BattleState s, Unit hero, string id, Unit target)
        {
            hero.Bar[0] = new AbilitySlot { AbilityId = id };
            hero.Cooldowns.Clear();
            return BattleEngine.Apply(s, new UseSlotCommand { HeroId = hero.Id, Slot = 0, Target = target.Pos });
        }

        /// <summary>The opening window: OpeningWindowSeconds, or a full lap from the start of the bar for a foe too fast for that.</summary>
        private static int Opening(BattleState s, int need)
        {
            int lap = int.MaxValue;
            foreach (var e in s.LivingList(Team.Enemy))
            {
                int rate = BattleEngine.TurnRatePerTick(s, e);
                lap = System.Math.Min(lap, (BattleConfig.BarLength + rate - 1) / rate);
            }
            return System.Math.Min(need, lap);
        }

        [Test]
        public void Heroes_act_first_in_every_wave()
        {
            int need = BattleEngine.NeedTicks(BattleConfig.OpeningWindowSeconds);
            for (int seed = 1; seed <= 12; seed++)
            {
                var setup = new BattleSetup { Stage = StageWaves(new[] { ("hare", 0, 0), ("hare", 2, 0), ("wolf", 4, 0) }, new[] { ("hare", 1, 0), ("hare", 3, 0) }), Seed = seed, UnlockedSlots = 5 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 7) });
                var s = BattleEngine.Create(Db(), setup, new List<BattleEvent>());
                Assert.That(BattleEngine.TurnTicks(s), Is.GreaterThanOrEqualTo(Opening(s, need)), $"seed {seed}: the party opens the battle");
                Assert.That(BattleEngine.TooLittleTime(s), Is.False);
                Assert.That(BattleEngine.HeroesLeftToAct(s).Count, Is.EqualTo(2));

                // Clear the first wave: the second opens with the party's window too.
                foreach (var e in s.LivingList(Team.Enemy).ToList()) e.Hp = 1;
                for (int i = 0; i < 40 && s.Wave == 0 && !s.IsOver; i++)
                {
                    var k = Hero(s, "knight");
                    var foe = s.LivingList(Team.Enemy).First();
                    var ev = BattleEngine.TooLittleTime(s) ? BattleEngine.Apply(s, new ShieldCommand())
                        : k.ActedThisWindow ? BattleEngine.Apply(s, new ShieldCommand()) : Use(s, k, "jab", foe);
                    if (s.Wave == 1)
                    {
                        Assert.That(ev.OfType<WaveStartedEvent>().Any(), Is.True);
                        Assert.That(BattleEngine.TurnTicks(s), Is.GreaterThanOrEqualTo(Opening(s, need)), $"seed {seed}: wave 2 opens on the party");
                        Assert.That(BattleEngine.TooLittleTime(s), Is.False);
                    }
                }
            }
        }

        [Test]
        public void An_action_is_refused_when_the_gap_is_shorter_than_its_minimum()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            var wolf = Wolves(s)[0];
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "jab" };
            long clock = s.Clock;

            // 0.9 s: a minimum drag (0.5 pickup + 0.35 a tile) fits, a 1 s skill doesn't.
            In(s, wolf, 9);
            Assert.That(BattleEngine.CanMove(s, k.Id), Is.True);
            Assert.That(BattleEngine.GetSlotStatus(s, k.Id, 0), Is.EqualTo(SlotStatus.TooLittleTime));
            var skill = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = wolf.Pos });
            Assert.That(skill.OfType<AbilityRefusedEvent>().Single().Reason, Is.EqualTo("Too little time"));
            Assert.That(BattleEngine.TooLittleTime(s), Is.False, "a drag still fits");

            // 0.8 s: nothing fits — the drag is refused too, and the party can only Guard.
            In(s, wolf, 8);
            Assert.That(BattleEngine.CanMove(s, k.Id), Is.False);
            Assert.That(BattleEngine.TooLittleTime(s), Is.True);
            var drag = Step(s, k, 0.5f);
            Assert.That(drag.OfType<AbilityRefusedEvent>().Single().Reason, Is.EqualTo("Too little time"));
            Assert.That(s.Clock, Is.EqualTo(clock), "a refused action costs nothing");
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(0));
            Assert.That(new HeroBot(3).Decide(s), Is.InstanceOf<ShieldCommand>(), "the smart bot Guards");
            Assert.That(new HeroBot(3, 0.5f).Decide(s), Is.InstanceOf<ShieldCommand>(), "so does the casual bot");

            // A drag spends at least its minimum, however fast the hand.
            In(s, wolf, 30);
            Assert.That(BattleEngine.CommandTicks(s, new MoveCommand { HeroId = k.Id, HeldSeconds = 0.05f }), Is.EqualTo(9));
        }

        [Test]
        public void Two_enemies_close_together_force_two_sacrifices_in_a_row()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0), ("wolf", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            var w = Wolves(s);
            In(s, w[0], 5);
            In(s, w[1], 8); // 0.3 s behind the first
            s.ManaCharge = 0;
            Assert.That(BattleEngine.TooLittleTime(s), Is.True);

            // The forced Guard still shields everyone and banks the sliver of time it gave up.
            int expected = BattleEngine.DampedCharge(s, BattleEngine.GuardChargeFor(0.5f));
            var first = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(first.OfType<TurnStartedEvent>().Select(t => t.UnitId), Is.EqualTo(new[] { w[0].Id }));
            Assert.That(first.OfType<ManaChargedEvent>().Single(c => c.Reason == "guard").Amount, Is.EqualTo(expected));
            var shielded = first.OfType<BuffAppliedEvent>().Where(b => b.BuffId == BattleConfig.ShieldBuffId).Select(b => b.UnitId).ToList();
            Assert.That(s.LivingList(Team.Hero).All(h => shielded.Contains(h.Id)), Is.True, "Protection covered the enemy's blow");
            Assert.That(first.Last() is PlayerControlEvent pc && pc.NewWindow, Is.True);

            // The next gap is 0.3 s: too short again — a second enemy acts before any hero has.
            Assert.That(BattleEngine.TurnTicks(s), Is.EqualTo(3));
            Assert.That(BattleEngine.TooLittleTime(s), Is.True);
            var second = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(second.OfType<TurnStartedEvent>().Select(t => t.UnitId), Is.EqualTo(new[] { w[1].Id }));
            Assert.That(s.Results.ActionsTaken, Is.EqualTo(2), "two Guards, no hero action");

            // A full lap later the party has room again.
            Assert.That(BattleEngine.TooLittleTime(s), Is.False);
            AssertInvariants(s);
        }

        [Test]
        public void Rally_breaks_a_lock_of_gaps_all_too_short()
        {
            // Four equal wolves 0.8 s apart lap in 3.2 s: every gap is too short for any action, for ever — unless the
            // forced Guards rally the party and slow them.
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0), ("wolf", 2, 0), ("wolf", 4, 0), ("wolf", 5, 1)), ("knight", 5, 7), ("cleric", 3, 7));
            var w = Wolves(s);
            int lap = (BattleConfig.BarLength + BattleEngine.TurnRatePerTick(s, w[0]) - 1) / BattleEngine.TurnRatePerTick(s, w[0]);
            for (int i = 0; i < 4; i++) In(s, w[i], (i + 1) * lap / 4);
            Assert.That(lap / 4, Is.LessThan(BattleEngine.NeedTicks(BattleEngine.MinDragSeconds)), "every gap is too short");
            Assert.That(BattleEngine.TooLittleTime(s), Is.True);
            int guards = 0;
            while (BattleEngine.TooLittleTime(s) && guards < 12 && !s.IsOver) { BattleEngine.Apply(s, new ShieldCommand()); guards++; }
            Assert.That(BattleEngine.TooLittleTime(s), Is.False, "the rally opened a gap");
            Assert.That(s.Rally, Is.InRange(1, BattleConfig.RallyMaxStacks));
            Assert.That(guards, Is.GreaterThanOrEqualTo(1), "an enemy turn went by first");
            Assert.That(BattleEngine.EnemyTempoMultiplier(s), Is.LessThan(1f));

            var k = Hero(s, "knight");
            var move = Step(s, k, BattleEngine.MinDragSeconds);
            Assert.That(move.OfType<AbilityRefusedEvent>(), Is.Empty);
            Assert.That(s.Rally, Is.EqualTo(0), "a hero action ends the rally");
        }

        [Test]
        public void Pushback_opens_a_gap_that_then_allows_an_action()
        {
            // Without a push: the knight's skill runs the window out, the first wolf acts, and the second is 0.3 s behind.
            var (plain, _) = Battle(Db(), Stage(("wolf", 0, 0), ("wolf", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            var pw = Wolves(plain);
            In(plain, pw[0], 10);
            In(plain, pw[1], 13);
            var jab = Use(plain, Hero(plain, "knight"), "jab", pw[1]);
            Assert.That(jab.OfType<TurnStartedEvent>().Select(t => t.UnitId), Is.EqualTo(new[] { pw[0].Id }));
            Assert.That(BattleEngine.TooLittleTime(plain), Is.True, "the cluster leaves no room: the party must Guard");

            // With a push on the second wolf, the gap after the first one acts is 1.8 s: the party acts.
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0), ("wolf", 2, 0)), ("knight", 5, 7), ("cleric", 3, 7));
            var w = Wolves(s);
            In(s, w[0], 10);
            In(s, w[1], 13);
            var shove = Use(s, Hero(s, "knight"), "shove_time", w[1]);
            Assert.That(shove.OfType<PushedBackEvent>().Any(), Is.True);
            Assert.That(shove.OfType<TurnStartedEvent>().Select(t => t.UnitId), Is.EqualTo(new[] { w[0].Id }));
            Assert.That(BattleEngine.TurnTicks(s), Is.InRange(13 + 15 - 10, 13 + 15 - 10 + 1)); // a push past IP 0 rounds its stun up a tick
            Assert.That(BattleEngine.TooLittleTime(s), Is.False);
            var move = Step(s, Hero(s, "cleric"), 1f);
            Assert.That(move.OfType<AbilityRefusedEvent>(), Is.Empty);
            Assert.That(move.OfType<ActionBeganEvent>().Any(a => a.ActorId == Hero(s, "cleric").Id), Is.True);
        }

        [Test]
        public void Bots_never_act_in_a_gap_too_short_and_replay_identically()
        {
            List<string> Run(float randomness)
            {
                var (s, _) = Battle(Db(), Stage(("hare", 0, 0), ("hare", 2, 0), ("wolf", 4, 0), ("goblin", 1, 2)), ("knight", 5, 7), ("cleric", 3, 7), ("mage", 1, 7));
                var bot = new HeroBot(17, randomness);
                var log = new List<string>();
                int guards = 0;
                for (int i = 0; i < 60 && !s.IsOver; i++)
                {
                    bool tight = BattleEngine.TooLittleTime(s);
                    var cmd = bot.Decide(s);
                    if (tight) Assert.That(cmd, Is.InstanceOf<ShieldCommand>(), "a gap with no room is a forced Guard");
                    if (cmd is ShieldCommand) guards++;
                    float need = BattleEngine.MinActionSeconds(cmd);
                    Assert.That(BattleEngine.ActionFits(s, need), Is.True, $"{cmd} in a {BattleEngine.TurnTicks(s)}-tick gap");
                    if (cmd is MoveCommand m && m.Path.Count > 0)
                        Assert.That(m.HeldSeconds, Is.GreaterThanOrEqualTo(System.Math.Min(BattleEngine.DragSeconds(m.Path.Count), BattleEngine.TurnWindowSeconds(s)) - 1e-4f), "no zero-time drags");
                    var ev = BattleEngine.Apply(s, cmd);
                    Assert.That(ev.OfType<AbilityRefusedEvent>().Where(r => r.Reason == "Too little time"), Is.Empty);
                    log.Add(cmd + " @" + s.Clock + " | " + string.Join(";", ev));
                    AssertInvariants(s);
                }
                log.Add("guards " + guards);
                return log;
            }
            Assert.That(Run(0f), Is.EqualTo(Run(0f)));
            Assert.That(Run(0.5f), Is.EqualTo(Run(0.5f)));
        }
    }
}
