using System.Collections.Generic;

namespace MazeSolver
{
    // Runs the real solver headless (analysis mode) to record the solve sheet before
    // playback. Uses no RNG of any kind, so the music seed drawn afterwards in
    // StartSession is untouched. Must never be replaced by MazeGenerator's
    // SimulateConcurrency, which follows different traversal rules.
    public static class SolvePresolver
    {
        public static SolveSheet Run(byte[,] grid, int n)
        {
            var engine = new MazeSolverEngine();
            engine.Initialize(grid, n, analysisMode: true);

            var active = new List<ushort>(256);
            var births = new List<ushort>(256);
            var deaths = new List<ushort>(256);
            var visited = new List<int>(256);
            int previousSpawned = engine.TotalSpawned;
            int previousDead = engine.DeadCount;
            int peakActive = 1, peakStep = 0, firstForkStep = -1;
            // Every step visits at least one new cell or kills at least one agent, and
            // agents never outnumber cells, so this bound is unreachable unless the
            // traversal contract breaks.
            int stepCap = 2 * n * n + 16;

            while (!engine.IsSolved && engine.HasActiveAgents && engine.CurrentStep < stepCap)
            {
                engine.Step();
                active.Add((ushort)System.Math.Min(engine.ActiveCount, ushort.MaxValue));
                int born = engine.TotalSpawned - previousSpawned;
                int died = engine.DeadCount - previousDead;
                births.Add((ushort)System.Math.Min(born, ushort.MaxValue));
                deaths.Add((ushort)System.Math.Min(died, ushort.MaxValue));
                visited.Add(engine.Visited.Count);
                previousSpawned = engine.TotalSpawned;
                previousDead = engine.DeadCount;
                if (firstForkStep < 0 && born > 0) firstForkStep = engine.CurrentStep;
                if (engine.ActiveCount > peakActive)
                {
                    peakActive = engine.ActiveCount;
                    peakStep = engine.CurrentStep;
                }
            }

            var turningPoints = new List<int>();
            for (int step = 1; step < active.Count - 1; step++)
            {
                int previous = active[step - 1], current = active[step], next = active[step + 1];
                if ((current > previous && current >= next) || (current < previous && current <= next))
                    turningPoints.Add(step + 1); // steps are 1-indexed
            }

            return new SolveSheet
            {
                TerminalStep = engine.CurrentStep,
                Outcome = engine.IsSolved ? SessionOutcome.Solved : SessionOutcome.Exhausted,
                Active = active.ToArray(),
                Births = births.ToArray(),
                Deaths = deaths.ToArray(),
                VisitedCount = visited.ToArray(),
                PeakActive = peakActive,
                PeakStep = peakStep,
                FirstForkStep = firstForkStep,
                TurningPoints = turningPoints.ToArray()
            };
        }
    }
}
