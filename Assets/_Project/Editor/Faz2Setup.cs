using System.Collections.Generic;
using System.IO;
using System.Text;
using Alteruna.Multiplayer.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// 'Avatar' hem Alteruna'da hem UnityEngine'de (animasyon) var; takma ad sart.
using AlterunaAvatar = Alteruna.Multiplayer.Unity.Avatar;

/// <summary>
/// Faz 2 (Alteruna multiplayer) kurulumunu ACIK ISTEK UZERINE uygular.
///
/// NEDEN OTOMATIK DEGIL: onceki denemede kurulum [InitializeOnLoadMethod] ile
/// Main.unity her acildiginda sahneyi degistirip KAYDEDIYORDU. Herkeste farkli bir
/// Main.unity olustu ve elle yapilan duzeltmeler bir sonraki acilista geri alindi.
/// Bu dosyada otomatik calisan HICBIR SEY yok; yalnizca menuden tetiklenir.
///
/// Sahne KAYDEDILMEZ, yalnizca "kirli" isaretlenir - kaydetme karari kullanicinindir.
/// ProjectSettings, XR loader'lari ve URP ayarlari BU DOSYADAN HIC DEGISTIRILMEZ.
/// </summary>
static class Faz2Setup
{
    const string k_AvatarPrefabPath = "Assets/_Project/Prefabs/NetworkPlayerAvatar.prefab";
    const string k_ItemPrefabFolder = "Assets/_Project/Items/Prefabs";

    // Iki oyuncu tezgaha birlikte uzanirken gercek hayatta birbirine girmesin.
    // Merkeze 0,75 m -> aralarinda 1,5 m kalir.
    const float k_PlayerLateralOffset = 0.75f;

    // ------------------------------------------------------------------ menu

    [MenuItem("Tools/Gece Vardiyasi/Faz 2 Kurulumunu Uygula", false, 40)]
    static void ApplySetup()
    {
        if (!EditorUtility.DisplayDialog(
                "Faz 2 kurulumu",
                "ACIK sahneye Alteruna katmani eklenecek:\n\n" +
                "- ConnectOnStart KAPATILIR (buluta otomatik baglanma yok)\n" +
                "- NetworkShiftCoordinator (host-otoriter kopru)\n" +
                "- Alteruna Spawner + esya prefab listesi\n" +
                "- PlayerRefs (kamera + iki el)\n" +
                "- Ag avatari prefabi (kafa + 2 el) ve MultiplayerManager baglantisi\n" +
                "- Esya prefablarina ItemOwnership + Rigidbody/Transform senkronu\n\n" +
                "Sahne KAYDEDILMEZ, yalnizca degismis olarak isaretlenir.\n" +
                "ProjectSettings ve XR ayarlarina DOKUNULMAZ.\n\nDevam edilsin mi?",
                "Uygula", "Vazgec"))
            return;

        var log = new StringBuilder();

        MultiplayerManager manager = EnsureMultiplayerManager(log);
        Spawner spawner = EnsureSpawner(manager, log);
        EnsureCoordinator(log);
        EnsurePlayerRefs(log);
        WireItemSpawner(spawner, log);
        PrepareItemPrefabs(log);
        EnsureAvatarPrefab(manager, log);

        // EN SONA: bileseni kapatmak, once yapilan alan yazmalarini engellemesin.
        DisableNetworkOnPlay(manager, log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("[Faz 2 Kurulum]\n" + log +
                  "\nSahne KAYDEDILMEDI. Kontrol edip Ctrl+S ile kendin kaydet.");
    }

    [MenuItem("Tools/Gece Vardiyasi/Faz 2 Kurulumunu Kontrol Et", false, 41)]
    static void CheckSetup()
    {
        var log = new StringBuilder();
        int problems = 0;

        // Include: bilesen KAPALI oldugu icin varsayilan arama onu bulamaz.
        var manager = Object.FindFirstObjectByType<MultiplayerManager>(FindObjectsInactive.Include);
        problems += Report(log, manager != null, "MultiplayerManager sahnede");

        // Play'e basmak hicbir soket acmamali; ag yalnizca butona basilinca kalkar.
        problems += Report(log, manager != null && !manager.enabled,
            "MultiplayerManager bileseni KAPALI (Play'de ag ayaga kalkmiyor)");
        if (manager != null && manager.enabled)
            log.AppendLine("            -> Tools > Gece Vardiyasi > Agi Play'de Kapali Baslat");

        var coordinator = Object.FindFirstObjectByType<NetworkShiftCoordinator>();
        problems += Report(log, coordinator != null, "NetworkShiftCoordinator sahnede");

        var spawner = Object.FindFirstObjectByType<Spawner>();
        problems += Report(log, spawner != null, "Alteruna Spawner sahnede");

        var itemSpawner = Object.FindFirstObjectByType<ItemSpawner>();
        problems += Report(log, itemSpawner != null, "ItemSpawner sahnede");

        var refs = Object.FindFirstObjectByType<PlayerRefs>();
        problems += Report(log, refs != null, "PlayerRefs sahnede");
        if (refs != null)
        {
            problems += Report(log, refs.MainCamera != null, "PlayerRefs.MainCamera bagli");

            // Hangi objelerin bulundugunu YAZ: "eksik" demek tek basina is gormuyor.
            bool handsOk = refs.LeftHand != null && refs.RightHand != null;
            problems += Report(log, handsOk,
                $"PlayerRefs sol/sag el bagli (sol={(refs.LeftHand != null ? refs.LeftHand.name : "YOK")}, " +
                $"sag={(refs.RightHand != null ? refs.RightHand.name : "YOK")})");

            if (!handsOk)
                log.AppendLine("            -> Kurulum komutunu tekrar calistir. Bulunamazsa " +
                               "rig'deki el objelerini PlayerRefs'e Inspector'dan elle surukle.");
        }

        if (manager != null)
        {
            // Kilitlenmenin bir numarali sebebi: Play aninda buluta baglanma denemesi.
            var serialized = new SerializedObject(manager);
            SerializedProperty connectOnStart = serialized.FindProperty("ConnectOnStart");
            problems += Report(log, connectOnStart != null && !connectOnStart.boolValue,
                "ConnectOnStart KAPALI (Play'de buluta baglanmaya calismiyor)");

            SerializedProperty maxPlayers = serialized.FindProperty("_maxPlayers");
            problems += Report(log, maxPlayers != null && maxPlayers.intValue == 2,
                "MaxPlayers = 2 (ucretsiz katman siniri bilerek konmus)");

            problems += Report(log, manager.AvatarPrefab != null, "MultiplayerManager.AvatarPrefab bagli");
            problems += Report(log, manager.AvatarSpawning == AvatarBehavior.SpawnOnJoin,
                "AvatarSpawning = SpawnOnJoin");

            // Avatar sablonu olarak sahnedeki AKTIF rig kullanilirsa Alteruna onu
            // klonlar, ayni UID iki nesnede olur ve orijinal rig senkron gonderemez.
            bool avatarIsSceneObject = manager.AvatarPrefab != null &&
                                       manager.AvatarPrefab.gameObject.scene.IsValid();
            problems += Report(log, !avatarIsSceneObject,
                "AvatarPrefab bir PREFAB (sahnedeki aktif rig degil)");
        }

        if (spawner != null && itemSpawner != null)
            problems += Report(log, spawner.SpawnableObjects.Count > 0,
                $"Spawner.SpawnableObjects dolu ({spawner.SpawnableObjects.Count} esya)");

        // Yansima tabanli RPC'ler IL2CPP kirpmasinda sessizce yok olur:
        // editorde calisir, gozlukte hicbir sey senkron olmaz.
        problems += Report(log, File.Exists("Assets/link.xml"),
            "Assets/link.xml var (IL2CPP kirpmasi RPC'leri silmiyor)");

        problems += CheckAlterunaConfig(log);

        // "Auto"da Android ag izni eksik kalabilir: editorde calisir, gozlukte baglanmaz.
        problems += Report(log, PlayerSettings.Android.forceInternetPermission,
            "Android Internet Access = Require");
        if (!PlayerSettings.Android.forceInternetPermission)
            log.AppendLine("            -> Tools > Gece Vardiyasi > Android Ag Iznini Zorunlu Yap");

        problems += CheckXrInput(log);
        problems += CheckEditorXrStartup(log);

        // Rig'in altinda global namespace'li kendi scriptlerimiz olmamali:
        // Alteruna'nin avatar temizligi type.Namespace.Length okur ve NULL'da patlar.
        problems += CheckNoOwnScriptsUnderRig(log);

        log.AppendLine();
        log.AppendLine("  Not: Bu kurulum SADECE LAN kullanir (Host / JoinLan). Bulut, oda listesi");
        log.AppendLine("  ve lisans dogrulamasi devrede DEGIL - Application ID kayitli olmasa da calisir.");

        log.Insert(0, problems == 0
            ? "TUMU HAZIR.\n\n"
            : $"{problems} EKSIK VAR.\n\n");

        Debug.Log("[Faz 2 Kontrol]\n" + log);
    }

    /// <summary>
    /// Android ag iznini ZORUNLU yapar (Player Settings > Other Settings > Internet Access).
    ///
    /// NEDEN AYRI KOMUT: bu bir ProjectSettings degisikligidir. Kurulum komutu
    /// ProjectSettings'e dokunmaz; paylasilan yapilandirmayi habersiz degistirmemek icin
    /// bunu ayri ve istenerek calistirilan bir adim yaptik.
    ///
    /// NEDEN GEREKLI: "Auto" birakildiginda Unity, uygulamanin aga ihtiyaci olup
    /// olmadigini kendi tahmin eder. Alteruna'nin baglantisi bu tahmine takilabilir;
    /// sonuc klasik tablodur: editorde calisir, gozlukte hic baglanmaz.
    /// </summary>
    [MenuItem("Tools/Gece Vardiyasi/Android Ag Iznini Zorunlu Yap", false, 50)]
    static void ForceAndroidInternetPermission()
    {
        if (PlayerSettings.Android.forceInternetPermission)
        {
            Debug.Log("[Faz 2] Android Internet Access zaten 'Require'.");
            return;
        }

        PlayerSettings.Android.forceInternetPermission = true;
        AssetDatabase.SaveAssets();

        Debug.Log("[Faz 2] Android Internet Access = Require yapildi.\n" +
                  "ProjectSettings/ProjectSettings.asset degisti - commit etmeyi unutma.");
    }

    /// <summary>
    /// El takibini kapatir, yalnizca kontrolcu birakir.
    ///
    /// NEDEN KONTROLCU: sartnamedeki uc yoklama kanalindan biri HAPTIKTIR
    /// ("Agirlik: tutulurken her karede dusuk genlikli surekli haptik gonder").
    /// El takibinde haptik YOKTUR - agir esya sinyali hic verilemez. Yani bu oyun
    /// kontrolcu ile oynanmak zorundadir; el takibi acik kalirsa oyuncu yanlislikla
    /// el moduna gecip oyunun ucte birini kaybeder.
    ///
    /// Kontrolcu profilini (OculusTouchControllerProfile) KAPATMIYORUZ - kapaliysa
    /// OpenXR kontrolcu girdilerini hicbir action'a baglamaz ve gozlukte
    /// hicbir sey algilanmaz.
    /// </summary>
    [MenuItem("Tools/Gece Vardiyasi/El Takibini Kapat (yalnizca kontrolcu)", false, 51)]
    static void DisableHandTracking()
    {
        var targets = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android };
        var log = new StringBuilder();

        foreach (BuildTargetGroup target in targets)
        {
            UnityEngine.XR.OpenXR.OpenXRSettings settings =
                UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(target);

            if (settings == null)
            {
                log.AppendLine($"  {target}: OpenXR ayari yok, atlandi.");
                continue;
            }

            foreach (UnityEngine.XR.OpenXR.Features.OpenXRFeature feature in settings.GetFeatures())
            {
                if (feature == null)
                    continue;

                // TAM ad esleseni kapat. "HandTracking" alt dizesiyle eslesirsek
                // MetaHandTrackingAim gibi etkilesim profillerini de kapatiriz.
                if (feature.GetType().Name == "HandTracking" && feature.enabled)
                {
                    feature.enabled = false;
                    EditorUtility.SetDirty(feature);
                    log.AppendLine($"  {target}: HandTracking KAPATILDI.");
                }

                // Kontrolcu profili kapaliysa gozlukte hicbir girdi calismaz.
                if (feature.GetType().Name == "OculusTouchControllerProfile" && !feature.enabled)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                    log.AppendLine($"  {target}: OculusTouchControllerProfile ACILDI (kontrolcu icin sart).");
                }
            }

            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(target);
        }

        AssetDatabase.SaveAssets();

        if (log.Length == 0)
            log.AppendLine("  Degisiklik gerekmedi; el takibi zaten kapali, kontrolcu profili acik.");

        Debug.Log("[Faz 2] El takibi ayarlari:\n" + log +
                  "\nArtik 'Hand Tracking Subsystem not found' uyarisi cikmayacak.\n" +
                  "Assets/XR/Settings/OpenXRPackageSettings.asset degisti - commit etmeyi unutma.");
    }

    [MenuItem("Tools/Gece Vardiyasi/Editorde XR Baslatmayi Kapat (Standalone)", false, 60)]
    static void DisableEditorXr() => SetStandaloneXrOnStartup(false);

    [MenuItem("Tools/Gece Vardiyasi/Editorde XR Baslatmayi Ac (Standalone)", false, 61)]
    static void EnableEditorXr() => SetStandaloneXrOnStartup(true);

    // ------------------------------------------------------------- kurulum

    static MultiplayerManager EnsureMultiplayerManager(StringBuilder log)
    {
        // Include: onceki kurulumda bilesen kapatilmis olabilir.
        var manager = Object.FindFirstObjectByType<MultiplayerManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            var go = new GameObject("Multiplayer");
            manager = go.AddComponent<MultiplayerManager>();
            Undo.RegisterCreatedObjectUndo(go, "Faz 2: Multiplayer");
            log.AppendLine("+ MultiplayerManager olusturuldu.");
        }
        else
        {
            log.AppendLine("= MultiplayerManager zaten var.");
        }

        return manager;
    }

    static Spawner EnsureSpawner(MultiplayerManager manager, StringBuilder log)
    {
        var spawner = Object.FindFirstObjectByType<Spawner>();
        if (spawner == null)
        {
            spawner = manager.gameObject.AddComponent<Spawner>();
            log.AppendLine("+ Alteruna Spawner eklendi.");
        }
        else
        {
            log.AppendLine("= Alteruna Spawner zaten var.");
        }

        // Sonradan katilan oyuncu, daha once uretilmis esyalari da gorsun.
        spawner.ForceSync = true;
        EditorUtility.SetDirty(spawner);

        return spawner;
    }

    /// <summary>
    /// Play'e basildiginda AG HIC AYAGA KALKMASIN.
    ///
    /// ONEMLI - ConnectOnStart ISE YARAMIYOR: bu SDK surumunde `ConnectOnStart`
    /// yalnizca yapicida ve GetDebuggingInfo metninde geciyor, hicbir davranisi
    /// KOSULLANDIRMIYOR (IL ile dogrulandi). Unity'nin otomatik cagirdigi
    /// `MultiplayerManager.Start()` kosulsuz olarak `Service.Start()` +
    /// `OpenPort()` calistiriyor: Play'e basar basmaz soketler aciliyor,
    /// LAN kesfi basliyor ve editor kilitlenebiliyor.
    ///
    /// GERCEK COZUM: bileseni KAPALI birak. Unity devre disi bilesende `Start()`
    /// cagirmaz - hicbir soket acilmaz. `Awake` yine calisir (zararsiz) ve
    /// `FindObjectsOfType` devre disi bileseni bulmaya devam ettigi icin
    /// koprumuz `Multiplayer` referansini kaybetmez.
    ///
    /// Butona basildiginda SDK kendini ayaga kaldirir: hem `Host()` hem
    /// `Connect()` icinde `enabled = true` + `Start()` cagrisi var.
    /// </summary>
    static void DisableNetworkOnPlay(MultiplayerManager manager, StringBuilder log)
    {
        var serialized = new SerializedObject(manager);

        // Davranisa etkisi yok ama Inspector'da niyet okunur dursun.
        SerializedProperty connectOnStart = serialized.FindProperty("ConnectOnStart");
        if (connectOnStart != null)
            connectOnStart.boolValue = false;

        // Ucretsiz katman zaten 2 oyuncu. Siniri BILEREK koy ki surprizle karsilasma.
        SerializedProperty maxPlayers = serialized.FindProperty("_maxPlayers");
        if (maxPlayers != null)
            maxPlayers.intValue = 2;

        // ASIL DUZELTME: bileseni kapat.
        SerializedProperty enabled = serialized.FindProperty("m_Enabled");
        if (enabled != null)
            enabled.boolValue = false;

        serialized.ApplyModifiedProperties();

        log.AppendLine("= MultiplayerManager bileseni KAPATILDI: Play'e basmak artik hicbir soket acmiyor.");
        log.AppendLine("  (Host / JoinLan butonuna basilinca SDK kendini ayaga kaldirir.)");
        log.AppendLine("= MaxPlayers = 2.");
    }

    /// <summary>
    /// Kurulumun tamamini tekrar calistirmadan yalnizca "Play'de ag kapali" ayarini uygular.
    /// Prefablari ve sahne hiyerarsisini ELLEMEZ.
    /// </summary>
    [MenuItem("Tools/Gece Vardiyasi/Agi Play'de Kapali Baslat", false, 42)]
    static void DisableNetworkOnPlayOnly()
    {
        var manager = Object.FindFirstObjectByType<MultiplayerManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[Faz 2] Sahnede MultiplayerManager yok.");
            return;
        }

        var log = new StringBuilder();
        DisableNetworkOnPlay(manager, log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Faz 2]\n" + log + "\nSahne KAYDEDILMEDI - Ctrl+S ile kendin kaydet.");
    }

    static void EnsureCoordinator(StringBuilder log)
    {
        if (Object.FindFirstObjectByType<NetworkShiftCoordinator>() != null)
        {
            log.AppendLine("= NetworkShiftCoordinator zaten var.");
            return;
        }

        // Systems altinda dursun - XR rig'in ALTINA konmaz (Alteruna avatar
        // temizligi global namespace'li scriptlerde NullReferenceException verir).
        var host = GameObject.Find("Systems");
        if (host == null)
        {
            var shiftManager = Object.FindFirstObjectByType<ShiftManager>();
            host = shiftManager != null ? shiftManager.gameObject : new GameObject("Systems");
        }

        host.AddComponent<NetworkShiftCoordinator>();
        log.AppendLine($"+ NetworkShiftCoordinator '{host.name}' altina eklendi.");
    }

    static void EnsurePlayerRefs(StringBuilder log)
    {
        var refs = Object.FindFirstObjectByType<PlayerRefs>();
        if (refs == null)
        {
            var host = GameObject.Find("Systems") ?? new GameObject("Systems");
            refs = host.AddComponent<PlayerRefs>();
            log.AppendLine($"+ PlayerRefs '{host.name}' altina eklendi.");
        }

        // Serialized alanlari editorde doldur; runtime cozumu yalnizca emniyet agi.
        var serialized = new SerializedObject(refs);
        Camera camera = Camera.main;
        if (camera == null)
        {
            foreach (var candidate in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                camera = candidate;
                break;
            }
        }

        if (camera != null)
            serialized.FindProperty("mainCamera").objectReferenceValue = camera;

        // Tek dogru uygulama PlayerRefs'te; burada kopyasini tutmuyoruz.
        PlayerRefs.FindHandTransforms(out Transform left, out Transform right);
        if (left != null)
            serialized.FindProperty("leftHand").objectReferenceValue = left;
        if (right != null)
            serialized.FindProperty("rightHand").objectReferenceValue = right;

        serialized.ApplyModifiedProperties();

        log.AppendLine($"= PlayerRefs: kamera={(camera != null ? camera.name : "YOK")}, " +
                       $"sol={(left != null ? left.name : "YOK")}, sag={(right != null ? right.name : "YOK")}");
    }

    static void WireItemSpawner(Spawner spawner, StringBuilder log)
    {
        var itemSpawner = Object.FindFirstObjectByType<ItemSpawner>();
        if (itemSpawner == null)
        {
            log.AppendLine("! ItemSpawner sahnede yok - once 'Main Sahnesini Kur' calistirilmali.");
            return;
        }

        var serialized = new SerializedObject(itemSpawner);
        serialized.FindProperty("networkSpawner").objectReferenceValue = spawner;

        // Alteruna indeksi KENDI listesine gore cozer. Iki liste ayni sirada
        // olmazsa host bir esyayi, istemci bambaska bir esyayi gorur.
        SerializedProperty prefabs = serialized.FindProperty("itemPrefabs");
        spawner.SpawnableObjects.Clear();

        for (int i = 0; i < prefabs.arraySize; i++)
        {
            var prefab = prefabs.GetArrayElementAtIndex(i)
                .FindPropertyRelative("prefab").objectReferenceValue as GameObject;

            if (prefab != null && !spawner.SpawnableObjects.Contains(prefab))
                spawner.SpawnableObjects.Add(prefab);
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(spawner);

        log.AppendLine($"= ItemSpawner -> Spawner baglandi, {spawner.SpawnableObjects.Count} esya listelendi.");
    }

    static void PrepareItemPrefabs(StringBuilder log)
    {
        int touched = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { k_ItemPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            try
            {
                if (root.GetComponent<XRGrabInteractable>() == null)
                    continue;

                if (root.GetComponent<ItemOwnership>() == null)
                {
                    root.AddComponent<ItemOwnership>();
                    changed = true;
                }

                // Rigidbody varsa fizigi, yoksa yalnizca transformu senkronla.
                // AYNI objede ikisi birden OLMAZ - Alteruna bunu hata olarak reddeder.
                if (root.GetComponent<RigidbodySynchronizable>() == null &&
                    root.GetComponent<TransformSynchronizable>() == null)
                {
                    if (root.GetComponent<Rigidbody>() != null)
                        root.AddComponent<RigidbodySynchronizable>();
                    else
                        root.AddComponent<TransformSynchronizable>();

                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    touched++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        log.AppendLine($"= Esya prefablari hazirlandi ({touched} tanesi guncellendi).");
    }

    static void EnsureAvatarPrefab(MultiplayerManager manager, StringBuilder log)
    {
        // TAM NITELIKLI: 'Avatar' adi UnityEngine.Avatar (animasyon) ile cakisir.
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(k_AvatarPrefabPath);
        AlterunaAvatar existing = prefabAsset != null ? prefabAsset.GetComponent<AlterunaAvatar>() : null;

        if (existing == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(k_AvatarPrefabPath));

            var root = new GameObject("NetworkPlayerAvatar");
            root.AddComponent<AlterunaAvatar>();
            var rig = root.AddComponent<NetworkAvatarRig>();

            // Eller kafadan daha hizli hareket eder; onlari daha sik yayinliyoruz.
            Transform head = CreateAvatarPart(root.transform, "Head", PrimitiveType.Cube, 0.20f, 20f);
            Transform left = CreateAvatarPart(root.transform, "HandLeft", PrimitiveType.Sphere, 0.09f, 40f);
            Transform right = CreateAvatarPart(root.transform, "HandRight", PrimitiveType.Sphere, 0.09f, 40f);

            var serialized = new SerializedObject(rig);
            serialized.FindProperty("m_Head").objectReferenceValue = head;
            serialized.FindProperty("m_LeftHand").objectReferenceValue = left;
            serialized.FindProperty("m_RightHand").objectReferenceValue = right;
            serialized.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, k_AvatarPrefabPath);
            Object.DestroyImmediate(root);

            existing = AssetDatabase.LoadAssetAtPath<GameObject>(k_AvatarPrefabPath).GetComponent<AlterunaAvatar>();
            log.AppendLine("+ Ag avatari prefabi olusturuldu: " + k_AvatarPrefabPath);
        }
        else
        {
            log.AppendLine("= Ag avatari prefabi zaten var.");
        }

        manager.AvatarPrefab = existing;
        manager.AvatarSpawning = AvatarBehavior.SpawnOnJoin;
        manager.SpawnAvatarPerIndex = true;
        manager.AvatarSpawnLocations = BuildSpawnLocations();
        EditorUtility.SetDirty(manager);

        log.AppendLine("= MultiplayerManager avatar ayarlari yazildi (SpawnOnJoin, 2 baslangic noktasi).");
    }

    static List<Transform> BuildSpawnLocations()
    {
        var parent = GameObject.Find("PlayerSpawnPoints");
        if (parent == null)
            parent = new GameObject("PlayerSpawnPoints");

        var result = new List<Transform>();
        for (int i = 0; i < 2; i++)
        {
            string name = i == 0 ? "Spawn_Host" : "Spawn_Istemci";
            Transform point = parent.transform.Find(name);

            if (point == null)
                point = new GameObject(name).transform;

            point.SetParent(parent.transform, false);

            // Ikisi de tezgaha bakar; aralarinda 1,5 m var.
            float x = i == 0 ? -k_PlayerLateralOffset : k_PlayerLateralOffset;
            point.localPosition = new Vector3(x, 0f, 0f);
            point.localRotation = Quaternion.identity;

            result.Add(point);
        }

        return result;
    }

    static Transform CreateAvatarPart(Transform parent, string name, PrimitiveType shape,
        float size, float refreshRate)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * size;

        // Avatar bir GOSTERGE, fizik nesnesi degil. Collider kalirsa oyuncunun kendi
        // eli avatarina carpar ve esyalar avatarla itisir.
        Object.DestroyImmediate(go.GetComponent<Collider>());

        // Uzak avatarin pozu agdan gelir.
        var sync = go.AddComponent<TransformSynchronizable>();

        // NetworkAvatarRig bu parcalara DUNYA pozunu yazar. Yerel konum senkronu,
        // iki cihazda avatar kokleri birebir ayni yerde degilse kayar.
        sync.UseGlobalPosition = true;
        sync.RefreshRate = refreshRate;

        return go.transform;
    }

    // -------------------------------------------------------------- kontrol

    /// <summary>
    /// Project Settings > Alteruna Multiplayer ekraninin diskteki karsiligini denetler.
    ///
    /// TRANSPORT NOTU: Bu SDK'da UDP diye bir secenek YOKTUR. TransportType enum'u
    /// yalnizca NaN / Default / TCP / TCPS / WebSocket icerir; dogru deger TCP'dir
    /// (WebSocket sadece WebGL icin). LAN kesfi ayri bir ayardir ve UDP yayinini
    /// kendi icinde kullanir - "LAN Discovery" acik olmasi yeterlidir.
    ///
    /// Dosyayi tipe bagli okumuyoruz: SerializedObject ile alan adlarindan okuyoruz,
    /// boylece SDK ic tipleri degisse de bu kontrol derlenmeye devam eder.
    /// </summary>
    static int CheckAlterunaConfig(StringBuilder log)
    {
        const string path = "Assets/Resources/AlterunaConfig.asset";

        var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (asset == null)
        {
            log.AppendLine("  [EKSIK] " + path + " yok. Project Settings > Alteruna Multiplayer " +
                           "ekranini bir kez ac; dosya orada olusur.");
            return 1;
        }

        var serialized = new SerializedObject(asset);
        int problems = 0;

        // LAN kesfi kapaliysa istemci host'u hic bulamaz - JoinLan() sessizce bos doner.
        SerializedProperty lan = serialized.FindProperty("_enableLanDiscovery");
        problems += Report(log, lan != null && lan.boolValue, "LAN Discovery ACIK");

        // 2 = TCP. Default (1) de kabul; WebSocket (4) yalnizca WebGL icindir.
        SerializedProperty transport = serialized.FindProperty("_transportType");
        int transportValue = transport != null ? transport.intValue : -1;
        problems += Report(log, transportValue == 1 || transportValue == 2,
            $"Transport = {TransportName(transportValue)} (bu SDK'da UDP secenegi YOK)");

        // 90 saniyelik bir oyunda ag LOD'una ihtiyac yok; kapali tutmak degisken sayisini azaltir.
        SerializedProperty lod = serialized.FindProperty("_enableLOD");
        if (lod != null && lod.boolValue)
            log.AppendLine("  [ONERI] EnableLOD acik. 90 saniyelik turda gerekmez, kapatmak " +
                           "ag degisken sayisini azaltir. (Hata degil.)");

        // Sahibi olmayan avatarlardaki kamera/AudioListener otomatik kapansin.
        SerializedProperty cams = serialized.FindProperty("_disableCamerasOnNonOwnedAvatars");
        problems += Report(log, cams != null && cams.boolValue,
            "Disable Cameras On Non-Owned Avatars ACIK");

        SerializedProperty defaultPort = serialized.FindProperty("_defaultPort");
        SerializedProperty discoveryPort = serialized.FindProperty("_discoveryPort");
        log.AppendLine($"  [BILGI] Portlar: veri={PortText(defaultPort)}, kesif={PortText(discoveryPort)}. " +
                       "Baska bir uygulama ayni portu tutuyorsa kesif SESSIZCE basarisiz olur.");

        // Bu dosya repoya girmezse takimin geri kalaninda LAN kesfi kapali gelir.
        log.AppendLine("  [BILGI] " + path + " commit edilmeli - yoksa ekipteki digerlerinde " +
                       "bu ayarlarin hicbiri olmaz.");

        return problems;
    }

    /// <summary>
    /// EDITORDE PLAY'E BASINCA UNITY KAPANIYORSA ILK BAKILACAK YER BURASI.
    ///
    /// Standalone hedefinde "Initialize XR on Startup" acikken Play'e basmak,
    /// editorun ana dongusunu gozlugun kare temposuna baglar. Gozluk takili/uyanik
    /// degilse (veya Link kopuksa) editor donar ya da sessizce kapanir: log
    /// "Shut down." ile biter, crash dump YOKTUR, tek satir istisna olusmaz.
    /// Bu yuzden saatlerce oyun kodunda hata aranir - orada hata yoktur.
    /// </summary>
    static int CheckEditorXrStartup(StringBuilder log)
    {
        if (!EditorBuildSettings.TryGetConfigObject(
                UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,
                out UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget perTarget) ||
            perTarget == null)
        {
            log.AppendLine("  [ATLA] Aktif XR yapilandirmasi okunamadi.");
            return 0;
        }

        UnityEngine.XR.Management.XRGeneralSettings standalone =
            perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);

        if (standalone == null)
            return 0;

        int problems = Report(log, !standalone.InitManagerOnStart,
            "Standalone 'Initialize XR on Startup' KAPALI (Play'de editor olmuyor)");

        if (standalone.InitManagerOnStart)
            log.AppendLine("            -> Tools > Gece Vardiyasi > Editorde XR Baslatmayi Kapat (Standalone)\n" +
                           "               ANDROID HEDEFI DEGISMEZ; APK'da VR aynen calisir.");

        return problems;
    }

    /// <summary>
    /// Bu oyun KONTROLCU ile oynanir (haptik kanali el takibinde yoktur).
    /// Kontrolcu profili kapaliysa OpenXR hicbir girdiyi baglamaz; el takibi acik
    /// kalirsa oyuncu yanlislikla el moduna gecebilir.
    /// </summary>
    static int CheckXrInput(StringBuilder log)
    {
        int problems = 0;

        foreach (BuildTargetGroup target in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            UnityEngine.XR.OpenXR.OpenXRSettings settings =
                UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(target);

            if (settings == null)
                continue;

            bool handTrackingOn = false;
            bool controllerProfileOn = false;

            foreach (UnityEngine.XR.OpenXR.Features.OpenXRFeature feature in settings.GetFeatures())
            {
                if (feature == null)
                    continue;

                string typeName = feature.GetType().Name;
                if (typeName == "HandTracking")
                    handTrackingOn = feature.enabled;
                else if (typeName == "OculusTouchControllerProfile")
                    controllerProfileOn = feature.enabled;
            }

            problems += Report(log, controllerProfileOn, $"{target}: Oculus Touch Controller Profile ACIK");
            problems += Report(log, !handTrackingOn, $"{target}: Hand Tracking KAPALI (oyun kontrolcu ile oynanir)");
        }

        if (problems > 0)
            log.AppendLine("            -> Tools > Gece Vardiyasi > El Takibini Kapat (yalnizca kontrolcu)");

        return problems;
    }

    static string TransportName(int value)
    {
        switch (value)
        {
            case 0: return "NaN";
            case 1: return "Default";
            case 2: return "TCP";
            case 3: return "TCPS";
            case 4: return "WebSocket (YANLIS - sadece WebGL)";
            default: return "okunamadi";
        }
    }

    static string PortText(SerializedProperty port)
    {
        return port != null ? port.intValue.ToString() : "?";
    }

    static int Report(StringBuilder log, bool ok, string label)
    {
        log.AppendLine((ok ? "  [OK]   " : "  [EKSIK] ") + label);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Alteruna'nin uzak avatar temizligi her alt Behaviour icin type.Namespace.Length
    /// okur. Bu projedeki scriptler global namespace'te oldugu icin Namespace NULL
    /// doner, temizlik NullReferenceException ile yarida kalir ve ikinci oyuncunun
    /// avatari bozuk kalir. Bu yuzden rig'in altina KENDI scriptlerimizi koymuyoruz.
    /// </summary>
    static int CheckNoOwnScriptsUnderRig(StringBuilder log)
    {
        var origin = GameObject.Find("XR Origin (XR Rig)") ?? GameObject.Find("XR Origin");
        if (origin == null)
        {
            log.AppendLine("  [ATLA] XR Origin bulunamadi, rig kontrolu yapilmadi.");
            return 0;
        }

        var offenders = new List<string>();
        foreach (var behaviour in origin.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            if (string.IsNullOrEmpty(behaviour.GetType().Namespace))
                offenders.Add($"{behaviour.GetType().Name} ({behaviour.name})");
        }

        if (offenders.Count == 0)
            return Report(log, true, "XR rig altinda namespace'siz script yok");

        log.AppendLine("  [EKSIK] XR rig altinda namespace'siz script(ler) var - " +
                       "Alteruna avatar temizligi bunlarda patlar:");
        foreach (string offender in offenders)
            log.AppendLine("            - " + offender);

        return 1;
    }

    // ------------------------------------------------------------------- XR

    /// <summary>
    /// Editorde Play'e basildiginda XR'in ayaga kaldirilmasini acar/kapatir.
    ///
    /// NEDEN: gozluk Link ile bagliyken Unity'nin ana dongusu gozlugun kare
    /// temposuna kilitlenir; gozluk takili/uyanik degilse editor DONMUS gorunur
    /// veya Play'e basar basmaz kapanir. Log'da hicbir istisna olmaz.
    ///
    /// YALNIZCA STANDALONE hedefini degistirir - ANDROID AYARI DEGISMEZ,
    /// yani APK'da VR aynen calisir.
    /// </summary>
    static void SetStandaloneXrOnStartup(bool enabled)
    {
        // Aktif XR yapilandirmasini EditorBuildSettings'ten al. Diskteki
        // Assets/XR/ klasorune bakmak yaniltir: projede birden fazla paralel
        // XR ayar agaci olabilir ve yanlisini duzenlemek saatler kaybettirir.
        // DIKKAT: XRGeneralSettings -> UnityEngine.XR.Management,
        //         XRGeneralSettingsPerBuildTarget -> UnityEditor.XR.Management (farkli namespace).
        if (!EditorBuildSettings.TryGetConfigObject(
                UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,
                out UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget perTarget) ||
            perTarget == null)
        {
            Debug.LogError("[XR] Aktif XR yapilandirmasi bulunamadi. " +
                           "Project Settings > XR Plug-in Management'i bir kez ac ve tekrar dene.");
            return;
        }

        UnityEngine.XR.Management.XRGeneralSettings settings =
            perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);

        if (settings == null)
        {
            Debug.LogError("[XR] Standalone icin XR ayari yok; degisiklik yapilmadi.");
            return;
        }

        settings.InitManagerOnStart = enabled;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[XR] Standalone 'Initialize XR on Startup' = {enabled}. " +
                  "Android hedefi DEGISMEDI; APK'da VR aynen calisir." +
                  (enabled ? "" : "\nArtik Play'e basinca editor gozlugu ayaga kaldirmaya calismaz."));
    }
}
