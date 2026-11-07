using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    public enum SlotStatus { Ready, Empty, Locked, Unaffordable, OutOfCharges, Cooldown, Silenced, CastCapReached, CannotAct, NoTargets, Acted, TooLittleTime }

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
                Difficulty = setup.Difficulty,
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
            TalkCommand t => t.HeroId, // the talking hero acts (spends the turn), so its seat must own it
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
            return s.Phase == BattlePhase.PlayerInput && h != null && h.Alive && h.IsCommandable && !h.IsImmobile && !h.ActedThisWindow
                && ActionFits(s, MinDragSeconds); // GG-T311: no drag starts in a gap too short for one
        }

        // ── Minimum action time (GG-T311, §2.0) ──────────────────────────────────────────────────────
        // Owner: "Heroes go first in the round, but unless they can get in a pincer between the intervals between enemies
        // hitting left they have to sacrifice their turn, and that could lead to several enemy attacks in a row before a
        // window wide enough for the player to act becomes available."

        /// <summary>Clock ticks <paramref name="seconds"/> of action needs (rounded up, at least 1).</summary>
        public static int NeedTicks(float seconds) =>
            System.Math.Max(1, (int)System.Math.Ceiling(seconds * BattleConfig.TicksPerSecond - 0.001f));

        /// <summary>The shortest drag that does anything: pick the hero up and walk one tile.</summary>
        public static float MinDragSeconds => DragSeconds(1);

        /// <summary>The human drag model: <see cref="BattleConfig.DragPickupSeconds"/> + <see cref="BattleConfig.DragSecondsPerTile"/> × tiles.</summary>
        public static float DragSeconds(int tiles) =>
            BattleConfig.DragPickupSeconds + BattleConfig.DragSecondsPerTile * System.Math.Max(1, tiles);

        /// <summary>The least time a command needs from the gap before it may start: a drag the minimum drag, an
        /// Ability/Item/Spell/Talk its use time (<see cref="BattleConfig.HeroActionSeconds"/>); Guard nothing.</summary>
        public static float MinActionSeconds(BattleCommand cmd)
        {
            switch (cmd)
            {
                case MoveCommand _: return MinDragSeconds;
                case UseSlotCommand _:
                case TalkCommand _: return BattleConfig.HeroActionSeconds;
                default: return 0f;
            }
        }

        /// <summary>Does an action of <paramref name="seconds"/> fit in the gap (the time until the next enemy reaches the
        /// left of the bar)? Always, when no enemy icon can arrive.</summary>
        public static bool ActionFits(BattleState s, float seconds)
        {
            if (!(seconds > 0f)) return true;
            int gap = TurnTicks(s);
            return gap < 0 || gap >= NeedTicks(seconds);
        }

        /// <summary>
        /// GG-T311: heroes are left to act but the gap is too short for any of their actions (the cheapest is a minimum
        /// drag or a skill's use time) — the party can only Guard, giving the window up, and the enemy acts.
        /// </summary>
        public static bool TooLittleTime(BattleState s)
        {
            if (s.Phase != BattlePhase.PlayerInput || TurnTicks(s) < 0) return false;
            if (HeroesLeftToAct(s).Count == 0) return false;
            return !ActionFits(s, System.Math.Min(MinDragSeconds, BattleConfig.HeroActionSeconds));
        }

        // ── The party's window (GG-T308, §2.0) ────────────────────────────────────────────────────────

        /// <summary>Living, able heroes (not Immobile) who have not acted yet in this window. Empty = the window is spent.</summary>
        public static List<Unit> HeroesLeftToAct(BattleState s)
        {
            var list = new List<Unit>();
            foreach (var u in s.Units)
                if (u.Alive && u.IsCommandable && u.OnBoard && !u.IsImmobile && !u.ActedThisWindow) list.Add(u);
            return list;
        }

        /// <summary>
        /// Clock ticks a hero command spends from the window: a drag the share of the window it was held (the view plays
        /// the bar at the same rate; a forced drop spends all of it; never under 1 tick), an Ability/Item/Spell/Talk the
        /// fixed <see cref="BattleConfig.HeroActionSeconds"/>. Guard spends the rest of the window instead (0 here).
        /// </summary>
        public static int CommandTicks(BattleState s, BattleCommand cmd)
        {
            switch (cmd)
            {
                case MoveCommand m: // GG-T311: never less than the minimum drag (pickup + a tile), never more than the window
                {
                    int t = System.Math.Max(HoldTicks(s, m.HeldSeconds), NeedTicks(MinDragSeconds));
                    int turn = TurnTicks(s);
                    return turn > 0 ? System.Math.Min(turn, t) : t;
                }
                case UseSlotCommand _:
                case TalkCommand _:
                    return System.Math.Max(1, Formulas.SecondsToTicks(BattleConfig.HeroActionSeconds));
                default: return 0;
            }
        }

        /// <summary>Clock ticks a hold of <paramref name="heldSeconds"/> real seconds stands for (see <see cref="CommandTicks"/>).</summary>
        public static int HoldTicks(BattleState s, float heldSeconds)
        {
            if (!(heldSeconds > 0f)) return 1;
            int turn = TurnTicks(s);
            if (turn < 0) return System.Math.Max(1, (int)System.Math.Round(heldSeconds * BattleConfig.TicksPerSecond));
            if (IsTimeUp(s, heldSeconds)) return turn;
            float w = TurnWindowSeconds(s);
            int t = (int)System.Math.Floor(heldSeconds / w * turn + 0.0001f);
            return System.Math.Max(1, System.Math.Min(turn, t));
        }

        public static SlotStatus GetSlotStatus(BattleState s, int heroId, int slot)
        {
            var h = s.UnitById(heroId);
            if (h == null || !h.Alive) return SlotStatus.CannotAct;
            if (slot < 0 || slot >= BattleRunner.MaxSlots) return SlotStatus.Empty;
            if (slot >= h.UnlockedSlots) return SlotStatus.Locked;
            if (slot >= h.Bar.Count || h.Bar[slot].IsEmpty) return SlotStatus.Empty;
            if (h.IsImmobile || h.IsIndependent) return SlotStatus.CannotAct; // a tamed ally acts on its own (GG-T303)
            if (h.ActedThisWindow) return SlotStatus.Acted; // GG-T308: one action per hero per window
            if (!ActionFits(s, BattleConfig.HeroActionSeconds)) return SlotStatus.TooLittleTime; // GG-T311
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

        // ── The one timeline model (GG-T286, Resonance-style): every icon moves at one linear rate; where it is drawn is
        //    the seconds until it acts. No Zone, no crawl. ──

        /// <summary>IP a turn icon gains per tick right now: <see cref="BattleConfig.BaseIpPerTick"/> × tempo × status × the
        /// difficulty dial (never under 1). GG-T318: Speed is not in it; it sets where the icon re-enters
        /// (<see cref="EntrySeconds"/>). Halting (Frozen) is not applied here — the clock simply skips a halted icon.</summary>
        public static int TurnRatePerTick(BattleState s, Unit u) =>
            System.Math.Max(1, (int)System.Math.Round(BattleConfig.BaseIpPerTick * TimelineScale(s, u) * DialSpeed(s, u)));

        /// <summary>GG-T311: the difficulty dial's enemy pace (1.0 at level 10, on test stages and for anyone not an enemy).</summary>
        private static float DialSpeed(BattleState s, Unit u) =>
            u.Team != Team.Enemy || s.Stage == null || s.Stage.IsTest ? 1f : DifficultyTable.At(s.Difficulty).EnemySpeed;

        /// <summary>GG-T318: the difficulty row whose spacing columns apply. Test stages and hand-built setups use the
        /// neutral row (no spacing, no jitter), so their timing stays exact.</summary>
        public static DifficultyRow SpacingRow(BattleState s) =>
            s.Stage == null || s.Stage.IsTest ? DifficultyTable.At(DifficultyTable.Neutral) : DifficultyTable.At(s.Difficulty);

        /// <summary>
        /// GG-T318 (§2.1): seconds from re-entering the bar to acting, at base pace. It is set by Speed against the average
        /// Speed for the unit's level: <c>EntryBase × (ReferenceSpeed / Speed)^EntrySpeedExponent</c>, clamped to
        /// EntryMin..EntryMax. An average foe enters <see cref="BattleConfig.EntryBaseSeconds"/> (4 s) out, and a faster one
        /// further left.
        /// </summary>
        public static float EntrySeconds(Unit u)
        {
            float speed = System.Math.Max(1f, u.Effective.Speed);
            float reference = Formulas.ReferenceEnemySpeed(u.Level);
            float secs = BattleConfig.EntryBaseSeconds * (float)System.Math.Pow(reference / speed, BattleConfig.EntrySpeedExponent);
            return System.Math.Max(BattleConfig.EntryMinSeconds, System.Math.Min(BattleConfig.EntryMaxSeconds, secs));
        }

        /// <summary>The IP a turn icon re-enters the bar at: <see cref="EntrySeconds"/> short of a full load, at base pace.</summary>
        public static int EntryIp(Unit u) =>
            System.Math.Max(0, BattleConfig.BarLength - (int)System.Math.Round(EntrySeconds(u) * BattleConfig.TicksPerSecond * BattleConfig.BaseIpPerTick));

        /// <summary>
        /// Clock ticks until this icon acts (a turn reaches the end of its load, a cast lands) at today's rates. Recovery
        /// and stun are included; no future pushback, haste or buff change is assumed. -1 when it never will: its owner is
        /// gone, or (unless <paramref name="ignoreHalt"/>) halted by Frozen.
        /// </summary>
        public static int TicksUntilAct(BattleState s, TimelineEntry e, bool ignoreHalt = false)
        {
            var u = s.UnitById(e.OwnerId);
            if (u == null || !u.Alive) return -1;
            if (!ignoreHalt && u.IsTimelineHalted) return -1;
            if (e.Kind == TimelineKind.Cast) return System.Math.Max(0, e.RemainingTicks);
            int left = System.Math.Max(0, BattleConfig.BarLength - e.Ip);
            int rate = TurnRatePerTick(s, u);
            return System.Math.Max(0, e.RecoverTicks) + System.Math.Max(0, e.StunTicks) + (left + rate - 1) / rate;
        }

        /// <summary>GG-T318: is this turn icon off the bar, recovering after its last action?</summary>
        public static bool InRecovery(TimelineEntry e) => e.Kind == TimelineKind.Turn && e.RecoverTicks > 0;

        /// <summary>GG-T319: recovery ticks left <paramref name="ticks"/> from now (a Frozen owner's recovery holds).</summary>
        public static int RecoverTicksAfter(BattleState s, TimelineEntry e, int ticks)
        {
            if (e.Kind != TimelineKind.Turn || e.RecoverTicks <= 0) return 0;
            var u = s.UnitById(e.OwnerId);
            if (u == null || !u.Alive || u.IsTimelineHalted) return e.RecoverTicks;
            return System.Math.Max(0, e.RecoverTicks - System.Math.Max(0, ticks));
        }

        /// <summary>GG-T319: where on the bar a recovering icon will re-enter (u of its entry: stun and the trip from its Ip,
        /// recovery excluded). For an icon already on the bar this is just <see cref="BarU"/>.</summary>
        public static float EntryBarU(BattleState s, TimelineEntry e)
        {
            int t = TicksUntilAct(s, e, ignoreHalt: true);
            return t < 0 ? 1f : UForTicks(System.Math.Max(0, t - System.Math.Max(0, e.Kind == TimelineKind.Turn ? e.RecoverTicks : 0)));
        }

        /// <summary>Seconds until this icon (turn or cast) acts at today's rates; +∞ when it never will (gone/Frozen).</summary>
        public static float SecondsUntilAct(BattleState s, TimelineEntry e)
        {
            int t = TicksUntilAct(s, e);
            return t < 0 ? float.PositiveInfinity : t / (float)BattleConfig.TicksPerSecond;
        }

        /// <summary>
        /// Where the icon sits on the drawn bar: clamp01(secondsUntilAct / <see cref="BattleConfig.BarSeconds"/>). 0 = the
        /// LEFT edge (acting now); a cast is born at castSeconds / BarSeconds. A Frozen icon holds where it was; a gone one
        /// sits at the right end.
        /// </summary>
        public static float BarU(BattleState s, TimelineEntry e) => UForTicks(TicksUntilAct(s, e, ignoreHalt: true));

        private static float UForTicks(int ticks) =>
            ticks < 0 ? 1f : System.Math.Max(0f, System.Math.Min(1f, ticks / (BattleConfig.TicksPerSecond * BattleConfig.BarSeconds)));

        /// <summary>Clock ticks until this unit's turn icon acts (see <see cref="TicksUntilAct"/>); -1 with no icon or Frozen.</summary>
        public static int TicksUntilTurn(BattleState s, int unitId)
        {
            var e = s.TurnEntryOf(unitId);
            return e == null ? -1 : TicksUntilAct(s, e);
        }

        /// <summary>Enemy turn-icon rate factor from the party's net tempo (GG-T268, §2.11): TempoStep^−net, clamped.</summary>
        public static float EnemyTempoMultiplier(BattleState s)
        {
            // GG-T311: the party's rally (forced Guards in a row) slows foes a further tempo step each, outside the tempo
            // clamp, so even a lock at the slowest tempo still opens.
            float rally = s.Rally > 0 ? (float)System.Math.Pow(BattleConfig.TempoStep, -s.Rally) : 1f;
            int net = s.NetTempo;
            if (net == 0) return rally;
            float m = (float)System.Math.Pow(BattleConfig.TempoStep, -net);
            return rally * System.Math.Max(BattleConfig.TempoMinMultiplier, System.Math.Min(BattleConfig.TempoMaxMultiplier, m));
        }

        /// <summary>A unit's turn-icon rate factor: its own buffs, times the party tempo for anyone not on the hero team.</summary>
        public static float TimelineScale(BattleState s, Unit u) =>
            u.TimelineSpeedMultiplier * (u.IsHero ? 1f : EnemyTempoMultiplier(s));

        /// <summary>
        /// Player turns until this enemy acts (Terra Battle-style countdown badge). 0 = it acts at the end of your next
        /// turn. GG-T188 (§2.0): every turn ends at the next enemy arrival, so this counts the distinct arrival ticks of
        /// the other enemies that land before this one's. GG-T318: after acting, each one recovers, then re-enters at its
        /// entry. The estimate uses base recovery and no spacing.
        /// </summary>
        public static int ActionsUntilTurn(BattleState s, int unitId)
        {
            int t = TicksUntilTurn(s, unitId);
            if (t < 0) return -1;
            var earlier = new HashSet<int>();
            foreach (var e in s.Timeline)
            {
                if (e.Kind != TimelineKind.Turn || e.OwnerId == unitId) continue;
                var o = s.UnitById(e.OwnerId);
                int first = TicksUntilAct(s, e);
                if (o == null || first < 0) continue;
                int rate = TurnRatePerTick(s, o);
                int lap = System.Math.Max(1, NeedTicks(BattleConfig.RecoverBaseSeconds) + (BattleConfig.BarLength - EntryIp(o) + rate - 1) / rate);
                for (int tick = first; tick < t; tick += lap) if (tick >= 1) earlier.Add(tick);
            }
            return earlier.Count;
        }

        // ── The held turn (GG-T188, §2.0) ─────────────────────────────────────────────────────────────

        /// <summary>
        /// What is left of the party's window in clock ticks: until the next enemy turn icon reaches the left edge (no
        /// pushback/hasten assumed). Each hero action spends some of it and a Pushback or Slow lengthens it (GG-T308). -1 when no enemy icon can arrive (none left, or every one halted) — the hold then has no limit.
        /// </summary>
        public static int TurnTicks(BattleState s)
        {
            int best = -1;
            foreach (var e in s.Timeline)
            {
                if (e.Kind != TimelineKind.Turn) continue;
                var o = s.UnitById(e.OwnerId);
                if (o == null || o.IsHero) continue;
                int t = TicksUntilTurn(s, e.OwnerId);
                if (t > 0 && (best < 0 || t < best)) best = t;
            }
            return best;
        }

        /// <summary>Real seconds a held hero has before the forced drop: the gap's ticks at TicksPerSecond (GG-T311: no
        /// floor — a gap too short to drag in can't start a drag at all). -1 = unlimited (no enemy icon can arrive).</summary>
        public static float TurnWindowSeconds(BattleState s)
        {
            int t = TurnTicks(s);
            if (t < 0) return -1f;
            return t / (float)BattleConfig.TicksPerSecond;
        }

        /// <summary>
        /// Guard bank (GG-T189, §2.5): the mana charge a Guard pressed after <paramref name="heldSeconds"/> of the turn
        /// earns — the turn's window minus the time already used, at <see cref="BattleConfig.GuardChargePerSecond"/>,
        /// capped at <see cref="BattleConfig.GuardMaxCharge"/>. With no enemy icon able to arrive the turn is the fixed
        /// <see cref="BattleConfig.ActionTicks"/> it would spend.
        /// </summary>
        public static int GuardCharge(BattleState s, float heldSeconds = 0f)
        {
            float w = TurnWindowSeconds(s);
            if (w < 0f) w = BattleConfig.ActionTicks / (float)BattleConfig.TicksPerSecond;
            return GuardChargeFor(w - System.Math.Max(0f, heldSeconds));
        }

        /// <summary>Fullness damping (§3.1.9, GG-T247): earned mana charge is scaled down as the shared bank fills.</summary>
        public static float ChargeDamping(BattleState s)
        {
            if (s.Bank.Capacity <= 0) return 1f;
            return 1f - BattleConfig.ChargeFullnessDamping * System.Math.Min(1f, s.Bank.Count / (float)s.Bank.Capacity);
        }

        /// <summary>An earned charge after fullness damping; a positive amount never rounds to nothing.</summary>
        public static int DampedCharge(BattleState s, int amount) =>
            amount <= 0 ? 0 : System.Math.Max(1, (int)System.Math.Round(amount * ChargeDamping(s)));

        /// <summary>The conversion maths alone: <paramref name="secondsLeft"/> of unused turn → mana charge (0..GuardMaxCharge).</summary>
        public static int GuardChargeFor(float secondsLeft)
        {
            if (!(secondsLeft > 0f)) return 0;
            double raw = System.Math.Round(secondsLeft * (double)BattleConfig.GuardChargePerSecond);
            return (int)System.Math.Min(BattleConfig.GuardMaxCharge, raw);
        }

        /// <summary>Did a hold of <paramref name="heldSeconds"/> run out the turn (a forced drop)?</summary>
        public static bool IsTimeUp(BattleState s, float heldSeconds)
        {
            if (heldSeconds <= 0f) return false;
            float w = TurnWindowSeconds(s);
            return w > 0f && heldSeconds >= w - 0.001f;
        }

        /// <summary>How many tiles a drag by the human model (<see cref="DragSeconds"/>, plus <paramref name="thinkSeconds"/>
        /// of hesitation while held) covers before the window runs out; int.MaxValue when unlimited, 0 when not even one.</summary>
        public static int MaxDragSteps(BattleState s, float thinkSeconds = 0f)
        {
            float w = TurnWindowSeconds(s);
            if (w < 0f) return int.MaxValue;
            float left = w - BattleConfig.DragPickupSeconds - System.Math.Max(0f, thinkSeconds);
            if (left <= 0f || BattleConfig.DragSecondsPerTile <= 0f) return left > 0f ? int.MaxValue : 0;
            return System.Math.Max(0, (int)System.Math.Floor(left / BattleConfig.DragSecondsPerTile + 0.0001f));
        }

        /// <summary>
        /// Where every timeline icon would sit on the bar (<see cref="BarU"/>, 0 = left/acting) <paramref name="ticks"/>
        /// into the held turn, by entry id, for the view to run the bar in real time while a hero is held. Icons stop at
        /// the left edge; a Frozen icon holds; nothing resolves (the hold always ends at or before the first arrival).
        /// </summary>
        public static Dictionary<int, float> TimelineUAfter(BattleState s, int ticks)
        {
            var at = new Dictionary<int, float>();
            foreach (var e in s.Timeline)
            {
                var o = s.UnitById(e.OwnerId);
                int left = TicksUntilAct(s, e, ignoreHalt: true);
                if (left >= 0 && !o.IsTimelineHalted) left = System.Math.Max(0, left - System.Math.Max(0, ticks));
                at[e.Id] = UForTicks(left);
            }
            return at;
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
