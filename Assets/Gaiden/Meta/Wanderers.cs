using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>
    /// Wanderers (§25.11.2, GG-T342; amends the GG-T236 random rescues). Owner, 2026-11-07: "you will sometimes meet a hero
    /// while fighting a monster and they will fight the monsters separately but you are able to complete pincer moves with
    /// them so you fill a trust gauge by pincering, or healing them at the end of the stage and the loot you and the team
    /// member you choose has a conversation with them, if they can come to an understanding they join, if not they go back
    /// into the pool of possible recruits" — and "the point is to gain their trust, tell them what they want to hear, and
    /// then you have a follower — another party member."
    ///
    /// <para>The roll: a campaign battle the player launches may hold one wanderer — a random hero the player doesn't own —
    /// seeded from the stage, the save's creation time and the battle seed, so a battle replays the same roll. Only the
    /// player's own launches roll (BattleScreen calls <see cref="Apply"/>); the bots' balance runs, the dotnet soaks, Test
    /// Mode and the Crucible never do.</para>
    ///
    /// <para>The understanding (<see cref="Judge"/>), deterministic and shown before the talk: they join only if the
    /// companion <b>says what they want to hear</b> AND they trust the party — <b>full trust</b>, or <b>at least half</b>
    /// with a companion of <b>their own faction</b> (STORY.md, factions.json). Under half trust, nothing said will do.</para>
    /// </summary>
    public static class Wanderers
    {
        public const float Chance = 0.25f;

        // ── the roll ────────────────────────────────────────────────────────────────

        /// <summary>Adds the rolled wanderer (if any) to <paramref name="setup"/>.</summary>
        public static void Apply(ContentDb c, SaveData s, BattleSetup setup)
        {
            if (setup?.Stage == null) return;
            setup.Wanderer = Roll(c, s, setup.Stage, setup.Seed);
        }

        public static WandererSpec Roll(ContentDb c, SaveData s, StageDef stage, int battleSeed)
        {
            if (c == null || s == null || stage == null || s.TestMode || stage.IsTest || Crucible.IsStage(stage.Id)) return null;
            if (!c.CampaignStages().Any(x => x.Id == stage.Id) || stage.Waves.Count == 0) return null;
            if (stage.Waves.Any(w => (w.Guests?.Count ?? 0) > 0 || (w.Recruitables?.Count ?? 0) > 0)) return null;
            var rng = new Random(SeedFor(s, stage.Id, battleSeed));
            if (rng.NextDouble() >= Chance) return null;
            var pool = Pool(c, s);
            if (pool.Count == 0) return null;
            // Early in the stage, so there's a real fight to earn their trust in.
            return new WandererSpec { Class = pool[rng.Next(pool.Count)], Wave = rng.Next(Math.Min(2, stage.Waves.Count)) };
        }

        /// <summary>Every hero a wanderer may be — and the Summon Circle's Diamond draw (GG-T343) picks from: unowned, not
        /// lost, and not already promised elsewhere — the Summon Circle's gold pool, authored guests and recruitables,
        /// named story recruits and the mercenaries keep their own ways in. Id order, so a seed always names the same hero.
        /// A wanderer who parts ways stays in it: owning the hero is the only thing that takes them out.</summary>
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
            // A Camp NPC wears its hero class's face (the Alchemist), so a wanderer of that class would be the NPC's double.
            foreach (var npc in CampNpcs.All) taken.Add(npc);
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

        // ── after the battle ────────────────────────────────────────────────────────

        /// <summary>A wanderer still standing at a victory, waiting to talk (<see cref="BattleReport.WandererOffer"/>).</summary>
        public sealed class Offer
        {
            public string ClassId;
            public int Level, Hp, MaxHp, Trust, TrustNeeded;
        }

        /// <summary>The offer a finished battle makes: the wanderer standing on the hero side at a victory, unowned.</summary>
        public static Offer OfferFrom(SaveData s, BattleState battle)
        {
            if (battle == null || battle.Phase != BattlePhase.Victory || battle.Wanderer == null || s.Hero(battle.Wanderer) != null) return null;
            var u = battle.Units.FirstOrDefault(x => x.Wanderer && x.Def.Id == battle.Wanderer);
            if (u == null || !u.Alive || !u.IsHero) return null;
            return new Offer { ClassId = u.Def.Id, Level = u.Level, Hp = u.Hp, MaxHp = u.MaxHp, Trust = u.Trust, TrustNeeded = u.TrustNeeded };
        }

        public enum TrustTier { Low, Half, Full }

        public static TrustTier Tier(int trust, int needed) =>
            needed <= 0 || trust >= needed ? TrustTier.Full : trust * 2 >= needed ? TrustTier.Half : TrustTier.Low;

        /// <summary>The companion is from the wanderer's own STORY.md faction (factions.json).</summary>
        public static bool Fits(ContentDb c, string wanderer, string companion)
        {
            var f = c.FactionOf(wanderer);
            return f != null && companion != null && c.FactionOf(companion) == f;
        }

        /// <summary>The whole understanding rule: say what they want to hear, and they trust you fully — or half, with a
        /// companion of their own faction.</summary>
        public static bool Joins(TrustTier tier, bool fits, bool heard) => heard && (tier == TrustTier.Full || (tier == TrustTier.Half && fits));

        public static bool Judge(ContentDb c, Offer o, string companion, bool heard) => o != null && Joins(Tier(o.Trust, o.TrustNeeded), Fits(c, o.ClassId, companion), heard);

        /// <summary>What a talk with <paramref name="companion"/> can reach, before anything is said (the results screen's hint).</summary>
        public static string Outlook(ContentDb c, Offer o, string companion)
        {
            var tier = Tier(o.Trust, o.TrustNeeded);
            if (tier == TrustTier.Low) return "Too little trust: nothing said will win them over this time.";
            if (tier == TrustTier.Full) return "Full trust: say what they want to hear and they join.";
            var f = c.FactionOf(o.ClassId);
            if (Fits(c, o.ClassId, companion)) return $"Half trust, and one of their own ({f.Name}): say what they want to hear and they join.";
            return f != null ? $"Half trust: only a companion from {f.Name} can win them over." : "Half trust: not enough on its own.";
        }

        /// <summary>The conversation for <paramref name="classId"/>: one written for that hero, else their faction's, else the generic one.</summary>
        public static DialogueDef Scene(ContentDb c, string classId)
        {
            var all = c.DialogueOrder.Where(d => d.Trigger?.Kind == DialogueTriggerKind.Wanderer).ToList();
            var faction = c.FactionOf(classId)?.Id;
            return all.FirstOrDefault(d => string.Equals(d.Trigger.Id, classId, StringComparison.OrdinalIgnoreCase))
                ?? (faction == null ? null : all.FirstOrDefault(d => string.Equals(d.Trigger.Id, faction, StringComparison.OrdinalIgnoreCase)))
                ?? all.FirstOrDefault(d => d.Trigger.Id == null);
        }

        /// <summary>A runner for the talk: "$hero" is the wanderer, "$with" the chosen companion. When the reply lands
        /// (<see cref="DialogueDef.OutcomeNext"/>) the rule is judged, a joiner joins, and the scene goes to its
        /// <see cref="DialogueDef.JoinNode"/> or <see cref="DialogueDef.PartNode"/>.</summary>
        public static DialogueRunner Begin(ContentDb c, SaveData s, Offer o, string companion)
        {
            var def = Scene(c, o.ClassId);
            if (def == null) return null;
            var run = new DialogueRunner(c, s, def, companion, o.ClassId);
            run.Outcome = r =>
            {
                bool heard = r.Transcript.Any(l => string.Equals(l.NodeId, DialogueDef.HeardNode, StringComparison.OrdinalIgnoreCase));
                bool joins = Judge(c, o, companion, heard) && s.Hero(o.ClassId) == null;
                if (joins) r.Log.Add(Join(c, s, o));
                return joins ? DialogueDef.JoinNode : DialogueDef.PartNode;
            };
            return run;
        }

        /// <summary>Headless talk (tests; a bot never meets a wanderer): picks <paramref name="answer"/> at the question.
        /// Returns whether they joined.</summary>
        public static bool Converse(ContentDb c, SaveData s, Offer o, string companion, int answer)
        {
            var run = Begin(c, s, o, companion);
            if (run == null) return false;
            for (int guard = 0; !run.Finished && guard < 200; guard++)
                if (!run.Advance()) run.Choose(Math.Min(answer, Math.Max(0, run.Choices.Count - 1)));
            return s.Hero(o.ClassId) != null;
        }

        /// <summary>The index of the answer that speaks to them in <paramref name="def"/>'s question (-1 if none).</summary>
        public static int RightAnswer(DialogueDef def)
        {
            var ask = def?.Nodes.FirstOrDefault(n => n != null && n.Choices.Count > 0);
            return ask == null ? -1 : ask.Choices.FindIndex(ch => string.Equals(ch?.Next, DialogueDef.HeardNode, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>They join exactly like a freed guest: the roster, the party if there's room, at their battle level.</summary>
        public static string Join(ContentDb c, SaveData s, Offer o)
        {
            bool party = BattleFactory.JoinRoster(c, s, o.ClassId, o.Level, o.Hp, o.MaxHp);
            var name = c.Class(o.ClassId).Name;
            return party ? $"{name} joins the caravan!" : $"{name} joins the caravan (in reserve)";
        }
    }
}
