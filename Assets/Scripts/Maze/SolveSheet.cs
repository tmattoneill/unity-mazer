namespace MazeSolver
{
    // The recorded future of one maze: per-step solver metrics from a completed presolve,
    // indexed 1..TerminalStep. The hard cap is 2*n*n+16 because each step visits a new
    // cell or kills an agent, keeping the arrays below a few hundred KiB at 100x100.
    public sealed class SolveSheet
    {
        public int TerminalStep;
        public SessionOutcome Outcome;
        public ushort[] Active;       // active agents after each step
        public ushort[] Births;       // agents spawned during each step
        public ushort[] Deaths;       // agents dead during each step
        public int[] VisitedCount;    // coverage = VisitedCount[s] / (n * n)
        public int PeakActive;
        public int PeakStep;
        public int FirstForkStep;     // -1 if the maze never forks
        public int[] TurningPoints;   // steps where the active count is a local extremum
    }
}
