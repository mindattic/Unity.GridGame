using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>Executes one command against a state, appending events. Split across partial files by concern.</summary>
    internal sealed partial class BattleRunner
    {
        public const int MaxSlots = 5;

        public readonly BattleState S;
        private readonly List<BattleEvent> ev;
        public bool PreviewOnly;

        /// <summary>
        /// Enemies already pushed back this player action (owner direction 2026-10-11 capped a single push's
        /// magnitude, but a pincer's several landed hits on the same target each re-triggered Pushback, stacking
        /// far past that cap — a big combo could erase an enemy's whole Zone approach in one action).
        /// </summary>
        private readonly HashSet<int> pushedThisAction = new HashSet<int>();

        private ContentDb C => S.Content;

        public BattleRunner(BattleState s, List<BattleEvent> events)
        {
            S = s;
            ev = events;
        }

        private void Emit(BattleEvent e)
        {
            if (!PreviewOnly) ev.Add(e);
        }

        private void Announce(string text, int unitId = -1) => Emit(new AnnounceEvent { Text = text, UnitId = unitId });

        // ─────────────────────────────────────────────────────────────────────────────
        // Command dispatch
        // ─────────────────────────────────────────────────────────────────────────────

        public void Apply(BattleCommand cmd)
        {
            if (S.IsOver) { Emit(new AbilityRefusedEvent { CasterId = -1, Reason = "Battle is over" }); return; }

            pushedThisAction.Clear();
            bool acted;
            bool shield = false;
            switch (cmd)
            {
                case MoveCommand m: acted = DoMove(m); break;
                case UseSlotCommand u: acted = DoUseSlot(u); break;
                case ShieldCommand _: acted = DoShield(); shield = true; break;
                default: acted = false; break;
            }
            if (!acted) return;
            CheckTilesStrict(cmd.GetType().Name);

            S.Results.ActionsTaken++;
            CheckEnd();
            if (!S.IsOver)
            {
                if (shield) AdvanceUntilEnemyTurn();
                else AdvanceClock(BattleConfig.ActionTicks);
            }
            if (!S.IsOver) CheckEnd();
            if (!S.IsOver) ReturnControl();
            EnforceTileInvariant(cmd.GetType().Name);
            S.Results.TicksElapsed = (int)S.Clock;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Occupancy (§1.1, GG-T97): two actors never share a tile once a slide is over. Every position write in the
        // engine goes through Relocate / MoveMovable / AddUnit below, which refuse a footprint that isn't open; the
        // invariant check after every command is only a last-resort net (it throws under BattleEngine.StrictTiles).
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Is every tile of <paramref name="u"/>'s footprint, anchored at <paramref name="anchor"/>, on the walkable board
        /// and free of every other living unit and of movable tiles? <paramref name="yields"/> names units this same step
        /// moves out of the way (a swap partner, heroes a 2×2 shoves); <paramref name="movablesYield"/> is true when the
        /// mover shoves any movable it lands on. Dead units (OnBoard false) never count — they're off the board.
        /// </summary>
        private bool FootprintOpen(Unit u, Coord anchor, Func<Unit, bool> yields = null, bool movablesYield = false)
        {
            for (int dy = 0; dy < u.H; dy++)
                for (int dx = 0; dx < u.W; dx++)
                {
                    var t = new Coord(anchor.X + dx, anchor.Y + dy);
                    if (!S.Walkable(t)) return false;
                    foreach (var o in S.Units)
                        if (o != u && o.Alive && o.Occupies(t) && (yields == null || !yields(o))) return false;
                    if (!movablesYield && S.MovableAt(t) != null) return false;
                }
            return true;
        }

        /// <summary>
        /// The single chokepoint for moving a unit on the board. Refuses (false, nothing moves) when the destination
        /// footprint isn't open — under <see cref="BattleEngine.StrictTiles"/> that's a bug and it throws.
        /// </summary>
        private bool Relocate(Unit u, Coord to, Func<Unit, bool> yields = null, bool movablesYield = false)
        {
            if (!FootprintOpen(u, to, yields, movablesYield))
            {
                Violation($"{u} can't move {u.Pos}->{to}: footprint not free");
                return false;
            }
            u.Pos = to;
            return true;
        }

        /// <summary>The chokepoint for a movable tile (§1.1.2): it may only land on a walkable tile nobody stands on that
        /// holds no other item (§1.1.3, GG-T129: never onto a pickup or another movable).</summary>
        private bool MoveMovable(MovableTile m, Coord to, Unit by)
        {
            var from = m.Pos;
            if (from == to) return true;
            var other = S.MovableAt(to);
            if (!S.Walkable(to) || S.UnitAt(to) != null || (other != null && other != m) || PickupBlocksMovable(to))
            {
                Violation($"movable at {from} can't be pushed to {to}");
                return false;
            }
            m.Pos = to;
            Emit(new AmplifierMovedEvent { From = from, To = to, ByUnitId = by?.Id ?? -1 });
            return true;
        }

        /// <summary>
        /// Does a pickup on <paramref name="c"/> keep a movable off it (§1.1.3)? Always, except in a hero-path preview:
        /// there pickups aren't collected, so the tiles the walk already crossed still list pickups that the real move
        /// will have picked up before the amplifier is shoved back onto them.
        /// </summary>
        private bool PickupBlocksMovable(Coord c) => !PreviewOnly && S.PickupAt(c) != null;

        /// <summary>The nearest tile a movable may land on (walkable, nobody there, no item), or null on a full board.</summary>
        private Coord? NearestMovableSpot(MovableTile m, Coord near)
        {
            Coord? best = null; int bestD = int.MaxValue;
            for (int y = 0; y < BattleConfig.Rows; y++)
                for (int x = 0; x < BattleConfig.Cols; x++)
                {
                    var c = new Coord(x, y);
                    if (!S.Walkable(c) || S.UnitAt(c) != null || PickupBlocksMovable(c)) continue;
                    var other = S.MovableAt(c);
                    if (other != null && other != m) continue;
                    int d = c.Manhattan(near);
                    if (d < bestD) { bestD = d; best = c; }
                }
            return best;
        }

        /// <summary>The chokepoint for putting a new unit on the board (spawns, waves, reinforcements, eggs, remains, guests).</summary>
        private bool AddUnit(Unit u)
        {
            if (u.OnBoard && !FootprintOpen(u, u.Pos))
            {
                Violation($"{u} can't appear at {u.Pos}: footprint not free");
                return false;
            }
            S.Units.Add(u);
            return true;
        }

        private bool Strict => S.StrictTilesOverride ?? BattleEngine.StrictTiles;

        /// <summary>A position write that would have broken the board. Strict mode throws; release refuses the write.</summary>
        private void Violation(string what)
        {
            if (Strict && !PreviewOnly)
                throw new TileConflictException(what + " (stage " + S.Stage?.Id + ", wave " + S.Wave + ", clock " + S.Clock + ")");
        }

        /// <summary>
        /// The first tile two actors share (or an actor off the walkable board), or two board items share (§1.1.3,
        /// GG-T129: a pickup on a pickup or under a movable, or a pickup off the walkable board), or null on a healthy
        /// board. A unit standing on a pickup is fine — pickups don't block (§13.3.1).
        /// </summary>
        private (Unit mover, Unit stays, MovableTile movable, Pickup pickup, Coord at)? FindTileConflict()
        {
            var seen = new Dictionary<Coord, Unit>();
            foreach (var u in S.Units)
            {
                if (!u.Alive) continue;
                foreach (var t in u.Tiles())
                {
                    if (!S.Walkable(t)) return (u, null, null, null, t);
                    if (!seen.TryGetValue(t, out var other)) { seen[t] = u; continue; }
                    var mover = u.Id > other.Id ? u : other;
                    return (mover, mover == u ? other : u, null, null, t);
                }
            }
            var itemTiles = new HashSet<Coord>();
            foreach (var m in S.Movables)
            {
                seen.TryGetValue(m.Pos, out var under);
                if (under != null || !S.Walkable(m.Pos) || !itemTiles.Add(m.Pos)) return (under, null, m, null, m.Pos);
            }
            foreach (var p in S.Pickups)
            {
                if (!S.Walkable(p.At) || !itemTiles.Add(p.At)) return (null, null, S.MovableAt(p.At), p, p.At);
            }
            return null;
        }

        /// <summary>Strict mode only: check the board at an inner boundary (after an enemy turn, a cast, a reinforcement).</summary>
        private void CheckTilesStrict(string context)
        {
            if (!Strict || PreviewOnly) return;
            var c = FindTileConflict();
            if (c != null) throw new TileConflictException(Describe(c.Value, context));
        }

        private string Describe((Unit mover, Unit stays, MovableTile movable, Pickup pickup, Coord at) c, string context) =>
            "Tile conflict at " + c.at + " after " + context + ": "
            + (c.pickup != null
                ? "pickup " + c.pickup.Id + " (" + c.pickup.PickupId + ") vs " + (c.movable != null ? "movable" : S.Walkable(c.at) ? "another pickup" : "off-board")
                : (c.mover?.ToString() ?? "-") + " vs " + (c.movable != null ? "movable" : c.stays?.ToString() ?? "off-board"))
            + " (stage " + S.Stage?.Id + ", wave " + S.Wave + ", clock " + S.Clock + ")";

        /// <summary>
        /// Last-resort net, run after every command: two actors must never share a tile (bible §1.1/§6). Every
        /// position write already goes through the chokepoints above, so this never fires on a healthy engine. Under
        /// <see cref="BattleEngine.StrictTiles"/> (tests, soak) a conflict throws so the root cause is found; in a
        /// release build it nudges the newer unit (or the movable) to a free tile with a visible step and emits a
        /// <see cref="TileConflictEvent"/> that the App logs loudly.
        /// </summary>
        internal void EnforceTileInvariant(string lastCommand)
        {
            var found = FindTileConflict();
            if (found == null) return;
            var c = found.Value;
            if (Strict) throw new TileConflictException(Describe(c, lastCommand));
            if (c.pickup != null && c.movable == null)
            {
                // Two pickups on one tile (or one off the board): the newer one fades, so only one token is ever drawn.
                Emit(new TileConflictEvent { UnitId = -1, OtherUnitId = -1, At = c.at, Command = lastCommand });
                S.Pickups.Remove(c.pickup);
                Emit(new PickupExpiredEvent { PickupEntryId = c.pickup.Id, PickupId = c.pickup.PickupId, At = c.pickup.At });
                return;
            }
            if (c.movable != null)
            {
                Emit(new TileConflictEvent { UnitId = c.mover?.Id ?? -1, OtherUnitId = -1, At = c.at, Command = lastCommand });
                var spot = NearestMovableSpot(c.movable, c.at);
                if (spot != null && spot.Value != c.movable.Pos) MoveMovable(c.movable, spot.Value, null);
                else S.Movables.Remove(c.movable);
                return;
            }
            var mover = c.mover;
            var from = mover.Pos;
            var free = RandomFreeTile(mover.W, mover.H, 0, BattleConfig.Rows - 1);
            Emit(new TileConflictEvent { UnitId = mover.Id, OtherUnitId = c.stays?.Id ?? -1, At = c.at, Command = lastCommand });
            if (free != null)
            {
                mover.Pos = free.Value;
                Emit(new DisplacedEvent { UnitId = mover.Id, From = from, To = free.Value, ByUnitId = c.stays?.Id ?? -1 });
            }
            // One conflict per command is already an exceptional event; the next command re-scans.
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Shield (§2.5)
        // ─────────────────────────────────────────────────────────────────────────────

        private bool DoShield()
        {
            Emit(new ActionBeganEvent { ActorId = -1, Label = "Shield" });
            var prot = C.TryBuff(BattleConfig.ShieldBuffId);
            if (prot != null)
                foreach (var h in S.LivingList(Team.Hero)) ApplyBuff(h, prot, prot.Duration);
            S.PendingTimeBankSeconds = 0f;
            return true;
        }

        /// <summary>Power Amplifier (§1.2.6): with none on the board, roll the luck-scaled chance to place one on a free tile.</summary>
        private void MaybeSpawnAmplifier()
        {
            if (S.Amplifier.HasValue || S.IsOver) return;
            float luck = 0f;
            foreach (var h in S.Living(Team.Hero)) luck += h.Effective.Luck;
            if (!S.Rng.Chance(Formulas.AmplifierChance(luck))) return;
            var free = new List<Coord>();
            for (int y = 0; y < BattleConfig.Rows; y++)
                for (int x = 0; x < BattleConfig.Cols; x++)
                {
                    var c = new Coord(x, y);
                    if (S.FreeForItem(c)) free.Add(c); // never onto a pickup (§1.1.3, GG-T129)
                }
            if (free.Count == 0) return;
            var at = free[S.Rng.Range(0, free.Count)];
            S.Amplifier = at;
            Emit(new AmplifierSpawnedEvent { At = at });
            Announce("Power Amplifier!");
        }

        /// <summary>Control returns to the player: hero turn-unit buffs and skill cooldowns tick (§8.3, §4.1.1).</summary>
        /// <summary>Corpses rot away a few actions after they fall (§14.7).</summary>
        private void AgeCorpses()
        {
            foreach (var u in S.LivingList(Team.Enemy))
            {
                if (!u.IsCorpse || --u.DecayIn > 0) continue;
                var at = u.Pos;
                u.Hp = 0; u.OnBoard = false;
                Emit(new DiedEvent { UnitId = u.Id, Team = u.Team, At = at });
            }
        }

        private void ReturnControl()
        {
            AgePickups();
            AgeCorpses();
            MaybeSpawnAmplifier();
            foreach (var h in S.LivingList(Team.Hero))
            {
                if (h.RegenPerTurn > 0 && h.Hp < h.MaxHp) Heal(h, h.RegenPerTurn, h);
                // A Regeneration-enchanted armor piece (GG-T179) knits the hero like the enemy kit does.
                if (h.Hp < h.MaxHp && HasPassive(h, "regenerate")) Heal(h, Math.Max(1, h.MaxHp * BattleConfig.RegeneratePercent / 100), h);
                TickTurnBuffs(h, false);
                var keys = new List<string>(h.Cooldowns.Keys);
                foreach (var k in keys)
                    if (h.Cooldowns[k] > 0) h.Cooldowns[k]--;
            }
            // Timeline-halting debuffs on enemies (Frozen) count down on the shared turn cycle,
            // since a halted icon never reaches its own turn boundary.
            foreach (var e in S.LivingList(Team.Enemy)) TickTurnBuffs(e, true);
            Emit(new PlayerControlEvent());
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Battle end / waves
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Wave/victory/defeat bookkeeping; call after anything that can kill. Returns true if the battle ended.</summary>
        private bool CheckEnd()
        {
            if (S.IsOver) return true;
            bool heroesAlive = false, enemiesAlive = false;
            foreach (var u in S.Units)
            {
                if (!u.Alive) continue;
                if (u.Team == Team.Hero) heroesAlive = true; else if (!u.IsCorpse) enemiesAlive = true; // bodies don't hold up a wave
            }
            if (!heroesAlive)
            {
                S.Phase = BattlePhase.Defeat;
                Emit(new BattleEndedEvent { Victory = false });
                return true;
            }
            // A reinforcement still queued for this wave (§14.8) holds it open even with the board clear.
            if (!enemiesAlive) foreach (var p in S.PendingSpawns) if (!p.Consumed && p.Wave == S.Wave) enemiesAlive = true;
            if (!enemiesAlive)
            {
                Emit(new WaveClearedEvent { Wave = S.Wave });
                if (S.Wave + 1 < S.Stage.Waves.Count)
                {
                    S.Wave++;
                    SpawnWave(S.Wave);
                    return false;
                }
                S.Phase = BattlePhase.Victory;
                BattleEngine.SnapshotGear(S);
                Emit(new BattleEndedEvent { Victory = true });
                return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Mana
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Fill the mana meter; every full ManaChargePerOrb becomes an orb (§3.1.9).</summary>
        private void AddCharge(int amount, int sourceId, string reason, int participants = 0, int chain = 0)
        {
            if (amount <= 0) return;
            S.ManaCharge += amount;
            int orbs = 0;
            while (S.ManaCharge >= BattleConfig.ManaChargePerOrb) { S.ManaCharge -= BattleConfig.ManaChargePerOrb; orbs++; }
            Emit(new ManaChargedEvent { SourceId = sourceId, Amount = amount, Total = S.ManaCharge, Reason = reason, Participants = participants, Chain = chain });
            for (int i = 0; i < orbs; i++) MintOrb(sourceId, reason);
        }

        private void MintOrb(int sourceId, string reason)
        {
            bool landed = S.Bank.Add(1) == 1;
            Emit(new OrbMintedEvent { SourceUnitId = sourceId, Landed = landed, Reason = reason, Slot = landed ? S.Bank.Count - 1 : -1 });
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Damage / heal / death (the single choke-points)
        // ─────────────────────────────────────────────────────────────────────────────

        private bool IsArmored(Unit u) => BattleEngine.IsArmored(S, u);

        /// <summary>Named weapon passive (§24.6.1, GG-T47): the strongest adjacent ally hero's Guardian-type weapon
        /// reduces incoming damage to this hero (non-stacking — the best adjacent guardian wins).</summary>
        private float BestAdjacentGuardianReduction(Unit target)
        {
            float best = 0f;
            foreach (var h in S.Living(Team.Hero))
            {
                if (h == target || h.WeaponId == null || !h.IsAdjacentTo(target)) continue;
                var weapon = C.TryItem(h.WeaponId);
                if (weapon != null && weapon.AdjacentAllyDamageReduction > best) best = weapon.AdjacentAllyDamageReduction;
            }
            return best;
        }

        /// <summary>
        /// Applies damage, handles Sleep-break, threat, pushback (hero→enemy in Zone), cast stagger and death.
        /// <paramref name="source"/> null = environmental (DOT, trap): no pushback, no stagger.
        /// </summary>
        private void Damage(Unit target, int amount, Unit source, HitKind hit, DamageType type, string note = null, bool canPush = true)
        {
            if (!target.Alive) return;
            if (hit != HitKind.Miss && amount > 0 && IsArmored(target)) { amount = 0; note = "Armored!"; }
            // Tough hide kit (§14.4): physical blows land at 70%.
            if (hit != HitKind.Miss && amount > 0 && type == DamageType.Physical && !target.IsHero && HasPassive(target, "tough_hide")) amount = Math.Max(1, amount * 7 / 10);
            if (hit != HitKind.Miss && amount > 0 && target.IsHero && source != null)
            {
                float reduction = BestAdjacentGuardianReduction(target);
                if (reduction > 0f) amount = Math.Max(1, (int)Math.Round(amount * (1f - reduction)));
            }
            if (hit != HitKind.Miss && amount > 0)
                RemoveBuffsWhere(target, b => b.Def.BreaksOnDamage, "woke");
            target.Hp = Math.Max(0, target.Hp - amount);
            Emit(new DamagedEvent { TargetId = target.Id, SourceId = source?.Id ?? -1, Amount = amount, Hit = hit, Type = type, HpAfter = target.Hp, Note = note });
            if (source != null && source.IsHero && target.Team == Team.Enemy) source.DamageDealt += amount;
            if (source != null && hit != HitKind.Miss && amount > 0 && target.IsHero && source.Team == Team.Enemy)
            {
                MaybeThorns(target, source, amount); // checked before WearArmor: the blow that breaks the piece still pricks
                WearArmor(target);
            }

            if (target.Hp <= 0) { Kill(target, source); ResolveDeathBursts(); return; }
            if (source == null || hit == HitKind.Miss) return;

            if (canPush && source.IsHero && target.Team == Team.Enemy) Pushback(target, source);
            StaggerCasts(target, source);
        }

        /// <summary>Enemies whose death burst is still to go off (resolved after the blow that killed them).</summary>
        private readonly List<Unit> DeathBurstPending = new List<Unit>();
        private bool bursting;

        /// <summary>Self-destruct kit (§14.4): damage every hero adjacent to the fallen enemy.</summary>
        private void ResolveDeathBursts()
        {
            if (bursting) return;
            bursting = true;
            while (DeathBurstPending.Count > 0 && !S.IsOver)
            {
                var u = DeathBurstPending[0];
                DeathBurstPending.RemoveAt(0);
                Announce($"{u.Name} self-destructs!", u.Id);
                var victims = new List<Unit>();
                foreach (var h in S.Living(Team.Hero)) if (u.IsAdjacentTo(h)) victims.Add(h);
                Emit(new AbilityResolvedEvent { CasterId = u.Id, AbilityId = "death_burst", Anchor = u.Pos });
                foreach (var h in victims) if (h.Alive) Damage(h, u.Def.DeathBurst, null, HitKind.Normal, DamageType.Fire, "Blast", canPush: false);
            }
            bursting = false;
        }

        private void Heal(Unit target, int amount, Unit source)
        {
            if (!target.Alive || amount <= 0) return;
            int before = target.Hp;
            target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
            Emit(new HealedEvent { TargetId = target.Id, SourceId = source?.Id ?? -1, Amount = target.Hp - before, HpAfter = target.Hp });
        }

        private void Kill(Unit u, Unit killer)
        {
            var at = u.Pos;
            if (!u.IsHero && u.Def.DeathBurst > 0) DeathBurstPending.Add(u);
            u.Hp = 0;
            u.OnBoard = false;
            u.Buffs.Clear();
            S.Timeline.RemoveAll(e => e.OwnerId == u.Id);
            Emit(new DiedEvent { UnitId = u.Id, Team = u.Team, At = at });
            if (u.IsCorpse) { Announce("Cleared away", u.Id); return; }
            if (u.IsRemains) { Announce("Laid to rest", u.Id); return; }
            // A fallen guest is down, not gone (GG-T114): revive them and they can still be recruited.
            if (u.IsGuest) Announce(permadeath ? $"{u.Name} has fallen for good" : $"{u.Name} has fallen!", u.Id);
            if (u.Team == Team.Enemy && !u.NoLoot) AwardLoot(u);
            if (u.Team == Team.Enemy) LeaveRemains(u, at);
            if (u.Team == Team.Enemy && !u.NoLoot && !u.IsRemains && !u.IsSnakeSegment) MaybeDropPickup(u, at, killer);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Pickups (§13.3.1)
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>A fallen enemy sometimes leaves a pickup on its tile (or the nearest free one beside it).</summary>
        private void MaybeDropPickup(Unit fallen, Coord at, Unit killer)
        {
            if (C.Pickups.Count == 0 || S.Pickups.Count >= BattleConfig.MaxPickups || S.IsOver) return;
            float luck = killer != null && killer.IsHero ? killer.Effective.Luck : 0f;
            float chance = Math.Min(BattleConfig.PickupMaxChance, BattleConfig.PickupDropChance + luck * BattleConfig.PickupDropChancePerLuck);
            if (!S.Rng.Chance(chance)) return;
            Coord? spot = null;
            if (PickupSpotFree(at)) spot = at;
            else foreach (var d in Coord.Cardinals) if (spot == null && PickupSpotFree(at + d)) spot = at + d;
            if (spot == null) return;
            int total = 0;
            foreach (var pk in C.Pickups.Values) total += pk.Weight;
            int roll = S.Rng.Range(0, total);
            PickupDef def = null;
            foreach (var pk in C.Pickups.Values) { if (roll < pk.Weight) { def = pk; break; } roll -= pk.Weight; }
            if (def == null) return;
            var p = new Pickup { Id = S.NextPickupId++, PickupId = def.Id, At = spot.Value, ActionsLeft = BattleConfig.PickupLifetimeActions };
            S.Pickups.Add(p);
            Emit(new PickupDroppedEvent { PickupEntryId = p.Id, PickupId = def.Id, At = p.At, FromUnitId = fallen.Id });
        }

        private bool PickupSpotFree(Coord c) => S.FreeForItem(c);

        /// <summary>A hero entered a tile: collect what lies there.</summary>
        private void CollectPickup(Unit hero)
        {
            if (!hero.Alive || !hero.IsHero) return;
            foreach (var t in new List<Coord>(hero.Tiles()))
            {
                var p = S.PickupAt(t);
                if (p == null) continue;
                S.Pickups.Remove(p);
                var def = C.Pickups.TryGetValue(p.PickupId, out var d) ? d : null;
                Emit(new PickupCollectedEvent { PickupEntryId = p.Id, PickupId = p.PickupId, At = p.At, UnitId = hero.Id });
                if (def == null) continue;
                if (def.HealFraction > 0f) Heal(hero, Math.Max(1, (int)Math.Round(hero.MaxHp * def.HealFraction)), hero);
                for (int i = 0; i < def.Orbs; i++) MintOrb(hero.Id, "pickup");
                if (def.HastenU > 0f) Hasten(hero, def.HastenU);
                var buff = C.TryBuff(def.ApplyBuff);
                if (buff != null) ApplyBuff(hero, buff, buff.Duration);
            }
        }

        /// <summary>Each control return, pickups age one action; the stale ones fade.</summary>
        private void AgePickups()
        {
            for (int i = S.Pickups.Count - 1; i >= 0; i--)
            {
                var p = S.Pickups[i];
                if (--p.ActionsLeft > 0) continue;
                S.Pickups.RemoveAt(i);
                Emit(new PickupExpiredEvent { PickupEntryId = p.Id, PickupId = p.PickupId, At = p.At });
            }
        }

        /// <summary>Undead leave a solid grave marker on their tile (§14.5); destroy it or they rise again.</summary>
        private void LeaveRemains(Unit u, Coord at)
        {
            var def = C.TryClass(u.Def.Remains);
            if (def == null || u.IsMultiTile) return;
            var r = SpawnEnemy(def, u.Level, at.X, at.Y);
            if (r == null || r.Pos != at) { if (r != null) { r.OnBoard = false; r.Hp = 0; S.Timeline.RemoveAll(e => e.OwnerId == r.Id); } return; }
            if (r.IsCorpse)
            {
                // A body in the way (§14.7): no turn, never rises, rots after a few actions.
                S.Timeline.RemoveAll(e => e.OwnerId == r.Id);
                r.CorpseOf = u.Def.Id;
                r.DecayIn = BattleConfig.CorpseDecayActions;
                r.NoLoot = true;
                Emit(new RemainsLeftEvent { RemainsId = r.Id, FromUnitId = u.Id, At = at });
                return;
            }
            r.RisesAs = u.Def.Id;
            r.RisesLevel = u.Level;
            Emit(new RemainsLeftEvent { RemainsId = r.Id, FromUnitId = u.Id, At = at });
            Announce($"{u.Name} leaves {def.Name.ToLowerInvariant()} — destroy it!", r.Id);
        }

        private void AwardLoot(Unit enemy)
        {
            // A kill mid pincer-chain pays out more XP, same multiplier the chain already pays in mana (§3.1.9).
            float chainMult = 1f + BattleConfig.ChargeChainBonus * chainBonusDepth;
            var loot = new LootEvent { FromUnitId = enemy.Id, Xp = (int)Math.Round(Formulas.XpReward(enemy) * chainMult) };
            C.DropTables.TryGetValue(enemy.Def.DropTable ?? "", out var table);
            if (table != null && table.CoinsMax > 0) loot.Coins = S.Rng.Range(table.CoinsMin, table.CoinsMax + 1);
            else if (!enemy.IsSnakeSegment) loot.Coins = CoinsFor(loot.Xp);
            if (table != null)
            {
                foreach (var entry in table.Entries)
                    if (S.Rng.Chance(entry.Chance))
                        loot.Items.Add(new ItemCount { Item = entry.Item, Count = S.Rng.Range(entry.Min, entry.Max + 1) });
            }
            if (S.NewGamePlus > 0) loot.Coins = (int)Math.Round(loot.Coins * (1f + BattleConfig.NgPlusCoinBonus * S.NewGamePlus));
            S.Results.Coins += loot.Coins;
            S.Results.Xp += loot.Xp;
            foreach (var ic in loot.Items) Add(S.Results.Items, ic.Item, ic.Count);
            if (!enemy.IsSnakeSegment) Add(S.Results.Kills, enemy.Def.Id, 1);
            Emit(loot);
        }

        /// <summary>Default coin drop when a table doesn't author one: about half the XP value, ±20%.</summary>
        private int CoinsFor(int xp)
        {
            float baseCoins = Math.Max(1f, xp * 0.5f);
            return Math.Max(1, (int)Math.Round(baseCoins * S.Rng.Range(0.8f, 1.2f)));
        }

        private static void Add(Dictionary<string, int> map, string key, int n)
        {
            map.TryGetValue(key, out var v);
            map[key] = v + n;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Buffs (§8)
        // ─────────────────────────────────────────────────────────────────────────────

        private void ApplyBuff(Unit u, BuffDef def, int duration)
        {
            if (!u.Alive || def == null) return;
            duration = Math.Max(1, duration);
            if (u.Def.ImmuneBuffs != null && u.Def.ImmuneBuffs.Contains(def.Id)) { Announce("Immune!", u.Id); return; }
            if (WardedAgainst(u, def.Id)) { Announce("Warded!", u.Id); return; } // armor enchant passives (GG-T179)
            var existing = u.Buffs.Find(b => b.Def.Id == def.Id);
            if (existing != null)
            {
                switch (def.Stacking)
                {
                    case BuffStacking.Ignore: return;
                    case BuffStacking.Upgrade:
                        if (existing.Remaining >= duration) return;
                        u.Buffs.Remove(existing);
                        break;
                    case BuffStacking.Stack:
                        // Keep every stack (ticks and effects apply per stack) up to MaxStacks; at the cap the
                        // oldest stack is replaced, so re-applying still refreshes the timer.
                        var stacks = u.Buffs.FindAll(b => b.Def.Id == def.Id);
                        if (def.MaxStacks > 0 && stacks.Count >= def.MaxStacks) u.Buffs.Remove(stacks[0]);
                        break;
                    default: u.Buffs.RemoveAll(b => b.Def.Id == def.Id); break; // Refresh (§8.6 default)
                }
            }
            u.Buffs.Add(new BuffInstance { Def = def, Remaining = duration, Fresh = true });
            Emit(new BuffAppliedEvent { UnitId = u.Id, BuffId = def.Id, Duration = duration });
        }

        private void RemoveBuffsWhere(Unit u, Predicate<BuffInstance> pred, string reason)
        {
            for (int i = u.Buffs.Count - 1; i >= 0; i--)
            {
                if (!pred(u.Buffs[i])) continue;
                var id = u.Buffs[i].Def.Id;
                u.Buffs.RemoveAt(i);
                Emit(new BuffRemovedEvent { UnitId = u.Id, BuffId = id, Reason = reason });
            }
        }

        private void Expire(Unit u, BuffInstance b)
        {
            u.Buffs.Remove(b);
            Emit(new BuffRemovedEvent { UnitId = u.Id, BuffId = b.Def.Id, Reason = "expired" });
            var chain = C.TryBuff(b.Def.OnExpireApply);
            if (chain != null && u.Alive) ApplyBuff(u, chain, chain.Duration);
        }

        /// <summary>Removes a buff as if it expired (runs its expire chain) — e.g. Fire melting Frozen.</summary>
        private void ForceExpire(Unit u, string buffId)
        {
            var b = u.Buffs.Find(x => x.Def.Id == buffId);
            if (b != null) Expire(u, b);
        }

        /// <param name="haltingOnly">Only tick timeline-halting turn buffs (used for enemies at the shared turn boundary).</param>
        private void TickTurnBuffs(Unit u, bool haltingOnly)
        {
            // Snapshot first: Expire()'s OnExpireApply chain, or a Stack-cap eviction, can remove a
            // *different* buff than the one just ticked and append a freshly chained one — index-based
            // iteration over the live list would then either skip an entry or immediately re-tick the
            // brand-new instance in this same pass. Buffs added mid-pass simply wait for the next tick.
            foreach (var b in u.Buffs.ToArray())
            {
                if (!u.Buffs.Contains(b)) continue;
                if (b.Def.Unit != DurationUnit.Turns) continue;
                if (haltingOnly != b.Def.HaltsTimeline) continue;
                if (b.Def.HaltsTimeline && b.Fresh) { b.Fresh = false; continue; }
                b.Remaining--;
                if (b.Remaining <= 0) Expire(u, b);
                else Emit(new BuffTickedEvent { UnitId = u.Id, BuffId = b.Def.Id, Remaining = b.Remaining });
            }
        }

        /// <summary>Tick-unit buffs: one step (and one damage tick) per BuffTickInterval of timeline time (§8.3).</summary>
        private void TickTimedBuffs(int ticks)
        {
            foreach (var u in S.Units.ToArray())
            {
                if (!u.Alive) continue;
                // Snapshot for the same reason as TickTurnBuffs: a chain/stack-cap eviction triggered from
                // within this loop can remove or add sibling buffs, which index-based iteration can't survive.
                foreach (var b in u.Buffs.ToArray())
                {
                    if (!u.Buffs.Contains(b)) continue;
                    if (b.Def.Unit != DurationUnit.Ticks) continue;
                    b.TickAccumulator += ticks;
                    while (b.TickAccumulator >= BattleConfig.BuffTickInterval && u.Alive && u.Buffs.Contains(b))
                    {
                        b.TickAccumulator -= BattleConfig.BuffTickInterval;
                        if (b.Def.DamagePerTick > 0)
                            Damage(u, b.Def.DamagePerTick, null, HitKind.Normal, DamageType.Physical, b.Def.Name);
                        if (b.Def.HealPerTick > 0 && u.Alive && u.Hp < u.MaxHp) Heal(u, b.Def.HealPerTick, null);
                        if (!u.Alive) break;
                        b.Remaining--;
                        if (b.Remaining <= 0) { Expire(u, b); break; }
                        Emit(new BuffTickedEvent { UnitId = u.Id, BuffId = b.Def.Id, Remaining = b.Remaining });
                    }
                    if (!u.Alive) break;
                }
                if (!u.Alive && CheckEnd()) return;
            }
        }
    }
}
