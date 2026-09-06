using System;
using UnityEngine;

namespace MazeSolver
{
    public enum MusicMode { Orchestra, Soundtrack }
    public enum Instrument { ViolinShort, ViolinLong, CelloShort, CelloLong, Bass, Horn, Trumpet, Flute, Bassoon, Timpani, BassDrum, Snare, Cymbal }
    public enum SessionOutcome { Solved, Exhausted }
    public enum Tonality { Minor, Major }

    [Serializable]
    public struct MusicSettings
    {
        public MusicMode Mode;
        public float Volume, AccentVolume, Energy, Density, Hall, Variation;
        public int Tempo, Tonic;
        public Tonality Tonality;
        public bool MusicMuted, AccentsMuted;
        public static MusicSettings Default => new MusicSettings
        {
            Volume = 0.65f, AccentVolume = 0.6f, Energy = 0.6f,
            Density = 0.65f, Hall = 0.28f, Tempo = 112, Tonic = 2, Variation = 0.35f
        };
    }

    public static class MusicalKey
    {
        public static readonly string[] Names = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
        public static string Name(MusicSettings settings) => Names[settings.Tonic] + " " + settings.Tonality.ToString().ToLowerInvariant();
    }

    public struct MazeMusicSnapshot
    {
        public int Active, Births, Deaths;
        public float Coverage;
    }

    public struct ScoreNote
    {
        public Instrument Instrument;
        public int Pitch, Alternate, Priority;
        public float Velocity, OffsetBeats, DurationBeats;
        public bool Accent;
    }

    // Plain immutable PCM is loaded before the audio thread starts.
    public sealed class InstrumentSample
    {
        public Instrument Instrument;
        public int Root, Layer, Alternate, Channels, Rate, Frames;
        public float[] Pcm;
        public float Gain, TuningCents;
        public int LoopStart, LoopEnd;
    }

    public sealed class InstrumentBankData
    {
        public InstrumentSample[] Samples;
        public long MemoryBytes;
    }

}
