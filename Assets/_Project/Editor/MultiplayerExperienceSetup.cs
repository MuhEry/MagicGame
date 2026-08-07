#if UNITY_EDITOR
using Alteruna.Multiplayer.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MultiplayerExperienceSetup
{
    const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

    // MultiplayerProjectSetup ile ayni gerekce: sahneyi acilista kendiliginden
    // degistirip kaydeden [InitializeOnLoadMethod] kaldirildi. Yalnizca menuden calisir.

    [MenuItem("Tools/Gece Vardiyası/Oyuncu Konumları ve HUD Kurulumunu Uygula", false, 41)]
    public static void ApplyFromMenu()
    {
        ApplySetup(true);
    }

    /// <summary>MainSceneBuilder sahneyi sifirdan kurduktan sonra bunu cagirir.</summary>
    public static void ApplySetupSilently()
    {
        ApplySetup(false);
    }

    public static bool SetupLooksComplete()
    {
        GameObject hud1 = GameObject.Find("HUD_Player1");
        GameObject hud2 = GameObject.Find("HUD_Player2");
        GameObject spawnRoot = GameObject.Find("PlayerSpawnPoints");
        GameObject versionMarker = GameObject.Find("PlayerExperienceSetup_v2");

        RectTransform hud1Rect = hud1 != null ? hud1.GetComponent<RectTransform>() : null;
        RectTransform hud2Rect = hud2 != null ? hud2.GetComponent<RectTransform>() : null;

        return versionMarker != null && hud1 != null && hud2 != null && spawnRoot != null &&
               hud1.GetComponent<LocalPlayerHud>() != null &&
               hud2.GetComponent<LocalPlayerHud>() != null &&
               hud1Rect != null && Mathf.Abs(hud1Rect.anchoredPosition.x + 0.45f) < 0.01f &&
               hud2Rect != null && Mathf.Abs(hud2Rect.anchoredPosition.x - 0.45f) < 0.01f &&
               spawnRoot.transform.Find("Player_1_Spawn") != null &&
               spawnRoot.transform.Find("Player_2_Spawn") != null;
    }

    static void ApplySetup(bool showDialog)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Oyuncu ve HUD Kurulumu", "Önce Play Mode'dan çık.", "Tamam");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainScenePath)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Oyuncu ve HUD Kurulumu", "Önce Main sahnesini aç.", "Tamam");
            return;
        }

        RemoveOrphanHudCanvases();
        ConfigurePlayerHudPair(scene);
        ConfigureSpawnPoints();
        if (GameObject.Find("PlayerExperienceSetup_v2") == null)
            new GameObject("PlayerExperienceSetup_v2");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[MultiplayerExperienceSetup] İki yerel HUD ve iki oyuncu başlangıç noktası hazırlandı.");
        if (showDialog)
            EditorUtility.DisplayDialog("Oyuncu ve HUD Kurulumu", "Kurulum tamamlandı.", "Tamam");
    }

    static void ConfigurePlayerHudPair(Scene scene)
    {
        GameObject hud1 = GameObject.Find("HUD_Player1");
        if (hud1 == null)
            hud1 = GameObject.Find("HUD");
        if (hud1 == null)
            throw new System.InvalidOperationException("Main sahnesindeki HUD bulunamadı.");

        hud1.name = "HUD_Player1";
        ConfigureHud(hud1, 0, new Vector3(-0.45f, 1.45f, 0.9f));

        GameObject hud2 = GameObject.Find("HUD_Player2");
        if (hud2 == null)
        {
            hud2 = Object.Instantiate(hud1);
            hud2.name = "HUD_Player2";
            SceneManager.MoveGameObjectToScene(hud2, scene);
        }

        ConfigureHud(hud2, 1, new Vector3(0.45f, 1.45f, 0.9f));
    }

    static void ConfigureHud(GameObject hud, int slot, Vector3 position)
    {
        hud.SetActive(true);
        hud.transform.SetParent(null);
        RectTransform rect = hud.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition3D = position;
            rect.localRotation = Quaternion.identity;
        }
        else
        {
            hud.transform.SetPositionAndRotation(position, Quaternion.identity);
        }

        LocalPlayerHud localHud = hud.GetComponent<LocalPlayerHud>();
        if (localHud == null)
            localHud = hud.AddComponent<LocalPlayerHud>();
        localHud.ConfigureSlot(slot);
        EditorUtility.SetDirty(localHud);

        ConfigureDiagnostics(hud);
    }

    /// <summary>
    /// Ag teshis satiri. Varsayilan olarak KAPALI durur; gozlukte sorun aranirken
    /// Inspector'dan showOnStart isaretlenir. Odaya girilip girilmedigini,
    /// host mu istemci mi olundugunu ve seed'i cihazda gosterir.
    /// </summary>
    static void ConfigureDiagnostics(GameObject hud)
    {
        RectTransform hudRect = hud.GetComponent<RectTransform>();
        if (hudRect == null)
            return;

        Transform existing = hudRect.Find("Txt_AgTeshis");
        TextMeshProUGUI label;
        if (existing != null)
        {
            label = existing.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            GameObject labelGo = new GameObject("Txt_AgTeshis", typeof(RectTransform));
            labelGo.transform.SetParent(hudRect, false);
            RectTransform labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0f, -270f);
            labelRect.sizeDelta = new Vector2(940f, 150f);

            label = labelGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = new Color(0.75f, 0.9f, 1f);
            label.text = "-";
        }

        if (label == null)
            return;

        NetworkDiagnosticsHud diagnostics = hud.GetComponent<NetworkDiagnosticsHud>();
        if (diagnostics == null)
            diagnostics = hud.AddComponent<NetworkDiagnosticsHud>();

        SerializedObject diagnosticsSo = new SerializedObject(diagnostics);
        diagnosticsSo.FindProperty("targetText").objectReferenceValue = label;
        diagnosticsSo.FindProperty("shiftManager").objectReferenceValue =
            Object.FindFirstObjectByType<ShiftManager>();
        diagnosticsSo.FindProperty("itemSpawner").objectReferenceValue =
            Object.FindFirstObjectByType<ItemSpawner>();
        diagnosticsSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(diagnostics);
    }

    /// <summary>
    /// Sahnede birikmis, hicbir metni bagli olmayan HUD kopyalarini siler
    /// (orn. "HUDCabinet"). Bunlar ShiftManager olaylarina abone oluyor ama
    /// hicbir sey gostermiyor; sahne panelini okumayi zorlastiriyorlardi.
    /// </summary>
    static void RemoveOrphanHudCanvases()
    {
        foreach (ShiftHudPresenter presenter in
                 Object.FindObjectsByType<ShiftHudPresenter>(FindObjectsSortMode.None))
        {
            if (presenter == null)
                continue;

            GameObject hud = presenter.gameObject;
            if (hud.name == "HUD" || hud.name == "HUD_Player1" || hud.name == "HUD_Player2")
                continue;

            SerializedObject so = new SerializedObject(presenter);
            bool hasAnyBinding =
                so.FindProperty("remainingTimeText").objectReferenceValue != null ||
                so.FindProperty("scoreText").objectReferenceValue != null ||
                so.FindProperty("reportText").objectReferenceValue != null;

            if (hasAnyBinding)
                continue;

            Debug.Log($"[MultiplayerExperienceSetup] Bos HUD kopyasi silindi: {hud.name}");
            Object.DestroyImmediate(hud);
        }
    }

    static void ConfigureSpawnPoints()
    {
        GameObject root = GameObject.Find("PlayerSpawnPoints");
        if (root == null)
            root = new GameObject("PlayerSpawnPoints");

        Transform spawn1 = GetOrCreateChild(root.transform, "Player_1_Spawn");
        Transform spawn2 = GetOrCreateChild(root.transform, "Player_2_Spawn");
        SetSpawnTransform(spawn1, new Vector3(-0.45f, 0f, -0.75f));
        SetSpawnTransform(spawn2, new Vector3(0.45f, 0f, -0.75f));

        AlterunaComponents.MultiplayerManager manager =
            Object.FindFirstObjectByType<AlterunaComponents.MultiplayerManager>();
        if (manager == null)
            throw new System.InvalidOperationException("MultiplayerManager bulunamadı.");

        SerializedObject managerSo = new SerializedObject(manager);
        SerializedProperty defaultSpawn = managerSo.FindProperty("AvatarSpawnLocation");
        if (defaultSpawn != null)
            defaultSpawn.objectReferenceValue = spawn1;

        SerializedProperty perIndex = managerSo.FindProperty("SpawnAvatarPerIndex");
        if (perIndex != null)
            perIndex.boolValue = true;

        SerializedProperty locations = managerSo.FindProperty("AvatarSpawnLocations");
        if (locations != null)
        {
            locations.arraySize = 2;
            locations.GetArrayElementAtIndex(0).objectReferenceValue = spawn1;
            locations.GetArrayElementAtIndex(1).objectReferenceValue = spawn2;
        }
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        GameObject rig = GameObject.Find("XR Origin Hands (XR Rig)");
        if (rig != null)
            rig.transform.SetPositionAndRotation(spawn1.position, spawn1.rotation);

        EditorUtility.SetDirty(manager);
    }

    static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    static void SetSpawnTransform(Transform spawn, Vector3 position)
    {
        spawn.SetPositionAndRotation(position, Quaternion.identity);
        spawn.localScale = Vector3.one;
    }
}
#endif
