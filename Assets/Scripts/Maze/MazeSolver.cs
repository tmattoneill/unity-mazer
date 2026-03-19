using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
    public class MazeSolverEngine
    {
        static readonly int[] DR = { 0, 0, 1, -1 };
        static readonly int[] DC = { 1, -1, 0, 0 };

        public Dictionary<int, AgentData> Agents { get; private set; }
        public HashSet<long> Visited { get; private set; }
        public List<SolverEvent> StepEvents { get; private set; }
        public bool IsSolved { get; private set; }
        public bool HasActiveAgents { get; private set; }
        public List<Vector2Int> SolutionPath { get; private set; }
        public int ActiveCount { get; private set; }
        public int DeadCount { get; private set; }
        public int TotalSpawned { get; private set; }

        byte[,] grid;
        int n;
        int nextAgentId;
        Vector2Int goal;

        public void Initialize(byte[,] grid, int n)
        {
            this.grid = grid;
            this.n = n;
            this.goal = new Vector2Int(n - 1, n - 1);

            Agents = new Dictionary<int, AgentData>();
            Visited = new HashSet<long>();
            StepEvents = new List<SolverEvent>();
            IsSolved = false;
            HasActiveAgents = true;
            SolutionPath = null;
            ActiveCount = 0;
            DeadCount = 0;
            TotalSpawned = 0;
            nextAgentId = 0;

            SpawnAgent(Vector2Int.zero, -1, new List<Vector2Int>());
        }

        void SpawnAgent(Vector2Int start, int parentId, List<Vector2Int> inheritedPath)
        {
            int id = nextAgentId++;
            var agent = new AgentData(id, parentId, start, inheritedPath);
            Agents[id] = agent;
            TotalSpawned++;

            // Mark visited
            Visited.Add(Pack(start.x, start.y));

            StepEvents.Add(new SolverEvent
            {
                Type = SolverEventType.AgentSpawned,
                AgentId = id,
                ParentId = parentId,
                Position = start
            });
        }

        public void Step()
        {
            StepEvents.Clear();

            if (IsSolved || !HasActiveAgents) return;

            // Collect active agents first (avoid modifying dict during iteration)
            var activeAgents = new List<AgentData>();
            foreach (var kvp in Agents)
            {
                if (kvp.Value.Status == AgentStatus.Active)
                    activeAgents.Add(kvp.Value);
            }

            foreach (var agent in activeAgents)
            {
                if (IsSolved) break;

                var neighbors = GetUnvisitedNeighbors(agent.Position);

                if (neighbors.Count == 0)
                {
                    agent.Status = AgentStatus.Dead;
                    StepEvents.Add(new SolverEvent
                    {
                        Type = SolverEventType.AgentDied,
                        AgentId = agent.Id,
                        ParentId = agent.ParentId,
                        Position = agent.Position
                    });
                }
                else
                {
                    // Capture fork path before moving
                    List<Vector2Int> forkPath = neighbors.Count > 1
                        ? new List<Vector2Int>(agent.FullPath)
                        : null;

                    // Move to first neighbor
                    MoveAgent(agent, neighbors[0]);

                    // Check goal
                    if (agent.Position == goal)
                    {
                        agent.Status = AgentStatus.Solved;
                        IsSolved = true;
                        SolutionPath = new List<Vector2Int>(agent.FullPath);
                        StepEvents.Add(new SolverEvent
                        {
                            Type = SolverEventType.SolutionFound,
                            AgentId = agent.Id,
                            Position = agent.Position,
                            SolutionPath = SolutionPath
                        });
                        break;
                    }

                    // Spawn children for extra branches
                    if (neighbors.Count > 1)
                    {
                        for (int i = 1; i < neighbors.Count; i++)
                        {
                            SpawnAgent(neighbors[i], agent.Id, forkPath);
                        }
                    }
                }
            }

            // Update counts
            ActiveCount = 0;
            DeadCount = 0;
            foreach (var kvp in Agents)
            {
                if (kvp.Value.Status == AgentStatus.Active) ActiveCount++;
                else if (kvp.Value.Status == AgentStatus.Dead) DeadCount++;
            }
            HasActiveAgents = ActiveCount > 0;
        }

        void MoveAgent(AgentData agent, Vector2Int pos)
        {
            agent.Position = pos;
            agent.FullPath.Add(pos);
            agent.OwnPath.Add(pos);

            StepEvents.Add(new SolverEvent
            {
                Type = SolverEventType.AgentMoved,
                AgentId = agent.Id,
                Position = pos
            });
        }

        List<Vector2Int> GetUnvisitedNeighbors(Vector2Int pos)
        {
            var result = new List<Vector2Int>(4);
            int r = pos.x, c = pos.y;

            for (int d = 0; d < 4; d++)
            {
                int nr = r + DR[d];
                int nc = c + DC[d];
                if (nr >= 0 && nr < n && nc >= 0 && nc < n && !Visited.Contains(Pack(nr, nc)))
                {
                    int wallR = r * 2 + DR[d];
                    int wallC = c * 2 + DC[d];
                    if (grid[wallR, wallC] == 0)
                    {
                        Visited.Add(Pack(nr, nc));
                        result.Add(new Vector2Int(nr, nc));
                    }
                }
            }

            return result;
        }

        static long Pack(int r, int c) => ((long)r << 32) | (uint)c;
    }
}
