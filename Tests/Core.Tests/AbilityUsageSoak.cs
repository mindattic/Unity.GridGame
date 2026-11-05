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
            Directory.CreateDirectory(dir);
            var c = Real;
            var heroes = c.Classes.Values.Where(k => k.Team == Team.Hero).Select(k => k.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var stages = c.CampaignStages().Take(15).ToList();
            int battle = 0;
            for (int round = 0; round < rounds; round++)
                for (int start = 0; start < heroes.Count; start += 5)
                {
                    var party = Enumerable.Range(0, 5).Select(i => heroes[(start + i + round * 2) % heroes.Count]).Distinct().ToList();
                    int stageIndex = battle % stages.Count;
                    var stage = stages[stageIndex];
                    int seed = 1000 + battle;
                    var save = Progression.NewGame(c, "Usage", party);
                    int level = Progression.StageRecommendedLevel(c, save, stage);
                    foreach (var h in save.Roster) h.TotalXp = Formulas.TotalXpForLevel(level);
                    var setup = BattleFactory.Build(c, save, stage.Id, seed);
                    var opening = new List<BattleEvent>();
                    var s = BattleEngine.Create(c, setup, opening);
                    var rec = new BattleRecorder(s, setup, "bot:greedy", opening);
                    var bot = new HeroBot(seed);
                    int refused = 0;
                    for (int i = 0; i < 900 && !s.IsOver; i++)
                    {
                        var ev = rec.Apply(s, bot.Decide(s));
                        refused = ev.Exists(e => e is PlayerControlEvent || e is BattleEndedEvent) ? 0 : refused + 1;
                        if (refused > 5) break;
                    }
                    File.WriteAllText(Path.Combine(dir, $"{stage.Id}_{battle:000}.json"), rec.Finish(s).ToString(Newtonsoft.Json.Formatting.None));
                    TestContext.Out.WriteLine($"{battle:000} {stage.Id} [{string.Join(",", party)}]: {s.Phase} wave {s.Wave + 1}");
                    battle++;
                }
        }
    }
}
