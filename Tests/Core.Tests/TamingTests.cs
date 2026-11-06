using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using Newtonsoft.Json;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T303 (§25.11.3): the Monster Tamer's Tame (and the Dog Whistle) turn a wild, non-Sentient monster to the party's
    /// side for a few of its own turns. It acts on its own AI against the party's foes with its own timeline icon, carries
    /// the Pincer keyword, fills its bond each turn it fights, reverts when the tame wears off, and — bond full, standing at a
    /// victory — asks to join; a domesticated monster persists in the save and fights as an independent ally.
    /// </summary>
    public class TamingTests
    {
        /// <summary>Mini content plus a tame skill, a whistle item and a bone pile.</summary>
        private static ContentDb Db(int wolfBond = 0, int tameTurns = 3)
        {
            var db = Mini();
            db.Abilities["tame"] = new AbilityDef { Id = "tame", Name = "Tame", Kind = AbilityKind.Skill, CooldownTurns = 2, Tame = 0.6f, TameTurns = tameTurns, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            db.Abilities["whistle_call"] = new AbilityDef { Id = "whistle_call", Name = "Dog Whistle", Kind = AbilityKind.Item, Tame = 0.35f, TameTurns = 2, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly };
            db.Items["whistle"] = new ItemDef { Id = "whistle", Name = "Dog Whistle", Type = ItemType.Consumable, OnUseAbility = "whistle_call", MaxStack = 3, BaseCost = 150 };
            db.Classes["bones"] = new ClassDef { Id = "bones", Name = "Bones", Team = Team.Enemy, Tags = { "Remains" }, BaseStats = new Stats { Vitality = 5, Speed = 1 }, MoveRange = 0 };
            db.Classes["wolf"].TameBond = wolfBond;
            return db;
        }

        /// <summary>Knight (bar: tame, then 2 whistles) at (0,3); a wolf at (2,3); goblins at (1,3) and far away at (5,0).</summary>
        private static BattleState Board(ContentDb db, int seed, params (string cls, int x, int y)[] enemies)
        {
            var setup = new BattleSetup { Stage = Stage(enemies.Length > 0 ? enemies : new[] { ("goblin", 1, 3), ("wolf", 2, 3), ("goblin", 5, 0) }), Seed = seed, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(0, 3), Bar = new List<string> { "tame", "item:whistle:2" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            return BattleEngine.Create(db, setup, new List<BattleEvent>());
        }

        private static Unit Wolf(BattleState s) => s.Units.First(u => u.Def.Id == "wolf");

        /// <summary>The first seed whose Tame (slot 0 by default) lands on the wolf; the state just after it.</summary>
        private static (BattleState s, List<BattleEvent> ev) Tamed(ContentDb db, int slot = 0)
        {
            for (int seed = 1; seed < 200; seed++)
            {
                var s = Board(db, seed);
                var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = slot, Target = Wolf(s).Pos });
                if (ev.Any(e => e is TamedEvent)) return (s, ev);
            }
            Assert.Fail("no seed tamed the wolf");
            return default;
        }

        /// <summary>Puts the wolf's turn icon right at the left edge so the next clock tick is its own turn.</summary>
        private static void WolfActsNext(BattleState s)
        {
            foreach (var e in s.Timeline.Where(e => e.Kind == TimelineKind.Turn)) e.Ip = 0;
            s.TurnEntryOf(Wolf(s).Id).Ip = BattleConfig.BarLength - 1;
        }

        [Test]
        public void Tame_chance_follows_hp_and_level_and_the_roll_is_deterministic()
        {
            var db = Db();
            var s = Board(db, 7);
            var knight = Hero(s, "knight");
            var wolf = Wolf(s);
            float fresh = Taming.Chance(db.Abilities["tame"], knight, wolf);
            wolf.Hp = 1;
            float beaten = Taming.Chance(db.Abilities["tame"], knight, wolf);
            Assert.That(beaten, Is.GreaterThan(fresh), "a nearly beaten monster is easier to tame");
            wolf.Hp = wolf.MaxHp;
            wolf.Level = knight.Level + 10;
            Assert.That(Taming.Chance(db.Abilities["tame"], knight, wolf), Is.LessThan(fresh), "a higher-level monster is harder");
            Assert.That(Taming.Chance(db.Abilities["whistle_call"], knight, Wolf(Board(db, 7))), Is.LessThan(fresh), "the whistle is weaker than the Tamer's Tame");

            // Same seed, same command: the same outcome, event for event.
            for (int seed = 1; seed <= 12; seed++)
            {
                string Run()
                {
                    var b = Board(db, seed);
                    var ev = BattleEngine.Apply(b, new UseSlotCommand { HeroId = Hero(b, "knight").Id, Slot = 0, Target = Wolf(b).Pos });
                    return string.Join("|", ev.Where(e => e is TamedEvent || e is TameFailedEvent));
                }
                Assert.That(Run(), Is.EqualTo(Run()));
            }
            // Both outcomes happen across seeds at ~30% (a fresh wolf, base 0.6 × 0.5).
            var outcomes = Enumerable.Range(1, 40).Select(seed =>
            {
                var b = Board(db, seed);
                return BattleEngine.Apply(b, new UseSlotCommand { HeroId = Hero(b, "knight").Id, Slot = 0, Target = Wolf(b).Pos }).Any(e => e is TamedEvent);
            }).ToList();
            Assert.That(outcomes, Does.Contain(true).And.Contain(false));
        }

        [Test]
        public void Sentient_foes_bosses_and_remains_are_refused()
        {
            var db = Db();
            var s = Board(db, 3, ("goblin", 1, 3), ("boss", 3, 0), ("bones", 2, 5), ("wolf", 5, 3));
            var tame = db.Abilities["tame"];
            var knight = Hero(s, "knight");
            Assert.That(Taming.WhyNot(s.Units.First(u => u.Def.Id == "goblin")), Is.EqualTo("Too clever to tame"));
            Assert.That(Taming.WhyNot(s.Units.First(u => u.Def.Id == "boss")), Is.EqualTo("Too mighty to tame"));
            Assert.That(Taming.WhyNot(s.Units.First(u => u.Def.Id == "bones")), Is.Not.Null);
            Assert.That(Taming.Tameable(Wolf(s)), Is.True);
            var picks = BattleEngine.ValidTargets(s, knight.Id, 0);
            Assert.That(picks, Is.EquivalentTo(new[] { Wolf(s).Pos }), "only the wild beast is a legal pick");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = new Coord(1, 3) });
            Assert.That(ev.OfType<AbilityRefusedEvent>().Any(), Is.True);
            Assert.That(s.Units.First(u => u.Def.Id == "goblin").Team, Is.EqualTo(Team.Enemy));
            Assert.That(knight.Cooldowns.ContainsKey("tame"), Is.False, "a refused pick spends nothing");
        }

        [Test]
        public void A_tamed_monster_joins_the_hero_team_with_the_pincer_keyword_and_pincers_with_heroes()
        {
            var (s, ev) = Tamed(Db());
            var wolf = Wolf(s);
            Assert.That(wolf.Team, Is.EqualTo(Team.Hero));
            Assert.That(wolf.Tamed && wolf.IsIndependent && wolf.HasPincerKeyword && wolf.CanPincer, Is.True);
            Assert.That(wolf.Def.HasTag("Sentient"), Is.False);
            Assert.That(s.TurnEntryOf(wolf.Id), Is.Not.Null, "it keeps its own timeline icon");
            // Knight (0,3) | goblin (1,3) | tamed wolf (2,3): a hero pincer.
            var p = PincerDetector.Detect(s, Team.Hero, Hero(s, "knight"));
            Assert.That(p.Any(x => (x.A == wolf || x.B == wolf) && x.Opponents.Any(o => o.Def.Id == "goblin")), Is.True);
            // Never commanded: no drag, no bar, not one of the heroes still to act.
            Assert.That(BattleEngine.CanMove(s, wolf.Id), Is.False);
            Assert.That(BattleEngine.HeroesLeftToAct(s), Does.Not.Contain(wolf));
        }

        [Test]
        public void On_its_own_turn_it_attacks_the_partys_foes_and_closes_a_pincer()
        {
            var (s, _) = Tamed(Db());
            var wolf = Wolf(s);
            WolfActsNext(s);
            var ev = Turn(s, new ShieldCommand());
            int turn = ev.FindIndex(e => e is TurnStartedEvent t && t.UnitId == wolf.Id);
            Assert.That(turn, Is.GreaterThanOrEqualTo(0), "the tamed wolf took its own turn");
            var after = ev.Skip(turn).ToList();
            // It stands at the far end of the knight's line over the goblin: it closes that pincer on its turn.
            Assert.That(after.OfType<PincerFormedEvent>().Any(p => p.Team == Team.Hero && (p.Attacker1 == wolf.Id || p.Attacker2 == wolf.Id)), Is.True);
            foreach (var a in after.OfType<AttackEvent>().Where(a => a.AttackerId == wolf.Id))
                Assert.That(s.UnitById(a.TargetId).Team, Is.Not.EqualTo(Team.Hero), "it never strikes the party");
            Assert.That(ev.OfType<BondGrewEvent>().Any(b => b.UnitId == wolf.Id && b.Bond == 1), Is.True);
        }

        [Test]
        public void It_charges_the_nearest_foe_through_anyone_in_its_way()
        {
            // Wolf alone at (2,3) after the tame with the near goblin gone: it walks toward the far goblin at (5,0).
            var (s, _) = Tamed(Db());
            var wolf = Wolf(s);
            var near = s.Units.First(u => u.Def.Id == "goblin" && u.Pos == new Coord(1, 3));
            near.Hp = 0; near.OnBoard = false;
            s.Timeline.RemoveAll(e => e.OwnerId == near.Id);
            var before = wolf.Pos.Manhattan(new Coord(5, 0));
            WolfActsNext(s);
            var ev = Turn(s, new ShieldCommand());
            Assert.That(ev.OfType<SteppedEvent>().Any(e => e.UnitId == wolf.Id), Is.True);
            Assert.That(wolf.Pos.Manhattan(new Coord(5, 0)), Is.LessThan(before));
        }

        [Test]
        public void The_tame_wears_off_and_the_monster_turns_back_where_it_stands()
        {
            var db = Db(wolfBond: 9, tameTurns: 1);
            var (s, _) = Tamed(db);
            var wolf = Wolf(s);
            WolfActsNext(s);
            var ev = Turn(s, new ShieldCommand());
            Assert.That(ev.OfType<TameEndedEvent>().Any(e => e.UnitId == wolf.Id), Is.True);
            Assert.That(wolf.Team, Is.EqualTo(Team.Enemy));
            Assert.That(wolf.Tamed || wolf.IsIndependent || wolf.HasPincerKeyword, Is.False);
            Assert.That(wolf.Bond, Is.EqualTo(1), "the bond it earned is kept for a re-tame");
            Assert.That(wolf.Alive, Is.True);
        }

        [Test]
        public void A_full_bond_keeps_it_and_at_victory_it_asks_to_join()
        {
            var db = Db(wolfBond: 1, tameTurns: 1);
            var (s, _) = Tamed(db);
            var wolf = Wolf(s);
            Assert.That(wolf.BondNeeded, Is.EqualTo(1));
            WolfActsNext(s);
            var ev = Turn(s, new ShieldCommand());
            Assert.That(ev.OfType<BondGrewEvent>().Any(b => b.Full), Is.True);
            Assert.That(wolf.Bonded && wolf.Team == Team.Hero, Is.True, "a full bond outlasts the tame's turns");

            // Win with it standing: it asks to join.
            foreach (var g in s.Units.Where(u => u.Team == Team.Enemy)) { g.Hp = 0; g.OnBoard = false; }
            s.Phase = BattlePhase.Victory;
            var save = new SaveData();
            var offers = Domestication.Offers(db, save, s);
            Assert.That(offers.Select(o => o.ClassId), Is.EqualTo(new[] { "wolf" }));
            var r = Domestication.Accept(db, save, offers[0]);
            Assert.That(r.Ok, Is.True, r.Message);
            var h = save.Hero("wolf");
            Assert.That(h.Domesticated, Is.True);
            Assert.That(save.Party, Does.Contain("wolf"));
            Assert.That(Domestication.Offers(db, save, s), Is.Empty, "one of each monster class");
        }

        [Test]
        public void A_bond_not_yet_full_makes_no_offer()
        {
            var db = Db(wolfBond: 5);
            var (s, _) = Tamed(db);
            s.Phase = BattlePhase.Victory;
            Assert.That(Domestication.Offers(db, new SaveData(), s), Is.Empty);
        }

        [Test]
        public void A_tamed_monster_alone_cannot_hold_the_field()
        {
            var (s, _) = Tamed(Db());
            foreach (var h in s.Units.Where(u => u.IsCommandable)) h.Hp = 1;
            Assert.That(Wolf(s).IsIndependent, Is.True);
            // Every hero falls: the party is beaten even with the wolf still standing.
            foreach (var h in s.Units.Where(u => u.IsCommandable)) { h.Hp = 0; h.OnBoard = false; }
            var ev = BattleEngine.Apply(s, new ShieldCommand());
            Assert.That(s.Phase == BattlePhase.Defeat || ev.Any(e => e is BattleEndedEvent b && !b.Victory), Is.True);
        }

        [Test]
        public void The_dog_whistle_tames_too_and_is_spent()
        {
            var db = Db();
            var (s, ev) = Tamed(db, slot: 1);
            var knight = Hero(s, "knight");
            Assert.That(ev.OfType<ItemUsedEvent>().Any(i => i.ItemId == "whistle"), Is.True);
            Assert.That(knight.Bar[1].Charges, Is.EqualTo(1));
            var tamed = ev.OfType<TamedEvent>().Single();
            Assert.That(tamed.Turns, Is.EqualTo(2), "the whistle's tame is shorter");
            Assert.That(Wolf(s).Team, Is.EqualTo(Team.Hero));
        }

        [Test]
        public void A_domesticated_monster_persists_in_the_save_and_fights_as_an_independent_ally()
        {
            var c = Real;
            var save = Progression.NewGame(c, "Tamer");
            var r = Domestication.Accept(c, save, new Domestication.Offer { ClassId = "wolf00", Level = 4 });
            Assert.That(r.Ok, Is.True, r.Message);
            var json = JsonConvert.SerializeObject(save, Formatting.Indented, ContentLoader.Settings);
            var back = SaveMigrations.Migrate(JsonConvert.DeserializeObject<SaveData>(json, ContentLoader.Settings));
            var h = back.Hero("wolf00");
            Assert.That(h, Is.Not.Null);
            Assert.That(h.Domesticated, Is.True);
            Assert.That(Progression.Level(h), Is.EqualTo(4));
            Assert.That(back.Party, Does.Contain("wolf00"));

            var s = BattleEngine.Create(c, BattleFactory.Build(c, back, c.CampaignStages().First().Id, 5), new List<BattleEvent>());
            var wolf = s.Units.First(u => u.Def.Id == "wolf00" && u.IsHero);
            Assert.That(wolf.Domesticated && wolf.IsIndependent && wolf.CanPincer, Is.True);
            Assert.That(s.TurnEntryOf(wolf.Id), Is.Not.Null, "it acts on its own icon");
            Assert.That(BattleEngine.CanMove(s, wolf.Id), Is.False);
            Assert.That(BattleEngine.HeroesLeftToAct(s), Does.Not.Contain(wolf));
        }

        [Test]
        public void Domesticated_growth_is_limited()
        {
            var h = new HeroSave { ClassId = "wolf00", Domesticated = true };
            Assert.That(Domestication.XpFor(h, 100), Is.EqualTo(50));
            h.TotalXp = Formulas.TotalXpForLevel(Domestication.LevelCap);
            Assert.That(Domestication.XpFor(h, 100), Is.EqualTo(0), "no growth past the cap");
            Assert.That(Domestication.XpFor(new HeroSave { ClassId = "knight" }, 100), Is.EqualTo(100));
            var c = Real;
            var save = Progression.NewGame(c, "Tamer");
            Domestication.Accept(c, save, new Domestication.Offer { ClassId = "wolf00", Level = 3 });
            var wolf = save.Hero("wolf00");
            Assert.That(Abilities.Learnable(c, wolf), Is.Empty, "no skills");
            Assert.That(Talents.Trees(c, wolf), Is.Empty, "no skill tree");
            Assert.That(Equipment.Equip(c, save, wolf, EquipmentSlot.Weapon, "eq_sword_rusty").Ok, Is.False, "no gear");
        }

        [Test]
        public void Real_content_ships_the_tamer_the_whistle_and_the_tameable_rule()
        {
            var c = Real;
            var tamer = c.Class("monstertamer");
            Assert.That(tamer.Team, Is.EqualTo(Team.Hero));
            Assert.That(tamer.DefaultLoadout, Does.Contain("tame"));
            Assert.That(c.Summon.Pool, Does.Contain("monstertamer"), "summoned at the Summon Circle");
            Assert.That(c.Ability("tame").Tame, Is.GreaterThan(c.Ability("dog_whistle_call").Tame));
            var whistle = c.Item("dog_whistle");
            Assert.That(whistle.SoldByVendor && whistle.OnUseAbility == "dog_whistle_call", Is.True);
            Assert.That(Shop.Stock(c), Does.Contain(whistle));
            Assert.That(c.Class("yeti").TameBond, Is.GreaterThan(Taming.BondFor(c.Class("wolf00"), 1)), "a rarer, stronger monster needs more bond");
            Assert.That(ContentLoader.Validate(c).Where(p => p.Contains("Tame") || p.Contains("monstertamer") || p.Contains("dog_whistle")), Is.Empty);
        }

        [Test]
        public void Validation_rejects_a_tame_that_isnt_a_single_picked_foe()
        {
            var db = Db();
            db.Abilities["bad_tame"] = new AbilityDef { Id = "bad_tame", Name = "Bad", Kind = AbilityKind.Spell, Tame = 1.5f, Shape = TargetShape.Square, Mode = TargetMode.PickTile, Filter = TargetFilter.EnemyOnly };
            var problems = ContentLoader.Validate(db).Where(p => p.StartsWith("ability bad_tame")).ToList();
            Assert.That(problems, Has.Count.EqualTo(4));
        }
    }
}
