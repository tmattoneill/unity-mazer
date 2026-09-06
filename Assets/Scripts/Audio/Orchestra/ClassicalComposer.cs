using System;

namespace MazeSolver
{
    // Clear periodic phrasing (4+4 antecedent/consequent), thematic development,
    // counterpoint with voice-leading checks, functional harmony with prepared cadences
    // and terraced dynamics. Percussion is timpani at cadences only. Fixed storage.
    public sealed class ClassicalComposer : StyleComposerBase
    {
        readonly int[] minorProgressions, majorProgressions, motif;
        readonly float[] sectionEnergy;
        readonly int[] theme = new int[8];
        int previousMelody, previousCounter, previousMelodyInterval = -1;
        int progression, baseProgression, transform;
        float variation;
        // Roles, cumulative: 1 continuo, 2 first-violin theme, 3 counter-melody,
        // 4 inner voices, 5 horns, 6 full tutti weight.
        readonly EnsembleTracker ensemble = new EnsembleTracker(
            new[] { 0f, 2f, 3f, 5f, 9f, 15f },
            new[] { 0f, 1.4f, 2.4f, 3.9f, 6.9f, 11.9f });
        public int Layers => ensemble.Layers;
        public override string Section => endingBeat >= 0 ? "Cadence" : ((Beat / 32) % 2 == 0 ? "Exposition" : "Development");

        public ClassicalComposer(OrchestralScoreRules rules)
        {
            minorProgressions = (int[])rules.MinorProgressions.Clone();
            majorProgressions = (int[])rules.MajorProgressions.Clone();
            motif = (int[])rules.MotifDegrees.Clone();
            sectionEnergy = (float[])rules.SectionEnergy.Clone();
            if (minorProgressions.Length != 32 || majorProgressions.Length != 32 || motif.Length != 8 || sectionEnergy.Length != 4)
                throw new ArgumentException("Classical rules require the shared score-rules shape.");
        }

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
            baseProgression = ComposeNext(4);
            for (int i = 0; i < theme.Length; i++) theme[i] = motif[i] % 7;
            theme[0] = 0;
            progression = baseProgression;
            transform = 0;
            variation = Math.Max(0, Math.Min(1, settings.Variation));
            Beat = 0; endingBeat = -1; Finished = false; Intensity = 0;
            activity = coverage = 0; births = deaths = 0;
            forecastBeats = progress = -1; rawActive = 1;
            ensemble.Reset();
            Root = Place(48 + tonic, 43, 54);
            previousMelody = Root + 24;
            previousCounter = Root + 12;
        }

        // Functional harmony: the consequent's last bar prepares V, and every fourth bar
        // pair lands V so phrases answer each other.
        int Degree(int bar)
        {
            int inPhrase = bar % 4;
            if (inPhrase == 3) return bar % 8 == 3 ? 4 : 0; // antecedent half-cadence on V, consequent home
            var progressions = tonality == Tonality.Major ? majorProgressions : minorProgressions;
            return progressions[progression * 8 + (bar % 8)];
        }

        public override int ComposeBeat(MusicSettings settings, ScoreNote[] buffer)
        {
            BeginBuffer(buffer);
            if (Finished) return 0;
            bool preparing = PreparingEnding(16);
            if (Beat % 4 == 0)
            {
                Tempo = Math.Max(92, Math.Min(132, settings.Tempo));
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
            if (Beat % 32 == 0)
            {
                variation = Math.Max(0, Math.Min(1, settings.Variation));
                progression = baseProgression; transform = 0;
                if (Beat / 32 % 4 != 0 && variation > 0)
                {
                    if (ComposeNext(1000) < variation * 1000) transform = 1 + ComposeNext(2); // inversion or sequence
                    if (variation > 0.5f && ComposeNext(1000) < variation * 1000) progression = ComposeNext(4);
                }
            }
            int bar = Beat / 4, pulse = Beat % 4, section = (bar / 8) % 4;
            SetChord(preparing && forecastBeats <= 8 ? 4 : Degree(bar));
            float target = (0.30f + 0.30f * settings.Energy + 0.20f * activity + 0.08f * coverage) * sectionEnergy[section];
            Intensity += (target - Intensity) * 0.25f;
            // Terraced dynamics: two levels per section rather than a continuous swell.
            float energy = Math.Max(0.25f, Intensity) > 0.55f ? 0.75f : 0.45f;
            if (preparing) energy = 0.75f;
            int layers = ensemble.Tick(Beat, settings.Density);
            bool cadenceBar = bar % 4 == 3;

            // Continuo: cello and bass articulate the harmony on every beat.
            Add(Instrument.CelloLong, Root + (pulse == 0 ? 0 : pulse == 2 ? ChordFifth : ChordThird), 0, 0.9f, 0.34f + energy * 0.2f, 3);
            if (pulse == 0 || pulse == 2)
                Add(Instrument.Bass, Root - 12, 0, 1.8f, 0.4f + energy * 0.22f, 4);

            // Theme in the first violins: scale-degree melody with development transforms.
            if (layers >= 2)
            {
                int index = (bar % 2) * 4 + pulse;
                int tone = theme[index];
                if (transform == 1) tone = (7 - tone) % 7;                  // inversion
                if (transform == 2) tone = (tone + (bar % 2 == 0 ? 0 : 1)) % 7; // sequence
                if (cadenceBar && pulse >= 2) tone = pulse == 2 ? 1 : 0;    // approach the cadence stepwise
                int melody = Root + 12 + Scale(tone);
                while (melody - previousMelody > 7) melody -= 12;
                while (previousMelody - melody > 7) melody += 12;
                melody = Place(melody, 60, 84);
                int melodyInterval = (melody - Root) % 12;
                previousMelody = melody;
                Add(Instrument.ViolinLong, melody, 0, pulse == 3 && cadenceBar ? 1.9f : 0.95f, 0.42f + energy * 0.26f, 5);

                // Counter-melody in contrary motion, refusing parallel perfect intervals.
                if (layers >= 3 && pulse % 2 == 1)
                {
                    int counterTone = (7 - tone + 2) % 7;
                    int counter = Root + Scale(counterTone);
                    while (counter - previousCounter > 6) counter -= 12;
                    while (previousCounter - counter > 6) counter += 12;
                    counter = Place(counter, 48, 76);
                    int counterInterval = (counter - Root) % 12;
                    bool parallelPerfect = (melodyInterval == counterInterval || Math.Abs(melodyInterval - counterInterval) == 7)
                        && previousMelodyInterval == melodyInterval;
                    if (parallelPerfect) counter = Place(counter + (ChordThird > 3 ? 4 : 3), 48, 76);
                    previousCounter = counter;
                    Add(pulse == 1 ? Instrument.Flute : Instrument.Bassoon, counter, 0.05f, 0.9f, 0.32f + energy * 0.18f, 3);
                }
                previousMelodyInterval = melodyInterval;
            }

            // Inner voices complete the harmony on strong beats.
            if (layers >= 4 && (pulse == 0 || pulse == 2))
            {
                Add(Instrument.ViolinShort, Root + 12 + ChordThird, 0.02f, 0.6f, 0.26f + energy * 0.14f, 2);
                Add(Instrument.CelloShort, Root + ChordFifth, 0.02f, 0.6f, 0.28f + energy * 0.14f, 2);
            }

            // Horns sustain the frame at forte passages.
            if (layers >= 5 && pulse == 0 && energy > 0.6f)
            {
                Add(Instrument.Horn, Root, 0.01f, 3.6f, 0.3f + energy * 0.18f, 3);
                Add(Instrument.Horn, Root + ChordFifth, 0.02f, 3.6f, 0.26f + energy * 0.16f, 2);
            }

            // Timpani mark cadences only.
            if (cadenceBar && pulse >= 2 && (layers >= 4 || preparing))
                Add(Instrument.Timpani, Root, 0, 0.9f, 0.4f + energy * 0.25f, 3);

            // Trumpet reinforces tutti climaxes.
            if (layers >= 6 && section == 3 && pulse == 0)
                Add(Instrument.Trumpet, Root + 24, 0.01f, 1.6f, 0.34f + energy * 0.2f, 3);

            if (births > 0) Add(Instrument.Flute, Root + 24 + ChordFifth, 0.5f, 0.4f, 0.4f, 2, true);
            if (deaths > 0) Add(Instrument.Bassoon, Root, 0.75f, 0.5f, 0.36f, 2, true);
            births = deaths = 0;
            Beat++;
            return NoteCount;
        }

        void ComposeEnding(int beat)
        {
            if (beat >= 10) { Finished = true; return; }
            // Cadential 6/4 over the dominant, dominant, then the resolution.
            SetChord(beat < 2 ? 0 : beat < 4 ? 4 : victory ? 0 : 5);
            if (beat < 4 && beat % 2 == 0)
            {
                int dominantRoot = Place(48 + tonic + Scale(4), 43, 54);
                Add(Instrument.Bass, dominantRoot - 12, 0, 2f, 0.6f, 6);
                Add(Instrument.CelloLong, dominantRoot, 0, 2f, 0.55f, 6);
                for (int i = 0; i < 3; i++)
                    Add(Instrument.ViolinLong, Root + 12 + (i == 0 ? 0 : i == 1 ? ChordThird : ChordFifth), i * 0.01f, 2f, 0.5f, 6);
                Add(Instrument.Timpani, dominantRoot, 0, 1.8f, 0.6f, 6);
            }
            else if (beat == 4)
            {
                Add(Instrument.Bass, Root - 12, 0, 5f, victory ? 0.75f : 0.4f, 6);
                Add(Instrument.CelloLong, Root, 0, 5f, victory ? 0.7f : 0.38f, 6);
                for (int i = 0; i < 3; i++)
                {
                    Add(Instrument.ViolinLong, Root + 12 + (i == 0 ? 0 : i == 1 ? ChordThird : ChordFifth), i * 0.01f, 5f, victory ? 0.68f : 0.36f, 6);
                    Add(Instrument.Horn, Root + (i == 0 ? 0 : i == 1 ? ChordThird : ChordFifth), i * 0.008f, 5f, victory ? 0.6f : 0.32f, 6);
                }
                Add(Instrument.Timpani, Root, 0, 4f, victory ? 0.8f : 0.45f, 6);
                if (victory) Add(Instrument.Trumpet, Root + 24, 0.01f, 3f, 0.7f, 6);
            }
        }
    }
}
