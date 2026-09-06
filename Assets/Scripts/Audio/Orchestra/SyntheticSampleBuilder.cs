using System;
using System.Collections.Generic;

namespace MazeSolver
{
    // Bakes the EDM voices as ordinary InstrumentSamples during bank load, so the audio
    // thread plays them through the existing sample path with zero new allocation there.
    // Oscillators are additive and band-limited for playback up to +19 semitones above
    // each root; sustained voices carry seamless integer-period loops.
    public static class SyntheticSampleBuilder
    {
        const int Rate = 44100;

        public static void Append(InstrumentBankData bank)
        {
            var extras = new List<InstrumentSample>
            {
                Tonal(Instrument.SynthBass, 33, saw: 1.0f, pulse: 0.2f, detune: 0f, level: 0.5f),
                Tonal(Instrument.SynthBass, 45, saw: 1.0f, pulse: 0.2f, detune: 0f, level: 0.5f),
                Tonal(Instrument.SynthLead, 57, saw: 0.6f, pulse: 0.7f, detune: 0f, level: 0.4f),
                Tonal(Instrument.SynthLead, 69, saw: 0.6f, pulse: 0.7f, detune: 0f, level: 0.4f),
                Tonal(Instrument.SynthPad, 50, saw: 0.8f, pulse: 0f, detune: 0.13f, level: 0.35f),
                Tonal(Instrument.SynthPad, 62, saw: 0.8f, pulse: 0f, detune: 0.13f, level: 0.35f),
                Kick(),
                Noise(Instrument.HatClosed, seconds: 0.07f, decay: 60f, highpass: 0.6f, level: 0.35f, root: 84),
                Noise(Instrument.HatOpen, seconds: 0.45f, decay: 9f, highpass: 0.6f, level: 0.3f, root: 84),
                Clap()
            };
            int oldLength = bank.Samples.Length;
            Array.Resize(ref bank.Samples, oldLength + extras.Count);
            for (int i = 0; i < extras.Count; i++)
            {
                bank.Samples[oldLength + i] = extras[i];
                bank.MemoryBytes += (long)extras[i].Pcm.Length * sizeof(float);
            }
        }

        static InstrumentSample Tonal(Instrument instrument, int root, float saw, float pulse, float detune, float level)
        {
            double frequency = 440.0 * Math.Pow(2, (root - 69) / 12.0);
            // Whole periods keep the loop seamless; band-limit for +19 semitones of headroom.
            int period = Math.Max(8, (int)Math.Round(Rate / frequency));
            int periods = Math.Max(24, (int)Math.Ceiling(0.25 * Rate / period));
            int frames = period * periods + 512;
            int harmonics = Math.Max(2, (int)(Rate * 0.45 / (frequency * Math.Pow(2, 19 / 12.0))));
            var pcm = new float[frames];
            double baseStep = 2 * Math.PI / period;
            double detuneStep = baseStep * (1 + detune / 100.0);
            for (int i = 0; i < frames; i++)
            {
                double value = 0;
                for (int h = 1; h <= harmonics; h++)
                {
                    double amp = saw / h + (h % 2 == 1 ? pulse / h : 0);
                    value += amp * Math.Sin(baseStep * i * h);
                    if (detune > 0) value += 0.7 * amp * Math.Sin(detuneStep * i * h);
                }
                pcm[i] = (float)value;
            }
            Normalize(pcm, level);
            // The renderer wraps Position to LoopStart + 256, so a seamless periodic loop
            // needs its length congruent to 256 modulo the waveform period.
            int loopEnd = period * periods;
            int wholePeriods = 1024 / period + 2;
            int loopStart = loopEnd - (wholePeriods * period + 256);
            return new InstrumentSample
            {
                Instrument = instrument, Root = root, Layer = 2, Alternate = 1,
                Channels = 1, Rate = Rate, Frames = frames, Pcm = pcm,
                LoopStart = loopStart, LoopEnd = loopEnd, Gain = 1
            };
        }

        static InstrumentSample Kick()
        {
            int frames = (int)(Rate * 0.32f);
            var pcm = new float[frames];
            double phase = 0;
            for (int i = 0; i < frames; i++)
            {
                float t = i / (float)Rate;
                double frequency = 45 + 110 * Math.Exp(-t * 28);
                phase += 2 * Math.PI * frequency / Rate;
                float body = (float)Math.Sin(phase) * (float)Math.Exp(-t * 9);
                float click = t < 0.004f ? (1 - t / 0.004f) * 0.4f : 0;
                pcm[i] = body + click;
            }
            Normalize(pcm, 0.8f);
            return new InstrumentSample
            {
                Instrument = Instrument.Kick, Root = 36, Layer = 2, Alternate = 1,
                Channels = 1, Rate = Rate, Frames = frames, Pcm = pcm, Gain = 1
            };
        }

        static InstrumentSample Noise(Instrument instrument, float seconds, float decay, float highpass, float level, int root)
        {
            int frames = (int)(Rate * seconds);
            var pcm = new float[frames];
            uint state = 0x2545f491u + (uint)instrument;
            float previous = 0;
            for (int i = 0; i < frames; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                float white = (state / (float)uint.MaxValue) * 2 - 1;
                float filtered = white - previous * highpass;
                previous = white;
                pcm[i] = filtered * (float)Math.Exp(-decay * i / (double)Rate);
            }
            Normalize(pcm, level);
            return new InstrumentSample
            {
                Instrument = instrument, Root = root, Layer = 2, Alternate = 1,
                Channels = 1, Rate = Rate, Frames = frames, Pcm = pcm, Gain = 1
            };
        }

        static InstrumentSample Clap()
        {
            int frames = (int)(Rate * 0.25f);
            var pcm = new float[frames];
            uint state = 0x9e3779b9u;
            float previous = 0;
            for (int i = 0; i < frames; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                float white = (state / (float)uint.MaxValue) * 2 - 1;
                float filtered = white - previous * 0.4f;
                previous = white;
                float t = i / (float)Rate;
                // Three quick bursts, then the tail.
                float envelope = 0;
                foreach (float burst in new[] { 0f, 0.012f, 0.026f })
                    if (t >= burst) envelope = Math.Max(envelope, (float)Math.Exp(-(t - burst) * 90));
                envelope = Math.Max(envelope, t >= 0.03f ? (float)Math.Exp(-(t - 0.03f) * 26) * 0.7f : 0);
                pcm[i] = filtered * envelope;
            }
            Normalize(pcm, 0.4f);
            return new InstrumentSample
            {
                Instrument = Instrument.Clap, Root = 76, Layer = 2, Alternate = 1,
                Channels = 1, Rate = Rate, Frames = frames, Pcm = pcm, Gain = 1
            };
        }

        static void Normalize(float[] pcm, float level)
        {
            float peak = 0;
            for (int i = 0; i < pcm.Length; i++) peak = Math.Max(peak, Math.Abs(pcm[i]));
            if (peak <= 0) return;
            float gain = level / peak;
            for (int i = 0; i < pcm.Length; i++) pcm[i] *= gain;
        }
    }
}
