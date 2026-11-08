using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// The heroes from the owner's art who are recruited as wanderers (§25.11.2.1; owner 2026-11-07: "met mid-battle, earn
    /// their trust, say what they want to hear, and they join"): each has a valid kit for its role, sits in the wanderer
    /// pool with a faction, has an Advice quiz, and carries its portrait's Figure and Thumbnail. Plus each hero's
    /// signature mechanic: the Herald's Essence (GG-T333).
    /// </summary>
    public class WandererHeroesTests
    {
        // ── shared checks ───────────────────────────────────────────────────────────

        private static void AssertKit(string id, string role, string signature, string homeTree)
        {
            var c = Real;
            Assert.That(ContentLoader.Validate(c), Is.Empty);
            var k = c.TryClass(id);
            Assert.That(k, Is.Not.Null, id);
            Assert.That(k.Team, Is.EqualTo(Team.Hero));
            Assert.That(k.Role, Is.EqualTo(role));
            Assert.That(k.Portrait, Is.EqualTo(id));
            foreach (var a in k.Abilities) Assert.That(c.TryAbility(a), Is.Not.Null, $"{id}: unknown ability {a}");
            foreach (var a in k.DefaultLoadout.Where(x => !x.StartsWith("item:"))) Assert.That(k.Abilities, Does.Contain(a), $"{id}: {a} in the bar but not learnable");
            Assert.That(k.DefaultLoadout, Does.Contain(signature), "the signature is on the starting bar");
            Assert.That(k.DefaultLoadout, Has.Count.EqualTo(5));
            Assert.That(k.HomeTree, Is.EqualTo(homeTree));
            Assert.That(c.TrySkillTree(k.HomeTree), Is.Not.Null);
            Assert.That(k.Support, Is.Not.Null, "backs pincers like every hero");
            // The Speed tier of the role: every healer and support but the White Ninja runs at 12.
            Assert.That(k.BaseStats.Speed, Is.EqualTo(12f));
        }

        private static void AssertWanderer(string id, string faction)
        {
            var c = Real;
            var s = Progression.NewGame(c, "W-" + id);
            Assert.That(Wanderers.Pool(c, s), Does.Contain(id), "met only as a wanderer (and the Diamond draw)");
            Assert.That(c.Summon.Pool, Does.Not.Contain(id));
            Assert.That(c.Mercenaries.Select(m => m.Class), Does.Not.Contain(id));
            Assert.That(c.FactionOf(id)?.Id, Is.EqualTo(faction), "its faction-mates bridge half trust");
            var scene = Wanderers.Scene(c, id);
            Assert.That(scene, Is.Not.Null);
            Assert.That(Wanderers.RightAnswer(scene), Is.InRange(0, 2));
        }

        private static void AssertQuiz(string id)
        {
            var c = Real;
            var quiz = c.Dialogues["talk_quiz_" + id];
            Assert.That(quiz.Trigger.Kind, Is.EqualTo(DialogueTriggerKind.Talk));
            Assert.That(quiz.Trigger.Id, Is.EqualTo(id));
            Assert.That(quiz.Nodes.SelectMany(n => n.Choices).Count(ch => ch.Effects.Any(e => e.Type == DialogueEffectType.GrantSkillPoint)), Is.EqualTo(1));
            Assert.That(ContentLoader.Validate(c).Where(p => p.Contains(quiz.Id)), Is.Empty);
        }

        private static void AssertArt(string id, (float x, float y, float w, float h) figure, (float zoom, float x, float y) thumb)
        {
            var k = Real.TryClass(id);
            Assert.That(File.Exists(Path.Combine(RepoRoot, "Assets", "Gaiden", "Resources", "Portraits", id + ".png")), Is.True);
            Assert.That(k.Figure, Is.Not.Null);
            Assert.That(k.Figure.X, Is.EqualTo(figure.x).Within(1e-4)); Assert.That(k.Figure.Y, Is.EqualTo(figure.y).Within(1e-4));
            Assert.That(k.Figure.W, Is.EqualTo(figure.w).Within(1e-4)); Assert.That(k.Figure.H, Is.EqualTo(figure.h).Within(1e-4));
            Assert.That(k.Thumbnail, Is.Not.Null);
            Assert.That(k.Thumbnail.Zoom, Is.EqualTo(thumb.zoom).Within(1e-3));
            Assert.That(k.Thumbnail.X, Is.EqualTo(thumb.x).Within(1e-4)); Assert.That(k.Thumbnail.Y, Is.EqualTo(thumb.y).Within(1e-4));
        }

        /// <summary>Mini content plus real abilities; a knight (bar: the given abilities) at (0,3), a cleric at (5,7), goblins.</summary>
        private static BattleState Board(ContentDb db, params string[] bar)
        {
            var setup = new BattleSetup { Stage = Stage(("goblin", 3, 0), ("goblin", 5, 0)), Seed = 3, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(0, 3), Bar = bar.ToList() });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            return BattleEngine.Create(db, setup, new List<BattleEvent>());
        }

        private static ContentDb WithReal(params string[] abilities)
        {
            var db = Mini();
            foreach (var a in abilities) db.Abilities[a] = Real.Abilities[a];
            return db;
        }

        // ── GG-T333 the Herald ──────────────────────────────────────────────────────

        [Test]
        public void Herald_is_a_healer_with_a_valid_kit() => AssertKit("herald", "Healer", "herald_lifegift", "cleric");

        [Test]
        public void Herald_is_recruited_as_a_wanderer_of_the_wild() => AssertWanderer("herald", "wild");

        [Test]
        public void Herald_has_an_advice_quiz() => AssertQuiz("herald");

        [Test]
        public void Herald_has_her_figure_and_thumbnail() =>
            AssertArt("herald", (0.0752f, 0.0391f, 0.8789f, 0.9238f), (2.844f, 0.4883f, 0.1592f));

        [Test]
        public void Lifegift_heals_an_ally_and_the_giver_pays_essence_but_never_her_last_hp()
        {
            var db = WithReal("herald_lifegift");
            var s = Board(db, "herald_lifegift");
            var giver = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            cleric.Hp = 5;
            int cost = Formulas.LifeCostHp(db.Abilities["herald_lifegift"], giver);
            Assert.That(cost, Is.EqualTo((int)System.Math.Round(giver.MaxHp * 0.12f)));
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = giver.Id, Slot = 0, Target = cleric.Pos });
            Assert.That(cleric.Hp, Is.GreaterThan(5), "the ally is mended");
            Assert.That(giver.Hp, Is.EqualTo(giver.MaxHp - cost), "she pays her essence");
            var paid = ev.OfType<DamagedEvent>().Single(d => d.TargetId == giver.Id);
            Assert.That(paid.Note, Is.EqualTo("Essence"));
            Assert.That(paid.SourceId, Is.EqualTo(-1), "a price, not a blow");
            AssertEventInvariants(ev);

            // At 2 HP she can still give, but only down to her last HP; at 1 HP the gift is free and she stands.
            giver.Hp = 2;
            Assert.That(Formulas.LifeCostHp(db.Abilities["herald_lifegift"], giver), Is.EqualTo(1));
            giver.Hp = 1;
            Assert.That(Formulas.LifeCostHp(db.Abilities["herald_lifegift"], giver), Is.EqualTo(0));
        }

        // ── GG-T337 the Aetherist ───────────────────────────────────────────────────

        [Test]
        public void Aetherist_is_a_healer_with_a_valid_kit() => AssertKit("aetherist", "Healer", "aetherist_aetherpour", "cleric");

        [Test]
        public void Aetherist_is_recruited_as_a_wanderer_of_the_fen_dark() => AssertWanderer("aetherist", "fendark");

        [Test]
        public void Aetherist_has_an_advice_quiz() => AssertQuiz("aetherist");

        [Test]
        public void Aetherist_has_her_figure_and_thumbnail() =>
            AssertArt("aetherist", (0.0264f, 0.0332f, 0.8877f, 0.9434f), (2.816f, 0.4880f, 0.1087f));

        [Test]
        public void Aether_pour_heals_an_ally_and_pours_an_orb_into_the_bank()
        {
            var db = WithReal("aetherist_aetherpour");
            Assert.That(db.Abilities["aetherist_aetherpour"].PoursOrbs, Is.True);
            var s = Board(db, "aetherist_aetherpour");
            var caster = Hero(s, "knight");
            var cleric = Hero(s, "cleric");
            cleric.Hp = 5;
            int orbs = s.Bank.Count;
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = caster.Id, Slot = 0, Target = cleric.Pos });
            Assert.That(cleric.Hp, Is.GreaterThan(5), "the ally is mended");
            Assert.That(ev.OfType<OrbMintedEvent>().Count(o => o.Reason == "aether"), Is.EqualTo(1));
            Assert.That(s.Bank.Count, Is.EqualTo(System.Math.Min(s.Bank.Capacity, orbs + 1)));
            Assert.That(caster.Cooldowns["aetherist_aetherpour"], Is.GreaterThan(0), "a Skill on a cooldown, not a free orb every action");
            AssertEventInvariants(ev);
        }

        [Test]
        public void A_bare_orb_grant_still_resolves_on_its_own()
        {
            var meditate = Real.Abilities["meditate"];
            Assert.That(meditate.PoursOrbs, Is.False, "Meditate is the whole ability: no target needed");
            var db = WithReal("meditate");
            var s = Board(db, "meditate");
            int orbs = s.Bank.Count;
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = 0, Target = Hero(s, "knight").Pos });
            Assert.That(s.Bank.Count, Is.EqualTo(System.Math.Min(s.Bank.Capacity, orbs + 1)));
            Assert.That(ev.OfType<OrbMintedEvent>().Count(), Is.EqualTo(1));
        }

        // ── GG-T336 the Tactician ───────────────────────────────────────────────────

        [Test]
        public void Tactician_is_a_support_with_a_valid_kit() => AssertKit("tactician", "Support", "tactician_countermarch", "alchemist");

        [Test]
        public void Tactician_is_recruited_as_a_wanderer_of_the_free_holds() => AssertWanderer("tactician", "holds");

        [Test]
        public void Tactician_has_an_advice_quiz() => AssertQuiz("tactician");

        [Test]
        public void Tactician_has_her_figure_and_thumbnail() =>
            AssertArt("tactician", (0.2314f, 0.0078f, 0.5205f, 0.9854f), (2.690f, 0.4917f, 0.1162f));

        /// <summary>Goblin at (3,0) acting next (3 s), goblin at (5,0) far off (5.5 s); both on the bar.</summary>
        private static (BattleState s, Unit soon, Unit late) Line()
        {
            var s = Board(WithReal("tactician_countermarch"), "tactician_countermarch");
            var soon = At(s, 3, 0); var late = At(s, 5, 0);
            foreach (var e in s.Timeline.Where(e => e.Kind == TimelineKind.Turn)) { e.RecoverTicks = 0; e.StunTicks = 0; }
            s.TurnEntryOf(soon.Id).Ip = BattleConfig.BarLength - 30 * BattleEngine.TurnRatePerTick(s, soon); // 3 s off: room for a hero action
            s.TurnEntryOf(late.Id).Ip = BattleConfig.BarLength - 55 * BattleEngine.TurnRatePerTick(s, late);
            return (s, soon, late);
        }

        [Test]
        public void Countermarch_trades_the_foe_s_turn_with_the_furthest_off_foe_exactly()
        {
            var (s, soon, late) = Line();
            int soonAt = BattleEngine.TicksUntilAct(s, s.TurnEntryOf(soon.Id)), lateAt = BattleEngine.TicksUntilAct(s, s.TurnEntryOf(late.Id));
            Assert.That(soonAt, Is.LessThan(lateAt));
            Assert.That(Countermarch.Partner(s, soon), Is.SameAs(s.TurnEntryOf(late.Id)));
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = 0, Target = soon.Pos });
            var pushed = ev.OfType<PushedBackEvent>().Single(e => e.UnitId == soon.Id);
            var hastened = ev.OfType<HastenedEvent>().Single(e => e.UnitId == late.Id);
            Assert.That(pushed.ToU, Is.GreaterThan(pushed.FromU), "the one about to act goes to the back");
            Assert.That(hastened.ToU, Is.LessThan(hastened.FromU), "the straggler steps up");
            Assert.That(pushed.ToU, Is.EqualTo(hastened.FromU).Within(1e-3f), "they trade places exactly");
            Assert.That(hastened.ToU, Is.EqualTo(pushed.FromU).Within(1e-3f));
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text == "Countermarch!"), Is.True);
            AssertEventInvariants(ev);
        }

        [Test]
        public void Countermarch_on_the_last_foe_or_one_off_the_bar_changes_nothing()
        {
            var (s, soon, late) = Line();
            Assert.That(Countermarch.Partner(s, late), Is.Null, "already furthest off");
            s.TurnEntryOf(soon.Id).RecoverTicks = 50;
            Assert.That(Countermarch.OnBar(s, soon), Is.Null, "recovering, off the bar");
            Assert.That(Countermarch.Partner(s, late), Is.Null, "a recovering foe is no partner");
            var ev = BattleEngine.Apply(s, new UseSlotCommand { HeroId = Hero(s, "knight").Id, Slot = 0, Target = late.Pos });
            Assert.That(ev.OfType<PushedBackEvent>(), Is.Empty);
            Assert.That(ev.OfType<AnnounceEvent>().Any(a => a.Text == "Already last"), Is.True);
        }

        /// <summary>GG-T356: the goblin line of <see cref="Line"/> (one acts in 3 s, the other in 5.5 s), with the knight beside
        /// the goblin about to act and the cleric where the caller puts it.</summary>
        private static (BattleState s, Unit soon, Unit late) Engaged(Coord cleric)
        {
            var setup = new BattleSetup { Stage = Stage(("goblin", 3, 0), ("goblin", 5, 0)), Seed = 3, UnlockedSlots = 5 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 0), Bar = new List<string> { "tactician_countermarch" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = cleric });
            var s = BattleEngine.Create(WithReal("tactician_countermarch"), setup, new List<BattleEvent>());
            var soon = At(s, 3, 0); var late = At(s, 5, 0);
            foreach (var e in s.Timeline.Where(e => e.Kind == TimelineKind.Turn)) { e.RecoverTicks = 0; e.StunTicks = 0; }
            s.TurnEntryOf(soon.Id).Ip = BattleConfig.BarLength - 30 * BattleEngine.TurnRatePerTick(s, soon);
            s.TurnEntryOf(late.Id).Ip = BattleConfig.BarLength - 55 * BattleEngine.TurnRatePerTick(s, late);
            return (s, soon, late);
        }

        [Test]
        public void Smart_bot_countermarches_only_to_avert_a_dangerous_imminent_blow()
        {
            // A healthy knight shrugs off the goblin's blow: the swap buys nothing worth a hero action.
            var (s, soon, late) = Engaged(new Coord(5, 7));
            Assert.That(HeroBot.NextTurnHarm(s, late), Is.EqualTo(0f), "the straggler reaches no one");
            Assert.That(HeroBot.NextTurnHarm(s, soon), Is.GreaterThan(0f).And.LessThan(HeroBot.CountermarchMinValue));
            Assert.That(HeroBot.CountermarchValue(s, soon), Is.LessThanOrEqualTo(0f), "skipped");

            // The same blow would KO a knight on its last HP: sending that goblin to the back is worth the action.
            Hero(s, "knight").Hp = 1;
            Assert.That(HeroBot.NextTurnHarm(s, soon), Is.GreaterThan(20f), "a KO");
            Assert.That(HeroBot.CountermarchValue(s, soon), Is.GreaterThanOrEqualTo(HeroBot.CountermarchMinValue));
            Assert.That(HeroBot.CountermarchValue(s, late), Is.LessThan(0f), "the last foe has no one to trade with");

            // Not when the straggler would land a KO of its own the moment it steps up.
            var (s2, soon2, late2) = Engaged(new Coord(5, 1));
            Hero(s2, "knight").Hp = 1;
            Hero(s2, "cleric").Hp = 1;
            Assert.That(HeroBot.NextTurnHarm(s2, late2), Is.GreaterThan(20f));
            Assert.That(HeroBot.CountermarchValue(s2, soon2), Is.LessThanOrEqualTo(0f), "it only trades one KO for another, sooner");
        }

        [Test]
        public void A_countermarch_must_pick_one_foe()
        {
            var db = Mini();
            db.Abilities["bad_march"] = new AbilityDef { Id = "bad_march", Name = "Bad March", Kind = AbilityKind.Skill, SwapsTurns = true, Shape = TargetShape.AllEnemies, Mode = TargetMode.Auto, Filter = TargetFilter.EnemyOnly };
            Assert.That(ContentLoader.Validate(db), Has.Some.Contains("a Countermarch (SwapsTurns)"));
            Assert.That(ContentLoader.Validate(WithReal("tactician_countermarch")).Where(x => x.Contains("SwapsTurns")), Is.Empty);
        }

        [Test]
        public void A_life_cost_must_pay_for_an_ally_heal()
        {
            var db = Mini();
            db.Abilities["bad_gift"] = new AbilityDef { Id = "bad_gift", Name = "Bad Gift", Kind = AbilityKind.Skill, BaseDamage = 10, Filter = TargetFilter.EnemyOnly, LifeCost = 0.8f };
            var p = ContentLoader.Validate(db);
            Assert.That(p, Has.Some.Contains("LifeCost 0.8 outside"));
            Assert.That(p, Has.Some.Contains("a LifeCost pays for an ally heal"));
            Assert.That(ContentLoader.Validate(WithReal("herald_lifegift")).Where(x => x.Contains("LifeCost")), Is.Empty);
        }
    }
}
