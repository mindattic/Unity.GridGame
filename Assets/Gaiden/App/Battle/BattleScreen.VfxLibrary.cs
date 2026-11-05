using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Named-VFX flourishes (bible §6.2): abilities carry a CastVfx/ProjectileVfx/ImpactVfx/LingerVfx name
    /// (e.g. "PuffyExplosion", "ToxicCloud", "GodRays") that, until now, no code ever read — presentation
    /// derived purely from DamageType/buff id (ElementBurst, StatusBurst), which already gives every element
    /// and status its own rich take. What that left flat was the ability-level arrival beat: Fireball and
    /// Meteor (both Fire) or Sleep and Silence (both non-damage) read identically at the moment the mote
    /// lands, before the DamageType/buff-specific burst follows. This groups the 55 distinct names in
    /// abilities.json into 8 visually distinct FAMILIES and gives each family its own extra flourish, layered
    /// on top of (never instead of) the existing impact — still entirely code-drawn UI Toolkit motes and
    /// lines (no particle prefabs/ParticleSystem GameObjects: that would mean a world-space render layer
    /// alongside the UI Toolkit canvas, which repo law #3 and the 2026-10-09 VFX amendment both rule out).
    /// </summary>
    public sealed partial class BattleScreen
    {
        private enum VfxFamily { Explosion, Slash, Cloud, Sparkle, Beam, Aura, Strike, Drift }

        private static readonly Dictionary<string, VfxFamily> VfxFamilyOf = new Dictionary<string, VfxFamily>
        {
            ["PuffyExplosion"] = VfxFamily.Explosion, ["LightningExplosion"] = VfxFamily.Explosion,
            ["ArcaneBurst"] = VfxFamily.Explosion, ["ShadowBurst"] = VfxFamily.Explosion,
            ["EmberBurst"] = VfxFamily.Explosion, ["IronSunder"] = VfxFamily.Explosion,

            ["QuickSlash"] = VfxFamily.Slash, ["SparkSlash"] = VfxFamily.Slash, ["SwiftArc"] = VfxFamily.Slash,
            ["ArcaneEdge"] = VfxFamily.Slash,

            ["ToxicCloud"] = VfxFamily.Cloud, ["PoisonCloud"] = VfxFamily.Cloud, ["InkVeil"] = VfxFamily.Cloud,
            ["GloomSigil"] = VfxFamily.Cloud, ["HexCreep"] = VfxFamily.Cloud,

            ["GoldSparkle"] = VfxFamily.Sparkle, ["PinkSpark"] = VfxFamily.Sparkle, ["PinkDust"] = VfxFamily.Sparkle,
            ["SleepDust"] = VfxFamily.Sparkle, ["IceSparkle"] = VfxFamily.Sparkle, ["AntidoteSparkle"] = VfxFamily.Sparkle,
            ["DustKick"] = VfxFamily.Sparkle, ["GritSpark"] = VfxFamily.Sparkle,

            ["RayBlast"] = VfxFamily.Beam, ["GodRays"] = VfxFamily.Beam, ["ScanRays"] = VfxFamily.Beam,
            ["HolyWard"] = VfxFamily.Beam,

            ["BlueGlow"] = VfxFamily.Aura, ["BuffLife"] = VfxFamily.Aura, ["HealAura"] = VfxFamily.Aura,
            ["MendingPulse"] = VfxFamily.Aura, ["ShadowBloom"] = VfxFamily.Aura, ["VoidBloom"] = VfxFamily.Aura,
            ["SlowShimmer"] = VfxFamily.Aura, ["IronGuard"] = VfxFamily.Aura, ["SteelGlint"] = VfxFamily.Aura,

            ["LightningStrike"] = VfxFamily.Strike, ["ShockBolt"] = VfxFamily.Strike, ["Flame"] = VfxFamily.Strike,
            ["Fireball"] = VfxFamily.Strike, ["FlamingTwist"] = VfxFamily.Strike, ["AcidSplash"] = VfxFamily.Strike,

            ["Afterimage"] = VfxFamily.Drift, ["WindStep"] = VfxFamily.Drift, ["Feather"] = VfxFamily.Drift,
            ["AquaDrift"] = VfxFamily.Drift, ["Shuriken"] = VfxFamily.Drift, ["ShadowTwist"] = VfxFamily.Drift,
            ["ShadowSigil"] = VfxFamily.Drift, ["SparkRig"] = VfxFamily.Drift, ["Bubble"] = VfxFamily.Drift,
        };

        /// <summary>The ability-named flourish that plays alongside (before) the DamageType/buff-specific
        /// burst, at the moment a spell's mote arrives. No-op for abilities with no named ImpactVfx or an
        /// unrecognised one — those still get the plain ring from ImpactSplash, same as before this pass.</summary>
        private void NamedImpactFlourish(string vfxName, Vector2 at, Color c)
        {
            if (vfxName == null || !VfxFamilyOf.TryGetValue(vfxName, out var fam) || ReduceMotion) return;
            switch (fam)
            {
                case VfxFamily.Explosion:
                    for (int i = 0; i < 7; i++)
                    {
                        var p = Particle(Random.Range(20f, 34f), Color.Lerp(c, Color.white, Random.value * 0.4f));
                        float ang = i * 6.283f / 7f + Random.Range(-0.2f, 0.2f);
                        App.StartCoroutine(Tween.Run(Timing.Standard, k =>
                        {
                            var pos = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 90f * Tween.EaseOut(k);
                            p.style.left = pos.x - 17; p.style.top = pos.y - 17; p.style.opacity = 1f - k;
                            p.style.scale = new Scale(Vector2.one * (1f + 0.5f * k));
                            if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    break;
                case VfxFamily.Slash:
                    App.StartCoroutine(Tween.Run(Timing.Quick, k =>
                    {
                        board.Lines.ClearLines();
                        float a0 = -0.7f, a1 = 0.7f, a = Mathf.Lerp(a0, a1, k);
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        board.Lines.Add(at - dir * 60f, at + dir * 60f, new Color(1f, 1f, 1f, 1f - k * 0.5f), 10f * (1f - k * 0.5f));
                    }));
                    App.StartCoroutine(ClearLinesAfter(0.16f));
                    break;
                case VfxFamily.Cloud:
                    for (int i = 0; i < 6; i++)
                    {
                        var p = Particle(Random.Range(26f, 44f), new Color(c.r, c.g, c.b, 0.55f));
                        var off = new Vector2(Random.Range(-45f, 45f), Random.Range(-30f, 30f));
                        App.StartCoroutine(Tween.Run(Timing.Dramatic, k =>
                        {
                            p.style.left = at.x + off.x * (0.6f + 0.6f * k) - 22; p.style.top = at.y + off.y * (0.6f + 0.6f * k) - 22;
                            p.style.opacity = Mathf.Sin(k * Mathf.PI) * 0.6f; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    break;
                case VfxFamily.Sparkle:
                    for (int i = 0; i < 8; i++)
                    {
                        var p = Particle(Random.Range(8f, 14f), Color.Lerp(c, Color.white, 0.5f));
                        float ang = Random.Range(0f, 6.283f);
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                        {
                            var pos = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 50f * k - Vector2.up * 20f * k;
                            p.style.left = pos.x - 6; p.style.top = pos.y - 6; p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    break;
                case VfxFamily.Beam:
                    App.StartCoroutine(Tween.Run(Timing.Standard, k =>
                    {
                        board.Lines.ClearLines();
                        for (int i = -1; i <= 1; i++)
                            board.Lines.Add(at + new Vector2(i * 20f, -140f * (1f - k)), at + new Vector2(i * 20f, 20f), new Color(c.r, c.g, c.b, 0.8f * (1f - k)), 6f);
                    }));
                    App.StartCoroutine(ClearLinesAfter(0.32f));
                    break;
                case VfxFamily.Aura:
                    App.StartCoroutine(Tween.Run(Timing.Emphasis, k => { board.Lines.Rings.Clear(); board.Lines.Ring(at, 24f + 70f * Tween.EaseOut(k), new Color(c.r, c.g, c.b, 1f - k), 7f); }));
                    App.StartCoroutine(ClearRingsAfter(0.42f));
                    break;
                case VfxFamily.Strike:
                {
                    var top = at + Vector2.up * 260f;
                    App.StartCoroutine(Tween.Run(Timing.Quick, k =>
                    {
                        board.Lines.ClearLines();
                        board.Lines.Add(Vector2.Lerp(top, at, k * k), at, new Color(c.r, c.g, c.b, 0.9f), 5f);
                    }));
                    App.StartCoroutine(ClearLinesAfter(0.14f));
                    break;
                }
                case VfxFamily.Drift:
                    for (int i = 0; i < 5; i++)
                    {
                        var p = Particle(Random.Range(12f, 20f), new Color(c.r, c.g, c.b, 0.7f));
                        float x0 = Random.Range(-30f, 30f);
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                        {
                            p.style.left = at.x + x0 * (1f - k) - 10; p.style.top = at.y - 30f * k - 10;
                            p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    break;
            }
        }

        private IEnumerator ClearLinesAfter(float s)
        {
            yield return Tween.Wait(s);
            board.Lines.ClearLines();
        }

        private IEnumerator ClearRingsAfter(float s)
        {
            yield return Tween.Wait(s);
            board.Lines.Rings.Clear();
            board.Lines.MarkDirtyRepaint();
        }
    }
}
