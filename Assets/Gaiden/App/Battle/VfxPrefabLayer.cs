using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace GridGame.App
{
    /// <summary>
    /// GG-T269: premade particle prefabs (Resources/Vfx, normalized by vfx.json) on the UI Toolkit board. An
    /// orthographic effects camera renders layer <see cref="Layer"/> into a RenderTexture, shown by an overlay
    /// stretched over the board's FX layer, one world unit per board pixel. Pause and battle speed drive every live
    /// ParticleSystem; Once effects stop emitting after their Duration and go back to a pool.
    /// </summary>
    public sealed class VfxPrefabLayer : IDisposable
    {
        public const int Layer = 31;
        private const int MaxRtSide = 1600;

        private sealed class Live
        {
            public VfxPrefabDef Def;
            public GameObject Go;
            public ParticleSystem[] Systems;
            public float[] BaseSpeed;
            public float Age;
            public bool Stopped;
            public string Owner;
        }

        private readonly VisualElement host, overlay;
        private readonly GameObject rig;
        private readonly Camera cam;
        private readonly Material composite;
        private readonly Dictionary<string, Stack<GameObject>> pool = new Dictionary<string, Stack<GameObject>>();
        private readonly List<Live> live = new List<Live>();
        private readonly Dictionary<string, Live> loops = new Dictionary<string, Live>();
        private RenderTexture rt;
        private float lastSpeed = -1f;

        public int LiveCount => live.Count;

        public VfxPrefabLayer(VisualElement fxHost)
        {
            host = fxHost;
            overlay = new VisualElement { name = "vfx-prefab-overlay", pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0;
            host.Add(overlay);
            overlay.RegisterCallback<GeometryChangedEvent>(_ => Resize());

            rig = new GameObject("VfxPrefabRig");
            Object.DontDestroyOnLoad(rig);
            var camGo = new GameObject("VfxCamera");
            camGo.transform.SetParent(rig.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.depth = -50;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 4000f;
            cam.enabled = false;
            var shader = Resources.Load<Shader>("Shaders/VfxComposite");
            if (shader != null)
            {
                composite = new Material(shader);
                camGo.AddComponent<Compositor>().Mat = composite;
            }
        }

        private sealed class Compositor : MonoBehaviour
        {
            public Material Mat;
            private void OnRenderImage(RenderTexture src, RenderTexture dst) => Graphics.Blit(src, dst, Mat);
        }

        private void Resize()
        {
            var r = overlay.layout;
            if (float.IsNaN(r.width) || r.width < 2f || r.height < 2f) return;
            float scale = Mathf.Min(1f, Screen.height / Mathf.Max(1f, GameApp.StageHeight), MaxRtSide / Mathf.Max(r.width, r.height));
            int w = Mathf.Max(8, Mathf.RoundToInt(r.width * scale)), h = Mathf.Max(8, Mathf.RoundToInt(r.height * scale));
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) { cam.targetTexture = null; rt.Release(); Object.Destroy(rt); }
                rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "VfxPrefabRT" };
                cam.targetTexture = rt;
                overlay.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            }
            cam.orthographicSize = r.height / 2f;
            cam.aspect = r.width / r.height;
            cam.transform.position = new Vector3(r.width / 2f, -r.height / 2f, -1000f);
        }

        private static Vector3 World(Vector2 boardPx) => new Vector3(boardPx.x, -boardPx.y, 0f);

        /// <summary>Plays a vfx.json effect at a board point; returns false (caller falls back to the code-drawn effect)
        /// when the prefab is missing. <paramref name="owner"/> keys a Loop so it can be moved and ended.</summary>
        public bool Play(VfxPrefabDef def, Vector2 at, Color? tint = null, string owner = null)
        {
            if (def == null || rt == null && overlay.layout.width < 2f) return false;
            var go = Take(def);
            if (go == null) return false;
            float scale = def.Tiles * BoardView.Tile / Mathf.Max(0.01f, def.Extent);
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = World(at);
            var l = new Live { Def = def, Go = go, Systems = go.GetComponentsInChildren<ParticleSystem>(true), Owner = owner };
            l.BaseSpeed = new float[l.Systems.Length];
            for (int i = 0; i < l.Systems.Length; i++)
            {
                var ps = l.Systems[i];
                var main = ps.main;
                l.BaseSpeed[i] = main.simulationSpeed > 0f ? main.simulationSpeed : 1f;
                main.simulationSpeed = Tween.Paused ? 0f : l.BaseSpeed[i] * Tween.Speed;
                if (def.Tint && tint.HasValue) main.startColor = tint.Value;
            }
            go.SetActive(true);
            foreach (var ps in l.Systems) { ps.Clear(true); ps.Play(true); }
            live.Add(l);
            if (owner != null) loops[owner] = l;
            cam.enabled = true;
            return true;
        }

        public bool HasLoop(string owner) => loops.ContainsKey(owner);

        public void MoveLoop(string owner, Vector2 at)
        {
            if (loops.TryGetValue(owner, out var l)) l.Go.transform.position = World(at);
        }

        public void StopLoop(string owner)
        {
            if (!loops.TryGetValue(owner, out var l)) return;
            loops.Remove(owner);
            StopEmitting(l);
        }

        /// <summary>Every loop whose owner key isn't in <paramref name="keep"/> stops (status ended, unit died, wave over).</summary>
        public void StopLoopsExcept(HashSet<string> keep)
        {
            var gone = new List<string>();
            foreach (var k in loops.Keys) if (!keep.Contains(k)) gone.Add(k);
            foreach (var k in gone) StopLoop(k);
        }

        private static void StopEmitting(Live l)
        {
            if (l.Stopped) return;
            l.Stopped = true;
            foreach (var ps in l.Systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>Per frame, before the battle's own pause gate: pause/speed, Once lifetimes, pool returns, repaint.</summary>
        public void Tick()
        {
            if (live.Count == 0) { if (cam.enabled) { cam.enabled = false; ClearRt(); } return; }
            float speed = Tween.Paused ? 0f : Tween.Speed;
            bool speedChanged = !Mathf.Approximately(speed, lastSpeed);
            lastSpeed = speed;
            float dt = Tween.Dt * Tween.Speed;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i];
                if (l.Go == null) { live.RemoveAt(i); continue; }
                if (speedChanged)
                    for (int s = 0; s < l.Systems.Length; s++) { var m = l.Systems[s].main; m.simulationSpeed = l.BaseSpeed[s] * speed; }
                l.Age += dt;
                if (l.Def.Mode == VfxMode.Once && !l.Stopped && l.Age >= l.Def.Duration) StopEmitting(l);
                bool alive = false;
                foreach (var ps in l.Systems) if (ps.IsAlive(false)) { alive = true; break; }
                if (l.Stopped && (!alive || l.Age > l.Def.Duration + 2.5f)) { Return(l); live.RemoveAt(i); }
            }
            overlay.MarkDirtyRepaint();
        }

        private void ClearRt()
        {
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;
            overlay.MarkDirtyRepaint();
        }

        private GameObject Take(VfxPrefabDef def)
        {
            if (pool.TryGetValue(def.ResourcePath, out var stack) && stack.Count > 0) return stack.Pop();
            var prefab = Resources.Load<GameObject>(def.ResourcePath);
            if (prefab == null) { Debug.LogWarning($"[Vfx] missing prefab Resources/{def.ResourcePath}"); return null; }
            var go = Object.Instantiate(prefab, rig.transform);
            go.name = def.ResourcePath;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.playOnAwake = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            go.SetActive(false);
            return go;
        }

        private void Return(Live l)
        {
            foreach (var ps in l.Systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            l.Go.SetActive(false);
            if (l.Owner != null && loops.TryGetValue(l.Owner, out var cur) && cur == l) loops.Remove(l.Owner);
            if (!pool.TryGetValue(l.Def.ResourcePath, out var stack)) pool[l.Def.ResourcePath] = stack = new Stack<GameObject>();
            stack.Push(l.Go);
        }

        /// <summary>Ends every live effect at once (dev contact sheets, so one effect never bleeds into the next strip).</summary>
        public void ClearAll()
        {
            foreach (var l in live) Return(l);
            live.Clear();
            loops.Clear();
            ClearRt();
        }

        public void Dispose()
        {
            live.Clear();
            loops.Clear();
            pool.Clear();
            overlay.RemoveFromHierarchy();
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
            if (composite != null) Object.Destroy(composite);
            if (rig != null) Object.Destroy(rig);
        }
    }
}
