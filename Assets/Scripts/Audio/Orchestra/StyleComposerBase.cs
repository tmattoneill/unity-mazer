using System;

namespace MazeSolver
{
    // Shared machinery for style composers: tonal foundation, dual RNG streams, snapshot
    // accumulation, ending latch and the bounded note buffer. Subclasses hold all
    // style-specific arrangement state. Fixed storage only; no Unity APIs.
    public abstract class StyleComposerBase : IStyleComposer
    {
        static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };

        protected uint performanceRandom, compositionRandom;
        protected int tonic;
        protected Tonality tonality;
        protected int endingBeat = -1;
        protected bool victory;
        protected float activity, coverage;
        protected int births, deaths;
        // Forecast state from the solve sheet; <= 0 means unknown, and composition must
        // then behave exactly as if forecasting did not exist.
        protected float forecastBeats = -1, progress = -1;
        protected int rawActive = 1;

        public int Beat { get; protected set; }
        public int Root { get; protected set; }
        public float Intensity { get; protected set; }
        public bool Finished { get; protected set; }
        public int Tempo { get; protected set; } = 112;
        public int ChordThird { get; protected set; }
        public int ChordFifth { get; protected set; } = 7;
        public abstract string Section { get; }

        public abstract void Reset(int seed, MusicSettings settings);
        public abstract int ComposeBeat(MusicSettings settings, ScoreNote[] buffer);

        protected void SeedRandoms(int seed)
        {
            // Addition is bijective across uint seeds; only xorshift's forbidden zero needs substitution.
            compositionRandom = unchecked((uint)seed + 0x9e3779b9u);
            if (compositionRandom == 0) compositionRandom = 0x6d2b79f5u;
            performanceRandom = unchecked((uint)seed + 0x85ebca6bu);
            if (performanceRandom == 0) performanceRandom = 0xc2b2ae35u;
        }

        protected int Scale(int degree)
        {
            int octave = degree / 7;
            return (tonality == Tonality.Major ? MajorScale : MinorScale)[degree % 7] + 12 * octave;
        }

        protected void SetChord(int degree)
        {
            Root = Place(48 + tonic + Scale(degree), 43, 54);
            ChordThird = tonality == Tonality.Minor && degree == 4 ? 4 : Scale(degree + 2) - Scale(degree);
            ChordFifth = Scale(degree + 4) - Scale(degree);
        }

        public static int Place(int pitch, int minimum, int maximum)
        {
            while (pitch < minimum) pitch += 12;
            while (pitch > maximum) pitch -= 12;
            return pitch;
        }

        public virtual void Observe(MazeMusicSnapshot snapshot)
        {
            activity = Math.Min(1f, (float)Math.Log(1 + Math.Max(0, snapshot.Active), 2) / 5f);
            coverage = Math.Max(0, Math.Min(1, snapshot.Coverage));
            births = Math.Min(64, births + snapshot.Births);
            deaths = Math.Min(64, deaths + snapshot.Deaths);
            rawActive = Math.Max(0, snapshot.Active);
            forecastBeats = snapshot.ForecastBeatsRemaining;
            progress = snapshot.Progress;
        }

        // Ending preparation window: true while a trustworthy forecast says the solve
        // lands within prepBeats. Never true once the actual cadence has begun.
        protected bool PreparingEnding(int prepBeats) =>
            endingBeat < 0 && forecastBeats > 0 && forecastBeats <= prepBeats;

        public void Complete(SessionOutcome outcome)
        {
            if (endingBeat >= 0) return;
            endingBeat = Beat;
            victory = outcome == SessionOutcome.Solved;
        }

        static int Next(ref uint state, int limit)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (int)(state % (uint)limit);
        }
        protected int Next(int limit) => Next(ref performanceRandom, limit);
        protected int ComposeNext(int limit) => Next(ref compositionRandom, limit);

        int count;
        ScoreNote[] output;

        protected void BeginBuffer(ScoreNote[] buffer)
        {
            output = buffer;
            count = 0;
        }
        protected int NoteCount => count;

        protected void Add(Instrument instrument, int pitch, float offset, float length, float velocity, int priority = 2, bool accent = false)
        {
            if (count >= output.Length) return;
            switch (instrument)
            {
                case Instrument.Bass: pitch = Place(pitch, 28, 52); break;
                case Instrument.CelloLong: case Instrument.CelloShort: pitch = Place(pitch, 36, 69); break;
                case Instrument.ViolinLong: case Instrument.ViolinShort: pitch = Place(pitch, 55, 86); break;
                case Instrument.Horn: pitch = Place(pitch, 41, 77); break;
                case Instrument.Trumpet: pitch = Place(pitch, 54, 81); break;
                case Instrument.Flute: pitch = Place(pitch, 60, 86); break;
                case Instrument.Bassoon: pitch = Place(pitch, 34, 72); break;
                case Instrument.Timpani: pitch = Place(pitch, 40, 57); break;
            }
            output[count++] = new ScoreNote
            {
                Instrument = instrument, Pitch = pitch, OffsetBeats = offset,
                DurationBeats = length, Velocity = Math.Max(0.05f, Math.Min(1f, velocity)),
                Alternate = Next(2) + 1, Priority = priority, Accent = accent
            };
        }
    }
}
