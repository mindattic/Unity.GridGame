using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Cracking floors (GG-T368, BIBLE §14.21), owner 2026-11-08: "cracking floors which open up holes to fall into
    /// that takes off some life and respawns actor next to hole (what if there are no free spaces? figure it out)".
    /// A crack counts steps, turns and blows; at its threshold it crumbles and a cave-in rides the timeline; the hole it
    /// opens drops whoever stands there (or is shoved in) for a share of their HP onto the nearest free tile.</summary>
    public class CrackingFloorTests
    {
        private static RuleEffectDef Crack() => new RuleEffectDef { Kind = RuleEffectKind.ModifyTiles, Area = RuleArea.Self, State = "cracked", Amount = 1 };

        /// <summary>Mini content plus a "floor" rule shaped like the real crackingfloor: cracked (2) → crumbling (schedules a
        /// 1 s cave-in) → hole (blocks, a pit, 20% of max HP, closes after 2 s).</summary>
        private static ContentDb Db(params RuleEffectDef[] atStart)
        {
            var db = Mini();
            var rule = new RegionRuleDef
            {
                Id = "floor",
                TileStates =
                {
                    new TileStateDef { Id = "cracked", Threshold = 2, Becomes = "crumbling" },
                    new TileStateDef { Id = "crumbling", OnMark = { new RuleEffectDef { Kind = RuleEffectKind.Schedule, Area = RuleArea.Self, Event = "cavein" } } },
                    new TileStateDef { Id = "hole", Blocks = true, Pit = true, FallDamage = 0.2f, LifeSeconds = 2f },
                },
                Events =
                {
                    new RuleEventDef
                    {
                        Id = "cavein", Seconds = 1f,
                        Effects =
                        {
                            new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.EventTiles, State = "hole" },
                            new RuleEffectDef { Kind = RuleEffectKind.ClearTiles, Area = RuleArea.EventTiles, State = "crumbling" },
                        },
                    },
                },
                Hooks =
                {
                    new RuleHookDef { On = RuleHook.EnterTile, State = "cracked", Effects = { Crack() } },
                    new RuleHookDef { On = RuleHook.Hit, State = "cracked", Effects = { Crack() } },
                },
            };
            if (atStart.Length > 0) rule.Hooks.Add(new RuleHookDef { On = RuleHook.BattleStart, Effects = atStart.ToList() });
            RegionRuleCatalog.Index(db, new List<RegionRuleDef> { rule });
            return db;
        }

        private static RuleEffectDef Paint(string state, params (int x, int y)[] tiles)
        {
            var e = new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, State = state };
            foreach (var t in tiles) e.Tiles.Add(new Coord(t.x, t.y));
            return e;
        }

        private static StageDef Ruled(params (string cls, int x, int y)[] enemies)
        {
            var st = Stage(enemies);
            st.Rules.Add("floor");
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

        private static int FallCost(Unit u, float share) => System.Math.Max(1, (int)System.Math.Round(u.MaxHp * share));

        [Test]
        public void A_crack_crumbles_at_its_threshold_and_the_cave_in_rides_the_timeline_first()
        {
            var (s, _) = Battle(Db(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(1, 6) }, State = "cracked", Amount = 1 }),
                Ruled(("tank", 5, 0)), ("knight", 1, 7), ("cleric", 4, 7));
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 6)) });
            Assert.That(s.Region.At(new Coord(1, 6)).State, Is.EqualTo("crumbling"), "the second count crumbles it");
            Assert.That(knight.Pos, Is.EqualTo(new Coord(1, 6)), "no hole yet: the floor only gives when its cave-in lands");
            var cave = s.Timeline.Single(e => e.Kind == TimelineKind.Hazard);
            Assert.That(cave.RuleEvent, Is.EqualTo("cavein"));
            Assert.That(cave.HazardTiles, Is.EqualTo(new[] { new Coord(1, 6) }));
            Assert.That(ev.OfType<HazardQueuedEvent>().Single().EventId, Is.EqualTo("cavein"));
            Assert.That(RegionBoard.DangerAt(s, knight.Pos, knight), Is.GreaterThan(0f), "the bot reads the telegraph");
            AssertInvariants(s);
        }

        [Test]
        public void A_hole_opens_under_a_unit_who_falls_loses_life_and_climbs_out_next_to_it()
        {
            var (s, _) = Battle(Db(Paint("crumbling", (2, 6))), Ruled(("tank", 5, 0)), ("knight", 2, 6), ("cleric", 5, 7));
            var knight = Hero(s, "knight");
            int hp = knight.Hp;
            var ev = GuardUntil(s, () => s.Timeline.All(e => e.Kind != TimelineKind.Hazard));
            var fell = ev.OfType<FellEvent>().Single();
            Assert.That(fell.UnitId, Is.EqualTo(knight.Id));
            Assert.That(fell.Hole, Is.EqualTo(new Coord(2, 6)));
            Assert.That(fell.SwappedId, Is.EqualTo(-1));
            Assert.That(knight.Pos.Manhattan(new Coord(2, 6)), Is.EqualTo(1), "it climbs out on a tile next to the hole");
            Assert.That(knight.Pos, Is.EqualTo(fell.To));
            Assert.That(knight.Hp, Is.LessThanOrEqualTo(hp - FallCost(knight, 0.2f)), "the fall takes a share of its life");
            Assert.That(s.Region.At(new Coord(2, 6))?.State, Is.EqualTo("hole"));
            Assert.That(s.Walkable(new Coord(2, 6)), Is.False, "an open hole is a block");
            Assert.That(s.FreeForSpawn(new Coord(2, 6)), Is.False, "nothing spawns or is placed in it");
            AssertInvariants(s);
        }

        [Test]
        public void With_no_free_tile_beside_the_hole_the_faller_takes_the_nearest_free_tile_outward()
        {
            var db = Db(Paint("crumbling", (0, 7)));
            var stage = Ruled(("tank", 5, 0));
            // Rocks all round the hole's corner; the nearest open floor is (3, 7), three tiles out.
            stage.Blocks.AddRange(new[] { new Coord(1, 7), new Coord(0, 6), new Coord(1, 6), new Coord(2, 7), new Coord(0, 5), new Coord(2, 6) });
            var (s, _) = Battle(db, stage, ("knight", 0, 7), ("cleric", 5, 7));
            var knight = Hero(s, "knight");
            GuardUntil(s, () => s.Timeline.All(e => e.Kind != TimelineKind.Hazard));
            Assert.That(knight.Pos.Manhattan(new Coord(0, 7)), Is.EqualTo(3), "breadth-first, ring by ring: the first ring with a free tile");
            Assert.That(s.Walkable(knight.Pos), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void On_a_full_board_the_faller_swaps_with_the_nearest_unit_and_the_hole_fills_in()
        {
            var db = Db(Paint("crumbling", (0, 7)));
            var stage = Ruled(("tank", 5, 0));
            for (int y = 0; y < BattleConfig.Rows; y++)
                for (int x = 0; x < BattleConfig.Cols; x++)
                {
                    var c = new Coord(x, y);
                    if (c != new Coord(0, 7) && c != new Coord(1, 7) && c != new Coord(5, 0)) stage.Blocks.Add(c);
                }
            var (s, _) = Battle(db, stage, ("knight", 0, 7), ("cleric", 1, 7));
            var knight = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            var ev = GuardUntil(s, () => s.Timeline.All(e => e.Kind != TimelineKind.Hazard));
            var fell = ev.OfType<FellEvent>().Single();
            Assert.That(fell.SwappedId, Is.EqualTo(cleric.Id), "the nearest unit, not the far tank");
            Assert.That(fell.Filled, Is.True);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(1, 7)));
            Assert.That(cleric.Pos, Is.EqualTo(new Coord(0, 7)), "jostled onto the hole, which filled in under it");
            Assert.That(s.Region.At(new Coord(0, 7)), Is.Null);
            AssertInvariants(s);
        }

        [Test]
        public void A_shove_into_an_open_hole_drops_the_foe_instead_of_slamming_it()
        {
            var db = Db(Paint("hole", (2, 3)));
            var setup = new BattleSetup { Stage = Ruled(("tank", 2, 4)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 5), Bar = { "shield_bash" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            Assert.That(RegionBoard.ShoveFloorValue(s, tank, new Coord(0, -1), 2), Is.EqualTo(1f), "the bot sees the shove ends in the hole");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = 0, Target = tank.Pos });
            var fell = ev.OfType<FellEvent>().Single();
            Assert.That(fell.UnitId, Is.EqualTo(tank.Id));
            Assert.That(fell.From, Is.EqualTo(new Coord(2, 4)));
            Assert.That(fell.Hole, Is.EqualTo(new Coord(2, 3)));
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text == "Slam!"), Is.False, "into the pit, not against a wall");
            Assert.That(tank.Pos.Manhattan(new Coord(2, 3)), Is.EqualTo(1));
            Assert.That(ev.OfType<DamagedEvent>().Any(d => d.TargetId == tank.Id && d.Note == "Fell!"), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void A_blow_on_a_cracked_tile_cracks_it()
        {
            var (s, _) = Battle(Db(Paint("cracked", (2, 3))), Ruled(("tank", 2, 3)), ("knight", 1, 3), ("cleric", 3, 5));
            // The cleric closes the pincer on the right: two blows land on the tank's cracked tile.
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(s.Region.At(new Coord(2, 3)).State, Is.EqualTo("crumbling"), "each landed blow counts");
            Assert.That(s.Timeline.Any(e => e.Kind == TimelineKind.Hazard && e.RuleEvent == "cavein"), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void Holes_block_a_walk_and_close_after_their_lifetime()
        {
            var (s, _) = Battle(Db(Paint("hole", (1, 6))), Ruled(("tank", 5, 0)), ("knight", 1, 7), ("cleric", 4, 7));
            var knight = Hero(s, "knight");
            BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 6)) });
            Assert.That(knight.Pos, Is.EqualTo(new Coord(1, 7)), "no walking into a hole");
            var ev = GuardUntil(s, () => s.Region.At(new Coord(1, 6)) == null);
            Assert.That(s.Region.At(new Coord(1, 6)), Is.Null, "this rule's holes close (LifeSeconds); 0 would keep them open");
            Assert.That(ev.OfType<RegionTilesEvent>().Any(t => t.Change == "cleared" && t.State == "hole"), Is.True);
            Assert.That(s.Walkable(new Coord(1, 6)), Is.True);
        }

        [Test]
        public void An_immune_unit_and_a_boss_never_fall_and_the_floor_holds()
        {
            var db = Db(Paint("crumbling", (2, 6)));
            db.Classes["knight"].RuleImmune.Add("hole");
            var (s, _) = Battle(db, Ruled(("tank", 5, 0)), ("knight", 2, 6), ("cleric", 5, 7));
            var knight = Hero(s, "knight");
            var ev = GuardUntil(s, () => s.Timeline.All(e => e.Kind != TimelineKind.Hazard));
            Assert.That(ev.OfType<FellEvent>(), Is.Empty);
            Assert.That(knight.Pos, Is.EqualTo(new Coord(2, 6)));
            Assert.That(s.Region.At(new Coord(2, 6)), Is.Null, "the cave-in can't open under it, so the crumbling tile settles back to floor");
            AssertInvariants(s);
        }

        [Test]
        public void A_crack_about_to_give_reads_as_danger()
        {
            var (s, _) = Battle(Db(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(0, 4), new Coord(1, 4) }, State = "cracked" }),
                Ruled(("tank", 5, 0)), ("knight", 1, 7));
            var knight = Hero(s, "knight");
            s.Region.At(new Coord(1, 4)).Counter = 1;
            Assert.That(RegionBoard.DangerAt(s, new Coord(0, 4), knight), Is.EqualTo(0f), "a fresh crack is safe to cross");
            Assert.That(RegionBoard.DangerAt(s, new Coord(1, 4), knight), Is.GreaterThan(0f), "one more count and it crumbles");
        }

        [Test]
        public void Validate_wants_a_pit_to_block()
        {
            var db = Db();
            var shaft = new TileStateDef { Id = "shaft", Pit = true, FallDamage = 2f };
            db.TryRegionRule("floor").TileStates.Add(shaft);
            db.TileStates["shaft"] = shaft;
            var p = new List<string>();
            RegionRuleCatalog.Validate(db, p);
            Assert.That(p, Has.Some.Contains("a Pit must also Block"));
            Assert.That(p, Has.Some.Contains("FallDamage outside 0..1"));
        }

        [Test]
        public void The_real_cracking_floor_is_valid_placed_and_named_in_Prose()
        {
            var rule = Real.TryRegionRule("crackingfloor");
            Assert.That(rule, Is.Not.Null);
            Assert.That(rule.Name, Is.EqualTo("Cracking Floor"), "rule:crackingfloor in Content/Text");
            Assert.That(Real.TryTileState("hole").Pit, Is.True);
            Assert.That(Real.TryTileState("hole").Blocks, Is.True);
            Assert.That(Real.TryTileState("cracked").Becomes, Is.EqualTo("crumbling"));
            Assert.That(rule.Hooks.Any(h => h.On == RuleHook.Hit), Is.True, "blows crack it");
            Assert.That(rule.Hooks.Any(h => h.On == RuleHook.TurnEnd), Is.True, "standing on it cracks it");
            Assert.That(rule.Hooks.Any(h => h.On == RuleHook.EnterTile), Is.True, "stepping on it cracks it");
            foreach (var id in new[] { "cave-03", "cathedral-02" })
                Assert.That(Real.Stages[id].Rules, Does.Contain("crackingfloor"), id);
            var p = new List<string>();
            RegionRuleCatalog.Validate(Real, p);
            Assert.That(p, Is.Empty, string.Join("\n", p));
        }
    }
}
