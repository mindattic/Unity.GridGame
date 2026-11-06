using System;
using System.Collections.Generic;
using System.Linq;
using GridGame.Core.Content;
using GridGame.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace GridGame.App
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Talents: a hero's skill trees as node graphs (§25.6.3, GG-T53)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Talents screen: the hero's home tree (and any off-tree it already has a node in) drawn as a node graph — one
    /// row per points-in-tree tier, prerequisite links drawn between cards — with Innate/Purchased/Available/Locked states,
    /// the skill-point counter, a detail panel for the tapped node (its tooltip) and confirm-to-spend.
    /// </summary>
    public sealed class SkillTreeScreen : HeroPage
    {
        private int tab;
        private string selected;
        private string popNode;
        public SkillTreeScreen(string heroId) : base(heroId) { }
        public override string Id => "talents";
        protected override string Title => "Talents";

        private static readonly Color PurchasedColor = Palette.Gold;
        private static readonly Color InnateColor = Palette.Good;
        private static readonly Color AvailableColor = new Color(0.55f, 0.75f, 1f);
        private static readonly Color LockedColor = new Color(0.45f, 0.48f, 0.62f, 0.8f);

        public static Color StateColor(TalentState s) => s == TalentState.Purchased ? PurchasedColor
            : s == TalentState.Innate ? InnateColor : s == TalentState.Available ? AvailableColor : LockedColor;

        public static string StateLabel(TalentState s) => s == TalentState.Purchased ? "Learned"
            : s == TalentState.Innate ? "Known" : s == TalentState.Available ? "Available" : "Locked";

        protected override void BuildBody(VisualElement body)
        {
            HeroSwitcher(body);
            var h = Hero;
            var trees = Talents.Trees(C, h);

            // Point counter + respec.
            var head = Ui.Window(null, body);
            var headRow = Ui.El("row", head);
            headRow.style.alignItems = Align.Center;
            var counter = Ui.El("col grow", headRow);
            var pts = Ui.Text($"{h.SkillPoints} skill point{(h.SkillPoints == 1 ? "" : "s")}", "h2 " + (h.SkillPoints > 0 ? "gold" : "dim"), counter);
            pts.name = "talent-points";
            int spent = Talents.PointsSpent(C, h);
            Ui.Text($"Spent {spent} · earn more on level-up", "tiny dim", counter);
            int cost = Talents.RespecCost(h);
            string closed = Talents.RespecClosed(S);
            var respec = Ui.Button($"Respec · {Ui.Gold(cost)}", () => Confirm.Show(
                $"Pay {Ui.Gold(cost)} at the Inn to unlearn {C.Class(h.ClassId).Name}'s talents and refund {spent} skill point{(spent == 1 ? "" : "s")}?",
                "Pay", () => Done(Talents.Respec(C, S, h))), "btn--small", headRow);
            respec.name = "talent-respec";
            Ui.SetEnabled(respec, closed == null && spent > 0 && S.Gold >= cost);
            if (closed != null) Ui.Text(closed, "tiny dim", head).style.whiteSpace = WhiteSpace.Normal;

            if (trees.Count == 0)
            {
                Ui.Text("This hero has no skill tree.", "small dim", Ui.Window(null, body));
                return;
            }
            tab = Mathf.Clamp(tab, 0, trees.Count - 1);
            if (trees.Count > 1)
                Ui.Tabs(trees.Select(t => Talents.IsHome(C, h, t) ? t.Name + " (home)" : Talents.Opened(C, h, t) ? t.Name : t.Name + " (new)").ToArray(),
                    tab, i => { tab = i; selected = null; Refresh(); }, body);
            var tree = trees[tab];

            // §25.6.2 (GG-T51): an off-tree says what it costs; an unopened one also says whether a slot is free.
            string offNote = "";
            if (!Talents.IsHome(C, h, tree))
            {
                // GG-T135: the premium grows with distance on the archetype ring.
                int steps = Talents.Distance(C, h, tree);
                string away = steps == 1 ? "neighbour" : steps == TreeAdjacency.Unreachable ? "off the board" : $"{steps} steps away";
                offNote = $" · off-tree ({away}): +{Talents.Premium(C, h, tree)} pt a node, deeper tiers need more";
                if (!Talents.Opened(C, h, tree)) offNote += " · " + (Talents.WhyClosed(C, h, tree) ?? "buying a node opens it");
            }
            var w = Ui.Window($"{tree.Name} — {Talents.PointsIn(h, tree)} pts in tree{offNote}", body);
            w.style.flexGrow = 1;
            if (trees.Count > 1) w.style.marginTop = Space.S0;
            var list = Ui.List(w).contentContainer;
            if (!string.IsNullOrEmpty(tree.Description))
            {
                var d = GlossaryText.Create(tree.Description, "tiny dim", list);
                d.style.whiteSpace = WhiteSpace.Normal; d.style.marginBottom = Space.S8;
            }
            var cards = Graph(tree, h, list);

            var sel = tree.Nodes.FirstOrDefault(n => n.Id == selected)
                ?? tree.Nodes.FirstOrDefault(n => Talents.State(C, h, tree, n) == TalentState.Available)
                ?? tree.Nodes.FirstOrDefault();
            if (sel != null)
            {
                if (cards.TryGetValue(sel.Id, out var selCard)) selCard.AddToClassList("talent-node--selected");
                Detail(tree, sel, h, body);
            }

            if (popNode != null && cards.TryGetValue(popNode, out var popped))
                App.StartCoroutine(Tween.Pop(popped, 1.12f, 0.35f));
            popNode = null;
        }

        /// <summary>Lays the tree out as rows by <see cref="SkillNodeDef.RequiredPointsInTree"/>, each row ordered by branch
        /// (a node follows its first prerequisite's root), and draws a link under every prerequisite → node pair.</summary>
        private Dictionary<string, VisualElement> Graph(SkillTreeDef tree, HeroSave h, VisualElement parent)
        {
            var graph = Ui.El("col", parent);
            graph.name = "talent-graph";
            var links = new TalentLinks();
            graph.Add(links);

            int RootIndex(SkillNodeDef n)
            {
                var cur = n;
                for (int guard = 0; guard < 32 && cur.Prerequisites.Count > 0; guard++)
                {
                    var p = tree.Nodes.FirstOrDefault(x => x.Id == cur.Prerequisites[0]);
                    if (p == null) break;
                    cur = p;
                }
                return tree.Nodes.IndexOf(cur);
            }

            var cards = new Dictionary<string, VisualElement>();
            foreach (var tier in tree.Nodes.GroupBy(n => n.RequiredPointsInTree).OrderBy(g => g.Key))
            {
                var row = Ui.El("row", graph);
                row.style.alignItems = Align.Center;
                row.style.marginBottom = Space.S32;
                int need = Talents.IsHome(C, h, tree) ? tier.Key : Talents.ScaledRequirement(tier.Key);
                var lbl = Ui.Text(need == 0 ? "Start" : $"{need} pts", "tiny dim", row);
                lbl.style.width = Size.ColumnL; lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                var cells = Ui.El("row grow", row);
                cells.style.justifyContent = Justify.SpaceAround; // cards shrink to share a wide tier rather than wrap
                foreach (var n in tier.OrderBy(RootIndex).ThenBy(x => tree.Nodes.IndexOf(x)))
                    cards[n.Id] = Card(tree, n, h, cells);
            }
            foreach (var n in tree.Nodes)
                foreach (var pre in n.Prerequisites)
                    if (cards.TryGetValue(pre, out var from) && cards.TryGetValue(n.Id, out var to))
                        links.Links.Add((from, to, Talents.Unlocked(h, tree, n) ? PurchasedColor : Talents.State(C, h, tree, n) == TalentState.Available ? AvailableColor : LockedColor));
            graph.RegisterCallback<GeometryChangedEvent>(_ => links.MarkDirtyRepaint());
            return cards;
        }

        private VisualElement Card(SkillTreeDef tree, SkillNodeDef n, HeroSave h, VisualElement parent)
        {
            var state = Talents.State(C, h, tree, n);
            var a = C.TryAbility(n.Grants);
            var card = Ui.El("col center talent-node", parent);
            card.name = "talent-" + n.Id;
            card.style.width = Size.Card; card.style.minHeight = Size.Thumb + Space.S32;
            card.style.paddingTop = card.style.paddingBottom = Space.S8;
            card.style.paddingLeft = card.style.paddingRight = Space.S8;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 16;
            bool sel = n.Id == selected;
            float bw = sel ? 7 : state == TalentState.Available ? 5 : 3;
            card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = bw;
            var bc = sel ? Color.white : StateColor(state);
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = bc;
            card.style.backgroundColor = state == TalentState.Locked ? Palette.Ink.WithA(0.96f)
                : state == TalentState.Available ? new Color(0.07f, 0.12f, 0.34f, 0.97f) : Palette.Ink.WithA(0.97f);
            var icon = Art.AbilityIcon(a);
            if (icon != null)
            {
                var ic = Ui.El("menu-icon", card);
                ic.style.backgroundImage = new StyleBackground(icon);
                ic.style.marginRight = Space.S0;
                if (state == TalentState.Locked) ic.style.unityBackgroundImageTintColor = new Color(0.5f, 0.5f, 0.6f, 0.7f);
            }
            var name = Ui.Text(n.Name ?? a?.Name ?? n.Id, "tiny" + (state == TalentState.Locked ? " dim" : ""), card);
            name.style.whiteSpace = WhiteSpace.Normal; name.style.unityTextAlign = TextAnchor.MiddleCenter;
            int cost = Talents.Cost(C, h, tree, n);
            string tag = state == TalentState.Purchased || state == TalentState.Innate ? "✔ " + StateLabel(state) : $"{cost} pt{(cost == 1 ? "" : "s")}";
            var tagLbl = Ui.Text(tag, "tiny", card);
            tagLbl.style.color = StateColor(state);
            card.AddToClassList("card-tap");
            if (a != null) InspectCard.Attach(card, b => InspectCard.Ability(b, a, $"{tag} · {tree.Name}"));
            Ui.Tappable(card, () => { selected = n.Id; Refresh(); }, "ui.select");
            return card;
        }

        /// <summary>The tapped node's tooltip: what it grants, its cost and gate, and the confirm-to-spend button.</summary>
        private void Detail(SkillTreeDef tree, SkillNodeDef n, HeroSave h, VisualElement body)
        {
            var state = Talents.State(C, h, tree, n);
            var a = C.TryAbility(n.Grants);
            var w = Ui.Window(null, body);
            w.name = "talent-detail";
            var top = Ui.El("row", w);
            top.style.alignItems = Align.Center;
            var icon = Art.AbilityIcon(a);
            if (icon != null) { var ic = Ui.El("menu-icon", top); ic.style.backgroundImage = new StyleBackground(icon); }
            var title = Ui.El("col grow", top);
            Ui.Text(n.Name ?? a?.Name ?? n.Id, "h2", title);
            string kind = a == null ? "" : a.Kind == AbilityKind.Passive ? "Passive" : a.Kind.ToString();
            Ui.Text($"{kind}{(a != null && a.Name != n.Name ? " · grants " + a.Name : "")}", "tiny dim", title);
            var st = Ui.Text(StateLabel(state), "small", top);
            st.style.color = StateColor(state);
            var desc = GlossaryText.Create(a?.Description ?? n.Description ?? "", "small dim", w);
            desc.style.whiteSpace = WhiteSpace.Normal; desc.style.marginTop = Space.S8;
            if (!string.IsNullOrEmpty(n.Description) && a?.Description != null && n.Description != a.Description)
            {
                var flavor = GlossaryText.Create(n.Description, "tiny dim", w);
                flavor.style.whiteSpace = WhiteSpace.Normal; flavor.style.marginTop = Space.S4;
            }
            var foot = Ui.El("row", w);
            foot.style.alignItems = Align.Center; foot.style.marginTop = Space.S8;
            int cost = Talents.Cost(C, h, tree, n);
            string pts = $"{cost} skill point{(cost == 1 ? "" : "s")}";
            bool offTree = !Talents.IsHome(C, h, tree);
            string premium = offTree && cost > n.Cost ? $" (off-tree: +{cost - n.Cost})" : "";
            string gate = state == TalentState.Locked ? Talents.WhyLocked(C, h, tree, n)
                : state == TalentState.Innate ? "Already in this hero's kit — no points needed"
                : state == TalentState.Purchased ? "Bought with skill points (Respec at the Inn refunds it)"
                : h.SkillPoints < cost ? $"Needs {pts}{premium} ({h.SkillPoints} now)"
                : $"Costs {pts}{premium}";
            var g = Ui.Text(gate, "tiny " + (state == TalentState.Locked || (state == TalentState.Available && h.SkillPoints < cost) ? "bad" : "dim") + " grow", foot);
            g.style.whiteSpace = WhiteSpace.Normal;
            if (state == TalentState.Available)
            {
                var hero = C.Class(h.ClassId).Name;
                bool opens = offTree && !Talents.Opened(C, h, tree);
                var learn = Ui.Button($"Learn · {cost} pt{(cost == 1 ? "" : "s")}", () => Confirm.Show(
                    $"Spend {pts} to teach {hero} {n.Name ?? a?.Name}?" + (opens ? $" This opens {tree.Name} as an off-tree." : ""), "Spend", () =>
                    {
                        var r = Talents.Unlock(C, S, h, tree.Id, n.Id);
                        if (r.Ok) { popNode = n.Id; selected = n.Id; }
                        Done(r);
                    }), "btn--primary btn--small", foot);
                learn.name = "talent-learn";
                Ui.SetEnabled(learn, h.SkillPoints >= cost);
            }
        }
    }

    /// <summary>Prerequisite links behind the talent cards: an elbow from the bottom of each prerequisite to the top of the
    /// node it opens, in the colour of how far along that link the hero is.</summary>
    public sealed class TalentLinks : VisualElement
    {
        public readonly List<(VisualElement from, VisualElement to, Color color)> Links = new List<(VisualElement, VisualElement, Color)>();

        public TalentLinks()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = Space.S0;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            foreach (var (from, to, color) in Links)
            {
                var fb = from.worldBound; var tb = to.worldBound;
                if (fb.width <= 0 || tb.width <= 0) continue;
                var a = this.WorldToLocal(new Vector2(fb.center.x, fb.yMax));
                var b = this.WorldToLocal(new Vector2(tb.center.x, tb.yMin));
                float midY = (a.y + b.y) / 2f;
                p.strokeColor = new Color(0f, 0f, 0f, 0.5f); p.lineWidth = 10f;
                Path(p, a, b, midY);
                p.strokeColor = color; p.lineWidth = 5f;
                Path(p, a, b, midY);
            }
        }

        private static void Path(Painter2D p, Vector2 a, Vector2 b, float midY)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(new Vector2(a.x, midY));
            p.LineTo(new Vector2(b.x, midY));
            p.LineTo(b);
            p.Stroke();
        }
    }

    /// <summary>The Inn's Respec (§25.6.3, GG-T53): every hero with talent points spent, its price (scaling with level),
    /// and a confirm that refunds all of that hero's points.</summary>
    public sealed class RespecScreen : HubPage
    {
        public override string Id => "respec";
        protected override string Title => "Inn — Respec";
        protected override string Npc => CampNpcs.Innkeeper;

        protected override void BuildBody(VisualElement body)
        {
            var info = Ui.Window(null, body);
            var t = Ui.Text($"Rest a night and forget your training: every skill point a hero spent on talents comes back. " +
                $"Costs {Ui.Gold(Talents.RespecBaseGold)} + {Ui.Gold(Talents.RespecGoldPerLevel)} per level. " +
                "Abilities from a hero's starting kit or bought at Learn are kept.", "small dim", info);
            t.style.whiteSpace = WhiteSpace.Normal;
            string closed = Talents.RespecClosed(S);
            if (closed != null) Ui.Text(closed, "small bad", info).style.whiteSpace = WhiteSpace.Normal;

            var w = Ui.Window("Heroes", body);
            w.style.flexGrow = 1;
            var list = Ui.List(w).contentContainer;
            foreach (var h in S.Roster.OrderByDescending(x => S.Party.Contains(x.ClassId)).ThenBy(x => x.ClassId))
            {
                var hero = h;
                int spent = Talents.PointsSpent(C, hero);
                int cost = Talents.RespecCost(hero);
                string name = C.TryClass(hero.ClassId)?.Name ?? hero.ClassId;
                string value = spent == 0 ? "No talents bought" : $"{spent} pts · {Ui.Gold(cost)}";
                bool ok = closed == null && spent > 0 && S.Gold >= cost;
                var row = Ui.MenuRow($"{name}  Lv {Progression.Level(hero)}", value, () => Confirm.Show(
                    $"Pay {Ui.Gold(cost)} to unlearn {name}'s talents and refund {spent} skill point{(spent == 1 ? "" : "s")}?", "Pay",
                    () => Done(Talents.Respec(C, S, hero))), list, null, ok);
                row.name = "respec-" + hero.ClassId;
            }
        }
    }
}
