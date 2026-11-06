using UnityEngine.UIElements;

namespace GridGame.App
{
    /// <summary>The one shared paragraph rule (POL-A2/A5/A6, GG-T273): wrapped text long enough to be a paragraph is body
    /// text — at least body size, and no wider than about 60 characters (≈ 30 em at a ~0.5 em average glyph advance).</summary>
    public static class Typography
    {
        public const int ParagraphChars = 60;

        public static void Paragraphs(VisualElement root)
        {
            root.Query<Label>().ForEach(l =>
            {
                if (l.resolvedStyle.whiteSpace != WhiteSpace.Normal || l.text == null || l.text.Length <= ParagraphChars) return;
                if (l.resolvedStyle.fontSize < TypeScale.Body - 0.5f) l.style.fontSize = TypeScale.Body;
                float cap = TypeScale.Body * ParagraphChars * 0.5f;
                if (l.style.maxWidth.value.value != cap) l.style.maxWidth = cap;
            });
            // A GlossaryText paragraph (GG-T300) is a row of word labels: the same rule, on its whole text.
            root.Query(className: GlossaryText.UssClass).ForEach(box =>
            {
                if (!(box.userData is int chars) || chars <= ParagraphChars) return;
                box.Query<Label>().ForEach(l => { if (l.resolvedStyle.fontSize < TypeScale.Body - 0.5f) l.style.fontSize = TypeScale.Body; });
                float cap = TypeScale.Body * ParagraphChars * 0.5f;
                if (box.style.maxWidth.value.value != cap) box.style.maxWidth = cap;
            });
        }

        /// <summary>Applies <see cref="Paragraphs"/> now and whenever a panel refills (detail windows rebuild on selection).</summary>
        public static void Watch(VisualElement root) => root.schedule.Execute(() => Paragraphs(root)).Every(200);
    }
}
