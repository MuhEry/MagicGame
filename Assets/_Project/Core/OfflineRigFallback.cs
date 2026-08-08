using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

/// <summary>
/// Oyuncu rig'i bekcisi. Sahnedeki XR rig, Alteruna icin pasif bir avatar
/// sablonudur. Odaya girilemezse goruntusuz kalmamak icin ag bilesenleri kapali
/// gecici bir kopya acar; Alteruna yerel avatari hazir olunca bu kopyayi kapatir.
/// Avatar sablonunun kendisi hicbir zaman etkinlestirilmez.
/// </summary>
[DisallowMultipleComponent]
public sealed class OfflineRigFallback : MonoBehaviour
{
    [Tooltip("Avatar sablonu olarak kullanilan, sahnede PASIF duran XR rig.")]
    [SerializeField] GameObject rigTemplate;

    [Tooltip("Bu kadar saniye aktif kamera bulunamazsa bekci devreye girer.")]
    [SerializeField, Min(0.5f)] float watchdogDelay = 12f;

    float elapsed;
    float nextControllerCheck;
    float nextControllerLog;
    bool rescued;
    bool loggedControllerOverride;
    GameObject offlineRig;

    float EffectiveWatchdogDelay => Mathf.Max(12f, watchdogDelay);

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
            GameObject spawned = FindActiveSpawnedLocalAvatar();
            if (spawned != null)
            {
                offlineRig.SetActive(false);
                Destroy(offlineRig);
                offlineRig = null;
                rescued = false;
                elapsed = 0f;

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
                $"[Rig Bekcisi] {EffectiveWatchdogDelay:0} sn boyunca aktif kamera bulunamadi. " +
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
        offlineRig.SetActive(true);

        Debug.LogWarning(
            $"[Rig Bekcisi] {EffectiveWatchdogDelay:0} sn boyunca aktif kamera bulunamadi" +
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

    GameObject FindActiveSpawnedLocalAvatar()
    {
        GameObject cameraCandidate = null;

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
            if (avatar.IsPossessor)
                return go;

            // Uzak avatarlar XRIAvatar tarafindan kamera ve AudioListener'dan
            // arindirilir. Sahiplik bayragi bir kare gec yazilsa bile aktif
            // kamerasi kalan avatar yerel rig'dir.
            Camera camera = go.GetComponentInChildren<Camera>(true);
            if (camera != null && camera.enabled)
                cameraCandidate = go;
        }

        return cameraCandidate;
    }

    void MaintainTrackedControllerMode()
    {
        bool leftTracked = IsControllerTracked(true);
        bool rightTracked = IsControllerTracked(false);
        if (!leftTracked && !rightTracked)
            return;

        GameObject rig = FindActiveSpawnedLocalAvatar();
        if (rig == null && offlineRig != null && offlineRig.activeInHierarchy)
            rig = offlineRig;

        if (rig == null)
            return;

        bool changed = false;
        if (leftTracked)
            changed |= SetInputGroup(rig, "Left Controller", "Left Hand");
        if (rightTracked)
            changed |= SetInputGroup(rig, "Right Controller", "Right Hand");

        if (changed && !loggedControllerOverride)
        {
            loggedControllerOverride = true;
            Debug.Log(
                $"[XR Kontrol] Air Link kontrolculeri izleniyor. " +
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
