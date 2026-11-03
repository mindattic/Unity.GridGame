using System.Collections.Generic;
using GridGame.Core;
using GridGame.Core.Battle;
using GridGame.Core.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>One actor on the board: framed portrait crop, HP strip, buff pips (turn order lives on the timeline).</summary>
    public sealed class UnitView
    {
        public readonly int Id;
        public readonly VisualElement Root;
        public readonly VisualElement Frame;
        public readonly VisualElement Art;
        public readonly VisualElement Keyline;
        public readonly VisualElement Hp;
        public readonly VisualElement HpFill;
        public readonly VisualElement Buffs;
        public readonly VisualElement Glow;
        public readonly CastRing Cast;
        public readonly VisualElement CastChip;
        /// <summary>The charging bar (owner direction 2026-10-11): fills left to right as a cast rides the timeline.</summary>
        public readonly VisualElement CastBar;
        private readonly VisualElement castBarFill;
        /// <summary>The armor plate (§13.5): a riveted steel bar on the edge the unit faces.</summary>
        public readonly VisualElement Plate;
        /// <summary>Hero gear durability telegraph (GG-T39): thin weapon/armor wear bars, and the
        /// cracked-weapon badge that appears once the weapon is on its last (×1.5, then shatter) point.</summary>
        public readonly VisualElement Gear;
        private readonly VisualElement weaponBarEl;
        private readonly VisualElement armorBarEl;
        public readonly VisualElement WeaponFill;
        public readonly VisualElement ArmorFill;
        public readonly VisualElement WeaponCrack;
        private bool weaponPulseAmber, armorPulseAmber, weaponGlow;
        private readonly float tileSize;
        public Coord Tile;
        public int W = 1, H = 1;
        public bool Dead;

        public UnitView(Unit u, float tile)
        {
            Id = u.Id;
            W = u.W; H = u.H;
            Root = new VisualElement { name = "unit-" + u.Id, pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.width = tile * u.W;
            Root.style.height = tile * u.H;

            Glow = new VisualElement { pickingMode = PickingMode.Ignore };
            Glow.style.position = Position.Absolute;
            Glow.style.left = Glow.style.top = -tile * 0.18f;
            Glow.style.right = Glow.style.bottom = -tile * 0.18f;
            Glow.style.backgroundImage = new StyleBackground(App.Art.Glow);
            Glow.style.opacity = 0f;
            Root.Add(Glow);

            // Same frame as every menu portrait (.thumb): white rounded border over the window gradient.
            Frame = new VisualElement { pickingMode = PickingMode.Ignore };
            Frame.AddToClassList("unit-frame");
            Frame.style.position = Position.Absolute;
            Frame.style.left = Frame.style.top = 6;
            Frame.style.right = Frame.style.bottom = 6;
            Frame.style.backgroundImage = new StyleBackground(App.Art.Window);
            Root.Add(Frame);

            // The token is a window onto the unit's real portrait, framed by its class Thumbnail (§12.1.1, GG-T221).
            // The host flips (so the framing mirrors about the token's centre); Art is the portrait child that tints.
            var artHost = new VisualElement { name = "token-art", pickingMode = PickingMode.Ignore };
            artHost.style.position = Position.Absolute;
            artHost.style.left = artHost.style.top = artHost.style.right = artHost.style.bottom = 0;
            if (!u.IsHero && !u.IsNeutral) artHost.style.scale = new Scale(new Vector2(-1, 1)); // enemies face the party
            Frame.Add(artHost);
            // A corpse shows the fallen unit's own art, upright and greyed (§14.7; owner 2026-10-19: never turn art sideways).
            Art = Ui.PortraitThumb(artHost, u.IsCorpse && u.CorpseOf != null ? u.CorpseOf : u.Def.Id);
            if (u.IsCorpse)
            {
                Art.style.unityBackgroundImageTintColor = new Color(0.42f, 0.38f, 0.38f);
                Frame.style.opacity = 0.8f;
            }

            // Team keyline just inside the white border, so heroes, foes and bosses still read apart.
            Keyline = new VisualElement { name = "keyline", pickingMode = PickingMode.Ignore };
            Keyline.AddToClassList("unit-keyline");
            Frame.Add(Keyline);
            SetTeamLook(u);

            Hp = new VisualElement { pickingMode = PickingMode.Ignore };
            Hp.AddToClassList("gauge");
            Hp.style.position = Position.Absolute;
            Hp.style.left = 18; Hp.style.right = 18; Hp.style.bottom = 12; Hp.style.height = 18;
            HpFill = new VisualElement { name = "fill" };
            HpFill.AddToClassList("gauge-fill");
            Hp.Add(HpFill);
            Root.Add(Hp);

            Buffs = new VisualElement { name = "buffs-" + u.Id, pickingMode = PickingMode.Ignore };
            Buffs.style.position = Position.Absolute;
            Buffs.style.flexDirection = FlexDirection.Row;
            Buffs.style.right = 10; Buffs.style.top = 10;
            Root.Add(Buffs);


            // Casting: countdown traced around the caster's frame. Targeted: the incoming spell's chip in the corner.
            Cast = new CastRing { name = "castring-" + u.Id, pickingMode = PickingMode.Ignore };
            Cast.style.display = DisplayStyle.None;
            Root.Add(Cast);
            CastChip = new VisualElement { name = "castchip-" + u.Id, pickingMode = PickingMode.Ignore };
            CastChip.style.position = Position.Absolute;
            CastChip.style.left = 4; CastChip.style.top = 4;
            CastChip.style.width = CastChip.style.height = 50;
            CastChip.style.borderTopLeftRadius = CastChip.style.borderTopRightRadius = CastChip.style.borderBottomLeftRadius = CastChip.style.borderBottomRightRadius = 12;
            CastChip.style.borderTopWidth = CastChip.style.borderBottomWidth = CastChip.style.borderLeftWidth = CastChip.style.borderRightWidth = 3;
            CastChip.style.backgroundColor = new Color(0.06f, 0.07f, 0.17f, 1f);
            CastChip.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            CastChip.style.display = DisplayStyle.None;
            CastChip.Add(new Label { name = "castchip-letter", pickingMode = PickingMode.Ignore });
            CastChip[0].AddToClassList("shadow");
            CastChip[0].style.fontSize = 30; CastChip[0].style.unityFontStyleAndWeight = FontStyle.Bold;
            CastChip[0].style.unityTextAlign = TextAnchor.MiddleCenter; CastChip[0].style.flexGrow = 1;
            Root.Add(CastChip);

            tileSize = tile;
            Plate = new VisualElement { name = "plate-" + u.Id, pickingMode = PickingMode.Ignore };
            Plate.style.position = Position.Absolute;
            Plate.style.backgroundColor = new Color(0.72f, 0.76f, 0.84f);
            Plate.style.borderTopWidth = Plate.style.borderBottomWidth = Plate.style.borderLeftWidth = Plate.style.borderRightWidth = 3;
            Plate.style.borderTopColor = Plate.style.borderLeftColor = new Color(0.95f, 0.97f, 1f);
            Plate.style.borderBottomColor = Plate.style.borderRightColor = new Color(0.3f, 0.32f, 0.4f);
            Plate.style.borderTopLeftRadius = Plate.style.borderTopRightRadius = Plate.style.borderBottomLeftRadius = Plate.style.borderBottomRightRadius = 6;
            Plate.style.display = DisplayStyle.None;
            for (int i = 0; i < 3; i++)
            {
                var rivet = new VisualElement { name = "rivet", pickingMode = PickingMode.Ignore };
                rivet.style.width = rivet.style.height = 8;
                rivet.style.backgroundColor = new Color(0.35f, 0.37f, 0.45f);
                rivet.style.borderTopLeftRadius = rivet.style.borderTopRightRadius = rivet.style.borderBottomLeftRadius = rivet.style.borderBottomRightRadius = 4;
                Plate.Add(rivet);
            }
            Plate.style.justifyContent = Justify.SpaceAround; Plate.style.alignItems = Align.Center;
            Root.Add(Plate);

            CastBar = new VisualElement { name = "castbar-" + u.Id, pickingMode = PickingMode.Ignore };
            CastBar.style.position = Position.Absolute;
            CastBar.style.left = 18; CastBar.style.right = 18; CastBar.style.bottom = 34; CastBar.style.height = 14;
            CastBar.style.backgroundColor = new Color(0.03f, 0.04f, 0.12f, 0.9f);
            CastBar.style.borderTopLeftRadius = CastBar.style.borderTopRightRadius = CastBar.style.borderBottomLeftRadius = CastBar.style.borderBottomRightRadius = 7;
            CastBar.style.borderTopWidth = CastBar.style.borderBottomWidth = CastBar.style.borderLeftWidth = CastBar.style.borderRightWidth = 2;
            CastBar.style.borderTopColor = CastBar.style.borderBottomColor = CastBar.style.borderLeftColor = CastBar.style.borderRightColor = new Color(1f, 1f, 1f, 0.7f);
            CastBar.style.overflow = Overflow.Hidden;
            CastBar.style.display = DisplayStyle.None;
            castBarFill = new VisualElement { pickingMode = PickingMode.Ignore };
            castBarFill.style.position = Position.Absolute;
            castBarFill.style.left = castBarFill.style.top = castBarFill.style.bottom = 0;
            CastBar.Add(castBarFill);
            Root.Add(CastBar);

            // Hero gear durability (GG-T39, §24.5): a thin weapon/armor wear strip above the cast bar,
            // plus a badge that only shows once the weapon is on its last point.
            Gear = new VisualElement { name = "gear-" + u.Id, pickingMode = PickingMode.Ignore };
            Gear.style.position = Position.Absolute;
            Gear.style.left = 18; Gear.style.right = 18; Gear.style.bottom = 50; Gear.style.height = 8;
            Gear.style.flexDirection = FlexDirection.Row;
            Gear.style.display = DisplayStyle.None;
            weaponBarEl = new VisualElement { name = "weapon-bar-" + u.Id };
            weaponBarEl.AddToClassList("gauge");
            weaponBarEl.style.height = 8; weaponBarEl.style.flexGrow = 1; weaponBarEl.style.marginRight = 4;
            WeaponFill = new VisualElement { name = "weapon-fill-" + u.Id };
            WeaponFill.AddToClassList("gauge-fill");
            weaponBarEl.Add(WeaponFill);
            Gear.Add(weaponBarEl);
            armorBarEl = new VisualElement { name = "armor-bar-" + u.Id };
            armorBarEl.AddToClassList("gauge");
            armorBarEl.style.height = 8; armorBarEl.style.flexGrow = 1;
            ArmorFill = new VisualElement { name = "armor-fill-" + u.Id };
            ArmorFill.AddToClassList("gauge-fill");
            armorBarEl.Add(ArmorFill);
            Gear.Add(armorBarEl);
            Root.Add(Gear);

            // A cracked badge, not a glyph (GG-LAW-11): a red roundel with two crossed white shards.
            WeaponCrack = new VisualElement { name = "weapon-crack-" + u.Id, pickingMode = PickingMode.Ignore };
            WeaponCrack.style.position = Position.Absolute;
            WeaponCrack.style.left = 14; WeaponCrack.style.bottom = 60;
            WeaponCrack.style.width = WeaponCrack.style.height = 24;
            WeaponCrack.style.borderTopLeftRadius = WeaponCrack.style.borderTopRightRadius = WeaponCrack.style.borderBottomLeftRadius = WeaponCrack.style.borderBottomRightRadius = 12;
            WeaponCrack.style.backgroundColor = new Color(0.5f, 0.06f, 0.05f, 0.92f);
            WeaponCrack.style.borderTopWidth = WeaponCrack.style.borderBottomWidth = WeaponCrack.style.borderLeftWidth = WeaponCrack.style.borderRightWidth = 2;
            WeaponCrack.style.borderTopColor = WeaponCrack.style.borderBottomColor = WeaponCrack.style.borderLeftColor = WeaponCrack.style.borderRightColor = new Color(1f, 0.35f, 0.3f);
            WeaponCrack.style.display = DisplayStyle.None;
            var shardA = new VisualElement { pickingMode = PickingMode.Ignore };
            shardA.style.position = Position.Absolute;
            shardA.style.left = 10; shardA.style.top = 2; shardA.style.width = 3; shardA.style.height = 13;
            shardA.style.backgroundColor = Color.white;
            shardA.style.rotate = new Rotate(22f);
            WeaponCrack.Add(shardA);
            var shardB = new VisualElement { pickingMode = PickingMode.Ignore };
            shardB.style.position = Position.Absolute;
            shardB.style.left = 10; shardB.style.top = 9; shardB.style.width = 3; shardB.style.height = 13;
            shardB.style.backgroundColor = Color.white;
            shardB.style.rotate = new Rotate(-26f);
            WeaponCrack.Add(shardB);
            Root.Add(WeaponCrack);

            // One scheduled beat drives both the amber pulse and the red glow; cheap no-op when neither is active.
            Root.schedule.Execute(() =>
            {
                if (weaponPulseAmber || armorPulseAmber)
                {
                    float k = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Tween.Clock * 4f));
                    if (weaponPulseAmber) weaponBarEl.style.opacity = k;
                    if (armorPulseAmber) armorBarEl.style.opacity = k;
                }
                if (weaponGlow) WeaponCrack.style.opacity = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Tween.Clock * 3f));
            }).Every(33);

            if (u.IsSnakeSegment)
            {
                Frame.style.borderTopLeftRadius = Frame.style.borderTopRightRadius = Frame.style.borderBottomLeftRadius = Frame.style.borderBottomRightRadius = tile * 0.4f;
                Keyline.style.borderTopLeftRadius = Keyline.style.borderTopRightRadius = Keyline.style.borderBottomLeftRadius = Keyline.style.borderBottomRightRadius = tile * 0.4f - 4;
            }
        }

        /// <summary>The team keyline and corner tag: hero / foe / boss, a GUEST, or a neutral recruitable's TALK (GG-T132).
        /// Called again when a recruitable is talked round, so its frame turns guest-green on the spot.</summary>
        public void SetTeamLook(Unit u)
        {
            foreach (var cls in new[] { "unit-keyline--guest", "unit-keyline--hero", "unit-keyline--boss", "unit-keyline--foe", "unit-keyline--neutral" })
                Keyline.RemoveFromClassList(cls);
            Keyline.AddToClassList(u.IsNeutral ? "unit-keyline--neutral" : u.IsGuest ? "unit-keyline--guest" : u.IsHero ? "unit-keyline--hero" : (u.Def.HasTag("Boss") || u.IsMultiTile) ? "unit-keyline--boss" : "unit-keyline--foe");
            Frame.Q<Label>("guest-" + u.Id)?.RemoveFromHierarchy();
            if (!u.IsGuest && !u.IsNeutral) return;
            var tag = new Label(u.IsNeutral ? "TALK" : "GUEST") { name = "guest-" + u.Id, pickingMode = PickingMode.Ignore };
            tag.AddToClassList("shadow");
            tag.style.position = Position.Absolute; tag.style.left = 10; tag.style.top = 6;
            tag.style.fontSize = 22; tag.style.unityFontStyleAndWeight = FontStyle.Bold;
            tag.style.color = u.IsNeutral ? new Color(1f, 0.82f, 0.4f) : new Color(0.6f, 1f, 0.75f);
            Frame.Add(tag);
        }

        public void SetHp(int hp, int max)
        {
            float f = max <= 0 ? 0 : Mathf.Clamp01(hp / (float)max);
            HpFill.style.width = Length.Percent(f * 100f);
            HpFill.EnableInClassList("gauge-fill--mid", f <= 0.5f && f > 0.25f);
            HpFill.EnableInClassList("gauge-fill--low", f <= 0.25f);
        }

        private readonly List<(string icon, string letter, Color color, float frac)> statuses = new List<(string, string, Color, float)>();
        private int statusBeat;

        /// <summary>
        /// One status slot per unit (owner direction 2026-10-10): it flashes through every buff, debuff and stun on
        /// the 1.2 s beat; the selected unit's card explains each one.
        /// </summary>
        public void SetBuffs(Unit u, bool colorblind, bool stunned = false)
        {
            statuses.Clear();
            if (stunned) statuses.Add(("stunned", "!", new Color(1f, 0.85f, 0.3f), 1f)); // pushback stun isn't a buff, but it's why it isn't moving
            foreach (var b in u.Buffs)
                statuses.Add((b.Def.Id, string.IsNullOrEmpty(b.Def.Letter) ? b.Def.Name.Substring(0, 1) : b.Def.Letter,
                    App.Art.HexColor(b.Def.Color, b.Def.Kind == BuffKind.Buff ? new Color(0.4f, 0.8f, 1f) : new Color(0.8f, 0.4f, 0.9f)),
                    Mathf.Clamp01(b.Remaining / (float)Mathf.Max(1, b.Def.Duration))));
            ShowStatus();
        }

        public void NextStatus()
        {
            if (statuses.Count < 2) return;
            statusBeat++;
            ShowStatus();
        }

        private void ShowStatus()
        {
            Buffs.Clear();
            if (statuses.Count == 0) return;
            var (icon, letter, color, frac) = statuses[statusBeat % statuses.Count];
            var pip = Ui.StatusPip(icon, letter, color, 50);
            pip.name = "statuspip-" + Id;
            pip.Add(new RingTimer(frac) { pickingMode = PickingMode.Ignore });
            Buffs.Add(pip);
            if (statuses.Count > 1)
            {
                var more = new Label((statuses.Count).ToString()) { pickingMode = PickingMode.Ignore };
                more.AddToClassList("shadow");
                more.style.position = Position.Absolute; more.style.right = -6; more.style.bottom = -10;
                more.style.fontSize = 20; more.style.unityFontStyleAndWeight = FontStyle.Bold;
                pip.Add(more);
            }
        }

        /// <summary>Caster side: show (or hide, with null) the countdown ring; <paramref name="left"/> = fraction of cast time remaining.</summary>
        public void SetCastRing(Color? color, float left)
        {
            Cast.style.display = color.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (color.HasValue) Cast.Set(color.Value, left);
        }

        /// <summary>Draw the plate on the facing edge; it fades as it dents and vanishes when broken.</summary>
        public void SetPlate(Coord facing, int hp, int max)
        {
            Plate.style.display = hp > 0 && max > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (hp <= 0 || max <= 0) return;
            // On the tile's outer rim (half outside the frame), so it never covers the HP bar or the art.
            float thick = 18f, inset = 14f, w = tileSize * W, h = tileSize * H, out_ = thick * 0.55f;
            bool horizontal = facing.Y != 0;
            Plate.style.flexDirection = horizontal ? FlexDirection.Row : FlexDirection.Column;
            Plate.style.left = facing.X > 0 ? w - thick + out_ : facing.X < 0 ? -out_ : inset;
            Plate.style.top = facing.Y > 0 ? h - thick + out_ : facing.Y < 0 ? -out_ : inset;
            Plate.style.width = horizontal ? w - inset * 2 : thick;
            Plate.style.height = horizontal ? thick : h - inset * 2;
            Plate.style.opacity = 0.45f + 0.55f * Mathf.Clamp01(hp / (float)max);
        }

        /// <summary>
        /// Hero gear durability telegraph (GG-T39): weapon/armor wear bars. Either fraction ≤10% pulses amber
        /// (steady color when <paramref name="animate"/> is false, for Reduce Motion); a weapon at its last
        /// point (the ×1.5 "last strike" durability of 1, §24.5) glows red with a cracked-weapon badge instead.
        /// </summary>
        public void SetGear(bool hasWeapon, float weaponFrac, bool weaponLastStrike, bool hasArmor, float armorFrac, bool animate)
        {
            Gear.style.display = (hasWeapon || hasArmor) ? DisplayStyle.Flex : DisplayStyle.None;

            weaponBarEl.style.display = hasWeapon ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasWeapon)
            {
                // High-durability gear (150+) leaves a 1-point sliver under 1% wide — floor the rendered
                // width so "nearly broken" still reads as a visible bar, never a blank one.
                WeaponFill.style.width = Length.Percent(Mathf.Max(Mathf.Clamp01(weaponFrac), 0.08f) * 100f);
                WeaponFill.EnableInClassList("gauge-fill--mid", !weaponLastStrike && weaponFrac <= 0.10f);
                WeaponFill.EnableInClassList("gauge-fill--low", weaponLastStrike);
            }
            weaponPulseAmber = animate && hasWeapon && !weaponLastStrike && weaponFrac <= 0.10f;
            if (!weaponPulseAmber) weaponBarEl.style.opacity = 1f;
            weaponGlow = animate && weaponLastStrike;
            WeaponCrack.style.display = weaponLastStrike ? DisplayStyle.Flex : DisplayStyle.None;
            if (!weaponGlow) WeaponCrack.style.opacity = 1f;

            armorBarEl.style.display = hasArmor ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasArmor)
            {
                ArmorFill.style.width = Length.Percent(Mathf.Max(Mathf.Clamp01(armorFrac), 0.08f) * 100f);
                ArmorFill.EnableInClassList("gauge-fill--mid", armorFrac <= 0.10f);
            }
            armorPulseAmber = animate && hasArmor && armorFrac <= 0.10f;
            if (!armorPulseAmber) armorBarEl.style.opacity = 1f;
        }

        /// <summary>Caster side: the charging bar, <paramref name="filled"/> = fraction of the cast already charged (null hides it).</summary>
        public void SetCastBar(Color? color, float filled)
        {
            CastBar.style.display = color.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!color.HasValue) return;
            castBarFill.style.width = Length.Percent(Mathf.Clamp01(filled) * 100f);
            castBarFill.style.backgroundColor = Color.Lerp(color.Value, Color.white, 0.15f + 0.25f * Mathf.Clamp01(filled));
        }

        /// <summary>Target side: the incoming spell's icon (or initial) in its colour, top-left of the tile.</summary>
        public void SetCastChip(Color? color, Texture2D icon, string name)
        {
            CastChip.style.display = color.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!color.HasValue) return;
            var c = color.Value;
            CastChip.style.borderTopColor = CastChip.style.borderBottomColor = CastChip.style.borderLeftColor = CastChip.style.borderRightColor = c;
            CastChip.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground((Texture2D)null);
            var letter = (Label)CastChip[0];
            letter.text = icon == null && !string.IsNullOrEmpty(name) ? name.Substring(0, 1) : "";
            letter.style.color = c;
        }

        public void SetGlow(Color c, float alpha)
        {
            Glow.style.unityBackgroundImageTintColor = c;
            Glow.style.opacity = alpha;
        }
    }

    /// <summary>The 6×8 board: tiles, trap marks, highlight overlay, units, floating fx.</summary>
    public sealed class BoardView
    {
        public const float Tile = 180f;
        public readonly VisualElement Root;
        public readonly VisualElement TileLayer;
        public readonly VisualElement MarkLayer;
        public readonly VisualElement UnitLayer;
        public readonly VisualElement FxLayer;
        public readonly Dictionary<int, UnitView> Units = new Dictionary<int, UnitView>();
        private readonly VisualElement[,] tiles = new VisualElement[BattleConfig.Cols, BattleConfig.Rows];
        private readonly VisualElement[,] marks = new VisualElement[BattleConfig.Cols, BattleConfig.Rows];
        private readonly List<VisualElement> trapMarks = new List<VisualElement>();
        public readonly LineLayer Lines;
        public readonly TrailLayer Trail;

        private readonly string theme;

        public BoardView(VisualElement parent, string theme = null)
        {
            this.theme = theme;
            Root = new VisualElement { name = "board" };
            Root.style.position = Position.Absolute;
            Root.style.width = Tile * BattleConfig.Cols;
            Root.style.height = Tile * BattleConfig.Rows;
            parent.Add(Root);

            // Order matters: highlights and lines draw OVER the units so targets and pincer previews read clearly.
            TileLayer = Layer("tiles");
            // Under the units: the streak trails behind the moving token (GG-T219).
            Trail = new TrailLayer { name = "trail" };
            Trail.style.position = Position.Absolute;
            Trail.style.left = Trail.style.top = Trail.style.right = Trail.style.bottom = 0;
            Root.Add(Trail);
            UnitLayer = Layer("units");
            MarkLayer = Layer("marks");
            Lines = new LineLayer();
            Lines.style.position = Position.Absolute;
            Lines.style.left = Lines.style.top = Lines.style.right = Lines.style.bottom = 0;
            Lines.pickingMode = PickingMode.Ignore;
            Root.Add(Lines);
            FxLayer = Layer("fx");

            for (int y = 0; y < BattleConfig.Rows; y++)
                for (int x = 0; x < BattleConfig.Cols; x++)
                {
                    var t = new VisualElement { pickingMode = PickingMode.Ignore };
                    t.style.position = Position.Absolute;
                    t.style.left = x * Tile; t.style.top = y * Tile;
                    t.style.width = Tile; t.style.height = Tile;
                    // GridGame2026's tile art recoloured per biome (tools/make_tiles.py), tinted light/dark for the checkerboard.
                    bool light = (x + y) % 2 == 0;
                    t.style.backgroundImage = new StyleBackground(App.Art.BoardTile(theme));
                    // Owner direction 2026-10-11: lighter and lower-contrast so the stage background reads clearly through the board.
                    t.style.unityBackgroundImageTintColor = light ? new Color(1.0f, 1.0f, 1.0f, 0.34f) : new Color(0.78f, 0.82f, 0.9f, 0.24f);
                    TileLayer.Add(t);
                    tiles[x, y] = t;

                    var m = new VisualElement { pickingMode = PickingMode.Ignore };
                    m.style.position = Position.Absolute;
                    m.style.left = x * Tile + 4; m.style.top = y * Tile + 4;
                    m.style.width = Tile - 8; m.style.height = Tile - 8;
                    m.style.borderTopLeftRadius = m.style.borderTopRightRadius = m.style.borderBottomLeftRadius = m.style.borderBottomRightRadius = 18;
                    m.style.opacity = 0;
                    MarkLayer.Add(m);
                    marks[x, y] = m;
                }
        }

        private VisualElement Layer(string name)
        {
            var l = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            l.style.position = Position.Absolute;
            l.style.left = l.style.top = l.style.right = l.style.bottom = 0;
            Root.Add(l);
            return l;
        }

        public static Vector2 PosOf(Coord c) => new Vector2(c.X * Tile, c.Y * Tile);
        public static Vector2 CenterOf(Coord c, int w = 1, int h = 1) => new Vector2((c.X + w / 2f) * Tile, (c.Y + h / 2f) * Tile);

        public Coord TileAt(Vector2 local)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / Tile), 0, BattleConfig.Cols - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(local.y / Tile), 0, BattleConfig.Rows - 1);
            return new Coord(x, y);
        }

        // ── Physical blocks (§1.1.1): raised stone that nothing can enter ──
        private readonly HashSet<Coord> drawnBlocks = new HashSet<Coord>();

        public void SyncBlocks(IEnumerable<Coord> blocks)
        {
            foreach (var c in blocks)
            {
                if (!drawnBlocks.Add(c)) continue;
                var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
                shadow.style.position = Position.Absolute;
                shadow.style.left = c.X * Tile + 14; shadow.style.top = c.Y * Tile + 22;
                shadow.style.width = Tile - 22; shadow.style.height = Tile - 22;
                shadow.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
                SetRadius(shadow, 26);
                TileLayer.Add(shadow);
                var rock = new VisualElement { name = $"block-{c.X}-{c.Y}", pickingMode = PickingMode.Ignore };
                rock.style.position = Position.Absolute;
                rock.style.left = c.X * Tile + 8; rock.style.top = c.Y * Tile + 8;
                rock.style.width = Tile - 16; rock.style.height = Tile - 22;
                rock.style.backgroundImage = new StyleBackground(App.Art.BoardTile(theme));
                rock.style.unityBackgroundImageTintColor = new Color(0.85f, 0.85f, 0.9f, 1f);
                rock.style.backgroundColor = new Color(0.32f, 0.33f, 0.38f, 1f);
                SetRadius(rock, 26);
                rock.style.borderTopWidth = 6; rock.style.borderLeftWidth = rock.style.borderRightWidth = 4; rock.style.borderBottomWidth = 12;
                rock.style.borderTopColor = new Color(0.78f, 0.8f, 0.86f, 0.9f);
                rock.style.borderLeftColor = rock.style.borderRightColor = new Color(0.45f, 0.47f, 0.53f, 1f);
                rock.style.borderBottomColor = new Color(0.16f, 0.17f, 0.2f, 1f);
                TileLayer.Add(rock);
            }
        }

        private static void SetRadius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;

        // ── Power Amplifier marker (§1.2.6): a pulsing gold diamond on its tile ──
        private VisualElement amp;
        public VisualElement Amp => amp;
        /// <summary>The tile the amplifier token is drawn on (null while hidden) — for BoardAudit (GG-T129).</summary>
        public Coord? AmpTile { get; private set; }

        public void SyncAmplifier(Coord? at)
        {
            if (amp == null)
            {
                amp = new VisualElement { name = "amplifier", pickingMode = PickingMode.Ignore };
                amp.style.position = Position.Absolute;
                amp.style.width = amp.style.height = Tile * 0.5f;
                amp.style.rotate = new Rotate(45f);
                amp.style.borderTopWidth = amp.style.borderBottomWidth = amp.style.borderLeftWidth = amp.style.borderRightWidth = 6;
                amp.style.borderTopColor = amp.style.borderBottomColor = amp.style.borderLeftColor = amp.style.borderRightColor = new Color(1f, 0.95f, 0.7f);
                amp.style.backgroundColor = new Color(1f, 0.72f, 0.15f, 0.85f);
                amp.style.borderTopLeftRadius = amp.style.borderTopRightRadius = amp.style.borderBottomLeftRadius = amp.style.borderBottomRightRadius = 10;
                var glow = new VisualElement { pickingMode = PickingMode.Ignore };
                glow.style.position = Position.Absolute;
                glow.style.left = glow.style.top = glow.style.right = glow.style.bottom = -30;
                glow.style.backgroundImage = new StyleBackground(App.Art.Glow);
                glow.style.unityBackgroundImageTintColor = new Color(1f, 0.8f, 0.3f, 0.8f);
                amp.Add(glow);
                MarkLayer.Add(amp);
                amp.schedule.Execute(() =>
                {
                    float k = 1f + 0.08f * Mathf.Sin(Tween.Clock * 5f);
                    amp.style.scale = new Scale(new Vector2(k, k));
                }).Every(33);
            }
            AmpTile = at;
            amp.style.display = at.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (at.HasValue) PlaceAmp(CenterOf(at.Value));
        }

        public void PlaceAmp(Vector2 centre)
        {
            amp.style.left = centre.x - Tile * 0.25f;
            amp.style.top = centre.y - Tile * 0.25f;
        }

        /// <summary>Board-space rect around every tile of a group (a pincer's attackers + opponents), slightly outside the frames.</summary>
        public static Rect GroupRect(IEnumerable<(Coord pos, int w, int h)> members)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var (pos, w, h) in members)
            {
                x0 = Mathf.Min(x0, pos.X * Tile); y0 = Mathf.Min(y0, pos.Y * Tile);
                x1 = Mathf.Max(x1, (pos.X + w) * Tile); y1 = Mathf.Max(y1, (pos.Y + h) * Tile);
            }
            return Rect.MinMaxRect(x0 - 4f, y0 - 4f, x1 + 4f, y1 + 4f);
        }

        /// <summary>Short link bridging the gap between a supporter's frame and its partner's (edge to edge, never over the art).</summary>
        public static (Vector2 a, Vector2 b) EdgeLink(Coord from, Coord to)
        {
            var d = new Vector2(to.X - from.X, to.Y - from.Y);
            if (d.sqrMagnitude > 0f) d.Normalize();
            var ca = CenterOf(from); var cb = CenterOf(to);
            return (ca + d * (Tile * 0.5f - 16f), cb - d * (Tile * 0.5f - 16f));
        }

        public UnitView Add(Unit u)
        {
            var v = new UnitView(u, Tile);
            Units[u.Id] = v;
            UnitLayer.Add(v.Root);
            Place(v, u.Pos);
            return v;
        }

        public void Place(UnitView v, Coord c)
        {
            v.Tile = c;
            var p = PosOf(c);
            v.Root.style.left = p.x;
            v.Root.style.top = p.y;
        }

        public void ClearMarks()
        {
            foreach (var m in marks) m.style.opacity = 0;
        }

        public void Mark(Coord c, Color color, float alpha = 0.55f)
        {
            if (!BattleState.InBounds(c)) return;
            var m = marks[c.X, c.Y];
            m.style.backgroundColor = color;
            m.style.opacity = alpha;
        }

        // ── Pickups (§13.3.1): a bobbing, glowing token on its tile; a ring shows how long until it fades ──
        public readonly Dictionary<int, VisualElement> PickupViews = new Dictionary<int, VisualElement>();

        public VisualElement AddPickup(Pickup p, PickupDef def)
        {
            var root = new VisualElement { name = "pickup-" + p.Id, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.width = root.style.height = Tile * 0.56f;
            var c = App.Art.HexColor(def?.Color, new Color(1f, 0.85f, 0.4f));
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.style.position = Position.Absolute;
            glow.style.left = glow.style.top = glow.style.right = glow.style.bottom = -36;
            glow.style.backgroundImage = new StyleBackground(App.Art.Glow);
            glow.style.unityBackgroundImageTintColor = new Color(c.r, c.g, c.b, 0.85f);
            root.Add(glow);
            var disc = new VisualElement { name = "disc", pickingMode = PickingMode.Ignore };
            disc.style.position = Position.Absolute;
            disc.style.left = disc.style.top = disc.style.right = disc.style.bottom = 0;
            SetRadius(disc, Tile * 0.28f);
            disc.style.backgroundColor = new Color(0.05f, 0.06f, 0.14f, 0.92f);
            disc.style.borderTopWidth = disc.style.borderBottomWidth = disc.style.borderLeftWidth = disc.style.borderRightWidth = 5;
            disc.style.borderTopColor = disc.style.borderBottomColor = disc.style.borderLeftColor = disc.style.borderRightColor = c;
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.left = icon.style.top = icon.style.right = icon.style.bottom = 8;
            var tex = App.Art.UiIcon(def?.Icon);
            if (tex != null) { icon.style.backgroundImage = new StyleBackground(tex); icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain); }
            disc.Add(icon);
            root.Add(disc);
            // The fade ring lives inside the bobbing disc so it floats with the token (GG-T229); the disc fills the root,
            // so the ring's -4 inset is unchanged and it still draws over the icon.
            var ring = new RingTimer(Mathf.Clamp01(p.ActionsLeft / (float)BattleConfig.PickupLifetimeActions)) { name = "life", pickingMode = PickingMode.Ignore };
            disc.Add(ring);
            var centre = CenterOf(p.At);
            root.style.left = centre.x - Tile * 0.28f;
            root.style.top = centre.y - Tile * 0.28f;
            float phase = (p.Id * 1.7f) % 6.28f;
            root.schedule.Execute(() =>
            {
                float t = Tween.Clock * 3f + phase;
                disc.style.translate = new Translate(0, -8f * Mathf.Abs(Mathf.Sin(t)));
                glow.style.opacity = 0.65f + 0.35f * Mathf.Sin(t * 1.3f);
            }).Every(33);
            root.userData = p.At; // pickups never move: the tile its token is drawn on (BoardAudit, GG-T129)
            MarkLayer.Add(root);
            PickupViews[p.Id] = root;
            return root;
        }

        /// <summary>Snap pickup tokens to the state: add missing ones, drop collected/faded ones, refresh fade rings.</summary>
        public void SyncPickups(BattleState s)
        {
            var live = new HashSet<int>();
            foreach (var p in s.Pickups)
            {
                live.Add(p.Id);
                s.Content.Pickups.TryGetValue(p.PickupId, out var def);
                if (!PickupViews.TryGetValue(p.Id, out var v)) v = AddPickup(p, def);
                var old = v.Q("life");
                old?.RemoveFromHierarchy();
                (v.Q("disc") ?? v).Add(new RingTimer(Mathf.Clamp01(p.ActionsLeft / (float)BattleConfig.PickupLifetimeActions)) { name = "life", pickingMode = PickingMode.Ignore });
                v.style.opacity = p.ActionsLeft <= 1 ? 0.6f : 1f;
            }
            foreach (var id in new List<int>(PickupViews.Keys))
                if (!live.Contains(id)) { PickupViews[id].RemoveFromHierarchy(); PickupViews.Remove(id); }
        }

        public void SyncTraps(BattleState s)
        {
            foreach (var t in trapMarks) t.RemoveFromHierarchy();
            trapMarks.Clear();
            foreach (var trap in s.Traps)
            {
                var e = new Label("☠") { pickingMode = PickingMode.Ignore };
                e.style.position = Position.Absolute;
                e.style.left = trap.At.X * Tile; e.style.top = trap.At.Y * Tile;
                e.style.width = Tile; e.style.height = Tile;
                e.style.unityTextAlign = TextAnchor.MiddleCenter;
                e.style.fontSize = 90;
                e.style.color = new Color(0.75f, 0.35f, 1f, 0.85f);
                e.style.backgroundColor = new Color(0.45f, 0.1f, 0.6f, 0.25f);
                MarkLayer.Add(e);
                trapMarks.Add(e);
            }
        }

        /// <summary>Floating combat text (damage numbers etc.), rising and fading.</summary>
        public Label Float(string text, Vector2 at, Color color, float size = 64)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("shadow");
            l.style.position = Position.Absolute;
            l.style.left = at.x - 150; l.style.top = at.y - 60;
            l.style.width = 300;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.fontSize = size;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = color;
            FxLayer.Add(l);
            return l;
        }
    }

    /// <summary>Radial countdown ring drawn around a buff pip: the arc shrinks clockwise as the buff runs out.</summary>
    public sealed class RingTimer : VisualElement
    {
        private readonly float frac;
        public RingTimer(float frac)
        {
            this.frac = frac;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = -4;
            generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                var r = contentRect;
                var c = r.center;
                float radius = Mathf.Min(r.width, r.height) / 2f - 2f;
                p.lineWidth = 4f;
                p.strokeColor = new Color(1f, 0.92f, 0.35f, 0.95f);
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.Arc(c, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * this.frac));
                p.Stroke();
            };
        }
    }

    /// <summary>
    /// Radial countdown traced around a unit's rounded frame, clockwise from top-centre: the lit part is the cast
    /// time still to run, so the line shrinks toward the moment the spell lands (bible §2.6, owner request).
    /// </summary>
    public sealed class CastRing : VisualElement
    {
        private Color color = Color.white;
        private float left = 1f;
        public float Left => left;
        public Color Color => color;

        public CastRing()
        {
            style.position = Position.Absolute;
            style.left = style.top = 2; style.right = style.bottom = 2;
            generateVisualContent += Draw;
        }

        public void Set(Color c, float remaining)
        {
            color = c; left = Mathf.Clamp01(remaining);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0 || left <= 0f) return;
            var pts = Perimeter(r, 22f, left);
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            p.strokeColor = new Color(0f, 0f, 0f, 0.55f); p.lineWidth = 13f;
            Stroke(p, pts);
            p.strokeColor = color; p.lineWidth = 8f;
            Stroke(p, pts);
        }

        private static void Stroke(Painter2D p, List<Vector2> pts)
        {
            if (pts.Count < 2) return;
            p.BeginPath(); p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
            p.Stroke();
        }

        /// <summary>Points along a rounded rect's border from top-centre clockwise, covering fraction <paramref name="f"/> of it.</summary>
        public static List<Vector2> Perimeter(Rect r, float rad, float f)
        {
            float inset = 5f;
            float x0 = r.xMin + inset, y0 = r.yMin + inset, x1 = r.xMax - inset, y1 = r.yMax - inset;
            rad = Mathf.Min(rad, (x1 - x0) / 2f, (y1 - y0) / 2f);
            var full = new List<Vector2>();
            void Line(Vector2 a, Vector2 b, int n) { for (int i = 0; i < n; i++) full.Add(Vector2.Lerp(a, b, i / (float)n)); }
            void Arc(Vector2 c, float a0, int n) { for (int i = 0; i < n; i++) { float a = (a0 + 90f * i / n) * Mathf.Deg2Rad; full.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad); } }
            float cx = (x0 + x1) / 2f;
            Line(new Vector2(cx, y0), new Vector2(x1 - rad, y0), 12);
            Arc(new Vector2(x1 - rad, y0 + rad), -90f, 8);
            Line(new Vector2(x1, y0 + rad), new Vector2(x1, y1 - rad), 24);
            Arc(new Vector2(x1 - rad, y1 - rad), 0f, 8);
            Line(new Vector2(x1 - rad, y1), new Vector2(x0 + rad, y1), 24);
            Arc(new Vector2(x0 + rad, y1 - rad), 90f, 8);
            Line(new Vector2(x0, y1 - rad), new Vector2(x0, y0 + rad), 24);
            Arc(new Vector2(x0 + rad, y0 + rad), 180f, 8);
            Line(new Vector2(x0 + rad, y0), new Vector2(cx, y0), 12);
            full.Add(new Vector2(cx, y0));
            int keep = Mathf.Clamp(Mathf.CeilToInt(full.Count * f), 2, full.Count);
            return full.GetRange(0, keep);
        }
    }

    /// <summary>Vector overlay: pincer lines, projectile trails, line-charge telegraphs.</summary>
    public sealed class LineLayer : VisualElement
    {
        public struct Seg { public Vector2 A, B; public Color Color; public float Width; }
        public readonly List<Seg> Segs = new List<Seg>();
        public readonly List<(Vector2 c, float r, Color color)> Dots = new List<(Vector2, float, Color)>();
        public readonly List<(Vector2 c, float r, Color color, float w)> Rings = new List<(Vector2, float, Color, float)>();
        /// <summary>Rounded outlines drawn around a group of tiles; <c>f</c> is how much of the border has drawn itself (0..1).</summary>
        public readonly List<(Rect r, Color color, float w, float f)> Outlines = new List<(Rect, Color, float, float)>();

        public LineLayer() { generateVisualContent += Draw; }

        public void ClearLines() { Segs.Clear(); Dots.Clear(); Rings.Clear(); Outlines.Clear(); MarkDirtyRepaint(); }
        public void Outline(Rect r, Color color, float w, float f) { Outlines.Add((r, color, w, f)); MarkDirtyRepaint(); }
        public void Ring(Vector2 c, float r, Color color, float w) { Rings.Add((c, r, color, w)); MarkDirtyRepaint(); }
        public void Add(Vector2 a, Vector2 b, Color c, float w) { Segs.Add(new Seg { A = a, B = b, Color = c, Width = w }); MarkDirtyRepaint(); }
        public void Dot(Vector2 c, float r, Color color) { Dots.Add((c, r, color)); MarkDirtyRepaint(); }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;
            foreach (var s in Segs)
            {
                p.strokeColor = s.Color;
                p.lineWidth = s.Width;
                p.BeginPath();
                p.MoveTo(s.A);
                p.LineTo(s.B);
                p.Stroke();
            }
            foreach (var (rect, color, w, f) in Outlines)
            {
                var pts = CastRing.Perimeter(rect, 30f, f);
                if (pts.Count < 2) continue;
                p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
                p.strokeColor = new Color(0f, 0f, 0f, 0.5f * color.a); p.lineWidth = w + 6f;
                p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]); p.Stroke();
                p.strokeColor = color; p.lineWidth = w;
                p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]); p.Stroke();
            }
            foreach (var (c, r, color, w) in Rings)
            {
                p.strokeColor = color;
                p.lineWidth = w;
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }
            foreach (var (c, r, color) in Dots)
            {
                p.fillColor = color;
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }
        }
    }

    /// <summary>
    /// GG-T219: a trail renderer for moving units (Terra Battle / Friends &amp; Dragons): a fast streak as wide as the
    /// token, drawn under it, following its real free-hand position. It is never anchored: points age out in
    /// <see cref="PointLife"/> seconds, so a faster or longer move draws a longer streak, and it catches up as soon
    /// as the unit stops. The tail keeps nearly full width and fades out by age rather than tapering to a point.
    /// </summary>
    public sealed class TrailLayer : VisualElement
    {
        private struct Pt { public Vector2 P; public float T; }
        private sealed class Trail
        {
            public VisualElement Source;
            public readonly List<Pt> Points = new List<Pt>();
            public Color Color;
            public float Width;
            public bool Following = true;
        }

        public const float PointLife = 0.15f;
        private const float MinVertexDistance = 4f;
        private readonly List<Trail> trails = new List<Trail>();
        private IVisualElementScheduledItem ticker;

        public TrailLayer() { generateVisualContent += Draw; pickingMode = PickingMode.Ignore; }

        /// <summary>Starts (or keeps) a streak behind <paramref name="source"/>, a unit token on the board.</summary>
        public void Follow(VisualElement source, Color color, float width = BoardView.Tile - 12f)
        {
            if (trails.Exists(t => t.Source == source && t.Following)) return;
            var tr = new Trail { Source = source, Color = color, Width = width };
            tr.Points.Add(new Pt { P = CenterOf(source), T = Time.unscaledTime });
            trails.Add(tr);
            if (ticker == null) ticker = schedule.Execute(Tick).Every(16);
            else ticker.Resume();
        }

        /// <summary>Stops sampling <paramref name="source"/>; what is left of its streak ages out on its own.</summary>
        public void Release(VisualElement source)
        {
            foreach (var t in trails)
                if (t.Source == source && t.Following) { Sample(t, Time.unscaledTime); t.Following = false; }
        }

        public void Clear() { trails.Clear(); MarkDirtyRepaint(); }

        private static Vector2 CenterOf(VisualElement e) =>
            new Vector2(e.resolvedStyle.left + BoardView.Tile / 2f, e.resolvedStyle.top + BoardView.Tile / 2f);

        private static void Sample(Trail t, float now)
        {
            if (t.Source.panel == null) { t.Following = false; return; }
            var p = CenterOf(t.Source);
            var last = t.Points.Count > 0 ? t.Points[t.Points.Count - 1].P : p + Vector2.one * MinVertexDistance * 2f;
            if ((p - last).sqrMagnitude >= MinVertexDistance * MinVertexDistance) t.Points.Add(new Pt { P = p, T = now });
        }

        private void Tick()
        {
            float now = Time.unscaledTime;
            foreach (var t in trails)
            {
                if (t.Following) Sample(t, now);
                t.Points.RemoveAll(q => now - q.T > PointLife);
            }
            trails.RemoveAll(t => !t.Following && t.Points.Count < 2);
            if (trails.Count == 0) ticker.Pause();
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float now = Time.unscaledTime;
            var pts = new List<Vector2>();
            var fade = new List<float>();
            foreach (var t in trails)
            {
                pts.Clear(); fade.Clear();
                // Newest first: the token's own position leads, so the streak meets it with no gap. Each point fades
                // with its age, so a fast move stretches the streak and it catches up within PointLife of stopping.
                if (t.Following && t.Source.panel != null) { pts.Add(CenterOf(t.Source)); fade.Add(1f); }
                for (int i = t.Points.Count - 1; i >= 0; i--)
                    if (pts.Count == 0 || (pts[pts.Count - 1] - t.Points[i].P).sqrMagnitude > 1f)
                    {
                        pts.Add(t.Points[i].P);
                        fade.Add(Mathf.Clamp01(1f - (now - t.Points[i].T) / PointLife));
                    }
                if (pts.Count < 2) continue;
                Ribbon(ctx, pts, fade, t.Width, t.Color, 0.6f);
                Ribbon(ctx, pts, fade, t.Width * 0.45f, t.Color, 0.75f);
            }
        }

        /// <summary>One triangle strip along <paramref name="pts"/> (newest first); <paramref name="fade"/> scales alpha and narrows the tail.</summary>
        private static void Ribbon(MeshGenerationContext ctx, List<Vector2> pts, List<float> fade, float width, Color color, float alpha)
        {
            int n = pts.Count;
            var verts = new Vertex[n * 2];
            var idx = new ushort[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                // UI Toolkit culls back faces: this tangent keeps the strip's winding front-facing for newest-first points.
                var tan = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                var normal = new Vector2(-tan.y, tan.x);
                // The tail stays nearly full width and fades out as a whole, never tapering to a point.
                float half = width * Mathf.Lerp(0.88f, 1f, fade[i]) * 0.5f;
                var c = new Color(color.r, color.g, color.b, color.a * alpha * fade[i] * fade[i]);
                var l = pts[i] - normal * half;
                var r = pts[i] + normal * half;
                verts[i * 2] = new Vertex { position = new Vector3(l.x, l.y, Vertex.nearZ), tint = c };
                verts[i * 2 + 1] = new Vertex { position = new Vector3(r.x, r.y, Vertex.nearZ), tint = c };
            }
            for (int i = 0; i < n - 1; i++)
            {
                ushort l0 = (ushort)(i * 2), r0 = (ushort)(i * 2 + 1), l1 = (ushort)(i * 2 + 2), r1 = (ushort)(i * 2 + 3);
                int k = i * 6;
                idx[k] = l0; idx[k + 1] = l1; idx[k + 2] = r0;
                idx[k + 3] = r0; idx[k + 4] = l1; idx[k + 5] = r1;
            }
            var mesh = ctx.Allocate(verts.Length, idx.Length);
            mesh.SetAllVertices(verts);
            mesh.SetAllIndices(idx);
        }
    }

    /// <summary>
    /// GG-T97: running tally of the board's drawn tokens against the rule "two actors never share a tile once a slide is
    /// over". BattleScreen feeds it after every beat (tokens unique), as playback settles (tokens match the state, before
    /// the safety-net snap hides a miss) and after that snap (must always be clean). AutomationServer serves it at
    /// /state → board_audit, and tools/harness.py boardaudit fails a run on any nonzero count.
    /// </summary>
    public static class BoardAudit
    {
        public static int Actions, Beats, BeatOverlaps, SettleDrift, SyncedProblems, TileConflicts;
        public static readonly List<string> Recent = new List<string>();

        public static void Beat(List<string> problems, BattleEvent after)
        {
            Beats++;
            if (problems.Count == 0) return;
            BeatOverlaps++;
            Note("beat after " + after, problems);
        }

        public static void Settled(List<string> problems)
        {
            Actions++;
            if (problems.Count == 0) return;
            SettleDrift++;
            Note("playback settle", problems);
        }

        public static void Synced(List<string> problems)
        {
            if (problems.Count == 0) return;
            SyncedProblems++;
            Note("after sync", problems);
        }

        public static void Conflict(string what)
        {
            TileConflicts++;
            Note("engine", new List<string> { what });
        }

        private static void Note(string where, List<string> problems)
        {
            var line = "[BoardAudit] " + where + ": " + string.Join("; ", problems);
            Debug.LogError(line);
            Recent.Add(line.Length > 400 ? line.Substring(0, 400) : line);
            while (Recent.Count > 20) Recent.RemoveAt(0);
        }

        public static void Reset()
        {
            Actions = Beats = BeatOverlaps = SettleDrift = SyncedProblems = TileConflicts = 0;
            Recent.Clear();
        }

        public static object Snapshot() => new { actions = Actions, beats = Beats, beat_overlaps = BeatOverlaps, settle_drift = SettleDrift, synced = SyncedProblems, tile_conflicts = TileConflicts, recent = Recent.ToArray() };
    }
}
