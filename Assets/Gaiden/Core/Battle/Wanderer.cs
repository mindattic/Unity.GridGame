namespace GridGame.Core.Battle
{
    /// <summary>A wandering hero for one battle (§25.11.2, GG-T342; was the GG-T236 captive): a hero class the player
    /// doesn't own who steps onto <see cref="Wave"/> and fights the monsters on its own. Rolled by the meta layer
    /// (<c>Rescues.Roll</c>), never authored in content.</summary>
    public sealed class WandererSpec
    {
        public string Class;
        /// <summary>0-based wave the wanderer appears on.</summary>
        public int Wave;
    }

    /// <summary>A wanderer steps onto the board (GG-T342): an independent ally with its own turn icon and an empty
    /// trust gauge out of <see cref="TrustNeeded"/>.</summary>
    public sealed class WandererJoinedEvent : BattleEvent
    {
        public int UnitId; public Coord At; public int TrustNeeded;
        public override string ToString() => $"Wanderer {UnitId} at {At} (trust 0/{TrustNeeded})";
    }

    /// <summary>A wanderer's trust grew (GG-T342): a hero pincer included it (<see cref="Reason"/> "pincer") or a hero
    /// healed it ("heal").</summary>
    public sealed class TrustGrewEvent : BattleEvent
    {
        public int UnitId; public int Trust; public int TrustNeeded; public int Gain; public string Reason;
        public bool Full => TrustNeeded > 0 && Trust >= TrustNeeded;
        public override string ToString() => $"Trust {UnitId} +{Gain} ({Reason}) {Trust}/{TrustNeeded}";
    }
}
