using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Sandbox_B_Cabinets.unity sahnesini tek tikla kuran editor araci.
///
/// Neden var: Gelistirici B'nin basari kriteri "sandbox sahnesinde elle suruklenen
/// test kupleriyle dogru/yanlis akisi gorunuyor". Bu sahneyi elle kurmak ~40 Inspector
/// alani demek; bu arac ayni seyi deterministik olarak yapar, boylece sahne her
/// bozuldugunda yeniden uretilebilir.
///
/// KAPSAM: Yalnizca Assets/_Project/Cabinets/ ve Assets/_Project/Scenes/Sandbox_B_Cabinets.unity
/// dosyalarina yazar. Main.unity'ye veya baska gelistiricinin klasorune DOKUNMAZ.
/// </summary>
static class CabinetSandboxBuilder
{
    const string k_ScenePath = "Assets/_Project/Scenes/Sandbox_B_Cabinets.unity";
    const string k_MaterialFolder = "Assets/_Project/Cabinets/Materials";

    static readonly ItemCategory[] k_Categories =
    {
        ItemCategory.Sesli,
        ItemCategory.Parlak,
        ItemCategory.Agir,
    };

    // Dolaplarin ayirt edilebilmesi icin notr renkler (Art'tan model gelince degisecek).
    static readonly Color[] k_CabinetColors =
    {
        new Color(0.30f, 0.42f, 0.62f), // Sesli  - mavi
        new Color(0.62f, 0.55f, 0.25f), // Parlak - sari
        new Color(0.40f, 0.40f, 0.44f), // Agir   - gri
    };

    [MenuItem("Tools/Kayip Esya/B - Sandbox Sahnesini Kur", false, 0)]
    static void BuildSandbox()
    {
        var exists = File.Exists(k_ScenePath);
        var message = exists
            ? "Sandbox_B_Cabinets.unity ZATEN VAR ve sifirdan yeniden olusturulacak.\n\n" +
              "Icindeki elle yaptigin degisiklikler KAYBOLUR. Devam edilsin mi?"
            : "Sandbox_B_Cabinets.unity olusturulacak:\n\n" +
              "- 3 dolap (Sesli / Parlak / Agir) + CategorySocket\n" +
              "- 3 test kupu (XRGrabInteractable + CabinetTestItem)\n" +
              "- XR Interaction Manager, zemin, tezgah\n\nDevam edilsin mi?";

        if (!EditorUtility.DisplayDialog("Sandbox_B_Cabinets", message, "Kur", "Vazgec"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder("Assets/_Project/Scenes");
        EnsureFolder(k_MaterialFolder);

        var materials = new Material[k_Categories.Length];
        for (var i = 0; i < k_Categories.Length; i++)
            materials[i] = GetOrCreateCabinetMaterial(k_Categories[i], k_CabinetColors[i]);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        SetUpCamera();
        CreateGround();
        CreateInteractionManager();
        CreateBench();

        for (var i = 0; i < k_Categories.Length; i++)
        {
            var x = -0.9f + i * 0.9f;
            CreateCabinet(k_Categories[i], materials[i], new Vector3(x, 0f, 1.7f));
        }

        for (var i = 0; i < k_Categories.Length; i++)
        {
            var x = -0.4f + i * 0.4f;
            CreateTestCube(k_Categories[i], materials[i], new Vector3(x, 1.0f, 0.6f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, k_ScenePath);
        AssetDatabase.Refresh();

        Debug.Log(
            "[Sandbox] " + k_ScenePath + " kuruldu.\n" +
            "TEST: Play'e bas -> Hierarchy'den bir TestKup_* sec -> Scene penceresinde ok " +
            "gizmosuyla kupu bir dolabin agzindaki kureye surukle.\n" +
            "Dogru dolap -> Console'da '[KARAR] DOGRU', yanlis dolap -> '[KARAR] YANLIS' " +
            "ve kup 0,4 sn sonra disari itilir.");
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        // Klasoru diskte olusturup Refresh diyoruz: boylece Unity repoda zaten
        // takipli olan .meta dosyasini (orn. Scenes.meta) yeniden kullanir ve
        // guid degismez. AssetDatabase.CreateFolder yeni guid uretebilir.
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

        // Adim 2'de FeedbackController emissive rengi runtime'da degistirecek.
        // SARTNAME "Onemli Noktalar #5": URP'de emissive'i runtime'da degistirebilmek icin
        // materyalin Emission'i ACIK olmali. Renk siyah -> normalde parlamaz.
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

    static void CreateCabinet(ItemCategory category, Material material, Vector3 position)
    {
        var root = new GameObject($"Dolap_{category}");
        root.transform.position = position;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Govde";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        body.transform.localScale = new Vector3(0.7f, 1.0f, 0.5f);
        if (material != null)
            body.GetComponent<Renderer>().sharedMaterial = material;

        // Soket: dolabin on yuzunde, bel hizasinda (~1,0 m).
        var socketGo = new GameObject("Socket");
        socketGo.transform.SetParent(root.transform, false);
        socketGo.transform.localPosition = new Vector3(0f, 1.0f, -0.3f);
        // forward'i oyuncuya (-Z) dogru cevir: yanlis esya BU yone itilecek.
        socketGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var trigger = socketGo.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.22f;

        var socket = socketGo.AddComponent<CategorySocket>();
        socket.acceptedCategory = category;
        EditorUtility.SetDirty(socket);
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
