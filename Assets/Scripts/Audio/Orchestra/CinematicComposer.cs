using System;

namespace MazeSolver
{
    // The original UnityMazer sound: repeating string patterns, orchestral layers, brass
    // themes and beat-anchored percussion. It retains ProceduralScore's seeded lineage;
    // RNG call order is part of that contract, so do not reorder calls.
    public sealed class CinematicComposer : StyleComposerBase
    {
        readonly int[] minorProgressions, majorProgressions, motif;
        readonly int[] theme = new int[8];
        readonly float[] sectionEnergy;
        readonly float[] previousVoicing = { 62, 65, 69 };
        int previousMelody = 62;
        int progression, baseProgression, basePattern, pattern, baseLead, lead, transform, displacement;
        float variation, pressure;
        // Layer roles, cumulative: 1 pad + ostinato core, 2 lead melody, 3 inner strings,
        // 4 percussion core, 5 backbeat + counter-lines, 6 brass double, 7 high
        // subdivisions + fills. Layer 1 always sounds, so no beat is ever empty.
        readonly EnsembleTracker ensemble = new EnsembleTracker(
            new[] { 0f, 2f, 3f, 5f, 8f, 13f, 21f },
            new[] { 0f, 1.4f, 2.4f, 3.9f, 6.4f, 10.4f, 16.9f });
        public int Layers => ensemble.Layers;
        public float AppliedVariation => variation;
        public int ThemeTransform => transform;
        public override string Section => endingBeat >= 0 ? "Cadence" : ((Beat / 32) % 4 == 2 ? "Contrast" : "Adventure");

        public CinematicComposer(OrchestralScoreRules rules)
        {
            minorProgressions = (int[])rules.MinorProgressions.Clone();
            majorProgressions = (int[])rules.MajorProgressions.Clone();
            motif = (int[])rules.MotifDegrees.Clone();
            sectionEnergy = (float[])rules.SectionEnergy.Clone();
            if (minorProgressions.Length != 32 || majorProgressions.Length != 32 || motif.Length != 8 || sectionEnergy.Length != 4)
                throw new ArgumentException("Score rules require four eight-bar progressions per mode, eight motif notes and four section energies.");
            foreach (int degree in minorProgressions) ValidateDegree(degree);
            foreach (int degree in majorProgressions) ValidateDegree(degree);
            foreach (int degree in motif) ValidateDegree(degree);
        }

        static void ValidateDegree(int degree)
        {
            if (degree < 0 || degree > 6) throw new ArgumentException("Scale degrees must be between zero and six.");
        }

        public void Reset(int seed) => Reset(seed, MusicSettings.Default);
        public override void Observe(MazeMusicSnapshot snapshot)
        {
            base.Observe(snapshot);
            ensemble.Observe(snapshot.Active);
        }

        public override void Reset(int seed, MusicSettings settings)
        {
            SeedRandoms(seed);
            tonic = Math.Max(0, Math.Min(11, settings.Tonic));
            tonality = settings.Tonality;
            baseProgression = ComposeNext(4); basePattern = ComposeNext(3); baseLead = ComposeNext(3);
            for (int i = 0; i < theme.Length; i++)
                theme[i] = (motif[i] / 2 + ComposeNext(3)) % 3;
            theme[0] = 0; theme[6] = 2;
            progression = baseProgression; pattern = basePattern; lead = baseLead;
            transform = displacement = 0;
            variation = Math.Max(0, Math.Min(1, settings.Variation));
            Beat = 0; endingBeat = -1; Finished = false; Intensity = 0;
            activity = coverage = pressure = 0; births = deaths = 0;
            Root = Place(48 + tonic, 43, 54);
            ensemble.Reset();
            forecastBeats = progress = -1;
            rawActive = 1;
            previousMelody = Root + 12;
            previousVoicing[0] = Root + 12;
            previousVoicing[1] = Root + 12 + (tonality == Tonality.Major ? 4 : 3);
            previousVoicing[2] = Root + 19;
        }

        void BeginPhrase(MusicSettings settings)
        {
            variation = Math.Max(0, Math.Min(1, settings.Variation));
            progression = baseProgression; pattern = basePattern; lead = baseLead;
            transform = displacement = 0;
            // The opening material returns every fourth phrase, even at maximum variation.
            if (Beat / 32 % 4 == 0 || variation == 0) return;
            if (ComposeNext(1000) < variation * 1000) transform = 1 + ComposeNext(3);
            if (ComposeNext(1000) < variation * 1000) pattern = ComposeNext(3);
            if (ComposeNext(1000) < variation * 1000) lead = ComposeNext(3);
            if (variation > 0.5f && ComposeNext(1000) < variation * 1000) progression = ComposeNext(4);
            if (variation > 0.65f && ComposeNext(1000) < variation * 700) displacement = 1;
        }

        public override int ComposeBeat(MusicSettings settings, ScoreNote[] buffer)
        {
            BeginBuffer(buffer);
            if (Finished) return 0;
            bool preparing = PreparingEnding(16);
            if (Beat % 4 == 0)
            {
                Tempo = Math.Max(92, Math.Min(132, settings.Tempo));
                // Nudge tempo within +-6% so a bar boundary coincides with the forecast
                // solve, letting the cadence land as a resolution instead of a cut.
                if (preparing && forecastBeats >= 4)
                {
                    float desired = Math.Max(4, (float)Math.Round(forecastBeats / 4) * 4);
                    float factor = Math.Max(0.94f, Math.Min(1.06f, forecastBeats / desired));
                    Tempo = Math.Max(92, Math.Min(132, (int)Math.Round(Tempo * factor)));
                }
            }
            if (endingBeat >= 0)
            {
                ComposeEnding(Beat - endingBeat);
                Beat++;
                return NoteCount;
            }
            if (Beat % 32 == 0) BeginPhrase(settings);
            int bar = Beat / 4, pulse = Beat % 4, section = (bar / 8) % 4;
            bool contrast = section == 2;
            var progressions = tonality == Tonality.Major ? majorProgressions : minorProgressions;
            SetChord(progressions[progression * 8 + bar % 8]);
            // Dominant preparation in the final approach, so Complete resolves it.
            if (preparing && forecastBeats <= 8) SetChord(4);
            int third = ChordThird;
            pressure += ((deaths / (float)Math.Max(1, births + deaths)) - pressure) * 0.15f;
            float target = (0.28f + 0.32f * settings.Energy + 0.23f * activity + 0.10f * pressure + 0.07f * coverage) * sectionEnergy[section];
            Intensity += (target - Intensity) * 0.25f;
            float energy = Math.Max(0.25f, Intensity);
            if (preparing) energy = Math.Max(energy, 0.55f);
            float density = settings.Density;
            int layers = ensemble.Tick(Beat, density);
            float phrase = 0.85f + (bar % 4) * 0.045f;

            if (pulse == 0)
            {
                Add(Instrument.Bass, Root - 12, 0, 3.6f, 0.62f + energy * 0.2f, 4);
                Add(Instrument.CelloLong, Root, 0.012f, 3.5f, 0.36f + energy * 0.25f);
                for (int voice = 0; voice < 3; voice++)
                {
                    int pitch = Root + (voice == 0 ? 0 : voice == 1 ? third : ChordFifth) + 12;
                    while (pitch - previousVoicing[voice] > 6) pitch -= 12;
                    while (previousVoicing[voice] - pitch > 6) pitch += 12;
                    pitch = Place(pitch, 60, 81);
                    previousVoicing[voice] = pitch;
                    if (layers >= 3 || voice == 1)
                        Add(Instrument.ViolinLong, pitch, 0.018f + voice * 0.008f, 3.7f, 0.23f + energy * 0.16f, 1);
                }
            }

            if (!contrast || pulse % 2 == 0)
            {
                Add(Instrument.CelloShort, Root + (pulse == 2 ? ChordFifth : 0), 0, 0.35f, (0.55f + energy * 0.25f) * phrase, 3);
                int subdivisions = layers >= 7 && energy > 0.6f ? 4 : 2;
                for (int n = 0; n < subdivisions; n++)
                {
                    if (pulse == 3 && n == subdivisions - 1 && bar % 4 == 3) continue;
                    int chordIndex = (pulse * 2 + n + (bar / 4) % 2) % 3;
                    if (pattern == 1) chordIndex = 2 - chordIndex;
                    if (pattern == 2) chordIndex = (chordIndex + pulse % 2) % 3;
                    int pitch = Root + 12 + (chordIndex == 0 ? 0 : chordIndex == 1 ? third : ChordFifth);
                    if (layers < 3 && n > 0) continue;
                    Add(Instrument.ViolinShort, pitch, n / (float)subdivisions + Next(4) * 0.003f,
                        0.23f, (n == 0 ? 0.62f : 0.40f) * phrase + energy * 0.15f, 2);
                }
            }

            int themeIndex = Beat % 8;
            if (transform == 3) themeIndex = (themeIndex + 2) % 8;
            int tone = theme[themeIndex];
            if (transform == 1) tone = 2 - tone;
            if (transform == 2) tone = (tone + bar % 2) % 3;
            int melody = Root + 12 + (tone == 0 ? 0 : tone == 1 ? third : ChordFifth);
            if (layers >= 2 && pulse % 2 == displacement)
            {
                while (melody - previousMelody > 7) melody -= 12;
                while (previousMelody - melody > 7) melody += 12;
                melody = Place(melody, contrast ? 60 : 48, 81);
                previousMelody = melody;
                Instrument instrument = contrast ? Instrument.Flute : lead == 0 ? Instrument.Horn : lead == 1 ? Instrument.Flute : Instrument.ViolinLong;
                Add(instrument, melody, 0.012f, pulse >= 2 ? 1.7f : 1.35f, 0.55f + energy * 0.26f, 5);
                if (layers >= 6 && section == 3 && energy > 0.55f && pulse == displacement)
                    Add(Instrument.Trumpet, melody, 0.006f, 0.75f, 0.55f + energy * 0.2f, 4);
            }
            // Contrast sections keep their counter-line texture at any layer count; outside
            // them the counter-lines are the layer-5 role and drop out while preparing.
            else if ((contrast || (layers >= 5 && !preparing)) && (bar % 4 >= 2 || contrast))
            {
                Add(Instrument.Flute, Root + 24 + (pulse == 1 ? third : 0), 0.5f, 0.43f, 0.45f, 2);
                Add(Instrument.Bassoon, Root + (pulse == 1 ? ChordFifth : 0), 0, 0.4f, 0.46f, 2);
            }

            if (layers >= 4 && (pulse == 0 || (pulse == 2 && !contrast)))
            {
                Add(Instrument.Timpani, Root, 0, 1.7f, 0.58f + energy * 0.25f, 4);
                Add(Instrument.BassDrum, 36, 0, 1.0f, pulse == 0 ? 0.7f : 0.48f, 4);
            }
            if (layers >= 5 && !contrast && energy > 0.4f && pulse % 2 == 1)
                Add(Instrument.Snare, 60, 0, 0.35f, 0.38f + energy * 0.27f, 3);
            if (layers >= 4 && bar % 8 == 0 && pulse == 0 && bar > 0)
                Add(Instrument.Cymbal, 60, 0, 4f, 0.64f, 3);
            if (layers >= 7 && !preparing && bar % 4 == 3 && pulse == 3)
                for (int n = 1; n < 4; n++) Add(Instrument.Snare, 60, n * 0.25f, 0.2f, 0.3f + n * 0.12f, 2);

            if (births > 0) Add(Instrument.ViolinShort, Root + 12 + ChordFifth, 0.5f, 0.3f, 0.5f, 2, true);
            if (deaths > 0) Add(Instrument.Bassoon, Root, 0.75f, 0.35f, 0.43f, 2, true);
            births = deaths = 0;
            Beat++;
            return NoteCount;
        }

        void ComposeEnding(int beat)
        {
            if (beat >= 8) { Finished = true; return; }
            SetChord(beat < 2 ? (victory ? 4 : 3) : 0);
            if (beat == 0 || beat == 2)
            {
                int third = ChordThird;
                float duration = beat == 0 ? 1.8f : 4f;
                Add(Instrument.Bass, Root - 12, 0, duration, 0.8f, 6);
                Add(Instrument.CelloLong, Root, 0, duration, 0.7f, 6);
                for (int i = 0; i < 3; i++)
                {
                    int pitch = Root + 12 + (i == 0 ? 0 : i == 1 ? third : ChordFifth);
                    Add(Instrument.ViolinLong, pitch, i * 0.01f, duration, victory ? 0.75f : 0.4f, 6);
                    Add(Instrument.Horn, pitch - 12, i * 0.008f, duration, victory ? 0.8f : 0.4f, 6);
                }
                Add(Instrument.Timpani, Root, 0, 3f, 0.85f, 6);
                if (victory && beat == 2)
                {
                    Add(Instrument.Cymbal, 60, 0, 5f, 0.75f, 6);
                    Add(Instrument.BassDrum, 36, 0, 2f, 0.9f, 6);
                    Add(Instrument.Trumpet, Root + 24, 0, 1.4f, 0.85f, 6);
                }
            }
        }
    }
}
