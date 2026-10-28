using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    internal sealed partial class BattleRunner
    {
        // ─────────────────────────────────────────────────────────────────────────────
        // Movement (§1.1) — one cardinal tile at a time; displaced occupant takes the tile just left.
        // ─────────────────────────────────────────────────────────────────────────────

        private bool DoMove(MoveCommand m)
        {
            var hero = S.UnitById(m.HeroId);
            if (hero == null || !hero.Alive || !hero.IsHero) { Emit(new AbilityRefusedEvent { CasterId = m.HeroId, Reason = "No such hero" }); return false; }
            AutoEquipBackupWeapon(hero);
            if (hero.IsImmobile) { Emit(new AbilityRefusedEvent { CasterId = hero.Id, Reason = "Can't move" }); return false; }

            var before = SnapshotPositions();
            int trapsBefore = S.Traps.Count, pickupsBefore = S.Pickups.Count;
            int hpBefore = hero.Hp;
            Emit(new ActionBeganEvent { ActorId = hero.Id, Label = "Move" });
            var taken = new List<Coord>();
            WalkPath(hero, m.Path, taken);
            bool nothingHappened = taken.Count == 0
                || (hero.Alive && hero.Hp == hpBefore && S.Traps.Count == trapsBefore && S.Pickups.Count == pickupsBefore && SamePositions(before)
                    && !WouldFormPincer(hero));
            if (nothingHappened)
            {
                // A drag that changes nothing is a cancel, not a turn (no clock cost) — unless walking out and
                // back onto the start tile re-forms a pincer (a deliberate, skillful tactic): that always counts.
                Emit(new AbilityRefusedEvent { CasterId = hero.Id, Reason = "No move" });
                return false;
            }
            if (BreaksOnMove(hero)) RemoveBuffsWhere(hero, b => b.Def.BreaksOnMove, "moved");

            S.PendingTimeBankSeconds = Math.Max(0f, Math.Min(BattleConfig.DragTimerSeconds, m.TimeLeftSeconds));
            if (CheckEnd()) return true; // a trap can kill the last hero
            ResolvePincers(Team.Hero, hero);
            CheckEnd();
            return true;
        }

        /// <summary>Pure geometry check (no mutation): would resolving pincers right now actually strike something?</summary>
        private bool WouldFormPincer(Unit hero) =>
            PincerDetector.Detect(S, Team.Hero, hero).Exists(p => p.A.Alive && p.B.Alive && p.Opponents.Exists(o => o.Alive));

        private static bool BreaksOnMove(Unit u)
        {
            foreach (var b in u.Buffs) if (b.Def.BreaksOnMove) return true;
            return false;
        }

        private Dictionary<int, Coord> SnapshotPositions()
        {
            var d = new Dictionary<int, Coord>();
            foreach (var u in S.Units) if (u.Alive) d[u.Id] = u.Pos;
            return d;
        }

        private bool SamePositions(Dictionary<int, Coord> before)
        {
            foreach (var u in S.Units)
                if (u.Alive && (!before.TryGetValue(u.Id, out var p) || p != u.Pos)) return false;
            return true;
        }

        /// <summary>Walks the hero step by step; returns false if a step was refused (the rest of the path is dropped).</summary>
        public bool WalkPath(Unit hero, List<Coord> path, List<Coord> taken)
        {
            foreach (var next in path)
            {
                if (!hero.Alive) return false;
                bool diagonal = hero.Pos.IsDiagonalNeighbor(next);
                if (!hero.Pos.IsCardinalNeighbor(next) && !(diagonal && BattleConfig.DiagonalSlipEnabled))
                {
                    Emit(new StepBlockedEvent { UnitId = hero.Id, At = next, Reason = "not adjacent" });
                    return false;
                }
                if (!BattleState.InBounds(next))
                {
                    Emit(new StepBlockedEvent { UnitId = hero.Id, At = next, Reason = "edge" });
                    return false;
                }
                if (S.IsBlock(next))
                {
                    Emit(new StepBlockedEvent { UnitId = hero.Id, At = next, Reason = "block" });
                    return false;
                }
                // A diagonal slip glides into open space only — never a swap, never onto anything at all.
                if (diagonal && (S.UnitAt(next) != null || S.MovableAt(next) != null))
                {
                    Emit(new StepBlockedEvent { UnitId = hero.Id, At = next, Reason = "occupied" });
                    return false;
                }
                var occ = S.UnitAt(next);
                if (occ != null && (occ.IsMultiTile || occ.IsSnakeMember || occ.IsRemains))
                {
                    Emit(new StepBlockedEvent { UnitId = hero.Id, At = next, Reason = "immovable" });
                    return false;
                }
                var from = hero.Pos;
                var pushed = S.MovableAt(next);
                // The occupant (or movable) of the entered tile swaps into the tile just left — the only tile this
                // step frees, so the pair can never land on a third actor (§1.1, GG-T97).
                if (!Relocate(hero, next, o => o == occ, movablesYield: true))
                {
                    Emit(new StepBlockedEvent { UnitId = hero.Id, At = next, Reason = "occupied" });
                    return false;
                }
                Emit(new SteppedEvent { UnitId = hero.Id, From = from, To = next });
                taken.Add(next);
                if (pushed != null) ShoveMovable(pushed, from, hero);
                if (occ != null && Relocate(occ, from))
                {
                    Emit(new DisplacedEvent { UnitId = occ.Id, From = next, To = from, ByUnitId = hero.Id });
                    if (!PreviewOnly && BreaksOnMove(occ)) RemoveBuffsWhere(occ, b => b.Def.BreaksOnMove, "moved");
                    if (!PreviewOnly) TriggerTrap(occ);
                }
                if (!PreviewOnly) TriggerTrap(hero);
                if (!PreviewOnly && !hero.Alive) return false;
            }
            return true;
        }

        private void TriggerTrap(Unit u)
        {
            if (!u.Alive || u.Team != Team.Hero) return;
            CollectPickup(u);
            if (!u.Alive) return;
            var trap = S.TrapAt(u.Pos);
            if (trap == null) return;
            S.Traps.Remove(trap);
            var def = C.Traps.TryGetValue(trap.TrapId, out var t) ? t : null;
            Emit(new TrapTriggeredEvent { UnitId = u.Id, At = u.Pos, TrapId = trap.TrapId });
            if (def == null) return;
            if (def.Damage > 0) Damage(u, def.Damage, null, HitKind.Normal, def.DamageType, def.Name);
            var buff = C.TryBuff(def.ApplyBuff);
            if (buff != null && u.Alive) ApplyBuff(u, buff, buff.Duration);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Pincers (§1.2, §13.1.1)
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Board-wide pincer scan for <paramref name="team"/>; resolves every pair in chain order.</summary>
        private void ResolvePincers(Team team, Unit dropped, Unit mustInclude = null)
        {
            var pincers = PincerDetector.Detect(S, team, dropped);
            int chain = 0;
            foreach (var p in pincers)
            {
                if (S.IsOver) return;
                if (mustInclude != null && p.A != mustInclude && p.B != mustInclude) continue;
                if (!p.A.Alive || !p.B.Alive) continue;
                bool anyAlive = false;
                foreach (var o in p.Opponents) if (o.Alive) anyAlive = true;
                if (!anyAlive)
                {
                    if (chain > 0) Emit(new RespiteEvent { Attacker1 = p.A.Id, Attacker2 = p.B.Id });
                    continue;
                }
                ResolvePincer(p, team, chain++);
            }
        }

        /// <summary>
        /// Chain depth of the pincer currently resolving (0 outside one), read by <see cref="AwardLoot"/> so a kill
        /// mid-chain earns bonus XP the same way <see cref="BattleConfig.ChargeChainBonus"/> already rewards mana
        /// (owner direction 2026-10-11: deeper combos should pay out more of both, not just mana).
        /// </summary>
        private int chainBonusDepth;

        private void ResolvePincer(Pincer p, Team team, int chainIndex)
        {
            chainBonusDepth = team == Team.Hero ? chainIndex : 0;
            var supporters = new List<Unit>();
            foreach (var x in p.SupportersA) if (!supporters.Contains(x)) supporters.Add(x);
            foreach (var x in p.SupportersB) if (!supporters.Contains(x)) supporters.Add(x);
            var formed = new PincerFormedEvent { Team = team, Attacker1 = p.A.Id, Attacker2 = p.B.Id, ChainIndex = chainIndex };
            // Snapshot the geometry now: by the time the view plays this back the state is the end of the whole
            // command, and any member may have moved since (enemy turns, knockback, a later chain link — GG-T98).
            formed.Attacker1At = UnitFootprint.Of(p.A); formed.Attacker2At = UnitFootprint.Of(p.B);
            foreach (var o in p.Opponents) { formed.Opponents.Add(o.Id); formed.OpponentsAt.Add(UnitFootprint.Of(o)); }
            foreach (var su in supporters) { formed.Supporters.Add(su.Id); formed.SupportersAt.Add(UnitFootprint.Of(su)); }
            Emit(formed);

            // Power Amplifier: a hero attacker standing on it strikes amplified and uses it up (§1.2.6).
            Unit amplified = null;
            if (team == Team.Hero && S.Amplifier.HasValue)
                foreach (var attacker in new[] { p.A, p.B })
                    if (amplified == null && attacker.Alive && TouchesTile(attacker, S.Amplifier.Value)) amplified = attacker;
            if (amplified != null)
            {
                Emit(new AmplifierUsedEvent { At = S.Amplifier.Value, HeroId = amplified.Id });
                Announce("Amplified!", amplified.Id);
                S.Amplifier = null;
            }

            var hpBefore = new Dictionary<Unit, int>();
            foreach (var o in p.Opponents) hpBefore[o] = o.Hp;

            foreach (var attacker in new[] { p.A, p.B })
            {
                if (!attacker.Alive) continue;
                float mult = 1f + attacker.PincerDamageBonus;
                if (attacker == amplified) mult *= BattleConfig.AmplifierDamageMultiplier;
                bool shatters = attacker.IsHero && attacker.WeaponId != null && attacker.WeaponDurability == 1;
                if (shatters) mult *= BattleConfig.ShatterTargetMultiplier;
                bool twice = HasPassive(attacker, "double_attack");
                foreach (var target in p.Opponents)
                {
                    if (!target.Alive || !attacker.Alive) continue;
                    Strike(attacker, target, mult, false);
                    if (twice && target.Alive && attacker.Alive) Strike(attacker, target, mult * 0.5f, false);
                    if (target.Alive) TryWeaponPincerBuff(attacker, target);
                }
                if (attacker.IsHero && attacker.WeaponId != null && attacker.WeaponDurability > 0) WearWeapon(attacker);
            }

            foreach (var sup in supporters)
                foreach (var target in p.Opponents)
                    if (sup.Alive && target.Alive) Strike(sup, target, BattleConfig.SupporterDamageFraction, true);

            if (team == Team.Hero)
            {
                // Mana charge (§3.1.9): the share of enemy HP this pincer took, divided by how many heroes it took to do
                // it, multiplied up the deeper it sits in a chain. Lean, chained pincers farm mana; dog-piles don't.
                float share = 0f;
                foreach (var o in p.Opponents)
                    if (o.MaxHp > 0 && !o.IsCorpse) share += Math.Min(1f, Math.Max(0, hpBefore[o] - o.Hp) / (float)o.MaxHp);
                int participants = 2 + supporters.Count;
                float divisor = 1f + BattleConfig.ChargeParticipantPenalty * Math.Max(0, participants - 2);
                float chainMult = 1f + BattleConfig.ChargeChainBonus * chainIndex;
                if (share > 0f)
                    AddCharge(Math.Max(BattleConfig.MinPincerCharge, (int)Math.Round(BattleConfig.PincerChargePerHp * share * chainMult / divisor)), p.A.Id, "pincer", participants, chainIndex + 1);
                if (amplified != null && amplified.Alive) AddCharge(BattleConfig.AmplifierCharge, amplified.Id, "amplifier");
            }
            chainBonusDepth = 0;
        }

        /// <summary>The unit has passive <paramref name="id"/>: from its class kit, or (GG-T179) from an enchant on an
        /// intact armor piece it wears (a broken piece leaves <see cref="Unit.Gear"/>, taking its enchant with it).</summary>
        private bool HasPassive(Unit u, string id)
        {
            var a = C.TryAbility(id);
            if (a == null || a.Kind != AbilityKind.Passive) return false;
            if (u.Def.Abilities != null && u.Def.Abilities.Contains(id)) return true;
            foreach (var piece in u.Gear.Values)
                if (piece.Durability > 0 && piece.Enchant != null && C.TryArmorEnchant(piece.Enchant)?.Passive == id) return true;
            return false;
        }

        /// <summary>Armor enchant passives that ward off buffs (GG-T179): passive id → the buff ids it makes the wearer immune to.</summary>
        private static readonly Dictionary<string, string[]> WardingPassives = new Dictionary<string, string[]>
        {
            { "poison_ward", new[] { "poisoned" } },
            { "steadfast", new[] { "frozen", "sleep" } },
        };

        /// <summary>A passive the unit has makes it immune to <paramref name="buffId"/>.</summary>
        private bool WardedAgainst(Unit u, string buffId)
        {
            foreach (var kv in WardingPassives)
                if (Array.IndexOf(kv.Value, buffId) >= 0 && HasPassive(u, kv.Key)) return true;
            return false;
        }

        private bool reflecting;

        /// <summary>Thorns (GG-T179): a hero wearing it reflects a share of a landed enemy hit back at the attacker.</summary>
        private void MaybeThorns(Unit target, Unit source, int amount)
        {
            if (reflecting || amount <= 0 || source == null || !source.Alive || !target.IsHero || source.Team != Team.Enemy || !HasPassive(target, "thorns")) return;
            reflecting = true;
            Damage(source, Math.Max(1, (int)Math.Round(amount * BattleConfig.ThornsFraction)), target, HitKind.Normal, DamageType.Physical, "Thorns", canPush: false);
            reflecting = false;
        }

        /// <summary>Mana Siphon (GG-T179): every hit the wearer lands adds a little charge to the mana meter.</summary>
        private void MaybeSiphon(Unit attacker, int dealt)
        {
            if (dealt > 0 && attacker.IsHero && HasPassive(attacker, "mana_siphon")) AddCharge(BattleConfig.ManaSiphonCharge, attacker.Id, "siphon");
        }

        /// <summary>Named weapon passive (§24.6.1, GG-T47): a landed pincer strike (not a supporter or counter hit)
        /// may apply the wielder's weapon's signature buff to the target, e.g. Flamebrand's Burn.</summary>
        private void TryWeaponPincerBuff(Unit attacker, Unit target)
        {
            if (!attacker.IsHero || attacker.WeaponId == null) return;
            var weapon = C.TryItem(attacker.WeaponId);
            if (weapon?.PincerApplyBuff == null || !S.Rng.Chance(weapon.PincerApplyBuffChance)) return;
            var buff = C.TryBuff(weapon.PincerApplyBuff);
            if (buff != null) ApplyBuff(target, buff, buff.Duration);
        }

        /// <summary>Named weapon passive (§24.6.1, GG-T47): heals the wielder a fraction of any damage a hero lands
        /// with it, e.g. Vampiric Edge.</summary>
        private void TryWeaponLifesteal(Unit attacker, int dealt)
        {
            if (dealt <= 0 || !attacker.IsHero || attacker.WeaponId == null) return;
            var weapon = C.TryItem(attacker.WeaponId);
            if (weapon == null || weapon.LifestealFraction <= 0f) return;
            Heal(attacker, Math.Max(1, (int)Math.Round(dealt * weapon.LifestealFraction)), attacker);
        }

        private bool countering;

        /// <summary>Counter Attack passive: a surviving, adjacent defender strikes back at half strength (no chains).</summary>
        private void MaybeCounter(Unit defender, Unit attacker)
        {
            if (countering || attacker == null || !defender.Alive || !attacker.Alive) return;
            if (defender.Team == attacker.Team || !defender.IsAdjacentTo(attacker) || !HasPassive(defender, "counter_attack")) return;
            countering = true;
            Announce("Counter!", defender.Id);
            Strike(defender, attacker, 0.5f, false);
            countering = false;
        }

        private void Strike(Unit attacker, Unit target, float mult, bool support)
        {
            Emit(new AttackEvent { AttackerId = attacker.Id, TargetId = target.Id, IsSupport = support });
            var roll = Formulas.Physical(attacker, target, S.Rng, mult * KitDamageMultiplier(attacker) * attacker.OutgoingDamageMultiplier);
            if (roll.Hit != HitKind.Miss && PlateAbsorbs(target, attacker, roll.Damage)) return;
            int before = target.Hp;
            Damage(target, roll.Damage, attacker, roll.Hit, DamageType.Physical);
            if (roll.Hit == HitKind.Critical && attacker.IsHero) AddCharge(BattleConfig.CritCharge, attacker.Id, "critical");
            if (roll.Hit != HitKind.Miss)
            {
                int dealt = before - target.Hp;
                AfterLandedHit(attacker, target, dealt);
                TryWeaponLifesteal(attacker, dealt);
                MaybeSiphon(attacker, dealt);
                MaybeCounter(target, attacker);
            }
        }

        // ── Armor plates (§13.5) ──

        /// <summary>The side of <paramref name="u"/> that <paramref name="from"/> stands on (dominant axis; ties go to the facing axis).</summary>
        public static Coord SideOf(Unit u, Unit from)
        {
            float cx = u.Pos.X + (u.W - 1) / 2f, cy = u.Pos.Y + (u.H - 1) / 2f;
            float fx = from.Pos.X + (from.W - 1) / 2f, fy = from.Pos.Y + (from.H - 1) / 2f;
            float dx = fx - cx, dy = fy - cy;
            if (Math.Abs(dx) > Math.Abs(dy) || (Math.Abs(dx) == Math.Abs(dy) && u.Facing.X != 0))
                return new Coord(Math.Sign(dx), 0);
            return new Coord(0, Math.Sign(dy));
        }

        /// <summary>A physical blow from the plated side dents the plate instead of the body; the plate breaks off at 0.</summary>
        private bool PlateAbsorbs(Unit target, Unit attacker, int amount)
        {
            if (!target.HasPlate || attacker == null || attacker.Team == target.Team || amount <= 0) return false;
            if (SideOf(target, attacker) != target.Facing) return false;
            target.PlateHp = Math.Max(0, target.PlateHp - amount);
            Emit(new PlateHitEvent { UnitId = target.Id, AttackerId = attacker.Id, Amount = amount, Left = target.PlateHp, Max = target.PlateMax });
            if (target.PlateHp == 0)
            {
                Emit(new PlateBrokenEvent { UnitId = target.Id, AttackerId = attacker.Id });
                Announce($"{target.Name}'s armor breaks off!", target.Id);
            }
            return true;
        }

        /// <summary>An armored unit turns its plate toward the hero it wants.</summary>
        private void FaceTarget(Unit u, bool emit = true)
        {
            if (u.PlateMax <= 0 || !u.Alive) return;
            var t = EnemyPlanner.PickTarget(S, u);
            if (t == null) return;
            var f = SideOf(u, t);
            if (f.X == 0 && f.Y == 0) return;
            if (f == u.Facing) return;
            u.Facing = f;
            if (emit) Emit(new FacingChangedEvent { UnitId = u.Id, Facing = f });
        }

        /// <summary>Is <paramref name="c"/> cardinally next to any tile of <paramref name="u"/>?</summary>
        private static bool TouchesTile(Unit u, Coord c)
        {
            foreach (var d in Coord.Cardinals) if (u.Occupies(c + d) && !u.Occupies(c)) return true;
            return false;
        }

        // ── Knockback (§6.3): shield bash and friends ──

        /// <summary>
        /// Shove <paramref name="t"/> up to <paramref name="tiles"/> straight away from the caster. Hitting another unit is
        /// a crash (both take damage); hitting stone or the board edge is a slam (heavy damage). Bosses and snakes don't budge.
        /// </summary>
        private void Knockback(Unit caster, Unit t, int tiles, int baseDamage)
        {
            if (!t.Alive || t.IsMultiTile || t.IsSnakeMember || t.IsRemains) return;
            var d = new Coord(t.Pos.X - caster.Pos.X, t.Pos.Y - caster.Pos.Y);
            var dir = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Coord(Math.Sign(d.X), 0) : new Coord(0, Math.Sign(d.Y));
            if (dir.X == 0 && dir.Y == 0) return;
            for (int i = 0; i < tiles && t.Alive && !S.IsOver; i++)
            {
                var next = t.Pos + dir;
                if (!S.Walkable(next))
                {
                    Announce("Slam!", t.Id);
                    Damage(t, BattleConfig.KnockbackSlamDamage + baseDamage, caster, HitKind.Normal, DamageType.Physical, "Slam!", canPush: false);
                    return;
                }
                var other = S.UnitAt(next);
                if (other != null)
                {
                    Announce("Crash!", t.Id);
                    int crash = BattleConfig.KnockbackCrashDamage + baseDamage / 2;
                    Damage(t, crash, caster, HitKind.Normal, DamageType.Physical, "Crash!", canPush: false);
                    if (other.Alive && other.Team != caster.Team) Damage(other, crash, caster, HitKind.Normal, DamageType.Physical, "Crash!", canPush: false);
                    return;
                }
                if (S.MovableAt(next) is MovableTile mv)
                {
                    var beyond = next + dir;
                    // A pickup beyond blocks the amplifier like a wall (two items never share a tile, §1.1.3, GG-T129).
                    if (!S.FreeForSpawn(beyond) || PickupBlocksMovable(beyond) || !MoveMovable(mv, beyond, t)) { Damage(t, BattleConfig.KnockbackCrashDamage, caster, HitKind.Normal, DamageType.Physical, "Crash!", canPush: false); return; }
                }
                var from = t.Pos;
                if (!Relocate(t, next)) return;
                Emit(new DisplacedEvent { UnitId = t.Id, From = from, To = next, ByUnitId = caster.Id });
                if (t.IsHero) TriggerTrap(t);
            }
        }

        // ── Enemy kits (§14.4): passives and on-hit effects shared by every enemy attack ──

        /// <summary>Berserk: below half HP an enemy hits 35% harder.</summary>
        private float KitDamageMultiplier(Unit attacker) =>
            attacker.IsHero ? 1f : BattleConfig.EnemyStrikeMultiplier * (skirmishing ? 0.8f : 1f) * (attacker.HpFraction < 0.5f && HasPassive(attacker, "berserk") ? 1.35f : 1f);

        /// <summary>After an enemy's attack lands: lifesteal heals half the damage; an on-hit buff may stick.</summary>
        private void AfterLandedHit(Unit attacker, Unit target, int dealt)
        {
            if (attacker.IsHero || !attacker.Alive) return;
            if (dealt > 0 && HasPassive(attacker, "lifesteal")) Heal(attacker, Math.Max(1, dealt / 2), attacker);
            var buff = C.TryBuff(attacker.Def.OnHitBuff);
            if (buff != null && target.Alive && S.Rng.Chance(attacker.Def.OnHitChance)) ApplyBuff(target, buff, buff.Duration);
        }

        /// <summary>Every equipped armor piece loses one durability per landing enemy hit; each breaks (and its
        /// bonuses go) independently at 0 (US-243, GG-T89 five-slot armor).</summary>
        private void WearArmor(Unit hero)
        {
            var slots = new List<EquipmentSlot>(hero.Gear.Keys);
            foreach (var slot in slots)
            {
                var piece = hero.Gear[slot];
                if (piece.Durability <= 0) continue;
                piece.Durability--;
                if (piece.Durability > 0) continue;
                var armor = C.TryItem(piece.ItemId);
                if (armor != null)
                {
                    hero.GearStats = hero.GearStats + armor.Stats * -piece.Mastery;
                    foreach (var kv in armor.Resistances)
                    {
                        if (!hero.GearResist.TryGetValue(kv.Key, out var cur)) continue;
                        // A piece granting 0 resistance (full immunity) can't be divided back out exactly —
                        // once it breaks, its own contribution simply reverts to neutral (owner direction:
                        // broken armor should leave 1x effectiveness, not a permanent 0x/immune stuck value).
                        hero.GearResist[kv.Key] = kv.Value != 0f ? cur / kv.Value : 1f;
                    }
                }
                Emit(new ArmorBrokenEvent { UnitId = hero.Id, ArmorId = piece.ItemId });
                Announce($"{armor?.Name ?? "Armor"} broken!", hero.Id);
                RecordBroken(S.Results.BrokenArmors, hero.Def.Id, piece.ItemId);
                hero.Gear.Remove(slot);
                // GG-T182: a broken piece no longer counts toward its set, so any tier it was holding up goes too.
                var set = C.TryArmorSetOf(piece.ItemId);
                if (set != null && ResolveSetBonuses(hero).Count > 0) Announce($"{set.Name} set bonus lost", hero.Id);
            }
        }

        /// <summary>One durability per pincer; at 0 the weapon shatters: the strike already dealt ×1.5, the wielder takes 15% MaxHP (§24.5).</summary>
        private void WearWeapon(Unit hero)
        {
            hero.WeaponDurability--;
            if (hero.WeaponDurability > 0)
            {
                Emit(new WeaponWornEvent { UnitId = hero.Id, WeaponId = hero.WeaponId, Durability = hero.WeaponDurability });
                return;
            }
            int self = Math.Max(1, (int)Math.Round(hero.MaxHp * BattleConfig.ShatterSelfDamageFraction));
            var weapon = C.TryItem(hero.WeaponId);
            Emit(new WeaponShatteredEvent { UnitId = hero.Id, WeaponId = hero.WeaponId, SelfDamage = self });
            if (weapon != null) hero.GearStats = hero.GearStats + weapon.Stats * -hero.WeaponMastery;
            RecordBroken(S.Results.BrokenWeapons, hero.Def.Id, hero.WeaponId);
            hero.WeaponId = null;
            hero.WeaponPower = 0f;
            hero.WeaponMastery = 1f;
            Damage(hero, self, null, HitKind.Normal, DamageType.Physical, "Shatter!");
        }

        /// <summary>Appends a shattered/broken item id to this hero's list, so <c>BattleFactory.Commit</c>
        /// can turn it into a Broken fragment even if a bar backup is auto-equipped afterward (GG-T41).</summary>
        private static void RecordBroken(Dictionary<string, List<string>> map, string heroId, string itemId)
        {
            if (!map.TryGetValue(heroId, out var list)) map[heroId] = list = new List<string>();
            list.Add(itemId);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Timeline reactions to hits (§2.3, §13.4)
        // ─────────────────────────────────────────────────────────────────────────────

        private void Pushback(Unit enemy, Unit attacker)
        {
            if (!pushedThisAction.Add(enemy.Id)) return; // one push per enemy per action — a chained pincer shouldn't stack it
            var entry = S.TurnEntryOf(enemy.Id);
            if (entry == null || !entry.InZone) return;
            float zoneWidth = BattleConfig.BarLength - BattleConfig.ZoneStart;
            float frac = (entry.Ip - BattleConfig.ZoneStart) / zoneWidth;
            float strMult = 1f + (attacker.Effective.Strength - 10f) / 40f;
            if (strMult < 0.5f) strMult = 0.5f;
            float push = BattleConfig.PushbackBaseU * (0.5f + frac) * strMult;
            if (push > BattleConfig.PushbackMaxU) push = BattleConfig.PushbackMaxU;
            float from = entry.U;
            entry.Ip = Math.Max(0, entry.Ip - (int)Math.Round(push * BattleConfig.BarLength));
            float agiMult = 1f + enemy.Effective.Agility / 50f;
            entry.StunTicks = Math.Max(1, (int)Math.Round(BattleConfig.BaseStunTicks / agiMult));
            Emit(new PushedBackEvent { UnitId = enemy.Id, FromU = from, ToU = entry.U, StunTicks = entry.StunTicks });
        }

        /// <summary>Cast-stagger interrupt: Clutch (hero only) → WIS shrug → delay; cancel once delay ≥ cast time.</summary>
        private void StaggerCasts(Unit caster, Unit attacker)
        {
            var cast = S.CastEntryOf(caster.Id);
            if (cast == null) return;
            var cs = caster.Effective;
            bool allowClutch = caster.IsHero;
            if (allowClutch)
            {
                float clutch = Math.Min(BattleConfig.ClutchMaxChance, cs.Luck * BattleConfig.ClutchChancePerLuck);
                if (S.Rng.Chance(clutch))
                {
                    Emit(new ClutchEvent { EntryId = cast.Id, CasterId = caster.Id });
                    Announce("Clutch!", caster.Id);
                    ResolveCast(cast);
                    return;
                }
            }
            float resist = Math.Min(BattleConfig.MaxResistChance, cs.Wisdom * BattleConfig.WisdomResistPerPoint);
            if (S.Rng.Chance(resist))
            {
                Emit(new CastStaggeredEvent { EntryId = cast.Id, CasterId = caster.Id, DelayTicks = 0, Resisted = true });
                return;
            }
            float delaySec = BattleConfig.BaseInterruptDelaySeconds
                * (1f + attacker.Effective.Strength / BattleConfig.InterruptStrengthScale)
                / (1f + cs.Wisdom / BattleConfig.InterruptWisdomDelayScale);
            int delay = Formulas.SecondsToTicks(delaySec);
            cast.AccumulatedDelayTicks += delay;
            cast.RemainingTicks += delay;
            Emit(new CastStaggeredEvent { EntryId = cast.Id, CasterId = caster.Id, DelayTicks = delay });
            if (cast.AccumulatedDelayTicks >= cast.TotalCastTicks)
            {
                S.Timeline.Remove(cast);
                Emit(new CastCancelledEvent { EntryId = cast.Id, CasterId = caster.Id, AbilityId = cast.AbilityId, Reason = "interrupted" });
                Announce("Interrupted!", caster.Id);
                if (cast.CasterTeam == Team.Enemy)
                {
                    AddCharge(BattleConfig.InterruptCharge, caster.Id, "interrupt");
                }
            }
        }
    }
}
