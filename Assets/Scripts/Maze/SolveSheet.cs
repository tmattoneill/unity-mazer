namespace MazeSolver
{
    // The recorded future of one maze: per-step solver metrics from a completed presolve,
    // indexed 1..TerminalStep. Steps are bounded near n*n (each step visits a new cell or
    // kills an agent), so arrays stay tens of KB even at 100x100.
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
