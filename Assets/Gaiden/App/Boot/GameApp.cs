using System;
using System.Collections;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// The whole app is constructed here, in code, at startup — the scene file is an empty shell.
    /// Owns the services (content, saves, audio), the UI Toolkit panel, the adaptive stage
    /// (GG-LAW-8, bible §26) and the screen router.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        /// <summary>The minimum stage: every screen is laid out to fit at least this (19.5:9 portrait).</summary>
        public const int DesignWidth = 1170;
        public const int DesignHeight = 2532;
        /// <summary>The widest (9:16) and tallest (9:21) phone the stage grows to fill; beyond them it is framed.</summary>
        public const int MaxStageWidth = 1424;
        public const int MaxStageHeight = 2730;
        /// <summary>Design-pixel margin around a framed stage, so its outline is fully on screen.</summary>
        private const float FramePad = 36f;

        /// <summary>The live stage size in design pixels; never below DesignWidth×DesignHeight.</summary>
        public static float StageWidth { get; private set; } = DesignWidth;
        public static float StageHeight { get; private set; } = DesignHeight;
        /// <summary>Raised after the stage changes size (window resize, harness resize).</summary>
        public static event Action StageResized;

        public static GameApp I { get; private set; }

        public ContentDb Content { get; private set; }
        /// <summary>The active player profile's save slots (Profiles/&lt;Name&gt;/Saves, GG-T99).</summary>
        public SaveStore Saves { get; private set; }
        /// <summary>The active player profile and its settings (bible §15.4, GG-T99).</summary>
        public ProfileSession Profile { get; private set; }
        /// <summary>The active profile's settings — the same whichever save slot is loaded, and on the title screen.</summary>
        public SettingsSave Settings => Profile?.Settings ?? fallbackSettings;
        private readonly SettingsSave fallbackSettings = new SettingsSave();
        public Audio Audio { get; private set; }
        public ScreenRouter Router { get; private set; }
        public VisualElement Stage { get; private set; }
        public VisualElement Overlay { get; private set; }
        public UIDocument Document { get; private set; }

        /// <summary>The save being played (null on the title screen).</summary>
        public SaveData Save { get; set; }
        public int Slot { get; set; } = -1;

        public List<string> ContentProblems { get; private set; } = new List<string>();
        public event Action<float> Ticked;

        private VisualElement letterbox;
        private VisualElement stageFrame;
        private Vector2 lastScreen;
        private float playClock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("GameApp");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        private void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;

            Content = LoadContent();
            Art.IndexSpells(Content.Abilities.Keys);
            OpenProfiles();
            Audio = Audio.Create(transform);

            BuildPanel();
            Router = new ScreenRouter(Stage.Q("screens"), Stage.Q("fade"));
            ApplySettings(); // the profile's settings hold on the title screen too, before any save is loaded

            if (CommandLine.Has("-noDiagonalSlip")) BattleConfig.DiagonalSlipEnabled = false;
            var skill = CommandLine.Value("-skill");
            if (skill != null && float.TryParse(skill, out var skillLevel)) BotSkillProfile.Level = skillLevel;
            BotSkillProfile.TelemetryEnabled = CommandLine.Has("-telemetry");

            var start = CommandLine.Value("-start");
            if (TestMode.Active) TestMode.Start();
            else if (DemoMode.Active) DemoMode.Start();
            else if (!string.IsNullOrEmpty(start) && Automation.StartScreens.TryGet(start, out var startScreen)) Router.ResetTo(startScreen);
            else Router.ResetTo(new TitleScreen());

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Automation.AutomationServer.TryStart(this);
#endif
        }

        private ContentDb LoadContent()
        {
            var db = ContentLoader.Load(name => Resources.Load<TextAsset>("Content/" + name)?.text);
            ContentProblems = ContentLoader.Validate(db, id => Art.Exists("Portraits/" + id));
            foreach (var p in ContentProblems) Debug.LogWarning("[Content] " + p);
            Debug.Log($"[GameApp] content: {db.Classes.Count} classes, {db.Abilities.Count} abilities, {db.Items.Count} items, {db.Stages.Count} stages");
            return db;
        }

        private void BuildPanel()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "GaidenPanel";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/GaidenTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(DesignWidth, DesignHeight);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.clearColor = true;
            settings.colorClearValue = Color.black;

            var docGo = new GameObject("UI");
            docGo.transform.SetParent(transform, false);
            docGo.SetActive(false);
            Document = docGo.AddComponent<UIDocument>();
            Document.panelSettings = settings;
            docGo.SetActive(true);

            var root = Document.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Theme"));
            root.style.backgroundColor = Color.black;
            Fonts.UseBody(root);

            letterbox = new VisualElement();
            letterbox.AddToClassList("fill");
            letterbox.AddToClassList("letterbox");
            root.Add(letterbox);

            stageFrame = new VisualElement { name = "stage-frame", pickingMode = PickingMode.Ignore };
            stageFrame.AddToClassList("stage-frame");
            letterbox.Add(stageFrame);

            Stage = new VisualElement { name = "stage" };
            Stage.AddToClassList("stage");
            letterbox.Add(Stage);

            var screens = new VisualElement { name = "screens" };
            screens.AddToClassList("fill");
            Stage.Add(screens);

            Overlay = new VisualElement { name = "overlay", pickingMode = PickingMode.Ignore };
            Overlay.AddToClassList("fill");
            Stage.Add(Overlay);

            var fade = new VisualElement { name = "fade", pickingMode = PickingMode.Ignore };
            fade.AddToClassList("fill");
            fade.AddToClassList("fade");
            Stage.Add(fade);

            FitStage();
#if DEVELOPMENT_BUILD
            if (CommandLine.Has("-traceInput"))
            {
                root.RegisterCallback<PointerDownEvent>(e => Debug.Log($"[Input] down at {e.position} target={(e.target as VisualElement)?.name}/{(e.target as VisualElement)?.GetType().Name} id={e.pointerId}"), TrickleDown.TrickleDown);
                root.RegisterCallback<PointerUpEvent>(e => Debug.Log($"[Input] up at {e.position} target={(e.target as VisualElement)?.name}/{(e.target as VisualElement)?.GetType().Name} id={e.pointerId}"), TrickleDown.TrickleDown);
                root.RegisterCallback<ClickEvent>(e =>
                {
                    var sb = new System.Text.StringBuilder();
                    for (var v = e.target as VisualElement; v != null; v = v.parent)
                        sb.Append($" > {v.GetType().Name}#{v.name}[{string.Join(".", v.GetClasses())}]{v.worldBound}");
                    Debug.Log("[Input] click path" + sb);
                }, TrickleDown.TrickleDown);
            }
#endif
        }

        /// <summary>Size of the stage for a screen aspect (width/height): never below the design size, grows the
        /// spare axis up to the widest/tallest supported phone, never scales non-uniformly (bible §26.3).</summary>
        public static Vector2 StageSizeFor(float screenAspect)
        {
            const float designAspect = DesignWidth / (float)DesignHeight;
            if (screenAspect >= designAspect)
                return new Vector2(Mathf.Clamp(Mathf.Round(DesignHeight * screenAspect), DesignWidth, MaxStageWidth), DesignHeight);
            return new Vector2(DesignWidth, Mathf.Clamp(Mathf.Round(DesignWidth / screenAspect), DesignHeight, MaxStageHeight));
        }

        /// <summary>Fit the stage inside the screen: phones fill it edge to edge; tablets and other out-of-range
        /// shapes get the nearest supported stage, centered and framed. Never stretched.</summary>
        private void FitStage()
        {
            var screen = new Vector2(Screen.width, Screen.height);
            if (screen == lastScreen && Stage.Q("screens") != null) return;
            lastScreen = screen;
            float screenAspect = screen.x / Mathf.Max(1f, screen.y);
            var size = StageSizeFor(screenAspect);
            bool resized = size.x != StageWidth || size.y != StageHeight;
            StageWidth = size.x; StageHeight = size.y;

            // Out of the phone range the stage is framed; leave room so the whole outline is visible on every side.
            float exactW = DesignHeight * screenAspect, exactH = DesignWidth / screenAspect;
            bool framed = exactW > MaxStageWidth + 2f || exactH > MaxStageHeight + 2f;
            float pad = framed ? FramePad : 0f;
            var fit = new Vector2(size.x + pad * 2f, size.y + pad * 2f);

            // Scale by the limiting axis so the whole stage (and frame) is visible.
            bool widthLimits = screen.x / fit.x <= screen.y / fit.y;
            Document.panelSettings.referenceResolution = new Vector2Int((int)fit.x, (int)fit.y);
            Document.panelSettings.match = widthLimits ? 0f : 1f;
            float logicalW = widthLimits ? fit.x : fit.y * screenAspect;
            float logicalH = widthLimits ? fit.x / screenAspect : fit.y;

            Stage.style.width = size.x; Stage.style.height = size.y;
            Stage.style.left = (logicalW - size.x) / 2f;
            Stage.style.top = (logicalH - size.y) / 2f;

            stageFrame.EnableInClassList("hidden", !framed);
            const float gap = 10f;
            stageFrame.style.left = (logicalW - size.x) / 2f - gap;
            stageFrame.style.top = (logicalH - size.y) / 2f - gap;
            stageFrame.style.width = size.x + gap * 2f;
            stageFrame.style.height = size.y + gap * 2f;

            ApplySafeArea(logicalW, logicalH);
            if (resized) StageResized?.Invoke();
        }

        /// <summary>Insets the screens where the device's safe area (notch / home bar) overlaps the stage (bible §26.5).</summary>
        private void ApplySafeArea(float logicalW, float logicalH)
        {
            var safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            // Test override: -safeArea <topPx>,<bottomPx> simulates a notch / home bar on desktop.
            var ov = CommandLine.Value("-safeArea");
            if (!string.IsNullOrEmpty(ov))
            {
                var parts = ov.Split(',');
                if (parts.Length == 2 && float.TryParse(parts[0], out var top) && float.TryParse(parts[1], out var bottom))
                    safe = new Rect(0, bottom, Screen.width, Screen.height - top - bottom);
            }
            float k = logicalH / Screen.height;
            float stageTop = (logicalH - StageHeight) / 2f, stageBottom = stageTop + StageHeight;
            float safeTop = (Screen.height - safe.yMax) * k, safeBottom = logicalH - safe.yMin * k;
            var screens = Stage.Q("screens");
            screens.style.top = Mathf.Max(0f, safeTop - stageTop);
            screens.style.bottom = Mathf.Max(0f, stageBottom - safeBottom);
        }

        private void Update()
        {
            FitStage();
            Tween.Advance();
            float dt = Tween.Dt;
            if (Save != null) { playClock += dt; if (playClock >= 1f) { Save.PlaySeconds += playClock; playClock = 0f; } }
            Router.Current?.Tick(dt);
            Ticked?.Invoke(dt);

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Router.Back();
        }

        // ── Session helpers ─────────────────────────────────────────────────────────

        public void Persist()
        {
            if (Save == null || Slot < 0) return;
            // SaveStore.Write never throws — it logs and reports failure via its return value (GG-T146).
            if (!Saves.Write(Slot, Save)) Toast("Couldn't save!");
        }

        public void Toast(string text, float seconds = 1.8f)
        {
            StartCoroutine(ToastRoutine(text, seconds));
        }

        private IEnumerator ToastRoutine(string text, float seconds)
        {
            var t = Ui.Text(text, "toast shadow", Overlay);
            t.name = "toast";
            t.style.opacity = 0;
            yield return null;
            t.style.opacity = 1;
            yield return new WaitForSecondsRealtime(seconds);
            t.style.opacity = 0;
            yield return new WaitForSecondsRealtime(0.25f);
            t.RemoveFromHierarchy();
        }

        public void ApplySettings()
        {
            var st = Settings;
            Audio?.Apply(st);
            Tween.Speed = Mathf.Clamp(st.BattleSpeed, 0.5f, 3f);
            Art.ApplyTheme(st.MenuTheme);
            Ui.ApplyMenuTheme(Document?.rootVisualElement);
            Fonts.ApplyFont(st.FontName);
        }

        /// <summary>Writes the active profile's settings to its settings.json (a settings change, bible §15.2).</summary>
        public void SaveSettings()
        {
            if (Profile == null) return;
            if (!Profile.SaveSettings()) Toast("Couldn't save settings!");
            SaveStore.SyncFiles();
        }

        // ── Player profiles (bible §15.4, GG-T99) ──────────────────────────────────

        /// <summary>Name of the profile `-demo` / `-testmode` play under, in a throwaway folder outside the real profiles.</summary>
        public const string ThrowawayProfile = "Throwaway";

        /// <summary>
        /// Picks the Profiles folder and the profile, then points <see cref="Saves"/> at that profile's slots:
        /// `-saveRoot DIR` (harness/tests) keeps everything under DIR/Profiles; Unity.Shared's `-profiles-root DIR` /
        /// MINDATTIC_PROFILES_ROOT name the folder outright; `-demo` / `-testmode` use a throwaway folder under the
        /// temp cache. Only the real folder (persistentDataPath/Profiles) gets the one-time legacy migration.
        /// `-profile NAME` selects (or creates) that profile; otherwise the last one used.
        /// </summary>
        private void OpenProfiles()
        {
            var saveRoot = CommandLine.Value("-saveRoot");
            bool throwaway = DemoMode.Active || TestMode.Active;
            Unity.Shared.Core.ProfileStore store;
            if (!string.IsNullOrEmpty(saveRoot)) store = Unity.Shared.Core.Profiles.UseRoot(System.IO.Path.Combine(saveRoot, Unity.Shared.Core.ProfileStore.FolderName));
            else if (Unity.Shared.Core.Profiles.OverrideRoot() != null) store = Unity.Shared.Core.Profiles.UseDataPath(Application.persistentDataPath);
            else if (throwaway)
            {
                var dir = System.IO.Path.Combine(Application.temporaryCachePath, "ThrowawayProfiles");
                try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
                catch (Exception e) { Debug.LogWarning("[Profiles] couldn't clear the throwaway folder: " + e.Message); }
                store = Unity.Shared.Core.Profiles.UseRoot(dir);
            }
            else
            {
                store = Unity.Shared.Core.Profiles.UseDataPath(Application.persistentDataPath);
                try
                {
                    var m = LegacySaveMigration.Run(Application.persistentDataPath, store);
                    if (m.Ran) Debug.Log($"[Profiles] migrated legacy saves into '{Unity.Shared.Core.ProfileStore.DefaultName}': copied [{string.Join(", ", m.Copied)}], kept [{string.Join(", ", m.Skipped)}], settings from slot {m.SeededFromSlot + 1}, old folder now {m.MovedTo}");
                }
                catch (Exception e) { Debug.LogError("[Profiles] legacy save migration failed (the old Saves folder is untouched): " + e.Message); }
            }

            var requested = CommandLine.Value("-profile") ?? (throwaway ? ThrowawayProfile : null);
            try { Profile = new ProfileSession(store, requested); }
            catch (ArgumentException e)
            {
                Debug.LogError($"[Profiles] -profile \"{requested}\": {e.Message} Using the last profile instead.");
                Profile = new ProfileSession(store);
            }
            Saves = new SaveStore(Profile.SavesFolder);
            SaveStore.SyncFiles();
            Debug.Log($"[Profiles] profile '{Profile.Name}' ({store.Root})");
        }

        /// <summary>Makes <paramref name="name"/> the active profile: its settings apply at once and its slots replace the old ones.</summary>
        public void SwitchProfile(string name)
        {
            Save = null; Slot = -1;
            Profile.Switch(name);
            ProfileChanged();
        }

        public void CreateProfile(string name) { Profile.Create(name); SaveStore.SyncFiles(); }

        public void RenameProfile(string name, string newName)
        {
            Profile.Rename(name, newName);
            ProfileChanged(); // the active profile's folder may have moved
        }

        public void DeleteProfile(string name)
        {
            Profile.Delete(name);
            ProfileChanged();
        }

        private void ProfileChanged()
        {
            Saves = new SaveStore(Profile.SavesFolder);
            SaveStore.SyncFiles();
            ApplySettings();
        }
    }

    public static class CommandLine
    {
        public static string Value(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static bool Has(string flag)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>
    /// `-demo`: boots straight into a scripted self-play loop (new game → battle, autoplayed → next
    /// stage → …, looping with a fresh save when the campaign ends or nothing is launchable) — this
    /// is the tour, the same flag name used for it in every MindAttic Unity project. The same name
    /// tools/harness.py campaign drives over HTTP. (The narrower, pre-existing meaning of "auto-resolve
    /// the current battle" was renamed to `-autobattle` — see BattleScreen.AutoPlay.)
    /// </summary>
    public static class DemoMode
    {
        public static bool Active => CommandLine.Has("-demo");
        /// <summary>`-demo -endless` (GG-T57): see <see cref="EndlessDemo"/>.</summary>
        public static bool Endless => Active && CommandLine.Has("-endless");

        public static void Start()
        {
            if (Endless) { EndlessDemo.Start(); return; }
            GameApp.I.Router.ResetTo(new TitleScreen());
            Restart();
        }

        /// <summary>Called from PostBattleScreen once the results are up: does whatever a human would click next.</summary>
        public static void AfterBattle(BattleReport report, string stageId)
        {
            if (Endless) { EndlessDemo.AfterBattle(report, stageId); return; }
            var app = GameApp.I;
            var c = app.Content;
            if (!report.Victory)
            {
                var theme = c.ThemeOf(stageId);
                string retryId = report.ExpeditionFailed && theme != null ? theme.Stages[0] : stageId;
                StageLauncher.Launch(retryId, true);
                return;
            }
            if (report.NextStageId != null) { StageLauncher.Launch(report.NextStageId, true); return; }
            var stages = c.CampaignStages();
            int idx = stages.FindIndex(s => s.Id == stageId);
            if (idx >= 0 && idx + 1 < stages.Count && Progression.IsStageUnlocked(app.Save, idx + 1) && Expeditions.CanLaunch(c, app.Save, stages[idx + 1].Id, out _))
            {
                StageLauncher.Launch(stages[idx + 1].Id, true);
                return;
            }
            Restart(); // campaign complete, or nothing left to launch — loop with a fresh save
        }

        /// <summary>Unlocks every class and shows the fighting-game-style select screen, which drives itself
        /// in demo mode: random tiles, so each run shows a different cross-section of the roster.</summary>
        private static void Restart()
        {
            GameApp.I.Router.Push(new CharacterSelectScreen(BeginRun, auto: true));
        }

        private static void BeginRun(List<string> party)
        {
            var app = GameApp.I;
            app.Save = Progression.NewGame(app.Content, "Demo", party);
            app.Slot = -1;
            // ResetTo fades out the still-visible CharacterSelectScreen before its mutate actually applies
            // (ScreenRouter.Go only mutates the stack synchronously when nothing is on screen yet), so pushing
            // the first stage immediately after would race it and land on top of the stale stack, then get wiped
            // out when the delayed Hub mutate finally runs. Wait for Hub to actually land before launching.
            void OnShown(GameScreen shown)
            {
                if (shown is not HubScreen) return;
                app.Router.Changed -= OnShown;
                var stages = app.Content.CampaignStages();
                if (stages.Count > 0) StageLauncher.Launch(stages[0].Id, false);
            }
            app.Router.Changed += OnShown;
            app.Router.ResetTo(new HubScreen());
        }
    }

    /// <summary>
    /// `-demo -endless` (GG-T57): an endless stream of fresh synthetic encounters with no Title, Hub, Camp or crawl —
    /// generate an encounter (<see cref="EncounterGenerator"/>, GG-T55) and a level-appropriate geared party for it
    /// (<see cref="EncounterParty"/>, GG-T56) → autoplayed battle → results (auto-advance) → the next encounter,
    /// forever. `-seed S` sets the base seed (default: the clock) and encounter i (0-based) uses seed S+i, so a run
    /// is reproducible encounter by encounter; `-count N` stops cleanly (quits) after N encounters. Every encounter
    /// runs on its own throwaway in-memory save on slot -1 (Persist never writes it, so no real slot is touched),
    /// and each synthetic `endless-` stage is pruned from the live ContentDb once it's done (the GG-T23 rule: a
    /// throwaway stage must never pile up in, or be borrowed back out of, the content).
    /// </summary>
    public static class EndlessDemo
    {
        /// <summary>Encounters finished (results reached).</summary>
        public static int Completed { get; private set; }
        private static int baseSeed;
        private static int? count;

        public static void Start()
        {
            var seedArg = CommandLine.Value("-seed");
            baseSeed = seedArg != null && int.TryParse(seedArg, out var s) ? s : Environment.TickCount;
            var countArg = CommandLine.Value("-count");
            count = countArg != null && int.TryParse(countArg, out var n) && n > 0 ? n : (int?)null;
            Completed = 0;
            Debug.Log($"[EndlessDemo] started: base seed {baseSeed}, {(count.HasValue ? count + " encounters" : "endless")}");
            Next();
        }

        /// <summary>Called from PostBattleScreen once the results are up.</summary>
        public static void AfterBattle(BattleReport report, string stageId)
        {
            Completed++;
            Debug.Log($"[EndlessDemo] encounter {Completed} ({stageId}): {(report.Victory ? "victory" : report.Escaped ? "escaped" : report.Retreated ? "retreated" : "defeat")}");
            if (count.HasValue && Completed >= count.Value) { Stop(); return; }
            Next();
        }

        private static void Next()
        {
            var app = GameApp.I;
            var c = app.Content;
            int seed = unchecked(baseSeed + Completed);
            // GG-T64: the same pure seed -> encounter build a headless Core replay uses (EndlessEncounter.Replay), so
            // `-demo -endless -seed S -count 1` reproduces exactly the encounter a record/report flagged with seed S.
            // Build prunes the previous encounter's stage first — safe now: its results screen reads name/theme from the report.
            var setup = EndlessEncounter.Build(c, seed);
            var stage = setup.Stage;

            // A fresh throwaway save per encounter: BattleFactory.Commit has somewhere to land (so the results screen
            // shows), nothing accumulates across encounters, and slot -1 means Persist never writes it.
            app.Save = Progression.NewGame(c, "Endless");
            app.Slot = -1;
            // The view's own randomness (the simulated cursor's overshoot/slip rolls) starts from the encounter seed too.
            UnityEngine.Random.InitState(seed);
            Debug.Log($"[EndlessDemo] encounter {Completed + 1} seed {seed}: {stage.Id} ({stage.Name}) Lv {stage.LevelOverride ?? 1} | party {string.Join(", ", setup.Party.ConvertAll(h => h.ClassId))}");
            // ResetTo, not Push: the stack never grows however long this runs. The autoplay bot's RNG is seeded from
            // the encounter seed (GG-T64), so its decisions match EndlessEncounter.Bot(seed) in a headless replay.
            app.Router.ResetTo(new BattleScreen(setup) { BotSeed = seed });
        }

        private static void Stop()
        {
            EndlessEncounter.Prune(GameApp.I.Content);
            Debug.Log($"[EndlessDemo] done: {Completed} encounters");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>
    /// `-testmode` (GG-T93, bot-only): self-plays the whole mechanical loop with no story, crawl, overworld
    /// or tutorial, to verify the game end to end — throwaway save on slot -1 (Persist never writes it) with
    /// the starting party → launch a stage → autoplayed battle → results → back to the Hub → CampRoutine
    /// (repair, Inn, potions, forge/equip, learn/assign) → launch again, forever. Unlike `-demo`, it visits
    /// camp and spends between every fight. Enemy level follows the party (Progression.StageRecommendedLevel)
    /// and stage choice lives in Meta (TestLoop.PickStage); this class only drives screens and logs one
    /// "[TestMode] cycle N | …" line per cycle (stage, party/enemy level, result, XP, gold, mats, camp actions).
    /// </summary>
    public static class TestMode
    {
        public static bool Active => CommandLine.Has("-testmode");
        /// <summary>Cycles finished (camp done and logged) — reported by the automation state dump.</summary>
        public static int Completed { get; private set; }
        public static string LastLine { get; private set; }
        private static TestCycle current;
        private static int losses;
        private static bool learned;

        public static void Start()
        {
            var app = GameApp.I;
            app.Save = TestLoop.NewSave(app.Content);
            app.Slot = -1;
            app.Settings.BattleSpeed = 3f; // the throwaway profile's settings, in memory only
            app.ApplySettings();
            Debug.Log($"[TestMode] started: throwaway save (slot -1), party {string.Join(", ", app.Save.Party)}, {app.Save.Gold}g");
            GoToCamp(runRoutine: false);
        }

        /// <summary>Called from PostBattleScreen once the results are up.</summary>
        public static void AfterBattle(BattleReport report, string stageId)
        {
            var app = GameApp.I;
            current?.Battle(app.Content, app.Save, report);
            losses = report.Victory ? 0 : losses + 1;
            GoToCamp(runRoutine: true);
        }

        private static void GoToCamp(bool runRoutine)
        {
            var app = GameApp.I;
            void OnShown(GameScreen shown)
            {
                if (shown is not HubScreen hub) return;
                app.Router.Changed -= OnShown;
                app.StartCoroutine(Camp(hub, runRoutine));
            }
            app.Router.Changed += OnShown;
            app.Router.ResetTo(new HubScreen());
        }

        private static IEnumerator Camp(HubScreen hub, bool runRoutine)
        {
            var app = GameApp.I;
            yield return new WaitForSecondsRealtime(1.0f);
            if (runRoutine && current != null)
            {
                current.RunCamp(app.Content, app.Save);
                LastLine = current.ToString();
                Completed++;
                Debug.Log(LastLine);
                learned |= TestLoop.LearnedAnAbility(current.Camp);
                if (!learned && Completed == TestLoop.CyclesThatMustLearnAnAbility)
                    Debug.LogWarning($"[TestMode] check failed: {Completed} cycles and camp never learned an ability (GG-T100)");
                if (app.Router.Current == hub) hub.Refresh();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            if (app.Router.Current != hub) yield break; // something else took over the screen; don't fight it
            Launch();
        }

        private static void Launch()
        {
            var app = GameApp.I;
            var c = app.Content; var s = app.Save;
            string stageId = TestLoop.PickStage(c, s, losses);
            if (stageId != null && !Expeditions.CanLaunch(c, s, stageId, out var why))
            {
                Debug.Log($"[TestMode] can't launch {stageId} ({why}) — abandoning the expedition");
                Expeditions.Abandon(s);
                stageId = TestLoop.PickStage(c, s, losses);
            }
            if (stageId == null) { Debug.LogError("[TestMode] no launchable stage"); return; }
            current = TestCycle.Begin(c, s, stageId, Completed + 1);
            Debug.Log($"[TestMode] cycle {current.Number} launch {stageId} | party Lv {current.PartyLevel} | enemy Lv {current.EnemyMin}-{current.EnemyMax}");
            StageLauncher.Launch(stageId, false);
        }
    }
}
