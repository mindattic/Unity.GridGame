using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    public enum SlotStatus { Ready, Empty, Locked, Unaffordable, OutOfCharges, Cooldown, Silenced, CastCapReached, CannotAct, NoTargets }

    public sealed class MovePreview
    {
        public Dictionary<int, Coord> Positions = new Dictionary<int, Coord>();
        public List<Coord> StepsTaken = new List<Coord>();
        public List<Pincer> Pincers = new List<Pincer>();
        public bool Blocked;
    }

    /// <summary>
    /// The battle rules engine. The only way a battle changes is <see cref="Apply"/>: one committed
    /// player command → the action resolves → the clock advances → enemies/casts that arrive act →
    /// control returns to the player. Every consequence is returned as an ordered event list.
    /// Deterministic for a given (content, setup, seed, command sequence).
    /// </summary>
    public static class BattleEngine
    {
        /// <summary>The five armor-piece slots, in the order §24.2's equip screen lists them (GG-T89).</summary>
        public static readonly EquipmentSlot[] ArmorSlots =
        {
            EquipmentSlot.Head, EquipmentSlot.Torso, EquipmentSlot.Legs, EquipmentSlot.Gloves, EquipmentSlot.Boots,
        };

        /// <summary>
        /// GG-T97 strict board mode: any moment two actors share a tile (unit/unit, unit/movable, or anything off the
        /// walkable board) throws <see cref="TileConflictException"/> instead of the release build's last-resort
        /// self-heal. The test suite turns it on for every test (and the bot soak); a shipped game leaves it off.
        /// </summary>
        public static bool StrictTiles;

        public static BattleState Create(ContentDb content, BattleSetup setup, List<BattleEvent> events)
        {
            var r = new BattleRunner(new BattleState
            {
                Content = content,
                Stage = setup.Stage,
                RecommendedLevel = setup.RecommendedLevel,
                NewGamePlus = setup.NewGamePlus,
                Rng = new Rng(setup.Seed),
                Blocks = new HashSet<Coord>(setup.Stage?.Blocks ?? new List<Coord>()),
            }, events);
            r.Setup(setup);
            r.EnforceTileInvariant("Setup");
            return r.S;
        }

        public static List<BattleEvent> Apply(BattleState s, BattleCommand cmd)
        {
            var events = new List<BattleEvent>();
            new BattleRunner(s, events).Apply(cmd);
            return events;
        }

        /// <summary>The hero a command acts for (-1 for party-wide commands like Guard).</summary>
        public static int CommandHeroId(BattleCommand cmd) => cmd switch
        {
            MoveCommand m => m.HeroId,
            UseSlotCommand u => u.HeroId,
            _ => -1,
        };

        /// <summary>
        /// GG-LAW-13 ownership: a command that acts for a hero must come from that hero's seat. Party-wide commands
        /// (Guard) are open to every seat. An unknown hero id is left to the rules, which already refuse it.
        /// </summary>
        public static bool IsAuthorized(BattleState s, BattleCommand cmd)
        {
            int heroId = CommandHeroId(cmd);
            if (heroId < 0) return true;
            var h = s.UnitById(heroId);
            return h == null || h.Owner == cmd.Player;
        }

        public static bool CanMove(BattleState s, int heroId)
        {
            var h = s.UnitById(heroId);
            return s.Phase == BattlePhase.PlayerInput && h != null && h.Alive && h.IsHero && !h.IsImmobile;
        }

        public static SlotStatus GetSlotStatus(BattleState s, int heroId, int slot)
        {
            var h = s.UnitById(heroId);
            if (h == null || !h.Alive) return SlotStatus.CannotAct;
            if (slot < 0 || slot >= BattleRunner.MaxSlots) return SlotStatus.Empty;
            if (slot >= h.UnlockedSlots) return SlotStatus.Locked;
            if (slot >= h.Bar.Count || h.Bar[slot].IsEmpty) return SlotStatus.Empty;
            if (h.IsImmobile) return SlotStatus.CannotAct;
            var sl = h.Bar[slot];
            if (sl.IsWeapon) return SlotStatus.Ready;
            if (sl.IsItem)
            {
                if (sl.Charges <= 0) return SlotStatus.OutOfCharges;
                return SlotStatus.Ready;
            }
            var a = s.Content.TryAbility(sl.AbilityId);
            if (a == null) return SlotStatus.Empty;
            if (a.Kind == AbilityKind.Skill && h.Cooldowns.TryGetValue(a.Id, out var cd) && cd > 0) return SlotStatus.Cooldown;
            if (a.Kind == AbilityKind.Spell)
            {
                if (h.IsSilenced) return SlotStatus.Silenced;
                if (!s.Bank.CanAfford(a.Cost)) return SlotStatus.Unaffordable;
                if (a.CastTime > 0f && s.HeroCastsInFlight >= BattleConfig.MaxHeroCastsInFlight) return SlotStatus.CastCapReached;
            }
            return SlotStatus.Ready;
        }

        /// <summary>
        /// Clock ticks until this enemy's turn icon reaches the trigger, assuming no pushback/hasten
        /// (stun and the shared Zone crawl included). -1 when it has no icon or is halted (Frozen).
        /// </summary>
        public static int TicksUntilTurn(BattleState s, int unitId)
        {
            var u = s.UnitById(unitId);
            var e = s.TurnEntryOf(unitId);
            if (u == null || e == null || !u.Alive) return -1;
            if (u.IsTimelineHalted) return -1;
            int ip = e.Ip, ticks = e.StunTicks;
            float mult = u.TimelineSpeedMultiplier;
            int zoneRate = System.Math.Max(1, (BattleConfig.BarLength - BattleConfig.ZoneStart) / BattleConfig.ZoneCrossingTicks);
            for (int guard = 0; guard < 10000 && ip < BattleConfig.BarLength; guard++)
            {
                float rate = ip >= BattleConfig.ZoneStart ? zoneRate : u.Effective.Speed * BattleConfig.OpenRatePerSpeed;
                ip += System.Math.Max(1, (int)System.Math.Round(rate * mult));
                ticks++;
            }
            return ticks;
        }

        /// <summary>How far along the shared u-axis an icon moves per tick right now (0 when halted/stunned) — the view uses it to decide who overtakes whom.</summary>
        public static float UPerTick(BattleState s, TimelineEntry e)
        {
            var u = s.UnitById(e.OwnerId);
            if (u == null || !u.Alive || u.IsTimelineHalted) return 0f;
            if (e.Kind == TimelineKind.Cast) return 1f / System.Math.Max(1, e.TotalCastTicks + e.AccumulatedDelayTicks);
            if (e.StunTicks > 0) return 0f;
            int zoneRate = System.Math.Max(1, (BattleConfig.BarLength - BattleConfig.ZoneStart) / BattleConfig.ZoneCrossingTicks);
            float rate = e.Ip >= BattleConfig.ZoneStart ? zoneRate : u.Effective.Speed * BattleConfig.OpenRatePerSpeed;
            return System.Math.Max(1, (int)System.Math.Round(rate * u.TimelineSpeedMultiplier)) / (float)BattleConfig.BarLength;
        }

        /// <summary>Player actions until this enemy acts (Terra Battle-style countdown badge). 0 = acts after your next action.</summary>
        public static int ActionsUntilTurn(BattleState s, int unitId)
        {
            int t = TicksUntilTurn(s, unitId);
            if (t < 0) return -1;
            return System.Math.Max(0, (t - 1) / BattleConfig.ActionTicks);
        }

        /// <summary>Writes every hero's end-of-battle gear state (weapon/armor durability, final weapon, bar weapons)
        /// into <see cref="BattleState.Results"/> for the meta layer to commit. Victory does this as the battle ends;
        /// the Crucible (§22.10) also calls it after a wipe or retreat, since it keeps that battle's wear too.</summary>
        public static void SnapshotGear(BattleState s)
        {
            foreach (var h in s.Units)
            {
                if (!h.IsHero) continue;
                s.Results.WeaponDurability[h.Def.Id] = h.WeaponId != null ? h.WeaponDurability : -1;
                var armorDur = new Dictionary<EquipmentSlot, int>();
                foreach (var slot in ArmorSlots) armorDur[slot] = h.Gear.TryGetValue(slot, out var piece) ? piece.Durability : -1;
                s.Results.ArmorDurability[h.Def.Id] = armorDur;
                s.Results.FinalWeapon[h.Def.Id] = h.WeaponId;
                s.Results.FinalBar[h.Def.Id] = h.Bar.ConvertAll(sl => sl.WeaponId != null ? "weapon:" + sl.WeaponId : null);
            }
        }

        /// <summary>Snake members are armored (immune) while a later chain member lives (§14.2).</summary>
        public static bool IsArmored(BattleState s, Unit u)
        {
            if (!u.IsSnakeMember) return false;
            foreach (var o in s.Units)
                if (o.Alive && o.SnakeHeadId == u.SnakeHeadId && o.SnakeIndex > u.SnakeIndex) return true;
            return false;
        }

        /// <summary>The ability a slot will fire (items resolve to their on-use ability or a synthesized heal/damage).</summary>
        /// <summary>The ability behind an id as events report it: a content ability, or "item:&lt;id&gt;" for a consumable's synthesized effect.</summary>
        public static AbilityDef ResolveAbilityId(ContentDb c, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("item:")) return BattleRunner.ItemAbility(c, c.TryItem(id.Substring(5)));
            return c.TryAbility(id);
        }

        public static AbilityDef SlotAbility(BattleState s, Unit hero, int slot)
        {
            if (slot < 0 || slot >= hero.Bar.Count) return null;
            return BattleRunner.AbilityForSlot(s.Content, hero.Bar[slot]);
        }

        /// <summary>All legal anchor tiles for a slot (empty list for Auto abilities — they need no pick).</summary>
        public static List<Coord> ValidTargets(BattleState s, int heroId, int slot)
        {
            var list = new List<Coord>();
            var h = s.UnitById(heroId);
            if (h == null) return list;
            var a = SlotAbility(s, h, slot);
            if (a == null || a.Mode == TargetMode.Auto) return list;
            for (int y = 0; y < BattleConfig.Rows; y++)
                for (int x = 0; x < BattleConfig.Cols; x++)
                {
                    var c = new Coord(x, y);
                    if (TargetResolver.IsValidPick(s, a, h, c)) list.Add(c);
                }
            return list;
        }

        /// <summary>Applies only the movement of a drag to a copy of the state (no time, no damage) for the live ghost preview.</summary>
        public static MovePreview PreviewMove(BattleState s, int heroId, List<Coord> path)
        {
            var copy = s.Clone();
            var r = new BattleRunner(copy, new List<BattleEvent>()) { PreviewOnly = true };
            var preview = new MovePreview();
            var hero = copy.UnitById(heroId);
            if (hero == null) return preview;
            preview.Blocked = !r.WalkPath(hero, path, preview.StepsTaken);
            foreach (var u in copy.Units) if (u.Alive) preview.Positions[u.Id] = u.Pos;
            preview.Pincers = PincerDetector.Detect(copy, Team.Hero, hero);
            return preview;
        }
    }
}
