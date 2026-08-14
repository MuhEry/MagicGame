using Alteruna.Multiplayer.Core;
using UnityEngine;

[RequireComponent(typeof(Alteruna.Multiplayer.Unity.Avatar))]
public sealed class AvatarRigBinder : MonoBehaviour
{
    [Header("Ag avatarinin takip noktaları")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform handL;
    [SerializeField] private Transform handR;

    private Alteruna.Multiplayer.Unity.Avatar avatar;
    private PlayerRefs playerRefs;
    private bool isLocalAvatar;
    private float nextResolveAttempt;

    private void Awake()
    {
        avatar = GetComponent<Alteruna.Multiplayer.Unity.Avatar>();
        ResolveAvatarTargets();
        DisableAvatarColliders();
        avatar.OnPossessed.AddListener(HandlePossessed);
    }

    private void Start()
    {
        // Normalde OnPossessed, Start'tan sonra gelir. ForceSync veya editor testinde
        // avatar daha erken sahiplenildiyse yerel/uzak durumunu burada da yakala.
        if (avatar.IsPossessed)
            HandlePossessed(avatar.Possessor);
    }

    private void OnDestroy()
    {
        if (avatar != null)
            avatar.OnPossessed.RemoveListener(HandlePossessed);
    }

    private void HandlePossessed(User user)
    {
        isLocalAvatar = avatar.IsMe;
        SetVisualsVisible(!isLocalAvatar);

        if (!isLocalAvatar)
        {
            Debug.Log($"[Avatar] Uzak avatar hazir. User={user?.Name}", this);
            return;
        }

        ResolvePlayerRefs();
        if (playerRefs != null && playerRefs.TrackingOriginTransform != null)
        {
            playerRefs.TrackingOriginTransform.SetPositionAndRotation(
                transform.position,
                transform.rotation);
        }

        Debug.Log(
            $"[Avatar] Yerel avatar XR rig'e baglandi. User={user?.Name} " +
            $"RefsReady={playerRefs != null && playerRefs.HasTrackingReferences}",
            this);
    }

    private void LateUpdate()
    {
        if (!isLocalAvatar)
            return;

        if (playerRefs == null || !playerRefs.HasTrackingReferences)
        {
            if (Time.unscaledTime < nextResolveAttempt)
                return;

            nextResolveAttempt = Time.unscaledTime + 1f;
            ResolvePlayerRefs();
            if (playerRefs == null || !playerRefs.HasTrackingReferences)
                return;
        }

        Transform trackingOrigin = playerRefs.TrackingOriginTransform;
        float yaw = trackingOrigin.eulerAngles.y;
        transform.SetPositionAndRotation(
            trackingOrigin.position,
            Quaternion.Euler(0f, yaw, 0f));

        ApplyTrackedPose(head, playerRefs.HeadTransform);
        ApplyTrackedPose(handL, playerRefs.LeftHandTransform);
        ApplyTrackedPose(handR, playerRefs.RightHandTransform);
    }

    private void ResolvePlayerRefs()
    {
        playerRefs = PlayerRefs.Instance;
        if (playerRefs == null)
            playerRefs = FindFirstObjectByType<PlayerRefs>(FindObjectsInactive.Include);

        playerRefs?.ResolveMissingReferences();
    }

    private void ResolveAvatarTargets()
    {
        if (head == null)
            head = transform.Find("Head");
        if (handL == null)
            handL = transform.Find("HandL");
        if (handR == null)
            handR = transform.Find("HandR");

        if (head == null || handL == null || handR == null)
        {
            Debug.LogError(
                $"[Avatar] Avatar hedefleri eksik. Head={NameOf(head)}, " +
                $"HandL={NameOf(handL)}, HandR={NameOf(handR)}",
                this);
            enabled = false;
        }
    }

    private void ApplyTrackedPose(Transform target, Transform source)
    {
        Transform trackingOrigin = playerRefs.TrackingOriginTransform;
        Vector3 trackingLocalPosition = trackingOrigin.InverseTransformPoint(source.position);
        Quaternion trackingLocalRotation =
            Quaternion.Inverse(trackingOrigin.rotation) * source.rotation;

        target.SetPositionAndRotation(
            transform.TransformPoint(trackingLocalPosition),
            transform.rotation * trackingLocalRotation);
    }

    private void DisableAvatarColliders()
    {
        foreach (Collider avatarCollider in GetComponentsInChildren<Collider>(true))
            avatarCollider.enabled = false;
    }

    private void SetVisualsVisible(bool visible)
    {
        foreach (Renderer avatarRenderer in GetComponentsInChildren<Renderer>(true))
            avatarRenderer.enabled = visible;
    }

    private static string NameOf(Object value)
    {
        return value != null ? value.name : "<null>";
    }
}
