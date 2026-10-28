#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App.Automation
{
    /// <summary>
    /// Development-build-only remote control so the game can be played and inspected by a script:
    /// a localhost HTTP endpoint (start the player with <c>-automation 7777</c>). Coordinates are
    /// design pixels (the live stage: 1170×2532 up to 1424 wide or 2730 tall, origin top-left).
    /// <para>
    /// Input (GG-T121): taps, drags and presses are dispatched straight into the UI Toolkit panel as
    /// pointer events — the same PointerDown/Move/Up (and the ClickEvent the panel synthesizes from them)
    /// that real input becomes, reaching the same handlers — rather than through an Input System virtual
    /// mouse. On desktop UI Toolkit's default event system drops all device input while the window is
    /// unfocused, so the old virtual-mouse path silently lost every tap whenever another player window on
    /// a shared desktop held focus, while still replying ok:true. The panel path doesn't care about OS
    /// focus, and every pointer reply now says what it hit: a tap that lands on nothing (off the stage, the
    /// bare stage background or a screen-transition fade) or whose events never reach the panel replies
    /// ok:false with an error.
    /// </para>
    /// </summary>
    public sealed class AutomationServer : MonoBehaviour
    {
        private HttpListener listener;
        private Thread thread;
        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private readonly List<string> logs = new List<string>();
        private string artifacts;
        private bool holdDrag;
        // Delivery receipts: a trickle-down listener on the panel root records each synthetic pointer
        // event as it is actually dispatched, so a reply can say whether (and where) it arrived.
        private EventBase sentEvent;
        private VisualElement sentTarget;
        private bool sentArrived;
        private Application.LogCallback logHandler;

        public static void TryStart(GameApp app)
        {
            var port = CommandLine.Value("-automation");
            if (string.IsNullOrEmpty(port)) return;
            var s = app.gameObject.AddComponent<AutomationServer>();
            s.Begin(int.Parse(port));
        }

        private void Begin(int port)
        {
            artifacts = CommandLine.Value("-artifacts") ?? Path.Combine(Application.persistentDataPath, "artifacts");
            Directory.CreateDirectory(artifacts);
            logHandler = (msg, stack, type) =>
            {
                if (type == LogType.Log && !msg.StartsWith("[")) return;
                lock (logs) { logs.Add($"{type}: {msg}" + (type == LogType.Exception || type == LogType.Error ? "\n" + stack : "")); if (logs.Count > 400) logs.RemoveAt(0); }
            };
            Application.logMessageReceivedThreaded += logHandler;
            Debug.developerConsoleEnabled = false;
            // The automation player must keep ticking (coroutines, tweens, layout) while another window
            // has focus; ProjectSettings already says so, this keeps it true whatever the build settings.
            Application.runInBackground = true;
            var root = GameApp.I.Document.rootVisualElement;
            root.RegisterCallback<PointerDownEvent>(Receipt, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(Receipt, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(Receipt, TrickleDown.TrickleDown);
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            thread = new Thread(Serve) { IsBackground = true, Name = "Automation" };
            thread.Start();
            Debug.Log($"[Automation] listening on {port}, artifacts → {artifacts}");
        }

        private void OnDestroy()
        {
            if (logHandler != null) Application.logMessageReceivedThreaded -= logHandler;
            try { listener?.Stop(); } catch { }
        }

        private void Update()
        {
            while (mainThread.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception)
                {
                    // Expected when OnDestroy() calls listener.Stop() while GetContext() is blocked;
                    // anything else should not silently end the listener thread.
                    if (!listener.IsListening) return;
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string body;
            int status = 200;
            try
            {
                var q = ctx.Request.QueryString;
                var path = ctx.Request.Url.AbsolutePath.Trim('/');
                var done = new ManualResetEventSlim();
                object result = null;
                Exception error = null;
                mainThread.Enqueue(() =>
                {
                    try
                    {
                        var r = Dispatch(path, q, v => { result = v; done.Set(); });
                        if (r != Pending) { result = r; done.Set(); }
                    }
                    catch (Exception e) { error = e; done.Set(); }
                });
                if (!done.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("main thread did not answer");
                if (error != null) throw error;
                body = result as string ?? JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception e)
            {
                status = 500;
                body = JsonConvert.SerializeObject(new { error = e.Message });
            }
            var bytes = Encoding.UTF8.GetBytes(body);
            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception) { /* client disconnected mid-response; nothing more to do */ }
            finally { try { ctx.Response.OutputStream.Close(); } catch (Exception) { } }
        }

        private static readonly object Pending = new object();

        private object Dispatch(string path, System.Collections.Specialized.NameValueCollection q, Action<object> reply)
        {
            var app = GameApp.I;
            switch (path)
            {
                case "ping": return new { ok = true, screen = app.Router.Current?.Id };
                case "state": return StateDump();
                case "boardaudit":
                    // GG-T97: drawn-token audit totals (see BoardAudit); ?reset=1 zeroes them first.
                    if (q["reset"] == "1") BoardAudit.Reset();
                    if (q["now"] == "1" && app.Router.Current is BattleScreen ab && !ab.Busy) return new { audit = BoardAudit.Snapshot(), now = ab.AuditTokens(true, rendered: true) };
                    return new { audit = BoardAudit.Snapshot() };
                case "logs":
                    lock (logs) { var copy = logs.ToList(); if (q["clear"] == "1") logs.Clear(); return copy; }
                case "record":
                    StartCoroutine(Record(SafeName(q["name"], "rec"), (int)F(q["frames"], 60), Math.Max(1, (int)F(q["every"], 1)), F(q["step"], 1f / 30f), reply));
                    return Pending;
                case "screenshot":
                    StartCoroutine(Screenshot(SafeName(q["name"], DateTime.Now.ToString("HHmmss")), reply));
                    return Pending;
                case "tap":
                    StartCoroutine(Tap(F(q["x"]), F(q["y"]), reply));
                    return Pending;
                case "drag":
                    holdDrag = q["hold"] == "1";
                    StartCoroutine(Drag(ParsePoints(q["path"]), F(q["step"], 0.03f), reply));
                    return Pending;
                case "click":
                {
                    var e = app.Stage.Q(q["name"]);
                    // Q(name) isn't filtered by visibility like clicktext's Query<>() is — without this,
                    // a hidden/zero-size/off-stage match (e.g. mid-transition, or a same-named element
                    // elsewhere not currently shown) taps nothing and still replies ok:true (GG-T16).
                    if (e == null || e.resolvedStyle.display == DisplayStyle.None || e.worldBound.width <= 0 || !Opaque(e))
                        return new { error = $"no visible element named '{q["name"]}'" };
                    var c = DesignPoint(e.worldBound.center);
                    StartCoroutine(Tap(c.x, c.y, reply));
                    return Pending;
                }
                case "scrollto":
                {
                    // Scrolls the nearest ScrollView ancestor until the named element is in view, so a
                    // following click lands on it rather than on whatever sits at its off-viewport
                    // coordinates (GG-T110: most of the party-select grid starts below the fold).
                    var e = app.Stage.Q(q["name"]);
                    if (e == null) return new { error = $"no element named '{q["name"]}'" };
                    var sv = e.GetFirstAncestorOfType<ScrollView>();
                    if (sv == null) return new { ok = true, scrolled = false };
                    sv.ScrollTo(e);
                    return new { ok = true, scrolled = true };
                }
                case "clicktext":
                {
                    var target = q["text"];
                    var label = app.Stage.Query<Label>().ToList().FirstOrDefault(l => l.text != null && l.text.Contains(target) && l.resolvedStyle.display != DisplayStyle.None && l.worldBound.width > 0);
                    var btn = app.Stage.Query<Button>().ToList().FirstOrDefault(b => b.text != null && b.text.Contains(target) && b.worldBound.width > 0);
                    VisualElement hit = (VisualElement)btn ?? label;
                    if (hit == null) return new { error = $"no visible text '{target}'" };
                    var c = DesignPoint(hit.worldBound.center);
                    StartCoroutine(Tap(c.x, c.y, reply));
                    return Pending;
                }
                case "pick":
                {
                    // Diagnostics for a tap that does nothing (GG-T16/GG-T95): what UI Toolkit would hit at a
                    // design point and whether the window has focus. Since GG-T121 automation input no longer
                    // depends on focus (it goes straight into the panel), but `focused` still tells a real
                    // player's dropped clicks apart from a game-side problem.
                    var panelPoint = ToPanel(F(q["x"]), F(q["y"]));
                    var hit = app.Stage.panel?.Pick(panelPoint);
                    var chain = new List<string>();
                    for (var e = hit; e != null; e = e.parent) chain.Add($"{e.GetType().Name}#{e.name}.{string.Join(".", e.GetClasses())}");
                    return new
                    {
                        chain, focused = Application.isFocused, hitsNothing = HitsNothing(hit), delivery = "panel",
                        screen = new[] { Screen.width, Screen.height }, panelPoint = panelPoint.ToString(),
                    };
                }
                case "back": app.Router.Back(); return new { ok = true };
                case "wait":
                    StartCoroutine(WaitIdle(F(q["ms"], 0) / 1000f, reply));
                    return Pending;
                case "newgame":
                {
                    int slot = (int)F(q["slot"], 0);
                    app.Save = Progression.NewGame(app.Content, q["name"] ?? "Bot");
                    app.Slot = slot;
                    if (q["gold"] != null) app.Save.Gold = (int)F(q["gold"]);
                    // npcs=1: every Camp NPC already met (GG-T119); npcs=merchant,blacksmith: just those (GG-T175).
                    if (q["npcs"] == "1") CampNpcs.JoinAll(app.Save);
                    else if (q["npcs"] != null)
                        foreach (var n in q["npcs"].Split(',')) app.Save.Flags.Add(ContentLoader.NpcFlagPrefix + n.Trim());
                    // seen=blacksmith,shop: those camp tips already dismissed, to reach a later onboarding step (GG-T177).
                    if (q["seen"] != null)
                        foreach (var t in q["seen"].Split(',')) CampTips.MarkSeen(app.Save, t.Trim());
                    app.ApplySettings();
                    app.Persist();
                    app.Router.ResetTo(new HubScreen());
                    return new { ok = true };
                }
                case "devequipall":
                {
                    // Test-only (GG-T39): a fresh save starts every hero bare-handed (weapons are bought
                    // at the Blacksmith), so harness checks of weapon-durability UI need something equipped
                    // first. Mirrors Meta.Equipment.Equip (grants + equips) across the whole roster.
                    if (app.Save == null) return new { error = "no save" };
                    var itemId = q["item"] ?? "eq_sword_iron";
                    // Equips into the item's own slot, so armor set pieces (GG-T182) work too.
                    var equipSlot = app.Content.TryItem(itemId)?.Slot ?? EquipmentSlot.Weapon;
                    int equipped = 0;
                    foreach (var h in app.Save.Roster)
                    {
                        app.Save.AddItem(itemId, 1);
                        if (!Equipment.Equip(app.Content, app.Save, h, equipSlot, itemId).Ok) continue;
                        equipped++;
                        // wear=0.5: battle-worn to that fraction of max (GG-T175's Blacksmith onboarding check).
                        if (q["wear"] != null && equipSlot == EquipmentSlot.Weapon)
                            h.WeaponDurability = Math.Max(1, (int)(Repair.EffectiveMax(app.Content.Item(itemId), h.WeaponRepairs, h.WeaponMastery) * F(q["wear"])));
                    }
                    return new { ok = equipped > 0, equipped };
                }
                case "devgive":
                {
                    // Test-only: grants bag items for harness checks that need a specific reagent/material
                    // without grinding a rare drop (e.g. GG-T45 potency-brew UI).
                    if (app.Save == null) return new { error = "no save" };
                    var itemId = q["item"];
                    if (itemId == null) return new { error = "missing item" };
                    app.Save.AddItem(itemId, (int)F(q["count"], 1));
                    return new { ok = true, count = app.Save.Count(itemId) };
                }
                case "goto":
                    if (!StartScreens.TryGet(q["screen"], out var screen)) return new { error = "unknown screen" };
                    app.Router.Push(screen);
                    return new { ok = true };
                case "battle":
                {
                    if (app.Save == null) { app.Save = Progression.NewGame(app.Content, "Bot"); app.Slot = 2; }
                    var stage = q["stage"] ?? app.Content.CampaignStages()[0].Id;
                    app.Router.Push(new BattleScreen(stage));
                    return new { ok = true, stage };
                }
                case "randombattle":
                {
                    // Battle-clip demo: picks a random real stage (any theme, main or postgame) and a
                    // random wave from it — one enemy family grouping, per the house "never mix families
                    // in a wave" convention — as a single-wave skirmish. Registers a throwaway synthetic
                    // StageDef in the in-memory ContentDb (never touches the JSON on disk) so the normal
                    // BattleFactory.Build path — party, LevelOverride-aware recommended level, everything —
                    // works exactly like a real stage. Launch with -capture-config pointing at
                    // capture.json (its "enabled" still true) to turn each one into its own short
                    // clip via its battle-start/battle-end rules — MindAttic.Capture only reads that
                    // file when told to with -capture-config, never just because it sits in
                    // StreamingAssets (see tools/harness.py's battle_clips()).
                    // Earlier calls' throwaway stages must not re-enter the pool — a prior run of this
                    // same endpoint, left running, fed itself its own synthetic stages as "random real"
                    // sources within a few dozen calls, compounding (skirmish of a skirmish of a...) and
                    // collapsing variety toward whatever got picked early. Prune them before picking.
                    // Safe while the last skirmish's level-up/results screens are still up (GG-T127): those read the
                    // stage's name and theme from the BattleReport, and LevelUpScreen stops its fanfare when popped.
                    const string SynthPrefix = "skirmish-";
                    foreach (var oldId in app.Content.Stages.Keys.Where(k => k.StartsWith(SynthPrefix)).ToList())
                        app.Content.Stages.Remove(oldId);
                    var realStages = app.Content.Stages.Values.Where(s => !s.IsTest && s.Waves.Count > 0).ToList();
                    if (realStages.Count == 0) return new { error = "no stages available" };
                    var src = realStages[UnityEngine.Random.Range(0, realStages.Count)];
                    var wave = src.Waves[UnityEngine.Random.Range(0, src.Waves.Count)];
                    int srcIndex = app.Content.CampaignStages().FindIndex(x => x.Id == src.Id);
                    int level = src.LevelOverride ?? (srcIndex >= 0 ? Progression.RecommendedLevel(srcIndex) : 5);
                    var synthId = SynthPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
                    app.Content.Stages[synthId] = new StageDef
                    {
                        Id = synthId, Name = $"{src.Name} Skirmish", Theme = src.Theme, Background = src.Background,
                        Music = src.Music, IsTest = false, LevelOverride = level, Waves = new List<WaveDef> { wave },
                    };
                    if (app.Save == null) { app.Save = Progression.NewGame(app.Content, "Bot"); app.Slot = 2; }
                    // ResetTo, not Push: this is meant to be called over and over for "tons of clips" —
                    // Push would grow the screen stack by two (battle, then its postbattle) every time.
                    app.Router.ResetTo(new BattleScreen(synthId));
                    return new { ok = true, stage = synthId, source = src.Id, level, enemies = wave.Enemies.Select(e => e.Class).ToList() };
                }
                case "autoplay":
                    if (app.Router.Current is BattleScreen bs) { bs.AutoPlay = q["on"] != "0"; return new { ok = true }; }
                    return new { error = "not in battle" };
                case "speed":
                    Tween.Speed = F(q["x"], 1);
                    if (app.Save != null) app.Save.Settings.BattleSpeed = Tween.Speed;
                    return new { ok = true, Tween.Speed };
                case "cmd":
                {
                    if (!(app.Router.Current is BattleScreen b)) return new { error = "not in battle" };
                    if (!b.CanCommand()) return new { error = "busy" };
                    BattleCommand cmd = null;
                    switch (q["type"])
                    {
                        case "shield": cmd = new ShieldCommand(); break;
                        case "move": cmd = new MoveCommand { HeroId = (int)F(q["hero"]), Path = ParseTiles(q["path"]) }; break;
                        case "use": cmd = new UseSlotCommand { HeroId = (int)F(q["hero"]), Slot = (int)F(q["slot"]), Target = new Coord((int)F(q["x"]), (int)F(q["y"])) }; break;
                    }
                    if (cmd == null) return new { error = "bad type" };
                    b.Commit(cmd);
                    StartCoroutine(WaitIdle(0, reply));
                    return Pending;
                }
                case "element":
                {
                    var e = app.Stage.Q(q["name"]);
                    if (e == null) return new { error = "not found" };
                    var tl = DesignPoint(e.worldBound.min); var br = DesignPoint(e.worldBound.max);
                    return new { x = tl.x, y = tl.y, w = br.x - tl.x, h = br.y - tl.y, visible = e.resolvedStyle.display != DisplayStyle.None };
                }
                case "layout": return LayoutAudit();
                case "texts":
                    return app.Stage.Query<TextElement>().ToList()
                        .Where(t => !string.IsNullOrEmpty(t.text) && t.resolvedStyle.display != DisplayStyle.None && t.resolvedStyle.visibility == Visibility.Visible && t.worldBound.width > 0 && Opaque(t))
                        .Select(t => t.text).ToList();
                case "names":
                {
                    var prefix = q["prefix"] ?? "";
                    return app.Stage.Query<VisualElement>().ToList().Where(e => e.name != null && e.name.StartsWith(prefix) && e.worldBound.width > 0).Select(e => e.name).ToList();
                }
                case "music": return new { track = app.Audio.CurrentTrack };
                case "cues":
                {
                    var list = app.Audio.CueLog.ToList();
                    if (q["clear"] == "1") app.Audio.CueLog.Clear();
                    return list;
                }
                case "setting":
                {
                    if (app.Save == null) return new { error = "no save" };
                    var st = app.Save.Settings;
                    bool on = q["value"] == "1" || q["value"] == "true";
                    switch (q["key"])
                    {
                        case "colorblind": st.ColorblindMode = on; break;
                        case "reducemotion": st.ReduceMotion = on; break;
                        case "dragtimer": st.DragTimer = on; break;
                        case "mutemusic": st.MuteMusic = on; break;
                        case "mutesfx": st.MuteSfx = on; break;
                        case "cutins": st.PincerCutIns = on; break;
                        case "confirm": st.ConfirmActions = on; break;
                        case "menutheme": st.MenuTheme = q["value"]; break; // GG-T185: value=Slate|Classic|...
                        default: return new { error = "unknown key" };
                    }
                    app.ApplySettings();
                    return new { ok = true };
                }
                case "givemana":
                    if (app.Router.Current is BattleScreen gm) { gm.DevGiveMana(int.TryParse(q["n"], out var gn) ? gn : 6, int.TryParse(q["charge"], out var gc) ? gc : -1); return new { ok = true, bank = gm.State.Bank.ToString(), charge = gm.State.ManaCharge }; }
                    return new { error = "not in battle" };
                case "suggest":
                {
                    if (!(app.Router.Current is BattleScreen sb2)) return new { error = "not in battle" };
                    var cmd = new GridGame.Core.Bot.HeroBot(5).BestMove(sb2.State);
                    if (cmd == null) return new { error = "no move" };
                    var pv = BattleEngine.PreviewMove(sb2.State, cmd.HeroId, cmd.Path);
                    var hero = sb2.State.UnitById(cmd.HeroId);
                    return new { hero = cmd.HeroId, from = new[] { hero.Pos.X, hero.Pos.Y }, path = cmd.Path.Select(c => new[] { c.X, c.Y }).ToList(), pincers = pv.Pincers.Count,
                                 moved = pv.Positions.Where(kv => kv.Key != cmd.HeroId && sb2.State.UnitById(kv.Key).Pos != kv.Value).Select(kv => new { id = kv.Key, x = kv.Value.X, y = kv.Value.Y }).ToList() };
                }
                case "style":
                {
                    var e = app.Stage.Q(q["name"]);
                    if (e == null) return new { error = "not found" };
                    var rs = e.resolvedStyle;
                    string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
                    return new { border = Hex(rs.borderTopColor), borderWidth = rs.borderTopWidth, background = Hex(rs.backgroundColor), tint = Hex(rs.unityBackgroundImageTintColor), rs.opacity, rs.width, rs.height, children = e.childCount, text = (e as TextElement)?.text, key = e.viewDataKey };
                }
                case "child_style":
                {
                    // style of the Nth child of a named element (unit frames are the 2nd child of unit-<id>)
                    var e = app.Stage.Q(q["name"]);
                    int idx = (int)F(q["index"], 0);
                    if (e == null || idx >= e.childCount) return new { error = "not found" };
                    var c = e[idx];
                    if (q["child"] != null) { int ci = (int)F(q["child"], 0); if (ci >= c.childCount) return new { error = "not found" }; c = c[ci]; }
                    var rs = c.resolvedStyle;
                    string Hex(Color col) => "#" + ColorUtility.ToHtmlStringRGBA(col);
                    return new { border = Hex(rs.borderTopColor), background = Hex(rs.backgroundColor), children = c.childCount };
                }
                case "devcast":
                    if (app.Router.Current is BattleScreen dcs) { int caster = dcs.DevQueueCast((int)F(q["target"]), q["ability"], (int)F(q["ticks"], 30)); return new { ok = caster >= 0, caster }; }
                    return new { error = "not in battle" };
                case "devamp":
                    if (app.Router.Current is BattleScreen dab) { var ampAt = new Coord((int)F(q["x"]), (int)F(q["y"])); if (!dab.State.FreeForSpawn(ampAt) || !dab.State.ItemFree(ampAt)) return new { error = "tile not free" }; dab.State.Amplifier = ampAt; dab.SyncAll(); dab.Feed(BattleScreen.AmpSpawnedFeed, BattleScreen.AmpFeedColor); return new { ok = true }; }
                    return new { error = "not in battle" };
                case "democaster":
                    if (app.Router.Current is BattleScreen democ) { democ.DemoCaster(); return new { ok = true }; }
                    return new { error = "not in battle" };
                case "democutin":
                    if (app.Router.Current is BattleScreen dc) { dc.DemoCutIn(); return new { ok = true }; }
                    return new { error = "not in battle" };
                case "demosurprise":
                    if (app.Router.Current is BattleScreen dsp) { dsp.DemoSurprise(q["speaker"] ?? "captain", q["line"] ?? "Reinforcements!"); return new { ok = true }; }
                    return new { error = "not in battle" };
                case "devhp":
                    if (app.Router.Current is BattleScreen dh) return new { ok = dh.DevHp((int)F(q["unit"]), (int)F(q["hp"], 1)) };
                    return new { error = "not in battle" };
                case "devweapon":
                    if (app.Router.Current is BattleScreen dw) return new { ok = dw.DevWeaponDurability((int)F(q["unit"]), (int)F(q["durability"], 1)) };
                    return new { error = "not in battle" };
                case "setbar":
                    if (app.Router.Current is BattleScreen setb) return new { ok = setb.DevSetBar((int)F(q["unit"]), (int)F(q["slot"]), q["entry"]) };
                    return new { error = "not in battle" };
                case "buff":
                    if (app.Router.Current is BattleScreen bb) return new { ok = bb.DevBuff((int)F(q["unit"]), q["id"]) };
                    return new { error = "not in battle" };
                case "gfx":
                    return new
                    {
                        api = SystemInfo.graphicsDeviceType.ToString(), SystemInfo.graphicsDeviceName, SystemInfo.graphicsShaderLevel,
                        colorSpace = QualitySettings.activeColorSpace.ToString(),
                        clear = app.Document.panelSettings.clearColor, camera = Camera.allCamerasCount,
                        uiShader = Shader.Find("Hidden/UIElements/Runtime") != null || Shader.Find("Hidden/UIE-Runtime") != null,
                        panelTarget = app.Document.panelSettings.targetTexture != null,
                    };
                case "shaders":
                    return Resources.FindObjectsOfTypeAll<Shader>().Where(sh => sh.name.Contains("UI") || sh.name.Contains("UIE") || sh.name.Contains("Text"))
                        .Select(sh => sh.name + (sh.isSupported ? "" : " [UNSUPPORTED]")).Distinct().ToList();
                case "panelclear":
                    app.Document.panelSettings.clearColor = q["on"] == "1";
                    return new { ok = true };
                case "camera":
                    foreach (var cam in Camera.allCameras) cam.enabled = q["on"] == "1";
                    return new { ok = true };
                case "banner":
                    return new { text = (app.Router.Current as BattleScreen)?.BannerText };
                case "press":
                    StartCoroutine(Press(F(q["x"]), F(q["y"]), F(q["ms"], 600) / 1000f, reply));
                    return Pending;
                case "release":
                {
                    var up = Send(lastHeld, PointerPhase.Up);
                    StartCoroutine(Frames(3, () => reply(
                        up.HitsNothing ? new { ok = false, error = "release hit nothing", target = up.Target }
                        : !up.Arrived ? new { ok = false, error = "release not delivered to the panel", target = up.Target }
                        : new { ok = true, error = (string)null, target = up.Target })));
                    return Pending;
                }
                case "camp":
                {
                    // GG-T136 full-loop harness: what each camp system can actually do with this save right now.
                    var c = app.Content; var s = app.Save;
                    if (s == null) return new { error = "no save" };
                    // Only stations the player can use now (GG-T174/T175): a keeper who hasn't joined, or the Blacksmith's
                    // still-closed workshop, offers nothing.
                    bool StationOpen(CraftStation st) => st == CraftStation.Forge ? CampUnlocks.NpcOpen(s, CampNpcs.Blacksmith) && CampUnlocks.SmithWorkshopOpen(s)
                        : CampUnlocks.NpcOpen(s, st == CraftStation.Alchemy ? CampNpcs.Alchemist : CampNpcs.Enchantress);
                    object Craftable(CraftStation st) => Crafting.Recipes(c, st)
                        .Where(r => StationOpen(st))
                        .Where(r => RecipeDiscovery.IsDiscovered(s, r) && Forging.ArmorOpen(c, s, r) && Crafting.IsUnlocked(s, r) && Crafting.HasInputs(s, r) && s.Gold >= r.Gold)
                        .Select(r => new { r.Id, r.Result, upgrade = r.BaseItem != null }).ToList();
                    var active = Bounties.Active(c, s);
                    return new
                    {
                        s.Gold,
                        sellable = Shop.Sellable(c, s).Select(i => new { i.Id, type = i.Type.ToString(), price = Shop.SellPrice(i), count = s.Count(i.Id) }).ToList(),
                        forge = Craftable(CraftStation.Forge), alchemy = Craftable(CraftStation.Alchemy), enchant = Craftable(CraftStation.Enchant),
                        worn = Repair.MostWorn(c, s) is var mw && mw != null ? new { row = mw.RowName, mw.Durability, mw.Max } : null,
                        smith = CampUnlocks.NpcOpen(s, CampNpcs.Blacksmith), workshop = CampUnlocks.SmithWorkshopOpen(s),
                        weapons = s.Party.Select(s.Hero).Where(h => h != null).ToDictionary(h => h.ClassId, h => h.WeaponId),
                        firstForge = Forging.First(c, s) is var ff && ff != null ? new { recipe = ff.Recipe.Id, weapon = ff.Weapon.Id, hero = ff.Hero.ClassId, ready = ff.Ready } : null,
                        firstArmor = Forging.FirstArmor(c, s) is var fa && fa != null ? new { recipe = fa.Recipe.Id, piece = fa.Weapon.Id, hero = fa.Hero.ClassId, ready = fa.Ready } : null,
                        armor = s.Party.Select(s.Hero).Where(h => h != null).ToDictionary(h => h.ClassId, h => h.Armor.Values.Select(ea => ea.ItemId).ToList()),
                        setRecipes = Crafting.Recipes(c, CraftStation.Forge).Count(r => Forging.IsSetArmor(c, r) && Forging.ArmorOpen(c, s, r)),
                        // GG-T179: the Enchantress (joins after the first armor) and the guided first armor enchant.
                        enchantress = CampUnlocks.NpcOpen(s, CampNpcs.Enchantress),
                        firstEnchant = ArmorEnchanting.First(c, s) is var fe && fe != null ? new { enchant = fe.Enchant.Id, row = fe.Target.RowName, piece = fe.Target.Item.Id, hero = fe.Target.Hero.ClassId, ready = fe.Ready } : null,
                        enchants = ArmorEnchanting.Pieces(c, s).Where(t => t.Piece.Enchant != null).ToDictionary(t => t.RowName, t => t.Piece.Enchant),
                        // GG-T180: onboarding step 7, the guided first skill, and each party hero's battle bar.
                        firstSkill = Training.First(c, s) is var fk && fk != null ? new { skill = fk.Skill.Id, hero = fk.Hero.ClassId, slot = fk.Slot } : null,
                        onboarded = s.Flags.Contains(Training.FirstSkillFlag),
                        bars = s.Party.Select(s.Hero).Where(h => h != null).ToDictionary(h => h.ClassId, h => h.Bar.ToList()),
                        bounty = new { active = active?.Id, progress = s.Bounty.Progress, complete = active != null && Bounties.IsComplete(c, s), posted = Bounties.Posted(c, s).Select(b => b.Id).ToList() },
                    };
                }
                case "stagebounds":
                {
                    var sb = app.Stage.worldBound;
                    var root = app.Document.rootVisualElement.worldBound;
                    float k = Screen.width / Mathf.Max(1f, root.width);
                    return new { x = sb.x * k, y = sb.y * k, w = sb.width * k, h = sb.height * k, screen = new[] { Screen.width, Screen.height }, design = new[] { GameApp.StageWidth, GameApp.StageHeight }, framed = app.Document.rootVisualElement.Q("stage-frame")?.ClassListContains("hidden") == false };
                }
                case "resize":
                    Screen.SetResolution((int)F(q["w"], 585), (int)F(q["h"], 1266), FullScreenMode.Windowed);
                    return new { ok = true };
                case "quit": Application.Quit(); return new { ok = true };
            }
            return new { error = "unknown endpoint " + path };
        }

        // ── Input injection (GG-T121: straight into the panel, independent of OS focus) ──

        private enum PointerPhase { Move, Down, Up }

        /// <summary>What one synthetic pointer event did: whether it reached the panel's dispatcher and what it targeted.</summary>
        private struct Delivery
        {
            public bool Arrived;
            public string Target;
            public bool HitsNothing;
        }

        private bool pointerDown;
        private Vector2 lastHeld;

        /// <summary>Design pixels → panel coordinates (the stage's own space scaled into the panel, origin top-left).</summary>
        private Vector2 ToPanel(float dx, float dy)
        {
            var stage = GameApp.I.Stage;
            return stage.worldBound.position + new Vector2(dx, dy) * (stage.worldBound.width / GameApp.StageWidth);
        }

        private Vector2 DesignPoint(Vector2 panelPoint)
        {
            var stage = GameApp.I.Stage;
            float k = GameApp.StageWidth / stage.worldBound.width;
            return (panelPoint - stage.worldBound.position) * k;
        }

        /// <summary>
        /// Nothing tappable under the point: off the panel, only the app's own scaffolding (root, letterbox,
        /// stage, the screens container) — every screen and widget lives below those — or the router's
        /// screen-transition fade, which swallows input while a screen is changing.
        /// </summary>
        private static bool HitsNothing(VisualElement hit)
        {
            if (hit == null) return true;
            var app = GameApp.I;
            return hit == app.Document.rootVisualElement || hit == app.Stage || hit == app.Stage.parent
                   || hit.name == "screens" || hit.name == "fade" || !app.Stage.Contains(hit);
        }

        private static string Describe(VisualElement e) => e == null ? null : $"{e.GetType().Name}#{e.name}";

        private void Receipt(EventBase e)
        {
            if (e != sentEvent) return;
            sentArrived = true;
            sentTarget = e.target as VisualElement;
        }

        /// <summary>
        /// Dispatches one mouse-type pointer event at a panel point through the panel's own event dispatcher —
        /// the same picking, pointer capture and ClickEvent synthesis real input goes through — and reports
        /// whether it arrived. Built from an IMGUI mouse Event, the way UI Toolkit's own event system builds them.
        /// </summary>
        private Delivery Send(Vector2 panelPoint, PointerPhase phase)
        {
            var panel = GameApp.I.Document.rootVisualElement.panel;
            var hit = panel?.Pick(panelPoint);
            var d = new Delivery { HitsNothing = HitsNothing(hit) };
            if (panel == null) return d;
            var type = phase == PointerPhase.Down ? EventType.MouseDown : phase == PointerPhase.Up ? EventType.MouseUp : pointerDown ? EventType.MouseDrag : EventType.MouseMove;
            var ev = new Event
            {
                type = type, mousePosition = panelPoint, button = 0, clickCount = phase == PointerPhase.Move ? 0 : 1,
                modifiers = EventModifiers.None, pointerType = UnityEngine.PointerType.Mouse,
            };
            EventBase evt = phase == PointerPhase.Down ? PointerDownEvent.GetPooled(ev)
                          : phase == PointerPhase.Up ? (EventBase)PointerUpEvent.GetPooled(ev)
                          : PointerMoveEvent.GetPooled(ev);
            sentEvent = evt; sentArrived = false; sentTarget = null;
            try { panel.visualTree.SendEvent(evt); }
            finally
            {
                // A pointer captured by an element (a Button between its press and release, the board during a
                // drag) gets its events straight at that element, so they may never pass the root listener —
                // but the dispatcher still gave the event a target. No receipt and no target = not delivered.
                d.Arrived = sentArrived || evt.target != null;
                d.Target = Describe(sentTarget ?? evt.target as VisualElement);
                sentEvent = null;
                evt.Dispose();
            }
            if (phase == PointerPhase.Down) pointerDown = true;
            else if (phase == PointerPhase.Up) pointerDown = false;
            return d;
        }

        private static object TapFailure(Delivery down, Delivery up, float x, float y)
        {
            if (down.HitsNothing)
                return new { ok = false, error = $"tap at ({x:0},{y:0}) hit nothing (off the stage, or only the stage background or a screen-transition fade is there)", x, y, target = down.Target };
            if (!down.Arrived || !up.Arrived)
                return new { ok = false, error = $"tap at ({x:0},{y:0}) not delivered to the panel (down={down.Arrived}, up={up.Arrived})", x, y };
            return null;
        }

        private IEnumerator Tap(float x, float y, Action<object> reply)
        {
            var p = ToPanel(x, y);
            Send(p, PointerPhase.Move); yield return null;
            var down = Send(p, PointerPhase.Down); yield return null; yield return null;
            var up = Send(p, PointerPhase.Up); yield return null; yield return null;
            reply(TapFailure(down, up, x, y) ?? new { ok = true, x, y, target = down.Target });
        }

        private static bool Opaque(VisualElement e)
        {
            for (var x = e; x != null; x = x.parent) if (x.resolvedStyle.opacity <= 0.01f) return false;
            return true;
        }

        private IEnumerator Frames(int n, Action done) { for (int i = 0; i < n; i++) yield return null; done(); }

        /// <summary>Press and hold at a design point for a while, then reply (button stays down until `release`).</summary>
        private IEnumerator Press(float x, float y, float seconds, Action<object> reply)
        {
            var p = ToPanel(x, y);
            lastHeld = p;
            Send(p, PointerPhase.Move); yield return null;
            var down = Send(p, PointerPhase.Down);
            if (down.HitsNothing) { reply(new { ok = false, error = $"press at ({x:0},{y:0}) hit nothing", target = down.Target }); yield break; }
            if (!down.Arrived) { reply(new { ok = false, error = "press not delivered to the panel" }); yield break; }
            yield return new WaitForSecondsRealtime(seconds);
            reply(new { ok = true, target = down.Target });
        }

        private IEnumerator Drag(List<Vector2> pts, float stepSeconds, Action<object> reply)
        {
            if (pts.Count == 0) { reply(new { ok = false, error = "empty path" }); yield break; }
            var start = ToPanel(pts[0].x, pts[0].y);
            Send(start, PointerPhase.Move); yield return null;
            var down = Send(start, PointerPhase.Down); yield return null;
            if (!down.Arrived || down.HitsNothing)
            {
                Send(start, PointerPhase.Up);
                holdDrag = false;
                reply(new { ok = false, error = down.HitsNothing ? $"drag start ({pts[0].x:0},{pts[0].y:0}) hit nothing" : "drag not delivered to the panel", target = down.Target });
                yield break;
            }
            int lost = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                int n = Mathf.Max(2, Mathf.CeilToInt((b - a).magnitude / 30f));
                for (int k = 1; k <= n; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)n);
                    if (!Send(ToPanel(p.x, p.y), PointerPhase.Move).Arrived) lost++;
                    yield return new WaitForSecondsRealtime(stepSeconds / n);
                }
            }
            var last = pts[pts.Count - 1];
            yield return null;
            lastHeld = ToPanel(last.x, last.y);
            if (lost > 0) { Send(lastHeld, PointerPhase.Up); holdDrag = false; reply(new { ok = false, error = $"{lost} drag moves not delivered to the panel" }); yield break; }
            if (holdDrag) { holdDrag = false; reply(new { ok = true, held = true, target = down.Target }); yield break; }
            var up = Send(lastHeld, PointerPhase.Up);
            yield return null; yield return null;
            reply(up.Arrived ? (object)new { ok = true, points = pts.Count, target = down.Target } : new { ok = false, error = "drag release not delivered to the panel" });
        }

        private IEnumerator WaitIdle(float minSeconds, Action<object> reply)
        {
            float start = Time.unscaledTime;
            yield return null;
            while (Time.unscaledTime - start < 30f)
            {
                bool busy = GameApp.I.Router.Current is BattleScreen b && b.Busy;
                if (!busy && Time.unscaledTime - start >= minSeconds) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.25f);
            reply(StateDump());
        }

        /// <summary>
        /// Frame recorder for art review: animations step a fixed amount per rendered frame (Tween.FixedStep) while every
        /// Nth frame is written to artifacts/&lt;name&gt;/f###.png, so slow PNG encoding never skips a beat.
        /// </summary>
        private IEnumerator Record(string name, int frames, int every, float step, Action<object> reply)
        {
            var dir = Path.Combine(artifacts, name);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "f*.png")) File.Delete(old);
            Tween.FixedStep = step;
            int written = 0;
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    yield return new WaitForEndOfFrame();
                    if (i % every != 0) continue;
                    // An unhandled exception here (e.g. disk I/O) would otherwise kill this coroutine
                    // silently — Handle()'s try/catch can't see it, since it's on a different thread
                    // waiting on `done` — leaving the HTTP caller hanging for the full 120s timeout
                    // instead of getting an immediate error. (A yield-return can't live inside a
                    // try/catch, only try/finally, hence this inner try nested one level in.)
                    Texture2D tex = null;
                    Exception captureError = null;
                    try { tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, $"f{written++:000}.png"), tex.EncodeToPNG()); }
                    catch (Exception e) { captureError = e; }
                    finally { if (tex != null) Destroy(tex); }
                    if (captureError != null) { reply(new { error = captureError.Message, dir, written }); yield break; }
                }
            }
            finally { Tween.FixedStep = 0f; }
            reply(new { dir, written });
        }

        private IEnumerator Screenshot(string name, Action<object> reply)
        {
            yield return new WaitForEndOfFrame();
            var file = Path.Combine(artifacts, name.EndsWith(".png") ? name : name + ".png");
            Texture2D tex = null;
            // Same reasoning as Record(): without this, a disk I/O failure here would silently kill the
            // coroutine and leave the HTTP caller hanging for the full 120s timeout instead of an immediate error.
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(file, tex.EncodeToPNG()); }
            catch (Exception e) { reply(new { error = e.Message }); yield break; }
            finally { if (tex != null) Destroy(tex); }
            reply(new { file });
        }

        // ── Introspection ────────────────────────────────────────────────────────

        private object StateDump()
        {
            var app = GameApp.I;
            var dump = new Dictionary<string, object>
            {
                ["screen"] = app.Router.Current?.Id,
                ["stack"] = app.Router.Stack.Select(s => s.Id).ToList(),
                ["screen_size"] = new[] { Screen.width, Screen.height },
                ["fps"] = Mathf.Round(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)),
                ["board_audit"] = BoardAudit.Snapshot(),
            };
            if (app.Save != null)
                dump["save"] = new { app.Save.Gold, app.Save.HighestClearedStage, party = app.Save.Party, inventory = app.Save.Inventory, levels = app.Save.Roster.ToDictionary(h => h.ClassId, Progression.Level), xp = app.Save.Roster.ToDictionary(h => h.ClassId, h => h.TotalXp), hp = app.Save.Roster.ToDictionary(h => h.ClassId, h => h.Hp), crucible = new { app.Save.Crucible.Best, app.Save.Crucible.Runs, app.Save.Crucible.TotalWaves }, expedition = app.Save.Expedition?.ThemeId };
            if (TestMode.Active) dump["testmode"] = new { cycles = TestMode.Completed, last = TestMode.LastLine };
            if (app.Router.Current is BattleScreen b && b.State != null)
            {
                var s = b.State;
                dump["battle"] = new
                {
                    phase = s.Phase.ToString(), wave = s.Wave, clock = s.Clock, busy = b.Busy, selected = b.SelectedHeroId, targeting = b.TargetingSlot, confirming = b.Confirming, record = b.LastRecordPath,
                    bank = s.Bank.ToString(), cutins = b.CutInsShown, coinsShown = b.CoinsShown, tallies = b.TalliesBurst, billboard = b.BillboardLines, paused = b.Paused, pauseOnTop = b.PauseOnTop, beats = b.PlaybackBeats, beatsWhilePaused = b.BeatsWhilePaused, orbSlots = b.OrbSlotsFlown, lines = b.LineStrokes, chain = b.ChainCount, amplifier = s.Amplifier.HasValue ? new[] { s.Amplifier.Value.X, s.Amplifier.Value.Y } : null,
                    units = s.Units.Where(u => u.Alive).Select(u => new
                    {
                        u.Id, cls = u.Def.Id, team = u.Team.ToString(), x = u.Pos.X, y = u.Pos.Y, u.W, u.H, u.Hp, u.MaxHp, u.Level,
                        weapon = u.WeaponId, weaponDurability = u.WeaponDurability,
                        armor = u.Gear.ToDictionary(kv => kv.Key.ToString(), kv => new { item = kv.Value.ItemId, durability = kv.Value.Durability }),
                        buffs = u.Buffs.Select(x => x.Def.Id + ":" + x.Remaining).ToList(),
                        acts_in = u.IsHero ? -1 : BattleEngine.ActionsUntilTurn(s, u.Id),
                        bar = u.IsHero ? Enumerable.Range(0, u.Bar.Count).Select(i => (u.Bar[i].AbilityId ?? u.Bar[i].ItemId ?? "-") + "=" + BattleEngine.GetSlotStatus(s, u.Id, i)).ToList() : null,
                    }).ToList(),
                    timeline = s.Timeline.Select(e => new { e.Id, kind = e.Kind.ToString(), owner = e.OwnerId, u = Math.Round(e.U, 3), e.AbilityId }).ToList(),
                    results = new { s.Results.Coins, s.Results.Xp, s.Results.ActionsTaken },
                };
            }
            return dump;
        }

        /// <summary>Layout audit of the current screen: elements with text that overflow, zero-size visible text, out-of-stage bounds.</summary>
        private object LayoutAudit()
        {
            var stage = GameApp.I.Stage;
            var sb = stage.worldBound;
            var problems = new List<string>();
            int labels = 0;
            foreach (var l in stage.Query<Label>().ToList())
            {
                if (l.resolvedStyle.display == DisplayStyle.None || string.IsNullOrEmpty(l.text) || l.resolvedStyle.opacity <= 0.01f) continue;
                labels++;
                var wb = l.worldBound;
                if (wb.width < 1 || wb.height < 1) { problems.Add($"zero-size label '{Trim(l.text)}'"); continue; }
                if (wb.xMin < sb.xMin - 2 || wb.xMax > sb.xMax + 2) problems.Add($"label off-stage horizontally '{Trim(l.text)}' x={DesignPoint(wb.position).x:0}..{DesignPoint(wb.max).x:0}");
                var size = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                float k = GameApp.StageWidth / sb.width;
                if (l.resolvedStyle.whiteSpace == WhiteSpace.NoWrap && size.x > l.contentRect.width + 4 && l.contentRect.width > 0)
                    problems.Add($"text overflows ({size.x:0}>{l.contentRect.width:0}) '{Trim(l.text)}'");
            }
            // Squashed controls: a tab or button drawn well short of its own styled height means its column ran out
            // of room and flex shrank it (the Merchant's tabs and footer, found by GG-T136's full loop).
            foreach (var el in stage.Query<VisualElement>().Where(e => e.ClassListContains("tab") || e.ClassListContains("btn")).ToList())
            {
                if (el.resolvedStyle.display == DisplayStyle.None || el.worldBound.height < 1) continue;
                float want = el.ClassListContains("tab") ? 96f : el.resolvedStyle.minHeight.value;
                float got = el.worldBound.height * (GameApp.StageWidth / sb.width);
                if (want > 0 && got < want * 0.8f)
                    problems.Add($"squashed {(el.ClassListContains("tab") ? "tab" : "button")} '{Trim((el as TextElement)?.text ?? el.name ?? "")}' {got:0}px of {want:0}");
            }
            return new { screen = GameApp.I.Router.Current?.Id, labels, problems };
        }

        private static string Trim(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        private static float F(string s, float def = 0f) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

        private static List<Vector2> ParsePoints(string s)
        {
            var list = new List<Vector2>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var part in s.Split(';'))
            {
                var xy = part.Split(',');
                if (xy.Length == 2) list.Add(new Vector2(F(xy[0]), F(xy[1])));
            }
            return list;
        }

        /// <summary>Strips any directory component/traversal from a request-supplied file name so it can't write or delete outside `artifacts`.</summary>
        private static string SafeName(string name, string fallback)
        {
            var n = Path.GetFileName(name);
            return string.IsNullOrEmpty(n) ? fallback : n;
        }

        private static List<Coord> ParseTiles(string s)
        {
            var list = new List<Coord>();
            foreach (var p in ParsePoints(s)) list.Add(new Coord((int)p.x, (int)p.y));
            return list;
        }
    }
}
#endif
