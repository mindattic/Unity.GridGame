using System;
using System.IO;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T373: spell prices on the 1–12 orb curve (BIBLE §3.1.1). Owner 2026-11-08: "with 12 mana slots that means
    /// some of these spells need to get much more expensive, need to consider balance."</summary>
    public class ManaCostCurveTests
    {
        private static ContentDb FreshReal() => ContentLoader.Load(name =>
        {
            var p = Path.Combine(ContentDir, name + ".json");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        });

        private static bool HeroSpell(AbilityDef a) => Abilities.IsHeroUsable(a) && a.Kind == AbilityKind.Spell;

        /// <summary>The curve's guide (BIBLE §3.1.1): effect points per target (10 damage = 1, 20 healing = 1, a status by
        /// its strength, 1.6 per second of Pushback, party tempo capped at the tempo floor) times the targets the shape
        /// expects to catch, then priced convexly (0.9 × raw^1.5) so concentrated power costs more than the sum of its parts.</summary>
        public static float GuideRaw(AbilityDef a)
        {
            float area = Area(a);
            float per = a.BaseDamage / 10f, flat = 0f;
            bool drain = a.Filter == TargetFilter.EnemyOnly && a.BaseHeal > 0;
            if (drain) flat += a.BaseHeal / 20f; else per += a.BaseHeal / 20f;
            if (a.ApplyBuff != null) per += Status(a.ApplyBuff) * a.ApplyBuffChance;
            if (a.RemovesDebuffs) per += 0.4f;
            per += 1.6f * a.Pushback + 0.25f * a.Pull + (a.SwapsPlaces ? 0.5f : 0f);
            if (a.HastenU > 0f) flat += 0.8f * Math.Min(3f, a.HastenU / BattleConfig.HastenUPerTempo * area);
            float v = per * area + flat;
            if (a.CastTime <= 0f) v *= 1.25f;   // instant: nothing to interrupt, lands this action
            if (a.AdjacentOnly) v *= 0.8f;      // has to walk into reach
            if (a.CooldownTurns >= 3) v *= 0.85f; // can't be chained
            return v;
        }

        public static int Guide(AbilityDef a) => Math.Max(1, Math.Min(ManaBank.DefaultCapacity, (int)Math.Round(0.9 * Math.Pow(GuideRaw(a), 1.5))));

        private static float Area(AbilityDef a)
        {
            switch (a.Shape)
            {
                case TargetShape.Cross: case TargetShape.Diamond: return a.Radius >= 2 ? 2.6f : 1.6f;
                case TargetShape.Square: return a.Radius >= 2 ? 2.4f : 1.9f;
                case TargetShape.Row: case TargetShape.Column: return 1.8f;
                case TargetShape.Plus: return 2.4f;
                case TargetShape.AllEnemies: return 2.5f;
                case TargetShape.AllAllies: return 4.5f;
                default: return 1f;
            }
        }

        private static float Status(string id)
        {
            switch (id)
            {
                case "frozen": return 1.5f;
                case "sleep": return 1f;
                case "burning": return 0.8f;
                case "poisoned": case "ward": return 0.7f;
                case "silenced": case "slowed": case "regen": case "empowered": return 0.6f;
                case "blinded": return 0.5f;
                case "wet": case "protection": return 0.3f;
                default: return 0.5f;
            }
        }

        [Test]
        public void Every_hero_spell_sits_within_two_orbs_of_the_curve_guide()
        {
            var off = Real.Abilities.Values.Where(HeroSpell)
                .Where(a => Math.Abs(a.Cost - Guide(a)) > 2)
                .Select(a => $"{a.Id} costs {a.Cost}, guide {Guide(a)} (raw {GuideRaw(a):0.00})").ToList();
            Assert.That(off, Is.Empty, "re-price against BIBLE §3.1.1, or amend the curve there");
        }

        [Test]
        public void The_curve_spans_cheap_staples_to_saved_for_spells()
        {
            var costs = Real.Abilities.Values.Where(HeroSpell).Select(a => a.Cost).ToList();
            Assert.That(costs.Count(c => c <= 2), Is.GreaterThanOrEqualTo(10), "bread-and-butter spells stay cheap");
            Assert.That(costs.Max(), Is.GreaterThanOrEqualTo(8), "an ultimate takes most of the bank");
            Assert.That(costs.Count(c => c >= HeroBot.SaveForCost), Is.GreaterThanOrEqualTo(5), "several spells are worth saving for");
        }

        [Test]
        public void Bigger_areas_of_the_same_effect_never_cost_less()
        {
            // Same base damage and status, larger shape: the bigger one is never cheaper (meteor vs a radius-1 nova, …).
            var spells = Real.Abilities.Values.Where(HeroSpell).ToList();
            foreach (var a in spells)
                foreach (var b in spells)
                    if (a.BaseDamage > 0 && a.BaseDamage <= b.BaseDamage && a.BaseHeal == b.BaseHeal && a.ApplyBuff == b.ApplyBuff
                        && a.CastTime > 0f && b.CastTime > 0f && a.CooldownTurns == b.CooldownTurns && !a.AdjacentOnly && !b.AdjacentOnly
                        && Area(b) > Area(a) && a.Filter == b.Filter)
                        Assert.That(b.Cost, Is.GreaterThanOrEqualTo(a.Cost), $"{b.Id} ({b.Cost}) hits more than {a.Id} ({a.Cost}) for as much");
        }

        [Test]
        public void Early_kits_open_with_spells_a_first_wave_can_pay_for()
        {
            // Two bar slots are open from the start (§4.7): their spells cost at most 3 orbs, and the starting roster's
            // and the tutorial's first spells (the Cleric's Heal and Pre-emptive Ward) one each.
            foreach (var c in Real.Classes.Values.Where(x => x.Team == Team.Hero))
                foreach (var id in c.DefaultLoadout.Take(2))
                {
                    var a = id == null || id.StartsWith("item:") ? null : Real.TryAbility(id);
                    if (a != null && a.Kind == AbilityKind.Spell) Assert.That(a.Cost, Is.LessThanOrEqualTo(3), $"{c.Id} opens with {a.Id} at {a.Cost} orbs");
                }
            Assert.That(Real.Ability("heal").Cost, Is.EqualTo(1));
            Assert.That(Real.Ability("aegiswardens_preemptiveward").Cost, Is.EqualTo(1));
        }

        [Test]
        public void Validate_refuses_a_spell_dearer_than_the_bank_and_a_skill_that_spends_orbs()
        {
            var db = FreshReal();
            db.Abilities["meteor"].Cost = ManaBank.DefaultCapacity + 1;
            db.Abilities["shield_bash"].Cost = 1;
            var problems = string.Join("\n", ContentLoader.Validate(db, _ => true));
            Assert.That(problems, Does.Contain("ability meteor: Cost 13"));
            Assert.That(problems, Does.Contain("ability shield_bash: only a Spell spends orbs"));
        }

        [Test]
        public void Real_content_passes_the_cost_checks()
        {
            var problems = ContentLoader.Validate(Real, _ => true).Where(p => p.Contains("GG-T373")).ToList();
            Assert.That(problems, Is.Empty);
        }

        // ── Bots save for a big spell (HeroBot.SavingFor) ─────────────────────────────────────────

        private static ContentDb SaverDb()
        {
            var db = Mini();
            db.Abilities["nova"] = new AbilityDef { Id = "nova", Name = "Nova", Kind = AbilityKind.Spell, Cost = 6, BaseDamage = 20, DamageType = DamageType.Fire, Shape = TargetShape.Square, Radius = 2, Mode = TargetMode.PickTile, Filter = TargetFilter.EnemyOnly };
            return db;
        }

        [Test]
        public void The_party_saves_for_its_nearest_unaffordable_big_spell()
        {
            var db = SaverDb();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" };
            m.Bar[1] = new AbilitySlot { AbilityId = "nova" };
            s.Bank.Add(2);
            Assert.That(HeroBot.SavingFor(s), Is.EqualTo(6), "a 6-orb spell on the bar and 2 in the bank");
            s.Bank.Add(4);
            Assert.That(HeroBot.SavingFor(s), Is.EqualTo(0), "the bank holds it: nothing left to save for");
        }

        [Test]
        public void Cheap_spells_and_locked_slots_are_not_saved_for()
        {
            var db = SaverDb();
            var (s, _) = Battle(db, Stage(("tank", 2, 2)), ("knight", 5, 7), ("mage", 4, 7));
            var m = Hero(s, "mage");
            m.Bar[0] = new AbilitySlot { AbilityId = "fireball" }; // 2 orbs: under SaveForCost
            Assert.That(HeroBot.SavingFor(s), Is.EqualTo(0));
            while (m.Bar.Count <= m.UnlockedSlots) m.Bar.Add(new AbilitySlot());
            m.Bar[m.UnlockedSlots] = new AbilitySlot { AbilityId = "nova" }; // a locked slot can't cast it
            Assert.That(HeroBot.SavingFor(s), Is.EqualTo(0));
        }

        [Test]
        public void Camp_fills_an_open_slot_with_a_spell_it_can_cast_most_battles()
        {
            var c = FreshReal();
            var h = Progression.NewHero(c, "redmage");
            var big = Abilities.Learnable(c, h).Where(a => a.Kind == AbilityKind.Spell && a.Cost > CampRoutine.FillerMaxCost).ToList();
            Assume.That(big, Is.Not.Empty, "the red mage can learn a big spell (Meteor)");
            var pick = CampRoutine.BestLearnable(c, h, 100000);
            Assert.That(pick, Is.Not.Null);
            Assert.That(pick.Kind != AbilityKind.Spell || pick.Cost <= CampRoutine.FillerMaxCost, Is.True, $"picked {pick.Id} ({pick.Cost} orbs)");
        }
    }
}
