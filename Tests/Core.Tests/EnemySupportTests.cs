using System;
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
    /// GG-T307 (§1.2.3): every monster brings its own support. Any enemy chained to a Sentient pincer attacker — Sentient
    /// or not — does its class's flavour (heal, buff, status, spell echo, shove, haste, crit) instead of the 50% strike.
    /// Mana is the party's meter, so it stays hero-only.
    /// </summary>
    public class EnemySupportTests
    {
        /// <summary>Two Sentient frogs flank the knight on row 3 — (0,3) frog, (1,3) knight, (2,3) frog — and a
        /// non-Sentient "helper" on (0,4) stands chained to the (0,3) frog with <paramref name="support"/>. The (0,3) frog's
        /// turn comes up and it closes the pincer.</summary>
        private static (BattleState s, List<BattleEvent> ev) EnemyPincer(SupportDef support, Action<ContentDb> tweak = null, Action<BattleState> before = null, int seed = 7)
        {
            var db = Mini();
            var st = new Stats { Strength = 5, Vitality = 2, Agility = 10, Speed = 10, Intelligence = 10 };
            db.Classes["frog"] = new ClassDef { Id = "frog", Name = "Frog", Team = Team.Enemy, Tags = { "Aquatic", "Sentient" }, BaseStats = st, MoveRange = 0 };
            db.Classes["helper"] = new ClassDef
            {
                Id = "helper", Name = "Helper", Team = Team.Enemy, Tags = { "Beast" }, MoveRange = 0, Support = support,
                BaseStats = new Stats { Strength = 5, Vitality = 2, Agility = 10, Speed = 10, Intelligence = 20 },
            };
            db.Buffs["poisoned"] = new BuffDef { Id = "poisoned", Name = "Poisoned", Kind = BuffKind.Debuff, Unit = DurationUnit.Ticks, Duration = 6, DamagePerTick = 1 };
            tweak?.Invoke(db);
            var setup = new BattleSetup { Stage = Stage(("frog", 0, 3), ("frog", 2, 3), ("helper", 0, 4)), Seed = seed, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            s.TurnEntryOf(FrogA(s).Id).Ip = BattleConfig.BarLength - 1;
            before?.Invoke(s);
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is PincerFormedEvent p && p.Team == Team.Enemy && p.Supporters.Contains(Helper(s).Id)), Is.True, Trace(ev));
            return (s, ev);
        }

        private static Unit FrogA(BattleState s) => At(s, 0, 3);
        private static Unit Helper(BattleState s) => s.Units.First(u => u.Def.Id == "helper");
        private static Unit Knight(BattleState s) => Hero(s, "knight");
        private static string Trace(List<BattleEvent> ev) => string.Join("\n", ev.Select(e => e.ToString()));
        private static SupportActionEvent Act(BattleState s, List<BattleEvent> ev) => ev.OfType<SupportActionEvent>().Single(a => a.SupporterId == Helper(s).Id);

        [Test]
        public void An_enemy_heal_supporter_mends_its_pincer_attacker()
        {
            var (s, ev) = EnemyPincer(new SupportDef { Effect = SupportEffect.Heal, Magnitude = 0.2f, Label = "Battle Hymn" },
                before: st => FrogA(st).Hp = Math.Max(1, FrogA(st).MaxHp / 2));
            var act = Act(s, ev);
            Assert.That(act.Label, Is.EqualTo("Battle Hymn"));
            Assert.That(act.Targets, Is.EqualTo(new[] { FrogA(s).Id }));
            var heal = ev.OfType<HealedEvent>().Single(h => h.SourceId == Helper(s).Id);
            Assert.That(heal.TargetId, Is.EqualTo(FrogA(s).Id));
            Assert.That(heal.Amount, Is.GreaterThan(0));
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == Helper(s).Id), Is.False, "no default strike");
        }

        [Test]
        public void An_enemy_buff_supporter_buffs_its_attacker()
        {
            var (s, ev) = EnemyPincer(new SupportDef { Effect = SupportEffect.Buff, Buff = "protection", Label = "Close Ranks" });
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.UnitId == FrogA(s).Id && b.BuffId == "protection"), Is.True, Trace(ev));
            Assert.That(FrogA(s).HasBuff("protection"), Is.True);
        }

        [Test]
        public void An_enemy_status_supporter_inflicts_its_status_on_the_trapped_hero()
        {
            var (s, ev) = EnemyPincer(new SupportDef { Effect = SupportEffect.Status, Magnitude = 1f, Buff = "poisoned", Label = "Toxic Skin" });
            var act = Act(s, ev);
            Assert.That(act.Targets, Is.EqualTo(new[] { Knight(s).Id }));
            Assert.That(act.BuffId, Is.EqualTo("poisoned"));
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.UnitId == Knight(s).Id && b.BuffId == "poisoned"), Is.True, Trace(ev));
        }

        [Test]
        public void An_enemy_cast_supporter_echoes_its_kit_spell_on_the_trapped_hero()
        {
            var (s, ev) = EnemyPincer(new SupportDef { Effect = SupportEffect.Cast, Magnitude = 0.5f, Label = "Hex Echo" },
                tweak: db => db.Classes["helper"].Abilities = new List<string> { "fireball" });
            var act = Act(s, ev);
            Assert.That(act.AbilityId, Is.EqualTo("fireball"));
            var hit = ev.OfType<DamagedEvent>().Single(d => d.SourceId == Helper(s).Id);
            Assert.That(hit.TargetId, Is.EqualTo(Knight(s).Id));
            Assert.That(hit.Type, Is.EqualTo(DamageType.Fire));
            Assert.That(hit.Amount, Is.GreaterThan(0));
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == Helper(s).Id), Is.False);
        }

        [Test]
        public void An_enemy_crit_supporter_lends_its_attacker_crit_for_the_pincer_only()
        {
            var (s, ev) = EnemyPincer(new SupportDef { Effect = SupportEffect.Crit, Magnitude = 0.25f, Label = "Pack Instinct" });
            var act = Act(s, ev);
            Assert.That(act.Targets, Is.EqualTo(new[] { FrogA(s).Id }));
            Assert.That(ev.IndexOf(act), Is.LessThan(ev.FindIndex(e => e is AttackEvent)), "lent before the frogs strike");
            Assert.That(s.Units.All(u => u.SupportCritBonus == 0f), Is.True);
        }

        [Test]
        public void An_enemy_haste_supporter_moves_its_sides_turn_icons_left_by_seconds()
        {
            var (s, ev) = EnemyPincer(new SupportDef { Effect = SupportEffect.Haste, Magnitude = 1f, Target = SupportTarget.Party, Label = "Time Skip" },
                before: st => { foreach (var u in st.Units.Where(u => u.Team == Team.Enemy && u != FrogA(st))) st.TurnEntryOf(u.Id).Ip = BattleConfig.BarLength - BattleConfig.BaseIpPerTick * 30; }); // 3 s out, on the 6 s bar (GG-T318)
            var frogB = At(s, 2, 3);
            var h = ev.OfType<HastenedEvent>().FirstOrDefault(x => x.UnitId == frogB.Id);
            Assert.That(h, Is.Not.Null, Trace(ev));
            Assert.That(h.FromU - h.ToU, Is.EqualTo(1f / BattleConfig.BarSeconds).Within(0.02f), "1 s sooner on the bar");
            Assert.That(ev.OfType<TempoChangedEvent>().Any(t => t.Source == "support:" + Helper(s).Id), Is.False, "an enemy haste lends the party no tempo");
        }

        /// <summary>Heroes have no turn icon; an enemy Pushback supporter slips a trapped hero's pending cast instead.</summary>
        [Test]
        public void An_enemy_pushback_supporter_slips_the_trapped_heros_cast()
        {
            const int castId = 900;
            void HeroCast(BattleState st) =>
                st.Timeline.Add(new TimelineEntry { Id = castId, Kind = TimelineKind.Cast, OwnerId = Hero(st, "knight").Id, CasterTeam = Team.Hero, AbilityId = "meteor", TotalCastTicks = 2000, RemainingTicks = 2000, TargetTile = new Coord(2, 0) });
            int Remaining(SupportDef d)
            {
                var (s, ev) = EnemyPincer(d, before: HeroCast);
                Assert.That(ev.OfType<PushedBackEvent>().Any(p => p.UnitId == Knight(s).Id), Is.False, "a hero has no turn icon to push");
                return s.Timeline.First(e => e.Id == castId).RemainingTicks;
            }
            int plain = Remaining(new SupportDef { Effect = SupportEffect.Heal, Magnitude = 0.1f });
            int shoved = Remaining(new SupportDef { Effect = SupportEffect.Pushback, Magnitude = 0.75f, Label = "Tongue Lash" });
            Assert.That(shoved - plain, Is.EqualTo(Formulas.SecondsToTicks(0.75f)), "an enemy pincer has no push of its own; the shove is all of it");
        }

        // ── Content ──

        [Test]
        public void Every_enemy_class_has_its_own_named_support()
        {
            var db = Real;
            var enemies = db.Classes.Values.Where(c => c.Team == Team.Enemy).ToList();
            Assert.That(enemies.Count, Is.GreaterThan(100));
            foreach (var c in enemies)
            {
                Assert.That(c.Support, Is.Not.Null, c.Id);
                Assert.That(c.Support.Effect, Is.Not.EqualTo(SupportEffect.Strike), $"{c.Id}: not the default strike");
                Assert.That(c.Support.Effect, Is.Not.EqualTo(SupportEffect.Mana), $"{c.Id}: Mana is hero-only");
                Assert.That(c.Support.Label, Is.Not.Null.And.Not.Empty, c.Id);
                var text = SupportText.Describe(db, c.Support, named: true);
                Assert.That(text, Does.StartWith("As a [Supporter]").And.Contain(c.Support.Label), c.Id);
                foreach (var t in Glossary.Terms(text)) Assert.That(db.TryTerm(t), Is.Not.Null, $"{c.Id}: [{t}]");
            }
            foreach (var c in enemies.Where(c => c.HasTag("Boss")))
                Assert.That(c.Support.Range, Is.GreaterThanOrEqualTo(2), $"{c.Id}: a boss's support reaches further");
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("support")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            foreach (var e in new[] { SupportEffect.Heal, SupportEffect.Buff, SupportEffect.Status, SupportEffect.Cast, SupportEffect.Pushback, SupportEffect.Crit, SupportEffect.Haste, SupportEffect.Cleanse })
                Assert.That(enemies.Any(c => c.Support.Effect == e), Is.True, $"some enemy supports with {e}");
        }

        /// <summary>Every real enemy support plays from the enemy side: the real class's Support and kit on the chained
        /// helper produce its labelled action.</summary>
        [Test]
        public void Every_real_enemy_support_resolves_in_an_enemy_pincer()
        {
            var real = Real;
            foreach (var c in real.Classes.Values.Where(c => c.Team == Team.Enemy))
            {
                var (s, ev) = EnemyPincer(c.Support, tweak: db =>
                {
                    foreach (var b in real.Buffs) db.Buffs[b.Key] = b.Value;
                    foreach (var a in real.Abilities) if (!db.Abilities.ContainsKey(a.Key)) db.Abilities[a.Key] = a.Value;
                    db.Classes["helper"].Abilities = new List<string>(c.Abilities);
                });
                var act = ev.OfType<SupportActionEvent>().SingleOrDefault(a => a.SupporterId == Helper(s).Id);
                Assert.That(act, Is.Not.Null, $"{c.Id}: {c.Support.Effect}\n{Trace(ev)}");
                Assert.That(act.Label, Is.EqualTo(c.Support.Label), c.Id);
                Assert.That(act.Effect, Is.EqualTo(c.Support.Effect), c.Id);
            }
        }

        [Test]
        public void Validation_rejects_an_enemy_mana_support()
        {
            var db = Real;
            var probe = db.Classes.Values.First(c => c.Team == Team.Enemy);
            var keep = probe.Support;
            probe.Support = new SupportDef { Effect = SupportEffect.Mana, Magnitude = 20f };
            try { Assert.That(ContentLoader.Validate(db).Any(p => p.Contains(probe.Id + " support") && p.Contains("hero-only")), Is.True); }
            finally { probe.Support = keep; }
        }
    }
}
