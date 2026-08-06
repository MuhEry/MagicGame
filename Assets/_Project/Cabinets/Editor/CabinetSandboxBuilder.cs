using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Dolap prefablarini ve Sandbox_B_Cabinets.unity sahnesini tek tikla kuran editor araci.
///
/// Neden var: Gelistirici B'nin teslimi "tek prefab + 3 kategori varyanti" ve calisir bir
/// sandbox sahnesi. Bunu elle kurmak ~40 Inspector alani demek; bu arac ayni seyi
/// deterministik olarak uretir, boylece sahne/prefab her bozuldugunda yeniden yaratilabilir.
///
/// KAPSAM: Yalnizca Assets/_Project/Cabinets/ ve Assets/_Project/Scenes/Sandbox_B_Cabinets.unity
/// altina yazar. Main.unity'ye veya baska gelistiricinin klasorune DOKUNMAZ.
/// (VRTemplateAssets altindaki ses dosyalari yalnizca OKUNUR, degistirilmez.)
/// </summary>
static class CabinetSandboxBuilder
{
    const string k_ScenePath = "Assets/_Project/Scenes/Sandbox_B_Cabinets.unity";
    const string k_MaterialFolder = "Assets/_Project/Cabinets/Materials";
    const string k_PrefabFolder = "Assets/_Project/Cabinets/Prefabs";
    const string k_BasePrefabPath = k_PrefabFolder + "/Dolap.prefab";

    // GECICI placeholder sesler. Projede baska wav yok ve sartname "kendi basina dosya
    // indirme" diyor -> VR sablonuyla gelen hazir kliplere referans veriyoruz.
    // Kendi seslerin gelince Assets/_Project/Cabinets/Audio/ altina koyup Inspector'dan degistir.
    const string k_CorrectClipPath = "Assets/VRTemplateAssets/Audio/Button_22_click.wav";
    const string k_WrongClipPath = "Assets/VRTemplateAssets/Audio/Button_14_hover.wav";

    static readonly ItemCategory[] k_Categories =
    {
        ItemCategory.Sesli,
        ItemCategory.Parlak,
        ItemCategory.Agir,
    };

    // Dolaplarin ayirt edilebilmesi icin notr renkler (D'nin modeli gelince degisecek).
    static readonly Color[] k_CabinetColors =
    {
        new Color(0.30f, 0.42f, 0.62f), // Sesli  - mavi
        new Color(0.62f, 0.55f, 0.25f), // Parlak - sari
        new Color(0.40f, 0.40f, 0.44f), // Agir   - gri
    };

    [MenuItem("Tools/Kayip Esya/B - Sandbox Sahnesini Kur", false, 0)]
    static void BuildAll()
    {
        var exists = File.Exists(k_ScenePath);
        var message = exists
            ? "Sandbox_B_Cabinets.unity ZATEN VAR ve sifirdan yeniden olusturulacak.\n\n" +
              "Sahnede elle yaptigin degisiklikler KAYBOLUR. Dolap prefablari da guncellenir.\n\n" +
              "Devam edilsin mi?"
            : "Su dosyalar olusturulacak:\n\n" +
              "- Cabinets/Prefabs/Dolap.prefab (ana prefab)\n" +
              "- Dolap_Sesli / Dolap_Parlak / Dolap_Agir (prefab varyantlari)\n" +
              "- Scenes/Sandbox_B_Cabinets.unity (3 dolap + 3 test kupu)\n\n" +
              "Devam edilsin mi?";

        if (!EditorUtility.DisplayDialog("Sandbox_B_Cabinets", message, "Kur", "Vazgec"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder("Assets/_Project/Scenes");
        EnsureFolder(k_MaterialFolder);
        EnsureFolder(k_PrefabFolder);

        var materials = new Material[k_Categories.Length];
        for (var i = 0; i < k_Categories.Length; i++)
            materials[i] = GetOrCreateCabinetMaterial(k_Categories[i], k_CabinetColors[i]);

        var correctClip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_CorrectClipPath);
        var wrongClip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_WrongClipPath);
        if (correctClip == null || wrongClip == null)
            Debug.LogWarning("[Sandbox] Placeholder ses dosyalari bulunamadi. Ses kanali sessiz kalacak, " +
                             "gorsel ve haptik calisir. AudioClip'leri Inspector'dan elle atayabilirsin.");

        // Bos bir sahne ac; prefablari bu sahnede uretip gecici objeleri temizleyecegiz.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var variants = BuildCabinetPrefabs(materials, correctClip, wrongClip);

        SetUpCamera();
        CreateGround();
        CreateInteractionManager();
        CreateBench();

        for (var i = 0; i < variants.Length; i++)
        {
            if (variants[i] == null)
                continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(variants[i], scene);
            instance.transform.position = new Vector3(-0.9f + i * 0.9f, 0f, 1.7f);
        }

        for (var i = 0; i < k_Categories.Length; i++)
            CreateTestCube(k_Categories[i], materials[i], new Vector3(-0.4f + i * 0.4f, 1.0f, 0.6f));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, k_ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Sandbox] Kuruldu.\n" +
            "  Prefablar: " + k_PrefabFolder + "/Dolap.prefab + 3 varyant\n" +
            "  Sahne    : " + k_ScenePath + "\n" +
            "TEST: Play -> Hierarchy'den TestKup_* sec -> Inspector'da Position yaz.\n" +
            "  Sesli dolap  (-0.9, 1, 1.4)\n" +
            "  Parlak dolap ( 0.0, 1, 1.4)\n" +
            "  Agir dolap   ( 0.9, 1, 1.4)");
    }

    /// <summary>
    /// Tek bir ana prefab + kategori basina bir PREFAB VARYANTI uretir.
    /// Sartname madde 5: "3 dolap prefab'i, ayni prefab'in 3 varyanti olarak
    /// (kategori Inspector'dan secilir)".
    /// </summary>
    static GameObject[] BuildCabinetPrefabs(Material[] materials, AudioClip correctClip, AudioClip wrongClip)
    {
        // --- Ana prefab ---
        var template = BuildCabinetGameObject("Dolap", ItemCategory.Sesli, materials[0], correctClip, wrongClip);
        var basePrefab = PrefabUtility.SaveAsPrefabAsset(template, k_BasePrefabPath);
        Object.DestroyImmediate(template);

        if (basePrefab == null)
        {
            Debug.LogError("[Sandbox] Ana dolap prefabi olusturulamadi.");
            return new GameObject[k_Categories.Length];
        }

        // --- Varyantlar ---
        var variants = new GameObject[k_Categories.Length];
        for (var i = 0; i < k_Categories.Length; i++)
        {
            var category = k_Categories[i];
            var variantPath = $"{k_PrefabFolder}/Dolap_{category}.prefab";

            // Bir prefab INSTANCE'ini yeni bir prefab olarak kaydetmek varyant uretir.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            instance.name = $"Dolap_{category}";

            var socket = instance.GetComponentInChildren<CategorySocket>();
            if (socket != null)
            {
                socket.acceptedCategory = category;
                EditorUtility.SetDirty(socket);
            }

            var body = instance.transform.Find("Govde");
            if (body != null && materials[i] != null)
                body.GetComponent<Renderer>().sharedMaterial = materials[i];

            variants[i] = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            Object.DestroyImmediate(instance);
        }

        return variants;
    }

    /// <summary>Bir dolabin tum hiyerarsisini kurar (prefab kaynagi olarak kullanilir).</summary>
    static GameObject BuildCabinetGameObject(string name, ItemCategory category, Material material,
        AudioClip correctClip, AudioClip wrongClip)
    {
        var root = new GameObject(name);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Govde";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        body.transform.localScale = new Vector3(0.7f, 1.0f, 0.5f);
        var bodyRenderer = body.GetComponent<Renderer>();
        if (material != null)
            bodyRenderer.sharedMaterial = material;

        // Soket: dolabin on yuzunde, bel hizasinda (~1,0 m).
        var socketGo = new GameObject("Socket");
        socketGo.transform.SetParent(root.transform, false);
        socketGo.transform.localPosition = new Vector3(0f, 1.0f, -0.3f);
        // forward'i oyuncuya (-Z) dogru cevir: yanlis esya BU yone itilecek.
        socketGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var trigger = socketGo.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.22f;

        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f; // 3D - ses dolaptan gelsin
        audio.minDistance = 0.5f;

        var feedback = root.AddComponent<FeedbackController>();
        var feedbackSo = new SerializedObject(feedback);
        feedbackSo.FindProperty("m_BodyRenderer").objectReferenceValue = bodyRenderer;
        feedbackSo.FindProperty("m_AudioSource").objectReferenceValue = audio;
        feedbackSo.FindProperty("m_CorrectClip").objectReferenceValue = correctClip;
        feedbackSo.FindProperty("m_WrongClip").objectReferenceValue = wrongClip;
        feedbackSo.ApplyModifiedPropertiesWithoutUndo();

        var socket = socketGo.AddComponent<CategorySocket>();
        socket.acceptedCategory = category;
        var socketSo = new SerializedObject(socket);
        socketSo.FindProperty("m_Feedback").objectReferenceValue = feedback;
        socketSo.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        // Klasoru diskte olusturup Refresh diyoruz: boylece Unity repoda zaten takipli
        // olan .meta dosyasini (orn. Scenes.meta) yeniden kullanir ve guid degismez.
        Directory.CreateDirectory(assetPath);
        AssetDatabase.Refresh();
    }

    static Material GetOrCreateCabinetMaterial(ItemCategory category, Color color)
    {
        var path = $"{k_MaterialFolder}/MAT_Dolap_{category}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[Sandbox] URP/Lit shader bulunamadi. Render pipeline ayarini kontrol et.");
            return null;
        }

        var material = new Material(shader) { name = $"MAT_Dolap_{category}" };
        material.SetColor("_BaseColor", color);

        // SARTNAME "Onemli Noktalar #5": URP'de emissive'i runtime'da degistirebilmek icin
        // materyalin Emission'i ACIK olmali. Baslangic rengi siyah -> normalde parlamaz.
        // FeedbackController bu degeri MaterialPropertyBlock ile ezer, materyali klonlamaz.
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    static void SetUpCamera()
    {
        var camera = Object.FindFirstObjectByType<Camera>();
        if (camera == null)
            return;

        // Gozluk yok - masaustunde bakabilmek icin goz hizasina alalim.
        camera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -0.6f), Quaternion.identity);
    }

    static void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Zemin";
        ground.transform.position = Vector3.zero;
    }

    static void CreateInteractionManager()
    {
        var go = new GameObject("XR Interaction Manager");
        go.AddComponent<XRInteractionManager>();
    }

    static void CreateBench()
    {
        var bench = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bench.name = "Tezgah";
        // Sartname: tezgah yuksekligi 0,95 m.
        bench.transform.position = new Vector3(0f, 0.925f, 0.6f);
        bench.transform.localScale = new Vector3(1.6f, 0.05f, 0.6f);
    }

    static void CreateTestCube(ItemCategory category, Material material, Vector3 position)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"TestKup_{category}";
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * 0.12f;
        if (material != null)
            cube.GetComponent<Renderer>().sharedMaterial = material;

        var rb = cube.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;

        cube.AddComponent<XRGrabInteractable>();

        var testItem = cube.AddComponent<CabinetTestItem>();
        testItem.category = category;
        testItem.itemId = 900 + (int)category; // gecici sahte id
        EditorUtility.SetDirty(testItem);
    }
}
