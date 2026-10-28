using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>Base for hub sub-screens: background art, header with back + gold, body column.</summary>
    public abstract class HubPage : GameScreen
    {
        protected Label GoldLabel;
        protected VisualElement Body;
        protected abstract string Title { get; }
        protected virtual string Backdrop => "greenvalley";
        /// <summary>Optional line shown under the header title (e.g. station level + next unlock, GG-T35).</summary>
        protected virtual string Subtitle => null;
        /// <summary>The Camp NPC who keeps this station (GG-T119), or null: the header gets a Talk button for them.</summary>
        protected virtual string Npc => null;
        /// <summary>The camp tip this page shows (GG-T173): its own Id, unless a tab has its own tip (the Blacksmith's Forge, GG-T177).</summary>
        protected virtual string TipStation => Id;

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(Backdrop, 1));
            bg.style.unityBackgroundImageTintColor = new Color(0.45f, 0.5f, 0.7f, 1f);
            var col = Ui.El("fill col", root);
            var header = Ui.Header(Title, () => App.Router.Back(), col, out GoldLabel);
            // Every Camp NPC can be talked to from their own station as well as from Camp's Talk row (GG-T119).
            var topic = Npc != null && CampNpcs.Joined(S, Npc) ? DialogueRunner.TalkTopic(C, S, ContentLoader.NpcFlagPrefix + Npc) : null;
            if (topic != null)
            {
                var talk = Ui.Button(S.SeenDialogues.Contains(topic.Id) ? "Talk" : "Talk!", () => App.Router.Push(new DialogueScreen(topic, (string)null, (GameScreen)null)), "btn--small", null);
                talk.name = "npc-talk";
                header.Insert(header.IndexOf(GoldLabel), talk);
            }
            if (Subtitle != null)
            {
                var sub = Ui.Text(Subtitle, "small gold", col);
                sub.style.marginLeft = 16;
                sub.style.marginTop = -6;
            }
            Body = Ui.El("col grow", col);
            Body.style.paddingLeft = Body.style.paddingRight = 10;
            Body.style.paddingBottom = 30;
            BuildBody(Body);
            UpdateGold();
            CampTipOverlay.ShowIfPending(this, TipStation);
        }

        protected abstract void BuildBody(VisualElement body);

        protected void UpdateGold()
        {
            if (GoldLabel != null && App.Save != null) GoldLabel.text = Ui.Gold(App.Save.Gold);
        }

        protected void Done(Result r, bool persist = true)
        {
            App.Toast(r.Message ?? (r.Ok ? "Done" : "Can't do that"));
            App.Audio.Sfx(r.Ok ? "ui.buy" : "ui.error");
            if (r.Ok && persist) App.Persist();
            Refresh();
        }

        protected ContentDb C => App.Content;
        protected SaveData S => App.Save;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Title
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class TitleScreen : GameScreen
    {
        public override string Id => "title";
        public override string Music => "TellerOfTheTales";

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background("cityruins", 0));
            var shade = Ui.El("fill", root);
            shade.style.backgroundColor = new Color(0, 0, 0.05f, 0.45f);

            var col = Ui.El("fill col center", root);
            var title = Ui.Text("GRIDGAME", "title shadow", col);
            Fonts.UseDisplay(title);
            var sub = Ui.Text("GAIDEN", "title shadow", col);
            Fonts.UseDisplay(sub);
            sub.style.fontSize = 150;
            sub.style.color = new Color(1f, 0.82f, 0.36f);
            var tag = Ui.Text("Slide. Flank. Strike.", "h2 shadow dim", col);
            tag.style.marginTop = 10;
            tag.style.marginBottom = 160;

            var menu = Ui.Window(null, col);
            menu.style.width = 760;
            int recent = App.Saves.MostRecent();
            Debug.Log($"[Title] most recent save slot: {recent}");
            if (recent >= 0) Ui.MenuRow("Continue", "", () => Continue(recent), menu);
            Ui.MenuRow("New Game", "", () => App.Router.Push(new SaveSlotScreen(true)), menu);
            Ui.MenuRow("Load Game", "", () => App.Router.Push(new SaveSlotScreen(false)), menu, null, recent >= 0);
            Ui.MenuRow("Credits", "", () => App.Router.Push(new CreditsScreen()), menu);
            Ui.SelectFirst(menu);

            var ver = Ui.Text($"v{Application.version}", "tiny dim", root);
            ver.style.position = Position.Absolute;
            ver.style.right = 30; ver.style.bottom = 24;
        }

        private void Continue(int slot)
        {
            Debug.Log($"[Title] continue slot {slot}");
            var s = App.Saves.Load(slot, App.Content);
            if (s == null) { App.Toast("That save is damaged"); return; }
            App.Save = s; App.Slot = slot;
            App.ApplySettings();
            App.Router.ResetTo(new HubScreen());
        }
    }

    public sealed class SaveSlotScreen : GameScreen
    {
        private readonly bool newGame;
        public SaveSlotScreen(bool newGame) { this.newGame = newGame; }
        public override string Id => newGame ? "newgame" : "load";

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background("cityruins", 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.55f);
            var col = Ui.El("fill col", root);
            Ui.Header(newGame ? "New Game" : "Load Game", () => App.Router.Back(), col, out _);
            var w = Ui.Window(newGame ? "Choose a slot" : "Choose a save", col);
            for (int i = 0; i < SaveStore.Slots; i++)
            {
                int slot = i;
                var s = App.Saves.Load(i, App.Content);
                string label = s == null ? $"Slot {i + 1} — Empty" : $"Slot {i + 1} — {s.Name}";
                string value = s == null ? "" : $"Stage {s.HighestClearedStage + 2}  {Ui.Gold(s.Gold)}";
                var row = Ui.MenuRow(label, value, () => Pick(slot, s), w, null, newGame || s != null);
                if (s != null)
                {
                    var info = Ui.Text($"{s.Roster.Count} heroes · {TimeSpan.FromSeconds(s.PlaySeconds):h\\:mm} played · {s.UpdatedUtc.ToLocalTime():g}", "small dim", w);
                    info.style.marginLeft = 64; info.style.marginBottom = 14;
                }
            }
        }

        private void Pick(int slot, SaveData existing)
        {
            if (!newGame)
            {
                App.Save = existing; App.Slot = slot;
                App.ApplySettings();
                App.Router.ResetTo(new HubScreen());
                return;
            }
            if (existing != null)
            {
                Confirm.Show($"Overwrite slot {slot + 1}?", "Overwrite", () => Start(slot));
                return;
            }
            Start(slot);
        }

        private void Start(int slot)
        {
            App.Router.Push(new CharacterSelectScreen(party =>
            {
                App.Save = Progression.NewGame(App.Content, "Player", party);
                App.Slot = slot;
                App.ApplySettings();
                App.Persist();
                App.Router.ResetTo(new HubScreen());
            }));
        }
    }

    /// <summary>
    /// Fighting-game-style roster pick (owner request: "a grid at the bottom, select a tile, the full
    /// artwork appears on top, lock in that character"). Every hero class is open here regardless of
    /// normal unlocks; <see cref="PartySize"/> picks build the small starting party — the rest of the five join
    /// at story beats (GG-T118). <c>-demo</c> drives the same screen by tapping random tiles instead of a
    /// human, so each attract-mode run shows a different squad.
    /// </summary>
    public sealed class CharacterSelectScreen : GameScreen
    {
        public const int PartySize = Progression.StartingPartySize;
        private readonly Action<List<string>> onDone;
        private readonly bool auto;
        private readonly List<string> locked = new List<string>();
        private string highlighted;
        private bool finishing;
        private Coroutine finishRoutine;
        private VisualElement partyStrip, revealPanel, grid;
        private Button lockBtn;

        /// <summary>Without this, backing out during the 0.7s Lock-In fanfare (the header's Back button has no
        /// `finishing` guard) left Finish() running: it still fired onDone() afterward, silently starting/
        /// overwriting a save and jumping to the Hub out from under a player who'd just tried to cancel —
        /// the same failure class as LevelUpScreen's fanfare coroutine (GG-T127).</summary>
        public override void OnHidden()
        {
            if (finishRoutine != null) App.StopCoroutine(finishRoutine);
            finishRoutine = null;
        }

        public CharacterSelectScreen(Action<List<string>> onDone, bool auto = false)
        {
            this.onDone = onDone;
            this.auto = auto || DemoMode.Active;
        }

        public override string Id => "charselect";
        public override string Music => "MinstrelGuild";

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background("cityruins", 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.55f);
            var col = Ui.El("fill col", root);
            Ui.Header("Choose Your Party", () => App.Router.Back(), col, out _);
            var hint = Ui.Text($"Pick {PartySize} to set out with — more heroes join along the road.", "small dim", col);
            hint.name = "charselect-hint";
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;

            partyStrip = Ui.El("row", col);
            partyStrip.style.justifyContent = Justify.Center;
            partyStrip.style.marginTop = 6; partyStrip.style.marginBottom = 10;

            revealPanel = Ui.Window(null, col);
            revealPanel.style.minHeight = 420;

            lockBtn = Ui.Button("Lock In", LockInHighlighted, "btn--primary", col);
            lockBtn.style.alignSelf = Align.Center;
            lockBtn.style.width = 300;
            lockBtn.style.marginBottom = 10;

            var gridWindow = Ui.Window("Every hero — tap to preview, tap again to lock in", col);
            gridWindow.style.flexGrow = 1;
            grid = Ui.List(gridWindow).contentContainer;
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;
            foreach (var def in App.Content.Classes.Values.Where(d => d.Team == Team.Hero).OrderBy(d => d.Name))
            {
                var id = def.Id;
                var t = Ui.Thumb(id, grid, 164);
                t.name = "tile-" + id;
                t.style.marginLeft = t.style.marginRight = t.style.marginTop = t.style.marginBottom = 10;
                t.AddToClassList("card-tap");
                Ui.Tappable(t, () => Pick(id));
            }

            RefreshState();
            if (auto) App.StartCoroutine(AutoPlay());
        }

        private void Pick(string id)
        {
            if (finishing || locked.Contains(id)) return;
            if (highlighted == id) { LockInHighlighted(); return; }
            highlighted = id;
            App.Audio.Sfx("ui.select");
            RefreshState();
        }

        private void LockInHighlighted()
        {
            if (finishing || highlighted == null || locked.Contains(highlighted)) return;
            locked.Add(highlighted);
            highlighted = null;
            App.Audio.Sfx("ui.buy");
            RefreshState();
            if (locked.Count >= PartySize) { finishing = true; finishRoutine = App.StartCoroutine(Finish()); }
        }

        private IEnumerator Finish()
        {
            yield return Tween.Wait(0.7f);
            onDone(new List<string>(locked));
        }

        /// <summary>Bombastic reveal (owner request: "slam into place similar to the Portraits"): oversized and
        /// tilted, punches down past full size with a back-ease overshoot, then a small settle shake.</summary>
        private IEnumerator SlamIn(VisualElement target)
        {
            target.style.scale = new Scale(Vector2.one * 1.5f);
            target.style.rotate = new Rotate(-6f);
            App.Audio.Sfx("shatter", 0.55f);
            yield return Tween.Run(0.22f, k =>
            {
                float s = Mathf.LerpUnclamped(1.5f, 1f, Tween.Back(k));
                target.style.scale = new Scale(Vector2.one * s);
                target.style.opacity = Mathf.Clamp01(k * 2.2f);
                target.style.rotate = new Rotate(Mathf.LerpUnclamped(-6f, 0f, k));
            });
            target.style.scale = new Scale(Vector2.one);
            target.style.opacity = 1f;
            target.style.rotate = new Rotate(0f);
            yield return Tween.Shake(target, 6f, 0.14f);
        }

        private IEnumerator AutoPlay()
        {
            var rng = new System.Random();
            while (!finishing && locked.Count < PartySize)
            {
                yield return Tween.Wait(0.5f);
                var pool = App.Content.Classes.Values.Where(d => d.Team == Team.Hero && !locked.Contains(d.Id)).ToList();
                if (pool.Count == 0) yield break;
                Pick(pool[rng.Next(pool.Count)].Id);
                yield return Tween.Wait(0.6f);
                LockInHighlighted();
            }
        }

        private void RefreshState()
        {
            partyStrip.Clear();
            for (int i = 0; i < PartySize; i++)
            {
                var slot = Ui.El("col", partyStrip);
                slot.style.width = 108; slot.style.marginLeft = slot.style.marginRight = 6; slot.style.alignItems = Align.Center;
                if (i < locked.Count)
                {
                    Ui.Thumb(locked[i], slot, 96);
                    var nm = Ui.Text(App.Content.Class(locked[i]).Name, "tiny", slot);
                    nm.style.unityTextAlign = TextAnchor.MiddleCenter;
                }
                else
                {
                    var empty = Ui.El("thumb", slot);
                    empty.style.width = empty.style.height = 96;
                    empty.style.opacity = 0.3f;
                }
            }

            revealPanel.Clear();
            var showId = highlighted ?? (locked.Count > 0 && !finishing ? locked[locked.Count - 1] : null);
            if (finishing) { Ui.Text("Party complete!", "h1 shadow gold", revealPanel).style.unityTextAlign = TextAnchor.MiddleCenter; }
            else if (showId != null)
            {
                var def = App.Content.Class(showId);
                var row = Ui.El("row", revealPanel);
                row.style.opacity = 0f; // slammed into place below, not swapped in cold
                var frame = Ui.El(null, row);
                frame.style.width = 360; frame.style.height = 400;
                frame.style.borderTopWidth = frame.style.borderBottomWidth = frame.style.borderLeftWidth = frame.style.borderRightWidth = 4;
                var roleColor = Ui.RoleColor(def.Role);
                frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = roleColor;
                frame.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.6f);
                frame.style.backgroundImage = new StyleBackground(Art.Portrait(showId));
                frame.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                var info = Ui.El("col", row);
                info.style.marginLeft = 24; info.style.flexGrow = 1;
                var nm = Ui.Text(def.Name, "h1 shadow", info);
                Fonts.UseDisplay(nm);
                Ui.RoleBadge(def.Role, info).style.marginBottom = 10;
                var desc = Ui.Text(def.Description ?? "", "small dim", info);
                desc.style.whiteSpace = WhiteSpace.Normal;
                if (locked.Contains(showId)) Ui.Text("Locked in", "small good", info).style.marginTop = 10;
                App.StartCoroutine(SlamIn(row));
            }
            else Ui.Text("Tap a hero below to preview them.", "h2 dim", revealPanel).style.unityTextAlign = TextAnchor.MiddleCenter;

            foreach (var child in grid.Children())
            {
                if (!child.name.StartsWith("tile-")) continue;
                var id = child.name.Substring(5);
                bool isLocked = locked.Contains(id);
                child.SetEnabled(!isLocked && !finishing);
                child.style.opacity = isLocked ? 0.25f : (highlighted == id ? 1f : 0.85f);
                child.EnableInClassList("thumb--selected", highlighted == id);
            }
            Ui.SetEnabled(lockBtn, !finishing && highlighted != null && !locked.Contains(highlighted));
        }
    }

    /// <summary>Modal confirm dialog on the overlay layer.</summary>
    public static class Confirm
    {
        private const string VeilName = "confirm-veil";

        public static void Show(string question, string yes, Action onYes, Action onNo = null)
        {
            var overlay = GameApp.I.Overlay;
            var veil = Ui.El("fill veil col center", overlay);
            veil.name = VeilName;
            veil.pickingMode = PickingMode.Position;
            var w = Ui.Window(null, veil);
            w.style.width = 860;
            var q = Ui.Text(question, "h2", w);
            q.style.whiteSpace = WhiteSpace.Normal;
            q.style.marginBottom = 24;
            var row = Ui.El("row", w);
            row.style.justifyContent = Justify.FlexEnd;
            Ui.Button("Cancel", () => { veil.RemoveFromHierarchy(); onNo?.Invoke(); }, null, row);
            Ui.Button(yes, () => { veil.RemoveFromHierarchy(); onYes?.Invoke(); }, "btn--primary", row);
        }

        /// <summary>A confirm veil answers a question about the screen it was raised on — if that screen
        /// changes from under it (any navigation, or an automation call that swaps screens directly) with
        /// the veil never explicitly answered, it would otherwise stay in Overlay forever: a full-screen
        /// PickingMode.Position layer silently eating every future click with no error anywhere (the real
        /// root cause behind GG-T16 — not an Input System/UI Toolkit staleness bug as first suspected).
        /// ScreenRouter calls this on every navigation so a stale veil can never outlive its screen.</summary>
        public static void DismissStale()
        {
            var overlay = GameApp.I?.Overlay;
            if (overlay == null) return;
            foreach (var veil in overlay.Query(VeilName).ToList()) veil.RemoveFromHierarchy();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Camp hub — the FF-style main menu (merged hub, bible §25.9)
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class HubScreen : GameScreen
    {
        public override string Id => "hub";
        public override string Music => "MinstrelGuild";

        protected override void Build(VisualElement root)
        {
            var c = App.Content; var s = App.Save;
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(ThemeFor(c, s), 2));
            bg.style.unityBackgroundImageTintColor = new Color(0.55f, 0.6f, 0.75f);

            var col = Ui.El("fill col", root);
            col.style.paddingTop = 40; col.style.paddingLeft = col.style.paddingRight = 16; col.style.paddingBottom = 30;

            // Party window (portraits, level, HP): a 2-column grid so a full party of six fits above the menu.
            var party = Ui.Window(null, col);
            party.name = "camp-party";
            party.style.flexDirection = FlexDirection.Row;
            party.style.flexWrap = Wrap.Wrap;
            party.style.justifyContent = Justify.SpaceBetween;
            foreach (var id in s.Party)
            {
                var h = s.Hero(id);
                if (h == null) continue;
                var u = Progression.Preview(c, h);
                var card = Ui.El("card", party);
                card.name = "camp-hero-" + id;
                card.style.width = Length.Percent(49);
                card.style.marginTop = card.style.marginBottom = 5;
                card.style.paddingTop = card.style.paddingBottom = card.style.paddingLeft = card.style.paddingRight = 8;
                var thumb = Ui.Thumb(id, card, 104);
                thumb.style.marginRight = 12;
                var info = Ui.El("col grow", card);
                info.style.flexShrink = 1; info.style.minWidth = 0;
                var top = Ui.El("row", info);
                var name = Ui.Text(c.Class(id).Name, "small grow", top);
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.overflow = Overflow.Hidden; name.style.textOverflow = TextOverflow.Ellipsis;
                Ui.RoleBadge(c.Class(id).Role, top, 20).style.marginLeft = 4;
                Ui.Text($"Lv {u.Level}", "small gold", top);
                int hp = h.Hp > 0 ? Math.Min(h.Hp, u.MaxHp) : u.MaxHp;
                Ui.Gauge(hp / (float)u.MaxHp, info, 300).style.marginTop = 6;
                Ui.Text($"{hp}/{u.MaxHp}", "tiny dim", info);
                card.AddToClassList("card-tap");
                Ui.Tappable(card, () => App.Router.Push(new StatusScreen(id)), "ui.select");
            }

            var lower = Ui.El("row grow", col);
            lower.style.alignItems = Align.Stretch;

            var menu = Ui.Window(null, lower);
            menu.style.flexGrow = 1;
            var list = Ui.List(menu);
            void Item(string label, Func<GameScreen> screen, string value = "") => Ui.MenuRow(label, value, () => App.Router.Push(screen()), list.contentContainer);
            // Stations open one at a time (GG-T174, §25.0.3): a closed one is a dimmed row saying when it arrives.
            void Station(string label, bool open, string hint, Func<GameScreen> screen, string value = "")
            {
                if (open) { Item(label, screen, value); return; }
                var row = Ui.MenuRow(label, hint, null, list.contentContainer, null, false);
                row.name = "locked-" + label.ToLowerInvariant().Replace(' ', '-');
                row.Q<Label>("value").AddToClassList("small");
            }
            Item("Adventure", () => new StageSelectScreen(), $"Stage {Math.Min(s.HighestClearedStage + 2, c.CampaignStages().Count)}");
            // The Crucible (§22.10): endless waves for XP and loot. Closed mid-expedition — the row says why.
            if (CampUnlocks.CrucibleOpen(s))
            {
                bool crucibleOpen = Crucible.CanLaunch(c, s, out var crucibleWhy);
                var crucible = Ui.MenuRow("Crucible", crucibleOpen ? CrucibleScreen.RecordText(s) : crucibleWhy, () => App.Router.Push(new CrucibleScreen()), list.contentContainer, null, crucibleOpen);
                crucible.name = "camp-crucible";
            }
            else Station("Crucible", false, CampUnlocks.MilestoneHint(c, CampUnlocks.CrucibleAfter), null);
            Item("Items", () => new ItemsScreen());
            Item("Status", () => new StatusScreen(s.Party.FirstOrDefault()));
            Item("Equip", () => new EquipScreen(s.Party.FirstOrDefault()));
            // GG-T180 (§25.0.9): onboarding step 7 introduces training here once the first armor enchant is bound.
            Item("Abilities", () => new AbilitiesScreen(s.Party.FirstOrDefault()), Training.Step(s) ? "Train a skill!" : "");
            Item("Party", () => new PartyScreen());
            // Talk (§27.1, GG-T117): chat with party members and joined Camp NPCs.
            int newTalks = DialogueRunner.TalkPartners(s).Count(p => { var t = DialogueRunner.TalkTopic(c, s, p); return t != null && !s.SeenDialogues.Contains(t.Id); });
            Item("Talk", () => new TalkScreen(), newTalks > 0 ? $"{newTalks} new" : "");
            // Camp NPCs (GG-T119): each station opens once its keeper has joined the caravan at a story beat.
            Station("Merchant", CampUnlocks.NpcOpen(s, CampNpcs.Merchant), CampUnlocks.JoinHint(c, CampNpcs.Merchant), () => new ShopScreen());
            if (CampUnlocks.NpcOpen(s, CampNpcs.Innkeeper))
            {
                if (Expeditions.InnOpen(s)) Ui.MenuRow(s.Expedition != null ? "Inn (Medic Wagon)" : "Inn", Inn.Cost(c, s) > 0 ? Ui.Gold(Inn.Cost(c, s)) : "Rested", Rest, list.contentContainer);
                else Ui.MenuRow("Inn", "On the road", null, list.contentContainer, null, false);
            }
            else Station("Inn", false, CampUnlocks.JoinHint(c, CampNpcs.Innkeeper), null);
            Station("Blacksmith", CampUnlocks.NpcOpen(s, CampNpcs.Blacksmith), CampUnlocks.JoinHint(c, CampNpcs.Blacksmith), () => new BlacksmithScreen());
            Station("Alchemist", CampUnlocks.NpcOpen(s, CampNpcs.Alchemist), CampUnlocks.JoinHint(c, CampNpcs.Alchemist), () => new AlchemistScreen());
            Station("Enchantress", CampUnlocks.NpcOpen(s, CampNpcs.Enchantress), CampUnlocks.JoinHint(c, CampNpcs.Enchantress), () => new EnchantressScreen());
            Station("Bounties", CampUnlocks.BountiesOpen(s), CampUnlocks.MilestoneHint(c, CampUnlocks.BountiesAfter), () => new BountyScreen(),
                Bounties.Active(c, s) != null ? $"{s.Bounty.Progress}/{Bounties.Active(c, s).Kills}" : "");
            Station("Summon Circle", CampUnlocks.SummonOpen(s), CampUnlocks.MilestoneHint(c, CampUnlocks.SummonAfter), () => new SummonScreen());
            int caravanLevels = c.Caravan.Keys.Sum(k => CaravanUpgrades.Level(s, k));
            Station("Caravan", CampUnlocks.CaravanOpen(s), CampUnlocks.MilestoneHint(c, CampUnlocks.CaravanAfter), () => new CaravanScreen(), caravanLevels > 0 ? $"{caravanLevels} upgrades" : "");
            Item("Bestiary", () => new BestiaryScreen());
            Item("Settings", () => new SettingsScreen());
            if (Progression.CanStartNewGamePlus(c, s))
                Ui.MenuRow($"New Game+ {s.NewGamePlus + 1}", "", () => Confirm.Show("Start New Game+? Keep your heroes, gear and gold; the campaign restarts with enemies 10 levels stronger.", "Begin", () =>
                {
                    var r = Progression.StartNewGamePlus(App.Content, App.Save);
                    App.Toast(r.Message);
                    if (r.Ok) { App.Persist(); Refresh(); }
                }), list.contentContainer).Q<Label>("label").AddToClassList("gold");
            Ui.MenuRow("Save & Quit", "", () => { App.Persist(); App.Save = null; App.Slot = -1; App.Router.ResetTo(new TitleScreen()); }, list.contentContainer);
            Ui.Select(list.contentContainer[0]);

            var side = Ui.El("col", lower);
            side.style.width = 330;
            if (s.Expedition != null)
            {
                var ex = Ui.Window(null, side);
                ex.name = "expedition-strip";
                var th = Expeditions.Theme(c, s.Expedition.ThemeId);
                Ui.Text("Expedition", "small dim", ex);
                Ui.Text(th?.Name ?? "", "small gold", ex).style.whiteSpace = WhiteSpace.Normal;
                Ui.Text($"Battle {s.Expedition.Next + 1}/{th?.Stages.Count ?? 0}", "small", ex);
                Ui.Text($"{s.Expedition.CarriedOrbs} orbs carried", "tiny dim", ex);
            }
            var goldW = Ui.Window(null, side);
            Ui.Text("Gold", "small dim", goldW);
            Ui.Text(Ui.Gold(s.Gold), "h2 gold num", goldW);
            Ui.Text("Time", "small dim", goldW).style.marginTop = 16;
            Ui.Text(TimeSpan.FromSeconds(s.PlaySeconds).ToString(@"h\:mm\:ss"), "h2 num", goldW);
            if (s.NewGamePlus > 0) Ui.Text($"New Game+ {s.NewGamePlus}", "h2 gold", goldW).style.marginTop = 16;
            Ui.Text("Cleared", "small dim", goldW).style.marginTop = 16;
            Ui.Text($"{s.ClearedStages.Count(id => c.Stages.ContainsKey(id) && !c.Stage(id).IsTest)}/{c.CampaignStages().Count}", "h2 num", goldW);
        }

        private void Rest()
        {
            var r = Inn.Rest(App.Content, App.Save);
            App.Toast(r.Message);
            App.Audio.Sfx(r.Ok ? "heal" : "ui.error");
            if (r.Ok) { App.Persist(); Refresh(); }
        }

        public static string ThemeFor(ContentDb c, SaveData s)
        {
            var list = c.CampaignStages();
            if (list.Count == 0) return "greenvalley";
            var st = list[Mathf.Clamp(s.HighestClearedStage + 1, 0, list.Count - 1)];
            return st.Theme ?? "greenvalley";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Stage select (newest-on-top, replayable, drops hint — §22.3) + story crawl (§27)
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class StageSelectScreen : HubPage
    {
        public override string Id => "stages";
        protected override string Title => "Adventure";
        protected override string Backdrop => HubScreen.ThemeFor(C, S);

        protected override void BuildBody(VisualElement body)
        {
            var active = Bounties.Active(C, S);
            if (active != null)
            {
                var b = Ui.Window(null, body);
                Ui.Text($"Bounty: {active.Name} — {S.Bounty.Progress}/{active.Kills}", "small gold", b);
            }
            var w = Ui.Window(null, body);
            w.style.flexGrow = 1;
            var list = Ui.List(w);
            var stages = C.CampaignStages();
            int locked = 0;
            // Expeditions (§22.5): each theme is a run of battles fought in order with the caravan.
            for (int ti = C.Campaign.Themes.Count - 1; ti >= 0; ti--)
            {
                var theme = C.Campaign.Themes[ti];
                int first = stages.FindIndex(x => theme.Stages.Count > 0 && x.Id == theme.Stages[0]);
                if (first < 0) continue;
                if (!Progression.IsStageUnlocked(S, first)) { locked += theme.Stages.Count; continue; }
                bool onIt = S.Expedition != null && S.Expedition.ThemeId == theme.Id;
                bool done = theme.Stages.All(id => S.ClearedStages.Contains(id));
                string status = onIt ? $"Battle {S.Expedition.Next + 1}/{theme.Stages.Count} · {S.Expedition.CarriedOrbs} orbs carried" : done ? "★ Completed" : $"{theme.Stages.Count} battles";
                var head = Ui.MenuRow((onIt ? "▶ " : "") + theme.Name + " Expedition", status, null, list.contentContainer, null, true);
                head.Q<Label>("label").AddToClassList("gold");
                head.name = "expedition-" + theme.Id;
                for (int k = 0; k < theme.Stages.Count; k++)
                {
                    var st = C.TryStage(theme.Stages[k]);
                    if (st == null) continue;
                    int index = stages.FindIndex(x => x.Id == st.Id);
                    bool cleared = S.ClearedStages.Contains(st.Id);
                    bool can = Expeditions.CanLaunch(C, S, st.Id, out var why) && Progression.IsStageUnlocked(S, index);
                    string label = (cleared ? "★ " : can ? "☆ " : "◇ ") + st.Name;
                    // Same level BattleFactory.Build actually fights it at: the postgame bonus stages carry a
                    // LevelOverride, so the index-based curve alone advertised the wrong level for them (GG-T70).
                    int recommended = st.LevelOverride ?? Progression.RecommendedLevel(index);
                    var row = Ui.MenuRow(label, can ? $"Lv {recommended} · {st.Waves.Count} waves" : (why ?? "Locked"), () => Launch(st, index), list.contentContainer, null, can);
                    row.style.marginLeft = 40;
                    if (CaravanUpgrades.Level(S, "scouts") > 0)
                    {
                        var foes = st.Waves.SelectMany(wv => wv.Enemies).Select(e => C.TryClass(e.Class)?.Name).Where(n => n != null).Distinct().Take(5);
                        var sc = Ui.Text("scouts: " + string.Join(", ", foes), "tiny warn", list.contentContainer);
                        sc.style.marginLeft = 110;
                    }
                    var drops = DropsHint(st);
                    if (can && drops.Length > 0)
                    {
                        var d = Ui.Text($"drops: {drops}", "tiny dim", list.contentContainer);
                        d.style.marginLeft = 110; d.style.marginBottom = 8;
                    }
                }
                if (onIt) Ui.MenuRow("Abandon expedition", "", () => Confirm.Show("Abandon the expedition? Progress on it is lost; wounds remain.", "Abandon", () => { App.Toast(Expeditions.Abandon(S).Message); App.Persist(); Refresh(); }), list.contentContainer).style.marginLeft = 40;
            }
            if (locked > 0) Ui.MenuRow($"◇ {locked} more stage{(locked == 1 ? "" : "s")} ahead…", "", null, list.contentContainer, null, false);
            Ui.SelectFirst(list.contentContainer);
        }

        private string DropsHint(StageDef st)
        {
            var names = st.Waves.SelectMany(w => w.Enemies).Select(e => C.TryClass(e.Class)?.DropTable).Where(t => t != null && C.DropTables.ContainsKey(t))
                .SelectMany(t => C.DropTables[t].Entries).Select(e => C.TryItem(e.Item)?.Name).Where(n => n != null).Distinct().Take(3);
            return string.Join(", ", names);
        }

        private void Launch(StageDef st, int index) => StageLauncher.Launch(st.Id, false);
    }

    /// <summary>The one way into a battle: plays the theme's story crawl on first entry (§27), then any pending story-beat
    /// dialogue (§27.1), then the stage.</summary>
    public static class StageLauncher
    {
        public static void Launch(string stageId, bool replaceCurrent)
        {
            var app = GameApp.I;
            if (app.Save != null)
            {
                if (!Expeditions.CanLaunch(app.Content, app.Save, stageId, out var why)) { app.Toast(why); return; }
                Expeditions.Begin(app.Content, app.Save, stageId);
                app.Persist();
            }
            var theme = app.Content.ThemeOf(stageId);
            // Story beats between battles (§27.1, GG-T117): play after the crawl, before the battle, once per save —
            // every one pending, one after another (GG-T119: a recruit and a Camp NPC can share a stage), each
            // picked when the last ends so later beats see what earlier ones did; the same set the bots play headless.
            GameScreen BeatsThenBattle()
            {
                var beat = DialogueRunner.PendingBeat(app.Content, app.Save, stageId);
                return beat == null ? new BattleScreen(stageId) : new DialogueScreen(beat, BeatsThenBattle, theme?.Id);
            }
            GameScreen next = BeatsThenBattle();
            if (theme != null && !string.IsNullOrEmpty(theme.Crawl) && app.Save != null && !app.Save.SeenCrawls.Contains(theme.Id))
            {
                app.Save.SeenCrawls.Add(theme.Id);
                app.Persist();
                next = new StoryCrawlScreen(theme, next);
            }
            if (replaceCurrent) app.Router.Replace(next); else app.Router.Push(next);
        }
    }

    /// <summary>The one way into a Crucible run (§22.10): generates the save's next gauntlet and opens its battle.</summary>
    public static class CrucibleLauncher
    {
        public static void Launch(bool replaceCurrent)
        {
            var app = GameApp.I;
            if (!Crucible.CanLaunch(app.Content, app.Save, out var why)) { app.Toast(why); app.Audio.Sfx("ui.error"); return; }
            var stage = Crucible.Prepare(app.Content, app.Save);
            app.Audio.Sfx("crucible_enter");
            GameScreen next = new BattleScreen(stage.Id);
            if (replaceCurrent) app.Router.Replace(next); else app.Router.Push(next);
        }
    }

    public sealed class StoryCrawlScreen : GameScreen
    {
        private readonly ThemeDef theme;
        private readonly GameScreen next;
        private VisualElement text;
        private float y;
        private bool done;
        public StoryCrawlScreen(ThemeDef theme, GameScreen next) { this.theme = theme; this.next = next; }
        public override string Id => "crawl";
        public override string Music => "TellerOfTheTales";

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(theme.Id, 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.35f, 0.35f, 0.45f);
            var clip = Ui.El("fill", root);
            clip.style.overflow = Overflow.Hidden;
            text = Ui.El("col", clip);
            text.style.position = Position.Absolute;
            text.style.left = 90; text.style.right = 90;
            var h = Ui.Text(theme.Name, "h1 shadow gold", text);
            Fonts.UseDisplay(h);
            h.style.unityTextAlign = TextAnchor.MiddleCenter;
            h.style.marginBottom = 60;
            var body = Ui.Text(theme.Crawl, "h2 shadow", text);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.UpperCenter;
            y = GameApp.StageHeight;
            text.style.top = y;
            var skip = Ui.Button("Skip ›", Finish, "btn--small", root);
            skip.style.position = Position.Absolute; skip.style.right = 30; skip.style.bottom = 40;
            root.RegisterCallback<ClickEvent>(_ => Finish());
            if (DemoMode.Active || TestMode.Active) root.schedule.Execute(Finish).ExecuteLater(600);
        }

        public override void Tick(float dt)
        {
            if (done || text == null) return;
            y -= dt * 95f;
            text.style.top = y;
            if (y < -text.layout.height - 100) Finish();
        }

        private void Finish()
        {
            if (done) return;
            done = true;
            App.Router.Replace(next);
        }
    }

    public sealed class CreditsScreen : GameScreen
    {
        public override string Id => "credits";
        protected override void Build(VisualElement root)
        {
            var col = Ui.El("fill col", root);
            Ui.Header("Credits", () => App.Router.Back(), col, out _);
            var w = Ui.Window(null, col);
            w.style.flexGrow = 1;
            var list = Ui.List(w);
            var text = Resources.Load<TextAsset>("Credits")?.text ?? "GridGame Gaiden — MindAttic";
            var l = Ui.Text(text, "small", list.contentContainer);
            l.style.whiteSpace = WhiteSpace.Normal;
        }
    }
}
