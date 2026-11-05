using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>
    /// Staging (owner direction 2026-10-11): "there is a rhythm and tempo to gameplay that requires pauses and
    /// transitions … if I clicked poison and an icon just appeared it would feel meaningless." Every action plays as a
    /// sequence of readable beats — wind-up on the caster, a charging bar that fills while the timeline moves, the cast
    /// splash, a projectile on a curve, the impact splash, and only then the status effect with its icon — and the
    /// status keeps showing (bubbling poison, embers, frost) for as long as it lasts.
    /// </summary>
    public sealed partial class BattleScreen
    {
        // ── Cast ghosts: a hero cast queues and lands inside one action, so the final state no longer holds it during
        //    playback. Playback keeps its own record from CastQueued to the resolve, so the bar/ring/chip and the channel
        //    aura follow the timeline glide. ──
        private sealed class CastGhost { public int EntryId, CasterId, TargetUnitId; public string AbilityId; public Coord TargetTile; public float U; }
        private readonly Dictionary<int, CastGhost> castGhosts = new Dictionary<int, CastGhost>();

        private void AddGhost(CastQueuedEvent cq) =>
            castGhosts[cq.EntryId] = new CastGhost { EntryId = cq.EntryId, CasterId = cq.CasterId, AbilityId = cq.AbilityId, TargetUnitId = cq.TargetUnitId, TargetTile = cq.TargetTile, U = 0f };

        private void EndGhost(int casterId, string abilityId)
        {
            var g = castGhosts.Values.FirstOrDefault(x => x.CasterId == casterId && (abilityId == null || x.AbilityId == abilityId));
            if (g != null) castGhosts.Remove(g.EntryId);
        }

        private void PruneGhosts()
        {
            foreach (var id in castGhosts.Keys.ToList())
                if (State.Timeline.All(e => e.Id != id)) castGhosts.Remove(id);
        }

        private bool IsChanneling(int casterId) =>
            castGhosts.Values.Any(g => g.CasterId == casterId) || State.CastEntryOf(casterId) != null;

        // ── Channel aura: energy keeps circling the caster while its spell charges ──
        private readonly HashSet<int> channeling = new HashSet<int>();

        private void StartChannel(int casterId, Color c)
        {
            if (!channeling.Add(casterId)) return;
            App.StartCoroutine(ChannelAura(casterId, c));
        }

        private IEnumerator ChannelAura(int casterId, Color c)
        {
            board.Units.TryGetValue(casterId, out var v);
            VisualElement halo = null;
            if (v != null)
            {
                halo = new VisualElement { name = "channel-" + casterId, pickingMode = PickingMode.Ignore };
                halo.style.position = Position.Absolute;
                halo.style.left = halo.style.top = halo.style.right = halo.style.bottom = -BoardView.Tile * 0.28f;
                halo.style.backgroundImage = new StyleBackground(Art.Glow);
                halo.style.unityBackgroundImageTintColor = c;
                v.Root.Insert(0, halo);
            }
            float t = 0f, nextMote = 0f;
            int motes = 0;
            while (!ended && v != null && !v.Dead && IsChanneling(casterId))
            {
                t += Tween.Dt;
                if (halo != null) halo.style.opacity = ReduceMotion ? 0.5f : 0.45f + 0.3f * Mathf.Sin(t * 7f);
                if (t >= nextMote && !ReduceMotion)
                {
                    nextMote = t + 0.09f;
                    var centre = BoardView.CenterOf(v.Tile, v.W, v.H);
                    var p = Particle(Random.Range(12f, 22f), Color.Lerp(c, Color.white, Random.Range(0f, 0.4f)));
                    float ang = (motes++ * 0.9f) % 6.283f, r = BoardView.Tile * (0.42f + Random.Range(0f, 0.12f)) * v.W;
                    App.StartCoroutine(Tween.Run(Timing.Dramatic, k =>
                    {
                        float a = ang + k * 3.2f;
                        var pos = centre + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.45f + BoardView.Tile * 0.25f - k * BoardView.Tile * 0.7f);
                        p.style.left = pos.x - 9; p.style.top = pos.y - 9;
                        p.style.opacity = Mathf.Sin(k * Mathf.PI);
                        if (k >= 1f) p.RemoveFromHierarchy();
                    }));
                }
                yield return null;
            }
            halo?.RemoveFromHierarchy();
            channeling.Remove(casterId);
        }

        // ── Splashes ──

        /// <summary>The arrival splash: a ring bursts outward and droplets fly — every spell lands visibly, damage or not.</summary>
        private void ImpactSplash(Vector2 at, Color c, float scale = 1f)
        {
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.style.position = Position.Absolute;
            board.FxLayer.Add(ring);
            App.StartCoroutine(Tween.Run(Timing.Standard, k =>
            {
                float r = (30f + 120f * Tween.EaseOut(k)) * scale;
                ring.style.left = at.x - r; ring.style.top = at.y - r; ring.style.width = ring.style.height = r * 2f;
                ring.style.borderTopLeftRadius = ring.style.borderTopRightRadius = ring.style.borderBottomLeftRadius = ring.style.borderBottomRightRadius = r;
                float w = Mathf.Lerp(14f, 2f, k) * scale;
                ring.style.borderTopWidth = ring.style.borderBottomWidth = ring.style.borderLeftWidth = ring.style.borderRightWidth = w;
                var col = new Color(c.r, c.g, c.b, 1f - k);
                ring.style.borderTopColor = ring.style.borderBottomColor = ring.style.borderLeftColor = ring.style.borderRightColor = col;
                if (k >= 1f) ring.RemoveFromHierarchy();
            }));
            if (!ReduceMotion) Sparks(at, Mathf.RoundToInt(8 * scale), Color.Lerp(c, Color.white, 0.3f));
        }

        /// <summary>An area spell washes over its tiles as a wave from the anchor outward.</summary>
        private IEnumerator AreaSplash(Coord anchor, List<Coord> tiles, Color c)
        {
            int maxD = 0;
            foreach (var t in tiles)
            {
                int d = t.Manhattan(anchor);
                maxD = Mathf.Max(maxD, d);
                var f = new VisualElement { pickingMode = PickingMode.Ignore };
                f.style.position = Position.Absolute;
                f.style.left = t.X * BoardView.Tile - 30; f.style.top = t.Y * BoardView.Tile - 30;
                f.style.width = f.style.height = BoardView.Tile + 60;
                f.style.backgroundImage = new StyleBackground(Art.Glow); // a soft bloom, never an opaque slab over the target
                f.style.unityBackgroundImageTintColor = new Color(c.r, c.g, c.b, 0.8f);
                f.style.opacity = 0f;
                board.FxLayer.Add(f);
                float delay = d * 0.06f;
                App.StartCoroutine(Tween.Run(0.42f + delay, k =>
                {
                    float total = 0.42f + delay;
                    float kk = Mathf.Clamp01((k * total - delay) / 0.42f);
                    f.style.opacity = kk <= 0f ? 0f : Mathf.Sin(kk * Mathf.PI) * 0.75f;
                    f.style.scale = new Scale(Vector2.one * (0.75f + 0.3f * kk));
                    if (k >= 1f) f.RemoveFromHierarchy();
                }));
            }
            App.Audio.Sfx("splash", 0.8f);
            yield return Tween.Wait(0.12f + maxD * 0.06f);
        }

        // ── Status infliction: the effect lands, THEN its icon pops in ──

        private IEnumerator InflictAnim(BuffAppliedEvent ba)
        {
            var u = State.UnitById(ba.UnitId);
            var def = App.Content.TryBuff(ba.BuffId);
            if (u == null || def == null || !board.Units.TryGetValue(u.Id, out var v) || v.Dead) { RefreshUnit(ba.UnitId); yield break; }
            var at = BoardView.CenterOf(v.Tile, v.W, v.H);
            var color = Art.HexColor(def.Color, def.Kind == BuffKind.Buff ? Palette.Mana : new Color(0.8f, 0.4f, 0.9f));
            if (!ReduceMotion) yield return StatusBurst(def.Id, at, color, v);
            else App.Audio.Sfx(def.Kind == BuffKind.Buff ? "cast" : "debuff", 0.6f);
            RefreshUnit(u.Id);
            App.StartCoroutine(Tween.Pop(v.Buffs, 1.6f, 0.28f));
            var aura = AuraFor(u);
            if (aura.a > 0f && u.Id != SelectedHeroId && !ReduceMotion) v.SetGlow(aura, 0.45f);
            // Floated higher than damage numbers so the status name never sits on top of the hit that caused it.
            App.StartCoroutine(FloatAway(board.Float(def.Name + "!", at + new Vector2(0, -120), Color.Lerp(color, Color.white, 0.25f), 50), 0.8f));
            yield return Tween.Wait(Timing.Quick);
        }

        /// <summary>The look of each status taking hold (~0.3–0.45 s).</summary>
        private IEnumerator StatusBurst(string id, Vector2 at, Color c, UnitView v)
        {
            switch (id)
            {
                case "poisoned":
                    App.Audio.Sfx("bubble");
                    for (int i = 0; i < 14; i++) Bubble(at + new Vector2(Random.Range(-70f, 70f), Random.Range(10f, 60f)), new Color(0.5f, 1f, 0.3f), Random.Range(0f, 0.25f), 1.2f);
                    v.Art.style.unityBackgroundImageTintColor = new Color(0.7f, 1f, 0.6f);
                    yield return Tween.Wait(Timing.Emphasis);
                    v.Art.style.unityBackgroundImageTintColor = Color.white;
                    break;
                case "burning":
                    App.Audio.Sfx("fire", 0.8f);
                    yield return ElementBurst(at, DamageType.Fire, false);
                    yield return Tween.Wait(Timing.Standard);
                    break;
                case "frozen":
                    App.Audio.Sfx("ice");
                    for (int i = 0; i < 10; i++)
                    {
                        var p = Particle(18f, Palette.Cleanse);
                        float ang = i * Mathf.PI * 2f / 10f, dist = 170f;
                        App.StartCoroutine(Tween.Run(Timing.Standard, k =>
                        {
                            var pos = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist * (1f - Tween.EaseOut(k));
                            p.style.left = pos.x - 9; p.style.top = pos.y - 9; p.style.opacity = 0.4f + 0.6f * k; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    yield return Tween.Wait(Timing.Standard);
                    v.Art.style.unityBackgroundImageTintColor = new Color(0.65f, 0.85f, 1f);
                    ImpactSplash(at, new Color(0.75f, 0.95f, 1f), 0.8f);
                    yield return Tween.Wait(Timing.Quick);
                    break;
                case "wet":
                    App.Audio.Sfx("splash", 0.6f, 1.3f);
                    for (int i = 0; i < 10; i++) Drip(at + new Vector2(Random.Range(-60f, 60f), -BoardView.Tile * 0.55f), Palette.Hero, Random.Range(0f, 0.2f));
                    yield return Tween.Wait(Timing.Standard);
                    break;
                case "sleep":
                    App.Audio.Sfx("debuff", 0.5f, 0.7f);
                    for (int i = 0; i < 3; i++) App.StartCoroutine(FloatZ(at, i * 0.12f, 1f));
                    yield return Tween.Wait(Timing.Emphasis);
                    break;
                case "slowed":
                    App.Audio.Sfx("debuff", 0.6f, 0.6f);
                    for (int i = 0; i < 10; i++)
                    {
                        var p = Particle(16f, new Color(0.75f, 0.55f, 1f));
                        float ang0 = i * Mathf.PI * 2f / 10f;
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                        {
                            float a = ang0 + k * 2.2f, r = 130f * (1f - 0.6f * k);
                            var pos = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                            p.style.left = pos.x - 8; p.style.top = pos.y - 8; p.style.opacity = 1f - k * 0.7f; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    yield return Tween.Wait(Timing.Emphasis);
                    break;
                case "silenced":
                    App.Audio.Sfx("debuff", 0.7f, 1.2f);
                    yield return Tween.Run(Timing.Standard, k => { board.Lines.Rings.Clear(); board.Lines.Ring(at, 150f * (1f - k) + 30f, new Color(0.8f, 0.8f, 0.9f, k), 8f); });
                    board.Lines.Rings.Clear(); board.Lines.MarkDirtyRepaint();
                    break;
                case "blinded":
                    App.Audio.Sfx("debuff", 0.7f, 0.9f);
                    for (int i = 0; i < 8; i++)
                    {
                        var p = Particle(Random.Range(40f, 60f), new Color(0.15f, 0.12f, 0.2f, 0.9f));
                        var off = new Vector2(Random.Range(-60f, 60f), Random.Range(-70f, -20f));
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                        {
                            var pos = at + off * (0.4f + 0.6f * k);
                            p.style.left = pos.x - 25; p.style.top = pos.y - 25; p.style.opacity = Mathf.Sin(k * Mathf.PI); if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    yield return Tween.Wait(Timing.Emphasis);
                    break;
                case "taunting":
                    yield return TauntWave(at, v);
                    break;
                case "empowered":
                    for (int i = 0; i < 12; i++)
                    {
                        var p = Particle(Random.Range(14f, 24f), Color.Lerp(Palette.Buff, Color.white, Random.value * 0.4f));
                        float x0 = Random.Range(-60f, 60f), rise = Random.Range(120f, 220f);
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                        {
                            p.style.left = at.x + x0 - 9; p.style.top = at.y + 50 - rise * Tween.EaseOut(k) - 9; p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                    }
                    yield return Tween.Run(Timing.Standard, k => v.Root.style.scale = new Scale(Vector2.one * (1f + 0.12f * Mathf.Sin(k * Mathf.PI))));
                    break;
                case "protection":
                    App.Audio.Sfx("cast", 0.6f, 1.4f);
                    yield return Tween.Run(Timing.Standard, k => { board.Lines.Rings.Clear(); board.Lines.Ring(at, 60f + 50f * Tween.EaseOut(k), new Color(0.6f, 1f, 0.7f, 1f - k * 0.6f), 10f); });
                    board.Lines.Rings.Clear(); board.Lines.MarkDirtyRepaint();
                    break;
                case "regen":
                    App.Audio.Sfx("heal", 0.5f, 1.2f);
                    yield return HealBurst(at);
                    yield return Tween.Wait(Timing.Standard);
                    break;
                default:
                    App.Audio.Sfx("debuff", 0.6f);
                    ImpactSplash(at, c, 0.7f);
                    yield return Tween.Wait(Timing.Standard);
                    break;
            }
        }

        /// <summary>Taunt: two red shockwaves roll off the tank, and every enemy's eyes snap to it (threat lines).</summary>
        private IEnumerator TauntWave(Vector2 at, UnitView v)
        {
            App.Audio.Sfx("taunt");
            App.StartCoroutine(Tween.Shake(shake, 8f * Fx, Timing.Standard));
            var red = new Color(1f, 0.3f, 0.2f);
            var foes = State.Units.Where(u => u.Alive && !u.IsHero && board.Units.ContainsKey(u.Id)).ToList();
            yield return Tween.Run(Timing.Emphasis, k =>
            {
                board.Lines.ClearLines();
                board.Lines.Ring(at, 50f + 300f * k, new Color(red.r, red.g, red.b, 1f - k), 22f);
                if (k > 0.25f) board.Lines.Ring(at, 50f + 300f * (k - 0.25f) / 0.75f, new Color(1f, 0.6f, 0.4f, 1f - k), 12f);
                foreach (var f in foes)
                {
                    var from = ViewCenter(f);
                    board.Lines.Add(from, Vector2.Lerp(from, at, Mathf.Min(1f, k * 1.6f)), new Color(1f, 0.35f, 0.25f, 0.85f * (1f - k * 0.5f)), 9f);
                }
            });
            foreach (var f in foes) if (board.Units.TryGetValue(f.Id, out var fv)) { fv.SetGlow(red, 0.7f); App.StartCoroutine(UnglowLater(fv, 0.35f)); }
            board.Lines.ClearLines();
        }

        private IEnumerator UnglowLater(UnitView v, float s)
        {
            yield return Tween.Wait(s);
            v.SetGlow(Color.white, 0f);
        }

        private void Bubble(Vector2 from, Color c, float delay, float size = 1f)
        {
            var p = Particle(Random.Range(14f, 26f) * size, c);
            float rise = Random.Range(60f, 140f), wob = Random.Range(0f, 6.28f);
            App.StartCoroutine(Tween.Run(0.75f + delay, k =>
            {
                float kk = Mathf.Clamp01((k * (0.75f + delay) - delay) / 0.75f);
                p.style.scale = new Scale(Vector2.one * (0.5f + kk * 0.9f));
                p.style.left = from.x + Mathf.Sin(wob + kk * 8f) * 10f - 10; p.style.top = from.y - rise * kk - 10;
                p.style.opacity = kk <= 0f ? 0f : kk > 0.85f ? (1f - kk) / 0.15f : 0.95f;
                if (k >= 1f) p.RemoveFromHierarchy();
            }));
        }

        private void Drip(Vector2 from, Color c, float delay)
        {
            var p = Particle(14f, c);
            float fall = Random.Range(120f, 200f);
            App.StartCoroutine(Tween.Run(0.45f + delay, k =>
            {
                float kk = Mathf.Clamp01((k * (0.45f + delay) - delay) / 0.45f);
                p.style.left = from.x - 7; p.style.top = from.y + fall * kk * kk - 7;
                p.style.opacity = kk <= 0f ? 0f : 1f - kk * 0.6f;
                if (k >= 1f) p.RemoveFromHierarchy();
            }));
        }

        private IEnumerator FloatZ(Vector2 at, float delay, float size)
        {
            yield return Tween.Wait(delay);
            var l = board.Float("z", at + new Vector2(30f, -40f), new Color(0.75f, 0.8f, 1f), 46 * size);
            var start = new Vector2(l.style.left.value.value, l.style.top.value.value);
            yield return Tween.Run(Timing.Dramatic, k =>
            {
                l.style.left = start.x + 50f * k; l.style.top = start.y - 90f * k;
                l.style.opacity = 1f - k;
                l.style.scale = new Scale(Vector2.one * (0.7f + 0.6f * k));
            });
            l.RemoveFromHierarchy();
        }

        // ── Lingering status: poison keeps bubbling, burns smoulder, frost glitters, sleepers snore ──
        private float lingerClock;
        private int lingerBeat;

        private void TickLinger(float dt)
        {
            if (ReduceMotion || board == null || State == null || ended) return;
            lingerClock += dt;
            if (lingerClock < 0.32f) return;
            lingerClock = 0f;
            lingerBeat++;
            if (board.FxLayer.childCount > 220) return;
            foreach (var u in State.Units)
            {
                if (!u.Alive || u.Buffs.Count == 0 || !board.Units.TryGetValue(u.Id, out var v) || v.Dead) continue;
                var b = u.Buffs[(lingerBeat + u.Id) % u.Buffs.Count];
                var at = BoardView.CenterOf(v.Tile, v.W, v.H);
                switch (b.Def.Id)
                {
                    case "poisoned": Bubble(at + new Vector2(Random.Range(-55f, 55f), Random.Range(20f, 60f)), new Color(0.5f, 1f, 0.3f, 0.9f), 0f, 0.8f); break;
                    case "burning":
                    {
                        var p = Particle(Random.Range(12f, 22f), Color.Lerp(Palette.Gold, new Color(1f, 0.3f, 0.05f), Random.value));
                        float x0 = Random.Range(-50f, 50f), rise = Random.Range(70f, 130f);
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k => { p.style.left = at.x + x0 - 8; p.style.top = at.y + 40 - rise * k - 8; p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy(); }));
                        break;
                    }
                    case "frozen":
                    case "wet":
                    {
                        bool frozen = b.Def.Id == "frozen";
                        if (frozen) v.Art.style.unityBackgroundImageTintColor = new Color(0.65f, 0.85f, 1f);
                        if (frozen)
                        {
                            var p = Particle(14f, Palette.Cleanse);
                            var off = new Vector2(Random.Range(-70f, 70f), Random.Range(-70f, 70f));
                            App.StartCoroutine(Tween.Run(Timing.Emphasis, k => { p.style.left = at.x + off.x - 7; p.style.top = at.y + off.y - 7; p.style.opacity = Mathf.Sin(k * Mathf.PI); if (k >= 1f) p.RemoveFromHierarchy(); }));
                        }
                        else Drip(at + new Vector2(Random.Range(-50f, 50f), -50f), Palette.Hero.WithA(0.8f), 0f);
                        break;
                    }
                    case "sleep": if (lingerBeat % 3 == 0) App.StartCoroutine(FloatZ(at, 0f, 0.7f)); break;
                    case "empowered":
                    case "taunting":
                    {
                        var col = b.Def.Id == "taunting" ? new Color(1f, 0.35f, 0.25f) : Palette.Buff;
                        var p = Particle(Random.Range(10f, 18f), col);
                        float ang = Random.Range(0f, 6.28f);
                        App.StartCoroutine(Tween.Run(Timing.Emphasis, k =>
                        {
                            var pos = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (60f + 40f * k) + Vector2.up * (-50f * k);
                            p.style.left = pos.x - 7; p.style.top = pos.y - 7; p.style.opacity = 1f - k; if (k >= 1f) p.RemoveFromHierarchy();
                        }));
                        break;
                    }
                    case "regen": if (lingerBeat % 2 == 0) Bubble(at + new Vector2(Random.Range(-50f, 50f), 40f), Palette.Good.WithA(0.85f), 0f, 0.7f); break;
                }
            }
        }

        // ── Items: a potion is uncorked and drunk; a bottle is thrown, arcs, and shatters ──
        private int pendingItemUser = -1;
        private string pendingItemId;

        private static Color LiquidFor(AbilityDef a)
        {
            if (a.BaseDamage > 0) return ColorFor(a.DamageType);
            if (a.GrantsOrbs > 0) return Art.Mana;
            if (a.RemovesDebuffs || a.RemovesBuff != null) return new Color(0.55f, 1f, 0.55f);
            if (a.Revives) return Palette.Gold;
            return new Color(1f, 0.4f, 0.5f);
        }

        private IEnumerator ItemVfx(Unit user, List<Unit> targets, AbilityDef a, string itemId)
        {
            if (!board.Units.TryGetValue(user.Id, out var uv)) yield break;
            var tex = Art.ItemIcon(itemId) ?? Art.AbilityIcon(a);
            var liquid = LiquidFor(a);
            Vector2 from = BoardView.CenterOf(uv.Tile, uv.W, uv.H);
            bool thrown = targets.Count > 0 && targets.Any(t => t != user);
            var bottle = new VisualElement { pickingMode = PickingMode.Ignore };
            bottle.style.position = Position.Absolute;
            bottle.style.width = bottle.style.height = 140;
            if (tex != null) { bottle.style.backgroundImage = new StyleBackground(tex); bottle.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain); }
            else { bottle.style.backgroundImage = new StyleBackground(Art.Orb); bottle.style.unityBackgroundImageTintColor = liquid; }
            board.FxLayer.Add(bottle);
            void Put(Vector2 p) { bottle.style.left = p.x - 70; bottle.style.top = p.y - 70; }
            // 1. The user produces it: the bottle rises out of the frame.
            Vector2 above = from + new Vector2(0, -BoardView.Tile * 0.62f);
            App.Audio.Sfx("pickup", 0.6f);
            yield return Tween.Run(Timing.Standard, k => { Put(Vector2.Lerp(from, above, Tween.EaseOut(k))); bottle.style.scale = new Scale(Vector2.one * (0.4f + 0.6f * k)); });
            if (!thrown)
            {
                // 2a. Drink: uncork, tip, and the liquid pours into the user.
                App.Audio.Sfx("glug");
                yield return Tween.Run(Timing.Emphasis, k =>
                {
                    bottle.style.rotate = new Rotate(-120f * Tween.EaseInOut(Mathf.Min(1f, k * 1.6f)));
                    if (Random.value < 0.6f && !ReduceMotion)
                    {
                        var d = Particle(Random.Range(10f, 18f), liquid);
                        var mouth = above + new Vector2(-34f, 4f);
                        App.StartCoroutine(Tween.Run(Timing.Standard, kk => { var pos = Vector2.Lerp(mouth, from + new Vector2(Random.Range(-10f, 10f), -10f), kk * kk); d.style.left = pos.x - 7; d.style.top = pos.y - 7; d.style.opacity = 1f - kk * 0.5f; if (kk >= 1f) d.RemoveFromHierarchy(); }));
                    }
                });
                yield return Tween.Fade(bottle, 1f, 0f, 0.14f);
                bottle.RemoveFromHierarchy();
                uv.SetGlow(liquid, 0.8f);
                ImpactSplash(from, liquid, 0.7f);
                App.StartCoroutine(UnglowLater(uv, 0.3f));
                yield return Tween.Wait(Timing.Instant);
                yield break;
            }
            // 2b. Throw: a spinning arc to the target, then the glass shatters into a splash.
            var target = targets.FirstOrDefault(t => t != user) ?? targets[0];
            Vector2 to = ViewCenter(target);
            App.Audio.Sfx("whoosh");
            float travel = Mathf.Clamp(Vector2.Distance(above, to) / 1500f, 0.28f, 0.5f);
            yield return Tween.Run(travel, k =>
            {
                var p = Vector2.Lerp(above, to, k) + Vector2.up * (-200f * Mathf.Sin(k * Mathf.PI));
                Put(p);
                bottle.style.rotate = new Rotate(540f * k);
            });
            bottle.RemoveFromHierarchy();
            App.Audio.Sfx("glass");
            foreach (var t in targets) ImpactSplash(ViewCenter(t), liquid, 1.1f);
            if (!ReduceMotion) Sparks(to, 10, Palette.Text);
            yield return Tween.Wait(Timing.Quick);
        }

        // ── Skills ──

        /// <summary>Melee skills (Cleave, Shield Bash): a lunge and a slashing arc across the target.</summary>
        private IEnumerator MeleeSkillVfx(Unit caster, Unit target)
        {
            if (!board.Units.TryGetValue(caster.Id, out var cv)) yield break;
            var a = ViewCenter(caster);
            var b = ViewCenter(target);
            var dir = (b - a).normalized;
            float lunge = 46f * Fx + 8f;
            App.Audio.Sfx("dash", 0.8f);
            yield return Tween.Run(Timing.Instant, k => cv.Root.style.translate = new Translate(dir.x * lunge * k, dir.y * lunge * k));
            var n = new Vector2(-dir.y, dir.x);
            yield return Tween.Run(Timing.Quick, k =>
            {
                board.Lines.ClearLines();
                for (int i = 0; i <= 10; i++)
                {
                    float s = i / 10f;
                    if (s > k) break;
                    float ang = Mathf.Lerp(-1f, 1f, s);
                    var p = b + n * (ang * 90f) - dir * (40f * (1f - ang * ang));
                    board.Lines.Dot(p, 16f - 8f * Mathf.Abs(ang), Palette.White.WithA(0.95f));
                }
            });
            board.Lines.ClearLines();
            App.StartCoroutine(Tween.Run(Timing.Quick, k => cv.Root.style.translate = new Translate(dir.x * lunge * (1 - k), dir.y * lunge * (1 - k))));
        }

        /// <summary>Self / party skills (Taunt, War Cry): the hero rears up and the call rolls out.</summary>
        private IEnumerator RallyVfx(Unit caster, AbilityDef a)
        {
            if (!board.Units.TryGetValue(caster.Id, out var cv)) yield break;
            var buff = App.Content.TryBuff(a.ApplyBuff);
            var c = Art.HexColor(buff?.Color, Palette.Neutral);
            var at = ViewCenter(caster);
            cv.SetGlow(c, 0.9f);
            yield return Tween.Run(Timing.Standard, k => cv.Root.style.scale = new Scale(Vector2.one * (1f + 0.16f * Mathf.Sin(k * Mathf.PI * 0.5f))));
            if (buff != null && buff.OutgoingDamageMultiplier > 1f)
            {
                App.Audio.Sfx("warcry");
                yield return Tween.Run(Timing.Emphasis, k => { board.Lines.Rings.Clear(); board.Lines.Ring(at, 60f + 520f * k, new Color(c.r, c.g, c.b, 1f - k), 12f); });
                board.Lines.Rings.Clear(); board.Lines.MarkDirtyRepaint();
            }
            yield return Tween.Run(Timing.Quick, k => cv.Root.style.scale = new Scale(Vector2.one * (1.16f - 0.16f * k)));
            cv.SetGlow(Color.white, 0f);
        }

        // ── Armor plates (§13.5): the blow rings off the steel; enough of them and it shatters ──

        private Vector2 PlateEdge(UnitView v, Coord facing)
        {
            var c = BoardView.CenterOf(v.Tile, v.W, v.H);
            return c + new Vector2(facing.X * BoardView.Tile * 0.45f * v.W, facing.Y * BoardView.Tile * 0.45f * v.H);
        }

        private IEnumerator PlateHitAnim(PlateHitEvent e)
        {
            var u = State.UnitById(e.UnitId);
            if (u == null || !board.Units.TryGetValue(u.Id, out var v)) yield break;
            var at = PlateEdge(v, u.Facing);
            App.Audio.Sfx("clang", 0.9f, Random.Range(0.92f, 1.08f));
            if (!ReduceMotion) Sparks(at, 10, new Color(1f, 0.95f, 0.75f));
            v.SetPlate(u.Facing, e.Left > 0 ? e.Left : 1, e.Max);
            v.Plate.style.backgroundColor = Color.white;
            App.StartCoroutine(Tween.Shake(v.Plate, 8f * Fx, Timing.Quick));
            App.StartCoroutine(FloatAway(board.Float($"CLANG! −{e.Amount}", at + new Vector2(0, -30), Palette.Info, 50), 0.7f));
            yield return Tween.Wait(Timing.Quick);
            v.Plate.style.backgroundColor = new Color(0.72f, 0.76f, 0.84f);
            v.SetPlate(u.Facing, e.Left, e.Max);
        }

        private IEnumerator PlateBrokenAnim(PlateBrokenEvent e)
        {
            var u = State.UnitById(e.UnitId);
            if (u == null || !board.Units.TryGetValue(u.Id, out var v)) yield break;
            var at = PlateEdge(v, u.Facing);
            App.Audio.Sfx("shatter", 1f, 0.8f);
            v.SetPlate(u.Facing, 0, 1);
            if (!ReduceMotion)
                for (int i = 0; i < 9; i++)
                {
                    var shard = new VisualElement { pickingMode = PickingMode.Ignore };
                    shard.style.position = Position.Absolute;
                    shard.style.width = Random.Range(14f, 30f); shard.style.height = Random.Range(8f, 16f);
                    shard.style.backgroundColor = new Color(0.78f, 0.82f, 0.9f);
                    board.FxLayer.Add(shard);
                    var vel = new Vector2(Random.Range(-260f, 260f), Random.Range(-320f, -120f));
                    float spin = Random.Range(-720f, 720f);
                    App.StartCoroutine(Tween.Run(Timing.Dramatic, k =>
                    {
                        var p = at + vel * k + new Vector2(0, 700f * k * k);
                        shard.style.left = p.x; shard.style.top = p.y; shard.style.rotate = new Rotate(spin * k); shard.style.opacity = 1f - k;
                        if (k >= 1f) shard.RemoveFromHierarchy();
                    }));
                }
            App.StartCoroutine(Tween.Shake(shake, 10f * Fx, Timing.Standard));
            yield return FloatText(u.Id, "ARMOR BROKEN!", new Color(1f, 0.85f, 0.5f), 0.3f, 64);
        }

        // ── Pickups (§13.3.1) ──

        private IEnumerator PickupDropAnim(PickupDroppedEvent e)
        {
            // Drawn where it dropped (the event's tile), not where the end-of-command State has it: since GG-T228 a pickup
            // can be shoved later in the same command, and its PickupMovedEvent slides it from here.
            var live = State.Pickups.FirstOrDefault(x => x.Id == e.PickupEntryId);
            var p = new Pickup { Id = e.PickupEntryId, PickupId = e.PickupId, At = e.At, ActionsLeft = live?.ActionsLeft ?? BattleConfig.PickupLifetimeActions };
            App.Content.Pickups.TryGetValue(e.PickupId, out var def);
            var v = board.PickupViews.TryGetValue(p.Id, out var ex) ? ex : board.AddPickup(p, def);
            App.Audio.Sfx("powerup", 0.7f, 0.8f);
            Billboard($"{def?.Name ?? "Pickup"} dropped! Walk a hero over it", Art.UiIcon(def?.Icon), Art.HexColor(def?.Color, Color.white));
            yield return Tween.Run(Timing.Emphasis, k =>
            {
                float hop = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 1.5f)) * (1f - k) * 120f;
                v.style.translate = new Translate(0, -hop);
                v.style.scale = new Scale(Vector2.one * Mathf.Min(1f, 0.3f + k * 1.4f));
            });
            v.style.translate = new Translate(0, 0);
        }

        private IEnumerator PickupCollectAnim(PickupCollectedEvent e)
        {
            App.Content.Pickups.TryGetValue(e.PickupId, out var def);
            var c = Art.HexColor(def?.Color, Color.white);
            var hero = State.UnitById(e.UnitId);
            App.Audio.Sfx("powerup");
            if (board.PickupViews.TryGetValue(e.PickupEntryId, out var v))
            {
                var start = new Vector2(v.resolvedStyle.left, v.resolvedStyle.top);
                var to = hero != null ? ViewCenter(hero) - new Vector2(BoardView.Tile * 0.28f, BoardView.Tile * 0.28f + 40f) : start;
                yield return Tween.Run(Timing.Quick, k => { v.style.left = Mathf.Lerp(start.x, to.x, k); v.style.top = Mathf.Lerp(start.y, to.y, k); v.style.scale = new Scale(Vector2.one * (1f + 0.4f * k)); });
                v.RemoveFromHierarchy();
                board.PickupViews.Remove(e.PickupEntryId);
            }
            if (hero != null)
            {
                var at = ViewCenter(hero);
                ImpactSplash(at, c, 0.9f);
                Billboard($"{hero.Name}: {def?.Name ?? "Pickup"} — {def?.Description}", Art.UiIcon(def?.Icon), c);
                yield return FloatText(hero.Id, def?.Name ?? "Pickup", c, 0.2f, 56);
            }
        }

        private IEnumerator PickupExpiredAnim(PickupExpiredEvent e)
        {
            if (!board.PickupViews.TryGetValue(e.PickupEntryId, out var v)) yield break;
            App.Audio.Sfx("miss", 0.5f, 1.4f);
            yield return Tween.Run(Timing.Standard, k => { v.style.opacity = 1f - k; v.style.scale = new Scale(Vector2.one * (1f - 0.6f * k)); });
            v.RemoveFromHierarchy();
            board.PickupViews.Remove(e.PickupEntryId);
        }
    }
}
