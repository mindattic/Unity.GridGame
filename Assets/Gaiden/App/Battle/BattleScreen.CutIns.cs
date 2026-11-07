using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Action cut-ins (owner direction 2026-10-11: "portrait slides … should be action packed just like the board that
    /// twists and zooms, and have supporters pop in and out to show their participation bonus"). Every portrait arrives
    /// on a slanted team-colour band with speed lines, twisting in from an oversized tilt with a flash and an impact
    /// shake, while the board camera punches toward the action and whips back.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private static Color BandColor(Team team) => team == Team.Hero ? new Color(0.16f, 0.36f, 0.95f) : new Color(0.85f, 0.15f, 0.2f);

        private static Texture2D bandTex;

        /// <summary>A horizontal stripe that is solid in the middle and feathers to nothing at both ends (tinted per use).</summary>
        private static Texture2D BandTex
        {
            get
            {
                if (bandTex != null) return bandTex;
                bandTex = new Texture2D(128, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "cutin-band" };
                for (int x = 0; x < 128; x++)
                {
                    float u = x / 127f, a = Ease.InOut(Mathf.Clamp01(Mathf.Min(u, 1f - u) * 3.2f));
                    for (int y = 0; y < 8; y++) bandTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                bandTex.Apply();
                return bandTex;
            }
        }

        /// <summary>
        /// A cut-in card: a feathered team-colour band with a bright core, light streaks that race across it, and the
        /// hero's bust framed in a tilted window (head and shoulders, not the whole figure), with an outlined tag.
        /// </summary>
        private VisualElement ActionCard(Unit u, float h, Team team, string tag, Color tagColor, out VisualElement portrait)
        {
            float w = h * 0.9f;
            float tilt = team == Team.Hero ? -8f : 8f;
            var c = BandColor(team);
            var root = new VisualElement { name = "cutin-card-" + u.Id, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.width = w; root.style.height = h;
            VisualElement Stripe(float top, float height, Color tint, float extra)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.style.position = Position.Absolute;
                e.style.left = -w * extra; e.style.right = -w * extra; e.style.top = top; e.style.height = height;
                e.style.backgroundImage = new StyleBackground(BandTex);
                e.style.unityBackgroundImageTintColor = tint;
                e.style.rotate = new Rotate(tilt);
                root.Add(e);
                return e;
            }
            Stripe(h * 0.22f, h * 0.56f, new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.6f, 0.75f), 0.9f); // dark under-band
            Stripe(h * 0.3f, h * 0.4f, new Color(c.r, c.g, c.b, 0.92f), 0.7f);                         // the band
            Stripe(h * 0.48f, h * 0.035f, Palette.White.WithA(0.95f), 0.6f);                         // bright core
            for (int i = 0; i < 7; i++)
            {
                var streak = Stripe(h * Random.Range(0.28f, 0.7f), Random.Range(3f, 8f), new Color(1f, 1f, 1f, Random.Range(0.4f, 0.85f)), 0f);
                streak.name = "streak";
                streak.style.left = StyleKeyword.Auto; streak.style.right = StyleKeyword.Auto;
                streak.style.width = Random.Range(w * 0.3f, w * 0.8f);
                streak.style.left = Random.Range(-w * 0.6f, w * 0.8f);
                streak.userData = Random.Range(900f, 1700f) * (team == Team.Hero ? 1f : -1f); // px/s it races across
            }
            // The bust window: a tilted frame that crops the portrait to head and shoulders.
            var window = new VisualElement { name = "bust", pickingMode = PickingMode.Ignore };
            window.style.position = Position.Absolute;
            window.style.left = w * 0.14f; window.style.right = w * 0.14f; window.style.top = h * 0.08f; window.style.bottom = h * 0.12f;
            window.style.overflow = Overflow.Hidden;
            window.style.rotate = new Rotate(tilt);
            window.style.borderTopWidth = window.style.borderBottomWidth = window.style.borderLeftWidth = window.style.borderRightWidth = 6;
            window.style.borderTopColor = window.style.borderBottomColor = window.style.borderLeftColor = window.style.borderRightColor = Palette.White.WithA(0.95f);
            window.style.borderTopLeftRadius = window.style.borderTopRightRadius = window.style.borderBottomLeftRadius = window.style.borderBottomRightRadius = 18;
            window.style.backgroundColor = new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.35f, 0.85f);
            portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            portrait.style.position = Position.Absolute;
            portrait.style.left = -w * 0.25f; portrait.style.right = -w * 0.25f; portrait.style.top = -h * 0.04f; portrait.style.height = h * 2.0f;
            portrait.style.backgroundImage = new StyleBackground(Art.PortraitFigure(u.Def.Id));
            portrait.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            var crop = u.Def.Crop;
            portrait.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, Length.Percent(crop?.X ?? 50f));
            portrait.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Length.Percent(crop?.Y ?? 0f));
            portrait.style.rotate = new Rotate(-tilt); // keep the hero upright inside the tilted window
            portrait.style.unityBackgroundImageTintColor = team == Team.Hero ? Color.white : new Color(1f, 0.88f, 0.88f);
            window.Add(portrait);
            root.Add(window);
            if (!string.IsNullOrEmpty(tag))
            {
                var t = new Label(tag) { pickingMode = PickingMode.Ignore };
                t.style.position = Position.Absolute;
                t.style.left = -w * 0.1f; t.style.right = -w * 0.1f; t.style.bottom = -h * 0.02f;
                t.style.unityTextAlign = TextAnchor.MiddleCenter;
                t.style.fontSize = Mathf.Clamp(h * 0.13f, 34f, 76f);
                Fonts.UseHeavy(t);
                t.style.color = tagColor;
                t.style.unityTextOutlineWidth = 3f;
                t.style.unityTextOutlineColor = new Color(0.03f, 0.03f, 0.1f, 1f);
                t.style.textShadow = new TextShadow { offset = new Vector2(4, 4), blurRadius = 2, color = new Color(0, 0, 0, 0.7f) };
                t.style.rotate = new Rotate(tilt * 0.7f);
                root.Add(t);
            }
            root.schedule.Execute(() =>
            {
                float dt = Tween.Dt;
                foreach (var st in root.Query<VisualElement>("streak").ToList())
                {
                    float x = st.style.left.value.value + (float)st.userData * dt * Tween.Speed;
                    if (x > w * 1.4f) x = -w * 1.2f; else if (x < -w * 1.2f) x = w * 1.4f;
                    st.style.left = x;
                }
            }).Every(16);
            // Inside the board, under the pincer outline (GG-T65/GG-T128): the card rides the camera with the board
            // and never covers the outline.
            cutInLayer.Add(root);
            return root;
        }

        /// <summary>
        /// Dim the stage behind a cut-in so the eye goes to it; returns the veil to fade out. The veil sits in the
        /// board's cut-in layer, under the card (oversized, so a camera twist never bares a corner); a second sheet
        /// at the end of the HUD dims the ability bar, orbs and actor panel, which draw after the board.
        /// </summary>
        private VisualElement CutInVeil(float alpha)
        {
            VisualElement Sheet(VisualElement parent, float top, float pad)
            {
                var e = new VisualElement { name = "cutin-veil", pickingMode = PickingMode.Ignore };
                e.style.position = Position.Absolute;
                e.style.left = e.style.right = e.style.bottom = -pad; e.style.top = top - pad;
                e.style.backgroundColor = new Color(0.01f, 0.01f, 0.05f, 1f);
                e.style.opacity = 0f;
                parent.Add(e);
                return e;
            }
            var veil = Sheet(cutInLayer, 0f, 400f);
            veil.userData = Sheet(shake, BoardTop + BoardView.Tile * BattleConfig.Rows, 0f);
            App.StartCoroutine(Tween.Run(Timing.Instant, k => SetVeil(veil, alpha * k)));
            return veil;
        }

        private static void SetVeil(VisualElement veil, float a)
        {
            veil.style.opacity = a;
            if (veil.userData is VisualElement lower) lower.style.opacity = a;
        }

        private IEnumerator LiftVeil(VisualElement veil)
        {
            float a0 = veil.resolvedStyle.opacity;
            yield return Tween.Run(Timing.Quick, k => SetVeil(veil, a0 * (1f - k)));
            veil.RemoveFromHierarchy();
            (veil.userData as VisualElement)?.RemoveFromHierarchy();
        }

        /// <summary>Twist-zoom in (oversized, tilted, flashing white) → snap with a shake → hold → whip out.</summary>
        private IEnumerator TwistCard(VisualElement card, VisualElement portrait, Vector2 at, float hold, float spin, Vector2 exitDir)
        {
            App.Audio.DuckFor(Timing.Quick + hold + Timing.Dramatic); // POL-F3: the music steps back for the card
            float w = card.style.width.value.value, h = card.style.height.value.value;
            void Put(Vector2 p) { card.style.left = p.x - w / 2f; card.style.top = p.y - h / 2f; }
            Put(at);
            yield return Tween.Run(Timing.Quick, k =>
            {
                float e = Tween.Back(k);
                card.style.scale = new Scale(Vector2.one * Mathf.LerpUnclamped(1.9f, 1f, e));
                card.style.rotate = new Rotate(Mathf.LerpUnclamped(spin, 0f, e));
                card.style.opacity = Mathf.Min(1f, k * 3f);
                portrait.style.unityBackgroundImageTintColor = Color.Lerp(new Color(3f, 3f, 3f), Color.white, k);
            });
            App.StartCoroutine(Tween.Shake(card, 16f * Fx, Timing.Quick));
            float t = 0f;
            yield return Tween.Run(hold, k => { t = k; card.style.scale = new Scale(Vector2.one * (1f + 0.04f * k)); });
            yield return Tween.Run(Timing.Quick, k =>
            {
                Put(at + exitDir * 900f * k * k);
                card.style.rotate = new Rotate(-spin * 0.4f * k);
                card.style.opacity = 1f - k;
            });
            card.RemoveFromHierarchy();
        }

        /// <summary>Camera punch: the board zooms and twists toward a stage point, then the settle eases it back.</summary>
        private IEnumerator CameraPunch(Vector2 stagePoint, float zoom, float twist, float seconds)
        {
            if (ReduceMotion) yield break;
            var cam = boardHost;
            var origin = new Vector2(BoardLeft, BoardTop);
            cam.style.transformOrigin = new TransformOrigin(stagePoint.x - origin.x, stagePoint.y - origin.y);
            yield return Tween.Run(seconds * 0.35f, k =>
            {
                float e = Tween.Back(k);
                cam.style.scale = new Scale(Vector2.one * Mathf.LerpUnclamped(1f, zoom, e));
                cam.style.rotate = new Rotate(Mathf.LerpUnclamped(0f, twist, e));
            });
            yield return Tween.Wait(seconds * 0.4f);
            yield return SettleCamera(cam);
        }

        /// <summary>A spell's cast splash: the caster smashes in on its band, the spell name on the tag.</summary>
        private IEnumerator CasterSlide(Unit caster, string spellName = null)
        {
            CutInsShown++;
            bool fromLeft = caster.IsHero;
            var card = ActionCard(caster, 560f, caster.Team, spellName, Palette.GoldLight, out var portrait);
            var at = new Vector2(fromLeft ? 300f : GameApp.StageWidth - 300f, BoardTop + 520f);
            var focus = new Vector2(BoardLeft, BoardTop) + ViewCenter(caster);
            App.Audio.Sfx("battle.dash", 0.8f, 0.85f);
            var veil = CutInVeil(0.38f);
            card.BringToFront();
            App.StartCoroutine(CameraPunch(focus, 1.07f, fromLeft ? -2.2f : 2.2f, 0.6f));
            yield return TwistCard(card, portrait, at, 0.3f, fromLeft ? -24f : 24f, new Vector2(fromLeft ? -1f : 1f, -0.25f));
            yield return LiftVeil(veil);
        }

        /// <summary>Debug/automation: the spell cut-in on the first hero, then every other hero popping in as a supporter.</summary>
        public void DemoCaster()
        {
            var heroes = State.Units.Where(u => u.IsHero && u.Alive).ToList();
            if (heroes.Count == 0) return;
            App.StartCoroutine(DemoCasterRun(heroes));
        }

        private IEnumerator DemoCasterRun(List<Unit> heroes)
        {
            yield return CasterSlide(heroes[0], "Fireball");
            supportersShown.Clear();
            foreach (var h in heroes.Skip(1)) { SupporterPop(h, Team.Hero); yield return Tween.Wait(Timing.Quick); }
        }

        /// <summary>Chained links: both attackers twist-pop beside their tiles with the chain count.</summary>
        private IEnumerator PopUpCards(Unit[] attackers, Team team)
        {
            CutInsShown++;
            App.Audio.Sfx("battle.dash", 0.8f, 1.15f);
            var mid = attackers.Select(ViewCenter).Aggregate(Vector2.zero, (x, y) => x + y) / attackers.Length + new Vector2(BoardLeft, BoardTop);
            App.StartCoroutine(CameraPunch(mid, 1.05f, Random.value < 0.5f ? -1.8f : 1.8f, 0.5f));
            var runs = new List<IEnumerator>();
            for (int i = 0; i < attackers.Length; i++)
            {
                var u = attackers[i];
                var c = ViewCenter(u) + new Vector2(BoardLeft, BoardTop);
                var card = ActionCard(u, 400f, team, chainCount > 1 ? $"CHAIN ×{chainCount}" : null, Palette.Gold, out var portrait);
                var at = new Vector2(Mathf.Clamp(c.x, 150f, GameApp.StageWidth - 150f), Mathf.Max(BoardTop + 230f, c.y - 260f));
                float spin = (i == 0 ? -1f : 1f) * 20f;
                runs.Add(TwistCard(card, portrait, at, 0.2f, spin, new Vector2(i == 0 ? -1f : 1f, 0.3f)));
            }
            yield return Tween.All(App, runs.ToArray());
        }

        // ── Supporters pop in as they strike, tagged with their contribution ──
        private readonly HashSet<int> supportersShown = new HashSet<int>();

        /// <param name="tag">The supporter's class support action (GG-T187), e.g. "HEAL", "GUARD" — "SUPPORT" if unknown.</param>
        private void SupporterPop(Unit sup, Team team, string tag = "SUPPORT")
        {
            if (!CutInsEnabled || ReduceMotion || !supportersShown.Add(sup.Id)) return;
            int index = supportersShown.Count - 1;
            var card = ActionCard(sup, 300f, team, tag, new Color(0.6f, 0.92f, 1f), out var portrait);
            bool left = index % 2 == 0;
            var at = new Vector2(left ? 170f : GameApp.StageWidth - 170f, BoardTop + 330f + 300f * (index / 2));
            App.Audio.Sfx("battle.pickup", 0.6f, 1.3f + 0.1f * index);
            App.StartCoroutine(TwistCard(card, portrait, at, 0.3f, left ? -18f : 18f, new Vector2(0f, 1f)));
        }

        // ── Mana charge (§3.1.9): the forming bubble and the arithmetic, shown as it happens ──
        private int shownCharge;

        private IEnumerator ChargeAnim(ManaChargedEvent mc)
        {
            int from = shownCharge;
            int to = mc.Total;
            bool rolled = mc.Amount + from >= BattleConfig.ManaChargePerOrb;
            if (mc.Reason == "pincer")
            {
                string why = mc.Participants > 2 ? $" · {mc.Participants} heroes ÷{1f + BattleConfig.ChargeParticipantPenalty * (mc.Participants - 2):0.#}" : " · clean pincer";
                if (mc.Chain > 1) why += $" · chain ×{1f + BattleConfig.ChargeChainBonus * (mc.Chain - 1):0.##}";
                Billboard($"Mana +{mc.Amount}{why}", null, new Color(0.55f, 0.85f, 1f));
            }
            else if (mc.Reason == "guard") // GG-T189: the unused turn time Guard gave up, banked into the bubble
                Billboard($"Guard · Mana +{mc.Amount} · {mc.Amount / BattleConfig.GuardChargePerSecond:0.#}s banked", null, new Color(0.55f, 0.85f, 1f));
            App.Audio.Sfx("battle.orb", 0.35f, 0.7f + 0.5f * (to / (float)BattleConfig.ManaChargePerOrb));
            yield return Tween.Run(Timing.Standard, k =>
            {
                float v = rolled ? Mathf.Lerp(from, BattleConfig.ManaChargePerOrb, k) : Mathf.Lerp(from, to, k);
                SetChargeBubble(v / BattleConfig.ManaChargePerOrb);
            });
            shownCharge = to;
            if (rolled) SetChargeBubble(0f); // the OrbMinted beats that follow fill the real orb
            else SetChargeBubble(to / (float)BattleConfig.ManaChargePerOrb);
        }

        /// <summary>The next empty orb fills from the centre like a bubble forming (0..1).</summary>
        private void SetChargeBubble(float frac)
        {
            foreach (var o in orbViews) o.Q("charge")?.RemoveFromHierarchy();
            int next = State.Bank.Count;
            if (next >= orbViews.Count || frac <= 0.001f) return;
            var orb = orbViews[next];
            var fill = new VisualElement { name = "charge", pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            float inset = (1f - Mathf.Sqrt(Mathf.Clamp01(frac))) * 50f;
            fill.style.left = fill.style.top = fill.style.right = fill.style.bottom = Length.Percent(inset);
            fill.style.backgroundImage = new StyleBackground(Art.Orb);
            fill.style.unityBackgroundImageTintColor = new Color(Art.Mana.r, Art.Mana.g, Art.Mana.b, 0.55f + 0.45f * frac);
            orb.Add(fill);
        }
    }
}
