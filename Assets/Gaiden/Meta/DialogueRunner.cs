using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;

namespace GridGame.Meta
{
    /// <summary>One spoken line as the screen shows it: who (resolved name + portrait) and what.</summary>
    public sealed class DialogueLine
    {
        public string NodeId;
        public string SpeakerId;
        public string Name;
        /// <summary>Portrait id (Resources/Portraits/&lt;id&gt;), or null for none.</summary>
        public string Portrait;
        public string Text;
        /// <summary>A choice the player picked to leave this line (null when it auto-advanced).</summary>
        public string Answer;
    }

    /// <summary>
    /// Plays one <see cref="DialogueDef"/> against a save (§27.1, GG-T117). A data interpreter, not a VM: it walks
    /// nodes, offers the choices whose conditions hold, and applies effects in the save's own vocabulary.
    /// Engine-free so it's covered by dotnet tests; DialogueScreen only draws what this says.
    /// </summary>
    public sealed class DialogueRunner
    {
        private readonly ContentDb c;
        private readonly SaveData s;
        private readonly Dictionary<string, DialogueNode> nodes;

        public DialogueDef Def { get; }
        /// <summary>The party member a Talk dialogue is with ("$with"); null otherwise.</summary>
        public string With { get; }
        public DialogueNode Node { get; private set; }
        public bool Finished => Node == null;
        /// <summary>Every line reached so far, in order (the current one last).</summary>
        public List<DialogueLine> Transcript { get; } = new List<DialogueLine>();
        /// <summary>What each effect did, in order ("Sellsword joins you!", "+50 G") — the screen toasts them.</summary>
        public List<string> Log { get; } = new List<string>();
        /// <summary>The hero this dialogue's first JoinHero brings ("$recruit"), fixed when the dialogue opens so
        /// the face that speaks is the hero who joins (GG-T118); null if it has none or nobody is left to join.</summary>
        public string Recruit => recruits.Values.FirstOrDefault(v => v != null);
        private readonly Dictionary<DialogueEffect, string> recruits = new Dictionary<DialogueEffect, string>();

        public DialogueRunner(ContentDb c, SaveData s, DialogueDef def, string with = null)
        {
            this.c = c; this.s = s; Def = def ?? throw new ArgumentNullException(nameof(def)); With = with;
            nodes = def.Nodes.Where(n => n?.Id != null).GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var promised = new List<string>();
            foreach (var e in def.AllEffects().Where(e => e?.Type == DialogueEffectType.JoinHero))
            {
                if (recruits.ContainsKey(e)) continue;
                var pick = Summon.StoryPick(c, s, e.Id, e.Role, promised);
                recruits[e] = pick;
                if (pick != null) promised.Add(pick);
            }
            Enter(def.Start ?? def.Nodes.FirstOrDefault()?.Id);
        }

        public DialogueLine Current => Finished ? null : Transcript[Transcript.Count - 1];

        /// <summary>The current node's choices the player may pick now (conditions met), with their indexes in the node.</summary>
        public List<DialogueChoice> Choices => Finished ? new List<DialogueChoice>() : Node.Choices.Where(ch => All(c, s, ch.Conditions)).ToList();

        public bool AwaitingChoice => !Finished && Node.Choices.Count > 0;

        /// <summary>Moves past a line that has no choices. Returns false if a choice is needed or it's over.</summary>
        public bool Advance()
        {
            if (Finished || AwaitingChoice) return false;
            Enter(Node.Next);
            return true;
        }

        /// <summary>Picks one of <see cref="Choices"/> (an index into that filtered list): applies its effects, then moves on.</summary>
        public Result Choose(int index)
        {
            var offered = Choices;
            if (!AwaitingChoice || index < 0 || index >= offered.Count) return Result.Fail("No such choice");
            var ch = offered[index];
            Current.Answer = ch.Label;
            foreach (var e in ch.Effects) Apply(e);
            Enter(ch.Next);
            return Result.Success();
        }

        private void Enter(string id)
        {
            if (id == null || !nodes.TryGetValue(id, out var n))
            {
                Node = null;
                s.SeenDialogues.Add(Def.Id);
                return;
            }
            Node = n;
            var sp = Def.Speakers.FirstOrDefault(x => string.Equals(x.Id, n.Speaker, StringComparison.OrdinalIgnoreCase));
            var (name, portrait) = Resolve(sp);
            Transcript.Add(new DialogueLine { NodeId = n.Id, SpeakerId = n.Speaker, Name = name, Portrait = portrait, Text = n.Text });
            foreach (var e in n.Effects) Apply(e);
        }

        /// <summary>A speaker's display name and portrait id, with "$lead"/"$with" bound to this save's heroes.</summary>
        public (string name, string portrait) Resolve(DialogueSpeaker sp)
        {
            if (sp == null) return ("", null);
            string cls = sp.Class == "$lead" ? s.Party.FirstOrDefault() : sp.Class == "$with" ? With : sp.Class == "$recruit" ? Recruit : sp.Class;
            var def = c.TryClass(cls);
            return (sp.Name ?? def?.Name ?? sp.Id, sp.Portrait ?? def?.Id);
        }

        private void Apply(DialogueEffect e)
        {
            var msg = e?.Type == DialogueEffectType.JoinHero && recruits.TryGetValue(e, out var who) ? JoinStoryHero(c, s, who) : ApplyEffect(c, s, e);
            if (msg != null) Log.Add(msg);
        }

        /// <summary>Plays a dialogue to the end with no screen, taking the first choice offered at every turn —
        /// how the bots (balance sims, Test Mode) live through story beats. Returns the effect log.</summary>
        public static List<string> PlayHeadless(ContentDb c, SaveData s, DialogueDef def, string with = null)
        {
            var r = new DialogueRunner(c, s, def, with);
            for (int guard = 0; !r.Finished && guard < 500; guard++)
                if (!r.Advance()) r.Choose(0);
            if (!r.Finished) s.SeenDialogues.Add(def.Id); // a loop no first choice escapes — count it as told
            return r.Log;
        }

        // ── the vocabulary ──────────────────────────────────────────────────────────

        public static bool All(ContentDb c, SaveData s, IEnumerable<DialogueCondition> conds) => conds == null || conds.All(x => Check(c, s, x));

        public static bool Check(ContentDb c, SaveData s, DialogueCondition x)
        {
            if (x == null) return true;
            switch (x.Type)
            {
                case DialogueConditionType.FlagSet: return s.Flags.Contains(x.Id);
                case DialogueConditionType.FlagNotSet: return !s.Flags.Contains(x.Id);
                case DialogueConditionType.HasItem: return s.Count(x.Id) >= Math.Max(1, x.Amount);
                case DialogueConditionType.GoldAtLeast: return s.Gold >= x.Amount;
                case DialogueConditionType.PartyHas: return s.Party.Any(p => string.Equals(p, x.Id, StringComparison.OrdinalIgnoreCase));
                case DialogueConditionType.RosterHas: return s.Hero(x.Id) != null;
                case DialogueConditionType.RosterLacks: return s.Hero(x.Id) == null;
                case DialogueConditionType.NpcJoined: return NpcJoined(s, x.Id);
                default: return false;
            }
        }

        public static bool NpcJoined(SaveData s, string npcId) => npcId != null && s.Flags.Contains(ContentLoader.NpcFlagPrefix + npcId);

        /// <summary>Applies one effect; returns a short player-facing note, or null when there's nothing to say.
        /// Nothing here removes a hero (owner rule: nobody dies permanently).</summary>
        public static string ApplyEffect(ContentDb c, SaveData s, DialogueEffect e)
        {
            if (e == null) return null;
            switch (e.Type)
            {
                case DialogueEffectType.SetFlag: s.Flags.Add(e.Id); return null;
                case DialogueEffectType.ClearFlag: if (!e.Id.StartsWith(ContentLoader.NpcFlagPrefix)) s.Flags.Remove(e.Id); return null;
                case DialogueEffectType.GrantGold:
                {
                    int delta = e.Amount < 0 ? -Math.Min(s.Gold, -e.Amount) : e.Amount; // never below zero
                    s.Gold += delta;
                    if (delta > 0) s.LifetimeCoins += delta;
                    return delta == 0 ? null : (delta > 0 ? $"+{delta} G" : $"{delta} G");
                }
                case DialogueEffectType.GrantItem:
                {
                    int n = Math.Max(1, e.Amount);
                    s.AddItem(e.Id, n);
                    return $"Received {c.TryItem(e.Id)?.Name ?? e.Id}" + (n > 1 ? $" ×{n}" : "");
                }
                case DialogueEffectType.RecruitHero:
                {
                    if (c.TryClass(e.Id) == null) return null;
                    bool had = s.Hero(e.Id) != null;
                    Summon.Join(c, s, e.Id);
                    return had ? null : $"{c.Class(e.Id).Name} joins you!";
                }
                case DialogueEffectType.JoinNpc:
                    return s.Flags.Add(ContentLoader.NpcFlagPrefix + e.Id) ? $"The {Npc(e.Id)} joins the caravan!" : null;
                case DialogueEffectType.JoinHero:
                    return JoinStoryHero(c, s, Summon.StoryPick(c, s, e.Id, e.Role));
                default: return null;
            }
        }

        /// <summary>A story recruit joins (GG-T118): free, at the roster's average experience, into the party if
        /// there's room (else the reserve) — exactly like a Summon Circle recruit.</summary>
        private static string JoinStoryHero(ContentDb c, SaveData s, string classId)
        {
            if (classId == null || c.TryClass(classId) == null || s.Hero(classId) != null) return null;
            Summon.Join(c, s, classId);
            return s.Party.Contains(classId) ? $"{c.Class(classId).Name} joins the party!" : $"{c.Class(classId).Name} joins you (in reserve)!";
        }

        /// <summary>A Camp NPC's display name ("blacksmith" → "Blacksmith").</summary>
        public static string Npc(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);

        // ── triggers ────────────────────────────────────────────────────────────────

        /// <summary>The story beat to play before launching <paramref name="stageId"/>, or null: the first dialogue
        /// (file order) not yet seen whose ThemeStart names the stage's theme, whose StageCleared stage is
        /// cleared, whose GearWorn threshold some equipped piece has fallen below, or whose CanBrew loot is in the bag (GG-T176), or whose FirstArmor piece has been forged (GG-T179), and whose conditions hold. Test Mode saves never get story (GG-T93: bot-only).</summary>
        public static DialogueDef PendingBeat(ContentDb c, SaveData s, string stageId) =>
            s == null || s.TestMode ? null : NextBeat(c, s, stageId);

        /// <summary>For the bots (balance sims and Test Mode, which never show story): plays every story beat that's
        /// pending before <paramref name="stageId"/> headlessly (first choice each time) so story recruits still
        /// join (GG-T118). Returns what the beats did ("Red Mage joins the party!").</summary>
        public static List<string> PlayPendingBeatsHeadless(ContentDb c, SaveData s, string stageId)
        {
            var log = new List<string>();
            if (s == null) return log;
            for (var d = NextBeat(c, s, stageId); d != null && !s.SeenDialogues.Contains(d.Id); d = NextBeat(c, s, stageId))
                log.AddRange(PlayHeadless(c, s, d));
            return log;
        }

        private static DialogueDef NextBeat(ContentDb c, SaveData s, string stageId)
        {
            var theme = c.ThemeOf(stageId);
            foreach (var d in c.DialogueOrder)
            {
                if (d.Trigger == null || s.SeenDialogues.Contains(d.Id) || !All(c, s, d.Conditions)) continue;
                bool fires = d.Trigger.Kind == DialogueTriggerKind.ThemeStart && theme != null && string.Equals(d.Trigger.Id, theme.Id, StringComparison.OrdinalIgnoreCase)
                    || d.Trigger.Kind == DialogueTriggerKind.StageCleared && s.ClearedStages.Contains(d.Trigger.Id)
                    || d.Trigger.Kind == DialogueTriggerKind.GearWorn && (Repair.AnyBelow(c, s, d.Trigger.Percent) || ReachedTheme(c, theme, d.Trigger.Id))
                    || d.Trigger.Kind == DialogueTriggerKind.CanBrew && (Brewing.LootedForFirstBrew(c, s) || ReachedTheme(c, theme, d.Trigger.Id))
                    || d.Trigger.Kind == DialogueTriggerKind.FirstArmor && (s.Flags.Contains(Forging.FirstArmorFlag) || ReachedTheme(c, theme, d.Trigger.Id));
                if (fires) return d;
            }
            return null;
        }

        /// <summary>True when <paramref name="theme"/> is <paramref name="fallbackThemeId"/> or comes after it in the campaign.</summary>
        private static bool ReachedTheme(ContentDb c, ThemeDef theme, string fallbackThemeId)
        {
            if (theme == null || fallbackThemeId == null) return false;
            int at = c.Campaign.Themes.FindIndex(t => string.Equals(t.Id, fallbackThemeId, StringComparison.OrdinalIgnoreCase));
            return at >= 0 && c.Campaign.Themes.IndexOf(theme) >= at;
        }

        /// <summary>Who Camp's Talk row offers: the party, then every joined NPC ("npc:&lt;id&gt;").</summary>
        public static List<string> TalkPartners(SaveData s)
        {
            var list = new List<string>(s.Party);
            foreach (var n in DialogueDef.Npcs) if (NpcJoined(s, n)) list.Add(ContentLoader.NpcFlagPrefix + n);
            return list;
        }

        /// <summary>The conversation to have with <paramref name="partner"/> (a party hero's class id or "npc:&lt;id&gt;"),
        /// or null: unseen before seen, a dialogue written for them before a generic "party" one, file order otherwise.</summary>
        public static DialogueDef TalkTopic(ContentDb c, SaveData s, string partner)
        {
            bool isNpc = partner != null && partner.StartsWith(ContentLoader.NpcFlagPrefix);
            var options = c.DialogueOrder.Where(d => d.Trigger?.Kind == DialogueTriggerKind.Talk
                    && (string.Equals(d.Trigger.Id, partner, StringComparison.OrdinalIgnoreCase) || !isNpc && d.Trigger.Id == "party")
                    && !(d.Once && s.SeenDialogues.Contains(d.Id))
                    && All(c, s, d.Conditions))
                .Select((d, i) => (d, i))
                .OrderBy(x => s.SeenDialogues.Contains(x.d.Id) ? 1 : 0)
                .ThenBy(x => x.d.Trigger.Id == "party" ? 1 : 0)
                .ThenBy(x => x.i);
            return options.Select(x => x.d).FirstOrDefault();
        }
    }

    /// <summary>
    /// The Camp's people (§25.0.1, GG-T119): the Merchant, Blacksmith, Alchemist, Enchantress and Innkeeper are met on
    /// the journey and join at story beats (a dialogue <c>JoinNpc</c> effect sets the flag <c>npc:&lt;id&gt;</c>). Until
    /// one joins, their Camp service is hidden and the camp routine doesn't use it.
    /// </summary>
    public static class CampNpcs
    {
        public const string Merchant = "merchant";
        public const string Blacksmith = "blacksmith";
        public const string Alchemist = "alchemist";
        public const string Enchantress = "enchantress";
        public const string Innkeeper = "innkeeper";

        public static IReadOnlyList<string> All => DialogueDef.Npcs;

        public static bool Joined(SaveData s, string npcId) => s != null && DialogueRunner.NpcJoined(s, npcId);

        /// <summary>Every NPC joins at once — a Test Mode save (it never plays story), the sweep's preview save, and
        /// the v4 migration of a save from before NPCs had to be met (they'd been using every service already).</summary>
        public static void JoinAll(SaveData s)
        {
            foreach (var n in DialogueDef.Npcs) s.Flags.Add(ContentLoader.NpcFlagPrefix + n);
        }
    }
}
