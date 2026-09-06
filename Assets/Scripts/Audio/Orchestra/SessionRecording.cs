using System;
using System.IO;
using System.Threading;

namespace MazeSolver
{
    public enum RecordingState { Capturing, Finalising, Ready, Cancelled, Failed }

    public sealed class SessionRecording
    {
        readonly float[] ring;
        readonly int sampleRate;
        readonly Thread worker;
        long read, write;
        int state, inputComplete, cancelled, overflow, forcedTail;
        int tailFrames, silentFrames;
        string error;
        public readonly string TemporaryPath, FileStem;
        public RecordingState State => (RecordingState)Volatile.Read(ref state);
        public string Error => Volatile.Read(ref error);
        public bool WorkerFinished => !worker.IsAlive;

        public SessionRecording(string directory, int rate, int seed, MusicSettings settings, int bufferSeconds = 4)
        {
            sampleRate = rate;
            ring = new float[rate * 2 * bufferSeconds];
            Directory.CreateDirectory(directory);
            TemporaryPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wav");
            FileStem = "Orchestra-" + settings.Style + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + MusicalKey.Name(settings).Replace(' ', '-') + "-seed-" + seed;
            worker = new Thread(WriteRecording) { IsBackground = true, Name = "Orchestra WAV writer" };
            worker.Start();
        }

        // Only the audio thread produces PCM; cancellation is an independent main-thread signal.
        public void Capture(float[] data, int channels, bool scoreFinished, bool paused)
        {
            if (forcedTail != 0) { Array.Clear(data, 0, data.Length); return; }
            if (Volatile.Read(ref cancelled) != 0 || Volatile.Read(ref inputComplete) != 0 || State == RecordingState.Failed) return;
            int frames = data.Length / channels;
            long position = write;
            if (position - Volatile.Read(ref read) + frames * 2 > ring.Length)
            {
                Volatile.Write(ref overflow, 1);
                Volatile.Write(ref inputComplete, 1);
                return;
            }
            bool complete = false;
            for (int frame = 0; frame < frames; frame++)
            {
                int index = frame * channels;
                float left = data[index], right = data[index + (channels > 1 ? 1 : 0)];
                if (scoreFinished && !paused)
                {
                    tailFrames++;
                    int remaining = sampleRate * 10 - tailFrames;
                    if (remaining < sampleRate / 50)
                    {
                        float gain = Math.Max(0, remaining / (sampleRate / 50f));
                        left *= gain; right *= gain;
                        data[index] = left;
                        if (channels > 1) data[index + 1] = right;
                    }
                    silentFrames = Math.Max(Math.Abs(left), Math.Abs(right)) < 0.0001f ? silentFrames + 1 : 0;
                    complete = silentFrames >= sampleRate || remaining <= 0;
                    if (remaining <= 0) forcedTail = 1;
                }
                ring[(int)(position++ % ring.Length)] = left;
                ring[(int)(position++ % ring.Length)] = right;
                if (complete)
                {
                    if (forcedTail != 0) Array.Clear(data, index + channels, data.Length - index - channels);
                    break;
                }
            }
            Volatile.Write(ref write, position);
            if (complete) Volatile.Write(ref inputComplete, 1);
        }

        public void Cancel() => Volatile.Write(ref cancelled, 1);
        public bool WaitForWriter(int milliseconds) => worker.Join(milliseconds);

        void WriteRecording()
        {
            try
            {
                using (var writer = new PcmWaveWriter(TemporaryPath, sampleRate))
                {
                    var block = new float[4096];
                    while (true)
                    {
                        if (Volatile.Read(ref cancelled) != 0) break;
                        // Observe completion before the write position, so the final block cannot be lost.
                        bool complete = Volatile.Read(ref inputComplete) != 0;
                        if (Volatile.Read(ref overflow) != 0) throw new IOException("Recording buffer filled. The recording was discarded; playback continues.");
                        long available = Volatile.Read(ref write) - read;
                        int count = (int)Math.Min(block.Length, available);
                        if (count > 0)
                        {
                            for (int i = 0; i < count; i++) block[i] = ring[(int)((read + i) % ring.Length)];
                            writer.Write(block, count);
                            Volatile.Write(ref read, read + count);
                        }
                        else if (complete) break;
                        else Thread.Sleep(5);
                        if (complete) Volatile.Write(ref state, (int)RecordingState.Finalising);
                    }
                }
                if (Volatile.Read(ref cancelled) != 0)
                {
                    File.Delete(TemporaryPath);
                    Volatile.Write(ref state, (int)RecordingState.Cancelled);
                }
                else Volatile.Write(ref state, (int)RecordingState.Ready);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref error, exception.Message);
                try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); }
                catch (Exception cleanupError) { Volatile.Write(ref error, exception.Message + " Temporary file cleanup: " + cleanupError.Message); }
                Volatile.Write(ref state, (int)RecordingState.Failed);
            }
        }
    }
}
