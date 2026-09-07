namespace MazeSolver
{
    // Rules assets per style; any missing entry falls back to the Cinematic rules, so a
    // project with only the Cinematic asset still runs every style.
    public struct StyleRuleBundle
    {
        public OrchestralScoreRules Cinematic, Ambient, EDM, Classical;
        public StyleRuleBundle(OrchestralScoreRules shared) => Cinematic = Ambient = EDM = Classical = shared;
    }

    // One pre-allocated composer per MusicStyle value; the renderer switches between them
    // only when handling a Start command, so style changes take effect at the next run.
    public static class StyleComposerFactory
    {
        public static IStyleComposer[] CreateAll(StyleRuleBundle rules)
        {
            var fallback = rules.Cinematic;
            var composers = new IStyleComposer[4];
            composers[(int)MusicStyle.Cinematic] = new CinematicComposer(fallback);
            composers[(int)MusicStyle.Ambient] = new AmbientComposer(rules.Ambient ? rules.Ambient : fallback);
            composers[(int)MusicStyle.EDM] = new EdmComposer(rules.EDM ? rules.EDM : fallback);
            composers[(int)MusicStyle.Classical] = new ClassicalComposer(rules.Classical ? rules.Classical : fallback);
            return composers;
        }
    }
}
