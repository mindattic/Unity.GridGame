using System;
using System.Collections.Generic;

namespace GridGame.Core.Content
{
    // ── Region rules (GG-T359, BIBLE §14.20) ────────────────────────────────────────────────────────────────────
    // A campaign theme or a stage names the board rules it plays under (campaign.json / stages.json "Rules"); each rule
    // is a regionrules.json record: the tile states it can paint on the board, the telegraphed events it can put on the
    // timeline, and the hooks that run its effects. The battle runs them in BattleRunner.Region.cs. Monster abilities
    // (AbilityDef.RuleEffects) and deaths (ClassDef.OnDeathRule) run the same effects, and a class can shrug a rule,
    // a tile state, an event or an ability off (ClassDef.RuleImmune). A rule's name and description are the Prose
    // "rule:<id>" description text (name + short), never JSON.

    /// <summary>When a region rule's hook runs.</summary>
    public enum RuleHook
    {
        /// <summary>Once, as the battle is set up (before wave 1 spawns).</summary>
        BattleStart,
        /// <summary>Each wave's spawn (wave 1 included), after its foes are on the board.</summary>
        WaveSpawn,
        /// <summary>A unit's own timeline turn begins (an enemy or independent ally's icon arrives; a hero's at each party window).</summary>
        TurnStart,
        /// <summary>A unit's own timeline turn has ended.</summary>
        TurnEnd,
        /// <summary>A unit moved into a tile (a step, a shove, a pull, a rule's own move).</summary>
        EnterTile,
        /// <summary>A unit fell.</summary>
        UnitDeath,
        /// <summary>A unit was struck by a foe's blow or ability and lives (GG-T368: a cracked floor cracks under the blow).</summary>
        Hit,
    }

    /// <summary>What one rule effect does.</summary>
    public enum RuleEffectKind
    {
        /// <summary>Paint <see cref="RuleEffectDef.State"/> on the area (counter starts at Amount). A blocking state skips occupied tiles.</summary>
        MarkTiles,
        /// <summary>Clear the area's marks (only <see cref="RuleEffectDef.State"/> when set).</summary>
        ClearTiles,
        /// <summary>Add Amount to the counter of the area's marks (only State when set); at the state's Threshold it becomes its Becomes state.</summary>
        ModifyTiles,
        /// <summary>Amount damage of DamageType to the units (environmental: no stagger, no attacker).</summary>
        Damage,
        /// <summary>Heal the units by Amount.</summary>
        Heal,
        /// <summary>Apply Buff to the units.</summary>
        ApplyBuff,
        /// <summary>Move the units up to Amount tiles (Move: toward/away from the Anchor or the area's first tile, or a direction).</summary>
        MoveUnits,
        /// <summary>Put Event on the timeline with the area as its telegraphed tiles, landing in Seconds (0 = the event's own).</summary>
        Schedule,
        /// <summary>Change the timers of every queued Event (all queued hazards when Event is null): Seconds later (negative = sooner), or Cancel.</summary>
        AdjustEvents,
        /// <summary>Set the named Anchor on the area's first tile.</summary>
        PlaceAnchor,
        /// <summary>Move the named Anchor Amount tiles (Move: a direction, or toward/away from the area's first tile).</summary>
        MoveAnchor,
        /// <summary>Raise a board-wide Flag (e.g. <see cref="RuleFlags.MaskTimeline"/>).</summary>
        SetFlag,
        /// <summary>Lower a board-wide Flag.</summary>
        ClearFlag,
    }

    /// <summary>Which tiles a rule effect works on.</summary>
    public enum RuleArea
    {
        /// <summary>The subject's tile (the unit whose turn/step/death it is, the caster), or where it fell.</summary>
        Self,
        /// <summary>An ability's anchor tile (falls back to Self).</summary>
        Target,
        /// <summary>The named <see cref="RuleEffectDef.Anchor"/>'s tile.</summary>
        Anchor,
        /// <summary>The telegraphed tiles of the event resolving (or the ability's shape).</summary>
        EventTiles,
        /// <summary>The listed <see cref="RuleEffectDef.Tiles"/>.</summary>
        Tiles,
        /// <summary>Row <see cref="RuleEffectDef.Index"/> (0 = top).</summary>
        Row,
        /// <summary>Column <see cref="RuleEffectDef.Index"/> (0 = left).</summary>
        Column,
        /// <summary><see cref="RuleEffectDef.Count"/> random tiles free for something new to appear, unmarked.</summary>
        RandomFree,
        /// <summary>The tiles of <see cref="RuleEffectDef.Count"/> random living units of <see cref="RuleEffectDef.Team"/>.</summary>
        RandomUnits,
        /// <summary>Every tile marked with <see cref="RuleEffectDef.State"/>.</summary>
        WithState,
        /// <summary>The whole board.</summary>
        All,
    }

    /// <summary>Which units a unit effect (Damage, Heal, ApplyBuff, MoveUnits) reaches; <see cref="RuleEffectDef.Team"/> filters them.</summary>
    public enum RuleUnits { OnArea, Subject, All }

    public enum RuleTeam { Any, Hero, Enemy }

    /// <summary>Which way MoveUnits/MoveAnchor go: toward/away from a point, or a board direction.</summary>
    public enum RuleMove { Toward, Away, Up, Down, Left, Right }

    /// <summary>Board-wide flags the battle and the view read (rules may raise any name; these have a meaning).</summary>
    public static class RuleFlags
    {
        /// <summary>The view masks enemy turn icons on the timeline (GG-T365's darkness builds on it).</summary>
        public const string MaskTimeline = "mask-timeline";
    }

    /// <summary>One effect a hook, an event, an ability or a death runs.</summary>
    public sealed class RuleEffectDef
    {
        public RuleEffectKind Kind;
        public RuleArea Area = RuleArea.Self;
        public List<Coord> Tiles = new List<Coord>();
        /// <summary>Row/Column index.</summary>
        public int Index;
        /// <summary>RandomFree/RandomUnits count.</summary>
        public int Count = 1;
        /// <summary>Grows the area by this Manhattan radius.</summary>
        public int Radius;
        public string Anchor;
        /// <summary>Tile state id (MarkTiles; the filter for ClearTiles, ModifyTiles and the WithState area).</summary>
        public string State;
        public RuleUnits Units = RuleUnits.OnArea;
        public RuleTeam Team = RuleTeam.Any;
        /// <summary>Damage, heal, counter delta, tiles moved; the starting counter for MarkTiles.</summary>
        public int Amount;
        public DamageType DamageType = DamageType.Physical;
        public string Buff;
        public RuleMove Move = RuleMove.Toward;
        public string Event;
        /// <summary>Schedule: seconds until it lands (0 = the event's own). AdjustEvents: seconds later (negative = sooner).</summary>
        public float Seconds;
        public bool Cancel;
        public string Flag;
        /// <summary>MarkTiles: leave a tile that already holds another state alone (after any elemental reaction), so a
        /// monster's trail never paints over a crack, a barricade or another rule's mark (GG-T370).</summary>
        public bool KeepMarks;
    }

    /// <summary>A persistent tile state a rule paints on the board (drawn on the tile; a mark lives until cleared or its LifeSeconds run out).</summary>
    public sealed class TileStateDef
    {
        public string Id;
        /// <summary>Nothing may enter, land on or spawn on it (a barricade, a collapsed floor). Never painted under someone.</summary>
        public bool Blocks;
        /// <summary>Harmful to stand on: the bot and the hero planner steer clear (immune units excepted).</summary>
        public bool Hazard;
        /// <summary>Counter value at which it turns into <see cref="Becomes"/> (0 = never).</summary>
        public int Threshold;
        /// <summary>The state it becomes at <see cref="Threshold"/>; null = the mark clears.</summary>
        public string Becomes;
        /// <summary>Clock seconds the mark lasts (0 = until cleared).</summary>
        public float LifeSeconds;
        /// <summary>Hex colour of the tile wash.</summary>
        public string Color;
        /// <summary>A short glyph drawn on the tile.</summary>
        public string Glyph;
        /// <summary>A hole (GG-T368; needs <see cref="Blocks"/>): a unit caught on the tile as it opens, or knocked or pulled
        /// into it, falls: it loses <see cref="FallDamage"/> of its max HP and climbs out on the nearest free tile.</summary>
        public bool Pit;
        /// <summary>The share of max HP a fall into this <see cref="Pit"/> costs (environmental, at least 1).</summary>
        public float FallDamage = 0.15f;
        /// <summary>Effects run on each tile as it takes this state (painted, or a counter turning into it): Self is that
        /// tile, the unit standing there (if any) the subject. A crumbling floor schedules its own cave-in this way.</summary>
        public List<RuleEffectDef> OnMark = new List<RuleEffectDef>();
        /// <summary>The element this state carries (GG-T370: fire, ice). Painted onto a tile holding a state that its
        /// elements.json row reacts to (GG-T371 <see cref="ElementDef.Tiles"/>: fire melts ice, ice douses fire, fire burns
        /// slime away, fire sets a mine off), the reaction runs on that tile and the incoming state is spent.</summary>
        public DamageType? Element;
    }

    /// <summary>A telegraphed event a rule puts on the timeline: its own icon rides the bar like a cast and resolves its Effects on arrival.</summary>
    public sealed class RuleEventDef
    {
        public string Id;
        /// <summary>Seconds from queued to landing (a Schedule may override it).</summary>
        public float Seconds = 4f;
        /// <summary>Re-arms for the same Seconds and tiles after it lands (a rule's heartbeat).</summary>
        public bool Repeat;
        /// <summary>Its telegraphed tiles are dangerous (drawn as a warning; the bot steps off them).</summary>
        public bool Hazard = true;
        /// <summary>Glyph on its timeline icon and its warning tiles.</summary>
        public string Glyph;
        public string Color;
        /// <summary>Optional UI icon name (Resources/Icons) for the timeline diamond.</summary>
        public string Icon;
        public List<RuleEffectDef> Effects = new List<RuleEffectDef>();
    }

    /// <summary>A hook: when <see cref="On"/> happens (and the filters hold), run the effects.</summary>
    public sealed class RuleHookDef
    {
        public RuleHook On;
        /// <summary>Whose turn/step/death it must be.</summary>
        public RuleTeam Team = RuleTeam.Any;
        /// <summary>The subject must stand on this tile state (TurnStart/TurnEnd/EnterTile/UnitDeath).</summary>
        public string State;
        /// <summary>Only in this wave (0-based); -1 = every wave.</summary>
        public int Wave = -1;
        /// <summary>Runs every Nth time it matches (1 = every time).</summary>
        public int Every = 1;
        public float Chance = 1f;
        public List<RuleEffectDef> Effects = new List<RuleEffectDef>();
    }

    /// <summary>One region rule (regionrules.json).</summary>
    public sealed class RegionRuleDef
    {
        public string Id;
        /// <summary>From Prose ("rule:&lt;id&gt;" payload "name"); never authored in JSON.</summary>
        public string Name;
        /// <summary>From Prose ("rule:&lt;id&gt;" payload "short"); never authored in JSON.</summary>
        public string Description;
        public List<TileStateDef> TileStates = new List<TileStateDef>();
        public List<RuleEventDef> Events = new List<RuleEventDef>();
        public List<RuleHookDef> Hooks = new List<RuleHookDef>();
    }

    public static class RegionRuleCatalog
    {
        /// <summary>The rules a stage plays under: its theme's (campaign.json), then its own (stages.json), each once.</summary>
        public static List<RegionRuleDef> ActiveFor(ContentDb db, StageDef stage)
        {
            var list = new List<RegionRuleDef>();
            if (db == null || stage == null || db.RegionRules.Count == 0) return list;
            var theme = db.ThemeOf(stage.Id) ?? db.Campaign.Themes.Find(t => string.Equals(t.Id, stage.Theme, StringComparison.OrdinalIgnoreCase));
            void Add(List<string> ids)
            {
                if (ids == null) return;
                foreach (var id in ids)
                    if (db.TryRegionRule(id) is RegionRuleDef r && !list.Contains(r)) list.Add(r);
            }
            Add(theme?.Rules);
            Add(stage.Rules);
            return list;
        }

        /// <summary>Indexes every rule's tile states and events by id (ids are unique across all rules; Validate reports a clash).</summary>
        public static void Index(ContentDb db, List<RegionRuleDef> rules)
        {
            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) throw new FormatException("RegionRuleDef with empty Id");
                if (db.RegionRules.ContainsKey(r.Id)) throw new FormatException($"Duplicate RegionRuleDef id '{r.Id}'");
                db.RegionRules[r.Id] = r;
                db.RegionRuleOrder.Add(r);
                foreach (var t in r.TileStates) if (t?.Id != null && !db.TileStates.ContainsKey(t.Id)) { db.TileStates[t.Id] = t; db.TileStateRule[t.Id] = r.Id; }
                foreach (var e in r.Events) if (e?.Id != null && !db.RuleEvents.ContainsKey(e.Id)) { db.RuleEvents[e.Id] = e; db.RuleEventRule[e.Id] = r.Id; }
            }
        }

        /// <summary>Cross-reference check of regionrules.json and every reference to it (themes, stages, classes, abilities).</summary>
        public static void Validate(ContentDb db, List<string> p)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in db.RegionRuleOrder)
            {
                string w = $"region rule {r.Id}";
                foreach (var t in r.TileStates)
                {
                    if (t == null || string.IsNullOrEmpty(t.Id)) { p.Add($"{w}: a tile state needs an Id"); continue; }
                    if (!seen.Add(t.Id)) p.Add($"{w}: tile state id '{t.Id}' is used twice (ids are shared by every rule, event and state)");
                    if (t.Threshold < 0) p.Add($"{w} state {t.Id}: Threshold can't be negative");
                    if (t.Becomes != null && db.TryTileState(t.Becomes) == null) p.Add($"{w} state {t.Id}: unknown Becomes state '{t.Becomes}'");
                    if (t.Becomes != null && t.Threshold <= 0) p.Add($"{w} state {t.Id}: Becomes needs a Threshold");
                    if (!(t.LifeSeconds >= 0f)) p.Add($"{w} state {t.Id}: LifeSeconds can't be negative");
                    if (t.Pit && !t.Blocks) p.Add($"{w} state {t.Id}: a Pit must also Block (nothing stands in a hole)");
                    if (t.Pit && !(t.FallDamage >= 0f && t.FallDamage <= 1f)) p.Add($"{w} state {t.Id}: FallDamage outside 0..1");
                    ValidateEffects(db, t.OnMark, $"{w} state {t.Id} OnMark", p);
                }
                foreach (var e in r.Events)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) { p.Add($"{w}: an event needs an Id"); continue; }
                    if (!seen.Add(e.Id)) p.Add($"{w}: event id '{e.Id}' is used twice (ids are shared by every rule, event and state)");
                    if (!(e.Seconds > 0f)) p.Add($"{w} event {e.Id}: Seconds must be positive (every hazard shows on the timeline ahead of time)");
                    if (e.Effects.Count == 0) p.Add($"{w} event {e.Id}: no Effects");
                    ValidateEffects(db, e.Effects, $"{w} event {e.Id}", p);
                }
                if (r.Hooks.Count == 0 && r.Events.Count == 0) p.Add($"{w}: no Hooks (nothing would ever run it)");
                for (int i = 0; i < r.Hooks.Count; i++)
                {
                    var h = r.Hooks[i];
                    string hw = $"{w} hook {i + 1} ({h?.On})";
                    if (h == null) { p.Add($"{w}: null hook"); continue; }
                    if (h.Every < 1) p.Add($"{hw}: Every must be at least 1");
                    if (!(h.Chance > 0f && h.Chance <= 1f)) p.Add($"{hw}: Chance outside (0, 1]");
                    if (h.State != null && db.TryTileState(h.State) == null) p.Add($"{hw}: unknown tile state '{h.State}'");
                    if (h.State != null && (h.On == RuleHook.BattleStart || h.On == RuleHook.WaveSpawn)) p.Add($"{hw}: a State filter needs a subject (TurnStart, TurnEnd, EnterTile, UnitDeath)");
                    if (h.Effects.Count == 0) p.Add($"{hw}: no Effects");
                    ValidateEffects(db, h.Effects, hw, p);
                }
            }
            foreach (var t in db.Campaign.Themes) NeedRules(db, t.Rules, $"theme {t.Id}", p);
            foreach (var s in db.StageOrder) NeedRules(db, s.Rules, $"stage {s.Id}", p);
            foreach (var c in db.Classes.Values)
            {
                foreach (var id in c.RuleImmune)
                    if (!IsRuleSource(db, id)) p.Add($"class {c.Id}: RuleImmune '{id}' is no region rule, tile state, rule event or ability");
                ValidateEffects(db, c.OnDeathRule, $"class {c.Id} OnDeathRule", p);
                ValidateEffects(db, c.TrailRule, $"class {c.Id} TrailRule", p);
                if (c.TrailEvery < 1) p.Add($"class {c.Id}: TrailEvery must be at least 1");
            }
            foreach (var a in db.Abilities.Values) ValidateEffects(db, a.RuleEffects, $"ability {a.Id} RuleEffects", p);
        }

        private static void NeedRules(ContentDb db, List<string> ids, string where, List<string> p)
        {
            if (ids == null) return;
            var once = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (db.TryRegionRule(id) == null) p.Add($"{where}: unknown region rule '{id}'");
                else if (!once.Add(id)) p.Add($"{where}: region rule '{id}' listed twice");
            }
        }

        /// <summary>An id a class may be immune to: a rule, a tile state, a rule event, or an ability (its RuleEffects).</summary>
        public static bool IsRuleSource(ContentDb db, string id) =>
            id != null && (db.TryRegionRule(id) != null || db.TryTileState(id) != null || db.TryRuleEvent(id) != null || db.TryAbility(id) != null);

        public static void ValidateEffects(ContentDb db, List<RuleEffectDef> effects, string where, List<string> p)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                string w = $"{where} effect {i + 1}";
                if (e == null) { p.Add($"{w}: null"); continue; }
                w += $" ({e.Kind})";
                if (e.State != null && db.TryTileState(e.State) == null) p.Add($"{w}: unknown tile state '{e.State}'");
                if (e.Event != null && db.TryRuleEvent(e.Event) == null) p.Add($"{w}: unknown rule event '{e.Event}'");
                if (e.Buff != null && db.TryBuff(e.Buff) == null) p.Add($"{w}: unknown buff '{e.Buff}'");
                if (e.Radius < 0 || e.Radius > 8) p.Add($"{w}: Radius outside 0..8");
                switch (e.Area)
                {
                    case RuleArea.Tiles: if (e.Tiles.Count == 0) p.Add($"{w}: area Tiles lists none"); break;
                    case RuleArea.Anchor: if (string.IsNullOrEmpty(e.Anchor)) p.Add($"{w}: area Anchor needs an Anchor name"); break;
                    case RuleArea.WithState: if (e.State == null) p.Add($"{w}: area WithState needs a State"); break;
                    case RuleArea.RandomFree:
                    case RuleArea.RandomUnits: if (e.Count < 1) p.Add($"{w}: Count must be at least 1"); break;
                    case RuleArea.Row: if (e.Index < 0 || e.Index >= Battle.BattleConfig.Rows) p.Add($"{w}: Row {e.Index} is off the board"); break;
                    case RuleArea.Column: if (e.Index < 0 || e.Index >= Battle.BattleConfig.Cols) p.Add($"{w}: Column {e.Index} is off the board"); break;
                }
                switch (e.Kind)
                {
                    case RuleEffectKind.MarkTiles: if (e.State == null) p.Add($"{w}: needs a State"); break;
                    case RuleEffectKind.Damage:
                    case RuleEffectKind.Heal: if (e.Amount <= 0) p.Add($"{w}: Amount must be positive"); break;
                    case RuleEffectKind.ApplyBuff: if (e.Buff == null) p.Add($"{w}: needs a Buff"); break;
                    case RuleEffectKind.MoveUnits:
                    case RuleEffectKind.MoveAnchor:
                        if (e.Amount <= 0) p.Add($"{w}: Amount (tiles) must be positive");
                        if (e.Kind == RuleEffectKind.MoveAnchor && string.IsNullOrEmpty(e.Anchor)) p.Add($"{w}: needs an Anchor");
                        break;
                    case RuleEffectKind.Schedule:
                        if (e.Event == null) p.Add($"{w}: needs an Event");
                        if (e.Seconds < 0f) p.Add($"{w}: Seconds can't be negative");
                        break;
                    case RuleEffectKind.AdjustEvents: if (!e.Cancel && e.Seconds == 0f) p.Add($"{w}: needs Seconds or Cancel"); break;
                    case RuleEffectKind.PlaceAnchor: if (string.IsNullOrEmpty(e.Anchor)) p.Add($"{w}: needs an Anchor"); break;
                    case RuleEffectKind.SetFlag:
                    case RuleEffectKind.ClearFlag: if (string.IsNullOrEmpty(e.Flag)) p.Add($"{w}: needs a Flag"); break;
                }
            }
        }
    }
}
