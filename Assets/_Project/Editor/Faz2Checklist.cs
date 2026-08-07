#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Alteruna.Multiplayer.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Sartnamedeki "Faz 2 kontrol listesi" ve "Asagidakilerin hepsi saglanmadan gun
/// bitmis sayilmaz" maddelerini tek komutta dogrular.
///
/// Neden var: Faz 2'de bozulan seylerin cogu derleme hatasi degil, SAHNE BAGLANTISI
/// hatasiydi (spawner listesi bos, coordinator bagli degil, rig'de avatar yok).
/// Bunlar Play'e basmadan gorunmuyordu ve gozlukte "hicbir sey olmuyor" seklinde
/// ortaya cikiyordu. Bu kontrol listesi ayni hatalari saniyeler icinde yakalar.
/// </summary>
public static class Faz2Checklist
{
    const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
    const string BaseItemPrefabPath = "Assets/_Project/Items/Prefabs/item.prefab";
    const string RigName = "XR Origin Hands (XR Rig)";

    static readonly List<string> s_Lines = new List<string>();
    static int s_Failures;
    static int s_Warnings;

    [MenuItem("Tools/Gece Vardiyası/Faz 2 Kontrol Listesini Doğrula", false, 60)]
    public static void Validate()
    {
        s_Lines.Clear();
        s_Failures = 0;
        s_Warnings = 0;

        if (SceneManager.GetActiveScene().path != MainScenePath)
        {
            EditorUtility.DisplayDialog("Faz 2 Kontrol",
                "Önce Assets/_Project/Scenes/Main.unity sahnesini aç.", "Tamam");
            return;
        }

        CheckPackages();
        CheckMultiplayerManager();
        CheckRig();
        CheckSystems();
        CheckItemPrefab();
        CheckCabinets();
        CheckHuds();
        CheckBuildSettings();

        StringBuilder report = new StringBuilder();
        report.AppendLine("=== FAZ 2 KONTROL LISTESI ===");
        foreach (string line in s_Lines)
            report.AppendLine(line);
        report.AppendLine();
        report.AppendLine($"Sonuc: {s_Failures} hata, {s_Warnings} uyari.");

        if (s_Failures == 0)
            Debug.Log(report.ToString());
        else
            Debug.LogError(report.ToString());

        EditorUtility.DisplayDialog("Faz 2 Kontrol",
            s_Failures == 0
                ? $"Tum zorunlu maddeler tamam ({s_Warnings} uyari).\nAyrinti Console'da."
                : $"{s_Failures} madde EKSIK.\nAyrinti Console'da.",
            "Tamam");
    }

    /// <summary>
    /// Build Settings'e zamanla ornek sahneler sizabiliyor (orn. XRI Starter Assets
    /// DemoScene). Fazladan sahne APK'yi sisirir ve yanlis sahnenin acilma riskini
    /// tasir. Bu komut listeyi yalnizca Main.unity'ye indirger.
    /// </summary>
    [MenuItem("Tools/Gece Vardiyası/Build Settings'i Onar (yalnızca Main)", false, 61)]
    public static void RepairBuildSettings()
    {
        string[] removed = EditorBuildSettings.scenes
            .Select(scene => scene.path)
            .Where(path => path != MainScenePath)
            .ToArray();

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MainScenePath, true),
        };

        string message = removed.Length == 0
            ? "Build Settings zaten yalnizca Main.unity iceriyordu."
            : "Build Settings artik yalnizca Main.unity iceriyor.\nCikarilanlar:\n- " +
              string.Join("\n- ", removed);

        Debug.Log("[Faz2] " + message);
        EditorUtility.DisplayDialog("Build Settings", message, "Tamam");
    }

    // ---------------------------------------------------------------- yardimci

    static void Pass(string message) => s_Lines.Add("  [OK]   " + message);

    static void Fail(string message)
    {
        s_Lines.Add("  [HATA] " + message);
        s_Failures++;
    }

    static void Warn(string message)
    {
        s_Lines.Add("  [UYARI] " + message);
        s_Warnings++;
    }

    static void Require(bool condition, string okMessage, string failMessage)
    {
        if (condition)
            Pass(okMessage);
        else
            Fail(failMessage);
    }

    // ----------------------------------------------------------------- paketler

    static void CheckPackages()
    {
        s_Lines.Add("1) Paketler");

        bool sdk = System.IO.Directory.Exists("Library/PackageCache") &&
                   System.IO.Directory.GetDirectories("Library/PackageCache")
                       .Any(path => path.Contains("com.alteruna.multiplayer"));
        Require(sdk, "Alteruna Multiplayer SDK kurulu.",
            "Alteruna Multiplayer SDK bulunamadi (Packages/manifest.json).");

        bool template = AssetDatabase.IsValidFolder("Assets/Multiplayer XR Template");
        Require(template, "Multiplayer XR Template kurulu.",
            "Multiplayer XR Template klasoru yok - avatar/rig referansi eksik.");
    }

    // ------------------------------------------------------------ oda / avatar

    static void CheckMultiplayerManager()
    {
        s_Lines.Add("2) Oda ve avatar (kontrol listesi md. 1-2)");

        AlterunaComponents.MultiplayerManager manager =
            Object.FindFirstObjectByType<AlterunaComponents.MultiplayerManager>();
        if (manager == null)
        {
            Fail("Sahnede MultiplayerManager yok. Multiplayer Kurulumunu Uygula komutunu calistir.");
            return;
        }

        Pass("MultiplayerManager sahnede.");

        SerializedObject so = new SerializedObject(manager);
        int maxPlayers = GetInt(so, "_maxPlayers", -1);
        if (maxPlayers == 2)
            Pass("Oda kapasitesi 2 (ucretsiz katman siniri).");
        else
            Warn($"Oda kapasitesi {maxPlayers}. Ucretsiz katman 2 oyuncu ile sinirli.");

        Require(GetBool(so, "ConnectOnStart", false),
            "ConnectOnStart acik.", "ConnectOnStart kapali - istemci hicbir zaman baglanmaz.");

        SerializedProperty avatarPrefab = so.FindProperty("AvatarPrefab");
        Require(avatarPrefab != null && avatarPrefab.objectReferenceValue != null,
            "AvatarPrefab atanmis (kafa + 2 el rig'i).",
            "AvatarPrefab bos - ikinci oyuncu hicbir yerde gorunmez.");

        SerializedProperty spawnLocations = so.FindProperty("AvatarSpawnLocations");
        bool twoSpawns = spawnLocations != null && spawnLocations.arraySize >= 2 &&
                         spawnLocations.GetArrayElementAtIndex(0).objectReferenceValue != null &&
                         spawnLocations.GetArrayElementAtIndex(1).objectReferenceValue != null;
        Require(twoSpawns, "Iki oyuncu icin ayri baslangic noktasi var.",
            "AvatarSpawnLocations eksik - iki oyuncu ust uste doguyor.");

        bool autoJoin = Object.FindFirstObjectByType<Alteruna.AutoJoin>() != null ||
                        GetBool(so, "AutoJoinFirstRoom", false) ||
                        GetBool(so, "AutoJoinOwnRoom", false);
        Require(autoJoin, "Odaya otomatik katilim acik (AutoJoin).",
            "Odaya katilim yok: ne AutoJoin bileseni ne de AutoJoinFirstRoom acik. " +
            "Iki gozluk de cevrimdisi oynar.");
    }

    // ------------------------------------------------------------------ XR rig

    static void CheckRig()
    {
        s_Lines.Add("3) XR rig ve el/kafa senkronizasyonu");

        GameObject rig = GameObject.Find(RigName);
        if (rig == null)
        {
            Fail($"'{RigName}' sahnede yok - gozlukte hicbir sey gorunmez.");
            return;
        }

        Pass("XR rig sahnede.");
        Require(rig.GetComponent<AlterunaComponents.Avatar>() != null,
            "Rig'de Alteruna Avatar var.", "Rig'de Alteruna Avatar YOK.");
        Require(rig.GetComponent<Alteruna.XRIAvatar>() != null,
            "Rig'de XRIAvatar var (uzak avatari temizler).", "Rig'de XRIAvatar YOK.");

        int synced = rig.GetComponentsInChildren<TransformSynchronizable>(true).Length;
        if (synced >= 4)
            Pass($"Rig altinda {synced} TransformSynchronizable (govde + kafa + 2 el).");
        else
            Fail($"Rig altinda yalnizca {synced} TransformSynchronizable var. " +
                 "Kafa ve iki el ayri ayri senkronize edilmeli.");

        // XRIAvatar.RemoveComponents her alt Behaviour icin type.Namespace.Length okur.
        // Bu projedeki scriptler namespace'siz oldugu icin Namespace null doner ve
        // uzak avatar kurulurken NullReferenceException atilir; temizlik yarida kalir.
        List<string> risky = rig.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(behaviour => behaviour != null && behaviour.GetType().Namespace == null)
            .Select(behaviour => behaviour.GetType().Name)
            .Distinct()
            .ToList();

        if (risky.Count == 0)
            Pass("Rig altinda namespace'siz script yok (XRIAvatar temizligi guvenli).");
        else
            Fail("Rig altinda namespace'siz script(ler) var: " + string.Join(", ", risky) +
                 ". XRIAvatar uzak avatari temizlerken NullReferenceException atar. " +
                 "Bu scriptleri rig'in disina tasi (orn. Systems).");

        List<string> locomotion = rig.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(behaviour => behaviour != null && behaviour.enabled)
            .Select(behaviour => behaviour.GetType().Name)
            .Where(typeName => typeName.Contains("MoveProvider") ||
                               typeName.Contains("TeleportationProvider") ||
                               typeName.Contains("ClimbProvider"))
            .Distinct()
            .ToList();

        if (locomotion.Count == 0)
            Pass("Yurume/isinlanma kapali (konfor kurali).");
        else
            Warn("Acik locomotion bileseni: " + string.Join(", ", locomotion));
    }

    // ---------------------------------------------------------------- sistemler

    static void CheckSystems()
    {
        s_Lines.Add("4) ShiftManager / ItemSpawner / ag koprusu (kontrol listesi md. 4)");

        ShiftManager shiftManager = Object.FindFirstObjectByType<ShiftManager>();
        ItemSpawner itemSpawner = Object.FindFirstObjectByType<ItemSpawner>();
        NetworkShiftCoordinator coordinator = Object.FindFirstObjectByType<NetworkShiftCoordinator>();
        Spawner networkSpawner = Object.FindFirstObjectByType<Spawner>();
        TelemetryLogger telemetry = Object.FindFirstObjectByType<TelemetryLogger>();

        Require(shiftManager != null, "ShiftManager sahnede.", "ShiftManager YOK.");
        Require(itemSpawner != null, "ItemSpawner sahnede.", "ItemSpawner YOK.");
        Require(telemetry != null, "TelemetryLogger sahnede (CSV).", "TelemetryLogger YOK - CSV uretilmez.");
        Require(coordinator != null,
            "NetworkShiftCoordinator sahnede (skoru host tutar).",
            "NetworkShiftCoordinator YOK - skor senkronize edilmez.");
        Require(networkSpawner != null,
            "Alteruna Spawner sahnede.", "Alteruna Spawner YOK - esya yalnizca host'ta doger.");

        if (shiftManager != null && coordinator != null)
        {
            SerializedObject shiftSo = new SerializedObject(shiftManager);
            SerializedProperty link = shiftSo.FindProperty("networkCoordinator");
            Require(link != null && link.objectReferenceValue != null,
                "ShiftManager -> NetworkShiftCoordinator bagli.",
                "ShiftManager.networkCoordinator bos.");
        }

        if (itemSpawner == null)
            return;

        SerializedObject spawnerSo = new SerializedObject(itemSpawner);
        SerializedProperty entries = spawnerSo.FindProperty("itemPrefabs");
        SerializedProperty networkSpawnerProperty = spawnerSo.FindProperty("networkSpawner");
        int seed = spawnerSo.FindProperty("seed").intValue;

        Require(networkSpawnerProperty != null && networkSpawnerProperty.objectReferenceValue != null,
            "ItemSpawner -> Alteruna Spawner bagli.",
            "ItemSpawner.networkSpawner bos - odada esya uretilmez.");

        int itemCount = entries != null ? entries.arraySize : 0;
        if (itemCount >= 9)
            Pass($"{itemCount} esya prefabi tanimli (sartname: 9).");
        else
            Fail($"Yalnizca {itemCount} esya prefabi tanimli, 9 olmali.");

        if (seed == 0)
            Pass("Seed 0: her vardiya farkli sira, host degeri istemciye gecirir.");
        else
            Warn($"Seed sabit ({seed}). Her vardiya AYNI sira gelir - hata ayiklama disinda 0 yap.");

        if (networkSpawner != null && entries != null)
            CompareSpawnLists(entries, networkSpawner);
    }

    /// <summary>
    /// Alteruna Spawner indeksi kendi SpawnableObjects listesine gore cozer.
    /// Iki liste ayni degilse host bir esyayi, istemci baskasini gorur.
    /// </summary>
    static void CompareSpawnLists(SerializedProperty entries, Spawner networkSpawner)
    {
        List<GameObject> expected = new List<GameObject>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty prefab = entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab");
            expected.Add(prefab != null ? prefab.objectReferenceValue as GameObject : null);
        }

        bool identical = expected.Count == networkSpawner.SpawnableObjects.Count;
        if (identical)
        {
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i] != networkSpawner.SpawnableObjects[i])
                {
                    identical = false;
                    break;
                }
            }
        }

        Require(identical,
            "Spawner.SpawnableObjects, ItemSpawner.itemPrefabs ile ayni sirada.",
            "Spawner.SpawnableObjects listesi ItemSpawner.itemPrefabs ile UYUSMUYOR. " +
            "Host ve istemci farkli esya gorur. Multiplayer Kurulumunu Uygula komutunu calistir.");
    }

    // ------------------------------------------------------------ esya prefabi

    static void CheckItemPrefab()
    {
        s_Lines.Add("5) Esya prefabi senkronizasyonu (kontrol listesi md. 3)");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseItemPrefabPath);
        if (prefab == null)
        {
            Fail($"{BaseItemPrefabPath} bulunamadi.");
            return;
        }

        Require(prefab.GetComponentInChildren<XRGrabInteractable>(true) != null,
            "XRGrabInteractable var.", "XRGrabInteractable YOK - esya tutulamaz.");
        Require(prefab.GetComponentInChildren<RigidbodySynchronizable>(true) != null,
            "RigidbodySynchronizable var (transform senkronizasyonu).",
            "RigidbodySynchronizable YOK - esya diger oyuncuda hareket etmez.");
        Require(prefab.GetComponentInChildren<Alteruna.XRGrabInteractableSync>(true) != null,
            "XRGrabInteractableSync var (kavramada sahiplik devri).",
            "XRGrabInteractableSync YOK - kavramada sahiplik devrolmaz.");
        Require(prefab.GetComponentInChildren<NetworkItemState>(true) != null,
            "NetworkItemState var (kategori replikasyonu).",
            "NetworkItemState YOK - istemci esyanin kategorisini bilmez.");
        Require(prefab.GetComponentInChildren<ItemIdentity>(true) != null,
            "ItemIdentity var (kalici int id - mimari kural 5).", "ItemIdentity YOK.");
        Require(prefab.GetComponentInChildren<ItemProbe>(true) != null,
            "ItemProbe var (uc yoklama kanali).", "ItemProbe YOK.");

        string[] dataGuids = AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/_Project/Items" });
        if (dataGuids.Length >= 9)
            Pass($"{dataGuids.Length} ItemData varligi (sartname: 9).");
        else
            Fail($"Yalnizca {dataGuids.Length} ItemData varligi var, 9 olmali.");
    }

    // ---------------------------------------------------------------- dolaplar

    static void CheckCabinets()
    {
        s_Lines.Add("6) Dolaplar");

        CategorySocket[] sockets = Object.FindObjectsByType<CategorySocket>(FindObjectsSortMode.None);
        if (sockets.Length == 0)
        {
            Fail("Sahnede hic CategorySocket yok.");
            return;
        }

        HashSet<ItemCategory> categories = new HashSet<ItemCategory>(
            sockets.Select(socket => socket.acceptedCategory));

        Require(categories.Count == 3,
            "Uc kategori dolabi da sahnede (Sesli / Parlak / Agir).",
            "Eksik kategori dolabi. Bulunan: " + string.Join(", ", categories));

        if (sockets.Length > 3)
            Warn($"{sockets.Length} soket var; sartname 3 dolap istiyor.");
    }

    // -------------------------------------------------------------------- HUD

    static void CheckHuds()
    {
        s_Lines.Add("7) Oyuncu panelleri");

        LocalPlayerHud[] huds = Object.FindObjectsByType<LocalPlayerHud>(FindObjectsSortMode.None);
        Require(huds.Length == 2, "Iki oyuncu paneli var.",
            $"{huds.Length} oyuncu paneli var, 2 olmali (HUD_Player1 / HUD_Player2).");

        if (huds.Length == 2)
        {
            bool slots = huds.Any(hud => hud.PlayerSlot == 0) && huds.Any(hud => hud.PlayerSlot == 1);
            Require(slots, "Panel slotlari 0 ve 1.",
                "Iki panel de ayni slotta - bir oyuncu hicbir sey gormez.");
        }

        Require(Object.FindFirstObjectByType<NetworkDiagnosticsHud>() != null,
            "Ag teshis satiri panelde (gozlukte sorun aramak icin).",
            "NetworkDiagnosticsHud yok - cihazda oda durumu gorunmez.");
    }

    // --------------------------------------------------------------- build

    static void CheckBuildSettings()
    {
        s_Lines.Add("8) Build");

        // Yalnizca ETKIN sahneler build'e girer; devre disi birakilmis ornek sahneler
        // listede durabilir. Onlar icin hata verip yanlis alarm uretme.
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        bool onlyMain = enabledScenes.Length == 1 && enabledScenes[0] == MainScenePath;
        Require(onlyMain,
            "Build Settings'te etkin tek sahne Main.unity.",
            "Build Settings'te etkin sahneler beklenenden farkli - APK yanlis sahneyle acilabilir. " +
            "Etkin: " + string.Join(", ", enabledScenes));

        int disabledCount = EditorBuildSettings.scenes.Length - enabledScenes.Length;
        if (disabledCount > 0)
            Warn($"{disabledCount} devre disi sahne listede duruyor. " +
                 "Build Settings'i Onar komutu bunlari temizler.");

        Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
            "Aktif platform Android.",
            $"Aktif platform {EditorUserBuildSettings.activeBuildTarget}. Quest icin Android'e gec.");

        string identifier = PlayerSettings.GetApplicationIdentifier(
            UnityEditor.Build.NamedBuildTarget.Android);
        Require(!string.IsNullOrEmpty(identifier) && !identifier.Contains("DefaultCompany"),
            $"Android paket adi: {identifier}",
            $"Android paket adi varsayilan: {identifier}");
    }

    // ------------------------------------------------------- serialized yardim

    static bool GetBool(SerializedObject so, string name, bool fallback)
    {
        SerializedProperty property = so.FindProperty(name);
        return property != null ? property.boolValue : fallback;
    }

    static int GetInt(SerializedObject so, string name, int fallback)
    {
        SerializedProperty property = so.FindProperty(name);
        return property != null ? property.intValue : fallback;
    }
}
#endif
