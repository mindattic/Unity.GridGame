using System.Collections.Generic;

namespace GridGame.Core.Battle
{
    /// <summary>What one target is likely to take from a command (GG-T252, POL-D1): ranges over several sampled rolls.</summary>
    public sealed class OutcomeTarget
    {
        public int UnitId;
        public int DamageMin, DamageMax, HealMin, HealMax;
        /// <summary>Share of samples in which the target dies during the action (0..1).</summary>
        public float KillChance;
        /// <summary>Buff/debuff id → share of samples in which the action applies it to this target.</summary>
        public Dictionary<string, float> StatusChance = new Dictionary<string, float>();
    }

    public sealed class OutcomePreview
    {
        public List<OutcomeTarget> Targets = new List<OutcomeTarget>();
        /// <summary>Orbs that would land in the bank during the action (min/max over samples).</summary>
        public int OrbsMin, OrbsMax;
        public int Samples;
        public bool Refused;

        public OutcomeTarget Of(int unitId)
        {
            foreach (var t in Targets) if (t.UnitId == unitId) return t;
            return null;
        }
    }

    /// <summary>
    /// The outcome preview behind the confirm card and the drag preview (GG-T252). Pure: every sample runs the command
    /// on its own <see cref="BattleState.Clone"/>, with its own rolls, so the real battle and its RNG are never touched.
    /// Only the acting side's resolution counts — the window closes when the next turn starts, and reopens for the
    /// moment the actor's own charged cast resolves.
    /// </summary>
    public static class OutcomePreviewer
    {
        public static OutcomePreview Preview(BattleState s, BattleCommand cmd, int samples = 6)
        {
            var result = new OutcomePreview { Samples = samples };
            int actor = BattleEngine.CommandHeroId(cmd);
            var perSample = new List<Dictionary<int, Sample>>(samples);
            var orbs = new List<int>(samples);
            for (int i = 0; i < samples; i++)
            {
                var copy = s.Clone();
                if (i > 0) copy.Rng = new Rng(unchecked((int)(s.Clock * 31 + i * 7919 + 17)));
                List<BattleEvent> events;
                try { events = BattleEngine.Apply(copy, cmd); }
                catch (TileConflictException) { result.Refused = true; return result; }
                var hits = new Dictionary<int, Sample>();
                int landed = 0;
                bool open = true;
                foreach (var e in events)
                {
                    switch (e)
                    {
                        case AbilityRefusedEvent rf when rf.CasterId == actor && hits.Count == 0: result.Refused = true; return result;
                        case TurnStartedEvent _: open = false; break;
                        case AbilityResolvedEvent ar: open = ar.CasterId == actor; break;
                    }
                    if (!open) continue;
                    switch (e)
                    {
                        case DamagedEvent d: Get(hits, d.TargetId).Damage += d.Amount; break;
                        case HealedEvent h: Get(hits, h.TargetId).Heal += h.Amount; break;
                        case DiedEvent dd: Get(hits, dd.UnitId).Died = true; break;
                        case BuffAppliedEvent b: Get(hits, b.UnitId).Buffs.Add(b.BuffId); break;
                        case OrbMintedEvent o when o.Landed: landed++; break;
                    }
                }
                perSample.Add(hits);
                orbs.Add(landed);
            }

            var ids = new HashSet<int>();
            foreach (var hs in perSample) foreach (var id in hs.Keys) ids.Add(id);
            foreach (var id in ids)
            {
                var t = new OutcomeTarget { UnitId = id, DamageMin = int.MaxValue, HealMin = int.MaxValue };
                int kills = 0;
                var statusCounts = new Dictionary<string, int>();
                foreach (var hs in perSample)
                {
                    hs.TryGetValue(id, out var x);
                    int dmg = x?.Damage ?? 0, heal = x?.Heal ?? 0;
                    if (dmg < t.DamageMin) t.DamageMin = dmg;
                    if (dmg > t.DamageMax) t.DamageMax = dmg;
                    if (heal < t.HealMin) t.HealMin = heal;
                    if (heal > t.HealMax) t.HealMax = heal;
                    if (x != null && x.Died) kills++;
                    if (x != null) foreach (var b in x.Buffs) statusCounts[b] = (statusCounts.TryGetValue(b, out var n) ? n : 0) + 1;
                }
                t.KillChance = kills / (float)samples;
                foreach (var kv in statusCounts) t.StatusChance[kv.Key] = kv.Value / (float)samples;
                result.Targets.Add(t);
            }
            result.OrbsMin = int.MaxValue;
            foreach (var n in orbs) { if (n < result.OrbsMin) result.OrbsMin = n; if (n > result.OrbsMax) result.OrbsMax = n; }
            if (orbs.Count == 0) result.OrbsMin = 0;
            return result;
        }

        private sealed class Sample
        {
            public int Damage, Heal;
            public bool Died;
            public HashSet<string> Buffs = new HashSet<string>();
        }

        private static Sample Get(Dictionary<int, Sample> d, int id)
        {
            if (!d.TryGetValue(id, out var x)) d[id] = x = new Sample();
            return x;
        }
    }
}
