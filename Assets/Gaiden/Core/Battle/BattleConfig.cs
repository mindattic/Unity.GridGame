namespace GridGame.Core.Battle
{
    /// <summary>
    /// Every tuning number of the battle simulation in one place (bible §2, §3, §13).
    /// Time is integer ticks (1 tick = 0.1 "game seconds"); the timeline bar is BarLength IP.
    /// The clock only advances when the player commits an action (bible §2, act-advanced amendment).
    /// </summary>
    public static class BattleConfig
    {
        public const int Cols = 6;
        public const int Rows = 8;

        /// <summary>
        /// Terra Battle-style skill tech: a fast, precise ~45° drag can slip a hero diagonally into an open
        /// tile, skipping the usual cardinal-only path instead of a softened "corner" rule. Player drags only
        /// (see <c>BattleScreen.Input.ExtendPath</c>) — enemies/bots never submit a diagonal step. Mutable, not
        /// const, so it's trivially flippable (`-noDiagonalSlip` at the command line).
        /// </summary>
        public static bool DiagonalSlipEnabled = true;

        // ── Timeline ────────────────────────────────────────────────────────────────
        public const int TicksPerSecond = 10;
        public const int BarLength = 1000;                 // a turn icon loads IP 0 → BarLength, then acts
        /// <summary>IP per tick per point of Speed, the one linear rate (GG-T286: no Zone crawl). Speed 10 → 42 IP/tick
        /// → a full lap in 2.4 s (GG-T311: 1.6 → 4.2 — with the minimum action time a tight cluster of foes now costs the
        /// party its turns; the difficulty dial's EnemySpeed column slows it below level 10). Tempo and buffs (Slowed, Hasted, Frozen) scale it (<see cref="BattleEngine.TimelineScale"/>). Static, not const, so a
        /// balance soak can sweep it (GAIDEN_TUNE, GG-T292).</summary>
        public static float IpPerTickPerSpeed = 4.2f;
        /// <summary>
        /// Seconds the drawn bar spans (GG-T286, Resonance-style): an icon sits at u = secondsUntilAct / BarSeconds, so
        /// u = 0 is the LEFT edge (acting now) and anything further off than BarSeconds waits at the right end.
        /// </summary>
        public static float BarSeconds = 10f;
        /// <summary>Reference pace (Speed 10) used to draw cast icons on the shared u-axis.</summary>
        public const float ReferencePacePerTick = 16f;
        /// <summary>
        /// Clock a spent window takes when no enemy turn icon can arrive at all (none left, all Frozen, a wave still
        /// waiting on reinforcements). Otherwise a window always runs to the next enemy arrival (GG-T188/GG-T308, §2.0).
        /// </summary>
        public const int ActionTicks = 20;
        /// <summary>
        /// GG-T308 (§2.0): game seconds an Ability, Item, Spell or Talk spends from the party's window (a drag spends the
        /// time it was held instead), so an instant action still costs budget. A spell's cast time rides the bar separately.
        /// </summary>
        public static float HeroActionSeconds = 1f;

        // ── Minimum action time (GG-T311, §2.0): a hero action can only start in a gap at least as long as it takes ──
        /// <summary>Seconds a drag costs before the hero has moved at all: picking it up (part of every drag's minimum).</summary>
        public static float DragPickupSeconds = 0.5f;
        /// <summary>Seconds a drag costs per tile walked. The shortest drag that does anything is pickup + one tile: a gap
        /// shorter than that can't start a drag (and every drag spends at least that much of the window).</summary>
        public static float DragSecondsPerTile = 0.35f;
        /// <summary>The party opens every wave: no enemy turn icon reaches the left edge sooner than this after the wave
        /// spawns (a fast foe's head start is trimmed), so the heroes always act first (GG-T311).</summary>
        public static float OpeningWindowSeconds = 3f;
        /// <summary>GG-T311: most rally stacks — forced Guards in a row (no action fitted the gap), each one tempo step slower for
        /// every enemy icon (×0.8, ×0.64, ×0.51, ×0.41, ×0.33, outside the tempo clamp) — so even foes whose turns all fall too close together leave a gap
        /// wide enough to act in after a few sacrifices. The next hero action resets it.</summary>
        public static int RallyMaxStacks = 5;

        // ── Party tempo (GG-T268, §2.11): hero haste slows enemy icons, Slow on heroes speeds them ─────────
        /// <summary>Each net tempo point scales every enemy turn icon's rate by this factor (+1 → ×0.8, −1 → ×1.25).</summary>
        public static float TempoStep = 1.25f;
        /// <summary>Clamp on the enemy icon multiplier so neither side ever freezes or runs away.</summary>
        public static float TempoMinMultiplier = 0.5f;
        public static float TempoMaxMultiplier = 2f;
        /// <summary>A hero haste ability of HastenU gives max(1, round(HastenU / this)) tempo points.</summary>
        public static float HastenUPerTempo = 0.15f;
        /// <summary>Party turns a tempo effect lasts when its source names none.</summary>
        public static int DefaultTempoTurns = 2;

        // ── Held turn (GG-T188, §2.0): picking up a hero runs the timeline in real time ──────────────────
        // GG-T311: no real-time floor any more (MinTurnSeconds 2 s let a 0.1 s gap play as a 2 s drag) — a hold runs at
        // 1 tick per 0.1 s, and a gap too short for any action is a forced Guard instead. The bot driver's drag time is
        // the human model DragPickupSeconds + DragSecondsPerTile × tiles (+ its think time, HeroBot.ThinkSeconds).
        /// <summary>Bot driver (GG-T191): a best move scoring under this is not worth a turn; the bot may Guard instead.</summary>
        public const float BotGuardBelowScore = 0.5f;
        /// <summary>Bot driver (GG-T191): chance of Guarding on such a turn (a lone hero always does).</summary>
        public const float BotGuardChance = 0.2f;
        /// <summary>Max random head-start IP for icons at wave spawn (staggered entry).</summary>
        public const int SpawnStaggerIp = 250;

        // ── Pushback (§2.3, GG-T286): only an ability/item carrying the Pushback keyword (seconds) moves an icon, anywhere
        //    on the bar; plain hits never push. One push per foe per action, capped. ──
        /// <summary>Seconds a pincer shoves back every foe it lands a blow on (the pincer attack carries the keyword).
        /// GG-T306: 1 → 1.5 s, paying the party back for foes that now engineer their own pincers.</summary>
        public static float PincerPushbackSeconds = 1.5f;
        /// <summary>Cap on one push, whatever its source.</summary>
        public static float PushbackMaxSeconds = 2f;

        // ── Buffs (§8.3) ────────────────────────────────────────────────────────────
        public const int BuffTickInterval = 10;            // one tick-unit step per 1.0 s of timeline
        public const float LightningWhenWetMultiplier = 1.5f;
        public const float SleepWhenWarmMultiplier = 1.5f;
        public const float LightningBlindChance = 0.30f;
        /// <summary>Spells never miss but crit at this fraction of the caster's physical crit chance (US-115 decision).</summary>
        public const float SpellCritFraction = 0.5f;
        public const float SpellCritMultiplier = 1.6f;

        // ── Casting (§2.6 / §13.4) ─────────────────────────────────────────────────
        public const int MaxHeroCastsInFlight = 4;
        public const float DefaultChargeCastSeconds = 2.5f;
        public const float BaseInterruptDelaySeconds = 0.6f;
        public const float InterruptStrengthScale = 20f;
        public const float InterruptWisdomDelayScale = 15f;
        public const float WisdomResistPerPoint = 0.015f;
        public const float MaxResistChance = 0.60f;
        public const float ClutchChancePerLuck = 1f / 200f;
        public const float ClutchMaxChance = 0.20f;

        // ── Supporters (§1.2.3) ─────────────────────────────────────────────────────
        public const float SupporterDamageFraction = 0.5f;

        // ── Shield (§2.5) ───────────────────────────────────────────────────────────
        public const string ShieldBuffId = "protection";
        public const int MaxBankCapacityBonus = 4;
        /// <summary>New Game+: enemy levels rise by this per cycle; coin rewards scale by NgPlusCoinBonus per cycle.</summary>
        public const int NgPlusLevelStep = 10;
        public const float NgPlusCoinBonus = 0.5f;

        // ── Time bank (§3.1.8, act-advanced amendment: drag-timer remainder) ────────
        public const float DragTimerSeconds = 4f;
        public const float SecondsPerTimeBankOrb = 1.25f;
        public const int MaxTimeBankOrbs = 3;

        // ── Guard bank (§2.5 / §3.1.9, GG-T189, owner 2026-10-21): Guard ends the turn early and the turn time it
        //    gives up fills the forming mana bubble (charge toward the next orb), proportional to the time left ──
        /// <summary>Mana charge per real second of turn left when Guard is pressed (100 charge = one orb).</summary>
        public const float GuardChargePerSecond = 10f;
        /// <summary>Most charge one Guard can bank — Guard ends the turn, so this is also the per-turn limit.</summary>
        public const int GuardMaxCharge = 40;

        // ── Steal (§6 stage 8) ──────────────────────────────────────────────────────
        public const float StealDivisor = 50f;

        // ── Weapon durability (§24.5) ───────────────────────────────────────────────
        public const float ShatterTargetMultiplier = 1.5f;
        public const float ShatterSelfDamageFraction = 0.15f;

        // ── Enemy AI (§14.1) ────────────────────────────────────────────────────────
        public const float RetreatHpThreshold = 0.30f;
        /// <summary>GG-T306 (§14.1.3) tuning, static so a soak can sweep them (GAIDEN_TUNE): a foe's pincer planner takes Setup
        /// and Support walks (not only Complete ones), and may swap places with a hero on its walk.</summary>
        public static bool EnemyPincerSetups = true;
        public static bool EnemyPincerSwapsHeroes = true;
        /// <summary>GG-T306: a wounded, fleeing foe's step score against a corner tile (no pincer axis reaches it).</summary>
        public static float FleeCornerPenalty = 3f;

        // ── Voluntary retreat (§22.2, FFIV-style partial loss, owner direction 2026-10-11) ──
        /// <summary>A voluntary Retreat (BattlePhase.Retreated) keeps this fraction of the battle's
        /// as-yet-uncommitted Results.Coins and each item stack (floored). A true party wipe (Defeat)
        /// still keeps nothing but hard-won XP, unchanged.</summary>
        public const float RetreatKeepFraction = 0.5f;

        // ── Power Amplifier (owner request 2026-10-10, from Terra Battle) ────────────
        /// <summary>Chance per player action that an amplifier appears (when none is on the board)…</summary>
        public const float AmplifierBaseChance = 0.12f;
        /// <summary>…plus this per point of the living party's combined Luck…</summary>
        public const float AmplifierChancePerLuck = 0.003f;
        /// <summary>…capped here.</summary>
        public const float AmplifierMaxChance = 0.45f;
        /// <summary>A pincer attacker standing on the amplifier strikes this much harder (and mints a bonus orb).</summary>
        public const float AmplifierDamageMultiplier = 1.75f;

        // ── Mana charge (§3.1.9, owner direction 2026-10-11: "mana needs to be a more rare resource … the number of
        //    participants in the attack increases the multiplier needed to earn a mana bubble") ─────────────────────
        /// <summary>Charge that makes one orb ("mana bubble").</summary>
        public const int ManaChargePerOrb = 100;
        /// <summary>Charge for a pincer that takes away a whole enemy's worth of HP (scaled by the share removed).
        /// GG-T247: 120 → 105 (the bar sat full on 17% of a skilled player's turns and ~half of all mana faded at the cap).</summary>
        public const float PincerChargePerHp = 105f;
        /// <summary>GG-T247: earned charge × (1 − this × bank fullness): an empty bank fills at full rate, a full one at 30%.</summary>
        public const float ChargeFullnessDamping = 0.7f;
        /// <summary>GG-T247: scales the Mana pincer-support action (Red Mage, Ritualist, …) — the second-biggest source.</summary>
        public const float SupportManaFactor = 0.5f;
        /// <summary>Each participant beyond the two attackers raises the divisor by this much (2 heroes ×1, 4 heroes ×2).</summary>
        public const float ChargeParticipantPenalty = 0.5f;
        /// <summary>Each link deeper in a chain earns this much more (chain ×2 → ×1.25, ×3 → ×1.5).</summary>
        public const float ChargeChainBonus = 0.25f;
        /// <summary>Any pincer that lands at least this much (chip damage on a giant still counts for something).</summary>
        public const int MinPincerCharge = 2;
        public const int CritCharge = 10;
        public const int InterruptCharge = 25;
        public const int AmplifierCharge = 30;

        /// <summary>Global weight on enemy physical strikes (difficulty knob; tuned with BalanceReport after the brains, §14.6).</summary>
        public const float EnemyStrikeMultiplier = 1.2f;
        /// <summary>Enemy MaxHP at spawn, as a share of the stat formula (GG-T262: waves ran a median 34 player actions
        /// against POL-G6's 6–20).</summary>
        public const float EnemyHpScale = 0.45f;
        /// <summary>An Ambusher lies in wait for at most this many of its own turns, then comes out (GG-T262: a lone
        /// ambushing naga held Mireholt Fens II's last wave to a median 116 player actions).</summary>
        public const int AmbushPatienceTurns = 3;

        // ── Masterwork crafting (§24.6.2, GG-T46) ────────────────────────────────────
        /// <summary>A Masterwork piece's durability ceiling over the content baseline.</summary>
        public const float MasterworkDurabilityMultiplier = 1.25f;

        // ── Armor plates (§13.5) and corpses (§14.7) ─────────────────────────────────
        /// <summary>Plate HP grows this much per enemy level above 1.</summary>
        public const float PlatePerLevel = 0.15f;
        /// <summary>Player actions a corpse lies on the board before it rots away.</summary>
        public const int CorpseDecayActions = 6;
        /// <summary>Gravestone/bone-pile HP as a share of the slain owner's MaxHP (§14.5, GG-T261): a marker to break,
        /// not a second body; at the owner's own level they out-lasted the party in every Veshker Ruins wave.</summary>
        public const float RemainsHpFraction = 0.25f;
        /// <summary>Remains rise on this many of their own turns, not the first (GG-T261): every action runs the clock to
        /// the next enemy turn (§2.0), so a marker left by the last foe standing used to rise inside the killing action.</summary>
        public const int RemainsTurnsToRise = 3;

        // ── Role identity (§13.1.5) ─────────────────────────────────────────────────
        public const float CasterPowerPerPoint = 0.035f;
        public const float CasterPowerMin = 0.7f;
        public const float CasterPowerMax = 1.6f;

        // ── Pickups (§13.3.1) ──────────────────────────────────────────────────────
        /// <summary>Chance a fallen enemy leaves a pickup…</summary>
        public const float PickupDropChance = 0.2f;
        /// <summary>…plus this per point of the killer's Luck…</summary>
        public const float PickupDropChancePerLuck = 0.004f;
        /// <summary>…capped here.</summary>
        public const float PickupMaxChance = 0.4f;
        /// <summary>Player actions a pickup stays on the board before it fades.</summary>
        public const int PickupLifetimeActions = 4;
        /// <summary>Never more than this many pickups on the board at once.</summary>
        public const int MaxPickups = 3;

        /// <summary>Knockback into another unit: both take this (+ half the blow).</summary>
        public const int KnockbackCrashDamage = 8;
        /// <summary>Knockback into stone or the board edge: the target takes this (+ the full blow).</summary>
        public const int KnockbackSlamDamage = 18;
    }
}
