using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MazeSolver
{
    public sealed class MazeAudioEngine : MonoBehaviour
    {
        public OrchestralScoreRules scoreRules;
        public OrchestralInstrumentBank instrumentBank;
        public AudioClip[] soundtrackClips;
        public MusicSettings Settings { get; private set; } = MusicSettings.Default;
        public bool OrchestraAvailable { get; private set; }
        public string Diagnostic { get; private set; }
        public int CurrentTrackIndex { get; private set; }
        public int ActiveVoices => renderer?.ActiveVoices ?? 0;
        public int MusicBeat => renderer?.Beat ?? 0;
        public int CurrentSeed { get; private set; }
        public bool NewSeedEachRun { get; set; } = true;
        public int SelectedSeed { get; set; } = 431;
        public bool CanSaveRecording => latestRecording != null;
        public string RecordingStatus { get; private set; } = "Complete a run to save its WAV.";
        public string RecordingsDirectory => Path.Combine(Application.persistentDataPath, "Recordings");
        public string LastSavedPath { get; private set; }
        SessionRecording activeRecording, latestRecording;
        readonly List<SessionRecording> retiredRecordings = new List<SessionRecording>();
        OrchestraRenderer renderer;
        InstrumentBankData bank;
        AudioSource orchestraSource, soundtrackSource;
        OrchestraOutput output;
        AudioClip silence;
        bool sessionActive, paused;
        int births, deaths;
        readonly Queue<AudioCommand> pending = new Queue<AudioCommand>(32);
        const int MaxPending = 128;

        void Awake()
        {
            // Remove the legacy source: each callback must belong to a single dedicated source.
            var legacy = GetComponent<AudioSource>();
            if (legacy) { legacy.Stop(); legacy.enabled = false; }
            if (!scoreRules) scoreRules = Resources.Load<OrchestralScoreRules>("Orchestra/ScoreRules");
            if (!instrumentBank) instrumentBank = Resources.Load<OrchestralInstrumentBank>("Orchestra/InstrumentBank");
            try
            {
                if (!scoreRules || !instrumentBank) throw new InvalidOperationException("Orchestral score or instrument bank asset is missing.");
                bank = instrumentBank.Load();
                renderer = new OrchestraRenderer(bank, scoreRules, AudioSettings.outputSampleRate);
                var child = new GameObject("Orchestral sampler");
                child.transform.SetParent(transform, false);
                orchestraSource = child.AddComponent<AudioSource>();
                orchestraSource.playOnAwake = false; orchestraSource.loop = true; orchestraSource.spatialBlend = 0;
                output = child.AddComponent<OrchestraOutput>();
                output.Renderer = renderer;
                silence = AudioClip.Create("Orchestra transport", 1024, 1, AudioSettings.outputSampleRate, false);
                orchestraSource.clip = silence;
                orchestraSource.Play();
                OrchestraAvailable = true;
                Debug.Log($"Orchestra ready: {bank.Samples.Length} samples, {bank.MemoryBytes / 1048576f:0.0} MiB PCM.");
            }
            catch (Exception exception)
            {
                Diagnostic = exception.Message;
                Debug.LogError("Orchestra unavailable: " + exception.Message);
            }
            var soundtrack = new GameObject("Recorded soundtrack");
            soundtrack.transform.SetParent(transform, false);
            soundtrackSource = soundtrack.AddComponent<AudioSource>();
            soundtrackSource.playOnAwake = false; soundtrackSource.loop = true; soundtrackSource.volume = 0;
            if (soundtrackClips == null || soundtrackClips.Length == 0)
                soundtrackClips = Resources.LoadAll<AudioClip>("Soundtracks");
            Array.Sort(soundtrackClips, (a, b) => string.CompareOrdinal(a.name, b.name));
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void Update()
        {
            Flush();
            PollRecording();
            float target = sessionActive && !paused && Settings.Mode == MusicMode.Soundtrack && !Settings.MusicMuted
                ? Settings.Volume * 0.65f : 0;
            soundtrackSource.volume = Mathf.MoveTowards(soundtrackSource.volume, target, Time.unscaledDeltaTime * 3f);
            if (Settings.Mode == MusicMode.Soundtrack && sessionActive && !paused && !soundtrackSource.isPlaying)
                StartSoundtrack();
            if ((!sessionActive || Settings.Mode != MusicMode.Soundtrack) && soundtrackSource.volume <= 0.001f)
                soundtrackSource.Stop();
            if (paused && soundtrackSource.volume <= 0.001f && soundtrackSource.isPlaying)
                soundtrackSource.Pause();
        }

        void Send(AudioCommand command)
        {
            if (renderer == null) return;
            Flush();
            if (pending.Count == 0 && renderer.Commands.TryWrite(command)) return;
            // Overload replaces intermediate snapshots/settings, never lifecycle commands.
            if (pending.Count >= MaxPending)
            {
                if (command.Type == AudioCommandType.Snapshot || command.Type == AudioCommandType.Settings) return;
                pending.Clear();
                pending.Enqueue(new AudioCommand { Type = AudioCommandType.Stop });
            }
            pending.Enqueue(command);
        }
        void Flush()
        {
            while (renderer != null && pending.Count > 0 && renderer.Commands.TryWrite(pending.Peek())) pending.Dequeue();
        }

        public void StartSession()
        {
            int seed = NewSeedEachRun ? UnityEngine.Random.Range(1, int.MaxValue) : SelectedSeed;
            SelectedSeed = seed;
            StartSession(seed, Settings);
        }

        public void StartSession(int seed, MusicSettings settings)
        {
            PollRecording();
            CancelRecording();
            CurrentSeed = seed;
            sessionActive = true; paused = false; births = deaths = 0;
            ApplySettings(settings);
            if (Settings.Mode == MusicMode.Orchestra)
            {
                try
                {
                    activeRecording = new SessionRecording(Path.Combine(Application.temporaryCachePath, "OrchestraTakes"), AudioSettings.outputSampleRate, seed, Settings);
                    RecordingStatus = "Recording orchestra...";
                }
                catch (Exception exception) { RecordingStatus = "Recording unavailable: " + exception.Message; }
            }
            Send(new AudioCommand { Type = AudioCommandType.Start, Value = seed, Settings = Settings, Recording = activeRecording });
            if (Settings.Mode == MusicMode.Soundtrack) StartSoundtrack();
        }
        public void ProcessEvents(List<SolverEvent> events)
        {
            foreach (var evt in events)
            {
                if (evt.Type == SolverEventType.AgentSpawned) births++;
                else if (evt.Type == SolverEventType.AgentDied) deaths++;
            }
        }
        public void SubmitSnapshot(int active, float coverage)
        {
            Send(new AudioCommand
            {
                Type = AudioCommandType.Snapshot,
                Snapshot = new MazeMusicSnapshot { Active = active, Coverage = coverage, Births = births, Deaths = deaths }
            });
            births = deaths = 0;
        }
        public void CompleteSession(SessionOutcome outcome)
        {
            Send(new AudioCommand { Type = AudioCommandType.Complete, Value = (int)outcome });
            // The procedural transport owns its cadence; recorded tracks fade out independently.
            sessionActive = false;
        }
        public void StopSession()
        {
            PollRecording();
            CancelRecording();
            Send(new AudioCommand { Type = AudioCommandType.Stop });
            sessionActive = false; paused = false; births = deaths = 0;
        }
        public void SetPaused(bool value)
        {
            paused = value;
            Send(new AudioCommand { Type = AudioCommandType.Pause, Value = value ? 1 : 0 });
            if (!paused && Settings.Mode == MusicMode.Soundtrack) soundtrackSource.UnPause();
        }
        public void ApplySettings(MusicSettings value)
        {
            value.Volume = Mathf.Clamp01(value.Volume); value.AccentVolume = Mathf.Clamp01(value.AccentVolume);
            value.Energy = Mathf.Clamp01(value.Energy); value.Density = Mathf.Clamp01(value.Density);
            value.Variation = Mathf.Clamp01(value.Variation); value.Tonic = Mathf.Clamp(value.Tonic, 0, 11);
            if (value.Tonality != Tonality.Major) value.Tonality = Tonality.Minor;
            if (value.Mode == MusicMode.Soundtrack && Settings.Mode != MusicMode.Soundtrack) { PollRecording(); CancelRecording(); }
            value.Hall = Mathf.Clamp01(value.Hall); value.Tempo = Mathf.Clamp(value.Tempo, 92, 132);
            if (!OrchestraAvailable && value.Mode == MusicMode.Orchestra) value.Mode = MusicMode.Soundtrack;
            Settings = value;
            Send(new AudioCommand { Type = AudioCommandType.Settings, Settings = value });
        }
        void CancelRecording()
        {
            if (activeRecording == null) return;
            activeRecording.Cancel();
            retiredRecordings.Add(activeRecording);
            activeRecording = null;
            RecordingStatus = latestRecording != null ? "Previous performance ready to save." : "Unfinished recording discarded.";
        }

        void PollRecording()
        {
            if (activeRecording != null)
            {
                if (activeRecording.State == RecordingState.Ready)
                {
                    if (latestRecording != null) retiredRecordings.Add(latestRecording);
                    latestRecording = activeRecording; activeRecording = null;
                    RecordingStatus = "Ready to save: " + latestRecording.FileStem;
                }
                else if (activeRecording.State == RecordingState.Failed || activeRecording.State == RecordingState.Cancelled)
                {
                    RecordingStatus = activeRecording.Error ?? "Unfinished recording discarded.";
                    retiredRecordings.Add(activeRecording); activeRecording = null;
                }
                else if (activeRecording.State == RecordingState.Finalising) RecordingStatus = "Finishing WAV...";
            }
            for (int i = retiredRecordings.Count - 1; i >= 0; i--)
            {
                var retired = retiredRecordings[i];
                if (!retired.WorkerFinished) continue;
                try { if (File.Exists(retired.TemporaryPath)) File.Delete(retired.TemporaryPath); }
                catch (Exception exception) { Debug.LogWarning("Temporary recording cleanup: " + exception.Message); }
                retiredRecordings.RemoveAt(i);
            }
        }

        public void SaveRecording()
        {
            PollRecording();
            if (latestRecording == null) return;
            string destination = null;
            bool created = false;
            try
            {
                Directory.CreateDirectory(RecordingsDirectory);
                destination = Path.Combine(RecordingsDirectory, latestRecording.FileStem + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
                using (var source = File.OpenRead(latestRecording.TemporaryPath))
                using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                {
                    created = true;
                    source.CopyTo(target);
                }
                LastSavedPath = destination;
                RecordingStatus = "Saved: " + destination;
            }
            catch (Exception exception)
            {
                RecordingStatus = "Could not save WAV: " + exception.Message;
                if (created)
                    try { File.Delete(destination); }
                    catch (Exception cleanupError) { Debug.LogWarning("Incomplete export cleanup: " + cleanupError.Message); }
            }
        }

        public void ShowRecordingsFolder()
        {
            try
            {
                Directory.CreateDirectory(RecordingsDirectory);
                Application.OpenURL(new Uri(RecordingsDirectory + Path.DirectorySeparatorChar).AbsoluteUri);
            }
            catch (Exception exception) { RecordingStatus = "Could not open folder: " + exception.Message; }
        }

        void StartSoundtrack()
        {
            if (soundtrackClips.Length == 0) return;
            soundtrackSource.clip = soundtrackClips[CurrentTrackIndex];
            soundtrackSource.Play();
        }
        public string[] GetTrackNames()
        {
            var names = new string[soundtrackClips.Length];
            for (int i = 0; i < names.Length; i++) names[i] = soundtrackClips[i].name;
            return names;
        }
        public void CycleTrack()
        {
            if (soundtrackClips.Length == 0) return;
            CurrentTrackIndex = (CurrentTrackIndex + 1) % soundtrackClips.Length;
            if (sessionActive && Settings.Mode == MusicMode.Soundtrack && !paused) StartSoundtrack();
        }
        void OnAudioConfigurationChanged(bool changed)
        {
            if (bank == null) return;
            // Device changes invalidate the sample clock. End the run's audio cleanly and rebuild at the new rate.
            PollRecording(); CancelRecording();
            orchestraSource.Stop();
            pending.Clear(); sessionActive = false; paused = false;
            renderer = new OrchestraRenderer(bank, scoreRules, AudioSettings.outputSampleRate);
            output.Renderer = renderer;
            orchestraSource.Play();
        }
        void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (orchestraSource) orchestraSource.Stop();
            CancelRecording();
            if (latestRecording != null) { retiredRecordings.Add(latestRecording); latestRecording = null; }
            foreach (var recording in retiredRecordings) recording.Cancel();
            foreach (var recording in retiredRecordings) recording.WaitForWriter(250);
            PollRecording();
            if (silence) Destroy(silence);
        }
    }
}
