namespace GridGame.Core.Content
{
    /// <summary>
    /// A text crawl that isn't a theme's (§27, GG-T231): campaign.json's <c>Prologue</c> (played once after a new
    /// campaign's party is locked in, before the first Camp) and <c>Ending</c> (played after the first victory over
    /// <see cref="Stage"/>, then the rolling credits, then the Title).
    /// </summary>
    public sealed class CrawlDef
    {
        public string Title;
        public string Crawl;
        /// <summary>Theme id whose art sits behind the text.</summary>
        public string Backdrop;
        /// <summary>Ending only: the campaign stage whose first victory ends the story.</summary>
        public string Stage;
    }
}
