using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MazeSolver.Editor
{
    public static class MemorySafetyValidation
    {
        [MenuItem("Tools/Orchestra/Validate memory safety")]
        public static void Run()
        {
            ValidateAudioQueueRelease();
            ValidateMazeRendererRelease();
            ValidateSoundtrackStreaming();
            Debug.Log("MEMORY SAFETY VALIDATION PASSED: queue references, runtime textures and soundtrack imports.");
        }

        static void ValidateAudioQueueRelease()
        {
            string directory = Path.Combine(Application.temporaryCachePath, "MemorySafetyValidation");
            var recording = new SessionRecording(directory, 8000, 431, MusicSettings.Default, 1);
            var queue = new AudioCommandQueue();
            Require(queue.TryWrite(new AudioCommand { Type = AudioCommandType.Start, Recording = recording }), "queue refused a test command");
            Require(queue.RetainedRecordingCount == 1, "queue did not retain its unread recording");
            Require(queue.TryRead(out AudioCommand command) && command.Recording == recording, "queue returned the wrong recording");
            Require(queue.RetainedRecordingCount == 0, "queue retained a consumed recording");
            recording.Cancel();
            Require(recording.WaitForWriter(2000), "recording worker did not stop");
            if (File.Exists(recording.TemporaryPath)) File.Delete(recording.TemporaryPath);
        }

        static void ValidateMazeRendererRelease()
        {
            var host = new GameObject("Maze renderer memory validation");
            var renderer = host.AddComponent<MazeRenderer>();
            var spriteRenderer = host.GetComponent<SpriteRenderer>();
            var grid = new byte[3, 3];
            renderer.Initialize(3, grid);
            var firstSprite = spriteRenderer.sprite;
            var firstTexture = firstSprite.texture;
            renderer.Initialize(3, grid);
            Require(!firstSprite && !firstTexture, "maze renderer retained replaced native assets");
            var finalSprite = spriteRenderer.sprite;
            var finalTexture = finalSprite.texture;
            UnityEngine.Object.DestroyImmediate(host);
            Require(!finalSprite && !finalTexture, "maze renderer retained native assets after teardown");
        }

        static void ValidateSoundtrackStreaming()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Soundtracks" });
            Require(guids.Length == 9, "expected nine soundtrack clips");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Require(importer != null, "missing audio importer for " + path);
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                Require(settings.loadType == AudioClipLoadType.Streaming, path + " is not streamed");
                Require(importer.loadInBackground, path + " does not load in the background");
                Require(!settings.preloadAudioData, path + " preloads audio data");
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Memory safety validation: " + message);
        }
    }
}
