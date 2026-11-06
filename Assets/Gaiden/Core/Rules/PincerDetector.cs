using System;
using System.Collections.Generic;
using GridGame.Core.Battle;
using GridGame.Core.Content;

namespace GridGame.Core.Rules
{
    public sealed class Pincer
    {
        public Unit A;
        public Unit B;
        /// <summary>Distinct opponents in the line, ordered from A toward B.</summary>
        public List<Unit> Opponents = new List<Unit>();
        public List<Unit> SupportersA = new List<Unit>();
        public List<Unit> SupportersB = new List<Unit>();
        /// <summary>Chain links toward A / toward B (§1.2.3, GG-T187): each ally → the unit it connects through on its
        /// way to that attacker (the attacker itself when adjacent, otherwise the next ally along the chain).</summary>
        public Dictionary<Unit, Unit> LinksA = new Dictionary<Unit, Unit>();
        public Dictionary<Unit, Unit> LinksB = new Dictionary<Unit, Unit>();

        /// <summary>The attacker a supporter is chained to (A when it supports both).</summary>
        public Unit AttackerOf(Unit supporter) => SupportersA.Contains(supporter) || !SupportersB.Contains(supporter) ? A : B;

        /// <summary>The unit a supporter connects through toward its attacker (null if it isn't one).</summary>
        public Unit LinkOf(Unit supporter)
        {
            var links = AttackerOf(supporter) == A ? LinksA : LinksB;
            return links.TryGetValue(supporter, out var next) ? next : null;
        }

        /// <summary>The chain from a supporter to its attacker, both ends included.</summary>
        public List<Unit> ChainOf(Unit supporter)
        {
            var attacker = AttackerOf(supporter);
            var links = attacker == A ? LinksA : LinksB;
            var chain = new List<Unit>();
            var cur = supporter;
            for (int guard = 0; guard < 64 && cur != null; guard++)
            {
                chain.Add(cur);
                if (cur == attacker) break;
                cur = links.TryGetValue(cur, out var next) ? next : null;
            }
            return chain;
        }

        /// <summary>Swap the ends (A↔B) with everything that hangs off them.</summary>
        public void Flip()
        {
            var t = A; A = B; B = t;
            var ts = SupportersA; SupportersA = SupportersB; SupportersB = ts;
            var tl = LinksA; LinksA = LinksB; LinksB = tl;
            Opponents.Reverse();
        }
    }

    /// <summary>
    /// Pincer geometry (bible §1.2, GG-LAW-2): two pincer-eligible allies (heroes, Sentient enemies) in the same row or column with
    /// every tile strictly between them covered by opponents (no gaps, no allies). A multi-tile
    /// enemy covering several line tiles counts as one opponent. Board-wide scan.
    /// </summary>
    public static class PincerDetector
    {
        public static List<Pincer> Detect(BattleState s, Team team, Unit dropped)
        {
            var members = new List<Unit>();
            foreach (var u in s.Units)
                if (u.Alive && u.Team == team && !u.IsMultiTile && u.CanPincer) members.Add(u);

            var pairs = new List<Pincer>();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                {
                    var p = TryPair(s, members[i], members[j], team);
                    if (p != null) pairs.Add(p);
                }
            return Order(pairs, dropped);
        }

        public static Pincer TryPair(BattleState s, Unit a, Unit b, Team team)
        {
            var pa = a.Pos; var pb = b.Pos;
            if (pa.X != pb.X && pa.Y != pb.Y) return null;
            if (pa.Manhattan(pb) < 2) return null;
            // Orient A→B
            var step = new Coord(Math.Sign(pb.X - pa.X), Math.Sign(pb.Y - pa.Y));
            var opponents = new List<Unit>();
            for (var c = pa + step; c != pb; c = c + step)
            {
                var occ = s.UnitAt(c);
                if (occ == null || occ.Team == team || occ.IsNeutral) return null; // a neutral in the line is a gap (GG-T132)
                if (!opponents.Contains(occ)) opponents.Add(occ);
            }
            if (opponents.Count == 0) return null;
            var p = new Pincer { A = a, B = b, Opponents = opponents };
            p.SupportersA = FindSupporters(s, a, b, p.LinksA);
            p.SupportersB = FindSupporters(s, b, a, p.LinksB);
            return p;
        }

        /// <summary>The support action a unit performs (§1.2.3, GG-T187): its class's, else the default 50% strike.</summary>
        public static SupportDef SupportOf(Unit u) => u?.Def?.Support ?? SupportDef.Default;

        /// <summary>
        /// Supporters (§1.2.3, GG-T187): every living ally (not the partner) connected to <paramref name="attacker"/> by an
        /// uninterrupted cardinal chain of allies — any gap breaks it — whose distance along that chain (1 = adjacent) is
        /// within its class's <see cref="SupportDef.Range"/>. Every ally is a link, whatever its own range.
        /// <paramref name="links"/> (optional) records which unit each one connects through.
        /// </summary>
        public static List<Unit> FindSupporters(BattleState s, Unit attacker, Unit partner, Dictionary<Unit, Unit> links = null)
        {
            var allies = new List<Unit>();
            foreach (var u in s.Units)
                if (u.Alive && u.Team == attacker.Team && u != attacker && u != partner) allies.Add(u);
            allies.Sort((x, y) => x.Id.CompareTo(y.Id));

            var dist = new Dictionary<Unit, int> { [attacker] = 0 };
            var queue = new Queue<Unit>();
            queue.Enqueue(attacker);
            var list = new List<Unit>();
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var u in allies)
                {
                    if (dist.ContainsKey(u) || !u.IsAdjacentTo(cur)) continue;
                    int d = dist[cur] + 1;
                    dist[u] = d;
                    queue.Enqueue(u);
                    if (links != null) links[u] = cur;
                    int range = SupportOf(u).Range;
                    if (range <= 0 || d <= range) list.Add(u);
                }
            }
            list.Sort((x, y) => x.Id.CompareTo(y.Id));
            return list;
        }

        /// <summary>Chains first (B of one pair is A/B of the next), otherwise nearest to the dropped hero (§1.2.2).</summary>
        private static List<Pincer> Order(List<Pincer> pairs, Unit dropped)
        {
            var ordered = new List<Pincer>();
            var remaining = new List<Pincer>(pairs);
            Coord origin = dropped != null ? dropped.Pos : new Coord(0, 0);
            Unit chainEnd = null;
            while (remaining.Count > 0)
            {
                Pincer next = null;
                if (chainEnd != null)
                {
                    foreach (var p in remaining)
                        if (p.A == chainEnd || p.B == chainEnd) { next = p; break; }
                }
                if (next == null)
                {
                    if (ordered.Count == 0 && dropped != null)
                        foreach (var p in remaining)
                            if (p.A == dropped || p.B == dropped) { next = p; break; }
                    if (next == null)
                    {
                        int best = int.MaxValue;
                        foreach (var p in remaining)
                        {
                            int d = Math.Min(p.A.Pos.Manhattan(origin), p.B.Pos.Manhattan(origin));
                            if (d < best) { best = d; next = p; }
                        }
                    }
                }
                // Orient so the chain flows: the shared hero is A of the next link.
                if (chainEnd != null && next.B == chainEnd) next.Flip();
                else if (ordered.Count == 0 && dropped != null && next.B == dropped) next.Flip();
                ordered.Add(next);
                remaining.Remove(next);
                chainEnd = next.B;
                origin = next.B.Pos;
            }
            return ordered;
        }
    }
}
