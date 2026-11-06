using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T246: a measurement soak, not a gate. With GAIDEN_ABILITY_RECORDS=&lt;dir&gt; it plays every hero class (in
    /// rotating parties of five, default bars, at each stage's recommended level) across the main campaign and writes
    /// one battle record per fight; `python tools/records.py --abilities &lt;dir&gt;` turns them into the per-ability
    /// usefulness table (docs/research/AbilityUsage.md). Unset, it does nothing.
    /// </summary>
    public class AbilityUsageSoak
    {
        [Test]
        public void Ability_usage_soak_across_every_hero_class()
        {
            var dir = Environment.GetEnvironmentVariable("GAIDEN_ABILITY_RECORDS");
            if (string.IsNullOrEmpty(dir)) { TestContext.Out.WriteLine("GAIDEN_ABILITY_RECORDS not set — nothing written"); return; }
            int rounds = int.TryParse(Environment.GetEnvironmentVariable("GAIDEN_ABILITY_ROUNDS"), out var n) ? n : 2;
            // GG-T255: GAIDEN_ABILITY_BOT=casual plays the casual bot (randomness 0.5), and GAIDEN_ABILITY_PER_STAGE=N
            // fights every stage N times with rotating parties, so POL-G1/G2/G7 get per-stage win rates.
            bool casual = Environment.GetEnvironmentVariable("GAIDEN_ABILITY_BOT") == "casual";
            int perStage = int.TryParse(Environment.GetEnvironmentVariable("GAIDEN_ABILITY_PER_STAGE"), out var ps) ? ps : 0;
            string source = casual ? "bot:casual" : "bot:greedy";
            // GG-T292: GAIDEN_TUNE="PincerPushbackSeconds=0.5;PushbackMaxSeconds=2" overrides BattleConfig's static tuning
            // fields for this soak only (a tuning sweep without a rebuild per value); restored when it ends.
            var restore = new List<(System.Reflection.FieldInfo f, object v)>();
            foreach (var kv in (Environment.GetEnvironmentVariable("GAIDEN_TUNE") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=');
                var f = typeof(BattleConfig).GetField(parts[0].Trim(), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Assert.That(f != null && !f.IsLiteral && !f.IsInitOnly, $"GAIDEN_TUNE: {parts[0]} is not a tunable BattleConfig field");
                restore.Add((f, f.GetValue(null)));
                f.SetValue(null, Convert.ChangeType(parts[1].Trim(), f.FieldType, System.Globalization.CultureInfo.InvariantCulture));
            }
            // GAIDEN_PUSHBACK_SCALE=1.5 scales every ability's Pushback seconds (capped at PushbackMaxSeconds) for this soak.
            var pushes = Real.Abilities.Values.Where(a => a.Pushback > 0f).ToDictionary(a => a, a => a.Pushback);
            if (float.TryParse(Environment.GetEnvironmentVariable("GAIDEN_PUSHBACK_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                foreach (var a in pushes.Keys) a.Pushback = Math.Min(BattleConfig.PushbackMaxSeconds, a.Pushback * scale);
            try { Soak(dir, rounds, casual, perStage, source); }
            finally
            {
                foreach (var (f, v) in restore) f.SetValue(null, v);
                foreach (var kv in pushes) kv.Key.Pushback = kv.Value;
            }
        }

        /// <summary>The party a campaign player fields at <paramref name="stage"/>: the starting three plus every JoinHero
        /// story beat that plays before it (a StageCleared beat for an earlier stage, a ThemeStart beat for its theme or
        /// an earlier one), capped at <see cref="Progression.MaxPartySize"/> (GG-T291/GG-T292).</summary>
        internal static int CampaignPartySize(ContentDb c, StageDef stage)
        {
            var order = c.CampaignStages().Select(st => st.Id).ToList();
            int at = order.IndexOf(stage.Id), joins = 0;
            foreach (var d in c.Dialogues.Values)
            {
                if (!d.Nodes.Any(n => n.Effects.Any(e => e.Type == DialogueEffectType.JoinHero))) continue;
                int when = d.Trigger.Kind == DialogueTriggerKind.StageCleared ? order.IndexOf(d.Trigger.Id) + 1
                         : d.Trigger.Kind == DialogueTriggerKind.ThemeStart ? FirstStageOf(c, d.Trigger.Id, order) : -1;
                if (when >= 0 && when <= at) joins++;
            }
            return Math.Min(Progression.MaxPartySize, Progression.StartingPartySize + joins);
        }

        private static int FirstStageOf(ContentDb c, string theme, List<string> order)
        {
            var t = c.Campaign.Themes.FirstOrDefault(x => x.Id == theme);
            return t == null || t.Stages.Count == 0 ? -1 : order.IndexOf(t.Stages[0]);
        }

        private static void Soak(string dir, int rounds, bool casual, int perStage, string source)
        {
            Directory.CreateDirectory(dir);
            var c = Real;
            var heroes = c.Classes.Values.Where(k => k.Team == Team.Hero).Select(k => k.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var stages = c.CampaignStages().Take(15).ToList();
            var plan = new List<(List<string> party, StageDef stage)>();
            if (perStage > 0)
            {
                // GG-T292: GAIDEN_ABILITY_PARTY=campaign fields the party the campaign has at that stage (the starting three,
                // plus a rotating hero for each story-beat join before it, up to the cap of six) instead of a flat five.
                bool campaignParty = Environment.GetEnvironmentVariable("GAIDEN_ABILITY_PARTY") == "campaign";
                for (int si = 0; si < stages.Count; si++)
                    for (int k = 0; k < perStage; k++)
                    {
                        int start = (si * perStage + k) * 5;
                        if (!campaignParty) { plan.Add((Enumerable.Range(0, 5).Select(i => heroes[(start + i) % heroes.Count]).Distinct().ToList(), stages[si])); continue; }
                        // The campaign's own start (Tank, Striker, Healer) plus rotating joiners up to the stage's party size.
                        var p = Progression.StartingRoster.ToList();
                        for (int i = 0; p.Count < CampaignPartySize(c, stages[si]); i++)
                            if (!p.Contains(heroes[(start + i) % heroes.Count])) p.Add(heroes[(start + i) % heroes.Count]);
                        plan.Add((p, stages[si]));
                    }
            }
            else
            {
                int b = 0;
                for (int round = 0; round < rounds; round++)
                    for (int start = 0; start < heroes.Count; start += 5, b++)
                        plan.Add((Enumerable.Range(0, 5).Select(i => heroes[(start + i + round * 2) % heroes.Count]).Distinct().ToList(), stages[b % stages.Count]));
            }
            string onlyStage = Environment.GetEnvironmentVariable("GAIDEN_ABILITY_STAGE");
            int battle = 0;
            foreach (var (party, stage) in plan)
                {
                    // GAIDEN_ABILITY_STAGE=<id> fights only that stage, with the same parties and seeds as the full run (GG-T274).
                    if (onlyStage != null && stage.Id != onlyStage) { battle++; continue; }
                    int seed = 1000 + battle;
                    var save = Progression.NewGame(c, "Usage", party);
                    // GG-T292: GAIDEN_LEVEL_OFFSET=-3 fights under-levelled, a stress test that shows how much skill matters.
                    int level = Math.Max(1, Progression.StageRecommendedLevel(c, save, stage) + (int.TryParse(Environment.GetEnvironmentVariable("GAIDEN_LEVEL_OFFSET"), out var lo) ? lo : 0));
                    foreach (var h in save.Roster) h.TotalXp = Formulas.TotalXpForLevel(level);
                    var setup = BattleFactory.Build(c, save, stage.Id, seed);
                    var opening = new List<BattleEvent>();
                    var s = BattleEngine.Create(c, setup, opening);
                    var rec = new BattleRecorder(s, setup, source, opening);
                    var bot = new HeroBot(seed, casual ? 0.5f : 0f) { IgnoreBar = Environment.GetEnvironmentVariable("GAIDEN_BOT_BAR") == "off" };
                    int refused = 0;
                    for (int i = 0; i < 900 && !s.IsOver; i++)
                    {
                        var ev = rec.Apply(s, bot.Decide(s));
                        refused = ev.Exists(e => e is PlayerControlEvent || e is BattleEndedEvent) ? 0 : refused + 1;
                        if (refused > 5) break;
                    }
                    File.WriteAllText(Path.Combine(dir, $"{(casual ? "casual" : "greedy")}_{stage.Id}_{battle:000}.json"), rec.Finish(s).ToString(Newtonsoft.Json.Formatting.None));
                    TestContext.Out.WriteLine($"{battle:000} {stage.Id} [{string.Join(",", party)}]: {s.Phase} wave {s.Wave + 1}");
                    battle++;
                }
        }
    }
}
