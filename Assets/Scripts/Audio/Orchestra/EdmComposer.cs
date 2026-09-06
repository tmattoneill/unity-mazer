using System;

namespace MazeSolver
{
    // A firm kick and bass foundation, repeating hooks, syncopation, layered percussion,
    // filter movement, builds, breakdowns and drops, paced against the solve sheet when a
    // forecast exists and on fixed sixteen-bar cycles otherwise. The kick sidechain-ducks
    // the bass and pad in the renderer. Fixed storage; audio-thread safe.
    public sealed class EdmComposer : StyleComposerBase
    {
        readonly int[] minorProgressions, majorProgressions, motif;
        readonly float[] sectionEnergy;
        readonly int[] hook = new int[8];
        int progression, baseProgression, hookShape;
        float variation;
        // Roles, cumulative: 1 kick + bass core, 2 hook, 3 closed hats + clap,
        // 4 pad, 5 open hats + stabs, 6 lead double, 7 full arrangement.
        readonly EnsembleTracker ensemble = new EnsembleTracker(
            new[] { 0f, 2f, 3f, 5f, 8f, 13f, 21f },
            new[] { 0f, 1.4f, 2.4f, 3.9f, 6.4f, 10.4f, 16.9f });
        public int Layers => ensemble.Layers;

        // Sixteen-bar cycle: groove, build in bars 6-7, drop bars 8-13, breakdown 14-15.
        int CycleBar => (Beat / 4) % 16;
        bool InBuild => CycleBar >= 6 && CycleBar <= 7;
        bool InDrop => CycleBar >= 8 && CycleBar <= 13;
        bool InBreakdown => CycleBar >= 14;
        public override string Section => endingBeat >= 0 ? "Outro" : InDrop ? "Drop" : InBuild ? "Build" : InBreakdown ? "Breakdown" : "Groove";

        public EdmComposer(OrchestralScoreRules rules)
        {
            minorProgressions = (int[])rules.MinorProgressions.Clone();
            majorProgressions = (int[])rules.MajorProgressions.Clone();
            motif = (int[])rules.MotifDegrees.Clone();
            sectionEnergy = (float[])rules.SectionEnergy.Clone();
            if (minorProgressions.Length != 32 || majorProgressions.Length != 32 || motif.Length != 8 || sectionEnergy.Length != 4)
                throw new ArgumentException("EDM rules require the shared score-rules shape.");
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
            hookShape = ComposeNext(3);
            for (int i = 0; i < hook.Length; i++)
                hook[i] = (motif[i] + (hookShape == 2 ? i % 2 : 0)) % 5;
            hook[0] = 0;
            progression = baseProgression;
            variation = Math.Max(0, Math.Min(1, settings.Variation));
            Beat = 0; endingBeat = -1; Finished = false; Intensity = 0;
            activity = coverage = 0; births = deaths = 0;
            forecastBeats = progress = -1; rawActive = 1;
            ensemble.Reset();
            Root = Place(48 + tonic, 43, 54);
        }

        public override int ComposeBeat(MusicSettings settings, ScoreNote[] buffer)
        {
            BeginBuffer(buffer);
            if (Finished) return 0;
            bool preparing = PreparingEnding(16);
            // EDM sits at the top of the tempo window.
            if (Beat % 4 == 0) Tempo = Math.Max(118, Math.Min(132, settings.Tempo + 12));
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
                if (Beat / 32 % 4 != 0 && variation > 0)
                {
                    if (variation > 0.5f && ComposeNext(1000) < variation * 1000) progression = ComposeNext(4);
                    if (ComposeNext(1000) < variation * 700) hookShape = ComposeNext(3);
                }
            }
            int bar = Beat / 4, pulse = Beat % 4, section = (bar / 8) % 4;
            // A four-chord loop keeps the hook grounded.
            var progressions = tonality == Tonality.Major ? majorProgressions : minorProgressions;
            SetChord(progressions[progression * 8 + bar % 4]);
            float target = (0.34f + 0.26f * settings.Energy + 0.22f * activity + 0.08f * coverage) * sectionEnergy[section];
            Intensity += (target - Intensity) * 0.25f;
            float energy = Math.Max(0.3f, Intensity);
            if (preparing) energy = Math.Max(energy, 0.6f);
            int layers = ensemble.Tick(Beat, settings.Density);
            // The final approach is one long build into the closing drop.
            bool build = InBuild || preparing;
            bool breakdown = InBreakdown && !preparing;
            bool drop = InDrop || preparing;
            float buildLift = build && forecastBeats > 0 ? Math.Max(0, 1 - forecastBeats / 16f) * 0.25f
                : build ? (Beat % 8) / 32f : 0;

            // Four-on-the-floor kick; the renderer ducks bass and pad from it.
            if (!breakdown)
                Add(Instrument.Kick, 36, 0, 0.9f, 0.72f + energy * 0.2f + buildLift, 5);

            // Offbeat bass, filter mostly open in drops.
            Add(Instrument.SynthBass, Root - 12, 0.5f, 0.42f, 0.5f + energy * 0.22f, 4,
                filterCutoff: drop ? 0.5f : 0.22f, filterSweep: build ? 0.4f : 0);
            if (drop && pulse % 2 == 1)
                Add(Instrument.SynthBass, Root - 12 + (pulse == 1 ? 0 : ChordFifth), 0.75f, 0.2f, 0.42f + energy * 0.16f, 3, filterCutoff: 0.55f);

            // The hook: a two-bar synth-lead loop.
            if (layers >= 2 && !breakdown)
            {
                int index = (bar % 2) * 4 + pulse;
                int tone = hook[index] + (hookShape == 1 && bar % 4 >= 2 ? 2 : 0);
                int pitch = Root + 12 + Scale(tone % 7);
                Add(Instrument.SynthLead, pitch, hookShape == 0 || pulse % 2 == 0 ? 0 : 0.25f, 0.55f, 0.42f + energy * 0.2f + buildLift * 0.5f, 4,
                    filterCutoff: drop ? 0.75f : 0.4f, filterSweep: build ? 0.5f : 0);
                if (layers >= 6 && drop)
                    Add(Instrument.SynthLead, pitch + 12, 0.5f, 0.3f, 0.3f + energy * 0.15f, 3, filterCutoff: 0.8f);
            }

            // Percussion layers.
            if (layers >= 3 && !breakdown)
            {
                Add(Instrument.HatClosed, 84, 0.5f, 0.2f, 0.3f + energy * 0.12f, 2);
                if (pulse == 1 || pulse == 3)
                    Add(Instrument.Clap, 76, 0, 0.4f, 0.42f + energy * 0.16f, 3);
            }
            if (layers >= 5 && (drop || build) && pulse % 2 == 0)
                Add(Instrument.HatOpen, 84, 0.5f, 0.45f, 0.24f + energy * 0.1f, 2);
            if (build)
            {
                // Riser roll accelerates into the drop.
                int rolls = 2 + (Beat % 8) / 2;
                for (int n = 0; n < rolls && n < 8; n++)
                    Add(Instrument.HatClosed, 84, n / (float)rolls, 0.12f, 0.2f + n * 0.05f + buildLift, 2);
            }

            // Pad glue with a slow sweep; stabs answer the hook at depth.
            if (layers >= 4 && Beat % 4 == 0)
                Add(Instrument.SynthPad, Root + (breakdown ? 0 : 12), 0.01f, 4.2f, 0.3f + energy * 0.12f, 2,
                    releaseBeats: 1.5f, filterCutoff: breakdown ? 0.12f : 0.3f, filterSweep: build ? 0.25f : 0.02f);
            if (layers >= 5 && drop && pulse == 2)
                Add(Instrument.SynthPad, Root + 12 + ChordThird, 0.5f, 0.35f, 0.34f + energy * 0.14f, 2, filterCutoff: 0.7f);

            // The full arrangement borrows orchestral weight.
            if (layers >= 7 && drop && pulse == 0)
            {
                Add(Instrument.CelloShort, Root, 0, 0.4f, 0.4f + energy * 0.15f, 2);
                Add(Instrument.Cymbal, 60, 0, 3f, 0.4f, 2);
            }

            // Births blip upward, deaths thud downward.
            if (births > 0) Add(Instrument.SynthLead, Root + 24 + ChordFifth, 0.5f, 0.2f, 0.45f, 2, true, filterCutoff: 0.85f);
            if (deaths > 0) Add(Instrument.SynthBass, Root - 24, 0.75f, 0.3f, 0.4f, 2, true, filterCutoff: 0.3f);
            births = deaths = 0;
            Beat++;
            return NoteCount;
        }

        void ComposeEnding(int beat)
        {
            if (beat >= 8) { Finished = true; return; }
            SetChord(0);
            if (beat == 0)
            {
                // The closing drop hit.
                float strength = victory ? 0.85f : 0.5f;
                Add(Instrument.Kick, 36, 0, 1f, strength, 6);
                Add(Instrument.SynthBass, Root - 12, 0, 4f, strength * 0.8f, 6, releaseBeats: 2f, filterCutoff: victory ? 0.7f : 0.2f);
                Add(Instrument.SynthPad, Root + 12, 0.02f, 6f, strength * 0.55f, 6, releaseBeats: 2.5f, filterCutoff: victory ? 0.5f : 0.1f, filterSweep: victory ? 0 : -0.08f);
                if (victory)
                {
                    Add(Instrument.SynthLead, Root + 24, 0.05f, 3f, 0.6f, 6, filterCutoff: 0.85f);
                    Add(Instrument.Cymbal, 60, 0, 5f, 0.6f, 6);
                }
            }
            else if (beat == 2 && victory)
            {
                Add(Instrument.Kick, 36, 0, 1f, 0.7f, 6);
                Add(Instrument.SynthLead, Root + 24 + ChordFifth, 0.5f, 2f, 0.5f, 6, filterCutoff: 0.8f);
            }
        }
    }
}
