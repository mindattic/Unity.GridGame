using System.Collections.Generic;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    internal sealed partial class BattleRunner
    {
        private CaptiveSpec captive;

        /// <summary>A rolled captive (§25.11.2, GG-T236) steps onto its wave as a guest ally: from then on it is an ordinary
        /// guest (standing at a victory → joins; down at the end → not lost).</summary>
        private void SpawnCaptive(int waveIndex)
        {
            if (captive == null || captive.Wave != waveIndex) return;
            var def = C.TryClass(captive.Class);
            if (def == null || def.Team != Team.Hero || excludedGuests.Contains(def.Id)) return;
            foreach (var u in S.Units) if (u.IsHero && u.Def.Id == def.Id) return;
            var at = RandomFreeTile(1, 1, 3, BattleConfig.Rows - 1) ?? RandomFreeTile(1, 1, 0, BattleConfig.Rows - 1);
            if (at == null) return;
            var spec = new HeroSpec { ClassId = def.Id, Level = S.RecommendedLevel, Bar = new List<string>(def.DefaultLoadout) };
            var unit = SpawnHero(spec, at, unlockedSlotsForGuests);
            if (!unit.OnBoard) return;
            unit.IsGuest = true;
            S.Captive = def.Id;
            Emit(new GuestJoinedEvent { UnitId = unit.Id, At = at.Value, Captive = true });
            Announce($"A captive {unit.Name}! Keep them alive to free them.", unit.Id);
        }
    }
}
