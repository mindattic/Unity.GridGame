using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GridGame.EditorTools
{
    /// <summary>
    /// GG-T269: one-time preparation of the effect prefabs imported from GridGame2026 into Resources/Vfx
    /// (Unity -batchmode -executeMethod GridGame.EditorTools.VfxImport.Prepare). Strips the old project's
    /// scripts and audio (cues come from vfx.json), makes every ParticleSystem scale with its transform,
    /// moves URP-only materials onto the built-in legacy particle shaders, and measures each effect
    /// (duration, loop, extent) into tools/vfx-measure.json for authoring vfx.json.
    /// </summary>
    public static class VfxImport
    {
        private const string PrefabDir = "Assets/Gaiden/Resources/Vfx";
        private const string LibDir = "Assets/Gaiden/Vfx";

        [MenuItem("Gaiden/Prepare Effect Prefabs")]
        public static void Prepare()
        {
            FixMaterials();
            var report = new SortedDictionary<string, object>();
            foreach (var path in Directory.GetFiles(PrefabDir, "*.prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
                foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.updateMode = AnimatorUpdateMode.UnscaledTime;
                // Renderers that pointed at URP's package default particle material now point at nothing (magenta).
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == null) { mats[i] = DefaultMaterial(); changed = true; }
                    if (mats.Length == 0) { mats = new[] { DefaultMaterial() }; changed = true; }
                    if (changed) r.sharedMaterials = mats;
                    if (r is ParticleSystemRenderer pr && pr.trailMaterial == null && pr.GetComponent<ParticleSystem>().trails.enabled) pr.trailMaterial = DefaultMaterial();
                }
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems)
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.stopAction = ParticleSystemStopAction.None;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                report[Path.GetFileNameWithoutExtension(path)] = Measure(root, systems);
                PrefabUtility.UnloadPrefabContents(root);
            }
            File.WriteAllText("tools/vfx-measure.json", JsonConvert.SerializeObject(report, Formatting.Indented));
            AssetDatabase.SaveAssets();
            Debug.Log($"[VfxImport] prepared {report.Count} effect prefabs");
        }

        private static object Measure(GameObject root, ParticleSystem[] systems)
        {
            bool loop = systems.Any(s => s.main.loop);
            float dur = 0f;
            foreach (var s in systems)
            {
                var m = s.main;
                float life = m.startLifetime.mode == ParticleSystemCurveMode.TwoConstants ? m.startLifetime.constantMax : m.startLifetime.constant;
                dur = Mathf.Max(dur, m.startDelay.constant + m.duration + life);
            }
            // Extent = twice the 80th-percentile reach of live particles (position plus half size) a beat into the
            // effect: renderer bounds were blown up by a few far-flung or ground-ring particles.
            var reach = new List<float>();
            var buf = new ParticleSystem.Particle[4096];
            foreach (var t in new[] { 0.35f, 0.7f })
            {
                foreach (var s in systems) s.Simulate(t, false, true);
                foreach (var s in systems)
                {
                    int n = s.GetParticles(buf);
                    bool local = s.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = local ? s.transform.TransformPoint(buf[i].position) : buf[i].position;
                        Vector3 d = p - root.transform.position;
                        reach.Add(Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) + buf[i].GetCurrentSize(s) * 0.5f);
                    }
                }
            }
            reach.Sort();
            float extent = reach.Count == 0 ? 1f : 2f * reach[Mathf.Clamp(Mathf.FloorToInt(reach.Count * 0.8f), 0, reach.Count - 1)];
            return new { loop, duration = System.Math.Round(dur, 2), extent = System.Math.Round(Mathf.Max(0.05f, extent), 3), systems = systems.Length };
        }

        private static Material defaultMat;

        /// <summary>The project's stand-in for URP's default particle material: legacy Alpha Blended, Unity's soft dot.</summary>
        private static Material DefaultMaterial()
        {
            const string path = LibDir + "/Materials/GaidenDefaultParticle.mat";
            if (defaultMat != null) return defaultMat;
            defaultMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (defaultMat != null) return defaultMat;
            defaultMat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            defaultMat.SetTexture("_MainTex", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            defaultMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            AssetDatabase.CreateAsset(defaultMat, path);
            return defaultMat;
        }

        /// <summary>URP-only shaders don't exist in this built-in-pipeline project: move them to the legacy particle
        /// shader with the same blend so they don't render magenta.</summary>
        private static void FixMaterials()
        {
            var additive = Shader.Find("Legacy Shaders/Particles/Additive");
            var alpha = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { LibDir }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                // By name only: shader.isSupported reads false for everything in batchmode.
                string sn = mat == null || mat.shader == null ? "" : mat.shader.name;
                if (mat == null || !(sn.Contains("Universal") || sn.Contains("URP") || sn == "Hidden/InternalErrorShader")) continue;
                // The URP shader is missing here, so HasProperty/GetFloat see nothing: read the saved values directly.
                var so = new SerializedObject(mat);
                var saved = so.FindProperty("m_SavedProperties");
                Texture tex = Saved(saved, "m_TexEnvs", "_BaseMap", p => p.FindPropertyRelative("m_Texture").objectReferenceValue as Texture)
                              ?? Saved(saved, "m_TexEnvs", "_MainTex", p => p.FindPropertyRelative("m_Texture").objectReferenceValue as Texture);
                Color col = Saved(saved, "m_Colors", "_BaseColor", p => (Color?)p.colorValue) ?? Color.white;
                bool add = Mathf.RoundToInt(Saved(saved, "m_Floats", "_Blend", p => (float?)p.floatValue) ?? 0f) == 2;
                mat.shader = add ? additive : alpha;
                if (tex != null) mat.SetTexture("_MainTex", tex);
                mat.SetColor("_TintColor", col * 0.5f);
                EditorUtility.SetDirty(mat);
                Debug.Log($"[VfxImport] {mat.name}: -> {mat.shader.name}");
            }
        }

        private static T Saved<T>(SerializedProperty saved, string list, string name, System.Func<SerializedProperty, T> read)
        {
            var arr = saved?.FindPropertyRelative(list);
            if (arr == null) return default;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == name) return read(e.FindPropertyRelative("second"));
            }
            return default;
        }
    }
}
