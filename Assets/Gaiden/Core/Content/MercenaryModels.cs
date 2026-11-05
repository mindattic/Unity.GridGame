using System.Collections.Generic;
using System.Linq;

namespace GridGame.Core.Content
{
    /// <summary>How a mercenary takes being haggled with (mercenaries.json, GG-T233).</summary>
    public enum MercTemperament { Easygoing, Fair, Greedy }

    /// <summary>A hero class for hire on Camp's Mercenaries board (mercenaries.json, GG-T233): a per-battle
    /// <see cref="Rate"/> at the shortest contract, the contract lengths offered (longer = cheaper per battle),
    /// and a temperament for haggling.</summary>
    public sealed class MercenaryDef
    {
        public string Class;
        public int Rate;
        public List<int> Terms = new List<int> { 3, 5, 10 };
        public MercTemperament Temperament = MercTemperament.Fair;
        /// <summary>The line they open with on the board.</summary>
        public string Pitch;
        /// <summary>The line they open with when a contract is nearly up.</summary>
        public string Renewal;
    }

    public static class MercenaryContent
    {
        public static void Validate(ContentDb db, List<string> p)
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var m in db.Mercenaries)
            {
                string where = $"mercenary {m.Class}";
                var cls = db.TryClass(m.Class);
                if (cls == null) { p.Add($"{where}: unknown class '{m.Class}'"); continue; }
                if (cls.Team != Team.Hero) p.Add($"{where}: not a hero class");
                if (!seen.Add(m.Class)) p.Add($"{where}: listed twice");
                if (m.Rate <= 0) p.Add($"{where}: Rate must be positive");
                if (m.Terms == null || m.Terms.Count == 0) p.Add($"{where}: no contract Terms");
                else
                {
                    if (m.Terms.Any(t => t < 2)) p.Add($"{where}: a contract must run at least 2 battles");
                    if (m.Terms.Distinct().Count() != m.Terms.Count) p.Add($"{where}: duplicate Terms");
                }
                if (string.IsNullOrWhiteSpace(m.Pitch)) p.Add($"{where}: no Pitch");
                if (string.IsNullOrWhiteSpace(m.Renewal)) p.Add($"{where}: no Renewal line");
            }
        }
    }
}
