using System;
using System.IO;
using GridGame.Meta;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Shared.Core;

namespace GridGame.Tests
{
    /// <summary>GG-T99 (bible §15.4): player profiles on com.mindattic.unity.shared — the legacy-save migration, per-profile
    /// isolation of saves and settings, and switching profiles reloading settings. Every test runs in its own temp folder,
    /// never the real persistentDataPath.</summary>
    public class ProfileTests
    {
        private string data;
        private ProfileStore store;

        [SetUp]
        public void SetUp()
        {
            data = Path.Combine(Path.GetTempPath(), "GaidenProfileTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            store = ProfileStore.ForDataPath(data);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }

        private string Legacy => Path.Combine(data, LegacySaveMigration.LegacyFolderName);

        /// <summary>An old-layout slot file: a save that still carries its own Settings block, last played at <paramref name="updated"/>.</summary>
        private void WriteLegacySlot(int slot, string name, DateTime updated, SettingsSave settings)
        {
            Directory.CreateDirectory(Legacy);
            var json = JObject.Parse(SaveFiles.Serialize(new SaveData { Name = name, Gold = 100 + slot, UpdatedUtc = updated }));
            json["Settings"] = JObject.FromObject(settings);
            File.WriteAllText(SaveFiles.PathFor(Legacy, slot), json.ToString());
        }

        [Test]
        public void Migration_moves_every_legacy_slot_into_the_Default_profile_and_keeps_the_old_folder()
        {
            var now = DateTime.UtcNow;
            WriteLegacySlot(0, "Ada", now.AddHours(-2), new SettingsSave());
            WriteLegacySlot(2, "Cy", now.AddHours(-1), new SettingsSave());

            var r = LegacySaveMigration.Run(data, store);

            Assert.That(r.Ran, Is.True);
            Assert.That(r.Copied, Is.EquivalentTo(new[] { "slot1.json", "slot3.json" }));
            var saves = store.Get(ProfileStore.DefaultName).SavesFolder;
            Assert.That(SaveFiles.Deserialize(File.ReadAllText(SaveFiles.PathFor(saves, 0))).Name, Is.EqualTo("Ada"));
            Assert.That(SaveFiles.Deserialize(File.ReadAllText(SaveFiles.PathFor(saves, 2))).Name, Is.EqualTo("Cy"));
            Assert.That(File.Exists(SaveFiles.PathFor(saves, 1)), Is.False);
            // Nothing is lost: the old folder is kept, renamed, with its files intact.
            Assert.That(Directory.Exists(Legacy), Is.False);
            var kept = Path.Combine(data, LegacySaveMigration.MigratedFolderName);
            Assert.That(r.MovedTo, Is.EqualTo(kept));
            Assert.That(File.Exists(SaveFiles.PathFor(kept, 0)) && File.Exists(SaveFiles.PathFor(kept, 2)), Is.True);
            Assert.That(store.Current.Name, Is.EqualTo(ProfileStore.DefaultName));
        }

        [Test]
        public void Migration_seeds_settings_from_the_most_recently_played_slot()
        {
            var now = DateTime.UtcNow;
            WriteLegacySlot(0, "Old", now.AddDays(-3), new SettingsSave { MusicVolume = 0.2f, BattleSpeed = 1.5f, MenuTheme = "Ember" });
            WriteLegacySlot(1, "Recent", now.AddMinutes(-5), new SettingsSave
            {
                MusicVolume = 0.4f, SfxVolume = 0.25f, MuteSfx = true, ReduceMotion = true,
                BattleSpeed = 3f, DragTimer = true, ColorblindMode = true, PincerCutIns = false, ConfirmActions = false,
                MenuTheme = "Slate", FontName = "Clean",
                Difficulty = new DifficultySettings { Preset = "Custom", EnemyHp = 1.5f },
            });
            // The older slot's file is touched last: the save's own UpdatedUtc decides, not the file time.
            File.SetLastWriteTimeUtc(SaveFiles.PathFor(Legacy, 0), now);

            var r = LegacySaveMigration.Run(data, store);

            Assert.That(r.SeededFromSlot, Is.EqualTo(1));
            var ps = ProfileSettings.Load(store.Get(ProfileStore.DefaultName).SettingsPath);
            Assert.That(ps.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
            // The shared common block holds the cross-game keys...
            Assert.That(ps.Common.MusicVolume, Is.EqualTo(0.4f).Within(1e-4));
            Assert.That(ps.Common.SfxVolume, Is.EqualTo(0.25f).Within(1e-4));
            Assert.That(ps.Common.MuteSfx, Is.True);
            Assert.That(ps.Common.ReduceMotion, Is.True);
            // ...and the Gaiden-only ones live under game.
            Assert.That(ps.Game.GetFloat(GaidenSettings.Keys.BattleSpeed), Is.EqualTo(3f));
            Assert.That(ps.Game.GetString(GaidenSettings.Keys.MenuTheme), Is.EqualTo("Slate"));
            Assert.That(ps.Game.GetBool(GaidenSettings.Keys.ColorblindMode), Is.True);
            Assert.That(ps.Game.GetObject(GaidenSettings.Keys.Difficulty).GetString("preset"), Is.EqualTo("Custom"));
            var back = GaidenSettings.Read(ps);
            Assert.That(back.DragTimer, Is.True);
            Assert.That(back.PincerCutIns, Is.False);
            Assert.That(back.ConfirmActions, Is.False);
            Assert.That(back.FontName, Is.EqualTo("Clean"));
            Assert.That(back.Difficulty.EnemyHp, Is.EqualTo(1.5f));
        }

        [Test]
        public void Migration_runs_once_and_does_nothing_without_a_legacy_folder()
        {
            Assert.That(LegacySaveMigration.Run(data, store).Ran, Is.False);
            WriteLegacySlot(0, "Ada", DateTime.UtcNow, new SettingsSave());
            Assert.That(LegacySaveMigration.Run(data, store).Ran, Is.True);
            Assert.That(LegacySaveMigration.Run(data, store).Ran, Is.False, "the old folder was renamed, so a second launch has nothing to do");
        }

        [Test]
        public void Migration_never_overwrites_a_slot_the_profile_already_has()
        {
            var saves = store.Create(ProfileStore.DefaultName).EnsureSavesFolder();
            File.WriteAllText(SaveFiles.PathFor(saves, 0), SaveFiles.Serialize(new SaveData { Name = "Kept" }));
            WriteLegacySlot(0, "Legacy", DateTime.UtcNow, new SettingsSave());

            var r = LegacySaveMigration.Run(data, store);

            Assert.That(r.Skipped, Is.EquivalentTo(new[] { "slot1.json" }));
            Assert.That(SaveFiles.Deserialize(File.ReadAllText(SaveFiles.PathFor(saves, 0))).Name, Is.EqualTo("Kept"));
            Assert.That(File.Exists(SaveFiles.PathFor(r.MovedTo, 0)), Is.True, "the skipped legacy slot is still in Saves.migrated");
        }

        [Test]
        public void A_legacy_slot_still_loads_after_migration_with_its_old_Settings_block_ignored()
        {
            WriteLegacySlot(0, "Ada", DateTime.UtcNow, new SettingsSave { BattleSpeed = 2f });
            LegacySaveMigration.Run(data, store);
            var s = SaveMigrations.Migrate(SaveFiles.Deserialize(File.ReadAllText(SaveFiles.PathFor(store.Current.SavesFolder, 0))));
            Assert.That(s.Name, Is.EqualTo("Ada"));
            Assert.That(s.Gold, Is.EqualTo(100));
        }

        [Test]
        public void Each_profile_has_its_own_save_slots()
        {
            var a = store.Create("Alice");
            var b = store.Create("Bob");
            File.WriteAllText(SaveFiles.PathFor(a.EnsureSavesFolder(), 0), SaveFiles.Serialize(new SaveData { Name = "Alice's run" }));

            Assert.That(SaveFiles.MostRecent(a.SavesFolder), Is.EqualTo(0));
            Assert.That(SaveFiles.MostRecent(b.EnsureSavesFolder()), Is.EqualTo(-1), "Bob doesn't see Alice's slot");
            Assert.That(a.SavesFolder, Does.EndWith(Path.Combine("Profiles", "Alice", "Saves")));
        }

        [Test]
        public void Each_profile_has_its_own_settings()
        {
            var session = new ProfileSession(store, "Alice");
            session.Settings.MusicVolume = 0.2f;
            session.Settings.MenuTheme = "Ember";
            Assert.That(session.SaveSettings(), Is.True);

            session.Create("Bob");
            session.Switch("Bob");
            Assert.That(session.Settings.MusicVolume, Is.EqualTo(new SettingsSave().MusicVolume), "Bob starts on defaults, not Alice's");
            Assert.That(session.Settings.MenuTheme, Is.EqualTo("Classic"));
            session.Settings.BattleSpeed = 2f;
            session.SaveSettings();

            var alice = GaidenSettings.Read(ProfileSettings.Load(store.Get("Alice").SettingsPath));
            Assert.That(alice.MusicVolume, Is.EqualTo(0.2f).Within(1e-4));
            Assert.That(alice.BattleSpeed, Is.EqualTo(1f), "Bob's change never reaches Alice's settings.json");
        }

        [Test]
        public void Switching_profiles_reloads_settings_and_remembers_the_choice()
        {
            var session = new ProfileSession(store, "Alice");
            session.Settings.ColorblindMode = true;
            session.SaveSettings();
            session.Create("Bob");

            session.Switch("Bob");
            Assert.That(session.Name, Is.EqualTo("Bob"));
            Assert.That(session.Settings.ColorblindMode, Is.False);

            session.Switch("Alice");
            Assert.That(session.Settings.ColorblindMode, Is.True, "Alice's settings come back from her settings.json");

            // The next launch opens the last profile used.
            var relaunch = new ProfileSession(ProfileStore.ForDataPath(data));
            Assert.That(relaunch.Name, Is.EqualTo("Alice"));
            Assert.That(relaunch.Settings.ColorblindMode, Is.True);
        }

        [Test]
        public void Unsaved_setting_changes_are_dropped_on_switch()
        {
            var session = new ProfileSession(store, "Alice");
            session.Create("Bob");
            session.Settings.DragTimer = true; // never saved (e.g. a harness speed tweak)
            session.Switch("Bob");
            session.Switch("Alice");
            Assert.That(session.Settings.DragTimer, Is.False);
        }

        [Test]
        public void Rename_and_delete_keep_the_session_on_a_real_profile()
        {
            var session = new ProfileSession(store, "Alice");
            session.Rename("Alice", "Alicia");
            Assert.That(session.Name, Is.EqualTo("Alicia"));
            Assert.That(Directory.Exists(session.SavesFolder), Is.True);

            session.Delete("Alicia");
            Assert.That(session.Name, Is.EqualTo(ProfileStore.DefaultName), "deleting the last profile leaves a fresh Default");
            Assert.That(Directory.GetDirectories(store.TrashFolder), Has.Length.EqualTo(1), "deleted profiles are moved aside, not erased");
        }

        [Test]
        public void A_session_mute_never_reaches_settings_json()
        {
            var session = new ProfileSession(store, "Alice");
            session.Settings.MusicVolume = 0.8f;
            session.SaveSettings();
            try
            {
                Profiles.SessionMuted = true;
                session.Switch("Alice");
                Assert.That(session.Settings.MusicVolume, Is.EqualTo(0.8f).Within(1e-4), "Gaiden reads the stored volume; the CLI mute is SessionAudio's job");
                session.SaveSettings();
            }
            finally { Profiles.SessionMuted = false; }
            var stored = ProfileSettings.Load(store.Get("Alice").SettingsPath);
            Assert.That(stored.Common.MusicVolume, Is.EqualTo(0.8f).Within(1e-4));
            Assert.That(stored.Common.MuteMusic, Is.False);
        }

        [Test]
        public void A_requested_profile_is_made_if_missing_and_a_bad_name_is_refused()
        {
            var session = new ProfileSession(store, "Harness");
            Assert.That(store.Exists("Harness"), Is.True);
            Assert.That(store.Current.Name, Is.EqualTo("Harness"));
            Assert.Throws<ArgumentException>(() => new ProfileSession(store, "bad/name"));
        }
    }
}
