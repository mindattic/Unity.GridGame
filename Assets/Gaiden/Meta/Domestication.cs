using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Meta
{
    /// <summary>
    /// Domestication (§25.11.3, GG-T303): a tamed monster still standing at a victory with a full bond asks to join. Accepted,
    /// it becomes a roster member (<see cref="HeroSave.Domesticated"/>) that goes to the party when a slot is free, else the
    /// reserve, and fights as an independent ally (its own AI and timeline icon, the Pincer keyword). The drawbacks: it
    /// wears no gear, learns no skills or tomes, has no skill tree, earns half XP and stops growing at <see cref="LevelCap"/>.
    /// One of each monster class (the roster is keyed by class).
    /// </summary>
    public static class Domestication
    {
        /// <summary>A domesticated monster never grows past this level.</summary>
        public const int LevelCap = 30;
        /// <summary>A domesticated monster earns this share of a battle's XP.</summary>
        public const float XpFactor = 0.5f;

        /// <summary>One "wants to join" offer on the results screen.</summary>
        public sealed class Offer
        {
            public string ClassId;
            public int Level;
            public override string ToString() => $"{ClassId} L{Level}";
        }

        /// <summary>The fully bonded tamed monsters standing at the end of this (won) battle, one per class, skipping
        /// classes the roster already holds.</summary>
        public static List<Offer> Offers(ContentDb c, SaveData s, BattleState battle)
        {
            var list = new List<Offer>();
            if (battle == null || battle.Phase != BattlePhase.Victory) return list;
            foreach (var u in battle.Units.Where(Taming.WantsToJoin).OrderBy(u => u.Id))
            {
                if (s.Hero(u.Def.Id) != null || list.Any(o => o.ClassId == u.Def.Id)) continue;
                list.Add(new Offer { ClassId = u.Def.Id, Level = Math.Min(LevelCap, u.Level) });
            }
            return list;
        }

        /// <summary>Why this offer can't be taken (already in the roster, no such monster), or null.</summary>
        public static string WhyNot(ContentDb c, SaveData s, Offer o)
        {
            var def = o == null ? null : c.TryClass(o.ClassId);
            if (def == null || def.Team != Team.Enemy) return "No such monster";
            if (s.Hero(o.ClassId) != null) return "Already with you";
            return null;
        }

        /// <summary>Takes the monster in: a new roster member at its battle level, into the party if there's room, else the reserve.</summary>
        public static Result Accept(ContentDb c, SaveData s, Offer o)
        {
            var why = WhyNot(c, s, o);
            if (why != null) return Result.Fail(why);
            var def = c.Class(o.ClassId);
            var h = new HeroSave { ClassId = def.Id, Domesticated = true, TotalXp = Formulas.TotalXpForLevel(Math.Max(1, Math.Min(LevelCap, o.Level))) };
            h.Bar = new List<string> { null, null, null, null, null };
            s.Roster.Add(h);
            bool party = s.Party.Count < Progression.MaxPartySize;
            if (party) s.Party.Add(h.ClassId);
            return Result.Success(party ? $"{def.Name} joins the party!" : $"{def.Name} joins you (in reserve)");
        }

        /// <summary>The XP a battle's <paramref name="xp"/> is worth to this roster member: all of it for a hero; for a
        /// domesticated monster <see cref="XpFactor"/> of it, never past <see cref="LevelCap"/>.</summary>
        public static int XpFor(HeroSave h, int xp)
        {
            if (h == null || !h.Domesticated || xp <= 0) return xp;
            int room = Math.Max(0, Formulas.TotalXpForLevel(LevelCap) - h.TotalXp);
            return Math.Min(room, (int)Math.Floor(xp * XpFactor));
        }
    }
}
