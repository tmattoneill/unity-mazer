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
        public SolveTreeRenderer solveTreeRenderer;

        public MazeAudioEngine AudioEngine => audioEngine;

        MazeSolverEngine solver;
        byte[,] currentGrid;
        int currentN;
        bool isSolving;
        bool isPaused;
        Coroutine solveCoroutine;
        SolveSheet solveSheet;
        bool sheetDivergent;
        public SolveSheet CurrentSolveSheet => solveSheet;

        void Start()
        {
            uiController.Initialize(this);
            if (solveTreeRenderer) solveTreeRenderer.Initialize(512, 512);
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

            uiController.SetWelcomeVisible(false);
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

            // Presolve before the audio session starts: it draws no random numbers, so
            // the music seed taken in StartSession is unaffected.
            solveSheet = SolvePresolver.Run(currentGrid, currentN);
            sheetDivergent = false;
            audioEngine.SetSolveSheet(solveSheet);

            isPaused = false;
            uiController.SetPauseText(false);

            audioEngine.StartSession();
            uiController.RefreshSeed();

            if (solveTreeRenderer)
                solveTreeRenderer.Initialize(512, 512);

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

                    // The live run should replay the sheet exactly; a mismatch means the
                    // forecast can no longer be trusted.
                    if (!sheetDivergent && solveSheet != null)
                    {
                        int step = solver.CurrentStep;
                        if (step > solveSheet.TerminalStep
                            || solveSheet.Active[step - 1] != solver.ActiveCount
                            || solveSheet.VisitedCount[step - 1] != solver.Visited.Count)
                        {
                            sheetDivergent = true;
                            audioEngine.ReportSheetDivergence();
                        }
                    }
                    audioEngine.SubmitSnapshot(solver.ActiveCount, explorationRatio, solver.CurrentStep);

                    // Update solve tree, unless its panel is collapsed
                    if (solveTreeRenderer && uiController.SolvePanelVisible)
                        solveTreeRenderer.RenderTree(solver);

                    // Update UI
                    uiController.SetAgentCount(solver.ActiveCount, solver.DeadCount, solver.TotalSpawned);
                    uiController.UpdateVitals(solver, uiController.SpeedMs);

                    if (solver.IsSolved)
                    {
                        uiController.SetStatus("Solved!");
                        uiController.SetSolving(false);
                        isSolving = false;
                        audioEngine.CompleteSession(SessionOutcome.Solved);
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
                audioEngine.CompleteSession(SessionOutcome.Exhausted);
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

        public void ResetMaze()
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
            audioEngine.StopSession();
            if (solveTreeRenderer) solveTreeRenderer.Clear();

            uiController.SetWelcomeVisible(true);
            uiController.SetStatus("Ready");
            uiController.SetSolving(false);
            uiController.SetPauseText(false);
            uiController.SetAgentCount(0, 0, 0);
        }

        int currentGridSize;

        // Called by the UI when the solve panel is shown or hidden mid-run.
        public void RefreshSolvePanel()
        {
            if (currentGridSize > 0) FitCamera(currentGridSize);
            if (solveTreeRenderer && solver != null && uiController.SolvePanelVisible)
                solveTreeRenderer.RenderTree(solver);
        }

        void FitCamera(int gridSize)
        {
            currentGridSize = gridSize;
            var cam = Camera.main;
            if (cam == null) return;
            // Sprite is centered at origin (pivot 0.5, 0.5), PPU=1. Fit the maze into the
            // pixels left free between the left control panel and the right solve panel.
            float aspect = (float)Screen.width / Screen.height;
            float padding = 5f;
            float world = gridSize + 2f * padding;
            float leftPixels = 260f;
            float rightPixels = uiController ? uiController.SolvePanelWidth : 0f;
            float freeFraction = Mathf.Max(0.2f, (Screen.width - leftPixels - rightPixels) / Screen.width);
            float size = Mathf.Max(world * 0.5f, world / (2f * aspect * freeFraction));
            cam.orthographicSize = size;
            // Center the maze in the free region.
            float visibleWorldWidth = 2f * size * aspect;
            float freeCenterPixels = leftPixels + (Screen.width - leftPixels - rightPixels) * 0.5f;
            float offset = (freeCenterPixels - Screen.width * 0.5f) / Screen.width * visibleWorldWidth;
            cam.transform.position = new Vector3(-offset, 0f, -10f);
        }
    }
}
