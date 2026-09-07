using System;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MazeSolver.Editor
{
    public static class BuildOrchestra
    {
        [MenuItem("Tools/Orchestra/Prepare and validate")]
        public static void PrepareAndValidate()
        {
            AssetDatabase.Refresh();
            var rules = AssetDatabase.LoadAssetAtPath<OrchestralScoreRules>("Assets/Resources/Orchestra/ScoreRules.asset");
            if (!rules)
            {
                rules = ScriptableObject.CreateInstance<OrchestralScoreRules>();
                AssetDatabase.CreateAsset(rules, "Assets/Resources/Orchestra/ScoreRules.asset");
            }
            var ambient = StyleRules("Ambient", new float[] { 0.5f, 0.7f, 0.4f, 0.75f }, null);
            var edm = StyleRules("Edm", new float[] { 0.7f, 1f, 0.6f, 1f }, new[] { 0, 0, 3, 4, 0, 5, 4, 2 });
            var classical = StyleRules("Classical", new float[] { 0.5f, 0.8f, 0.6f, 1f }, null);
            var bankAsset = AssetDatabase.LoadAssetAtPath<OrchestralInstrumentBank>("Assets/Resources/Orchestra/InstrumentBank.asset");
            if (!bankAsset)
            {
                bankAsset = ScriptableObject.CreateInstance<OrchestralInstrumentBank>();
                AssetDatabase.CreateAsset(bankAsset, "Assets/Resources/Orchestra/InstrumentBank.asset");
            }
            bankAsset.Manifest = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Orchestra/manifest.json");
            // A ScriptableObject without a matching script asset works in-memory but fails in players.
            foreach (ScriptableObject asset in new ScriptableObject[] { rules, ambient, edm, classical, bankAsset })
            {
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("m_Script").objectReferenceValue = MonoScript.FromScriptableObject(asset);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(serialized.FindProperty("m_Script").objectReferenceValue != null, "missing serialized script reference");
                EditorUtility.SetDirty(asset);
            }
            EditorUtility.SetDirty(bankAsset);
            AssetDatabase.SaveAssets();
            CreateMazeSolverScene.Create();
            PresolveValidation.Run();
            Validate(rules, bankAsset);
            ValidateStyles(new StyleRuleBundle(rules) { Ambient = ambient, EDM = edm, Classical = classical }, bankAsset);
        }

        static OrchestralScoreRules StyleRules(string name, float[] sectionEnergy, int[] motif)
        {
            string path = "Assets/Resources/Orchestra/" + name + "ScoreRules.asset";
            var asset = AssetDatabase.LoadAssetAtPath<OrchestralScoreRules>(path);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<OrchestralScoreRules>();
                asset.SectionEnergy = sectionEnergy;
                if (motif != null) asset.MotifDegrees = motif;
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        // Every style must satisfy the renderer-level contract: seeded determinism,
        // allocation-free rendering, a completing cadence and a clean stop.
        public static void ValidateStyles(StyleRuleBundle bundle, OrchestralInstrumentBank bankAsset)
        {
            var bank = bankAsset.Load();
            OrchestraFeatureValidation.RunStyles(bundle);
            float[] a = new float[1024], b = new float[1024];
            foreach (MusicStyle style in Enum.GetValues(typeof(MusicStyle)))
            {
                var settings = MusicSettings.Default;
                settings.Style = style; settings.Density = 1; settings.Energy = 0.8f;
                var first = new OrchestraRenderer(bank, bundle, 44100);
                var second = new OrchestraRenderer(bank, bundle, 44100);
                var start = new AudioCommand { Type = AudioCommandType.Start, Value = 431, Settings = settings };
                first.Commands.TryWrite(start); second.Commands.TryWrite(start);
                for (int block = 0; block < 500; block++)
                {
                    if (block % 25 == 0)
                        first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Snapshot,
                            Snapshot = new MazeMusicSnapshot { Active = 4 + block / 25, Births = 1, Deaths = 1, Coverage = block / 500f } });
                    if (block % 25 == 0)
                        second.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Snapshot,
                            Snapshot = new MazeMusicSnapshot { Active = 4 + block / 25, Births = 1, Deaths = 1, Coverage = block / 500f } });
                    first.Render(a, 2); second.Render(b, 2);
                    for (int i = 0; i < a.Length; i++)
                    {
                        Require(a[i] == b[i], style + " seeded audio differs");
                        Require(!float.IsNaN(a[i]) && Math.Abs(a[i]) <= 0.891f, style + " output clipped or NaN");
                    }
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int block = 0; block < 100; block++) first.Render(a, 2);
                Require(GC.GetAllocatedBytesForCurrentThread() == before, style + " allocated inside render");
                first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Complete, Value = (int)SessionOutcome.Solved });
                for (int block = 0; block < 900; block++) first.Render(a, 2);
                Require(first.Finished, style + " cadence did not complete");
                first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Stop });
                for (int block = 0; block < 400; block++) first.Render(a, 2);
                Require(first.ActiveVoices == 0, style + " stop left voices playing");
            }
            Debug.Log("STYLE VALIDATION PASSED: 4 styles, deterministic, allocation-free, completing cadences, clean stops.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Orchestra validation: " + message);
        }

        public static void Validate(OrchestralScoreRules rules, OrchestralInstrumentBank bankAsset)
        {
            OrchestraFeatureValidation.Run(rules);
            var bank = bankAsset.Load();
            Require(bank.MemoryBytes <= 256L * 1048576, "PCM bank exceeds budget");
            var score = new CinematicComposer(rules);
            score.Reset(431);
            var notes = new ScoreNote[64];
            for (int beat = 0; beat < 256; beat++)
            {
                score.Observe(new MazeMusicSnapshot { Active = beat % 64, Births = 100, Deaths = 100, Coverage = beat / 256f });
                int count = score.ComposeBeat(MusicSettings.Default, notes), accents = 0;
                Require(count > 0 && count < notes.Length, "unbounded or empty beat");
                for (int i = 0; i < count; i++)
                {
                    Require(notes[i].Pitch >= 28 && notes[i].Pitch <= 86, "instrument range");
                    Require(notes[i].OffsetBeats >= 0 && notes[i].OffsetBeats < 1, "note outside beat");
                    if (notes[i].Accent) accents++;
                }
                Require(accents <= 2, "too many event accents");
            }
            var first = new OrchestraRenderer(bank, rules, 44100);
            var second = new OrchestraRenderer(bank, rules, 44100);
            var start = new AudioCommand { Type = AudioCommandType.Start, Value = 431, Settings = MusicSettings.Default };
            first.Commands.TryWrite(start); second.Commands.TryWrite(start);
            float[] a = new float[1024], b = new float[1024];
            for (int block = 0; block < 1000; block++)
            {
                first.Render(a, 2); second.Render(b, 2);
                for (int i = 0; i < a.Length; i++) Require(a[i] == b[i], "seeded audio differs");
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int block = 0; block < 100; block++) first.Render(a, 2);
            Require(GC.GetAllocatedBytesForCurrentThread() == before, "allocation inside audio render");
            first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Pause, Value = 1 });
            for (int block = 0; block < 100; block++) first.Render(a, 2);
            int heldBeat = first.Beat;
            for (int block = 0; block < 100; block++) first.Render(a, 2);
            Require(first.Beat == heldBeat, "paused transport advanced");
            for (int i = 0; i < a.Length; i++) Require(Math.Abs(a[i]) < 0.0001f, "pause is audible");
            first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Pause, Value = 0 });
            first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Complete, Value = (int)SessionOutcome.Solved });
            for (int block = 0; block < 600; block++) first.Render(a, 2);
            Require(first.Finished, "cadence did not complete");
            first.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Stop });
            for (int block = 0; block < 400; block++) first.Render(a, 2);
            Require(first.ActiveVoices == 0, "stop left voices playing");
            // Stress the renderer at full density, with rapid lifecycle changes and a different output rate.
            var stress = new OrchestraRenderer(bank, rules, 48000);
            var loud = MusicSettings.Default; loud.Volume = 1; loud.AccentVolume = 1; loud.Density = 1; loud.Energy = 1;
            stress.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Start, Value = 19, Settings = loud });
            for (int block = 0; block < 1500; block++)
            {
                if (block % 20 == 0) stress.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Snapshot,
                    Snapshot = new MazeMusicSnapshot { Active = 10000, Births = 10000, Deaths = 10000, Coverage = 1 } });
                stress.Render(a, block % 2 == 0 ? 1 : 2);
                for (int i = 0; i < a.Length; i++) Require(!float.IsNaN(a[i]) && Math.Abs(a[i]) <= 0.891f, "stress render clipped");
            }
            for (int repeat = 0; repeat < 10; repeat++)
            {
                stress.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Start, Value = repeat, Settings = loud });
                stress.Render(a, 2);
                stress.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Stop });
                stress.Render(a, 2);
            }
            var muted = loud; muted.MusicMuted = true; muted.AccentsMuted = true;
            stress.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Settings, Settings = muted });
            for (int block = 0; block < 500; block++) stress.Render(a, 2);
            for (int i = 0; i < a.Length; i++) Require(Math.Abs(a[i]) < 0.0001f, "muted output is audible");
            Directory.CreateDirectory("Builds/Audio");
            RenderDemo(bank, rules, "Builds/Audio/Orchestra-demo.wav", MusicSettings.Default, 431);
            var alternative = MusicSettings.Default;
            RenderDemo(bank, rules, "Builds/Audio/Orchestra-seed-432.wav", alternative, 432);
            alternative.Tonic = 10; alternative.Variation = 0.8f;
            RenderDemo(bank, rules, "Builds/Audio/Orchestra-Bb-minor.wav", alternative, 1729);
            alternative.Tonic = 0; alternative.Tonality = Tonality.Major; alternative.Variation = 0;
            RenderDemo(bank, rules, "Builds/Audio/Orchestra-C-major.wav", alternative, 431);
            Debug.Log($"ORCHESTRA VALIDATION PASSED: {bank.Samples.Length} samples; {bank.MemoryBytes / 1048576f:0.0} MiB PCM; deterministic audio, allocation-free render, pause, cadence, stop.");
        }

        static void RenderDemo(InstrumentBankData bank, OrchestralScoreRules rules, string path, MusicSettings settings, int seed)
        {
            const int rate = 44100, seconds = 80;
            var renderer = new OrchestraRenderer(bank, rules, rate);
            renderer.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Start, Value = seed, Settings = settings });
            var data = new float[1024];
            var watch = new Stopwatch();
            double totalMilliseconds = 0;
            int blocks = rate * seconds / 512;
            using (var writer = new PcmWaveWriter(path, rate, true))
            {
                for (int block = 0; block < blocks; block++)
                {
                    if (block % 86 == 0)
                        renderer.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Snapshot,
                            Snapshot = new MazeMusicSnapshot { Active = block < 1600 ? 3 : 24, Births = 5, Deaths = 3, Coverage = block / (float)blocks } });
                    if (block == blocks - 750)
                        renderer.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Complete, Value = 0 });
                    watch.Restart(); renderer.Render(data, 2); watch.Stop();
                    totalMilliseconds += watch.Elapsed.TotalMilliseconds;
                    for (int i = 0; i < data.Length; i++)
                    {
                        Require(!float.IsNaN(data[i]) && Math.Abs(data[i]) <= 0.891f, "nonfinite or clipped output");
                    }
                    writer.Write(data, data.Length);
                }
            }
            double average = totalMilliseconds / blocks;
            Require(average < (512.0 / rate * 1000) * 0.5, "render exceeds half its audio deadline");
            Debug.Log($"Orchestral demo: {path}; peak {renderer.Peak:0.000}; average 512-frame render {average:0.000} ms; stolen voices {renderer.StolenVoices}.");
        }
        [MenuItem("Tools/Orchestra/Validate and build macOS")]
        public static void ValidateAndBuild()
        {
            PrepareAndValidate();
            BuildMac();
        }

        [MenuItem("Tools/Orchestra/Build macOS")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/UnityMazer.app");

        [MenuItem("Tools/Orchestra/Build macOS memory audit")]
        public static void BuildMacMemoryAudit()
        {
            MemorySafetyValidation.Run();
            Build(BuildTarget.StandaloneOSX, "Builds/MemoryAudit/UnityMazer.app", BuildOptions.Development);
        }

        [MenuItem("Tools/Orchestra/Build Windows")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/UnityMazer.exe");

        [MenuItem("Tools/Orchestra/Build Linux")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/UnityMazer");

        static void Build(BuildTarget target, string locationPathName, BuildOptions options = BuildOptions.None)
        {
            PlayerSettings.productName = "UnityMazer";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MazeSolver.unity" },
                locationPathName = locationPathName, target = target,
                options = options
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception(target + " build failed: " + report.summary.result);
            if (target == BuildTarget.StandaloneOSX && Application.platform == RuntimePlatform.OSXEditor)
            {
                // Unity's universal-player assembly can invalidate the signatures of bundled native libraries.
                SignMac("--force --deep --sign - " + locationPathName);
                SignMac("--verify --deep --strict " + locationPathName);
            }
            Debug.Log("ORCHESTRA BUILD SUCCEEDED (" + target + "): " + locationPathName);
        }

        static void SignMac(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo("/usr/bin/codesign", arguments)
            { UseShellExecute = false, RedirectStandardError = true }))
            {
                if (process == null) throw new IOException("Could not start macOS code signing.");
                string diagnostic = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new IOException("macOS code signing failed: " + diagnostic);
            }
        }
    }
}
