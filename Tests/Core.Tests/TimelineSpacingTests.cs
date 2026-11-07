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
    /// <summary>
    /// GG-T318 (BIBLE §2.1, §2.12): every icon flows at one rate. Speed sets where a foe re-enters the bar (an average foe
    /// 4 s out), and a recovery interval off the bar, stretched by the difficulty row's spacer, spreads foes apart.
    /// </summary>
    public class TimelineSpacingTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            db.Classes["avg"] = new ClassDef { Id = "avg", Name = "Avg", Team = Team.Enemy, Tags = { "Beast" }, BaseStats = new Stats { Strength = 5, Vitality = 2, Agility = 10, Speed = 11 }, MoveRange = 0 };
            db.Abilities["shove_time"] = new AbilityDef { Id = "shove_time", Name = "Shove Time", Kind = AbilityKind.Skill, BaseDamage = 1, Pushback = 1.5f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly, DamageType = DamageType.Physical };
            db.Abilities["heavy"] = new AbilityDef { Id = "heavy", Name = "Heavy", Kind = AbilityKind.Skill, RecoverSeconds = 3.5f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            return db;
        }

        private static Unit Of(BattleState s, string cls) => s.Units.First(u => u.Def.Id == cls && u.Alive);

        private static StageDef RealStage(params string[] classes)
        {
            var st = new StageDef { Id = "spacing", Name = "Spacing", IsTest = false };
            var wave = new WaveDef();
            for (int i = 0; i < classes.Length; i++) wave.Enemies.Add(new SpawnDef { Class = classes[i], Level = 1, X = i, Y = 0 });
            st.Waves.Add(wave);
            return st;
        }

        private static BattleState Real(int difficulty, int seed, params string[] classes)
        {
            var setup = new BattleSetup { Stage = RealStage(classes), Seed = seed, UnlockedSlots = 5, Difficulty = difficulty };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(5, 7) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7) });
            return BattleEngine.Create(Db(), setup, new List<BattleEvent>());
        }

        [Test]
        public void An_average_foe_re_enters_four_seconds_out_and_a_faster_one_further_left()
        {
            var (s, _) = Battle(Db(), Stage(("avg", 0, 0), ("wolf", 1, 0), ("witch", 2, 0), ("tank", 3, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Assert.That(BattleEngine.EntrySeconds(Of(s, "avg")), Is.EqualTo(BattleConfig.EntryBaseSeconds).Within(1e-4), "Speed 11 is the Lv1 average");
            Assert.That(BattleEngine.EntrySeconds(Of(s, "witch")), Is.LessThan(BattleEngine.EntrySeconds(Of(s, "wolf"))), "Speed 30 enters left of Speed 10");
            Assert.That(BattleEngine.EntrySeconds(Of(s, "wolf")), Is.GreaterThan(BattleConfig.EntryBaseSeconds), "Speed 10 is a touch slower than average");
            Assert.That(BattleEngine.EntrySeconds(Of(s, "tank")), Is.EqualTo(BattleConfig.EntryMaxSeconds), "Speed 1 clamps at the right end");
        }

        [Test]
        public void Entry_is_judged_against_the_level_so_a_fast_foe_stays_fast_at_every_level()
        {
            var (s, _) = Battle(Db(), Stage(("witch", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var witch = Of(s, "witch");
            float lv1 = BattleEngine.EntrySeconds(witch);
            witch.Level = 30;
            witch.LevelStats.Speed = 30f * Formulas.ReferenceEnemySpeed(30) / Formulas.ReferenceEnemySpeed(1); // the same lead over its peers
            Assert.That(BattleEngine.EntrySeconds(witch), Is.EqualTo(lv1).Within(0.05f));
        }

        [Test]
        public void Every_icon_flows_at_the_same_rate_whatever_its_Speed()
        {
            var (s, _) = Battle(Db(), Stage(("witch", 0, 0), ("tank", 1, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            Assert.That(BattleEngine.TurnRatePerTick(s, Of(s, "witch")), Is.EqualTo(BattleConfig.BaseIpPerTick));
            Assert.That(BattleEngine.TurnRatePerTick(s, Of(s, "tank")), Is.EqualTo(BattleConfig.BaseIpPerTick));
        }

        [Test]
        public void After_acting_a_foe_recovers_off_the_bar_then_re_enters_at_its_entry()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var wolf = Of(s, "wolf");
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.OfType<TurnStartedEvent>().Any(t => t.UnitId == wolf.Id), Is.True);
            var e = s.TurnEntryOf(wolf.Id);
            Assert.That(BattleEngine.InRecovery(e), Is.True);
            Assert.That(e.RecoverTicks, Is.EqualTo(BattleEngine.NeedTicks(BattleConfig.RecoverBaseSeconds)), "neutral row: base recovery, no spacing, no jitter");
            Assert.That(e.Ip, Is.EqualTo(BattleEngine.EntryIp(wolf)));
            int travel = (BattleConfig.BarLength - e.Ip + BattleConfig.BaseIpPerTick - 1) / BattleConfig.BaseIpPerTick;
            Assert.That(BattleEngine.TicksUntilAct(s, e), Is.EqualTo(e.RecoverTicks + travel), "recovery, then the trip from its entry");
        }

        [Test]
        public void Pushback_on_a_recovering_foe_extends_its_recovery_and_keeps_its_entry()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 2, 3)), ("knight", 2, 4), ("cleric", 5, 7));
            var wolf = Of(s, "wolf");
            BattleEngine.Apply(s, new ShieldCommand());
            var e = s.TurnEntryOf(wolf.Id);
            int before = e.RecoverTicks;
            long clock = s.Clock;
            var knight = Hero(s, "knight");
            knight.Bar[0] = new AbilitySlot { AbilityId = "shove_time" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = wolf.Pos });
            Assert.That(ev.OfType<PushedBackEvent>().Any(p => p.UnitId == wolf.Id), Is.True);
            Assert.That(e.Ip, Is.EqualTo(BattleEngine.EntryIp(wolf)), "its entry still reads its Speed");
            Assert.That(e.RecoverTicks, Is.EqualTo(before + 15 - (int)(s.Clock - clock)));
        }

        [Test]
        public void Validate_rejects_a_recovery_past_the_cap()
        {
            var problems = ContentLoader.Validate(Db());
            Assert.That(problems.Any(p => p.Contains("heavy") && p.Contains("RecoverSeconds")), Is.True);
            Assert.That(problems.Any(p => p.Contains("shove_time") && p.Contains("RecoverSeconds")), Is.False);
        }

        [Test]
        public void Shipped_heavy_charges_recover_longer_and_validate()
        {
            var real = TestKit.Real;
            Assert.That(ContentLoader.Validate(real).Where(p => p.Contains("RecoverSeconds")), Is.Empty);
            foreach (var id in new[] { "charge_fireball", "charge_temporal_drag", "charge_miasma", "charge_thunder", "charge_ice" })
                Assert.That(real.Abilities[id].RecoverSeconds, Is.GreaterThanOrEqualTo(1f), id);
            Assert.That(real.Abilities["charge_howl"].RecoverSeconds, Is.EqualTo(0f), "a wolf pack keeps its pressure");
        }

        [Test]
        public void A_heavy_charge_adds_its_recovery_to_the_casters_turn()
        {
            var db = Db();
            db.Abilities["charge_bolt"].RecoverSeconds = 1.5f;
            var (s, _) = Battle(db, Stage(("witch", 2, 0)), ("knight", 5, 7), ("cleric", 0, 7));
            var witch = Of(s, "witch");
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is CastQueuedEvent q && q.IsCharge), Is.True, "the witch winds up its charge");
            Assert.That(s.TurnEntryOf(witch.Id).RecoverTicks, Is.EqualTo(BattleEngine.NeedTicks(BattleConfig.RecoverBaseSeconds + 1.5f)));
        }

        // ── The spacer (pure) ────────────────────────────────────────────────────

        [Test]
        public void The_spacer_holds_a_foe_just_long_enough_to_clear_MinGap()
        {
            Assert.That(TimelineSpacing.Delay(40, new[] { 41 }, minGap: 10, maxSpread: 20), Is.EqualTo(11));
            Assert.That(TimelineSpacing.Delay(40, new[] { 20 }, minGap: 10, maxSpread: 20), Is.EqualTo(0), "already clear");
            Assert.That(TimelineSpacing.Delay(40, new int[0], minGap: 10, maxSpread: 20), Is.EqualTo(0), "nobody else");
            Assert.That(TimelineSpacing.Delay(40, new[] { 41 }, minGap: 0, maxSpread: 20), Is.EqualTo(0), "no spacing on this row");
        }

        [Test]
        public void The_spacer_never_holds_past_MaxSpread_and_a_crowded_bar_still_clusters()
        {
            Assert.That(TimelineSpacing.Delay(40, new[] { 41 }, minGap: 10, maxSpread: 5), Is.EqualTo(5), "the widest gap on offer");
            int d = TimelineSpacing.Delay(45, new[] { 35, 45, 55 }, minGap: 10, maxSpread: 4);
            Assert.That(d, Is.InRange(0, 4));
            Assert.That(TimelineSpacing.SmallestGap(45 + d, new[] { 35, 45, 55 }), Is.LessThan(10), "no room: a cluster happens");
        }

        // ── The dial ─────────────────────────────────────────────────────────────

        [Test]
        public void On_the_neutral_row_nothing_is_spaced()
        {
            var (s, _) = Battle(Db(), Stage(("wolf", 0, 0), ("wolf", 1, 0)), ("knight", 5, 7), ("cleric", 4, 7));
            var acts = s.Timeline.Select(e => BattleEngine.TicksUntilAct(s, e)).ToList();
            Assert.That(acts[0], Is.EqualTo(acts[1]), "two identical foes, no spacer: they arrive together");
        }

        [Test]
        public void At_difficulty_1_two_identical_foes_open_the_wave_at_least_MinGap_apart()
        {
            var s = Real(1, 7, "wolf", "wolf");
            var acts = s.Timeline.Where(e => e.Kind == TimelineKind.Turn).Select(e => BattleEngine.TicksUntilAct(s, e)).OrderBy(t => t).ToList();
            Assert.That(acts.Count, Is.EqualTo(2));
            int minGap = (int)System.Math.Round(DifficultyTable.At(1).MinGapSeconds * BattleConfig.TicksPerSecond);
            Assert.That(acts[1] - acts[0], Is.GreaterThanOrEqualTo(minGap));
            Assert.That(acts[0], Is.GreaterThanOrEqualTo(BattleEngine.NeedTicks(BattleConfig.OpeningWindowSeconds)), "heroes still act first");
        }

        [Test]
        public void Spaced_schedules_replay_identically_from_the_same_seed()
        {
            string Snapshot(BattleState st) => string.Join(";", st.Timeline.Select(e => $"{e.Id}:{e.Ip}:{e.RecoverTicks}"));
            var a = Real(10, 11, "wolf", "wolf", "witch", "avg");
            var b = Real(10, 11, "wolf", "wolf", "witch", "avg");
            Assert.That(Snapshot(a), Is.EqualTo(Snapshot(b)));
            for (int i = 0; i < 6 && !a.IsOver; i++) { BattleEngine.Apply(a, new ShieldCommand()); BattleEngine.Apply(b, new ShieldCommand()); }
            Assert.That(Snapshot(a), Is.EqualTo(Snapshot(b)));
        }

        [Test] // GG-T321
        public void Foes_grow_with_every_hero_fielded_beyond_the_starting_three()
        {
            BattleState WithParty(params string[] heroes)
            {
                var setup = new BattleSetup { Stage = RealStage("wolf"), Seed = 3, UnlockedSlots = 5, Difficulty = 10 };
                for (int i = 0; i < heroes.Length; i++) setup.Party.Add(new HeroSpec { ClassId = heroes[i], Start = new Coord(i, 7) });
                return BattleEngine.Create(Db(), setup, new List<BattleEvent>());
            }
            var three = WithParty("knight", "cleric", "mage");
            var five = WithParty("knight", "cleric", "mage", "knight", "cleric");
            Assert.That(BattleEngine.PartyScale(three), Is.EqualTo(1f), "the starting three fight foes as designed");
            Assert.That(BattleEngine.PartyScale(five), Is.EqualTo(1f + 2 * BattleConfig.EnemyScalePerExtraHero).Within(1e-5));
            Assert.That(Of(five, "wolf").MaxHp, Is.GreaterThan(Of(three, "wolf").MaxHp));
            var (test, _) = Battle(Db(), Stage(("wolf", 0, 0)), ("knight", 0, 7), ("cleric", 1, 7), ("mage", 2, 7), ("knight", 3, 7), ("cleric", 4, 7));
            Assert.That(BattleEngine.PartyScale(test), Is.EqualTo(1f), "test stages stay unscaled");
        }

        [Test]
        public void The_dial_s_spacing_columns_only_tighten_as_the_level_rises()
        {
            Assert.That(DifficultyTable.Problems(), Is.Empty);
            Assert.That(DifficultyTable.At(DifficultyTable.Neutral).MinGapSeconds, Is.EqualTo(0f));
            Assert.That(DifficultyTable.At(1).MinGapSeconds, Is.GreaterThan(DifficultyTable.At(10).MinGapSeconds));
        }
    }
}
