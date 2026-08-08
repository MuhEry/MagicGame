#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Meta OpenXR, Quest Link oturumu hazir degilken xrGetSystem icinde
/// XR_ERROR_FORM_FACTOR_UNAVAILABLE donduruyor. Unity 6 Editor bu hatadan sonra
/// OpenXR Input baslangicinda kilitlenebiliyor. Play'e girmeden once Meta'nin
/// yerel cihaz durumunu kontrol ederek bu native kilitlenmeyi onler.
/// </summary>
[InitializeOnLoad]
public static class AirLinkPlayGuard
{
    const string LogPrefix = "[Air Link Koruma]";

    static AirLinkPlayGuard()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("Tools/Gece Vardiyasi/Air Link Durumunu Kontrol Et", false, 41)]
    static void CheckFromMenu()
    {
        bool ready = AirLinkSafeXrBootstrap.IsAirLinkReady(out string reason);
        EditorUtility.DisplayDialog(
            ready ? "Air Link Hazir" : "Air Link Hazir Degil",
            reason,
            "Tamam");
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
            EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows)
        {
            CancelPlay(
                "Air Link ile Editor Play testi icin aktif platform Windows olmali.\n\n" +
                "File > Build Profiles > Windows secip Switch Platform yap. " +
                "Android'i yalnizca APK alacagin zaman sec.");
            return;
        }

        if (!AirLinkSafeXrBootstrap.IsAirLinkReady(out string reason))
            CancelPlay(reason);
    }

    static void CancelPlay(string reason)
    {
        EditorApplication.isPlaying = false;
        Debug.LogError($"{LogPrefix} Play engellendi. {reason}");
        EditorApplication.delayCall += () => EditorUtility.DisplayDialog(
            "Air Link Hazir Degil - Play Baslatilmadi",
            reason +
            "\n\nGozlukte: Hizli Ayarlar > Quest Link > Baslat. " +
            "PC VR ortami tamamen acildiktan sonra kontrolculeri uyandir ve Play'e tekrar bas.",
            "Tamam");
    }

}
#endif
