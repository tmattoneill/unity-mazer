using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MazeSolver.Editor
{
    public static class SetAppIcon
    {
        [MenuItem("Tools/Orchestra/Assign app icon")]
        public static void Apply()
        {
            AssetDatabase.Refresh();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/AppIcon.png");
            if (!icon) throw new InvalidOperationException("Assets/Textures/AppIcon.png not found or not imported as Texture2D.");
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, new[] { icon }, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("App icon assigned for Standalone: " + AssetDatabase.GetAssetPath(icon));
        }

        // Batchmode entry point: icon changes made in a separate editor run do not always
        // persist to ProjectSettings.asset, so assign and build in the same session.
        public static void ApplyAndBuildMac()
        {
            Apply();
            BuildOrchestra.BuildMac();
        }
    }
}
