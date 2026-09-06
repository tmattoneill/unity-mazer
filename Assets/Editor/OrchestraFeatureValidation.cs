using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MazeSolver.Editor
{
    public static class OrchestraFeatureValidation
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Orchestra feature validation: " + message);
        }
        public static void Run(OrchestralScoreRules rules)
        {
            var settings = MusicSettings.Default;
            var notes = new ScoreNote[64];
            var signatures = new HashSet<ulong>();
            for (int seed = 430; seed < 450; seed++)
            {
                ulong signature = Signature(rules, settings, seed);
                Require(signature == Signature(rules, settings, seed), "composition is not repeatable");
                Require(signatures.Add(signature), "different seeds produced the same composition");
            }
            for (int mode = 0; mode < 2; mode++)
            for (int key = 0; key < 12; key++)
            {
                settings.Tonic = key; settings.Tonality = (Tonality)mode; settings.Variation = 1;
                var score = new CinematicComposer(rules); score.Reset(432, settings);
                for (int beat = 0; beat < 256; beat++)
                {
                    score.Observe(new MazeMusicSnapshot { Active = 2 + beat / 8 });
                    int count = score.ComposeBeat(settings, notes);
                    CheckChordNotes(score, notes, count);
                }
                foreach (var outcome in new[] { SessionOutcome.Solved, SessionOutcome.Exhausted })
                {
                    score.Reset(431, settings); score.Complete(outcome);
                    for (int beat = 0; beat < 9; beat++)
                    {
                        int count = score.ComposeBeat(settings, notes);
                        CheckChordNotes(score, notes, count);
                        if (beat == 2)
                        {
                            Require(score.Root % 12 == key, "cadence does not resolve to selected tonic");
                            Require(score.ChordThird == (mode == 0 ? 3 : 4), "cadence does not follow selected mode");
                        }
                    }
                    Require(score.Finished, "ending did not finish");
                }
            }
            settings = MusicSettings.Default; settings.Variation = 0;
            var phraseScore = new CinematicComposer(rules); phraseScore.Reset(431, settings);
            phraseScore.ComposeBeat(settings, notes);
            settings.Variation = 1;
            for (int beat = 1; beat < 32; beat++)
            {
                phraseScore.ComposeBeat(settings, notes);
                Require(phraseScore.AppliedVariation == 0, "variation changed within a phrase");
            }
            phraseScore.ComposeBeat(settings, notes);
            Require(phraseScore.AppliedVariation == 1 && phraseScore.ThemeTransform != 0, "variation not applied at phrase boundary");
            for (int beat = 33; beat <= 128; beat++) phraseScore.ComposeBeat(settings, notes);
            Require(phraseScore.ThemeTransform == 0, "opening theme did not return");
            // Pending key settings must not transpose a running composition.
            settings = MusicSettings.Default;
            var first = new CinematicComposer(rules); var second = new CinematicComposer(rules);
            first.Reset(431, settings); second.Reset(431, settings);
            var changed = settings; changed.Tonic = 10; changed.Tonality = Tonality.Major;
            var other = new ScoreNote[64];
            for (int beat = 0; beat < 40; beat++)
            {
                int count = first.ComposeBeat(settings, notes);
                Require(count == second.ComposeBeat(changed, other), "pending key changed the running arrangement");
                for (int i = 0; i < count; i++) Require(notes[i].Pitch == other[i].Pitch, "pending key changed running notes");
            }
            ValidateRecording();
            Debug.Log("ORCHESTRA FEATURES PASSED: 20 distinct seeded compositions, 24 keys, both cadences, phrase variation, returning theme, pending key, 24-bit WAV capture, pause, tail, cancellation, overflow and file errors.");
        }

        // Style-specific content contracts, checked at the composer level.
        public static void RunStyles(StyleRuleBundle bundle)
        {
            var settings = MusicSettings.Default;
            settings.Density = 1;
            var notes = new ScoreNote[64];

            var ambient = new AmbientComposer(bundle.Ambient ? bundle.Ambient : bundle.Cinematic);
            ambient.Reset(431, settings);
            float totalDuration = 0; int sustained = 0;
            for (int beat = 0; beat < 256; beat++)
            {
                ambient.Observe(new MazeMusicSnapshot { Active = 3 + beat / 16 });
                int count = ambient.ComposeBeat(settings, notes);
                for (int i = 0; i < count; i++)
                {
                    var instrument = notes[i].Instrument;
                    Require(instrument != Instrument.Snare && instrument != Instrument.BassDrum && instrument != Instrument.Kick,
                        "Ambient used beat percussion");
                    Require(notes[i].OffsetBeats >= 0 && notes[i].OffsetBeats < 1, "Ambient note timing invalid");
                    if (!notes[i].Accent) { totalDuration += notes[i].DurationBeats; sustained++; }
                }
            }
            Require(sustained > 0 && totalDuration / sustained >= 1.5f, "Ambient notes are not sustained");
            ambient.Complete(SessionOutcome.Solved);
            for (int beat = 0; beat < 13; beat++) ambient.ComposeBeat(settings, notes);
            Require(ambient.Finished, "Ambient ending did not finish");
            Require(ambient.Root % 12 == settings.Tonic % 12, "Ambient did not resolve home");

            var edm = new EdmComposer(bundle.EDM ? bundle.EDM : bundle.Cinematic);
            edm.Reset(431, settings);
            for (int beat = 0; beat < 256; beat++)
            {
                edm.Observe(new MazeMusicSnapshot { Active = 8 });
                string section = edm.Section;
                int count = edm.ComposeBeat(settings, notes);
                bool kick = false;
                for (int i = 0; i < count; i++)
                {
                    kick |= notes[i].Instrument == Instrument.Kick;
                    Require(notes[i].Instrument != Instrument.Snare, "EDM used the orchestral snare");
                    Require(notes[i].OffsetBeats >= 0 && notes[i].OffsetBeats < 1, "EDM note timing invalid");
                }
                Require(kick == (section != "Breakdown"), "EDM kick pattern wrong during " + section);
            }
            edm.Complete(SessionOutcome.Solved);
            for (int beat = 0; beat < 9; beat++) edm.ComposeBeat(settings, notes);
            Require(edm.Finished, "EDM ending did not finish");

            var classical = new ClassicalComposer(bundle.Classical ? bundle.Classical : bundle.Cinematic);
            classical.Reset(431, settings);
            for (int beat = 0; beat < 256; beat++)
            {
                classical.Observe(new MazeMusicSnapshot { Active = 6 });
                int bar = beat / 4;
                int count = classical.ComposeBeat(settings, notes);
                Require(count > 0, "Classical produced an empty beat");
                for (int i = 0; i < count; i++)
                {
                    var instrument = notes[i].Instrument;
                    Require(instrument < Instrument.Kick, "Classical used a synth voice");
                    if (instrument >= Instrument.Timpani)
                    {
                        Require(instrument == Instrument.Timpani, "Classical used non-timpani percussion");
                        Require(bar % 4 == 3, "Classical timpani away from a cadence");
                    }
                }
            }
            classical.Complete(SessionOutcome.Solved);
            for (int beat = 0; beat < 11; beat++) classical.ComposeBeat(settings, notes);
            Require(classical.Finished, "Classical ending did not finish");
            Require(classical.Root % 12 == settings.Tonic % 12, "Classical cadence did not resolve to the tonic");

            Debug.Log("STYLE FEATURES PASSED: Ambient texture rules, EDM kick/breakdown pattern, Classical scoring rules, all endings resolve.");
        }

        static ulong Signature(OrchestralScoreRules rules, MusicSettings settings, int seed)
        {
            var score = new CinematicComposer(rules); score.Reset(seed, settings);
            var notes = new ScoreNote[64]; ulong result = 14695981039346656037UL;
            for (int beat = 0; beat < 128; beat++)
            {
                // A live population brings in the seed-bearing theme and lead layers.
                score.Observe(new MazeMusicSnapshot { Active = 6 });
                int count = score.ComposeBeat(settings, notes);
                // Ignore sample takes and human timing: this checks the composition itself.
                for (int i = 0; i < count; i++)
                    result = unchecked((result ^ (uint)(notes[i].Pitch + 128 * (int)notes[i].Instrument)) * 1099511628211UL);
            }
            return result;
        }

        static void CheckChordNotes(CinematicComposer score, ScoreNote[] notes, int count)
        {
            Require(count < notes.Length, "note buffer saturated");
            for (int i = 0; i < count; i++)
            {
                var note = notes[i];
                Require(note.Pitch >= 28 && note.Pitch <= 86, "note outside orchestral register");
                Require(note.OffsetBeats >= 0 && note.OffsetBeats < 1, "invalid note timing");
                if (note.Instrument > Instrument.Timpani) continue;
                int interval = (note.Pitch - score.Root + 120) % 12;
                Require(interval == 0 || interval == score.ChordThird || interval == score.ChordFifth, "pitched note outside its intended chord");
            }
        }

        static void ValidateRecording()
        {
            string directory = Path.Combine("Builds", "RecordingChecks", Guid.NewGuid().ToString("N"));
            var settings = MusicSettings.Default;
            var take = new SessionRecording(directory, 1000, 431, settings);
            var signal = new[] { 0.25f, -0.25f, 0.5f, -0.5f, 0.75f, -0.75f };
            take.Capture(signal, 2, false, false);
            var paused = new float[6];
            take.Capture(paused, 2, false, true);
            var silence = new float[2000];
            long before = GC.GetAllocatedBytesForCurrentThread();
            take.Capture(silence, 2, true, false);
            Require(GC.GetAllocatedBytesForCurrentThread() == before, "recording callback allocated");
            Require(take.WaitForWriter(3000) && take.State == RecordingState.Ready, "recording failed to finish");
            using (var reader = new BinaryReader(File.OpenRead(take.TemporaryPath)))
            {
                Require(new string(reader.ReadChars(4)) == "RIFF", "missing RIFF header");
                Require(reader.ReadUInt32() + 8 == reader.BaseStream.Length, "WAV length header mismatch");
                reader.BaseStream.Position = 22; Require(reader.ReadInt16() == 2, "WAV not stereo");
                Require(reader.ReadInt32() == 1000, "WAV sample rate mismatch");
                reader.BaseStream.Position = 34; Require(reader.ReadInt16() == 24, "WAV not 24 bit");
                reader.BaseStream.Position = 40; Require(reader.ReadInt32() == 1006 * 6, "pause or final tail missing");
                foreach (float expected in signal)
                {
                    int value = reader.ReadByte() | reader.ReadByte() << 8 | reader.ReadByte() << 16;
                    if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
                    Require(Math.Abs(value / 8388607f - expected) < 0.0000002f, "captured samples differ from output");
                }
            }
            // A zero-capacity queue exercises overflow without relying on thread scheduling.
            var overflow = new SessionRecording(directory, 1000, 1, settings, 0);
            overflow.Capture(signal, 2, false, false);
            Require(overflow.WaitForWriter(3000) && overflow.State == RecordingState.Failed && !File.Exists(overflow.TemporaryPath), "overflow exposed a corrupt WAV");
            var cancel = new SessionRecording(directory, 1000, 1, settings);
            cancel.Capture(signal, 2, false, false); cancel.Cancel();
            Require(cancel.WaitForWriter(3000) && cancel.State == RecordingState.Cancelled && !File.Exists(cancel.TemporaryPath), "cancelled WAV retained");
            var invalid = new SessionRecording(directory, 1000, 1, settings);
            invalid.Capture(new[] { float.NaN, 0f }, 2, false, false);
            Require(invalid.WaitForWriter(3000) && invalid.State == RecordingState.Failed, "writer errors not reported");
            bool refusedOverwrite = false;
            try { using (var writer = new PcmWaveWriter(take.TemporaryPath, 1000)) { } }
            catch (IOException) { refusedOverwrite = true; }
            Require(refusedOverwrite, "writer overwrote an existing recording");
            var forced = new SessionRecording(directory, 1000, 1, settings, 20);
            var ringing = new float[22000];
            for (int i = 0; i < ringing.Length; i++) ringing[i] = 0.25f;
            forced.Capture(ringing, 2, true, false);
            Require(forced.WaitForWriter(3000) && forced.State == RecordingState.Ready, "maximum tail did not finish");
            Require(new FileInfo(forced.TemporaryPath).Length == 44 + 10000 * 6, "maximum tail duration is wrong");
            Require(ringing[19999] == 0 && ringing[20000] == 0, "tail fade jumps back to full level");
            for (int i = 0; i < signal.Length; i++) signal[i] = 0.25f;
            forced.Capture(signal, 2, true, false);
            Require(signal[0] == 0, "tail became audible again after fade");
            var mono = new SessionRecording(directory, 1000, 1, settings);
            mono.Capture(new[] { 0.5f }, 1, false, false);
            mono.Capture(silence, 1, true, false);
            Require(mono.WaitForWriter(3000) && mono.State == RecordingState.Ready, "mono capture failed");
            using (var reader = new BinaryReader(File.OpenRead(mono.TemporaryPath)))
            {
                reader.BaseStream.Position = 44;
                var left = reader.ReadBytes(3); var right = reader.ReadBytes(3);
                for (int i = 0; i < 3; i++) Require(left[i] == right[i], "mono was not duplicated into stereo");
            }
        }
    }
}
