using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using NUnit.Framework;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T321: a measurement soak for the re-entry timeline (GG-T318), not a gate. With GAIDEN_TIMELINE_SOAK=&lt;out.md&gt;
    /// it plays the first 15 campaign stages with the smart and casual bots and reports, per stage: wins, the gap between
    /// consecutive enemy turns (median, share under a minimum drag), the forced-Guard share of hero actions and the
    /// actions per window. Unset, it does nothing.
    /// Knobs: GAIDEN_DIFFICULTY (1..10, default 10); GAIDEN_SOAK_PARTY = campaign (default: the party the campaign has at
    /// that stage, starting with the real three) | six; GAIDEN_SOAK_PER_STAGE (default 4); GAIDEN_MINGAP / GAIDEN_MAXSPREAD
    /// / GAIDEN_JITTER override that difficulty row's spacing; GAIDEN_TUNE="EntryBaseSeconds=4;RecoverBaseSeconds=1"
    /// overrides BattleConfig statics.
    /// </summary>
    public class TimelineTuningSoak
    {
        private sealed class Tally
        {
            public int Battles, Wins, HeroActions, ForcedGuards, Guards, Windows;
            public readonly List<int> Gaps = new List<int>();
        }

        private static float? EnvF(string name) =>
            float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (float?)null;

        [Test]
        public void Timeline_tuning_soak()
        {
            var outPath = Environment.GetEnvironmentVariable("GAIDEN_TIMELINE_SOAK");
            if (string.IsNullOrEmpty(outPath)) { TestContext.Out.WriteLine("GAIDEN_TIMELINE_SOAK not set — nothing run"); return; }

            int difficulty = BotDifficulty.FromEnv();
            string partyMode = Environment.GetEnvironmentVariable("GAIDEN_SOAK_PARTY") ?? "campaign";
            int perStage = int.TryParse(Environment.GetEnvironmentVariable("GAIDEN_SOAK_PER_STAGE"), out var ps) ? ps : 4;
            var row = DifficultyTable.At(difficulty);
            if (EnvF("GAIDEN_MINGAP") is float mg) row.MinGapSeconds = mg;
            if (EnvF("GAIDEN_MAXSPREAD") is float sp) row.MaxSpreadSeconds = sp;
            if (EnvF("GAIDEN_JITTER") is float jt) row.JitterSeconds = jt;
            foreach (var kv in (Environment.GetEnvironmentVariable("GAIDEN_TUNE") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=');
                var f = typeof(BattleConfig).GetField(parts[0].Trim());
                Assert.That(f != null && !f.IsLiteral && !f.IsInitOnly, $"GAIDEN_TUNE: {parts[0]} is not a tunable BattleConfig field");
                f.SetValue(null, Convert.ChangeType(float.Parse(parts[1], CultureInfo.InvariantCulture), f.FieldType, CultureInfo.InvariantCulture));
            }

            var c = TestKit.Real;
            var heroes = c.Classes.Values.Where(k => k.Team == Team.Hero).Select(k => k.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var stages = c.CampaignStages().Take(15).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"# Timeline soak — difficulty {difficulty}, party {partyMode}, {perStage}/stage");
            sb.AppendLine($"MinGap {row.MinGapSeconds}s, MaxSpread {row.MaxSpreadSeconds}s, Jitter {row.JitterSeconds}s, EntryBase {BattleConfig.EntryBaseSeconds}s, RecoverBase {BattleConfig.RecoverBaseSeconds}s, BarSeconds {BattleConfig.BarSeconds}s");
            sb.AppendLine();
            sb.AppendLine("| stage | party | bot | wins | median gap s | gaps <0.85s | forced Guard share | actions/window |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var totals = new Dictionary<string, Tally> { ["smart"] = new Tally(), ["casual"] = new Tally() };
            int battle = 0;
            for (int si = 0; si < stages.Count; si++)
            {
                var stage = stages[si];
                foreach (var botName in new[] { "smart", "casual" })
                {
                    var t = new Tally();
                    int partySize = 0;
                    for (int k = 0; k < perStage; k++, battle++)
                    {
                        int start = (si * perStage + k) * 5;
                        int size = partyMode == "six" ? Progression.MaxPartySize : AbilityUsageSoak.CampaignPartySize(c, stage);
                        var party = Progression.StartingRoster.ToList();
                        for (int i = 0; party.Count < size; i++)
                            if (!party.Contains(heroes[(start + i) % heroes.Count])) party.Add(heroes[(start + i) % heroes.Count]);
                        partySize = party.Count;
                        int seed = 5000 + si * 100 + k;
                        var save = Progression.NewGame(c, "Soak", party);
                        int level = Math.Max(1, Progression.StageRecommendedLevel(c, save, stage));
                        foreach (var h in save.Roster) h.TotalXp = Formulas.TotalXpForLevel(level);
                        var s = BattleEngine.Create(c, BattleFactory.Build(c, save, stage.Id, seed, difficulty), new List<BattleEvent>());
                        var bot = new HeroBot(seed, botName == "casual" ? 0.5f : 0f);
                        Play(s, bot, t);
                        t.Battles++;
                        if (s.Phase == BattlePhase.Victory) t.Wins++;
                    }
                    Merge(totals[botName], t);
                    sb.AppendLine(Line(stage.Id, partySize.ToString(), botName, t));
                }
            }
            sb.AppendLine();
            foreach (var kv in totals) sb.AppendLine(Line("**all**", "", kv.Key, kv.Value));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, sb.ToString());
            TestContext.Out.WriteLine(sb.ToString());
        }

        private static void Play(BattleState s, HeroBot bot, Tally t)
        {
            long clock = 0, lastTurn = -1;
            int refused = 0;
            for (int i = 0; i < 3000 && !s.IsOver; i++)
            {
                var cmd = bot.Decide(s);
                bool forced = cmd is ShieldCommand && BattleEngine.TooLittleTime(s);
                var ev = BattleEngine.Apply(s, cmd);
                bool accepted = !ev.Exists(e => e is AbilityRefusedEvent);
                if (accepted)
                {
                    if (cmd is ShieldCommand) { t.Guards++; if (forced) t.ForcedGuards++; }
                    else t.HeroActions++;
                }
                foreach (var e in ev)
                {
                    if (e is TimelineAdvancedEvent ta) clock += ta.Ticks;
                    else if (e is WaveStartedEvent) lastTurn = -1;
                    else if (e is PlayerControlEvent pc && pc.NewWindow) t.Windows++;
                    else if (e is TurnStartedEvent ts && s.UnitById(ts.UnitId) is Unit u && u.Team == Team.Enemy && !u.IsIndependent)
                    {
                        if (lastTurn >= 0) t.Gaps.Add((int)(clock - lastTurn));
                        lastTurn = clock;
                    }
                }
                refused = ev.Exists(e => e is PlayerControlEvent || e is BattleEndedEvent) ? 0 : refused + 1;
                if (refused > 5) break;
            }
        }

        private static void Merge(Tally into, Tally t)
        {
            into.Battles += t.Battles; into.Wins += t.Wins; into.HeroActions += t.HeroActions; into.ForcedGuards += t.ForcedGuards;
            into.Guards += t.Guards; into.Windows += t.Windows; into.Gaps.AddRange(t.Gaps);
        }

        private static string Line(string stage, string party, string bot, Tally t)
        {
            var g = t.Gaps.OrderBy(x => x).ToList();
            string median = g.Count == 0 ? "-" : (g[g.Count / 2] / 10f).ToString("0.0", CultureInfo.InvariantCulture);
            int need = BattleEngine.NeedTicks(BattleEngine.MinDragSeconds);
            string clusters = g.Count == 0 ? "-" : Pct(g.Count(x => x < need), g.Count);
            string forced = Pct(t.ForcedGuards, t.HeroActions + t.ForcedGuards);
            string apw = t.Windows == 0 ? "-" : (t.HeroActions / (float)t.Windows).ToString("0.00", CultureInfo.InvariantCulture);
            return $"| {stage} | {party} | {bot} | {t.Wins}/{t.Battles} ({Pct(t.Wins, t.Battles)}) | {median} | {clusters} | {forced} | {apw} |";
        }

        private static string Pct(int n, int d) => d == 0 ? "-" : ((int)Math.Round(100.0 * n / d)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
