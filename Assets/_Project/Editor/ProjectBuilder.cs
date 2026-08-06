using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class ProjectBuilder
{
    [MenuItem("Gece Vardiyasi/Create Assets and Prefabs")]
    public static void CreateAssetsAndPrefabs()
    {
        Debug.Log("Starting generation of assets and prefabs...");

        // 1. Create Directories if they do not exist
        string dataDir = "Assets/_Project/Items/Data";
        string prefabDir = "Assets/_Project/Items/Prefabs";

        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        if (!Directory.Exists(prefabDir)) Directory.CreateDirectory(prefabDir);

        AssetDatabase.Refresh();

        // 2. Load audio clip placeholders from VRTemplateAssets or Samples
        AudioClip hoverClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/VRTemplateAssets/Audio/Button_14_hover.wav");
        AudioClip clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/VRTemplateAssets/Audio/Button_22_click.wav");

        if (hoverClip == null) Debug.LogWarning("Could not find hover audio clip placeholder!");
        if (clickClip == null) Debug.LogWarning("Could not find click audio clip placeholder!");

        // 3. Create materials for items (we want to use standard Lit material and color them)
        Material itemBaseMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        itemBaseMat.EnableKeyword("_EMISSION");
        itemBaseMat.SetColor("_EmissionColor", Color.black);
        AssetDatabase.CreateAsset(itemBaseMat, "Assets/_Project/Items/ItemBaseMaterial.mat");

        // 4. Generate 9 ItemData assets
        ItemData[] itemDataArray = new ItemData[9];

        // --- Sesli (101-103) ---
        itemDataArray[0] = CreateItemData(101, "Cingirak", ItemCategory.Sesli, 1.0f, hoverClip, Color.black, dataDir);
        itemDataArray[1] = CreateItemData(102, "Kutu", ItemCategory.Sesli, 1.0f, clickClip, Color.black, dataDir);
        itemDataArray[2] = CreateItemData(103, "Kumbara", ItemCategory.Sesli, 1.2f, clickClip, Color.black, dataDir);

        // --- Parlak (201-203) ---
        itemDataArray[3] = CreateItemData(201, "Kure", ItemCategory.Parlak, 1.0f, null, Color.red * 4f, dataDir);
        itemDataArray[4] = CreateItemData(202, "Kristal", ItemCategory.Parlak, 1.0f, null, Color.green * 4f, dataDir);
        itemDataArray[5] = CreateItemData(203, "Fener", ItemCategory.Parlak, 1.5f, null, Color.cyan * 4f, dataDir);

        // --- Agir (301-303) ---
        itemDataArray[6] = CreateItemData(301, "Kulp", ItemCategory.Agir, 5.0f, null, Color.black, dataDir);
        itemDataArray[7] = CreateItemData(302, "Ors", ItemCategory.Agir, 8.0f, null, Color.black, dataDir);
        itemDataArray[8] = CreateItemData(303, "Kese", ItemCategory.Agir, 10.0f, null, Color.black, dataDir);

        AssetDatabase.SaveAssets();

        // 5. Create Base Prefab
        GameObject baseGo = new GameObject("BaseItem");
        baseGo.transform.position = Vector3.zero;
        baseGo.transform.rotation = Quaternion.identity;

        // Visuals
        GameObject visualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visualGo.name = "Visuals";
        visualGo.transform.SetParent(baseGo.transform);
        visualGo.transform.localPosition = Vector3.zero;
        visualGo.transform.localRotation = Quaternion.identity;
        visualGo.transform.localScale = Vector3.one * 0.15f; // reasonable VR size (15cm cube)

        // Components on base
        Rigidbody rb = baseGo.AddComponent<Rigidbody>();
        rb.mass = 1.0f;
        rb.useGravity = true;

        XRGrabInteractable grab = baseGo.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        grab.throwOnDetach = false;

        // Attach transform setup
        GameObject attachGo = new GameObject("AttachTransform");
        attachGo.transform.SetParent(baseGo.transform);
        attachGo.transform.localPosition = new Vector3(0, 0, -0.05f); // aligned forward with grip
        attachGo.transform.localRotation = Quaternion.identity;
        grab.attachTransform = attachGo.transform;

        baseGo.AddComponent<ItemIdentity>();
        baseGo.AddComponent<ItemProbe>();
        baseGo.AddComponent<ItemOwnership>();

        AudioSource audio = baseGo.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.loop = false;
        audio.spatialBlend = 1.0f; // 3D Audio

        // Set renderer and material
        Renderer visualRenderer = visualGo.GetComponent<Renderer>();
        visualRenderer.sharedMaterial = itemBaseMat;

        // Clean up collider on child (move it to root for proper physics interaction)
        Collider childCollider = visualGo.GetComponent<Collider>();
        if (childCollider != null)
        {
            Object.DestroyImmediate(childCollider);
        }
        BoxCollider boxCollider = baseGo.AddComponent<BoxCollider>();
        boxCollider.size = Vector3.one * 0.15f;

        // Save base prefab
        string basePrefabPath = Path.Combine(prefabDir, "item.prefab");
        GameObject basePrefab = PrefabUtility.SaveAsPrefabAsset(baseGo, basePrefabPath);
        Object.DestroyImmediate(baseGo);

        Debug.Log("Base prefab created successfully at: " + basePrefabPath);

        // 6. Generate Variant Prefabs
        for (int i = 0; i < 9; i++)
        {
            ItemData data = itemDataArray[i];
            GameObject variantGo = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            variantGo.name = "Item_" + data.category + "_" + data.displayName;

            // Configure identity
            ItemIdentity identity = variantGo.GetComponent<ItemIdentity>();
            identity.ItemData = data;

            // Configure physics mass
            Rigidbody vRb = variantGo.GetComponent<Rigidbody>();
            vRb.mass = data.mass;

            // Set different mesh filters based on category/index to make them look unique
            GameObject vVisuals = variantGo.transform.Find("Visuals").gameObject;
            MeshFilter mf = vVisuals.GetComponent<MeshFilter>();
            BoxCollider col = variantGo.GetComponent<BoxCollider>();

            // Change visuals based on type
            if (data.category == ItemCategory.Sesli)
            {
                // Sphere
                GameObject sphereTemp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mf.sharedMesh = sphereTemp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(sphereTemp);
                vVisuals.transform.localScale = Vector3.one * 0.16f;

                // Update collider
                Object.DestroyImmediate(col);
                SphereCollider sphereCol = variantGo.AddComponent<SphereCollider>();
                sphereCol.radius = 0.08f;
            }
            else if (data.category == ItemCategory.Parlak)
            {
                // Cylinder
                GameObject cylTemp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mf.sharedMesh = cylTemp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(cylTemp);
                vVisuals.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f);

                // Update collider
                Object.DestroyImmediate(col);
                CapsuleCollider capCol = variantGo.AddComponent<CapsuleCollider>();
                capCol.radius = 0.06f;
                capCol.height = 0.16f;
                capCol.direction = 1; // Y-axis
            }
            else
            {
                // Cube (Heavy)
                vVisuals.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
                col.size = new Vector3(0.14f, 0.14f, 0.14f);
            }

            // Create a unique material instance so they look different colors in editor
            Material varMat = new Material(itemBaseMat);
            Color baseColor = Color.white;
            if (data.category == ItemCategory.Sesli) baseColor = new Color(0.8f, 0.5f, 0.2f); // bronze-like
            else if (data.category == ItemCategory.Parlak) baseColor = new Color(0.2f, 0.6f, 0.8f); // glowy teal base
            else baseColor = new Color(0.4f, 0.4f, 0.4f); // heavy steel grey

            varMat.color = baseColor;
            AssetDatabase.CreateAsset(varMat, $"Assets/_Project/Items/Material_{data.name}.mat");
            vVisuals.GetComponent<Renderer>().sharedMaterial = varMat;

            // Save variant prefab
            string varPrefabPath = Path.Combine(prefabDir, variantGo.name + ".prefab");
            PrefabUtility.SaveAsPrefabAsset(variantGo, varPrefabPath);
            Object.DestroyImmediate(variantGo);

            Debug.Log("Created variant prefab: " + varPrefabPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Asset and Prefab generation complete!");
    }

    private static ItemData CreateItemData(int id, string displayName, ItemCategory category, float mass, AudioClip rattleClip, Color glowColor, string directory)
    {
        ItemData data = ScriptableObject.CreateInstance<ItemData>();
        data.id = id;
        data.displayName = displayName;
        data.category = category;
        data.mass = mass;
        data.rattleClip = rattleClip;
        data.glowColor = glowColor;

        string assetPath = Path.Combine(directory, $"Data_{category}_{displayName}.asset");
        AssetDatabase.CreateAsset(data, assetPath);
        return data;
    }

    [MenuItem("Gece Vardiyasi/Setup Sandbox Scene")]
    public static void SetupSandboxScene()
    {
        Debug.Log("Setting up Sandbox scene...");

        // 1. Create/Open Sandbox Scene
        string scenePath = "Assets/_Project/Scenes/Sandbox_A_Items.unity";
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects, UnityEditor.SceneManagement.NewSceneMode.Single);

        // 2. Remove default Main Camera (we will use XR Origin)
        GameObject defaultCam = GameObject.Find("Main Camera");
        if (defaultCam != null)
        {
            Object.DestroyImmediate(defaultCam);
        }

        // 3. Instantiate XR Origin prefab from Starter Assets
        string originPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        GameObject originPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(originPrefabPath);
        if (originPrefab == null)
        {
            // Try template layout fallback
            originPrefabPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";
            originPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(originPrefabPath);
        }

        if (originPrefab != null)
        {
            GameObject originInstance = PrefabUtility.InstantiatePrefab(originPrefab) as GameObject;
            originInstance.name = "XR Origin";
            originInstance.transform.position = Vector3.zero;
            originInstance.transform.rotation = Quaternion.identity;

            // Setup PlayerRefs and Camera
            GameObject playerRefsGo = new GameObject("PlayerRefs");
            PlayerRefs playerRefs = playerRefsGo.AddComponent<PlayerRefs>();

            Camera xrCam = originInstance.GetComponentInChildren<Camera>();
            if (xrCam != null)
            {
                // Force tag to MainCamera
                xrCam.gameObject.tag = "MainCamera";
                // Assign to PlayerRefs
                var serializedRefs = new SerializedObject(playerRefs);
                serializedRefs.FindProperty("mainCamera").objectReferenceValue = xrCam;
                serializedRefs.ApplyModifiedProperties();
            }
        }
        else
        {
            Debug.LogError("Could not find any XR Origin prefab in starter assets or templates!");
        }

        // 4. Create Workbench (Height: 0.95m, Position: 0, 0, 0.8m)
        GameObject workbench = GameObject.CreatePrimitive(PrimitiveType.Cube);
        workbench.name = "Workbench";
        workbench.transform.position = new Vector3(0.0f, 0.95f / 2f, 0.7f); // Center of cube
        workbench.transform.localScale = new Vector3(2.0f, 0.95f, 0.8f);

        // Add a nice color to workbench
        Material benchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        benchMat.color = new Color(0.3f, 0.2f, 0.1f); // Wood brown
        AssetDatabase.CreateAsset(benchMat, "Assets/_Project/Items/Material_Workbench.mat");
        workbench.GetComponent<Renderer>().sharedMaterial = benchMat;

        // 5. Place the 9 variant items on the workbench
        string prefabDir = "Assets/_Project/Items/Prefabs";
        string[] prefabFiles = Directory.GetFiles(prefabDir, "Item_*.prefab");

        float startX = -0.6f;
        float stepX = 0.15f;
        int itemIndex = 0;

        foreach (string file in prefabFiles)
        {
            GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
            if (itemPrefab != null)
            {
                GameObject itemInstance = PrefabUtility.InstantiatePrefab(itemPrefab) as GameObject;
                // Place on table top (Y = 0.95m + offset)
                float x = startX + (itemIndex * stepX);
                float z = 0.65f + (itemIndex % 2 == 0 ? 0.05f : -0.05f); // staggered layout
                itemInstance.transform.position = new Vector3(x, 0.95f + 0.1f, z);
                itemInstance.transform.rotation = Quaternion.identity;
                itemIndex++;
            }
        }

        // Save Scene
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log("Sandbox scene configured and saved to: " + scenePath);
    }
}
