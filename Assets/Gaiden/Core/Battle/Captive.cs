namespace GridGame.Core.Battle
{
    /// <summary>A rescued-hero roll for one battle (§25.11.2, GG-T236): a hero class held captive, freed as a guest ally
    /// on <see cref="Wave"/>. Rolled by the meta layer (<c>Rescues.Roll</c>), never authored in content.</summary>
    public sealed class CaptiveSpec
    {
        public string Class;
        /// <summary>0-based wave the captive appears on.</summary>
        public int Wave;
    }
}
