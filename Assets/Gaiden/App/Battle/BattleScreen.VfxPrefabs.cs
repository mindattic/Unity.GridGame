using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>
    /// GG-T269: where vfxstyle.json names an effect prefab (vfx.json), it plays instead of the code-drawn burst; a
    /// status whose prefab is a Loop stays on its unit while the status lasts. Reduce Motion keeps the code-drawn
    /// effects (fewer, smaller particles) and no prefabs.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private VfxPrefabLayer prefabFx;
        private readonly HashSet<string> loopKeep = new HashSet<string>();

        private void CreatePrefabFx()
        {
            prefabFx?.Dispose();
            prefabFx = new VfxPrefabLayer(board.FxLayer);
        }

        private void DisposePrefabFx()
        {
            prefabFx?.Dispose();
            prefabFx = null;
        }

        private VfxPrefabDef PrefabDef(VfxStyleDef s, VfxTier tier) =>
            s == null ? null : App.Content.VfxPrefabs.Find(VfxCatalog.PrefabFor(Vfx, s, tier));

        /// <summary>Plays the style's Once prefab at <paramref name="at"/>; false means play the code-drawn effect.</summary>
        private bool TryPrefab(VfxStyleDef s, Vector2 at, VfxTier tier, out float seconds)
        {
            seconds = 0f;
            if (prefabFx == null || ReduceMotion) return false;
            var def = PrefabDef(s, tier);
            if (def == null || def.Mode != VfxMode.Once) return false;
            if (!prefabFx.Play(def, at, CoreOf(s))) return false;
            seconds = def.Duration;
            return true;
        }

        private static string LoopKey(Unit u, string buffId) => u.Id + ":" + buffId;

        /// <summary>True when this status shows as a looping prefab on the unit (the code-drawn wisps then stand down).</summary>
        private bool HasPrefabLoop(Unit u, string buffId) => prefabFx != null && prefabFx.HasLoop(LoopKey(u, buffId));

        /// <summary>Every frame (before the pause gate): particle pause/speed and lifetimes, then status loops reconciled
        /// against the battle — a loop exists exactly while its unit stands with that status.</summary>
        private void TickPrefabFx()
        {
            if (prefabFx == null) return;
            loopKeep.Clear();
            if (!ReduceMotion && State != null && board != null && !ended)
            {
                foreach (var u in State.Units)
                {
                    if (!u.Alive || u.Buffs.Count == 0 || !board.Units.TryGetValue(u.Id, out var v) || v.Dead) continue;
                    foreach (var b in u.Buffs)
                    {
                        var def = App.Content.VfxPrefabs.Find(StatusStyle(b.Def.Id)?.Prefab);
                        if (def == null || def.Mode != VfxMode.Loop) continue;
                        string key = LoopKey(u, b.Def.Id);
                        loopKeep.Add(key);
                        var at = BoardView.CenterOf(v.Tile, v.W, v.H);
                        if (prefabFx.HasLoop(key)) prefabFx.MoveLoop(key, at);
                        else prefabFx.Play(def, at, CoreOf(StatusStyle(b.Def.Id)), key);
                    }
                }
            }
            if (devLoop != null) loopKeep.Add(devLoop);
            prefabFx.StopLoopsExcept(loopKeep);
            prefabFx.Tick();
        }

        private string devLoop;

        /// <summary>Dev/automation (GG-T269): plays one vfx.json effect on the first foe (the prefab contact sheet).</summary>
        public bool DevPrefab(string effectId, bool keep = false, int nth = 0)
        {
            var def = App.Content.VfxPrefabs.Find(effectId);
            if (def == null || prefabFx == null) return false;
            if (!keep) { devLoop = null; prefabFx.ClearAll(); }
            foreach (var u in State.Units)
            {
                if (!u.Alive || u.IsHero || nth-- > 0) continue;
                if (def.Mode == VfxMode.Loop) devLoop = "dev:" + effectId;
                return prefabFx.Play(def, ViewCenter(u), null, devLoop);
            }
            return false;
        }
    }
}
