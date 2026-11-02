using System;
using System.IO;
using System.Runtime.InteropServices;
using GridGame.Meta;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>Three save slots as JSON files (atomic write via temp + replace) in one folder: the active player
    /// profile's <c>Profiles/&lt;Name&gt;/Saves</c> (GG-T99), or a test folder.</summary>
    public sealed class SaveStore
    {
        public const int Slots = SaveFiles.Slots;
        public readonly string Root;

        /// <summary>
        /// Save files get their own settings rather than reusing <see cref="ContentLoader.Settings"/>: that one's
        /// MissingMemberHandling.Error is right for strict content validation, but applied to a save it would throw
        /// on deserialization the moment any field is renamed/removed — before SaveMigrations.Migrate ever runs,
        /// defeating the migration path SaveData documents. Saves tolerate unknown/stale properties instead
        /// (<see cref="SaveFiles.Settings"/>, shared with the engine-free tests).
        /// </summary>
        private static JsonSerializerSettings SaveSettings => SaveFiles.Settings;

        /// <summary><paramref name="root"/> is required: the old default (persistentDataPath/Saves) is the pre-profile
        /// folder LegacySaveMigration moves away, and recreating it would migrate it again on the next launch.</summary>
        public SaveStore(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("A save folder is needed.", nameof(root));
            Root = root;
            Directory.CreateDirectory(Root);
        }

        private string PathFor(int slot) => SaveFiles.PathFor(Root, slot);

        public bool Exists(int slot) => File.Exists(PathFor(slot));

        /// <summary><paramref name="content"/> drives GG-T52's proficiency grandfather clause during
        /// migration (a save equipped before that gate existed keeps its gear instead of losing it);
        /// omit it only where content isn't loaded yet (e.g. save-shape-only tests).</summary>
        public SaveData Load(int slot, GridGame.Core.Content.ContentDb content = null)
        {
            var p = PathFor(slot);
            if (!File.Exists(p)) return null;
            try
            {
                var s = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(p), SaveSettings);
                return SaveMigrations.Migrate(s, content);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] slot {slot + 1} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes the slot atomically (temp file + replace). Never throws: a disk-full/locked-file/permissions
        /// failure at any step is caught, logged, and reported back as <c>false</c> instead of silently losing
        /// the player's progress (GG-T146) — mirroring how <see cref="Load"/> reports a bad read with a log and
        /// a sentinel return rather than an exception. Best-effort-deletes the temp file on failure so a stray
        /// `.tmp` doesn't linger; a save attempted again later overwrites it either way.
        /// </summary>
        public bool Write(int slot, SaveData s)
        {
            s.UpdatedUtc = DateTime.UtcNow;
            var p = PathFor(slot);
            var tmp = p + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonConvert.SerializeObject(s, Formatting.Indented, SaveSettings));
                try
                {
                    if (File.Exists(p)) File.Replace(tmp, p, null);
                    else File.Move(tmp, p);
                }
                catch (PlatformNotSupportedException)
                {
                    // WebGL's file system has no atomic replace.
                    if (File.Exists(p)) File.Delete(p);
                    File.Move(tmp, p);
                }
                Flush();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] slot {slot + 1} write failed: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (Exception) { /* best effort; a later write overwrites it anyway */ }
                return false;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void GaidenSyncFiles();
        private static void Flush() => GaidenSyncFiles();
#else
        private static void Flush() { }
#endif

        /// <summary>Pushes file writes made outside this store (profile settings.json, profile create/rename/delete) to
        /// durable storage — a no-op everywhere but WebGL.</summary>
        public static void SyncFiles() => Flush();

        public void Delete(int slot)
        {
            var p = PathFor(slot);
            if (File.Exists(p)) File.Delete(p);
            Flush();
        }

        public int MostRecent() => SaveFiles.MostRecent(Root);
    }
}
