using System;
using GridGame.Core.Content;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>
    /// The design tokens (GG-T250, docs/POLISH.md POL-A1/B1/B4/C1/E1/E2). Every color, type size, spacing step,
    /// duration and easing in App code comes from here; Theme.uss mirrors the same values as :root custom properties.
    /// </summary>
    public static class Palette
    {
        // Surfaces and text
        public static readonly Color Text = new Color(0.94f, 0.95f, 1f);
        public static readonly Color TextDim = new Color(0.59f, 0.63f, 0.8f);
        public static readonly Color White = Color.white;
        public static readonly Color Ink = new Color(0.06f, 0.07f, 0.17f);
        public static readonly Color Panel = new Color(0.04f, 0.06f, 0.2f, 0.94f);
        public static readonly Color Shade = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color Frame = new Color(0.93f, 0.93f, 1f);

        // Semantic feedback: one meaning per hue family (POL-B4)
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
        public static readonly Color GoldLight = new Color(1f, 0.93f, 0.6f);
        public static readonly Color Good = new Color(0.45f, 0.95f, 0.58f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.34f);
        public static readonly Color Warn = new Color(1f, 0.77f, 0.36f);
        public static readonly Color Info = new Color(0.86f, 0.88f, 1f);
        /// <summary>A tappable glossary [term] (GG-T300): the same light blue class cards always marked keywords with.</summary>
        public static readonly Color Term = new Color(0.61f, 0.78f, 1f);
        public static readonly Color Mana = Art.Mana;

        // Teams
        public static readonly Color Hero = new Color(0.47f, 0.67f, 1f);
        public static readonly Color Enemy = new Color(0.92f, 0.27f, 0.29f);
        public static readonly Color Boss = new Color(0.86f, 0.4f, 1f);
        public static readonly Color Guest = new Color(0.43f, 0.94f, 0.63f);
        /// <summary>A tamed or domesticated monster fighting for the party on its own (GG-T303): its board keyline and turn icon.</summary>
        public static readonly Color Tamed = new Color(0.76f, 0.92f, 0.28f);
        /// <summary>A wanderer fighting the monsters on its own (GG-T342): its keyline, WANDERER tag, turn icon and trust meter.</summary>
        public static readonly Color Wanderer = new Color(1f, 0.56f, 0.8f);
        public static readonly Color Neutral = new Color(1f, 0.8f, 0.37f);
        // Casts riding the timeline (GG-T287): never the red of an enemy turn, so a spell reads apart from a foe.
        public static readonly Color CastHero = new Color(0.15f, 0.77f, 0.9f);
        public static readonly Color CastEnemy = new Color(1f, 0.55f, 0.1f);

        // Statuses that aren't an element
        public static readonly Color Haste = new Color(0.35f, 1f, 0.88f);
        public static readonly Color Buff = new Color(1f, 0.72f, 0.28f);
        public static readonly Color Cleanse = new Color(0.9f, 1f, 1f);

        /// <summary>The element table: each DamageType owns one hue no status or UI message reuses (POL-B4). The hue lives
        /// in vfxstyle.json (GG-T253, POL-E4); these literals are only the fallback before content loads.</summary>
        public static Color Element(DamageType t)
        {
            var st = GameApp.I?.Content?.Vfx?.Element(t);
            if (st?.Core != null) return Hex(st.Core, Color.white);
            switch (t)
            {
                case DamageType.Fire: return new Color(1f, 0.45f, 0.15f);
                case DamageType.Ice: return new Color(0.5f, 0.85f, 1f);
                case DamageType.Lightning: return new Color(1f, 0.95f, 0.35f);
                case DamageType.Poison: return new Color(0.6f, 1f, 0.3f);
                case DamageType.Holy: return new Color(1f, 0.97f, 0.78f);
                case DamageType.Dark: return new Color(0.62f, 0.3f, 0.95f);
                case DamageType.Arcane: return new Color(1f, 0.45f, 0.85f);
                default: return new Color(0.85f, 0.88f, 0.95f);
            }
        }

        public static Color Role(string role)
        {
            switch (role)
            {
                case "Tank": return new Color(0.95f, 0.36f, 0.3f);
                case "Healer": return new Color(0.4f, 0.9f, 0.5f);
                case "Mage": return new Color(0.55f, 0.6f, 1f);
                case "Striker": return new Color(1f, 0.66f, 0.25f);
                case "Support": return new Color(0.35f, 0.85f, 0.85f);
                case "Ranged": return new Color(0.68f, 0.85f, 0.35f);
                case Ui.TamedRole: return Tamed; // a domesticated monster's badge (GG-T303)
                default: return new Color(0.8f, 0.8f, 0.85f);
            }
        }

        public static Color WithA(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>A #RRGGBB content color (vfxstyle.json), or <paramref name="fallback"/> when absent or malformed.</summary>
        public static Color Hex(string hex, Color fallback) =>
            hex != null && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }

    /// <summary>Type scale ≈1.25 (POL-A1). Body ≥ 40, captions ≥ 30 (POL-A2).</summary>
    public static class TypeScale
    {
        public const float Badge = 24, Caption = 30, Small = 36, Body = 40, H3 = 48, H2 = 60, H1 = 76, Display = 96, Title = 120;
    }

    /// <summary>8-px spacing scale (POL-C1); S4 is the half step for hairline insets inside small widgets.</summary>
    public static class Space
    {
        public const float S0 = 0, S4 = 4, S8 = 8, S16 = 16, S24 = 24, S32 = 32, S48 = 48, S64 = 64, S96 = 96;
        /// <summary>Half of the 24-px gap between tap targets (POL-C3): each neighbour's margin, so two add up to S24
        /// (USS <c>--space-12</c>).</summary>
        public const float TargetHalfGap = 12;
    }

    /// <summary>Named element sizes in design px (POL-C1). Design px already scale uniformly to every resolution
    /// (bible §26.2), so fixed-size things (icons, pips, portraits) use these; layout widths use <see cref="Fit"/>.</summary>
    public static class Size
    {
        // Lines, bars and pips
        public const float Hairline = 3, Track = 6, Bar = 8, Shard = 13, Gauge = 14, HpBar = 18, Crack = 24, Pip = 34;
        // Icons and pictures
        public const float IconS = 40, Chip = 50, IconM = 56, Orb = 60, Icon = 64, IconL = 76, AvatarW = 80, Glyph = 88,
            Avatar = 96, PartySlot = 108, Thumb = 120, Medallion = 140, Gem = 192, PortraitW = 360, PortraitH = 400,
            ArtW = 420, ArtH = 620;
        // Controls and table columns (POL-C2: a tap target is at least 132×132)
        public const float Plate = 92, Target = 132, Cell = 170, Slot = 196, Card = 300,
            ColumnS = 70, Column = 90, ColumnL = 100, Label = 180;
    }

    /// <summary>Aspect-agnostic layout units (GG-T272): fractions of the safe-area stage, which grows from 1170×2532 up to
    /// 9:16 or 9:21 (§26.3). Read them when a screen builds; every screen rebuilds on <see cref="GameApp.StageResized"/>.</summary>
    public static class Fit
    {
        /// <summary>The one screen-edge gutter: every full-width panel sits this far from the stage's left and right edges.</summary>
        public const float Edge = Space.S24;
        public static float Width => GameApp.StageWidth;
        public static float Height => GameApp.StageHeight - GameApp.SafeTop - GameApp.SafeBottom;
        /// <summary><paramref name="pct"/> percent of the usable stage width.</summary>
        public static float W(float pct) => Width * pct / 100f;
        /// <summary><paramref name="pct"/> percent of the usable stage height.</summary>
        public static float H(float pct) => Height * pct / 100f;
    }

    /// <summary>Duration tokens in seconds (POL-E1). Anything longer than Cinematic is a hold, also in the cinematic band.</summary>
    public static class Timing
    {
        public const float Instant = 0.08f, Quick = 0.15f, Standard = 0.25f, Emphasis = 0.4f, Dramatic = 0.7f, Cinematic = 1.2f;
    }

    /// <summary>Easing tokens (POL-E2).</summary>
    public static class Ease
    {
        public static readonly Func<float, float> Out = Tween.EaseOut;
        public static readonly Func<float, float> InOut = Tween.EaseInOut;
        public static readonly Func<float, float> Back = Tween.Back;
        public static readonly Func<float, float> Snap = t => 1f - Mathf.Pow(1f - t, 4f);
        /// <summary>0 → 1 → 0 over the run: a hop, a flash's rise and fall, a projectile's lift.</summary>
        public static readonly Func<float, float> Arc = t => Mathf.Sin(t * Mathf.PI);
    }
}
