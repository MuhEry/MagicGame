#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Ayni dogrulama zincirini hem Unity menusunden hem de CI/batch build'den calistirir.
/// </summary>
public static class QuestBatchBuilder
{
    const string DefaultOutputPath = "Builds/Android/GeceVardiyasi.apk";

    [MenuItem("Tools/Gece Vardiyasi/Dogrulanmis Quest APK Olustur", false, 40)]
    public static void BuildFromMenu()
    {
        Build(DefaultOutputPath, true);
    }

    public static void BuildFromCommandLine()
    {
        string outputPath = ReadArgument("-questApkPath") ?? DefaultOutputPath;
        bool cleanBuild = ReadArgument("-questIncremental") == null;
        Build(outputPath, cleanBuild);
    }

    static void Build(string outputPath, bool cleanBuild)
    {
        var validationErrors = QuestBuildValidator.ValidateProject();
        if (validationErrors.Count > 0)
            throw new InvalidOperationException(
                "Quest build dogrulamasi basarisiz:\n- " + string.Join("\n- ", validationErrors));

        string fullOutputPath = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = fullOutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            // OpenXR paketlerinin Android manifest hook'lari artimli build
            // klasorunde daha once eklenmis izinleri geri almiyor. Ozellikle
            // Hand Tracking kapatildiktan sonra eski HAND_TRACKING girdileri
            // APK'da kalabiliyor ve Quest'in OVRInput katmanini kilitliyor.
            // Quest APK'si bu nedenle her zaman temiz player cache'i ile uretilir.
            options = cleanBuild ? BuildOptions.CleanBuildCache : BuildOptions.None,
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"Quest APK build basarisiz: {report.summary.result}, " +
                $"hata={report.summary.totalErrors}, uyari={report.summary.totalWarnings}");

        Debug.Log(
            $"[Quest Build] APK hazir: {fullOutputPath} " +
            $"({report.summary.totalSize / (1024f * 1024f):0.0} MB)");
    }

    static string ReadArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
#endif
