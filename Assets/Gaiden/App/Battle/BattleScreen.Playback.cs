using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using GridGame.Meta;
using MindAttic.Capture;
using MindAttic.Capture.Unity;
using MindAttic.Telemetry.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    public sealed partial class BattleScreen
    {
        private int chainCount;
        /// <summary>Pincers played so far in the current event list (automation: "mid-chain").</summary>
        public int ChainCount => playing ? chainCount : 0;
        private float playStarted;

        /// <summary>
        /// Plays an event list back, one awaited beat each (bible: no instant anythings). The simulation
        /// already resolved everything; if any animation is skipped or interrupted, the final SyncAll
        /// snaps every view to the true state.
        /// </summary>
        private IEnumerator Play(List<BattleEvent> events, bool skipMovePrefix)
        {
            playing = true;
            playStarted = battleTime;
            chainCount = 0;
            int i = 0;
            // The drag already showed the hero's walk live: the hero follows the finger and ApplyPreview slid every
            // displaced actor to where the whole walk leaves it. So the walk's own moves (the hero's steps, actors it
            // displaced) are not replayed; only their side effects are (traps, pickups, Sleep breaking). Every token
            // the walk touched is snapped to where the walk — not the whole command — leaves it: an enemy that moves
            // again later in this command (a drag can run a full enemy turn via AdvanceClock) starts that move from
            // its true post-walk tile, and no token is ever drawn on a tile another one holds at that moment, which a
            // partial replay (hero mid-walk, displaced actors already at their preview tiles) used to do (GG-T97).
            int walkHero = -1, walkEnd = 0;
            var walkAt = new Dictionary<int, Coord>();
            if (skipMovePrefix)
            {
                var touched = new HashSet<int>();
                for (walkEnd = 0; walkEnd < events.Count && !EndsWalk(events[walkEnd]); walkEnd++)
                {
                    switch (events[walkEnd])
                    {
                        case ActionBeganEvent ab when walkHero < 0: walkHero = ab.ActorId; touched.Add(ab.ActorId); break;
                        case SteppedEvent se when se.UnitId == walkHero: walkAt[se.UnitId] = se.To; break;
                        case DisplacedEvent de when de.ByUnitId == walkHero: touched.Add(de.UnitId); walkAt[de.UnitId] = de.To; break;
                        case StepBlockedEvent sb: touched.Add(sb.UnitId); break;
                    }
                }
                foreach (var id in touched)
                    if (board.Units.TryGetValue(id, out var v) && !v.Dead) board.Place(v, walkAt.TryGetValue(id, out var at) ? at : v.Tile);
            }
            for (; i < events.Count; i++)
            {
                // Within the already-shown walk, its moves are no-ops (their tokens already stand at the walk's end).
                bool shownLive = i < walkEnd && ((events[i] is SteppedEvent ws && ws.UnitId == walkHero) || (events[i] is DisplacedEvent wd && wd.ByUnitId == walkHero) || events[i] is StepBlockedEvent);
                if (shownLive) continue;
                // GG-T126: no beat starts while the pause menu is up (a beat's side effects — banners, tallies,
                // outlines — run the moment Animate is called, before its first yield).
                while (Tween.Paused) yield return null;
                PlaybackBeats++;
                // POL-E5: a pincer's hits land as Major; an ability's at its own tier (AbilityAnim); a new action resets.
                if (events[i] is ActionBeganEvent || events[i] is TurnStartedEvent) hitTier = VfxTier.Minor;
                else if (events[i] is PincerFormedEvent) hitTier = VfxTier.Major;
                IEnumerator anim = null;
                try { anim = Animate(events[i]); }
                catch (System.Exception e) { Debug.LogException(e); }
                if (anim != null) yield return Safe(anim);
                // GG-T97: once a beat ends no two tokens may be drawn on one tile. The one legitimate in-between is a
                // swap still finishing — the stepper has arrived and its partner's DisplacedEvent (or the amplifier's
                // shove) is the very next beat — so the check waits for that beat.
                bool swapPending = i + 1 < events.Count && (events[i + 1] is DisplacedEvent || events[i + 1] is AmplifierMovedEvent || events[i + 1] is PickupMovedEvent);
                // GG-T228: a unit token may sit on a pickup token only while the step that shoves the pickup is still
                // playing out (a snake's segments follow before its head's pickup goes behind the tail, a 2×2's lanes
                // shove in turn), so the unit-on-pickup check waits until no move beat is next.
                bool movePending = swapPending || (i + 1 < events.Count && (events[i + 1] is SteppedEvent || events[i + 1] is TeleportedEvent));
                if (!swapPending) BoardAudit.Beat(AuditTokens(false, unitsOnItems: !movePending), events[i]);
            }
            board.Lines.ClearLines();
            PruneGhosts();
            pendingItemUser = -1; pendingItemId = null;
            // Before the safety-net snap: did playback leave every token where the state says it stands? A miss here
            // means a beat was skipped or played out of order (the snap below would hide it as a jump).
            BoardAudit.Settled(AuditTokens(true));
            SyncAll();
            BoardAudit.Synced(AuditTokens(true, rendered: true));
            while (Tween.Paused) yield return null;
            playing = false;
            if (State.IsOver && !ended) yield return EndBattle();
        }

        /// <summary>The first event after a hero's walk (MoveCommand's WalkPath): pincers, the clock, or the end of the action.</summary>
        private static bool EndsWalk(BattleEvent e) =>
            e is PincerFormedEvent || e is RespiteEvent || e is TimelineAdvancedEvent || e is TurnStartedEvent
            || e is WaveClearedEvent || e is BattleEndedEvent || e is PlayerControlEvent || e is AbilityRefusedEvent;

        private float Fx => ReduceMotion ? 0f : 1f;

        /// <summary>Runs a (possibly nested) animation; an exception ends that beat but never the playback.</summary>
        private static IEnumerator Safe(IEnumerator e)
        {
            while (true)
            {
                // GG-T126: a beat never steps while paused, even across a bare one-frame yield.
                while (Tween.Paused) yield return null;
                object cur;
                try
                {
                    if (!e.MoveNext()) yield break;
                    cur = e.Current;
                }
                catch (System.Exception ex) { Debug.LogException(ex); yield break; }
                if (cur is IEnumerator nested) yield return Safe(nested);
                else yield return cur;
            }
        }

        private IEnumerator Animate(BattleEvent e)
        {
            switch (e)
            {
                case ActionBeganEvent a when a.ActorId >= 0 && a.Label != "Move":
                    ShowBanner($"{State.UnitById(a.ActorId)?.Name}: {a.Label}");
                    return Tween.Wait(Timing.Quick);
                case ActionBeganEvent a when a.Label == "Shield":
                    ShowBanner("Shields up!");
                    App.Audio.Sfx("battle.cast");
                    return Tween.Wait(Timing.Standard);

                case SteppedEvent s: return StepAnim(s.UnitId, s.To, 0.16f, "battle.move");
                case DisplacedEvent d: return StepAnim(d.UnitId, d.To, 0.14f, "battle.slide");
                case TeleportedEvent t: return TeleportAnim(t);

                case PincerFormedEvent p: FlushTallies(); return PincerAnim(p);
                case RespiteEvent rs: return RespiteAnim(rs);
                case AttackEvent at: return AttackAnim(at);
                case SupportActionEvent sa: return SupportAnim(sa);
                case DamagedEvent dm: return DamageAnim(dm);
                case HealedEvent h: return HealAnim(h);
                case DiedEvent d: EndGhost(d.UnitId, null); return DeathAnim(d);
                case LootEvent l: return LootAnim(l);
                case RemainsLeftEvent rl: return RemainsAnim(rl.RemainsId, rl.At, false);
                case GuestJoinedEvent gj: return GuestAnim(gj);
                case RecruitableAppearedEvent rap: return RecruitableAppearedAnim(rap);
                case RecruitOfferedEvent rof: return RecruitOfferedAnim(rof);
                case RecruitJoinedEvent rj: return RecruitJoinedAnim(rj);
                case RoseAgainEvent ra: return RiseAnim(ra);
                case AmplifierSpawnedEvent asp: return AmpSpawnAnim(asp);
                case AmplifierMovedEvent am: return AmpMoveAnim(am);
                case PickupMovedEvent pm: return PickupMoveAnim(pm);
                case AmplifierUsedEvent au: return AmpUsedAnim(au);
                case ManaChargedEvent mc: return ChargeAnim(mc);
                case OrbMintedEvent o: if (o.Reason == "pincer") FlushTallies(); return OrbAnim(o);
                case OrbsSpentEvent os: SyncOrbs(os.CountAfter); App.Audio.Sfx("battle.cast", 0.6f); return Tween.Wait(Timing.Quick);

                case TimelineAdvancedEvent ta: FlushTallies(); return TimelineAnim(ta);
                case PushedBackEvent pb: return PushAnim(pb);
                case HastenedEvent hs: return HastenAnim(hs);
                case TurnStartedEvent ts: FlushTallies(); return TurnAnim(ts);
                case TurnSkippedEvent sk: return FloatText(sk.UnitId, sk.Reason == "asleep" ? "Zzz…" : "…", new Color(0.7f, 0.8f, 1f), 0.35f);

                case CastQueuedEvent cq: return CastQueuedAnim(cq);
                case CastStaggeredEvent cs: return FloatText(cs.CasterId, cs.Resisted ? "Steady!" : "Stagger!", Palette.Gold, 0.25f);
                case CastCancelledEvent cc: castGhosts.Remove(cc.EntryId); SyncTimeline(null); board.Lines.ClearLines(); board.ClearMarks(); return FloatText(cc.CasterId, cc.Reason == "interrupted" ? "Interrupted!" : "Fizzled", new Color(1f, 0.6f, 0.3f), 0.35f);
                case ClutchEvent cl: return ClutchAnim(cl);
                case AbilityResolvedEvent ar: FlushTallies(); return AbilityAnim(ar);
                case AbilityRefusedEvent rf: App.Audio.Sfx("ui.error"); ShowBanner(rf.Reason); return null;
                case ItemUsedEvent iu: pendingItemUser = iu.UserId; pendingItemId = iu.ItemId; ShowBanner($"{App.Content.TryItem(iu.ItemId)?.Name} ({iu.ChargesLeft} left)"); return Tween.Wait(Timing.Instant);
                case PlateHitEvent ph: return PlateHitAnim(ph);
                case PlateBrokenEvent pbk: return PlateBrokenAnim(pbk);
                case FacingChangedEvent fc: { var fu = State.UnitById(fc.UnitId); if (fu != null && board.Units.TryGetValue(fu.Id, out var fv)) { fv.SetPlate(fc.Facing, fu.PlateHp, fu.PlateMax); App.StartCoroutine(Tween.Pop(fv.Plate, 1.2f, 0.18f)); } return null; }
                case PickupDroppedEvent pd: return PickupDropAnim(pd);
                case PickupCollectedEvent pc: return PickupCollectAnim(pc);
                case PickupExpiredEvent pe: return PickupExpiredAnim(pe);

                case BuffAppliedEvent ba: return InflictAnim(ba);
                case BuffRemovedEvent br: RefreshUnit(br.UnitId); return br.Reason == "steam" ? FloatText(br.UnitId, "Steam!", Palette.White, 0.25f) : null;
                case BuffTickedEvent bt: RefreshUnit(bt.UnitId); return null;
                case AnnounceEvent an: ShowBanner(an.Text); return Tween.Wait(Timing.Standard);
                case ScannedEvent sc: return ScanAnim(sc);
                case TrapPlacedEvent tp: board.SyncTraps(State); App.Audio.Sfx("battle.trap"); return FloatText(tp.ById, "Snare!", Palette.Boss, 0.3f);
                case TrapTriggeredEvent tt: board.SyncTraps(State); App.Audio.Sfx("battle.trap"); return FloatText(tt.UnitId, "Trap!", Palette.Boss, 0.25f);
                case WeaponWornEvent _: return null;
                case WeaponShatteredEvent ws: App.Audio.Sfx("battle.shatter"); return FloatText(ws.UnitId, "SHATTER!", Palette.Buff, 0.5f, 80);
                case ArmorBrokenEvent ab: App.Audio.Sfx("battle.shatter", 0.7f); return FloatText(ab.UnitId, "Armor broken!", new Color(1f, 0.6f, 0.4f), 0.4f, 64);
                case WeaponSwappedEvent sw: App.Audio.Sfx("battle.dash"); SyncBar(); return FloatText(sw.UnitId, "⭾ " + (App.Content.TryItem(sw.ToWeapon)?.Name ?? "Swap"), Palette.Info, 0.3f, 54);
                case BossPhaseEvent bp: return BossAnim(bp);
                case RuleBrokenEvent rb: return RuleBreakAnim(rb);
                case WaveStartedEvent wv: TutorialWaveStarted(); return WaveAnim(wv);
                case ReinforcementsArrivedEvent ra: return ReinforcementsAnim(ra);
                case EggLaidEvent el: return EggLaidAnim(el);
                case EggHatchedEvent eh: return HatchAnim(eh);
                case WaveClearedEvent wc: FlushTallies(); ShowBanner("Wave cleared!"); return Tween.Wait(Timing.Emphasis);
                case BattleEndedEvent be: lastEnded = be; ShowBanner(be.Victory ? "VICTORY!" : be.Escaped ? "ESCAPED" : "DEFEAT"); App.Audio.Sfx(be.Victory ? "battle.victory" : be.Escaped ? "battle.dash" : "battle.defeat"); return Tween.Wait(Timing.Cinematic);
                case RevivedEvent rv: return ReviveAnim(rv);
                case PlayerControlEvent _: FlushTallies(); ShowBanner(""); SyncAll(); return null;
                case TileConflictEvent tc:
                    // The engine's last-resort net fired (GG-T97): a root-cause bug. Loud in every build's log; in a
                    // dev build also on screen, and counted in /state so the harness fails on it.
                    Debug.LogError($"[BattleRunner] {tc} — stage {State.Stage.Id}, wave {State.Wave}, clock {State.Clock}");
                    BoardAudit.Conflict(tc.ToString());
                    if (Debug.isDebugBuild) ShowBanner("TILE CONFLICT — see log");
                    return null;
            }
            return null;
        }

        /// <summary>
        /// GG-T97, the view half of "two actors never share a tile": the tiles the board currently draws tokens on.
        /// Always checks that no two live tokens overlap; with <paramref name="againstState"/> also that every living
        /// unit is drawn exactly where it stands (and nothing fallen is still drawn); with <paramref name="rendered"/>
        /// that each token's actual on-screen position is its tile (no tween left mid-way).
        /// </summary>
        /// <param name="unitsOnItems">Also check that no unit token is drawn on a pickup token's tile (GG-T228). Per beat
        /// only non-hero tokens count (a hero's collect beat can trail its step by a few beats); against the state every unit.</param>
        public List<string> AuditTokens(bool againstState, bool rendered = false, bool unitsOnItems = true)
        {
            var problems = new List<string>();
            var drawn = new Dictionary<Coord, int>();
            foreach (var kv in board.Units)
            {
                var v = kv.Value;
                if (v.Dead) continue;
                for (int dy = 0; dy < v.H; dy++)
                    for (int dx = 0; dx < v.W; dx++)
                    {
                        var t = new Coord(v.Tile.X + dx, v.Tile.Y + dy);
                        if (drawn.TryGetValue(t, out var other)) problems.Add($"tokens {other} and {kv.Key} both drawn on {t}");
                        else drawn[t] = kv.Key;
                    }
                if (rendered)
                {
                    var want = BoardView.PosOf(v.Tile);
                    float left = v.Root.style.left.value.value, top = v.Root.style.top.value.value;
                    if (Mathf.Abs(left - want.x) > 0.5f || Mathf.Abs(top - want.y) > 0.5f) problems.Add($"token {kv.Key} rendered at ({left:0},{top:0}), not on its tile {v.Tile}");
                }
            }
            if (againstState)
                foreach (var u in State.Units)
                {
                    bool live = board.Units.TryGetValue(u.Id, out var v) && !v.Dead;
                    if (u.Alive && !live) problems.Add($"{u} stands on {u.Pos} but isn't drawn");
                    else if (!u.Alive && live) problems.Add($"{u} has fallen but is still drawn on {v.Tile}");
                    else if (u.Alive && v.Tile != u.Pos) problems.Add($"{u} drawn on {v.Tile} but stands on {u.Pos}");
                }
            AuditItemTokens(problems, againstState);
            if (unitsOnItems)
                foreach (var kv in board.PickupViews)
                {
                    if (!(kv.Value.userData is Coord pat) || !drawn.TryGetValue(pat, out var unitId)) continue;
                    if (againstState || State.UnitById(unitId)?.IsHero != true) problems.Add($"unit {unitId} drawn on pickup {kv.Key} at {pat}");
                }
            return problems;
        }

        /// <summary>
        /// GG-T129, the view half of "at most one board item per tile, ever" (§1.1.3): no two item tokens (pickups, the
        /// Power Amplifier) drawn on one tile, and with <paramref name="againstState"/> no stale token — every drawn
        /// pickup is still on the board where the state has it, every pickup in the state is drawn, and the amplifier
        /// is drawn exactly where (and only while) it stands.
        /// </summary>
        private void AuditItemTokens(List<string> problems, bool againstState)
        {
            var items = new Dictionary<Coord, string>();
            void Drawn(Coord at, string what)
            {
                if (items.TryGetValue(at, out var other)) problems.Add($"items {other} and {what} both drawn on {at}");
                else items[at] = what;
            }
            foreach (var kv in board.PickupViews)
                if (kv.Value.userData is Coord pat) Drawn(pat, "pickup " + kv.Key);
            if (board.AmpTile.HasValue) Drawn(board.AmpTile.Value, "amplifier");
            if (!againstState) return;
            foreach (var kv in board.PickupViews)
            {
                var p = State.Pickups.FirstOrDefault(x => x.Id == kv.Key);
                if (p == null) problems.Add($"pickup {kv.Key} is gone but its token is still drawn");
                else if (kv.Value.userData is Coord pat && pat != p.At) problems.Add($"pickup {kv.Key} drawn on {pat} but lies on {p.At}");
            }
            foreach (var p in State.Pickups)
                if (!board.PickupViews.ContainsKey(p.Id)) problems.Add($"pickup {p.Id} lies on {p.At} but isn't drawn");
            if (board.AmpTile != State.Amplifier) problems.Add($"amplifier drawn on {board.AmpTile?.ToString() ?? "nothing"} but stands on {State.Amplifier?.ToString() ?? "nothing"}");
        }

        // ── Individual beats ──────────────────────────────────────────────────────

        private IEnumerator StepAnim(int unitId, Coord to, float seconds, string sfx)
        {
            if (!board.Units.TryGetValue(unitId, out var v)) yield break;
            var from = BoardView.PosOf(v.Tile);
            // GG-T219: a walking unit streaks; a displaced one is shown by its shove instead.
            bool trail = sfx == "battle.move" && v.Tile != to;
            if (trail) board.Trail.Follow(v.Root, State.UnitById(unitId)?.IsHero == true ? HeroTrail : EnemyTrail);
            v.Tile = to;
            App.Audio.Sfx(sfx, 0.6f);
            try { yield return Tween.Move(v.Root, from, BoardView.PosOf(to), seconds); }
            finally { if (trail) board.Trail.Release(v.Root); }
        }

        private IEnumerator TeleportAnim(TeleportedEvent t)
        {
            if (!board.Units.TryGetValue(t.UnitId, out var v)) yield break;
            App.Audio.Sfx("element.arcane");
            yield return Tween.Fade(v.Root, 1, 0, Timing.Quick);
            board.Place(v, t.To);
            yield return Tween.Fade(v.Root, 0, 1, Timing.Quick);
        }

        private IEnumerator PincerAnim(PincerFormedEvent p)
        {
            chainCount++;
            supportersShown.Clear();
            var a = State.UnitById(p.Attacker1); var b = State.UnitById(p.Attacker2);
            if (a == null || b == null) yield break;
            board.Lines.ClearLines();
            var color = p.Team == Team.Hero ? Palette.Gold.WithA(0.95f) : new Color(1f, 0.3f, 0.3f, 0.95f);
            // Every coordinate below is the event's formation-time snapshot. State is already the END of the command,
            // so reading positions from it stretched the outline across the board whenever a member moved later in the
            // same command (GG-T98: "a gold box ~3×3 tiles"). Views that drifted from the snapshot are put back on it.
            var aAt = p.Attacker1At; var bAt = p.Attacker2At;
            var oppAt = p.OpponentsAt;
            var geometryError = p.GeometryError();
            if (geometryError != null && Debug.isDebugBuild) Debug.LogError($"[GG-T98] malformed pincer outline: {geometryError} — {p}");
            foreach (var fp in new[] { aAt, bAt }.Concat(oppAt))
                if (board.Units.TryGetValue(fp.UnitId, out var fv) && !fv.Dead && fv.Tile != fp.At)
                {
                    if (Debug.isDebugBuild) Debug.LogWarning($"[GG-T98] view of unit {fp.UnitId} was on {fv.Tile}, pincer formed with it on {fp.At}");
                    board.Place(fv, fp.At);
                }
            DodgeBillboard(new[] { aAt, bAt }.Concat(oppAt).SelectMany(FootprintTiles));
            // The group (Hero, foe…, Hero) gets an outline that draws itself around it; supporters link edge to edge.
            var group = new List<(Coord, int, int)> { (aAt.At, aAt.W, aAt.H), (bAt.At, bAt.W, bAt.H) };
            foreach (var o in oppAt) group.Add((o.At, o.W, o.H));
            var rect = BoardView.GroupRect(group);
            var links = new List<(Vector2, Vector2)>();
            // GG-T187: a supporter links to the unit its chain runs through (an attacker or the next ally on the chain).
            var members = new List<UnitFootprint> { aAt, bAt };
            members.AddRange(p.SupportersAt);
            for (int si = 0; si < p.SupportersAt.Count; si++)
            {
                var s = p.SupportersAt[si];
                int linkId = si < p.SupporterLinks.Count ? p.SupporterLinks[si] : -1;
                var hit = members.Where(m => m.UnitId == linkId).ToList();
                var partner = hit.Count > 0 ? hit[0] : FootprintTiles(s).Any(t => t.IsCardinalNeighbor(aAt.At)) ? aAt : bAt;
                links.Add(BoardView.EdgeLink(s.At, partner.At));
            }
            App.Audio.Sfx("battle.pincer", 1f, 1f + 0.08f * (chainCount - 1));
            // The outline draws itself fully around the trapped line before anything else happens — a beat of
            // realization — so the pincer reads as "caught" before the cut-in slams in (owner request).
            yield return Tween.Run(ReduceMotion ? 0.05f : 0.24f, k =>
            {
                board.Lines.ClearLines();
                board.Lines.Outline(rect, color, 9f, k);
                if (k >= 0.6f) foreach (var (la, lb) in links) board.Lines.Add(la, Vector2.Lerp(la, lb, (k - 0.6f) / 0.4f), Palette.Mana.WithA(0.95f), 12f);
            });
            if (!ReduceMotion) yield return Tween.Wait(Timing.Quick);
            tallying = true;
            string label = p.Team == Team.Hero ? (chainCount > 1 ? $"CHAIN ×{chainCount}!" : "PINCER!") : "Enemy pincer!";
            Billboard($"{a.Name} & {b.Name} — {(chainCount > 1 ? $"Chain ×{chainCount}" : "Pincer")}", null, p.Team == Team.Hero ? new Color(1f, 0.9f, 0.5f) : new Color(1f, 0.55f, 0.5f));
            var supporters = p.Supporters.Select(State.UnitById).Where(x => x != null).ToList();
            if (supporters.Count > 0) Billboard("Support: " + string.Join(", ", supporters.Select(x => x.Name)), null, new Color(0.6f, 0.85f, 1f));
            ShowBanner(label);
            var mid = (BoardView.CenterOf(aAt.At) + BoardView.CenterOf(bAt.At)) / 2f;
            var f = board.Float(label, mid, color, 96);
            App.StartCoroutine(FloatAway(f, 0.7f));
            if (board.Units.TryGetValue(a.Id, out var av)) App.StartCoroutine(Tween.Pop(av.Root, 1.15f, 0.2f));
            if (board.Units.TryGetValue(b.Id, out var bv)) App.StartCoroutine(Tween.Pop(bv.Root, 1.15f, 0.2f));
            // Cut-in modes keep a multi-attacker frenzy readable: the full sweep on the first link (with the supporters
            // sliding up from the bottom), quick pop-up cards beside the attackers on every chained link.
            if (!CutInsEnabled || ReduceMotion) yield return Tween.Wait(Timing.Standard);
            else if (chainCount == 1)
            {
                yield return PincerCutIn(a, b, p.Team, aAt.At, bAt.At);
            }
            else yield return PopUpCards(new[] { a, b }, p.Team); // their views sit on the snapshot tiles (snapped above)
        }

        // ── Pincer cut-in (ported from GridGame2026 PortraitManager / Portrait2DInstance, US-145) ──
        // Both attackers' portraits sweep past in close-up lanes flanking the attacked line on the old
        // slide curve (fast in, slight overshoot, drift back, fast out) with hand-held sway, while the
        // board "camera" pushes in on the pincer and settles back afterwards.

        public int CutInsShown { get; private set; }
        private bool CutInsEnabled => App.Settings.PincerCutIns;

        private static readonly AnimationCurve CutInCurve = new AnimationCurve(
            new Keyframe(0.0f, -1.0f, 0f, 0f, 0f, 0f),
            new Keyframe(0.8f, 0.05202637f, 0f, 0f, 0.33333334f, 0.70263505f),
            new Keyframe(1.2f, -0.05f, 0f, 0f, 0.33333334f, 0.33322528f),
            new Keyframe(1.993103f, 1.0f, 0f, 0f, 0f, 0f));
        private const float CutInSpeed = 1.75f;
        private const float CutInStagger = 0.14f;
        private static float CutInSeconds => 1.993103f / CutInSpeed;

        /// <summary>Debug/automation: play the cut-in on the first two living heroes.</summary>
        public void DemoCutIn()
        {
            var heroes = State.Units.Where(u => u.IsHero && u.Alive).Take(2).ToList();
            if (heroes.Count == 2) App.StartCoroutine(PincerCutIn(heroes[0], heroes[1], Team.Hero, ViewTile(heroes[0]), ViewTile(heroes[1])));
        }

        /// <summary>Debug/automation: play the mid-wave surprise narrative beat (§14.8) on demand.</summary>
        public void DemoSurprise(string speakerClassId, string line)
        {
            App.StartCoroutine(SurpriseAnnounce(speakerClassId, line));
        }

        private sealed class CutInPortrait
        {
            public VisualElement El; public bool Horizontal; public float Lane; public float Dir; public float Delay;
            public float Distance; public float W, H; public float Seed;
        }

        /// <param name="aAt">Where <paramref name="a"/> stood when the pincer formed (not its end-of-command State tile).</param>
        private IEnumerator PincerCutIn(Unit a, Unit b, Team team, Coord aAt, Coord bAt)
        {
            CutInsShown++;
            var layer = new VisualElement { name = "cutin", pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
            layer.style.overflow = Overflow.Hidden;
            var dim = new VisualElement { pickingMode = PickingMode.Ignore };
            dim.style.position = Position.Absolute;
            dim.style.left = 0; dim.style.top = 0; dim.style.right = 0; dim.style.bottom = 0;
            dim.style.backgroundColor = team == Team.Hero ? new Color(0.02f, 0.03f, 0.08f) : new Color(0.12f, 0.0f, 0.02f);
            dim.style.opacity = 0f;
            layer.Add(dim);
            App.Overlay.Add(layer);

            var origin = new Vector2(BoardLeft, BoardTop);
            var mid = origin + (BoardView.CenterOf(aAt, a.W, a.H) + BoardView.CenterOf(bAt, b.W, b.H)) / 2f;
            bool rowLine = aAt.Y == bAt.Y;
            float StageW = GameApp.StageWidth, StageH = GameApp.StageHeight;

            // Entry pattern (old US-145): counter sweep, mirrored, same-side chase, staggered one-two.
            float d1 = Random.value < 0.5f ? 1f : -1f, d2 = -d1, delay2 = 0f;
            switch (Random.Range(0, 4))
            {
                case 2: d2 = d1; delay2 = CutInStagger; break;
                case 3: delay2 = CutInStagger; break;
            }

            var shots = new List<CutInPortrait>();
            Unit[] who = { a, b };
            for (int i = 0; i < 2; i++)
            {
                var tex = Art.Portrait(who[i].Def.Id);
                float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 0.7f;
                float h = rowLine ? 760f : 1040f, w = h * aspect;
                if (!rowLine && w > 540f) { w = 540f; h = w / aspect; }
                var el = new VisualElement { name = $"cutin-{who[i].Id}", pickingMode = PickingMode.Ignore };
                el.style.position = Position.Absolute;
                el.style.width = w; el.style.height = h;
                el.style.backgroundImage = new StyleBackground(tex);
                el.style.left = -w * 2; el.style.top = -h * 2;
                layer.Add(el);
                shots.Add(new CutInPortrait
                {
                    El = el, Horizontal = rowLine, Dir = i == 0 ? d1 : d2, Delay = i == 0 ? 0f : delay2, W = w, H = h,
                    Distance = rowLine ? StageW / 2f + w / 2f + 2f : StageH / 2f + h / 2f + 2f, Seed = Random.Range(0f, 100f),
                });
            }
            // Lanes flank the attacked line without covering it; at the board edge both shift into the open space.
            if (rowLine)
            {
                float gap = BoardView.Tile / 2f + 20f;
                float la = mid.y - gap - shots[0].H / 2f, lb = mid.y + gap + shots[1].H / 2f;
                float shift = Mathf.Max(0f, shots[0].H / 2f - la) - Mathf.Max(0f, lb + shots[1].H / 2f - StageH);
                shots[0].Lane = la + shift; shots[1].Lane = lb + shift;
            }
            else
            {
                float gap = BoardView.Tile / 2f + 10f;
                float la = mid.x - gap - shots[0].W / 2f, lb = mid.x + gap + shots[1].W / 2f;
                float shift = Mathf.Max(0f, shots[0].W / 2f - la) - Mathf.Max(0f, lb + shots[1].W / 2f - StageW);
                shots[0].Lane = la + shift; shots[1].Lane = lb + shift;
            }

            // The standard cut-in look (feathered team band with a bright core) runs along each portrait's lane.
            var bands = new List<VisualElement>();
            foreach (var shot in shots)
            {
                var band = new VisualElement { pickingMode = PickingMode.Ignore };
                band.style.position = Position.Absolute;
                band.style.backgroundImage = new StyleBackground(BandTex);
                var bc = BandColor(team);
                band.style.unityBackgroundImageTintColor = new Color(bc.r, bc.g, bc.b, 0.9f);
                float thick = (rowLine ? shot.H : shot.W) * 0.28f;
                var core = new VisualElement { pickingMode = PickingMode.Ignore };
                core.style.position = Position.Absolute;
                core.style.backgroundImage = new StyleBackground(BandTex);
                core.style.unityBackgroundImageTintColor = Palette.White.WithA(0.95f);
                if (rowLine) { core.style.left = 0; core.style.right = 0; core.style.top = Length.Percent(46); core.style.height = 5; }
                else { core.style.top = 0; core.style.bottom = 0; core.style.left = Length.Percent(46); core.style.width = 5; }
                band.Add(core);
                if (rowLine) { band.style.left = -200; band.style.width = StageW + 400; band.style.top = shot.Lane - thick / 2f; band.style.height = thick; band.style.rotate = new Rotate(shot.Dir * -3f); }
                else { band.style.top = -200; band.style.height = StageH + 400; band.style.left = shot.Lane - thick / 2f; band.style.width = thick; band.style.rotate = new Rotate(shot.Dir * 3f); }
                band.style.opacity = 0f;
                layer.Insert(1, band); // above the dim, below the portraits
                bands.Add(band);
            }
            App.Audio.Sfx("battle.dash", 0.7f, 0.9f);
            if (delay2 > 0f) App.StartCoroutine(DelayedSfx("battle.dash", delay2, 1.05f));
            var cam = boardHost;
            cam.style.transformOrigin = new TransformOrigin(mid.x - origin.x, mid.y - origin.y);
            float total = CutInSeconds + delay2;
            float camSeed = Random.Range(0f, 100f);
            float camTwist = (Random.value < 0.5f ? -1f : 1f) * 2.6f;
            var flash = new VisualElement { pickingMode = PickingMode.Ignore };
            flash.style.position = Position.Absolute;
            flash.style.left = flash.style.top = flash.style.right = flash.style.bottom = 0;
            flash.style.backgroundColor = Palette.White;
            layer.Add(flash);
            App.StartCoroutine(Tween.Shake(shake, 14f * Fx, Timing.Standard));
            yield return Tween.Run(total, k =>
            {
                float elapsed = k * total;
                float fade = Mathf.Clamp01(Mathf.Min(elapsed / 0.15f, (total - elapsed) / 0.2f));
                dim.style.opacity = 0.5f * fade;
                foreach (var band in bands) band.style.opacity = fade;
                // Board camera: slow push-in on the pincer with a hand-held drift.
                float zoom = 1f + 0.14f * Tween.Back(Mathf.Clamp01(elapsed / 0.35f));
                float sx = (Mathf.PerlinNoise(camSeed, elapsed * 1.6f) - 0.5f) * 18f;
                float sy = (Mathf.PerlinNoise(camSeed + 7f, elapsed * 1.6f) - 0.5f) * 18f;
                cam.style.scale = new Scale(new Vector2(zoom, zoom));
                cam.style.translate = new Translate(sx, sy);
                cam.style.rotate = new Rotate(camTwist * Tween.EaseOut(Mathf.Clamp01(elapsed / 0.35f)) + (Mathf.PerlinNoise(camSeed + 13f, elapsed * 1.1f) - 0.5f) * 1.2f);
                flash.style.opacity = Mathf.Clamp01(1f - elapsed / 0.12f) * 0.7f;
                foreach (var s in shots)
                {
                    float t = Mathf.Max(0f, elapsed - s.Delay) * CutInSpeed;
                    float v = t <= 0f ? -1f : CutInCurve.Evaluate(Mathf.Min(t, 1.993103f));
                    float along = s.Dir * s.Distance * v;
                    float hx = (Mathf.PerlinNoise(s.Seed, elapsed * 2.4f) - 0.5f) * 30f;
                    float hy = (Mathf.PerlinNoise(s.Seed + 3f, elapsed * 2.4f) - 0.5f) * 30f;
                    float cx = s.Horizontal ? StageW / 2f + along : s.Lane;
                    float cy = s.Horizontal ? s.Lane : StageH / 2f + along;
                    float push = 1f + 0.07f * Mathf.Clamp01(t / 1.993103f);
                    s.El.style.left = cx - s.W / 2f + hx;
                    s.El.style.top = cy - s.H / 2f + hy;
                    s.El.style.scale = new Scale(new Vector2(push, push));
                    s.El.style.rotate = new Rotate((Mathf.PerlinNoise(s.Seed + 9f, elapsed * 1.7f) - 0.5f) * 4f);
                }
            });
            layer.RemoveFromHierarchy();
            App.StartCoroutine(SettleCamera(cam));
        }

        private IEnumerator DelayedSfx(string cue, float delay, float pitch)
        {
            yield return Tween.Wait(delay);
            App.Audio.Sfx(cue, 0.7f, pitch);
        }

        private static IEnumerator SettleCamera(VisualElement cam)
        {
            float s0 = cam.resolvedStyle.scale.value.x;
            var t0 = cam.resolvedStyle.translate;
            float r0 = cam.resolvedStyle.rotate.angle.value;
            yield return Tween.Run(Timing.Standard, k =>
            {
                float e = Tween.EaseInOut(k);
                float s = Mathf.Lerp(s0, 1f, e);
                cam.style.scale = new Scale(new Vector2(s, s));
                cam.style.translate = new Translate(t0.x * (1f - e), t0.y * (1f - e));
                cam.style.rotate = new Rotate(r0 * (1f - e));
            });
            cam.style.scale = new Scale(Vector2.one);
            cam.style.translate = new Translate(0, 0);
            cam.style.rotate = new Rotate(0);
        }

        private IEnumerator RespiteAnim(RespiteEvent rs)
        {
            foreach (var id in new[] { rs.Attacker1, rs.Attacker2 })
                if (board.Units.TryGetValue(id, out var v))
                    App.StartCoroutine(Tween.Run(Timing.Emphasis, k => v.Root.style.rotate = new Rotate(k >= 1f ? 0f : 360f * Tween.EaseInOut(k))));
            yield return FloatText(rs.Attacker1, "Respite", new Color(0.8f, 0.95f, 1f), 0.45f, 56);
        }

        /// <summary>GG-T187 (§1.2.3): a supporter performs its class's support action — its cut-in card carries the action's
        /// name and the action floats over it; the effect's own beats (heal, buff, haste, mana, strikes) follow.</summary>
        private IEnumerator SupportAnim(SupportActionEvent sa)
        {
            var sup = State.UnitById(sa.SupporterId);
            if (sup == null || !board.Units.TryGetValue(sup.Id, out var sv)) yield break;
            string label = string.IsNullOrEmpty(sa.Label) ? sa.Effect.ToString() : sa.Label;
            SupporterPop(sup, sup.Team, label.ToUpperInvariant());
            if (sa.Effect == SupportEffect.Strike) yield break; // the support strikes animate themselves
            var color = SupportColor(sa.Effect);
            Feed($"✦ {sup.Name}: {label}", color);
            App.Audio.Sfx(sa.Effect == SupportEffect.Heal ? "battle.heal" : sa.Effect == SupportEffect.Mana ? "battle.orb" : "battle.cast", 0.7f, 1.2f);
            App.StartCoroutine(Tween.Pop(sv.Root, 1.12f, 0.18f));
            var at = BoardView.CenterOf(sv.Tile, sv.W, sv.H);
            App.StartCoroutine(FloatAway(board.Float(label + "!", at, color, 60), 0.6f));
            yield return Tween.Wait(ReduceMotion ? 0.08f : 0.2f);
        }

        private static Color SupportColor(SupportEffect e)
        {
            switch (e)
            {
                case SupportEffect.Heal: return Palette.Good;
                case SupportEffect.Buff: return Palette.Buff;
                case SupportEffect.Haste: return Palette.Haste;
                case SupportEffect.Mana: return Palette.Mana;
                case SupportEffect.Cleanse: return Palette.Cleanse;
                default: return Palette.Info;
            }
        }

        private IEnumerator AttackAnim(AttackEvent at)
        {
            var a = State.UnitById(at.AttackerId); var t = State.UnitById(at.TargetId);
            if (a == null || t == null || !board.Units.TryGetValue(a.Id, out var av)) yield break;
            Vector2 dir = (ViewCenter(t) - ViewCenter(a)).normalized;
            if (at.IsRanged)
            {
                // A hexed bolt flies down the lane.
                var from = ViewCenter(a); var to = ViewCenter(t);
                App.Audio.Sfx("element.arcane", 0.7f);
                yield return Tween.Run(Timing.Standard, k => { board.Lines.ClearLines(); board.Lines.Dot(ProjectilePoint(ProjectileMotion.Twist, from, to, k), 22f, new Color(0.7f, 0.35f, 1f)); });
                board.Lines.ClearLines();
                yield break;
            }
            if (at.IsSupport) SupporterPop(a, a.Team);
            float lunge = (at.IsSupport ? 18f : 34f) * Fx + 6f;
            yield return Tween.Run(Timing.Instant, k => av.Root.style.translate = new Translate(dir.x * lunge * k, dir.y * lunge * k));
            App.StartCoroutine(Tween.Run(Timing.Quick, k => av.Root.style.translate = new Translate(dir.x * lunge * (1 - k), dir.y * lunge * (1 - k))));
        }

        private IEnumerator DamageAnim(DamagedEvent d)
        {
            var t = State.UnitById(d.TargetId);
            if (t == null || !board.Units.TryGetValue(t.Id, out var v)) yield break;
            var at = BoardView.CenterOf(v.Tile, v.W, v.H);
            if (d.Hit == HitKind.Miss)
            {
                App.Audio.Sfx("battle.miss");
                App.StartCoroutine(FloatAway(board.Float("MISS", at, Palette.Info, 56), 0.6f));
                yield return Tween.Wait(Timing.Quick);
                yield break;
            }
            bool crit = d.Hit == HitKind.Critical;
            var style = StyleOf(d.Type);
            // A crit or a quarter of the target's HP hits one tier harder than the action's own (POL-E5).
            var tier = Vfx.TierDef(crit || d.Amount >= t.MaxHp * 0.25f ? Bump(hitTier) : hitTier);
            App.Audio.Sfx(d.SourceId < 0 ? "status.debuff" : crit ? "battle.crit" : style.Cue);
            var src = State.UnitById(d.SourceId);
            Feed($"{(d.SourceId < 0 ? "☠" : "†")} {(src != null ? src.Name + " → " : "")}{t.Name}: {d.Amount}{(crit ? " CRIT" : "")}{(d.Note != null ? " (" + d.Note + ")" : "")}", t.IsHero ? new Color(1f, 0.65f, 0.6f) : Palette.White.WithA(0.9f));
            // Anticipation (POL-E6): the target braces for a beat before the hit lands.
            if (!ReduceMotion) yield return Tween.Run(Timing.Instant, k => v.Root.style.scale = new Scale(Vector2.one * (1f - 0.06f * Ease.Out(k))));
            if (d.Amount > 0 && !ReduceMotion)
            {
                if (d.SourceId < 0) Emit(style, at, 0.5f, quiet: true); // a damage-over-time tick: a small puff of its element
                else App.StartCoroutine(ElementBurst(at, style, tier));
            }
            string text = d.Note == "Armored!" ? "ARMORED" : (crit ? $"{d.Amount}!" : d.Amount.ToString());
            var color = d.Note == "Armored!" ? new Color(0.7f, 0.7f, 0.75f) : crit ? new Color(1f, 0.85f, 0.2f) : t.IsHero ? new Color(1f, 0.5f, 0.45f) : Palette.White;
            if (tallying && d.SourceId >= 0 && d.Note != "Armored!") AddToTally(t, at, d.Amount, crit, d.Note);
            else App.StartCoroutine(FloatAway(board.Float(text, at, color, crit ? 100 : 72), 0.75f));
            v.SetHp(d.HpAfter, t.MaxHp);
            v.Frame.style.unityBackgroundImageTintColor = Palette.Bad;
            v.Art.style.unityBackgroundImageTintColor = new Color(1f, 0.55f, 0.55f);
            App.StartCoroutine(Tween.Shake(v.Root, 14f * Fx, Timing.Quick));
            if (tier.Shake > 0f) App.StartCoroutine(Tween.Shake(shake, tier.Shake * Fx, Timing.Standard));
            // Impact: the hit-stop holds the frame, longer for heavier tiers (POL-E5/E6); Reduce Motion skips it.
            yield return Tween.Wait(Timing.Instant + (ReduceMotion ? 0f : tier.HitStop));
            // Follow-through: the target recoils back past rest and settles.
            if (!ReduceMotion) App.StartCoroutine(Tween.Run(Timing.Quick, k => v.Root.style.scale = new Scale(Vector2.one * Mathf.LerpUnclamped(0.94f, 1f, Ease.Back(k)))));
            v.Art.style.unityBackgroundImageTintColor = Palette.White;
            v.Frame.style.unityBackgroundImageTintColor = Palette.White;
        }

        private IEnumerator HealAnim(HealedEvent h)
        {
            var t = State.UnitById(h.TargetId);
            if (t == null || !board.Units.TryGetValue(t.Id, out var v)) yield break;
            Feed($"✚ {t.Name} +{h.Amount} HP", Palette.Good);
            App.Audio.Sfx("battle.heal");
            if (!ReduceMotion) App.StartCoroutine(HealBurst(BoardView.CenterOf(v.Tile, v.W, v.H)));
            v.SetHp(h.HpAfter, t.MaxHp);
            v.SetGlow(new Color(0.4f, 1f, 0.5f), 0.8f);
            App.StartCoroutine(FloatAway(board.Float("+" + h.Amount, BoardView.CenterOf(v.Tile), Palette.Good, 72), 0.8f));
            yield return Tween.Wait(Timing.Standard);
            v.SetGlow(Palette.White, 0f);
        }

        private IEnumerator DeathAnim(DiedEvent d)
        {
            if (!board.Units.TryGetValue(d.UnitId, out var v)) yield break;
            var du = State.UnitById(d.UnitId);
            if (du != null) Feed($"★ {du.Name} is defeated", du.IsHero ? new Color(1f, 0.45f, 0.4f) : Palette.Gold);
            App.Audio.Sfx("battle.death");
            // GridGame2026's death-portrait dissolve (GG-T96): the token goes at once and the unit's whole,
            // uncropped artwork takes its place over the board, shuddering, shrinking and fading on its own —
            // playback carries straight on (loot burst, the next kill in the chain) while it plays out.
            if (du != null) App.StartCoroutine(DeathDissolve(du, v));
            v.Root.RemoveFromHierarchy();
            v.Dead = true;
            yield return Tween.Wait(Timing.Quick);
        }

        // Tuned from GridGame2026's per-frame Portrait3D dissolve at 60 fps: alpha 196/255 minus 1% a frame
        // (about 1.3 s), scale ×0.99 a frame, jittering up to ±1/12 tile around its start point.
        private const float DissolveStartAlpha = 196f / 255f;
        private const float DissolveSeconds = DissolveStartAlpha / 0.6f;
        private const float DissolveShrinkPerSecond = 0.6f; // -ln(0.99) × 60 = 0.603

        /// <summary>The fallen unit's full portrait, centred on its footprint and well over a tile so the whole art reads.</summary>
        private IEnumerator DeathDissolve(Unit u, UnitView v)
        {
            string classId = u.IsCorpse && u.CorpseOf != null ? u.CorpseOf : u.Def?.Id;
            if (string.IsNullOrEmpty(classId) || !Art.Exists("Portraits/" + classId)) yield break;
            var tex = Art.Portrait(classId);
            float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1f;
            float box = BoardView.Tile * (2f + Mathf.Max(v.W, v.H));
            float w = aspect >= 1f ? box : box * aspect, h = aspect >= 1f ? box / aspect : box;
            var centre = BoardView.CenterOf(v.Tile, v.W, v.H);
            var el = new VisualElement { name = "dissolve-" + u.Id, pickingMode = PickingMode.Ignore };
            el.style.position = Position.Absolute;
            el.style.width = w; el.style.height = h;
            // Centred on the footprint, but nudged back inside the board at its edges so the whole art
            // still reads instead of sliding under the HUD and banners (a top-row kill otherwise lost half).
            float boardW = BoardView.Tile * BattleConfig.Cols, boardH = BoardView.Tile * BattleConfig.Rows;
            el.style.left = w < boardW ? Mathf.Clamp(centre.x - w / 2f, 0f, boardW - w) : centre.x - w / 2f;
            el.style.top = h < boardH ? Mathf.Clamp(centre.y - h / 2f, 0f, boardH - h) : centre.y - h / 2f;
            el.style.backgroundImage = new StyleBackground(tex);
            el.style.opacity = DissolveStartAlpha;
            // Beneath the floating numbers and coin burst on the fx layer, above every token, mark and line.
            board.FxLayer.Insert(0, el);
            float jitter = BoardView.Tile / 12f;
            yield return Tween.Run(DissolveSeconds, k =>
            {
                float t = k * DissolveSeconds;
                el.style.opacity = Mathf.Max(0f, DissolveStartAlpha - 0.6f * t);
                el.style.scale = new Scale(Vector2.one * Mathf.Exp(-DissolveShrinkPerSecond * t));
                el.style.translate = ReduceMotion || k >= 1f
                    ? new Translate(0, 0)
                    : new Translate(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter));
            });
            el.RemoveFromHierarchy();
        }

        private IEnumerator LootAnim(LootEvent l)
        {
            if (l.Coins <= 0 && l.Items.Count == 0) { SyncCoins(); yield break; }
            var at = BoardView.CenterOf(board.Units.TryGetValue(l.FromUnitId, out var v) ? v.Tile : new Coord(2, 2));
            if (l.Coins > 0)
            {
                App.StartCoroutine(FloatAway(board.Float($"+{l.Coins} G", at + new Vector2(0, -20), Palette.Gold, 52), 0.9f));
                App.StartCoroutine(CoinBurst(at, l.Coins));
            }
            foreach (var it in l.Items)
            {
                var name = App.Content.TryItem(it.Item)?.Name ?? it.Item;
                App.StartCoroutine(FloatAway(board.Float($"{name} ×{it.Count}", at + new Vector2(0, 40), new Color(0.7f, 1f, 0.8f), 40), 1.1f));
            }
            App.Audio.Sfx("ui.buy", 0.6f);
            yield return Tween.Wait(Timing.Instant);
        }

        /// <summary>A guest ally steps onto the board (§25.11).</summary>
        private IEnumerator GuestAnim(GuestJoinedEvent e)
        {
            var u = State.UnitById(e.UnitId);
            if (u == null) yield break;
            var v = board.Units.TryGetValue(u.Id, out var ex) ? ex : board.Add(u);
            board.Place(v, e.At);
            v.SetHp(u.Hp, u.MaxHp);
            App.Audio.Sfx("battle.pickup", e.Captive ? 0.75f : 0.9f);
            Billboard(e.Captive ? $"A captive {u.Name}! Keep them alive to free them." : $"{u.Name} joins the fight! Keep them alive to recruit them.", null, new Color(0.6f, 1f, 0.75f));
            v.Root.style.scale = new Scale(Vector2.zero);
            yield return Tween.Run(Timing.Standard, k => v.Root.style.scale = new Scale(Vector2.one * (k < 0.7f ? k / 0.7f * 1.15f : 1.15f - 0.15f * (k - 0.7f) / 0.3f)));
            v.Root.style.scale = new Scale(Vector2.one);
        }

        /// <summary>Remains drop onto the fallen undead's tile with a thud (§14.5).</summary>
        private IEnumerator RemainsAnim(int remainsId, Coord at, bool quiet)
        {
            var r = State.UnitById(remainsId);
            if (r == null) yield break;
            if (!board.Units.TryGetValue(r.Id, out var v)) v = board.Add(r);
            board.Place(v, at);
            v.SetHp(r.Hp, r.MaxHp);
            SyncTimeline(null);
            if (!quiet) App.Audio.Sfx("battle.shatter", 0.5f, 0.6f);
            v.Root.style.translate = new Translate(0, -120);
            yield return Tween.Run(Timing.Quick, k => v.Root.style.translate = new Translate(0, -120f * (1f - k * k)));
            App.StartCoroutine(Tween.Shake(v.Root, 10f * Fx, Timing.Quick));
            Billboard($"{r.Name} — destroy it or it rises!", null, Palette.Info);
        }

        /// <summary>The remains crumble and the undead climbs back out.</summary>
        private IEnumerator RiseAnim(RoseAgainEvent e)
        {
            if (board.Units.TryGetValue(e.RemainsId, out var rv))
            {
                yield return Tween.Run(Timing.Standard, k => { rv.Root.style.opacity = 1f - k; rv.Root.style.scale = new Scale(Vector2.one * (1f - 0.3f * k)); });
                rv.Root.RemoveFromHierarchy(); rv.Dead = true;
            }
            var u = State.UnitById(e.UnitId);
            if (u == null) yield break;
            var v = board.Units.TryGetValue(u.Id, out var existing) ? existing : board.Add(u);
            board.Place(v, e.At);
            v.SetHp(u.Hp, u.MaxHp);
            SyncTimeline(null);
            App.Audio.Sfx("status.debuff", 0.9f, 0.8f);
            Billboard($"{u.Name} rises again!", null, new Color(0.75f, 1f, 0.75f));
            v.Root.style.translate = new Translate(0, 60);
            yield return Tween.Run(Timing.Standard, k => { v.Root.style.translate = new Translate(0, 60f * (1f - k)); v.Root.style.opacity = k; });
        }

        /// <summary>An egg drops onto a free tile beside its layer (§14.8).</summary>
        private IEnumerator EggLaidAnim(EggLaidEvent e)
        {
            var r = State.UnitById(e.EggId);
            if (r == null) yield break;
            if (!board.Units.TryGetValue(r.Id, out var v)) v = board.Add(r);
            board.Place(v, e.At);
            v.SetHp(r.Hp, r.MaxHp);
            SyncTimeline(null);
            App.Audio.Sfx("battle.pickup", 0.6f, 0.8f);
            v.Root.style.translate = new Translate(0, -60);
            yield return Tween.Run(Timing.Quick, k => v.Root.style.translate = new Translate(0, -60f * (1f - k * k)));
            Billboard($"{State.UnitById(e.FromUnitId)?.Name} lays an egg!", null, new Color(0.85f, 1f, 0.75f));
        }

        /// <summary>The egg splits open and the real enemy climbs out, at full strength.</summary>
        private IEnumerator HatchAnim(EggHatchedEvent e)
        {
            if (board.Units.TryGetValue(e.EggId, out var ev))
            {
                yield return Tween.Run(Timing.Standard, k => { ev.Root.style.opacity = 1f - k; ev.Root.style.scale = new Scale(Vector2.one * (1f - 0.3f * k)); });
                ev.Root.RemoveFromHierarchy(); ev.Dead = true;
            }
            var u = State.UnitById(e.UnitId);
            if (u == null) yield break;
            var v = board.Units.TryGetValue(u.Id, out var existing) ? existing : board.Add(u);
            board.Place(v, e.At);
            v.SetHp(u.Hp, u.MaxHp);
            SyncTimeline(null);
            App.Audio.Sfx("battle.crit", 0.8f, 1.1f);
            Billboard($"{u.Name} hatches!", null, Palette.GoldLight);
            v.Root.style.scale = new Scale(Vector2.zero);
            yield return Tween.Run(Timing.Standard, k => v.Root.style.scale = new Scale(Vector2.one * Mathf.Clamp01(k * 1.3f)));
            v.Root.style.scale = new Scale(Vector2.one);
        }

        /// <summary>Mid-wave reinforcements arrive (§14.8): each spawns with an entrance matched to its kind, then — on
        /// the first delayed entry a wave authored a line for — a held narrative beat announces the twist.</summary>
        private IEnumerator ReinforcementsAnim(ReinforcementsArrivedEvent ra)
        {
            for (int si = 0; si < ra.Spawned.Count; si++)
            {
                int id = ra.Spawned[si];
                var u = State.UnitById(id);
                if (u == null || board.Units.ContainsKey(id)) continue;
                var v = board.Add(u);
                if (si < ra.SpawnedAt.Count) board.Place(v, ra.SpawnedAt[si].At); // it may already have walked on this command
                v.SetHp(u.Hp, u.MaxHp);
                App.StartCoroutine(EntranceAnim(v, u.Def));
            }
            SyncTimeline(null);
            if (ra.Spawned.Count > 0) yield return Tween.Wait(Timing.Standard);
            if (!string.IsNullOrEmpty(ra.Line)) yield return SurpriseAnnounce(ra.SpeakerClassId, ra.Line);
        }

        /// <summary>A quick spawn-entrance styled by the class's own tags/brain (§14.8) — no new content fields needed.</summary>
        private IEnumerator EntranceAnim(UnitView v, ClassDef def)
        {
            if (def.Brain == Brain.Burrower)
            {
                App.Audio.Sfx("battle.trap", 0.8f, 0.7f);
                v.Root.style.opacity = 0;
                yield return Tween.Run(Timing.Standard, k => { v.Root.style.opacity = k; v.Root.style.scale = new Scale(Vector2.one * (0.7f + 0.3f * k)); });
                App.StartCoroutine(Tween.Shake(v.Root, 8f * Fx, Timing.Quick));
            }
            else if (def.HasTag("Flying"))
            {
                App.Audio.Sfx("battle.dash", 0.7f, 0.8f);
                v.Root.style.opacity = 0;
                yield return Tween.Run(Timing.Standard, k => { v.Root.style.opacity = k; v.Root.style.translate = new Translate(0, -160f * (1f - k)); }, Tween.EaseOut);
            }
            else if (def.HasTag("Undead"))
            {
                App.Audio.Sfx("status.debuff", 0.7f, 0.7f);
                v.Root.style.opacity = 0;
                yield return Tween.Run(Timing.Emphasis, k => v.Root.style.opacity = k);
            }
            else
            {
                App.Audio.Sfx("battle.shatter", 0.5f, 0.7f);
                v.Root.style.opacity = 0;
                yield return Tween.Run(Timing.Standard, k => { v.Root.style.opacity = k; v.Root.style.translate = new Translate(0, -80f * (1f - k)); }, Tween.EaseOut);
            }
        }

        /// <summary>A held narrative beat (§14.8): the speaker's portrait, name and a line, dismissed by a tap or a timer
        /// (StoryCrawlScreen's dual-exit idiom), reusing the 360×400 bordered-frame convention (CharacterSelectScreen,
        /// LevelUpScreen) without leaving the battle screen.</summary>
        private IEnumerator SurpriseAnnounce(string speakerClassId, string line)
        {
            var def = App.Content.TryClass(speakerClassId);
            var veil = Ui.El("fill veil col center", App.Overlay);
            veil.pickingMode = PickingMode.Position;
            veil.style.backgroundColor = new Color(0.01f, 0.01f, 0.05f, 0.7f);

            var row = Ui.El("row", veil);
            row.style.opacity = 0f;
            var frame = Ui.El(null, row);
            frame.style.width = 360; frame.style.height = 400;
            frame.style.borderTopWidth = frame.style.borderBottomWidth = frame.style.borderLeftWidth = frame.style.borderRightWidth = 4;
            var bandColor = BandColor(Team.Enemy);
            frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = bandColor;
            frame.style.backgroundColor = Palette.Ink.WithA(0.6f);
            if (def != null)
            {
                frame.style.backgroundImage = new StyleBackground(Art.Portrait(def.Id));
                frame.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                var crop = def.Crop;
                frame.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, Length.Percent(crop?.X ?? 50f));
                frame.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Length.Percent(crop?.Y ?? 0f));
            }

            var info = Ui.El("col", row);
            info.style.marginLeft = 24; info.style.width = 420;
            var nm = Ui.Text(def?.Name ?? "???", "h1 shadow", info);
            Fonts.UseDisplay(nm);
            nm.style.color = bandColor;
            var body = Ui.Text(line, "h2 shadow", info);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginTop = 12;

            bool done = false;
            void Finish() { if (done) return; done = true; }
            veil.RegisterCallback<ClickEvent>(_ => Finish());

            App.Audio.Sfx("battle.shatter", 0.5f);
            yield return Tween.Run(Timing.Standard, k =>
            {
                row.style.opacity = Mathf.Clamp01(k * 2.2f);
                row.style.scale = new Scale(Vector2.one * Mathf.LerpUnclamped(1.15f, 1f, Tween.Back(k)));
            });
            row.style.opacity = 1f; row.style.scale = new Scale(Vector2.one);

            float t = 0f;
            while (!done && t < 2.6f) { t += Tween.Dt; yield return null; }

            yield return Tween.Run(Timing.Quick, k => row.style.opacity = 1f - k);
            veil.RemoveFromHierarchy();
        }

        /// <summary>The feed line for a spawned amplifier; the dev <c>devamp</c> endpoint posts it too, so
        /// uicheck's layout audit covers the real text (GG-T110).</summary>
        public const string AmpSpawnedFeed = "Power Amplifier appeared — pincer on its tile";
        public static readonly Color AmpFeedColor = Palette.Gold;

        private IEnumerator AmpSpawnAnim(AmplifierSpawnedEvent e)
        {
            board.SyncAmplifier(e.At);
            App.Audio.Sfx("battle.orb", 0.8f, 0.7f);
            Feed(AmpSpawnedFeed, AmpFeedColor);
            yield return Tween.Pop(board.Amp, 1.6f, 0.3f);
        }

        private IEnumerator AmpMoveAnim(AmplifierMovedEvent e)
        {
            var from = BoardView.CenterOf(e.From); var to = BoardView.CenterOf(e.To);
            App.Audio.Sfx("battle.slide", 0.6f, 1.3f);
            yield return Tween.Run(Timing.Standard, k => board.PlaceAmp(Vector2.Lerp(from, to, Tween.EaseOut(k))));
            board.SyncAmplifier(e.To);
            var by = State.UnitById(e.ByUnitId);
            if (by != null) Feed($"{by.Name} shoved the amplifier", new Color(1f, 0.6f, 0.4f));
        }

        /// <summary>A non-hero entered a pickup's tile and shoved it aside (§13.3.1, GG-T228): it slides like the amplifier.</summary>
        private IEnumerator PickupMoveAnim(PickupMovedEvent e)
        {
            if (!board.PickupViews.TryGetValue(e.PickupEntryId, out var v)) yield break;
            var from = BoardView.PickupPos(e.From); var to = BoardView.PickupPos(e.To);
            v.userData = e.To;
            App.Audio.Sfx("battle.slide", 0.6f, 1.5f);
            yield return Tween.Run(Timing.Standard, k => { var p = Vector2.Lerp(from, to, Tween.EaseOut(k)); v.style.left = p.x; v.style.top = p.y; });
            BoardView.PlacePickup(v, e.To);
        }

        private IEnumerator AmpUsedAnim(AmplifierUsedEvent e)
        {
            var at = BoardView.CenterOf(e.At);
            App.Audio.Sfx("battle.crit", 0.9f);
            Billboard("Power Amplifier ×1.75", null, Palette.Gold);
            ampTally = true;
            yield return Tween.Run(Timing.Standard, k => { board.Lines.Rings.Clear(); board.Lines.Ring(at, 40f + 160f * k, new Color(1f, 0.8f, 0.3f, 1f - k), 12f); });
            board.Lines.Rings.Clear(); board.Lines.MarkDirtyRepaint();
            board.SyncAmplifier(null);
            yield return FloatText(e.HeroId, "AMPLIFIED!", Palette.Gold, 0.2f, 70);
        }

        // ── Damage tally (owner direction 2026-10-10): hits within a pincer stack up, then explode into the total ──

        private sealed class Tally { public Label El; public List<string> Terms = new List<string>(); public int Total; public bool Crit; public Vector2 At; public int UnitId; }
        private readonly Dictionary<int, Tally> tallies = new Dictionary<int, Tally>();
        private bool tallying, ampTally;
        public int TalliesBurst { get; private set; }

        private void AddToTally(Unit t, Vector2 at, int amount, bool crit, string note)
        {
            if (!tallies.TryGetValue(t.Id, out var tl))
            {
                tl = new Tally { At = at, UnitId = t.Id };
                tl.El = board.Float("", at + new Vector2(0, -BoardView.Tile * 0.62f), Palette.White, 50);
                tl.El.name = "tally-" + t.Id;
                tl.El.style.unityTextOutlineColor = Color.black; tl.El.style.unityTextOutlineWidth = 2f;
                tallies[t.Id] = tl;
            }
            tl.Terms.Add(crit ? amount + "!" : amount.ToString());
            if (ampTally && tl.Terms.Count == 1) tl.Terms.Add("AMP");
            tl.Total += amount; tl.Crit |= crit;
            // Each hit lands on its own line below the last, stacking downward like a fighting-game combo list.
            tl.El.text = string.Join("\n+ ", tl.Terms).Replace("+ AMP", "×AMP");
            tl.El.style.color = tl.Crit ? Palette.Gold : Palette.White;
            App.StartCoroutine(Tween.Pop(tl.El, 1.25f, 0.12f));
        }

        /// <summary>Each target's stacked terms collapse into one big number that bursts and floats away.</summary>
        private void FlushTallies()
        {
            tallying = false; ampTally = false;
            if (tallies.Count == 0) return;
            foreach (var tl in tallies.Values.ToList()) App.StartCoroutine(BurstTally(tl));
            tallies.Clear();
        }

        private IEnumerator BurstTally(Tally tl)
        {
            int realTerms = tl.Terms.Count(x => x != "AMP");
            if (realTerms <= 1)
            {
                tl.El.text = tl.Terms.Count > 0 ? tl.Terms[0] : "";
                yield return FloatAway(tl.El, 0.6f);
                yield break;
            }
            TalliesBurst++;
            yield return Tween.Run(Timing.Quick, k => { tl.El.style.scale = new Scale(Vector2.one * (1f - 0.5f * k)); tl.El.style.opacity = 1f - 0.6f * k; });
            tl.El.RemoveFromHierarchy();
            var total = board.Float(tl.Crit ? tl.Total + "!" : tl.Total.ToString(), tl.At, tl.Crit ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.95f, 0.8f), 120);
            total.name = "tally-total";
            App.Audio.Sfx("battle.crit", 0.7f, 0.9f);
            App.StartCoroutine(Tween.Shake(shake, 10f * Fx, Timing.Quick));
            yield return Tween.Run(Timing.Quick, k => total.style.scale = new Scale(Vector2.one * (1.7f - 0.7f * Tween.EaseOut(k))));
            yield return FloatAway(total, 0.9f);
        }

        // ── Billboard (FF VI-style spell window) ──

        private VisualElement billboard;
        public int BillboardLines => billboard?.childCount ?? 0;
        /// <summary>Strokes on the pincer/outline layer right now (automation: the pause harness pauses mid-chain).</summary>
        public int LineStrokes => board == null ? 0 : board.Lines.Segs.Count + board.Lines.Outlines.Count + board.Lines.Rings.Count;

        /// <summary>A top-of-board window naming every spell, ability and combo as it happens; lines fade after 1.6 s.</summary>
        /// <summary>The billboard steps out of the way: when the action is in the top two rows it moves to the board's foot.</summary>
        // ── Where a unit is NOW in playback (GG-T98) ──
        // State is the end of the whole command by the time anything plays, so a beat must never read a unit's
        // position from it: a unit that moved later in the same command (an enemy turn, knockback, a chain link)
        // is already on its final tile there. The board view's UnitView.Tile advances one event at a time
        // (StepAnim, Displaced, Teleport, spawns), so it is the position as of the beat being played.

        private Coord ViewTile(Unit u) => board.Units.TryGetValue(u.Id, out var v) ? v.Tile : u.Pos;

        private Vector2 ViewCenter(Unit u) =>
            board.Units.TryGetValue(u.Id, out var v) ? BoardView.CenterOf(v.Tile, v.W, v.H) : BoardView.CenterOf(u.Pos, u.W, u.H);

        private IEnumerable<Coord> ViewTiles(Unit u) => FootprintTiles(new UnitFootprint { UnitId = u.Id, At = ViewTile(u), W = u.W, H = u.H });

        private static IEnumerable<Coord> FootprintTiles(UnitFootprint f)
        {
            for (int y = 0; y < Mathf.Max(1, f.H); y++)
                for (int x = 0; x < Mathf.Max(1, f.W); x++)
                    yield return new Coord(f.At.X + x, f.At.Y + y);
        }

        private void DodgeBillboard(IEnumerable<Coord> tiles)
        {
            billboardLow = tiles != null && tiles.Any(t => t.Y <= 1);
            if (billboard != null) billboard.style.top = BillboardTop;
        }

        private bool billboardLow;
        // Dodging moves it up over the banner line (which says the same thing), never down onto the heroes.
        private float BillboardTop => billboardLow ? BoardTop - 120 : BoardTop - 30;

        private void Billboard(string text, Texture2D icon, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (billboard == null)
            {
                billboard = Ui.Window(null, App.Overlay, "window--compact");
                billboard.name = "billboard";
                billboard.pickingMode = PickingMode.Ignore;
                billboard.style.position = Position.Absolute;
                billboard.style.left = 120; billboard.style.right = 120; billboard.style.top = BoardTop - 30;
                billboard.style.paddingTop = billboard.style.paddingBottom = 10;
                billboard.style.alignItems = Align.Center;
            }
            billboard.style.display = DisplayStyle.Flex;
            billboard.style.top = BillboardTop;
            billboard.BringToFront();
            var row = Ui.El("row", billboard);
            row.pickingMode = PickingMode.Ignore;
            if (icon != null)
            {
                var ic = Ui.El("", row);
                ic.style.width = ic.style.height = 56; ic.style.marginRight = 14;
                ic.style.backgroundImage = new StyleBackground(icon);
                ic.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            var l = Ui.Text(text, "h2 shadow", row);
            l.style.color = color;
            l.style.fontSize = text.Length > 30 ? 36 : 44;
            l.style.whiteSpace = WhiteSpace.Normal; // a wider font should wrap here, never overflow the window
            while (billboard.childCount > 3) billboard[0].RemoveFromHierarchy();
            App.StartCoroutine(Tween.Pop(row, 1.12f, 0.12f));
            App.StartCoroutine(RetireBillboardRow(billboard, row));
        }

        /// <summary>Drops a billboard line after 1.6 s of game time — held while paused, unlike a scheduler delay (GG-T126).
        /// Takes the billboard itself: OnHidden may null the field before this fires.</summary>
        private IEnumerator RetireBillboardRow(VisualElement bb, VisualElement row)
        {
            yield return Tween.Wait(Timing.Cinematic + Timing.Emphasis);
            row.RemoveFromHierarchy();
            if (bb.childCount == 0) bb.style.display = DisplayStyle.None;
        }

        /// <summary>Gold shown on the HUD: the party's purse plus what this battle has dropped so far.</summary>
        private int shownCoins = -1;
        public int CoinsShown => shownCoins;

        private void SyncCoins(int? value = null)
        {
            shownCoins = value ?? ((App.Save?.Gold ?? 0) + State.Results.Coins);
            coinLabel.text = $"{shownCoins} G";
        }

        /// <summary>
        /// The enemy "explodes" into gold (as in GridGame2026): coins burst out and bounce, then fly to the gold counter
        /// in the top-right, which counts up as each one lands. Runs alongside the next beats; never blocks play.
        /// </summary>
        private IEnumerator CoinBurst(Vector2 boardAt, int coins)
        {
            int n = Mathf.Clamp(coins, 3, ReduceMotion ? 4 : 12);
            int start = shownCoins < 0 ? (App.Save?.Gold ?? 0) + State.Results.Coins - coins : shownCoins;
            Vector2 from = App.Overlay.WorldToLocal(board.Root.LocalToWorld(boardAt));
            var wb = coinLabel.worldBound;
            Vector2 to = App.Overlay.WorldToLocal(new Vector2(wb.xMax - 40f, wb.center.y));
            int landed = 0;
            for (int i = 0; i < n; i++)
            {
                var coin = new VisualElement { name = "coin", pickingMode = PickingMode.Ignore };
                coin.style.position = Position.Absolute;
                coin.style.width = coin.style.height = 34;
                coin.style.backgroundImage = new StyleBackground(Art.Orb);
                coin.style.unityBackgroundImageTintColor = new Color(1f, 0.82f, 0.25f);
                App.Overlay.Add(coin);
                float ang = Random.Range(-Mathf.PI * 0.95f, -Mathf.PI * 0.05f), speed = Random.Range(260f, 520f);
                var vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed;
                float floor = from.y + Random.Range(30f, 70f), delay = i * 0.035f;
                int share = coins / n + (i < coins % n ? 1 : 0);
                App.StartCoroutine(OneCoin(coin, from, vel, floor, to, delay, () =>
                {
                    landed++;
                    SyncCoins(Mathf.Min(start + coins, shownCoins + share));
                    if (landed == n) SyncCoins();
                    App.Audio.Sfx("ui.buy", 0.35f, 1.1f + landed * 0.03f);
                    App.StartCoroutine(Tween.Pop(coinLabel, 1.15f, 0.08f));
                }));
            }
            yield break;
        }

        private IEnumerator OneCoin(VisualElement coin, Vector2 from, Vector2 vel, float floor, Vector2 to, float delay, System.Action arrive)
        {
            yield return Tween.Wait(delay);
            // Burst: ballistic arc with one bounce, ~0.45 s.
            var pos = from; float t = 0f; bool bounced = false;
            while (t < 0.45f)
            {
                float dt = Mathf.Min(Tween.Dt * Tween.Speed, 0.05f);
                t += dt;
                vel.y += 1800f * dt;
                pos += vel * dt;
                if (pos.y > floor && vel.y > 0f && !bounced) { pos.y = floor; vel.y *= -0.45f; vel.x *= 0.7f; bounced = true; }
                coin.style.left = pos.x - 17; coin.style.top = pos.y - 17;
                yield return null;
            }
            // Fly to the counter, accelerating, shrinking a little.
            var start = pos;
            yield return Tween.Run(Timing.Emphasis, k =>
            {
                float e = k * k;
                var p = Vector2.Lerp(start, to, e) + Vector2.up * (-80f * Ease.Arc(k));
                coin.style.left = p.x - 17; coin.style.top = p.y - 17;
                coin.style.scale = new Scale(Vector2.one * (1f - 0.35f * k));
            });
            coin.RemoveFromHierarchy();
            arrive();
        }

        private IEnumerator OrbAnim(OrbMintedEvent o)
        {
            App.Audio.Sfx("battle.orb", 0.7f, 1f + Random.Range(-0.05f, 0.1f));
            var src = State.UnitById(o.SourceUnitId);
            // GG-T112: the slot it landed in when it was minted, not the bank's final count — State is already the
            // end of the action, so several orbs minted together used to all fly to the last slot.
            int index = o.Landed ? (o.Slot >= 0 ? o.Slot : State.Bank.Count - 1) : orbViews.Count - 1;
            if (o.Landed) { OrbSlotsFlown.Add(index); if (OrbSlotsFlown.Count > 24) OrbSlotsFlown.RemoveAt(0); }
            // Fly a small orb from the source to its slot in the line.
            var orb = new VisualElement { pickingMode = PickingMode.Ignore };
            orb.style.position = Position.Absolute;
            orb.style.width = orb.style.height = 60;
            orb.style.backgroundImage = new StyleBackground(Art.Orb);
            orb.style.unityBackgroundImageTintColor = Art.Mana;
            App.Overlay.Add(orb);
            Vector2 from = src != null && board.Units.TryGetValue(src.Id, out var v)
                ? new Vector2(BoardLeft, BoardTop) + BoardView.CenterOf(v.Tile) : new Vector2(GameApp.StageWidth / 2f, 1200);
            var slot = orbViews[Mathf.Clamp(index, 0, orbViews.Count - 1)];
            Vector2 to = slot.worldBound.center - App.Stage.worldBound.position;
            if (float.IsNaN(to.x)) to = new Vector2(GameApp.StageWidth / 2f, 2210);
            yield return Tween.Run(Timing.Standard, k =>
            {
                var p = Vector2.Lerp(from, to, Tween.EaseInOut(k)) + Vector2.up * (-140f * Ease.Arc(k));
                orb.style.left = p.x - 30; orb.style.top = p.y - 30;
                if (!o.Landed) orb.style.opacity = 1f - k;
            });
            orb.RemoveFromHierarchy();
            if (o.Landed && o.Slot >= 0) SyncOrbs(o.Slot + 1); // light up to this orb's slot; later orbs fill theirs
            else SyncOrbs();
            if (o.Landed && index >= 0 && index < orbViews.Count) App.StartCoroutine(Tween.Pop(orbViews[index], 1.3f, 0.15f));
        }

        private IEnumerator TimelineAnim(TimelineAdvancedEvent ta)
        {
            var starts = new Dictionary<int, float>();
            foreach (var kv in timelineIconViews)
                starts[kv.Key] = (kv.Value.resolvedStyle.left + kv.Value.resolvedStyle.width / 2f) / TimelineWidth;
            // Create icons for entries that appeared, then glide everything to the snapshot.
            foreach (var e in State.Timeline)
                if (!timelineIconViews.ContainsKey(e.Id)) { var icon = MakeTimelineIcon(e); timelineIconViews[e.Id] = icon; timelineIcons.Add(icon); starts[e.Id] = ta.U.TryGetValue(e.Id, out var u0) ? u0 : e.U; }
            float seconds = Mathf.Clamp(ta.Ticks / 40f, 0.18f, 0.45f);
            yield return Tween.Run(seconds, k =>
            {
                var at = new Dictionary<int, float>();
                foreach (var kv in ta.U) at[kv.Key] = Mathf.Lerp(starts.TryGetValue(kv.Key, out var v) ? v : castGhosts.TryGetValue(kv.Key, out var g) ? g.U : kv.Value, kv.Value, k);
                SyncTimeline(at); // lanes, overtakes, badges and cast rings follow the glide
            }, Tween.EaseInOut);
            SyncTimeline(ta.U);
        }

        /// <summary>Glide one icon between two u positions while everything else holds still.</summary>
        private IEnumerator GlideOne(int entryId, float fromU, float toU, float seconds)
        {
            var hold = new Dictionary<int, float>();
            foreach (var e in State.Timeline) hold[e.Id] = lastAt != null && lastAt.TryGetValue(e.Id, out var u) ? u : e.U;
            yield return Tween.Run(seconds, k => { hold[entryId] = Mathf.Lerp(fromU, toU, Tween.EaseOut(k)); SyncTimeline(hold); });
        }

        private IEnumerator PushAnim(PushedBackEvent pb)
        {
            var entry = State.TurnEntryOf(pb.UnitId);
            App.Audio.Sfx("battle.slide", 0.8f, 0.8f);
            if (entry != null && timelineIconViews.ContainsKey(entry.Id))
                App.StartCoroutine(GlideOne(entry.Id, pb.FromU, pb.ToU, 0.3f));
            yield return FloatText(pb.UnitId, "Pushback!", Palette.Info, 0.12f, 46);
        }

        private IEnumerator HastenAnim(HastenedEvent h)
        {
            if (timelineIconViews.ContainsKey(h.EntryId))
                App.StartCoroutine(GlideOne(h.EntryId, h.FromU, h.ToU, 0.3f));
            yield return FloatText(h.UnitId, "Haste!", Palette.Gold, 0.2f, 50);
        }

        private IEnumerator TurnAnim(TurnStartedEvent ts)
        {
            var u = State.UnitById(ts.UnitId);
            if (u != null) Feed($"▶ {u.Name}'s turn", new Color(1f, 0.6f, 0.5f));
            App.Audio.Sfx("battle.turn", 0.7f);
            var entry = State.TurnEntryOf(ts.UnitId);
            if (entry != null && timelineIconViews.TryGetValue(entry.Id, out var icon))
            {
                App.StartCoroutine(Tween.Pop(icon, 1.35f, 0.25f));
                var reset = new Dictionary<int, float>();
                foreach (var e in State.Timeline) reset[e.Id] = lastAt != null && lastAt.TryGetValue(e.Id, out var lu) ? lu : e.U;
                reset[entry.Id] = 0f;
                SyncTimeline(reset);
            }
            if (u != null && board.Units.TryGetValue(u.Id, out var v))
            {
                v.SetGlow(Palette.Bad, 0.85f);
                ShowBanner($"{u.Name}'s turn");
                yield return Tween.Pop(v.Root, 1.1f, 0.2f);
                v.SetGlow(Palette.White, 0f);
            }
        }

        private IEnumerator CastQueuedAnim(CastQueuedEvent cq)
        {
            SyncTimeline(null);
            App.Audio.Sfx("battle.cast");
            var caster = State.UnitById(cq.CasterId);
            if (cq.LineTiles != null) foreach (var t in cq.LineTiles) board.Mark(t, new Color(1f, 0.2f, 0.15f), 0.4f);
            string what = App.Content.TryAbility(cq.AbilityId)?.Name ?? "spell";
            var entry = State.Timeline.FirstOrDefault(x => x.Id == cq.EntryId);
            var target = entry == null ? null : State.UnitById(entry.TargetUnitId) ?? State.UnitAt(entry.TargetTile);
            string on = target != null ? $" on {target.Name}" : cq.LineTiles != null ? " down a line" : "";
            float secs = cq.Ticks / (float)BattleConfig.TicksPerSecond;
            ShowBanner($"{caster?.Name} {(cq.IsCharge ? "charges" : "casts")} {what}{on} — lands in {secs:0.#}s");
            Billboard($"{caster?.Name}: {what}{on}", Art.AbilityIcon(App.Content.TryAbility(cq.AbilityId)), caster != null && caster.IsHero ? Palette.White : new Color(1f, 0.7f, 0.65f));
            AddGhost(cq);
            var tu = State.UnitById(cq.TargetUnitId);
            DodgeBillboard(new[] { cq.TargetTile }.Concat(tu != null ? ViewTiles(tu) : Enumerable.Empty<Coord>()).Concat(cq.LineTiles ?? new List<Coord>()));
            var castColor = CastColor(App.Content.TryAbility(cq.AbilityId), cq.AbilityId);
            if (caster != null) yield return Gather(caster, castColor);
            if (caster != null) { StartChannel(caster.Id, castColor); App.Audio.Sfx("battle.channel", 0.7f); }
            SyncTimeline(null);
            yield return FloatText(cq.CasterId, cq.IsCharge ? "Charging…" : "Casting…", new Color(0.6f, 0.85f, 1f), 0.3f, 50);
        }

        private IEnumerator ClutchAnim(ClutchEvent c)
        {
            var flash = Ui.El("fill", App.Overlay);
            flash.style.backgroundColor = Palette.White;
            flash.pickingMode = PickingMode.Ignore;
            App.Audio.Sfx("battle.heal");
            yield return Tween.Fade(flash, 0.9f * Mathf.Max(0.3f, Fx), 0f, 0.35f);
            flash.RemoveFromHierarchy();
            yield return FloatText(c.CasterId, "CLUTCH!", Palette.GoldLight, 0.3f, 90);
        }

        private IEnumerator AbilityAnim(AbilityResolvedEvent ar)
        {
            var a = BattleEngine.ResolveAbilityId(App.Content, ar.AbilityId);
            hitTier = VfxStyleSheet.TierOf(a);
            var abilityStyle = StyleForAbility(a);
            DodgeBillboard(ar.Tiles.Concat(ar.Targets.Select(State.UnitById).Where(u => u != null).SelectMany(ViewTiles)));
            var caster = State.UnitById(ar.CasterId);
            var hit = ar.Targets.Select(State.UnitById).Where(u => u != null).Select(u => u.Name).Distinct().ToList();
            string who = hit.Count == 0 ? "" : hit.Count <= 2 ? " → " + string.Join(" & ", hit) : $" → {hit.Count} targets";
            ShowBanner(a == null ? "" : caster != null ? $"{caster.Name}: {a.Name}{who}" : a.Name + who);
            if (a != null && a.Kind != AbilityKind.Passive) Billboard(a.Name + who, Art.AbilityIcon(a), Palette.White);
            // The caster's portrait is the cast-splash beat: it slides in and out BEFORE the projectile flies, so it never covers the flight.
            if (a != null && caster != null && a.Kind == AbilityKind.Spell && CutInsEnabled && !ReduceMotion) yield return CasterSlide(caster, a.Name);
            board.ClearMarks();
            Color c = a == null ? Palette.White : a.BaseHeal > 0 ? Palette.Good : ColorFor(a.DamageType);
            foreach (var t in ar.Tiles) board.Mark(t, c, 0.22f);
            var targets = ar.Targets.Select(State.UnitById).Where(u => u != null).ToList();
            EndGhost(ar.CasterId, ar.AbilityId);
            SyncCastRings(lastAt);
            bool itemUse = pendingItemUser >= 0 && pendingItemUser == ar.CasterId;
            string itemId = pendingItemId;
            pendingItemUser = -1; pendingItemId = null;
            if (itemUse && caster != null && a != null)
                yield return ItemVfx(caster, targets, a, itemId);
            else if (caster != null && a != null && a.Kind == AbilityKind.Spell && targets.Count > 0)
            {
                yield return SpellVfx(caster, targets, a, c);
                if (ar.Tiles.Count > 1) yield return AreaSplash(ar.Anchor, ar.Tiles, c);
            }
            else if (caster != null && a != null && a.Kind == AbilityKind.Skill && a.AdjacentOnly && targets.Count > 0)
                yield return MeleeSkillVfx(caster, targets[0]);
            else if (caster != null && a != null && a.Kind == AbilityKind.Skill && (a.Shape == TargetShape.Self || a.Shape == TargetShape.AllAllies) && a.ApplyBuff != null)
                yield return RallyVfx(caster, a);
            else if (caster != null && a != null && a.Motion != ProjectileMotion.None && targets.Count > 0)
            {
                Vector2 from = ViewCenter(caster);
                App.Audio.Sfx("battle.cast", 0.7f);
                yield return Tween.Run(a.Motion == ProjectileMotion.Strike ? 0.18f : 0.26f, k =>
                {
                    board.Lines.ClearLines();
                    foreach (var t in targets)
                    {
                        Vector2 to = ViewCenter(t);
                        var p = ProjectilePoint(a.Motion, from, to, k);
                        board.Lines.Dot(p, 26f, c);
                        board.Lines.Dot(ProjectilePoint(a.Motion, from, to, Mathf.Max(0f, k - 0.08f)), 16f, new Color(c.r, c.g, c.b, 0.5f));
                    }
                });
                board.Lines.ClearLines();
                foreach (var t in targets) NamedImpactFlourish(a.ImpactVfx, ViewCenter(t), abilityStyle);
            }
            else yield return Tween.Wait(Timing.Quick);
            App.StartCoroutine(ClearMarksLater(0.35f));
        }

        /// <summary>Where the last spell mote stopped above each target: the element strike (lightning…) starts there.</summary>
        private readonly Dictionary<int, Vector2> strikeFrom = new Dictionary<int, Vector2>();

        /// <summary>
        /// Every spell plays the same readable arc (owner direction 2026-10-10): the caster draws energy (motes spiral
        /// in), a cast burst, then one mote per target curves to the space above it; the impact (lightning strike,
        /// flame burst, frost ring, heal sparkles …) follows from that point when the damage/heal lands.
        /// </summary>
        private IEnumerator SpellVfx(Unit caster, List<Unit> targets, AbilityDef a, Color c)
        {
            Vector2 from = ViewCenter(caster);
            bool calm = ReduceMotion;
            var st = StyleForAbility(a);
            float sag = st.Shape == "bubble" || st.Shape == "droplet" ? 1f : 0.2f; // heavy trails drip, light ones hang
            board.Units.TryGetValue(caster.Id, out var cv);
            // 1. Drawing energy (a timed cast already drew it when it was queued).
            if (a.CastTime <= 0f) yield return Gather(caster, c);
            // 2. Cast burst.
            App.Audio.Sfx("battle.cast", 0.9f, 1.2f);
            yield return Tween.Run(Timing.Quick, k => { board.Lines.Rings.Clear(); board.Lines.Ring(from, 40f + 90f * k, new Color(c.r, c.g, c.b, 1f - k), 10f); });
            board.Lines.Rings.Clear(); board.Lines.MarkDirtyRepaint();
            if (cv != null) cv.SetGlow(Palette.White, 0f);
            // 3. One mote per target curves up and over to the space above it.
            var ends = targets.ToDictionary(t => t.Id, t => ViewCenter(t) + new Vector2(0f, -BoardView.Tile * 0.85f));
            var bends = targets.ToDictionary(t => t.Id, t => (Random.value < 0.5f ? -1f : 1f) * Random.Range(90f, 160f));
            float travel = calm ? 0.2f : Mathf.Clamp(Vector2.Distance(from, ends.Values.First()) / 1600f, 0.26f, 0.5f);
            // Each projectile is a glowing orb (bloom + white-hot core) leaving a comet tail of fading embers.
            var heads = targets.ToDictionary(t => t.Id, t => (glow: Particle(110f, new Color(c.r, c.g, c.b, 0.95f)), core: Particle(34f, Palette.White)));
            Vector2 Mid(int id, float kk)
            {
                Vector2 to = ends[id];
                var d = to - from; var n = new Vector2(-d.y, d.x).normalized;
                Vector2 ctrl = (from + to) / 2f + n * bends[id] + Vector2.up * -120f;
                float e = Tween.EaseInOut(kk);
                return (1 - e) * (1 - e) * from + 2 * (1 - e) * e * ctrl + e * e * to;
            }
            yield return Tween.Run(travel, k =>
            {
                foreach (var t in targets)
                {
                    var p = Mid(t.Id, k);
                    var (glow, core) = heads[t.Id];
                    glow.style.left = p.x - 55; glow.style.top = p.y - 55;
                    core.style.left = p.x - 17; core.style.top = p.y - 17;
                    if (!calm)
                    {
                        // The comet tail is the element's own particle (embers for fire, shards for ice, bubbles for poison).
                        var ember = Shaped(st, Random.Range(18f, 36f) * 0.7f, Color.Lerp(c, Palette.White, Random.value * 0.35f), Random.Range(0f, 6.28f));
                        var ep = p + new Vector2(Random.Range(-8f, 8f), Random.Range(-8f, 8f));
                        Fly(ember, Timing.Standard, 0f, kk => (ep + Vector2.up * 30f * kk * sag, 1f - kk, 1f - 0.6f * kk));
                    }
                }
            });
            foreach (var (glow, core) in heads.Values) { glow.RemoveFromHierarchy(); core.RemoveFromHierarchy(); }
            App.Audio.Sfx("battle.whoosh", 0.5f, 1.3f);
            // Scan and pure cleanse/hasten utility spells read as their own bespoke burst rather than the
            // generic ring (bible §6.2 future-pass VFX wiring) — plain damage/heal/debuff spells keep it.
            bool isCleanse = a.BaseDamage <= 0 && a.BaseHeal <= 0 && (a.RemovesDebuffs || a.RemovesBuff != null);
            bool isHastenOnly = a.BaseDamage <= 0 && a.BaseHeal <= 0 && a.HastenU > 0f && a.ApplyBuff == null;
            foreach (var t in targets)
            {
                var at = ViewCenter(t);
                if (a.IsScan) App.StartCoroutine(ScanBurst(at));
                else if (isCleanse) App.StartCoroutine(CleanseBurst(at));
                else if (isHastenOnly) App.StartCoroutine(HastenBurst(at));
                else
                {
                    ImpactSplash(at, c, a.BaseDamage > 0 ? 0.8f : 1f);
                    NamedImpactFlourish(a.ImpactVfx, at, st);
                }
            }
            if (a.BaseDamage <= 0 && !a.IsScan) App.Audio.Sfx("element.water", 0.6f, 1.2f);
            foreach (var kv in ends) strikeFrom[kv.Key] = kv.Value;
            if (a.BaseHeal > 0 || (a.Filter == TargetFilter.AllyOnly && !isCleanse && !isHastenOnly && !a.IsScan))
                foreach (var t in targets) App.StartCoroutine(HealBurst(ViewCenter(t)));
        }

        /// <summary>Projectile curves (bible §6.1). Reduce Motion flattens everything to a straight line.</summary>
        private Vector2 ProjectilePoint(ProjectileMotion m, Vector2 from, Vector2 to, float k)
        {
            if (ReduceMotion) m = ProjectileMotion.Straight;
            var d = to - from;
            var n = new Vector2(-d.y, d.x).normalized;
            switch (m)
            {
                case ProjectileMotion.Bezier: return Vector2.Lerp(from, to, k) + Vector2.up * (-160f * Ease.Arc(k));
                case ProjectileMotion.Homing: return Vector2.Lerp(from, to, Ease.InOut(k)) + n * (90f * Ease.Arc(k) * (1f - k));
                case ProjectileMotion.Spiral: { float r = 70f * (1f - k); float ang = k * Mathf.PI * 6f; return Vector2.Lerp(from, to, k) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r; }
                case ProjectileMotion.Twist: return Vector2.Lerp(from, to, k) + n * (40f * Mathf.Sin(k * Mathf.PI * 3f));
                case ProjectileMotion.Strike: { var top = new Vector2(to.x, to.y - 520f); return Vector2.Lerp(top, to, k * k); }
                default: return Vector2.Lerp(from, to, k);
            }
        }

        private IEnumerator ClearMarksLater(float s)
        {
            yield return Tween.Wait(s);
            if (TargetingSlot < 0) board.ClearMarks();
        }

        /// <summary>The colour a spell's energy reads as: its element (fire orange, poison green…), heals green, status spells their status colour.</summary>
        private static Color CastColor(AbilityDef a, string id)
        {
            if (a == null) return Art.SpellColor(id);
            if (a.BaseHeal > 0) return Palette.Good;
            if (a.DamageType != DamageType.Physical && a.DamageType != DamageType.Arcane) return ColorFor(a.DamageType);
            var b = App.Content.TryBuff(a.ApplyBuff);
            if (b != null) return Art.HexColor(b.Color, Art.SpellColor(id));
            // Non-damage utility spells read gold (scan's god-rays, cleanse's breaking chains, hasten's sparkle)
            // instead of falling through to a generic arcane purple (bible §6.2 future-pass VFX wiring).
            if (a.IsScan || a.RemovesDebuffs || a.RemovesBuff != null || a.HastenU > 0f) return Palette.Gold;
            return Art.SpellColor(id);
        }

        /// <summary>Scan: vertical light rays sweep down over the target as its stats are read (bible §6.2 GodRays/ScanRays).</summary>
        private IEnumerator ScanBurst(Vector2 at)
        {
            App.Audio.Sfx("battle.cast", 0.6f, 1.5f);
            var gold = Palette.GoldLight;
            for (int i = 0; i < 6; i++)
            {
                var p = Particle(10f, gold);
                float x0 = at.x + (i - 2.5f) * 24f;
                App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                {
                    p.style.left = x0 - 5; p.style.top = at.y - 160f + 220f * k - 5;
                    p.style.opacity = Ease.Arc(k);
                    if (k >= 1f) p.RemoveFromHierarchy();
                }));
            }
            yield return Tween.Run(Timing.Emphasis, k => { board.Lines.Rings.Clear(); board.Lines.Ring(at, 20f + 90f * k, new Color(gold.r, gold.g, gold.b, 1f - k), 6f); });
            board.Lines.Rings.Clear(); board.Lines.MarkDirtyRepaint();
        }

        /// <summary>Cleanse / dispel: every debuff burns off as breaking gold motes (bible §6.2 AntidoteSparkle).</summary>
        private IEnumerator CleanseBurst(Vector2 at)
        {
            App.Audio.Sfx("battle.heal", 0.7f, 1.3f);
            var gold = new Color(1f, 0.88f, 0.5f);
            for (int i = 0; i < 10; i++)
            {
                var p = Particle(Random.Range(12f, 20f), gold);
                float ang = Random.Range(0f, 6.28f), dist = Random.Range(50f, 110f);
                App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                {
                    var pos = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist * Tween.EaseOut(k) + Vector2.up * (-40f * k);
                    p.style.left = pos.x - 9; p.style.top = pos.y - 9; p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy();
                }));
            }
            yield return Tween.Wait(Timing.Standard);
        }

        /// <summary>Hasten-only spells (Quicken): a gold updraft nudges the target's timeline icon forward.</summary>
        private IEnumerator HastenBurst(Vector2 at)
        {
            App.Audio.Sfx("battle.cast", 0.6f, 1.6f);
            var gold = Palette.Gold;
            for (int i = 0; i < 8; i++)
            {
                var p = Particle(Random.Range(10f, 16f), gold);
                float x0 = Random.Range(-40f, 40f);
                App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                {
                    p.style.left = at.x + x0 - 7; p.style.top = at.y - 120f * Tween.EaseOut(k) - 7;
                    p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy();
                }));
            }
            yield return Tween.Wait(Timing.Standard);
        }

        private static Color ColorFor(DamageType t) => Palette.Element(t);

        private IEnumerator ScanAnim(ScannedEvent sc)
        {
            var u = State.UnitById(sc.UnitId);
            if (u == null) yield break;
            panelUnitId = u.Id;
            SyncPanel();
            ShowBanner($"{u.Name}: HP {sc.Hp}/{sc.MaxHp}  STR {sc.Stats.Strength:0}  VIT {sc.Stats.Vitality:0}  INT {sc.Stats.Intelligence:0}");
            yield return Tween.Wait(Timing.Dramatic);
        }

        private IEnumerator BossAnim(BossPhaseEvent bp)
        {
            App.Audio.Sfx("battle.explosion");
            App.StartCoroutine(Tween.Shake(shake, 20f * Fx, Timing.Emphasis));
            yield return FloatText(bp.UnitId, string.IsNullOrEmpty(bp.Text) ? "ENRAGED!" : bp.Text, new Color(1f, 0.3f, 0.6f), 0.6f, 80);
        }

        private IEnumerator WaveAnim(WaveStartedEvent wv)
        {
            for (int si = 0; si < wv.Spawned.Count; si++)
            {
                int id = wv.Spawned[si];
                var u = State.UnitById(id);
                if (u == null || board.Units.ContainsKey(id)) continue;
                var v = board.Add(u);
                if (si < wv.SpawnedAt.Count) board.Place(v, wv.SpawnedAt[si].At); // it may already have walked on this command
                v.SetHp(u.Hp, u.MaxHp);
                v.Root.style.opacity = 0;
                App.StartCoroutine(Tween.Run(Timing.Standard, k => { v.Root.style.opacity = k; v.Root.style.translate = new Translate(0, -80 * (1 - k)); }, Tween.EaseOut));
            }
            SyncTimeline(null);
            if (InCrucible)
            {
                // The Crucible's waves never run out (§22.10): no "N/total", and a rising sting marks each one.
                ShowBanner(wv.Wave == 0 ? "The Crucible: wave 1" : $"Crucible wave {wv.Wave + 1} · Lv {Crucible.WaveLevel(State.Stage.LevelOverride ?? 1, wv.Wave)}");
                App.Audio.Sfx("battle.crucible_wave", 1f, 1f + Mathf.Min(0.5f, wv.Wave * 0.04f));
                var f = board.Float($"WAVE {wv.Wave + 1}", new Vector2(BoardView.Tile * 3, BoardView.Tile * 4), new Color(1f, 0.55f, 0.3f), 120);
                App.StartCoroutine(FloatAway(f, 1.0f));
                yield return Tween.Wait(Timing.Dramatic);
            }
            else if (wv.Wave > 0 || wv.TotalWaves > 1)
            {
                ShowBanner($"Wave {wv.Wave + 1}/{wv.TotalWaves}");
                var f = board.Float($"WAVE {wv.Wave + 1}", new Vector2(BoardView.Tile * 3, BoardView.Tile * 4), Palette.GoldLight, 120);
                App.StartCoroutine(FloatAway(f, 1.0f));
                yield return Tween.Wait(Timing.Dramatic);
            }
        }

        private IEnumerator ReviveAnim(RevivedEvent rv)
        {
            var u = State.UnitById(rv.UnitId);
            if (u == null) yield break;
            if (!board.Units.TryGetValue(u.Id, out var v)) v = board.Add(u);
            if (v.Dead) { board.UnitLayer.Add(v.Root); v.Dead = false; }
            board.Place(v, rv.At);
            v.SetHp(rv.Hp, u.MaxHp);
            App.Audio.Sfx("battle.heal");
            yield return Tween.Run(Timing.Emphasis, k => { v.Root.style.opacity = k; v.Root.style.scale = new Scale(Vector2.one * (0.6f + 0.4f * k)); });
            yield return FloatText(u.Id, "Revived!", Palette.GoldLight, 0.2f, 70);
        }

        private VisualElement Particle(float size, Color c)
        {
            var p = new VisualElement { pickingMode = PickingMode.Ignore };
            p.style.position = Position.Absolute;
            p.style.width = p.style.height = size;
            p.style.backgroundImage = new StyleBackground(Art.Glow);
            p.style.unityBackgroundImageTintColor = c;
            board.FxLayer.Add(p);
            return p;
        }

        /// <summary>An element's impact (bible §6.2, GG-T253): its vfxstyle.json look at the hit's tier.</summary>
        private IEnumerator ElementBurst(Vector2 at, VfxStyleDef style, VfxTierDef tier)
        {
            Emit(style, at, tier.Count, sizeMul: tier.Size);
            yield return Tween.Wait(style.Motion == "bolt" ? Timing.Quick : Timing.Instant);
        }

        /// <summary>"Drawing energy": motes spiral into the caster while its frame glows in the spell colour.</summary>
        private IEnumerator Gather(Unit caster, Color c)
        {
            Vector2 from = ViewCenter(caster);
            App.Audio.Sfx("battle.cast", 0.5f);
            board.Units.TryGetValue(caster.Id, out var cv);
            cv?.SetGlow(c, 0.8f);
            int motes = ReduceMotion ? 4 : 12;
            for (int i = 0; i < motes; i++)
            {
                var p = Particle(Random.Range(14f, 24f), c);
                float ang = i * Mathf.PI * 2f / motes + Random.Range(-0.2f, 0.2f), dist = Random.Range(120f, 170f), spin = Random.Range(1.5f, 2.5f);
                App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                {
                    float r = dist * (1f - k * k), aa = ang + spin * k;
                    var pos = from + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * r;
                    p.style.left = pos.x - 10; p.style.top = pos.y - 10;
                    p.style.opacity = 0.4f + 0.6f * k; if (k >= 1f) p.RemoveFromHierarchy();
                }));
            }
            yield return Tween.Wait(Timing.Emphasis);
            cv?.SetGlow(Palette.White, 0f);
        }

        /// <summary>The mote end above the unit at <paramref name="at"/>, consumed once.</summary>
        private Vector2? TakeStrikeOrigin(Vector2 at)
        {
            foreach (var kv in strikeFrom)
                if (Mathf.Abs(kv.Value.x - at.x) < BoardView.Tile * 0.6f && kv.Value.y < at.y && at.y - kv.Value.y < BoardView.Tile * 2.2f)
                { strikeFrom.Remove(kv.Key); return kv.Value; }
            return null;
        }

        private IEnumerator HealBurst(Vector2 at)
        {
            Emit(HealStyle, at);
            yield break;
        }

        /// <summary>Little particles radiating from a hit (UI Toolkit, no particle system needed).</summary>
        private void Sparks(Vector2 at, int count, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                var p = new VisualElement { pickingMode = PickingMode.Ignore };
                p.style.position = Position.Absolute;
                float size = Random.Range(10f, 22f);
                p.style.width = p.style.height = size;
                p.style.backgroundImage = new StyleBackground(Art.Glow);
                p.style.unityBackgroundImageTintColor = color;
                board.FxLayer.Add(p);
                float ang = Random.Range(0f, Mathf.PI * 2f), dist = Random.Range(60f, 150f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                App.StartCoroutine(Tween.Run(Timing.Standard, k =>
                {
                    var pos = at + dir * dist * Tween.EaseOut(k) + Vector2.up * (60f * k * k);
                    p.style.left = pos.x - size / 2; p.style.top = pos.y - size / 2;
                    p.style.opacity = 1f - k;
                    if (k >= 1f) p.RemoveFromHierarchy();
                }));
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        private void RefreshUnit(int id)
        {
            var u = State.UnitById(id);
            if (u != null && board.Units.TryGetValue(id, out var v))
            {
                v.SetPlate(u.Facing, u.PlateHp, u.PlateMax);
                v.SetBuffs(u, Colorblind, State.TurnEntryOf(u.Id)?.StunTicks > 0);
                v.SetHp(u.Hp, u.MaxHp);
                if (!u.HasBuff("frozen")) v.Art.style.unityBackgroundImageTintColor = Palette.White; // the frost tint thaws with the status
            }
        }

        private IEnumerator FloatText(int unitId, string text, Color c, float hold, float size = 60)
        {
            var u = State.UnitById(unitId);
            Vector2 at = u != null && board.Units.TryGetValue(unitId, out var v) ? BoardView.CenterOf(v.Tile, v.W, v.H) : new Vector2(BoardView.Tile * 3, BoardView.Tile * 4);
            App.StartCoroutine(FloatAway(board.Float(text, at + new Vector2(0, -50), c, size), 0.8f));
            yield return Tween.Wait(hold);
        }

        private IEnumerator FloatAway(Label l, float seconds)
        {
            float top = l.resolvedStyle.top;
            if (float.IsNaN(top)) top = 0;
            var start = l.style.top.value.value;
            yield return Tween.Run(seconds, k =>
            {
                l.style.top = start - 90f * Tween.EaseOut(k);
                l.style.opacity = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                float s = k < 0.15f ? 0.6f + k / 0.15f * 0.5f : 1.1f - 0.1f * Mathf.Min(1, (k - 0.15f) * 4);
                l.style.scale = new Scale(Vector2.one * s);
            });
            l.RemoveFromHierarchy();
        }

        private IEnumerator EndBattle()
        {
            ended = true;
            CaptureUnity.Raise("battle-end", new CaptureData
            {
                ["stage"] = State.Stage.Id,
                ["result"] = lastEnded == null ? "unknown" : lastEnded.Victory ? "victory" : lastEnded.Escaped ? "escaped" : "defeat",
            });
            SaveRecord();
            RecordEncounterTelemetry(); // GG-T59: one JSONL encounter record, flushed, before the session closes
            TelemetryUnity.Finish(new
            {
                stage = State.Stage.Id,
                result = lastEnded == null ? "unknown" : lastEnded.Victory ? "victory" : lastEnded.Escaped ? "escaped" : "defeat",
                waveReached = State.Wave + 1,
                actions = State.Results.ActionsTaken,
                ticks = State.Results.TicksElapsed,
                coins = State.Results.Coins,
                xp = State.Results.Xp,
                heroesStanding = State.Units.Count(u => u.IsHero && u.Alive),
            });
            yield return Tween.Wait(Timing.Dramatic);
            if (!CommitsToSave || App.Save == null)
            {
                App.Router.Back();
                yield break;
            }
            var report = BattleFactory.Commit(App.Content, App.Save, State);
            App.Persist();
            PostBattleScreen.RouteFrom(report, State.Stage.Id);
        }
    }
}
