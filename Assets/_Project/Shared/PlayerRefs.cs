using UnityEngine;

public class PlayerRefs : MonoBehaviour
{
    public static PlayerRefs Instance { get; private set; }

    [SerializeField] private Camera mainCamera;
    [SerializeField] private Transform trackingOriginTransform;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;

    public Camera MainCamera => mainCamera;
    public Transform TrackingOriginTransform => trackingOriginTransform;
    public Transform HeadTransform => headTransform;
    public Transform LeftHandTransform => leftHandTransform;
    public Transform RightHandTransform => rightHandTransform;
    public bool HasTrackingReferences =>
        trackingOriginTransform != null && headTransform != null &&
        leftHandTransform != null && rightHandTransform != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ResolveMissingReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ResolveMissingReferences()
    {
        if (mainCamera == null)
            mainCamera = Camera.main != null
                ? Camera.main
                : FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

        if (headTransform == null && mainCamera != null)
            headTransform = mainCamera.transform;

        if (trackingOriginTransform == null)
        {
            trackingOriginTransform = FindSceneTransform("XR Origin Hands (XR Rig)");
            if (trackingOriginTransform == null && headTransform != null)
                trackingOriginTransform = FindTrackingOriginAncestor(headTransform);
        }

        if (leftHandTransform == null)
            leftHandTransform = FindSceneTransform("Left Controller");

        if (rightHandTransform == null)
            rightHandTransform = FindSceneTransform("Right Controller");

        if (!HasTrackingReferences)
        {
            Debug.LogWarning(
                $"[PlayerRefs] XR referanslari eksik. Origin={NameOf(trackingOriginTransform)}, " +
                $"Head={NameOf(headTransform)}, " +
                $"Left={NameOf(leftHandTransform)}, Right={NameOf(rightHandTransform)}",
                this);
            return;
        }

        Debug.Log(
            $"[PlayerRefs] XR referanslari hazir. Origin={trackingOriginTransform.name}, " +
            $"Head={headTransform.name}, " +
            $"Left={leftHandTransform.name}, Right={rightHandTransform.name}",
            this);
    }

    private static Transform FindSceneTransform(string objectName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Transform candidate in transforms)
        {
            if (candidate.name == objectName && candidate.gameObject.scene.IsValid())
                return candidate;
        }

        return null;
    }

    private static Transform FindTrackingOriginAncestor(Transform child)
    {
        Transform current = child;
        Transform highestSceneTransform = child;

        while (current != null)
        {
            highestSceneTransform = current;
            if (current.name.Contains("XR Origin"))
                return current;

            current = current.parent;
        }

        return highestSceneTransform;
    }

    private static string NameOf(Object value)
    {
        return value != null ? value.name : "<null>";
    }
}
