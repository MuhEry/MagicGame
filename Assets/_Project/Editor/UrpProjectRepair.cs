using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Asset Store'dan gelen Multiplayer XR Template, Built-in/Standard materyallerle
/// birlikte proje render pipeline ayarlarini bos birakabiliyor. Bu arac URP'yi
/// yeniden etkinlestirir ve yalnizca template'in uyumsuz materyallerini donusturur.
/// </summary>
[InitializeOnLoad]
static class UrpProjectRepair
{
    const string k_QualityPipelinePath =
        "Assets/Settings/Project Configuration/Quality URP Config.asset";
    const string k_TemplateMaterialFolder =
        "Assets/Multiplayer XR Template/Materials";

    static UrpProjectRepair()
    {
        EditorApplication.delayCall += RepairAutomatically;
    }

    [MenuItem("Tools/Kayip Esya/URP Pembe Materyalleri Onar", false, 40)]
    public static void RepairAll()
    {
        RepairPipeline();
        int converted = ConvertTemplateMaterials();
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();

        Debug.Log($"[URP Repair] Pipeline baglandi; {converted} Standard materyal URP/Lit'e donusturuldu.");
    }

    static void RepairAutomatically()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        RepairAll();
    }

    static void RepairPipeline()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(k_QualityPipelinePath);
        if (pipeline == null)
        {
            Debug.LogError("[URP Repair] URP pipeline asset bulunamadi: " + k_QualityPipelinePath);
            return;
        }

        if (GraphicsSettings.defaultRenderPipeline != pipeline)
            GraphicsSettings.defaultRenderPipeline = pipeline;

        // Aktif kalite seviyesi Unity yeniden baslatilmadan da hemen URP'ye gecsin.
        if (QualitySettings.renderPipeline != pipeline)
            QualitySettings.renderPipeline = pipeline;
    }

    static int ConvertTemplateMaterials()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[URP Repair] Universal Render Pipeline/Lit shader bulunamadi.");
            return 0;
        }

        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { k_TemplateMaterialFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == urpLit)
                continue;

            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (shaderName != "Standard" && shaderName != "Hidden/InternalErrorShader")
                continue;

            Color baseColor = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;

            Texture baseTexture = material.HasProperty("_BaseMap")
                ? material.GetTexture("_BaseMap")
                : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;

            Vector2 textureScale = material.HasProperty("_MainTex")
                ? material.GetTextureScale("_MainTex")
                : Vector2.one;
            Vector2 textureOffset = material.HasProperty("_MainTex")
                ? material.GetTextureOffset("_MainTex")
                : Vector2.zero;

            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            float smoothness = material.HasProperty("_Smoothness")
                ? material.GetFloat("_Smoothness")
                : material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.5f;

            Color emission = material.HasProperty("_EmissionColor")
                ? material.GetColor("_EmissionColor")
                : Color.black;

            material.shader = urpLit;
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_Color", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetColor("_EmissionColor", emission);

            if (baseTexture != null)
            {
                material.SetTexture("_BaseMap", baseTexture);
                material.SetTextureScale("_BaseMap", textureScale);
                material.SetTextureOffset("_BaseMap", textureOffset);
            }

            if (emission.maxColorComponent > 0f)
                material.EnableKeyword("_EMISSION");
            else
                material.DisableKeyword("_EMISSION");

            EditorUtility.SetDirty(material);
            converted++;
        }

        return converted;
    }
}
