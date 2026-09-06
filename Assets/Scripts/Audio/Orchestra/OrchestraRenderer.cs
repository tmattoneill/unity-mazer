using System;
using System.Threading;

namespace MazeSolver
{
    public enum AudioCommandType { Start, Stop, Pause, Settings, Snapshot, Complete }
    public struct AudioCommand
    {
        public AudioCommandType Type;
        public int Value;
        public MusicSettings Settings;
        public MazeMusicSnapshot Snapshot;
        public SessionRecording Recording;
    }

    // One producer (main thread), one consumer (audio thread). Lifecycle messages are retried by the producer.
    public sealed class AudioCommandQueue
    {
        readonly AudioCommand[] entries = new AudioCommand[256];
        int read, write;
        public bool TryWrite(AudioCommand command)
        {
            int next = (write + 1) % entries.Length;
            if (next == Volatile.Read(ref read)) return false;
            entries[write] = command;
            Volatile.Write(ref write, next);
            return true;
        }
        public bool TryRead(out AudioCommand command)
        {
            if (read == Volatile.Read(ref write)) { command = default; return false; }
            command = entries[read];
            Volatile.Write(ref read, (read + 1) % entries.Length);
            return true;
        }
    }

    public sealed class OrchestraRenderer
    {
        struct Voice
        {
            public InstrumentSample Sample;
            public double Position, Rate;
            public int Remaining, Release, ReleaseLength, Priority;
            public float Gain, Left, Right, Envelope, AttackStep, LastLeft, LastRight, StealLeft, StealRight;
            public bool Accent;
        }
        readonly Voice[] voices = new Voice[96];
        readonly InstrumentBankData bank;
        readonly IStyleComposer[] composers;
        IStyleComposer score;
        readonly ScoreNote[] notes = new ScoreNote[64];
        readonly AudioCommandQueue commands = new AudioCommandQueue();
        readonly StereoHall hall;
        readonly int sampleRate;
        MusicSettings settings = MusicSettings.Default;
        bool running, paused;
        int noteCount, nextNote, beatFrame, beatLength;
        float transportGain, orchestraGain = 1, musicGain, accentGain, limiterGain = 1;
        float releaseCoefficient;
        int activeVoices;
        SessionRecording recording;
        public int ActiveVoices => Volatile.Read(ref activeVoices);
        public int Beat => score.Beat;
        public bool Finished => score.Finished;
        public long RenderedFrames { get; private set; }
        public float Peak { get; private set; }
        public int StolenVoices { get; private set; }
        public AudioCommandQueue Commands => commands;

        public OrchestraRenderer(InstrumentBankData bank, OrchestralScoreRules rules, int sampleRate)
        {
            this.bank = bank;
            this.sampleRate = sampleRate;
            composers = StyleComposerFactory.CreateAll(rules);
            score = composers[(int)MusicStyle.Cinematic];
            hall = new StereoHall(sampleRate);
            releaseCoefficient = 1f / (sampleRate * 0.025f);
        }

        void HandleCommands()
        {
            while (commands.TryRead(out var command))
            {
                switch (command.Type)
                {
                    case AudioCommandType.Start:
                        recording?.Cancel(); recording = command.Recording;
                        Array.Clear(voices, 0, voices.Length); hall.Clear();
                        transportGain = musicGain = accentGain = 0; limiterGain = 1;
                        settings = command.Settings;
                        int style = (int)settings.Style;
                        score = composers[style >= 0 && style < composers.Length ? style : 0];
                        score.Reset(command.Value, settings);
                        running = true; paused = false; beatFrame = beatLength = noteCount = nextNote = 0;
                        break;
                    case AudioCommandType.Stop:
                        recording?.Cancel(); recording = null;
                        running = false; paused = false; noteCount = nextNote = 0; ReleaseAll(); break;
                    case AudioCommandType.Pause: paused = command.Value != 0; break;
                    case AudioCommandType.Settings:
                        settings = command.Settings;
                        if (settings.Mode == MusicMode.Soundtrack) { recording?.Cancel(); recording = null; }
                        break;
                    case AudioCommandType.Snapshot: score.Observe(command.Snapshot); break;
                    case AudioCommandType.Complete: score.Complete((SessionOutcome)command.Value); break;
                }
            }
        }

        void ReleaseAll()
        {
            for (int i = 0; i < voices.Length; i++)
                if (voices[i].Sample != null)
                {
                    voices[i].Remaining = 0;
                    voices[i].ReleaseLength = voices[i].Release = sampleRate / 30;
                }
        }

        void ScheduleBeat()
        {
            noteCount = score.ComposeBeat(settings, notes);
            nextNote = 0; beatFrame = 0;
            beatLength = (int)Math.Round(sampleRate * 60.0 / score.Tempo);
            // Stable, bounded insertion sort keeps simultaneous attacks in musical priority order.
            for (int i = 1; i < noteCount; i++)
            {
                ScoreNote note = notes[i];
                int j = i - 1;
                while (j >= 0 && notes[j].OffsetBeats > note.OffsetBeats)
                { notes[j + 1] = notes[j]; j--; }
                notes[j + 1] = note;
            }
        }

        void StartNote(ScoreNote note)
        {
            InstrumentSample selected = null;
            float best = float.MaxValue;
            for (int i = 0; i < bank.Samples.Length; i++)
            {
                var candidate = bank.Samples[i];
                if (candidate.Instrument != note.Instrument) continue;
                float cost = Math.Abs(candidate.Root - note.Pitch) * 10f
                    + Math.Abs(candidate.Layer - (1f + note.Velocity * 2f))
                    + (candidate.Alternate == note.Alternate ? 0 : 0.2f);
                if (cost < best) { best = cost; selected = candidate; }
            }
            if (selected == null) return;
            int slot = -1;
            float quietest = float.MaxValue;
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].Sample == null) { slot = i; break; }
                float importance = voices[i].Priority * 10 + voices[i].Envelope * voices[i].Gain;
                if (importance < quietest) { quietest = importance; slot = i; }
            }
            var old = voices[slot];
            if (old.Sample != null && old.Priority > note.Priority) return;
            if (old.Sample != null) StolenVoices++;
            float pan = Pan(note.Instrument);
            bool shortNote = note.Instrument == Instrument.ViolinShort || note.Instrument == Instrument.CelloShort || note.Instrument == Instrument.Trumpet || note.Instrument == Instrument.Bassoon;
            bool percussion = note.Instrument >= Instrument.Timpani;
            float release = percussion ? 0.6f : shortNote ? 0.10f : 0.28f;
            voices[slot] = new Voice
            {
                Sample = selected, Rate = Math.Pow(2, (note.Pitch - selected.Root - selected.TuningCents / 100.0) / 12.0) * selected.Rate / sampleRate,
                Remaining = (int)(note.DurationBeats * beatLength), ReleaseLength = (int)(sampleRate * release),
                Release = (int)(sampleRate * release), Gain = note.Velocity * selected.Gain * InstrumentGain(note.Instrument),
                Left = (float)Math.Sqrt((1 - pan) * 0.5), Right = (float)Math.Sqrt((1 + pan) * 0.5),
                AttackStep = 1f / (sampleRate * (percussion || shortNote ? 0.002f : 0.018f)),
                Priority = note.Priority, Accent = note.Accent,
                StealLeft = old.LastLeft, StealRight = old.LastRight
            };
        }

        static float Pan(Instrument instrument)
        {
            switch (instrument)
            {
                case Instrument.ViolinLong: case Instrument.ViolinShort: return -0.42f;
                case Instrument.CelloLong: case Instrument.CelloShort: return 0.28f;
                case Instrument.Horn: return -0.2f;
                case Instrument.Trumpet: return 0.18f;
                case Instrument.Flute: return -0.12f;
                case Instrument.Bassoon: return 0.12f;
                case Instrument.Cymbal: return 0.35f;
                default: return 0;
            }
        }
        static float InstrumentGain(Instrument instrument)
        {
            switch (instrument)
            {
                case Instrument.ViolinLong: return 0.22f;
                case Instrument.ViolinShort: return 0.48f;
                case Instrument.CelloLong: return 0.34f;
                case Instrument.CelloShort: return 0.5f;
                case Instrument.Bass: return 0.48f;
                case Instrument.Horn: return 0.55f;
                case Instrument.Trumpet: return 0.40f;
                case Instrument.Flute: return 0.40f;
                case Instrument.Bassoon: return 0.36f;
                case Instrument.Timpani: return 0.52f;
                case Instrument.BassDrum: return 0.64f;
                case Instrument.Snare: return 0.30f;
                default: return 0.27f;
            }
        }

        public void Render(float[] data, int channels)
        {
            HandleCommands();
            int live = 0;
            for (int frame = 0; frame < data.Length; frame += channels)
            {
                float targetTransport = paused ? 0 : 1;
                transportGain += (targetTransport - transportGain) * releaseCoefficient;
                orchestraGain += ((settings.Mode == MusicMode.Orchestra ? 1 : 0) - orchestraGain) * releaseCoefficient;
                musicGain += ((settings.MusicMuted ? 0 : settings.Volume) - musicGain) * releaseCoefficient;
                accentGain += ((settings.AccentsMuted ? 0 : settings.AccentVolume) - accentGain) * releaseCoefficient;
                if (paused && transportGain < 0.0001f)
                {
                    for (int channel = 0; channel < channels; channel++) data[frame + channel] = 0;
                    continue;
                }
                if (running && !paused)
                {
                    if (beatFrame >= beatLength) ScheduleBeat();
                    while (nextNote < noteCount && beatFrame >= (int)(notes[nextNote].OffsetBeats * beatLength))
                        StartNote(notes[nextNote++]);
                    beatFrame++;
                }
                float left = 0, right = 0, sendLeft = 0, sendRight = 0;
                live = 0;
                for (int index = 0; index < voices.Length; index++)
                {
                    ref Voice voice = ref voices[index];
                    if (voice.Sample == null) continue;
                    if (voice.Remaining > 0 && voice.Sample.LoopEnd > 0 && voice.Position >= voice.Sample.LoopEnd)
                        voice.Position = voice.Sample.LoopStart + 256 + (voice.Position - voice.Sample.LoopEnd);
                    int position = (int)voice.Position;
                    if (position >= voice.Sample.Frames - 1 || voice.Release <= 0) { voice.Sample = null; continue; }
                    live++;
                    if (voice.Remaining-- <= 0) voice.Release--;
                    voice.Envelope = Math.Min(1, voice.Envelope + voice.AttackStep);
                    float envelope = voice.Envelope * Math.Min(1, voice.Release / (float)voice.ReleaseLength);
                    // Fade the natural sample end even if a long note exceeds its recording.
                    envelope *= (float)Math.Min(1, (voice.Sample.Frames - voice.Position - 1) / (voice.Sample.Rate * 0.04));
                    float fraction = (float)(voice.Position - position);
                    int c = voice.Sample.Channels;
                    var pcm = voice.Sample.Pcm;
                    int p = position * c;
                    float l = pcm[p] + (pcm[p + c] - pcm[p]) * fraction;
                    int r = p + (c > 1 ? 1 : 0);
                    float rv = pcm[r] + (pcm[r + c] - pcm[r]) * fraction;
                    if (voice.Remaining > 0 && voice.Sample.LoopEnd > 0 && position >= voice.Sample.LoopEnd - 256)
                    {
                        int loopPosition = voice.Sample.LoopStart + position - (voice.Sample.LoopEnd - 256);
                        float blend = (position - (voice.Sample.LoopEnd - 256)) / 256f;
                        l += (pcm[loopPosition * c] - l) * blend;
                        rv += (pcm[loopPosition * c + (c > 1 ? 1 : 0)] - rv) * blend;
                    }
                    float gain = envelope * voice.Gain * (voice.Accent ? accentGain : musicGain);
                    l = l * gain * voice.Left + voice.StealLeft;
                    rv = rv * gain * voice.Right + voice.StealRight;
                    voice.StealLeft *= 0.97f; voice.StealRight *= 0.97f;
                    voice.LastLeft = l; voice.LastRight = rv;
                    left += l; right += rv;
                    float send = voice.Sample.Instrument >= Instrument.Timpani ? 0.22f : 0.7f;
                    sendLeft += l * send; sendRight += rv * send;
                    voice.Position += voice.Rate;
                }
                hall.Process(sendLeft, sendRight, out float wetLeft, out float wetRight);
                left = (left + wetLeft * settings.Hall) * transportGain * orchestraGain * 2.5f;
                right = (right + wetRight * settings.Hall) * transportGain * orchestraGain * 2.5f;
                float peak = Math.Max(Math.Abs(left), Math.Abs(right));
                float required = peak > 0.89f ? 0.89f / peak : 1;
                limiterGain = required < limiterGain ? required : Math.Min(required, limiterGain + 1f / (sampleRate * 0.15f));
                left *= limiterGain; right *= limiterGain;
                Peak = Math.Max(Peak, Math.Max(Math.Abs(left), Math.Abs(right)));
                if (channels == 1) data[frame] = (left + right) * 0.5f;
                else
                {
                    data[frame] = left; data[frame + 1] = right;
                    for (int channel = 2; channel < channels; channel++) data[frame + channel] = 0;
                }
                RenderedFrames++;
            }
            Volatile.Write(ref activeVoices, live);
            recording?.Capture(data, channels, score.Finished, paused);
        }
    }

    // Four damped, cross-coupled delays form a shared stereo hall. Storage is fixed at startup.
    sealed class StereoHall
    {
        readonly float[][] lines;
        readonly int[] positions = new int[4];
        readonly float[] damping = new float[4];
        public StereoHall(int rate)
        {
            int[] lengths = { 1493, 1601, 1867, 1999 };
            lines = new float[4][];
            for (int i = 0; i < 4; i++) lines[i] = new float[(int)(lengths[i] * rate / 22050.0)];
        }
        public void Clear()
        {
            foreach (var line in lines) Array.Clear(line, 0, line.Length);
            Array.Clear(positions, 0, positions.Length);
            Array.Clear(damping, 0, damping.Length);
        }
        public void Process(float left, float right, out float wetLeft, out float wetRight)
        {
            float a = lines[0][positions[0]], b = lines[1][positions[1]];
            float c = lines[2][positions[2]], d = lines[3][positions[3]];
            float sum = (a + b + c + d) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float old = lines[i][positions[i]];
                float input = (i % 2 == 0 ? left : right) * 0.32f;
                damping[i] += ((sum - old) * 0.82f - damping[i]) * 0.38f;
                lines[i][positions[i]] = input + damping[i];
                if (++positions[i] == lines[i].Length) positions[i] = 0;
            }
            wetLeft = a + c; wetRight = b + d;
        }
    }
}
