using System;
using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;

/// <summary>
/// Yerel oyun donguse ile Alteruna arasindaki host-otoriter kopru.
/// Odaya GERCEKTEN girilene kadar oyun cevrimdisi calismaya devam eder.
///
/// Mimari kural 1 ve 2 (sartname): skor/sure/kalan esya yalnizca ShiftManager'da tutulur,
/// her durum degisikligi tek bir metottan gecer. Bu sinif o metotlari aga tasir.
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
    int hostSeed;
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

    /// <summary>Teshis paneli icin: odadaki kullanici sayisi (odada degilken 0).</summary>
    public int UserCount { get; private set; }

    /// <summary>Teshis paneli icin: bu cihazin Alteruna kullanici indeksi.</summary>
    public int LocalUserIndex { get; private set; } = -1;

    /// <summary>Teshis paneli icin: Multiplayer bileseni bulunup abone olundu mu.</summary>
    public bool IsBridgeReady => subscribed;

    /// <summary>Vardiya boyunca esyalarin uretildigi seed. Host uretir, istemci alir.</summary>
    public int ActiveSeed => hostSeed;

    /// <summary>
    /// Bir esya tuketildiginde (dogru dolaba yerlesti) TUM istemcilerde tetiklenir.
    /// Static: dolaplar sahne yuklenirken koordinator henuz olusmamis olabilir,
    /// ornek referansina bagli abonelik sessizce kurulmuyordu.
    /// </summary>
    public static event Action<int> ItemConsumed;

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
        // CommunicationBridge.Multiplayer sahnedeki MultiplayerManager'dan cozulur.
        // Start() aninda henuz hazir degilse ESKI KOD BIR DAHA HIC DENEMIYORDU:
        // abonelik kurulmuyor, OnRoomJoined hic gelmiyor, IsInRoom sonsuza kadar
        // false kaliyor ve iki gozluk de sessizce CEVRIMDISI oynuyordu.
        if (!subscribed)
            Subscribe();

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

    /// <summary>
    /// Dogru karar sonrasi esyanin TUM istemcilerde ayni sekilde ortadan kalkmasini saglar.
    /// Yalnizca host yayinlar; istemciler ApplyItemConsumed uzerinden ayni isi yapar.
    /// Odada degilsek hicbir sey gonderilmez, cevrimdisi oyun etkilenmez.
    /// </summary>
    public void BroadcastItemConsumed(int itemId)
    {
        if (!IsInRoom || !IsHost)
            return;

        InvokeRemoteMethod(nameof(ApplyItemConsumed), UserId.All, itemId);
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
        hostSeed = Environment.TickCount ^ (hostSessionSequence * 397);

        ApplyStart(hostSessionSequence, hostSeed);
        InvokeRemoteMethod(nameof(ApplyStart), UserId.All, hostSessionSequence, hostSeed);
    }

    [SynchronizableMethod]
    void ApplyStart(int sessionSequence, int seed)
    {
        if (sessionSequence <= lastAppliedSession)
            return;

        lastAppliedSession = sessionSequence;
        lastAppliedDecision = 0;
        hostSeed = seed;
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

        // Iki istemci de ayni soket olayini gorebilir. Yalnizca es zamanli kopyayi
        // eliyoruz; reddedilen esya disari atildiktan sonra tekrar denenebilmeli.
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
            Debug.LogWarning($"[Network] Gecersiz karar istegi reddedildi. itemId={itemId}", this);
            return;
        }

        // Agdaki istemcinin bildirdigi dogru kategoriye guvenme. Karari her zaman
        // host'un gercekten urettigi nesnenin calisma zamani verisinden dogrula.
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
    void ApplyItemConsumed(int itemId)
    {
        ItemConsumed?.Invoke(itemId);
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
        RefreshRole();
        nextClockBroadcast = 0f;
        Debug.Log($"[Network] Odaya katilindi. Rol: {(IsHost ? "HOST" : "ISTEMCI")}, " +
                  $"kullanici={UserCount}, indeks={LocalUserIndex}", this);
    }

    void HandleRoomLeft(RoomLeftEvent _)
    {
        IsInRoom = false;
        IsHost = false;
        UserCount = 0;
        Debug.Log("[Network] Odadan cikildi, oyun cevrimdisi moda dondu.", this);
    }

    void HandleOtherUserJoined(OtherUserJoinedEvent args)
    {
        RefreshRole();
        Debug.Log($"[Network] Baska bir oyuncu katildi. Toplam kullanici={UserCount}", this);

        // Vardiya devam ederken katilan oyuncu bos ekranda kalmasin: host,
        // acik oturumu ve saati yeni gelene de yayinlar. ApplyStart zaten
        // sessionSequence korumasi tasidigi icin mevcut oyunculara zarar vermez.
        if (!IsHost || shiftManager == null || shiftManager.State != ShiftState.Vardiya)
            return;

        InvokeRemoteMethod(nameof(ApplyStart), UserId.All, hostSessionSequence, hostSeed);
        InvokeRemoteMethod(nameof(ApplyClock), UserId.All, shiftManager.RemainingSeconds);
    }

    void HandleOtherUserLeft(OtherUserLeftEvent args)
    {
        RefreshRole();
    }

    /// <summary>
    /// Rolu her zaman Multiplayer bileseninden okur. Olay argumanindaki controller
    /// tipine bagimli kalmiyoruz; host dusup baskasi devraldiginda da dogru sonuc verir.
    /// </summary>
    void RefreshRole()
    {
        if (Multiplayer == null)
            return;

        LocalUserIndex = Multiplayer.Me != null ? Multiplayer.Me.Index : -1;
        IsHost = LocalUserIndex >= 0 && LocalUserIndex == Multiplayer.LowestUserIndex;
        // Room.GetUserCount() 2.1'de obsolete; CurrentUsers ayni degeri verir.
        UserCount = Multiplayer.CurrentRoom != null ? Multiplayer.CurrentRoom.CurrentUsers : 0;
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

        if (shiftManager == null)
            ResolveReferences();

        if (shiftManager != null)
            shiftManager.OnStateChanged += HandleShiftStateChanged;

        subscribed = true;
        Debug.Log("[Network] Alteruna kopru baglandi, oda olaylari dinleniyor.", this);
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
