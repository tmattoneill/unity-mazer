using System.Collections.Generic;
using UnityEngine;

namespace MazeSolver
{
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
        float masterVolume = 0.4f;
        int spawnCountThisFrame;
        const int MAX_SPAWNS_PER_FRAME = 8;

        // Solution arpeggio frequencies: C4 → E4 → G4 → C5
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
            public bool IsTriangle; // vs sine
            public bool Done;
        }

        void Start()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            // Dummy clip to keep OnAudioFilterRead firing
            audioSource.clip = AudioClip.Create("silence", (int)sampleRate, 1, (int)sampleRate, false);
            audioSource.Play();
        }

        public void StartDrone()
        {
            droneActive = true;
            phase1 = 0;
            phase2 = 0;
            lfoPhase = 0;
            lpfCutoff = 200f;
            ResetFilter();
        }

        public void StopDrone()
        {
            droneActive = false;
        }

        public void SetPaused(bool paused)
        {
            isPaused = paused;
        }

        public void ResetSpawnCount()
        {
            spawnCountThisFrame = 0;
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
                            if (spawnCountThisFrame < MAX_SPAWNS_PER_FRAME)
                            {
                                sfxQueue.Enqueue(new SfxEvent { Type = SfxType.Spawn, AgentId = evt.AgentId });
                                spawnCountThisFrame++;
                            }
                            break;
                        case SolverEventType.AgentDied:
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
            // Filter cutoff: 200→1200 Hz
            lpfCutoff = 200f + explorationRatio * 1000f;

            // LFO rate varies with activity
            float rate = Mathf.Clamp(activeCount, 1, 8);
            float norm = (rate - 1f) / 7f;
            lfoFreq = 0.5f + norm * 1.0f;
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
                if (droneActive && !isPaused)
                {
                    // Triangle osc 1
                    float tri1 = Triangle(phase1);
                    phase1 += droneBaseFreq / sampleRate;
                    if (phase1 > 1.0) phase1 -= 1.0;

                    // Triangle osc 2 (6 cents detuned)
                    float freq2 = droneBaseFreq * Mathf.Pow(2f, 6f / 1200f);
                    float tri2 = Triangle(phase2);
                    phase2 += freq2 / sampleRate;
                    if (phase2 > 1.0) phase2 -= 1.0;

                    float drone = (tri1 + tri2) * 0.5f;

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
                        Freq = freq,
                        StartFreq = freq,
                        GlideTarget = freq * 1.5f,
                        Phase = 0,
                        Elapsed = 0,
                        Attack = 0.02f,
                        Decay = 0.2f,
                        Duration = 0.24f,
                        Peak = 0.05f,
                        IsTriangle = true
                    });
                    break;
                }
                case SfxType.Death:
                {
                    float freq = AgentColorUtil.AgentIdToFreq(evt.AgentId);
                    activeSfx.Add(new ActiveSfx
                    {
                        Type = SfxType.Death,
                        Freq = freq,
                        StartFreq = freq,
                        GlideTarget = freq * 0.707f,
                        Phase = 0,
                        Elapsed = 0,
                        Attack = 0.01f,
                        Decay = 0.4f,
                        Duration = 0.43f,
                        Peak = 0.03f,
                        IsTriangle = false
                    });
                    break;
                }
                case SfxType.Fanfare:
                {
                    for (int i = 0; i < SOLUTION_ARPEGGIO.Length; i++)
                    {
                        float freq = SOLUTION_ARPEGGIO[i];
                        float delay = i * 0.08f;
                        activeSfx.Add(new ActiveSfx
                        {
                            Type = SfxType.Fanfare,
                            Freq = freq,
                            StartFreq = freq,
                            GlideTarget = freq,
                            Phase = 0,
                            Elapsed = -delay, // negative = delayed start
                            Attack = 0.02f,
                            Decay = 1.0f,
                            Duration = 1.5f + delay,
                            Peak = 0.08f,
                            IsTriangle = false
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
                            Freq = freq,
                            StartFreq = freq,
                            GlideTarget = freq,
                            Phase = 0,
                            Elapsed = 0,
                            Attack = 0.01f,
                            Decay = 2.5f,
                            Duration = 3.0f,
                            Peak = 0.07f,
                            IsTriangle = false
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
            lock (sfxLock)
            {
                sfxQueue.Clear();
            }
            activeSfx.Clear();
        }
    }
}
