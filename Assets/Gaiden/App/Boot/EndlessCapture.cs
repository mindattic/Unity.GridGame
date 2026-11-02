using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using MindAttic.Capture;
using MindAttic.Capture.Unity;
using MindAttic.Telemetry;
using MindAttic.Telemetry.Unity;
using Newtonsoft.Json;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>
    /// GG-T58: endless capture. Under `-demo -endless` with MindAttic.Capture running (`-capture-config path.json`),
    /// every encounter is its own clip — the config's battle-start/battle-end rules cut it, or, when the config has no
    /// rule recording battles, this drives Start/StopRecording itself — and gets a sidecar beside its .mp4,
    /// `{clip}.encounter.json`: seed, location, enemy families per wave, level, party classes/levels, outcome, waves
    /// cleared, duration.
    ///
    /// It also keeps a 100+ encounter run from silently losing its recording. MindAttic.Capture's ResourceValve stops
    /// the whole engine for good when a limit trips (low RAM, as in GG-T23); the game would keep playing with every
    /// later clip quietly missing. Here a trip (or the engine going away for any other reason) is logged as an error,
    /// recorded to telemetry, and the run keeps playing; every encounter played while capture is down is logged and
    /// telemetered as NOT RECORDED, and a fresh engine is started on the same host and options as soon as the machine
    /// has recovered (available RAM back above the valve's limit plus headroom, free disk back above its limit) — mid
    /// encounter too, in which case the rest of that encounter is recorded and its sidecar says it's partial.
    /// Installs itself (no edits to the endless loop needed) and does nothing outside `-demo -endless` + capture.
    /// </summary>
    public sealed class EndlessCapture : MonoBehaviour
    {
        /// <summary>RAM above the valve's MinFreeMemoryMB before capture restarts, so it doesn't trip again at once.</summary>
        private const double MemoryHeadroomMB = 1024;
        /// <summary>Disk above the valve's MinFreeDiskGB before capture restarts.</summary>
        private const double DiskHeadroomGB = 5;
        private const float RecoveryPollSeconds = 5f;
        private const float RetryAfterFailureSeconds = 30f;
        private const float DownReminderSeconds = 60f;
        /// <summary>A rule-driven recording starts at battle-start, a moment before the screen change this sees.</summary>
        private const double MatchSlackBeforeSeconds = 10;
        private const double MatchSlackAfterSeconds = 2;

        private sealed class Encounter
        {
            public int Index;
            /// <summary>The encounter's battle, held weakly (GG-T61): an ended encounter waits here up to 15 minutes for
            /// its clip, and a strong reference kept every finished BattleScreen — its whole state and visual tree —
            /// alive that long (the 300-encounter run's leak check saw each one survive a full GC).</summary>
            public BattleScreen Screen
            {
                get => screen != null && screen.TryGetTarget(out var s) ? s : null;
                set => screen = value != null ? new WeakReference<BattleScreen>(value) : null;
            }
            private WeakReference<BattleScreen> screen;
            public StageDef Stage;
            public int Seed;
            public DateTimeOffset StartedAt;
            public DateTimeOffset? EndedAt;
            public string Outcome;
            public int WavesCleared;
            public List<object> Party;
            public bool CaptureDownAtStart;
            public bool CaptureLostDuring;
            public bool Partial;
            public string DriveTag;
            /// <summary>Usually one; two when capture stopped mid-encounter (the valve exports what it had) and then resumed.</summary>
            public readonly List<CaptureResult> Clips = new List<CaptureResult>();
            public int SidecarsWritten;
            public bool ReportedUnrecorded;
            public bool Done => EndedAt.HasValue && SidecarsWritten == Clips.Count;
        }

        private static EndlessCapture instance;

        private readonly ConcurrentQueue<CaptureResult> clips = new ConcurrentQueue<CaptureResult>();
        private readonly ConcurrentQueue<string> trips = new ConcurrentQueue<string>();
        private readonly List<Encounter> encounters = new List<Encounter>();
        private readonly HashSet<ICapture> hooked = new HashSet<ICapture>();
        private readonly List<ICapture> ownEngines = new List<ICapture>();
        private ScreenRouter router;
        private Encounter current;
        private int encounterCount;
        private bool rulesRecordBattles;

        // Capture-down bookkeeping.
        private bool down;
        private string downReason;
        private DateTimeOffset downSince;
        private int downTrips;
        private int unrecorded;
        private float nextRecoveryCheck, nextReminder;
        private Task restarting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Options.Enabled false is the config's kill switch ("enabled": false): capture is meant to be off, not down.
            if (instance != null || !DemoMode.Endless || CaptureUnity.Options == null || !CaptureUnity.Options.Enabled) return;
            var go = new GameObject("GridGame.EndlessCapture") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<EndlessCapture>();
        }

        private void Awake()
        {
            var rules = CaptureUnity.Options?.Rules;
            rulesRecordBattles = rules != null && rules.Any(r =>
                string.Equals(r.Action, "start-recording", StringComparison.OrdinalIgnoreCase) && Matches(r.On, "battle-start"));
            Application.quitting += OnQuitting;
            Debug.Log($"[EndlessCapture] on: one clip + .encounter.json sidecar per encounter ({(rulesRecordBattles ? "cut by the capture-config's battle rules" : "no battle-start recording rule in the config, so recording is driven here")}); a tripped capture valve restarts capture once resources recover.");
        }

        private static bool Matches(string pattern, string name)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern == "*") return true;
            if (pattern.EndsWith("*", StringComparison.Ordinal))
                return name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            return string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);
        }

        private void Update()
        {
            HookEngine();
            if (router == null && GameApp.I != null && GameApp.I.Router != null)
            {
                router = GameApp.I.Router;
                router.Changed += OnScreenChanged;
            }
            while (trips.TryDequeue(out var reason)) GoDown(reason, tripped: true);
            CheckEngineAlive();
            TrackEncounterEnd();
            while (clips.TryDequeue(out var clip)) MatchClip(clip);
            WriteReadySidecars();
            if (down) TryRecover();
        }

        // ------------------------------------------------------------ engine hooks

        /// <summary>Subscribes to whichever engine is current (the package's own, then each one restarted here).</summary>
        private void HookEngine()
        {
            var engine = Capture.Current;
            if (engine == null || !engine.Enabled || hooked.Contains(engine)) return;
            hooked.Add(engine);
            engine.Captured += r => clips.Enqueue(r); // background thread: handled in Update
            if (engine is CaptureEngine ce) ce.LimitReached += reason => trips.Enqueue(reason);
        }

        /// <summary>The engine can also go away without a valve reason (tape failure, disposal): never let that pass silently.</summary>
        private void CheckEngineAlive()
        {
            if (down) return;
            var engine = Capture.Current;
            if (engine != null && engine.Enabled) return;
            // Before the package's engine is up (it starts asynchronously on the first frame) nothing is down yet —
            // unless it never came up at all and encounters are already being played.
            if (hooked.Count == 0 && (encounterCount == 0 || Time.realtimeSinceStartup < 30f)) return;
            GoDown(hooked.Count == 0 ? "the capture engine never started"
                : engine?.Diagnostics?.StoppedBy ?? "the capture engine stopped (no valve reason given)", tripped: false);
        }

        private void GoDown(string reason, bool tripped)
        {
            if (down && !tripped) return;
            down = true;
            downTrips++;
            downReason = reason;
            downSince = DateTimeOffset.Now;
            nextRecoveryCheck = Time.unscaledTime + RecoveryPollSeconds;
            nextReminder = Time.unscaledTime + DownReminderSeconds;
            if (current != null && current.EndedAt == null) current.CaptureLostDuring = true;
            var mem = AvailableMemoryMB();
            Debug.LogError($"[EndlessCapture] !!! CAPTURE STOPPED (#{downTrips}) during encounter {encounterCount}: {reason}. " +
                           $"RAM available {Fmt(mem)} MB. The endless run KEEPS PLAYING; encounters are NOT RECORDED until capture restarts, " +
                           $"which happens automatically once {RecoveryNeed()}.");
            TelemetryUnity.RecordEvent("capture-stopped", new TelemetryData
            {
                ["reason"] = reason,
                ["valve"] = tripped,
                ["trip"] = downTrips,
                ["encounter"] = encounterCount,
                ["availableMemoryMB"] = mem.HasValue ? Math.Round(mem.Value) : (double?)null,
            });
        }

        // ------------------------------------------------------------ recovery

        private void TryRecover()
        {
            if (restarting != null && !restarting.IsCompleted) return;
            if (Time.unscaledTime >= nextReminder)
            {
                nextReminder = Time.unscaledTime + DownReminderSeconds;
                Debug.LogError($"[EndlessCapture] !!! capture still DOWN for {(DateTimeOffset.Now - downSince).TotalMinutes:0.0} min ({downReason}); " +
                               $"{unrecorded} encounter(s) unrecorded so far; RAM available {Fmt(AvailableMemoryMB())} MB; waiting until {RecoveryNeed()}.");
            }
            if (Time.unscaledTime < nextRecoveryCheck) return;
            nextRecoveryCheck = Time.unscaledTime + RecoveryPollSeconds;
            if (downReason != null && downReason.IndexOf("maximum duration", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // The user capped the capture length on purpose (-capture ... max duration): honour it, loudly.
                return;
            }
            if (!Recovered(out var why)) return;
            Restart(why);
        }

        private bool Recovered(out string state)
        {
            var limits = CaptureUnity.Options?.Limits;
            var mem = AvailableMemoryMB();
            var disk = FreeDiskGB();
            state = $"RAM available {Fmt(mem)} MB, disk free {Fmt(disk)} GB";
            if (limits == null || !limits.Enabled) return true;
            if (limits.MinFreeMemoryMB is double needMem && needMem > 0 && mem.HasValue && mem.Value < needMem + MemoryHeadroomMB) return false;
            if (limits.MinFreeDiskGB is double needDisk && needDisk > 0 && disk.HasValue && disk.Value < needDisk + DiskHeadroomGB) return false;
            // Encoder backlog limits clear on a fresh engine; give the machine a moment before retrying.
            return (DateTimeOffset.Now - downSince).TotalSeconds >= RetryAfterFailureSeconds;
        }

        private string RecoveryNeed()
        {
            var limits = CaptureUnity.Options?.Limits;
            var parts = new List<string>();
            if (limits != null && limits.Enabled)
            {
                if (limits.MinFreeMemoryMB is double m && m > 0) parts.Add($"RAM available >= {m + MemoryHeadroomMB:0} MB");
                if (limits.MinFreeDiskGB is double d && d > 0) parts.Add($"disk free >= {d + DiskHeadroomGB:0} GB");
            }
            parts.Add($"{RetryAfterFailureSeconds:0}s have passed");
            return string.Join(" and ", parts);
        }

        private void Restart(string state)
        {
            var runner = Resources.FindObjectsOfTypeAll<CaptureRunner>().FirstOrDefault();
            var options = CaptureUnity.Options;
            if (runner == null || options == null)
            {
                Debug.LogError("[EndlessCapture] !!! can't restart capture: the MindAttic.Capture host is gone. Encounters stay UNRECORDED.");
                nextRecoveryCheck = Time.unscaledTime + RetryAfterFailureSeconds;
                return;
            }
            Debug.LogWarning($"[EndlessCapture] resources recovered ({state}); restarting capture after {(DateTimeOffset.Now - downSince).TotalMinutes:0.0} min down.");
            restarting = StartEngine(runner, options);
        }

        private async Task StartEngine(CaptureRunner runner, CaptureOptions options)
        {
            CaptureEngine engine;
            try
            {
                engine = await CaptureEngine.StartNewAsync(runner, options); // also makes it Capture.Current
            }
            catch (Exception e)
            {
                Debug.LogError($"[EndlessCapture] !!! capture restart FAILED: {e.Message}. Retrying in {RetryAfterFailureSeconds:0}s; encounters stay UNRECORDED.");
                nextRecoveryCheck = Time.unscaledTime + RetryAfterFailureSeconds;
                downSince = DateTimeOffset.Now;
                return;
            }
            // Continues on the main thread (Unity's synchronization context).
            ownEngines.Add(engine);
            HookEngine();
            var minutes = (DateTimeOffset.Now - downSince).TotalMinutes;
            down = false;
            downReason = null;
            Debug.LogWarning($"[EndlessCapture] capture RESUMED (session {engine.SessionId}) after {minutes:0.0} min; {unrecorded} encounter(s) went unrecorded.");
            TelemetryUnity.RecordEvent("capture-resumed", new TelemetryData
            {
                ["downMinutes"] = Math.Round(minutes, 1),
                ["unrecordedEncounters"] = unrecorded,
                ["encounter"] = encounterCount,
            });
            // Mid-encounter: record the rest of it rather than waiting for the next one.
            if (current != null && current.EndedAt == null && current.Screen?.State != null && !current.Screen.State.IsOver)
            {
                current.Partial = true;
                BeginRecording(current);
            }
        }

        // ------------------------------------------------------------ encounters

        private void OnScreenChanged(GameScreen shown)
        {
            if (current != null && current.EndedAt == null && shown != current.Screen) EndEncounter(current);
            if (shown is BattleScreen battle && battle.State != null && (current == null || current.Screen != battle)
                && battle.State.Stage != null && battle.State.Stage.Id.StartsWith(EncounterGenerator.IdPrefix, StringComparison.Ordinal))
                BeginEncounter(battle);
        }

        private void BeginEncounter(BattleScreen battle)
        {
            var state = battle.State;
            var e = new Encounter
            {
                Index = ++encounterCount,
                Screen = battle,
                Stage = state.Stage,
                Seed = SeedOf(state.Stage.Id),
                StartedAt = DateTimeOffset.Now,
                CaptureDownAtStart = down,
                Party = state.Units.Where(u => u.IsHero).Select(u => (object)new { classId = u.Def?.Id, level = u.Level }).ToList(),
            };
            current = e;
            encounters.Add(e);
            // Keep a short history (a clip exports some seconds after its encounter ends); a 100+ run mustn't grow this forever.
            if (encounters.Count > 16) encounters.RemoveAll(x => x != e && x.EndedAt.HasValue && (DateTimeOffset.Now - x.EndedAt.Value).TotalMinutes > 15);
            if (down)
            {
                unrecorded++;
                e.ReportedUnrecorded = true;
                Debug.LogError($"[EndlessCapture] !!! encounter {e.Index} (seed {e.Seed}) is NOT BEING RECORDED: capture down since {downSince:HH:mm:ss} ({downReason}). {unrecorded} unrecorded so far.");
                TelemetryUnity.RecordEvent("capture-down", new TelemetryData
                {
                    ["reason"] = downReason,
                    ["encounter"] = e.Index,
                    ["unrecordedEncounters"] = unrecorded,
                });
            }
            else if (!rulesRecordBattles) BeginRecording(e);
        }

        /// <summary>Starts this encounter's clip: through the config's rules when they record battles, otherwise directly.</summary>
        private void BeginRecording(Encounter e)
        {
            if (rulesRecordBattles)
            {
                CaptureUnity.Raise("battle-start", new CaptureData { ["stage"] = e.Stage.Id });
                return;
            }
            e.DriveTag = "endless-" + unchecked((uint)e.Seed).ToString("x8");
            CaptureUnity.StartRecording(e.DriveTag);
        }

        private void TrackEncounterEnd()
        {
            if (current == null || current.EndedAt != null) return;
            var state = current.Screen?.State;
            if (state != null && state.IsOver) EndEncounter(current);
        }

        private void EndEncounter(Encounter e)
        {
            var state = e.Screen?.State;
            e.EndedAt = DateTimeOffset.Now;
            var phase = state?.Phase ?? BattlePhase.PlayerInput;
            e.Outcome = phase == BattlePhase.PlayerInput ? "abandoned" : phase.ToString().ToLowerInvariant();
            int waves = e.Stage?.Waves?.Count ?? 0;
            e.WavesCleared = phase == BattlePhase.Victory ? waves : Math.Max(0, Math.Min(state?.Wave ?? 0, waves));
            if (e.DriveTag != null && !down) CaptureUnity.StopRecording();
            if (down && !e.ReportedUnrecorded && e.CaptureLostDuring)
                Debug.LogError($"[EndlessCapture] !!! encounter {e.Index} (seed {e.Seed}) ended with capture DOWN: only the part before the stop was recorded.");
        }

        /// <summary>Pairs an exported clip with its encounter: by the tag when recording is driven here, otherwise by
        /// when the recording started (a clip's Timestamp) falling inside the encounter's span.</summary>
        private void MatchClip(CaptureResult r)
        {
            if (r == null || r.Skipped || r.Error != null || r.Files == null || r.Files.Count == 0) return;
            if (r.Kind != CaptureKind.Recording && r.Kind != CaptureKind.Clip && r.Kind != CaptureKind.Replay) return;
            Encounter best = null;
            foreach (var e in encounters)
                if (e.DriveTag != null && r.Tag == e.DriveTag) { best = e; break; }
            if (best == null)
            {
                var now = DateTimeOffset.Now;
                foreach (var e in encounters)
                {
                    bool inside = r.Timestamp >= e.StartedAt.AddSeconds(-MatchSlackBeforeSeconds)
                                  && r.Timestamp <= (e.EndedAt ?? now).AddSeconds(MatchSlackAfterSeconds);
                    if (inside && (best == null || e.StartedAt > best.StartedAt)) best = e;
                }
            }
            if (best == null)
            {
                Debug.LogWarning($"[EndlessCapture] clip {Path.GetFileName(r.Files[0])} matches no encounter; no sidecar written.");
                return;
            }
            best.Clips.Add(r);
        }

        private void WriteReadySidecars()
        {
            foreach (var e in encounters)
            {
                if (e.EndedAt == null) continue;
                while (e.SidecarsWritten < e.Clips.Count) WriteSidecar(e, e.Clips[e.SidecarsWritten++]);
            }
        }

        private void WriteSidecar(Encounter e, CaptureResult clip)
        {
            var video = clip.Files.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) ?? clip.Files[0];
            var path = Path.Combine(Path.GetDirectoryName(video) ?? "", Path.GetFileNameWithoutExtension(video) + ".encounter.json");
            var content = GameApp.I?.Content;
            var stage = e.Stage;
            var waves = new List<object>();
            if (stage?.Waves != null)
                for (int i = 0; i < stage.Waves.Count; i++)
                {
                    var w = stage.Waves[i];
                    var families = w.Enemies.Select(s => FamilyOf(content, s.Class)).Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    waves.Add(new
                    {
                        wave = i + 1,
                        families,
                        group = w.Group,
                        levels = w.Enemies.Select(s => s.Level).Distinct().OrderBy(l => l).ToList(),
                        enemies = w.Enemies.Select(s => s.Class).ToList(),
                    });
                }
            var sidecar = new
            {
                kind = "gridgame-endless-encounter",
                encounter = e.Index,
                seed = e.Seed,
                stage = stage?.Id,
                location = new { name = LocationName(stage?.Name), theme = stage?.Theme, background = stage?.Background },
                level = stage?.LevelOverride,
                waves,
                familiesPerWave = waves.Count == 0 ? null : stage.Waves.Select(w => w.Enemies.Select(s => FamilyOf(content, s.Class)).FirstOrDefault(f => f != null)).ToList(),
                party = e.Party,
                outcome = e.Outcome,
                wavesCleared = e.WavesCleared,
                waveCount = stage?.Waves?.Count ?? 0,
                durationSeconds = Math.Round(((e.EndedAt ?? DateTimeOffset.Now) - e.StartedAt).TotalSeconds, 2),
                clipSeconds = Math.Round(clip.Duration.TotalSeconds, 2),
                clipStartedAt = clip.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                startedAt = e.StartedAt.ToString("o", CultureInfo.InvariantCulture),
                endedAt = e.EndedAt?.ToString("o", CultureInfo.InvariantCulture),
                clip = Path.GetFileName(video),
                files = clip.Files.Select(Path.GetFileName).ToList(),
                clipPart = e.Clips.Count > 1 ? e.Clips.IndexOf(clip) + 1 : (int?)null,
                partial = e.Partial || e.CaptureLostDuring,
                captureLostDuringEncounter = e.CaptureLostDuring,
                captureResumedMidEncounter = e.Partial,
            };
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(sidecar, Formatting.Indented));
                Debug.Log($"[EndlessCapture] encounter {e.Index} (seed {e.Seed}, {e.Outcome}, {e.WavesCleared} waves): {Path.GetFileName(video)} + {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EndlessCapture] couldn't write the sidecar {path}: {ex.Message}");
            }
        }

        private static string FamilyOf(ContentDb content, string classId)
        {
            if (content == null || classId == null || !content.Classes.TryGetValue(classId, out var def)) return null;
            return string.IsNullOrEmpty(def.DropTable) ? null : def.DropTable;
        }

        /// <summary>The donor stage's own name: EncounterGenerator names an encounter "{donor} — Endless".</summary>
        private static string LocationName(string stageName)
        {
            const string suffix = " — Endless";
            if (stageName == null) return null;
            return stageName.EndsWith(suffix, StringComparison.Ordinal) ? stageName.Substring(0, stageName.Length - suffix.Length) : stageName;
        }

        /// <summary>The seed back out of an `endless-xxxxxxxx` stage id (the generator writes the seed as unsigned hex).</summary>
        private static int SeedOf(string stageId)
        {
            var hex = stageId.Substring(EncounterGenerator.IdPrefix.Length);
            return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? unchecked((int)u) : 0;
        }

        // ------------------------------------------------------------ shutdown

        /// <summary>The package's own runner shuts down only the engine it started; restarted ones are ours to export.</summary>
        private void OnQuitting()
        {
            Application.quitting -= OnQuitting;
            if (down)
                Debug.LogError($"[EndlessCapture] !!! quitting with capture DOWN ({downReason}); {unrecorded} encounter(s) went unrecorded this run.");
            else if (unrecorded > 0)
                Debug.LogWarning($"[EndlessCapture] {unrecorded} encounter(s) went unrecorded this run across {downTrips} capture stop(s).");
            foreach (var engine in ownEngines)
            {
                try
                {
                    if (!Task.Run(() => engine.DisposeAsync().AsTask()).Wait(TimeSpan.FromMinutes(15)))
                        Debug.LogWarning("[EndlessCapture] a restarted capture engine did not finish shutting down within 15 minutes.");
                }
                catch (AggregateException ex)
                {
                    Debug.LogError("[EndlessCapture] restarted capture shutdown failed: " + ex.GetBaseException().Message);
                }
            }
            ownEngines.Clear();
        }

        // ------------------------------------------------------------ system stats

        private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0", CultureInfo.InvariantCulture) : "?";

        private static double? FreeDiskGB()
        {
            try
            {
                var folder = Capture.Current?.SessionFolder;
                if (string.IsNullOrEmpty(folder)) folder = CaptureUnity.Options?.OutputRoot;
                var root = string.IsNullOrEmpty(folder) ? null : Path.GetPathRoot(folder);
                if (string.IsNullOrEmpty(root)) return null;
                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace / (1024.0 * 1024 * 1024) : (double?)null;
            }
            catch (Exception) { return null; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        /// <summary>Available physical memory, MB (the figure the capture valve's MinFreeMemoryMB checks); null off Windows.</summary>
        private static double? AvailableMemoryMB()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return null;
            try
            {
                var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                return GlobalMemoryStatusEx(ref s) ? s.AvailPhys / (1024.0 * 1024) : (double?)null;
            }
            catch (Exception) { return null; }
        }
    }
}
