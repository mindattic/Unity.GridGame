using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>Results: victory/defeat/retreat, gold, loot, unlocks (bible §22). Level-ups are shown
    /// first, by LevelUpScreen — route through <see cref="RouteFrom"/> rather than constructing this directly.</summary>
    public sealed class PostBattleScreen : GameScreen
    {
        private readonly BattleReport report;
        private readonly string stageId;
        private readonly List<Coroutine> reveals = new List<Coroutine>();
        public PostBattleScreen(BattleReport report, string stageId) { this.report = report; this.stageId = stageId; }
        public override string Id => "postbattle";
        public override string Music => report.Victory || report.Crucible ? "HeroicAge" : "Heartbreaking";

        public override void OnHidden()
        {
            foreach (var co in reveals) if (co != null) App.StopCoroutine(co);
            reveals.Clear();
        }

        /// <summary>The fought stage's theme and name, read from the report (GG-T127): a generated stage may already be
        /// pruned from the ContentDb while its results still show; fall back to a live lookup for older reports.</summary>
        public static string ThemeOf(ContentDb c, BattleReport report, string stageId) => report.StageTheme ?? c.TryStage(stageId)?.Theme;
        public static string NameOf(ContentDb c, BattleReport report, string stageId) => report.StageName ?? c.TryStage(stageId)?.Name ?? "";

        /// <summary>Battle-end routing: a level-up fanfare first if anyone leveled, else straight to loot.</summary>
        public static void RouteFrom(BattleReport report, string stageId) =>
            App.Router.Replace(report.LevelUps.Count > 0 ? (GameScreen)new LevelUpScreen(report, stageId) : new PostBattleScreen(report, stageId));

        protected override void Build(VisualElement root)
        {
            var c = App.Content;
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background(ThemeOf(c, report, stageId), 3));
            bg.style.unityBackgroundImageTintColor = new Color(0.45f, 0.45f, 0.55f);
            var col = Ui.El("fill col", root);
            col.style.paddingTop = Fit.H(5.5f); col.style.paddingLeft = col.style.paddingRight = Space.S32; col.style.paddingBottom = Space.S48;

            if (report.Crucible) { BuildCrucible(root, col, c); return; }
            var title = Ui.Text(report.Victory ? "VICTORY" : report.Retreated ? "RETREATED" : report.Escaped ? "ESCAPED" : "DEFEAT", "title shadow", col);
            Fonts.UseDisplay(title);
            title.style.color = report.Victory ? Palette.Gold : new Color(1f, 0.45f, 0.45f);
            Ui.Text(NameOf(c, report, stageId), "h2 shadow dim", col).style.unityTextAlign = TextAnchor.MiddleCenter;

            var w = Ui.Window(null, col);
            w.style.marginTop = Space.S48;
            w.style.flexGrow = 1;
            var list = Ui.List(w).contentContainer;
            if (report.Victory)
            {
                Ui.MenuRow("Gold", "+" + Ui.Gold(report.Gold), null, list).Q<Label>("value").AddToClassList("gold");
                Ui.MenuRow("Experience", $"+{report.XpEach} each", null, list);
                // Higher difficulty, greater rewards (GG-T309): name the dial the gold, XP and drops above were scaled by.
                if (report.Difficulty > GridGame.Core.Battle.DifficultyTable.Neutral)
                    Ui.MenuRow($"Difficulty {report.Difficulty}", "rewards " + GridGame.Core.Battle.DifficultyTable.RewardLabel(report.Difficulty), null, list).name = "results-difficulty";
                if (report.Items.Count == 0) Ui.Text("No items dropped.", "small dim", list);
                RevealItems(report.Items, c, list);
                if (report.Milestone != null) // GG-T301: a theme's first full clear
                    Ui.MenuRow($"{report.Milestone} cleared!", "Milestone: " + (c.TryItem(report.MilestoneItem)?.Name ?? report.MilestoneItem), null, list).name = "results-milestone";
                if (report.UnlockedStage != null) Ui.MenuRow("New stage unlocked", report.UnlockedStage, null, list).Q<Label>("value").AddToClassList("gold");
                if (report.NewSlots > 0) Ui.MenuRow("Ability slot unlocked!", $"+{report.NewSlots}", null, list).Q<Label>("label").AddToClassList("good");
                if (report.BountyProgress != null) Ui.MenuRow("Bounty", report.BountyProgress, null, list);
                foreach (var note in report.QuestProgress) Ui.MenuRow("Quest", note, null, list);
                if (report.AutoSold > 0) Ui.MenuRow("Bag full — sold extras", $"{report.AutoSold} items", null, list);
                foreach (var g in report.GuestsJoined)
                    Ui.MenuRow(report.Rescued && g == report.Captive ? $"{c.Class(g).Name} is freed and joins the caravan!" : $"{c.Class(g).Name} joins the caravan!", "New hero", null, list).Q<Label>("label").AddToClassList("good");
                foreach (var offer in report.TameOffers) TameOfferBlock(offer, c, list); // GG-T303
                if (report.CampaignComplete) Ui.MenuRow("Campaign complete!", "New Game+ unlocked at camp", null, list).Q<Label>("label").AddToClassList("gold");
                if (report.ExpeditionComplete)
                {
                    Ui.MenuRow($"{report.ExpeditionName} expedition complete!", "+" + Ui.Gold(report.ExpeditionBonus), null, list).Q<Label>("label").AddToClassList("gold");
                    Ui.Text("The caravan makes camp — everyone rests.", "small good", list);
                }
                else if (report.ExpeditionName != null)
                {
                    var er = Ui.MenuRow($"Expedition: battle {report.ExpeditionStep} of {report.ExpeditionLength} won", $"{report.CarriedOrbs} orbs carry over", null, list);
                    er.name = "expedition-progress";
                    Ui.Text("Wounds carry to the next battle; the inn stays closed until the expedition ends.", "tiny dim", list).style.whiteSpace = WhiteSpace.Normal;
                }
            }
            else
            {
                var t = Ui.Text(report.Retreated ? "You fall back with part of your spoils — safer than risking the whole party. The stage is yours to retry." :
                    report.Escaped ? "The party slips away in the smoke. Wounds remain; no rewards." :
                    report.ExpeditionFailed ? $"The {report.ExpeditionName} expedition has failed. The party falls back to the caravan and recovers — the expedition restarts from battle 1." :
                    "The party retreats to camp and recovers. The gold and loot are lost — but every hero keeps the experience they earned. The stage is yours to retry.", "h2", list);
                t.style.whiteSpace = WhiteSpace.Normal;
                if (report.Retreated)
                {
                    Ui.MenuRow("Gold", "+" + Ui.Gold(report.Gold), null, list).Q<Label>("value").AddToClassList("gold");
                    RevealItems(report.Items, c, list);
                }
                if (!report.Escaped && report.XpEach > 0)
                    Ui.MenuRow("Hard-won experience", $"+{report.XpEach} each", null, list).name = "defeat-xp";
            }

            // A guest down at the end isn't lost (GG-T114) — they'll be met on this stage again.
            foreach (var g in report.GuestsFell)
                Ui.MenuRow($"{c.Class(g).Name} fell in battle", g == report.Captive ? "May be found again" : "Will return", null, list).Q<Label>("label").AddToClassList("dim");
            foreach (var g in report.GuestsLost) // permadeath only (a future Ironman mode) — never in the normal game
                Ui.MenuRow($"{c.Class(g).Name} fell in battle", "Gone for good", null, list).Q<Label>("label").AddToClassList("bad");
            var row = Ui.El("row", col);
            row.style.justifyContent = Justify.Center;
            if (!report.Victory)
            {
                // A failed expedition restarts from its first battle.
                var theme = c.ThemeOf(stageId);
                string retryId = report.ExpeditionFailed && theme != null ? theme.Stages[0] : stageId;
                if (c.TryStage(retryId) != null) Ui.Button("Retry", () => StageLauncher.Launch(retryId, true), null, row); // a pruned generated stage can't be retried
            }
            // A freed captive (GG-T236) says their piece on the way back to camp, not on the way into the ending.
            var rescue = report.Rescued && !TestMode.Active && !Story.EndingDue(c, App.Save, report.Victory, stageId) ? Rescues.Scene(c, report.Captive) : null;
            var cont = Ui.Button("Continue", () =>
            {
                if (rescue != null) App.Router.ResetTo(DialogueScreen.WithThen(rescue, report.Captive, StoryBeats.Camp));
                else StoryBeats.AfterResults(report.Victory, stageId);
            }, "btn--primary", row);
            cont.name = "continue";
            bool ending = Story.EndingDue(c, App.Save, report.Victory, stageId);
            if (ending) Ui.Text("The road ahead is quiet, for now. Continue to see how the story closes.", "small gold", list).style.whiteSpace = WhiteSpace.Normal;
            if (!ending && report.Victory && report.NextStageId != null)
            {
                Ui.Button("Retreat early", () => Confirm.Show(
                    "Stop the expedition here? You keep everything you've earned so far — you just give up the completion bonus for pushing on.",
                    "Retreat early", () => { Expeditions.Abandon(App.Save); App.Persist(); App.Router.ResetTo(new HubScreen()); }), null, row);
                Ui.Button("Next battle ›", () => StageLauncher.Launch(report.NextStageId, true), "btn--primary", row).name = "next";
            }
            else if (!ending && report.Victory)
            {
                var stages = c.CampaignStages();
                int idx = stages.FindIndex(s => s.Id == stageId);
                if (idx >= 0 && idx + 1 < stages.Count && Progression.IsStageUnlocked(App.Save, idx + 1) && Expeditions.CanLaunch(c, App.Save, stages[idx + 1].Id, out _))
                    Ui.Button("Next expedition ›", () => StageLauncher.Launch(stages[idx + 1].Id, true), null, row).name = "next";
            }

            if (TestMode.Active) root.schedule.Execute(() => TestMode.AfterBattle(report, stageId)).ExecuteLater(1500);
            else if (DemoMode.Active) root.schedule.Execute(() => DemoMode.AfterBattle(report, stageId)).ExecuteLater(1500);
        }

        /// <summary>Crucible results (§22.10): waves survived, the record, and the whole haul, which a Crucible run
        /// keeps however it ended. Again › starts the save's next gauntlet; Continue goes back to camp.</summary>
        private void BuildCrucible(VisualElement root, VisualElement col, ContentDb c)
        {
            var title = Ui.Text("CRUCIBLE", "title shadow", col);
            Fonts.UseDisplay(title);
            title.style.color = new Color(1f, 0.6f, 0.3f);
            var waves = Ui.Text($"Survived {report.CrucibleWaves} wave{(report.CrucibleWaves == 1 ? "" : "s")}", "h1 shadow gold", col);
            waves.name = "crucible-waves";
            waves.style.unityTextAlign = TextAnchor.MiddleCenter;
            var best = Ui.Text(report.CrucibleNewBest ? "New best!" : $"Best: {report.CrucibleBest} waves", report.CrucibleNewBest ? "h2 shadow good" : "h2 shadow dim", col);
            best.name = "crucible-best";
            best.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (report.CrucibleNewBest)
            {
                // The record is the run's headline: a fanfare and a pop, after the results have settled in.
                best.style.opacity = 0f;
                reveals.Add(App.StartCoroutine(NewBest(best)));
            }

            var w = Ui.Window(null, col);
            w.style.marginTop = Space.S32;
            w.style.flexGrow = 1;
            var list = Ui.List(w).contentContainer;
            var t = Ui.Text(report.Victory ? "Every wave falls. The Crucible is silent, and the whole haul is yours." :
                report.Retreated || report.Escaped ? "The party steps out of the Crucible with everything it earned, and recovers." :
                "The party falls in the Crucible, but every coin, drop and point of experience earned is kept. Everyone recovers.", "small", list);
            t.style.whiteSpace = WhiteSpace.Normal;
            Ui.MenuRow("Gold", "+" + Ui.Gold(report.Gold), null, list).Q<Label>("value").AddToClassList("gold");
            Ui.MenuRow("Experience", $"+{report.XpEach} each", null, list).name = "crucible-xp";
            if (report.Items.Count == 0) Ui.Text("No items dropped.", "small dim", list);
            RevealItems(report.Items, c, list);
            if (report.BountyProgress != null) Ui.MenuRow("Bounty", report.BountyProgress, null, list);
            if (report.AutoSold > 0) Ui.MenuRow("Bag full: sold extras", $"{report.AutoSold} items", null, list);

            var row = Ui.El("row", col);
            row.style.justifyContent = Justify.Center;
            var cont = Ui.Button("Continue", () => App.Router.ResetTo(new HubScreen()), "btn--primary", row);
            cont.name = "continue";
            var again = Ui.Button("Again ›", () => CrucibleLauncher.Launch(true), null, row);
            again.name = "crucible-again";
            Ui.SetEnabled(again, Crucible.CanLaunch(c, App.Save, out _));
            if (DemoMode.Active) root.schedule.Execute(() => App.Router.ResetTo(new HubScreen())).ExecuteLater(1500);
        }

        private IEnumerator NewBest(VisualElement label)
        {
            yield return Tween.Wait(Timing.Emphasis);
            App.Audio.Sfx("battle.crucible_best");
            label.style.opacity = 1f;
            yield return Tween.Pop(label, 1.25f, 0.3f);
        }

        /// <summary>Items pop in one at a time instead of dumping the whole list at once (owner direction
        /// 2026-10-11: the same tempo and pause that makes a results screen enjoyable to read).</summary>
        private void RevealItems(Dictionary<string, int> items, ContentDb c, VisualElement list)
        {
            int i = 0;
            foreach (var kv in items)
            {
                var it = c.TryItem(kv.Key);
                if (it == null) continue;
                var row = ItemUi.Row(it, $"×{kv.Value}", null, list);
                row.style.opacity = 0f;
                reveals.Add(App.StartCoroutine(RevealRow(row, i * 0.08f)));
                // GG-T301: a looted Tome offers "Teach to…" right here; a Chest can be opened on the spot.
                VisualElement extra = null;
                if (it.Type == ItemType.Tome) extra = TomeUi.Offer(list, it);
                else if (it.Type == ItemType.Chest) extra = ChestBlock(it, c, list);
                if (extra != null)
                {
                    extra.style.opacity = 0f;
                    reveals.Add(App.StartCoroutine(RevealRow(extra, i * 0.08f + 0.04f)));
                }
                i++;
            }
        }

        /// <summary>A looted chest's Open button; what comes out is listed beneath it (Tomes with their own Teach row).</summary>
        private VisualElement ChestBlock(ItemDef chest, ContentDb c, VisualElement list)
        {
            var box = Ui.El("col", list);
            box.style.marginLeft = Space.S16; box.style.marginBottom = Space.S8;
            var row = Ui.El("row", box);
            TomeUi.OpenButton(row, chest, (r, haul) =>
            {
                if (!r.Ok || haul == null) return;
                box.Clear();
                var inner = Ui.El("col", box);
                if (haul.Gold > 0) Ui.MenuRow("Gold", "+" + Ui.Gold(haul.Gold), null, inner).Q<Label>("value").AddToClassList("gold");
                RevealItems(haul.Items, c, inner);
                if (App.Save.Count(chest.Id) > 0) ChestBlock(chest, c, box);
            });
            return box;
        }

        /// <summary>A fully bonded tamed monster asks to join (GG-T303): Accept takes it into the party (or the reserve when
        /// the party is full) as a domesticated monster; Decline lets it go back to the wild.</summary>
        private void TameOfferBlock(Domestication.Offer offer, ContentDb c, VisualElement list)
        {
            var def = c.TryClass(offer.ClassId);
            if (def == null) return;
            var box = Ui.El("row", list);
            box.name = "tame-offer-" + offer.ClassId;
            box.style.alignItems = Align.Center;
            box.style.marginTop = Space.S8; box.style.marginBottom = Space.S8;
            Ui.Thumb(def.Id, box, 96).style.marginRight = Space.S16;
            var text = Ui.El("col", box);
            text.style.flexGrow = 1; text.style.flexShrink = 1;
            var head = Ui.Text($"{def.Name} wants to join you!", "h2 good", text);
            head.style.whiteSpace = WhiteSpace.Normal;
            var sub = Ui.Text($"Lv {offer.Level} - fights on its own with the Pincer keyword. No gear or skills; grows slowly (max Lv {Domestication.LevelCap}).", "tiny dim", text);
            sub.style.whiteSpace = WhiteSpace.Normal;
            var buttons = Ui.El("row", text);
            buttons.style.marginTop = Space.S8;
            void Settle(string msg, bool good)
            {
                buttons.Clear();
                Ui.Text(msg, good ? "small good" : "small dim", buttons).style.whiteSpace = WhiteSpace.Normal;
            }
            Ui.Button("Accept", () =>
            {
                var r = Domestication.Accept(c, App.Save, offer);
                App.Audio.Sfx(r.Ok ? "battle.victory" : "ui.error", r.Ok ? 0.6f : 1f);
                if (r.Ok) App.Persist();
                Settle(r.Message, r.Ok);
            }, "btn--primary", buttons).name = "tame-accept";
            Ui.Button("Decline", () =>
            {
                App.Audio.Sfx("ui.cancel");
                Settle($"{def.Name} slips back into the wild.", false);
            }, null, buttons).name = "tame-decline";
            box.style.opacity = 0f;
            reveals.Add(App.StartCoroutine(RevealRow(box, 0.2f)));
        }

        private IEnumerator RevealRow(VisualElement row, float delay)
        {
            yield return Tween.Wait(delay);
            row.style.opacity = 1f;
            yield return Tween.Pop(row, 1.12f, 0.18f);
        }
    }
}
