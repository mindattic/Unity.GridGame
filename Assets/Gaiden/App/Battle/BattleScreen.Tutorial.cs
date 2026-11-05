using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// The guided first battle (US-331, expanded 2026-10-11 — owner: "there needs to be a full tutorial with
    /// explanations and examples, the first battle needs 5 waves of increasing difficulty"). Ten lessons, each an
    /// explanation plus a concrete example; a lesson completes when the player does the thing (or taps Next), and
    /// lessons that need particular foes wait for their wave. Steps that teach moving light up a demo move.
    /// </summary>
    public sealed partial class BattleScreen
    {
        private sealed class Lesson
        {
            public string Title, Text, Example;
            /// <summary>The wave (0-based) the lesson needs on the board.</summary>
            public int Wave;
            /// <summary>Show the bot's move as a glowing demo while idle.</summary>
            public bool DemoMove;
            /// <summary>Done when this returns true for a committed command's events.</summary>
            public Func<BattleScreen, BattleCommand, List<BattleEvent>, bool> Done;
            /// <summary>Highlight on show: (hero class, bar slot) to pulse.</summary>
            public string FocusHero; public int FocusSlot = -1;
            /// <summary>Actions after which the lesson moves on by itself.</summary>
            public int Patience = 5;
            /// <summary>Done the moment the player holds something to read it (GG-T252).</summary>
            public bool DoneOnInspect;
        }

        private static readonly Lesson[] Lessons =
        {
            new Lesson { Title = "Moving", DemoMove = true,
                Text = "Drag a hero across the board, tile by tile. Anyone in the way swaps into the tile you just left — moving never hurts anyone.",
                Example = "Example: follow the glowing path to the glowing tile.",
                Done = (b, c, e) => c is MoveCommand },
            new Lesson { Title = "Pincers", DemoMove = true,
                Text = "Trap an enemy in a straight line between two heroes to PINCER it — both heroes strike. Heroes linked to an attacker by an unbroken chain of allies join in as supporters, each with its class's support action.",
                Example = "Example: Hero · Slime · Hero in a row or column.",
                Done = (b, c, e) => e.Exists(x => x is PincerFormedEvent p && p.Team == Team.Hero) },
            // GG-T252: recognition over recall — anything on screen explains itself when held.
            new Lesson { Title = "Hold to read", Patience = 3, DoneOnInspect = true,
                Text = "Hold anything for a moment to read it: a hero, an enemy, a spell slot, a status pip or an icon on the timeline. Let go to close the card — holding never acts.",
                Example = "Example: press and hold a slime until its card opens.",
                Done = (b, c, e) => false },
            // GG-T190: shoving is a tactic, not an accident — the held hero moves other heroes and enemies into place.
            new Lesson { Title = "Shoving", Patience = 4,
                Text = "Your held hero is a lever. Drag it through an enemy and the enemy slides into the tile you just left — so shove a foe into line between two heroes and the drop pincers it. While you drag, arrows show where everyone will slide and gold outlines show the pincers the drop completes.",
                Example = "Example: a slime sits above the gap beside your hero. Step into the gap, then up through the slime: it drops into the gap, between your heroes.",
                Done = (b, c, e) => ShovedIntoPincer(e) },
            new Lesson { Title = "The hold timer", Patience = 3,
                Text = "Picking a hero up starts the clock: the bar under it drains until the next enemy reaches ACT, then the hero drops where it is. Changed your mind? Drop it back on its own tile, or off the board, to cancel — no time spent.",
                Example = "Example: lift a hero, then drag it off the board and let go.",
                Done = (b, c, e) => c is MoveCommand },
            new Lesson { Title = "Mana",
                Text = "Pincers fill the mana bubble at the end of your orb row; at 100 it becomes an orb. Lean pincers earn the most: every extra hero divides the mana, every chain link multiplies it.",
                Example = "Example: two heroes taking half a foe's HP earn 60 of the 100.",
                Done = (b, c, e) => e.Exists(x => x is OrbMintedEvent o && o.Reason == "pincer"), Patience = 3 },
            new Lesson { Title = "Chains", Wave = 1, DemoMove = true,
                Text = "One drag can set off several pincers in a row — a CHAIN. Every link strikes, and every link earns more mana than the last.",
                Example = "Example: line both slimes up between your heroes, then drag the last hero in.",
                Done = (b, c, e) => e.Exists(x => x is PincerFormedEvent p && p.Team == Team.Hero && p.ChainIndex >= 1) },
            new Lesson { Title = "Spells", Wave = 2, FocusHero = "cleric", FocusSlot = 0,
                Text = "Tap a hero, a lit spell, then a target, then Confirm. A spell charges first — its bar fills as time moves — then it flies and lands.",
                Example = "Example: Cleric → Heal → a hurt ally → Cast.",
                Done = (b, c, e) => e.Exists(x => x is CastQueuedEvent q && !q.IsCharge) },
            new Lesson { Title = "Buffs & debuffs", Wave = 2, Patience = 3,
                Text = "Coloured pips on a token are its buffs (blue) and debuffs (purple): poison, haste, shields, slows. Hold a unit, or a pip in the panel, to read exactly what each does and how long it lasts.",
                Example = "Example: hold a hero after the Cleric's spell lands.",
                Done = (b, c, e) => e.Exists(x => x is BuffAppliedEvent) },
            new Lesson { Title = "The timeline", Wave = 2, Patience = 3,
                Text = "Time moves only when you act. Enemies act when their icon reaches ACT; the gold ring hits next. Hit a foe inside the orange zone to push it back. GUARD skips ahead with everyone shielded.",
                Example = "Example: pincer the gold-ringed foe while it's in the zone.",
                Done = (b, c, e) => e.Exists(x => x is PushedBackEvent) },
            new Lesson { Title = "Roles & Taunt", Wave = 3, FocusHero = "paladin", FocusSlot = 0,
                Text = "Each hero has a role. Your Paladin is a Tank: Taunt makes every enemy in reach come for it, so your casters can finish their spells. The Barbarian strikes, the Cleric heals.",
                Example = "Example: Paladin → Taunt → Go.",
                Done = (b, c, e) => e.Exists(x => x is BuffAppliedEvent a && a.BuffId == "taunting") },
            new Lesson { Title = "Pickups", Wave = 3,
                Text = "Fallen enemies sometimes drop a pickup: a tonic, a mana shard, quicksilver or a might rune. Walk a hero over it before its ring runs out.",
                Example = "Example: drag any hero onto the glowing token.",
                Done = (b, c, e) => e.Exists(x => x is PickupCollectedEvent) },
            new Lesson { Title = "Interrupting", Wave = 4,
                Text = "A charging enemy wears a ring and marks its target. Hit it with a pincer to stagger the cast — enough blows cancel it and earn mana.",
                Example = "Example: pincer the wolf while it howls.",
                Done = (b, c, e) => e.Exists(x => x is CastStaggeredEvent || (x is CastCancelledEvent cc && cc.Reason == "interrupted")) },
            new Lesson { Title = "Armor plates", Wave = 4, Patience = 2,
                Text = "Some foes wear an armor plate on one side, drawn as a thick edge. Blows into the plate chip it instead of the foe: strike from the sides, break it head-on, or use spells, which ignore it.",
                Example = "Example: hold an armored foe later on to see its plate and which way it faces.",
                Done = (b, c, e) => e.Exists(x => x is PlateBrokenEvent) },
            new Lesson { Title = "Ready", Wave = 4, Patience = 99,
                Text = "That's the whole toolkit: move, pincer, chain for mana, cast with care, let your tank take the hits. Clear the last wave!",
                Example = "Tip: Settings → How to play has all of this.",
                Done = (b, c, e) => false },
        };

        /// <summary>GG-T190: did this command's drag shove an enemy into a line that its drop then pincered?</summary>
        private static bool ShovedIntoPincer(List<BattleEvent> events)
        {
            var shoved = new HashSet<int>();
            foreach (var x in events)
            {
                if (x is DisplacedEvent d) shoved.Add(d.UnitId);
                else if (x is PincerFormedEvent p && p.Team == Team.Hero && p.Opponents.Exists(shoved.Contains)) return true;
                else if (x is TimelineAdvancedEvent) return false; // the move is over; later slides are enemy turns
            }
            return false;
        }

        public int TutorialStep { get; private set; } = -1;
        private int tutorialActions;
        private bool TutorialActive => TutorialStep >= 0;
        private Lesson CurrentLesson => TutorialActive ? Lessons[TutorialStep] : null;
        private bool LessonWaiting => CurrentLesson != null && State.Wave < CurrentLesson.Wave;
        private bool TutorialDemoMove => CurrentLesson != null && CurrentLesson.DemoMove && !LessonWaiting;

        private void FinishTutorial()
        {
            TutorialStep = -1;
            if (App.Save != null) { App.Save.TutorialDone = true; App.Persist(); }
            SyncPanel();
        }

        private void AdvanceTutorial(int to)
        {
            if (!TutorialActive || to <= TutorialStep) return;
            if (to >= Lessons.Length) { FinishTutorial(); return; }
            TutorialStep = to;
            tutorialActions = 0;
            hintShown = false; idle = 0f;
            App.Audio.Sfx("ui.select", 0.6f);
            panelUnitId = -1;
            SyncPanel();
            FocusLesson();
        }

        private void NextLesson() => AdvanceTutorial(TutorialStep + 1);

        private void TutorialAfterCommit(BattleCommand cmd, List<BattleEvent> events)
        {
            if (!TutorialActive) return;
            var l = CurrentLesson;
            if (LessonWaiting) return;
            tutorialActions++;
            if (l.Done(this, cmd, events) || tutorialActions >= l.Patience) NextLesson();
            // A lesson the player already did earlier (a chain in wave 1 …) is skipped as soon as it comes up.
            while (TutorialActive && !LessonWaiting && CurrentLesson.Done(this, cmd, events) && TutorialStep < Lessons.Length - 1) NextLesson();
        }

        /// <summary>A lesson that names a hero/slot selects the hero and pulses the slot (the "example" made visible).</summary>
        private void FocusLesson()
        {
            var l = CurrentLesson;
            if (l == null || LessonWaiting || l.FocusHero == null) return;
            var hero = State.Units.FirstOrDefault(u => u.IsHero && u.Alive && u.Def.Id == l.FocusHero);
            if (hero == null) return;
            SelectHero(hero.Id);
            if (l.FocusSlot >= 0 && l.FocusSlot < slotViews.Count)
            {
                var slot = slotViews[l.FocusSlot];
                App.StartCoroutine(Tween.Pop(slot, 1.15f, 0.4f));
                App.StartCoroutine(Tween.Pop(board.Units[hero.Id].Root, 1.12f, 0.4f));
            }
        }

        private void TutorialCard()
        {
            var l = CurrentLesson;
            var row = Ui.El("row", panel);
            row.style.alignItems = Align.Center;
            var col = Ui.El("col grow", row);
            col.style.flexShrink = 1;
            var title = Ui.Text($"Tutorial {TutorialStep + 1}/{Lessons.Length} · {l.Title}", "small gold", col);
            title.name = "tutorial-title";
            if (LessonWaiting)
            {
                var w = Ui.Text($"Next lesson arrives in wave {l.Wave + 1}. Keep fighting — clear this wave!", "tiny", col);
                w.style.whiteSpace = WhiteSpace.Normal;
            }
            else
            {
                var t = Ui.Text(l.Text, "tiny", col);
                t.name = "tutorial-text";
                t.style.whiteSpace = WhiteSpace.Normal;
                var ex = Ui.Text(l.Example, "tiny", col);
                ex.name = "tutorial-example";
                ex.style.whiteSpace = WhiteSpace.Normal;
                ex.style.color = new Color(0.62f, 0.9f, 1f);
            }
            var btns = Ui.El("col", row);
            btns.style.flexShrink = 0;
            bool last = TutorialStep == Lessons.Length - 1;
            if (last) Ui.Button("Got it", FinishTutorial, "btn--small btn--primary", btns).name = "tutorial-done";
            else if (!LessonWaiting) Ui.Button("Next", NextLesson, "btn--small btn--primary", btns).name = "tutorial-next";
            Ui.Button("Skip", FinishTutorial, "btn--small", btns).name = "tutorial-skip";
        }

        /// <summary>When a wave starts, a lesson that was waiting for it comes on stage.</summary>
        private void TutorialWaveStarted()
        {
            if (!TutorialActive) return;
            SyncPanel();
            if (!LessonWaiting) { App.Audio.Sfx("ui.select", 0.6f); FocusLesson(); }
        }
    }
}
