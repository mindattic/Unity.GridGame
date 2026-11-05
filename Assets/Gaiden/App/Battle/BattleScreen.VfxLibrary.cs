using System.Collections;
using System.Collections.Generic;
using GridGame.Core.Content;
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

        /// <summary>The ability-named flourish that plays alongside (before) the element/status burst at the moment a
        /// spell's mote arrives. Since GG-T253 a family is only a MOTION played in the ability's own element style
        /// (vfxstyle.json), so Fireball's explosion and Frost's explosion share a gesture but never a look, and every
        /// flourish of one element reads as that element. No-op for abilities with no or an unrecognised ImpactVfx.</summary>
        private void NamedImpactFlourish(string vfxName, Vector2 at, VfxStyleDef st)
        {
            if (vfxName == null || st == null || !VfxFamilyOf.TryGetValue(vfxName, out var fam) || ReduceMotion) return;
            var c = GlowOf(st);
            switch (fam)
            {
                case VfxFamily.Explosion: Emit(st, at, 0.6f, "burst", quiet: true); break;
                case VfxFamily.Cloud: Emit(st, at, 0.6f, "billow", 1.6f, quiet: true); break;
                case VfxFamily.Sparkle: Emit(st, at, 0.8f, "burst", 0.5f, quiet: true); break;
                case VfxFamily.Beam: Emit(st, at, 0.5f, "fall", quiet: true); break;
                case VfxFamily.Aura: Emit(st, at, 0.6f, "pulse"); break;
                case VfxFamily.Drift: Emit(st, at, 0.5f, "rise", 0.7f, quiet: true); break;
                case VfxFamily.Slash:
                    App.StartCoroutine(Tween.Run(Timing.Quick, k =>
                    {
                        board.Lines.ClearLines();
                        float a = Mathf.Lerp(-0.7f, 0.7f, Ease.Snap(k));
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        board.Lines.Add(at - dir * 60f, at + dir * 60f, c.WithA(1f - k * 0.5f), 10f * (1f - k * 0.5f));
                    }));
                    App.StartCoroutine(ClearLinesAfter(Timing.Quick + Timing.Instant * 0.2f));
                    break;
                case VfxFamily.Strike:
                {
                    var top = at + Vector2.up * 260f;
                    App.StartCoroutine(Tween.Run(Timing.Quick, k =>
                    {
                        board.Lines.ClearLines();
                        board.Lines.Add(Vector2.Lerp(top, at, k * k), at, c.WithA(0.9f), 5f);
                    }));
                    App.StartCoroutine(ClearLinesAfter(Timing.Quick));
                    break;
                }
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
