using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Bot;
using GridGame.Core.Content;
using GridGame.Core.Rules;
using GridGame.Meta;
using MindAttic.Capture;
using MindAttic.Capture.Unity;
using MindAttic.Telemetry;
using MindAttic.Telemetry.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// The battle. Layout follows the bible HUD rows (§9): top bar · timeline + shield · action banner ·
    /// 6×8 board · ability bar · mana orbs · actor panel. The simulation (BattleState) is the only truth;
    /// this screen turns input into commands and plays the resulting events back.
    /// </summary>
    public sealed partial class BattleScreen : GameScreen
    {
        public override string Id => "battle";
        public override string Music => "Crusade";

        // Layout (design pixels). Vertical positions are fixed from the top; horizontal ones follow the live stage
        // width (bible §26.3) so a wider phone centres the board and stretches the timeline instead of leaving a gap.
        private const float BoardTop = 450f;
        private static float BoardLeft => (GameApp.StageWidth - BoardView.Tile * BattleConfig.Cols) / 2f;
        private const float TimelineLeft = 40f, TimelineY = 250f;
        private static float TimelineWidth => GameApp.StageWidth - 230f;
        private VisualElement timelineFrame;
        /// <summary>The track line's y inside the timeline frame; every icon centres on it.</summary>
        private const float TrackY = 50f;

        public BattleState State { get; private set; }
        private LocalSession session;
        /// <summary>The battle's session seam (GG-LAW-13): one local seat over the battle authority. Re-wrapped if the
        /// state is ever replaced, so commands always reach the battle on screen.</summary>
        private LocalSession Session => session != null && session.State == State ? session : (session = new LocalSession(State));
        public bool Busy => playing || dragging;
        public int SelectedHeroId { get; private set; } = -1;
        public int TargetingSlot { get; private set; } = -1;
        /// <summary>The picked anchor awaiting Confirm (owner direction 2026-10-11: select, confirm, act).</summary>
        public Coord? PendingTarget { get; private set; }
        public bool Confirming => PendingTarget.HasValue;
        private bool ConfirmActions => App.Settings.ConfirmActions;
        /// <summary>HeroBot plays this battle. Forced on by <c>-autobattle</c> or <c>-demo</c>
        /// (the tour, see GameApp.DemoMode); otherwise toggled from the pause menu or the automation
        /// "autoplay" endpoint.</summary>
        public bool AutoPlay { get; set; }

        private readonly string stageId;
        private readonly BattleSetup setupOverride;
        private BoardView board;
        private VisualElement timelineBar, timelineIcons, zone;
        private readonly Dictionary<int, VisualElement> timelineIconViews = new Dictionary<int, VisualElement>();
        private readonly List<VisualElement> slotViews = new List<VisualElement>();
        private readonly List<VisualElement> orbViews = new List<VisualElement>();
        private VisualElement panel;
        private VisualElement feed;
        private VisualElement bannerViewport;
        private Coroutine bannerScrollRoutine;
        private const float BannerViewportHeight = 90f;
        private Label banner, waveLabel, coinLabel, timerLabel;
        private VisualElement shake;
        private VisualElement boardHost;
        /// <summary>Stage-sized layer inside the board, under board.Lines, that holds cut-in cards (GG-T128).</summary>
        private VisualElement cutInLayer;
        private bool playing;
        private bool ended;
        private HeroBot autoBot;
        /// <summary>Strict-best shadow bot (BotSkillProfile demo mode): only instantiated when `-telemetry`
        /// and a below-100 skill are both active, to log the road not taken alongside the move actually played.</summary>
        private HeroBot perfectBot;

        // First-battles hint: after a pause, show the move the bot would make (§29 tutorial question → contextual hints).
        private float idle;
        private bool hintShown;
        private const float HintDelay = 5f;
        private bool HintsEnabled => App.Save == null || App.Save.HighestClearedStage < 2;

        // drag state
        private bool pressing, dragging;
        /// <summary>True for the duration of <see cref="SimulatedHumanDrag"/> — pauses the real drag-timer
        /// countdown (Update()) so a slow, low-skill simulated drag can't be auto-committed out from under it.</summary>
        private bool simulatingDrag;
        private int dragHeroId = -1;
        private Vector2 pressPos;
        private Coord dragTile;
        private readonly List<Coord> dragPath = new List<Coord>();
        private MovePreview preview;
        private float dragTimeLeft;
        // diagonal slip gesture tracking (BattleScreen.Input.cs)
        private Vector2 lastPointerPos;
        private float lastPointerTime;
        // MindAttic.Capture: how the battle ended, set when BattleEndedEvent plays (BattleScreen.Playback.cs)
        private BattleEndedEvent lastEnded;

        public BattleScreen(string stageId) { this.stageId = stageId; }
        public BattleScreen(BattleSetup setup) { setupOverride = setup; stageId = setup.Stage.Id; }

        /// <summary>Seeds the autoplay bot's RNG (null = the clock). `-demo -endless` sets it to the encounter seed
        /// (GG-T64) so the bot decides exactly as <c>EndlessEncounter.Bot(seed)</c> does in a headless replay.</summary>
        public int? BotSeed { get; set; }

        /// <summary>The end of this battle is committed to App.Save and routed to the results screen. A battle built
        /// from an explicit setup normally just pops back instead — except under `-demo -endless` (GG-T57), whose
        /// setup is a generated party fought on a throwaway slot -1 save, and whose loop runs through the results.</summary>
        private bool CommitsToSave => setupOverride == null || DemoMode.Endless;

        /// <summary>A still-open pause veil lives on the global Overlay (see OpenMenu), which outlives this screen's
        /// own root — remove it explicitly so it doesn't linger on top of whatever screen comes next.</summary>
        public override void OnHidden()
        {
            GameApp.StageResized -= OnStageResized;
            if (pauseVeil != null) { pauseVeil.RemoveFromHierarchy(); pauseVeil = null; ResumeBattle(); }
            // The billboard lives on the Overlay too; without this, each battle left one behind (lines from the
            // last ~1.6 s showing over the PostBattle screen) and they piled up battle after battle (GG-T70).
            billboard?.RemoveFromHierarchy();
            billboard = null;
            // Build() rebuilds `banner` fresh when the player backs out of "How to play" onto this same
            // instance (see the Build() comment); an in-flight scroll left running would keep animating
            // whatever `banner` now points to using timing measured against the old one (GG-T70).
            if (bannerScrollRoutine != null) { App.StopCoroutine(bannerScrollRoutine); bannerScrollRoutine = null; }
        }

        private bool Colorblind => App.Settings.ColorblindMode;
        /// <summary>This battle is a Crucible run (§22.10): open-ended wave count, keep-everything retreat.</summary>
        private bool InCrucible => Crucible.IsStage(stageId);
        private bool ReduceMotion => App.Settings.ReduceMotion;

        protected override void Build(VisualElement root)
        {
            var initial = new List<BattleEvent>();
            // Build runs again on this same instance when the player backs out of "How to play" (the router
            // re-mounts the screen). Every per-build view cache must start empty, or the HUD keeps writing to
            // the previous build's detached elements (every slot read "Locked", orbs empty, timeline icons
            // gone, coin label blank) — and the once-per-battle work (tutorial, opening playback, capture
            // event) must not run a second time (GG-T70).
            bool firstBuild = State == null;
            slotViews.Clear();
            orbViews.Clear();
            timelineIconViews.Clear();
            shownCoins = -1;
            if (firstBuild)
            {
                // A Crucible run is seeded by the save (§22.10) so the same save replays the same gauntlet.
                int seed = InCrucible && App.Save != null ? Crucible.SeedFor(App.Save) : Environment.TickCount;
                var setup = setupOverride ?? BattleFactory.Build(App.Content, App.Save, stageId, seed);
                if (setupOverride == null) Rescues.Apply(App.Content, App.Save, setup); // GG-T236: maybe a captive to free
                if (setupOverride == null && DevNextCaptive != null) { setup.Captive = new CaptiveSpec { Class = DevNextCaptive, Wave = 0 }; DevNextCaptive = null; }
                State = BattleEngine.Create(App.Content, setup, initial);
                recorder = new BattleRecorder(State, setup, "player", initial);
                var diff = App.Settings.Difficulty;
                TelemetryUnity.Begin("GridGameGaiden", seed: setup.Seed, contentVersion: Application.version,
                    tags: new TelemetryData
                    {
                        ["stage"] = State.Stage.Id, ["source"] = "player",
                        // Difficulty is a scaffold today (§22.9) — tagging every battle with it now means
                        // the data to learn "what multiplier affects what" already exists once it's wired in.
                        ["difficultyPreset"] = diff?.Preset,
                        ["diffEnemyHp"] = diff?.EnemyHp, ["diffEnemyDamage"] = diff?.EnemyDamage, ["diffEnemySpeed"] = diff?.EnemySpeed,
                        ["diffRewardGold"] = diff?.RewardGold, ["diffRewardXp"] = diff?.RewardXp, ["diffItemDropRate"] = diff?.ItemDropRate,
                    });
                BeginEncounterTelemetry(setup, initial, diff); // GG-T59
            }

            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(State.Stage.Theme, Math.Abs(stageId.GetHashCode()) % 4));
            bg.style.unityBackgroundImageTintColor = new Color(0.78f, 0.78f, 0.86f);
            shake = Ui.El("fill", root);

            BuildTopBar(shake);
            BuildTimeline(shake);
            bannerViewport = Ui.El("", shake);
            bannerViewport.name = "banner-viewport";
            bannerViewport.style.position = Position.Absolute;
            bannerViewport.style.left = 0; bannerViewport.style.right = 0; bannerViewport.style.top = 318; bannerViewport.style.height = BannerViewportHeight;
            bannerViewport.style.overflow = Overflow.Hidden;
            banner = Ui.Text("", "h2 shadow", bannerViewport);
            banner.style.position = Position.Absolute;
            banner.style.left = 0; banner.style.right = 0; banner.style.top = 0;
            banner.style.unityTextAlign = TextAnchor.UpperCenter;
            banner.style.whiteSpace = WhiteSpace.Normal;

            boardHost = Ui.El("", shake);
            boardHost.style.position = Position.Absolute;
            boardHost.style.top = BoardTop;
            board = new BoardView(boardHost, State.Stage.Theme);
            board.Root.pickingMode = PickingMode.Position;
            // The pincer outline (board.Lines) stays inside the board, so it inherits every camera twist, zoom,
            // drift and screen shake and always hugs its tiles (GG-T128). Cut-in cards (ActionCard: SupporterPop,
            // chain pop-ups, caster slides) live in a stage-sized layer inside the board too, just BELOW the lines,
            // so a card popping in during a pincer never covers the outline (GG-T65's need), and both sit in this
            // screen's own tree, under the global Overlay, so nothing of the battle can draw over Pause (GG-T126).
            // Board order: tiles, units, marks, fx, cut-in cards, lines.
            cutInLayer = new VisualElement { name = "card-layer", pickingMode = PickingMode.Ignore };
            cutInLayer.style.position = Position.Absolute;
            cutInLayer.style.top = -BoardTop;
            board.Root.Add(cutInLayer);
            board.Lines.BringToFront();
            board.Root.RegisterCallback<PointerDownEvent>(OnPointerDown);
            board.Root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            board.Root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            board.Root.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragging) CommitDrag(); pressing = false; });

            feed = Ui.El("col", shake);
            feed.name = "feed";
            feed.pickingMode = PickingMode.Ignore;
            feed.style.position = Position.Absolute;
            feed.style.top = BoardTop + BoardView.Tile * BattleConfig.Rows - 140;
            feed.style.width = Length.Percent(55);
            feed.style.backgroundColor = new Color(0f, 0f, 0.05f, 0.35f);
            feed.style.borderTopRightRadius = 12;
            feed.style.justifyContent = Justify.FlexEnd;

            BuildAbilityBar(shake);
            BuildOrbs(shake);
            panel = Ui.Window(null, shake, "window--compact");
            panel.style.position = Position.Absolute;
            panel.style.left = 20; panel.style.right = 20; panel.style.top = 2250; panel.style.bottom = 20;
            panel.style.marginTop = panel.style.marginBottom = 0;
            panel.style.paddingTop = panel.style.paddingBottom = 14;

            ApplyStageLayout();
            foreach (var u in State.Units) if (u.Alive) board.Add(u);
            SelectedHeroId = State.Units.FirstOrDefault(u => u.IsHero && u.Alive)?.Id ?? -1;
            if (firstBuild && setupOverride == null && !InCrucible && App.Save != null && !App.Save.TutorialDone && App.Save.HighestClearedStage < 0 && !CommandLine.Has("-autobattle") && !DemoMode.Active && !TestMode.Active) TutorialStep = 0;
            SyncAll();
            if (!firstBuild) return;
            App.StartCoroutine(Play(initial, false));
            if (CommandLine.Has("-autobattle") || DemoMode.Active || TestMode.Active) AutoPlay = true;
            else if (TutorialStep < 0 && App.Save != null && App.Save.Flags.Add("hint:inspect"))
            {
                // GG-T252: a player who skipped the tutorial still learns the gesture once.
                App.Persist();
                panel.schedule.Execute(() => ShowBanner("Tip: hold any hero, foe, slot or icon to read it")).StartingIn(2500);
            }
            CaptureUnity.Raise("battle-start", new CaptureData { ["stage"] = State.Stage.Id });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HUD construction
        // ─────────────────────────────────────────────────────────────────────────

        private void BuildTopBar(VisualElement root)
        {
            var bar = Ui.El("row", root);
            bar.style.position = Position.Absolute;
            bar.style.left = 20; bar.style.right = 20; bar.style.top = 30; bar.style.height = 110;
            Ui.Button("II", OpenMenu, "btn--small btn--icon", bar).name = "menu";
            var mid = Ui.El("col grow", bar);
            mid.style.alignItems = Align.Center;
            Ui.Text(State.Stage.Name, "h2 shadow", mid);
            waveLabel = Ui.Text("", "small shadow dim", mid);
            coinLabel = Ui.Text("", "h2 gold shadow", bar);
            coinLabel.style.width = 200; coinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            timerLabel = Ui.Text("", "h1 shadow warn", root);
            timerLabel.style.position = Position.Absolute;
            timerLabel.style.right = 40; timerLabel.style.top = 318;
        }

        /// <summary>Re-applies the positions that depend on the stage width (build time and on resize).</summary>
        private void ApplyStageLayout()
        {
            if (boardHost == null) return;
            boardHost.style.left = BoardLeft;
            cutInLayer.style.left = -BoardLeft;
            cutInLayer.style.width = GameApp.StageWidth; cutInLayer.style.height = GameApp.StageHeight;
            feed.style.left = BoardLeft + 14;
            timelineFrame.style.width = TimelineWidth;
        }

        private void OnStageResized()
        {
            ApplyStageLayout();
            SyncAll();
        }

        public override void OnShown() { GameApp.StageResized -= OnStageResized; GameApp.StageResized += OnStageResized; }

        private void BuildTimeline(VisualElement root)
        {
            var frame = Ui.El("", root);
            timelineFrame = frame;
            frame.style.position = Position.Absolute;
            frame.style.left = TimelineLeft; frame.style.top = TimelineY - 50; frame.style.height = 100;
            timelineBar = Ui.El("", frame);
            timelineBar.name = "timeline-track";
            timelineBar.style.position = Position.Absolute;
            timelineBar.style.left = 0; timelineBar.style.right = 0; timelineBar.style.top = TrackY - 3; timelineBar.style.height = 6;
            timelineBar.style.backgroundColor = Palette.Text.WithA(0.85f);
            timelineBar.style.borderTopLeftRadius = timelineBar.style.borderTopRightRadius = timelineBar.style.borderBottomLeftRadius = timelineBar.style.borderBottomRightRadius = 3;
            zone = Ui.El("", frame);
            zone.style.position = Position.Absolute;
            zone.style.left = Length.Percent((1f - BattleConfig.ZoneU) * 100f); zone.style.right = 0;
            zone.style.top = 8; zone.style.bottom = 8;
            zone.style.backgroundColor = new Color(1f, 0.55f, 0.2f, 0.18f);
            zone.style.borderLeftWidth = 4; zone.style.borderLeftColor = new Color(1f, 0.6f, 0.25f, 0.9f);
            var act = Ui.Text("ACT", "tiny warn shadow", frame);
            act.style.position = Position.Absolute; act.style.right = -6; act.style.top = -22;
            timelineIcons = Ui.El("", frame);
            timelineIcons.style.position = Position.Absolute;
            timelineIcons.style.left = timelineIcons.style.top = timelineIcons.style.right = timelineIcons.style.bottom = 0;
            timelineIcons.schedule.Execute(CycleCastRings).Every(1200);

            var shield = Ui.Button("GUARD", () => { if (CanCommand()) Commit(new ShieldCommand()); }, "btn--icon", root);
            shield.name = "shield";
            shield.style.position = Position.Absolute;
            shield.style.right = 18; shield.style.top = TimelineY - 64; shield.style.width = 128; shield.style.height = 128;
            shield.style.fontSize = 30;
        }

        private void BuildAbilityBar(VisualElement root)
        {
            var bar = Ui.El("row", root);
            bar.name = "abilitybar";
            bar.style.position = Position.Absolute;
            // Even gutters on the space scale; the slots, not the gaps, take a wider stage (POL-C1/C8).
            bar.style.left = Space.S16; bar.style.right = Space.S16; bar.style.top = BoardTop + BoardView.Tile * BattleConfig.Rows + 20; bar.style.height = 196;
            bar.style.justifyContent = Justify.FlexStart;
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var slot = Ui.El("col center", bar);
                slot.name = "slot" + i;
                slot.style.flexGrow = 1; slot.style.flexBasis = 0; slot.style.height = 196;
                slot.style.marginLeft = slot.style.marginRight = Space.S8;
                slot.style.borderTopLeftRadius = slot.style.borderTopRightRadius = slot.style.borderBottomLeftRadius = slot.style.borderBottomRightRadius = 18;
                slot.style.borderTopWidth = slot.style.borderBottomWidth = slot.style.borderLeftWidth = slot.style.borderRightWidth = 4;
                slot.style.backgroundImage = new StyleBackground(Art.Window);
                slot.AddToClassList("slot");
                Ui.Pressable(slot);
                AttachSlotInspect(slot, idx);
                slot.RegisterCallback<ClickEvent>(_ => OnSlot(idx));
                var icon = Ui.El("menu-icon", slot); icon.name = "icon"; icon.style.marginRight = 0; icon.style.width = icon.style.height = 76;
                var name = Ui.Text("", "tiny", slot); name.name = "name"; name.style.unityTextAlign = TextAnchor.MiddleCenter; name.style.whiteSpace = WhiteSpace.Normal; name.style.maxWidth = Length.Percent(94);
                var cost = Ui.El("row", slot); cost.name = "cost"; cost.style.height = 36;
                slotViews.Add(slot);
            }
        }

        private void BuildOrbs(VisualElement root)
        {
            var row = Ui.El("row", root);
            row.name = "orbs";
            row.style.position = Position.Absolute;
            row.style.left = 40; row.style.right = 40; row.style.top = BoardTop + BoardView.Tile * BattleConfig.Rows + 228; row.style.height = 76;
            row.style.justifyContent = Justify.SpaceBetween;
            int cap = Mathf.Clamp(State.Bank.Capacity, ManaBank.DefaultCapacity, 16);
            float size = cap <= 12 ? 74 : Mathf.Floor(1080f / cap) - 6;
            for (int i = 0; i < cap; i++)
            {
                var o = Ui.Text("", "", row);
                o.name = "orb" + i;
                o.style.width = o.style.height = size;
                o.style.backgroundImage = new StyleBackground(Art.Orb);
                o.style.unityTextAlign = TextAnchor.MiddleCenter;
                o.style.fontSize = 30; Fonts.UseHeavy(o);
                o.style.color = Palette.Ink;
                orbViews.Add(o);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Sync views ← state (the safety net after every playback)
        // ─────────────────────────────────────────────────────────────────────────

        public void SyncAll()
        {
            foreach (var u in State.Units)
            {
                if (!board.Units.TryGetValue(u.Id, out var v))
                {
                    if (!u.Alive) continue;
                    v = board.Add(u);
                }
                if (!u.Alive)
                {
                    if (!v.Dead) { v.Root.RemoveFromHierarchy(); v.Dead = true; }
                    continue;
                }
                if (v.Dead) { board.UnitLayer.Add(v.Root); v.Dead = false; }
                board.Place(v, u.Pos);
                v.Root.style.opacity = 1; v.Root.style.scale = new Scale(Vector2.one); v.Root.style.translate = new Translate(0, 0);
                v.SetHp(u.Hp, u.MaxHp);
                v.SetBuffs(u, Colorblind, State.TurnEntryOf(u.Id)?.StunTicks > 0);
                v.SetPlate(u.Facing, u.PlateHp, u.PlateMax);
                if (u.IsHero)
                {
                    var weaponDef = u.WeaponId != null ? App.Content.TryItem(u.WeaponId) : null;
                    bool hasWeapon = weaponDef != null && weaponDef.Durability > 0;
                    float wMax = hasWeapon ? weaponDef.Durability * (u.WeaponMastery > 1f ? BattleConfig.MasterworkDurabilityMultiplier : 1f) : 1f;
                    float wFrac = hasWeapon ? u.WeaponDurability / wMax : 1f;
                    // Five armor pieces condense to one gear icon: any equipped at all, worst-condition fraction (GG-T89).
                    bool hasArmor = u.Gear.Count > 0;
                    float aFrac = 1f;
                    foreach (var piece in u.Gear.Values)
                    {
                        var def = App.Content.TryItem(piece.ItemId);
                        if (def == null || def.Durability <= 0) continue;
                        float max = def.Durability * (piece.Mastery > 1f ? BattleConfig.MasterworkDurabilityMultiplier : 1f);
                        float frac = max > 0f ? piece.Durability / max : 1f;
                        if (frac < aFrac) aFrac = frac;
                    }
                    v.SetGear(hasWeapon, wFrac, hasWeapon && u.WeaponDurability == 1, hasArmor, aFrac, !ReduceMotion);
                }
                else v.SetGear(false, 0f, false, false, 0f, !ReduceMotion);
                bool sel = u.Id == SelectedHeroId;
                var aura = AuraFor(u);
                if (sel) v.SetGlow(new Color(1f, 0.9f, 0.5f), 0.65f);
                else if (aura.a > 0f && !ReduceMotion) v.SetGlow(aura, 0.45f);
                else v.SetGlow(Color.white, 0f);
            }
            board.SyncTraps(State);
            board.SyncBlocks(State.Blocks);
            board.SyncAmplifier(State.Amplifier);
            board.SyncPickups(State);
            SyncTimeline(null);
            SyncOrbs();
            SyncBar();
            SyncPanel();
            int wave = Math.Min(State.Wave + 1, State.Stage.Waves.Count);
            waveLabel.text = InCrucible ? $"Crucible wave {wave}" : $"Wave {wave}/{State.Stage.Waves.Count}";
            if (shownCoins < 0) SyncCoins();
        }

        /// <summary>Linger effect (bible §6.2 stage 4): units carrying a strong status glow its color.</summary>
        private static Color AuraFor(Unit u)
        {
            foreach (var b in u.Buffs)
                switch (b.Def.Id)
                {
                    case "burning": return new Color(1f, 0.45f, 0.1f, 1f);
                    case "frozen": return new Color(0.6f, 0.9f, 1f, 1f);
                    case "poisoned": return new Color(0.5f, 1f, 0.3f, 1f);
                    case "regen": return Palette.Good;
                    case "sleep": return new Color(0.7f, 0.7f, 1f, 1f);
                    case "taunting": return new Color(1f, 0.3f, 0.2f, 1f);
                    case "empowered": return Palette.Buff;
                }
            return new Color(0, 0, 0, 0);
        }

        private void SyncTimeline(Dictionary<int, float> uOverride)
        {
            var live = new HashSet<int>();
            var at = new Dictionary<int, float>();
            foreach (var e in State.Timeline)
            {
                if (e.Kind == TimelineKind.Cast) { at[e.Id] = Mathf.Clamp01(uOverride != null && uOverride.TryGetValue(e.Id, out var cu) ? cu : e.U); continue; } // casts live on the caster, not the line
                live.Add(e.Id);
                if (!timelineIconViews.TryGetValue(e.Id, out var icon))
                {
                    icon = MakeTimelineIcon(e);
                    timelineIconViews[e.Id] = icon;
                    timelineIcons.Add(icon);
                }
                at[e.Id] = Mathf.Clamp01(uOverride != null && uOverride.TryGetValue(e.Id, out var ou) ? ou : e.U);
            }
            lastAt = at;
            LayoutIcons(at);
            MarkNextAttacker();
            SyncTimelineStatus();
            SyncCastRings(at);
            foreach (var kv in timelineIconViews.ToList())
                if (!live.Contains(kv.Key)) { kv.Value.RemoveFromHierarchy(); timelineIconViews.Remove(kv.Key); }
        }

        // Owner direction 2026-10-11: every timeline icon (turn or cast) should be the same size.
        private const float TurnIcon = 84f, CastIcon = 84f;

        private VisualElement MakeTimelineIcon(TimelineEntry e)
        {
            var owner = State.UnitById(e.OwnerId);
            var icon = new VisualElement { name = "tl-" + e.Id, pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.flexShrink = 0;
            if (e.Kind == TimelineKind.Turn)
            {
                icon.style.width = icon.style.height = TurnIcon;
                icon.style.top = TrackY - TurnIcon / 2f; // centred on the line
                SetBorder(icon, TurnIcon / 2f - 2f, 4, TurnRing(owner));
                // Opaque disc so the track never shows through; overflow:hidden masks the art to the circle.
                icon.style.backgroundColor = Palette.Ink;
                icon.style.overflow = Overflow.Hidden;
                // Heroes and enemies alike: the unit's portrait through its Thumbnail window (§12.1.1, GG-T221).
                var sprite = new VisualElement { name = "tl-art", pickingMode = PickingMode.Ignore };
                sprite.style.position = Position.Absolute;
                sprite.style.left = sprite.style.top = sprite.style.right = sprite.style.bottom = 3;
                Ui.PortraitThumb(sprite, owner?.Def.Id);
                icon.Add(sprite);
            }
            else
            {
                // A cast rides the same line, from 0 (cast start) to 1 (lands) over its own cast time, so a quick
                // spell visibly overtakes turn icons that started earlier.
                var a = App.Content.TryAbility(e.AbilityId);
                icon.style.width = icon.style.height = CastIcon;
                icon.style.top = TrackY - CastIcon / 2f;
                SetBorder(icon, CastIcon / 2f - 2f, 5, Art.SpellColor(e.AbilityId));
                icon.style.backgroundColor = e.CasterTeam == Team.Hero ? new Color(0.08f, 0.14f, 0.36f, 1f) : new Color(0.3f, 0.06f, 0.1f, 1f);
                icon.style.overflow = Overflow.Hidden;
                var tex = Art.AbilityIcon(a);
                if (tex != null) { icon.style.backgroundImage = new StyleBackground(tex); icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain); }
            }
            AttachTimelineInspect(icon, e);
            return icon;
        }

        private static readonly Color NextRing = Palette.Gold;

        private static Color TurnRing(Unit owner) =>
            owner != null && (owner.Def.HasTag("Boss") || owner.IsMultiTile) ? Palette.Boss : Palette.Enemy;

        /// <summary>The enemy turn closest to ACT gets a gold ring and a slight lift: "this one hits next".</summary>
        private void MarkNextAttacker()
        {
            var next = State.Timeline.Where(e => e.Kind == TimelineKind.Turn && State.UnitById(e.OwnerId) is Unit o && !o.IsHero)
                .OrderByDescending(e => e.U).FirstOrDefault();
            foreach (var e in State.Timeline)
            {
                if (e.Kind != TimelineKind.Turn || !timelineIconViews.TryGetValue(e.Id, out var icon)) continue;
                bool isNext = next != null && e.Id == next.Id;
                SetBorder(icon, TurnIcon / 2f - 2f, 4, isNext ? NextRing : TurnRing(State.UnitById(e.OwnerId)));
                icon.style.scale = new Scale(isNext ? new Vector2(1.18f, 1.18f) : Vector2.one);
                icon.EnableInClassList("tl-next", isNext);
            }
        }

        /// <summary>
        /// Every circle sits on the same centre line (owner direction 2026-10-10: never lift or bury one). Where circles
        /// overlap they form a cluster, and each member gets its own beat on top: the front one rotates every 1.2 s.
        /// </summary>
        private void LayoutIcons(Dictionary<int, float> at)
        {
            var entries = State.Timeline.Where(e => e.Kind == TimelineKind.Turn && timelineIconViews.ContainsKey(e.Id))
                .OrderBy(e => at[e.Id]).ThenBy(e => e.Id).ToList();
            foreach (var e in entries)
            {
                var icon = timelineIconViews[e.Id];
                icon.style.left = at[e.Id] * TimelineWidth - TurnIcon / 2f;
                icon.style.top = TrackY - TurnIcon / 2f;
            }
            // Clusters of overlapping circles (sorted by position, chained while neighbours overlap).
            var cluster = new List<TimelineEntry>();
            void Flush()
            {
                if (cluster.Count == 0) return;
                foreach (var e in cluster) timelineIconViews[e.Id].BringToFront();
                if (cluster.Count > 1) timelineIconViews[cluster[castCycle % cluster.Count].Id].BringToFront();
                cluster.Clear();
            }
            float lastX = float.NegativeInfinity;
            foreach (var e in entries)
            {
                float x = at[e.Id] * TimelineWidth;
                if (x - lastX >= TurnIcon * 0.85f) Flush();
                cluster.Add(e);
                lastX = x;
            }
            Flush();
        }

        /// <summary>Why an enemy isn't moving: a badge on its timeline circle for halting/immobilising effects and pushback stun.</summary>
        private void SyncTimelineStatus()
        {
            foreach (var e in State.Timeline)
            {
                if (e.Kind != TimelineKind.Turn || !timelineIconViews.TryGetValue(e.Id, out var icon)) continue;
                var owner = State.UnitById(e.OwnerId);
                var badge = timelineIcons.Q<VisualElement>("tl-status-" + e.Id);
                var status = owner == null ? null : StatusOf(owner, e);
                var art = icon.Q("tl-art");
                if (status == null) { badge?.RemoveFromHierarchy(); if (art != null) art.style.opacity = 1f; continue; }
                if (badge != null && badge.viewDataKey != status.Value.icon) { badge.RemoveFromHierarchy(); badge = null; }
                if (badge == null)
                {
                    badge = Ui.StatusPip(status.Value.icon, status.Value.letter, status.Value.color, 40, timelineIcons);
                    badge.name = "tl-status-" + e.Id;
                    badge.AddToClassList("tl-status");
                    badge.style.position = Position.Absolute;
                }
                badge.style.left = icon.style.left.value.value + TurnIcon - 30f;
                badge.style.top = icon.style.top.value.value + TurnIcon - 30f;
                badge.BringToFront();
                if (art != null) art.style.opacity = 0.55f; // dimmed: it isn't moving
            }
            foreach (var b in timelineIcons.Query<VisualElement>(className: "tl-status").ToList())
                if (b.name != null && b.name.StartsWith("tl-status-") && !State.Timeline.Any(e => "tl-status-" + e.Id == b.name && State.UnitById(e.OwnerId) is Unit o && o.Alive && StatusOf(o, e) != null))
                    b.RemoveFromHierarchy();
        }

        /// <summary>The effect that stops this unit's clock or turn, if any: buffs that halt/immobilise, else pushback stun.</summary>
        private static (string icon, string letter, Color color, string name)? StatusOf(Unit u, TimelineEntry turn)
        {
            foreach (var b in u.Buffs)
                if (b.Def.HaltsTimeline || b.Def.Immobile)
                    return (b.Def.Id, string.IsNullOrEmpty(b.Def.Letter) ? b.Def.Name.Substring(0, 1) : b.Def.Letter, Art.HexColor(b.Def.Color, new Color(0.6f, 0.85f, 1f)), b.Def.Name);
            if (turn != null && turn.StunTicks > 0) return ("stunned", "!", Palette.Gold, "Stunned");
            return null;
        }

        // ── Cast countdowns on the target's frame ──────────────────────────────

        private Dictionary<int, float> lastAt;
        private int castCycle;

        /// <summary>
        /// Cast time shows on the caster (owner direction 2026-10-10): every pending cast traces a radial countdown
        /// around its caster's frame in the spell's colour; its target carries the spell chip, so "who casts what on
        /// whom" reads on the board. A unit with several pending casts (or targeted by several) cycles every 1.2 s.
        /// </summary>
        private void SyncCastRings(Dictionary<int, float> at)
        {
            var casting = new Dictionary<int, List<(Color color, float left)>>();
            var targeted = new Dictionary<int, List<(Color color, AbilityDef a)>>();
            var rides = State.Timeline.Where(e => e.Kind == TimelineKind.Cast).Select(e => (e.Id, e.OwnerId, e.AbilityId, e.TargetUnitId, e.TargetTile, e.U)).ToList();
            foreach (var g in castGhosts.Values)
                if (rides.All(r => r.Id != g.EntryId)) rides.Add((g.EntryId, g.CasterId, g.AbilityId, g.TargetUnitId, g.TargetTile, g.U));
            foreach (var e in rides)
            {
                var color = Art.SpellColor(e.AbilityId);
                var a = App.Content.TryAbility(e.AbilityId);
                float u = at != null && at.TryGetValue(e.Id, out var x) ? x : e.U;
                if (castGhosts.TryGetValue(e.Id, out var ghost)) ghost.U = u;
                if (!casting.TryGetValue(e.OwnerId, out var cl)) casting[e.OwnerId] = cl = new List<(Color, float)>();
                cl.Add((color, 1f - u));
                int target = e.TargetUnitId;
                if (State.UnitById(target) is not Unit tu || !tu.Alive) target = State.UnitAt(e.TargetTile)?.Id ?? -1;
                if (target < 0) continue;
                if (!targeted.TryGetValue(target, out var tl)) targeted[target] = tl = new List<(Color, AbilityDef)>();
                tl.Add((color, a));
            }
            foreach (var kv in board.Units)
            {
                if (casting.TryGetValue(kv.Key, out var cl)) { var (c, left) = cl[castCycle % cl.Count]; kv.Value.SetCastRing(c, left); kv.Value.SetCastBar(c, 1f - left); }
                else { kv.Value.SetCastRing(null, 0f); kv.Value.SetCastBar(null, 0f); }
                if (targeted.TryGetValue(kv.Key, out var tl))
                {
                    var (c, a) = tl[castCycle % tl.Count];
                    kv.Value.SetCastChip(c, Art.AbilityIcon(a), a?.Name);
                }
                else kv.Value.SetCastChip(null, null, null);
            }
        }

        /// <summary>The shared 1.2 s beat: cast rings, overlapping timeline circles and status slots each show their next item.</summary>
        private void CycleCastRings()
        {
            if (Paused) return; // the 1.2 s beat is game time too (GG-T126)
            castCycle++;
            if (State == null || board == null || lastAt == null) return;
            SyncCastRings(lastAt);
            LayoutIcons(lastAt);
            SyncTimelineStatus();
            foreach (var v in board.Units.Values) v.NextStatus();
        }

        /// <summary>Dev hook (automation): queue a pending enemy cast on a unit so the countdown ring can be checked.</summary>
        public int DevQueueCast(int targetId, string abilityId, int ticks)
        {
            var target = State.UnitById(targetId);
            var caster = State.Units.FirstOrDefault(u => u.Alive && !u.IsHero);
            if (target == null || caster == null || App.Content.TryAbility(abilityId) == null) return -1;
            State.Timeline.Add(new TimelineEntry { Id = State.NextEntryId++, Kind = TimelineKind.Cast, OwnerId = caster.Id, AbilityId = abilityId, CasterTeam = Team.Enemy, TargetUnitId = target.Id, TargetTile = target.Pos, TotalCastTicks = ticks, RemainingTicks = ticks, IsCharge = true });
            SyncAll();
            return caster.Id;
        }

        private static void SetBorder(VisualElement e, float radius, float width, Color c)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }

        /// <param name="upTo">Mid-playback (GG-T112): fill only this many slots, so orbs minted in one action light
        /// their own slots one by one instead of all at once; -1 shows the bank as it is. Also used by
        /// <see cref="OrbsSpentEvent"/> playback (GG-T130) to redraw to the count right after that spend, not the
        /// command's end-of-action <see cref="BattleState.Bank"/> count, which may already include orbs the same
        /// action mints later.</param>
        private void SyncOrbs(int upTo = -1)
        {
            int filled = upTo >= 0 ? Mathf.Min(upTo, State.Bank.Count) : State.Bank.Count;
            shownCharge = State.ManaCharge;
            if (orbViews.Count > 0 && !playing) SetChargeBubble(State.ManaCharge / (float)BattleConfig.ManaChargePerOrb);
            for (int i = 0; i < orbViews.Count; i++)
            {
                var o = (Label)orbViews[i];
                if (i < filled)
                {
                    o.style.unityBackgroundImageTintColor = Art.Mana;
                    o.text = "";
                    o.style.opacity = 1f;
                }
                else
                {
                    o.style.unityBackgroundImageTintColor = new Color(0.22f, 0.25f, 0.38f, 0.85f);
                    o.text = "";
                    o.style.opacity = 1f;
                }
            }
        }

        private void SyncBar()
        {
            var hero = State.UnitById(SelectedHeroId);
            for (int i = 0; i < slotViews.Count; i++)
            {
                var v = slotViews[i];
                var icon = v.Q("icon");
                var name = v.Q<Label>("name");
                var cost = v.Q("cost");
                cost.Clear();
                icon.style.backgroundImage = StyleKeyword.None;
                var status = hero == null ? SlotStatus.CannotAct : BattleEngine.GetSlotStatus(State, hero.Id, i);
                var border = Palette.Text.WithA(0.75f);
                float alpha = 1f;
                if (hero == null || i >= hero.UnlockedSlots)
                {
                    name.text = hero == null ? "" : "Locked";
                    alpha = 0.35f;
                }
                else if (i >= hero.Bar.Count || hero.Bar[i].IsEmpty)
                {
                    name.text = "—";
                    alpha = 0.5f;
                }
                else
                {
                    var sl = hero.Bar[i];
                    var a = BattleEngine.SlotAbility(State, hero, i);
                    if (sl.IsWeapon)
                    {
                        var w = App.Content.TryItem(sl.WeaponId);
                        name.text = $"⭾ {w?.Name}";
                        border = new Color(0.8f, 0.8f, 0.9f);
                        icon.style.backgroundImage = new StyleBackground(Art.ItemIcon(sl.WeaponId) ?? Art.Orb);
                    }
                    else if (sl.IsItem)
                    {
                        var item = App.Content.TryItem(sl.ItemId);
                        name.text = $"{item?.Name}\n{sl.Charges}/{sl.MaxCharges}";
                        border = new Color(0.86f, 0.62f, 0.36f);
                        icon.style.backgroundImage = new StyleBackground(Art.ItemIcon(sl.ItemId) ?? Art.AbilityIcon(a) ?? Art.Orb);
                    }
                    else if (a != null)
                    {
                        name.text = a.Name;
                        icon.style.backgroundImage = new StyleBackground(Art.AbilityIcon(a) ?? Art.Orb);
                        if (a.Kind == AbilityKind.Skill)
                        {
                            border = new Color(0.4f, 0.9f, 0.5f);
                            if (status == SlotStatus.Cooldown) name.text = $"{a.Name}\nCD {hero.Cooldowns[a.Id]}";
                        }
                        else
                        {
                            border = Palette.Mana;
                            Ui.CostPips(a.Cost, cost, 30);
                        }
                    }
                    if (status == SlotStatus.Silenced) { border = new Color(1f, 0.25f, 0.25f); name.text += "\nSilenced"; }
                    if (status != SlotStatus.Ready) alpha = 0.45f;
                }
                bool targeting = i == TargetingSlot;
                if (targeting) border = Palette.Gold;
                v.EnableInClassList("slot--targeting", targeting);
                v.style.scale = StyleKeyword.Null; // scale belongs to the stylesheet (press / targeting states)
                v.style.borderTopColor = v.style.borderBottomColor = v.style.borderLeftColor = v.style.borderRightColor = border;
                v.style.opacity = alpha;
            }
        }

        private int panelUnitId = -1;

        private void SyncPanel()
        {
            panel.Clear();
            if (TutorialActive && TargetingSlot < 0 && panelUnitId < 0) { TutorialCard(); return; }
            if (TargetingSlot >= 0 && State.UnitById(SelectedHeroId) is Unit caster)
            {
                var ab = BattleEngine.SlotAbility(State, caster, TargetingSlot);
                if (ab != null) { AbilityInfo(ab); return; }
            }
            var u = State.UnitById(panelUnitId >= 0 ? panelUnitId : SelectedHeroId);
            if (u == null || !u.Alive) u = State.UnitById(SelectedHeroId);
            if (u == null) return;
            var row = Ui.El("row", panel);
            Ui.Thumb(u.Def.Id, row, 200);
            var col = Ui.El("col grow", row);
            var top = Ui.El("row", col);
            Ui.Text(u.Name, "h2 grow", top);
            if (u.IsHero && !string.IsNullOrEmpty(u.Def.Role)) Ui.RoleBadge(u.Def.Role, top, 26);
            Ui.Text($"Lv {u.Level}", "h2 gold", top);
            bool reveal = u.IsHero || State.Results.Scanned.Contains(u.Def.Id) || (App.Save?.Bestiary.TryGetValue(u.Def.Id, out var be) == true && be.Scanned);
            var hpRow = Ui.El("row", col);
            Ui.Gauge(u.HpFraction, hpRow, 460);
            Ui.Text(reveal ? $"  {u.Hp}/{u.MaxHp}" : "  ???", "small", hpRow);
            if (reveal) Ui.Text(Ui.StatLine(u.Effective), "tiny dim", col);
            if (!u.IsHero)
            {
                int n = BattleEngine.ActionsUntilTurn(State, u.Id);
                var cast = State.CastEntryOf(u.Id);
                Ui.Text(cast != null ? $"Charging {App.Content.TryAbility(cast.AbilityId)?.Name}!" : n < 0 ? "Frozen in time" : n == 0 ? "Acts after your next move!" : $"Acts in {n} moves", "small warn", col);
                var mind = Ui.Text($"{EnemyPlanner.DescribeBrain(u.Def.Brain)} · {EnemyPlanner.Describe(EnemyPlanner.TargetingOf(u)).ToLowerInvariant()}", "tiny dim", col);
                mind.name = "enemy-brain";
                mind.style.whiteSpace = WhiteSpace.Normal;
                if (u.HasPlate) { var pl = Ui.Text($"Armor plate {u.PlateHp}/{u.PlateMax} on its {SideName(u.Facing)} — strike from the sides, break it head-on, or use spells", "tiny", col); pl.style.whiteSpace = WhiteSpace.Normal; pl.style.color = Palette.Info; }
                if (u.IsCorpse) { var co = Ui.Text($"The body of a {App.Content.TryClass(u.CorpseOf)?.Name ?? "foe"} — blocks the tile, rots in {u.DecayIn} actions", "tiny dim", col); co.style.whiteSpace = WhiteSpace.Normal; }
                var kit = Ui.KitTraits(App.Content, u.Def);
                if (kit.Length > 0) { var kt = Ui.Text(kit, "tiny gold", col); kt.name = "enemy-kit"; kt.style.whiteSpace = WhiteSpace.Normal; }
            }
            // What the flashing status slot on the tile means, one line per effect.
            var statusTurn = State.TurnEntryOf(u.Id);
            if (statusTurn != null && statusTurn.StunTicks > 0) StatusRow(col, "stunned", "!", Palette.Gold, "Stunned", "knocked back on the timeline — its turn icon is held");
            foreach (var b in u.Buffs)
            {
                var pip = StatusRow(col, b.Def.Id, string.IsNullOrEmpty(b.Def.Letter) ? b.Def.Name.Substring(0, 1) : b.Def.Letter,
                    Art.HexColor(b.Def.Color, b.Def.Kind == BuffKind.Buff ? Palette.Mana : new Color(0.8f, 0.4f, 0.9f)),
                    b.Def.Name, $"{Ui.BuffEffect(b.Def)} ({b.Remaining} {(b.Def.Unit == GridGame.Core.Content.DurationUnit.Turns ? "turns" : "s")} left)");
                AttachStatusInspect(pip, b.Def, b.Remaining);
            }
        }

        private static string SideName(Coord f) => f.Y > 0 ? "front (facing down)" : f.Y < 0 ? "front (facing up)" : f.X > 0 ? "front (facing right)" : "front (facing left)";

        private static VisualElement StatusRow(VisualElement parent, string icon, string letter, Color color, string name, string what)
        {
            var row = Ui.El("row", parent);
            var pip = Ui.StatusPip(icon, letter, color, 40, row);
            pip.style.marginRight = 10;
            var t = Ui.Text($"{name} — {what}", "tiny", row);
            t.style.whiteSpace = WhiteSpace.Normal; t.style.flexShrink = 1;
            return pip;
        }

        /// <summary>
        /// The ability card in the actor panel (fits its ~260 design px). While aiming it explains the ability; once a
        /// target is picked it becomes the confirm card: what, on whom, then Cast / Cancel stacked on the right.
        /// </summary>
        private void AbilityInfo(AbilityDef a)
        {
            bool confirm = PendingTarget.HasValue && TargetingSlot >= 0;
            var row = Ui.El("row", panel);
            row.style.alignItems = Align.Center;
            var icon = Ui.El("menu-icon", row);
            icon.style.width = icon.style.height = 96;
            var itemTex = a.Id != null && a.Id.StartsWith("item:") ? Art.ItemIcon(a.Id.Substring(5)) : null;
            icon.style.backgroundImage = new StyleBackground(itemTex ?? Art.AbilityIcon(a) ?? Art.Orb);
            var col = Ui.El("col grow", row);
            col.style.flexShrink = 1;
            var top = Ui.El("row", col);
            top.style.alignItems = Align.Center;
            Ui.Text(a.Name, "h2", top);
            if (a.Cost > 0) Ui.CostPips(a.Cost, top, 32).style.marginLeft = 12;
            string timing = a.Kind == AbilityKind.Skill ? (a.CooldownTurns > 0 ? $"Skill · recharges in {a.CooldownTurns} turns" : "Skill")
                : a.Kind == AbilityKind.Item ? "Item · acts at once"
                : a.CastTime > 0 ? $"Charges {a.CastTime:0.#}s on the timeline" : "Instant";
            Ui.Text(timing, "tiny gold", col);
            if (!confirm)
            {
                var d = Ui.Text(a.Description ?? "", "tiny", col);
                d.style.whiteSpace = WhiteSpace.Normal;
                Ui.Text("Tap a highlighted tile · tap the slot again to cancel", "tiny dim", col);
                return;
            }
            var hero = State.UnitById(SelectedHeroId);
            var hits = hero == null ? new List<Unit>() : TargetResolver.Collect(State, a, hero, PendingTarget.Value);
            string who = hits.Count == 0 ? (a.IsTeleport ? "Teleport to the marked tile" : "No one in the area")
                : hits.Count == 1 ? (hits[0] == hero ? $"On {hero.Name} (self)" : $"On {hits[0].Name}")
                : $"On {string.Join(", ", hits.Take(3).Select(h => h.Name))}{(hits.Count > 3 ? $" +{hits.Count - 3}" : "")}";
            var summary = Ui.Text(who, "small", col);
            summary.name = "confirm-summary";
            summary.style.whiteSpace = WhiteSpace.Normal;
            OutcomeLines(col, a);
            var btns = Ui.El("col", row);
            btns.style.width = Length.Percent(28); btns.style.minWidth = 264; btns.style.flexShrink = 0;
            btns.style.marginLeft = Space.S16;
            string verb = a.Kind == AbilityKind.Item ? "Use" : a.Kind == AbilityKind.Spell ? "Cast" : "Go";
            var ok = Ui.Button(verb, ConfirmPending, "btn--primary", btns);
            ok.name = "confirm-action";
            ok.style.marginTop = ok.style.marginBottom = 4;
            var cancel = Ui.Button("Cancel", CancelByPlayer, "btn--small", btns);
            cancel.name = "confirm-cancel";
            cancel.style.marginTop = cancel.style.marginBottom = 4;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Commands
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Not while a drag is in progress either: a second finger tapping GUARD or a slot mid-drag would
        /// commit that instead and silently drop the drag, leaving the previewed units out of place (GG-T70).
        /// CommitDrag clears <c>dragging</c> before it commits, so the drag's own command still goes through.</summary>
        public bool CanCommand() => !playing && !ended && !State.IsOver && !dragging && !Paused;

        /// <summary>Dev/automation helper: add orbs to the bank (cheat — development builds only call this).</summary>
        public void DevGiveMana(int n, int charge = -1)
        {
            State.Bank.Add(n);
            // Dev only: a charge past one orb is allowed on purpose, so the next charge gain rolls several orbs at
            // once (tools/harness.py orbs, GG-T112).
            if (charge >= 0) State.ManaCharge = Mathf.Clamp(charge, 0, 10 * BattleConfig.ManaChargePerOrb);
            SyncOrbs();
            SyncBar();
        }

        public string BannerText => banner?.text;

        /// <summary>Dev/automation helper: put a buff on a unit and refresh its view.</summary>
        /// <summary>Dev/automation helper: put an ability ("poison") or item ("item:healing_potion_basic:3") in a hero's bar slot.</summary>
        public bool DevSetBar(int unitId, int slot, string entry)
        {
            var u = State.UnitById(unitId);
            if (u == null || !u.IsHero || slot < 0 || slot >= u.Bar.Count || string.IsNullOrEmpty(entry)) return false;
            if (entry.StartsWith("item:"))
            {
                var parts = entry.Split(':');
                int n = parts.Length > 2 && int.TryParse(parts[2], out var k) ? k : 3;
                u.Bar[slot] = new AbilitySlot { ItemId = parts[1], Charges = n, MaxCharges = n };
            }
            else u.Bar[slot] = new AbilitySlot { AbilityId = entry };
            SyncBar();
            return true;
        }

        /// <summary>Dev/automation helper: set a unit's HP (art review: make the next blow a kill).</summary>
        /// <summary>Dev/automation (GG-T241): the next player-launched battle holds this captive, skipping the 25% roll.</summary>
        public static string DevNextCaptive;

        /// <summary>Dev/automation (GG-T241): end the battle as a victory now and commit it like a real one, so story
        /// beats that hang off a win (the ending, interludes, rescues) can be reached without fighting every wave.</summary>
        public bool DevWin()
        {
            if (playing || ended || State.IsOver || App.Save == null || !CommitsToSave) return false;
            ended = true;
            State.Phase = BattlePhase.Victory;
            var rep = BattleFactory.Commit(App.Content, App.Save, State);
            App.Persist();
            PostBattleScreen.RouteFrom(rep, State.Stage.Id);
            return true;
        }

        public bool DevHp(int unitId, int hp)
        {
            var u = State.UnitById(unitId);
            if (u == null || !u.Alive) return false;
            u.Hp = Mathf.Clamp(hp, 1, u.MaxHp);
            SyncAll();
            return true;
        }

        /// <summary>Dev/automation helper: force a hero's weapon to a given durability (art/harness review — GG-T39).</summary>
        public bool DevWeaponDurability(int unitId, int durability)
        {
            var u = State.UnitById(unitId);
            if (u == null || !u.Alive || u.WeaponId == null) return false;
            u.WeaponDurability = Mathf.Max(0, durability);
            SyncAll();
            return true;
        }

        public bool DevBuff(int unitId, string buffId)
        {
            var u = State.UnitById(unitId);
            var def = App.Content.TryBuff(buffId);
            if (u == null || def == null) return false;
            u.Buffs.Add(new BuffInstance { Def = def, Remaining = def.Duration });
            SyncAll();
            return true;
        }

        /// <summary>Apply a command to the simulation and play its consequences. <paramref name="perfectCmd"/>:
        /// demo-mode only (BotSkillProfile) — the strict-best move this same decision point would have made at
        /// full skill, recorded alongside whatever imperfect <paramref name="cmd"/> was actually played.</summary>
        public void Commit(BattleCommand cmd, bool skipMovePrefix = false, BattleCommand perfectCmd = null)
        {
            if (!CanCommand()) return;
            bool wasTargeting = TargetingSlot >= 0;
            CancelTargeting();
            if (wasTargeting) { SyncBar(); panelUnitId = -1; SyncPanel(); }
            bool telemetryOn = TelemetryUnity.Current.Enabled;
            var before = recorder != null || telemetryOn ? BattleRecorder.Snapshot(State) : null;
            // GG-LAW-13: input never writes state; the command is stamped with this machine's seat and goes through the
            // session's one pipeline (LocalSession applies it this frame, exactly as BattleEngine.Apply always did).
            cmd.Player = Session.LocalPlayer;
            var events = Session.SubmitAndStep(cmd);
            recorder?.Add(before, cmd, events, State, perfectCmd, perfectCmd != null ? BotSkillProfile.Level : (float?)null);
            ObserveEncounterTelemetry(events); // GG-T59
            if (telemetryOn)
            {
                bool accepted = events.Exists(e => e is PlayerControlEvent || e is BattleEndedEvent);
                // Same type-labeled shape BattleRecorder writes to disk, so every event is self-describing (not just its raw fields).
                TelemetryUnity.RecordTurn(before, BattleRecorder.Command(cmd), accepted, BattleRecorder.Events(events), BattleRecorder.Snapshot(State));
            }
            if (AutoPlay) autoplayedTurns++;
            Debug.Log($"[Battle] {cmd} → {events.Count} events, clock {State.Clock}");
            TutorialAfterCommit(cmd, events);
            App.StartCoroutine(Play(events, skipMovePrefix));
        }

        private void OnSlot(int slot)
        {
            if (!CanCommand()) return;
            var hero = State.UnitById(SelectedHeroId);
            if (hero == null) return;
            if (TargetingSlot == slot) { CancelByPlayer(); return; }
            if (TargetingSlot >= 0) CancelTargeting();
            var status = BattleEngine.GetSlotStatus(State, hero.Id, slot);
            if (status != SlotStatus.Ready)
            {
                App.Audio.Sfx("ui.error");
                ShowBanner(StatusText(status));
                return;
            }
            var a = BattleEngine.SlotAbility(State, hero, slot);
            if (a == null) return;
            App.Audio.Sfx("ui.select");
            if (a.Mode == TargetMode.Auto)
            {
                if (!ConfirmActions || AutoPlay) { Commit(new UseSlotCommand { HeroId = hero.Id, Slot = slot, Target = hero.Pos }); return; }
                TargetingSlot = slot;
                SetPending(hero.Pos);
                SyncBar();
                return;
            }
            var targets = BattleEngine.ValidTargets(State, hero.Id, slot);
            if (targets.Count == 0) { ShowBanner("No valid target"); App.Audio.Sfx("ui.error"); return; }
            TargetingSlot = slot;
            board.ClearMarks();
            foreach (var t in targets) board.Mark(t, Palette.Gold, 0.35f);
            ShowBanner($"{a.Name}: choose a target");
            SyncBar();
            SyncPanel();
        }

        private static string StatusText(SlotStatus s)
        {
            switch (s)
            {
                case SlotStatus.Unaffordable: return "Not enough mana";
                case SlotStatus.Cooldown: return "Recharging";
                case SlotStatus.OutOfCharges: return "None left";
                case SlotStatus.Silenced: return "Silenced!";
                case SlotStatus.CastCapReached: return "Too many spells!";
                case SlotStatus.Locked: return "Unlocks as you progress";
                case SlotStatus.CannotAct: return "Can't act";
                default: return "Empty slot";
            }
        }

        public void CancelTargeting()
        {
            if (TargetingSlot < 0) return;
            TargetingSlot = -1;
            PendingTarget = null;
            board.ClearMarks();
            ClearTargetGlows();
            ShowBanner("");
        }

        /// <summary>Player-facing cancel (the confirm card's Cancel, a slot re-tap, Back): a soft "never mind" tone.</summary>
        public void CancelByPlayer()
        {
            if (TargetingSlot < 0) return;
            App.Audio.Sfx("ui.cancel", 0.8f);
            CancelTargeting();
            SyncBar();
            SyncPanel();
        }

        /// <summary>
        /// Lock a pick and ask for confirmation: the area and every unit it would touch light up, the banner names the
        /// deal, and the actor panel turns into the confirm card. Tapping another legal tile re-aims; tapping the same
        /// tile again (or Confirm) commits.
        /// </summary>
        private void SetPending(Coord anchor)
        {
            var hero = State.UnitById(SelectedHeroId);
            var a = hero == null || TargetingSlot < 0 ? null : BattleEngine.SlotAbility(State, hero, TargetingSlot);
            if (a == null) return;
            PendingTarget = anchor;
            RedrawTargeting();
            var hits = TargetResolver.Collect(State, a, hero, anchor);
            string who = hits.Count == 0 ? "" : hits.Count == 1 ? $" on {(hits[0] == hero ? "self" : hits[0].Name)}" : $" on {hits.Count} targets";
            ShowBanner($"{a.Name}{who}? Confirm below");
            App.Audio.Sfx("ui.select", 0.8f);
            SyncPanel();
            var btn = panel.Q<Button>("confirm-action");
            if (btn != null) App.StartCoroutine(Tween.Pop(btn, 1.08f, 0.25f));
        }

        /// <summary>Commit the pending pick (Confirm button, or a second tap on the same tile).</summary>
        public void ConfirmPending()
        {
            if (!PendingTarget.HasValue || TargetingSlot < 0 || !CanCommand()) return;
            var hero = State.UnitById(SelectedHeroId);
            if (hero == null) return;
            App.Audio.Sfx("ui.confirm");
            var cmd = new UseSlotCommand { HeroId = hero.Id, Slot = TargetingSlot, Target = PendingTarget.Value };
            Commit(cmd);
        }

        private readonly HashSet<int> targetGlows = new HashSet<int>();

        private void ClearTargetGlows()
        {
            foreach (var id in targetGlows)
                if (board.Units.TryGetValue(id, out var v)) v.SetGlow(Color.white, 0f);
            targetGlows.Clear();
            if (board.Units.TryGetValue(SelectedHeroId, out var sv)) sv.SetGlow(new Color(1f, 0.9f, 0.5f), 0.65f);
        }

        /// <summary>Legal anchors in gold; the pending pick's area (red harmful, green helpful, blue teleport) and its units glowing.</summary>
        private void RedrawTargeting()
        {
            var hero = State.UnitById(SelectedHeroId);
            var a = hero == null || TargetingSlot < 0 ? null : BattleEngine.SlotAbility(State, hero, TargetingSlot);
            board.ClearMarks();
            ClearTargetGlows();
            if (a == null) return;
            if (a.Mode != TargetMode.Auto)
                foreach (var vt in BattleEngine.ValidTargets(State, hero.Id, TargetingSlot)) board.Mark(vt, Palette.Gold, 0.25f);
            if (!PendingTarget.HasValue) return;
            var at = PendingTarget.Value;
            bool harmful = a.BaseDamage > 0 || a.Filter == TargetFilter.EnemyOnly;
            var color = a.IsTeleport ? Palette.Mana : harmful ? new Color(1f, 0.3f, 0.25f) : new Color(0.4f, 0.9f, 0.5f);
            var hits = TargetResolver.Collect(State, a, hero, at);
            if (a.Mode == TargetMode.Auto || a.Shape == TargetShape.Self || a.Shape == TargetShape.AllAllies || a.Shape == TargetShape.AllEnemies)
                foreach (var u in hits) foreach (var t in u.Tiles()) board.Mark(t, color, 0.42f);
            else foreach (var t in TargetResolver.Tiles(at, a.Shape, a.Radius)) board.Mark(t, color, 0.42f);
            foreach (var u in hits)
                if (board.Units.TryGetValue(u.Id, out var v)) { v.SetGlow(color, 0.9f); targetGlows.Add(u.Id); }
        }

        public override bool OnBack()
        {
            // Back/Escape on the open pause menu resumes, like its Resume row.
            if (pauseVeil != null) { closePause?.Invoke(); return false; }
            if (TargetingSlot >= 0) { CancelByPlayer(); return false; }
            OpenMenu();
            return false;
        }

        private VisualElement pauseVeil;
        private Action closePause;

        /// <summary>The pause menu is up: the battle's clock is stopped (GG-T126).</summary>
        public bool Paused => pauseVeil != null;
        /// <summary>Playback beats started so far this battle — the automation proof that none start while paused.</summary>
        public int PlaybackBeats { get; private set; }
        private int beatsAtPause;
        /// <summary>Beats that started while the menu was up (must stay 0; logged as an error when not).</summary>
        public int BeatsWhilePaused { get; private set; }
        /// <summary>Bank slot each landed orb flew to, in playback order (last 24) — the automation proof that orbs
        /// minted together fan into consecutive slots (GG-T112).</summary>
        public readonly List<int> OrbSlotsFlown = new List<int>();
        /// <summary>Seconds of unpaused battle time; the playback watchdog measures against this, so a long pause
        /// can't make it fire on Resume.</summary>
        private float battleTime;

        /// <summary>
        /// Stops the battle's clock (GG-T126): Tween.Dt reads 0, so every playback beat, cut-in, outline, float, coin
        /// burst, dissolve, banner scroll and ambient pulse holds exactly where it is; the event player and Safe()
        /// stop stepping; the drag timer, hints and auto battle stop; ringing sound effects pause mid-clip. Resume
        /// carries on from the same frame.
        /// </summary>
        private void PauseBattle()
        {
            Tween.Paused = true;
            beatsAtPause = PlaybackBeats;
            App.Audio.HoldSfx(true);
        }

        private void ResumeBattle()
        {
            Tween.Paused = false;
            App.Audio.HoldSfx(false);
        }

        /// <summary>The pause veil must be the last child of the Overlay, above the full-screen pincer cut-in, the
        /// billboard, coin and orb flights — everything playback adds there or BringToFront()s (GG-T126).</summary>
        /// <summary>Automation: the open pause veil is the topmost Overlay child (nothing of the battle draws over it).</summary>
        public bool PauseOnTop => pauseVeil != null && pauseVeil.parent != null && pauseVeil.parent.IndexOf(pauseVeil) == pauseVeil.parent.childCount - 1;

        private void KeepPauseOnTop()
        {
            if (pauseVeil != null && pauseVeil.parent != null && pauseVeil.parent.IndexOf(pauseVeil) != pauseVeil.parent.childCount - 1)
                pauseVeil.BringToFront();
        }

        private void OpenMenu()
        {
            // Escape/Back re-fires OnBack unconditionally (it doesn't know a veil is already up) — without
            // this guard, repeated presses before "Resume" stack another full veil on the global Overlay
            // each time, the same stale-overlay risk the board.Lines/Confirm.DismissStale comments warn about.
            if (ended || pauseVeil != null) return;
            var overlay = App.Overlay;
            var veil = pauseVeil = Ui.El("fill veil col center", overlay);
            veil.name = "pause-veil";
            veil.pickingMode = PickingMode.Position;
            PauseBattle();
            var w = Ui.Window("Paused", veil);
            w.style.width = Length.Percent(72);
            void Close() { veil.RemoveFromHierarchy(); pauseVeil = null; ResumeBattle(); }
            closePause = Close;
            Ui.MenuRow("Resume", "", Close, w);
            Ui.MenuRow(AutoPlay ? "Auto battle: ON" : "Auto battle: OFF", "", () => { AutoPlay = !AutoPlay; Close(); }, w);
            Ui.MenuRow("How to play", "", () => { Close(); App.Router.Push(new SettingsScreen()); }, w);
            int retreatPct = (int)(BattleConfig.RetreatKeepFraction * 100);
            if (InCrucible)
                Ui.MenuRow("Leave the Crucible", "(keep all)", () => { Close();
                    Confirm.Show($"Leave the Crucible after {State.Wave} wave{(State.Wave == 1 ? "" : "s")}? You keep every coin, item and point of experience earned so far.", "Leave", Retreat); }, w);
            else
                Ui.MenuRow("Retreat", $"(keep {retreatPct}%)", () => { Close();
                    Confirm.Show($"Retreat from battle? You'll keep {retreatPct}% of this fight's gold and loot — safer than risking a wipe.", "Retreat", Retreat); }, w);
            Ui.Select(w[1]);
        }

        // ── Battle records (§22.6): every battle is saved as JSON for balance analysis and future ML ──
        private BattleRecorder recorder;
        private int autoplayedTurns;
        public string LastRecordPath { get; private set; }

        private void SaveRecord()
        {
            if (recorder == null) return;
            try
            {
                var doc = recorder.Finish(State);
                if (autoplayedTurns > 0) doc["source"] = autoplayedTurns >= recorder.Turns ? "bot:autoplay" : "player+autoplay";
                var art = CommandLine.Value("-artifacts");
                var dir = CommandLine.Value("-records") ?? (art != null ? System.IO.Path.Combine(art, "records") : System.IO.Path.Combine(Application.persistentDataPath, "battle-records"));
                System.IO.Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{State.Stage.Id}_{State.Phase}.json");
                System.IO.File.WriteAllText(file, recorder.ToJson());
                LastRecordPath = file;
                Debug.Log($"[Battle] record saved: {file} ({recorder.Turns} turns)");
            }
            catch (Exception e) { Debug.LogWarning($"[Battle] could not save the battle record: {e.Message}"); }
            recorder = null;
        }

        private void Retreat()
        {
            // Retreating while an event list is still animating would tear down this screen out from under
            // coroutines that `App` (the persistent singleton, not this screen) is still running for it —
            // they'd keep playing sfx/vfx into whatever screen comes next. Same Busy gate as CanCommand().
            // The Confirm dialog outlives the pause menu, so it can be answered after the battle already ended
            // (autoplay or the last playback finishing behind it): EndBattle has set `ended` and is about to
            // Commit — retreating then would overwrite Victory/Defeat and Commit the battle twice (GG-T70).
            if (playing || ended || State.IsOver) return;
            ended = true;
            SaveRecord();
            if (App.Save != null && CommitsToSave)
            {
                State.Phase = BattlePhase.Retreated;
                var rep = BattleFactory.Commit(App.Content, App.Save, State);
                App.Persist();
                PostBattleScreen.RouteFrom(rep, State.Stage.Id);
            }
            else App.Router.Back();
        }

        public override void Tick(float dt)
        {
            if (Paused)
            {
                // GG-T126: the battle's clock is stopped — no watchdog, drag timer, hint, long-press or auto battle
                // runs, and the veil stays above anything a frozen beat already put on the Overlay.
                KeepPauseOnTop();
                if (PlaybackBeats != beatsAtPause)
                {
                    BeatsWhilePaused += PlaybackBeats - beatsAtPause;
                    Debug.LogError($"[Pause] {PlaybackBeats - beatsAtPause} playback beat(s) advanced while paused");
                    beatsAtPause = PlaybackBeats;
                }
                return;
            }
            battleTime += dt;
            // Watchdog: playback can never hold input hostage.
            if (playing && battleTime - playStarted > 20f)
            {
                Debug.LogError("[Battle] playback watchdog fired; resyncing");
                playing = false;
                SyncAll();
            }
            TickLinger(dt);
            if (dragging && !simulatingDrag && App.Settings.DragTimer)
            {
                dragTimeLeft -= dt;
                timerLabel.text = $"{Mathf.Max(0, dragTimeLeft):0.0}";
                if (dragTimeLeft <= 0f) CommitDrag();
            }
            else if (timerLabel != null) timerLabel.text = "";

            TickBoardInspect();
            if (TutorialActive && !LessonWaiting && CurrentLesson.DoneOnInspect && InspectCard.IsOpen) NextLesson();

            if (!AutoPlay && CanCommand() && !pressing && TargetingSlot < 0 && HintsEnabled)
            {
                idle += dt;
                if (idle > (TutorialDemoMove ? 0.8f : HintDelay) && !hintShown) ShowHint();
            }
            else ClearHint();

            if (AutoPlay && CanCommand() && !pressing)
            {
                autoBot ??= new HeroBot(BotSeed ?? Environment.TickCount);
                autoBot.SkillLevel = BotSkillProfile.Level;
                var cmd = autoBot.Decide(State);
                BattleCommand perfect = null;
                if (BotSkillProfile.TelemetryEnabled && BotSkillProfile.Level < 99.9f)
                {
                    perfectBot ??= new HeroBot(BotSeed ?? Environment.TickCount) { SkillLevel = 100f };
                    perfect = perfectBot.Decide(State);
                }
                if (cmd is MoveCommand m && m.Path.Count > 0) App.StartCoroutine(SimulatedHumanDrag(m, perfect));
                else Commit(cmd, perfectCmd: perfect);
            }
        }

        /// <summary>
        /// Plays a bot move as a human-like simulated drag, driving the real pointer pipeline
        /// (<see cref="HandlePointerPosition"/>) instead of teleporting the hero tile-to-tile — so a diagonal
        /// slip succeeds or fails for the same kinematic reasons a real player's drag would, and the capture
        /// footage shows an actual hand-shaped path rather than a mechanical snap. The route is built from a
        /// cubic Bézier through every waypoint of <paramref name="m"/>'s path (Catmull-Rom-style tangents so
        /// corners carry velocity through them instead of stopping dead — a dead stop can never clear the
        /// diagonal-slip speed gate), timed by Fitts's Law and eased by a minimum-jerk profile, with
        /// physiological tremor and an occasional overshoot-then-correct, all scaled by BotSkillProfile.
        /// <paramref name="perfectCmd"/>: the strict-best move at this same decision (telemetry only, may be null).
        /// </summary>
        private IEnumerator SimulatedHumanDrag(MoveCommand m, BattleCommand perfectCmd)
        {
            if (!BeginSimulatedDrag(m.HeroId)) yield break;
            playing = true;
            simulatingDrag = true;
            playStarted = battleTime;

            var hero = State.UnitById(m.HeroId);
            var pts = new List<Vector2> { BoardView.CenterOf(hero.Pos) };
            foreach (var step in m.Path) pts.Add(BoardView.CenterOf(step));

            float skill01 = BotSkillProfile.Skill01;
            float totalLen = 0f;
            for (int i = 1; i < pts.Count; i++) totalLen += Vector2.Distance(pts[i - 1], pts[i]);
            float duration = Mathf.Max(0.08f, BotSkillProfile.FittsDuration(totalLen, BoardView.Tile));
            float tremor = BotSkillProfile.TremorPixels;
            bool willOvershoot = pts.Count > 0 && UnityEngine.Random.value < BotSkillProfile.OvershootChance;
            var overshootDir = pts.Count > 1 ? (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized : Vector2.zero;
            var overshootPoint = pts[pts.Count - 1] + overshootDir * Mathf.Lerp(45f, 8f, skill01);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float tau = BotSkillProfile.MinimumJerk(elapsed / duration);
                var basePos = EvaluateHumanPath(pts, tau, skill01);
                var jitter = new Vector2(Mathf.PerlinNoise(elapsed * 11f, 0.37f) - 0.5f, Mathf.PerlinNoise(0.91f, elapsed * 11f) - 0.5f) * tremor;
                HandlePointerPosition(basePos + jitter);
                if (!pressing) { playing = false; simulatingDrag = false; yield break; } // hero died / battle ended mid-drag
                yield return null;
                elapsed += Tween.Dt;
            }
            if (willOvershoot)
            {
                float ct = 0f; const float correctDuration = 0.1f;
                HandlePointerPosition(overshootPoint);
                while (ct < correctDuration)
                {
                    HandlePointerPosition(Vector2.Lerp(overshootPoint, pts[pts.Count - 1], ct / correctDuration));
                    yield return null;
                    ct += Tween.Dt;
                }
            }
            HandlePointerPosition(pts[pts.Count - 1]);
            playing = false;
            simulatingDrag = false;
            CommitDrag(perfectCmd);
        }

        /// <summary>
        /// A point at parametric position <paramref name="tau"/> (0-1) along a smoothed polyline through
        /// <paramref name="pts"/>: each interior waypoint is pulled toward the corner's bisector by an amount
        /// that grows with skill (a confident player cuts the corner close to a true diagonal; a clumsy one
        /// hugs the exact tile-to-tile route — see <see cref="BotSkillProfile.DiagonalAttemptChance"/>'s sibling
        /// reasoning), and consecutive segments share Catmull-Rom tangents so the path — and its velocity —
        /// stays continuous through every corner instead of stopping and restarting at each tile.
        /// </summary>
        private static Vector2 EvaluateHumanPath(List<Vector2> pts, float tau, float skill01)
        {
            if (pts.Count < 2) return pts.Count == 1 ? pts[0] : Vector2.zero;
            float cut = Mathf.Lerp(0.05f, 0.8f, skill01);
            var shaped = new List<Vector2> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
                shaped.Add(Vector2.Lerp(pts[i], Vector2.Lerp(pts[i - 1], pts[i + 1], 0.5f), cut));
            shaped.Add(pts[pts.Count - 1]);

            float[] segLen = new float[shaped.Count - 1];
            float total = 0f;
            for (int i = 0; i < segLen.Length; i++) { segLen[i] = Vector2.Distance(shaped[i], shaped[i + 1]); total += segLen[i]; }
            if (total <= 0f) return shaped[0];

            float target = Mathf.Clamp01(tau) * total, acc = 0f;
            int seg = 0;
            while (seg < segLen.Length - 1 && acc + segLen[seg] < target) { acc += segLen[seg]; seg++; }
            float localTau = segLen[seg] > 0f ? (target - acc) / segLen[seg] : 0f;

            var p0 = shaped[seg];
            var p3 = shaped[seg + 1];
            var prev = seg > 0 ? shaped[seg - 1] : p0 - (p3 - p0);
            var next = seg + 2 < shaped.Count ? shaped[seg + 2] : p3 + (p3 - p0);
            var p1 = p0 + (p3 - prev) / 6f;
            var p2 = p3 - (next - p0) / 6f;
            float u = 1f - localTau;
            return u * u * u * p0 + 3f * u * u * localTau * p1 + 3f * u * localTau * localTau * p2 + localTau * localTau * localTau * p3;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Selection / banner helpers
        // ─────────────────────────────────────────────────────────────────────────

        private void ShowHint()
        {
            hintShown = true;
            var decision = TutorialDemoMove ? (BattleCommand)new HeroBot(7).BestMove(State) ?? new HeroBot(7).Decide(State) : new HeroBot(7).Decide(State);
            if (decision is UseSlotCommand use)
            {
                var caster = State.UnitById(use.HeroId);
                var ab = caster == null ? null : BattleEngine.SlotAbility(State, caster, use.Slot);
                if (caster != null && ab != null)
                {
                    SelectHero(caster.Id);
                    if (use.Slot < slotViews.Count) App.StartCoroutine(Tween.Pop(slotViews[use.Slot], 1.12f, 0.35f));
                    ShowBanner($"Hint: {caster.Name} can use {ab.Name}");
                }
                return;
            }
            if (decision is ShieldCommand) { ShowBanner("Hint: nothing good to do? Press GUARD"); return; }
            var cmd = decision as MoveCommand;
            if (cmd == null || cmd.Path.Count == 0) return;
            var pv = BattleEngine.PreviewMove(State, cmd.HeroId, cmd.Path);
            if (pv.Pincers.Count == 0) { ShowBanner("Tip: trap an enemy between two heroes"); return; }
            var hero = State.UnitById(cmd.HeroId);
            var dest = cmd.Path[cmd.Path.Count - 1];
            board.ClearMarks();
            board.Mark(dest, Palette.Gold, 0.5f);
            if (board.Units.TryGetValue(hero.Id, out var hv)) { hv.SetGlow(Palette.Gold, 0.95f); App.StartCoroutine(Tween.Pop(hv.Root, 1.12f, 0.35f)); }
            board.Lines.ClearLines();
            var from = BoardView.CenterOf(hero.Pos);
            foreach (var step in cmd.Path) { var to = BoardView.CenterOf(step); board.Lines.Add(from, to, Palette.Gold.WithA(0.55f), 10f); from = to; }
            ShowBanner($"Hint: drag {hero.Name} to the glowing tile");
        }

        private void ClearHint()
        {
            idle = 0f;
            if (!hintShown) return;
            hintShown = false;
            if (TargetingSlot < 0 && !dragging) { board.ClearMarks(); board.Lines.ClearLines(); }
        }

        public void SelectHero(int id)
        {
            if (SelectedHeroId == id)
            {
                // Re-touching the selected hero still dismisses an inspected enemy card (it hid the tutorial card).
                if (panelUnitId >= 0) { panelUnitId = -1; SyncPanel(); }
                return;
            }
            CancelTargeting();
            SelectedHeroId = id;
            panelUnitId = -1;
            foreach (var u in State.Units)
                if (board.Units.TryGetValue(u.Id, out var v) && u.Alive)
                    v.SetGlow(new Color(1f, 0.9f, 0.5f), u.Id == id ? 0.65f : 0f);
            SyncBar();
            SyncPanel();
        }

        /// <summary>Combat feed (bible §9 row 4): the last few events as fading lines over the board's corner.</summary>
        public void Feed(string text, Color color)
        {
            if (feed == null || string.IsNullOrEmpty(text)) return;
            var l = Ui.Text(text, "tiny shadow", feed);
            l.style.color = color;
            l.style.fontSize = 30;
            l.style.whiteSpace = WhiteSpace.Normal; // a long line wraps inside the 640px feed instead of spilling over the board (GG-T110)
            while (feed.childCount > 3) feed.RemoveAt(0);
            App.StartCoroutine(FeedFade(l));
        }

        private IEnumerator FeedFade(Label l)
        {
            float hold = 0f;
            while (hold < 4f && l.parent != null) { hold += Tween.Dt; yield return null; } // game time: holds while paused
            float t = 0;
            while (t < 0.6f && l.parent != null) { t += Tween.Dt; l.style.opacity = 1f - t / 0.6f; yield return null; }
            l.RemoveFromHierarchy();
        }

        /// <summary>
        /// Action/status banner above the board; long text steps the font down, then wraps, and if it still doesn't
        /// fit its viewport it's shown, paused, then slowly scrolled so the whole message gets read.
        /// </summary>
        private void ShowBanner(string text)
        {
            if (banner == null) return;
            text ??= "";
            banner.text = text;
            banner.style.fontSize = text.Length > 44 ? 32 : text.Length > 32 ? 38 : 48;
            banner.style.top = 0;
            if (bannerScrollRoutine != null) { App.StopCoroutine(bannerScrollRoutine); bannerScrollRoutine = null; }
            if (text.Length > 0) bannerScrollRoutine = App.StartCoroutine(ScrollBannerIfNeeded());
        }

        private IEnumerator ScrollBannerIfNeeded()
        {
            yield return null; // one frame so the wrapped text has been laid out and resolvedStyle.height is current
            float content = banner.resolvedStyle.height;
            if (content <= BannerViewportHeight)
            {
                banner.style.top = (BannerViewportHeight - content) / 2f;
                bannerScrollRoutine = null;
                yield break;
            }
            float travel = content - BannerViewportHeight;
            yield return Tween.Wait(Timing.Cinematic); // let the reader start before it moves
            yield return Tween.Run(Mathf.Clamp(travel / 40f, 0.8f, 4f), k => banner.style.top = -travel * k);
            yield return Tween.Wait(Timing.Dramatic); // hold on the tail before the next message replaces it
            bannerScrollRoutine = null;
        }
    }
}
