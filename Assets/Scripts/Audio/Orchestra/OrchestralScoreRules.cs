using UnityEngine;

namespace MazeSolver
{
    [CreateAssetMenu(menuName = "Maze/Orchestral score rules")]
    public sealed class OrchestralScoreRules : ScriptableObject
    {
        // Four eight-bar progressions, expressed as zero-based scale degrees.
        public int[] MinorProgressions = {
            0, 5, 2, 4, 0, 3, 5, 4,
            0, 3, 6, 2, 5, 3, 4, 4,
            0, 6, 5, 4, 3, 0, 5, 4,
            2, 6, 0, 5, 2, 3, 5, 4 };
        public int[] MajorProgressions = {
            0, 3, 5, 4, 0, 3, 1, 4,
            0, 5, 3, 4, 2, 5, 1, 4,
            0, 4, 5, 3, 0, 2, 3, 4,
            5, 3, 0, 4, 5, 2, 3, 4 };
        public int[] MotifDegrees = { 0, 2, 4, 3, 2, 1, 4, 0 };
        public float[] SectionEnergy = { 0.58f, 0.95f, 0.48f, 1.0f };
    }

}
