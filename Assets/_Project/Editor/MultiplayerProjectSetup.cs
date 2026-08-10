#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Alteruna.Multiplayer.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

public static class MultiplayerProjectSetup
{
    const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
    const string BaseItemPrefabPath = "Assets/_Project/Items/Prefabs/item.prefab";
    const string ControllerRigPrefabPath =
        "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string ControllerRigName = "XR Origin (XR Rig)";
    const string LegacyHandsRigName = "XR Origin Hands (XR Rig)";

    // DIKKAT: Burada BILEREK [InitializeOnLoadMethod] YOK.
    //
    // Eski surum, Main.unity her acildiginda sahneyi kendiliginden degistirip
    // EditorSceneManager.SaveScene ile KAYDEDIYORDU. Sonuc: iki kisi ayni sahneyi
    // acinca herkeste farkli bir Main.unity olusuyor, git surekli catisiyor ve
    // elle yapilan duzeltmeler bir sonraki acilista geri aliniyordu.
    // Kurulum artik yalnizca menuden, kullanicinin istegiyle calisir.
    public static bool SetupLooksComplete()
    {
        GameObject multiplayerGo = FindRoot(SceneManager.GetActiveScene(), "Multiplayer");
        GameObject rig = FindRig();
        NetworkShiftCoordinator coordinator = Object.FindFirstObjectByType<NetworkShiftCoordinator>();
        Spawner spawner = Object.FindFirstObjectByType<Spawner>();
        GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseItemPrefabPath);

        return multiplayerGo != null &&
               multiplayerGo.GetComponent<AlterunaComponents.MultiplayerManager>() != null &&
               rig != null && rig.GetComponent<AlterunaComponents.Avatar>() != null &&
               coordinator != null && spawner != null &&
               itemPrefab != null && itemPrefab.GetComponentInChildren<NetworkItemState>(true) != null;
    }

    [MenuItem("Tools/Gece Vardiyası/Multiplayer Kurulumunu Uygula", false, 40)]
    public static void ApplySetupFromMenu()
    {
        ApplySetup(true);
    }

    /// <summary>MainSceneBuilder sahneyi sifirdan kurduktan sonra bunu cagirir.</summary>
    public static void ApplySetupSilently()
    {
        ApplySetup(false);
    }

    /// <summary>CI/Codex icin Main sahnesini acip guvenli kurulumu kaydeder.</summary>
    public static void ApplyMainSceneFromCommandLine()
    {
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        ApplySetup(false);
    }

    static void ApplySetup(bool showDialog)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Multiplayer Kurulumu", "Önce Play Mode'dan çık.", "Tamam");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainScenePath)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Multiplayer Kurulumu",
                    "Önce Assets/_Project/Scenes/Main.unity sahnesini aç.", "Tamam");
            return;
        }

        ConfigureItemPrefab();
        ConfigureMainScene(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[MultiplayerProjectSetup] Alteruna manager, XR avatar, network spawner ve eşya senkronizasyonu hazırlandı.");
        if (showDialog)
            EditorUtility.DisplayDialog("Multiplayer Kurulumu", "Kurulum tamamlandı ve Main sahnesi kaydedildi.", "Tamam");
    }

    static void ConfigureItemPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BaseItemPrefabPath);
        try
        {
            XRGrabInteractable grab = root.GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = root.GetComponentInChildren<XRGrabInteractable>(true);

            GameObject target = grab != null ? grab.gameObject : root;
            GetOrAdd<RigidbodySynchronizable>(target);
            GetOrAdd<Alteruna.XRGrabInteractableSync>(target);
            GetOrAdd<NetworkItemState>(target);
            GetOrAdd<ItemOwnership>(target);

            PrefabUtility.SaveAsPrefabAsset(root, BaseItemPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void ConfigureMainScene(Scene scene)
    {
        ShiftManager shiftManager = Object.FindFirstObjectByType<ShiftManager>();
        ItemSpawner itemSpawner = Object.FindFirstObjectByType<ItemSpawner>();
        if (shiftManager == null || itemSpawner == null)
            throw new System.InvalidOperationException("Main sahnesinde ShiftManager veya ItemSpawner bulunamadı.");

        GameObject systems = shiftManager.gameObject;
        NetworkShiftCoordinator coordinator = GetOrAdd<NetworkShiftCoordinator>(systems);
        Spawner networkSpawner = GetOrAdd<Spawner>(systems);
        networkSpawner.ForceSync = true;
        networkSpawner.SpawnableObjects.Clear();

        SerializedObject itemSpawnerSo = new SerializedObject(itemSpawner);
        SerializedProperty entries = itemSpawnerSo.FindProperty("itemPrefabs");
        for (int i = 0; i < entries.arraySize; i++)
        {
            GameObject prefab = entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue as GameObject;
            if (prefab != null)
                networkSpawner.SpawnableObjects.Add(prefab);
        }
        itemSpawnerSo.FindProperty("networkSpawner").objectReferenceValue = networkSpawner;
        itemSpawnerSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject coordinatorSo = new SerializedObject(coordinator);
        coordinatorSo.FindProperty("shiftManager").objectReferenceValue = shiftManager;
        coordinatorSo.FindProperty("itemSpawner").objectReferenceValue = itemSpawner;
        coordinatorSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject shiftSo = new SerializedObject(shiftManager);
        shiftSo.FindProperty("networkCoordinator").objectReferenceValue = coordinator;
        shiftSo.ApplyModifiedPropertiesWithoutUndo();

        GameObject multiplayerGo = FindRoot(scene, "Multiplayer");
        if (multiplayerGo == null)
            multiplayerGo = new GameObject("Multiplayer");

        AlterunaComponents.MultiplayerManager manager =
            GetOrAdd<AlterunaComponents.MultiplayerManager>(multiplayerGo);
        GetOrAdd<Alteruna.AutoJoin>(multiplayerGo);

        GameObject rig = EnsureControllerOnlyRig(scene);
        if (rig == null)
            throw new System.InvalidOperationException("XR Origin Hands (XR Rig) bulunamadı.");

        AlterunaComponents.Avatar avatar = GetOrAdd<AlterunaComponents.Avatar>(rig);
        GetOrAdd<TransformSynchronizable>(rig);
        GetOrAdd<Alteruna.XRIAvatar>(rig);

        // Mimari kural 8: Camera.main dogrudan cagrilmaz, PlayerRefs uzerinden alinir.
        // Faz 2'de sahnede iki avatar oldugu icin "ana kamera" belirsizlesir; ItemProbe'un
        // parlama kanali yanlis kafaya bakarsa parlak esya hic parlamiyor gibi gorunur.
        //
        // PlayerRefs BILEREK rig'in DISINDA, Systems altinda durur. Alteruna'nin
        // XRIAvatar bileseni uzak avatari temizlerken her alt Behaviour icin
        // type.Namespace.Length okur; global namespace'teki (namespace'siz) bir
        // scriptte Namespace NULL doner ve NullReferenceException ile temizlik
        // yarida kalir -> ikinci oyuncunun avatari bozuk kalir. Bu projedeki tum
        // scriptler global namespace'te oldugu icin rig'e KENDI scriptlerimizi EKLEME.
        PlayerRefs playerRefs = GetOrAdd<PlayerRefs>(systems);
        Transform rigCamera = rig.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name == "Main Camera");
        if (rigCamera != null)
        {
            SerializedObject playerRefsSo = new SerializedObject(playerRefs);
            SerializedProperty cameraProperty = playerRefsSo.FindProperty("mainCamera");
            if (cameraProperty != null)
                cameraProperty.objectReferenceValue = rigCamera.GetComponent<Camera>();
            playerRefsSo.ApplyModifiedPropertiesWithoutUndo();
        }

        AddTransformSync(rig, "Main Camera");
        AddTransformSync(rig, "Left Controller");
        AddTransformSync(rig, "Right Controller");

        SerializedObject managerSo = new SerializedObject(manager);
        // Quest'te Alteruna'nin sahne yuklenirken baglanmasi OpenXR acilisini
        // bloke edebiliyor. AutoJoin ilk kareden sonra Connect() cagirir.
        SetBool(managerSo, "ConnectOnStart", false);
        SetInt(managerSo, "_maxPlayers", 2);
        SetBool(managerSo, "AutoJoinFirstRoom", false);
        SetBool(managerSo, "AutoJoinOwnRoom", false);
        SerializedProperty avatarSpawning = managerSo.FindProperty("AvatarSpawning");
        if (avatarSpawning != null)
            avatarSpawning.enumValueIndex = 1;
        SerializedProperty avatarPrefab = managerSo.FindProperty("AvatarPrefab");
        if (avatarPrefab != null)
            avatarPrefab.objectReferenceValue = avatar;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        ConfigureAvatarTemplate(systems, rig);
        multiplayerGo.SetActive(false);

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(networkSpawner);
        EditorUtility.SetDirty(coordinator);
        EditorUtility.SetDirty(itemSpawner);
        EditorUtility.SetDirty(shiftManager);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>
    /// Sahnedeki rig, Alteruna icin bir avatar SABLONUDUR ve PASIF durmalidir.
    /// Alteruna'nin kendi ornek sahnesinde de oyle: "XR Avatar Rig" nesnesinin
    /// m_IsActive override'i 0. Sablon aktif kalirsa kendi UID'siyle kaydolur,
    /// Alteruna onu klonlayinca ayni UID iki nesnede olur ve orijinal rig
    /// "Synchronizable not registered" ile senkron gonderemez hale gelir;
    /// ayrica sahnede iki kamera ve iki AudioListener kalir.
    /// </summary>
    static void ConfigureAvatarTemplate(GameObject systems, GameObject rig)
    {
        // Quest build'i yalnizca Touch kontrolculeri kullanir. Modality manager
        // aktif kalirsa avatar spawn edilirken el subsystem'ini arayabilir ve Meta
        // runtime'inda OVRInput thread'ini yogun bir HAND_TRACKER dongusune sokabilir.
        foreach (XRInputModalityManager modalityManager in
                 rig.GetComponentsInChildren<XRInputModalityManager>(true))
        {
            modalityManager.enabled = false;
            EditorUtility.SetDirty(modalityManager);
        }

        if (rig.activeSelf)
        {
            rig.SetActive(false);
            Debug.Log("[MultiplayerProjectSetup] XR rig avatar SABLONU olarak pasiflestirildi " +
                      "(Alteruna ornek sahnesindeki desen). Oyuncu rig'ini Alteruna spawn eder.");
        }

        OfflineRigFallback fallback = GetOrAdd<OfflineRigFallback>(systems);
        SerializedObject fallbackSo = new SerializedObject(fallback);
        SerializedProperty template = fallbackSo.FindProperty("rigTemplate");
        if (template != null)
            template.objectReferenceValue = rig;
        SerializedProperty networkRoot = fallbackSo.FindProperty("networkRoot");
        if (networkRoot != null)
            networkRoot.objectReferenceValue = FindRoot(systems.scene, "Multiplayer");
        fallbackSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fallback);
    }

    static GameObject FindRoot(Scene scene, string objectName)
    {
        return scene.IsValid()
            ? scene.GetRootGameObjects().FirstOrDefault(root => root.name == objectName)
            : null;
    }

    /// <summary>
    /// GameObject.Find yalnizca AKTIF nesneleri bulur. Rig artik sablon oldugu icin
    /// pasif duruyor; pasifleri de tarayan bir arama sart.
    /// </summary>
    public static GameObject FindRig()
    {
        GameObject direct = GameObject.Find(ControllerRigName);
        if (direct != null)
            return direct;

        foreach (Transform candidate in Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.parent == null &&
                (candidate.name == ControllerRigName || candidate.name == LegacyHandsRigName))
                return candidate.gameObject;
        }

        return null;
    }

    static GameObject EnsureControllerOnlyRig(Scene scene)
    {
        GameObject existingControllerRig = FindRoot(scene, ControllerRigName);
        if (existingControllerRig != null)
            return existingControllerRig;

        GameObject controllerRigPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(ControllerRigPrefabPath);
        if (controllerRigPrefab == null)
            throw new System.InvalidOperationException(
                $"Kontrolcu XR rig prefabi bulunamadi: {ControllerRigPrefabPath}");

        GameObject legacyHandsRig = FindRoot(scene, LegacyHandsRigName);
        Vector3 position = legacyHandsRig != null ? legacyHandsRig.transform.position : Vector3.zero;
        Quaternion rotation = legacyHandsRig != null ? legacyHandsRig.transform.rotation : Quaternion.identity;
        Vector3 scale = legacyHandsRig != null ? legacyHandsRig.transform.localScale : Vector3.one;

        GameObject controllerRig =
            PrefabUtility.InstantiatePrefab(controllerRigPrefab, scene) as GameObject;
        if (controllerRig == null)
            throw new System.InvalidOperationException("Kontrolcu XR rig sahneye eklenemedi.");

        controllerRig.name = ControllerRigName;
        controllerRig.transform.SetPositionAndRotation(position, rotation);
        controllerRig.transform.localScale = scale;
        controllerRig.SetActive(false);

        if (legacyHandsRig != null)
        {
            Object.DestroyImmediate(legacyHandsRig);
            Debug.LogWarning(
                "[MultiplayerProjectSetup] Hands Interaction Demo avatar kaldirildi; " +
                "yerine controller-only XR Origin kuruldu. Bu, Quest OVRInput/HAND_TRACKER " +
                "kilitlenmesini engeller.");
        }

        return controllerRig;
    }

    static void AddTransformSync(GameObject root, string childName)
    {
        Transform child = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name == childName);
        if (child != null)
            GetOrAdd<TransformSynchronizable>(child.gameObject);
    }

    static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    static void SetInt(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.intValue = value;
    }
}
#endif
