using System.Collections;
using GridGame.Core.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>Wanderers on the board (§25.11.2, GG-T342): a wandering hero pops in with a rose keyline, a WANDERER tag and
    /// its own rose turn icon, and a billboard says what it wants; each time trust grows it floats "Trust n/m", and a full
    /// gauge calls out "Trusts you!".</summary>
    public sealed partial class BattleScreen
    {
        private IEnumerator WandererAnim(WandererJoinedEvent e)
        {
            var u = State.UnitById(e.UnitId);
            if (u == null) yield break;
            var v = board.Units.TryGetValue(u.Id, out var ex) ? ex : board.Add(u);
            board.Place(v, e.At);
            v.SetHp(u.Hp, u.MaxHp);
            v.SetTeamLook(u, true);
            SyncTimeline(null);
            App.Audio.Sfx("battle.pickup", 0.8f);
            Billboard($"A wandering {u.Name} fights the monsters! Fight beside them to earn their trust.", null, Palette.Wanderer);
            v.Root.style.scale = new Scale(Vector2.zero);
            yield return Tween.Run(Timing.Standard, k => v.Root.style.scale = new Scale(Vector2.one * (k < 0.7f ? k / 0.7f * 1.15f : 1.15f - 0.15f * (k - 0.7f) / 0.3f)));
            v.Root.style.scale = new Scale(Vector2.one);
        }

        private IEnumerator TrustAnim(TrustGrewEvent e)
        {
            RefreshUnit(e.UnitId);
            App.Audio.Sfx(e.Full ? "battle.warcry" : "ui.select", e.Full ? 0.7f : 0.5f);
            string text = e.Full ? "Trusts you!" : $"Trust {e.Trust}/{e.TrustNeeded}";
            return FloatText(e.UnitId, text, Palette.Wanderer, e.Full ? 0.5f : 0.25f, e.Full ? 60 : 44);
        }
    }
}
