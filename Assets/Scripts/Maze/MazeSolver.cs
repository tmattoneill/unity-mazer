using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
    // Traversal contract (relied on by presolve/solve-sheet equality): agents step in
    // ascending id order; neighbors are inspected E,W,S,N (DR/DC order); a cell is claimed
    // in Visited at inspection time, not on arrival; children spawn in remaining-neighbor
    // order; the first agent to reach the goal ends the step. Changing any of these
    // invalidates recorded solve sheets.
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
        public int CurrentStep { get; private set; }
        public int SolvedCount { get; private set; }

        byte[,] grid;
        int n;
        int nextAgentId;
        Vector2Int goal;
        List<AgentData> agentsById;
        readonly List<AgentData> stepAgents = new List<AgentData>();
        readonly List<Vector2Int> neighborBuffer = new List<Vector2Int>(4);
        bool analysisMode;
        static readonly List<Vector2Int> EmptyPath = new List<Vector2Int>();

        // analysisMode runs the identical traversal without path bookkeeping or per-move
        // events, so a headless presolve stays cheap while producing the same trace.
        public void Initialize(byte[,] grid, int n, bool analysisMode = false)
        {
            this.analysisMode = analysisMode;
            this.grid = grid;
            this.n = n;
            this.goal = new Vector2Int(n - 1, n - 1);

            Agents = new Dictionary<int, AgentData>();
            agentsById = new List<AgentData>();
            Visited = new HashSet<long>();
            StepEvents = new List<SolverEvent>();
            IsSolved = false;
            HasActiveAgents = true;
            SolutionPath = null;
            ActiveCount = 0;
            DeadCount = 0;
            TotalSpawned = 0;
            CurrentStep = 0;
            SolvedCount = 0;
            nextAgentId = 0;

            SpawnAgent(Vector2Int.zero, -1, new List<Vector2Int>());
        }

        void SpawnAgent(Vector2Int start, int parentId, List<Vector2Int> inheritedPath)
        {
            int id = nextAgentId++;
            var agent = new AgentData(id, parentId, start, analysisMode ? EmptyPath : inheritedPath, CurrentStep);
            Agents[id] = agent;
            agentsById.Add(agent);
            TotalSpawned++;

            // Mark visited
            Visited.Add(Pack(start.x, start.y));

            if (analysisMode) return;
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

            CurrentStep++;

            // Snapshot active agents in ascending id order (children spawned this step
            // must not step until the next one, and spawning must not disturb iteration)
            stepAgents.Clear();
            for (int i = 0; i < agentsById.Count; i++)
            {
                if (agentsById[i].Status == AgentStatus.Active)
                    stepAgents.Add(agentsById[i]);
            }

            foreach (var agent in stepAgents)
            {
                if (IsSolved) break;

                GetUnvisitedNeighbors(agent.Position, neighborBuffer);
                var neighbors = neighborBuffer;

                if (neighbors.Count == 0)
                {
                    agent.Status = AgentStatus.Dead;
                    agent.DeathStep = CurrentStep;
                    if (!analysisMode) StepEvents.Add(new SolverEvent
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
                    List<Vector2Int> forkPath = neighbors.Count > 1 && !analysisMode
                        ? new List<Vector2Int>(agent.FullPath)
                        : null;

                    // Move to first neighbor
                    MoveAgent(agent, neighbors[0]);

                    // Check goal
                    if (agent.Position == goal)
                    {
                        agent.Status = AgentStatus.Solved;
                        agent.DeathStep = CurrentStep;
                        IsSolved = true;
                        if (!analysisMode)
                        {
                            SolutionPath = new List<Vector2Int>(agent.FullPath);
                            StepEvents.Add(new SolverEvent
                            {
                                Type = SolverEventType.SolutionFound,
                                AgentId = agent.Id,
                                Position = agent.Position,
                                SolutionPath = SolutionPath
                            });
                        }
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
            SolvedCount = 0;
            for (int i = 0; i < agentsById.Count; i++)
            {
                if (agentsById[i].Status == AgentStatus.Active) ActiveCount++;
                else if (agentsById[i].Status == AgentStatus.Dead) DeadCount++;
                else if (agentsById[i].Status == AgentStatus.Solved) SolvedCount++;
            }
            HasActiveAgents = ActiveCount > 0;
        }

        void MoveAgent(AgentData agent, Vector2Int pos)
        {
            agent.Position = pos;
            if (analysisMode) return;
            agent.FullPath.Add(pos);
            agent.OwnPath.Add(pos);

            StepEvents.Add(new SolverEvent
            {
                Type = SolverEventType.AgentMoved,
                AgentId = agent.Id,
                Position = pos
            });
        }

        void GetUnvisitedNeighbors(Vector2Int pos, List<Vector2Int> result)
        {
            result.Clear();
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

        }

        static long Pack(int r, int c) => ((long)r << 32) | (uint)c;
    }
}
