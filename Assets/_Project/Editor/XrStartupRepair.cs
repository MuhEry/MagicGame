#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editorde gozlukle test edilebilmesi icin XR yapilandirmasini onarir.
///
/// TESHIS
/// Play'e basildigi anda editor oluyordu. Editor-prev.log her seferinde ayni yerde
/// susuyordu: XRGeneralSettings:AttemptInitializeXRSDKOnLoad -> OpenXR Display ->
/// OpenXR Input -> "Shut down.". Hicbir istisna, hicbir C# hatasi, crash dump yok.
///
/// SEBEP
/// Projede IKI paralel XR ayar agaci var ve AKTIF olan, Multiplayer XR Template'ten
/// gelen kopyaydi:
///   EditorBuildSettings > m_configObjects > com.unity.xr.management.loader_settings
///
/// O kopyadaki "Open XR Package Settings.asset" icinde SCRIPTI KAYIP bir ozellik
/// girdisi var (guid 96efa89124dda0941802f28ad8249b87 - projede ve hicbir pakette
/// yok). OpenXR yukleyicisi ozellik listesini gezerken bu bozuk girdiye carpiyor.
///
/// Onemli ayrinti: scripti kayip bir MonoBehaviour NULL DEGILDIR - nesne olarak
/// durur. Bu yuzden "null girdileri temizle" yaklasimi onu bulamaz.
///
/// COZUM
/// Projenin KENDI temiz ayarlarina gecmek. Karsilastirma:
///   Assets/XR/Settings/OpenXRPackageSettings.asset          101 MonoBehaviour, 0 kayip
///   Multiplayer XR Template/.../Open XR Package Settings     103 MonoBehaviour, 1 kayip
///
/// Ustelik projenin kendi XRGeneralSettingsPerBuildTarget'inda Standalone ve Android
/// hedeflerinin IKISI de OpenXR loader kullaniyor (template'inki Android'de Oculus
/// Loader kullaniyordu - actigimiz tum OpenXR ozellikleriyle tutarsizdi).
/// </summary>
public static class XrStartupRepair
{
    const string LoaderSettingsKey = "com.unity.xr.management.loader_settings";
    const string OpenXrSettingsKey = "com.unity.xr.openxr.settings4";

    const string CleanLoaderSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
    const string CleanOpenXrSettingsPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";

    [MenuItem("Tools/Gece Vardiyası/Editörde VR Testini Onar (XR yapılandırması)", false, 43)]
    public static void RepairFromMenu()
    {
        StringBuilder report = new StringBuilder();

        report.AppendLine(SwitchToCleanXrConfig());
        report.AppendLine();
        QuestLinkGraphicsRepair.EnsureD3D11();
        report.AppendLine("Windows grafik API'si Quest Link icin Direct3D11'e sabitlendi.");
        report.AppendLine();
        // Unity 6 + Meta Link, hazir olmayan bir PC VR oturumunda OpenXR'i
        // otomatik baslatirken native tarafta kilitlenebiliyor. Windows XR'ini
        // AirLinkSafeXrBootstrap USB/Air Link gercekten hazirken baslatir.
        report.AppendLine(SetStandaloneXrInitOnStart(false));
        report.AppendLine();
        report.AppendLine(XrInputRepair.RepairOpenXrFeatures(BuildTargetGroup.Standalone));
        report.AppendLine();
        report.AppendLine(XrInputRepair.RepairOpenXrFeatures(BuildTargetGroup.Android));

        AssetDatabase.SaveAssets();

        report.AppendLine();
        report.AppendLine("Unity'yi KAPATIP yeniden acin - XR yapilandirmasi acilista okunur.");

        Debug.Log("[XR Onarimi]\n" + report);
        EditorUtility.DisplayDialog("Editörde VR Testini Onar", report.ToString(), "Tamam");
    }

    [MenuItem("Tools/Gece Vardiyası/Editörde XR Başlatmayı Kapat (acil çıkış)", false, 44)]
    public static void DisableFromMenu()
    {
        string result = SetStandaloneXrInitOnStart(false);
        AssetDatabase.SaveAssets();

        Debug.Log("[XR Onarimi]\n" + result);
        EditorUtility.DisplayDialog("Editörde XR Başlatma", result, "Tamam");
    }

    // ------------------------------------------------------ temiz yapilandirma

    /// <summary>
    /// Aktif XR ayar nesnelerini template'in kopyalarindan projenin kendi temiz
    /// varliklarina cevirir. Bozuk ozellik girdisi boylece devreden tamamen cikar.
    /// </summary>
    static string SwitchToCleanXrConfig()
    {
        StringBuilder result = new StringBuilder();

        result.AppendLine(SwitchConfigObject(LoaderSettingsKey, CleanLoaderSettingsPath,
            "XR loader ayarlari"));
        result.Append(SwitchConfigObject(OpenXrSettingsKey, CleanOpenXrSettingsPath,
            "OpenXR paket ayarlari"));

        return result.ToString();
    }

    static string SwitchConfigObject(string key, string cleanAssetPath, string label)
    {
        Object clean = AssetDatabase.LoadAssetAtPath<Object>(cleanAssetPath);
        if (clean == null)
            return $"{label}: HATA - {cleanAssetPath} bulunamadi.";

        EditorBuildSettings.TryGetConfigObject(key, out Object current);
        if (current == clean)
            return $"{label}: zaten temiz varlik kullaniliyor ({cleanAssetPath}).";

        string previous = current != null
            ? AssetDatabase.GetAssetPath(current)
            : "(atanmamis)";

        EditorBuildSettings.AddConfigObject(key, clean, true);
        return $"{label}: {previous}\n    -> {cleanAssetPath}";
    }

    // --------------------------------------------- Standalone XR init anahtari

    /// <summary>
    /// Editorde Play, Standalone XR ayarlarini kullanir. Bu bayrak kapaliyken
    /// AirLinkSafeXrBootstrap XR'i yalnizca Meta USB/Air Link PC VR oturumu hazirsa elle
    /// baslatir. Android ayarina dokunulmaz.
    /// </summary>
    public static string SetStandaloneXrInitOnStart(bool enabled)
    {
        if (!EditorBuildSettings.TryGetConfigObject(LoaderSettingsKey, out Object loaderSettings) ||
            loaderSettings == null)
            return "XR Management: aktif loader ayarlari bulunamadi.";

        SerializedObject so = new SerializedObject(loaderSettings);
        SerializedProperty keys = so.FindProperty("Keys");
        SerializedProperty values = so.FindProperty("Values");

        if (keys == null || values == null || !keys.isArray || !values.isArray)
            return "XR Management: Keys/Values alanlari okunamadi.";

        const int standaloneBuildTargetGroup = 1; // BuildTargetGroup.Standalone
        for (int i = 0; i < keys.arraySize && i < values.arraySize; i++)
        {
            if (keys.GetArrayElementAtIndex(i).intValue != standaloneBuildTargetGroup)
                continue;

            Object entry = values.GetArrayElementAtIndex(i).objectReferenceValue;
            if (entry == null)
                continue;

            SerializedObject entrySo = new SerializedObject(entry);
            SerializedProperty initOnStart = entrySo.FindProperty("m_InitManagerOnStart");
            if (initOnStart == null)
                return "XR Management: m_InitManagerOnStart alani bulunamadi.";

            initOnStart.boolValue = enabled;
            entrySo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(entry);

            // Loader yoneticisinin otomatik yuklemesi de acik olmali, yoksa
            // "Initialize XR on Startup" isaretli olsa bile gozluk ayaga kalkmaz.
            SerializedProperty manager = entrySo.FindProperty("m_LoaderManagerInstance");
            if (manager != null && manager.objectReferenceValue != null)
            {
                SerializedObject managerSo = new SerializedObject(manager.objectReferenceValue);
                SerializedProperty automaticLoading = managerSo.FindProperty("m_AutomaticLoading");
                SerializedProperty automaticRunning = managerSo.FindProperty("m_AutomaticRunning");
                if (automaticLoading != null)
                    automaticLoading.boolValue = enabled;
                if (automaticRunning != null)
                    automaticRunning.boolValue = enabled;
                managerSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager.objectReferenceValue);
            }

            EditorUtility.SetDirty(loaderSettings);

            return enabled
                ? "Editor (Standalone) otomatik XR baslatma ACIK."
                : "Editor (Standalone) otomatik XR baslatma KAPALI. " +
                  "USB/Air Link hazirsa guvenli baslatici XR'i elle acar. ANDROID AYARI DEGISMEDI.";
        }

        return "XR Management: Standalone girdisi bulunamadi.";
    }
}
#endif
