using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T190 — shoving as a deliberate tactic: while dragging, the preview shows where every displaced hero and
    /// enemy will slide and which pincers the drop completes (and whether a shove completed them). These tests hold
    /// the preview to the real result: what the drag shows is exactly what the drop does.
    /// </summary>
    public class ShovePreviewTests
    {
        /// <summary>Where every unit stands once the move itself is over (before any pincer resolves or the clock runs).</summary>
        private static Dictionary<int, Coord> AfterMove(Dictionary<int, Coord> start, List<BattleEvent> events)
        {
            var pos = new Dictionary<int, Coord>(start);
            foreach (var e in events)
            {
                if (e is PincerFormedEvent || e is TimelineAdvancedEvent || e is AbilityRefusedEvent) break;
                if (e is SteppedEvent st) pos[st.UnitId] = st.To;
                else if (e is DisplacedEvent d) pos[d.UnitId] = d.To;
            }
            return pos;
        }

        private static Dictionary<int, Coord> Positions(BattleState s) => s.Units.Where(u => u.Alive).ToDictionary(u => u.Id, u => u.Pos);

        private static List<PincerFormedEvent> HeroPincers(List<BattleEvent> events) =>
            events.TakeWhile(e => !(e is TimelineAdvancedEvent)).OfType<PincerFormedEvent>().Where(p => p.Team == Team.Hero).ToList();

        /// <summary>The preview and the real drop agree on every tile, every displaced actor and the pincers formed.</summary>
        private static void AssertPreviewMatchesDrop(BattleState s, int heroId, List<Coord> path, string label)
        {
            var start = Positions(s);
            var pv = BattleEngine.PreviewMove(s, heroId, path);
            var shove = ShovePreview.Of(s, heroId, pv);
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = heroId, Path = new List<Coord>(path) });
            var after = AfterMove(start, ev);

            foreach (var id in start.Keys)
                Assert.That(pv.Positions[id], Is.EqualTo(after[id]), $"{label}: unit {id} previewed on {pv.Positions[id]} but the drop left it on {after[id]}");
            var moved = start.Keys.Where(id => id != heroId && after[id] != start[id]).OrderBy(id => id).ToList();
            Assert.That(shove.Displaced.Select(d => d.UnitId).OrderBy(id => id), Is.EqualTo(moved), $"{label}: displaced actors");
            foreach (var d in shove.Displaced)
            {
                Assert.That(d.From, Is.EqualTo(start[d.UnitId]), $"{label}: unit {d.UnitId} slides from its current tile");
                Assert.That(d.To, Is.EqualTo(after[d.UnitId]), $"{label}: unit {d.UnitId} slides to where the drop leaves it");
            }

            var formed = HeroPincers(ev);
            Assert.That(formed.Count > 0, Is.EqualTo(pv.Pincers.Count > 0), $"{label}: preview showed {pv.Pincers.Count} pincers, the drop formed {formed.Count}");
            if (formed.Count == 0) return;
            // Chain order is the preview's order; the first link always resolves (nothing has died yet).
            var first = formed[0];
            var p = pv.Pincers[0];
            Assert.That((first.Attacker1, first.Attacker2), Is.EqualTo((p.A.Id, p.B.Id)), $"{label}: first pincer's attackers");
            Assert.That(first.Opponents, Is.EqualTo(p.Opponents.Select(o => o.Id).ToList()), $"{label}: first pincer's opponents");
            Assert.That(first.Supporters.OrderBy(x => x), Is.EqualTo(p.SupportersA.Concat(p.SupportersB).Select(u => u.Id).Distinct().OrderBy(x => x)), $"{label}: first pincer's supporters");
            Assert.That(first.Attacker1At.At, Is.EqualTo(pv.Positions[p.A.Id]), $"{label}: attacker 1 tile");
            Assert.That(first.Attacker2At.At, Is.EqualTo(pv.Positions[p.B.Id]), $"{label}: attacker 2 tile");
            foreach (var o in first.OpponentsAt)
                Assert.That(o.At, Is.EqualTo(pv.Positions[o.UnitId]), $"{label}: opponent {o.UnitId} tile");
            // Every link the drop resolved was in the preview.
            foreach (var f in formed)
                Assert.That(pv.Pincers.Any(x => x.A.Id == f.Attacker1 && x.B.Id == f.Attacker2), Is.True, $"{label}: {f} wasn't previewed");
        }

        [Test]
        public void Preview_lists_every_displaced_hero_and_enemy_with_where_it_slides()
        {
            // bible §1.1: K C T T → (drag K east twice) C T K T: the cleric and the first tank both slide.
            var db = Mini();
            var (s, _) = Battle(db, Stage(("tank", 2, 0), ("tank", 3, 0)), ("knight", 0, 0), ("cleric", 1, 0));
            var knight = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            var tank = At(s, 2, 0);
            var shove = ShovePreview.Of(s, knight.Id, BattleEngine.PreviewMove(s, knight.Id, P((1, 0), (2, 0))));
            Assert.That(shove.Displaced, Has.Exactly(2).Items);
            var c = shove.Displaced.Single(d => d.UnitId == cleric.Id);
            Assert.That((c.From, c.To, c.IsEnemy), Is.EqualTo((new Coord(1, 0), new Coord(0, 0), false)));
            var t = shove.Displaced.Single(d => d.UnitId == tank.Id);
            Assert.That((t.From, t.To, t.IsEnemy), Is.EqualTo((new Coord(2, 0), new Coord(1, 0), true)));
            Assert.That(shove.IsShoved(knight.Id), Is.False, "the held hero isn't a shoved actor");
            AssertPreviewMatchesDrop(s, knight.Id, P((1, 0), (2, 0)), "K C T T");
            AssertInvariants(s);
        }

        [Test]
        public void Shoving_an_enemy_into_line_previews_the_pincer_the_drop_then_forms()
        {
            // K(1,6) · gap(2,6) · C(2,7), goblin at (2,5) above the gap. K steps into the gap and up through the goblin:
            // the goblin drops into (2,6), between K(2,5) and C(2,7).
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 2, 5)), ("knight", 1, 6), ("cleric", 2, 7));
            var knight = Hero(s, "knight");
            var goblin = At(s, 2, 5);
            var path = P((2, 6), (2, 5));
            var pv = BattleEngine.PreviewMove(s, knight.Id, path);
            var shove = ShovePreview.Of(s, knight.Id, pv);
            Assert.That(pv.Pincers, Has.Exactly(1).Items);
            Assert.That(shove.CompletedByShove(pv.Pincers[0]), Is.True);
            Assert.That(shove.ShovePincerCount, Is.EqualTo(1));
            Assert.That(shove.EnemiesShovedIntoPincers(), Is.EqualTo(new[] { goblin.Id }));
            var d = shove.Displaced.Single();
            Assert.That((d.UnitId, d.From, d.To, d.IsEnemy), Is.EqualTo((goblin.Id, new Coord(2, 5), new Coord(2, 6), true)));

            var start = Positions(s);
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = knight.Id, Path = path });
            var formed = HeroPincers(ev).Single();
            Assert.That(formed.Opponents, Is.EqualTo(new[] { goblin.Id }));
            Assert.That(formed.OpponentsAt.Single().At, Is.EqualTo(new Coord(2, 6)));
            Assert.That(AfterMove(start, ev)[goblin.Id], Is.EqualTo(new Coord(2, 6)));
            AssertInvariants(s);
        }

        [Test]
        public void Shoving_a_hero_onto_the_end_of_a_line_counts_as_a_shove_pincer()
        {
            // C(1,0) above K(1,1) · goblin(2,1) · M(3,1). K walks up through the cleric: the cleric slides into (1,1)
            // and takes K's place at the end of the line — the held hero migrated an ally into the pincer.
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 2, 1)), ("knight", 1, 1), ("cleric", 1, 0), ("mage", 3, 1));
            var knight = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            var path = P((1, 0));
            var pv = BattleEngine.PreviewMove(s, knight.Id, path);
            var shove = ShovePreview.Of(s, knight.Id, pv);
            var p = pv.Pincers.Single();
            Assert.That(new[] { p.A.Id, p.B.Id }, Does.Contain(cleric.Id));
            Assert.That(new[] { p.A.Id, p.B.Id }, Does.Not.Contain(knight.Id));
            Assert.That(shove.CompletedByShove(p), Is.True);
            Assert.That(p.SupportersA.Concat(p.SupportersB).Select(u => u.Id), Does.Contain(knight.Id), "the held hero now supports the shoved partner");
            AssertPreviewMatchesDrop(s, knight.Id, path, "partner shove");
            AssertInvariants(s);
        }

        [Test]
        public void Walking_up_to_a_foe_is_a_pincer_but_not_a_shove()
        {
            var db = Mini();
            var (s, _) = Battle(db, Stage(("goblin", 1, 0)), ("knight", 0, 1), ("cleric", 2, 0));
            var knight = Hero(s, "knight");
            var pv = BattleEngine.PreviewMove(s, knight.Id, P((0, 0)));
            var shove = ShovePreview.Of(s, knight.Id, pv);
            Assert.That(pv.Pincers, Has.Exactly(1).Items);
            Assert.That(shove.Displaced, Is.Empty);
            Assert.That(shove.CompletedByShove(pv.Pincers[0]), Is.False);
            Assert.That(shove.ShovePincerCount, Is.EqualTo(0));
            AssertPreviewMatchesDrop(s, knight.Id, P((0, 0)), "walk-up");
        }

        private static List<HeroSpec> Party(ContentDb c) =>
            new[] { "paladin", "barbarian", "cleric" }
                .Select(id => new HeroSpec { ClassId = id, Level = 1, Bar = new List<string>(c.Class(id).DefaultLoadout) })
                .ToList();

        private static BattleState Fresh(ContentDb c, string stageId, int seed) =>
            BattleEngine.Create(c, new BattleSetup { Stage = c.Stage(stageId), Seed = seed, UnlockedSlots = 2, Party = Party(c) }, new List<BattleEvent>());

        private static readonly Coord[] Dirs = { new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1) };

        private static IEnumerable<List<Coord>> Paths(Coord from, int maxLen)
        {
            if (maxLen <= 0) yield break;
            foreach (var d in Dirs)
            {
                var next = from + d;
                if (!BattleState.InBounds(next)) continue;
                yield return new List<Coord> { next };
                foreach (var rest in Paths(next, maxLen - 1))
                {
                    var path = new List<Coord> { next };
                    path.AddRange(rest);
                    yield return path;
                }
            }
        }

        [TestCase("greenvalley-02")]
        [TestCase("greenvalley-03")]
        public void An_early_stage_opens_on_a_foe_to_shove_into_a_pincer(string stageId)
        {
            // GG-T190: the opening wave puts a foe above the gap between the lead hero (1,6) and the third hero (2,7):
            // step into the gap and up through it, and it drops between them. No drag pincers anything without a shove.
            var c = Real;
            var s = Fresh(c, stageId, 3);
            Assert.That(At(s, 1, 6)?.IsHero, Is.True, "lead hero on its default start");
            Assert.That(At(s, 2, 7)?.IsHero, Is.True, "third hero on its default start");
            var foe = At(s, 2, 5);
            Assert.That(foe != null && foe.Team == Team.Enemy, Is.True, "a foe above the gap");
            Assert.That(At(s, 2, 6), Is.Null, "the gap is open");

            var lead = At(s, 1, 6);
            var path = P((2, 6), (2, 5));
            var pv = BattleEngine.PreviewMove(s, lead.Id, path);
            var shove = ShovePreview.Of(s, lead.Id, pv);
            Assert.That(pv.Pincers, Is.Not.Empty, "the lesson drag pincers");
            Assert.That(shove.EnemiesShovedIntoPincers(), Does.Contain(foe.Id));

            // Every pincer any hero can make with a short drag here needs a shove: the stage teaches the tactic.
            foreach (var h in s.Units.Where(u => u.IsHero && u.Alive).ToList())
                foreach (var tryPath in Paths(h.Pos, 3))
                {
                    var tpv = BattleEngine.PreviewMove(s, h.Id, tryPath);
                    var tsh = ShovePreview.Of(s, h.Id, tpv);
                    foreach (var tp in tpv.Pincers)
                        Assert.That(tsh.CompletedByShove(tp), Is.True, $"{h.Name} via {string.Join(" ", tryPath)} pincers without a shove");
                }

            var start = Positions(s);
            var ev = BattleEngine.Apply(s, new MoveCommand { HeroId = lead.Id, Path = path });
            Assert.That(AfterMove(start, ev)[foe.Id], Is.EqualTo(new Coord(2, 6)));
            Assert.That(HeroPincers(ev).First().Opponents, Does.Contain(foe.Id));
        }

        [Test]
        public void Preview_matches_the_real_drop_across_random_drags_on_the_early_stages()
        {
            var c = Real;
            int drags = 0;
            foreach (var stageId in new[] { "greenvalley-01", "greenvalley-02", "greenvalley-03" })
                for (int seed = 1; seed <= 40; seed++)
                {
                    var rng = new Random(seed * 101 + stageId.Length * 7 + stageId[stageId.Length - 1]);
                    var s = Fresh(c, stageId, seed);
                    var heroes = s.Units.Where(u => u.IsHero && u.Alive).ToList();
                    var hero = heroes[rng.Next(heroes.Count)];
                    var path = new List<Coord>();
                    var at = hero.Pos;
                    int len = 1 + rng.Next(6);
                    for (int i = 0; i < len; i++)
                    {
                        var options = Dirs.Select(d => at + d).Where(BattleState.InBounds).ToList();
                        at = options[rng.Next(options.Count)];
                        path.Add(at);
                    }
                    AssertPreviewMatchesDrop(s, hero.Id, path, $"{stageId} seed {seed} {hero.Name} via {string.Join(" ", path)}");
                    drags++;
                }
            Assert.That(drags, Is.EqualTo(120));
        }
    }
}
