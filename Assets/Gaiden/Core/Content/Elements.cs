using System;
using System.Collections.Generic;
using System.Linq;

namespace GridGame.Core.Content
{
    // ── There is always a way (GG-T371, BIBLE §3.3.1) ───────────────────────────────────────────────────────────────
    // Owner 2026-11-08: "always a way to defeat them (cast fire against ice, be able to buy a consumable with elemental
    // effects in case your party doesn't have what it needs specced into the build, there is always a way)."
    // elements.json is the interaction table: which element counters which, which statuses an element breaks or
    // exploits, and what it does to the board's tiles (traps today; GG-T359 tile states, and GG-T370's hazard trails, by
    // the same ids). ElementCounters.Validate is the "always a way" check: every campaign foe, rule-break boss and tile
    // hazard has a counter the Merchant always sells, whatever the party's build.

    /// <summary>What an element (or a tool's reaction set) does to a marked tile. Mirrors GG-T359's rule effects:
    /// Clear = ClearTiles on that state, Becomes = the mark turns into <see cref="TileReactionDef.Into"/>.</summary>
    public enum TileReactionKind { Clear, Becomes }

    /// <summary>One tile reaction: on a tile holding <see cref="On"/>, do <see cref="Kind"/>.</summary>
    public sealed class TileReactionDef
    {
        /// <summary>A trap id (traps.json) or a tile state id (regionrules.json TileStates, GG-T359).</summary>
        public string On;
        public TileReactionKind Kind = TileReactionKind.Clear;
        /// <summary>Becomes only: the trap or tile state it turns into.</summary>
        public string Into;
    }

    /// <summary>One element's row of the interaction table.</summary>
    public sealed class ElementDef
    {
        public DamageType Type;
        /// <summary>The elements this one is strong against: a foe aligned with (resisting) one of these is never meant
        /// to resist this one too (Validate checks the campaign's foes agree).</summary>
        public List<DamageType> Counters = new List<DamageType>();
        /// <summary>The status this element's flasks apply (a buff id), or null.</summary>
        public string Status;
        /// <summary>Statuses a hit of this element breaks off its target (Fire thaws [Frozen] and steams [Wet] off).</summary>
        public List<string> Breaks = new List<string>();
        /// <summary>Statuses a hit of this element exploits (Lightning on [Wet] ×1.5).</summary>
        public List<string> Exploits = new List<string>();
        /// <summary>What a hit of this element does to the tiles it covers (a hero's ability that deals it).</summary>
        public List<TileReactionDef> Tiles = new List<TileReactionDef>();
    }

    /// <summary>A named set of tile reactions a tool carries (<see cref="AbilityDef.TileReaction"/>): the disarm kit, the salts.</summary>
    public sealed class ReactionSetDef
    {
        public string Id;
        public List<TileReactionDef> Tiles = new List<TileReactionDef>();
    }

    /// <summary>How a rule-break boss (§14.9) is answered.</summary>
    public enum CounterKind
    {
        /// <summary>By play any party can make: where heroes stand, what dies first (no element or build needed).</summary>
        Positional,
        /// <summary>By damage of <see cref="RuleBreakCounterDef.Element"/>: a Merchant flask of it must exist.</summary>
        Element,
        /// <summary>By using <see cref="RuleBreakCounterDef.Item"/>: the Merchant must sell it.</summary>
        Item,
    }

    public sealed class RuleBreakCounterDef
    {
        public RuleBreak Rule;
        public CounterKind Counter = CounterKind.Positional;
        public DamageType? Element;
        public string Item;
    }

    /// <summary>elements.json (GG-T371).</summary>
    public sealed class ElementTableDef
    {
        /// <summary>A damage multiplier at or below this blocks the type (a foe "immune or nearly").</summary>
        public float HeavyResistance = 0.25f;
        public List<ElementDef> Elements = new List<ElementDef>();
        public List<ReactionSetDef> Reactions = new List<ReactionSetDef>();
        public List<RuleBreakCounterDef> RuleBreaks = new List<RuleBreakCounterDef>();

        public ElementDef Of(DamageType t) => Elements.Find(e => e.Type == t);
        public ReactionSetDef Reaction(string id) => id == null ? null : Reactions.Find(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        public RuleBreakCounterDef CounterOf(RuleBreak r) => RuleBreaks.Find(x => x.Rule == r);
    }

    public static class ElementCounters
    {
        private static readonly DamageType[] AllTypes = (DamageType[])Enum.GetValues(typeof(DamageType));

        /// <summary>The Merchant's always-on shelf of battle consumables (Meta.Shop.Stock sells every SoldByVendor item with a
        /// price, from the first expedition on, §25.1), each with the ability it fires.</summary>
        public static List<(ItemDef Item, AbilityDef Ability)> Purchasable(ContentDb db)
        {
            var list = new List<(ItemDef, AbilityDef)>();
            foreach (var i in db.Items.Values.OrderBy(i => i.BaseCost).ThenBy(i => i.Id, StringComparer.Ordinal))
            {
                if (!i.SoldByVendor || i.BaseCost <= 0 || i.Type != ItemType.Consumable || i.OutOfBattleOnly) continue;
                var a = Battle.BattleRunner.ItemAbility(db, i);
                if (a != null) list.Add((i, a));
            }
            return list;
        }

        /// <summary>A purchasable consumable's damage type, or null when it deals none to foes.</summary>
        public static DamageType? DamageOf(AbilityDef a) =>
            a != null && a.BaseDamage > 0 && a.Filter != TargetFilter.AllyOnly ? a.DamageType : (DamageType?)null;

        /// <summary>How much of a <paramref name="t"/> hit lands on a foe of this class: its Resistances times every always-on
        /// ReductionMultiplier passive (§4.1) that holds for any foe (no condition, or only a damage-type one).</summary>
        public static float Multiplier(ContentDb db, ClassDef c, DamageType t)
        {
            float m = c.Resistances != null && c.Resistances.TryGetValue(t, out var r) ? r : 1f;
            foreach (var id in c.Abilities)
            {
                var a = db.TryAbility(id);
                var tr = a?.Trigger;
                if (tr == null || tr.On != TriggerOn.Always) continue;
                var cond = tr.Condition;
                if (cond != null && !(cond.SelfHpBelow == 0f && cond.SelfHpAbove == 0f && cond.HasStatus == null && cond.LacksStatus == null
                                      && cond.AdjacentFoesAtLeast == 0 && cond.OtherTag == null && cond.OtherHasStatus == null && cond.OtherHpBelow == 0f))
                    continue; // situational: not a standing wall
                if (cond?.DamageType != null && cond.DamageType != t) continue;
                foreach (var e in tr.Effects)
                    if (e.Kind == TriggerEffectKind.ReductionMultiplier) m *= e.Amount;
            }
            return m;
        }

        /// <summary>The Merchant consumable a party without the right build buys for this foe: the one whose damage lands
        /// hardest (a weakness first), then the cheapest. Null when nothing it sells hurts the foe at full strength.</summary>
        public static ItemDef BestCounter(ContentDb db, ClassDef c) => BestCounter(db, c, Purchasable(db));

        private static ItemDef BestCounter(ContentDb db, ClassDef c, List<(ItemDef Item, AbilityDef Ability)> shelf)
        {
            ItemDef best = null; float bestScore = 0f;
            var resisted = AllTypes.Where(x => Multiplier(db, c, x) < 1f).ToList();
            foreach (var (item, a) in shelf)
            {
                var t = DamageOf(a);
                if (t == null) continue;
                float m = Multiplier(db, c, t.Value);
                if (m < 1f) continue;
                // On a tie, the element the table names as the counter to what the foe resists (Arcane past Tough Hide).
                var row = db.Elements?.Of(t.Value);
                float score = m + (row != null && resisted.Any(x => row.Counters.Contains(x)) ? 0.001f : 0f);
                if (score > bestScore) { best = item; bestScore = score; }
            }
            return best;
        }

        /// <summary>Does this foe stand against the party's build: it resists an element, blocks one, or wears a plate?</summary>
        public static bool NeedsCounter(ContentDb db, ClassDef c) =>
            c.ArmorPlate > 0 || AllTypes.Any(t => Multiplier(db, c, t) < 1f);

        /// <summary>The tile reactions an ability carries: its tool set (<see cref="AbilityDef.TileReaction"/>), then its
        /// element's (when it deals damage).</summary>
        public static List<TileReactionDef> ReactionsOf(ContentDb db, AbilityDef a)
        {
            var list = new List<TileReactionDef>();
            if (db?.Elements == null || a == null) return list;
            var set = db.Elements.Reaction(a.TileReaction);
            if (set != null) list.AddRange(set.Tiles);
            if (a.BaseDamage > 0 && db.Elements.Of(a.DamageType) is ElementDef e) list.AddRange(e.Tiles);
            return list;
        }

        /// <summary>The first reaction in the list for a tile holding <paramref name="id"/>, or null.</summary>
        public static TileReactionDef Match(List<TileReactionDef> reactions, string id)
        {
            if (id == null) return null;
            foreach (var r in reactions) if (r != null && string.Equals(r.On, id, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        /// <summary>The Merchant consumables a party should carry into <paramref name="stage"/> (the camp bot's shopping
        /// list): the best counter for each foe that resists or blocks something, and the tool that clears each trap its foes
        /// lay. Cheapest-first, each once.</summary>
        public static List<ItemDef> CountersFor(ContentDb db, StageDef stage)
        {
            var list = new List<ItemDef>();
            if (stage == null) return list;
            var shelf = Purchasable(db);
            foreach (var c in Foes(db, stage))
            {
                if (NeedsCounter(db, c) && BestCounter(db, c, shelf) is ItemDef i && !list.Contains(i)) list.Add(i);
                foreach (var trap in new[] { c.Trap, c.Trail })
                    if (trap != null && Clearer(db, trap, shelf) is ItemDef k && !list.Contains(k)) list.Add(k);
                // GG-T370: the hazard trail states it paints (TrailRule MarkTiles) want a clearer too.
                foreach (var fx in c.TrailRule)
                    if (fx?.Kind == RuleEffectKind.MarkTiles && fx.State != null && Clearer(db, fx.State, shelf) is ItemDef h && !list.Contains(h)) list.Add(h);
            }
            return list.OrderBy(i => i.BaseCost).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>Every enemy class a stage can field: its waves' foes (rule-break bosses included) and what they lay.</summary>
        public static List<ClassDef> Foes(ContentDb db, StageDef stage)
        {
            var list = new List<ClassDef>();
            void Add(string id)
            {
                var c = db.TryClass(id);
                if (c == null || list.Contains(c)) return;
                list.Add(c);
                Add(c.LaysEgg);
            }
            foreach (var w in stage.Waves) foreach (var e in w.Enemies) Add(e.Class);
            return list;
        }

        /// <summary>The cheapest Merchant consumable whose tile reactions clear (or transform) a tile holding <paramref name="id"/>.</summary>
        private static ItemDef Clearer(ContentDb db, string id, List<(ItemDef Item, AbilityDef Ability)> shelf)
        {
            foreach (var (item, a) in shelf)
                if (Match(ReactionsOf(db, a), id) != null) return item;
            return null;
        }

        /// <summary>The "always a way" check (GG-T371), run by <see cref="ContentLoader.Validate"/>.</summary>
        /// <param name="isTileState">Whether an id names a region-rule tile state (null = <see cref="ContentDb.TryTileState"/>, GG-T359).</param>
        /// <param name="hazardStates">Every tile state that hurts to stand on; each needs a Merchant counter too (null = the
        /// region rules' tile states marked Hazard, GG-T359; GG-T370's trails land there).</param>
        public static void Validate(ContentDb db, List<string> p, Func<string, bool> isTileState = null, IEnumerable<string> hazardStates = null)
        {
            isTileState = isTileState ?? (id => db.TryTileState(id) != null);
            hazardStates = hazardStates ?? db.TileStates.Values.Where(s => s.Hazard).Select(s => s.Id).ToList();
            var t = db.Elements;
            if (t == null || t.Elements.Count == 0) { p.Add("elements.json: no element table (GG-T371)"); return; }
            const string Tag = " (always a way, GG-T371)";
            bool TileKnown(string id) => id != null && (db.Traps.ContainsKey(id) || (isTileState != null && isTileState(id)));

            // ── the table itself ──
            if (!(t.HeavyResistance >= 0f && t.HeavyResistance < 1f)) p.Add($"elements.json: HeavyResistance {t.HeavyResistance} outside 0..1");
            foreach (var dt in AllTypes)
            {
                int n = t.Elements.Count(e => e.Type == dt);
                if (n == 0) p.Add($"elements.json: no row for {dt} (every DamageType has one)");
                else if (n > 1) p.Add($"elements.json: {dt} has {n} rows");
            }
            void CheckTiles(List<TileReactionDef> tiles, string where)
            {
                foreach (var r in tiles)
                {
                    if (r == null || string.IsNullOrEmpty(r.On)) { p.Add($"{where}: a tile reaction needs On"); continue; }
                    if (!TileKnown(r.On)) p.Add($"{where}: unknown trap or tile state '{r.On}'");
                    if (r.Kind == TileReactionKind.Becomes && !TileKnown(r.Into)) p.Add($"{where}: Becomes '{r.On}' needs a known Into (got '{r.Into}')");
                    if (r.Kind == TileReactionKind.Clear && r.Into != null) p.Add($"{where}: Clear '{r.On}' takes no Into");
                }
                foreach (var dup in tiles.Where(r => r?.On != null).GroupBy(r => r.On, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    p.Add($"{where}: '{dup.Key}' listed twice");
            }
            foreach (var e in t.Elements)
            {
                string w = $"elements.json {e.Type}";
                if (e.Counters.Contains(e.Type)) p.Add($"{w}: an element can't counter itself");
                if (e.Status != null && db.TryBuff(e.Status) == null) p.Add($"{w}: unknown Status buff '{e.Status}'");
                foreach (var b in e.Breaks.Concat(e.Exploits)) if (db.TryBuff(b) == null) p.Add($"{w}: unknown buff '{b}'");
                CheckTiles(e.Tiles, w);
            }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in t.Reactions)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) { p.Add("elements.json: a reaction set needs an Id"); continue; }
                if (!ids.Add(r.Id)) p.Add($"elements.json: reaction set '{r.Id}' listed twice");
                if (r.Tiles.Count == 0) p.Add($"elements.json reaction {r.Id}: no Tiles");
                CheckTiles(r.Tiles, $"elements.json reaction {r.Id}");
            }
            foreach (var a in db.Abilities.Values)
            {
                if (a.TileReaction == null) continue;
                if (t.Reaction(a.TileReaction) == null) p.Add($"ability {a.Id}: unknown TileReaction set '{a.TileReaction}' (elements.json Reactions)");
                if (a.Mode != TargetMode.PickTile || a.Shape == TargetShape.AllAllies || a.Shape == TargetShape.AllEnemies || a.Shape == TargetShape.Self)
                    p.Add($"ability {a.Id}: a TileReaction tool picks a tile and covers tiles (PickTile, a tile shape)");
            }
            foreach (var dup in t.RuleBreaks.GroupBy(r => r.Rule).Where(g => g.Count() > 1)) p.Add($"elements.json: rule break {dup.Key} listed twice");

            // ── the Merchant's shelf ──
            var shelf = Purchasable(db);
            var sold = new Dictionary<DamageType, ItemDef>();
            foreach (var (item, a) in shelf)
                if (DamageOf(a) is DamageType dt && !sold.ContainsKey(dt)) sold[dt] = item;
            foreach (var e in t.Elements)
                if (e.Type != DamageType.Physical && !sold.ContainsKey(e.Type))
                    p.Add($"elements.json {e.Type}: the Merchant sells no consumable that deals it (every element has a flask){Tag}");

            // ── every campaign foe ──
            var checkedClass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in db.CampaignStages())
            {
                foreach (var c in Foes(db, s))
                {
                    if (!checkedClass.Add(c.Id)) continue;
                    string w = $"stage {s.Id}: {c.Id}";
                    var mult = AllTypes.ToDictionary(x => x, x => Multiplier(db, c, x));
                    // 1. Something the Merchant sells always hurts it at full strength.
                    if (BestCounter(db, c, shelf) == null)
                    {
                        var walls = AllTypes.Where(x => mult[x] < 1f).Select(x => $"{x} ×{mult[x]:0.##}");
                        p.Add($"{w} resists or blocks {string.Join(", ", walls)} and no Merchant consumable hurts it at full strength{Tag}");
                    }
                    // 2. Each weakness can be bought.
                    foreach (var x in AllTypes)
                        if (mult[x] > 1f && !sold.ContainsKey(x)) p.Add($"{w} is weak to {x} (×{mult[x]:0.##}) but the Merchant sells nothing that deals {x}{Tag}");
                    // 3. The table agrees with the foe: whatever counters an element it resists is not resisted too, and can be bought.
                    foreach (var x in AllTypes.Where(x => mult[x] < 1f))
                    {
                        var counters = t.Elements.Where(e => e.Counters.Contains(x)).Select(e => e.Type).ToList();
                        if (counters.Count == 0) { p.Add($"{w} resists {x}, and elements.json names nothing that counters {x}{Tag}"); continue; }
                        foreach (var k in counters.Where(k => mult[k] < 1f))
                            p.Add($"{w} resists {x} and also {k}, though elements.json says {k} counters {x}{Tag}");
                        if (!counters.Any(k => sold.ContainsKey(k) && mult[k] >= 1f))
                            p.Add($"{w} resists {x}: the Merchant sells nothing of {string.Join("/", counters)} that hurts it at full strength{Tag}");
                    }
                    // 4. A plate (§13.5) stops Physical blows from the front: an element the Merchant sells goes around it.
                    if (c.ArmorPlate > 0 && !sold.Keys.Any(x => x != DamageType.Physical && mult[x] > t.HeavyResistance))
                        p.Add($"{w} wears an armor plate and the Merchant sells no non-Physical consumable that gets past it{Tag}");
                    // 5. An element-gated ward: a buff the foe can raise that blocks most damage needs an element that breaks it.
                    foreach (var b in GatedWards(db, c))
                    {
                        var breakers = t.Elements.Where(e => e.Breaks.Contains(b.Id, StringComparer.OrdinalIgnoreCase)).Select(e => e.Type).ToList();
                        if (!breakers.Any(k => sold.ContainsKey(k)))
                            p.Add($"{w} can raise [{b.Id}] (takes ×{b.IncomingDamageMultiplier:0.##}) and no element the Merchant sells breaks it (elements.json Breaks){Tag}");
                    }
                }
                // 6. A rule-break boss is answered by play any party can make, or by something the Merchant sells.
                foreach (var wave in s.Waves)
                    foreach (var e in wave.Enemies)
                    {
                        if (e.RuleBreak == RuleBreak.None) continue;
                        string w = $"stage {s.Id}: rule breaker {e.Class} ({e.RuleBreak})";
                        var rc = t.CounterOf(e.RuleBreak);
                        if (rc == null) p.Add($"{w}: elements.json RuleBreaks has no counter for {e.RuleBreak}{Tag}");
                        else if (rc.Counter == CounterKind.Element && (rc.Element == null || !sold.ContainsKey(rc.Element.Value)))
                            p.Add($"{w}: its counter is {rc.Element} damage and the Merchant sells none{Tag}");
                        else if (rc.Counter == CounterKind.Item && !shelf.Exists(x => string.Equals(x.Item.Id, rc.Item, StringComparison.OrdinalIgnoreCase)))
                            p.Add($"{w}: its counter item '{rc.Item}' is not sold by the Merchant{Tag}");
                    }
            }

            // ── every trap and tile hazard ──
            foreach (var trap in db.Traps.Values)
                if (Clearer(db, trap.Id, shelf) == null)
                    p.Add($"trap {trap.Id}: no Merchant consumable clears it (an element's Tiles or a tool's reaction set){Tag}");
            if (hazardStates != null)
                foreach (var id in hazardStates)
                    if (Clearer(db, id, shelf) == null)
                        p.Add($"tile state {id}: a hazard no Merchant consumable clears (an element's Tiles or a tool's reaction set){Tag}");
        }

        /// <summary>Buffs a foe can put on itself or its kin (its kit's ApplyBuff of kind Buff, boss phases) that block most
        /// damage (IncomingDamageMultiplier at or below the table's HeavyResistance): an element-gated ward.</summary>
        public static List<BuffDef> GatedWards(ContentDb db, ClassDef c)
        {
            var list = new List<BuffDef>();
            float heavy = db.Elements?.HeavyResistance ?? 0.25f;
            var abilities = new List<string>(c.Abilities);
            if (c.ChargeAbility != null) abilities.Add(c.ChargeAbility);
            foreach (var ph in c.BossPhases) if (ph.OnEnterAbility != null) abilities.Add(ph.OnEnterAbility);
            foreach (var id in abilities)
            {
                var a = db.TryAbility(id);
                var b = db.TryBuff(a?.ApplyBuff);
                if (b != null && b.Kind == BuffKind.Buff && b.IncomingDamageMultiplier <= heavy && !list.Contains(b)) list.Add(b);
            }
            return list;
        }
    }
}
