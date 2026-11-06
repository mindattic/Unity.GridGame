using System.Collections;
using GridGame.Core.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>Taming on the board (§25.11.3, GG-T303): a tamed monster turns chartreuse (keyline, TAMED tag and its own
    /// timeline icon in <see cref="Palette.Tamed"/>) with a "Tamed!" float; each of its own turns floats its bond; a full
    /// bond calls out "Bonded!"; when the tame wears off it flashes back to the foe's red.</summary>
    public sealed partial class BattleScreen
    {
        private IEnumerator TamedAnim(TamedEvent e)
        {
            var u = State.UnitById(e.UnitId);
            if (u == null || !board.Units.TryGetValue(u.Id, out var v)) yield break;
            v.SetTeamLook(u, true);
            SyncTimeline(null);
            App.Audio.Sfx("battle.powerup", 0.8f);
            Billboard($"{u.Name} is tamed! It fights for you on its own for {e.Turns} of its turns ({Mathf.RoundToInt(e.Chance * 100)}% chance).", null, Palette.Tamed);
            App.StartCoroutine(FloatText(u.Id, "Tamed!", Palette.Tamed, 0f, 64));
            yield return Tween.Run(Timing.Standard, k => v.Root.style.scale = new Scale(Vector2.one * (k < 0.6f ? 1f + k / 0.6f * 0.18f : 1.18f - 0.18f * (k - 0.6f) / 0.4f)));
            v.Root.style.scale = new Scale(Vector2.one);
        }

        private IEnumerator TameFailedAnim(TameFailedEvent e)
        {
            App.Audio.Sfx("battle.miss");
            return FloatText(e.UnitId, $"Resisted ({Mathf.RoundToInt(e.Chance * 100)}%)", new Color(1f, 0.6f, 0.4f), 0.4f, 52);
        }

        private IEnumerator BondAnim(BondGrewEvent e)
        {
            RefreshUnit(e.UnitId);
            App.Audio.Sfx(e.Full ? "battle.warcry" : "ui.select", e.Full ? 0.7f : 0.5f);
            string text = e.Full ? "Bonded!" : $"Bond {e.Bond}/{e.BondNeeded}";
            return FloatText(e.UnitId, text, Palette.Tamed, e.Full ? 0.5f : 0.25f, e.Full ? 60 : 44);
        }

        private IEnumerator TameEndedAnim(TameEndedEvent e)
        {
            var u = State.UnitById(e.UnitId);
            if (u == null || !board.Units.TryGetValue(u.Id, out var v)) yield break;
            v.SetTeamLook(u, false);
            SyncTimeline(null);
            App.Audio.Sfx("status.debuff", 0.8f);
            yield return FloatText(u.Id, "Breaks free!", Palette.Enemy, 0.4f, 56);
        }
    }
}
