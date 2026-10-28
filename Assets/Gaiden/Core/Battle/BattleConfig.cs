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
        public const int BarLength = 1000;                 // u = ip / BarLength
        public const float ZoneU = 0.35f;                  // Prepare/Pushback Zone width
        public const int ZoneStart = (int)(BarLength * (1f - ZoneU)); // 650
        /// <summary>Open-track IP per tick per point of Speed (Speed 10 → 16 IP/tick → 4 s to the Zone).</summary>
        public const float OpenRatePerSpeed = 1.6f;
        /// <summary>Shared Zone crawl: every icon crosses the Zone in ZoneCrossingTicks regardless of Speed.</summary>
        public const int ZoneCrossingTicks = 30;
        /// <summary>Reference pace (Speed 10) used to draw cast icons on the shared u-axis.</summary>
        public const float ReferencePacePerTick = 16f;
        /// <summary>Game-clock time one committed hero action costs.</summary>
        public const int ActionTicks = 20;
        /// <summary>Max random head-start IP for icons at wave spawn (staggered entry).</summary>
        public const int SpawnStaggerIp = 250;

        // ── Pushback (§2.3 / §2.7, owner direction 2026-10-11: monsters almost never reached ACT — a pincer's
        //    hits could each re-trigger pushback on the same in-zone target, repeatedly erasing its progress) ──
        public const float PushbackBaseU = 0.07f;
        public const float PushbackMaxU = 0.15f;
        public const int BaseStunTicks = 4;

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

        // ── Steal (§6 stage 8) ──────────────────────────────────────────────────────
        public const float StealDivisor = 50f;

        // ── Weapon durability (§24.5) ───────────────────────────────────────────────
        public const float ShatterTargetMultiplier = 1.5f;
        public const float ShatterSelfDamageFraction = 0.15f;

        // ── Enemy AI (§14.1) ────────────────────────────────────────────────────────
        public const float RetreatHpThreshold = 0.30f;

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
        /// <summary>Charge for a pincer that takes away a whole enemy's worth of HP (scaled by the share removed).</summary>
        public const float PincerChargePerHp = 120f;
        /// <summary>Each participant beyond the two attackers raises the divisor by this much (2 heroes ×1, 4 heroes ×2).</summary>
        public const float ChargeParticipantPenalty = 0.5f;
        /// <summary>Each link deeper in a chain earns this much more (chain ×2 → ×1.25, ×3 → ×1.5).</summary>
        public const float ChargeChainBonus = 0.25f;
        /// <summary>Any pincer that lands at least this much (chip damage on a giant still counts for something).</summary>
        public const int MinPincerCharge = 2;
        public const int CritCharge = 12;
        public const int InterruptCharge = 25;
        public const int AmplifierCharge = 30;

        // ── Armor enchant passives (GG-T179): granted by an intact enchanted armor piece ──────────────────────────
        /// <summary>Regenerate: percent of MaxHP knit back (an enemy at the end of its turn, a hero each time control returns).</summary>
        public const int RegeneratePercent = 6;
        /// <summary>Thorns: share of a landed enemy hit reflected back at the attacker (min 1).</summary>
        public const float ThornsFraction = 0.25f;
        /// <summary>Mana Siphon: charge each landed hit by the wearer adds to the meter.</summary>
        public const int ManaSiphonCharge = 5;

        /// <summary>Global weight on enemy physical strikes (difficulty knob; tuned with BalanceReport after the brains, §14.6).</summary>
        public const float EnemyStrikeMultiplier = 0.9f;

        // ── Masterwork crafting (§24.6.2, GG-T46) ────────────────────────────────────
        /// <summary>A Masterwork piece's durability ceiling over the content baseline.</summary>
        public const float MasterworkDurabilityMultiplier = 1.25f;

        // ── Armor plates (§13.5) and corpses (§14.7) ─────────────────────────────────
        /// <summary>Plate HP grows this much per enemy level above 1.</summary>
        public const float PlatePerLevel = 0.15f;
        /// <summary>Player actions a corpse lies on the board before it rots away.</summary>
        public const int CorpseDecayActions = 6;

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
