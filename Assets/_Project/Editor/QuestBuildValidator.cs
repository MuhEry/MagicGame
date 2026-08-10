#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

/// <summary>
/// Quest APK'sinin calistigi bilinen minimal XR ve Alteruna yapilandirmasindan
/// sessizce sapmasini engeller.
/// </summary>
public sealed class QuestBuildValidator : IPreprocessBuildWithReport
{
    const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
    const string ApplicationDataPath = "Assets/Alteruna/Resources/ApplicationData.asset";
    const string AlterunaConfigPath = "Assets/Resources/AlterunaConfig.asset";
    const string OpenXrSettingsPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";
    const string OpenXrSettingsKey = "com.unity.xr.openxr.settings4";
    const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
    const string ControllerRigPrefabGuid = "f6336ac4ac8b4d34bc5072418cdc62a0";
    const string LegacyHandsRigPrefabGuid = "d6878e1999eb4b44a9f5a263af86c185";

    static readonly string[] RequiredFeatureIds =
    {
        "com.unity.openxr.feature.metaquest",
        "com.unity.openxr.feature.input.oculustouch",
        "com.unity.openxr.feature.input.metaquestplus",
        "com.unity.openxr.feature.compositionlayers",
    };

    static readonly string[] ForbiddenFeatureIds =
    {
        "AndroidXR-OpenXRLifeCycle",
        "MetaOpenXR-OpenXRLifeCycle",
        "com.unity.openxr.feature.androidxr-display-utilities",
        "com.unity.openxr.feature.meta-display-utilities",
        "com.unity.openxr.feature.input.handinteraction",
        "com.unity.openxr.feature.input.handtracking",
        "com.unity.openxr.feature.input.metahandtrackingaim",
        "com.unity.openxr.features.runtimedebugger",
    };

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
            return;

        List<string> errors = ValidateProject();
        if (errors.Count > 0)
            throw new BuildFailedException(
                "Quest build dogrulamasi basarisiz:\n- " + string.Join("\n- ", errors));

        Debug.Log("[Quest Build] Dogrulama basarili: minimal OpenXR + Alteruna yapilandirmasi hazir.");
    }

    [MenuItem("Tools/Gece Vardiyası/Quest Build Ayarlarını Doğrula", false, 39)]
    public static void ValidateFromMenu()
    {
        List<string> errors = ValidateProject();
        string message = errors.Count == 0
            ? "Quest build ayarlari dogru."
            : "Hatalar:\n- " + string.Join("\n- ", errors);

        if (errors.Count == 0)
            Debug.Log("[Quest Build] " + message);
        else
            Debug.LogError("[Quest Build] " + message);

        EditorUtility.DisplayDialog("Quest Build Dogrulamasi", message, "Tamam");
    }

    public static List<string> ValidateProject()
    {
        List<string> errors = new List<string>();

        ValidateApplicationId(errors);
        ValidateAlterunaTransport(errors);
        ValidateBuildScenes(errors);
        ValidatePlayerSettings(errors);
        ValidateScene(errors);
        ValidateOpenXrSettings(errors);
        ValidateUniqueMetaQuestFeature(errors);

        return errors;
    }

    static void ValidateApplicationId(List<string> errors)
    {
        UnityEngine.Object data = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ApplicationDataPath);
        if (data == null)
        {
            errors.Add($"Alteruna ApplicationData bulunamadi: {ApplicationDataPath}");
            return;
        }

        SerializedProperty idProperty = new SerializedObject(data).FindProperty("_applicationId");
        SerializedProperty serverModeProperty = new SerializedObject(data).FindProperty("_serverMode");
        string id = idProperty != null ? idProperty.stringValue : string.Empty;
        if (!Guid.TryParse(id, out Guid parsed) || parsed == Guid.Empty)
            errors.Add("Alteruna Project ID gecersiz veya bos.");
        if (serverModeProperty == null || serverModeProperty.intValue != 0)
            errors.Add("Alteruna server modu portaldaki Single Server ayariyla uyusmuyor.");
    }

    static void ValidateAlterunaTransport(List<string> errors)
    {
        UnityEngine.Object config = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AlterunaConfigPath);
        if (config == null)
        {
            errors.Add($"AlterunaConfig bulunamadi: {AlterunaConfigPath}");
            return;
        }

        SerializedProperty transport = new SerializedObject(config).FindProperty("_transportType");
        if (transport == null || transport.intValue != 1)
            errors.Add("Alteruna transport ayari Default (UDP) olmali; portal Transport Mode ile ayni tutulmali.");

        SerializedProperty lanDiscovery = new SerializedObject(config).FindProperty("_enableLanDiscovery");
        if (lanDiscovery == null || lanDiscovery.boolValue)
            errors.Add("Internet matchmaking build'inde LAN Discovery kapali olmali.");
    }

    static void ValidateBuildScenes(List<string> errors)
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length != 1 ||
            !string.Equals(enabledScenes[0], MainScenePath, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Build Scene List yalnizca Main sahnesini icermeli.");
        }
    }

    static void ValidatePlayerSettings(List<string> errors)
    {
        if (!File.Exists(ProjectSettingsPath))
        {
            errors.Add("ProjectSettings.asset bulunamadi.");
            return;
        }

        string projectSettings = File.ReadAllText(ProjectSettingsPath);
        if (!projectSettings.Contains("activeInputHandler: 1"))
            errors.Add("Android Active Input Handling yalnizca Input System Package olmali.");
        if (!Regex.IsMatch(projectSettings,
                @"managedStrippingLevel:\s*\r?\n\s+Android: 0"))
            errors.Add("Android Managed Stripping Level, RPC/reflection guvenligi icin Minimal olmali.");
    }

    static void ValidateScene(List<string> errors)
    {
        if (!File.Exists(MainScenePath))
        {
            errors.Add($"Main sahnesi bulunamadi: {MainScenePath}");
            return;
        }

        string scene = File.ReadAllText(MainScenePath);
        if (!scene.Contains("ConnectOnStart: 0"))
            errors.Add("MultiplayerManager ConnectOnStart kapali olmali; AutoJoin ilk kareden sonra baglanir.");
        if (!Regex.IsMatch(scene,
                @"m_Name: Multiplayer\r?\n(?:.*\r?\n){0,8}\s+m_IsActive: 0"))
            errors.Add("Multiplayer kok nesnesi ilk XR karesine kadar pasif olmali.");
        if (!scene.Contains("_maxPlayers: 2"))
            errors.Add("MultiplayerManager oda kapasitesi iki oyuncu olmali.");
        if (!scene.Contains("AvatarSpawning: 1") || scene.Contains("AvatarPrefab: {fileID: 0}"))
            errors.Add("MultiplayerManager yerel/uzak avatar spawn ayari eksik.");
        if (!Regex.IsMatch(scene,
                @"AvatarSpawnLocations:\r?\n\s+- \{fileID: [1-9]\d*\}\r?\n\s+- \{fileID: [1-9]\d*\}"))
            errors.Add("Iki oyuncu icin iki gecerli avatar spawn noktasi bulunmali.");
        if (!scene.Contains("Assembly-CSharp::Alteruna.AutoJoin"))
            errors.Add("Main sahnesinde dayanıklı AutoJoin bileseni bulunamadi.");
        if (!scene.Contains("Assembly-CSharp::OfflineRigFallback"))
            errors.Add("Main sahnesinde agdan bagimsiz yerel XR rig bekcisi bulunamadi.");
        if (!scene.Contains("networkRoot: {fileID: 398427124}"))
            errors.Add("OfflineRigFallback ertelenmis Multiplayer kokune bagli degil.");
        if (!scene.Contains($"guid: {ControllerRigPrefabGuid}"))
            errors.Add("Avatar sablonu controller-only XR Origin olmali.");
        if (scene.Contains($"guid: {LegacyHandsRigPrefabGuid}"))
            errors.Add("Hands Interaction Demo XR rig'i Quest build'inde yasak; OVRInput kilitlenmesine yol aciyor.");
    }

    static void ValidateOpenXrSettings(List<string> errors)
    {
        if (!EditorBuildSettings.TryGetConfigObject(OpenXrSettingsKey, out UnityEngine.Object active) ||
            active == null)
        {
            errors.Add("Aktif OpenXR paket ayari atanmamis.");
            return;
        }

        string activePath = AssetDatabase.GetAssetPath(active);
        if (!string.Equals(activePath, OpenXrSettingsPath, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Yanlis OpenXR ayar dosyasi aktif: {activePath}");

        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null)
        {
            errors.Add("Android OpenXR ayarlari bulunamadi.");
            return;
        }

        Dictionary<string, bool> enabledById = settings.GetFeatures()
            .Where(feature => feature != null)
            .GroupBy(ReadFeatureId, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.Any(feature => feature.enabled),
                StringComparer.OrdinalIgnoreCase);

        foreach (string required in RequiredFeatureIds)
        {
            if (!enabledById.TryGetValue(required, out bool enabled) || !enabled)
                errors.Add($"Gerekli Android OpenXR ozelligi kapali/eksik: {required}");
        }

        foreach (string forbidden in ForbiddenFeatureIds)
        {
            if (enabledById.TryGetValue(forbidden, out bool enabled) && enabled)
                errors.Add($"Quest VR ile cakisan/gereksiz OpenXR ozelligi acik: {forbidden}");
        }
    }

    static void ValidateUniqueMetaQuestFeature(List<string> errors)
    {
        string[] assets = AssetDatabase.FindAssets("t:MetaQuestFeature");
        if (assets.Length != 1)
            errors.Add($"Projede tam bir MetaQuestFeature varligi olmali; bulunan: {assets.Length}.");
    }

    static string ReadFeatureId(OpenXRFeature feature)
    {
        SerializedProperty property = new SerializedObject(feature).FindProperty("featureIdInternal");
        return property != null ? property.stringValue : string.Empty;
    }
}
#endif
