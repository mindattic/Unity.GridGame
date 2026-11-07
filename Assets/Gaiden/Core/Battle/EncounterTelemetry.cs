using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Bot;
using GridGame.Core.Content;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// GG-T59: per-encounter telemetry. Watches one battle's event stream (every command's events, fed through
    /// <see cref="Observe"/>) and builds the single structured summary record the telemetry session appends once
    /// the encounter is over (<see cref="Build"/>) — one JSONL line per encounter: seed, location, level band, each
    /// wave's family and enemy classes, the party's classes/levels/gear score, difficulty tags, outcome, and the
    /// per-wave/per-hero/per-ability tallies. Engine-free and clock-agnostic: the host passes its own real-time
    /// seconds, so the same accumulator runs under the Unity view or a headless soak.
    ///
    /// Damage by ability is attributed from event context, since <see cref="DamagedEvent"/> carries no ability id:
    /// the most recent <see cref="AbilityResolvedEvent"/> (its ability id), <see cref="AttackEvent"/> ("attack" /
    /// "support-attack"), <see cref="TrapTriggeredEvent"/> ("trap:id"), <see cref="BuffTickedEvent"/> ("buff:id")
    /// or <see cref="WeaponShatteredEvent"/> ("shatter") sets the key, and a turn or timeline boundary clears it
    /// (anything left over is "other").
    /// </summary>
    public sealed class EncounterTelemetry
    {
        /// <summary>The record's outcome when the battle was left before it resolved.</summary>
        public const string Stalled = "stalled";

        private readonly ContentDb db;
        private readonly EncounterRecord record;
        private readonly double startSeconds;
        private readonly Dictionary<int, UnitInfo> units = new Dictionary<int, UnitInfo>();
        private readonly Dictionary<int, HeroTally> heroes = new Dictionary<int, HeroTally>();
        private readonly Dictionary<int, WaveTally> waves = new Dictionary<int, WaveTally>();
        private int currentWave;
        private string context;
        private bool built;

        private sealed class UnitInfo { public string ClassId; public Team Team; public bool IsHero; }

        public EncounterTelemetry(ContentDb db, BattleSetup setup, BattleState state, IEnumerable<BattleEvent> initial,
            IDictionary<string, object> difficultyTags, double nowSeconds)
        {
            this.db = db;
            startSeconds = nowSeconds;
            var stage = state.Stage ?? setup?.Stage;
            record = new EncounterRecord
            {
                Seed = setup?.Seed ?? 0,
                StageId = stage?.Id,
                Location = stage?.Name,
                Theme = stage?.Theme,
                RecommendedLevel = setup?.RecommendedLevel ?? 1,
                StageLevel = stage?.LevelOverride,
                NewGamePlus = setup?.NewGamePlus ?? 0,
                TotalWaves = stage?.Waves.Count ?? 0,
                Difficulty = difficultyTags != null ? new Dictionary<string, object>(difficultyTags) : new Dictionary<string, object>(),
            };

            // The planned waves (what the stage authored/generated); actual spawns are filled in as each wave starts.
            if (stage != null)
            {
                for (int i = 0; i < stage.Waves.Count; i++)
                {
                    var w = stage.Waves[i];
                    var classes = w.Enemies.Select(e => e.Class).ToList();
                    var levels = w.Enemies.Select(e => e.Level).ToList();
                    waves[i] = new WaveTally
                    {
                        Index = i,
                        Group = w.Group,
                        Families = Families(classes),
                        EnemyClasses = classes,
                        EnemyLevels = levels,
                    };
                }
            }

            Remember(state);
            foreach (var u in state.Units.Where(u => u.IsHero))
            {
                heroes[u.Id] = new HeroTally
                {
                    UnitId = u.Id,
                    ClassId = u.Def?.Id,
                    Level = u.Level,
                    GearScore = GearScore(u),
                    MaxHp = u.MaxHp,
                    HpStart = u.Hp,
                };
            }

            currentWave = state.Wave;
            Wave(currentWave).StartedSeconds = 0;
            Wave(currentWave).Started = true;
            if (initial != null) Tally(initial, state, nowSeconds);
        }

        /// <summary>Folds one applied command's events into the tallies. <paramref name="state"/> is the state after it.</summary>
        public void Observe(IEnumerable<BattleEvent> events, BattleState state, double nowSeconds)
        {
            if (built) return;
            Remember(state);
            Wave(currentWave).Turns++;
            record.Turns++;
            Tally(events, state, nowSeconds);
        }

        /// <summary>The finished record. <paramref name="outcome"/> null = read it from the battle's phase
        /// (victory / defeat / escaped / retreated, or <see cref="Stalled"/> if it never resolved).</summary>
        public EncounterRecord Build(BattleState state, double nowSeconds, string outcome = null)
        {
            Remember(state);
            built = true;
            double elapsed = Math.Max(0, nowSeconds - startSeconds);
            record.Outcome = outcome ?? OutcomeOf(state);
            record.Waves = waves.Values.OrderBy(w => w.Index).ToList();
            foreach (var w in record.Waves)
                if (w.Started && w.EndedSeconds == null) w.Seconds = Round(elapsed - w.StartedSeconds);
            record.WavesCleared = record.Waves.Count(w => w.Cleared);
            record.WaveReached = state.Wave + 1;
            record.Seconds = Round(elapsed);
            record.Ticks = state.Results?.TicksElapsed ?? 0;
            record.Actions = state.Results?.ActionsTaken ?? 0;
            foreach (var h in heroes.Values)
            {
                var u = state.UnitById(h.UnitId);
                h.HpLeft = u != null && u.Alive ? u.Hp : 0;
                if (u != null) h.MaxHp = u.MaxHp;
                h.Alive = u != null && u.Alive;
            }
            record.Party = heroes.Values.OrderBy(h => h.UnitId).ToList();
            record.PartyGearScore = Round(record.Party.Sum(h => h.GearScore));
            record.PartyLevelMin = record.Party.Count > 0 ? record.Party.Min(h => h.Level) : 0;
            record.PartyLevelMax = record.Party.Count > 0 ? record.Party.Max(h => h.Level) : 0;
            var enemyLevels = record.Waves.SelectMany(w => w.EnemyLevels).ToList();
            record.LevelBandMin = enemyLevels.Count > 0 ? enemyLevels.Min() : 0;
            record.LevelBandMax = enemyLevels.Count > 0 ? enemyLevels.Max() : 0;
            return record;
        }

        public static string OutcomeOf(BattleState state)
        {
            switch (state.Phase)
            {
                case BattlePhase.Victory: return "victory";
                case BattlePhase.Defeat: return "defeat";
                case BattlePhase.Escaped: return "escaped";
                case BattlePhase.Retreated: return "retreated";
                default: return Stalled;
            }
        }

        /// <summary>A hero's gear score: <see cref="MetaHeuristics.Score"/> summed over its weapon, armor and equipped items.</summary>
        public float GearScore(Unit u)
        {
            if (db == null || u == null) return 0f;
            float s = MetaHeuristics.Score(db.TryItem(u.WeaponId));
            foreach (var piece in u.Gear.Values) s += MetaHeuristics.Score(db.TryItem(piece?.ItemId));
            return Round(s);
        }

        private void Tally(IEnumerable<BattleEvent> events, BattleState state, double nowSeconds)
        {
            int heroChain = 0, enemyChain = 0;
            foreach (var e in events)
            {
                switch (e)
                {
                    case TurnStartedEvent _:
                    case TimelineAdvancedEvent _:
                    case PlayerControlEvent _:
                        context = null;
                        break;
                    case AbilityResolvedEvent ar: context = ar.AbilityId; break;
                    case AttackEvent at: context = at.IsSupport ? "support-attack" : "attack"; break;
                    case TrapTriggeredEvent tt: context = "trap:" + tt.TrapId; break;
                    case BuffTickedEvent bt: context = "buff:" + bt.BuffId; break;
                    case PincerFormedEvent p:
                        if (p.Team == Team.Hero) { record.Pincers.Hero++; heroChain = Math.Max(heroChain, p.ChainIndex + 1); }
                        else if (p.Team == Team.Enemy) { record.Pincers.Enemy++; enemyChain = Math.Max(enemyChain, p.ChainIndex + 1); }
                        Wave(currentWave).Pincers++;
                        break;
                    case DamagedEvent d: Damage(d); break;
                    case HealedEvent h:
                        if (heroes.TryGetValue(h.TargetId, out var healed)) healed.HealingReceived += h.Amount;
                        if (heroes.TryGetValue(h.SourceId, out var healer)) healer.HealingDone += h.Amount;
                        break;
                    case DiedEvent died:
                        if (heroes.TryGetValue(died.UnitId, out var fallen)) { fallen.Deaths++; record.HeroDeaths++; }
                        else if (died.Team == Team.Enemy) { record.EnemyDeaths++; Wave(currentWave).Kills++; }
                        break;
                    case RevivedEvent rv:
                        if (heroes.TryGetValue(rv.UnitId, out var revived)) revived.Revives++;
                        break;
                    case OrbsSpentEvent os: if (!os.Drained) record.ManaSpent += os.Count; break; // GG-T332: a siphon isn't spending
                    case ManaChargedEvent mc: record.ManaCharged += mc.Amount; break;
                    case WeaponShatteredEvent ws:
                        context = "shatter";
                        record.WeaponShatters.Add(new WeaponShatter { UnitId = ws.UnitId, ClassId = ClassOf(ws.UnitId), WeaponId = ws.WeaponId, Wave = currentWave });
                        break;
                    case ItemUsedEvent iu:
                        if (iu.ItemId != null) record.ItemsUsed[iu.ItemId] = (record.ItemsUsed.TryGetValue(iu.ItemId, out var n) ? n : 0) + 1;
                        if (heroes.TryGetValue(iu.UserId, out var user)) user.ItemsUsed++;
                        break;
                    case WaveClearedEvent wc:
                        {
                            var w = Wave(wc.Wave);
                            w.Cleared = true;
                            w.EndedSeconds = Round(nowSeconds - startSeconds);
                            w.Seconds = Round(w.EndedSeconds.Value - w.StartedSeconds);
                            w.EndClock = state.Clock;
                        }
                        break;
                    case WaveStartedEvent ws2:
                        {
                            currentWave = ws2.Wave;
                            var w = Wave(ws2.Wave);
                            w.Started = true;
                            w.StartedSeconds = Round(nowSeconds - startSeconds);
                            w.StartClock = state.Clock;
                            var spawned = ws2.Spawned.Select(id => state.UnitById(id)).Where(u => u != null).ToList();
                            if (spawned.Count > 0)
                            {
                                w.EnemyClasses = spawned.Select(u => u.Def?.Id).ToList();
                                w.EnemyLevels = spawned.Select(u => u.Level).ToList();
                                w.Families = Families(w.EnemyClasses);
                            }
                        }
                        break;
                }
            }
            if (heroChain > 0) record.Pincers.HeroChains.Add(heroChain);
            if (enemyChain > 0) record.Pincers.EnemyChains.Add(enemyChain);
            record.Pincers.LongestHeroChain = Math.Max(record.Pincers.LongestHeroChain, heroChain);
            record.Pincers.LongestEnemyChain = Math.Max(record.Pincers.LongestEnemyChain, enemyChain);
        }

        private void Damage(DamagedEvent d)
        {
            if (d.Hit == HitKind.Miss || d.Amount <= 0) return;
            string key = context ?? "other";
            bool byHero = heroes.ContainsKey(d.SourceId);
            bool onHero = heroes.TryGetValue(d.TargetId, out var target);
            var wave = Wave(currentWave);
            if (byHero && !onHero)
            {
                var src = heroes[d.SourceId];
                src.DamageDealt += d.Amount;
                Add(src.DealtByAbility, key, d.Amount);
                Ability(key).Dealt += d.Amount;
                record.DamageDealt += d.Amount;
                wave.DamageDealt += d.Amount;
            }
            if (onHero)
            {
                target.DamageTaken += d.Amount;
                Add(target.TakenByAbility, key, d.Amount);
                Ability(key).Taken += d.Amount;
                record.DamageTaken += d.Amount;
                wave.DamageTaken += d.Amount;
            }
        }

        private AbilityTally Ability(string key)
        {
            if (!record.ByAbility.TryGetValue(key, out var t)) record.ByAbility[key] = t = new AbilityTally();
            return t;
        }

        private static void Add(Dictionary<string, int> map, string key, int amount) =>
            map[key] = (map.TryGetValue(key, out var n) ? n : 0) + amount;

        private WaveTally Wave(int index)
        {
            if (!waves.TryGetValue(index, out var w)) waves[index] = w = new WaveTally { Index = index };
            return w;
        }

        private void Remember(BattleState state)
        {
            foreach (var u in state.Units)
                if (!units.ContainsKey(u.Id)) units[u.Id] = new UnitInfo { ClassId = u.Def?.Id, Team = u.Team, IsHero = u.IsHero };
        }

        private string ClassOf(int unitId) => units.TryGetValue(unitId, out var i) ? i.ClassId : null;

        private List<string> Families(IEnumerable<string> classIds) =>
            classIds.Select(id => db?.TryClass(id)?.DropTable).Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static float Round(double v) => (float)Math.Round(v, 2);
    }

    /// <summary>One encounter's telemetry summary (GG-T59) — serialized as-is into the session's JSONL.</summary>
    public sealed class EncounterRecord
    {
        public int Seed;
        public string StageId;
        public string Location;
        public string Theme;
        public int? StageLevel;
        public int RecommendedLevel;
        public int NewGamePlus;
        public int LevelBandMin;
        public int LevelBandMax;
        public Dictionary<string, object> Difficulty = new Dictionary<string, object>();
        /// <summary>victory / defeat / escaped / retreated / stalled.</summary>
        public string Outcome;
        public int TotalWaves;
        public int WavesCleared;
        public int WaveReached;
        public int Turns;
        public int Actions;
        public int Ticks;
        public float Seconds;
        public List<WaveTally> Waves = new List<WaveTally>();
        public List<HeroTally> Party = new List<HeroTally>();
        public float PartyGearScore;
        public int PartyLevelMin;
        public int PartyLevelMax;
        public int HeroDeaths;
        public int EnemyDeaths;
        public int DamageDealt;
        public int DamageTaken;
        public Dictionary<string, AbilityTally> ByAbility = new Dictionary<string, AbilityTally>();
        public PincerTally Pincers = new PincerTally();
        public int ManaSpent;
        public int ManaCharged;
        public List<WeaponShatter> WeaponShatters = new List<WeaponShatter>();
        public Dictionary<string, int> ItemsUsed = new Dictionary<string, int>();
    }

    public sealed class WaveTally
    {
        public int Index;
        public string Group;
        public List<string> Families = new List<string>();
        public List<string> EnemyClasses = new List<string>();
        public List<int> EnemyLevels = new List<int>();
        public bool Started;
        public bool Cleared;
        public int Turns;
        public float StartedSeconds;
        public float? EndedSeconds;
        public float Seconds;
        public long StartClock;
        public long EndClock;
        public int Kills;
        public int Pincers;
        public int DamageDealt;
        public int DamageTaken;
    }

    public sealed class HeroTally
    {
        public int UnitId;
        public string ClassId;
        public int Level;
        public float GearScore;
        public int MaxHp;
        public int HpStart;
        public int HpLeft;
        public bool Alive;
        public int Deaths;
        public int Revives;
        public int DamageDealt;
        public int DamageTaken;
        public int HealingDone;
        public int HealingReceived;
        public int ItemsUsed;
        public Dictionary<string, int> DealtByAbility = new Dictionary<string, int>();
        public Dictionary<string, int> TakenByAbility = new Dictionary<string, int>();
    }

    public sealed class AbilityTally
    {
        /// <summary>Damage heroes dealt with it.</summary>
        public int Dealt;
        /// <summary>Damage heroes took from it.</summary>
        public int Taken;
    }

    public sealed class PincerTally
    {
        public int Hero;
        public int Enemy;
        /// <summary>One entry per command that formed any hero pincer: the chain length it reached.</summary>
        public List<int> HeroChains = new List<int>();
        public List<int> EnemyChains = new List<int>();
        public int LongestHeroChain;
        public int LongestEnemyChain;
    }

    public sealed class WeaponShatter
    {
        public int UnitId;
        public string ClassId;
        public string WeaponId;
        public int Wave;
    }
}
