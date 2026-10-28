using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>GG-T179: an enchanted armor piece gives its wearer the enchant's passive in battle while it's intact.</summary>
    public class ArmorEnchantTests
    {
        private static ContentDb Db()
        {
            var db = Mini();
            db.Items["mail"] = new ItemDef { Id = "mail", Name = "Mail", Type = ItemType.Equipment, Slot = EquipmentSlot.Torso, Durability = 50 };
            db.Buffs["poisoned"] = new BuffDef { Id = "poisoned", Name = "Poisoned", Unit = DurationUnit.Ticks, Duration = 6, DamagePerTick = 1 };
            foreach (var id in new[] { "poison_ward", "thorns", "regenerate" })
            {
                db.Abilities[id] = new AbilityDef { Id = id, Name = id, Kind = AbilityKind.Passive };
                var e = new ArmorEnchantDef { Id = "ae_" + id, Name = id, Passive = id };
                db.ArmorEnchants[e.Id] = e;
                db.ArmorEnchantOrder.Add(e);
            }
            db.Classes["wolf"].MoveRange = 0;
            db.Classes["wolf"].BaseStats.Luck = 0;
            db.Classes["wolf"].OnHitBuff = "poisoned";
            db.Classes["wolf"].OnHitChance = 1f;
            return db;
        }

        /// <summary>A knight in mail (enchanted with <paramref name="enchant"/>, or plain) beside a wolf that poisons on hit;
        /// the wolf gets several turns. Returns the knight, the wolf and every event.</summary>
        private static (Unit knight, Unit wolf, List<BattleEvent> events) Fight(string enchant)
        {
            var db = Db();
            var setup = new BattleSetup { Stage = Stage(("wolf", 2, 2)), Seed = 3, UnlockedSlots = 2 };
            setup.Party.Add(new HeroSpec { ClassId = "knight", Start = new Coord(2, 3), Armor = { [EquipmentSlot.Torso] = new ArmorPieceSpec { ItemId = "mail", Enchant = enchant } }, EquippedItemIds = { "mail" } });
            setup.Party.Add(new HeroSpec { ClassId = "cleric", Start = new Coord(5, 7) });
            var all = new List<BattleEvent>();
            var s = BattleEngine.Create(db, setup, all);
            var k = Hero(s, "knight");
            var wolf = s.Units.First(u => u.Def.Id == "wolf");
            for (int i = 0; i < 4 && wolf.Alive && k.Alive; i++)
            {
                s.TurnEntryOf(wolf.Id).Ip = 999;
                all.AddRange(BattleEngine.Apply(s, new ShieldCommand()));
            }
            return (k, wolf, all);
        }

        private static bool Poisoned(List<BattleEvent> ev, Unit k) => ev.OfType<BuffAppliedEvent>().Any(b => b.UnitId == k.Id && b.BuffId == "poisoned");

        [Test]
        public void Poison_ward_keeps_the_wearer_from_being_poisoned()
        {
            var (plainKnight, _, plain) = Fight(null);
            Assert.That(Poisoned(plain, plainKnight), Is.True, "control: the wolf's bite poisons an unwarded knight");
            var (k, _, ev) = Fight("ae_poison_ward");
            Assert.That(Poisoned(ev, k), Is.False);
            Assert.That(ev.Any(e => e is AnnounceEvent a && a.Text == "Warded!"), Is.True);
        }

        [Test]
        public void Thorns_reflects_part_of_a_landed_hit_at_the_attacker()
        {
            var (_, wolf, plain) = Fight(null);
            Assert.That(plain.OfType<DamagedEvent>().Any(d => d.TargetId == wolf.Id && d.Note == "Thorns"), Is.False);
            var (k, w, ev) = Fight("ae_thorns");
            var hit = ev.OfType<DamagedEvent>().First(d => d.TargetId == k.Id && d.SourceId == w.Id && d.Amount > 0);
            var thorns = ev.OfType<DamagedEvent>().FirstOrDefault(d => d.TargetId == w.Id && d.Note == "Thorns");
            Assert.That(thorns, Is.Not.Null, "the wolf takes thorns damage");
            Assert.That(thorns.Amount, Is.EqualTo(System.Math.Max(1, (int)System.Math.Round(hit.Amount * BattleConfig.ThornsFraction))));
        }

        [Test]
        public void Regeneration_heals_the_wearer_when_control_returns()
        {
            var (plainKnight, _, plain) = Fight(null);
            Assert.That(plain.OfType<HealedEvent>().Any(h => h.TargetId == plainKnight.Id && h.Amount > 0), Is.False, "control: nothing heals a plain knight");
            var (k, _, ev) = Fight("ae_regenerate");
            Assert.That(ev.OfType<HealedEvent>().Any(h => h.TargetId == k.Id && h.Amount > 0), Is.True);
        }
    }
}
