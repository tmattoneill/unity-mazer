using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
    public static class MazeGenerator
    {
        static readonly int[] DR = { 0, 0, 1, -1 };
        static readonly int[] DC = { 1, -1, 0, 0 };

        public static byte[,] Generate(int n, float pNewest = 0.75f, System.Random rng = null)
        {
            if (rng == null) rng = new System.Random();

            int rows = 2 * n - 1;
            int cols = 2 * n - 1;
            var grid = new byte[rows, cols];

            // Fill all with wall (1)
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[r, c] = 1;

            // Mark cell positions as passage
            for (int r = 0; r < rows; r += 2)
                for (int c = 0; c < cols; c += 2)
                    grid[r, c] = 0;

            var visited = new HashSet<long>();
            var active = new List<(int r, int c)>();

            visited.Add(Pack(0, 0));
            active.Add((0, 0));

            var neighbors = new List<(int r, int c)>(4);

            while (active.Count > 0)
            {
                int idx;
                if ((float)rng.NextDouble() < pNewest)
                    idx = active.Count - 1;
                else
                    idx = rng.Next(active.Count);

                var (cr, cc) = active[idx];
                neighbors.Clear();

                for (int d = 0; d < 4; d++)
                {
                    int nr = cr + DR[d];
                    int nc = cc + DC[d];
                    if (nr >= 0 && nr < n && nc >= 0 && nc < n && !visited.Contains(Pack(nr, nc)))
                        neighbors.Add((nr, nc));
                }

                if (neighbors.Count == 0)
                {
                    // Swap-remove
                    active[idx] = active[active.Count - 1];
                    active.RemoveAt(active.Count - 1);
                }
                else
                {
                    var (nr, nc) = neighbors[rng.Next(neighbors.Count)];
                    int wallR = cr * 2 + (nr - cr);
                    int wallC = cc * 2 + (nc - cc);
                    grid[wallR, wallC] = 0;
                    visited.Add(Pack(nr, nc));
                    active.Add((nr, nc));
                }
            }

            return grid;
        }

        // seed uses System.Random only; UnityEngine.Random supplies the music seed and
        // must stay untouched here so a seeded maze cannot perturb the score.
        public static byte[,] GenerateTargeted(int n, int targetPaths = 3, int maxAttempts = 5, int? seed = null)
        {
            targetPaths = Mathf.Clamp(targetPaths, 1, 5);
            var rng = seed.HasValue ? new System.Random(seed.Value) : null;

            // Base p_newest: 1→1.0, 2→0.96, 3→0.92, 4→0.88, 5→0.84
            float pNewest = 1.0f - (targetPaths - 1) * 0.04f;

            if (n > 30)
                pNewest -= Mathf.Min(0.03f, (n - 30) * 0.0005f);

            pNewest = Mathf.Clamp(pNewest, 0.3f, 1.0f);

            byte[,] bestGrid = null;
            float bestDistance = float.MaxValue;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var grid = Generate(n, pNewest, rng);
                float concurrency = SimulateConcurrency(grid, n);
                float distance = Mathf.Abs(concurrency - targetPaths);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestGrid = grid;
                }

                if (distance < 0.5f) break;
            }

            return bestGrid;
        }

        public static float SimulateConcurrency(byte[,] grid, int n)
        {
            int goalR = n - 1, goalC = n - 1;
            var visited = new HashSet<long>();
            visited.Add(Pack(0, 0));

            var agents = new List<(int r, int c)> { (0, 0) };
            float totalActive = 0;
            int steps = 0;

            while (agents.Count > 0)
            {
                steps++;
                totalActive += agents.Count;
                var nextAgents = new List<(int r, int c)>();

                for (int a = 0; a < agents.Count; a++)
                {
                    var (ar, ac) = agents[a];
                    if (ar == goalR && ac == goalC)
                    {
                        return steps <= 1 ? 1f : totalActive / steps;
                    }

                    for (int d = 0; d < 4; d++)
                    {
                        int nr = ar + DR[d];
                        int nc = ac + DC[d];
                        if (nr >= 0 && nr < n && nc >= 0 && nc < n && !visited.Contains(Pack(nr, nc)))
                        {
                            int wallR = ar * 2 + (nr - ar);
                            int wallC = ac * 2 + (nc - ac);
                            if (grid[wallR, wallC] == 0)
                            {
                                visited.Add(Pack(nr, nc));
                                nextAgents.Add((nr, nc));
                            }
                        }
                    }
                }

                agents = nextAgents;
            }

            return steps == 0 ? 1f : totalActive / steps;
        }

        static long Pack(int r, int c) => ((long)r << 32) | (uint)c;
    }
}
