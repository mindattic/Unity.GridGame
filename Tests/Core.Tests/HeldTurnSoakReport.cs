using System.Collections.Generic;
using System.Linq;
using System.Text;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Meta;
using NUnit.Framework;
using static GridGame.Tests.TestKit;

namespace GridGame.Tests
{
    /// <summary>
    /// GG-T191 soak instrument for the held-turn timeline (§2.0), the Guard mana bank (§2.5, §3.1.9) and class support
    /// effects (§1.2.3): plays the campaign with bots and tabulates average turn time used, Guard frequency, mana from
    /// Guard vs pincers (and the other sources), support heal / buff / haste / cleanse totals and win rate per stage.
    /// Every tuning number it reports on lives in <see cref="BattleConfig"/>. Asserts only that nothing stalls.
    /// </summary>
    public class HeldTurnSoakReport
    {
        private sealed class Row
        {
            public int Runs, Wins, Moves, Guards, Forced;
            public float HeldSeconds, WindowSeconds;
            public readonly Dictionary<string, int> Mana = new Dictionary<string, int>();
            public int HealFromSupport, HealTotal, SupportBuffs, SupportHaste, SupportCleanse, SupportActions;
        }

        private static void Tally(Row row, BattleCommand cmd, List<BattleEvent> events, BattleState before, float window)
        {
            if (cmd is MoveCommand mv)
            {
                row.Moves++;
                row.HeldSeconds += window < 0f ? mv.HeldSeconds : System.Math.Min(mv.HeldSeconds, window);
                row.WindowSeconds += window < 0f ? mv.HeldSeconds : window;
            }
            else if (cmd is ShieldCommand) row.Guards++;
            bool inSupport = false;
            foreach (var e in events)
            {
                switch (e)
                {
                    case TurnTimeUpEvent _: row.Forced++; break;
                    case ManaChargedEvent mc: row.Mana[mc.Reason ?? "?"] = (row.Mana.TryGetValue(mc.Reason ?? "?", out var v) ? v : 0) + mc.Amount; break;
                    case SupportActionEvent sa:
                        row.SupportActions++; inSupport = true;
                        if (sa.Effect == SupportEffect.Haste) row.SupportHaste += sa.Targets.Count;
                        else if (sa.Effect == SupportEffect.Cleanse) row.SupportCleanse += sa.Targets.Count;
                        else if (sa.Effect == SupportEffect.Buff) row.SupportBuffs += sa.Targets.Count;
                        break;
                    case HealedEvent h:
                        row.HealTotal += h.Amount;
                        if (inSupport) row.HealFromSupport += h.Amount;
                        break;
                    case PincerFormedEvent _: inSupport = false; break;
                }
            }
        }

        [TestCase(0.0f, "greedy")]
        [TestCase(0.5f, "casual")]
        public void Held_turn_guard_and_support_soak(float randomness, string label)
        {
            var c = Real;
            var rows = new Dictionary<string, Row>();
            const int seeds = 3;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var s = Progression.NewGame(c, "Soak", Progression.StartingRoster);
                CampNpcs.JoinAll(s);
                int battleSeed = seed * 1000;
                foreach (var stage in c.CampaignStages())
                {
                    bool cleared = false;
                    for (int attempt = 0; attempt < 4 && !cleared; attempt++)
                    {
                        DialogueRunner.PlayPendingBeatsHeadless(c, s, stage.Id);
                        CampRoutine.Run(c, s);
                        var setup = BattleFactory.Build(c, s, stage.Id, battleSeed++);
                        if (!rows.TryGetValue(stage.Id, out var row)) rows[stage.Id] = row = new Row();
                        row.Runs++;
                        float window = -1f;
                        var st = BotRunner.Play(c, setup, new HeroBot(battleSeed, randomness), 900, (cmd, ev, state) =>
                        {
                            if (cmd != null) Tally(row, cmd, ev, null, window);
                            window = BattleEngine.TurnWindowSeconds(state); // the window of the turn the NEXT command plays
                        });
                        if (st.Phase == BattlePhase.Victory) { row.Wins++; cleared = true; }
                        BattleFactory.Commit(c, s, st);
                    }
                    if (!cleared) break;
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"=== held-turn soak, {label} bot (randomness {randomness}), {seeds} seeds ===");
            sb.AppendLine("stage                  win%  turn-used%  guard%  forced%  mana(guard/pincer/other)  heal(sup/all)  buffs haste cleanse");
            foreach (var kv in rows)
            {
                var r = kv.Value;
                int turns = System.Math.Max(1, r.Moves + r.Guards);
                float used = r.WindowSeconds > 0f ? 100f * r.HeldSeconds / r.WindowSeconds : 0f;
                int g = r.Mana.TryGetValue("guard", out var gv) ? gv : 0, p = r.Mana.TryGetValue("pincer", out var pv) ? pv : 0;
                int other = r.Mana.Where(m => m.Key != "guard" && m.Key != "pincer").Sum(m => m.Value);
                sb.AppendLine($"{kv.Key,-22} {100 * r.Wins / r.Runs,4}%  {used,9:F0}%  {100f * r.Guards / turns,5:F0}%  {100f * r.Forced / turns,6:F0}%  {g,7}/{p}/{other}  {r.HealFromSupport,6}/{r.HealTotal}  {r.SupportBuffs,5} {r.SupportHaste,5} {r.SupportCleanse,7}");
            }
            TestContext.Out.WriteLine(sb.ToString());
            Assert.That(rows, Is.Not.Empty);
        }
    }
}
