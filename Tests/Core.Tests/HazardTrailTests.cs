using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Hazard trails (GG-T370, BIBLE §14.22), owner 2026-11-08: "some enemies drop slime trails when they move, some
    /// fire, some ice, some land mines... always a way to defeat them". Each hazard is a tile state; the counters are
    /// GG-T371's elements.json tile reactions (fire melts ice, ice douses fire, fire burns slime, any hit sets a mine off).</summary>
    public class HazardTrailTests
    {
        private static RuleEffectDef Paint(string state, params (int x, int y)[] tiles)
        {
            var e = new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, State = state };
            foreach (var t in tiles) e.Tiles.Add(new Coord(t.x, t.y));
            return e;
        }

        /// <summary>Mini content, a "trails" rule shaped like the real ones, and a two-row element table.</summary>
        private static ContentDb Db(params RuleEffectDef[] atStart)
        {
            var db = Mini();
            var rule = new RegionRuleDef
            {
                Id = "trails",
                TileStates =
                {
                    new TileStateDef { Id = "fire", Hazard = true, Element = DamageType.Fire },
                    new TileStateDef { Id = "ice", Hazard = true, Element = DamageType.Ice },
                    new TileStateDef { Id = "slime", Hazard = true },
                    new TileStateDef { Id = "dust" },
                    new TileStateDef { Id = "mine", Hazard = true },
                    new TileStateDef { Id = "armedmine", OnMark = { new RuleEffectDef { Kind = RuleEffectKind.Schedule, Area = RuleArea.Self, Radius = 1, Event = "blast" } } },
                },
                Events =
                {
                    new RuleEventDef
                    {
                        Id = "blast", Seconds = 1f,
                        Effects =
                        {
                            new RuleEffectDef { Kind = RuleEffectKind.Damage, Area = RuleArea.EventTiles, Amount = 14, DamageType = DamageType.Fire },
                            new RuleEffectDef { Kind = RuleEffectKind.ClearTiles, Area = RuleArea.EventTiles, State = "armedmine" },
                        },
                    },
                },
                Hooks =
                {
                    new RuleHookDef { On = RuleHook.EnterTile, State = "mine", Effects = { new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Self, State = "armedmine" } } },
                },
            };
            if (atStart.Length > 0) rule.Hooks.Add(new RuleHookDef { On = RuleHook.BattleStart, Effects = atStart.ToList() });
            RegionRuleCatalog.Index(db, new List<RegionRuleDef> { rule });
            TileReactionDef Clear(string on) => new TileReactionDef { On = on, Kind = TileReactionKind.Clear };
            var arm = new TileReactionDef { On = "mine", Kind = TileReactionKind.Becomes, Into = "armedmine" };
            db.Elements = new ElementTableDef
            {
                Elements =
                {
                    new ElementDef { Type = DamageType.Fire, Counters = { DamageType.Ice }, Tiles = { Clear("slime"), Clear("ice"), arm } },
                    new ElementDef { Type = DamageType.Ice, Counters = { DamageType.Fire }, Tiles = { Clear("fire"), arm } },
                },
            };
            db.Abilities["ember_toss"] = new AbilityDef
            {
                Id = "ember_toss", Name = "Ember Toss", Kind = AbilityKind.Skill, BaseDamage = 5, DamageType = DamageType.Fire,
                Shape = TargetShape.SingleTile, Mode = TargetMode.PickTile, Filter = TargetFilter.EnemyOnly,
            };
            db.Abilities["frost_toss"] = new AbilityDef
            {
                Id = "frost_toss", Name = "Frost Toss", Kind = AbilityKind.Skill, BaseDamage = 5, DamageType = DamageType.Ice,
                Shape = TargetShape.SingleTile, Mode = TargetMode.PickTile, Filter = TargetFilter.EnemyOnly,
            };
            return db;
        }

        private static StageDef Ruled(params (string cls, int x, int y)[] enemies)
        {
            var st = Stage(enemies);
            st.Rules.Add("trails");
            return st;
        }

        private static List<BattleEvent> GuardUntil(BattleState s, System.Func<bool> done, int max = 40)
        {
            var all = new List<BattleEvent>();
            for (int i = 0; i < max && !s.IsOver && !done(); i++)
            {
                all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
                AssertInvariants(s);
            }
            return all;
        }

        private static BattleState WithBar(ContentDb db, StageDef stage, string ability, out Unit knight)
        {
            var setup = new BattleSetup { Stage = stage, Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 7), Bar = { ability } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            knight = Hero(s, "knight");
            return s;
        }

        [Test]
        public void A_trail_monster_paints_the_tile_it_steps_off_and_brings_its_rule_into_play()
        {
            var db = Db();
            db.Classes["wolf"].TrailRule.Add(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Self, State = "fire", KeepMarks = true });
            db.Classes["wolf"].RuleImmune.Add("fire");
            var (s, _) = Battle(db, Stage(("wolf", 2, 0)), ("knight", 2, 7));
            Assert.That(s.Region.Rules, Is.Empty, "a rule-less stage");
            var wolf = s.Units.Single(u => u.Def.Id == "wolf");
            var start = wolf.Pos;
            var ev = GuardUntil(s, () => wolf.Pos != start);
            Assert.That(wolf.Pos, Is.Not.EqualTo(start));
            Assert.That(s.Region.At(start)?.State, Is.EqualTo("fire"), "the tile it left burns");
            Assert.That(s.Region.Rules, Does.Contain("trails"), "the monster brought its rule with it");
            Assert.That(ev.OfType<RegionRuleEvent>().Any(r => r.RuleId == "trails"), Is.True, "and it is announced");
            Assert.That(RegionBoard.DangerAt(s, start, wolf), Is.EqualTo(0f), "its own trail is no danger to it");
            Assert.That(RegionBoard.DangerAt(s, start, Hero(s, "knight")), Is.GreaterThan(0f));
        }

        [Test]
        public void A_mine_layer_spaces_its_mines_out()
        {
            var db = Db();
            db.Classes["wolf"].TrailRule.Add(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Self, State = "mine", KeepMarks = true });
            db.Classes["wolf"].TrailEvery = 3;
            db.Classes["wolf"].RuleImmune.Add("mine");
            var (s, _) = Battle(db, Stage(("wolf", 2, 0)), ("knight", 2, 7));
            var wolf = s.Units.Single(u => u.Def.Id == "wolf");
            var start = wolf.Pos;
            GuardUntil(s, () => wolf.Pos != start);
            Assert.That(s.Region.Tiles.Count, Is.EqualTo(0), "the first step lays nothing");
            Assert.That(wolf.TrailSteps, Is.EqualTo(1));
        }

        [Test]
        public void Fire_melts_ice_ice_douses_fire_and_fire_burns_slime_away()
        {
            var (s, ev) = Battle(Db(
                    Paint("ice", (0, 4)), Paint("fire", (0, 4)),
                    Paint("fire", (1, 4)), Paint("ice", (1, 4)),
                    Paint("slime", (2, 4)), Paint("fire", (2, 4))),
                Ruled(("tank", 5, 0)), ("knight", 1, 7));
            Assert.That(s.Region.At(new Coord(0, 4)), Is.Null, "fire melts the ice and is spent");
            Assert.That(s.Region.At(new Coord(1, 4)), Is.Null, "ice douses the fire and is spent");
            Assert.That(s.Region.At(new Coord(2, 4)), Is.Null, "fire burns the slime away");
            Assert.That(ev.OfType<RegionTilesEvent>().Count(t => t.Change == "cleared"), Is.EqualTo(3));
        }

        [Test]
        public void A_trail_never_paints_over_another_mark()
        {
            var (s, _) = Battle(Db(Paint("dust", (0, 4)), new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(0, 4) }, State = "slime", KeepMarks = true }),
                Ruled(("tank", 5, 0)), ("knight", 1, 7));
            Assert.That(s.Region.At(new Coord(0, 4)).State, Is.EqualTo("dust"));
        }

        [Test]
        public void Stepping_on_a_mine_arms_it_and_the_blast_rides_the_timeline_first()
        {
            var (s, _) = Battle(Db(Paint("mine", (1, 6))), Ruled(("tank", 5, 0)), ("knight", 1, 7), ("cleric", 4, 7));
            var knight = Hero(s, "knight");
            int hp = knight.Hp;
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 6)) });
            Assert.That(s.Region.At(new Coord(1, 6)).State, Is.EqualTo("armedmine"));
            var blast = s.Timeline.Single(e => e.Kind == TimelineKind.Hazard);
            Assert.That(blast.RuleEvent, Is.EqualTo("blast"));
            Assert.That(blast.HazardTiles, Does.Contain(new Coord(1, 6)).And.Contain(new Coord(1, 5)), "the mine and the tiles round it");
            Assert.That(knight.Hp, Is.EqualTo(hp), "nothing lands until the icon reaches the edge");
            Assert.That(RegionBoard.DangerAt(s, knight.Pos, knight), Is.GreaterThan(0f));
            GuardUntil(s, () => s.Timeline.All(e => e.Kind != TimelineKind.Hazard));
            Assert.That(knight.Hp, Is.EqualTo(hp - 14), "the knight stayed on it");
            Assert.That(s.Region.At(new Coord(1, 6)), Is.Null, "a spent mine is gone");
        }

        [Test]
        public void Shoving_a_foe_onto_a_mine_sets_it_off()
        {
            var db = Db(Paint("mine", (2, 3)));
            var setup = new BattleSetup { Stage = Ruled(("tank", 2, 4)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 5), Bar = { "shield_bash" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = 0, Target = tank.Pos });
            Assert.That(ev.OfType<HazardQueuedEvent>().Any(h => h.EventId == "blast"), Is.True, "the shoved foe ran the mine's EnterTile hook");
            AssertInvariants(s);
        }

        [Test]
        public void Hitting_a_mine_from_range_arms_it_and_a_frost_hit_douses_fire()
        {
            var s = WithBar(Db(Paint("mine", (3, 2))), Ruled(("tank", 5, 0)), "ember_toss", out var knight);
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = new Coord(3, 2) });
            Assert.That(s.Region.At(new Coord(3, 2))?.State, Is.EqualTo("armedmine"), "any hit of any element sets it off");
            Assert.That(ev.OfType<HazardQueuedEvent>().Any(h => h.EventId == "blast"), Is.True);
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text == "Missed!"), Is.False, "a tile pick on the mine is a fair pick");

            s = WithBar(Db(Paint("fire", (3, 2))), Ruled(("tank", 5, 0)), "frost_toss", out knight);
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = new Coord(3, 2) });
            Assert.That(s.Region.At(new Coord(3, 2)), Is.Null, "ice douses the fire (elements.json Ice row)");
        }

        [Test]
        public void The_real_trails_are_placed_named_countered_and_always_beatable()
        {
            foreach (var (rule, state, cls, stage) in new[]
                     {
                         ("slimetrail", "slime", "slime02", "greenvalley-02"), ("firetrail", "fire", "hellspawn03", "desert-03"),
                         ("icetrail", "ice", "yeti", "cave-02"), ("landmines", "mine", "heavytrooper04", "still-03"),
                     })
            {
                var r = Real.TryRegionRule(rule);
                Assert.That(r, Is.Not.Null, rule);
                Assert.That(string.IsNullOrEmpty(r.Name), Is.False, $"rule:{rule} in Prose");
                Assert.That(Real.TryTileState(state).Hazard, Is.True, state);
                var c = Real.Classes[cls];
                Assert.That(c.TrailRule.Any(fx => fx.Kind == RuleEffectKind.MarkTiles && fx.State == state && fx.KeepMarks), Is.True, cls);
                Assert.That(c.RuleImmune, Does.Contain(state), $"{cls} walks its own trail");
                Assert.That(Real.Stages[stage].Rules, Does.Contain(rule), stage);
                Assert.That(Real.Stages[stage].Waves.Any(w => w.Enemies.Any(e => e.Class == cls)), Is.True, $"{cls} fights in {stage}");
                Assert.That(ElementCounters.CountersFor(Real, Real.Stages[stage]).Any(), Is.True, $"the camp bot finds a counter for {stage}");
            }
            Assert.That(Real.Classes["heavytrooper04"].TrailEvery, Is.EqualTo(3));
            Assert.That(Real.TryTileState("fire").Element, Is.EqualTo(DamageType.Fire));
            Assert.That(Real.TryTileState("ice").Element, Is.EqualTo(DamageType.Ice));
            var fire = Real.Elements.Of(DamageType.Fire).Tiles;
            Assert.That(ElementCounters.Match(fire, "ice")?.Kind, Is.EqualTo(TileReactionKind.Clear), "fire melts ice");
            Assert.That(ElementCounters.Match(fire, "slime")?.Kind, Is.EqualTo(TileReactionKind.Clear), "fire burns slime");
            Assert.That(ElementCounters.Match(Real.Elements.Of(DamageType.Ice).Tiles, "fire")?.Kind, Is.EqualTo(TileReactionKind.Clear), "ice douses fire");
            foreach (var row in Real.Elements.Elements)
                Assert.That(ElementCounters.Match(row.Tiles, "mine")?.Into, Is.EqualTo("armedmine"), $"a {row.Type} hit sets a mine off");
            var p = new List<string>();
            ElementCounters.Validate(Real, p);
            RegionRuleCatalog.Validate(Real, p);
            Assert.That(p, Is.Empty, string.Join("\n", p));
        }

        [Test]
        public void Validate_checks_trail_rules()
        {
            var db = Db();
            db.Classes["wolf"].TrailRule.Add(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, State = "lava" });
            db.Classes["wolf"].TrailEvery = 0;
            var p = new List<string>();
            RegionRuleCatalog.Validate(db, p);
            Assert.That(p, Has.Some.Contains("TrailRule").And.Contains("unknown tile state 'lava'"));
            Assert.That(p, Has.Some.Contains("TrailEvery must be at least 1"));
        }
    }
}
