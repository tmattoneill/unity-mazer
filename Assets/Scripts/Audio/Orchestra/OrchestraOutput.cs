using UnityEngine;

namespace MazeSolver
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class OrchestraOutput : MonoBehaviour
    {
        OrchestraRenderer renderer;
        public OrchestraRenderer Renderer
        {
            get => System.Threading.Volatile.Read(ref renderer);
            set => System.Threading.Volatile.Write(ref renderer, value);
        }
        void OnAudioFilterRead(float[] data, int channels)
        {
            var current = Renderer;
            if (current != null) current.Render(data, channels);
            else System.Array.Clear(data, 0, data.Length);
        }
    }
}
