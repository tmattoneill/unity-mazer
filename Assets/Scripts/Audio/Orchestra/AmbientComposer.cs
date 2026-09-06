using System;

namespace MazeSolver
{
    // Slowly changing harmony, sustained textures, sparse motifs, long releases and gentle
    // dynamics. Passages may pass without a strong beat; more live paths add depth and
    // colour rather than percussion. Fixed storage; audio-thread safe.
    public sealed class AmbientComposer : StyleComposerBase
    {
        readonly int[] minorProgressions, majorProgressions, motif;
        readonly float[] sectionEnergy;
        readonly float[] previousVoicing = { 62, 65, 69 };
        int progression, baseProgression, colour;
        float variation;
        // Colour roles, cumulative: 1 pedal + low pad, 2 upper pad voices, 3 flute motif
        // fragments, 4 horn swells, 5 high shimmer + timpani swell.
        readonly EnsembleTracker ensemble = new EnsembleTracker(
            new[] { 0f, 2f, 3f, 6f, 12f },
            new[] { 0f, 1.4f, 2.4f, 4.9f, 9.4f });
        public int Layers => ensemble.Layers;
        public override string Section => endingBeat >= 0 ? "Resolution" : ((Beat / 32) % 4 == 2 ? "Stillness" : "Drift");

        public AmbientComposer(OrchestralScoreRules rules)
        {
            minorProgressions = (int[])rules.MinorProgressions.Clone();
            majorProgressions = (int[])rules.MajorProgressions.Clone();
            motif = (int[])rules.MotifDegrees.Clone();
            sectionEnergy = (float[])rules.SectionEnergy.Clone();
            if (minorProgressions.Length != 32 || majorProgressions.Length != 32 || motif.Length != 8 || sectionEnergy.Length != 4)
                throw new ArgumentException("Ambient rules require the shared score-rules shape.");
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
            colour = ComposeNext(3);
            progression = baseProgression;
            variation = Math.Max(0, Math.Min(1, settings.Variation));
            Beat = 0; endingBeat = -1; Finished = false; Intensity = 0;
            activity = coverage = 0; births = deaths = 0;
            forecastBeats = progress = -1; rawActive = 1;
            ensemble.Reset();
            Root = Place(48 + tonic, 43, 54);
            previousVoicing[0] = Root + 12;
            previousVoicing[1] = Root + 12 + (tonality == Tonality.Major ? 4 : 3);
            previousVoicing[2] = Root + 19;
        }

        public override int ComposeBeat(MusicSettings settings, ScoreNote[] buffer)
        {
            BeginBuffer(buffer);
            if (Finished) return 0;
            bool preparing = PreparingEnding(24);
            // Ambient breathes slower than the dial suggests.
            if (Beat % 4 == 0) Tempo = Math.Max(92, Math.Min(132, settings.Tempo) - 20);
            if (endingBeat >= 0)
            {
                ComposeEnding(Beat - endingBeat);
                Beat++;
                return NoteCount;
            }
            if (Beat % 32 == 0)
            {
                variation = Math.Max(0, Math.Min(1, settings.Variation));
                progression = baseProgression;
                if (Beat / 32 % 4 != 0 && variation > 0 && ComposeNext(1000) < variation * 1000) progression = ComposeNext(4);
                if (variation > 0 && ComposeNext(1000) < variation * 700) colour = ComposeNext(3);
            }
            int bar = Beat / 4, section = (bar / 8) % 4;
            // Harmony moves every two bars: half the progression row per phrase.
            var progressions = tonality == Tonality.Major ? majorProgressions : minorProgressions;
            SetChord(progressions[progression * 8 + (bar / 2) % 8]);
            float target = (0.20f + 0.28f * settings.Energy + 0.20f * activity + 0.08f * coverage) * sectionEnergy[section];
            Intensity += (target - Intensity) * 0.08f;
            float energy = Math.Max(0.15f, Intensity);
            if (preparing) energy = Math.Max(energy, 0.4f);
            int layers = ensemble.Tick(Beat, settings.Density, 4, 16);

            // Pedal and low pad renew every two bars; everything overlaps its successor.
            if (Beat % 8 == 0)
            {
                Add(Instrument.Bass, Root - 12, 0, 8.5f, 0.4f + energy * 0.2f, 4, releaseBeats: 2f);
                Add(Instrument.CelloLong, Root, 0.03f, 8.2f, 0.3f + energy * 0.2f, 3, releaseBeats: 2.5f);
                if (layers >= 2)
                    for (int voice = 0; voice < 3; voice++)
                    {
                        int pitch = Root + (voice == 0 ? 0 : voice == 1 ? ChordThird : ChordFifth) + 12;
                        while (pitch - previousVoicing[voice] > 5) pitch -= 12;
                        while (previousVoicing[voice] - pitch > 5) pitch += 12;
                        pitch = Place(pitch, 58, 80);
                        previousVoicing[voice] = pitch;
                        Add(Instrument.ViolinLong, pitch, 0.2f + voice * 0.26f, 7.5f, 0.16f + energy * 0.14f, 2, releaseBeats: 3f);
                    }
            }

            // Sparse motif fragments drift off the grid; more paths, more often.
            if (layers >= 3 && Next(8) < 1 + Math.Min(3, rawActive / 4))
            {
                int tone = motif[(Beat / 2 + colour) % 8] % 5;
                int pitch = Root + 12 + Scale(tone);
                Add(Instrument.Flute, pitch, Next(16) / 16f, 2.6f + Next(3), 0.22f + energy * 0.18f, 2);
                if (Next(3) == 0)
                    Add(Instrument.ViolinLong, pitch + 12, Next(16) / 16f, 3.5f, 0.12f + energy * 0.1f, 1);
            }

            // Horn swell once per two bars at depth, timed mid-bar.
            if (layers >= 4 && Beat % 8 == 4)
                Add(Instrument.Horn, Root + (Next(2) == 0 ? 0 : ChordFifth), 0.3f, 4.5f, 0.2f + energy * 0.22f, 3, releaseBeats: 2f);

            // Shimmer and a soft timpani swell only for crowded mazes.
            if (layers >= 5)
            {
                if (Beat % 2 == 0)
                    Add(Instrument.ViolinShort, Root + 24 + (Next(2) == 0 ? 0 : ChordThird), Next(8) / 8f, 1.4f, 0.10f + energy * 0.08f, 1);
                if (Beat % 16 == 8)
                    Add(Instrument.Timpani, Root, 0.2f, 3.5f, 0.18f + energy * 0.15f, 2);
            }

            // Births glint, deaths sigh; both stay gentle.
            if (births > 0) Add(Instrument.Flute, Root + 24 + ChordFifth, 0.6f, 1.2f, 0.3f, 2, true);
            if (deaths > 0) Add(Instrument.Bassoon, Root - 12 + ChordThird, 0.75f, 1.6f, 0.28f, 2, true);
            births = deaths = 0;
            Beat++;
            return NoteCount;
        }

        void ComposeEnding(int beat)
        {
            if (beat >= 12) { Finished = true; return; }
            SetChord(beat < 4 ? 3 : 0); // plagal colour, then home
            if (beat == 0 || beat == 4)
            {
                float strength = victory ? 0.5f : 0.32f;
                Add(Instrument.Bass, Root - 12, 0, 8f, strength, 6);
                Add(Instrument.CelloLong, Root, 0.05f, 8f, strength, 6);
                for (int i = 0; i < 3; i++)
                    Add(Instrument.ViolinLong, Root + 12 + (i == 0 ? 0 : i == 1 ? ChordThird : ChordFifth), 0.2f + i * 0.25f, 7.5f, strength * 0.7f, 6);
                Add(Instrument.Horn, Root, 0.6f, 6f, strength * 0.8f, 6);
                if (victory && beat == 4) Add(Instrument.Flute, Root + 24, 0.9f, 5f, 0.4f, 6);
            }
        }
    }
}
