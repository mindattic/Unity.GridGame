using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>A hero talks to the neutral recruitable cardinally beside it (§25.11.1, GG-T132; Fire Emblem's "talk").
    /// Conditions unmet: it says what it wants and the talk is free (no clock). Met: it joins — costs an action.</summary>
    public sealed class TalkCommand : BattleCommand
    {
        public int HeroId;
        public int TargetId;
        public override string ToString() => $"Talk {HeroId} -> {TargetId}";
    }

    /// <summary>One spoken line of a recruit exchange: the speaker's class (portrait, name) and the text.</summary>
    public sealed class RecruitLine
    {
        public string SpeakerClassId;
        public string SpeakerName;
        public string Text;
    }

    /// <summary>A neutral recruitable steps onto the board at a wave's start, with everything it will ask for.</summary>
    public sealed class RecruitableAppearedEvent : BattleEvent
    {
        public int UnitId; public Coord At;
        public List<string> Wants = new List<string>();
        public override string ToString() => $"Recruitable {UnitId} appears at {At}";
    }

    /// <summary>A hero talked to a recruitable that still wants something: its lines, and what's still unmet (no time passed).</summary>
    public sealed class RecruitOfferedEvent : BattleEvent
    {
        public int HeroId; public int UnitId;
        public List<RecruitLine> Lines = new List<RecruitLine>();
        /// <summary>Plain-language asks still unmet ("Heal me", "Pay 500 G" …).</summary>
        public List<string> Wants = new List<string>();
        public override string ToString() => $"RecruitOffered {HeroId}->{UnitId} wants [{string.Join("; ", Wants)}]";
    }

    /// <summary>A recruitable was talked round: it is on the hero team now, a guest ally (standing at a victory, it joins).</summary>
    public sealed class RecruitJoinedEvent : BattleEvent
    {
        public int HeroId; public int UnitId;
        public List<RecruitLine> Lines = new List<RecruitLine>();
        public int GoldPaid;
        public override string ToString() => $"RecruitJoined {UnitId} (talked by {HeroId}, paid {GoldPaid})";
    }

    /// <summary>Read-only recruit rules (GG-T132) shared by the engine, the bot and the view.</summary>
    public static class Recruitment
    {
        /// <summary>Banked gold still unspent this battle.</summary>
        public static int PurseLeft(BattleState s) => Math.Max(0, s.Purse - s.Results.CoinsSpent);

        public static bool IsRecruitable(Unit u) => u != null && u.Alive && u.IsNeutral && u.Recruitable != null;

        /// <summary>Can this hero talk to that unit right now (living hero, living recruitable, cardinally adjacent)?</summary>
        public static bool CanTalk(BattleState s, Unit hero, Unit target) =>
            s.Phase == BattlePhase.PlayerInput && hero != null && hero.Alive && hero.IsHero && !hero.IsImmobile
            && !hero.ActedThisWindow // GG-T308: talking is that hero's action for the window
            && IsRecruitable(target) && hero.IsAdjacentTo(target);

        /// <summary>The conditions this hero's talk wouldn't yet meet (empty = it joins).</summary>
        public static List<RecruitCondition> Unmet(BattleState s, Unit hero, Unit target)
        {
            var list = new List<RecruitCondition>();
            var def = target?.Recruitable;
            if (def == null) return list;
            int gold = 0;
            foreach (var c in def.Conditions)
            {
                if (c == null) continue;
                switch (c.Type)
                {
                    case RecruitConditionType.HealedBy:
                        if (!target.RecruitMarks.Contains(c.Id == null ? "healed" : "healed:" + c.Id)) list.Add(c);
                        break;
                    case RecruitConditionType.ParticipatedInPincerWith:
                        if (!target.RecruitMarks.Contains(c.Id == null ? "pincer" : "pincer:" + c.Id)) list.Add(c);
                        break;
                    case RecruitConditionType.PayGold:
                        gold += c.Amount;
                        if (PurseLeft(s) < gold) list.Add(c);
                        break;
                    case RecruitConditionType.TalkedBy:
                        if (hero == null || !string.Equals(hero.Def.Id, c.Id, StringComparison.OrdinalIgnoreCase)) list.Add(c);
                        break;
                }
            }
            return list;
        }

        /// <summary>What a condition asks, in the recruitable's own plain words.</summary>
        public static string Describe(ContentDb content, RecruitCondition c)
        {
            string Name(string id) => content?.TryClass(id)?.Name ?? id;
            switch (c.Type)
            {
                case RecruitConditionType.HealedBy: return c.Id == null ? "Heal me" : $"Have {Name(c.Id)} heal me";
                case RecruitConditionType.ParticipatedInPincerWith: return c.Id == null ? "Strike a pincer from beside me" : $"Have {Name(c.Id)} strike a pincer beside me";
                case RecruitConditionType.PayGold: return $"Pay {c.Amount} G";
                case RecruitConditionType.TalkedBy: return $"Only {Name(c.Id)} can talk me round";
            }
            return c.Type.ToString();
        }

        /// <summary>The first hero that could recruit someone right now (every condition met), as a command; else null.
        /// The bot takes it ahead of any other move (§6 bot soak carve-out, GG-T132).</summary>
        public static TalkCommand ReadyTalk(BattleState s)
        {
            if (s.Phase != BattlePhase.PlayerInput) return null;
            foreach (var n in s.Units)
            {
                if (!IsRecruitable(n)) continue;
                foreach (var h in s.Units)
                    if (CanTalk(s, h, n) && Unmet(s, h, n).Count == 0) return new TalkCommand { HeroId = h.Id, TargetId = n.Id };
            }
            return null;
        }

        /// <summary>The lines of a Recruit dialogue: before the join node (it still wants something) or from it on (it joins).</summary>
        public static List<RecruitLine> Lines(ContentDb content, Unit hero, Unit target, bool joining)
        {
            var lines = new List<RecruitLine>();
            var d = content?.Dialogues != null && target?.Recruitable?.Dialogue != null && content.Dialogues.TryGetValue(target.Recruitable.Dialogue, out var dd) ? dd : null;
            if (d == null || d.Nodes.Count == 0) return lines;
            var byId = new Dictionary<string, DialogueNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in d.Nodes) if (n?.Id != null && !byId.ContainsKey(n.Id)) byId[n.Id] = n;
            string id = joining ? RecruitableDef.JoinNode : d.Start ?? d.Nodes[0]?.Id;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (id != null && byId.TryGetValue(id, out var node) && seen.Add(id))
            {
                if (!joining && string.Equals(id, RecruitableDef.JoinNode, StringComparison.OrdinalIgnoreCase)) break;
                var sp = d.Speakers.Find(x => x != null && string.Equals(x.Id, node.Speaker, StringComparison.OrdinalIgnoreCase));
                string cls = sp?.Class == "$with" || sp?.Class == "$lead" ? hero?.Def.Id : sp?.Class ?? target.Def.Id;
                lines.Add(new RecruitLine
                {
                    SpeakerClassId = cls,
                    SpeakerName = sp?.Name ?? content.TryClass(cls)?.Name ?? cls,
                    Text = node.Text,
                });
                id = node.Next;
            }
            return lines;
        }
    }
}
