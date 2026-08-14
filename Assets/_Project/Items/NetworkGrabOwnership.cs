using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(RigidbodySynchronizable), typeof(XRGrabInteractable))]
public sealed class NetworkGrabOwnership : AttributesSync
{
    private static NetworkGrabOwnership locallyHeldItem;

    [SynchronizableField] private int holderIndex = -1;
    [SynchronizableField] private int lastInteractorIndex = -1;

    private RigidbodySynchronizable sync;
    private XRGrabInteractable grab;
    private Collider[] itemColliders;
    private bool waitingForOwnership;
    private bool locallyHeld;
    private bool localRigCollisionsIgnored;
    private int syncTick;
    private Coroutine releaseRoutine;

    public int LastInteractorIndex => lastInteractorIndex;

    private void Awake()
    {
        sync = GetComponent<RigidbodySynchronizable>();
        grab = GetComponent<XRGrabInteractable>();
        itemColliders = GetComponentsInChildren<Collider>(true);
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
        if (locallyHeldItem == this)
            locallyHeldItem = null;

        SetLocalRigCollisionsIgnored(false);
        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);

        if (Multiplayer != null)
        {
            Multiplayer.OnLockAcquired.RemoveListener(OnLockAcquired);
            Multiplayer.OnLockDenied.RemoveListener(OnLockDenied);
        }

        base.OnDestroy();
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        if (args.interactorObject is XRSocketInteractor)
            return;

        if (locallyHeldItem != null && locallyHeldItem != this)
        {
            if (grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

            Debug.Log("[NetGrab] Oyuncu zaten bir esya tutuyor; ikinci tutus reddedildi.", this);
            return;
        }

        locallyHeldItem = this;

        if (releaseRoutine != null)
        {
            StopCoroutine(releaseRoutine);
            releaseRoutine = null;
        }

        locallyHeld = true;
        SetLocalRigCollisionsIgnored(true);
        sync.WakeUp();

        if (!sync.HasOwnership)
        {
            waitingForOwnership = true;
            sync.TakeOwnership(true);
            return;
        }

        PublishHolder(Multiplayer.Me.Index);
        sync.SendData = true;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        if (args.interactorObject is XRSocketInteractor)
            return;

        locallyHeld = false;
        if (locallyHeldItem == this)
            locallyHeldItem = null;

        SetLocalRigCollisionsIgnored(false);

        if (!sync.HasOwnership)
            return;

        PublishHolder(-1);
        releaseRoutine = StartCoroutine(ReleaseOwnershipAfterPhysicsStep());
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
        lastInteractorIndex = Multiplayer.Me.Index;
        if (!locallyHeld)
        {
            PublishHolder(-1);
            releaseRoutine = StartCoroutine(ReleaseOwnershipAfterPhysicsStep());
            return;
        }

        PublishHolder(Multiplayer.Me.Index);
        sync.SendData = true;
        Debug.Log($"[NetGrab] Esya alindi. Oyuncu={holderIndex}", this);
    }

    private void OnLockDenied(LockDeniedEvent args)
    {
        if (!waitingForOwnership || args.UniqueID.UID != sync.GetUID())
            return;

        waitingForOwnership = false;
        locallyHeld = false;
        if (locallyHeldItem == this)
            locallyHeldItem = null;

        SetLocalRigCollisionsIgnored(false);
        if (grab.isSelected)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

        Debug.Log("[NetGrab] Esya baska oyuncuda; tutma reddedildi.", this);
    }

    private System.Collections.IEnumerator ReleaseOwnershipAfterPhysicsStep()
    {
        sync.isKinematic = false;
        sync.useGravity = true;
        sync.SyncSettings();
        sync.ForceUpdate(true);

        yield return new WaitForFixedUpdate();

        if (!locallyHeld && sync.HasOwnership)
        {
            sync.ForceUpdate(true);
            sync.ReleaseOwnership();
        }

        releaseRoutine = null;
    }

    private void PublishHolder(int index)
    {
        holderIndex = index;
        if (index >= 0)
            lastInteractorIndex = index;

        ForceSync();

        if (Multiplayer != null && Multiplayer.InRoom)
            BroadcastRemoteMethod(nameof(ReceiveHolder), index, lastInteractorIndex);
    }

    [SynchronizableMethod]
    private void ReceiveHolder(int index, int lastInteractor)
    {
        holderIndex = index;
        lastInteractorIndex = lastInteractor;
    }

    private void SetLocalRigCollisionsIgnored(bool ignored)
    {
        if (localRigCollisionsIgnored == ignored)
            return;

        PlayerRefs refs = PlayerRefs.Instance;
        if (refs == null || refs.TrackingOriginTransform == null)
            return;

        Collider[] rigColliders =
            refs.TrackingOriginTransform.GetComponentsInChildren<Collider>(true);

        foreach (Collider itemCollider in itemColliders)
        {
            if (itemCollider == null)
                continue;

            foreach (Collider rigCollider in rigColliders)
            {
                // Trigger collider'lar XR secimini yapar; yalnizca fiziksel govdeyi yok say.
                if (rigCollider == null || rigCollider.isTrigger)
                    continue;

                Physics.IgnoreCollision(itemCollider, rigCollider, ignored);
            }
        }

        localRigCollisionsIgnored = ignored;
    }
}
