using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(RigidbodySynchronizable), typeof(XRGrabInteractable))]
public sealed class NetworkGrabOwnership : AttributesSync
{
    [SynchronizableField] private int holderIndex = -1;

    private RigidbodySynchronizable sync;
    private XRGrabInteractable grab;
    private bool waitingForOwnership;
    private bool locallyHeld;
    private int syncTick;

    private void Awake()
    {
        sync = GetComponent<RigidbodySynchronizable>();
        grab = GetComponent<XRGrabInteractable>();
        sync.AllowCollisionToAssumeOwner = false;
        sync.SyncEveryNUpdates = 1;
        sync.FullSyncEveryNSync = 2;

        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
    }

    private void Start()
    {
        Multiplayer.OnLockAcquired.AddListener(OnLockAcquired);
        Multiplayer.OnLockDenied.AddListener(OnLockDenied);
    }

    public override void OnDestroy()
    {
        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);

        if (Multiplayer != null)
        {
            Multiplayer.OnLockAcquired.RemoveListener(OnLockAcquired);
            Multiplayer.OnLockDenied.RemoveListener(OnLockDenied);
        }

        base.OnDestroy();
    }

    private void OnGrabbed(SelectEnterEventArgs _)
    {
        locallyHeld = true;
        sync.WakeUp();

        if (!sync.HasOwnership)
        {
            waitingForOwnership = true;
            sync.TakeOwnership(true);
            return;
        }

        holderIndex = Multiplayer.Me.Index;
        sync.SendData = true;
        ForceSync();
    }

    private void OnReleased(SelectExitEventArgs _)
    {
        locallyHeld = false;
        waitingForOwnership = false;
        if (!sync.HasOwnership)
            return;

        sync.ForceUpdate(true);
        holderIndex = -1;
        ForceSync();
        sync.ReleaseOwnership();
    }

    private void Update()
    {
        bool heldByOther = holderIndex >= 0 && holderIndex != Multiplayer.Me.Index;
        if (grab.enabled == heldByOther)
            grab.enabled = !heldByOther;
    }

    private void FixedUpdate()
    {
        if (!locallyHeld || !sync.HasOwnership)
            return;

        sync.SendData = true;
        sync.ForceUpdate(++syncTick % 2 == 0);
    }

    private void OnLockAcquired(LockAcquiredEvent args)
    {
        if (!waitingForOwnership || args.UniqueID.UID != sync.GetUID())
            return;

        waitingForOwnership = false;
        holderIndex = Multiplayer.Me.Index;
        sync.SendData = true;
        ForceSync();
        Debug.Log($"[NetGrab] Esya alindi. Oyuncu={holderIndex}", this);
    }

    private void OnLockDenied(LockDeniedEvent args)
    {
        if (!waitingForOwnership || args.UniqueID.UID != sync.GetUID())
            return;

        waitingForOwnership = false;
        locallyHeld = false;
        if (grab.isSelected)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

        Debug.Log("[NetGrab] Esya baska oyuncuda; tutma reddedildi.", this);
    }
}
