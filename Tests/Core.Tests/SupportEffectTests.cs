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
    /// <summary>GG-T298 (§1.2.3): supporters bring their class's own edge — Crit, Cast, Status, Pushback, Haste in seconds.</summary>
    public class SupportEffectTests
    {
        /// <summary>knight(1,3) and the cleric dropping on (3,3) pincer the tank on (2,3); the mage on (1,4) supports the
        /// knight with <paramref name="support"/>. Returns the state and the command's events.</summary>
        private static (BattleState s, List<BattleEvent> ev) Pincer(SupportDef support, int seed = 7, System.Action<ContentDb> tweak = null, System.Action<BattleState> before = null)
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Vitality = 30; // dented, not killed
            db.Classes["mage"].Support = support;
            tweak?.Invoke(db);
            var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = seed, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            setup.Party.Add(new HeroSpec { ClassId = "mage", Start = new Coord(1, 4) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            before?.Invoke(s);
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            return (s, ev);
        }

        private static Unit Tank(BattleState s) => s.Units.First(u => u.Def.Id == "tank");

        private static string Trace(List<BattleEvent> ev) => string.Join("\n", ev.Select(e => e.ToString()));

        // ── Crit ──

        [Test]
        public void A_crit_supporter_lends_its_attacker_crit_chance_before_the_blows_and_takes_it_back()
        {
            var crit = new SupportDef { Effect = SupportEffect.Crit, Magnitude = 0.3f, Range = 1, Label = "Shadow Edge" };
            var (s, ev) = Pincer(crit);
            var act = ev.OfType<SupportActionEvent>().Single(a => a.SupporterId == Hero(s, "mage").Id);
            Assert.That(act.Effect, Is.EqualTo(SupportEffect.Crit));
            Assert.That(act.Targets, Is.EqualTo(new[] { Hero(s, "knight").Id }));
            Assert.That(act.Amount, Is.EqualTo(0.3f));
            Assert.That(ev.IndexOf(act), Is.LessThan(ev.FindIndex(e => e is AttackEvent)), "the edge is lent before the attackers strike");
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == Hero(s, "mage").Id), Is.False, "a Crit supporter doesn't strike");
            Assert.That(s.Units.All(u => u.SupportCritBonus == 0f), Is.True, "the loan ends with the pincer");
            Assert.That(Trace(Pincer(crit).ev), Is.EqualTo(Trace(ev)), "deterministic");
        }

        [Test]
        public void Support_crit_raises_the_crit_band_up_to_its_cap()
        {
            var db = Mini();
            var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = 1 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var k = Hero(s, "knight"); var t = Tank(s);
            int Crits(float bonus)
            {
                k.SupportCritBonus = bonus;
                var rng = new Rng(42);
                int n = 0;
                for (int i = 0; i < 2000; i++) if (Formulas.RollHit(k, t, rng) == HitKind.Critical) n++;
                return n;
            }
            int plain = Crits(0f), lent = Crits(0.3f), huge = Crits(5f);
            float basePct = Formulas.CritPercent(k.Effective);
            Assert.That(plain / 20f, Is.EqualTo(basePct).Within(3f));
            Assert.That(lent / 20f, Is.EqualTo(basePct + 30f).Within(3f));
            Assert.That(huge / 20f, Is.EqualTo(SupportDef.MaxSupportedCritPercent).Within(3f), "capped");
        }

        // ── Cast ──

        [Test]
        public void A_cast_supporter_echoes_a_damaging_spell_from_its_kit_at_reduced_power()
        {
            var cast = new SupportDef { Effect = SupportEffect.Cast, Magnitude = 0.4f, Range = 1, Label = "Arcane Echo" };
            void Kit(ContentDb db) => db.Classes["mage"].Abilities = new List<string> { "heal", "teleport", "fireball", "quicken" };
            var (s, ev) = Pincer(cast, tweak: Kit);
            var mage = Hero(s, "mage");
            var act = ev.OfType<SupportActionEvent>().Single(a => a.SupporterId == mage.Id);
            Assert.That(act.AbilityId, Is.EqualTo("fireball"), "the only damaging spell in its kit");
            var hit = ev.OfType<DamagedEvent>().Single(d => d.SourceId == mage.Id);
            var fireball = s.Content.TryAbility("fireball");
            var tank = Tank(s);
            int expect = System.Math.Max(1, (int)System.Math.Round(Formulas.SpellDamage(fireball, tank, false) * Formulas.CasterPower(fireball, mage) * 0.4f));
            Assert.That(hit.TargetId, Is.EqualTo(tank.Id));
            Assert.That(hit.Type, Is.EqualTo(DamageType.Fire));
            Assert.That(hit.Amount, Is.EqualTo(expect));
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.BuffId == "burning"), Is.False, "an echo carries no rider status");
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == mage.Id), Is.False);
        }

        [Test]
        public void A_cast_supporter_with_no_damaging_spell_or_silenced_does_nothing()
        {
            var cast = new SupportDef { Effect = SupportEffect.Cast, Magnitude = 0.4f, Range = 1 };
            var (s, ev) = Pincer(cast, tweak: db => db.Classes["mage"].Abilities = new List<string> { "heal", "teleport", "quicken" });
            Assert.That(ev.OfType<SupportActionEvent>().Any(a => a.SupporterId == Hero(s, "mage").Id), Is.False);
            Assert.That(ev.OfType<DamagedEvent>().Any(d => d.SourceId == Hero(s, "mage").Id), Is.False);

            var (s2, ev2) = Pincer(cast, tweak: db =>
            {
                db.Classes["mage"].Abilities = new List<string> { "fireball" };
                db.Buffs["silenced"] = new BuffDef { Id = "silenced", Name = "Silenced", Kind = BuffKind.Debuff, Duration = 5, BlocksSpells = true };
            }, before: st => Hero(st, "mage").Buffs.Add(new BuffInstance { Def = st.Content.TryBuff("silenced"), Remaining = 5 }));
            Assert.That(ev2.OfType<DamagedEvent>().Any(d => d.SourceId == Hero(s2, "mage").Id), Is.False, "silenced supporters can't echo a spell");
        }

        [Test]
        public void A_cast_supporter_picks_among_its_damaging_spells_deterministically()
        {
            var cast = new SupportDef { Effect = SupportEffect.Cast, Magnitude = 0.4f, Range = 1 };
            void Kit(ContentDb db) => db.Classes["mage"].Abilities = new List<string> { "fireball", "heal", "frost" };
            var picks = new HashSet<string>();
            for (int seed = 1; seed <= 30; seed++)
            {
                var a = Pincer(cast, seed, Kit).ev.OfType<SupportActionEvent>().Single(x => x.Effect == SupportEffect.Cast);
                var b = Pincer(cast, seed, Kit).ev.OfType<SupportActionEvent>().Single(x => x.Effect == SupportEffect.Cast);
                Assert.That(b.AbilityId, Is.EqualTo(a.AbilityId), $"seed {seed}");
                picks.Add(a.AbilityId);
            }
            Assert.That(picks, Is.EquivalentTo(new[] { "fireball", "frost" }));
        }

        // ── Status ──

        [Test]
        public void A_status_supporter_inflicts_its_status_on_the_trapped_foes_by_chance()
        {
            void Poison(ContentDb db) => db.Buffs["poisoned"] = new BuffDef { Id = "poisoned", Name = "Poisoned", Kind = BuffKind.Debuff, Unit = DurationUnit.Ticks, Duration = 6, DamagePerTick = 1 };
            bool Lands(float chance, int seed)
            {
                var (s, ev) = Pincer(new SupportDef { Effect = SupportEffect.Status, Magnitude = chance, Buff = "poisoned", Range = 1, Label = "Venom Rain" }, seed, Poison);
                var act = ev.OfType<SupportActionEvent>().Single(a => a.Effect == SupportEffect.Status);
                Assert.That(act.Targets, Is.EqualTo(new[] { Tank(s).Id }));
                Assert.That(act.BuffId, Is.EqualTo("poisoned"));
                return ev.Any(e => e is BuffAppliedEvent b && b.UnitId == Tank(s).Id && b.BuffId == "poisoned");
            }
            Assert.That(Lands(1f, 7), Is.True, "chance 1 always lands");
            int hits = Enumerable.Range(1, 60).Count(seed => Lands(0.5f, seed));
            Assert.That(hits, Is.InRange(15, 45), "about half at 50%");
            Assert.That(Enumerable.Range(1, 30).Count(seed => Lands(0.001f, seed)), Is.EqualTo(0));
            Assert.That(Enumerable.Range(1, 10).Select(seed => Lands(0.5f, seed)), Is.EqualTo(Enumerable.Range(1, 10).Select(seed => Lands(0.5f, seed))), "deterministic");
        }

        // ── Pushback ──

        private static float PushDelta(SupportDef support)
        {
            var (s, ev) = Pincer(support, tweak: db => db.Classes["tank"].BaseStats.Speed = 5, // GG-T311: slow, so a push from a drag's minimum out never clamps
                before: st => ActsAfterAction(st, Tank(st))); // about to act (a drag still fits, GG-T311): no clamp at the bar's end
            var pushes = ev.OfType<PushedBackEvent>().Where(p => p.UnitId == Tank(s).Id).ToList();
            Assert.That(pushes.Count, Is.EqualTo(1), "one push per foe per action");
            return pushes[0].ToU - pushes[0].FromU;
        }

        [Test]
        public void A_pushback_supporter_adds_seconds_to_the_pincer_push_capped_and_once()
        {
            float plain = PushDelta(new SupportDef { Effect = SupportEffect.Heal, Magnitude = 0.1f, Range = 1 });
            float shove = PushDelta(new SupportDef { Effect = SupportEffect.Pushback, Magnitude = 0.5f, Range = 1, Label = "Shove" });
            float huge = PushDelta(new SupportDef { Effect = SupportEffect.Pushback, Magnitude = 2f, Range = 1 });
            float sec = 1f / BattleConfig.BarSeconds;
            Assert.That(plain, Is.EqualTo(BattleConfig.PincerPushbackSeconds * sec).Within(0.02f));
            Assert.That(shove, Is.EqualTo((BattleConfig.PincerPushbackSeconds + 0.5f) * sec).Within(0.02f));
            Assert.That(huge, Is.EqualTo(BattleConfig.PushbackMaxSeconds * sec).Within(0.02f), "capped at PushbackMaxSeconds");
        }

        // ── Haste (seconds) ──

        [Test]
        public void Haste_support_is_measured_in_seconds()
        {
            var haste = new SupportDef { Effect = SupportEffect.Haste, Magnitude = 1.5f, Target = SupportTarget.Party, Range = 1, Label = "Tailwind" };
            var (s, ev) = Pincer(haste, before: st =>
                st.Timeline.Add(new TimelineEntry { Id = 900, Kind = TimelineKind.Cast, OwnerId = Hero(st, "cleric").Id, CasterTeam = Team.Hero, AbilityId = "meteor", TotalCastTicks = 60, RemainingTicks = 60, TargetTile = new Coord(2, 0) }));
            var h = ev.OfType<HastenedEvent>().SingleOrDefault(x => x.EntryId == 900);
            Assert.That(h, Is.Not.Null, Trace(ev));
            Assert.That(h.FromU - h.ToU, Is.EqualTo(1.5f / BattleConfig.BarSeconds).Within(0.02f), "1.5 s sooner on a 10 s bar");
            var tempo = ev.OfType<TempoChangedEvent>().FirstOrDefault(t => t.Source == "support:" + Hero(s, "mage").Id);
            Assert.That(tempo, Is.Not.Null, "a hasted hero lifts party tempo");
        }

        // ── Content ──

        [Test]
        public void Real_supports_validate_and_read_as_glossary_sentences()
        {
            var db = Real;
            var problems = ContentLoader.Validate(db).Where(p => p.Contains("support")).ToList();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            var heroes = db.Classes.Values.Where(c => c.Team == Team.Hero && c.Support != null).ToList();
            foreach (var e in new[] { SupportEffect.Crit, SupportEffect.Cast, SupportEffect.Status, SupportEffect.Pushback })
                Assert.That(heroes.Any(c => c.Support.Effect == e), Is.True, $"some hero class supports with {e}");
            foreach (var c in heroes)
            {
                var text = SupportText.Describe(db, c.Support);
                Assert.That(text, Does.StartWith("As a [Supporter]"), c.Id);
                foreach (var t in Glossary.Terms(text)) Assert.That(db.TryTerm(t), Is.Not.Null, $"{c.Id}: [{t}]");
            }
            Assert.That(SupportText.Describe(db, new SupportDef { Effect = SupportEffect.Crit, Magnitude = 0.2f }), Does.Contain("+20% [Crit]"));
        }

        [Test]
        public void Validation_rejects_bad_support_magnitudes()
        {
            var db = Real;
            var probe = db.Classes.Values.First(c => c.Team == Team.Hero && c.Support != null);
            var keep = probe.Support;
            string Check(SupportDef d)
            {
                probe.Support = d;
                try { return string.Join("\n", ContentLoader.Validate(db).Where(p => p.Contains(probe.Id + " support"))); }
                finally { probe.Support = keep; }
            }
            Assert.That(Check(new SupportDef { Effect = SupportEffect.Crit, Magnitude = 0.9f }), Does.Contain("Crit"));
            Assert.That(Check(new SupportDef { Effect = SupportEffect.Status, Magnitude = 0.5f, Buff = "nope" }), Does.Contain("unknown status"));
            Assert.That(Check(new SupportDef { Effect = SupportEffect.Status, Magnitude = 0.5f, Buff = "regen" }), Does.Contain("not a debuff"));
            Assert.That(Check(new SupportDef { Effect = SupportEffect.Pushback, Magnitude = 5f }), Does.Contain("Pushback"));
            Assert.That(Check(new SupportDef { Effect = SupportEffect.Haste, Magnitude = 25f }), Does.Contain("seconds"));
            Assert.That(Check(new SupportDef { Effect = SupportEffect.Cast, Magnitude = 3f }), Does.Contain("Cast power"));
        }
    }
}
