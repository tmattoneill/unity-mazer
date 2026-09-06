using System;

namespace MazeSolver
{
    // Main-thread forecaster: pairs the presolved solve sheet with observed step timing
    // to estimate when the run will end. The requested per-step delay is only a floor
    // (frame clamping, per-step render cost and timeScale all stretch it), so the
    // estimate comes from measured deltas, never from the speed slider alone.
    public sealed class SolveConductor
    {
        SolveSheet sheet;
        double lastStepTime = -1;
        float emaStepSeconds;
        int fastAdaptSteps;
        bool divergent;
        int lastStep;

        public bool HasSheet => sheet != null && !divergent;
        public int TerminalStep => sheet?.TerminalStep ?? -1;

        public void SetSheet(SolveSheet value)
        {
            sheet = value;
            divergent = false;
            lastStepTime = -1;
            emaStepSeconds = 0;
            fastAdaptSteps = 0;
            lastStep = 0;
        }

        public void MarkDivergent() => divergent = true;

        // Called when the pause state flips; a paused gap must not enter the estimate.
        public void OnPauseChanged() => lastStepTime = -1;

        // A speed-slider change invalidates history: adopt the new requested delay as a
        // prior and re-learn quickly for a few steps.
        public void OnRequestedDelayChanged(float requestedSeconds)
        {
            emaStepSeconds = Math.Max(emaStepSeconds > 0 ? 0 : 0, requestedSeconds);
            fastAdaptSteps = 8;
        }

        public void OnStep(int liveStep, double now)
        {
            lastStep = liveStep;
            if (lastStepTime >= 0)
            {
                float delta = (float)(now - lastStepTime);
                if (delta > 0 && delta < 5f)
                {
                    float rate = fastAdaptSteps > 0 ? 0.5f : 0.08f;
                    if (fastAdaptSteps > 0) fastAdaptSteps--;
                    emaStepSeconds = emaStepSeconds <= 0 ? delta : emaStepSeconds + (delta - emaStepSeconds) * rate;
                }
            }
            lastStepTime = now;
        }

        public float ObservedStepSeconds => emaStepSeconds;

        public float RemainingSeconds
        {
            get
            {
                if (!HasSheet || emaStepSeconds <= 0) return -1;
                int remaining = sheet.TerminalStep - lastStep;
                if (remaining < 0) return -1;
                return remaining * emaStepSeconds;
            }
        }

        public float ForecastBeatsRemaining(int tempo)
        {
            float seconds = RemainingSeconds;
            if (seconds < 0 || tempo <= 0) return -1;
            return seconds * tempo / 60f;
        }

        public float Progress => HasSheet && sheet.TerminalStep > 0
            ? Math.Min(1f, lastStep / (float)sheet.TerminalStep)
            : -1;
    }
}
