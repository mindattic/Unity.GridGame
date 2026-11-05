using System;
using System.Collections;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace GridGame.App
{
    /// <summary>
    /// The one visual language (GG-T253, docs/POLISH.md POL-E4..E6): every element impact, status taking hold,
    /// lingering status particle, projectile trail and named flourish draws from its vfxstyle.json entry, so an
    /// element always reads by the same color, silhouette, motion, timing and sound family. Intensity tiers
    /// (minor/major/ultimate) scale particle count, size, screen shake and hit-stop.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private static readonly VfxStyleDef FallbackStyle = new VfxStyleDef
        {
            Id = "Fallback", Core = "#D9E0F2", Shape = "mote", Motion = "burst", Cue = "battle.hit", Count = 6, Size = 18, Reach = 110, Time = "Standard",
        };

        /// <summary>The tier of the hits being played: a pincer's or the resolving ability's, reset at every new action.</summary>
        private VfxTier hitTier = VfxTier.Minor;

        private VfxStyleSheet Vfx => App.Content.Vfx;
        private VfxStyleDef StyleOf(DamageType t) => Vfx.Element(t) ?? FallbackStyle;
        private VfxStyleDef StatusStyle(string id) => Vfx.Status(id);
        private VfxStyleDef HealStyle => Vfx.Family("Heal") ?? FallbackStyle;

        /// <summary>An ability's look: heals read as Heal, damage as its element, a pure status spell as that status.</summary>
        private VfxStyleDef StyleForAbility(AbilityDef a)
        {
            if (a == null) return FallbackStyle;
            if (a.BaseHeal > 0) return HealStyle;
            if (a.BaseDamage <= 0 && a.ApplyBuff != null && StatusStyle(a.ApplyBuff) is VfxStyleDef s) return s;
            return StyleOf(a.DamageType);
        }

        private static Color CoreOf(VfxStyleDef s) => Palette.Hex(s.Core, Palette.Info);
        private static Color GlowOf(VfxStyleDef s) => Palette.Hex(s.Glow, CoreOf(s));
        private static Color AccentOf(VfxStyleDef s) => Palette.Hex(s.Accent, Palette.White);

        private static float Dur(string time)
        {
            switch (time)
            {
                case "Instant": return Timing.Instant;
                case "Quick": return Timing.Quick;
                case "Standard": return Timing.Standard;
                case "Dramatic": return Timing.Dramatic;
                case "Cinematic": return Timing.Cinematic;
                default: return Timing.Emphasis;
            }
        }

        private static VfxTier Bump(VfxTier t) => t == VfxTier.Ultimate ? t : t + 1;

        /// <summary>A particle in the style's silhouette: sparks streak, shards and droplets stretch along their path,
        /// smoke is soft and wide; everything else is a round glow.</summary>
        private VisualElement Shaped(VfxStyleDef s, float size, Color c, float angle)
        {
            var p = Particle(size, c);
            switch (s.Shape)
            {
                case "spark": p.style.width = size * 2.2f; p.style.height = size * 0.55f; break;
                case "shard": p.style.width = size * 1.8f; p.style.height = size * 0.6f; break;
                case "ray": p.style.width = size * 4f; p.style.height = size * 0.5f; break;
                case "droplet": p.style.width = size * 1.3f; p.style.height = size * 0.8f; break;
                case "smoke": p.style.width = size * 1.3f; break;
            }
            p.style.rotate = new Rotate(angle * Mathf.Rad2Deg);
            return p;
        }

        /// <summary>Drives one particle for <paramref name="seconds"/> after a fixed <paramref name="delay"/> (POL-E3: no
        /// random timing); <paramref name="f"/> maps progress to position, opacity and scale.</summary>
        private void Fly(VisualElement p, float seconds, float delay, Func<float, (Vector2 pos, float alpha, float scale)> f)
        {
            float total = seconds + delay;
            p.style.opacity = 0f;
            App.StartCoroutine(Tween.Run(total, kk =>
            {
                float elapsed = kk * total;
                if (elapsed < delay && kk < 1f) { p.style.opacity = 0f; return; }
                float k = Mathf.Clamp01((elapsed - delay) / seconds);
                var (pos, a, sc) = f(k);
                p.style.left = pos.x - p.style.width.value.value / 2f;
                p.style.top = pos.y - p.style.height.value.value / 2f;
                p.style.opacity = a;
                p.style.scale = new Scale(Vector2.one * sc);
                if (kk >= 1f) p.RemoveFromHierarchy();
            }));
        }

        /// <summary>A ring that grows out of (or, contracting, closes in on) a point, independent of other rings.</summary>
        private void RingEl(Vector2 at, Color c, float reach, float seconds, bool contract, float width = 10f)
        {
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.style.position = Position.Absolute;
            board.FxLayer.Add(ring);
            App.StartCoroutine(Tween.Run(seconds, k =>
            {
                float r = contract ? 30f + reach * (1f - Ease.Out(k)) : 24f + reach * Ease.Out(k);
                ring.style.left = at.x - r; ring.style.top = at.y - r; ring.style.width = ring.style.height = r * 2f;
                ring.style.borderTopLeftRadius = ring.style.borderTopRightRadius = ring.style.borderBottomLeftRadius = ring.style.borderBottomRightRadius = r;
                float w = Mathf.Lerp(width, width * 0.25f, k);
                ring.style.borderTopWidth = ring.style.borderBottomWidth = ring.style.borderLeftWidth = ring.style.borderRightWidth = w;
                var col = c.WithA(contract ? k : 1f - k);
                ring.style.borderTopColor = ring.style.borderBottomColor = ring.style.borderLeftColor = ring.style.borderRightColor = col;
                if (k >= 1f) ring.RemoveFromHierarchy();
            }));
        }

        /// <summary>
        /// Plays a style's particles at <paramref name="at"/> and returns how long its motion runs.
        /// <paramref name="intensity"/> scales count, size and reach (a tier, or below 1 for a lingering wisp);
        /// <paramref name="motion"/> overrides the style's motion (a named flourish); <paramref name="quiet"/> skips rings.
        /// </summary>
        private float Emit(VfxStyleDef s, Vector2 at, float intensity = 1f, string motion = null, float sizeMul = 1f, bool quiet = false)
        {
            if (s == null || board == null) return 0f;
            // GG-T269: a named prefab replaces the burst (not lingering wisps or named flourishes).
            if (motion == null && intensity >= 0.75f && TryPrefab(s, at, hitTier, out float prefabSeconds)) return prefabSeconds;
            motion = motion ?? s.Motion;
            float seconds = Dur(s.Time);
            int n = Mathf.Max(1, Mathf.RoundToInt((s.Count ?? 8) * intensity * (ReduceMotion ? 0.5f : 1f)));
            float size = (s.Size ?? 20f) * sizeMul * Mathf.Lerp(1f, Mathf.Sqrt(Mathf.Max(0.1f, intensity)), 0.6f);
            float reach = (s.Reach ?? 120f) * Mathf.Lerp(1f, intensity, 0.4f);
            Color core = CoreOf(s), glow = GlowOf(s), accent = AccentOf(s);

            if (!quiet && (s.Ring == true || motion == "pulse" || motion == "contract"))
                RingEl(at, core, reach, seconds, motion == "contract", 6f + 4f * intensity);
            if (motion == "bolt") App.StartCoroutine(Bolt(at, core, glow, intensity));
            if (motion == "drift")
            {
                for (int i = 0; i < n; i++) App.StartCoroutine(GlyphDrift(s, at, i * Timing.Quick, sizeMul * (intensity < 1f ? 0.7f : 1f), seconds));
                return seconds + n * Timing.Quick;
            }

            for (int i = 0; i < n; i++)
            {
                float ang = (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / n;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float sz = size * Random.Range(0.75f, 1.25f), d = reach * Random.Range(0.7f, 1f);
                var col = Color.Lerp(core, i % 3 == 0 ? accent : glow, Random.value * 0.5f);
                float delay = (float)i / n * Timing.Instant;
                float x0 = Random.Range(-0.5f, 0.5f) * reach * 0.8f, wob = Random.value * 6.28f;
                switch (motion)
                {
                    case "rise":
                    {
                        bool bubble = s.Shape == "bubble";
                        var p = Shaped(s, sz, col, -Mathf.PI / 2f);
                        Fly(p, seconds, delay, k => (at + new Vector2(x0 + Mathf.Sin(wob + k * 9f) * 12f, 30f - d * Ease.Out(k)), 1f - k, bubble ? 0.6f + 0.8f * k : 1f));
                        break;
                    }
                    case "fall":
                    {
                        var p = Shaped(s, sz, col, Mathf.PI / 2f);
                        var top = at + new Vector2(x0, -BoardView.Tile * 0.55f);
                        Fly(p, seconds, delay, k => (top + Vector2.up * d * k * k, 1f - 0.6f * k, 1f));
                        break;
                    }
                    case "implode":
                    {
                        var p = Shaped(s, sz, col, ang);
                        Fly(p, seconds, delay, k => (at + dir * d * (1f - Ease.InOut(k)), 0.3f + 0.7f * k, 1f));
                        break;
                    }
                    case "spiral":
                    {
                        var p = Shaped(s, sz, col, ang);
                        Fly(p, seconds, delay, k => { float a = ang + k * 2.2f; return (at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d * (1f - 0.6f * k), 1f - 0.7f * k, 1f); });
                        break;
                    }
                    case "billow":
                    {
                        var p = Shaped(s, sz, col.WithA(0.9f), ang);
                        var off = new Vector2(dir.x * d, -Mathf.Abs(dir.y) * d);
                        Fly(p, seconds, delay, k => (at + off * (0.4f + 0.6f * k), Ease.Arc(k) * 0.9f, 0.8f + 0.4f * k));
                        break;
                    }
                    case "pulse":
                    case "contract":
                    {
                        bool inward = motion == "contract";
                        var p = Shaped(s, sz, col, ang);
                        Fly(p, seconds, delay, k => (at + dir * (inward ? 30f + d * (1f - Ease.Out(k)) : 24f + d * Ease.Out(k)), inward ? k : 1f - k, 1f));
                        break;
                    }
                    default: // burst, rays, bolt: out from the point along each particle's own direction
                    {
                        var p = Shaped(s, sz, col, ang);
                        float gravity = s.Shape == "spark" ? 60f : 0f, from = motion == "rays" ? 30f : 0f;
                        Fly(p, seconds, delay, k => (at + dir * (from + d * Ease.Out(k)) + Vector2.up * (gravity * k * k), 1f - k, 1f));
                        break;
                    }
                }
            }
            return seconds;
        }

        /// <summary>A lightning strike from the mote above the target (or the sky), flickering twice.</summary>
        private IEnumerator Bolt(Vector2 at, Color core, Color glow, float intensity)
        {
            var origin = TakeStrikeOrigin(at) ?? at + new Vector2(Random.Range(-30f, 30f), -560f);
            var pts = new List<Vector2> { origin };
            while (pts[pts.Count - 1].y < at.y - 40) pts.Add(new Vector2(at.x + Random.Range(-46f, 46f), pts[pts.Count - 1].y + Random.Range(60f, 110f)));
            pts.Add(at);
            for (int flash = 0; flash < 2; flash++)
            {
                board.Lines.ClearLines();
                for (int i = 1; i < pts.Count; i++) { board.Lines.Add(pts[i - 1], pts[i], glow.WithA(0.95f), 10f * intensity); board.Lines.Add(pts[i - 1], pts[i], Palette.White, 4f); }
                yield return Tween.Wait(Timing.Instant);
                board.Lines.ClearLines();
                yield return Tween.Wait(Timing.Instant);
            }
        }

        /// <summary>The glyph shape (a sleeper's "z"): drifts up and away, growing as it fades.</summary>
        private IEnumerator GlyphDrift(VfxStyleDef s, Vector2 at, float delay, float sizeMul, float seconds)
        {
            yield return Tween.Wait(delay);
            var l = board.Float(s.Glyph ?? "z", at + new Vector2(30f, -40f), CoreOf(s), (s.Size ?? 46f) * sizeMul);
            var start = new Vector2(l.style.left.value.value, l.style.top.value.value);
            yield return Tween.Run(seconds, k =>
            {
                l.style.left = start.x + 50f * k; l.style.top = start.y - (s.Reach ?? 90f) * k;
                l.style.opacity = 1f - k;
                l.style.scale = new Scale(Vector2.one * (0.7f + 0.6f * k));
            });
            l.RemoveFromHierarchy();
        }

        /// <summary>One wisp of a lingering status (poison bubbling, a burn smouldering): the style at a fraction of its count.</summary>
        private void Linger(VfxStyleDef s, Vector2 at)
        {
            var jitter = new Vector2(Random.Range(-40f, 40f), Random.Range(-20f, 30f));
            Emit(s, at + jitter, 1f / Mathf.Max(1, s.Count ?? 8), quiet: true, sizeMul: 0.8f);
        }

        /// <summary>
        /// Dev/automation (GG-T253): plays one ability's whole look — cast, flight, impact at its tier, the status it
        /// inflicts — from the first hero onto the first foe, without touching the battle state. Drives the
        /// tools/harness.py vfxsheet contact sheets.
        /// </summary>
        public bool DevVfx(string abilityId)
        {
            var a = App.Content.TryAbility(abilityId);
            Unit caster = null, target = null;
            foreach (var u in State.Units)
            {
                if (!u.Alive) continue;
                if (caster == null && u.IsHero) caster = u;
                if (target == null && !u.IsHero) target = u;
            }
            if (a == null || caster == null || target == null) return false;
            App.StartCoroutine(DevVfxPlay(a, caster, target));
            return true;
        }

        private IEnumerator DevVfxPlay(AbilityDef a, Unit caster, Unit target)
        {
            var ev = new AbilityResolvedEvent { CasterId = caster.Id, AbilityId = a.Id, Anchor = target.Pos };
            ev.Targets.Add(target.Id);
            ev.Tiles.Add(target.Pos);
            yield return AbilityAnim(ev);
            hitTier = VfxStyleSheet.TierOf(a);
            if (a.BaseHeal > 0) Emit(HealStyle, ViewCenter(target));
            else if (a.BaseDamage > 0)
                yield return DamageAnim(new DamagedEvent { TargetId = target.Id, SourceId = caster.Id, Amount = a.BaseDamage, Hit = HitKind.Normal, Type = a.DamageType, HpAfter = target.Hp });
            if (a.ApplyBuff != null && board.Units.TryGetValue(target.Id, out var v) && StatusStyle(a.ApplyBuff) != null)
                yield return StatusBurst(a.ApplyBuff, ViewCenter(target), Palette.Info, v);
            yield return Tween.Wait(Timing.Standard);
            if (board.Units.TryGetValue(target.Id, out var tv)) { tv.Art.style.unityBackgroundImageTintColor = Palette.White; tv.Root.style.scale = new Scale(Vector2.one); }
            SyncAll();
        }
    }
}
