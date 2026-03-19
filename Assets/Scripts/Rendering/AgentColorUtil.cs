using UnityEngine;

namespace MazeSolver
{
    public static class AgentColorUtil
    {
        const float GOLDEN_ANGLE = 137.508f;

        public static Color32 GetHeadColor(int agentId)
        {
            float h = (agentId * GOLDEN_ANGLE) % 360f / 360f;
            Color c = Color.HSVToRGB(h, 0.95f, 0.80f);
            return new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
        }

        public static Color32 GetTailColor(int agentId, float alpha)
        {
            float h = (agentId * GOLDEN_ANGLE) % 360f / 360f;
            Color c = Color.HSVToRGB(h, 0.70f, 0.45f);
            return new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(alpha * 255));
        }

        public static float AgentIdToFreq(int agentId)
        {
            // Pentatonic scale: C D E G A across 2 octaves (10 notes)
            float[] scale = {
                130.81f, 146.83f, 164.81f, 196.00f, 220.00f,
                261.63f, 293.66f, 329.63f, 392.00f, 440.00f
            };
            int idx = Mathf.FloorToInt((agentId * GOLDEN_ANGLE) % 360f / 360f * scale.Length) % scale.Length;
            return scale[idx];
        }
    }
}
