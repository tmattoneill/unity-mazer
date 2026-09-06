namespace MazeSolver
{
    // One pre-allocated composer per MusicStyle value; the renderer switches between them
    // only when handling a Start command, so style changes take effect at the next run.
    public static class StyleComposerFactory
    {
        // Styles without their own composer yet fall back to Cinematic. Each style gains
        // its own rules parameter here as it is implemented.
        public static IStyleComposer[] CreateAll(OrchestralScoreRules cinematicRules)
        {
            var composers = new IStyleComposer[4];
            composers[(int)MusicStyle.Cinematic] = new CinematicComposer(cinematicRules);
            composers[(int)MusicStyle.Ambient] = composers[(int)MusicStyle.Cinematic];
            composers[(int)MusicStyle.EDM] = composers[(int)MusicStyle.Cinematic];
            composers[(int)MusicStyle.Classical] = composers[(int)MusicStyle.Cinematic];
            return composers;
        }
    }
}
