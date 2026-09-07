#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace MazeSolver
{
    public sealed class MemoryAuditRunner : MonoBehaviour
    {
        [Serializable]
        sealed class AuditReport
        {
            public string applicationVersion;
            public string unityVersion;
            public string platform;
            public string operatingSystem;
            public string deviceModel;
            public int systemMemoryMiB;
            public string startedUtc;
            public bool smokeOnly;
            public bool passed;
            public string[] failures;
            public List<MemoryCheckpoint> checkpoints = new List<MemoryCheckpoint>();
        }

        [Serializable]
        sealed class MemoryCheckpoint
        {
            public string name;
            public int frame;
            public float elapsedSeconds;
            public long managedUsedBytes;
            public long unityAllocatedBytes;
            public long unityReservedBytes;
            public long graphicsDriverBytes;
            public long gcHeapBytes;
            public int textureCount;
            public int spriteCount;
            public int recordingWorkers;
            public int pendingRecordings;
            public int temporaryWavFiles;
            public int loadedSoundtracks;
        }

        const long DriftBudgetBytes = 16L * 1024 * 1024;
        readonly AuditReport report = new AuditReport();
        readonly List<string> failures = new List<string>();
        string outputPath;
        MazeGameManager manager;
        MazeAudioEngine audioEngine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!HasArgument(arguments, "--memory-audit") && !HasArgument(arguments, "--memory-audit-smoke")) return;
            var host = new GameObject("Memory audit runner");
            DontDestroyOnLoad(host);
            host.AddComponent<MemoryAuditRunner>();
        }

        IEnumerator Start()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            report.applicationVersion = Application.version;
            report.unityVersion = Application.unityVersion;
            report.platform = Application.platform.ToString();
            report.operatingSystem = SystemInfo.operatingSystem;
            report.deviceModel = SystemInfo.deviceModel;
            report.systemMemoryMiB = SystemInfo.systemMemorySize;
            report.startedUtc = DateTime.UtcNow.ToString("O");
            report.smokeOnly = HasArgument(arguments, "--memory-audit-smoke");
            outputPath = ReadOutputPath(arguments);

            float deadline = Time.realtimeSinceStartup + 120f;
            while ((!manager || !audioEngine || !audioEngine.OrchestraAvailable) && Time.realtimeSinceStartup < deadline)
            {
                manager = FindAnyObjectByType<MazeGameManager>();
                audioEngine = manager ? manager.AudioEngine : null;
                yield return null;
            }
            if (!manager || !audioEngine || !audioEngine.OrchestraAvailable)
            {
                failures.Add("Application did not reach an orchestra-ready state within 120 seconds.");
                Finish();
                yield break;
            }

            yield return new WaitForSecondsRealtime(3f);
            yield return Collect("startup-settled");
            if (report.smokeOnly)
            {
                Finish();
                yield break;
            }

            // Establish the one retained display texture before measuring replacement drift.
            manager.uiController.mazeSizeSlider.SetValueWithoutNotify(100);
            manager.uiController.targetPathsSlider.SetValueWithoutNotify(5);
            manager.GenerateAndSolve();
            yield return null;
            manager.ResetMaze();
            yield return Collect("maze-warm");
            MemoryCheckpoint mazeBaseline = LastCheckpoint;

            for (int cycle = 1; cycle <= 100; cycle++)
            {
                manager.GenerateAndSolve();
                yield return null;
                manager.ResetMaze();
                yield return null;
                if (cycle % 10 == 0) yield return Collect("maze-cycle-" + cycle);
            }
            CheckDrift("maze cycles", mazeBaseline, LastCheckpoint);
            CheckStableObjects("maze cycles", mazeBaseline, LastCheckpoint);

            var orchestra = audioEngine.Settings;
            orchestra.Mode = MusicMode.Orchestra;
            audioEngine.ApplySettings(orchestra);
            yield return Collect("recording-warm");
            MemoryCheckpoint recordingBaseline = LastCheckpoint;
            for (int cycle = 1; cycle <= 50; cycle++)
            {
                audioEngine.StartSession(431 + cycle, orchestra);
                yield return null;
                yield return null;
                audioEngine.StopSession();
                yield return null;
                if (cycle % 10 == 0) yield return Collect("recording-cycle-" + cycle);
            }
            float workerDeadline = Time.realtimeSinceStartup + 10f;
            while (MazeAudioEngine.ActiveRecordingWorkers > 0 && Time.realtimeSinceStartup < workerDeadline) yield return null;
            yield return Collect("recording-final");
            CheckDrift("recording cycles", recordingBaseline, LastCheckpoint);
            if (LastCheckpoint.recordingWorkers != 0) failures.Add("Recording workers remained alive after cancellation.");
            if (LastCheckpoint.temporaryWavFiles != recordingBaseline.temporaryWavFiles) failures.Add("Temporary WAV files did not return to baseline.");

            var soundtrack = orchestra;
            soundtrack.Mode = MusicMode.Soundtrack;
            audioEngine.StartSession(431, soundtrack);
            int trackCycles = Math.Max(1, audioEngine.GetTrackNames().Length * 2);
            for (int cycle = 0; cycle < trackCycles; cycle++)
            {
                audioEngine.CycleTrack();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            audioEngine.StopSession();
            yield return new WaitForSecondsRealtime(1f);
            yield return Collect("soundtracks-final");
            if (LastCheckpoint.loadedSoundtracks != 0) failures.Add("Soundtrack PCM remained loaded after the session stopped.");

            for (int rebuild = 0; rebuild < 25; rebuild++)
            {
                audioEngine.RebuildAudioRendererForAudit();
                yield return null;
            }
            yield return Collect("renderer-rebuild-final");
            if (LastCheckpoint.recordingWorkers != 0) failures.Add("Audio renderer rebuilds left recording workers alive.");

            manager.ResetMaze();
            yield return Collect("final");
            Finish();
        }

        MemoryCheckpoint LastCheckpoint => report.checkpoints[report.checkpoints.Count - 1];

        IEnumerator Collect(string name)
        {
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            yield return null;
            report.checkpoints.Add(Capture(name));
        }

        MemoryCheckpoint Capture(string name)
        {
            var clips = audioEngine.soundtrackClips ?? Array.Empty<AudioClip>();
            int loadedSoundtracks = 0;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] && clips[i].loadState == AudioDataLoadState.Loaded) loadedSoundtracks++;
            string directory = Path.Combine(Application.temporaryCachePath, "OrchestraTakes");
            return new MemoryCheckpoint
            {
                name = name,
                frame = Time.frameCount,
                elapsedSeconds = Time.realtimeSinceStartup,
                managedUsedBytes = Profiler.GetMonoUsedSizeLong(),
                unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                unityReservedBytes = Profiler.GetTotalReservedMemoryLong(),
                graphicsDriverBytes = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                gcHeapBytes = GC.GetTotalMemory(false),
                textureCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length,
                spriteCount = Resources.FindObjectsOfTypeAll<Sprite>().Length,
                recordingWorkers = MazeAudioEngine.ActiveRecordingWorkers,
                pendingRecordings = audioEngine.PendingRecordingCount,
                temporaryWavFiles = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.wav").Length : 0,
                loadedSoundtracks = loadedSoundtracks
            };
        }

        void CheckDrift(string scenario, MemoryCheckpoint baseline, MemoryCheckpoint final)
        {
            long managedDrift = final.managedUsedBytes - baseline.managedUsedBytes;
            long unityDrift = final.unityAllocatedBytes - baseline.unityAllocatedBytes;
            if (managedDrift > DriftBudgetBytes || unityDrift > DriftBudgetBytes)
                failures.Add(scenario + " exceeded the 16 MiB settled-memory drift budget.");
        }

        void CheckStableObjects(string scenario, MemoryCheckpoint baseline, MemoryCheckpoint final)
        {
            if (final.textureCount != baseline.textureCount || final.spriteCount != baseline.spriteCount)
                failures.Add(scenario + " changed the settled Texture2D or Sprite count.");
        }

        void Finish()
        {
            report.failures = failures.ToArray();
            report.passed = failures.Count == 0;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            Debug.Log("MEMORY AUDIT " + (report.passed ? "PASSED" : "FAILED") + ": " + outputPath);
#if !UNITY_EDITOR
            Application.Quit(report.passed ? 0 : 2);
#endif
        }

        static string ReadOutputPath(string[] arguments)
        {
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] != "--memory-audit-output") continue;
                string candidate = Path.GetFullPath(arguments[i + 1]);
                if (Path.IsPathRooted(candidate)) return candidate;
            }
            return Path.Combine(Application.temporaryCachePath, "MemoryAudits", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        }

        static bool HasArgument(string[] arguments, string value)
        {
            for (int i = 0; i < arguments.Length; i++) if (arguments[i] == value) return true;
            return false;
        }
    }
}
#endif
