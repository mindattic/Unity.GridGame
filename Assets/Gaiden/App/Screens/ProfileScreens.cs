using System;
using Unity.Shared.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Player profiles (bible §15.4, GG-T99) — GridGame2026's ProfileSelect / ProfileCreate, built in code.
    // Each profile owns its own settings.json and three save slots (Profiles/<Name>/…), via com.mindattic.unity.shared.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Pick, create, rename or delete a player profile. Reached from the Title's profile row.</summary>
    public sealed class ProfileSelectScreen : GameScreen
    {
        public override string Id => "profiles";

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background("cityruins", 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.55f);
            var col = Ui.El("fill col", root);
            Ui.Header("Profiles", () => App.Router.Back(), col, out _);
            var hint = Ui.Text("Each profile keeps its own settings and its own three save slots.", "small dim", col);
            hint.style.marginLeft = 24; hint.style.marginBottom = 8;
            hint.style.whiteSpace = WhiteSpace.Normal;

            var w = Ui.Window("Choose a profile", col);
            w.style.flexGrow = 1;
            var list = Ui.List(w).contentContainer;
            string current = App.Profile.Name;
            foreach (var name in App.Profile.List())
            {
                string n = name;
                bool active = string.Equals(n, current, StringComparison.OrdinalIgnoreCase);
                var row = Ui.MenuRow(n, active ? "Playing" : "", () => Switch(n), list);
                row.name = "profile-" + n;
                if (active) row.Q<Label>("label").AddToClassList("gold");
                var actions = Ui.El("row", list);
                actions.style.marginLeft = 64; actions.style.marginBottom = 14;
                var rename = Ui.Button("Rename", () => App.Router.Push(new ProfileNameScreen(n)), "btn--small", actions);
                rename.name = "rename-" + n;
                rename.style.marginRight = 16;
                var delete = Ui.Button("Delete", () => AskDelete(n), "btn--small", actions);
                delete.name = "delete-" + n;
            }
            var create = Ui.MenuRow("+ New profile", "", () => App.Router.Push(new ProfileNameScreen(null)), list);
            create.name = "new-profile";
            Ui.SelectFirst(list);
        }

        private void Switch(string name)
        {
            if (!string.Equals(name, App.Profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                App.SwitchProfile(name);
                App.Toast($"Playing as {App.Profile.Name}");
            }
            App.Router.Back();
        }

        private void AskDelete(string name)
        {
            bool last = App.Profile.List().Count <= 1;
            string more = last ? " A fresh Default profile takes its place." : "";
            Confirm.Show($"Delete profile \"{name}\"? Its settings and save slots go with it (moved to the trash folder, not erased).{more}", "Delete", () =>
            {
                try { App.DeleteProfile(name); App.Toast($"Deleted {name}"); }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is System.IO.IOException) { App.Toast("Couldn't delete it"); Debug.LogError("[Profiles] delete: " + e.Message); }
                Refresh();
            });
        }
    }

    /// <summary>Type a profile name: a new profile (<c>renaming == null</c>) or a new name for an existing one.</summary>
    public sealed class ProfileNameScreen : GameScreen
    {
        private readonly string renaming;
        private TextField field;
        private Label problem;
        private Button ok;

        public ProfileNameScreen(string renaming) { this.renaming = renaming; }
        public override string Id => "profile-name";

        protected override void Build(VisualElement root)
        {
            var bg = Ui.El("fill", root);
            bg.style.backgroundImage = new StyleBackground(Art.Background("cityruins", 0));
            bg.style.unityBackgroundImageTintColor = new Color(0.4f, 0.4f, 0.55f);
            var col = Ui.El("fill col", root);
            Ui.Header(renaming == null ? "New Profile" : "Rename Profile", () => App.Router.Back(), col, out _);
            var w = Ui.Window(renaming == null ? "Name your profile" : $"New name for \"{renaming}\"", col);

            field = new TextField { name = "profile-name-field", maxLength = ProfileStore.MaxNameLength, value = renaming ?? "" };
            field.style.fontSize = 48;
            field.style.marginTop = 10; field.style.marginBottom = 10;
            field.style.minHeight = 96;
            var input = field.Q(className: "unity-text-field__input") ?? field.Q(className: "unity-base-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = new Color(0.05f, 0.06f, 0.12f, 0.9f);
                input.style.color = Color.white;
                input.style.paddingLeft = input.style.paddingRight = 18;
                input.style.borderTopWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 3;
                input.style.borderTopColor = input.style.borderBottomColor = input.style.borderLeftColor = input.style.borderRightColor = new Color(1f, 0.82f, 0.36f, 0.8f);
            }
            field.RegisterValueChangedCallback(_ => Validate());
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); });
            w.Add(field);

            problem = Ui.Text("", "small warn", w);
            problem.name = "profile-name-problem";
            problem.style.whiteSpace = WhiteSpace.Normal;

            var row = Ui.El("row", w);
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 16;
            Ui.Button("Cancel", () => App.Router.Back(), null, row);
            ok = Ui.Button(renaming == null ? "Create" : "Rename", Submit, "btn--primary", row);
            ok.name = "profile-name-ok";
            Validate();
            field.schedule.Execute(() => field.Focus()).ExecuteLater(200);
        }

        /// <summary>The reason <paramref name="name"/> can't be used, or null when it can.</summary>
        public static string Problem(string name, string renaming)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Type a name.";
            name = name.Trim();
            if (!ProfileStore.IsValidName(name)) return "That name can't be used (no / \\ : * ? \" < > |, no leading or trailing dot).";
            bool sameProfile = renaming != null && string.Equals(name, renaming, StringComparison.OrdinalIgnoreCase);
            if (!sameProfile && GameApp.I.Profile.Store.Exists(name)) return "There's already a profile with that name.";
            return null;
        }

        private void Validate()
        {
            var why = Problem(field.value, renaming);
            // An empty field just waits for typing; only a real problem is spelled out.
            problem.text = string.IsNullOrWhiteSpace(field.value) ? "" : why ?? "";
            Ui.SetEnabled(ok, why == null);
        }

        private void Submit()
        {
            var name = field.value?.Trim();
            var why = Problem(name, renaming);
            if (why != null) { App.Audio.Sfx("ui.error"); problem.text = why; return; }
            try
            {
                if (renaming == null)
                {
                    App.CreateProfile(name);
                    App.SwitchProfile(name); // a new profile is made to be played (GridGame2026's ProfileCreate did the same)
                    App.Toast($"Playing as {name}");
                    App.Router.ResetTo(new TitleScreen());
                    return;
                }
                if (name != renaming) App.RenameProfile(renaming, name);
                App.Router.Back();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is System.IO.IOException)
            {
                App.Audio.Sfx("ui.error");
                problem.text = "Couldn't do that: " + e.Message;
            }
        }
    }
}
