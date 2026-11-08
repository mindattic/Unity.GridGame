using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>Region rules (GG-T359, BIBLE §14.20): data-driven board rules a theme or stage plays under — painted tile
    /// states, telegraphed timeline events, hooks — and the monster abilities, deaths and immunities that touch them.</summary>
    public class RegionRuleTests
    {
        private static RuleEffectDef Fx(RuleEffectKind kind, RuleArea area = RuleArea.Self) => new RuleEffectDef { Kind = kind, Area = area };

        /// <summary>Mini content plus one rule ("test-rule") with a hazard state, a blocking state that rubble becomes, and a strike event.</summary>
        private static ContentDb Db(params RuleHookDef[] hooks)
        {
            var db = Mini();
            var rule = new RegionRuleDef
            {
                Id = "test-rule",
                TileStates =
                {
                    new TileStateDef { Id = "embers", Hazard = true, Color = "#ff6020", Glyph = "*" },
                    new TileStateDef { Id = "cracked", Threshold = 2, Becomes = "pit" },
                    new TileStateDef { Id = "pit", Blocks = true },
                    new TileStateDef { Id = "fading", LifeSeconds = 0.5f },
                },
                Events =
                {
                    new RuleEventDef
                    {
                        Id = "strike", Seconds = 1f,
                        Effects = { new RuleEffectDef { Kind = RuleEffectKind.Damage, Area = RuleArea.EventTiles, Amount = 7, DamageType = DamageType.Lightning } },
                    },
                    new RuleEventDef
                    {
                        Id = "pulse", Seconds = 1f, Repeat = true, Hazard = false,
                        Effects = { new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(0, 4) }, State = "embers" } },
                    },
                },
            };
            rule.Hooks.AddRange(hooks);
            RegionRuleCatalog.Index(db, new List<RegionRuleDef> { rule });
            return db;
        }

        private static StageDef RuledStage(params (string cls, int x, int y)[] enemies)
        {
            var st = Stage(enemies);
            st.Rules.Add("test-rule");
            return st;
        }

        private static RuleHookDef OnStart(params RuleEffectDef[] fx)
        {
            var h = new RuleHookDef { On = RuleHook.BattleStart };
            h.Effects.AddRange(fx);
            return h;
        }

        /// <summary>Guard (the party gives its window up) until <paramref name="done"/> holds; every event on the way.</summary>
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

        [Test]
        public void A_stage_without_rules_has_an_empty_region()
        {
            var (s, ev) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Row, Index = 3, State = "embers" })),
                Stage(("tank", 2, 0)), ("knight", 1, 7));
            Assert.That(s.Region.Rules, Is.Empty);
            Assert.That(s.Region.Tiles, Is.Empty);
            Assert.That(ev.OfType<RegionRuleEvent>(), Is.Empty);
        }

        [Test]
        public void Battle_start_paints_tiles_and_announces_the_rule()
        {
            var (s, ev) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Row, Index = 3, State = "embers" })),
                RuledStage(("tank", 2, 0)), ("knight", 1, 7));
            Assert.That(s.Region.Rules, Is.EqualTo(new[] { "test-rule" }));
            Assert.That(ev.OfType<RegionRuleEvent>().Single().RuleId, Is.EqualTo("test-rule"));
            Assert.That(s.Region.Tiles.Count, Is.EqualTo(BattleConfig.Cols));
            Assert.That(s.Region.At(new Coord(0, 3)).State, Is.EqualTo("embers"));
            Assert.That(ev.OfType<RegionTilesEvent>().Single().Change, Is.EqualTo("marked"));
            AssertInvariants(s);
        }

        [Test]
        public void A_blocking_state_is_never_painted_under_a_unit_and_nothing_spawns_on_it()
        {
            var (s, _) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Row, Index = 7, State = "pit" })),
                RuledStage(("tank", 2, 0)), ("knight", 1, 7));
            Assert.That(s.Region.At(new Coord(1, 7)), Is.Null, "the knight's tile stays open");
            Assert.That(s.Region.BlocksAt(new Coord(0, 7)), Is.True);
            Assert.That(s.Walkable(new Coord(0, 7)), Is.False, "a blocking state is a block");
            Assert.That(s.FreeForSpawn(new Coord(3, 7)), Is.False);
            AssertInvariants(s);
        }

        [Test]
        public void A_scheduled_event_rides_the_timeline_ahead_of_time_then_lands_on_its_tiles()
        {
            var (s, ev) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.Schedule, Area = RuleArea.Tiles, Tiles = { new Coord(1, 7) }, Event = "strike" })),
                RuledStage(("tank", 2, 0)), ("knight", 1, 7), ("cleric", 4, 7));
            var hazard = s.Timeline.Single(e => e.Kind == TimelineKind.Hazard);
            Assert.That(hazard.OwnerId, Is.EqualTo(0));
            Assert.That(hazard.HazardTiles, Is.EqualTo(new[] { new Coord(1, 7) }));
            Assert.That(BattleEngine.TicksUntilAct(s, hazard), Is.EqualTo(BattleConfig.TicksPerSecond), "shows on the bar a full second ahead");
            Assert.That(ev.OfType<HazardQueuedEvent>().Single().EventId, Is.EqualTo("strike"));
            var knight = Hero(s, "knight");
            Assert.That(RegionBoard.DangerAt(s, knight.Pos, knight), Is.GreaterThan(0f), "the bot reads the telegraph");
            int hp = knight.Hp, clericHp = Hero(s, "cleric").Hp;
            var all = GuardUntil(s, () => s.Timeline.All(e => e.Kind != TimelineKind.Hazard));
            Assert.That(all.OfType<HazardLandedEvent>().Single().EventId, Is.EqualTo("strike"));
            Assert.That(knight.Hp, Is.EqualTo(hp - 7), "the strike lands on its telegraphed tile");
            Assert.That(Hero(s, "cleric").Hp, Is.EqualTo(clericHp), "and nowhere else");
        }

        [Test]
        public void A_repeating_event_rearms_on_the_bar()
        {
            var (s, _) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.Schedule, Area = RuleArea.Tiles, Tiles = { new Coord(0, 4) }, Event = "pulse" })),
                RuledStage(("tank", 2, 0)), ("knight", 1, 7));
            var entry = s.Timeline.Single(e => e.Kind == TimelineKind.Hazard);
            GuardUntil(s, () => s.Region.At(new Coord(0, 4)) != null);
            Assert.That(s.Region.At(new Coord(0, 4)).State, Is.EqualTo("embers"));
            Assert.That(s.Timeline.Contains(entry), Is.True, "Repeat keeps the same icon");
            Assert.That(entry.RemainingTicks, Is.InRange(1, entry.TotalCastTicks));
            Assert.That(RegionBoard.DangerAt(s, new Coord(5, 0), Hero(s, "knight")), Is.EqualTo(0f));
        }

        [Test]
        public void Adjusting_and_cancelling_rule_timers()
        {
            var db = Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.Schedule, Area = RuleArea.Tiles, Tiles = { new Coord(1, 7) }, Event = "strike" }));
            db.Abilities["stall"] = new AbilityDef
            {
                Id = "stall", Name = "Stall", Kind = AbilityKind.Skill, Shape = TargetShape.Self, Mode = TargetMode.Auto, Filter = TargetFilter.Any,
                RuleEffects = { new RuleEffectDef { Kind = RuleEffectKind.AdjustEvents, Event = "strike", Seconds = 2f } },
            };
            db.Abilities["dispel"] = new AbilityDef
            {
                Id = "dispel", Name = "Dispel", Kind = AbilityKind.Skill, Shape = TargetShape.Self, Mode = TargetMode.Auto, Filter = TargetFilter.Any,
                RuleEffects = { new RuleEffectDef { Kind = RuleEffectKind.AdjustEvents, Cancel = true } },
            };
            var setup = new BattleSetup { Stage = RuledStage(("tank", 2, 0)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 7), Bar = { "stall" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(4, 7), Bar = { "dispel" } });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var hazard = s.Timeline.Single(e => e.Kind == TimelineKind.Hazard);
            int before = hazard.RemainingTicks;
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = 0, Target = Hero(s, "knight").Pos });
            Assert.That(ev.OfType<HazardRetimedEvent>().Single().Cancelled, Is.False);
            Assert.That(hazard.RemainingTicks, Is.GreaterThan(before), "two seconds later, less the action's own time");
            ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "cleric").Id, Slot = 0, Target = Hero(s, "cleric").Pos });
            Assert.That(ev.OfType<HazardRetimedEvent>().Any(r => r.Cancelled), Is.True);
            Assert.That(s.Timeline.Any(e => e.Kind == TimelineKind.Hazard), Is.False);
            AssertInvariants(s);
        }

        [Test]
        public void Entering_a_hazard_tile_runs_the_EnterTile_hook_and_immune_classes_shrug_it_off()
        {
            var enter = new RuleHookDef { On = RuleHook.EnterTile, Team = RuleTeam.Hero, State = "embers" };
            enter.Effects.Add(new RuleEffectDef { Kind = RuleEffectKind.Damage, Units = RuleUnits.Subject, Amount = 5, DamageType = DamageType.Fire });
            var db = Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(1, 6), new Coord(4, 6) }, State = "embers" }), enter);
            db.Classes["cleric"].RuleImmune.Add("embers");
            var (s, _) = Battle(db, RuledStage(("tank", 2, 0)), ("knight", 1, 7), ("cleric", 4, 7));
            var knight = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            int kHp = knight.Hp, cHp = cleric.Hp;
            BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 6)) });
            Assert.That(knight.Hp, Is.EqualTo(kHp - 5));
            BattleEngine.Apply(s, new MoveCommand { HeroId = cleric.Id, Path = P((4, 6)) });
            Assert.That(cleric.Hp, Is.EqualTo(cHp), "immune to the embers state");
            Assert.That(RegionBoard.DangerAt(s, new Coord(4, 6), cleric), Is.EqualTo(0f));
            Assert.That(RegionBoard.DangerAt(s, new Coord(4, 6), knight), Is.GreaterThan(0f));
            AssertInvariants(s);
        }

        [Test]
        public void A_counter_reaching_its_threshold_becomes_the_next_state_but_never_under_a_unit()
        {
            var enter = new RuleHookDef { On = RuleHook.EnterTile, State = "cracked" };
            enter.Effects.Add(new RuleEffectDef { Kind = RuleEffectKind.ModifyTiles, Area = RuleArea.Self, State = "cracked", Amount = 1 });
            var db = Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(1, 6), new Coord(1, 5) }, State = "cracked", Amount = 1 }), enter);
            var (s, _) = Battle(db, RuledStage(("tank", 5, 0)), ("knight", 1, 7), ("cleric", 4, 7));
            var knight = Hero(s, "knight");
            BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = P((1, 6)) });
            var m = s.Region.At(new Coord(1, 6));
            Assert.That(m.State, Is.EqualTo("cracked"), "a pit never opens under the one standing on it");
            Assert.That(m.Counter, Is.EqualTo(2));
            Assert.That(s.Walkable(knight.Pos), Is.True);
            AssertInvariants(s);
        }

        [Test]
        public void Tile_marks_with_a_lifetime_fade_on_the_clock()
        {
            var (s, _) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(0, 4) }, State = "fading" })),
                RuledStage(("tank", 2, 0)), ("knight", 1, 7));
            Assert.That(s.Region.At(new Coord(0, 4)), Is.Not.Null);
            var ev = GuardUntil(s, () => s.Region.At(new Coord(0, 4)) == null);
            Assert.That(ev.OfType<RegionTilesEvent>().Any(t => t.Change == "cleared" && t.State == "fading"), Is.True);
        }

        [Test]
        public void A_monster_ability_and_a_death_can_paint_the_board()
        {
            var db = Db(new RuleHookDef[0]);
            db.Classes["goblin"].OnDeathRule.Add(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Self, State = "embers" });
            db.Abilities["sow"] = new AbilityDef
            {
                Id = "sow", Name = "Sow", Kind = AbilityKind.Skill, Shape = TargetShape.SingleTile, Mode = TargetMode.PickTile, Filter = TargetFilter.EmptyOnly,
                RuleEffects = { new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Target, State = "embers" } },
            };
            var setup = new BattleSetup { Stage = Stage(("goblin", 2, 3), ("tank", 5, 0)), Seed = 7, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(1, 3), Bar = { "sow" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(3, 5) });
            var s = BattleEngine.Create(db, setup, new List<BattleEvent>());
            var knight = Hero(s, "knight");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = knight.Id, Slot = 0, Target = new Coord(0, 0) });
            Assert.That(s.Region.At(new Coord(0, 0))?.State, Is.EqualTo("embers"), "rules work on a rule-less stage when a monster brings them");
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text == "Missed!"), Is.False, "a board-only ability misses nobody");
            // The goblin falls to a pincer (knight left of it, cleric closes on the right).
            var gob = s.Units.Single(u => u.Def.Id == "goblin");
            gob.Hp = 1;
            var at = gob.Pos;
            BattleEngine.Apply(s, new MoveCommand { HeroId = Hero(s, "cleric").Id, Path = P((3, 4), (3, 3)) });
            Assert.That(gob.Alive, Is.False);
            Assert.That(s.Region.At(at)?.State, Is.EqualTo("embers"), "OnDeathRule paints where it fell");
            AssertInvariants(s);
        }

        [Test]
        public void Anchors_place_move_and_pull_units()
        {
            var db = Db(OnStart(
                new RuleEffectDef { Kind = RuleEffectKind.PlaceAnchor, Area = RuleArea.Tiles, Tiles = { new Coord(1, 2) }, Anchor = "maw" },
                new RuleEffectDef { Kind = RuleEffectKind.MoveAnchor, Anchor = "maw", Move = RuleMove.Down, Amount = 1 },
                new RuleEffectDef { Kind = RuleEffectKind.MoveUnits, Units = RuleUnits.All, Team = RuleTeam.Hero, Anchor = "maw", Move = RuleMove.Toward, Amount = 2 },
                new RuleEffectDef { Kind = RuleEffectKind.SetFlag, Flag = RuleFlags.MaskTimeline }));
            var (s, ev) = Battle(db, RuledStage(("tank", 5, 0)), ("knight", 1, 7));
            Assert.That(s.Region.Anchor("maw"), Is.EqualTo(new Coord(1, 3)));
            Assert.That(Hero(s, "knight").Pos, Is.EqualTo(new Coord(1, 5)), "dragged two tiles toward the anchor");
            Assert.That(ev.OfType<DisplacedEvent>().Count(d => d.UnitId == Hero(s, "knight").Id), Is.EqualTo(2));
            Assert.That(RegionBoard.MasksTimeline(s), Is.True);
            Assert.That(ev.OfType<RegionFlagEvent>().Single().On, Is.True);
        }

        [Test]
        public void A_hook_can_run_every_nth_time_and_on_turn_boundaries()
        {
            var turn = new RuleHookDef { On = RuleHook.TurnStart, Team = RuleTeam.Enemy, Every = 2 };
            turn.Effects.Add(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Self, State = "embers" });
            var (s, _) = Battle(Db(turn), RuledStage(("tank", 2, 0)), ("knight", 1, 7));
            var tank = s.Units.Single(u => u.Def.Id == "tank");
            GuardUntil(s, () => tank.TurnsTaken >= 1);
            Assert.That(s.Region.At(tank.Pos), Is.Null, "the first turn start doesn't count yet");
            GuardUntil(s, () => tank.TurnsTaken >= 2);
            Assert.That(s.Region.At(tank.Pos)?.State, Is.EqualTo("embers"), "the second does");
        }

        [Test]
        public void The_region_is_cloned_deep_for_previews()
        {
            var (s, _) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, Area = RuleArea.Tiles, Tiles = { new Coord(0, 4) }, State = "cracked", Amount = 1 })),
                RuledStage(("tank", 2, 0)), ("knight", 1, 7));
            var copy = s.Clone();
            copy.Region.At(new Coord(0, 4)).Counter = 9;
            copy.Region.Flags.Add("x");
            Assert.That(s.Region.At(new Coord(0, 4)).Counter, Is.EqualTo(1));
            Assert.That(s.Region.HasFlag("x"), Is.False);
        }

        [Test]
        public void Validate_reports_broken_rule_references()
        {
            var db = Db(new RuleHookDef { On = RuleHook.BattleStart, Every = 0, Effects = { new RuleEffectDef { Kind = RuleEffectKind.MarkTiles, State = "nope" } } });
            db.TryRegionRule("test-rule").Events.Add(new RuleEventDef { Id = "instant", Seconds = 0f });
            db.Stages["t"] = new StageDef { Id = "t", Rules = { "no-such-rule" } };
            db.StageOrder.Add(db.Stages["t"]);
            db.Classes["wolf"].RuleImmune.Add("ghost-state");
            var p = new List<string>();
            RegionRuleCatalog.Validate(db, p);
            Assert.That(p, Has.Some.Contains("unknown tile state 'nope'"));
            Assert.That(p, Has.Some.Contains("Every must be at least 1"));
            Assert.That(p, Has.Some.Contains("Seconds must be positive"));
            Assert.That(p, Has.Some.Contains("unknown region rule 'no-such-rule'"));
            Assert.That(p, Has.Some.Contains("RuleImmune 'ghost-state'"));
        }

        [Test]
        public void Real_content_rule_names_come_from_Prose_keys()
        {
            var keys = ProseText.Keys(Real);
            foreach (var r in Real.RegionRuleOrder)
            {
                Assert.That(keys.TryGetValue("rule:" + r.Id, out var kind) && kind == "description", Is.True, r.Id);
                Assert.That(string.IsNullOrEmpty(r.Name), Is.False, $"{r.Id}: its name is joined from Prose");
            }
            Assert.That(ProseText.NarrativeFields.ContainsKey("regionrules"), Is.True, "rule names/descriptions may never sit in regionrules.json");
        }

        [Test]
        public void The_bot_steps_off_a_telegraphed_tile()
        {
            var (s, _) = Battle(Db(OnStart(new RuleEffectDef { Kind = RuleEffectKind.Schedule, Area = RuleArea.Tiles, Tiles = { new Coord(1, 7) }, Event = "strike", Seconds = 3f })),
                RuledStage(("tank", 5, 0)), ("knight", 1, 7));
            var move = new GridGame.Core.Bot.HeroBot(3).BestMove(s);
            Assert.That(move, Is.Not.Null);
            Assert.That(move.Path.Last(), Is.Not.EqualTo(new Coord(1, 7)));
            Assert.That(RegionBoard.DangerAt(s, move.Path.Last(), Hero(s, "knight")), Is.EqualTo(0f), "it ends somewhere the strike won't land");
        }
    }
}
