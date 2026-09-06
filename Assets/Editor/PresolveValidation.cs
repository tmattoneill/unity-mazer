using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MazeSolver.Editor
{
    // Validates the presolve trace contract, the presolve time budget, the ensemble layer
    // rules and the conductor forecast, all without audio hardware.
    public static class PresolveValidation
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Presolve validation: " + message);
        }

        [MenuItem("Tools/Orchestra/Validate presolve and ensemble")]
        public static void Run()
        {
            ValidateTraces();
            Benchmark();
            ValidateLayers();
            ValidateConductor();
            Debug.Log("PRESOLVE VALIDATION PASSED: trace equality, time budget, layer hysteresis, forecast.");
        }

        static void ValidateTraces()
        {
            foreach (int size in new[] { 5, 25, 60, 100 })
            foreach (int targetPaths in new[] { 1, 3, 5 })
            {
                var grid = MazeGenerator.GenerateTargeted(size, targetPaths, seed: size * 100 + targetPaths);
                var sheet = SolvePresolver.Run(grid, size);
                Require(sheet.TerminalStep > 0 && sheet.TerminalStep == sheet.Active.Length, "sheet step count mismatch");

                var live = new MazeSolverEngine();
                live.Initialize(grid, size);
                int previousSpawned = live.TotalSpawned, previousDead = live.DeadCount;
                while (!live.IsSolved && live.HasActiveAgents)
                {
                    live.Step();
                    int step = live.CurrentStep;
                    Require(step <= sheet.TerminalStep, "live run outlasted the sheet");
                    Require(sheet.Active[step - 1] == live.ActiveCount, "active count diverged at step " + step);
                    Require(sheet.Births[step - 1] == live.TotalSpawned - previousSpawned, "births diverged at step " + step);
                    Require(sheet.Deaths[step - 1] == live.DeadCount - previousDead, "deaths diverged at step " + step);
                    Require(sheet.VisitedCount[step - 1] == live.Visited.Count, "coverage diverged at step " + step);
                    previousSpawned = live.TotalSpawned; previousDead = live.DeadCount;
                }
                Require(live.CurrentStep == sheet.TerminalStep, "terminal step diverged");
                Require((live.IsSolved ? SessionOutcome.Solved : SessionOutcome.Exhausted) == sheet.Outcome, "outcome diverged");
            }
            // An unreachable goal must presolve to Exhausted without hanging.
            var walled = MazeGenerator.GenerateTargeted(9, 3, seed: 7);
            int wallSize = 2 * 9 - 1;
            for (int i = 0; i < wallSize; i++) { walled[i, wallSize - 2] = 1; walled[wallSize - 2, i] = 1; }
            Require(SolvePresolver.Run(walled, 9).Outcome == SessionOutcome.Exhausted, "walled maze not exhausted");
        }

        static void Benchmark()
        {
            foreach (int size in new[] { 25, 50, 100 })
            {
                var grid = MazeGenerator.GenerateTargeted(size, 3, seed: size);
                SolvePresolver.Run(grid, size); // warm up
                var watch = Stopwatch.StartNew();
                var sheet = SolvePresolver.Run(grid, size);
                watch.Stop();
                Debug.Log($"Presolve {size}x{size}: {watch.Elapsed.TotalMilliseconds:0.00} ms, {sheet.TerminalStep} steps, peak {sheet.PeakActive} agents.");
                if (size == 100) Require(watch.Elapsed.TotalMilliseconds <= 50, "100x100 presolve exceeded 50 ms");
            }
        }

        static void ValidateLayers()
        {
            var rules = AssetDatabase.LoadAssetAtPath<OrchestralScoreRules>("Assets/Resources/Orchestra/ScoreRules.asset");
            Require(rules, "score rules asset missing");
            var settings = MusicSettings.Default;
            settings.Density = 1;
            var notes = new ScoreNote[64];

            // Flicker around the layer-2 boundary (enter 2, exit below 1) must not flap.
            var score = new CinematicComposer(rules);
            score.Reset(431, settings);
            for (int beat = 0; beat < 64; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = beat % 2 == 0 ? 2 : 1 });
                score.ComposeBeat(settings, notes);
            }
            int settled = score.Layers;
            for (int beat = 64; beat < 128; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = beat % 2 == 0 ? 2 : 1 });
                score.ComposeBeat(settings, notes);
                Require(score.Layers == settled, "layers flapped under a flickering population");
            }

            // Monotone growth in the population produces monotone non-decreasing layers,
            // and layer changes land only on bar boundaries.
            score.Reset(431, settings);
            int previousLayers = score.Layers;
            for (int beat = 0; beat < 256; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = 1 + beat / 4 });
                score.ComposeBeat(settings, notes);
                if (score.Layers != previousLayers)
                {
                    Require(beat % 4 == 0, "layer change off a bar boundary");
                    Require(score.Layers > previousLayers, "layers shrank during growth");
                }
                previousLayers = score.Layers;
            }
            Require(score.Layers == 7, "crowded population did not reach the full ensemble");

            // Density is the ceiling.
            var sparse = MusicSettings.Default; sparse.Density = 0.2f;
            score.Reset(431, sparse);
            for (int beat = 0; beat < 256; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = 100 });
                score.ComposeBeat(sparse, notes);
            }
            Require(score.Layers <= 3, "density did not cap the ensemble");

            // Collapse: layers come back down and only at boundaries.
            score.Reset(431, settings);
            for (int beat = 0; beat < 128; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = 60 });
                score.ComposeBeat(settings, notes);
            }
            Require(score.Layers == 7, "did not reach full ensemble before collapse");
            for (int beat = 128; beat < 384; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = 1 });
                score.ComposeBeat(settings, notes);
            }
            Require(score.Layers == 1, "collapse did not return to the core ensemble");
        }

        static void ValidateConductor()
        {
            var sheet = new SolveSheet { TerminalStep = 200, Outcome = SessionOutcome.Solved,
                Active = new ushort[200], Births = new ushort[200], Deaths = new ushort[200], VisitedCount = new int[200] };
            var conductor = new SolveConductor();
            conductor.SetSheet(sheet);
            Require(conductor.RemainingSeconds < 0, "forecast produced before any timing exists");
            double now = 100;
            for (int step = 1; step <= 100; step++) { conductor.OnStep(step, now); now += 0.05; }
            float remaining = conductor.RemainingSeconds;
            Require(Math.Abs(remaining - 100 * 0.05f) < 0.5f, "remaining-time estimate off: " + remaining);
            Require(Math.Abs(conductor.ForecastBeatsRemaining(120) - remaining * 2) < 0.01f, "beat conversion wrong");
            Require(Math.Abs(conductor.Progress - 0.5f) < 0.01f, "progress wrong");

            // A pause gap must not poison the estimate.
            conductor.OnPauseChanged();
            now += 30;
            conductor.OnStep(101, now);
            Require(Math.Abs(conductor.RemainingSeconds - 99 * 0.05f) < 0.5f, "pause gap entered the estimate");

            // A speed change adapts quickly to the new cadence.
            conductor.OnRequestedDelayChanged(0.2f);
            for (int step = 102; step <= 140; step++) { now += 0.2; conductor.OnStep(step, now); }
            Require(Math.Abs(conductor.ObservedStepSeconds - 0.2f) < 0.05f, "speed change not adopted");

            // Divergence disables the forecast permanently.
            conductor.MarkDivergent();
            Require(conductor.RemainingSeconds < 0 && conductor.Progress < 0, "divergent sheet still forecast");

            // Composer ending preparation: a shrinking forecast prepares the dominant, and
            // Complete then lands the tonic cadence.
            var rules = AssetDatabase.LoadAssetAtPath<OrchestralScoreRules>("Assets/Resources/Orchestra/ScoreRules.asset");
            var score = new CinematicComposer(rules);
            var settings = MusicSettings.Default;
            score.Reset(431, settings);
            var notes = new ScoreNote[64];
            for (int beat = 0; beat < 96; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = 6, ForecastBeatsRemaining = 100 - beat, Progress = beat / 100f });
                score.ComposeBeat(settings, notes);
            }
            score.Observe(new MazeMusicSnapshot { Active = 6, ForecastBeatsRemaining = 4, Progress = 0.96f });
            score.ComposeBeat(settings, notes);
            Require(score.Root % 12 == (settings.Tonic + 7) % 12, "ending preparation did not reach the dominant");
            score.Complete(SessionOutcome.Solved);
            for (int beat = 0; beat < 9; beat++) score.ComposeBeat(settings, notes);
            Require(score.Finished, "prepared ending did not finish");
            Require(score.Root % 12 == settings.Tonic % 12, "prepared cadence did not resolve to the tonic");

            // No forecast means exactly the old behavior: composition runs until Complete.
            score.Reset(431, settings);
            for (int beat = 0; beat < 64; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = 6 });
                Require(score.ComposeBeat(settings, notes) > 0, "inert planner produced an empty beat");
            }
            Require(!score.Finished, "planner ended a run without a forecast or Complete");
        }
    }
}
