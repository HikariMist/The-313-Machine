using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HikariForge.M313.EditorTools
{
    /// <summary>
    /// One-click Windows build. Menu: Hikari Forge → Build Windows (x64).
    /// Command line: Unity -batchmode -quit -projectPath . -executeMethod HikariForge.M313.EditorTools.BuildScript.BuildWindows
    /// </summary>
    public static class BuildScript
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string Output = "Builds/Windows/The313Machine.exe";

        [MenuItem("Hikari Forge/Configure Project")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Hikari Forge Studios";
            PlayerSettings.productName = "The 3:13 Machine";
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "0.1") PlayerSettings.bundleVersion = "0.9.0";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = false;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[M313] Project configured.");
        }

        [MenuItem("Hikari Forge/Build Windows (x64)")]
        public static void BuildWindows()
        {
            Configure();
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Debug.Log($"[M313] Build {report.summary.result}: {report.summary.outputPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
