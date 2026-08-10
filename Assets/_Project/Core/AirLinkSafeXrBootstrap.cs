#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using Debug = UnityEngine.Debug;

/// <summary>
/// Windows Editor/Player'da OpenXR'i yalnizca Meta Quest Link (USB veya Air Link)
/// PC VR oturumu gercekten hazirken baslatir. Hazir olmayan runtime'a xrGetSystem cagrisi
/// yapilmasini ve Unity 6 Editor'un native OpenXR baslangicinda kilitlenmesini
/// engeller. Android kendi otomatik XR yasam dongusunu kullanmaya devam eder.
/// </summary>
public static class AirLinkSafeXrBootstrap
{
    const string LogPrefix = "[Air Link Guvenli XR]";

    [Serializable]
    sealed class DeviceCache
    {
        public DeviceInfo[] devices;
    }

    [Serializable]
    sealed class DeviceInfo
    {
        public string type;
        public string subtype;
        public string connectionState;
        public string rdConnectionState;
        public string powerState;
        public bool isUsingAirLink;
    }

#if UNITY_STANDALONE_WIN
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ScheduleSafeXrStart()
    {
#if UNITY_EDITOR
        // Alteruna 2.1.x'in yerel ag servisi Unity 6 Editor'de Start/OpenPort
        // icinde ana thread'i kilitliyor. Air Link Play testi ag oturumuna ihtiyaç
        // duymadigi icin yalnizca Editor'de, Start calismadan once devre disi birak.
        // Android ve Windows Player buildlerinde multiplayer aynen etkin kalir.
        SceneManager.sceneLoaded -= DisableAlterunaForEditorAirLinkTest;
        SceneManager.sceneLoaded += DisableAlterunaForEditorAirLinkTest;
#endif

        GameObject starter = new GameObject(nameof(AirLinkSafeXrStarter));
        UnityEngine.Object.DontDestroyOnLoad(starter);
        starter.AddComponent<AirLinkSafeXrStarter>();
    }

#if UNITY_EDITOR
    static void DisableAlterunaForEditorAirLinkTest(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= DisableAlterunaForEditorAirLinkTest;

        int managerCount = 0;
        int autoJoinCount = 0;
        int synchronizableCount = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            AlterunaComponents.MultiplayerManager[] managers =
                root.GetComponentsInChildren<AlterunaComponents.MultiplayerManager>(true);
            foreach (AlterunaComponents.MultiplayerManager manager in managers)
            {
                manager.enabled = false;
                managerCount++;
            }

            Alteruna.AutoJoin[] autoJoins =
                root.GetComponentsInChildren<Alteruna.AutoJoin>(true);
            foreach (Alteruna.AutoJoin autoJoin in autoJoins)
            {
                autoJoin.enabled = false;
                autoJoinCount++;
            }

            Alteruna.Multiplayer.Unity.Synchronizable[] synchronizables =
                root.GetComponentsInChildren<Alteruna.Multiplayer.Unity.Synchronizable>(true);
            foreach (Alteruna.Multiplayer.Unity.Synchronizable synchronizable in synchronizables)
            {
                synchronizable.enabled = false;
                synchronizableCount++;
            }
        }

        Debug.LogWarning(
            $"{LogPrefix} Unity 6 Editor uyumlulugu: {managerCount} Alteruna manager ve " +
            $"{autoJoinCount} AutoJoin, {synchronizableCount} Synchronizable devre disi. " +
            "Player/Android buildleri etkilenmez.");
    }
#endif

    internal static bool TryStartXr(out string result)
    {
        if (!IsAirLinkReady(out string reason))
        {
            result = $"{LogPrefix} XR baslatilmadi. {reason}";
            return false;
        }

        XRGeneralSettings general = XRGeneralSettings.Instance;
        XRManagerSettings manager = general != null ? general.Manager : null;
        if (manager == null)
        {
            result = $"{LogPrefix} XR Manager bulunamadi.";
            return false;
        }

        if (!manager.isInitializationComplete)
            manager.InitializeLoaderSync();

        if (!manager.isInitializationComplete || manager.activeLoader == null)
        {
            result = $"{LogPrefix} OpenXR loader baslatilamadi.";
            return false;
        }

        manager.StartSubsystems();
        result = $"{LogPrefix} {reason} OpenXR ve kontrolcu alt sistemleri baslatildi.";
        return true;
    }
#endif

    public static bool IsAirLinkReady(out string reason)
    {
        string cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Oculus",
            "DeviceCache.json");

        if (!File.Exists(cachePath))
        {
            reason = "Meta Horizon Link cihaz durumu bulunamadi. Meta Horizon Link uygulamasini ac.";
            return false;
        }

        try
        {
            DeviceCache cache = JsonUtility.FromJson<DeviceCache>(File.ReadAllText(cachePath));
            if (cache?.devices == null)
            {
                reason = "Meta cihaz listesi okunamadi. Meta Horizon Link'i yeniden baslat.";
                return false;
            }

            DeviceInfo headset = null;
            DeviceInfo leftTouch = null;
            DeviceInfo rightTouch = null;

            foreach (DeviceInfo device in cache.devices)
            {
                if (device == null)
                    continue;

                if (string.Equals(device.type, "headset", StringComparison.OrdinalIgnoreCase))
                    headset = device;
                else if (string.Equals(device.subtype, "lruby", StringComparison.OrdinalIgnoreCase))
                    leftTouch = device;
                else if (string.Equals(device.subtype, "rruby", StringComparison.OrdinalIgnoreCase))
                    rightTouch = device;
            }

            // isUsingAirLink yalnizca baglanti turunu bildirir. USB Quest Link'te
            // false olmasi beklenir ve kulakligin hazir olmadigi anlamina gelmez.
            bool headsetReady = IsConnectedAndActive(headset);
            bool controllersReady =
                IsConnectedAndActive(leftTouch) && IsConnectedAndActive(rightTouch);
            bool dashRunning = IsOculusDashRunning();

            if (headsetReady && dashRunning)
            {
                // Meta Horizon Link'in rdConnectionState alani bazi surumlerde aktif
                // Link oturumunda bile "disconnected" kalabiliyor. OculusDash ve
                // bagli/aktif gozluk, PC VR oturumu icin daha guvenilir kanit.
                // Touch kontrolculer Play oncesinde uyuyabilir ve runtime acikken
                // yeniden baglanabilir; bu nedenle Play'i engellemezler.
                string transport = headset.isUsingAirLink ? "Air Link" : "USB Link";
                reason = controllersReady
                    ? $"Quest {transport} PC VR oturumu ve iki Touch kontrolcu hazir."
                    : $"Quest {transport} PC VR oturumu hazir; Touch kontrolculeri uyandir.";
                return true;
            }

            if (!headsetReady)
            {
                reason = headset == null
                    ? "Meta Horizon Link'te Quest gozluk bulunamadi."
                    : "Quest Link baglantisi hazir degil " +
                      $"(cihaz={headset.connectionState}, guc={headset.powerState}, " +
                      $"AirLink={headset.isUsingAirLink}).";
                return false;
            }

            if (!dashRunning)
            {
                reason = "Gozluk bagli fakat OculusDash/PC VR ortami calismiyor. " +
                         "Gozlukte Quest Link'i Baslat.";
                return false;
            }

            reason = "Quest Link durumu dogrulanamadi.";
            return false;
        }
        catch (Exception exception)
        {
            reason = "Meta cihaz durumu okunamadi: " + exception.Message;
            return false;
        }
    }

    static bool IsConnectedAndActive(DeviceInfo device)
    {
        return device != null &&
               string.Equals(device.connectionState, "connected", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(device.powerState, "active", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsOculusDashRunning()
    {
        try
        {
            Process[] processes = Process.GetProcessesByName("OculusDash");
            bool running = processes.Length > 0;
            foreach (Process process in processes)
                process.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }
}

#if UNITY_STANDALONE_WIN
/// <summary>
/// Play gecisinde USB/Air Link'in dusmedigini tekrar kontrol eder ve yalnizca basarili
/// elle baslatmadan sonra XR'i guvenle kapatir.
/// </summary>
public sealed class AirLinkSafeXrStarter : MonoBehaviour
{
    bool started;

    IEnumerator Start()
    {
        // Domain reload ve sahne yuklenirken gozluk uykuya girerse Play oncesi
        // kontrol bayat kalabilir. Native OpenXR'a girmeden hemen once tekrar bak.
        yield return new WaitForSecondsRealtime(2f);

        started = AirLinkSafeXrBootstrap.TryStartXr(out string result);
        if (started)
            Debug.Log(result);
        else
            Debug.LogError(result);
    }

    void OnDestroy()
    {
        if (!started)
            return;

        XRManagerSettings manager = XRGeneralSettings.Instance != null
            ? XRGeneralSettings.Instance.Manager
            : null;

        if (manager == null || !manager.isInitializationComplete)
            return;

        if (manager.activeLoader != null)
            manager.StopSubsystems();

        manager.DeinitializeLoader();
    }
}
#endif
#endif
