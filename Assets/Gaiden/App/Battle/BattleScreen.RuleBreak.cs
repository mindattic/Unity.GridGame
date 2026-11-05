using System.Collections;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;

namespace GridGame.App
{
    public sealed partial class BattleScreen
    {
        private static readonly Color RuleBreakColor = new Color(1f, 0.3f, 0.75f);

        /// <summary>A finale boss breaks a rule (§14.9, GG-T234): its entrance names the rule with a war cry and a shake;
        /// each bite after that plays its own cue and float over the boss.</summary>
        private IEnumerator RuleBreakAnim(RuleBrokenEvent rb)
        {
            ShowBanner(rb.Text);
            if (rb.Arrived)
            {
                App.Audio.Sfx("warcry");
                App.StartCoroutine(Tween.Shake(shake, 24f * Fx, 0.5f));
                yield return FloatText(rb.UnitId, "RULE BREAKER", RuleBreakColor, 1.1f, 84);
                yield break;
            }
            switch (rb.Rule)
            {
                case RuleBreak.TwinTurn: App.Audio.Sfx("dash"); break;
                case RuleBreak.AxisWard: App.Audio.Sfx(rb.Text.Contains("shatters") ? "shatter" : "clang"); break;
                case RuleBreak.MireSwap: App.Audio.Sfx("whoosh"); break;
                case RuleBreak.GravityStamp: App.Audio.Sfx("explosion"); App.StartCoroutine(Tween.Shake(shake, 30f * Fx, 0.45f)); break;
                case RuleBreak.Undying: App.Audio.Sfx("powerup"); RefreshUnit(rb.UnitId); break;
            }
            yield return FloatText(rb.UnitId, RuleBreaks.Label(rb.Rule), RuleBreakColor, 0.6f, 72);
        }
    }
}
