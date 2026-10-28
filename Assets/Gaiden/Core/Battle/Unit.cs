using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    public sealed class BuffInstance
    {
        public BuffDef Def;
        public int Remaining;
        /// <summary>Tick-unit buffs accumulate timeline ticks here; one duration step per BattleConfig.BuffTickInterval.</summary>
        public int TickAccumulator;
        /// <summary>Set on application; a halting (Frozen) buff skips the turn boundary of the action that applied it.</summary>
        public bool Fresh;

        public BuffInstance Clone() => (BuffInstance)MemberwiseClone();
    }

    /// <summary>One armor piece worn in battle (§24.2, GG-T89): mirrors the save's per-piece durability/mastery.</summary>
    public sealed class EquippedPiece
    {
        public string ItemId;
        public int Durability;
        public float Mastery = 1f;
        /// <summary>Armor enchant id (GG-T179): grants its passive while this piece is worn (a broken piece leaves Gear).</summary>
        public string Enchant;

        public EquippedPiece Clone() => (EquippedPiece)MemberwiseClone();
    }

    /// <summary>One AbilityBar slot: an ability (skill/spell) or a consumable item stack.</summary>
    public sealed class AbilitySlot
    {
        public string AbilityId;
        public string ItemId;
        /// <summary>A spare weapon carried on the bar; using it swaps it with the equipped one.</summary>
        public string WeaponId;
        /// <summary>Remaining durability of the weapon stowed in this slot, carried across swaps so stowing and re-equipping never resets wear.</summary>
        public int WeaponDurability;
        public int Charges;
        public int MaxCharges;
        /// <summary>True when the stack came from the player's inventory (use also consumes the item).</summary>
        public bool FromInventory;

        public bool IsEmpty => AbilityId == null && ItemId == null && WeaponId == null;
        public bool IsWeapon => WeaponId != null;
        public bool IsItem => ItemId != null;

        public AbilitySlot Clone() => (AbilitySlot)MemberwiseClone();
    }

    public sealed class Unit
    {
        public int Id;
        public ClassDef Def;
        public Team Team;
        public int Level = 1;
        /// <summary>Enemy turns this unit has started (brains with a rhythm: sluggish, blinkers, burrowers).</summary>
        public int TurnsTaken;

        /// <summary>Level-derived stats before equipment.</summary>
        public Stats LevelStats = new Stats();
        /// <summary>Summed equipment stat bonuses.</summary>
        public Stats GearStats = new Stats();

        public int MaxHp;
        public int Hp;

        public Coord Pos;
        public int W = 1;
        public int H = 1;
        public bool OnBoard = true;

        public List<BuffInstance> Buffs = new List<BuffInstance>();
        public Dictionary<string, int> Cooldowns = new Dictionary<string, int>();
        public List<AbilitySlot> Bar = new List<AbilitySlot>();
        public int UnlockedSlots = 2;

        // Equipment (heroes)
        public string WeaponId;
        public float WeaponPower;
        public int WeaponDurability;
        /// <summary>Masterwork stat multiplier for the current weapon (1 = normal, §24.6.2, GG-T46).</summary>
        public float WeaponMastery = 1f;
        /// <summary>Armor pieces currently worn (Head/Torso/Legs/Gloves/Boots), each with its own durability
        /// and Masterwork multiplier; a piece is removed from here the instant it breaks (§24.2, GG-T89).</summary>
        public Dictionary<EquipmentSlot, EquippedPiece> Gear = new Dictionary<EquipmentSlot, EquippedPiece>();
        public float PincerDamageBonus;
        public int RegenPerTurn;
        public Dictionary<DamageType, float> GearResist = new Dictionary<DamageType, float>();
        /// <summary>Armor set bonus tiers currently folded into GearStats/GearResist/PincerDamageBonus/RegenPerTurn
        /// (GG-T182) — kept so a piece breaking mid-battle can take back exactly the tiers it switches off.</summary>
        public List<SetBonusDef> SetBonuses = new List<SetBonusDef>();

        // Snake chain (segmented boss): head has SnakeIndex 0; segments 1..n follow.
        public int SnakeHeadId = -1;
        public int SnakeIndex;

        public int BossPhase = -1;
        /// <summary>Damage this hero has dealt this battle (enemy threat targeting, US-080).</summary>
        public int DamageDealt;

        public bool Alive => Hp > 0 && OnBoard;
        public bool IsHero => Team == Team.Hero;
        public bool IsMultiTile => W > 1 || H > 1;
        /// <summary>A grave marker left by an undead (§14.5): solid, never acts except to raise its owner.</summary>
        public bool IsRemains => Def != null && Def.HasTag("Remains");
        /// <summary>An egg laid mid-battle (§14.8): solid, never acts except to hatch.</summary>
        public bool IsEgg => Def != null && Def.HasTag("Egg");
        /// <summary>A body left on the board (§14.7): blocks its tile, never rises, doesn't hold up a wave, rots away.</summary>
        public bool IsCorpse => Def != null && Def.HasTag("Corpse");
        /// <summary>Class id of the fallen unit a corpse came from (its art, its name).</summary>
        public string CorpseOf;
        /// <summary>Player actions until a corpse rots away.</summary>
        public int DecayIn;
        /// <summary>Armor plate (§13.5): HP left on the plate and its full value; 0 = none or broken.</summary>
        public int PlateHp, PlateMax;
        /// <summary>The side the unit faces (a cardinal unit vector): its plate is on this side.</summary>
        public Coord Facing = new Coord(0, 1);
        public bool HasPlate => PlateHp > 0;
        /// <summary>For remains: the class and level that rise when its turn arrives.</summary>
        public string RisesAs;
        public int RisesLevel;
        /// <summary>A guest ally (§25.11), hero-team and player-controlled for this battle only.</summary>
        public bool IsGuest;
        /// <summary>A risen undead drops nothing the second time.</summary>
        public bool NoLoot;
        public bool IsSnakeSegment => SnakeHeadId >= 0 && SnakeIndex > 0;
        public bool IsSnakeMember => SnakeHeadId >= 0;
        public string Name => Def.Name;
        public bool IsHumanoid => Team == Team.Hero || Def.HasTag("Humanoid");
        public bool IsRanged => Def.AttackRange > 1;

        public Stats Effective
        {
            get
            {
                var s = LevelStats + GearStats;
                s.Speed = LevelStats.Speed + GearStats.Speed + GearStats.Agility * 0.5f;
                if (s.Strength < 1) s.Strength = 1;
                if (s.Vitality < 1) s.Vitality = 1;
                if (s.Speed < 1) s.Speed = 1;
                return s;
            }
        }

        public bool Occupies(Coord c) =>
            OnBoard && c.X >= Pos.X && c.X < Pos.X + W && c.Y >= Pos.Y && c.Y < Pos.Y + H;

        public IEnumerable<Coord> Tiles()
        {
            for (int dy = 0; dy < H; dy++)
                for (int dx = 0; dx < W; dx++)
                    yield return new Coord(Pos.X + dx, Pos.Y + dy);
        }

        /// <summary>Cardinal adjacency, footprint-aware (a 2×2 touches any tile beside its body).</summary>
        public bool IsAdjacentTo(Unit o)
        {
            foreach (var a in Tiles())
                foreach (var b in o.Tiles())
                    if (a.IsCardinalNeighbor(b)) return true;
            return false;
        }

        public int DistanceTo(Unit o)
        {
            int best = int.MaxValue;
            foreach (var a in Tiles())
                foreach (var b in o.Tiles())
                {
                    int d = a.Manhattan(b);
                    if (d < best) best = d;
                }
            return best;
        }

        public bool HasBuff(string id)
        {
            foreach (var b in Buffs) if (b.Def.Id == id) return true;
            return false;
        }

        public bool IsImmobile
        {
            get { foreach (var b in Buffs) if (b.Def.Immobile) return true; return false; }
        }

        public bool IsTimelineHalted
        {
            get { foreach (var b in Buffs) if (b.Def.HaltsTimeline) return true; return false; }
        }

        public bool IsSilenced
        {
            get { foreach (var b in Buffs) if (b.Def.BlocksSpells) return true; return false; }
        }

        /// <summary>Carries a taunt buff: enemies must come for this hero (§14.1.1).</summary>
        public bool IsTaunting
        {
            get { foreach (var b in Buffs) if (b.Def.Taunts) return true; return false; }
        }

        public float OutgoingDamageMultiplier
        {
            get { float m = 1f; foreach (var b in Buffs) m *= b.Def.OutgoingDamageMultiplier; return m; }
        }

        public float IncomingDamageMultiplier
        {
            get { float m = 1f; foreach (var b in Buffs) m *= b.Def.IncomingDamageMultiplier; return m; }
        }

        public float TimelineSpeedMultiplier
        {
            get { float m = 1f; foreach (var b in Buffs) m *= b.Def.TimelineSpeedMultiplier; return m; }
        }

        public float AccuracyMultiplier
        {
            get { float m = 1f; foreach (var b in Buffs) m *= b.Def.AccuracyMultiplier; return m; }
        }

        public float ResistanceMultiplier(DamageType type)
        {
            float m = 1f;
            if (Def.Resistances != null && Def.Resistances.TryGetValue(type, out var r)) m *= r;
            if (GearResist != null && GearResist.TryGetValue(type, out var g)) m *= g;
            return m;
        }

        public float HpFraction => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;

        public Unit Clone()
        {
            var u = (Unit)MemberwiseClone();
            u.LevelStats = LevelStats.Clone();
            u.GearStats = GearStats.Clone();
            u.Buffs = new List<BuffInstance>(Buffs.Count);
            foreach (var b in Buffs) u.Buffs.Add(b.Clone());
            u.Cooldowns = new Dictionary<string, int>(Cooldowns);
            u.Bar = new List<AbilitySlot>(Bar.Count);
            foreach (var s in Bar) u.Bar.Add(s.Clone());
            u.GearResist = new Dictionary<DamageType, float>(GearResist);
            u.SetBonuses = new List<SetBonusDef>(SetBonuses);
            u.Gear = new Dictionary<EquipmentSlot, EquippedPiece>();
            foreach (var kv in Gear) u.Gear[kv.Key] = kv.Value.Clone();
            return u;
        }

        public override string ToString() => $"{Name}#{Id}";
    }
}
