using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Paintball.Editor
{
    /// <summary>
    /// CI/CD-Build-Skript (NFR-18): Automatisierte Builds für alle Zielplattformen.
    /// Wird vom GitHub Actions Workflow (unity-build.yml) über game-ci/unity-builder aufgerufen.
    /// </summary>
    public static class BuildScript
    {
        private const string OutputFolder = "build";

        public static void PerformBuild()
        {
            string[] scenes = GetEnabledScenes();

            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildScript] Keine Szenen in Build Settings konfiguriert!");
                EditorApplication.Exit(1);
                return;
            }

            string platformName = GetPlatformFolder();
            string outputPath = Path.Combine(OutputFolder, platformName);

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = EditorUserBuildSettings.activeBuildTarget,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] Build erfolgreich: {summary.outputPath} ({summary.totalSize} bytes)");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[BuildScript] Build fehlgeschlagen: {summary.result}");
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error)
                            Debug.LogError(message.content);
                    }
                }
                EditorApplication.Exit(1);
            }
        }

        private static string[] GetEnabledScenes()
        {
            var enabledScenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path))
                    enabledScenes.Add(scene.path);
            }
            return enabledScenes.ToArray();
        }

        private static string GetPlatformFolder()
        {
            return EditorUserBuildSettings.activeBuildTarget switch
            {
                BuildTarget.StandaloneWindows64 => "Win64",
                BuildTarget.StandaloneLinux64 => "Linux64",
                BuildTarget.StandaloneOSX => "OSXUniversal",
                BuildTarget.Android => "Android",
                BuildTarget.iOS => "iOS",
                BuildTarget.WebGL => "WebGL",
                _ => "Other"
            };
        }
    }
}