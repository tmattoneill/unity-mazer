using System;
using System.IO;
using System.Text;

namespace MazeSolver
{
    // Used by the background recorder and offline demos; never called from the audio callback.
    public sealed class PcmWaveWriter : IDisposable
    {
        readonly BinaryWriter writer;
        readonly int rate;
        readonly byte[] bytes = new byte[12288];
        long sampleCount;
        public PcmWaveWriter(string path, int sampleRate, bool replace = false)
        {
            rate = sampleRate;
            writer = new BinaryWriter(new FileStream(path, replace ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            Header(0);
        }
        void Header(uint length)
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(length + 36);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 6);
            writer.Write((short)6); writer.Write((short)24);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(length);
        }
        public void Write(float[] samples, int count)
        {
            if (sampleCount + count > (uint.MaxValue - 36L) / 3)
                throw new IOException("Recording exceeds the WAV size limit.");
            for (int offset = 0; offset < count;)
            {
                int length = Math.Min(bytes.Length / 3, count - offset);
                for (int i = 0; i < length; i++)
                {
                    float sample = samples[offset + i];
                    if (float.IsNaN(sample) || float.IsInfinity(sample)) throw new IOException("Recording contains invalid audio.");
                    int value = (int)Math.Round(Math.Max(-1, Math.Min(1, sample)) * 8388607);
                    bytes[i * 3] = (byte)value; bytes[i * 3 + 1] = (byte)(value >> 8); bytes[i * 3 + 2] = (byte)(value >> 16);
                }
                writer.Write(bytes, 0, length * 3);
                offset += length;
            }
            sampleCount += count;
        }
        public void Dispose()
        {
            try { writer.BaseStream.Position = 0; Header((uint)(sampleCount * 3)); }
            finally { writer.Dispose(); }
        }
    }
}
