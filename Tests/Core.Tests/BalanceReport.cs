using System.Collections.Generic;
using System.Linq;
using System.Text;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// Balance instrument (bible §0.3 pacing targets): plays the campaign in order with natural progression —
    /// XP, carried wounds, inn rests when affordable, retries on defeat — for bots of different skill, and
    /// reports win rate, actions per battle and HP left. Asserts only the guard rails (no stalls; a strong
    /// bot can finish the campaign); the table is the tuning dashboard.
    /// </summary>
    public class BalanceReport
    {

        private sealed class Row { public int Runs, Wins, Actions, HpPct; }

        private Dictionary<string, Row> Campaign(float randomness, int seed, int maxAttemptsPerStage, out bool finished, bool manageCamp = false) =>
            Campaign(randomness, seed, maxAttemptsPerStage, out finished, out _, manageCamp, Progression.StartingRoster, true);

        /// <param name="story">Play each stage's pending story beats first (headless, first choice) so the story
        /// recruits join the small starting party the way they do in the game (GG-T118).</param>
        private Dictionary<string, Row> Campaign(float randomness, int seed, int maxAttemptsPerStage, out bool finished, out SaveData save, bool manageCamp, IReadOnlyList<string> startingParty, bool story)
        {
            var c = Real;
            var s = save = Progression.NewGame(c, "Sim", startingParty);
            // GG-T119: Camp NPCs join at story beats. A no-story row is the pre-story yardstick, so it has every
            // service from the start, as every save did before; story rows meet them through the headless beats.
            if (!story) CampNpcs.JoinAll(s);
            var rows = new Dictionary<string, Row>();
            var stages = c.CampaignStages();
            finished = true;
            int battleSeed = seed * 1000;
            foreach (var stage in stages)
            {
                bool cleared = false;
                for (int attempt = 0; attempt < maxAttemptsPerStage && !cleared; attempt++)
                {
                    if (story) DialogueRunner.PlayPendingBeatsHeadless(c, s, stage.Id);
                    if (manageCamp) CampRoutine.Run(c, s);
                    else if (CampNpcs.Joined(s, CampNpcs.Innkeeper) && Inn.Cost(c, s) > 0 && Inn.Cost(c, s) <= s.Gold) Inn.Rest(c, s);
                    var setup = BattleFactory.Build(c, s, stage.Id, battleSeed++);
                    var st = BotRunner.Play(c, setup, new HeroBot(battleSeed, randomness), 900); // the first battle is a 20-wave gauntlet
                    if (!rows.TryGetValue(stage.Id, out var row)) rows[stage.Id] = row = new Row();
                    row.Runs++;
                    row.Actions += st.Results.ActionsTaken;
                    var heroes = st.Units.Where(u => u.IsHero).ToList();
                    row.HpPct += (int)(100f * heroes.Sum(u => u.Alive ? u.Hp : 0) / heroes.Sum(u => u.MaxHp));
                    if (st.Phase == BattlePhase.Victory) { row.Wins++; cleared = true; }
                    BattleFactory.Commit(c, s, st);
                }
                if (!cleared) { finished = false; break; }
            }
            // GG-T119: every Camp NPC is met on the road, early enough that a finished run has them all.
            if (finished)
                Assert.That(CampNpcs.All.Where(n => !CampNpcs.Joined(s, n)), Is.Empty, "a finished campaign should have met every Camp NPC");
            return rows;
        }
        /// <summary>The pre-GG-T118 starting five (the default trio plus the two story recruits' preferred classes) —
        /// the baseline the small-start rows are balanced against.</summary>
        private static readonly string[] FiveStart = Progression.StartingRoster.Concat(new[] { "redmage", "engineer" }).ToArray();

        /// <summary>Every real new game starts small (GG-T118) and grows at story beats, so the default rows play
        /// the trio with story recruits. "no-story" keeps the trio short (GG-T87's trio-start row) so the camp
        /// routine's Summon Circle step always has an open slot to fill; "five-start" is the old full-party start,
        /// kept as the yardstick for the early-stage rebalance.</summary>
        [TestCase(0.0f, "greedy", false, 3, true)]
        [TestCase(0.5f, "casual", false, 3, true)]
        [TestCase(0.85f, "button-masher", false, 3, true)]
        [TestCase(0.0f, "greedy+camp", true, 3, true)]
        [TestCase(0.5f, "casual+camp", true, 3, true)]
        [TestCase(0.0f, "greedy+camp trio no-story", true, 3, false)]
        [TestCase(0.0f, "greedy five-start (pre-GG-T118)", false, 5, false)]
        public void Campaign_pacing_table(float randomness, string label, bool manageCamp, int startingHeroes, bool story)
        {
            var total = new Dictionary<string, Row>();
            int finishedRuns = 0, recruits = 0, bountiesClaimed = 0, bountySp = 0, roster = 0;
            const int seeds = 4;
            var party = FiveStart.Take(startingHeroes).ToList();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var rows = Campaign(randomness, seed, 4, out bool finished, out var save, manageCamp, party, story);
                recruits += save.Recruits;
                roster += save.Roster.Count;
                bountiesClaimed += save.Bounty.Claimed.Count;
                bountySp += Bounties.SkillPointsClaimed(Real, save); // GG-T134: skill points paid each party hero
                if (finished) finishedRuns++;
                foreach (var kv in rows)
                {
                    if (!total.TryGetValue(kv.Key, out var t)) total[kv.Key] = t = new Row();
                    t.Runs += kv.Value.Runs; t.Wins += kv.Value.Wins; t.Actions += kv.Value.Actions; t.HpPct += kv.Value.HpPct;
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"=== {label} bot (randomness {randomness}) — campaign finished {finishedRuns}/{seeds} — summon recruits {recruits}, roster at the end {roster / (float)seeds:F1}, bounties claimed {bountiesClaimed} (+{bountySp / (float)seeds:F1} SP each per run) ===");
            sb.AppendLine("stage                  win%   actions   hp-left%");
            foreach (var kv in total)
                sb.AppendLine($"{kv.Key,-22} {100 * kv.Value.Wins / kv.Value.Runs,4}%   {kv.Value.Actions / (float)kv.Value.Runs,7:F1}   {kv.Value.HpPct / kv.Value.Runs,6}%");
            TestContext.Out.WriteLine(sb.ToString());
            if (randomness == 0f) Assert.That(finishedRuns >= 1, Is.True, "a greedy bot should be able to finish the campaign");
            // GG-T87: the camp routine must actually drive the Bounty Board and (given an open slot) the
            // Summon Circle as part of organic progression, not just leave them untouched.
            // (Only a run that finishes is guaranteed to pass a contract's last target — a bot that stalls early may not.)
            if (manageCamp && finishedRuns > 0) Assert.That(bountiesClaimed > 0, Is.True, "the camp routine never claimed a bounty over a played-out campaign");
            if (manageCamp && !story && startingHeroes < Progression.MaxPartySize) Assert.That(recruits > 0, Is.True, "the camp routine never recruited into an open party slot");
        }
    }
}
