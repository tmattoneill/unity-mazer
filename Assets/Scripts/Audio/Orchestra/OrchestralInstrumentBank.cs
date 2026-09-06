using System;
using UnityEngine;

namespace MazeSolver
{
    [CreateAssetMenu(menuName = "Maze/Orchestral instrument bank")]
    public sealed class OrchestralInstrumentBank : ScriptableObject
    {
        public TextAsset Manifest;

        [Serializable] public sealed class ManifestData { public string revision; public SampleEntry[] samples; }
        [Serializable] public sealed class SampleEntry
        {
            public string instrument, resource, source, sha256;
            public int root, layer, alternate, tuningCents, loopStartFrame, loopEndFrame;
        }

        public InstrumentBankData Load()
        {
            if (!Manifest) throw new InvalidOperationException("The orchestral sample manifest is missing.");
            var manifest = JsonUtility.FromJson<ManifestData>(Manifest.text);
            if (manifest?.samples == null || manifest.samples.Length == 0)
                throw new InvalidOperationException("The orchestral sample manifest is empty.");
            var bank = new InstrumentBankData { Samples = new InstrumentSample[manifest.samples.Length] };
            var present = new bool[13];
            for (int index = 0; index < manifest.samples.Length; index++)
            {
                var entry = manifest.samples[index];
                var clip = Resources.Load<AudioClip>(entry.resource);
                if (!clip) throw new InvalidOperationException("Missing orchestral sample: " + entry.resource);
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                int frames = Math.Min(clip.samples, clip.frequency * 4);
                var pcm = new float[frames * clip.channels];
                if (!clip.GetData(pcm, 0)) throw new InvalidOperationException("Cannot decode " + entry.resource);
                float peak = 0;
                for (int n = 0; n < pcm.Length; n++) peak = Math.Max(peak, Math.Abs(pcm[n]));
                var instrument = (Instrument)Enum.Parse(typeof(Instrument), entry.instrument);
                bank.Samples[index] = new InstrumentSample
                {
                    Instrument = instrument, Root = entry.root, Layer = entry.layer, Alternate = entry.alternate, TuningCents = entry.tuningCents,
                    Channels = clip.channels, Rate = clip.frequency, Frames = frames, Pcm = pcm,
                    LoopStart = entry.loopStartFrame, LoopEnd = entry.loopEndFrame,
                    // Preserve layer dynamics through note velocity; avoid boosting noisy recordings.
                    Gain = Math.Min(2.5f, 0.7f / Math.Max(0.05f, peak))
                };
                if (entry.loopEndFrame != 0 && (entry.loopStartFrame < 0 || entry.loopEndFrame > frames - 1 || entry.loopEndFrame - entry.loopStartFrame < 1024))
                    throw new InvalidOperationException("Invalid sustain loop: " + entry.resource);
                bank.MemoryBytes += (long)pcm.Length * sizeof(float);
                present[(int)instrument] = true;
                clip.UnloadAudioData();
                Resources.UnloadAsset(clip);
            }
            for (int i = 0; i < present.Length; i++)
                if (!present[i]) throw new InvalidOperationException("Missing instrument: " + (Instrument)i);
            if (bank.MemoryBytes > 256L * 1024 * 1024)
                throw new InvalidOperationException("Orchestra exceeds the 256 MiB decoded memory budget.");
            return bank;
        }
    }
}
