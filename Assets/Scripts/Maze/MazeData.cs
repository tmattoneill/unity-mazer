using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
    public enum AgentStatus { Active, Dead, Solved }

    public enum SolverEventType { AgentSpawned, AgentMoved, AgentDied, SolutionFound }

    public struct SolverEvent
    {
        public SolverEventType Type;
        public int AgentId;
        public int ParentId; // -1 if none
        public Vector2Int Position;
        public List<Vector2Int> SolutionPath;
    }

    public class AgentData
    {
        public int Id;
        public int ParentId; // -1 if none
        public List<Vector2Int> FullPath;
        public List<Vector2Int> OwnPath;
        public Vector2Int Position;
        public AgentStatus Status;
        public int SpawnStep;
        public int DeathStep; // -1 if still alive

        public AgentData(int id, int parentId, Vector2Int start, List<Vector2Int> inheritedPath, int spawnStep)
        {
            Id = id;
            ParentId = parentId;
            Position = start;
            FullPath = new List<Vector2Int>(inheritedPath) { start };
            OwnPath = new List<Vector2Int> { start };
            Status = AgentStatus.Active;
            SpawnStep = spawnStep;
            DeathStep = -1;
        }
    }
}
