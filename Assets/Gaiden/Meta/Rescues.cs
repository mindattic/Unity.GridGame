using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// Random rescues (§25.11.2, GG-T236): a campaign battle the player launches may hold a captive — a random hero the
    /// player doesn't own — who fights as a guest ally (§25.11) and joins the caravan if standing at a victory. Seeded
    /// from the stage, the save's creation time and the battle seed, so a battle replays the same roll. Only the
    /// player's own launches roll (BattleScreen calls <see cref="Apply"/>); the bots' balance runs, Test Mode and the
    /// Crucible never do.
    /// </summary>
    public static class Rescues
    {
        public const float Chance = 0.25f;

        /// <summary>Adds the rolled captive (if any) to <paramref name="setup"/>.</summary>
        public static void Apply(ContentDb c, SaveData s, BattleSetup setup)
        {
            if (setup?.Stage == null) return;
            setup.Captive = Roll(c, s, setup.Stage, setup.Seed);
        }

        public static CaptiveSpec Roll(ContentDb c, SaveData s, StageDef stage, int battleSeed)
        {
            if (c == null || s == null || stage == null || s.TestMode || stage.IsTest || Crucible.IsStage(stage.Id)) return null;
            if (!c.CampaignStages().Any(x => x.Id == stage.Id) || stage.Waves.Count == 0) return null;
            if (stage.Waves.Any(w => (w.Guests?.Count ?? 0) > 0 || (w.Recruitables?.Count ?? 0) > 0)) return null;
            var rng = new Random(SeedFor(s, stage.Id, battleSeed));
            if (rng.NextDouble() >= Chance) return null;
            var pool = Pool(c, s);
            if (pool.Count == 0) return null;
            // Early in the stage, so there's a real fight to keep them alive through.
            return new CaptiveSpec { Class = pool[rng.Next(pool.Count)], Wave = rng.Next(Math.Min(2, stage.Waves.Count)) };
        }

        /// <summary>Every hero a captive may be: unowned, not lost, and not already promised elsewhere — the Summon
        /// Circle's pool, authored guests and recruitables, named story recruits and the mercenaries keep their own
        /// ways in. Id order, so a seed always names the same hero.</summary>
        public static List<string> Pool(ContentDb c, SaveData s)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in c.Summon.Pool) taken.Add(id);
            foreach (var w in c.Stages.Values.SelectMany(st => st.Waves))
            {
                foreach (var g in w.Guests) taken.Add(g.Class);
                foreach (var r in w.Recruitables) taken.Add(r.Class);
            }
            foreach (var e in c.Dialogues.Values.SelectMany(d => d.AllEffects()))
                if (e != null && (e.Type == DialogueEffectType.JoinHero || e.Type == DialogueEffectType.RecruitHero) && e.Id != null) taken.Add(e.Id);
            foreach (var m in c.Mercenaries) taken.Add(m.Class);
            return c.Classes.Values
                .Where(d => d.Team == Team.Hero && s.Hero(d.Id) == null && !Permadeath.IsLost(s, d.Id) && !taken.Contains(d.Id))
                .Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        public static int SeedFor(SaveData s, string stageId, int battleSeed)
        {
            unchecked
            {
                long t = s.CreatedUtc.Ticks;
                int h = (int)(t ^ (t >> 32));
                foreach (char ch in stageId) h = h * 31 + ch;
                return h * 7919 + battleSeed;
            }
        }

        /// <summary>The scene to play once <paramref name="classId"/> is freed: one written for that hero first, else the generic one.</summary>
        public static DialogueDef Scene(ContentDb c, string classId)
        {
            var all = c.DialogueOrder.Where(d => d.Trigger?.Kind == DialogueTriggerKind.Rescue).ToList();
            return all.FirstOrDefault(d => string.Equals(d.Trigger.Id, classId, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(d => d.Trigger.Id == null);
        }
    }
}
