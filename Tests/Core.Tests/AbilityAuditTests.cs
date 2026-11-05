using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T183: at least 100 hero abilities that differ in mechanic, every one buyable at the Learn screen and
    /// none a no-op; plus the movement / timeline / drain mechanics the audit added to the dispatcher.</summary>
    public class AbilityAuditTests
    {
        [Test]
        public void At_least_100_learnable_hero_abilities_differ_in_mechanic()
        {
            var learnable = Abilities.LearnableByAnyHero(Real);
            var groups = learnable.GroupBy(Abilities.MechanicKey).ToList();
            TestContext.Out.WriteLine($"{learnable.Count} learnable hero abilities, {groups.Count} distinct mechanics");
            foreach (var g in groups.Where(g => g.Count() > 1).OrderByDescending(g => g.Count()))
                TestContext.Out.WriteLine($"  {g.Count(),2} x {g.Key}: {string.Join(", ", g.Select(a => a.Id))}");
            Assert.That(groups.Count, Is.GreaterThanOrEqualTo(100));
        }

        [Test]
        public void Every_learnable_hero_ability_does_something()
        {
            var noOps = Abilities.LearnableByAnyHero(Real).Where(a => !Abilities.HasEffect(a)).Select(a => a.Id).ToList();
            Assert.That(noOps, Is.Empty, "abilities that resolve to nothing: " + string.Join(", ", noOps));
        }

        [Test]
        public void Every_counted_ability_is_offered_at_some_heros_Learn_screen()
        {
            var c = Real;
            var offered = new HashSet<string>();
            foreach (var cls in c.Classes.Values.Where(x => x.Team == Team.Hero))
                foreach (var a in Abilities.Learnable(c, new HeroSave { ClassId = cls.Id })) offered.Add(a.Id);
            var missing = Abilities.LearnableByAnyHero(c).Where(a => !offered.Contains(a.Id)).Select(a => a.Id).ToList();
            Assert.That(missing, Is.Empty, string.Join(", ", missing));
        }

        [Test]
        public void Reskins_share_a_key_and_new_mechanics_do_not()
        {
            var c = Real;
            string K(string id) => Abilities.MechanicKey(c.Ability(id));
            // Same move, different coat of paint: numbers and element (bar Fire/Lightning) don't count.
            Assert.That(K("cleave"), Is.EqualTo(K("striker_finishingblow")));
            // A self-buff is the same whether its filter says Any or AllyOnly.
            Assert.That(K("adrenaline_rush"), Is.EqualTo(K("shieldmaiden_valkyrie")));
            // The audit's reworks now play differently from the plain strike they used to copy.
            foreach (var id in new[] { "fencer_fleche", "duelist_riposte", "skirmisher_hitandrun", "berserker_wildswing", "prizefighter_knockout" })
                Assert.That(K(id), Is.Not.EqualTo(K("cleave")), id);
        }

        // ── the mechanics behind the new keys ──

        private static (BattleState s, Unit hero) WithSkill(AbilityDef a, StageDef stage, int hx = 2, int hy = 4)
        {
            var db = Mini();
            a.Kind = AbilityKind.Skill;
            a.Name = a.Id;
            db.Abilities[a.Id] = a;
            var (s, _) = Battle(db, stage, ("knight", hx, hy), ("cleric", 5, 7));
            var k = Hero(s, "knight");
            k.Bar[0] = new AbilitySlot { AbilityId = a.Id };
            return (s, k);
        }

        private static List<BattleEvent> Use(BattleState s, Unit hero, Coord target) =>
            BattleEngine.Apply(s, new UseSlotCommand { HeroId = hero.Id, Slot = 0, Target = target });

        private static Unit Foe(BattleState s) => s.Units.First(u => !u.IsHero && u.Alive);

        [Test]
        public void Pull_reels_a_foe_in_toward_the_caster()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "hook", BaseDamage = 1, Pull = 2, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 0)));
            var ev = Use(s, k, new Coord(2, 0));
            Assert.That(Foe(s).Pos, Is.EqualTo(new Coord(2, 2)));
            Assert.That(ev.OfType<DisplacedEvent>().Count(), Is.EqualTo(2));
            AssertInvariants(s);
        }

        [Test]
        public void Pull_stops_beside_the_caster()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "hook", BaseDamage = 1, Pull = 5, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 1)));
            Use(s, k, new Coord(2, 1));
            Assert.That(Foe(s).Pos, Is.EqualTo(new Coord(2, 3)));
            AssertInvariants(s);
        }

        [Test]
        public void Swap_trades_tiles_with_the_target()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "fleche", BaseDamage = 1, SwapsPlaces = true, AdjacentOnly = true, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 3)));
            var foe = Foe(s);
            Use(s, k, new Coord(2, 3));
            Assert.That(k.Pos, Is.EqualTo(new Coord(2, 3)));
            Assert.That(foe.Pos, Is.EqualTo(new Coord(2, 4)));
            AssertInvariants(s);
        }

        [Test]
        public void Step_back_puts_distance_between_the_caster_and_the_nearest_foe()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "hitrun", BaseDamage = 1, StepBack = 2, AdjacentOnly = true, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 3)));
            Use(s, k, new Coord(2, 3));
            Assert.That(k.Pos, Is.EqualTo(new Coord(2, 6)));
            AssertInvariants(s);
        }

        [Test]
        public void Delay_slides_the_foes_turn_icon_back()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "hinder", DelayU = 0.3f, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 0)));
            var foe = Foe(s);
            var turn = s.TurnEntryOf(foe.Id);
            turn.Ip = BattleConfig.BarLength / 2;
            int before = turn.Ip;
            var ev = Use(s, k, foe.Pos);
            Assert.That(ev.OfType<PushedBackEvent>().Any(p => p.UnitId == foe.Id), Is.True);
            Assert.That(s.TurnEntryOf(foe.Id).Ip, Is.LessThan(before));
        }

        [Test]
        public void A_beneficial_buff_on_a_foe_targeting_ability_lands_on_the_caster_not_the_foe()
        {
            // GG-T266: Opening Jab empowered the foe it hit; War Stomp and Battle Cry made enemies taunt.
            var (s, k) = WithSkill(new AbilityDef { Id = "jab", BaseDamage = 1, ApplyBuff = "protection", Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 0)));
            var foe = Foe(s);
            var gifts = Use(s, k, foe.Pos).OfType<BuffAppliedEvent>().Where(b => b.BuffId == "protection").ToList();
            Assert.That(gifts.Any(b => b.UnitId == foe.Id), Is.False, "the foe is never buffed by our jab");
            Assert.That(gifts.Count(b => b.UnitId == k.Id), Is.EqualTo(1), "the caster is, once");
        }

        [Test]
        public void A_heal_aimed_at_a_foe_drains_into_the_caster()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "feast", BaseDamage = 5, BaseHeal = 10, Shape = TargetShape.SingleActor, Mode = TargetMode.PickActor, Filter = TargetFilter.EnemyOnly }, Stage(("tank", 2, 0)));
            var foe = Foe(s);
            k.Hp = k.MaxHp / 2;
            int me = k.Hp, it = foe.Hp;
            Use(s, k, foe.Pos);
            Assert.That(k.Hp, Is.GreaterThan(me), "the caster drinks");
            Assert.That(foe.Hp, Is.LessThan(it), "the foe only bleeds");
        }

        [Test]
        public void Harm_and_heal_in_one_burst_hurts_only_foes_and_mends_only_friends()
        {
            var (s, k) = WithSkill(new AbilityDef { Id = "hope", BaseDamage = 10, BaseHeal = 10, Shape = TargetShape.Cross, Radius = 1, Mode = TargetMode.PickTile, Filter = TargetFilter.Any }, Stage(("tank", 1, 1)), hx: 2, hy: 1);
            var foe = Foe(s);
            k.Hp = k.MaxHp / 2;
            int me = k.Hp, it = foe.Hp;
            Use(s, k, new Coord(1, 1));
            Assert.That(k.Hp, Is.GreaterThan(me), "the ally in the burst is mended, not scorched");
            Assert.That(foe.Hp, Is.LessThan(it), "the foe is scorched, not mended");
        }

        [Test]
        public void The_new_mechanics_resolve_from_real_content()
        {
            var c = Real;
            Assert.That(c.Ability("disengage").StepBack, Is.GreaterThan(0));
            Assert.That(c.Ability("fencer_fleche").SwapsPlaces, Is.True);
            Assert.That(c.Ability("undertow").Pull, Is.GreaterThan(0));
            Assert.That(c.Ability("stasis").DelayU, Is.GreaterThan(0f));
            Assert.That(c.Ability("shadowstep_fade_step").IsTeleport, Is.True);
        }
    }
}
