#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Controller-only Quest surumunde, onceki artimli build'lerden veya opsiyonel
/// XR Hands paketinden kalabilen el takibi manifest girdilerini son asamada siler.
/// </summary>
public sealed class ControllerOnlyAndroidManifest : IPostGenerateGradleAndroidProject
{
    const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    const string HandPermission = "com.oculus.permission.HAND_TRACKING";
    const string HandFeature = "oculus.software.handtracking";

    // XR Hands'in manifest hook'u callbackOrder=10. Bunun kesinlikle ardindan calis.
    public int callbackOrder => 10000;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = FindManifest(path);
        if (manifestPath == null)
            throw new FileNotFoundException(
                "Controller-only Quest manifesti bulunamadi.", path);

        var document = new XmlDocument();
        document.PreserveWhitespace = true;
        document.Load(manifestPath);

        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("android", AndroidNamespace);

        int removed = 0;
        removed += RemoveNodes(document, namespaces,
            $"/manifest/uses-permission[@android:name='{HandPermission}']");
        removed += RemoveNodes(document, namespaces,
            $"/manifest/uses-feature[@android:name='{HandFeature}']");

        if (removed > 0)
        {
            document.Save(manifestPath);
            Debug.Log(
                $"[Quest Build] Controller-only manifest temizlendi; " +
                $"{removed} el takibi girdisi kaldirildi: {manifestPath}");
        }
    }

    static string FindManifest(string buildPath)
    {
        string direct = Path.Combine(buildPath, "src", "main", "AndroidManifest.xml");
        if (File.Exists(direct))
            return direct;

        string unityLibrary = Path.Combine(
            buildPath, "unityLibrary", "src", "main", "AndroidManifest.xml");
        return File.Exists(unityLibrary) ? unityLibrary : null;
    }

    static int RemoveNodes(
        XmlDocument document,
        XmlNamespaceManager namespaces,
        string xpath)
    {
        XmlNodeList nodes = document.SelectNodes(xpath, namespaces);
        if (nodes == null)
            return 0;

        int removed = 0;
        foreach (XmlNode node in nodes)
        {
            if (node.ParentNode == null)
                continue;

            node.ParentNode.RemoveChild(node);
            removed++;
        }

        return removed;
    }
}
#endif
