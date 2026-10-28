using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    internal sealed partial class BattleRunner
    {
        private static readonly Coord[] DefaultHeroStarts =
        {
            new Coord(1, 6), new Coord(4, 6), new Coord(2, 7), new Coord(3, 7),
            new Coord(0, 7), new Coord(5, 7),
        };

        private HashSet<string> excludedGuests = new HashSet<string>();
        private int unlockedSlotsForGuests = 2;
        private bool permadeath;

        public void Setup(BattleSetup setup)
        {
            excludedGuests = setup.ExcludedGuests ?? new HashSet<string>();
            permadeath = setup.Permadeath;
            unlockedSlotsForGuests = setup.UnlockedSlots;
            carriedOrbs = Math.Max(0, setup.StartOrbs);
            int i = 0;
            foreach (var spec in setup.Party)
            {
                Coord? start = spec.Start ?? DefaultHeroStarts[Math.Min(i, DefaultHeroStarts.Length - 1)];
                // FreeForSpawn, not just UnitAt: a stage's Blocks (or an out-of-bounds Start) would otherwise
                // drop a hero onto a solid/off-board tile it can never leave (GG-T70). A taken start falls back to a
                // free tile in the party's half, then anywhere; a board with no free tile at all (only possible on a
                // stage walled with blocks) seats the hero out of the battle rather than stacking it on someone (GG-T97).
                if (!S.FreeForSpawn(start.Value)) start = RandomFreeTile(1, 1, 4, BattleConfig.Rows - 1) ?? RandomFreeTile(1, 1, 0, BattleConfig.Rows - 1);
                SpawnHero(spec, start, setup.UnlockedSlots);
                i++;
            }
            if (pendingBankBonus > 0) S.Bank = new ManaBank(ManaBank.DefaultCapacity + System.Math.Min(BattleConfig.MaxBankCapacityBonus, pendingBankBonus));
            ApplyBattleStartOrbs();
            var prot = C.TryBuff(BattleConfig.ShieldBuffId);
            if (prot != null)
                foreach (var id in startProtected) { var h = S.UnitById(id); if (h != null) ApplyBuff(h, prot, prot.Duration); }
            SpawnWave(0);
        }

        /// <summary>Guest allies of a wave join the hero team on their tile (or the nearest free one).</summary>
        private void SpawnGuests(WaveDef wave)
        {
            if (wave.Guests == null) return;
            foreach (var g in wave.Guests)
            {
                var def = C.TryClass(g.Class);
                if (def == null || def.Team != Team.Hero || excludedGuests.Contains(def.Id)) continue;
                // Not just the living: a hero of this class who's merely down (no permadeath — still in
                // S.Units, not removed) already represents this class too, so a later wave reusing the
                // same guest class must not spawn a second, simultaneous instance of them.
                bool already = false;
                foreach (var u in S.Units) if (u.IsHero && u.Def.Id == def.Id) already = true;
                if (already) continue;
                var at = new Coord(g.X, g.Y);
                if (!S.FreeForSpawn(at)) { var free = RandomFreeTile(1, 1, 3, BattleConfig.Rows - 1) ?? RandomFreeTile(1, 1, 0, BattleConfig.Rows - 1); if (free == null) continue; at = free.Value; }
                var spec = new HeroSpec { ClassId = def.Id, Level = Math.Max(g.Level, S.RecommendedLevel), Bar = new List<string>(def.DefaultLoadout) };
                var unit = SpawnHero(spec, at, unlockedSlotsForGuests);
                if (!unit.OnBoard) continue;
                unit.IsGuest = true;
                Emit(new GuestJoinedEvent { UnitId = unit.Id, At = at });
                Announce($"{unit.Name} joins the fight!", unit.Id);
            }
        }

        /// <param name="at">Null = no free tile anywhere: the hero is built (so its gear still round-trips) but sits out off the board.</param>
        private Unit SpawnHero(HeroSpec spec, Coord? at, int unlockedSlots)
        {
            var def = C.Class(spec.ClassId);
            var u = new Unit
            {
                Id = S.NextUnitId++,
                Def = def,
                Team = Team.Hero,
                Level = Math.Max(1, spec.Level),
                Pos = at ?? new Coord(0, 0),
                OnBoard = at.HasValue,
                UnlockedSlots = Math.Max(0, Math.Min(MaxSlots, unlockedSlots)),
            };
            u.LevelStats = Formulas.StatsAtLevel(def, u.Level);
            u.WeaponMastery = spec.WeaponMastery;
            var masteryById = new Dictionary<string, float>();
            if (spec.WeaponId != null) masteryById[spec.WeaponId] = spec.WeaponMastery;
            foreach (var kv in spec.Armor) if (kv.Value?.ItemId != null) masteryById[kv.Value.ItemId] = kv.Value.Mastery;
            foreach (var itemId in spec.EquippedItemIds)
            {
                var item = C.TryItem(itemId);
                if (item == null) continue;
                float mastery = masteryById.TryGetValue(itemId, out var m) ? m : 1f;
                u.GearStats = u.GearStats + item.Stats * mastery;
                pendingStartOrbs += Math.Max(0, item.BattleStartManaOrbs);
                pendingBankBonus += Math.Max(0, item.BankCapacityBonus);
                u.PincerDamageBonus += item.PincerDamageBonus;
                u.RegenPerTurn += item.RegenPerTurn;
                if (item.StartsProtected && !startProtected.Contains(u.Id)) startProtected.Add(u.Id);
                foreach (var kv in item.Resistances)
                {
                    // TryGetValue's own false (no entry yet) means "no resistance from gear so far" (identity 1f);
                    // a present entry is used as-is even when it's 0f (full immunity from an earlier piece), so a
                    // second piece's resistance to the same type can't silently erase it.
                    float cur = u.GearResist.TryGetValue(kv.Key, out var existing) ? existing : 1f;
                    u.GearResist[kv.Key] = cur * kv.Value;
                }
            }
            var weapon = C.TryItem(spec.WeaponId);
            if (weapon != null)
            {
                u.WeaponId = weapon.Id;
                u.WeaponPower = weapon.BaseDamage * spec.WeaponMastery;
                u.WeaponDurability = weapon.Durability > 0 ? Math.Max(0, spec.WeaponDurability > 0 ? spec.WeaponDurability : weapon.Durability) : 0;
                if (!spec.EquippedItemIds.Contains(weapon.Id))
                {
                    u.GearStats = u.GearStats + weapon.Stats * spec.WeaponMastery;
                }
            }
            foreach (var slot in BattleEngine.ArmorSlots)
            {
                if (!spec.Armor.TryGetValue(slot, out var piece) || piece?.ItemId == null) continue;
                var armor = C.TryItem(piece.ItemId);
                if (armor == null || armor.Durability <= 0) continue;
                u.Gear[slot] = new EquippedPiece
                {
                    ItemId = armor.Id,
                    Durability = piece.Durability > 0 ? piece.Durability : armor.Durability,
                    Mastery = piece.Mastery,
                    Enchant = piece.Enchant,
                };
            }
            ResolveSetBonuses(u);
            foreach (var b in u.SetBonuses)
            {
                pendingStartOrbs += Math.Max(0, b.BattleStartManaOrbs);
                if (b.StartsProtected && !startProtected.Contains(u.Id)) startProtected.Add(u.Id);
            }
            u.MaxHp = Formulas.MaxHp(u.Effective, u.Level);
            u.Hp = spec.Hp > 0 ? Math.Min(spec.Hp, u.MaxHp) : u.MaxHp;

            for (int s = 0; s < MaxSlots; s++)
            {
                string entry = s < spec.Bar.Count ? spec.Bar[s] : null;
                u.Bar.Add(MakeSlot(entry, spec));
            }
            if (!AddUnit(u)) { u.OnBoard = false; S.Units.Add(u); }
            return u;
        }

        private AbilitySlot MakeSlot(string entry, HeroSpec spec)
        {
            var slot = new AbilitySlot();
            if (string.IsNullOrEmpty(entry)) return slot;
            if (entry.StartsWith("weapon:", StringComparison.Ordinal))
            {
                var w = C.TryItem(entry.Substring(7));
                spec.ItemCounts.TryGetValue(entry.Substring(7), out var have);
                if (w != null && w.Slot == EquipmentSlot.Weapon && have > 0) { slot.WeaponId = w.Id; slot.WeaponDurability = w.Durability; spec.ItemCounts[w.Id] = have - 1; }
                return slot;
            }
            if (entry.StartsWith("item:", StringComparison.Ordinal))
            {
                // "item:<id>" draws charges from the owned count; "item:<id>:<n>" is a preset stack.
                var parts = entry.Split(':');
                var item = C.TryItem(parts[1]);
                if (item == null) return slot;
                slot.ItemId = item.Id;
                if (parts.Length >= 3 && int.TryParse(parts[2], out var preset))
                {
                    slot.MaxCharges = slot.Charges = preset;
                }
                else
                {
                    spec.ItemCounts.TryGetValue(item.Id, out var owned);
                    slot.MaxCharges = item.MaxStack;
                    slot.Charges = Math.Min(owned, item.MaxStack);
                    slot.FromInventory = true;
                    // Two slots of the same item split the owned stack rather than duplicating it.
                    spec.ItemCounts[item.Id] = Math.Max(0, owned - slot.Charges);
                }
                return slot;
            }
            if (C.TryAbility(entry) != null) slot.AbilityId = entry;
            return slot;
        }

        /// <summary>Armor set bonuses (GG-T182): re-reads which set tiers the hero's intact armor switches on and
        /// folds the difference into its gear numbers — every tier newly on is added, every tier no longer on is
        /// taken back out (a piece broke). Battle-start orbs/Protection are read once at spawn, not here.
        /// Returns the tiers that just switched off.</summary>
        private List<SetBonusDef> ResolveSetBonuses(Unit u)
        {
            var worn = new List<string>();
            foreach (var piece in u.Gear.Values) if (piece.Durability > 0) worn.Add(piece.ItemId);
            var now = ArmorSets.ActiveBonuses(C, worn);
            var lost = new List<SetBonusDef>();
            foreach (var b in u.SetBonuses)
            {
                if (now.Contains(b)) continue;
                lost.Add(b);
                u.GearStats = u.GearStats + b.Stats * -1f;
                u.PincerDamageBonus -= b.PincerDamageBonus;
                u.RegenPerTurn -= b.RegenPerTurn;
                foreach (var kv in b.Resistances)
                {
                    if (!u.GearResist.TryGetValue(kv.Key, out var cur)) continue;
                    u.GearResist[kv.Key] = kv.Value != 0f ? cur / kv.Value : 1f; // same rule as a broken armor piece
                }
            }
            foreach (var b in now)
            {
                if (u.SetBonuses.Contains(b)) continue;
                u.GearStats = u.GearStats + b.Stats;
                u.PincerDamageBonus += b.PincerDamageBonus;
                u.RegenPerTurn += b.RegenPerTurn;
                foreach (var kv in b.Resistances)
                {
                    float cur = u.GearResist.TryGetValue(kv.Key, out var existing) ? existing : 1f;
                    u.GearResist[kv.Key] = cur * kv.Value;
                }
            }
            u.SetBonuses = now;
            return lost;
        }

        private int pendingStartOrbs;
        private int carriedOrbs;
        private int pendingBankBonus;
        private readonly List<int> startProtected = new List<int>();

        /// <summary>Gear with BattleStartManaOrbs (Mage/Wizard Robe) seeds orbs, capped by the bank (§24.8).</summary>
        private void ApplyBattleStartOrbs()
        {
            for (int i = 0; i < pendingStartOrbs; i++) MintOrb(-1, "battle start");
            pendingStartOrbs = 0;
            for (int i = 0; i < carriedOrbs; i++) MintOrb(-1, "carried");
            carriedOrbs = 0;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Waves
        // ─────────────────────────────────────────────────────────────────────────────

        public void SpawnWave(int index)
        {
            var wave = S.Stage.Waves[index];
            var started = new WaveStartedEvent { Wave = index, TotalWaves = S.Stage.Waves.Count };
            SpawnGuests(wave);
            foreach (var sp in wave.Enemies)
            {
                if (sp.DelayTicks > 0)
                {
                    S.PendingSpawns.Add(new PendingSpawn { Wave = index, Spawn = sp, TriggerClock = S.Clock + sp.DelayTicks });
                    continue;
                }
                var def = C.TryClass(sp.Class);
                if (def == null) continue;
                int level = S.Stage.IsTest ? sp.Level : Math.Max(sp.Level, S.RecommendedLevel) + S.NewGamePlus * BattleConfig.NgPlusLevelStep;
                // No room anywhere for this footprint: a wave's opening spawn is skipped (the board was just cleared,
                // so this only happens on a stage packed with blocks) — never stacked on another actor (GG-T97).
                var u = SpawnEnemy(def, level, sp.X, sp.Y);
                if (u == null) continue;
                started.Spawned.Add(u.Id);
                if (def.SnakeSegments > 0) GrowSnake(u, def.SnakeSegments, started.Spawned);
            }
            foreach (var id in started.Spawned) if (S.UnitById(id) is Unit su) started.SpawnedAt.Add(UnitFootprint.Of(su));
            Emit(started);
        }

        /// <summary>Resolves any delayed reinforcements (§14.8) for the current wave whose trigger tick has arrived.</summary>
        private void ResolvePendingSpawns()
        {
            if (S.PendingSpawns.Count == 0) return;
            List<int> spawned = null;
            string speakerClassId = null, line = null;
            foreach (var pending in S.PendingSpawns)
            {
                if (pending.Consumed || pending.Wave != S.Wave || S.Clock < pending.TriggerClock) continue;
                var def = C.TryClass(pending.Spawn.Class);
                if (def == null) { pending.Consumed = true; continue; }
                int level = S.Stage.IsTest ? pending.Spawn.Level : Math.Max(pending.Spawn.Level, S.RecommendedLevel) + S.NewGamePlus * BattleConfig.NgPlusLevelStep;
                var u = SpawnEnemy(def, level, pending.Spawn.X, pending.Spawn.Y);
                if (u == null)
                {
                    // A full board (GG-T97): the reinforcement waits and retries every tick until a tile frees up,
                    // never landing on an occupied one. If nothing else on the enemy side is left to make room (or
                    // to hold the wave open), it gives up so the wave can still end.
                    bool othersHoldWave = false;
                    foreach (var e in S.Units) if (e.Alive && e.Team == Team.Enemy && !e.IsCorpse) othersHoldWave = true;
                    if (!othersHoldWave) pending.Consumed = true;
                    continue;
                }
                pending.Consumed = true;
                spawned ??= new List<int>();
                spawned.Add(u.Id);
                if (def.SnakeSegments > 0) GrowSnake(u, def.SnakeSegments, spawned);
                if (speakerClassId == null)
                {
                    var wave = S.Stage.Waves[pending.Wave];
                    if (!string.IsNullOrEmpty(wave.SurpriseLine)) { speakerClassId = wave.SurpriseSpeaker; line = wave.SurpriseLine; }
                }
            }
            if (spawned != null)
            {
                var arrived = new ReinforcementsArrivedEvent { Wave = S.Wave, Spawned = spawned, SpeakerClassId = speakerClassId, Line = line };
                foreach (var id in spawned) if (S.UnitById(id) is Unit su) arrived.SpawnedAt.Add(UnitFootprint.Of(su));
                Emit(arrived);
            }
            S.PendingSpawns.RemoveAll(p => p.Consumed);
        }

        internal Unit SpawnEnemy(ClassDef def, int level, int x, int y)
        {
            int w = Math.Max(1, def.Footprint?.W ?? 1), h = Math.Max(1, def.Footprint?.H ?? 1);
            Coord? at = null;
            if (x >= 0 && y >= 0 && FootprintFree(new Coord(x, y), w, h)) at = new Coord(x, y);
            if (at == null) at = RandomFreeTile(w, h, 0, 4) ?? RandomFreeTile(w, h, 0, BattleConfig.Rows - 1);
            if (at == null) return null;
            var u = new Unit
            {
                Id = S.NextUnitId++,
                Def = def,
                Team = Team.Enemy,
                Level = Math.Max(1, level),
                Pos = at.Value,
                W = w,
                H = h,
            };
            u.LevelStats = Formulas.StatsAtLevel(def, u.Level);
            u.MaxHp = Formulas.MaxHp(u.Effective, u.Level);
            u.Hp = u.MaxHp;
            if (def.ArmorPlate > 0) u.PlateHp = u.PlateMax = (int)Math.Round(def.ArmorPlate * (1f + BattleConfig.PlatePerLevel * (u.Level - 1)));
            if (!AddUnit(u)) return null;
            if (u.PlateMax > 0) FaceTarget(u, false);
            S.Timeline.Add(new TimelineEntry
            {
                Id = S.NextEntryId++,
                Kind = TimelineKind.Turn,
                OwnerId = u.Id,
                Ip = S.Rng.Range(0, BattleConfig.SpawnStaggerIp + 1),
            });
            if (!def.HasTag("Remains")) S.Results.Seen.Add(def.Id);
            return u;
        }

        /// <summary>Segmented snake (§14.2): body segments trail the head on free adjacent tiles; no timeline icon.</summary>
        private void GrowSnake(Unit head, int segments, List<int> spawned)
        {
            head.SnakeHeadId = head.Id;
            head.SnakeIndex = 0;
            var prev = head.Pos;
            for (int i = 1; i <= segments; i++)
            {
                Coord? next = null;
                foreach (var d in new[] { Coord.Up, Coord.Left, Coord.Right, Coord.Down })
                {
                    var c = prev + d;
                    if (S.FreeForSpawn(c)) { next = c; break; }
                }
                if (next == null) break;
                var seg = new Unit
                {
                    Id = S.NextUnitId++,
                    Def = head.Def,
                    Team = Team.Enemy,
                    Level = head.Level,
                    Pos = next.Value,
                    SnakeHeadId = head.Id,
                    SnakeIndex = i,
                    LevelStats = head.LevelStats.Clone(),
                };
                seg.MaxHp = seg.Hp = Math.Max(1, head.MaxHp / 2);
                if (!AddUnit(seg)) break;
                spawned.Add(seg.Id);
                prev = next.Value;
            }
        }

        private bool FootprintFree(Coord anchor, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    var c = new Coord(anchor.X + dx, anchor.Y + dy);
                    if (!S.FreeForSpawn(c) || S.TrapAt(c) != null) return false;
                }
            return true;
        }

        private Coord? RandomFreeTile(int w, int h, int minRow, int maxRow)
        {
            var free = new List<Coord>();
            for (int y = minRow; y <= maxRow; y++)
                for (int x = 0; x < BattleConfig.Cols; x++)
                    if (FootprintFree(new Coord(x, y), w, h)) free.Add(new Coord(x, y));
            if (free.Count == 0) return null;
            return free[S.Rng.Range(0, free.Count)];
        }
    }
}
