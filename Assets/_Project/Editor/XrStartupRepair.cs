#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play'e basildigi anda editorun olmesini onaran arac.
///
/// TESHIS (Editor-prev.log): log her seferinde tam olarak
///   UnityEngine.XR.Management.XRGeneralSettings:AttemptInitializeXRSDKOnLoad ()
///   [Subsystems] Loading plugin UnityOpenXR for subsystem OpenXR Display...
///   [Subsystems] Loading plugin UnityOpenXR for subsystem OpenXR Input...
/// satirlarindan sonra susuyordu. Hicbir istisna, hicbir C# hatasi yok; bizim
/// scriptlerimiz tek satir log basmadan olay bitiyordu. Yani sorun oyun kodunda
/// degil, Play'e girerken XR'in ayaga kaldirilmasinda.
///
/// Bulunan iki somut bozukluk:
///
/// 1) AKTIF OpenXR ayar varliginda SCRIPTI KAYIP bir ozellik girdisi var
///    (guid 96efa89124dda0941802f28ad8249b87 - projede ve paketlerde hicbir yerde yok).
///    Unity acilista "The referenced script (Unknown) on this Behaviour is missing!"
///    uyarisini tam da XR init sirasinda basiyor. OpenXR yukleyicisi ozellik
///    listesini gezerken bozuk girdiye denk geliyor.
///
/// 2) Editorde Play, Standalone ayarlarini kullanir ve orada
///    "Initialize XR on Startup" ACIK. Yani her Play denemesi gozluk baglantisini
///    ayaga kaldirmaya calisiyor. Oysa bu projede test yolu Build & Run.
///
/// Bu arac ikisini de onarir. ANDROID ayarlarina DOKUNMAZ: APK'nin XR baslatmasi
/// aynen korunur.
/// </summary>
public static class XrStartupRepair
{
    const string LoaderSettingsKey = "com.unity.xr.management.loader_settings";
    const string OpenXrSettingsKey = "com.unity.xr.openxr.settings4";

    [MenuItem("Tools/Gece Vardiyası/Editörde XR Başlatmayı Kapat (Play çökmesi)", false, 43)]
    public static void RepairFromMenu()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine(RemoveBrokenOpenXrFeatures());
        report.AppendLine();
        report.AppendLine(SetStandaloneXrInitOnStart(false));

        AssetDatabase.SaveAssets();

        Debug.Log("[XR Baslangic Onarimi]\n" + report);
        EditorUtility.DisplayDialog("Editörde XR Başlatma", report.ToString(), "Tamam");
    }

    [MenuItem("Tools/Gece Vardiyası/Editörde XR Başlatmayı Aç (geri al)", false, 44)]
    public static void RestoreFromMenu()
    {
        string result = SetStandaloneXrInitOnStart(true);
        AssetDatabase.SaveAssets();

        Debug.Log("[XR Baslangic Onarimi]\n" + result);
        EditorUtility.DisplayDialog("Editörde XR Başlatma", result, "Tamam");
    }

    // ------------------------------------------------- bozuk ozellik girdileri

    /// <summary>
    /// Aktif OpenXR ayar varligindaki, scripti cozulemeyen (null) ozellik
    /// girdilerini listeden cikarir. Bunlar Unity'nin "referenced script is
    /// missing" uyarisinin kaynagidir.
    /// </summary>
    static string RemoveBrokenOpenXrFeatures()
    {
        if (!EditorBuildSettings.TryGetConfigObject(OpenXrSettingsKey, out Object settingsObject) ||
            settingsObject == null)
            return "OpenXR: aktif ayar varligi bulunamadi, ozellik temizligi atlandi.";

        SerializedObject packageSo = new SerializedObject(settingsObject);
        int removedTotal = 0;
        List<string> touched = new List<string>();

        SerializedProperty iterator = packageSo.GetIterator();
        while (iterator.NextVisible(true))
        {
            if (iterator.propertyType != SerializedPropertyType.ObjectReference ||
                iterator.objectReferenceValue == null)
                continue;

            // Her build target'in kendi OpenXRSettings nesnesi var; icindeki
            // "features" dizisini ayri bir SerializedObject ile temizliyoruz.
            SerializedObject perTarget = new SerializedObject(iterator.objectReferenceValue);
            SerializedProperty features = perTarget.FindProperty("features");
            if (features == null || !features.isArray)
                continue;

            int removed = 0;
            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = features.GetArrayElementAtIndex(i);
                if (element.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                // Scripti kayip bir MonoBehaviour burada NULL gorunur.
                if (element.objectReferenceValue == null)
                {
                    features.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }

            if (removed <= 0)
                continue;

            perTarget.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(iterator.objectReferenceValue);
            removedTotal += removed;
            touched.Add($"{iterator.objectReferenceValue.name} ({removed})");
        }

        EditorUtility.SetDirty(settingsObject);

        return removedTotal == 0
            ? "OpenXR: bozuk (scripti kayip) ozellik girdisi bulunamadi."
            : $"OpenXR: {removedTotal} bozuk ozellik girdisi SILINDI -> " + string.Join(", ", touched);
    }

    // --------------------------------------------- Standalone XR init anahtari

    /// <summary>
    /// Editorde Play, Standalone XR ayarlarini kullanir. Bu anahtar kapatilinca
    /// Play artik gozlugu ayaga kaldirmaya calismaz; oyun masaustu penceresinde
    /// calisir ve ag katmani aynen test edilebilir. ANDROID AYARI DEGISMEZ.
    /// </summary>
    static string SetStandaloneXrInitOnStart(bool enabled)
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

            bool before = initOnStart.boolValue;
            initOnStart.boolValue = enabled;
            entrySo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(entry);
            EditorUtility.SetDirty(loaderSettings);

            if (before == enabled)
                return $"Editor (Standalone) XR baslatma zaten {(enabled ? "ACIK" : "KAPALI")}. " +
                       "Android ayari degistirilmedi.";

            return enabled
                ? "Editor (Standalone) XR baslatma ACILDI. Play artik gozlugu ayaga kaldirmayi dener."
                : "Editor (Standalone) XR baslatma KAPATILDI. Play artik masaustu penceresinde " +
                  "calisir ve cokmez. ANDROID AYARI DEGISMEDI - APK'da VR aynen calisir.";
        }

        return "XR Management: Standalone girdisi bulunamadi.";
    }
}
#endif
