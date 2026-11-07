using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T296 (§4.1): Passive and Reactive abilities are data on one trigger engine, for heroes and enemies alike.</summary>
    public class TriggerEngineTests
    {
        private static AbilityDef Reactive(string id, TriggerOn on, float chance, params TriggerEffect[] effects) => new AbilityDef
        {
            Id = id, Name = id, Kind = AbilityKind.Reactive,
            Trigger = new TriggerDef { On = on, Chance = chance, Effects = effects.ToList() },
        };

        /// <summary>A knight (with <paramref name="knightPassives"/>) beside a wolf that bites it on each of a few forced turns.</summary>
        private static (BattleState s, Unit knight, Unit wolf, List<BattleEvent> ev) WolfBites(ContentDb db, int seed = 3, int turns = 3, params string[] knightPassives)
        {
            db.Classes["wolf"].MoveRange = 0;
            var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = seed, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3), Passives = knightPassives.ToList() });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var all = new List<BattleEvent>();
            var s = BattleEngine.Create(db, setup, all);
            var k = Hero(s, "knight");
            var wolf = s.Units.First(u => u.Def.Id == "wolf");
            for (int i = 0; i < turns && wolf.Alive && k.Alive && !s.IsOver; i++)
            {
                s.TurnEntryOf(wolf.Id).Ip = BattleConfig.BarLength - 1;
                all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            }
            return (s, k, wolf, all);
        }

        private static bool BitAndLanded(List<BattleEvent> ev, Unit wolf, Unit k) =>
            ev.OfType<DamagedEvent>().Any(d => d.SourceId == wolf.Id && d.TargetId == k.Id && d.Hit != HitKind.Miss);

        [Test]
        public void A_new_reactive_counterstrikes_when_attacked_directly()
        {
            var db = Mini();
            db.Abilities["riposte"] = Reactive("riposte", TriggerOn.OnAttackedDirect, 1f, new TriggerEffect { Kind = TriggerEffectKind.Counterstrike, Amount = 0.5f });
            var (_, k, wolf, ev) = WolfBites(db, knightPassives: "riposte");
            Assume.That(BitAndLanded(ev, wolf, k), Is.True);
            int cue = ev.FindIndex(e => e is TriggeredEvent t && t.UnitId == k.Id && t.AbilityId == "riposte");
            Assert.That(cue, Is.GreaterThanOrEqualTo(0), "the reactive fired");
            var t0 = (TriggeredEvent)ev[cue];
            Assert.That(t0.Effect, Is.EqualTo(TriggerEffectKind.Counterstrike));
            Assert.That(t0.Kind, Is.EqualTo(AbilityKind.Reactive));
            Assert.That(ev.Skip(cue + 1).First() is AttackEvent a && a.AttackerId == k.Id && a.TargetId == wolf.Id, Is.True, "the cue plays before the counter blow");
        }

        [Test]
        public void A_chance_reactive_fires_sometimes_and_is_deterministic_per_seed()
        {
            int fired = 0, landed = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var db = Mini();
                db.Abilities["riposte"] = Reactive("riposte", TriggerOn.OnAttackedDirect, 0.5f, new TriggerEffect { Kind = TriggerEffectKind.Counterstrike, Amount = 0.5f });
                var (_, k, wolf, ev) = WolfBites(db, seed, 2, "riposte");
                var (_, _, _, again) = WolfBites(CloneMini("riposte", 0.5f), seed, 2, "riposte");
                Assert.That(again.Select(e => e.ToString()), Is.EqualTo(ev.Select(e => e.ToString())), $"seed {seed} replays identically");
                landed += ev.OfType<DamagedEvent>().Count(d => d.SourceId == wolf.Id && d.TargetId == k.Id && d.Hit != HitKind.Miss);
                fired += ev.OfType<TriggeredEvent>().Count(t => t.AbilityId == "riposte");
            }
            Assert.That(fired, Is.GreaterThan(0));
            Assert.That(fired, Is.LessThan(landed), "a 50% reactive doesn't fire on every landed bite");
        }

        private static ContentDb CloneMini(string id, float chance)
        {
            var db = Mini();
            db.Abilities[id] = Reactive(id, TriggerOn.OnAttackedDirect, chance, new TriggerEffect { Kind = TriggerEffectKind.Counterstrike, Amount = 0.5f });
            return db;
        }

        [Test]
        public void A_reaction_never_provokes_a_reaction()
        {
            var db = Mini();
            db.Abilities["counter_attack"] = Real.Ability("counter_attack");
            db.Classes["wolf"].Abilities.Add("counter_attack");
            var (_, k, wolf, ev) = WolfBites(db, turns: 1, knightPassives: "counter_attack");
            Assume.That(BitAndLanded(ev, wolf, k), Is.True);
            Assert.That(ev.OfType<TriggeredEvent>().Count(t => t.UnitId == k.Id), Is.EqualTo(1), "the knight counters the bite");
            Assert.That(ev.OfType<TriggeredEvent>().Any(t => t.UnitId == wolf.Id), Is.False, "the wolf never counters the counter");
            Assert.That(ev.OfType<AttackEvent>().Count(a => a.AttackerId == wolf.Id), Is.EqualTo(1));
        }

        [Test]
        public void CastAbility_resolves_an_active_spell_through_the_normal_dispatcher()
        {
            var db = Mini();
            db.Abilities["fire_skin"] = Reactive("fire_skin", TriggerOn.OnAttackedDirect, 1f, new TriggerEffect { Kind = TriggerEffectKind.CastAbility, Ability = "fireball" });
            var (_, k, wolf, ev) = WolfBites(db, turns: 1, knightPassives: "fire_skin");
            Assume.That(BitAndLanded(ev, wolf, k), Is.True);
            Assert.That(ev.OfType<AbilityResolvedEvent>().Any(r => r.CasterId == k.Id && r.AbilityId == "fireball" && r.Targets.Contains(wolf.Id)), Is.True);
            Assert.That(ev.OfType<DamagedEvent>().Any(d => d.SourceId == k.Id && d.TargetId == wolf.Id && d.Type == DamageType.Fire), Is.True);
        }

        [Test]
        public void OnSurrounded_fires_at_turn_start_with_enough_adjacent_foes()
        {
            var db = Mini();
            db.Classes["wolf"].MoveRange = 0;
            db.Buffs["rallied"] = new BuffDef { Id = "rallied", Name = "Rallied", Kind = BuffKind.Buff, Unit = DurationUnit.Turns, Duration = 2 };
            db.Abilities["last_stand"] = new AbilityDef
            {
                Id = "last_stand", Name = "Last Stand", Kind = AbilityKind.Passive,
                Trigger = new TriggerDef { On = TriggerOn.OnSurrounded, Count = 2, Effects = { new TriggerEffect { Kind = TriggerEffectKind.ApplyBuff, Buff = "rallied" } } },
            };
            BattleState Run(params (string, int, int)[] wolves)
            {
                var setup = new BattleSetup { Stage = Stage(wolves), Seed = 5, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(3, 4), Passives = { "last_stand" } });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(6, 7) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                BattleEngine.Apply(s, new ShieldCommand());
                Assert.That(s.IsOver, Is.False);
                return s;
            }
            var two = Run(("wolf", 2, 4), ("wolf", 4, 4));
            Assert.That(Hero(two, "knight").HasBuff("rallied"), Is.True, "two adjacent foes: surrounded");
            var one = Run(("wolf", 2, 4), ("wolf", 0, 0));
            Assert.That(Hero(one, "knight").HasBuff("rallied"), Is.False, "one adjacent foe isn't enough");
        }

        [Test]
        public void An_enemy_reactive_reflects_a_heros_pincer_blow()
        {
            var db = Mini();
            db.Abilities["thorns"] = Real.Ability("thorns");
            db.Classes["tank"].Abilities.Add("thorns");
            var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var blow = ev.OfType<DamagedEvent>().First(d => d.TargetId == tank.Id && d.Amount > 0 && d.Hit != HitKind.Miss);
            var back = ev.OfType<DamagedEvent>().FirstOrDefault(d => d.SourceId == tank.Id && d.Note == "Thorns");
            Assert.That(back, Is.Not.Null, "the tank's thorns prick the hero that struck it");
            Assert.That(back.TargetId, Is.EqualTo(blow.SourceId));
            Assert.That(back.Amount, Is.EqualTo(System.Math.Max(1, (int)System.Math.Round(blow.Amount * 0.25f))));
        }

        [Test]
        public void A_skill_tree_passive_reaches_the_battle()
        {
            var c = Real;
            var cls = c.Classes.Values.First(x => x.Team == Team.Hero).Id;
            var save = new HeroSave { ClassId = cls, Known = { "counter_attack" } };
            var spec = BattleFactory.SpecFor(c, save);
            Assert.That(spec.Passives, Does.Contain("counter_attack"), "SpecFor carries known passives");

            var db = Mini();
            db.Abilities["counter_attack"] = Real.Ability("counter_attack");
            var (_, k, wolf, ev) = WolfBites(db, turns: 1, knightPassives: "counter_attack");
            Assume.That(BitAndLanded(ev, wolf, k), Is.True);
            Assert.That(ev.OfType<TriggeredEvent>().Any(t => t.UnitId == k.Id && t.AbilityId == "counter_attack"), Is.True, "Last Bastion's Counter Attack works in battle");
        }

        [Test]
        public void Ported_lifesteal_heals_a_wounded_biter()
        {
            var db = Mini();
            db.Abilities["lifesteal"] = Real.Ability("lifesteal");
            db.Classes["wolf"].Abilities.Add("lifesteal");
            db.Classes["wolf"].MoveRange = 0;
            var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3) });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var k = Hero(s, "knight");
            var wolf = s.Units.First(u => u.Def.Id == "wolf");
            wolf.Hp = 1;
            var all = new List<BattleEvent>();
            for (int i = 0; i < 4 && !all.OfType<DamagedEvent>().Any(d => d.SourceId == wolf.Id && d.Amount > 0); i++)
            {
                s.TurnEntryOf(wolf.Id).Ip = BattleConfig.BarLength - 1;
                all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            }
            var bite = all.OfType<DamagedEvent>().FirstOrDefault(d => d.SourceId == wolf.Id && d.TargetId == k.Id && d.Amount > 0);
            Assume.That(bite, Is.Not.Null);
            var heal = all.OfType<HealedEvent>().First(h => h.TargetId == wolf.Id);
            Assert.That(heal.Amount, Is.EqualTo(System.Math.Max(1, (int)System.Math.Round(bite.Amount * 0.25f))));
            Assert.That(all.OfType<TriggeredEvent>().Any(t => t.UnitId == wolf.Id && t.AbilityId == "lifesteal" && t.Effect == TriggerEffectKind.Lifesteal), Is.True);
        }

        [Test]
        public void Ported_modifiers_match_the_old_integer_formulas()
        {
            var c = Real;
            var hide = c.Ability("tough_hide").Trigger.Effects[0].Amount;
            for (int amount = 1; amount <= 2000; amount++)
                Assert.That(Triggers.FloorScaled(amount, hide), Is.EqualTo(System.Math.Max(1, amount * 7 / 10)), $"tough hide on {amount}");
            var regen = c.Ability("regenerate").Trigger.Effects[0].Amount;
            for (int maxHp = 1; maxHp <= 5000; maxHp++)
                Assert.That(Triggers.FloorScaled(maxHp, regen), Is.EqualTo(System.Math.Max(1, maxHp * 6 / 100)), $"regenerate on {maxHp}");
            Assert.That(c.Ability("berserk").Trigger.Effects[0].Amount, Is.EqualTo(1.35f));
            Assert.That(c.Ability("lifesteal").Trigger.Effects[0].Amount, Is.EqualTo(0.25f), "a quarter of the damage (GG-T282)");
            Assert.That(c.Ability("thorns").Trigger.Effects[0].Amount, Is.EqualTo(0.25f));
            Assert.That(c.Ability("mana_siphon").Trigger.Effects[0].Amount, Is.EqualTo(5f));
        }

        [Test]
        public void Berserk_tough_hide_and_wards_read_from_data_for_heroes_too()
        {
            var db = Mini();
            foreach (var id in new[] { "berserk", "tough_hide", "steadfast", "poison_ward" }) db.Abilities[id] = Real.Ability(id);
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 3, 4), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            var wolf = s.Units.Single(u => u.Def.Id == "wolf");
            k.Passives.AddRange(new[] { "berserk", "tough_hide", "steadfast" });
            Assert.That(Triggers.IncomingMultiplier(s, k, wolf, DamageType.Physical), Is.EqualTo(0.7f));
            Assert.That(Triggers.IncomingMultiplier(s, k, wolf, DamageType.Fire), Is.EqualTo(1f), "tough hide is physical only");
            Assert.That(Triggers.OutgoingMultiplier(s, k, wolf), Is.EqualTo(1f), "berserk sleeps at full HP");
            k.Hp = k.MaxHp / 2 - 1;
            Assert.That(Triggers.OutgoingMultiplier(s, k, wolf), Is.EqualTo(1.35f));
            Assert.That(Triggers.ImmunityTo(s, k, "frozen")?.Id, Is.EqualTo("steadfast"));
            Assert.That(Triggers.ImmunityTo(s, k, "poisoned"), Is.Null);
        }

        [Test]
        public void Mana_siphon_charges_the_meter_from_a_landed_blow()
        {
            var db = Mini();
            db.Abilities["mana_siphon"] = Real.Ability("mana_siphon");
            var setup = new BattleSetup { Stage = Stage(("tank", 2, 3)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3), Passives = { "mana_siphon" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            Assert.That(ev.OfType<TriggeredEvent>().Any(t => t.AbilityId == "mana_siphon" && t.Effect == TriggerEffectKind.ManaCharge), Is.True);
        }

        [Test]
        public void Every_shipped_passive_is_data_and_validates()
        {
            var c = Real;
            Assert.That(ContentLoader.Validate(c).Where(p => p.Contains("trigger") || p.Contains("Reactive")), Is.Empty);
            foreach (var id in new[] { "double_attack", "counter_attack", "lifesteal", "tough_hide", "berserk", "regenerate", "poison_ward", "steadfast", "thorns", "mana_siphon" })
                Assert.That(c.Ability(id).Trigger, Is.Not.Null, id);
            Assert.That(c.Ability("counter_attack").Kind, Is.EqualTo(AbilityKind.Reactive));
            Assert.That(c.Ability("thorns").Kind, Is.EqualTo(AbilityKind.Reactive));
            Assert.That(TriggerText.Describe(c, c.Ability("counter_attack")), Is.EqualTo("When [attacked directly]: [Counterstrike] (×0.5)."));
        }

        [Test]
        public void Validate_rejects_a_malformed_trigger()
        {
            var db = Mini();
            db.Abilities["bad"] = new AbilityDef
            {
                Id = "bad", Name = "Bad", Kind = AbilityKind.Reactive,
                Trigger = new TriggerDef { On = TriggerOn.Always, Chance = 2f, Effects = { new TriggerEffect { Kind = TriggerEffectKind.CastAbility, Ability = "nope" } } },
            };
            db.Abilities["bare"] = new AbilityDef { Id = "bare", Name = "Bare", Kind = AbilityKind.Reactive };
            var problems = new List<string>();
            ContentLoader.ValidateTrigger(db, db.Abilities["bad"], problems);
            ContentLoader.ValidateTrigger(db, db.Abilities["bare"], problems);
            Assert.That(problems.Any(p => p.Contains("not Always")), Is.True);
            Assert.That(problems.Any(p => p.Contains("Chance")), Is.True);
            Assert.That(problems.Any(p => p.Contains("unknown ability 'nope'")), Is.True);
            Assert.That(problems.Any(p => p.Contains("needs a Trigger")), Is.True);
        }

        [Test]
        public void Double_attack_follows_up_at_half_strength_for_an_enemy()
        {
            var db = Mini();
            db.Abilities["double_attack"] = Real.Ability("double_attack");
            db.Classes["wolf"].Abilities.Add("double_attack");
            var (_, k, wolf, ev) = WolfBites(db, turns: 1);
            Assert.That(ev.OfType<AttackEvent>().Count(a => a.AttackerId == wolf.Id && a.IsMelee), Is.EqualTo(2), "two melee swings");
        }
    }
}
