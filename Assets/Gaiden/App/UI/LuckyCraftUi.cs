using System;
using System.Collections;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>The Forge's Diamond controls (§24.6.5, GG-T235): buy a Diamond through the paywall hook, and a lucky craft
    /// that spends one for a far better Masterwork chance, revealed over time.</summary>
    public static class LuckyCraftUi
    {
        public static void Section(VisualElement w, ContentDb c, SaveData s, RecipeDef r, bool craftable, Action refresh)
        {
            if (r.Station != CraftStation.Forge || r.ResultCount != 1 || !(c.TryItem(r.Result) is ItemDef result)
                || result.Type != ItemType.Equipment || result.Durability <= 0) return;
            var box = Ui.El("col", w);
            box.name = "lucky-craft";
            box.style.marginTop = Space.S16;
            var head = Ui.El("row", box);
            var tex = Art.ItemIcon(Crafting.DiamondId);
            if (tex != null)
            {
                var img = Ui.El(null, head);
                img.style.width = img.style.height = Size.IconS; img.style.marginRight = Space.S8;
                img.style.backgroundImage = new StyleBackground(tex);
            }
            Ui.Text($"Diamonds: {s.Count(Crafting.DiamondId)}", "small", head);

            var buy = Ui.Button($"Buy Diamond · {Paywall.Tag(PaywallKind.LuckyDiamond, s)}", () =>
                TimedJobView.Begin(now => (Paywall.Start(GameApp.I.Save, PaywallKind.LuckyDiamond, Crafting.DiamondId, now, out var job), job),
                    "Cutting a Diamond…", _ => refresh()), "btn--small", box);
            buy.name = "buy-diamond";
            buy.style.marginTop = Space.S8;
            Ui.SetEnabled(buy, s.Gold >= Paywall.Cost(PaywallKind.LuckyDiamond, s) && Paywall.Pending(s, PaywallKind.LuckyDiamond) == null);

            bool can = Crafting.CanLucky(c, s, r);
            var lucky = Ui.Button($"Lucky craft (use 1 Diamond) · ★ {Math.Round(Crafting.MasterworkChance(c, s, true) * 100)}%", () =>
            {
                var res = Forging.Forge(c, s, r, lucky: true);
                if (res.Ok) GameApp.I.Persist();
                Reveal(res, refresh);
            }, can ? "btn--primary" : null, box);
            lucky.name = "lucky-craft-btn";
            lucky.style.marginTop = Space.S8;
            Ui.SetEnabled(lucky, can && craftable);
        }

        /// <summary>The diamond spins up in a veil, then the result lands: a Masterwork with a fanfare, else a softer cue.</summary>
        private static void Reveal(Result res, Action refresh)
        {
            var app = GameApp.I;
            if (!res.Ok)
            {
                app.Toast(res.Message);
                app.Audio.Sfx("ui.error");
                refresh();
                return;
            }
            var veil = Ui.El("fill veil col center", app.Overlay);
            veil.name = "lucky-reveal";
            veil.pickingMode = PickingMode.Position;
            var gem = Ui.El(null, veil);
            gem.style.width = gem.style.height = Size.Gem;
            var tex = Art.ItemIcon(Crafting.DiamondId);
            if (tex != null) gem.style.backgroundImage = new StyleBackground(tex);
            var text = Ui.Text("", "h2", veil);
            text.style.marginTop = Space.S24;
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.opacity = 0f;
            app.StartCoroutine(Play(gem, text, veil, res, refresh));
        }

        private static IEnumerator Play(VisualElement gem, Label text, VisualElement veil, Result res, Action refresh)
        {
            var app = GameApp.I;
            app.Audio.Sfx("element.arcane");
            yield return Tween.Run(Timing.Cinematic, t =>
            {
                gem.style.rotate = new Rotate(new Angle(t * 720f));
                float k = 0.6f + 0.6f * t;
                gem.style.scale = new Scale(new Vector3(k, k, 1f));
            }, Tween.EaseInOut);
            bool masterwork = res.Message != null && res.Message.StartsWith("★");
            app.Audio.Sfx(masterwork ? "battle.powerup" : "battle.clang");
            text.text = res.Message;
            yield return Tween.All(app, Tween.Fade(text, 0f, 1f, 0.3f), Tween.Pop(gem, masterwork ? 1.4f : 1.1f, Timing.Emphasis));
            yield return Tween.Wait(Timing.Cinematic);
            yield return Tween.Fade(veil, 1f, 0f, 0.25f);
            veil.RemoveFromHierarchy();
            refresh();
        }
    }
}
