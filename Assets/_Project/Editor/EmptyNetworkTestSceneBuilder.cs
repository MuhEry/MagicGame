#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Main sahnesindeki calisan XR/Alteruna omurgasini koruyup oyun icerigini
/// kaldiran tanisal sahneyi uretir.
/// </summary>
public static class EmptyNetworkTestSceneBuilder
{
    public const string SourceScenePath = "Assets/_Project/Scenes/Main.unity";
    public const string TestScenePath = "Assets/_Project/Scenes/EmptyNetworkTest.unity";

    static readonly string[] RemovedRoots =
    {
        "Baca_SpawnPoint",
        "PlayerExperienceSetup_v2",
        "Spot Light",
        "Tezgah",
        "Zemin",
    };

    [MenuItem("Tools/Gece Vardiyasi/Bos XR + Alteruna Test Sahnesi Olustur", false, 41)]
    public static void CreateAndActivate()
    {
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath))
            throw new InvalidOperationException($"Kaynak sahne bulunamadi: {SourceScenePath}");

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null &&
            !AssetDatabase.DeleteAsset(TestScenePath))
        {
            throw new InvalidOperationException($"Eski test sahnesi silinemedi: {TestScenePath}");
        }

        if (!AssetDatabase.CopyAsset(SourceScenePath, TestScenePath))
            throw new InvalidOperationException("Bos test sahnesi kopyalanamadi.");

        AssetDatabase.Refresh();
        Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (RemovedRoots.Contains(root.name, StringComparer.Ordinal))
                UnityEngine.Object.DestroyImmediate(root);
        }

        GameObject systems = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "Systems");
        if (systems == null)
            throw new InvalidOperationException("Systems koku bulunamadi; XR bekcisi korunamiyor.");

        // Seviye oynanis kodlarini denklemden cikar. Yalnizca kamerayi ayakta
        // tutan bekci ve onun rig referanslari kalsin. Alteruna kendi ayri
        // kokunde aynen calismaya devam eder.
        foreach (MonoBehaviour behaviour in systems.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            behaviour.enabled = typeName == nameof(OfflineRigFallback) ||
                                typeName == "PlayerRefs";
        }

        CreateDiagnosticGeometry();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, TestScenePath))
            throw new InvalidOperationException("Bos test sahnesi kaydedilemedi.");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(TestScenePath, true),
        };
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Bos Test] Sahne hazir ve tek build sahnesi yapildi: {TestScenePath}. " +
            "XR rig + Alteruna aktif; oyun seviye icerigi ve oynanis sistemleri kapali.");
    }

    static void CreateDiagnosticGeometry()
    {
        GameObject root = new GameObject("EMPTY NETWORK TEST");

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Diagnostic Floor";
        floor.transform.SetParent(root.transform);
        floor.transform.position = new Vector3(0f, -0.1f, 2f);
        floor.transform.localScale = new Vector3(10f, 0.2f, 10f);

        for (int i = 0; i < 3; i++)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = $"Diagnostic Marker {i + 1}";
            marker.transform.SetParent(root.transform);
            marker.transform.position = new Vector3((i - 1) * 1.5f, 1f, 3f + i);
        }

        GameObject lightObject = new GameObject("Diagnostic Directional Light");
        lightObject.transform.SetParent(root.transform);
        lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.None;
    }
}
#endif
