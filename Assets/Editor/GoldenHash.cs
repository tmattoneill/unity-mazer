using System;
using UnityEditor;
using UnityEngine;

namespace MazeSolver.Editor
{
    // Renders a fixed, scripted Cinematic session and logs a hash of the audio stream.
    // Run before and after composer refactors; the hash must not change.
    public static class GoldenHash
    {
        [MenuItem("Tools/Orchestra/Golden hash")]
        public static void Run()
        {
            var rules = AssetDatabase.LoadAssetAtPath<OrchestralScoreRules>("Assets/Resources/Orchestra/ScoreRules.asset");
            var bankAsset = AssetDatabase.LoadAssetAtPath<OrchestralInstrumentBank>("Assets/Resources/Orchestra/InstrumentBank.asset");
            if (!rules || !bankAsset) throw new InvalidOperationException("Orchestra assets missing; run Prepare and validate first.");
            var renderer = new OrchestraRenderer(bankAsset.Load(), rules, 44100);
            renderer.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Start, Value = 431, Settings = MusicSettings.Default });
            var data = new float[1024];
            ulong hash = 14695981039346656037UL;
            for (int block = 0; block < 3000; block++)
            {
                if (block % 100 == 0)
                    renderer.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Snapshot,
                        Snapshot = new MazeMusicSnapshot { Active = 1 + block / 100, Births = 2, Deaths = 1, Coverage = block / 3000f } });
                if (block == 2400)
                    renderer.Commands.TryWrite(new AudioCommand { Type = AudioCommandType.Complete, Value = (int)SessionOutcome.Solved });
                renderer.Render(data, 2);
                for (int i = 0; i < data.Length; i++)
                {
                    unchecked
                    {
                        uint bits = (uint)BitConverter.SingleToInt32Bits(data[i]);
                        hash = (hash ^ bits) * 1099511628211UL;
                    }
                }
            }
            Debug.Log($"GOLDEN HASH: {hash:x16} finished={renderer.Finished} peak={renderer.Peak:0.000}");
        }
    }
}
