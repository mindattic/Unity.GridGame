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
            Directory.CreateDirectory(dir);
            var c = Real;
            var heroes = c.Classes.Values.Where(k => k.Team == Team.Hero).Select(k => k.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var stages = c.CampaignStages().Take(15).ToList();
            var plan = new List<(List<string> party, StageDef stage)>();
            if (perStage > 0)
            {
                for (int si = 0; si < stages.Count; si++)
                    for (int k = 0; k < perStage; k++)
                    {
                        int start = (si * perStage + k) * 5;
                        plan.Add((Enumerable.Range(0, 5).Select(i => heroes[(start + i) % heroes.Count]).Distinct().ToList(), stages[si]));
                    }
            }
            else
            {
                int b = 0;
                for (int round = 0; round < rounds; round++)
                    for (int start = 0; start < heroes.Count; start += 5, b++)
                        plan.Add((Enumerable.Range(0, 5).Select(i => heroes[(start + i + round * 2) % heroes.Count]).Distinct().ToList(), stages[b % stages.Count]));
            }
            int battle = 0;
            foreach (var (party, stage) in plan)
                {
                    int seed = 1000 + battle;
                    var save = Progression.NewGame(c, "Usage", party);
                    int level = Progression.StageRecommendedLevel(c, save, stage);
                    foreach (var h in save.Roster) h.TotalXp = Formulas.TotalXpForLevel(level);
                    var setup = BattleFactory.Build(c, save, stage.Id, seed);
                    var opening = new List<BattleEvent>();
                    var s = BattleEngine.Create(c, setup, opening);
                    var rec = new BattleRecorder(s, setup, source, opening);
                    var bot = new HeroBot(seed, casual ? 0.5f : 0f);
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
