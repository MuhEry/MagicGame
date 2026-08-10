using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// Oyuncu rig'i bekcisi. Sahnedeki XR rig, Alteruna icin pasif bir avatar
/// sablonudur. Odaya girilemezse goruntusuz kalmamak icin ag bilesenleri kapali
/// gecici bir kopya acar; Alteruna yerel avatari hazir olunca bu kopyayi kapatir.
/// Avatar sablonunun kendisi hicbir zaman etkinlestirilmez.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public sealed class OfflineRigFallback : MonoBehaviour
{
    [Tooltip("Avatar sablonu olarak kullanilan, sahnede PASIF duran XR rig.")]
    [SerializeField] GameObject rigTemplate;

    [Tooltip("Ilk XR karesi goruldukten sonra etkinlestirilecek Alteruna kok nesnesi.")]
    [SerializeField] GameObject networkRoot;

    [Tooltip("Alteruna servis hazirligi bu sureyi asarsa ag kapali kalir; XR donmaz.")]
    [SerializeField, Min(2f)] float networkStartupTimeout = 12f;

    [Tooltip("Bu kadar saniye aktif kamera bulunamazsa bekci devreye girer.")]
    [SerializeField, Min(0.5f)] float watchdogDelay = 12f;

    [Tooltip("Alteruna yerel kamerasi hazir gorundukten sonra cevrimdisi rig'in korunacagi sure.")]
    [SerializeField, Min(0.1f)] float localRigHandoffDelay = 0.75f;

    float elapsed;
    float nextControllerCheck;
    float nextControllerLog;
    bool rescued;
    bool loggedControllerOverride;
    GameObject offlineRig;
    float localRigReadySince = -1f;

    float EffectiveWatchdogDelay => Mathf.Max(12f, watchdogDelay);

    void Awake()
    {
        // Kamera ve kontrolculer herhangi bir Alteruna Awake/Start metodundan
        // once hazir olsun. Boylece lisans veya ag islemi gecikse bile Quest
        // bos compositor katmaninda/gri ortamda kalmaz.
        if (HasActiveCamera())
            return;

        rescued = true;
        Rescue();
    }

    IEnumerator Start()
    {
        // En az bir yerel XR karesi cizilsin; Alteruna bundan sonra baslasin.
        yield return null;

        if (networkRoot == null || networkRoot.activeSelf)
            yield break;

        AlterunaComponents.MultiplayerManager manager =
            networkRoot.GetComponentInChildren<AlterunaComponents.MultiplayerManager>(true);
        if (manager == null)
        {
            Debug.LogError(
                "[Multiplayer] Alteruna kok nesnesinde MultiplayerManager bulunamadi; " +
                "ag baslatilmadi ve XR cevrimdisi calismaya devam edecek.", this);
            yield break;
        }

        Debug.Log(
            "[Multiplayer] Ilk XR karesi hazir. Alteruna servis cekirdegi worker thread'de hazirlaniyor.",
            this);

        Task<AlterunaServicePrewarmer.Result> prewarm = AlterunaServicePrewarmer.Begin(manager);
        float deadline = Time.realtimeSinceStartup + Mathf.Max(2f, networkStartupTimeout);
        while (!prewarm.IsCompleted && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!prewarm.IsCompleted)
        {
            Debug.LogError(
                $"[Multiplayer] Alteruna servis hazirligi {networkStartupTimeout:0} saniyeyi asti. " +
                "Ana thread korunarak ag devre disi birakildi; XR donmayacak.", this);
            yield break;
        }

        AlterunaServicePrewarmer.Result result = prewarm.Result;
        if (!result.Success)
        {
            Debug.LogException(result.Error, this);
            Debug.LogError(
                "[Multiplayer] Alteruna servis hazirligi basarisiz; " +
                "ag devre disi birakildi ve XR cevrimdisi calismaya devam edecek.", this);
            yield break;
        }

        networkRoot.SetActive(true);
        Debug.Log(
            "[Multiplayer] Alteruna servis cekirdegi hazir; ag nesneleri ana thread'de etkinlestirildi.",
            this);
    }

    void Update()
    {
        if (Time.unscaledTime >= nextControllerCheck)
        {
            nextControllerCheck = Time.unscaledTime + 0.25f;
            MaintainTrackedControllerMode();
        }

        if (Time.unscaledTime >= nextControllerLog)
        {
            nextControllerLog = Time.unscaledTime + 2f;
            LogControllerState();
        }

        // Odaya girme, cevrimdisi watchdog'dan daha gec tamamlanabilir. Yerel
        // avatar acildigi anda gecici rig kaldirilir; boylece tek kamera ve tek
        // kontrolcu/interactor takimi kalir.
        if (offlineRig != null)
        {
            GameObject spawned = FindReadySpawnedLocalAvatar();
            if (spawned == null)
            {
                localRigReadySince = -1f;
            }
            else if (localRigReadySince < 0f)
            {
                // Possession callback'i ile kamera/TrackedPoseDriver ayni karede
                // hazir olmayabilir. Fallback kamerayi hemen kapatmak siyah kareye
                // neden olur; yeni rig'i en az kisa bir sure saglikli tut.
                localRigReadySince = Time.unscaledTime;
            }
            else if (Time.unscaledTime - localRigReadySince >= localRigHandoffDelay)
            {
                offlineRig.SetActive(false);
                Destroy(offlineRig);
                offlineRig = null;
                rescued = false;
                elapsed = 0f;
                localRigReadySince = -1f;

                Debug.Log(
                    $"[Rig Bekcisi] Alteruna'nin yerel avatari ('{spawned.name}') hazir. " +
                    "Gecici cevrimdisi rig kapatildi; tek kamera ve tek kontrolcu rig'i kaldi.", this);
            }
        }

        if (rescued)
            return;

        if (HasActiveCamera())
        {
            elapsed = 0f;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < EffectiveWatchdogDelay)
            return;

        rescued = true;
        Rescue();
    }

    static bool HasActiveCamera()
    {
        foreach (Camera camera in FindObjectsByType<Camera>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (camera != null && camera.enabled && camera.isActiveAndEnabled)
                return true;
        }

        return false;
    }

    void Rescue()
    {
        NetworkShiftCoordinator network = NetworkShiftCoordinator.Instance;
        bool inRoom = network != null && network.IsInRoom;

        // Alteruna'nin urettigi avatar pasif kaldiysa yeni bir rig uretmek yerine
        // onu ac. UID'leri Alteruna atadigi icin multiplayer icin dogru nesne odur.
        GameObject spawned = FindInactiveSpawnedAvatar();
        if (spawned != null)
        {
            spawned.SetActive(true);
            Debug.LogWarning(
                "[Rig Bekcisi] Aktif kamera bulunamadi. " +
                $"Alteruna'nin spawn ettigi avatar ('{spawned.name}') acildi.", this);
            return;
        }

        if (rigTemplate == null)
        {
            Debug.LogError(
                "[Rig Bekcisi] Aktif kamera yok ve acilacak bir rig de yok. " +
                "Inspector'da 'Rig Template' alani bos.", this);
            return;
        }

        // KRITIK: rigTemplate'i dogrudan acma. O nesne Alteruna'nin avatar
        // prefabi ve sabit network UID'leri tasiyor. Dogrudan acilirsa UID'leri
        // kaydeder; Alteruna ayni sablonu klonladiginda yerel avatar
        // "Synchronizable already registered" cakismasina girer.
        offlineRig = Instantiate(
            rigTemplate,
            rigTemplate.transform.position,
            rigTemplate.transform.rotation);
        offlineRig.name = rigTemplate.name + " (Offline)";
        DisableAlterunaBehaviours(offlineRig);
        ConfigureControllerOnlyRig(offlineRig);
        offlineRig.SetActive(true);

        Debug.LogWarning(
            "[Rig Bekcisi] Aktif kamera bulunamadi" +
            (inRoom ? " (odadayiz ama Alteruna avatar acmadi)" : " (odaya girilemedi)") +
            ". Ag bilesenleri kapali gecici XR rig acildi.", this);
    }

    static void DisableAlterunaBehaviours(GameObject rig)
    {
        foreach (MonoBehaviour behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            string componentNamespace = behaviour.GetType().Namespace;
            if (!string.IsNullOrEmpty(componentNamespace) &&
                componentNamespace.StartsWith("Alteruna", StringComparison.Ordinal))
            {
                behaviour.enabled = false;
            }
        }
    }

    GameObject FindInactiveSpawnedAvatar()
    {
        foreach (Alteruna.Multiplayer.Unity.Avatar avatar in
                 FindObjectsByType<Alteruna.Multiplayer.Unity.Avatar>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (avatar == null)
                continue;

            GameObject go = avatar.gameObject;
            if (go == rigTemplate || go == offlineRig)
                continue;

            if (!go.activeSelf)
                return go;
        }

        return null;
    }

    GameObject FindReadySpawnedLocalAvatar()
    {
        foreach (Alteruna.Multiplayer.Unity.Avatar avatar in
                 FindObjectsByType<Alteruna.Multiplayer.Unity.Avatar>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (avatar == null)
                continue;

            GameObject go = avatar.gameObject;
            if (go == rigTemplate || go == offlineRig || !go.activeInHierarchy)
                continue;

            // Alteruna spawn ettigi avatarin adini kullanici/oda bilgisiyle
            // degistirebildigi icin "(Clone)" adina guvenme.
            // Kamera aktarimi yalnizca SDK sahipligi kesinlestirdikten ve yeni
            // rig'in aktif bir kamerası olduktan sonra yapilir. Uzak avatarin
            // kisa sure acik kalan kamerasini yerel kamera sanma.
            if (avatar.IsPossessor && HasUsableCamera(go))
                return go;
        }

        return null;
    }

    static bool HasUsableCamera(GameObject rig)
    {
        foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
        {
            if (camera != null && camera.enabled && camera.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    void MaintainTrackedControllerMode()
    {
        bool leftTracked = IsControllerTracked(true);
        bool rightTracked = IsControllerTracked(false);
        if (!leftTracked && !rightTracked)
            return;

        GameObject rig = FindReadySpawnedLocalAvatar();
        if (rig == null && offlineRig != null && offlineRig.activeInHierarchy)
            rig = offlineRig;

        if (rig == null)
            return;

        ConfigureControllerOnlyRig(rig);

        bool changed = false;
        if (leftTracked)
            changed |= SetInputGroup(rig, "Left Controller", "Left Hand");
        if (rightTracked)
            changed |= SetInputGroup(rig, "Right Controller", "Right Hand");

        if (changed && !loggedControllerOverride)
        {
            loggedControllerOverride = true;
            Debug.Log(
                $"[XR Kontrol] Quest Link kontrolculeri izleniyor. " +
                $"Sol={leftTracked}, Sag={rightTracked}; fiziksel kontrolcu gruplari etkin tutuluyor.",
                this);
        }
    }

    static bool SetInputGroup(GameObject rig, string controllerName, string handName)
    {
        Transform controller = FindDescendant(rig.transform, controllerName);
        Transform hand = FindDescendant(rig.transform, handName);
        bool changed = false;

        if (controller != null && !controller.gameObject.activeSelf)
        {
            controller.gameObject.SetActive(true);
            changed = true;
        }

        if (hand != null && hand.gameObject.activeSelf)
        {
            hand.gameObject.SetActive(false);
            changed = true;
        }

        return changed;
    }

    static void ConfigureControllerOnlyRig(GameObject rig)
    {
        if (rig == null)
            return;

        // Bu build'de OpenXR el takibi bilerek kapali. XRI modality manager
        // subsystem'i her kare yeniden arayip uyari basmasin; Touch rig'i sabit tut.
        foreach (XRInputModalityManager manager in
                 rig.GetComponentsInChildren<XRInputModalityManager>(true))
        {
            manager.enabled = false;
        }

        SetInputGroup(rig, "Left Controller", "Left Hand");
        SetInputGroup(rig, "Right Controller", "Right Hand");
    }

    static Transform FindDescendant(Transform root, string objectName)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == objectName)
                return candidate;
        }

        return null;
    }

    static bool IsControllerTracked(bool left)
    {
        InputDeviceCharacteristics handedness = left
            ? InputDeviceCharacteristics.Left
            : InputDeviceCharacteristics.Right;
        var xrDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.HeldInHand |
            handedness,
            xrDevices);

        foreach (UnityEngine.XR.InputDevice device in xrDevices)
        {
            if (!device.isValid)
                continue;

            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked)
                return true;

            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out UnityEngine.XR.InputTrackingState state) &&
                (state & (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation)) != 0)
                return true;
        }

        InternedString usage = left
            ? UnityEngine.InputSystem.CommonUsages.LeftHand
            : UnityEngine.InputSystem.CommonUsages.RightHand;
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (!(device is XRController controller) || !device.usages.Contains(usage))
                continue;

            if (controller.isTracked.isPressed || (controller.trackingState.ReadValue() & 3) != 0)
                return true;
        }

        return false;
    }

    void LogControllerState()
    {
        var report = new StringBuilder("[XR Kontrol Testi]");
        AppendHandState(report, true);
        AppendHandState(report, false);
        Debug.Log(report.ToString(), this);
    }

    static void AppendHandState(StringBuilder report, bool left)
    {
        string label = left ? "Sol" : "Sag";
        InternedString usage = left
            ? UnityEngine.InputSystem.CommonUsages.LeftHand
            : UnityEngine.InputSystem.CommonUsages.RightHand;
        XRController newest = null;
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device is XRController controller && device.usages.Contains(usage) &&
                (newest == null || device.lastUpdateTime > newest.lastUpdateTime))
            {
                newest = controller;
            }
        }

        if (newest == null)
        {
            report.Append($" | {label}: cihaz yok");
            return;
        }

        float trigger = ReadAxis(newest, "trigger");
        float grip = ReadAxis(newest, "grip");
        Vector3 position = newest.devicePosition.ReadValue();
        report.Append(
            $" | {label}: {newest.displayName}, tracked={newest.isTracked.isPressed}, " +
            $"state={newest.trackingState.ReadValue()}, pos={position:F2}, " +
            $"trigger={trigger:F2}, grip={grip:F2}");
    }

    static float ReadAxis(UnityEngine.InputSystem.InputDevice device, string controlName)
    {
        var control = device.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>(controlName);
        return control != null ? control.ReadValue() : 0f;
    }
}
