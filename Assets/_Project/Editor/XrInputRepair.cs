#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

/// <summary>
/// VR girdisini ayaga kaldirir.
///
/// NEDEN VAR: Gozlukte "hicbir sey algilanmiyor" belirtisinin iki ayri sebebi vardi
/// ve ikisi de sessizdi (Console'da yalnizca uyari, hata yok):
///
///  1. OpenXR ayarlarinda TEK bir ozellik aciksa (yalnizca "Meta Quest Support"),
///     hicbir interaction profile yuklenmez. OpenXR o zaman kontrolcu girdilerini
///     hicbir action'a baglamaz: el pozisyonu gelmez, kavrama tetiklenmez.
///     Ayrica "Hand Tracking Subsystem" kapaliyken Hands rig'i el izlemesine
///     hic abone olamaz ("Hand Tracking Subsystem not found or not running").
///
///  2. Sahnede InputActionManager yoksa XRI'in Input Action Asset'i HIC etkinlestirilmez.
///     Unity'nin uyarisi bunu birebir soyler: "'Enable Input Tracking' is enabled, but
///     Position and/or Rotation Action is disabled... The Input Action Manager behavior
///     can be added to a GameObject in a Scene".
///
/// Bu dosya ikisini de tek komutta onarir ve neyi degistirdigini raporlar.
/// </summary>
public static class XrInputRepair
{
    const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

    const string DefaultActionAssetPath =
        "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/XRI Default Input Actions.inputactions";

    /// <summary>
    /// PC Air Link testinde yalnızca hareket kontrolcusu profilleri gerekir. El
    /// takibi ve Meta Android yasam dongusu Editor'de acilirsa Meta RuntimeIPC,
    /// Android servislerini Windows'ta baslatmaya calisip Editor'u kapatabiliyor.
    /// APK tarafinda da temel Quest destegi ve Touch kontrolcu profilleri korunur;
    /// bu controller tabanli VR surumunde kullanilmayan MR/el takibi katmanlari kapatilir.
    /// </summary>
    static readonly string[] k_StandaloneRequiredFeatureNames =
    {
        "Oculus Touch Controller Profile",
        "Meta Quest Touch Plus Controller Profile",
    };

    static readonly string[] k_AndroidRequiredFeatureNames =
    {
        "Meta Quest Support",
        "Oculus Touch Controller Profile",
        "Meta Quest Touch Plus Controller Profile",
        "Composition Layers Support",
    };

    static readonly string[] k_AndroidDisabledFeatureNames =
    {
        // Bu surum controller tabanli saf VR'dir. MR/el takibi eklentileri,
        // Quest 2 acilisinda gereksiz vendor extension'lari yuklememelidir.
        "Hand Tracking Subsystem",
        "Hand Interaction Profile",
        "Meta Hand Tracking Aim",
        "Meta Quest: Display Utilities",
        "Runtime Debugger",
    };

    static readonly string[] k_StandaloneDisabledFeatureNames =
    {
        "Hand Tracking Subsystem",
        "Hand Interaction Profile",
        "Meta Hand Tracking Aim",
        "Meta Quest: Display Utilities",
        "Composition Layers Support",
    };

    const string MetaOpenXrLifeCycleFeatureId = "MetaOpenXR-OpenXRLifeCycle";

    static readonly string[] k_AndroidDisabledFeatureIds =
    {
        // Bu proje Meta AR Foundation/scene-discovery kullanmiyor. Android XR
        // veya Meta OpenXR yasam dongusu Quest 2'de xrDiscoverSpacesMETA
        // aramasini tetikleyip XR acilisini bloke edebiliyor. Standart OpenXR
        // Quest ve kontrolcu profilleri bu gizli yasam dongusune bagli degil.
        "AndroidXR-OpenXRLifeCycle",
        "com.unity.openxr.feature.androidxr-display-utilities",
        "MetaOpenXR-OpenXRLifeCycle",
        "com.unity.openxr.feature.meta-display-utilities",
    };

    [MenuItem("Tools/Gece Vardiyası/VR Girdisini Onar (OpenXR + Input Actions)", false, 42)]
    public static void RepairFromMenu()
    {
        StringBuilder report = new StringBuilder();
        // Standalone = editorde gozlukle test, Android = APK. Ikisi de gerekli.
        report.AppendLine(RepairOpenXrFeatures(BuildTargetGroup.Standalone));
        report.AppendLine();
        report.AppendLine(RepairOpenXrFeatures(BuildTargetGroup.Android));
        report.AppendLine();
        report.AppendLine(EnsureInputActionManager(true));

        Debug.Log("[VR Onarim]\n" + report);
        EditorUtility.DisplayDialog("VR Girdisini Onar", report.ToString(), "Tamam");
    }

    // ------------------------------------------------------------------ OpenXR

    public static string RepairOpenXrFeatures()
    {
        return RepairOpenXrFeatures(BuildTargetGroup.Android);
    }

    /// <summary>
    /// Gozlukle EDITORDE test edebilmek icin Standalone hedefinde de ayni
    /// ozelliklerin acik olmasi gerekir; APK icin Android hedefi kullanilir.
    /// </summary>
    public static string RepairOpenXrFeatures(BuildTargetGroup targetGroup)
    {
        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
        if (settings == null)
            return $"OpenXR ({targetGroup}): ayarlar bulunamadi. XR Plug-in Management'ta OpenXR acik mi?";

        FeatureHelpers.RefreshFeatures(targetGroup);

        string[] requiredFeatureNames = targetGroup == BuildTargetGroup.Standalone
            ? k_StandaloneRequiredFeatureNames
            : k_AndroidRequiredFeatureNames;

        List<string> turnedOn = new List<string>();
        List<string> turnedOff = new List<string>();
        List<string> alreadyOn = new List<string>();
        List<string> notFound = new List<string>(requiredFeatureNames);

        foreach (OpenXRFeature feature in settings.GetFeatures())
        {
            if (feature == null)
                continue;

            string label = ReadNameUi(feature);
            string featureId = ReadFeatureId(feature);

            bool disableForStandalone = targetGroup == BuildTargetGroup.Standalone &&
                (k_StandaloneDisabledFeatureNames.Any(blocked =>
                     string.Equals(label, blocked, System.StringComparison.OrdinalIgnoreCase)) ||
                 string.Equals(featureId, MetaOpenXrLifeCycleFeatureId,
                     System.StringComparison.OrdinalIgnoreCase));

            bool disableForAndroid = targetGroup == BuildTargetGroup.Android &&
                (k_AndroidDisabledFeatureNames.Any(blocked =>
                     string.Equals(label, blocked, System.StringComparison.OrdinalIgnoreCase)) ||
                 k_AndroidDisabledFeatureIds.Any(blocked =>
                     string.Equals(featureId, blocked, System.StringComparison.OrdinalIgnoreCase)));

            if (disableForStandalone || disableForAndroid)
            {
                if (feature.enabled)
                {
                    feature.enabled = false;
                    EditorUtility.SetDirty(feature);
                    turnedOff.Add(string.IsNullOrEmpty(label) ? featureId : label);
                }

                continue;
            }

            string match = requiredFeatureNames
                .FirstOrDefault(wanted => string.Equals(label, wanted, System.StringComparison.OrdinalIgnoreCase));

            if (match == null)
                continue;

            notFound.Remove(match);

            if (feature.enabled)
            {
                alreadyOn.Add(label);
                continue;
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            turnedOn.Add(label);
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        StringBuilder result = new StringBuilder();
        result.AppendLine($"OpenXR ({targetGroup}):");
        result.AppendLine(turnedOn.Count > 0
            ? "  ACILDI: " + string.Join(", ", turnedOn)
            : "  Acilacak yeni ozellik yoktu.");
        if (turnedOff.Count > 0)
            result.AppendLine(targetGroup == BuildTargetGroup.Standalone
                ? "  PC QUEST LINK ICIN KAPATILDI: " + string.Join(", ", turnedOff)
                : "  QUEST ILE CAKISTIGI ICIN KAPATILDI: " + string.Join(", ", turnedOff));
        if (alreadyOn.Count > 0)
            result.AppendLine("  Zaten acikti: " + string.Join(", ", alreadyOn));
        if (notFound.Count > 0)
            result.AppendLine("  BULUNAMADI (paket eksik olabilir): " + string.Join(", ", notFound));

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// nameUi, OpenXRFeature uzerinde public degil; SerializedObject ile okunur.
    /// Tip adina gore eslestirmek paket surumlerine daha kirilgan olurdu.
    /// </summary>
    static string ReadNameUi(OpenXRFeature feature)
    {
        SerializedObject so = new SerializedObject(feature);
        SerializedProperty property = so.FindProperty("nameUi");
        return property != null ? property.stringValue : feature.GetType().Name;
    }

    static string ReadFeatureId(OpenXRFeature feature)
    {
        SerializedObject so = new SerializedObject(feature);
        SerializedProperty property = so.FindProperty("featureIdInternal");
        return property != null ? property.stringValue : string.Empty;
    }

    // ----------------------------------------------------------- Input Actions

    public static string EnsureInputActionManager(bool saveScene)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainScenePath)
            return "Input Actions: Main sahnesi acik degil, sahne tarafi atlandi.";

        InputActionManager manager = Object.FindFirstObjectByType<InputActionManager>();
        bool created = false;

        if (manager == null)
        {
            GameObject host = GameObject.Find("Input Action Manager");
            if (host == null)
            {
                host = new GameObject("Input Action Manager");
                SceneManager.MoveGameObjectToScene(host, scene);
            }

            manager = host.AddComponent<InputActionManager>();
            created = true;
        }

        InputActionAsset actionAsset =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>(DefaultActionAssetPath);
        if (actionAsset == null)
            return "Input Actions: HATA - " + DefaultActionAssetPath + " bulunamadi. " +
                   "XRI Starter Assets ornegi import edilmis mi?";

        SerializedObject managerSo = new SerializedObject(manager);
        SerializedProperty assets = managerSo.FindProperty("m_ActionAssets");
        if (assets == null)
            return "Input Actions: InputActionManager.m_ActionAssets alani bulunamadi.";

        bool alreadyReferenced = false;
        for (int i = 0; i < assets.arraySize; i++)
        {
            if (assets.GetArrayElementAtIndex(i).objectReferenceValue == actionAsset)
            {
                alreadyReferenced = true;
                break;
            }
        }

        if (!alreadyReferenced)
        {
            assets.arraySize++;
            assets.GetArrayElementAtIndex(assets.arraySize - 1).objectReferenceValue = actionAsset;
        }

        managerSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);

        if (saveScene)
            EditorSceneManager.SaveScene(scene);

        if (created)
            return "Input Actions: 'Input Action Manager' OLUSTURULDU ve " +
                   System.IO.Path.GetFileName(DefaultActionAssetPath) + " baglandi.";

        return alreadyReferenced
            ? "Input Actions: InputActionManager zaten dogru kurulu."
            : "Input Actions: Mevcut InputActionManager'a " +
              System.IO.Path.GetFileName(DefaultActionAssetPath) + " baglandi.";
    }
}
#endif
