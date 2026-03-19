using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
    public enum DroneMode { Mono, Chord, Soundtrack }

    [RequireComponent(typeof(AudioSource))]
    public class MazeAudioEngine : MonoBehaviour
    {
        // Drone parameters
        float droneBaseFreq = 65.41f; // C2
        float droneVolume = 0.03f;
        float lfoDepth = 0.012f;
        float lfoFreq = 0.8f;
        float lpfCutoff = 200f;
        float lpfQ = 0.7f;
        int pitchSemitones = 0;

        // Drone mode
        DroneMode droneMode = DroneMode.Mono;

        // Soundtrack mode
        public AudioClip[] soundtrackClips;
        int currentTrackIndex;
        AudioSource soundtrackSource;

        // Chord mode state (3 oscillators for triad)
        double[] chordPhases = new double[3];
        int[] chordIntervals = { 0, 4, 7 }; // major triad
        float[] chordWeights = { 0.9f, 1.1f, 0.9f };
        int lastActiveCount;

        // Mono mode: harmonic partials
        double[] harmonicPhases = new double[6];
        float[] harmonicGains = new float[6];
        int harmonicCount;
        float harmonicTimer;
        float harmonicInterval = 5f; // seconds between adding partials

        // Oscillator state
        double phase1, phase2;
        double lfoPhase;
        double sampleRate;

        // Low-pass filter state (biquad)
        float filtX1, filtX2, filtY1, filtY2;
        float filtA0, filtA1, filtA2, filtB1, filtB2;

        // SFX queue (thread-safe)
        readonly Queue<SfxEvent> sfxQueue = new Queue<SfxEvent>();
        readonly List<ActiveSfx> activeSfx = new List<ActiveSfx>();
        readonly object sfxLock = new object();

        bool droneActive;
        bool isPaused;
        bool droneMuted;
        bool sfxMuted;
        float masterVolume = 0.4f;
        int spawnCountThisFrame;
        const int MAX_SPAWNS_PER_FRAME = 8;

        // Solution arpeggio frequencies: C4 -> E4 -> G4 -> C5
        static readonly float[] SOLUTION_ARPEGGIO = { 261.63f, 329.63f, 392.0f, 523.25f };
        // Final chord: C3 + E3 + G3 + C4
        static readonly float[] FINAL_CHORD = { 130.81f, 164.81f, 196.0f, 261.63f };

        enum SfxType { Spawn, Death, Fanfare, FinalChord }

        struct SfxEvent
        {
            public SfxType Type;
            public int AgentId;
        }

        class ActiveSfx
        {
            public SfxType Type;
            public float Freq;
            public float StartFreq;
            public double Phase;
            public float Elapsed;
            public float Duration;
            public float Attack;
            public float Decay;
            public float GlideTarget;
            public float Peak;
            public bool IsTriangle;
            public bool Done;
        }

        void Start()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.clip = AudioClip.Create("silence", (int)sampleRate, 1, (int)sampleRate, false);
            audioSource.Play();

            // Second AudioSource for soundtrack playback
            soundtrackSource = gameObject.AddComponent<AudioSource>();
            soundtrackSource.playOnAwake = false;
            soundtrackSource.loop = true;
            soundtrackSource.volume = 0f;

            // Auto-load soundtrack clips from Resources if not assigned
            if (soundtrackClips == null || soundtrackClips.Length == 0)
                soundtrackClips = Resources.LoadAll<AudioClip>("Soundtracks");
        }

        public string[] GetTrackNames()
        {
            if (soundtrackClips == null) return new string[0];
            var names = new string[soundtrackClips.Length];
            for (int i = 0; i < soundtrackClips.Length; i++)
                names[i] = soundtrackClips[i].name;
            return names;
        }

        public int CurrentTrackIndex => currentTrackIndex;

        public void SetTrack(int index)
        {
            if (soundtrackClips == null || soundtrackClips.Length == 0) return;
            currentTrackIndex = Mathf.Clamp(index, 0, soundtrackClips.Length - 1);
            bool wasPlaying = soundtrackSource.isPlaying;
            soundtrackSource.clip = soundtrackClips[currentTrackIndex];
            if (wasPlaying || (droneActive && droneMode == DroneMode.Soundtrack))
                soundtrackSource.Play();
        }

        public void CycleTrack()
        {
            if (soundtrackClips == null || soundtrackClips.Length == 0) return;
            SetTrack((currentTrackIndex + 1) % soundtrackClips.Length);
        }

        public void StartDrone()
        {
            droneActive = true;
            phase1 = 0;
            phase2 = 0;
            lfoPhase = 0;
            lpfCutoff = droneMode == DroneMode.Chord ? 1400f : 200f;
            harmonicCount = 0;
            harmonicTimer = 0;
            for (int i = 0; i < harmonicPhases.Length; i++)
            {
                harmonicPhases[i] = 0;
                harmonicGains[i] = 0;
            }
            for (int i = 0; i < chordPhases.Length; i++)
                chordPhases[i] = 0;
            ResetFilter();
            UpdateSoundtrackState();
        }

        public void StopDrone()
        {
            droneActive = false;
            if (soundtrackSource) soundtrackSource.Stop();
        }

        public void SetPaused(bool paused)
        {
            isPaused = paused;
            if (soundtrackSource && droneMode == DroneMode.Soundtrack)
            {
                if (paused) soundtrackSource.Pause();
                else soundtrackSource.UnPause();
            }
        }

        public void SetDroneMuted(bool muted)
        {
            droneMuted = muted;
            UpdateSoundtrackVolume();
        }

        public void SetSfxMuted(bool muted)
        {
            sfxMuted = muted;
        }

        public void SetDroneMode(DroneMode mode)
        {
            droneMode = mode;
            if (mode == DroneMode.Chord)
                lpfCutoff = Mathf.Max(lpfCutoff, 1400f);
            UpdateSoundtrackState();
        }

        public void SetDronePitchSemitones(int semitones)
        {
            pitchSemitones = Mathf.Clamp(semitones, -24, 24);
            droneBaseFreq = 65.41f * Mathf.Pow(2f, pitchSemitones / 12f);
        }

        public void SetDroneVolumePct(float pct)
        {
            float clamped = Mathf.Clamp(pct, 0f, 100f);
            // Oscillator modes: 0..0.12; soundtrack needs more headroom: 0..1.0
            droneVolume = droneMode == DroneMode.Soundtrack
                ? (clamped / 100f) * 1.0f
                : (clamped / 100f) * 0.12f;
            UpdateSoundtrackVolume();
        }

        void UpdateSoundtrackState()
        {
            if (!soundtrackSource) return;
            if (droneMode == DroneMode.Soundtrack && droneActive)
            {
                if (soundtrackClips != null && soundtrackClips.Length > 0)
                {
                    if (soundtrackSource.clip != soundtrackClips[currentTrackIndex])
                        soundtrackSource.clip = soundtrackClips[currentTrackIndex];
                    if (!soundtrackSource.isPlaying)
                        soundtrackSource.Play();
                }
                UpdateSoundtrackVolume();
            }
            else
            {
                soundtrackSource.Stop();
            }
        }

        void UpdateSoundtrackVolume()
        {
            if (!soundtrackSource) return;
            if (droneMuted || droneMode != DroneMode.Soundtrack)
                soundtrackSource.volume = 0f;
            else
                soundtrackSource.volume = droneVolume * masterVolume;
        }

        public void SetDroneWobblePct(float pct)
        {
            float clamped = Mathf.Clamp(pct, 0f, 100f);
            lfoDepth = (clamped / 100f) * 0.04f;
        }

        public void ProcessEvents(List<SolverEvent> events)
        {
            spawnCountThisFrame = 0;
            lock (sfxLock)
            {
                foreach (var evt in events)
                {
                    switch (evt.Type)
                    {
                        case SolverEventType.AgentSpawned:
                            if (!sfxMuted && spawnCountThisFrame < MAX_SPAWNS_PER_FRAME)
                            {
                                sfxQueue.Enqueue(new SfxEvent { Type = SfxType.Spawn, AgentId = evt.AgentId });
                                spawnCountThisFrame++;
                            }
                            break;
                        case SolverEventType.AgentDied:
                            if (!sfxMuted)
                                sfxQueue.Enqueue(new SfxEvent { Type = SfxType.Death, AgentId = evt.AgentId });
                            break;
                        case SolverEventType.SolutionFound:
                            sfxQueue.Enqueue(new SfxEvent { Type = SfxType.Fanfare });
                            break;
                    }
                }
            }
        }

        public void PlayFinalChord()
        {
            lock (sfxLock)
            {
                sfxQueue.Enqueue(new SfxEvent { Type = SfxType.FinalChord });
            }
        }

        public void UpdateIntensity(int activeCount, float explorationRatio)
        {
            lastActiveCount = activeCount;

            // Filter cutoff: 200->1200 Hz
            float cutoff = 200f + explorationRatio * 1000f;
            lpfCutoff = droneMode == DroneMode.Chord ? Mathf.Max(cutoff, 1200f) : cutoff;

            // LFO rate varies with activity
            float rate = Mathf.Clamp(activeCount, 1, 8);
            float norm = (rate - 1f) / 7f;
            lfoFreq = 0.5f + norm * 1.0f;

            // Chord mode: even active = minor [0,3,7], odd = major [0,4,7]
            if (droneMode == DroneMode.Chord)
            {
                if (activeCount % 2 == 0)
                {
                    chordIntervals[0] = 0; chordIntervals[1] = 3; chordIntervals[2] = 7;
                }
                else
                {
                    chordIntervals[0] = 0; chordIntervals[1] = 4; chordIntervals[2] = 7;
                }
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            // Process SFX queue
            lock (sfxLock)
            {
                while (sfxQueue.Count > 0)
                {
                    var evt = sfxQueue.Dequeue();
                    SpawnSfxOscillators(evt);
                }
            }

            float dt = 1f / (float)sampleRate;

            UpdateFilterCoefficients(lpfCutoff, lpfQ);

            for (int i = 0; i < data.Length; i += channels)
            {
                float sample = 0f;

                // Drone
                if (droneActive && !isPaused && !droneMuted)
                {
                    float drone = 0f;

                    if (droneMode == DroneMode.Mono)
                    {
                        // Two detuned triangle oscillators
                        float tri1 = Triangle(phase1);
                        phase1 += droneBaseFreq / sampleRate;
                        if (phase1 > 1.0) phase1 -= 1.0;

                        float freq2 = droneBaseFreq * Mathf.Pow(2f, 6f / 1200f);
                        float tri2 = Triangle(phase2);
                        phase2 += freq2 / sampleRate;
                        if (phase2 > 1.0) phase2 -= 1.0;

                        drone = (tri1 + tri2) * 0.5f;

                        // Harmonic partials
                        for (int h = 0; h < harmonicCount; h++)
                        {
                            int[] partials = { 3, 5, 7, 9, 11, 13 };
                            float hFreq = droneBaseFreq * partials[h];
                            float hSample = Mathf.Sin((float)(harmonicPhases[h] * 2.0 * Mathf.PI));
                            harmonicPhases[h] += hFreq / sampleRate;
                            if (harmonicPhases[h] > 1.0) harmonicPhases[h] -= 1.0;
                            drone += hSample * harmonicGains[h];
                        }

                        // Grow harmonics over time
                        harmonicTimer += dt;
                        if (harmonicTimer > harmonicInterval && harmonicCount < 6)
                        {
                            harmonicGains[harmonicCount] = 0.015f / (harmonicCount + 1);
                            harmonicCount++;
                            harmonicTimer = 0;
                        }
                    }
                    else // Chord
                    {
                        float sumW = chordWeights[0] + chordWeights[1] + chordWeights[2];
                        for (int c = 0; c < 3; c++)
                        {
                            float cFreq = droneBaseFreq * Mathf.Pow(2f, chordIntervals[c] / 12f);
                            float tri = Triangle(chordPhases[c]);
                            chordPhases[c] += cFreq / sampleRate;
                            if (chordPhases[c] > 1.0) chordPhases[c] -= 1.0;
                            drone += tri * (chordWeights[c] / sumW);
                        }
                    }

                    // LFO tremolo
                    float lfo = Mathf.Sin((float)(lfoPhase * 2.0 * Mathf.PI));
                    lfoPhase += lfoFreq / sampleRate;
                    if (lfoPhase > 1.0) lfoPhase -= 1.0;

                    drone *= (droneVolume + lfo * lfoDepth);

                    // Apply low-pass filter
                    drone = ApplyFilter(drone);

                    sample += drone;
                }

                // SFX oscillators
                for (int s = activeSfx.Count - 1; s >= 0; s--)
                {
                    var sfx = activeSfx[s];
                    if (sfx.Done) { activeSfx.RemoveAt(s); continue; }

                    sfx.Elapsed += dt;
                    if (sfx.Elapsed < 0) continue; // delayed start
                    if (sfx.Elapsed > sfx.Duration) { sfx.Done = true; activeSfx.RemoveAt(s); continue; }

                    // Envelope
                    float env;
                    if (sfx.Elapsed < sfx.Attack)
                        env = sfx.Elapsed / sfx.Attack * sfx.Peak;
                    else
                        env = sfx.Peak * Mathf.Pow(0.001f, (sfx.Elapsed - sfx.Attack) / sfx.Decay);

                    // Frequency glide
                    float t = sfx.Elapsed / sfx.Duration;
                    float freq = Mathf.Lerp(sfx.StartFreq, sfx.GlideTarget, t);

                    // Oscillator
                    float osc;
                    if (sfx.IsTriangle)
                        osc = Triangle(sfx.Phase);
                    else
                        osc = Mathf.Sin((float)(sfx.Phase * 2.0 * Mathf.PI));

                    sfx.Phase += freq / sampleRate;
                    if (sfx.Phase > 1.0) sfx.Phase -= 1.0;

                    sample += osc * env;
                }

                sample *= masterVolume;
                sample = Mathf.Clamp(sample, -1f, 1f);

                for (int ch = 0; ch < channels; ch++)
                    data[i + ch] = sample;
            }
        }

        void SpawnSfxOscillators(SfxEvent evt)
        {
            switch (evt.Type)
            {
                case SfxType.Spawn:
                {
                    float freq = AgentColorUtil.AgentIdToFreq(evt.AgentId);
                    activeSfx.Add(new ActiveSfx
                    {
                        Type = SfxType.Spawn,
                        Freq = freq, StartFreq = freq,
                        GlideTarget = freq * 1.5f,
                        Phase = 0, Elapsed = 0,
                        Attack = 0.02f, Decay = 0.2f, Duration = 0.24f,
                        Peak = 0.05f, IsTriangle = true
                    });
                    break;
                }
                case SfxType.Death:
                {
                    float freq = AgentColorUtil.AgentIdToFreq(evt.AgentId);
                    activeSfx.Add(new ActiveSfx
                    {
                        Type = SfxType.Death,
                        Freq = freq, StartFreq = freq,
                        GlideTarget = freq * 0.707f,
                        Phase = 0, Elapsed = 0,
                        Attack = 0.01f, Decay = 0.4f, Duration = 0.43f,
                        Peak = 0.03f, IsTriangle = false
                    });
                    break;
                }
                case SfxType.Fanfare:
                {
                    for (int n = 0; n < SOLUTION_ARPEGGIO.Length; n++)
                    {
                        float freq = SOLUTION_ARPEGGIO[n];
                        float delay = n * 0.08f;
                        activeSfx.Add(new ActiveSfx
                        {
                            Type = SfxType.Fanfare,
                            Freq = freq, StartFreq = freq,
                            GlideTarget = freq,
                            Phase = 0, Elapsed = -delay,
                            Attack = 0.02f, Decay = 1.0f, Duration = 1.5f + delay,
                            Peak = 0.08f, IsTriangle = false
                        });
                    }
                    break;
                }
                case SfxType.FinalChord:
                {
                    foreach (float freq in FINAL_CHORD)
                    {
                        activeSfx.Add(new ActiveSfx
                        {
                            Type = SfxType.FinalChord,
                            Freq = freq, StartFreq = freq,
                            GlideTarget = freq,
                            Phase = 0, Elapsed = 0,
                            Attack = 0.01f, Decay = 2.5f, Duration = 3.0f,
                            Peak = 0.07f, IsTriangle = false
                        });
                    }
                    break;
                }
            }
        }

        static float Triangle(double phase)
        {
            float p = (float)(phase % 1.0);
            return 4f * Mathf.Abs(p - 0.5f) - 1f;
        }

        void ResetFilter()
        {
            filtX1 = filtX2 = filtY1 = filtY2 = 0;
            UpdateFilterCoefficients(lpfCutoff, lpfQ);
        }

        void UpdateFilterCoefficients(float cutoff, float q)
        {
            float w0 = 2f * Mathf.PI * cutoff / (float)sampleRate;
            float cosw0 = Mathf.Cos(w0);
            float sinw0 = Mathf.Sin(w0);
            float alpha = sinw0 / (2f * q);

            float a0Inv = 1f / (1f + alpha);
            filtA0 = ((1f - cosw0) * 0.5f) * a0Inv;
            filtA1 = (1f - cosw0) * a0Inv;
            filtA2 = filtA0;
            filtB1 = (-2f * cosw0) * a0Inv;
            filtB2 = (1f - alpha) * a0Inv;
        }

        float ApplyFilter(float input)
        {
            float output = filtA0 * input + filtA1 * filtX1 + filtA2 * filtX2
                         - filtB1 * filtY1 - filtB2 * filtY2;
            filtX2 = filtX1;
            filtX1 = input;
            filtY2 = filtY1;
            filtY1 = output;
            return output;
        }

        public void StopAll()
        {
            droneActive = false;
            if (soundtrackSource) soundtrackSource.Stop();
            lock (sfxLock)
            {
                sfxQueue.Clear();
            }
            activeSfx.Clear();
        }
    }
}
