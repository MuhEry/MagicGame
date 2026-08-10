using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Main.unity'yi tek tikla kurar.
///
/// Neden var: Main.unity bos kalmisti (yalnizca kamera + isik). Sahneyi elle kurmak
/// ~60 Inspector alani demek. Bu arac ayni seyi deterministik olarak yapar; sahne
/// bozulursa menuye tekrar basip sifirdan uretilebilir.
///
/// Kurdugu sey:
///   - XR Origin (Hands rig) + yurume/isinlanma KAPALI (snap turn acik kalir)
///   - Zemin, tezgah, bacanin altindaki spawn noktasi
///   - 3 dolap (B'nin prefab varyantlari)
///   - ShiftManager + ItemSpawner + TelemetryLogger (birbirine bagli)
///   - World-space HUD + "Yeni Vardiya" butonu (ShiftHudPresenter'a bagli)
///   - Build Settings'i Main.unity'ye cevirir
///
/// Yerlesim sartnameye gore: oyuncu sabit durur, her sey kol mesafesinde,
/// onundeki ~120 derecelik yayda, bel-goz hizasinda. Tezgah 0,95 m.
/// Baca esyayi oyuncunun KAFASININ USTUNDE degil, tezgahin uzak kenarinda birakir.
/// </summary>
static class MainSceneBuilder
{
    const string k_ScenePath = "Assets/_Project/Scenes/Main.unity";
    const string k_CabinetPrefabFolder = "Assets/_Project/Cabinets/Prefabs";
    const string k_ItemPrefabFolder = "Assets/_Project/Items/Prefabs";

    const string k_XrOriginPrefab =
        "Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";

    // GECICI placeholder. Projede baska wav yok ve "kendi basina dosya indirme"
    // kurali var. Gercek dusme sesi gelince Inspector'dan degistirilecek.
    const string k_DropClipPath = "Assets/VRTemplateAssets/Audio/Button_14_hover.wav";

    // Sartname: locomotion yok, sadece snap turn. Adinda bunlardan biri gecen
    // bilesenler kapatilir. Tip adiyla eslestiriyoruz ki XRI'in namespace
    // degisikliklerinden etkilenmesin (2.x -> 3.x'te bunlar yer degistirdi).
    static readonly string[] k_LocomotionToDisable =
    {
        "MoveProvider",          // DynamicMoveProvider, ContinuousMoveProvider
        "TeleportationProvider",
        "GrabMoveProvider",
        "ClimbProvider",
    };

    static readonly ItemCategory[] k_Categories =
    {
        ItemCategory.Sesli,
        ItemCategory.Parlak,
        ItemCategory.Agir,
    };

    [MenuItem("Tools/Kayip Esya/Main Sahnesini Kur", false, 20)]
    static void BuildMain()
    {
        var exists = File.Exists(k_ScenePath);
        var message = (exists
                          ? "Main.unity ZATEN VAR ve sifirdan yeniden olusturulacak.\nIcindeki elle yapilan degisiklikler KAYBOLUR.\n\n"
                          : "Main.unity olusturulacak.\n\n") +
                      "Kurulacaklar:\n" +
                      "- XR Origin (yurume/isinlanma kapali, snap turn acik)\n" +
                      "- 3 dolap + zemin + tezgah + spawn noktasi\n" +
                      "- ShiftManager + ItemSpawner + TelemetryLogger\n" +
                      "- World-space HUD + Yeni Vardiya butonu\n" +
                      "- Build Settings -> Main.unity\n\nDevam edilsin mi?";

        if (!EditorUtility.DisplayDialog("Main.unity kur", message, "Kur", "Vazgec"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder("Assets/_Project/Scenes");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLighting();
        CreateEnvironment(out var spawnPoint);
        CreateInteractionManager();
        CreateXrOrigin();
        PlaceCabinets();

        var spawner = CreateSystems(spawnPoint, out var shiftManager);
        SetUpChimneyEffect(spawnPoint, spawner);
        CreateHud(shiftManager);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, k_ScenePath);

        // FAZ 2: bu arac eskiden yalnizca tek oyuncu sahnesini kuruyordu. Kim
        // "Main Sahnesini Kur" derse Alteruna kurulumu (MultiplayerManager, avatar,
        // network spawner, oyuncu baslangic noktalari, iki HUD) siliniyordu ve
        // multiplayer sessizce geri gidiyordu. Artik ayni komut Faz 2'yi de kurar.
        MultiplayerProjectSetup.ApplySetupSilently();
        MultiplayerExperienceSetup.ApplySetupSilently();

        UpdateBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Main] " + k_ScenePath + " kuruldu (Faz 1 + Faz 2) ve Build Settings guncellendi.\n" +
            "Spawner'a eklenen esya sayisi: " + (spawner != null ? CountSpawnerItems(spawner) : 0) + "\n" +
            "TEST: Play -> HUD'daki 'Yeni Vardiya' butonuna bas -> bacadan esya duser.\n" +
            "Faz 2 kontrolu: Tools > Gece Vardiyasi > Faz 2 Kontrol Listesini Dogrula");
    }

    // ------------------------------------------------------------------ ortam

    static void CreateLighting()
    {
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void CreateEnvironment(out Transform spawnPoint)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Zemin";
        ground.transform.position = Vector3.zero;

        // Sartname: tezgah yuksekligi 0,95 m (oturarak/ayakta erisilebilir).
        var bench = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bench.name = "Tezgah";
        bench.transform.position = new Vector3(0f, 0.925f, 0.5f);
        bench.transform.localScale = new Vector3(1.6f, 0.05f, 0.5f);

        // Baca: esya buradan duser. Oyuncunun KAFASININ USTUNDE DEGIL - tezgahin
        // uzak kenarinin uzerinde, yukari bakma zorunlulugu olmasin diye.
        var chimney = new GameObject("Baca_SpawnPoint");
        chimney.transform.position = new Vector3(0f, 1.5f, 0.7f);
        spawnPoint = chimney.transform;

        // Baca efektinin isik ve ses kaynaklari. ChimneyEffect bilesenini
        // spawner olustuktan sonra SetUpChimneyEffect bagliyor.
        var light = chimney.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.55f);
        light.range = 3.5f;
        light.intensity = 0f;
        light.enabled = false;

        var audio = chimney.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f; // 3D - ses bacadan gelsin, oyuncu yonu duysun
        audio.minDistance = 0.6f;
        audio.maxDistance = 8f;
    }

    /// <summary>
    /// Bacadan esya duserken ses + isik. ItemSpawner.ItemSpawned event'ine baglanir.
    /// </summary>
    static void SetUpChimneyEffect(Transform spawnPoint, ItemSpawner spawner)
    {
        if (spawnPoint == null)
            return;

        var effect = spawnPoint.gameObject.AddComponent<ChimneyEffect>();

        var dropClip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_DropClipPath);
        if (dropClip == null)
            Debug.LogWarning("[Main] Baca dusme sesi bulunamadi: " + k_DropClipPath +
                             " - isik calisir, ses sessiz kalir.");
        else
            Debug.Log("[Main] Baca sesi PLACEHOLDER olarak baglandi (" + Path.GetFileName(k_DropClipPath) +
                      "). Gercek dusme sesi gelince Inspector'dan degistir.");

        var so = new SerializedObject(effect);
        so.FindProperty("m_Spawner").objectReferenceValue = spawner;
        so.FindProperty("m_Light").objectReferenceValue = spawnPoint.GetComponent<Light>();
        so.FindProperty("m_AudioSource").objectReferenceValue = spawnPoint.GetComponent<AudioSource>();
        so.FindProperty("m_DropClip").objectReferenceValue = dropClip;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateInteractionManager()
    {
        var go = new GameObject("XR Interaction Manager");
        go.AddComponent<XRInteractionManager>();
    }

    static void CreateXrOrigin()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_XrOriginPrefab);
        if (prefab == null)
        {
            Debug.LogError("[Main] XR Origin prefabi bulunamadi: " + k_XrOriginPrefab +
                           "\nXRI Starter Assets / Hands Interaction Demo ornekleri import edilmemis olabilir. " +
                           "Sahne kuruldu ama GOZLUKTE HICBIR SEY GORUNMEZ - rig'i elle eklemen gerekir.");
            return;
        }

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.transform.position = Vector3.zero;
        rig.transform.rotation = Quaternion.identity;

        DisableLocomotion(rig);
    }

    /// <summary>
    /// Sartname: "Oyuncu sabit durur. Locomotion yok, snap turn 30-45 var."
    /// Snap turn bilesenine DOKUNULMAZ, sadece yurume/isinlanma kapatilir.
    /// </summary>
    static void DisableLocomotion(GameObject rig)
    {
        var disabled = new List<string>();

        foreach (var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            var typeName = behaviour.GetType().Name;

            foreach (var needle in k_LocomotionToDisable)
            {
                if (!typeName.Contains(needle))
                    continue;

                behaviour.enabled = false;
                EditorUtility.SetDirty(behaviour);
                disabled.Add(typeName);
                break;
            }
        }

        if (disabled.Count > 0)
            Debug.Log("[Main] Yurume/isinlanma kapatildi: " + string.Join(", ", disabled));
        else
            Debug.LogWarning("[Main] Kapatilacak locomotion bileseni bulunamadi. " +
                             "Rig'de yurume varsa Inspector'dan elle kapat.");
    }

    // ---------------------------------------------------------------- dolaplar

    static void PlaceCabinets()
    {
        // x = -0.75 / 0 / +0.75, z = 1.15 -> soketler ~0,85 m onde, ~82 derecelik yay.
        for (var i = 0; i < k_Categories.Length; i++)
        {
            var category = k_Categories[i];
            var path = $"{k_CabinetPrefabFolder}/Dolap_{category}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError("[Main] Dolap prefabi bulunamadi: " + path);
                continue;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(-0.75f + i * 0.75f, 0f, 1.15f);
        }
    }

    // --------------------------------------------------------------- sistemler

    static ItemSpawner CreateSystems(Transform spawnPoint, out ShiftManager shiftManager)
    {
        var systems = new GameObject("Systems");

        var spawner = systems.AddComponent<ItemSpawner>();
        shiftManager = systems.AddComponent<ShiftManager>();
        var telemetry = systems.AddComponent<TelemetryLogger>();

        // --- ItemSpawner ---
        var spawnerSo = new SerializedObject(spawner);
        spawnerSo.FindProperty("spawnPoint").objectReferenceValue = spawnPoint;
        spawnerSo.FindProperty("seed").intValue = 0; // 0 = her vardiya farkli sira
        FillItemPrefabs(spawnerSo.FindProperty("itemPrefabs"));
        spawnerSo.ApplyModifiedPropertiesWithoutUndo();

        // --- ShiftManager ---
        var shiftSo = new SerializedObject(shiftManager);
        shiftSo.FindProperty("itemSpawner").objectReferenceValue = spawner;
        shiftSo.ApplyModifiedPropertiesWithoutUndo();

        // --- TelemetryLogger ---
        var telemetrySo = new SerializedObject(telemetry);
        telemetrySo.FindProperty("shiftManager").objectReferenceValue = shiftManager;
        telemetrySo.ApplyModifiedPropertiesWithoutUndo();

        return spawner;
    }

    /// <summary>
    /// Items/Prefabs altindaki, uzerinde gecerli ItemData bulunan tum prefablari
    /// spawner listesine yazar. id, A'nin ItemData varligindan okunur - elle
    /// girilen bir sayi degil, boylece telemetriyle her zaman tutarli kalir.
    /// </summary>
    static void FillItemPrefabs(SerializedProperty listProperty)
    {
        if (listProperty == null)
        {
            Debug.LogError("[Main] ItemSpawner.itemPrefabs alani bulunamadi.");
            return;
        }

        listProperty.ClearArray();

        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { k_ItemPrefabFolder });
        var index = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            var identity = prefab.GetComponentInChildren<ItemIdentity>(true);
            if (identity == null || identity.ItemData == null)
                continue; // temel item.prefab gibi ItemData'si atanmamis olanlari atla

            listProperty.InsertArrayElementAtIndex(index);
            var element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("itemId").intValue = identity.ItemId;
            element.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            index++;
        }

        if (index == 0)
            Debug.LogWarning("[Main] " + k_ItemPrefabFolder + " altinda ItemData'si atanmis prefab bulunamadi. " +
                             "Spawner bos kalir, bacadan esya dusmez.");
    }

    static int CountSpawnerItems(ItemSpawner spawner)
    {
        var so = new SerializedObject(spawner);
        var list = so.FindProperty("itemPrefabs");
        return list != null ? list.arraySize : 0;
    }

    // --------------------------------------------------------------------- HUD

    static void CreateHud(ShiftManager shiftManager)
    {
        // XRI'in UI'a isin gonderebilmesi icin EventSystem + XRUIInputModule sart.
        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<XRUIInputModule>();

        // Sartname: world-space pano, ~2,0 m mesafe, ~1,4 m yukseklik.
        var canvasGo = new GameObject("HUD", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        var canvasRect = (RectTransform)canvasGo.transform;
        canvasRect.sizeDelta = new Vector2(1000f, 600f);
        canvasRect.position = new Vector3(0f, 1.45f, 2.0f);
        canvasRect.localScale = Vector3.one * 0.001f; // 1000 px -> 1 m

        var timeText = CreateText(canvasRect, "Txt_KalanSure", new Vector2(0f, 230f), new Vector2(900f, 90f), 64f);
        var scoreText = CreateText(canvasRect, "Txt_Skor", new Vector2(0f, 130f), new Vector2(900f, 80f), 48f);
        var stateText = CreateText(canvasRect, "Txt_Durum", new Vector2(0f, 50f), new Vector2(900f, 70f), 36f);
        var lastText = CreateText(canvasRect, "Txt_SonKarar", new Vector2(0f, -40f), new Vector2(900f, 120f), 32f);

        // Rapor paneli: vardiya bitince gorunur, baslangicta kapali.
        var reportGo = new GameObject("Panel_Rapor", typeof(RectTransform));
        reportGo.transform.SetParent(canvasRect, false);
        var reportRect = (RectTransform)reportGo.transform;
        reportRect.anchorMin = reportRect.anchorMax = new Vector2(0.5f, 0.5f);
        reportRect.pivot = new Vector2(0.5f, 0.5f);
        reportRect.anchoredPosition = Vector2.zero;
        reportRect.sizeDelta = new Vector2(1000f, 600f);
        var reportBg = reportGo.AddComponent<Image>();
        reportBg.color = new Color(0f, 0f, 0f, 0.85f);
        var reportText = CreateText(reportRect, "Txt_Rapor", new Vector2(0f, 60f), new Vector2(900f, 400f), 40f);
        reportGo.SetActive(false);

        var presenter = canvasGo.AddComponent<ShiftHudPresenter>();
        var presenterSo = new SerializedObject(presenter);
        presenterSo.FindProperty("shiftManager").objectReferenceValue = shiftManager;
        presenterSo.FindProperty("remainingTimeText").objectReferenceValue = timeText;
        presenterSo.FindProperty("scoreText").objectReferenceValue = scoreText;
        presenterSo.FindProperty("stateText").objectReferenceValue = stateText;
        presenterSo.FindProperty("lastDecisionText").objectReferenceValue = lastText;
        presenterSo.FindProperty("reportPanel").objectReferenceValue = reportGo;
        presenterSo.FindProperty("reportText").objectReferenceValue = reportText;
        presenterSo.ApplyModifiedPropertiesWithoutUndo();

        CreateNewShiftButton(canvasRect, presenter);
    }

    static void CreateNewShiftButton(RectTransform parent, ShiftHudPresenter presenter)
    {
        var buttonGo = new GameObject("Btn_YeniVardiya", typeof(RectTransform));
        buttonGo.transform.SetParent(parent, false);

        var rect = (RectTransform)buttonGo.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -210f);
        rect.sizeDelta = new Vector2(500f, 120f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color(0.15f, 0.45f, 0.25f);

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;

        var label = CreateText(rect, "Txt_Buton", Vector2.zero, new Vector2(480f, 100f), 44f);
        label.text = "Yeni Vardiya";

        // Butonu ShiftHudPresenter.StartNewShiftFromButton'a bagla.
        UnityEventTools.AddPersistentListener(button.onClick, presenter.StartNewShiftFromButton);
    }

    static TMP_Text CreateText(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.text = "-";
        return text;
    }

    // ---------------------------------------------------------- build settings

    static void UpdateBuildSettings()
    {
        // Build Settings SampleScene'i isaret ediyordu -> APK'da bos ekran cikiyordu.
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(k_ScenePath, true),
        };

        Debug.Log("[Main] Build Settings -> yalnizca " + k_ScenePath);
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        Directory.CreateDirectory(assetPath);
        AssetDatabase.Refresh();
    }
}
