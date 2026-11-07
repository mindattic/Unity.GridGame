using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GridGame.EditorTools
{
    /// <summary>
    /// Command-line entry points (Unity -batchmode -executeMethod GridGame.EditorTools.GaidenBuild.X).
    /// The one scene is an empty shell — GameApp builds everything at runtime — so this file is the
    /// only "scene authoring" the project has.
    /// </summary>
    public static class GaidenBuild
    {
        public const string ScenePath = "Assets/Gaiden/Scenes/Main.unity";

        [MenuItem("Gaiden/Ensure Main Scene")]
        public static void EnsureScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("[Build] created " + ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.bundleVersion = Version();
            AssetDatabase.SaveAssets();
        }

        private static bool CommandLine(string flag) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), flag) >= 0;

        private static string Version()
        {
            try { return File.ReadAllText("version.txt").Trim(); } catch { return "1"; }
        }

        [MenuItem("Gaiden/Build Windows (Development)")]
        public static void BuildWindowsDev() => Build(BuildTarget.StandaloneWindows64, "Build/Windows/GridGameGaiden.exe", true);

        public static void BuildWindowsRelease() => Build(BuildTarget.StandaloneWindows64, "Build/WindowsRelease/GridGameGaiden.exe", false);

        public static void BuildAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.mindattic.gridgamegaiden");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            EditorUserBuildSettings.buildAppBundle = false;
            // -vulkan: Vulkan-only test build (diagnostics); default keeps Unity's automatic API list.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, !CommandLine("-vulkan"));
            if (CommandLine("-vulkan")) PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            Build(BuildTarget.Android, CommandLine("-vulkan") ? "Build/Android/GridGameGaiden-vulkan.apk" : "Build/Android/GridGameGaiden.apk", true);
        }

        public static void BuildWebGL()
        {
            // Uncompressed so any static file server (and Playwright) can serve it without special headers.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template = "APPLICATION:Minimal";
            PlayerSettings.defaultWebScreenWidth = 585;
            PlayerSettings.defaultWebScreenHeight = 1266;
            Build(BuildTarget.WebGL, "Build/WebGL", CommandLine("-dev"));
        }

        private static void Build(BuildTarget target, string output, bool development)
        {
            EnsureScene();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[Build] {s.result} {target} → {output} size={s.totalSize} errors={s.totalErrors} warnings={s.totalWarnings} time={s.totalTime}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Force-reimport every Resources asset so GaidenImport's settings apply (run after adding art).</summary>
        public static void ReimportArt()
        {
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/Gaiden/Resources" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            Debug.Log("[Build] reimported Resources art/audio");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Compile-only check for CI/agents: succeeds when scripts compiled (we got here at all).</summary>
        public static void CompileCheck()
        {
            EnsureScene();
            Debug.Log("[Build] compile OK");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    /// <summary>Import settings by folder so no asset ever needs hand-tuning in the Inspector.</summary>
    public sealed class GaidenImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Gaiden/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.sRGBTexture = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (assetPath.Contains("/Portraits/")) ti.maxTextureSize = 1024;
            // GG-T353: backdrops keep the art's resolution (1536x3072 originals): 4096 so Unity never halves them.
            // Still CompressedHQ (BC7 / ASTC 4x4, ~1 byte per pixel, no mips): ~4.5 MB of texture memory each.
            else if (assetPath.Contains("/Backgrounds/")) ti.maxTextureSize = 4096;
            else if (assetPath.Contains("/Icons/")) { ti.maxTextureSize = 128; ti.textureCompression = TextureImporterCompression.Uncompressed; }
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Gaiden/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            if (assetPath.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
            }
            ai.defaultSampleSettings = s;
        }
    }
}
