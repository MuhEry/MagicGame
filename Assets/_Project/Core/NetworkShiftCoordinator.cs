using System;
using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;

/// <summary>
/// Yerel oyun dongusu ile Alteruna arasindaki HOST-OTORITER kopru.
///
/// Tasarim kurali: ODAYA GIRILMEDIGI SURECE BU SINIF HICBIR SEY YAPMAZ.
/// Faz 1 akisi (tek oyuncu) birebir korunur; her ag cagrisi <see cref="IsInRoom"/>
/// ile korumalidir. Sahnede bu bilesen hic yoksa da oyun calisir - ShiftManager
/// koordinator bulamazsa dogrudan cevrimdisi yola girer.
///
/// Sartnamedeki mimari kurallar:
///   1. Oyun durumu yalnizca ShiftManager'da tutulur -> burada kopya durum YOK,
///      sadece "hangi paketi uyguladik" sayaclari var.
///   2. Her durum degisikligi tek bir metottan gecer -> RegisterDecision.
///      Bu sinif o metodun ag karsiligini (ApplyDecision) saglar.
///   4. Rastgelelik seed'li: host kendi seed'ini ApplyStart ile istemciye gecirir.
///
/// SDK NOTU: Alteruna Multiplayer SDK 2.1.1003'e gore yazildi.
///   - Oda (SADECE LAN): Multiplayer.Host() / CreateRoom() / JoinLan() / DirectConnect(ip)
///     JoinFirstAvailable() BULUT oda listesini kullanir - bu projede KULLANILMIYOR.
///   - RPC: InvokeRemoteMethod(nameof(X), UserId.All, ...) + [SynchronizableMethod]
///   - Rol: Me.Index == LowestUserIndex  (ucretsiz katman 2 oyuncu, host = dusuk indeks)
/// Baska bir SDK surumune gecerken bu dort satir dogrulanmali.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Kayip Esya/Network Shift Coordinator")]
public sealed class NetworkShiftCoordinator : AttributesSync
{
    public static NetworkShiftCoordinator Instance { get; private set; }

    [Header("Baglar (bos birakilirsa sahnede aranir)")]
    [SerializeField] ShiftManager m_ShiftManager;
    [SerializeField] ItemSpawner m_ItemSpawner;

    [Header("Ag")]
    [Tooltip("Host'un kalan sureyi istemciye kac saniyede bir yayinlayacagi.")]
    [SerializeField, Min(0.1f)] float m_ClockBroadcastInterval = 0.5f;

    [Tooltip("Ayni esya + ayni dolap icin bu sure icinde gelen ikinci karar yok sayilir.")]
    [SerializeField, Min(0f)] float m_DuplicateDecisionWindow = 0.65f;

    // --- host'un urettigi diziler. Istemci bunlari yalnizca OKUR. ---
    int m_HostSessionSequence;
    int m_HostDecisionSequence;
    int m_HostSeed;

    // --- her iki tarafta da "bu paketi zaten uyguladik mi" korumasi ---
    int m_LastAppliedSession;
    int m_LastAppliedDecision;

    int m_LastRequestedItemId = int.MinValue;
    ItemCategory m_LastRequestedChoice;
    float m_LastRequestTime = float.NegativeInfinity;
    float m_NextClockBroadcast;
    bool m_Subscribed;

    /// <summary>Gercekten bir odadayiz. False ise TUM ag yollari kapalidir.</summary>
    public bool IsInRoom { get; private set; }

    /// <summary>Bu cihaz odanin en dusuk indeksli kullanicisi mi (= host).</summary>
    public bool IsHost { get; private set; }

    /// <summary>Cevrimdisiyken de true doner: tek oyuncu kendi otoritesidir.</summary>
    public bool HasAuthority => !IsInRoom || IsHost;

    /// <summary>Teshis paneli icin: odadaki kullanici sayisi (odada degilken 0).</summary>
    public int UserCount { get; private set; }

    /// <summary>Teshis paneli icin: bu cihazin Alteruna kullanici indeksi. -1 = yok.</summary>
    public int LocalUserIndex { get; private set; } = -1;

    /// <summary>Teshis paneli icin: MultiplayerManager bulunup abone olundu mu.</summary>
    public bool IsBridgeReady => m_Subscribed;

    /// <summary>Vardiyada esyalarin uretildigi seed. Host uretir, istemci alir.</summary>
    public int ActiveSeed => m_HostSeed;

    /// <summary>
    /// Bir esya tuketildiginde (dogru dolaba girdi) TUM istemcilerde tetiklenir.
    ///
    /// STATIC olmasinin sebebi: dolaplar sahne yuklenirken OnEnable'da abone olur,
    /// koordinator o an henuz Awake olmamis olabilir. Ornek referansina bagli
    /// abonelik bu yuzden sessizce hic kurulmuyordu.
    /// </summary>
    public static event Action<int> ItemConsumed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Network] Sahnede birden fazla NetworkShiftCoordinator var; fazlasi kaldirildi.", this);
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    // DIKKAT: base sinifin Unity mesajlari GIZLENMEZ, override edilir.
    // base.OnEnable() cagrilmazsa CommunicationBridgeUID kendini Multiplayer'a
    // KAYDETMEZ ve bu bilesenin butun RPC'leri sessizce hic calismaz.
    public override void OnEnable()
    {
        base.OnEnable();
        TrySubscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
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
        // Multiplayer, sahnedeki MultiplayerManager'dan cozulur ve ilk karede
        // hazir olmayabilir. Tek seferlik denenirse abonelik hic kurulmaz,
        // OnRoomJoined hic gelmez ve iki gozluk de sessizce CEVRIMDISI oynar.
        if (!m_Subscribed)
            TrySubscribe();

        if (!IsInRoom || !IsHost || m_ShiftManager == null ||
            m_ShiftManager.State != ShiftState.Vardiya ||
            Time.unscaledTime < m_NextClockBroadcast)
            return;

        m_NextClockBroadcast = Time.unscaledTime + m_ClockBroadcastInterval;
        InvokeRemoteMethod(nameof(ApplyClock), UserId.All, m_ShiftManager.RemainingSeconds);
    }

    // ------------------------------------------------------------------ oda

    /// <summary>
    /// LAN'da oda ac (Gozluk A). HUD butonuna baglanabilir.
    ///
    /// SADECE LAN: bulut/oda-listesi yoluna HIC girmiyoruz. Bulut yolu lisans
    /// dogrulamasi + oda listesi tazeleme + sunucu gecikmesi demek; iki gozluk
    /// ayni odadayken bunlarin hepsi gereksiz risk. Calisan bir LAN kurulumundan
    /// buluta gecmek kolaydir, tersi degildir.
    /// </summary>
    public void HostLanSession()
    {
        if (!IsMultiplayerUsable("oda acilamiyor"))
            return;

        // RoomMenu ile ayni akis: servis zaten ayaktaysa yalnizca oda kur.
        if (Multiplayer.IsConnected)
            Multiplayer.CreateRoom();
        else
            Multiplayer.Host();

        Debug.Log("[Network] Host istegi gonderildi (LAN).", this);
    }

    /// <summary>
    /// LAN'daki odaya katil (Gozluk B). HUD butonuna baglanabilir.
    ///
    /// DIKKAT: <c>JoinFirstAvailable()</c> DEGIL. O cagri bulut oda listesini
    /// kullanir; lisans/internet gerektirir ve iki gozluklu LAN testinde
    /// "connecting" durumunda takilir. LAN kesfi icin dogru cagri JoinLan().
    /// </summary>
    public void JoinLanSession()
    {
        if (!IsMultiplayerUsable("odaya katilinamiyor"))
            return;

        Multiplayer.JoinLan();
        Debug.Log("[Network] JoinLan istegi gonderildi (UDP yayini dinleniyor).", this);
    }

    /// <summary>
    /// LAN kesfi calismiyorsa (kurum agi / AP izolasyonu) host'un IP'siyle
    /// dogrudan baglan. Pratikte en saglam yedek: telefon hotspot'u + bu cagri.
    /// </summary>
    public void JoinDirect(string hostAddress)
    {
        if (!IsMultiplayerUsable("baglanilamiyor"))
            return;

        string address = string.IsNullOrWhiteSpace(hostAddress) ? "localhost" : hostAddress.Trim();
        Multiplayer.DirectConnect(address);
        Debug.Log($"[Network] DirectConnect -> {address}", this);
    }

    bool IsMultiplayerUsable(string what)
    {
        if (Multiplayer == null)
        {
            Debug.LogError($"[Network] Sahnede MultiplayerManager yok, {what}.", this);
            return false;
        }

        // Servis daha ayaga kalkmadiysa cagri sessizce dusebilir. Engellemiyoruz
        // (Host/JoinLan servisi kendisi baslatir) ama durumu gorunur kiliyoruz.
        if (!Multiplayer.Started)
            Debug.LogWarning("[Network] Alteruna servisi henuz baslamadi; istek yine de gonderiliyor.", this);

        return true;
    }

    /// <summary>Odadan cik; oyun cevrimdisi moda doner.</summary>
    public void LeaveRoom()
    {
        Multiplayer?.CurrentRoom?.Leave();
    }

    // -------------------------------------------------------------- vardiya

    /// <summary>
    /// ShiftManager.StartShift odadayken buraya yonlenir. Istemci yalnizca ISTER,
    /// vardiyayi her zaman host baslatir - iki cihazda iki farkli seed olmasin diye.
    /// </summary>
    public void RequestStartShift()
    {
        ResolveReferences();

        if (!IsInRoom)
        {
            m_ShiftManager?.StartShiftOffline();
            return;
        }

        if (IsHost)
            BeginAuthoritativeShift();
        else
            InvokeRemoteMethod(nameof(ReceiveStartRequest), UserId.All);
    }

    /// <summary>
    /// ShiftManager.RegisterDecision odadayken buraya yonlenir. Skoru HOST tutar,
    /// istemci yalnizca gosterir (sartname Faz 2 maddesi 4).
    /// </summary>
    public void SubmitDecision(int itemId, ItemCategory reportedCorrect, ItemCategory chosen,
        float inspectMs, int shakeCount)
    {
        if (!IsInRoom)
        {
            m_ShiftManager?.ApplyDecisionFromNetwork(itemId, reportedCorrect, chosen, inspectMs, shakeCount);
            return;
        }

        if (IsHost)
            ProcessDecisionRequest(itemId, reportedCorrect, chosen, inspectMs, shakeCount);
        else
            InvokeRemoteMethod(nameof(ReceiveDecisionRequest), UserId.All,
                itemId, (int)reportedCorrect, (int)chosen, inspectMs, shakeCount);
    }

    /// <summary>
    /// Dogru karardan sonra esyanin HER iki gozlukte de ayni sekilde kaybolmasini saglar.
    /// Yalnizca host yayinlar. Cevrimdisi oyunda ve istemcide sessizce hicbir sey yapmaz.
    /// </summary>
    public void BroadcastItemConsumed(int itemId)
    {
        if (!IsInRoom || !IsHost)
            return;

        InvokeRemoteMethod(nameof(ApplyItemConsumed), UserId.All, itemId);
    }

    // ------------------------------------------------------------------ RPC

    [SynchronizableMethod]
    void ReceiveStartRequest()
    {
        if (IsHost)
            BeginAuthoritativeShift();
    }

    void BeginAuthoritativeShift()
    {
        m_HostSessionSequence++;

        // UnityEngine.Random KULLANILMAZ (mimari kural 4). Tohum TickCount'tan
        // gelir, uretim System.Random ile deterministiktir; ayni seed -> ayni sira.
        m_HostSeed = Environment.TickCount ^ (m_HostSessionSequence * 397);

        ApplyStart(m_HostSessionSequence, m_HostSeed);
        InvokeRemoteMethod(nameof(ApplyStart), UserId.All, m_HostSessionSequence, m_HostSeed);
    }

    [SynchronizableMethod]
    void ApplyStart(int sessionSequence, int seed)
    {
        // Gec gelen / tekrar eden paketler eski bir vardiyayi yeniden baslatmasin.
        if (sessionSequence <= m_LastAppliedSession)
            return;

        m_LastAppliedSession = sessionSequence;
        m_LastAppliedDecision = 0;
        m_HostSeed = seed;

        ResolveReferences();
        m_ShiftManager?.StartShiftFromNetwork(seed, IsHost);
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
        if (!IsHost || m_ShiftManager == null || m_ShiftManager.State != ShiftState.Vardiya)
            return;

        // Iki cihaz da ayni soket olayini gorebilir. Yalnizca ES ZAMANLI kopyayi
        // eliyoruz; reddedilen esya disari atildiktan sonra tekrar denenebilmeli.
        if (itemId == m_LastRequestedItemId && chosen == m_LastRequestedChoice &&
            Time.unscaledTime - m_LastRequestTime < m_DuplicateDecisionWindow)
            return;

        m_LastRequestedItemId = itemId;
        m_LastRequestedChoice = chosen;
        m_LastRequestTime = Time.unscaledTime;

        // Istemcinin bildirdigi "dogru kategori"ye GUVENME. Karari her zaman host'un
        // kendi sahnesindeki nesnenin verisinden dogrula; yoksa istemci skoru uydurabilir.
        ItemCategory verifiedCorrect = reportedCorrect;
        GameObject current = m_ItemSpawner != null ? m_ItemSpawner.CurrentSpawnedItem : null;
        if (current != null)
        {
            ItemIdentity identity = current.GetComponentInChildren<ItemIdentity>(true);
            if (identity != null && identity.ItemData != null && identity.ItemId == itemId)
                verifiedCorrect = identity.ItemData.category;
        }

        m_HostDecisionSequence++;
        ApplyDecision(m_HostDecisionSequence, itemId, (int)verifiedCorrect, (int)chosen, inspectMs, shakeCount);
        InvokeRemoteMethod(nameof(ApplyDecision), UserId.All, m_HostDecisionSequence,
            itemId, (int)verifiedCorrect, (int)chosen, inspectMs, shakeCount);
    }

    [SynchronizableMethod]
    void ApplyDecision(int decisionSequence, int itemId, int correct, int chosen,
        float inspectMs, int shakeCount)
    {
        if (decisionSequence <= m_LastAppliedDecision)
            return;

        m_LastAppliedDecision = decisionSequence;
        ResolveReferences();
        m_ShiftManager?.ApplyDecisionFromNetwork(itemId, (ItemCategory)correct,
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
        // Host kendi saatini zaten coroutine ile isletiyor; kendi yayinini geri islemesin.
        if (!IsHost)
            m_ShiftManager?.ApplyNetworkClock(remainingSeconds);
    }

    [SynchronizableMethod]
    void ApplyShiftEnd(int sessionSequence)
    {
        if (sessionSequence == m_LastAppliedSession)
            m_ShiftManager?.EndShiftFromNetwork();
    }

    // --------------------------------------------------------------- olaylar

    void HandleRoomJoined(RoomJoinedEvent args)
    {
        IsInRoom = true;
        RefreshRole();
        m_NextClockBroadcast = 0f;

        Debug.Log($"[Network] Odaya katilindi. Rol: {(IsHost ? "HOST" : "ISTEMCI")}, " +
                  $"kullanici={UserCount}, indeks={LocalUserIndex}", this);
    }

    void HandleRoomLeft(RoomLeftEvent args)
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

        // Vardiya devam ederken katilan oyuncu bos ekranda kalmasin: host acik
        // oturumu ve saati yeni gelene de yayinlar. ApplyStart zaten sessionSequence
        // korumasi tasidigi icin mevcut oyunculara zarar vermez.
        if (!IsHost || m_ShiftManager == null || m_ShiftManager.State != ShiftState.Vardiya)
            return;

        InvokeRemoteMethod(nameof(ApplyStart), UserId.All, m_HostSessionSequence, m_HostSeed);
        InvokeRemoteMethod(nameof(ApplyClock), UserId.All, m_ShiftManager.RemainingSeconds);
    }

    void HandleOtherUserLeft(OtherUserLeftEvent args)
    {
        RefreshRole();
    }

    // --------------------------------------------------------------- teshis
    // "Baglanmiyor" bir veri degildir. Bu dort dinleyici olmadan sorunun
    // lisans mi, ag mi, oda mi oldugu ayirt edilemez.

    void HandleNetworkError(NetworkErrorEvent args)
    {
        Debug.LogError($"[Network] AG HATASI. Endpoint={args.Endpoint}. " +
                       "Ayrinti icin Ag Teshis Bilgisini Yaz komutunu calistir.", this);
    }

    void HandleJoinRejected(JoinRejectedEvent args)
    {
        Debug.LogError($"[Network] ODAYA ALINMADIK. Sebep: {args.Reason}", this);
    }

    void HandleDisconnected(DisconnectedEvent args)
    {
        Debug.LogWarning("[Network] Baglanti koptu. Gozluk uyku/proximity sensoru veya " +
                         "Wi-Fi guc tasarrufu en sik sebeplerdir.", this);
    }

    void HandleStarted(StartedEvent args)
    {
        Debug.Log("[Network] Alteruna servisi basladi; Host / JoinLan cagrilabilir.", this);
    }

    /// <summary>
    /// SDK'nin kendi tanilama dokumunu Console'a basar. Discord'da soru sorarken
    /// bu ciktiyi yapistirin. Inspector'daki ... menusunden de calistirilabilir.
    /// </summary>
    [ContextMenu("Ag Teshis Bilgisini Yaz")]
    public void LogDebuggingInfo()
    {
        if (Multiplayer == null)
        {
            Debug.LogError("[Network] MultiplayerManager yok.", this);
            return;
        }

        Debug.Log($"[Network] Durum: Started={Multiplayer.Started}, IsConnected={Multiplayer.IsConnected}, " +
                  $"InRoom={Multiplayer.InRoom}, SonEngel={Multiplayer.GetLastBlockResponse()}\n" +
                  Multiplayer.GetDebuggingInfo(), this);
    }

    /// <summary>
    /// Rolu her zaman MultiplayerManager'dan okur. Olay argumanindaki controller'a
    /// bagli kalmiyoruz; host dusup baskasi devraldiginda da dogru sonuc verir.
    /// </summary>
    void RefreshRole()
    {
        if (Multiplayer == null)
            return;

        LocalUserIndex = Multiplayer.Me != null ? Multiplayer.Me.Index : -1;
        IsHost = LocalUserIndex >= 0 && LocalUserIndex == Multiplayer.LowestUserIndex;
        UserCount = Multiplayer.CurrentRoom != null ? Multiplayer.CurrentRoom.CurrentUsers : 0;
    }

    void HandleShiftStateChanged(ShiftState state)
    {
        if (!IsInRoom || !IsHost || state != ShiftState.Rapor)
            return;

        InvokeRemoteMethod(nameof(ApplyShiftEnd), UserId.All, m_LastAppliedSession);
    }

    void TrySubscribe()
    {
        if (m_Subscribed || Multiplayer == null)
            return;

        Multiplayer.OnRoomJoined.AddListener(HandleRoomJoined);
        Multiplayer.OnRoomLeft.AddListener(HandleRoomLeft);
        Multiplayer.OnOtherUserJoined.AddListener(HandleOtherUserJoined);
        Multiplayer.OnOtherUserLeft.AddListener(HandleOtherUserLeft);

        // Teshis: bunlar olmadan "baglanmiyor" disinda bir sey ogrenilemez.
        Multiplayer.OnNetworkError.AddListener(HandleNetworkError);
        Multiplayer.OnJoinRejected.AddListener(HandleJoinRejected);
        Multiplayer.OnDisconnected.AddListener(HandleDisconnected);
        Multiplayer.OnStarted.AddListener(HandleStarted);

        ResolveReferences();

        if (m_ShiftManager != null)
            m_ShiftManager.OnStateChanged += HandleShiftStateChanged;

        m_Subscribed = true;
        Debug.Log("[Network] Alteruna kopru baglandi, oda olaylari dinleniyor.", this);
    }

    void Unsubscribe()
    {
        if (!m_Subscribed)
            return;

        if (Multiplayer != null)
        {
            Multiplayer.OnRoomJoined.RemoveListener(HandleRoomJoined);
            Multiplayer.OnRoomLeft.RemoveListener(HandleRoomLeft);
            Multiplayer.OnOtherUserJoined.RemoveListener(HandleOtherUserJoined);
            Multiplayer.OnOtherUserLeft.RemoveListener(HandleOtherUserLeft);
            Multiplayer.OnNetworkError.RemoveListener(HandleNetworkError);
            Multiplayer.OnJoinRejected.RemoveListener(HandleJoinRejected);
            Multiplayer.OnDisconnected.RemoveListener(HandleDisconnected);
            Multiplayer.OnStarted.RemoveListener(HandleStarted);
        }

        if (m_ShiftManager != null)
            m_ShiftManager.OnStateChanged -= HandleShiftStateChanged;

        m_Subscribed = false;
    }

    void ResolveReferences()
    {
        if (m_ShiftManager == null)
            m_ShiftManager = FindFirstObjectByType<ShiftManager>();

        if (m_ItemSpawner == null)
            m_ItemSpawner = FindFirstObjectByType<ItemSpawner>();
    }
}
