using System.Collections;
using UnityEngine;

namespace MazeSolver
{
    public class MazeGameManager : MonoBehaviour
    {
        [Header("References")]
        public MazeRenderer mazeRenderer;
        public MazeUIController uiController;
        public MazeAudioEngine audioEngine;

        MazeSolverEngine solver;
        byte[,] currentGrid;
        int currentN;
        bool isSolving;
        bool isPaused;
        Coroutine solveCoroutine;

        void Start()
        {
            uiController.Initialize(this);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                TogglePause();
        }

        public void GenerateAndSolve()
        {
            if (solveCoroutine != null)
                StopCoroutine(solveCoroutine);

            currentN = uiController.MazeSize;
            int targetPaths = uiController.TargetPaths;
            int gridSize = 2 * currentN - 1;

            uiController.SetStatus("Generating...");
            uiController.SetSolving(true);
            uiController.SetAgentCount(0, 0, 0);

            currentGrid = MazeGenerator.GenerateTargeted(currentN, targetPaths);

            mazeRenderer.Initialize(gridSize, currentGrid);

            // Fit camera to maze
            FitCamera(gridSize);

            solver = new MazeSolverEngine();
            solver.Initialize(currentGrid, currentN);

            isPaused = false;
            uiController.SetPauseText(false);

            audioEngine.StartDrone();

            uiController.SetStatus("Solving...");
            isSolving = true;
            solveCoroutine = StartCoroutine(SolveCoroutine());
        }

        IEnumerator SolveCoroutine()
        {
            while (solver.HasActiveAgents && !solver.IsSolved)
            {
                if (!isPaused)
                {
                    solver.Step();
                    mazeRenderer.RenderFrame(solver, currentGrid, currentN);

                    // Process audio events
                    audioEngine.ProcessEvents(solver.StepEvents);

                    // Update intensity
                    int totalCells = currentN * currentN;
                    float explorationRatio = (float)solver.Visited.Count / totalCells;
                    audioEngine.UpdateIntensity(solver.ActiveCount, explorationRatio);

                    // Update UI
                    uiController.SetAgentCount(solver.ActiveCount, solver.DeadCount, solver.TotalSpawned);

                    if (solver.IsSolved)
                    {
                        uiController.SetStatus("Solved!");
                        uiController.SetSolving(false);
                        isSolving = false;
                        audioEngine.PlayFinalChord();
                        break;
                    }
                }

                float delay = uiController.SpeedMs / 1000f;
                yield return new WaitForSeconds(delay);
            }

            if (!solver.IsSolved && !solver.HasActiveAgents)
            {
                uiController.SetStatus("No solution found");
                uiController.SetSolving(false);
                isSolving = false;
                audioEngine.StopDrone();
            }
        }

        public void TogglePause()
        {
            if (!isSolving) return;
            isPaused = !isPaused;
            uiController.SetPauseText(isPaused);
            uiController.SetStatus(isPaused ? "Paused" : "Solving...");
            audioEngine.SetPaused(isPaused);
        }

        public void Reset()
        {
            if (solveCoroutine != null)
            {
                StopCoroutine(solveCoroutine);
                solveCoroutine = null;
            }

            isSolving = false;
            isPaused = false;
            solver = null;

            mazeRenderer.Clear();
            audioEngine.StopAll();

            uiController.SetStatus("Ready");
            uiController.SetSolving(false);
            uiController.SetPauseText(false);
            uiController.SetAgentCount(0, 0, 0);
        }

        void FitCamera(int gridSize)
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Sprite is centered at origin (pivot 0.5, 0.5), PPU=1
            // Account for UI panel on left (~260px at screen width)
            float aspect = (float)Screen.width / Screen.height;
            float halfGrid = gridSize * 0.5f;
            float padding = 5f;
            // Shift camera right to make room for UI panel on the left
            float uiWorldWidth = (260f / Screen.width) * (halfGrid + padding) * 2f * aspect;
            cam.orthographicSize = halfGrid + padding;
            cam.transform.position = new Vector3(uiWorldWidth * 0.5f, 0f, -10f);
        }
    }
}
