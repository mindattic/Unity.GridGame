using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// Wanderers (§25.11.2, GG-T342). Owner: "you will sometimes meet a hero while fighting a monster and they will fight
    /// the monsters separately but you are able to complete pincer moves with them so you fill a trust gauge by pincering,
    /// or healing them". A rolled wanderer is a hero-team <see cref="Unit.IsIndependent"/> ally like a tamed beast: never
    /// commanded, its own turn icon, the shared ally AI (<see cref="RunAllyTurn"/>), and it pincers with the party. Trust
    /// grows by <see cref="BattleConfig.TrustPerPincer"/> when a hero pincer includes it and
    /// <see cref="BattleConfig.TrustPerHeal"/> when a hero heals it, up to <see cref="BattleConfig.WandererTrustNeeded"/>.
    /// Whether it joins is decided on the results screen (Meta <c>Wanderers</c>), never here.
    /// </summary>
    internal sealed partial class BattleRunner
    {
        private WandererSpec wanderer;

        /// <summary>A rolled wanderer steps onto its wave at the stage's recommended level with its class loadout.</summary>
        private void SpawnWanderer(int waveIndex)
        {
            if (wanderer == null || wanderer.Wave != waveIndex) return;
            var def = C.TryClass(wanderer.Class);
            if (def == null || def.Team != Team.Hero || excludedGuests.Contains(def.Id)) return;
            foreach (var u in S.Units) if (u.IsHero && u.Def.Id == def.Id) return;
            var at = RandomFreeTile(1, 1, 3, BattleConfig.Rows - 1) ?? RandomFreeTile(1, 1, 0, BattleConfig.Rows - 1);
            if (at == null) return;
            var spec = new HeroSpec { ClassId = def.Id, Level = S.RecommendedLevel, Bar = new List<string>(def.DefaultLoadout) };
            var unit = SpawnHero(spec, at, unlockedSlotsForGuests);
            if (!unit.OnBoard) return;
            unit.Wanderer = true;
            unit.Trust = 0;
            unit.TrustNeeded = BattleConfig.WandererTrustNeeded;
            unit.ActedThisWindow = false;
            S.Wanderer = def.Id;
            EnsureTurnEntry(unit);
            Emit(new WandererJoinedEvent { UnitId = unit.Id, At = at.Value, TrustNeeded = unit.TrustNeeded });
            Announce($"A wandering {unit.Name} fights the monsters! Fight beside them to earn their trust.", unit.Id);
        }

        /// <summary>Adds trust to a standing wanderer (capped at its need) and says so.</summary>
        private void GainTrust(Unit w, int amount, string reason)
        {
            if (w == null || !w.Wanderer || !w.Alive || !w.IsHero || amount <= 0 || w.Trust >= w.TrustNeeded) return;
            bool wasFull = w.TrustFull;
            int before = w.Trust;
            w.Trust = Math.Min(w.TrustNeeded, w.Trust + amount);
            Emit(new TrustGrewEvent { UnitId = w.Id, Trust = w.Trust, TrustNeeded = w.TrustNeeded, Gain = w.Trust - before, Reason = reason });
            if (w.TrustFull && !wasFull) Announce($"{w.Name} trusts you!", w.Id);
        }

        /// <summary>A hero pincer that includes a wanderer — as one of its two ends or as a flanking supporter — alongside at
        /// least one hero the player commands earns its trust once per pincer.</summary>
        private void TrustFromPincer(Pincer p, List<Unit> supporters)
        {
            Unit w = null; bool commanded = false;
            void See(Unit u)
            {
                if (u == null) return;
                if (u.Wanderer && u.IsHero) w = u;
                else if (u.IsCommandable) commanded = true;
            }
            See(p.A); See(p.B);
            foreach (var su in supporters) See(su);
            if (w != null && commanded) GainTrust(w, BattleConfig.TrustPerPincer, "pincer");
        }

        /// <summary>A hero the player commands restored a wanderer's HP.</summary>
        private void TrustFromHeal(Unit target, Unit source, int healed)
        {
            if (healed <= 0 || source == null || source == target || !source.IsCommandable) return;
            if (target.Wanderer) GainTrust(target, BattleConfig.TrustPerHeal, "heal");
        }
    }
}
