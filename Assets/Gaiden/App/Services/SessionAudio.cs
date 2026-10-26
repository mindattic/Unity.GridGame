using Unity.Shared.Core;
using UnityEngine;

namespace GridGame.App
{
    // GG-T205: GridGame doesn't read its sound settings from Unity.Shared profiles yet (that migration is
    // GG-T99, separate and out of scope here); this is the narrow slice that still lets a CLI or agent
    // launching the game silence it. Unity.Shared.ProfilesBootstrap turns on Profiles.SessionMuted before
    // the first scene loads when launched with -muted or MINDATTIC_CLI_MUTED=1 (session-only: nothing is
    // ever written to this game's own settings). Mirrors Unity.QuickDuel's SessionAudio.
    public static class SessionAudio
    {
        // True once Apply found the session muted (the CLI mute only; GridGame has no player-facing mute
        // setting of its own yet).
        public static bool Muted { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnLoad() => Apply();

        // Zeroes the listener for this run if Profiles.SessionMuted is on. Never unmutes what something
        // else muted. Returns Muted.
        public static bool Apply()
        {
            Muted = Profiles.SessionMuted;
            if (Muted)
            {
                AudioListener.volume = 0f;
                Debug.Log("GridGame: audio muted for this session (Unity.Shared CLI mute; -muted / MINDATTIC_CLI_MUTED=1).");
            }
            return Muted;
        }
    }
}
