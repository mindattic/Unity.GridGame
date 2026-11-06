using System;
using System.Collections.Generic;

namespace GridGame.Core.Content
{
    /// <summary>
    /// Learnable-passive rewards (GG-T301, §25.6.4): a <see cref="ItemType.Tome"/> teaches one Passive or Reactive to a
    /// chosen hero, a <see cref="ItemType.Chest"/> rolls a droptables.json row when opened, and a theme's
    /// <see cref="ThemeDef.MilestoneReward"/> pays once on its first full clear. Every source (quests, bounties, campaign
    /// milestones, chests, battle drop tables, dialogue GrantItem) grants the Tome as an ordinary item, so adding a new
    /// signature passive to the reward tables is one items.json row plus a reference.
    /// </summary>
    public static class RewardCatalog
    {
        /// <summary>The ability a Tome item teaches, or null when <paramref name="item"/> isn't a valid Tome.</summary>
        public static AbilityDef TaughtBy(ContentDb db, ItemDef item) =>
            item != null && item.Type == ItemType.Tome ? db?.TryAbility(item.TeachesAbility) : null;

        /// <summary>A Passive or Reactive that fights (has a trigger): what a Tome may teach.</summary>
        public static bool Teachable(AbilityDef a) =>
            a != null && (a.Kind == AbilityKind.Passive || a.Kind == AbilityKind.Reactive) && a.Trigger != null;

        public static void Validate(ContentDb db, List<string> p)
        {
            foreach (var i in db.Items.Values)
            {
                string w = $"item {i.Id}";
                if (i.Type == ItemType.Tome)
                {
                    if (string.IsNullOrEmpty(i.TeachesAbility)) p.Add($"{w}: a Tome needs TeachesAbility");
                    else if (!db.Abilities.TryGetValue(i.TeachesAbility, out var a)) p.Add($"{w}: unknown ability '{i.TeachesAbility}'");
                    else if (!Teachable(a)) p.Add($"{w}: '{a.Id}' isn't a Passive or Reactive with a Trigger (a Tome teaches battle passives only)");
                }
                else if (i.TeachesAbility != null) p.Add($"{w}: only a Tome may set TeachesAbility");
                if (i.Type == ItemType.Chest)
                {
                    if (string.IsNullOrEmpty(i.OpensTable)) p.Add($"{w}: a Chest needs OpensTable");
                    else if (!db.DropTables.TryGetValue(i.OpensTable, out var t)) p.Add($"{w}: unknown drop table '{i.OpensTable}'");
                    else
                    {
                        if (t.Entries.Count == 0) p.Add($"{w}: chest table '{t.Id}' is empty");
                        foreach (var e in t.Entries)
                            if (db.TryItem(e.Item)?.Type == ItemType.Chest) p.Add($"{w}: chest table '{t.Id}' holds another chest '{e.Item}'");
                    }
                }
                else if (i.OpensTable != null) p.Add($"{w}: only a Chest may set OpensTable");
            }
            foreach (var d in db.DropTables.Values)
            {
                if (d.CoinsMin < 0 || d.CoinsMax < d.CoinsMin) p.Add($"droptable {d.Id}: coins must be 0 <= CoinsMin <= CoinsMax");
                foreach (var e in d.Entries)
                    if (!(e.Chance > 0f && e.Chance <= 1f) || e.Min < 1 || e.Max < e.Min)
                        p.Add($"droptable {d.Id}: entry '{e.Item}' needs 0 < Chance <= 1 and 1 <= Min <= Max");
            }
            foreach (var a in db.Abilities.Values)
            {
                if (a.LearnableBy == null || a.LearnableBy.Count == 0) continue;
                if (a.Kind != AbilityKind.Passive && a.Kind != AbilityKind.Reactive) p.Add($"ability {a.Id}: LearnableBy is for Passive/Reactive abilities");
                foreach (var who in a.LearnableBy)
                    if (who == null || (!ContentLoader.HeroRoles.Contains(who) && !(db.TryClass(who) is ClassDef c && c.Team == Team.Hero)))
                        p.Add($"ability {a.Id}: LearnableBy '{who}' is neither a hero class nor a Role");
            }
            foreach (var t in db.Campaign.Themes)
            {
                if (t.MilestoneReward == null) continue;
                if (db.TryItem(t.MilestoneReward) == null) p.Add($"theme {t.Id}: unknown milestone reward '{t.MilestoneReward}'");
                if (t.Stages.Count == 0) p.Add($"theme {t.Id}: a milestone reward needs a stage to clear");
            }
        }

        /// <summary>Whether <paramref name="cls"/> may learn <paramref name="a"/> (its <see cref="AbilityDef.LearnableBy"/> names
        /// the class or its Role, or is empty).</summary>
        public static bool AllowedFor(AbilityDef a, ClassDef cls)
        {
            if (a == null || cls == null) return false;
            if (a.LearnableBy == null || a.LearnableBy.Count == 0) return true;
            foreach (var who in a.LearnableBy)
                if (string.Equals(who, cls.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(who, cls.Role, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
