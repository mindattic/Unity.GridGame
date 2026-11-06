using System;
using System.Collections.Generic;
using GridGame.Core.Content;
using GridGame.Core.Rules;

namespace GridGame.Core.Battle
{
    /// <summary>Mid-battle recruitment (§25.11.1, GG-T132; Fire Emblem's "talk"): neutral recruitables, what heroes do for
    /// them, and the Talk command that brings them over.</summary>
    internal sealed partial class BattleRunner
    {
        /// <summary>A wave's recruitables step onto the board on <see cref="Team.Neutral"/>: no timeline icon, nobody's target.</summary>
        private void SpawnRecruitables(WaveDef wave)
        {
            if (wave.Recruitables == null) return;
            foreach (var r in wave.Recruitables)
            {
                var def = r == null ? null : C.TryClass(r.Class);
                if (def == null || def.Team != Team.Hero || excludedGuests.Contains(def.Id)) continue;
                // Never a second of a class already on the board (a party member, a guest, or an earlier recruitable).
                bool already = false;
                foreach (var u in S.Units) if ((u.IsHero || u.IsNeutral) && u.Def.Id == def.Id) already = true;
                if (already) continue;
                Coord? at = new Coord(r.X, r.Y);
                if (r.X < 0 || r.Y < 0 || !S.FreeForSpawn(at.Value) || S.TrapAt(at.Value) != null)
                    at = RandomFreeTile(1, 1, 2, BattleConfig.Rows - 3) ?? RandomFreeTile(1, 1, 0, BattleConfig.Rows - 1);
                if (at == null) continue;
                var spec = new HeroSpec { ClassId = def.Id, Level = Math.Max(r.Level, S.RecommendedLevel), Bar = new List<string>(def.DefaultLoadout) };
                var unit = SpawnHero(spec, at, unlockedSlotsForGuests);
                if (!unit.OnBoard) { S.Units.Remove(unit); continue; }
                unit.Team = Team.Neutral;
                unit.Recruitable = r;
                unit.Hp = Math.Max(1, unit.MaxHp * r.EntryHpPercent / 100);
                var appeared = new RecruitableAppearedEvent { UnitId = unit.Id, At = at.Value };
                foreach (var c in r.Conditions) if (c != null) appeared.Wants.Add(Recruitment.Describe(C, c));
                Emit(appeared);
                Announce($"{unit.Name} watches from the side — talk to them!", unit.Id);
            }
        }

        /// <summary>Note something a hero did for a recruitable ("healed", "pincer"), both in general and by that hero's class.</summary>
        private void MarkRecruit(Unit neutral, string what, Unit hero)
        {
            if (neutral?.Recruitable == null || hero == null) return;
            bool fresh = neutral.RecruitMarks.Add(what);
            neutral.RecruitMarks.Add(what + ":" + hero.Def.Id);
            if (fresh && !PreviewOnly) Announce(what == "healed" ? $"{neutral.Name} looks grateful" : $"{neutral.Name} fought beside you!", neutral.Id);
        }

        /// <summary>A hero pincer strikes: every recruitable standing where a supporter would — beside either attacker — took part.</summary>
        private void MarkPincerWitnesses(Pincer p)
        {
            foreach (var u in S.Units)
            {
                if (!Recruitment.IsRecruitable(u)) continue;
                if (p.A.Alive && u.IsAdjacentTo(p.A)) MarkRecruit(u, "pincer", p.A);
                if (p.B.Alive && u.IsAdjacentTo(p.B)) MarkRecruit(u, "pincer", p.B);
            }
        }

        /// <summary>The Talk command (GG-T132). Unmet terms: the recruitable says what it wants, no time passes (returns
        /// false, like a no-op drag). Met: pay any gold asked, flip it to the hero team as a guest ally — one action.</summary>
        private bool DoTalk(TalkCommand t)
        {
            var hero = S.UnitById(t.HeroId);
            var target = S.UnitById(t.TargetId);
            if (hero == null || !hero.Alive || !hero.IsCommandable) return Refuse(null, "No such hero");
            if (!Recruitment.IsRecruitable(target)) return Refuse(hero, "Nobody to talk to");
            if (!hero.IsAdjacentTo(target)) return Refuse(hero, "Too far to talk");
            if (hero.IsImmobile) return Refuse(hero, "Can't act");

            var unmet = Recruitment.Unmet(S, hero, target);
            if (unmet.Count > 0)
            {
                var offer = new RecruitOfferedEvent { HeroId = hero.Id, UnitId = target.Id, Lines = Recruitment.Lines(C, hero, target, false) };
                foreach (var c in unmet) offer.Wants.Add(Recruitment.Describe(C, c));
                Emit(offer);
                return false;
            }

            Emit(new ActionBeganEvent { ActorId = hero.Id, Label = "Talk" });
            int gold = target.Recruitable.GoldAsked;
            if (gold > 0) S.Results.CoinsSpent += gold;
            target.Team = Team.Hero;
            target.IsGuest = true; // from here it's a guest ally: standing at a victory, it joins the roster (§25.11)
            S.PendingTimeBankSeconds = 0f;
            Emit(new RecruitJoinedEvent { HeroId = hero.Id, UnitId = target.Id, GoldPaid = gold, Lines = Recruitment.Lines(C, hero, target, true) });
            Announce($"{target.Name} joins the fight!", target.Id);
            return true;
        }
    }
}
