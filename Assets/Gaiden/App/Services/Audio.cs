using System.Collections.Generic;
using UnityEngine;

namespace GridGame.App
{
    /// <summary>Music + SFX by key. Every battle event maps to a cue (bible §12.0: every event gets a sound).</summary>
    public sealed class Audio : MonoBehaviour
    {
        private AudioSource music;
        private readonly List<AudioSource> sfx = new List<AudioSource>();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private string currentTrack;
        public string CurrentTrack => currentTrack;
        /// <summary>Recent cues (for automation checks that every event is voiced).</summary>
        public readonly List<string> CueLog = new List<string>();
        private float musicVolume = 0.6f, sfxVolume = 0.85f;
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        public static Audio Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<Audio>();
            a.music = go.AddComponent<AudioSource>();
            a.music.loop = true;
            a.music.playOnAwake = false;
            for (int i = 0; i < 10; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                a.sfx.Add(s);
            }
            return a;
        }

        public void Apply(Meta.SettingsSave s)
        {
            musicVolume = s.MuteMusic ? 0f : s.MusicVolume;
            sfxVolume = s.MuteSfx ? 0f : s.SfxVolume;
            music.volume = musicVolume;
        }

        private AudioClip Clip(string path)
        {
            if (clips.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>(path);
            clips[path] = c;
            return c;
        }

        public void Music(string track)
        {
            if (track == currentTrack) return;
            currentTrack = track;
            var c = track == null ? null : Clip("Audio/Music/" + track);
            music.Stop();
            if (c == null) return;
            music.clip = c;
            music.volume = musicVolume;
            music.Play();
        }

        /// <summary>Plays a cue. Rapid repeats of the same cue are thinned so chains don't clip into noise.</summary>
        public void Sfx(string key, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(key)) return;
            CueLog.Add(key);
            if (CueLog.Count > 300) CueLog.RemoveAt(0);
            if (sfxVolume <= 0f) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(key, out var last) && now - last < 0.04f) return;
            lastPlayed[key] = now;
            var c = Clip("Audio/Sfx/" + Cue(key));
            if (c == null) return;
            AudioSource src = null;
            foreach (var s in sfx) if (!s.isPlaying && !held.Contains(s)) { src = s; break; }
            if (src == null) foreach (var s in sfx) if (!held.Contains(s)) { src = s; break; }
            if (src == null) return;
            src.pitch = pitch;
            src.PlayOneShot(c, volume * sfxVolume);
        }

        private readonly HashSet<AudioSource> held = new HashSet<AudioSource>();

        /// <summary>
        /// Battle pause (GG-T126): freezes every sound effect still ringing from the frozen beat, mid-clip, and
        /// resumes them from the same sample on Resume. Music keeps playing, and new cues (the pause menu's own
        /// clicks) take a source that isn't held.
        /// </summary>
        public void HoldSfx(bool hold)
        {
            if (hold)
            {
                foreach (var s in sfx) if (s.isPlaying) { s.Pause(); held.Add(s); }
                return;
            }
            foreach (var s in held) if (s != null) s.UnPause();
            held.Clear();
        }

        /// <summary>Silences every sound effect still ringing (held or playing) and drops the hold set — the endless
        /// loop's between-encounter teardown (GG-T61), so nothing from the last battle bleeds into the next. The pool
        /// itself is fixed (10 sources, never grown), and music is left alone.</summary>
        public void StopSfx()
        {
            foreach (var s in sfx) if (s != null) s.Stop();
            held.Clear();
        }

        /// <summary>Number of AudioSources this service owns (music + the fixed SFX pool), for leak checks.</summary>
        public int SourceCount => sfx.Count + (music != null ? 1 : 0);

        /// <summary>Semantic cue → file. Keep game code speaking in cues, not filenames.</summary>
        private static string Cue(string key)
        {
            switch (key)
            {
                case "ui.click": case "ui.back": return "ui_click";
                case "ui.select": return "ui_select";
                case "ui.buy": return "ui_coin";
                case "ui.error": return "ui_error";
                case "ui.press": return "ui_press";
                case "ui.confirm": return "ui_confirm";
                case "ui.cancel": return "ui_cancel";
                case "move": return "move_" + Random.Range(0, 6);
                case "hit": return "hit_" + Random.Range(0, 7);
                default: return key; // every other cue has a same-named file (tools/make_sfx.py)
            }
        }
    }
}
