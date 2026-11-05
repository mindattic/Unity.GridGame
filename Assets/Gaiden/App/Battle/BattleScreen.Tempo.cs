using System.Collections;
using GridGame.Core.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>Party tempo readout (GG-T268, BIBLE §2.11): "Tempo +2" under the timeline, the track tinted cool
    /// while foes are slowed and warm while the party is slowed.</summary>
    public sealed partial class BattleScreen
    {
        private Label tempoLabel;
        private int shownTempo;

        private void EnsureTempoLabel()
        {
            if (tempoLabel != null && tempoLabel.parent != null) return;
            tempoLabel = Ui.Text("", "tiny shadow", timelineFrame);
            tempoLabel.name = "timeline-tempo";
            tempoLabel.pickingMode = PickingMode.Ignore;
            tempoLabel.style.position = Position.Absolute;
            tempoLabel.style.left = Space.S0;
            tempoLabel.style.top = -Space.S24;
            Fonts.UseHeavy(tempoLabel);
        }

        private static Color TempoColor(int net) => net > 0 ? Palette.Haste : net < 0 ? Palette.Bad : Palette.Text;

        /// <summary>Snap the readout and tint to the state without animating (screen build, resync).</summary>
        private void SyncTempo()
        {
            if (timelineFrame == null || State == null) return;
            EnsureTempoLabel();
            shownTempo = State.NetTempo;
            ApplyTempoLook(shownTempo, 1f);
        }

        private void ApplyTempoLook(int net, float k)
        {
            tempoLabel.text = net == 0 ? "" : net > 0 ? $"Tempo +{net}" : $"Tempo −{-net}";
            tempoLabel.style.color = TempoColor(net);
            tempoLabel.style.opacity = net == 0 ? 0f : k;
            var baseTrack = Palette.Text.WithA(0.85f);
            if (timelineBar != null) timelineBar.style.backgroundColor = net == 0 ? baseTrack : Color.Lerp(baseTrack, TempoColor(net), 0.55f * k);
        }

        private IEnumerator TempoAnim(TempoChangedEvent t)
        {
            EnsureTempoLabel();
            int from = shownTempo;
            shownTempo = t.Net;
            if (t.Net == from) yield break;
            bool up = t.Net > from;
            App.Audio.Sfx(up ? "battle.powerup" : "status.debuff", 0.7f, up ? 1.25f : 0.8f);
            Feed(t.Net > 0 ? $"Tempo +{t.Net}: foes slowed (×{t.Multiplier:0.00})"
                 : t.Net < 0 ? $"Tempo −{-t.Net}: foes hastened (×{t.Multiplier:0.00})"
                 : "Tempo back to normal", TempoColor(t.Net));
            tempoLabel.style.scale = new Scale(Vector2.one * 1.3f);
            yield return Tween.Run(Timing.Standard, k =>
            {
                ApplyTempoLook(t.Net, t.Net == 0 ? 1f - k : k);
                tempoLabel.style.scale = new Scale(Vector2.one * Mathf.Lerp(1.3f, 1f, k));
            }, Tween.EaseOut);
            ApplyTempoLook(t.Net, 1f);
        }
    }
}
