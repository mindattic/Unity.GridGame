using System.Collections.Generic;

namespace GridGame.Core.Content
{
    // Plain data definitions deserialized from Assets/Gaiden/Resources/Content/*.json.
    // Property names are the JSON keys (PascalCase); enums serialize as strings.

    /// <summary>Neutral (GG-T132): a recruitable bystander on the board — neither side fights it or counts it; a hero who
    /// meets its <see cref="RecruitableDef.Conditions"/> and talks to it flips it to Hero (a guest ally).</summary>
    public enum Team { Hero, Enemy, Neutral }

    /// <summary>How an enemy chooses which hero to go after (bible §14.1). Auto derives it from INT and tags.</summary>
    public enum Targeting { Auto, Closest, Weakest, Threat, Squishy, Erratic, Caster }

    /// <summary>
    /// How an enemy moves and decides (bible §14.6, owner direction 2026-10-11: "ai for every single enemy unit, they are
    /// all different in their own unique ways"). Combined with its targeting personality and kit, every family plays
    /// differently.
    /// </summary>
    public enum Brain
    {
        Hunter, Pack, Ambusher, Skirmisher, Kiter, Guardian, Berserker, Scavenger, Trapper, Sluggish,
        Hopper, Blinker, Burrower, Bomber, Medic, Commander, Duelist, Stalker, Sentinel, Relentless,
    }


    public enum DamageType { Physical, Fire, Ice, Lightning, Poison, Holy, Dark, Arcane }

    /// <summary>The three ability kinds (owner design, GG-T296): Skill/Spell/Item are <i>Active</i> (you take them);
    /// <c>Passive</c> just happens (an always-on modifier, or an "every time I…" effect); <c>Reactive</c> fires after
    /// something happens <i>to</i> you, usually on a chance. Passive and Reactive abilities carry a
    /// <see cref="AbilityDef.Trigger"/> that the battle's trigger engine evaluates (§4.1).</summary>
    public enum AbilityKind { Skill, Spell, Item, Passive, Reactive }

    /// <summary>When a <see cref="TriggerDef"/> is evaluated (§4.1, GG-T296), fired by the battle funnels in a fixed order.</summary>
    public enum TriggerOn
    {
        /// <summary>Not an event: an always-on modifier (DamageMultiplier, ReductionMultiplier, Immunity), checked where the number is computed.</summary>
        Always,
        /// <summary>The owner makes a primary weapon attack (as a pincer attacker, or an enemy's melee swing), hit or miss. Context: Target.</summary>
        OnAttack,
        /// <summary>A weapon blow by the owner lands (any blow: pincer, supporter, counter, melee, ranged). Context: Target, Damage.</summary>
        OnLandedHit,
        /// <summary>The owner loses HP to anything (Damage &gt; 0; Attacker is null for a poison tick or a blast).</summary>
        OnDamaged,
        /// <summary>A foe's weapon blow from an adjacent tile lands on the owner (not a miss). Context: Attacker, Damage.</summary>
        OnAttackedDirect,
        /// <summary>Any foe's blow or ability lands on the owner, from any range (not a miss). Context: Attacker, Damage.</summary>
        OnAttackedAny,
        /// <summary>A foe's weapon blow misses the owner (the dodge hook GG-T297 builds on). Context: Attacker.</summary>
        OnDodge,
        /// <summary>At the owner's turn start, at least <see cref="TriggerDef.Count"/> foes stand adjacent.</summary>
        OnSurrounded,
        /// <summary>The owner gains status <see cref="TriggerDef.Status"/> (any status when empty).</summary>
        OnStatusGained,
        /// <summary>A hit drops the owner from at/above <see cref="TriggerDef.Threshold"/> of its HP to below it, and it lives.</summary>
        OnLowHp,
        /// <summary>An ally of the owner falls. Context: Target = the fallen ally, Attacker = its killer.</summary>
        OnAllyKO,
        /// <summary>The owner fells a foe. Context: Target = the fallen foe.</summary>
        OnKill,
        /// <summary>The owner was one of a pincer's two attackers, after their blows. Context: Target = a trapped foe.</summary>
        OnPincerAsAttacker,
        /// <summary>The owner supported a pincer, after its support action (GG-T298 builds supporter effects on it). Context: Target = the attacker it backed.</summary>
        OnPincerAsSupporter,
        /// <summary>The owner's turn begins (an enemy's own turn; for heroes, each time control returns to the party).</summary>
        TurnStart,
        /// <summary>The owner's turn ends (an enemy's own turn; for heroes, each time control returns to the party).</summary>
        TurnEnd,
        /// <summary>The owner just finished an action (the hero whose command it was; an enemy at the end of its turn).</summary>
        ActionEnd,
    }

    /// <summary>What a fired trigger does (§4.1, GG-T296).</summary>
    public enum TriggerEffectKind
    {
        /// <summary>Heal Who (default Self) Amount × its MaxHP.</summary>
        Heal,
        /// <summary>Heal the owner Amount × the damage in the context.</summary>
        Lifesteal,
        /// <summary>Apply <see cref="TriggerEffect.Buff"/> to Who (default Self).</summary>
        ApplyBuff,
        /// <summary>A weapon blow at ×Amount on Who (default the attacker), only when it's a foe within the owner's reach.</summary>
        Counterstrike,
        /// <summary>Strike the context's Target again at ×Amount of the blow that fired it (Double Attack).</summary>
        FollowUp,
        /// <summary>Resolve Active ability <see cref="TriggerEffect.Ability"/> through the normal dispatcher, instantly and free,
        /// at Who (default the attacker, else the target, else Self).</summary>
        CastAbility,
        /// <summary>Deal Amount × the context's damage back to the attacker (Thorns).</summary>
        Reflect,
        /// <summary>Remove every debuff from Who (default Self).</summary>
        Cleanse,
        /// <summary>Add Amount charge to the party's mana meter (hero owners only).</summary>
        ManaCharge,
        /// <summary>The Pushback keyword on Who (default the attacker, else the target): Amount seconds, foes only.</summary>
        Pushback,
        /// <summary>Always-on: the owner's weapon blows deal ×Amount (while the condition holds).</summary>
        DamageMultiplier,
        /// <summary>Always-on: damage the owner takes is ×Amount (while the condition holds; Condition.DamageType narrows it).</summary>
        ReductionMultiplier,
        /// <summary>Always-on: the owner can't gain any of <see cref="TriggerEffect.Statuses"/>.</summary>
        Immunity,
    }

    /// <summary>Whom an effect lands on. Default picks the effect kind's natural target (see <see cref="TriggerEffectKind"/>).</summary>
    public enum EffectTarget { Default, Self, Target, Attacker }

    /// <summary>
    /// The data half of a Passive or Reactive ability (§4.1, GG-T296): <see cref="On"/> an event, roll <see cref="Chance"/>,
    /// check <see cref="Condition"/>, then run every <see cref="Effects"/> entry. Pure data validated by
    /// <see cref="ContentLoader.Validate"/>; the battle's trigger engine is the only code that reads it, so a new passive
    /// or reactive is a new abilities.json row (GG-LAW-5).
    /// </summary>
    public sealed class TriggerDef
    {
        public TriggerOn On;
        /// <summary>0..1; 1 fires every time without touching the Rng.</summary>
        public float Chance = 1f;
        /// <summary>Added to Chance per point of the owner's Luck (optional).</summary>
        public float ChancePerLuck;
        /// <summary>OnSurrounded: how many adjacent foes.</summary>
        public int Count;
        /// <summary>OnLowHp: the HP fraction (0..1) crossed.</summary>
        public float Threshold;
        /// <summary>OnStatusGained: the buff id (empty = any).</summary>
        public string Status;
        public TriggerCondition Condition;
        public List<TriggerEffect> Effects = new List<TriggerEffect>();
        /// <summary>Once fired, the owner's turns before it can fire again (0 = none).</summary>
        public int Cooldown;
        /// <summary>Fires at most once per action (a command, or one enemy turn).</summary>
        public bool OncePerAction;
        /// <summary>The float shown when it fires; empty = the ability's name.</summary>
        public string Callout;
    }

    /// <summary>A small predicate vocabulary (§4.1): every set field must hold. "Other" is the context's attacker, else its target.</summary>
    public sealed class TriggerCondition
    {
        /// <summary>Owner HP fraction strictly below this (0 = unchecked).</summary>
        public float SelfHpBelow;
        /// <summary>Owner HP fraction strictly above this (0 = unchecked).</summary>
        public float SelfHpAbove;
        public string HasStatus;
        public string LacksStatus;
        /// <summary>At least this many foes adjacent to the owner (0 = unchecked).</summary>
        public int AdjacentFoesAtLeast;
        /// <summary>The other unit carries this class tag (Undead, Beast…).</summary>
        public string OtherTag;
        /// <summary>GG-T338: the other unit carries this status (the Ember Executioner's blows on a [Burning] hero).</summary>
        public string OtherHasStatus;
        /// <summary>GG-T340: the other unit's HP fraction is strictly below this (0 = unchecked) — the Hellspawn's
        /// Bloodscent, its blows on a hero already bleeding.</summary>
        public float OtherHpBelow;
        /// <summary>The damage in the context is of this type (null = any).</summary>
        public DamageType? DamageType;
    }

    public sealed class TriggerEffect
    {
        public TriggerEffectKind Kind;
        /// <summary>Fraction, multiplier, seconds or charge, by kind.</summary>
        public float Amount;
        public EffectTarget Who;
        /// <summary>ApplyBuff: the buff id.</summary>
        public string Buff;
        /// <summary>CastAbility: the Active ability id.</summary>
        public string Ability;
        /// <summary>Immunity: the buff ids warded off.</summary>
        public List<string> Statuses = new List<string>();
    }

    public enum TargetShape { Self, SingleActor, SingleTile, Square, Diamond, Cross, Plus, Row, Column, AllEnemies, AllAllies }

    public enum TargetMode { Auto, PickActor, PickTile }

    public enum TargetFilter { Any, EnemyOnly, AllyOnly, EmptyOnly }

    public enum ProjectileMotion { None, Straight, Bezier, Homing, Spiral, Twist, Strike }

    public enum BuffKind { Buff, Debuff }

    /// <summary>What happens when a buff is applied to a unit that already has it (bible §8.6).</summary>
    public enum BuffStacking { Refresh, Stack, Ignore, Upgrade }

    public enum DurationUnit { Turns, Ticks }

    /// <summary>Tome (GG-T301): teaches <see cref="ItemDef.TeachesAbility"/>, a Passive or Reactive, to a chosen hero.
    /// Chest (GG-T301): opened from the bag, it rolls <see cref="ItemDef.OpensTable"/> (a droptables.json row).</summary>
    public enum ItemType { Equipment, Consumable, CraftingMaterial, QuestItem, RecipeScroll, Tome, Chest }

    public enum ItemRarity { Junk, Common, Uncommon, Rare, Epic, Legendary }

    /// <summary>
    /// Every equip slot (§24.2, GG-T89). Weapon and the five armor pieces (Head/Torso/Legs/Gloves/Boots) each
    /// hold exactly one exact-matching item (<see cref="ItemDef.Slot"/> == the slot). Necklace is the same
    /// (one exact slot). Earring1/Earring2 and Ring1/Ring2 are *instances* of a slot category: any item flagged
    /// <see cref="ItemDef.IsEarring"/>/<see cref="ItemDef.IsRing"/> fits either of its pair, the same way any
    /// <c>IsRelic</c> item used to fit any of the old Relic1-3.
    /// </summary>
    public enum EquipmentSlot { None, Weapon, Head, Torso, Legs, Gloves, Boots, Necklace, Earring1, Earring2, Ring1, Ring2 }

    /// <summary>A weapon's proficiency category (GG-T52). Every hero can always wield <c>Simple</c> weapons;
    /// <c>Martial</c> and <c>Arcane</c> are gated by <see cref="ClassDef.WeaponProficiencies"/> (the class's
    /// own home-tree training) or an earned proficiency-unlock passive (Martial Weapons / Arcane Focus).</summary>
    public enum WeaponClass { Simple, Martial, Arcane }

    /// <summary>An armor piece's weight class (GG-T52), ordered light-to-heavy so a hero's allowance can be
    /// compared with <![CDATA[<=]]>. Only <c>Heavy</c> is ever actually gated today — everyone can wear Light
    /// and Medium regardless of class (see <see cref="ClassDef.ArmorProficiency"/>); Heavy needs the class's
    /// own training or the Heavy Armor Proficiency passive.</summary>
    public enum ArmorWeight { Light, Medium, Heavy }

    public enum CraftStation { Forge, Alchemy, Enchant }

    public enum ChargeShape { Single, Line }

    public sealed class Stats
    {
        public float Strength;
        public float Vitality;
        public float Agility;
        public float Speed;
        public float Stamina;
        public float Intelligence;
        public float Wisdom;
        public float Luck;

        public Stats Clone() => (Stats)MemberwiseClone();

        public static Stats operator +(Stats a, Stats b) => new Stats
        {
            Strength = a.Strength + b.Strength,
            Vitality = a.Vitality + b.Vitality,
            Agility = a.Agility + b.Agility,
            Speed = a.Speed + b.Speed,
            Stamina = a.Stamina + b.Stamina,
            Intelligence = a.Intelligence + b.Intelligence,
            Wisdom = a.Wisdom + b.Wisdom,
            Luck = a.Luck + b.Luck,
        };

        public static Stats operator *(Stats a, float k) => new Stats
        {
            Strength = a.Strength * k,
            Vitality = a.Vitality * k,
            Agility = a.Agility * k,
            Speed = a.Speed * k,
            Stamina = a.Stamina * k,
            Intelligence = a.Intelligence * k,
            Wisdom = a.Wisdom * k,
            Luck = a.Luck * k,
        };
    }

    public sealed class MilestoneGrowth
    {
        public int Level;
        public Stats Growth = new Stats();
    }

    public sealed class Footprint
    {
        public int W = 1;
        public int H = 1;
    }

    /// <summary>
    /// Where a cut-in's "bust" close-up (BattleScreen.CutIns.ActionCard) should anchor on this class's
    /// full-body Portrait, as a 0-100 position on each axis (CSS background-position style: X 50/Y 0 is
    /// the default — horizontally centred, anchored to the top — which is right for a front-facing
    /// humanoid but wrong for an off-centre pose, e.g. a wolf sitting in profile with its head to one
    /// side). Null (the common case) means "use the default." Owner direction 2026-10-11: "make sure
    /// their eyes are in the portrait."
    /// </summary>
    public sealed class PortraitCrop
    {
        public float X = 50f;
        public float Y;
    }

    /// <summary>
    /// GG-T220: a unit's thumbnail is a window onto its one real portrait, never a separate image (owner 2026-11-03:
    /// "it always uses the real existing portrait and simply zooms in and offsets … no duplicating assets").
    /// (<see cref="X"/>, <see cref="Y"/>) is the portrait point, as fractions of its width and height from the top-left,
    /// that sits at the thumbnail's centre; <see cref="Zoom"/> is portrait width ÷ thumbnail side (1 = the whole width
    /// fills the square). Framed in tools/ThumbnailStudio.
    /// </summary>
    public sealed class Thumbnail
    {
        public const float MinZoom = 0.25f, MaxZoom = 20f, MinOffset = -0.5f, MaxOffset = 1.5f;
        public float Zoom = 1f;
        public float X = 0.5f;
        public float Y = 0.5f;
    }

    /// <summary>
    /// GG-T352: where the figure sits on its portrait. Owner 2026-11-07: "all characters are 1024x1024 with transparent
    /// backgrounds; that's the rule, that's what the assets are", so a portrait is the whole canvas, margins included.
    /// This is the figure's box on it (the alpha bounds, written by tools/import_art.py when the portrait is imported), as
    /// fractions of the portrait's width and height from its top-left. Whole-figure views (dialogue, cut-ins, cards,
    /// the death dissolve) draw only this box, so a figure reads at the same size whatever its canvas margins;
    /// thumbnails frame the whole canvas through <see cref="Thumbnail"/>.
    /// </summary>
    public sealed class Figure
    {
        public float X;
        public float Y;
        public float W = 1f;
        public float H = 1f;

        /// <summary>A box with some area, inside the portrait.</summary>
        public bool IsValid => W > 0f && H > 0f && X >= 0f && Y >= 0f && X + W <= 1.0001f && Y + H <= 1.0001f;
    }

    public sealed class BossPhaseDef
    {
        /// <summary>Phase begins when HP fraction drops to or below this (0..1).</summary>
        public float HpThreshold;
        public bool PrefersCharge;
        /// <summary>Ability fired once on phase entry (e.g. Quicken on self = enrage). Optional.</summary>
        public string OnEnterAbility;
        public string Announce;
    }

    /// <summary>What a supporter does when it joins a pincer (§1.2.3, GG-T187).</summary>
    public enum SupportEffect
    {
        /// <summary>Strike every trapped opponent at <see cref="SupportDef.Magnitude"/> × damage (the classic 50% support hit).</summary>
        Strike,
        /// <summary>Heal each target <see cref="SupportDef.Magnitude"/> × its max HP.</summary>
        Heal,
        /// <summary>Apply buff <see cref="SupportDef.Buff"/> to each target (ATK up, shield, regen, taunt…).</summary>
        Buff,
        /// <summary>Each target acts <see cref="SupportDef.Magnitude"/> seconds sooner (its turn icon slides forward, or its
        /// cast is rushed; a hero's party also gains tempo). Seconds since GG-T298 (was a fraction of the bar).</summary>
        Haste,
        /// <summary>Add <see cref="SupportDef.Magnitude"/> charge to the mana meter (100 = one orb). Hero team only.</summary>
        Mana,
        /// <summary>Strip every debuff from each target.</summary>
        Cleanse,
        /// <summary>GG-T298: the attacker the supporter is chained to gains +<see cref="SupportDef.Magnitude"/> crit chance
        /// (0.2 = +20 points) on its blows in this pincer. Resolves before the attackers strike.</summary>
        Crit,
        /// <summary>GG-T298: the supporter casts a random damaging spell from its own kit (no mana, no cast time) on every
        /// trapped opponent at <see cref="SupportDef.Magnitude"/> × its power. Nothing happens if its kit has none.</summary>
        Cast,
        /// <summary>GG-T298: each trapped opponent gains status <see cref="SupportDef.Buff"/> with chance
        /// <see cref="SupportDef.Magnitude"/> (poison, blind, slow, silence, burn…).</summary>
        Status,
        /// <summary>GG-T298: <see cref="SupportDef.Magnitude"/> extra seconds of Pushback on every trapped opponent, folded
        /// into the pincer's own push (one push per foe per action, capped at PushbackMaxSeconds).</summary>
        Pushback,
    }

    /// <summary>Who a supporter's effect lands on (§1.2.3, GG-T187). Strike, Cast, Status and Pushback always hit the
    /// trapped opponents; Crit always goes to the attacker the supporter is chained to.</summary>
    public enum SupportTarget
    {
        /// <summary>The pincer attacker the supporter's chain reaches.</summary>
        Attacker,
        /// <summary>Every ally on the chain from the supporter to that attacker (both ends included).</summary>
        Chain,
        /// <summary>Every living member of the supporter's team.</summary>
        Party,
        /// <summary>The supporter itself (taunt).</summary>
        Self,
    }

    /// <summary>A class's support action (§1.2.3, GG-T187): what it does when it joins a pincer as a supporter.
    /// Classes without one (every enemy) use <see cref="Default"/>, the classic 50% strike from beside an attacker.</summary>
    public sealed class SupportDef
    {
        public SupportEffect Effect = SupportEffect.Strike;
        /// <summary>Strike: damage fraction. Heal: fraction of the target's max HP. Haste: seconds sooner. Mana: charge.
        /// Crit: crit chance added (0.2 = +20 points). Cast: spell power fraction. Status: chance 0..1. Pushback: seconds.</summary>
        public float Magnitude = 0.5f;
        public SupportTarget Target = SupportTarget.Attacker;
        /// <summary>How many links along the chain of allies the supporter may stand from the attacker (1 = adjacent).</summary>
        public int Range = 1;
        /// <summary>Buff id applied by <see cref="SupportEffect.Buff"/>, or the status inflicted by <see cref="SupportEffect.Status"/>.</summary>
        public string Buff;
        /// <summary>Short name shown on the cut-in and float text ("Mend", "Guard", "Haste"…); defaults to the effect.</summary>
        public string Label;

        public static readonly SupportDef Default = new SupportDef();

        /// <summary>Largest Haste a support may give, in seconds (GG-T298).</summary>
        public const float MaxHasteSeconds = 3f;
        /// <summary>Largest crit chance one Crit support may add (0.5 = +50 points), and the cap on the attacker's total crit
        /// percent while supported (GG-T298).</summary>
        public const float MaxCritBonus = 0.5f;
        public const float MaxSupportedCritPercent = 75f;
        /// <summary>Largest spell power fraction a Cast support may use (GG-T298).</summary>
        public const float MaxCastPower = 1f;

        public string DisplayLabel => string.IsNullOrEmpty(Label) ? Effect.ToString() : Label;
    }

    public sealed class ClassDef
    {
        public string Id;
        public string Name;
        public Team Team;
        public List<string> Tags = new List<string>();
        public string Description;
        public string Lore;
        public string Card;
        public string Portrait;
        public PortraitCrop Crop;
        public Thumbnail Thumbnail;
        /// <summary>The figure's box on the portrait (GG-T352).</summary>
        public Figure Figure;
        public Footprint Footprint = new Footprint();
        public Stats BaseStats = new Stats();
        public Stats Growth = new Stats();
        public List<MilestoneGrowth> Milestones = new List<MilestoneGrowth>();
        public Dictionary<DamageType, float> Resistances = new Dictionary<DamageType, float>();
        /// <summary>Abilities this class can learn/use (hero) or uses (enemy charge / trap).</summary>
        public List<string> Abilities = new List<string>();
        /// <summary>Default ability bar preset (ability ids; items as "item:id").</summary>
        public List<string> DefaultLoadout = new List<string>();
        public string DropTable;
        public int MoveRange = 1;
        /// <summary>Trap id left on every tile this enemy steps off (slime trail, mire, quicksand — §14.4).</summary>
        public string Trail;
        /// <summary>Buff a landed enemy attack may inflict (venom, frost, blinding dirt — §14.4)…</summary>
        public string OnHitBuff;
        /// <summary>…with this chance per landed hit.</summary>
        public float OnHitChance;
        /// <summary>Class id of the remains this enemy leaves (gravestone, bone pile — §14.5). Destroy them or it rises again.</summary>
        public string Remains;
        /// <summary>Class id of the egg this enemy lays periodically (§14.8). Optional.</summary>
        public string LaysEgg;
        /// <summary>Lay an egg every Nth own turn (only used when LaysEgg is set).</summary>
        public int EggInterval = 4;
        /// <summary>Damage dealt to every adjacent hero when this enemy dies (cyber-zombie self-destruct). 0 = none.</summary>
        public int DeathBurst;
        /// <summary>Enemy targeting personality (§14.1).</summary>
        public Targeting Targeting = Targeting.Auto;
        /// <summary>Armor plate HP on the side it faces (§13.5); 0 = none. Physical blows from that side dent the plate, not the body.</summary>
        public int ArmorPlate;
        /// <summary>Movement / decision style (§14.6).</summary>
        public Brain Brain = Brain.Hunter;
        /// <summary>Straight-line attack reach for the Ranged archetype (1 = melee only).</summary>
        public int AttackRange = 1;
        /// <summary>Buff ids this class shrugs off ("Immune!") — the Mechanical archetype.</summary>
        public List<string> ImmuneBuffs = new List<string>();
        public int XpReward = 10;
        public List<BossPhaseDef> BossPhases = new List<BossPhaseDef>();
        /// <summary>Enemy charge spell id (Caster archetype). Optional.</summary>
        public string ChargeAbility;
        public ChargeShape ChargeShape = ChargeShape.Single;
        /// <summary>Trap id laid by trap-layers. Optional.</summary>
        public string Trap;
        /// <summary>Snake body segments grown behind this head at spawn (0 = not a snake).</summary>
        public int SnakeSegments;
        /// <summary>Hero role (§25.13): Tank, Healer, Mage, Striker or Support — what the class is for in a party.</summary>
        public string Role;
        /// <summary>Support action (§1.2.3, GG-T187) this class performs when chained to a pincer attacker; null = the
        /// default 50% strike from an adjacent tile (<see cref="SupportDef.Default"/>).</summary>
        public SupportDef Support;
        /// <summary>Archetype skill tree id (§25.6.1, GG-T50) every hero of this class spends skill points in.
        /// Hero-only; required for Team.Hero, left null for Team.Enemy. The class's own starting kit
        /// (<see cref="DefaultLoadout"/>) is known from level 1 regardless of what's unlocked in the tree.</summary>
        public string HomeTree;
        /// <summary>Weapon classes (GG-T52) this class is trained in from level 1 — no proficiency passive
        /// needed. Every hero can always use <see cref="WeaponClass.Simple"/> regardless of this list; leave
        /// it empty for a class that starts Simple-only (e.g. a healer/support archetype).</summary>
        public List<WeaponClass> WeaponProficiencies = new List<WeaponClass>();
        /// <summary>Heaviest armor (GG-T52) this class can wear from level 1 without a proficiency passive.
        /// Defaults to Medium — the only weight actually gated is Heavy, reserved for the Tank archetype
        /// (Guardian) by default; every other class earns it through Heavy Armor Proficiency.</summary>
        public ArmorWeight ArmorProficiency = ArmorWeight.Medium;
        /// <summary>Bond a tamed monster of this class needs before it asks to join (§25.11.3, GG-T303); 0 = derived from its
        /// level (<c>Taming.BondFor</c>). Rarer, stronger monsters author more.</summary>
        public int TameBond;

        public bool HasTag(string tag) => Tags != null && Tags.Contains(tag);
    }

    public sealed class AbilityDef
    {
        public string Id;
        public string Name;
        public string Description;
        public AbilityKind Kind;
        /// <summary>Mana cost in generic orbs (0 for skills/items).</summary>
        public int Cost;
        public float CastTime;
        public int CooldownTurns;
        public TargetShape Shape = TargetShape.SingleActor;
        public int Radius;
        public TargetMode Mode = TargetMode.PickActor;
        public TargetFilter Filter = TargetFilter.EnemyOnly;
        public ProjectileMotion Motion = ProjectileMotion.None;
        public DamageType DamageType = DamageType.Arcane;
        public int BaseDamage;
        public int BaseHeal;
        public string ApplyBuff;
        public float ApplyBuffChance = 1f;
        public bool RemovesDebuffs;
        public bool StealsMana;
        public float HastenU;
        /// <summary>Party turns a hero haste from this ability keeps the party's tempo up (GG-T268); 0 = BattleConfig.DefaultTempoTurns.</summary>
        public int TempoTurns;
        public bool IsTeleport;
        /// <summary>Tiles the target is shoved straight away from the caster (shield bash) — §6.3.</summary>
        public int Knockback;
        /// <summary>The pick must be cardinally adjacent to the caster (melee skills).</summary>
        public bool AdjacentOnly;
        /// <summary>The Pushback keyword (§2.3, GG-T286), in seconds: each foe it reaches acts this much later, wherever its
        /// icon sits on the bar (a held cast slips instead; never cancelled). Capped per action at
        /// <see cref="Battle.BattleConfig.PushbackMaxSeconds"/>, one push per foe per action. Plain hits never push.</summary>
        public float Pushback;
        /// <summary>GG-T318 (§2.12): extra seconds the unit spends off the timeline after the turn it used this, on top of
        /// <see cref="Battle.BattleConfig.RecoverBaseSeconds"/>. Heavy blows recover longer, so a foe's rhythm can be read.
        /// 0..<see cref="Battle.BattleConfig.RecoverMaxSeconds"/>.</summary>
        public float RecoverSeconds;
        /// <summary>GG-T332: a foe's siphon draws aether out of the party — when it lands on at least one hero, up to this
        /// many orbs leave the party's bank (the Bulwark's aether pack, a portable Still). Enemy charges only (`charge_`),
        /// 0..<see cref="MaxDrainsOrbs"/>.</summary>
        public int DrainsOrbs;
        public const int MaxDrainsOrbs = 3;
        /// <summary>Movement (GG-T183): each target is dragged up to this many tiles straight toward the caster, stopping
        /// beside it or at the first thing in the way. Bosses and snakes don't budge.</summary>
        public int Pull;
        /// <summary>Movement (GG-T183): the caster and its (one-tile) target trade tiles.</summary>
        public bool SwapsPlaces;
        /// <summary>Movement (GG-T183): once it resolves, the caster steps up to this many tiles straight away from the
        /// nearest foe (Disengage, Hit and Run).</summary>
        public int StepBack;
        /// <summary>Random WUBRG orbs added to the bank on resolve (ethers).</summary>
        public int GrantsOrbs;
        /// <summary>Removes just this buff id (Eye Drops → blinded).</summary>
        public string RemovesBuff;
        /// <summary>Brings a fallen ally back onto a free tile beside the caster with BaseHeal HP.</summary>
        public bool Revives;
        /// <summary>Ends the battle as an escape (no rewards, no defeat penalty).</summary>
        public bool Escape;
        /// <summary>Damage ×BonusMultiplier against targets with this tag (Holy Water vs Undead).</summary>
        public string BonusVsTag;
        public float BonusMultiplier = 1f;
        public string Icon;
        public string CastVfx;
        public string ProjectileVfx;
        public string ImpactVfx;
        public string LingerVfx;
        /// <summary>Optional VFX tier (GG-T253): Minor, Major or Ultimate; empty derives it from cost and cooldown (VfxStyleSheet.TierOf).</summary>
        public string Intensity;
        public string Sfx;
        /// <summary>Class-agnostic Skill: learnable by any hero regardless of class, like every Spell already is
        /// (<see cref="GridGame.Meta.MetaServices.Abilities.Learnable"/>). Ignored on Spell/Passive, which already
        /// have their own universal/owning-class rules.</summary>
        public bool Universal;
        /// <summary>Passive/Reactive only (GG-T296): when and how it fires (§4.1). Null on Active abilities and on
        /// meta-only passives (the proficiency unlocks).</summary>
        public TriggerDef Trigger;
        /// <summary>Passive/Reactive only (GG-T301): who a Tome may teach it to, as hero class ids and/or Roles
        /// (Tank/Healer/Mage/Striker/Support/Ranged). Empty = any hero.</summary>
        public List<string> LearnableBy = new List<string>();
        /// <summary>The Tame effect (§25.11.3, GG-T303): base chance (0..1) to turn a tameable foe — a non-Sentient, non-boss
        /// monster — to the party's side for <see cref="TameTurns"/> of its own turns. 0 = not a tame. Any Skill or Item
        /// ability may carry it (the Monster Tamer's Tame, the Dog Whistle); <c>Taming.Chance</c> scales it by the foe's
        /// HP left, the level gap and the user's Luck/Wisdom.</summary>
        public float Tame;
        /// <summary>Own turns a successful <see cref="Tame"/> lasts before the monster breaks free (a full bond keeps it).</summary>
        public int TameTurns;
        /// <summary>Essence (GG-T333, the Herald): once the heal has landed, the caster pays this share of its own max HP
        /// (at least 1 HP, never its last one). 0 = free. A heal only (BaseHeal &gt; 0, AllyOnly), 0..<see cref="MaxLifeCost"/>.</summary>
        public float LifeCost;
        public const float MaxLifeCost = 0.5f;
        /// <summary>GG-T337 (the Aetherist): an orb grant that rides a per-target effect (a heal, a hit, a buff) instead of
        /// being the whole ability (Meditate, an ether). It resolves on its targets first, then pours
        /// <see cref="GrantsOrbs"/> into the bank.</summary>
        public bool PoursOrbs => GrantsOrbs > 0 && (BaseHeal > 0 || BaseDamage > 0 || ApplyBuff != null);
        /// <summary>Countermarch (GG-T336, the Tactician): the picked foe's turn icon trades places on the timeline with the
        /// foe whose turn is furthest off (<see cref="Battle.Countermarch"/>). A picked single foe from a Skill or Spell.</summary>
        public bool SwapsTurns;
    }

    public sealed class BuffDef
    {
        public string Id;
        public string Name;
        public BuffKind Kind = BuffKind.Debuff;
        public DurationUnit Unit = DurationUnit.Turns;
        public int Duration = 1;
        public int DamagePerTick;
        public int HealPerTick;
        public bool Immobile;
        public bool HaltsTimeline;
        public bool BreaksOnDamage;
        public bool BreaksOnMove;
        public float IncomingDamageMultiplier = 1f;
        public float TimelineSpeedMultiplier = 1f;
        /// <summary>Signed party tempo this buff adds while it sits on a hero (GG-T268): Slow on heroes is negative, so enemy icons speed up.</summary>
        public int Tempo;
        public float AccuracyMultiplier = 1f;
        /// <summary>Evasion points (GG-T297, §13.1.0) the bearer gains while this sits on it (Blur's +15); -40..40.</summary>
        public float EvasionBonus;
        public bool BlocksSpells;
        /// <summary>Taunt (§14.1.1): while any hero carries this, enemies may only pick taunting heroes.</summary>
        public bool Taunts;
        /// <summary>Damage the bearer deals is scaled by this (War Cry / Might pickup → Empowered).</summary>
        public float OutgoingDamageMultiplier = 1f;
        public string OnExpireApply;
        public BuffStacking Stacking = BuffStacking.Refresh;
        /// <summary>Cap for Stack buffs (0 = unlimited). Burning stacks to 3.</summary>
        public int MaxStacks;
        public string Letter;
        public string Color;
    }

    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public string Description;
        public ItemType Type;
        public ItemRarity Rarity = ItemRarity.Common;
        public EquipmentSlot Slot = EquipmentSlot.None;
        public string WeaponType;
        /// <summary>Weapon-slot items only (GG-T52): which proficiency category (Simple/Martial/Arcane) this
        /// weapon needs. Ignored for every other slot. Default Simple = usable by any hero.</summary>
        public WeaponClass WeaponClass = WeaponClass.Simple;
        /// <summary>Head/Torso/Legs/Gloves/Boots items only (GG-T52): how much armor training (Light/Medium/
        /// Heavy) this piece demands. Ignored for Weapon/jewelry slots. Default Light = usable by any hero.</summary>
        public ArmorWeight ArmorWeight = ArmorWeight.Light;
        public int BaseCost;
        /// <summary>-1 = BaseCost/2.</summary>
        public int SellValue = -1;
        public int MaxStack = 99;
        public int Durability;
        public int BaseHealing;
        public int BaseDamage;
        public Stats Stats = new Stats();
        public int BattleStartManaOrbs;
        /// <summary>Consumable that casts this ability on use (e.g. Sleep Dart → "sleep").</summary>
        public string OnUseAbility;
        public Dictionary<DamageType, float> Resistances = new Dictionary<DamageType, float>();
        public bool SoldByVendor;
        public string Icon;
        public DamageType DamageType = DamageType.Physical;
        public int GrantsOrbs;
        public string CuresBuff;
        public bool CuresAll;
        public bool Revives;
        public bool Escape;
        /// <summary>Usable only from the camp Items menu (Tent).</summary>
        public bool OutOfBattleOnly;
        public string BonusVsTag;
        public float BonusMultiplier = 1f;
        /// <summary>Extra ManaBank slots while equipped (bible §3.1.4 knob), summed over the party, max +4.</summary>
        public int BankCapacityBonus;
        /// <summary>Relic passive: this hero's pincer strikes deal +X (fraction).</summary>
        public float PincerDamageBonus;
        /// <summary>Gear passive (GG-T297): Evasion points while equipped, 0..20; lost when the piece breaks.</summary>
        public float EvasionBonus;
        /// <summary>Relic passive: HP restored each time control returns to the player.</summary>
        public int RegenPerTurn;
        /// <summary>Relic passive: start every battle with Protection.</summary>
        public bool StartsProtected;
        /// <summary>RecipeScroll only (§24.6.2, GG-T36): the recipe this item teaches when read at the Items screen.</summary>
        public string TeachesRecipe;
        /// <summary>Tome only (GG-T301): the Passive or Reactive ability it teaches the hero it's read to.</summary>
        public string TeachesAbility;
        /// <summary>Chest only (GG-T301): the droptables.json row rolled when it's opened.</summary>
        public string OpensTable;
        /// <summary>Named upgrade passive (§24.6.1, GG-T47): flavor name of a weapon's fully-upgraded (Master-tier,
        /// "+3") named passive, shown on the Equip compare panel. A weapon whose base already carries an on-hit or
        /// lifesteal trait (GG-T181) names that trait here on every step, and its "+3" passive is the same mechanic,
        /// stronger. Null = no named passive.</summary>
        public string PassiveName;
        /// <summary>PassiveName's effect text for the compare panel, e.g. "Pincer strikes have a 20% chance to Burn."</summary>
        public string PassiveDescription;
        /// <summary>PassiveName mechanic: a landed pincer strike (not a supporter or counter hit) by this weapon's
        /// wielder has this chance to apply this buff to the target (e.g. Flamebrand's Burn).</summary>
        public string PincerApplyBuff;
        public float PincerApplyBuffChance;
        /// <summary>PassiveName mechanic: heals the wielder this fraction of any damage a hero lands with this weapon
        /// (e.g. Vampiric Edge).</summary>
        public float LifestealFraction;
        /// <summary>PassiveName mechanic: while this weapon's wielder stands adjacent to an ally hero, that ally takes
        /// this much less damage (the strongest adjacent guardian wins — doesn't stack).</summary>
        public float AdjacentAllyDamageReduction;

        public int EffectiveSellValue => SellValue >= 0 ? SellValue : System.Math.Max(1, BaseCost / 2);
        /// <summary>Content authors always set <c>Slot: "Earring1"</c> for an earring item; this flag (not an
        /// exact slot match) is what lets it fit either Earring1 or Earring2 (GG-T89).</summary>
        public bool IsEarring => Slot == EquipmentSlot.Earring1 || Slot == EquipmentSlot.Earring2;
        /// <summary>Content authors always set <c>Slot: "Ring1"</c> for a ring item; fits either Ring1 or Ring2.</summary>
        public bool IsRing => Slot == EquipmentSlot.Ring1 || Slot == EquipmentSlot.Ring2;
    }

    /// <summary>One tier of a named armor set's bonus (GG-T182): active while the hero wears at least
    /// <see cref="Pieces"/> of the set's five pieces. Every field stacks onto the hero exactly like the same
    /// field on an <see cref="ItemDef"/> (stats add, resistances multiply, relic passives add).</summary>
    public sealed class SetBonusDef
    {
        public int Pieces;
        public Stats Stats = new Stats();
        public Dictionary<DamageType, float> Resistances = new Dictionary<DamageType, float>();
        public float PincerDamageBonus;
        public int RegenPerTurn;
        public int BattleStartManaOrbs;
        public bool StartsProtected;
    }

    /// <summary>A named five-piece armor set (GG-T182, armorsets.json): one Head/Torso/Legs/Gloves/Boots piece
    /// each, all of one <see cref="ArmorWeight"/>, with 2- and 5-piece bonuses resolved by
    /// <see cref="GridGame.Core.Rules.ArmorSets"/>. <see cref="Level"/> is the campaign level it's pitched at.</summary>
    public sealed class ArmorSetDef
    {
        public string Id;
        public string Name;
        public ArmorWeight Weight;
        public int Level = 1;
        public string Description;
        public List<string> Pieces = new List<string>();
        public List<SetBonusDef> Bonuses = new List<SetBonusDef>();
    }

    public sealed class ItemCount
    {
        public string Item;
        public int Count = 1;
    }

    public sealed class RecipeDef
    {
        public string Id;
        public CraftStation Station;
        public List<ItemCount> Inputs = new List<ItemCount>();
        public int Gold;
        /// <summary>Optional base item consumed (upgrade/enchant).</summary>
        public string BaseItem;
        public string Result;
        public int ResultCount = 1;
        /// <summary>Tier ladder rung (§24.6.1): 1 Apprentice (from the start), 2 Journeyman, 3 Master —
        /// gated by the crafting station's level (GG-T35). Missing from content = 1.</summary>
        public int Tier = 1;
        /// <summary>A starter weapon recipe (GG-T177, §25.0.6): the Forge's guided first forge. An Apprentice Forge
        /// recipe for a weapon, no gold, inputs from the first theme's drops; forging one equips it on the hero it suits.</summary>
        public bool Starter;
        /// <summary>Reagent quality (GG-T45): brewing with these inputs instead of <see cref="Inputs"/> — swapping
        /// in a higher-rarity reagent for the recipe's key material — yields <see cref="PotencyResult"/> instead
        /// of <see cref="Result"/>. Same recipe, better inputs, better output. Null/empty = no potency variant.</summary>
        public List<ItemCount> PotencyInputs = new List<ItemCount>();
        public string PotencyResult;
        public int PotencyResultCount = 1;
    }

    public sealed class DropEntry
    {
        public string Item;
        public float Chance;
        public int Min = 1;
        public int Max = 1;
    }

    public sealed class DropTableDef
    {
        public string Id;
        public int CoinsMin;
        public int CoinsMax;
        public List<DropEntry> Entries = new List<DropEntry>();
    }

    public sealed class SpawnDef
    {
        public string Class;
        public int Level = 1;
        /// <summary>Optional fixed tile (0-based col); -1 = random free tile.</summary>
        public int X = -1;
        /// <summary>Optional fixed tile (0-based row, 0 = top); -1 = random free tile.</summary>
        public int Y = -1;
        /// <summary>Ticks after this wave starts before this entry spawns (§14.8). 0 = immediate, today's only behavior.</summary>
        public int DelayTicks;
        /// <summary>A finale boss's rule-break (§14.9, GG-T234); None = plays by the rules.</summary>
        public RuleBreak RuleBreak;
    }

    public sealed class WaveDef
    {
        /// <summary>An <see cref="EncounterGroupDef"/> id (encounters.json, GG-T184). When set, the loader fills
        /// <see cref="Enemies"/> from that group (its mix, formation tiles and level); leave Enemies empty then.</summary>
        public string Group;
        public List<SpawnDef> Enemies = new List<SpawnDef>();
        /// <summary>Guest allies met in this wave (§25.11): fight with you; standing at a victory (revived counts) and they join, down at the end and you'll meet them again (GG-T114).</summary>
        public List<SpawnDef> Guests = new List<SpawnDef>();
        /// <summary>Neutral recruitables met in this wave (§25.11.1, GG-T132): they stand aside until a hero talks them round.</summary>
        public List<RecruitableDef> Recruitables = new List<RecruitableDef>();
        /// <summary>Class id whose portrait/name show when this wave's first delayed reinforcement arrives (§14.8). Optional.</summary>
        public string SurpriseSpeaker;
        /// <summary>The line that speaker says. Null/empty = no dialogue beat, just the spawn (silent reinforcement).</summary>
        public string SurpriseLine;
    }

    /// <summary>One authored enemy group (GG-T184, encounters.json): a single-family mix of 1-5 foes, each on its
    /// own tile of the enemy half (the <see cref="Formation"/>), pitched at <see cref="Level"/>. Campaign waves
    /// reference groups by id (<see cref="WaveDef.Group"/>); the Crucible and endless encounters draw their waves
    /// from them (<see cref="EncounterCatalog"/>). No two groups share the same mix on the same tiles.</summary>
    public sealed class EncounterGroupDef
    {
        public string Id;
        public string Name;
        /// <summary>The formation's name (Pincer, Wedge, Crown ...): what the tiles in <see cref="Enemies"/> form.</summary>
        public string Formation;
        /// <summary>The campaign level the group is pitched at: a campaign wave spawns it at this level (still
        /// floored by the stage's recommended level), and the generators prefer groups at or under the wave's level.</summary>
        public int Level = 1;
        /// <summary>The foes and their tiles (Level and DelayTicks are ignored here: the group's Level applies).</summary>
        public List<SpawnDef> Enemies = new List<SpawnDef>();
    }

    public sealed class StageDef
    {
        public string Id;
        public string Name;
        public string Theme;
        public string Background;
        public string Music;
        public bool IsTest;
        /// <summary>Overrides the flattened-campaign-index-derived RecommendedLevel (Progression.RecommendedLevel)
        /// when set — for postgame/bonus stages appended after the main campaign, whose difficulty shouldn't keep
        /// climbing just because of where they sit in CampaignStages()'s flattened order (§7 frontier note).</summary>
        public int? LevelOverride;
        /// <summary>Physical blocks (rocks, pillars, wreckage): tiles no unit may enter, land on, be pushed into or spawn on (§1.1.1).</summary>
        public List<Coord> Blocks = new List<Coord>();
        public List<WaveDef> Waves = new List<WaveDef>();
    }

    /// <summary>A caravan upgrade (§25.12): bought with gold, one value per level.</summary>
    public sealed class CaravanUpgradeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string Icon;
        public List<CaravanLevel> Levels = new List<CaravanLevel>();
        /// <summary>The crew member who runs this wagon (GG-T75), e.g. "Brannoc".</summary>
        public string Crew;
        /// <summary>Their job on the caravan, e.g. "cask-keeper".</summary>
        public string CrewRole;
        /// <summary>One-line flavor barks shown on Camp and the Caravan screen — never dialogue: no branching, no choices (§27).</summary>
        public List<string> Barks = new List<string>();
    }

    public sealed class CaravanLevel
    {
        public int Cost;
        public float Value;
        public string Text;
    }

    public sealed class ThemeDef
    {
        public string Id;
        public string Name;
        public string Crawl;
        public List<string> Stages = new List<string>();
        /// <summary>Campaign milestone (GG-T301): an item granted once, the first time the theme's last stage is won
        /// (a Tome, so a story beat can teach a passive). Null = none.</summary>
        public string MilestoneReward;
        /// <summary>GG-T344: the theme's background set, <c>Backgrounds/&lt;set&gt;_01..NN</c> (owner rule: named after the
        /// story region, numbered from 01). Null = the theme id (<see cref="Backdrops.SetOf"/>).</summary>
        public string Backgrounds;
    }

    public sealed class CampaignDef
    {
        public List<ThemeDef> Themes = new List<ThemeDef>();
        /// <summary>GG-T231: the crawl after New Campaign's party pick, and the one after the final boss.</summary>
        public CrawlDef Prologue;
        public CrawlDef Ending;
    }

    /// <summary>
    /// A battle pickup (§13.3.1): a fallen enemy sometimes leaves one on the board; the first hero to step on it
    /// collects it. It fades after <see cref="BattleConfig.PickupLifetimeActions"/> player actions.
    /// </summary>
    public sealed class PickupDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Relative chance among pickups when one drops.</summary>
        public int Weight = 1;
        /// <summary>Heals the collector by this fraction of its max HP.</summary>
        public float HealFraction;
        /// <summary>Orbs minted into the bank.</summary>
        public int Orbs;
        /// <summary>Timeline hasten for the collector (u).</summary>
        public float HastenU;
        /// <summary>Buff applied to the collector.</summary>
        public string ApplyBuff;
        public string Color;
        public string Icon;
    }

    public sealed class TrapDef
    {
        public string Id;
        public string Name;
        public int Damage;
        public string ApplyBuff;
        public float LayChance = 0.45f;
        /// <summary>Element of the trap's damage (default Poison).</summary>
        public DamageType DamageType = DamageType.Poison;
    }

    public sealed class BountyDef
    {
        public string Id;
        public string Name;
        public string TargetClass;
        public int Kills;
        public int RewardGold;
        public string RewardItem;
        /// <summary>Optional skill-point payout (GG-T134): on Claim every hero in the active party gains this
        /// many skill points. 0 (the default) pays none.</summary>
        public int RewardSkillPoints;
    }

    public sealed class SummonDef
    {
        public List<string> Pool = new List<string>();
    }

    public sealed class EnchantDef
    {
        public string Id;
        public string Name;
        public string Element;
        public string Essence;
        public int ArcaneDust = 2;
        public int Gold = 150;
        public Stats Bonus = new Stats();
    }

    /// <summary>
    /// An armor enchant (GG-T179, armorenchants.json): the Enchantress binds a <see cref="AbilityKind.Passive"/>
    /// ability into one worn armor piece (Head/Torso/Legs/Gloves/Boots). Unlike the weapon-only elemental
    /// <see cref="EnchantDef"/>s, which turn a weapon into an "(Element)" variant item, an armor enchant lives on the
    /// piece itself (its save entry's <c>Enchant</c>), so set membership, wear and Masterwork rolls are untouched; the
    /// wearer has <see cref="Passive"/> in battle while the piece is intact. One enchant per piece (a new one replaces it).
    /// </summary>
    public sealed class ArmorEnchantDef
    {
        public string Id;
        public string Name;
        /// <summary>The passive ability (abilities.json, Kind Passive) the enchanted piece grants its wearer.</summary>
        public string Passive;
        public List<ItemCount> Inputs = new List<ItemCount>();
        public int Gold;
        /// <summary>Tier ladder rung (§24.6.1): unlocks with the Alchemist's station level, like every Enchantress recipe.</summary>
        public int Tier = 1;
    }

    /// <summary>
    /// One node in an archetype skill tree (§25.6.1, GG-T50). Unlocking it costs <see cref="Cost"/> skill
    /// points and grants the ability or passive named by <see cref="Grants"/>. Gated by
    /// <see cref="RequiredPointsInTree"/> (total points already spent anywhere in the tree) and
    /// <see cref="Prerequisites"/> (sibling node ids in the same tree that must already be unlocked).
    /// Spending points against a tree (and applying a granted passive to a hero in battle) is a later
    /// ticket; this is the content shape that work will read.
    /// </summary>
    public sealed class SkillNodeDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Ability or passive id granted when this node unlocks. Exactly one of this and
        /// <see cref="GrantsStat"/> is set (<see cref="ContentLoader.Validate"/>).</summary>
        public string Grants;
        /// <summary>Attribute-bonus node (GG-T77): a flat permanent bonus to one stat instead of an ability.
        /// Mutually exclusive with <see cref="Grants"/>.</summary>
        public StatGrantDef GrantsStat;
        public int Cost = 1;
        public int RequiredPointsInTree;
        public List<string> Prerequisites = new List<string>();

        public bool IsStatNode => GrantsStat != null;
    }

    /// <summary>A skill-tree attribute bonus (GG-T77): <see cref="Amount"/> flat points of the stat named by
    /// <see cref="Stat"/> — one of <see cref="Names"/>, the real <see cref="Stats"/> field names.</summary>
    public sealed class StatGrantDef
    {
        public string Stat;
        public float Amount;

        public static readonly string[] Names = { "Strength", "Vitality", "Agility", "Speed", "Stamina", "Intelligence", "Wisdom", "Luck" };

        public static bool IsStatName(string s) => s != null && System.Array.IndexOf(Names, s) >= 0;

        /// <summary>This bonus as a <see cref="Stats"/> block (all zero but the named stat), ready to add to a hero's stats.</summary>
        public Stats ToStats()
        {
            var s = new Stats();
            switch (Stat)
            {
                case "Strength": s.Strength = Amount; break;
                case "Vitality": s.Vitality = Amount; break;
                case "Agility": s.Agility = Amount; break;
                case "Speed": s.Speed = Amount; break;
                case "Stamina": s.Stamina = Amount; break;
                case "Intelligence": s.Intelligence = Amount; break;
                case "Wisdom": s.Wisdom = Amount; break;
                case "Luck": s.Luck = Amount; break;
            }
            return s;
        }
    }

    /// <summary>One archetype's skill tree (§25.6.1, GG-T50): named by a <see cref="ClassDef.HomeTree"/> id.</summary>
    public sealed class SkillTreeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public List<SkillNodeDef> Nodes = new List<SkillNodeDef>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Dialogue (§27.1, GG-T117): dialogues.json. The engine (GridGame.Meta.DialogueRunner) is a data
    // interpreter over these shapes; conditions and effects are the save's own vocabulary, not a script.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>When a dialogue plays. Story beats (ThemeStart, StageCleared, GearWorn, CanBrew) play once, between battles,
    /// from StageLauncher; Talk dialogues are picked from Camp's Talk row and may repeat unless <see cref="DialogueDef.Once"/>.
    /// GearWorn (GG-T175) fires once any hero's equipped weapon or armor first falls below <see cref="DialogueTrigger.Percent"/>
    /// of its max durability — the moment a repair first matters — or, if <see cref="DialogueTrigger.Id"/> names a theme,
    /// at the latest on launching any stage of that theme or a later one (so a party that never wears gear still meets him).
    /// CanBrew (GG-T176) fires once the bag holds every looted input — everything but the Empty Bottle — of some
    /// Apprentice-tier bottle recipe (the player's first brew), with the same optional fallback theme in <see cref="DialogueTrigger.Id"/>.
    /// FirstArmor (GG-T179) fires once the guided first armor piece has been forged (the save flag
    /// <c>onboard:first-armor</c>), with the same optional fallback theme in <see cref="DialogueTrigger.Id"/>.
    /// Recruit (GG-T132) is never played between battles: a stage's <see cref="RecruitableDef.Dialogue"/> names it and the
    /// battle speaks it when a hero talks to the neutral (<see cref="DialogueTrigger.Id"/> = the recruitable's class).
    /// LevelReached (GG-T231) fires once any roster hero is at <see cref="DialogueTrigger.Level"/> or above — on the way back
    /// to Camp after the battle that did it, or at the next launch. StageStart (GG-T231) fires on launching the stage
    /// <see cref="DialogueTrigger.Id"/> (a boss's introduction).
    /// Wanderer (GG-T342; replaces the GG-T236 Rescue scene) is the talk with a wanderer standing at a victory, played from
    /// the results screen: "$hero" is the wanderer, "$with" the party member the player chose to talk, and
    /// <see cref="DialogueTrigger.Id"/> is the wanderer's hero class, a factions.json faction id, or null (the generic one).
    /// It has one question with three answers — exactly one goes to <see cref="DialogueDef.HeardNode"/> (what they want to
    /// hear), the others to <see cref="DialogueDef.UnheardNode"/> — both of which go on to <see cref="DialogueDef.OutcomeNext"/>,
    /// where the meta layer's rule picks <see cref="DialogueDef.JoinNode"/> or <see cref="DialogueDef.PartNode"/>.</summary>
    public enum DialogueTriggerKind { ThemeStart, StageCleared, Talk, GearWorn, CanBrew, FirstArmor, Recruit, LevelReached, StageStart,
        Wanderer }

    public sealed class DialogueTrigger
    {
        public DialogueTriggerKind Kind;
        /// <summary>ThemeStart: the theme id. StageCleared: the stage id. Talk: who it's with — a hero class id,
        /// "party" (any party member; speaker class "$with" is them) or "npc:&lt;id&gt;" (a joined Camp NPC). GearWorn, CanBrew, FirstArmor: optional fallback theme id.</summary>
        public string Id;
        /// <summary>GearWorn only: the durability percentage (1-99) a piece must fall below.</summary>
        public int Percent;
        /// <summary>LevelReached only: the hero level (2+) that fires it.</summary>
        public int Level;
    }

    /// <summary>QuestAvailable / QuestActive / QuestDone (GG-T237, §22.11): <see cref="DialogueCondition.Id"/> is a quests.json id.
    /// Available = its unlock stage is cleared and it's neither under way nor handed in.</summary>
    public enum DialogueConditionType { FlagSet, FlagNotSet, HasItem, GoldAtLeast, PartyHas, RosterHas, RosterLacks, NpcJoined, QuestAvailable, QuestActive, QuestDone }

    public sealed class DialogueCondition
    {
        public DialogueConditionType Type;
        /// <summary>Flag / item / class / NPC id, by type.</summary>
        public string Id;
        /// <summary>HasItem count (default 1) or GoldAtLeast amount.</summary>
        public int Amount;
    }

    /// <summary>Nothing here can remove a hero for good (owner rule: nobody dies permanently).
    /// RecruitHero hires exactly the named class (no-op if owned); JoinHero (GG-T118) is a story recruit that
    /// always brings someone new: the named class if it's free, else a hero of <see cref="DialogueEffect.Role"/>
    /// (or of a role the roster lacks) the player doesn't have yet.
    /// GrantSkillPoint (GG-T133) gives a hero — <see cref="DialogueEffect.Id"/> as a class id, or "$with"/null for the
    /// Talk partner — <see cref="DialogueEffect.Amount"/> skill points (default 1), once per dialogue per hero: the
    /// save flag <c>sp:&lt;dialogue&gt;:&lt;hero&gt;</c> guards it, so a replayed Talk can't farm it.
    /// StartQuest (GG-T237) puts the quests.json quest <see cref="DialogueEffect.Id"/> into the journal (no-op if it's under way or done).</summary>
    public enum DialogueEffectType { SetFlag, ClearFlag, GrantGold, GrantItem, RecruitHero, JoinNpc, JoinHero, GrantSkillPoint, StartQuest }

    public sealed class DialogueEffect
    {
        public DialogueEffectType Type;
        public string Id;
        /// <summary>GrantGold amount (negative = pay), GrantItem count (default 1) or GrantSkillPoint count (default 1).</summary>
        public int Amount;
        /// <summary>JoinHero only: the role to fall back on when <see cref="Id"/> is missing or already owned.</summary>
        public string Role;
    }

    public sealed class DialogueSpeaker
    {
        public string Id;
        /// <summary>A class id (its name and portrait), or "$lead" (the first party member), "$with" (the
        /// party member a Talk dialogue is with) or "$recruit" (the hero this dialogue's first JoinHero brings, GG-T118), or
        /// "$hero" (the wanderer a Wanderer talk is with, GG-T342).</summary>
        public string Class;
        /// <summary>Display name; defaults to the class's name.</summary>
        public string Name;
        /// <summary>Portrait id (Resources/Portraits/&lt;id&gt;); defaults to the class id.</summary>
        public string Portrait;
    }

    public sealed class DialogueChoice
    {
        public string Label;
        /// <summary>Node id to go to; null ends the dialogue.</summary>
        public string Next;
        /// <summary>All must hold for the choice to be offered.</summary>
        public List<DialogueCondition> Conditions = new List<DialogueCondition>();
        public List<DialogueEffect> Effects = new List<DialogueEffect>();
    }

    public sealed class DialogueNode
    {
        public string Id;
        /// <summary>A <see cref="DialogueSpeaker.Id"/> of this dialogue.</summary>
        public string Speaker;
        public string Text;
        /// <summary>Auto-advance target when there are no choices; null ends the dialogue.</summary>
        public string Next;
        public List<DialogueChoice> Choices = new List<DialogueChoice>();
        /// <summary>Applied when the node is reached.</summary>
        public List<DialogueEffect> Effects = new List<DialogueEffect>();
    }

    public sealed class DialogueDef
    {
        /// <summary>Camp NPC ids a JoinNpc effect / NpcJoined condition / "npc:" Talk may name (GG-T119 gates services on them).</summary>
        public static readonly string[] Npcs = { "blacksmith", "alchemist", "enchantress", "merchant", "innkeeper" };

        public string Id;
        /// <summary>Talk row label (Talk dialogues); also a debugging name.</summary>
        public string Title;
        /// <summary>Wanderer talks (GG-T342): the reply that speaks to them, the ones that don't, and the two endings the
        /// <see cref="OutcomeNext"/> marker resolves to.</summary>
        public const string HeardNode = "heard", UnheardNode = "unheard", JoinNode = "join", PartNode = "part", OutcomeNext = "$outcome";

        public DialogueTrigger Trigger = new DialogueTrigger();
        /// <summary>All must hold for the dialogue to be offered.</summary>
        public List<DialogueCondition> Conditions = new List<DialogueCondition>();
        /// <summary>Talk only: play once, then never offer again (story beats always play once).</summary>
        public bool Once;
        /// <summary>Backdrop theme id (defaults to the trigger's theme, else the camp's).</summary>
        public string Backdrop;
        /// <summary>Story beats only (GG-T231): a text crawl, titled <see cref="Title"/>, that scrolls before the conversation.</summary>
        public string Crawl;
        public List<DialogueSpeaker> Speakers = new List<DialogueSpeaker>();
        /// <summary>First node id; defaults to the first node.</summary>
        public string Start;
        public List<DialogueNode> Nodes = new List<DialogueNode>();

        /// <summary>Every effect in the dialogue — nodes' then their choices', in node order (null nodes/choices skipped).</summary>
        public IEnumerable<DialogueEffect> AllEffects()
        {
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                foreach (var e in n.Effects) yield return e;
                foreach (var ch in n.Choices)
                    if (ch != null) foreach (var e in ch.Effects) yield return e;
            }
        }
    }
}
