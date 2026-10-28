using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    public enum BattlePhase { PlayerInput, Victory, Defeat, Escaped, Retreated }

    public sealed class Trap
    {
        public Coord At;
        public string TrapId;
        public int OwnerId;
        public Trap Clone() => (Trap)MemberwiseClone();
    }

    /// <summary>Everything a battle needs to know about one party member, supplied by the meta layer.</summary>
    public sealed class HeroSpec
    {
        public string ClassId;
        public int Level = 1;
        /// <summary>Current HP carried between battles; 0 or less = full.</summary>
        public int Hp;
        public string WeaponId;
        public int WeaponDurability;
        /// <summary>Masterwork stat multiplier carried from the save (§24.6.2, GG-T46); 1 = normal.</summary>
        public float WeaponMastery = 1f;
        /// <summary>Armor equipped per slot (Head/Torso/Legs/Gloves/Boots), each with its own durability/mastery (§24.2, GG-T89).</summary>
        public Dictionary<EquipmentSlot, ArmorPieceSpec> Armor = new Dictionary<EquipmentSlot, ArmorPieceSpec>();
        public List<string> EquippedItemIds = new List<string>();
        /// <summary>Ability bar entries: ability id, or "item:id". Null/empty = empty slot.</summary>
        public List<string> Bar = new List<string>();
        /// <summary>Item id → owned count, used to size bar item stacks.</summary>
        public Dictionary<string, int> ItemCounts = new Dictionary<string, int>();
        public Coord? Start;
    }

    /// <summary>One equipped armor piece carried from the save into a battle (§24.2, GG-T89).</summary>
    public sealed class ArmorPieceSpec
    {
        public string ItemId;
        public int Durability;
        public float Mastery = 1f;
        /// <summary>Armor enchant id (GG-T179): the piece grants that enchant's passive while intact; null = none.</summary>
        public string Enchant;
    }

    public sealed class BattleSetup
    {
        public StageDef Stage;
        public List<HeroSpec> Party = new List<HeroSpec>();
        public int Seed = 1;
        public int UnlockedSlots = 2;
        /// <summary>Enemy levels are floored to this (campaign difficulty curve, §22.3).</summary>
        public int RecommendedLevel = 1;
        /// <summary>New Game+ cycle (0 = first playthrough): enemies +10 levels and coins +50% per cycle.</summary>
        public int NewGamePlus;
        /// <summary>Orbs carried over from the previous battle of an expedition (§22.5).</summary>
        public int StartOrbs;
        /// <summary>Guest classes that must not appear (already recruited, or — only with permadeath on — lost).</summary>
        public HashSet<string> ExcludedGuests = new HashSet<string>();
        /// <summary>Permadeath for this battle — set only from the meta layer's single rule (<c>Permadeath.Enabled</c>,
        /// GG-T114), which is always off in the normal game; a planned opt-in Ironman mode is the only thing meant to
        /// turn it on. Off: a fallen unit is just down (revivable, back next battle).</summary>
        public bool Permadeath;
    }

    /// <summary>Totals the meta layer commits after the battle (XP, coins, drops, kills, bestiary).</summary>
    public sealed class BattleResults
    {
        public int Coins;
        public int Xp;
        public Dictionary<string, int> Items = new Dictionary<string, int>();
        public Dictionary<string, int> Kills = new Dictionary<string, int>();
        public HashSet<string> Seen = new HashSet<string>();
        public HashSet<string> Scanned = new HashSet<string>();
        /// <summary>Item uses that came from inventory stacks (deducted on commit).</summary>
        public Dictionary<string, int> ItemsConsumed = new Dictionary<string, int>();
        /// <summary>Hero class → weapon durability after battle (-1 = shattered/removed).</summary>
        public Dictionary<string, int> WeaponDurability = new Dictionary<string, int>();
        /// <summary>Hero class → armor slot → durability after battle (-1 = that slot broke/was never equipped, GG-T89).</summary>
        public Dictionary<string, Dictionary<EquipmentSlot, int>> ArmorDurability = new Dictionary<string, Dictionary<EquipmentSlot, int>>();
        /// <summary>Hero class → weapon equipped at the end (after bar swaps), and what the bar slot then held.</summary>
        public Dictionary<string, string> FinalWeapon = new Dictionary<string, string>();
        public Dictionary<string, List<string>> FinalBar = new Dictionary<string, List<string>>();
        /// <summary>Hero class → weapon item ids that shattered during this battle, kept even if a bar
        /// backup was later auto-equipped — so Commit turns each into a Broken fragment instead of
        /// discarding it or handing it back to the bag whole (GG-T41).</summary>
        public Dictionary<string, List<string>> BrokenWeapons = new Dictionary<string, List<string>>();
        /// <summary>Hero class → armor item ids that broke during this battle (GG-T41).</summary>
        public Dictionary<string, List<string>> BrokenArmors = new Dictionary<string, List<string>>();
        public int ActionsTaken;
        public int TicksElapsed;

        private static Dictionary<string, List<string>> CloneListMap(Dictionary<string, List<string>> map)
        {
            var copy = new Dictionary<string, List<string>>();
            foreach (var kv in map) copy[kv.Key] = new List<string>(kv.Value);
            return copy;
        }

        private static Dictionary<string, Dictionary<EquipmentSlot, int>> CloneArmorMap(Dictionary<string, Dictionary<EquipmentSlot, int>> map)
        {
            var copy = new Dictionary<string, Dictionary<EquipmentSlot, int>>();
            foreach (var kv in map) copy[kv.Key] = new Dictionary<EquipmentSlot, int>(kv.Value);
            return copy;
        }

        public BattleResults Clone()
        {
            return new BattleResults
            {
                Coins = Coins, Xp = Xp, ActionsTaken = ActionsTaken, TicksElapsed = TicksElapsed,
                Items = new Dictionary<string, int>(Items),
                Kills = new Dictionary<string, int>(Kills),
                Seen = new HashSet<string>(Seen),
                Scanned = new HashSet<string>(Scanned),
                ItemsConsumed = new Dictionary<string, int>(ItemsConsumed),
                WeaponDurability = new Dictionary<string, int>(WeaponDurability),
                ArmorDurability = CloneArmorMap(ArmorDurability),
                FinalWeapon = new Dictionary<string, string>(FinalWeapon),
                FinalBar = new Dictionary<string, List<string>>(FinalBar),
                BrokenWeapons = CloneListMap(BrokenWeapons),
                BrokenArmors = CloneListMap(BrokenArmors),
            };
        }
    }

    public enum MovableKind { Amplifier }

    /// <summary>A pickup lying on a tile (§13.3.1). Doesn't block; the first hero to enter the tile collects it.</summary>
    public sealed class Pickup
    {
        public int Id;
        public string PickupId;
        public Coord At;
        public int ActionsLeft;
        public Pickup Clone() => (Pickup)MemberwiseClone();
    }

    /// <summary>A pushable board object (§1.1.2). Occupies its tile; displaced like an actor; never an attacker or a target.</summary>
    public sealed class MovableTile
    {
        public MovableKind Kind;
        public Coord Pos;
        public MovableTile Clone() => (MovableTile)MemberwiseClone();
    }

    /// <summary>A delayed mid-wave spawn (§14.8), queued at wave start and resolved once the clock reaches its trigger tick.</summary>
    public sealed class PendingSpawn
    {
        public int Wave;
        public SpawnDef Spawn;
        public long TriggerClock;
        public bool Consumed;
        public PendingSpawn Clone() => (PendingSpawn)MemberwiseClone();
    }

    public sealed class BattleState
    {
        public ContentDb Content;
        public StageDef Stage;
        public int RecommendedLevel = 1;
        public int NewGamePlus;
        public Rng Rng;
        public List<Unit> Units = new List<Unit>();
        public List<TimelineEntry> Timeline = new List<TimelineEntry>();
        public List<Trap> Traps = new List<Trap>();
        public List<Pickup> Pickups = new List<Pickup>();
        public int NextPickupId = 1;

        public Pickup PickupAt(Coord c)
        {
            foreach (var p in Pickups) if (p.At == c) return p;
            return null;
        }
        /// <summary>
        /// Movable tile objects (§1.1.2): they occupy a tile like an actor — nothing spawns or lands on them — but anyone
        /// can push them around exactly like an actor. Physical blocks (<see cref="Blocks"/>) are the immovable kind.
        /// </summary>
        public List<MovableTile> Movables = new List<MovableTile>();

        public MovableTile MovableAt(Coord c)
        {
            foreach (var m in Movables) if (m.Pos == c) return m;
            return null;
        }

        /// <summary>The Power Amplifier's tile, if one is on the board (§1.2.6). Setting it places/moves/removes it.</summary>
        public Coord? Amplifier
        {
            get { foreach (var m in Movables) if (m.Kind == MovableKind.Amplifier) return m.Pos; return null; }
            set
            {
                var amp = Movables.Find(m => m.Kind == MovableKind.Amplifier);
                if (!value.HasValue) { if (amp != null) Movables.Remove(amp); return; }
                if (amp == null) Movables.Add(new MovableTile { Kind = MovableKind.Amplifier, Pos = value.Value });
                else amp.Pos = value.Value;
            }
        }

        /// <summary>Free for something new to appear (spawn, reinforcement, trap, teleport landing): walkable, no unit, no movable tile.</summary>
        public bool FreeForSpawn(Coord c) => Walkable(c) && UnitAt(c) == null && MovableAt(c) == null;

        /// <summary>No board item on the tile: no pickup and no movable tile (§1.1.3, GG-T129 — at most one item per tile, ever).</summary>
        public bool ItemFree(Coord c) => PickupAt(c) == null && MovableAt(c) == null;

        /// <summary>Free for a new board item to appear (a pickup drop, the Power Amplifier): walkable, nobody standing
        /// there, no trap, and no other item (§1.1.3, GG-T129). Every item placement goes through this one check.</summary>
        public bool FreeForItem(Coord c) => Walkable(c) && UnitAt(c) == null && TrapAt(c) == null && ItemFree(c);
        public ManaBank Bank = new ManaBank();
        /// <summary>Progress toward the next orb, 0..ManaChargePerOrb-1 (§3.1.9).</summary>
        public int ManaCharge;
        public int Wave;
        /// <summary>Delayed mid-wave spawns (§14.8) queued by <c>SpawnWave</c>, resolved as the clock reaches their trigger tick.</summary>
        public List<PendingSpawn> PendingSpawns = new List<PendingSpawn>();
        public BattlePhase Phase = BattlePhase.PlayerInput;
        public int NextUnitId = 1;
        public int NextEntryId = 1;
        public long Clock;
        /// <summary>Seconds of drag timer left at the last hero drop (time-banked orbs, §3.1.8).</summary>
        public float PendingTimeBankSeconds;
        public BattleResults Results = new BattleResults();

        public bool IsOver => Phase != BattlePhase.PlayerInput;

        /// <summary>Per-battle override of <see cref="BattleEngine.StrictTiles"/> (null = follow it) — lets one test
        /// exercise the release self-heal while the rest of the suite runs strict (GG-T97).</summary>
        public bool? StrictTilesOverride;

        public Unit UnitById(int id)
        {
            foreach (var u in Units) if (u.Id == id) return u;
            return null;
        }

        public Unit UnitAt(Coord c)
        {
            foreach (var u in Units) if (u.Alive && u.Occupies(c)) return u;
            return null;
        }

        public static bool InBounds(Coord c) => c.X >= 0 && c.X < BattleConfig.Cols && c.Y >= 0 && c.Y < BattleConfig.Rows;

        /// <summary>Physical blocks on this board (from the stage). Immutable during a battle, so clones share it.</summary>
        public HashSet<Coord> Blocks = new HashSet<Coord>();

        public bool IsBlock(Coord c) => Blocks.Contains(c);

        /// <summary>The collider check: on the board and not a physical block. Every enter/land/displace/spawn goes through it.</summary>
        public bool Walkable(Coord c) => InBounds(c) && !Blocks.Contains(c);

        public IEnumerable<Unit> Living(Team team)
        {
            foreach (var u in Units) if (u.Alive && u.Team == team) yield return u;
        }

        public List<Unit> LivingList(Team team)
        {
            var l = new List<Unit>();
            foreach (var u in Units) if (u.Alive && u.Team == team) l.Add(u);
            return l;
        }

        public TimelineEntry TurnEntryOf(int unitId)
        {
            foreach (var e in Timeline) if (e.Kind == TimelineKind.Turn && e.OwnerId == unitId) return e;
            return null;
        }

        public TimelineEntry CastEntryOf(int unitId)
        {
            foreach (var e in Timeline) if (e.Kind == TimelineKind.Cast && e.OwnerId == unitId) return e;
            return null;
        }

        public int HeroCastsInFlight
        {
            get
            {
                int n = 0;
                foreach (var e in Timeline) if (e.Kind == TimelineKind.Cast && e.CasterTeam == Team.Hero) n++;
                return n;
            }
        }

        public Trap TrapAt(Coord c)
        {
            foreach (var t in Traps) if (t.At == c) return t;
            return null;
        }

        public BattleState Clone()
        {
            var s = (BattleState)MemberwiseClone();
            s.Rng = Rng.Clone();
            s.Units = new List<Unit>(Units.Count);
            foreach (var u in Units) s.Units.Add(u.Clone());
            s.Timeline = new List<TimelineEntry>(Timeline.Count);
            foreach (var e in Timeline) s.Timeline.Add(e.Clone());
            s.Movables = new List<MovableTile>(Movables.Count);
            foreach (var m in Movables) s.Movables.Add(m.Clone());
            s.Traps = new List<Trap>(Traps.Count);
            foreach (var t in Traps) s.Traps.Add(t.Clone());
            s.Pickups = new List<Pickup>(Pickups.Count);
            foreach (var p in Pickups) s.Pickups.Add(p.Clone());
            s.PendingSpawns = new List<PendingSpawn>(PendingSpawns.Count);
            foreach (var p in PendingSpawns) s.PendingSpawns.Add(p.Clone());
            s.Bank = Bank.Clone();
            s.Results = Results.Clone();
            return s;
        }
    }
}
