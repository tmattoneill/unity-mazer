using System;

namespace MazeSolver
{
    // Maps the live active-path count onto a bounded number of ensemble layers.
    // Observe() runs per snapshot (solver-step cadence, may drop under load — the last
    // value holds); Tick() runs once per beat so smoothing is tempo-relative and layer
    // changes land only on bar or phrase boundaries. Enter thresholds sit strictly above
    // exit thresholds, so populations flickering near a boundary cannot flap a layer.
    public sealed class EnsembleTracker
    {
        readonly float[] enterAt;
        readonly float[] exitBelow;
        float smoothed = 1;
        int lastActive = 1;
        int currentLayers = 1;

        public EnsembleTracker(float[] enterAt, float[] exitBelow)
        {
            if (enterAt.Length != exitBelow.Length || enterAt.Length < 1)
                throw new ArgumentException("Layer tables need matching enter/exit thresholds.");
            for (int i = 1; i < enterAt.Length; i++)
                if (exitBelow[i] >= enterAt[i]) throw new ArgumentException("Exit thresholds must sit below enter thresholds.");
            this.enterAt = (float[])enterAt.Clone();
            this.exitBelow = (float[])exitBelow.Clone();
        }

        public int Layers => currentLayers;
        public int MaxLayers => enterAt.Length;

        public void Reset()
        {
            smoothed = 1;
            lastActive = 1;
            currentLayers = 1;
        }

        public void Observe(int active) => lastActive = Math.Max(0, active);

        public int Tick(int beat, float density, int barBeats = 4, int phraseBeats = 32)
        {
            smoothed += (Math.Max(1, lastActive) - smoothed) * 0.15f;

            int target = currentLayers;
            while (target < enterAt.Length && smoothed >= enterAt[target]) target++;
            while (target > 1 && smoothed < exitBelow[target - 1]) target--;

            // Density is the ceiling on ensemble size, per the roadmap.
            int ceiling = 1 + (int)Math.Round(density * (enterAt.Length - 1));
            if (target > ceiling) target = ceiling;
            if (target < 1) target = 1;

            bool barBoundary = beat % barBeats == 0;
            bool phraseBoundary = beat % phraseBeats == 0;
            if (target > currentLayers && barBoundary)
                currentLayers++;
            else if (target < currentLayers && (phraseBoundary || (barBoundary && currentLayers - target >= 2)))
                currentLayers--;
            return currentLayers;
        }
    }
}
