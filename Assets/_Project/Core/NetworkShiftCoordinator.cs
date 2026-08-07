using System;
using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;

/// <summary>
/// Host-authoritative bridge between the local game loop and Alteruna.
/// Offline play remains available until a room has actually been joined.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkShiftCoordinator : AttributesSync
{
    public static NetworkShiftCoordinator Instance { get; private set; }

    [SerializeField] ShiftManager shiftManager;
    [SerializeField] ItemSpawner itemSpawner;
    [SerializeField, Min(0.1f)] float clockBroadcastInterval = 0.5f;
    [SerializeField, Min(0.1f)] float duplicateDecisionWindow = 0.65f;

    int hostSessionSequence;
    int hostDecisionSequence;
    int lastAppliedSession;
    int lastAppliedDecision;
    int lastRequestedItemId = int.MinValue;
    ItemCategory lastRequestedChoice;
    float lastRequestTime = float.NegativeInfinity;
    float nextClockBroadcast;
    bool subscribed;

    public bool IsInRoom { get; private set; }
    public bool IsHost { get; private set; }
    public bool HasAuthority => !IsInRoom || IsHost;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    void Start()
    {
        Subscribe();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        Unsubscribe();
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!IsInRoom || !IsHost || shiftManager == null ||
            shiftManager.State != ShiftState.Vardiya || Time.unscaledTime < nextClockBroadcast)
            return;

        nextClockBroadcast = Time.unscaledTime + clockBroadcastInterval;
        InvokeRemoteMethod(nameof(ApplyClock), UserId.All, shiftManager.RemainingSeconds);
    }

    public void RequestStartShift()
    {
        ResolveReferences();

        if (!IsInRoom)
        {
            shiftManager?.StartShiftOffline();
            return;
        }

        if (IsHost)
            BeginAuthoritativeShift();
        else
            InvokeRemoteMethod(nameof(ReceiveStartRequest), UserId.All);
    }

    public void SubmitDecision(int itemId, ItemCategory reportedCorrect, ItemCategory chosen,
        float inspectMs, int shakeCount)
    {
        if (!IsInRoom)
        {
            shiftManager?.ApplyDecisionFromNetwork(itemId, reportedCorrect, chosen, inspectMs, shakeCount);
            return;
        }

        if (IsHost)
            ProcessDecisionRequest(itemId, reportedCorrect, chosen, inspectMs, shakeCount);
        else
            InvokeRemoteMethod(nameof(ReceiveDecisionRequest), UserId.All,
                itemId, (int)reportedCorrect, (int)chosen, inspectMs, shakeCount);
    }

    public void HostLanSession()
    {
        Multiplayer.Host();
    }

    public void JoinLanSession(string hostAddress)
    {
        Multiplayer.DirectConnect(string.IsNullOrWhiteSpace(hostAddress) ? "localhost" : hostAddress.Trim());
    }

    public void JoinFirstAvailableRoom()
    {
        Multiplayer.JoinFirstAvailable();
    }

    [SynchronizableMethod]
    void ReceiveStartRequest()
    {
        if (IsHost)
            BeginAuthoritativeShift();
    }

    void BeginAuthoritativeShift()
    {
        hostSessionSequence++;
        int seed = Environment.TickCount ^ (hostSessionSequence * 397);

        ApplyStart(hostSessionSequence, seed);
        InvokeRemoteMethod(nameof(ApplyStart), UserId.All, hostSessionSequence, seed);
    }

    [SynchronizableMethod]
    void ApplyStart(int sessionSequence, int seed)
    {
        if (sessionSequence <= lastAppliedSession)
            return;

        lastAppliedSession = sessionSequence;
        lastAppliedDecision = 0;
        ResolveReferences();
        shiftManager?.StartShiftFromNetwork(seed, IsHost);
    }

    [SynchronizableMethod]
    void ReceiveDecisionRequest(int itemId, int reportedCorrect, int chosen, float inspectMs, int shakeCount)
    {
        if (IsHost)
            ProcessDecisionRequest(itemId, (ItemCategory)reportedCorrect, (ItemCategory)chosen, inspectMs, shakeCount);
    }

    void ProcessDecisionRequest(int itemId, ItemCategory reportedCorrect, ItemCategory chosen,
        float inspectMs, int shakeCount)
    {
        if (!IsHost || shiftManager == null || shiftManager.State != ShiftState.Vardiya)
            return;

        // Both clients can observe the same socket event. Collapse only the near-simultaneous duplicate;
        // a rejected item can still be tried again after it has been ejected.
        if (itemId == lastRequestedItemId && chosen == lastRequestedChoice &&
            Time.unscaledTime - lastRequestTime < duplicateDecisionWindow)
            return;

        lastRequestedItemId = itemId;
        lastRequestedChoice = chosen;
        lastRequestTime = Time.unscaledTime;

        GameObject current = itemSpawner != null ? itemSpawner.CurrentSpawnedItem : null;
        ItemIdentity identity = current != null ? current.GetComponentInChildren<ItemIdentity>() : null;
        if (identity == null || identity.ItemData == null || identity.ItemId != itemId)
        {
            Debug.LogWarning($"[Network] Geçersiz karar isteği reddedildi. itemId={itemId}", this);
            return;
        }

        // Ağdaki istemcinin bildirdiği doğru kategoriye güvenme. Kararı her zaman
        // host'un gerçekten ürettiği nesnenin çalışma zamanı verisinden doğrula.
        ItemCategory verifiedCorrect = identity.ItemData.category;

        hostDecisionSequence++;
        ApplyDecision(hostDecisionSequence, itemId, (int)verifiedCorrect, (int)chosen, inspectMs, shakeCount);
        InvokeRemoteMethod(nameof(ApplyDecision), UserId.All, hostDecisionSequence,
            itemId, (int)verifiedCorrect, (int)chosen, inspectMs, shakeCount);
    }

    [SynchronizableMethod]
    void ApplyDecision(int decisionSequence, int itemId, int correct, int chosen,
        float inspectMs, int shakeCount)
    {
        if (decisionSequence <= lastAppliedDecision)
            return;

        lastAppliedDecision = decisionSequence;
        ResolveReferences();
        shiftManager?.ApplyDecisionFromNetwork(itemId, (ItemCategory)correct,
            (ItemCategory)chosen, inspectMs, shakeCount);
    }

    [SynchronizableMethod]
    void ApplyClock(float remainingSeconds)
    {
        if (!IsHost)
            shiftManager?.ApplyNetworkClock(remainingSeconds);
    }

    [SynchronizableMethod]
    void ApplyShiftEnd(int sessionSequence)
    {
        if (sessionSequence == lastAppliedSession)
            shiftManager?.EndShiftFromNetwork();
    }

    void HandleRoomJoined(RoomJoinedEvent args)
    {
        IsInRoom = true;
        IsHost = args.Controller.Me.Index == args.Controller.LowestUserIndex;
        nextClockBroadcast = 0f;
        Debug.Log($"[Network] Odaya katılındı. Rol: {(IsHost ? "HOST" : "CLIENT")}", this);
    }

    void HandleRoomLeft(RoomLeftEvent _)
    {
        IsInRoom = false;
        IsHost = false;
    }

    void HandleOtherUserJoined(OtherUserJoinedEvent args)
    {
        IsHost = args.Controller.Me.Index == args.Controller.LowestUserIndex;
    }

    void HandleOtherUserLeft(OtherUserLeftEvent args)
    {
        IsHost = args.Controller.Me.Index == args.Controller.LowestUserIndex;
    }

    void HandleShiftStateChanged(ShiftState state)
    {
        if (!IsInRoom || !IsHost || state != ShiftState.Rapor)
            return;

        InvokeRemoteMethod(nameof(ApplyShiftEnd), UserId.All, lastAppliedSession);
    }

    void Subscribe()
    {
        if (subscribed || Multiplayer == null)
            return;

        Multiplayer.OnRoomJoined.AddListener(HandleRoomJoined);
        Multiplayer.OnRoomLeft.AddListener(HandleRoomLeft);
        Multiplayer.OnOtherUserJoined.AddListener(HandleOtherUserJoined);
        Multiplayer.OnOtherUserLeft.AddListener(HandleOtherUserLeft);
        if (shiftManager != null)
            shiftManager.OnStateChanged += HandleShiftStateChanged;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (Multiplayer != null)
        {
            Multiplayer.OnRoomJoined.RemoveListener(HandleRoomJoined);
            Multiplayer.OnRoomLeft.RemoveListener(HandleRoomLeft);
            Multiplayer.OnOtherUserJoined.RemoveListener(HandleOtherUserJoined);
            Multiplayer.OnOtherUserLeft.RemoveListener(HandleOtherUserLeft);
        }

        if (shiftManager != null)
            shiftManager.OnStateChanged -= HandleShiftStateChanged;
        subscribed = false;
    }

    void ResolveReferences()
    {
        if (shiftManager == null)
            shiftManager = FindFirstObjectByType<ShiftManager>();
        if (itemSpawner == null)
            itemSpawner = FindFirstObjectByType<ItemSpawner>();
    }
}
