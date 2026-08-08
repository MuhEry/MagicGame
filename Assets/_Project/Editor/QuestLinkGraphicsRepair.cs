#if UNITY_EDITOR_WIN
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Quest Link + OpenXR Editor testi icin Windows'u kararlı D3D11 yolunda tutar.
/// Unity 6'nin otomatik secimi bu makinede Editor'u D3D12 ile aciyordu ve XR
/// goruntu/input basladiktan hemen sonra native tarafta donmaya yol aciyordu.
/// </summary>
[InitializeOnLoad]
public static class QuestLinkGraphicsRepair
{
    const string MenuPath = "Tools/Gece Vardiyasi/Quest Link icin Windows DX11 Yap";

    static QuestLinkGraphicsRepair()
    {
        EditorApplication.delayCall += EnsureD3D11;
    }

    [MenuItem(MenuPath, false, 42)]
    public static void EnsureD3D11()
    {
        const BuildTarget target = BuildTarget.StandaloneWindows64;
        GraphicsDeviceType[] configured = PlayerSettings.GetGraphicsAPIs(target);
        bool alreadyD3D11Only =
            !PlayerSettings.GetUseDefaultGraphicsAPIs(target) &&
            configured.Length == 1 &&
            configured[0] == GraphicsDeviceType.Direct3D11;

        if (alreadyD3D11Only)
            return;

        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(
            target,
            new[] { GraphicsDeviceType.Direct3D11 });
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[Quest Link DX11] Windows grafik API'si Direct3D11'e sabitlendi. " +
            "Degisikligin Editor'e uygulanmasi icin Unity yeniden acilmali.");
    }
}
#endif
