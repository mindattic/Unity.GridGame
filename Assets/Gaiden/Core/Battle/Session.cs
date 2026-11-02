using System;
using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// A player's seat (GG-LAW-13, Unity.Multiplayer's multiplayer-ready standard, GG-T192). Every command carries the
    /// seat that issued it and every hero is owned by a seat, so the rules see players only as ids — never as "the"
    /// player. The single-player game runs entirely as seat 0, the default, so nothing it does changes.
    /// </summary>
    [Serializable]
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public readonly int Seat;
        public PlayerId(int seat) { Seat = seat; }
        public bool Equals(PlayerId o) => Seat == o.Seat;
        public override bool Equals(object obj) => obj is PlayerId o && Equals(o);
        public override int GetHashCode() => Seat;
        public static bool operator ==(PlayerId a, PlayerId b) => a.Seat == b.Seat;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Seat != b.Seat;
        public override string ToString() => "P" + Seat;
    }

    /// <summary>
    /// The one seam game code talks to (multiplayer-ready rule 7). Input builds a <see cref="BattleCommand"/> stamped with
    /// <see cref="LocalPlayer"/> and hands it to <see cref="Submit"/>; presentation reads <see cref="State"/> and listens
    /// to <see cref="EventRaised"/>. Today the only implementation is <see cref="LocalSession"/>; a networked session
    /// would implement the same interface and nothing in input, presentation or rules would change.
    /// </summary>
    public interface ISession
    {
        /// <summary>Which seat this machine controls. Only input and presentation ask; the rules never do.</summary>
        PlayerId LocalPlayer { get; }
        IReadOnlyList<PlayerId> Players { get; }
        /// <summary>The authority's state. Presentation reads it; only the authority (via commands) writes it.</summary>
        BattleState State { get; }
        /// <summary>The fixed session tick: each tick applies at most one queued command.</summary>
        long Tick { get; }
        event Action<BattleEvent> EventRaised;
        /// <summary>The one command pipeline. The command is queued and applied on the next tick.</summary>
        void Submit(BattleCommand command);
    }

    /// <summary>
    /// The in-process session the shipped game runs on: owns the battle authority (<see cref="BattleState"/> +
    /// <see cref="BattleEngine"/>), queues submitted commands and applies them one per fixed tick, keeps the command log
    /// (seed + setup + log replays the battle exactly), and raises every event to presentation. Hot-seat co-op is the
    /// same session with more than one seat.
    /// </summary>
    public sealed class LocalSession : ISession
    {
        private readonly List<PlayerId> players;
        private readonly Queue<BattleCommand> pending = new Queue<BattleCommand>();

        public PlayerId LocalPlayer { get; }
        public IReadOnlyList<PlayerId> Players => players;
        public BattleState State { get; }
        public long Tick { get; private set; }
        /// <summary>Events <see cref="BattleEngine.Create"/> produced while setting the battle up (empty when attached to an existing state).</summary>
        public List<BattleEvent> OpeningEvents { get; } = new List<BattleEvent>();
        /// <summary>Every command the authority applied, in order (refused ones included: the authority saw them).</summary>
        public List<BattleCommand> CommandLog { get; } = new List<BattleCommand>();
        public event Action<BattleEvent> EventRaised;

        /// <summary>Wraps an existing battle as a session. <paramref name="playerCount"/> seats 0..n-1; heroes keep the owners they already have.</summary>
        public LocalSession(BattleState state, int playerCount = 1, int localSeat = 0)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (playerCount < 1) throw new ArgumentOutOfRangeException(nameof(playerCount));
            players = new List<PlayerId>(playerCount);
            for (int i = 0; i < playerCount; i++) players.Add(new PlayerId(i));
            LocalPlayer = new PlayerId(localSeat);
        }

        /// <summary>
        /// Starts a battle with <paramref name="playerCount"/> seats. Party member i is owned by seat i % playerCount
        /// (one seat = the whole party, exactly as the single-player game). Hero unit ids are stable: the authority
        /// assigns them in setup order, so the same setup always yields the same ids.
        /// </summary>
        public static LocalSession Create(ContentDb content, BattleSetup setup, int playerCount = 1, int localSeat = 0)
        {
            var opening = new List<BattleEvent>();
            var state = BattleEngine.Create(content, setup, opening);
            int i = 0;
            foreach (var u in state.Units)
                if (u.IsHero) u.Owner = new PlayerId(i++ % Math.Max(1, playerCount));
            var session = new LocalSession(state, playerCount, localSeat);
            session.OpeningEvents.AddRange(opening);
            return session;
        }

        /// <summary>Rebuilds a battle from its setup and a serialized command log (<see cref="CommandCodec"/>): a replay.</summary>
        public static LocalSession Replay(ContentDb content, BattleSetup setup, int playerCount, IEnumerable<string> commandLog)
        {
            var session = Create(content, setup, playerCount);
            foreach (var json in commandLog)
            {
                session.Submit(CommandCodec.FromJson(json));
                session.Step();
            }
            return session;
        }

        public bool IsSeated(PlayerId p) => p.Seat >= 0 && p.Seat < players.Count;

        public void Submit(BattleCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!IsSeated(command.Player))
            {
                EventRaised?.Invoke(new AbilityRefusedEvent { CasterId = -1, Reason = "Not a seated player" });
                return;
            }
            pending.Enqueue(command);
        }

        /// <summary>Advances <paramref name="ticks"/> fixed ticks; each applies at most one queued command. Returns every event raised.</summary>
        public List<BattleEvent> Step(int ticks = 1)
        {
            var all = new List<BattleEvent>();
            for (int t = 0; t < ticks; t++)
            {
                Tick++;
                if (pending.Count == 0) continue;
                var cmd = pending.Dequeue();
                var events = BattleEngine.Apply(State, cmd);
                CommandLog.Add(cmd);
                all.AddRange(events);
                if (EventRaised != null) foreach (var e in events) EventRaised(e);
            }
            return all;
        }

        /// <summary>Submits a command and steps until it (and anything queued ahead of it) has been applied — the
        /// battle screen's path, so a tap resolves on the same frame exactly as it always has.</summary>
        public List<BattleEvent> SubmitAndStep(BattleCommand command)
        {
            int before = pending.Count;
            Submit(command);
            return pending.Count > before ? Step(pending.Count) : new List<BattleEvent>();
        }
    }
}
