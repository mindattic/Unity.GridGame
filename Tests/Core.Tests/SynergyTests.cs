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
    /// <summary>Roles that need each other (§25.13): taunt, caster power, empowered strikes and battle pickups (§13.3.1).</summary>
    public class SynergyTests
    {
        private static ContentDb WithRoleKit()
        {
            var db = Mini();
            db.Buffs["taunting"] = new BuffDef { Id = "taunting", Name = "Taunting", Kind = BuffKind.Buff, Unit = DurationUnit.Turns, Duration = 2, IncomingDamageMultiplier = 0.75f, Taunts = true };
            db.Buffs["empowered"] = new BuffDef { Id = "empowered", Name = "Empowered", Kind = BuffKind.Buff, Unit = DurationUnit.Turns, Duration = 2, OutgoingDamageMultiplier = 1.3f };
            db.Abilities["taunt"] = new AbilityDef { Id = "taunt", Name = "Taunt", Kind = AbilityKind.Skill, CooldownTurns = 3, Shape = TargetShape.Self, Mode = TargetMode.Auto, Filter = TargetFilter.AllyOnly, ApplyBuff = "taunting" };
            db.Abilities["war_cry"] = new AbilityDef { Id = "war_cry", Name = "War Cry", Kind = AbilityKind.Skill, CooldownTurns = 4, Shape = TargetShape.AllAllies, Mode = TargetMode.Auto, Filter = TargetFilter.AllyOnly, ApplyBuff = "empowered" };
            return db;
        }

        [Test]
        public void A_taunting_hero_draws_every_enemy_that_can_choose()
        {
            var db = WithRoleKit();
            db.Classes["goblin"].Targeting = Targeting.Weakest;
            var (s, _) = Battle(db, Stage(("goblin", 2, 2)), ("knight", 2, 7), ("mage", 3, 3));
            var gob = s.Units.Single(u => u.Def.Id == "goblin");
            var knight = Hero(s, "knight");
            var mage = Hero(s, "mage");
            mage.Hp = 3; // the weakest, and right next to it
            Assert.That(EnemyPlanner.PickTarget(s, gob), Is.SameAs(mage));

            knight.Bar[0] = new AbilitySlot { AbilityId = "taunt" };
            var ev = Turn(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0 });
            Assert.That(ev.Any(e => e is BuffAppliedEvent b && b.BuffId == "taunting" && b.UnitId == knight.Id), Is.True);
            Assert.That(knight.IsTaunting, Is.True);
            Assert.That(EnemyPlanner.PickTarget(s, gob), Is.SameAs(knight));
            Assert.That(knight.Cooldowns["taunt"] + 1, Is.EqualTo(3)); // started at 3, ticked once at control return
        }

        [Test]
        public void Taunt_only_overrides_among_heroes_in_reach()
        {
            var db = WithRoleKit();
            var (s, _) = Battle(db, Stage(("goblin", 2, 2)), ("knight", 5, 7), ("mage", 2, 3));
            var gob = s.Units.Single(u => u.Def.Id == "goblin");
            Hero(s, "knight").Buffs.Add(new BuffInstance { Def = db.Buffs["taunting"], Remaining = 2 });
            // In melee only the mage is adjacent: the taunter can't be hit from here, so the mage still takes the blow.
            Assert.That(EnemyPlanner.PickFrom(s, gob, new List<Unit> { Hero(s, "mage") }), Is.SameAs(Hero(s, "mage")));
            Assert.That(EnemyPlanner.PickTarget(s, gob), Is.SameAs(Hero(s, "knight")));
        }

        [Test]
        public void Caster_power_makes_a_mage_out_hit_a_fighter_and_a_sage_out_heal()
        {
            var c = Real;
            var fire = c.Abilities["fireball"];
            var mage = new Unit { Def = c.Classes["redmage"], Team = Team.Hero, LevelStats = c.Classes["redmage"].BaseStats.Clone() };
            var fighter = new Unit { Def = c.Classes["fighter"], Team = Team.Hero, LevelStats = c.Classes["fighter"].BaseStats.Clone() };
            Assert.That(Formulas.CasterPower(fire, mage) > 1.2f, Is.True);
            Assert.That(Formulas.CasterPower(fire, fighter) < 1f, Is.True);
            var cleave = c.Abilities["cleave"];
            Assert.That(Formulas.CasterPower(cleave, fighter) > Formulas.CasterPower(cleave, mage), Is.True, "physical skills scale with STR");
            var sage = new Unit { Def = c.Classes["sage"], Team = Team.Hero, LevelStats = c.Classes["sage"].BaseStats.Clone() };
            Assert.That(Formulas.HealPower(sage) > Formulas.HealPower(fighter), Is.True);
            var enemy = new Unit { Def = c.Classes["hag00"], Team = Team.Enemy, LevelStats = c.Classes["hag00"].BaseStats.Clone() };
            Assert.That(Formulas.CasterPower(fire, enemy), Is.EqualTo(1f)); // enemy kits keep their tuned numbers
        }

        [Test]
        public void War_cry_empowers_every_ally_and_their_pincers_hit_harder()
        {
            var db = WithRoleKit();
            int Pincer(bool empowered)
            {
                var (s, _) = Battle(db, Stage(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4), ("mage", 5, 7));
                if (empowered)
                {
                    var m = Hero(s, "mage");
                    m.Bar[0] = new AbilitySlot { AbilityId = "war_cry" };
                    var ev0 = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0 });
                    Assert.That(ev0.OfType<BuffAppliedEvent>().Count(b => b.BuffId == "empowered"), Is.EqualTo(3));
                }
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                return ev.OfType<DamagedEvent>().Where(d => d.SourceId == Hero(s, "knight").Id && d.Hit != HitKind.Miss).Sum(d => d.Amount);
            }
            int plain = Pincer(false), loud = Pincer(true);
            Assert.That(plain > 0, Is.True);
            Assert.That(loud > plain, Is.True, $"empowered {loud} should beat {plain}");
        }

        private static ContentDb WithPickups(float heal = 0.5f, int orbs = 0)
        {
            var db = WithRoleKit();
            db.Pickups["pk"] = new PickupDef { Id = "pk", Name = "Tonic", Weight = 1, HealFraction = heal, Orbs = orbs };
            return db;
        }

        [Test]
        public void Stepping_on_a_pickup_collects_it_and_heals()
        {
            var db = WithPickups();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 3, 6), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            k.Hp = 5;
            s.Pickups.Add(new Pickup { Id = 1, PickupId = "pk", At = new Coord(3, 5), ActionsLeft = 4 });
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((3, 5)) });
            Assert.That(ev.Any(e => e is PickupCollectedEvent pc && pc.UnitId == k.Id), Is.True);
            Assert.That(ev.Any(e => e is HealedEvent h && h.TargetId == k.Id), Is.True);
            Assert.That(s.Pickups, Is.Empty);
        }

        [Test]
        public void A_round_trip_drag_that_only_grabs_a_pickup_still_counts_as_a_move()
        {
            var db = WithPickups(0f, 2);
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 3, 6), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            s.Pickups.Add(new Pickup { Id = 1, PickupId = "pk", At = new Coord(3, 5), ActionsLeft = 4 });
            int before = s.Bank.Count;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((3, 5), (3, 6)) });
            Assert.That(ev.Any(e => e is AbilityRefusedEvent), Is.False);
            Assert.That(s.Bank.Count >= before + 2, Is.True);
        }

        [Test]
        public void Pickups_fade_after_their_lifetime()
        {
            var db = WithPickups();
            var (s, _) = Battle(db, Stage(("tank", 0, 0)), ("knight", 3, 6), ("cleric", 5, 7));
            s.Pickups.Add(new Pickup { Id = 1, PickupId = "pk", At = new Coord(0, 5), ActionsLeft = 2 });
            var k = Hero(s, "knight");
            Turn(s, new MoveCommand { HeroId = k.Id, Path = P((2, 6)) });
            Assert.That(s.Pickups, Has.Exactly(1).Items);
            var ev = Turn(s, new MoveCommand { HeroId = k.Id, Path = P((3, 6)) });
            Assert.That(ev.Any(e => e is PickupExpiredEvent), Is.True);
            Assert.That(s.Pickups, Is.Empty);
        }

        [Test]
        public void Fallen_enemies_sometimes_leave_pickups()
        {
            var db = WithPickups();
            int drops = 0;
            for (int seed = 1; seed <= 80; seed++)
            {
                var setup = new BattleSetup { Stage = Stage(("goblin", 2, 3), ("tank", 0, 0)), Seed = seed, UnlockedSlots = 2 };
                setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3) });
                setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 4) });
                var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                if (ev.Any(e => e is PickupDroppedEvent)) { drops++; Assert.That(s.Pickups, Has.Exactly(1).Items); AssertInvariants(s); }
            }
            Assert.That(drops, Is.InRange(6, 40)); // 20% base
        }

        [Test]
        public void Every_hero_has_a_role_and_kits_match_it()
        {
            var c = Real;
            Assert.That(ContentLoader.Validate(c), Is.Empty);
            foreach (var h in c.Classes.Values.Where(x => x.Team == Team.Hero))
            {
                var bar = h.DefaultLoadout.Where(x => !x.StartsWith("item:")).ToList();
                switch (h.Role)
                {
                    case "Tank":
                        Assert.That(bar, Does.Contain("taunt"));
                        // A lone tank (party wiped around it) can't pincer by itself — it needs some non-pincer
                        // damage source in its own bar or it can soft-lock against a weak enemy neither side can
                        // finish (found live 2026-10-13: a paladin with every bar slot non-damage/on
                        // cooldown/unaffordable stalled ~40 simulated minutes against a 7-HP straggler).
                        Assert.That(bar.Any(x => c.Abilities[x].BaseDamage > 0), Is.True, h.Id);
                        break;
                    case "Healer": Assert.That(bar, Does.Contain("heal")); break;
                    case "Mage": Assert.That(bar.Count(x => c.Abilities[x].Kind == AbilityKind.Spell && c.Abilities[x].BaseDamage > 0) >= 2, Is.True, h.Id); break;
                    case "Striker": Assert.That(bar, Does.Contain("cleave")); break;
                    case "Support": Assert.That(bar.Contains("quicken") || bar.Contains("regen"), Is.True, h.Id); break;
                    case "Ranged": Assert.That(bar.All(x => !c.Abilities[x].AdjacentOnly), Is.True, h.Id); break;
                }
                foreach (var a in bar) Assert.That(h.Abilities, Does.Contain(a));
            }
        }
    
        [Test]
        public void The_bar_can_be_reordered_within_unlocked_slots()
        {
            var c = Real; var save = Progression.NewGame(c, "Org");
            var h = save.Hero("paladin");
            var first = h.Bar[0]; var second = h.Bar[1];
            Assert.That(Abilities.Swap(save, h, 0, 1).Ok, Is.True);
            Assert.That(h.Bar[0], Is.EqualTo(second));
            Assert.That(h.Bar[1], Is.EqualTo(first));
            Assert.That(Abilities.Swap(save, h, 1, Progression.MaxBarSlots).Ok, Is.False, "there is no sixth slot");
        }
    
        // ── Mana charge (§3.1.9) ──

        [Test]
        public void More_heroes_in_a_pincer_earn_less_mana_per_hp_taken()
        {
            var db = Mini();
            db.Classes["tank"].BaseStats.Vitality = 30;
            (int amount, float share) Run(bool withSupporter)
            {
                var heroes = new List<(string, int, int)> { ("knight", 1, 3), ("cleric", 3, 4) };
                if (withSupporter) heroes.Add(("mage", 1, 4));
                var (s, _) = Battle(db, Stage(("tank", 2, 3)), heroes.ToArray());
                var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
                var tank = s.Units.Single(u => u.Def.Id == "tank");
                return (ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "pincer").Amount, (tank.MaxHp - tank.Hp) / (float)tank.MaxHp);
            }
            var lean = Run(false); var crowd = Run(true);
            Assert.That(crowd.share >= lean.share, Is.True, "the supporter adds damage…");
            Assert.That(crowd.amount / crowd.share < lean.amount / lean.share, Is.True, "…but each point of HP taken charges less");
        }

        [Test]
        public void Charge_rolls_over_into_orbs_and_keeps_the_remainder()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            s.ManaCharge = BattleConfig.ManaChargePerOrb - 1;
            int before = s.Bank.Count;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var c = ev.OfType<ManaChargedEvent>().First(x => x.Reason == "pincer");
            Assert.That(ev.Any(e => e is OrbMintedEvent o && o.Reason == "pincer"), Is.True);
            Assert.That(s.Bank.Count >= before + 1, Is.True);
            Assert.That(s.ManaCharge, Is.InRange(0, BattleConfig.ManaChargePerOrb - 1));
            Assert.That(c.Total, Is.EqualTo((BattleConfig.ManaChargePerOrb - 1 + c.Amount) % BattleConfig.ManaChargePerOrb));
        }

        [Test]
        public void A_killing_pincer_earns_mana_by_hp_taken_not_per_hero()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            Assert.That(ev.Any(e => e is DiedEvent), Is.True);
            var charge = ev.OfType<ManaChargedEvent>().Single(c => c.Reason == "pincer");
            Assert.That(charge.Amount, Is.EqualTo((int)System.Math.Round(BattleConfig.PincerChargePerHp))); // the whole foe: share 1, two heroes ×1
            Assert.That(ev.OfType<OrbMintedEvent>().Count(o => o.Reason == "pincer"), Is.EqualTo(charge.Amount / BattleConfig.ManaChargePerOrb)); // one per 100, not one per hero
        }

        [Test] // GG-T112: several orbs minted by one action each carry their own slot, so the fly-in fans into consecutive slots
        public void Orbs_minted_together_carry_consecutive_bank_slots()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            s.ManaCharge = BattleConfig.ManaChargePerOrb - 1; // a killing pincer (+120) now rolls over twice
            int before = s.Bank.Count;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var landed = ev.OfType<OrbMintedEvent>().Where(o => o.Landed).ToList();
            Assert.That(landed.Count, Is.GreaterThanOrEqualTo(2), "the setup must mint more than one orb in one action");
            for (int i = 0; i < landed.Count; i++) Assert.That(landed[i].Slot, Is.EqualTo(before + i), $"orb {i} lands in its own slot");
            Assert.That(landed.Last().Slot, Is.EqualTo(s.Bank.Count - 1));
        }

        [Test] // GG-T112: an orb that overflows a full bank has no slot
        public void An_overflowing_orb_has_no_slot()
        {
            var db = Mini();
            // Two whole orbs at once, one slot left. An explicit grant, so fullness damping (§3.1.9, GG-T247) can't shrink it.
            db.Abilities["test_two_orbs"] = new AbilityDef { Id = "test_two_orbs", Name = "Two Orbs", Kind = AbilityKind.Skill, Shape = TargetShape.Self, Mode = TargetMode.Auto, Filter = TargetFilter.AllyOnly, GrantsOrbs = 2 };
            var (s, _) = Battle(db, Stage(("goblin", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            s.Bank.Add(s.Bank.Capacity - 1 - s.Bank.Count);
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = "test_two_orbs" };
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = k.Id, Slot = 0, Target = k.Pos });
            var minted = ev.OfType<OrbMintedEvent>().ToList();
            Assert.That(minted.First().Landed && minted.First().Slot == s.Bank.Capacity - 1, Is.True);
            Assert.That(minted.Skip(1).All(o => !o.Landed && o.Slot == -1), Is.True);
            Assert.That(minted.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test] // owner direction 2026-10-11: "more combos more xp and mana get generated" — chain depth already paid mana, now XP too
        public void A_kill_deeper_in_a_pincer_chain_earns_bonus_xp()
        {
            var db = Mini();
            // One cleric drag forms two pincers in sequence (knight+cleric, then cleric+mage): chainIndex 0 then 1.
            var (s, _) = Battle(db, Stage(("goblin", 1, 0), ("goblin", 3, 0)), ("knight", 0, 0), ("cleric", 2, 1), ("mage", 4, 0));
            var goblins = s.Units.Where(u => u.Def.Id == "goblin").ToList();
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 0)) });
            var pincers = ev.OfType<PincerFormedEvent>().OrderBy(p => p.ChainIndex).ToList();
            Assert.That(pincers.Count, Is.EqualTo(2));
            var loots = ev.OfType<LootEvent>().ToList();
            Assert.That(loots.Count, Is.EqualTo(2));
            foreach (var p in pincers)
            {
                var deadId = p.Opponents.Single();
                var goblin = goblins.Single(g => g.Id == deadId);
                var loot = loots.Single(l => l.FromUnitId == deadId);
                int baseXp = Formulas.XpReward(goblin);
                int expected = (int)System.Math.Round(baseXp * (1f + BattleConfig.ChargeChainBonus * p.ChainIndex));
                Assert.That(loot.Xp, Is.EqualTo(expected));
            }
            Assert.That(loots.Single(l => l.FromUnitId == pincers[1].Opponents.Single()).Xp >
                        loots.Single(l => l.FromUnitId == pincers[0].Opponents.Single()).Xp, Is.True, "deeper in the chain should pay out more XP");
        }

        [Test]
        public void The_first_battle_has_five_waves_of_rising_difficulty()
        {
            var c = Real;
            var first = c.CampaignStages()[0];
            Assert.That(first.Waves.Count, Is.EqualTo(5));
            int Weight(WaveDef w) => w.Enemies.Sum(e => { var d = c.Classes[e.Class]; return (int)(d.BaseStats.Vitality + d.BaseStats.Strength) * e.Level + (d.ChargeAbility != null ? 5 : 0) + (d.Trap != null ? 5 : 0); });
            for (int i = 1; i < first.Waves.Count; i++)
                Assert.That(Weight(first.Waves[i]) > Weight(first.Waves[i - 1]), Is.True, $"wave {i + 1} should be harder than wave {i}");
        }
    
        // ── Enemy brains (§14.6) ──

        [Test]
        public void Every_enemy_family_plays_differently()
        {
            var c = Real;
            var sig = new Dictionary<string, string>();
            foreach (var d in c.Classes.Values.Where(x => x.Team == Team.Enemy && !x.HasTag("Remains") && !x.HasTag("Egg")))
            {
                string fam = System.Text.RegularExpressions.Regex.Replace(d.Id, @"\d+$", "");
                string key = string.Join("|", d.Brain, EnemyPlanner.TargetingOf(new Unit { Def = d, Team = Team.Enemy, LevelStats = d.BaseStats.Clone() }), d.MoveRange, d.AttackRange,
                    d.ChargeAbility, d.Trap, d.Trail, d.OnHitBuff, d.DeathBurst, string.Join(",", d.Abilities.OrderBy(x => x)));
                if (sig.TryGetValue(key, out var other) && other != fam) Assert.Fail($"{fam} plays exactly like {other}: {key}");
                sig[key] = fam;
            }
            Assert.That(c.Classes.Values.Where(x => x.Team == Team.Enemy).Select(x => x.Brain).Distinct().Count() >= 18, Is.True);
        }

        private static ContentDb BrainDb(Brain brain, int move = 1)
        {
            var db = Mini();
            db.Classes["wolf"].Brain = brain;
            db.Classes["wolf"].MoveRange = move;
            db.Classes["wolf"].BaseStats.Vitality = 40;
            return db;
        }

        [Test]
        public void An_ambusher_waits_until_a_hero_comes_close()
        {
            var (s, _) = Battle(BrainDb(Brain.Ambusher), Stage(("wolf", 2, 0)), ("knight", 2, 6), ("cleric", 5, 7));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            Assert.That(EnemyPlanner.PlanStep(s, w), Is.EqualTo(w.Pos));
            Hero(s, "knight").Pos = new Coord(2, 2);
            Assert.That(EnemyPlanner.PlanStep(s, w), Is.Not.EqualTo(w.Pos));
        }

        [Test]
        public void A_sentinel_never_moves_and_a_sluggish_foe_moves_every_other_turn()
        {
            var (s, _) = Battle(BrainDb(Brain.Sentinel), Stage(("wolf", 2, 0)), ("knight", 2, 3), ("cleric", 5, 7));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            Assert.That(EnemyPlanner.PlanStep(s, w), Is.EqualTo(w.Pos));
            var (s2, _) = Battle(BrainDb(Brain.Sluggish), Stage(("wolf", 2, 0)), ("knight", 2, 3), ("cleric", 5, 7));
            var w2 = s2.Units.Single(u => u.Def.Id == "wolf");
            w2.TurnsTaken = 1; Assert.That(EnemyPlanner.PlanStep(s2, w2), Is.Not.EqualTo(w2.Pos));
            w2.TurnsTaken = 2; Assert.That(EnemyPlanner.PlanStep(s2, w2), Is.EqualTo(w2.Pos));
        }

        [Test]
        public void A_hopper_leaps_two_tiles_over_a_unit()
        {
            var (s, _) = Battle(BrainDb(Brain.Hopper), Stage(("wolf", 2, 0), ("tank", 2, 1)), ("knight", 2, 4), ("cleric", 5, 7));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            Assert.That(EnemyPlanner.PlanStep(s, w), Is.EqualTo(new Coord(2, 2)));
        }

        [Test]
        public void A_bomber_dives_where_it_touches_the_most_heroes()
        {
            var (s, _) = Battle(BrainDb(Brain.Bomber), Stage(("wolf", 2, 2)), ("knight", 1, 3), ("cleric", 3, 3), ("mage", 0, 0));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            Assert.That(EnemyPlanner.PlanStep(s, w), Is.EqualTo(new Coord(2, 3))); // between knight and cleric: touches two
        }

        [Test]
        public void A_skirmisher_strikes_then_darts_away()
        {
            var db = BrainDb(Brain.Skirmisher);
            db.Classes["wolf"].ChargeAbility = null;
            var (s, _) = Battle(db, Stage(("wolf", 2, 2)), ("knight", 2, 3), ("cleric", 5, 7));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            s.TurnEntryOf(w.Id).Ip = BattleConfig.BarLength - 1;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is AttackEvent a && a.AttackerId == w.Id), Is.True);
            Assert.That(w.DistanceTo(Hero(s, "knight")) >= 2, Is.True, "it backed off after the strike");
        }

        [Test]
        public void A_burrower_surfaces_beside_its_target()
        {
            var db = BrainDb(Brain.Burrower);
            db.Classes["wolf"].ChargeAbility = null;
            var (s, _) = Battle(db, Stage(("wolf", 0, 0)), ("knight", 4, 6), ("cleric", 5, 7));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            w.TurnsTaken = 2; // its next turn is the third: it burrows
            s.TurnEntryOf(w.Id).Ip = BattleConfig.BarLength - 1;
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(ev.Any(e => e is TeleportedEvent t && t.UnitId == w.Id), Is.True);
            Assert.That(s.Units.Where(u => u.IsHero).Any(h => w.IsAdjacentTo(h)), Is.True);
        }

        [Test]
        public void A_guardian_stays_by_its_ward()
        {
            var db = BrainDb(Brain.Guardian);
            var (s, _) = Battle(db, Stage(("wolf", 2, 1), ("witch", 0, 0)), ("knight", 2, 6), ("cleric", 5, 7));
            var w = s.Units.Single(u => u.Def.Id == "wolf");
            var step = EnemyPlanner.PlanStep(s, w);
            Assert.That(step.Manhattan(new Coord(0, 0)) <= w.Pos.Manhattan(new Coord(0, 0)), Is.True, "it closes on the witch, not the heroes");
        }
    
        // ── Armor plates (§13.5) and corpses (§14.7) ──

        private static ContentDb Plated(int plate = 40)
        {
            var db = Mini();
            db.Classes["goblin"].ArmorPlate = plate;
            db.Classes["goblin"].BaseStats.Vitality = 60;
            db.Classes["goblin"].DropTable = null;
            return db;
        }

        [Test]
        public void A_plate_takes_the_blow_from_its_facing_side_and_the_flank_hits_flesh()
        {
            var (s, _) = Battle(Plated(), Stage(("goblin", 2, 3), ("tank", 0, 0)), ("knight", 2, 4), ("cleric", 2, 0));
            var gob = s.Units.Single(u => u.Def.Id == "goblin");
            Assert.That(gob.Facing, Is.EqualTo(new Coord(0, 1))); // it faces the closest hero, below it
            int hp = gob.Hp;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((2, 1), (2, 2)) });
            Assert.That(ev.Any(e => e is PlateHitEvent ph && ph.AttackerId == Hero(s, "knight").Id), Is.True);
            Assert.That(ev.Any(e => e is DamagedEvent d && d.TargetId == gob.Id && d.SourceId == Hero(s, "cleric").Id), Is.True);
            Assert.That(ev.Any(e => e is DamagedEvent d && d.TargetId == gob.Id && d.SourceId == Hero(s, "knight").Id), Is.False);
            Assert.That(gob.PlateHp < gob.PlateMax, Is.True);
        }

        [Test]
        public void A_pincer_across_its_line_of_sight_skips_the_plate_and_a_broken_plate_stops_blocking()
        {
            var (s, _) = Battle(Plated(), Stage(("goblin", 2, 3), ("tank", 0, 0)), ("knight", 2, 4), ("cleric", 3, 3), ("mage", 0, 3));
            var gob = s.Units.Single(u => u.Def.Id == "goblin");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "mage").Id, Path = P((1, 3)) }); // mage (1,3) · goblin · cleric (3,3): from the sides
            Assert.That(ev.Any(e => e is PlateHitEvent), Is.False);
            Assert.That(ev.Any(e => e is DamagedEvent d && d.TargetId == gob.Id && d.Amount > 0), Is.True);

            var (s2, _) = Battle(Plated(1), Stage(("goblin", 2, 3), ("tank", 0, 0)), ("knight", 2, 4), ("cleric", 2, 0));
            var g2 = s2.Units.Single(u => u.Def.Id == "goblin");
            var ev2 = BattleEngine.Apply(s2, new MoveCommand { HeroId = Hero(s2, "cleric").Id, Path = P((2, 1), (2, 2)) });
            Assert.That(ev2.Any(e => e is PlateBrokenEvent), Is.True);
            Assert.That(g2.HasPlate, Is.False);
        }

        [Test]
        public void Spells_go_around_the_plate()
        {
            var db = Plated();
            var (s, _) = Battle(db, Stage(("goblin", 2, 3), ("tank", 0, 0)), ("mage", 2, 5), ("knight", 5, 7));
            var gob = s.Units.Single(u => u.Def.Id == "goblin");
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            s.Bank.Add(3);
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = m.Id, Slot = 0, Target = gob.Pos });
            Assert.That(ev.Any(e => e is PlateHitEvent), Is.False);
            Assert.That(ev.Any(e => e is DamagedEvent d && d.TargetId == gob.Id && d.Amount > 0), Is.True);
        }

        private static ContentDb WithCorpses()
        {
            var db = Mini();
            db.Classes["corpse"] = new ClassDef { Id = "corpse", Name = "Corpse", Team = Team.Enemy, Tags = { "Remains", "Corpse" }, BaseStats = new Stats { Vitality = 6 }, MoveRange = 0, Brain = Brain.Sentinel };
            db.Classes["goblin"].Remains = "corpse";
            db.Classes["goblin"].DropTable = null;
            return db;
        }

        [Test]
        public void A_fallen_foe_leaves_a_corpse_that_blocks_the_way_but_not_the_win()
        {
            var db = WithCorpses();
            var (s, _) = Battle(db, Stage(("goblin", 2, 3)), ("knight", 1, 3), ("cleric", 3, 4));
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            Assert.That(ev.Any(e => e is RemainsLeftEvent), Is.True);
            var corpse = s.Units.Single(u => u.Alive && u.IsCorpse);
            Assert.That(corpse.Pos, Is.EqualTo(new Coord(2, 3)));
            Assert.That(s.Phase, Is.EqualTo(BattlePhase.Victory)); // only a body left: the wave is clear
            Assert.That(s.Timeline.Any(e => e.OwnerId == corpse.Id), Is.False);
        }

        [Test]
        public void A_corpse_blocks_movement_and_rots_away()
        {
            var db = WithCorpses();
            var (s, _) = Battle(db, StageWaves(new[] { ("goblin", 2, 3) }, new[] { ("tank", 0, 0) }), ("knight", 1, 3), ("cleric", 3, 4));
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 3)) });
            var corpse = s.Units.Single(u => u.Alive && u.IsCorpse);
            var k = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = k.Id, Path = P((2, 3)) });
            Assert.That(ev.Any(e => e is StepBlockedEvent), Is.True);
            for (int i = 0; i < BattleConfig.CorpseDecayActions + 1 && corpse.Alive; i++)
                BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(corpse.Alive, Is.False, "it rotted away");
        }
    }
}
