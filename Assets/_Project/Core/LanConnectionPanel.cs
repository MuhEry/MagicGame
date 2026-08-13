using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using AlterunaComponents;
using Alteruna.Multiplayer.Unity.EventArgument;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built, VR-compatible control panel for Alteruna's LAN sample flow.
/// The registered Alteruna V2 project remains intact. One device calls Host,
/// the other JoinLan; cloud room browsing and Connect On Start are not used.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class LanConnectionPanel : MonoBehaviour
{
    [SerializeField] private MultiplayerManager multiplayerManager;
    [SerializeField] private NetworkTestSpawner networkTestSpawner;
    [SerializeField] private Canvas targetCanvas;

    private TMP_Text statusText;
    private Button hostButton;
    private Button joinButton;
    private Button spawnButton;
    private Button shiftButton;
    private Button resetItemButton;
    private ItemSpawner itemSpawner;
    private float nextStatusRefresh;
    private string lastStateSnapshot;
    private int connectionAttempt;
    private UdpClient lanProbeSocket;
    private bool lanProbeIsHost;
    private bool lanProbeAckReceived;
    private bool autoDirectConnectStarted;
    private string lanProbeStatus = "PROBE: bekliyor";

    private const int LanProbePort = 47777;
    private const string LanProbeDiscover = "MAGICGAME_LAN_PROBE_V1";
    private const string LanProbeAck = "MAGICGAME_LAN_PROBE_ACK_V1";

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject multicastLock;
#endif

    private void Awake()
    {
        if (multiplayerManager == null)
            multiplayerManager = GetComponent<MultiplayerManager>();

        if (networkTestSpawner == null)
            networkTestSpawner = GetComponent<NetworkTestSpawner>();

        itemSpawner = FindFirstObjectByType<ItemSpawner>();

        // Alteruna V2 2.1.1r3'te ConnectOnStart alani otomatik Start akisini
        // durdurmuyor. Manager aktif kalirsa daha oyuncu Host/Join secmeden
        // managed sunucuya baglanir ve Host() "already connected" hatasi verir.
        // Awake/lisans dogrulamasi yine calisir; yalniz Unity Start ertelenir.
        // Host() ve JoinLan() gerekli anda Manager'i kendileri etkinlestirir.
        if (multiplayerManager != null)
            multiplayerManager.enabled = false;

        AcquireAndroidMulticastLock();
        LogNetworkEnvironment("Awake");
    }

    private void Start()
    {
        if (multiplayerManager == null)
        {
            Debug.LogError("[LAN] MultiplayerManager bulunamadi.", this);
            enabled = false;
            return;
        }

        if (targetCanvas == null)
        {
            GameObject hud = GameObject.Find("HUD");
            if (hud != null)
                targetCanvas = hud.GetComponent<Canvas>();
        }

        if (targetCanvas == null)
            targetCanvas = FindFirstObjectByType<Canvas>();

        if (targetCanvas == null)
        {
            Debug.LogError("[LAN] Panel icin Canvas bulunamadi.", this);
            enabled = false;
            return;
        }

        multiplayerManager.OnRoomJoined.AddListener(HandleRoomJoined);
        multiplayerManager.OnOtherUserJoined.AddListener(HandleOtherUserJoined);
        multiplayerManager.OnJoinRejected.AddListener(HandleJoinRejected);
        multiplayerManager.OnNetworkError.AddListener(HandleNetworkError);

        DisableCloudAndLocalTestButtons();
        BuildPanel();
        SetStatus("Hazir. Bir cihaz HOST LAN, digeri JOIN LAN secsin.");

        Debug.Log("[LAN] Panel hazir. Bir cihaz HOST LAN, digeri JOIN LAN secsin.", this);
    }

    private void OnDestroy()
    {
        if (multiplayerManager != null)
        {
            multiplayerManager.OnRoomJoined.RemoveListener(HandleRoomJoined);
            multiplayerManager.OnOtherUserJoined.RemoveListener(HandleOtherUserJoined);
            multiplayerManager.OnJoinRejected.RemoveListener(HandleJoinRejected);
            multiplayerManager.OnNetworkError.RemoveListener(HandleNetworkError);
        }

        ReleaseAndroidMulticastLock();
        StopLanProbe();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            ReleaseAndroidMulticastLock();
        else
            AcquireAndroidMulticastLock();
    }

    private void Update()
    {
        PollLanProbe();

        if (Time.unscaledTime < nextStatusRefresh)
            return;

        nextStatusRefresh = Time.unscaledTime + 0.5f;
        RefreshState();
    }

    public void HostLan()
    {
        StartHostLanProbe();
        RunConnectionAction("HOST", "Host baslatiliyor...", () => multiplayerManager.Host());
    }

    public void JoinLan()
    {
        StartClientLanProbe();
        // Alteruna V2 2.1.1r3 Android'de JoinLan kesif cevabini 127.0.0.1
        // olarak kaydediyor. Kendi UDP probumuz hostun gercek LAN adresini
        // bulur; ACK gelince resmi DirectConnect(ip) API'si otomatik cagrilir.
        // Kullanici IP girmek zorunda kalmaz ve bulut/oda listesi kullanilmaz.
        RunConnectionAction("JOIN", "LAN host otomatik araniyor...", () => { });
    }

    public void SpawnTestObject()
    {
        if (networkTestSpawner == null)
        {
            SetStatus("NetworkTestSpawner bulunamadi.");
            Debug.LogError("[LAN] NetworkTestSpawner bulunamadi.", this);
            return;
        }

        networkTestSpawner.SpawnTestObject();
    }

    public void ResetCurrentItem()
    {
        if (itemSpawner == null)
            itemSpawner = FindFirstObjectByType<ItemSpawner>();

        if (itemSpawner == null)
        {
            SetStatus("ItemSpawner bulunamadi.");
            return;
        }

        itemSpawner.ResetCurrentItem();
        SetStatus("Aktif esya bacada sifirlaniyor...");
    }

    private void RunConnectionAction(string operation, string pendingMessage, Action action)
    {
        // IsConnected, Alteruna servis baglantisini da kapsar. Lisans
        // dogrulandiktan sonra oda kurulmadan once true olabilir. LAN butonlarini
        // yalniz gercekten bir odaya girildiginde kilitle.
        if (multiplayerManager.InRoom)
        {
            SetStatus("Zaten bir LAN oturumuna bagli.");
            return;
        }

        try
        {
            AcquireAndroidMulticastLock();
            LogNetworkEnvironment(operation + " before");
            LogManagerSnapshot(operation + " before");

            // Onceki bir denemeden kalan managed/cloud baglantisi varsa LAN
            // baslatmadan once temizle. Disconnect senkron olarak servisi durdurur;
            // secilen Host/Join cagrisi temiz yerel konfigurasyonla yeniden baslar.
            if (multiplayerManager.IsConnected ||
                multiplayerManager.IsConnecting ||
                multiplayerManager.State.ToString() != "Uninitialized")
            {
                Debug.Log("[LAN] Onceki servis/yeniden baglanma dongusu kapatiliyor.", this);
                multiplayerManager.Disconnect();
            }

            SetStatus(pendingMessage);
            int attempt = ++connectionAttempt;
            Debug.Log($"[LAN-DIAG] Attempt={attempt} Operation={operation} {pendingMessage}", this);
            action();
            LogManagerSnapshot(operation + " immediately-after");
            StartCoroutine(TraceConnectionAttempt(attempt, operation));

            if (operation == "JOIN")
                StartCoroutine(SendLanProbeSequence(attempt));
        }
        catch (Exception exception)
        {
            SetStatus($"Baglanti hatasi: {exception.Message}");
            Debug.LogException(exception, this);
        }
    }

    private void RefreshState()
    {
        bool serviceConnected = multiplayerManager.IsConnected;
        bool connecting = multiplayerManager.IsConnecting;
        bool inRoom = multiplayerManager.InRoom;
        bool isHost = inRoom && multiplayerManager.IsHost();
        bool canStartLan = !connecting && !inRoom;

        if (hostButton != null)
            hostButton.interactable = canStartLan;
        if (joinButton != null)
            joinButton.interactable = canStartLan;
        if (spawnButton != null)
            spawnButton.interactable = inRoom && isHost;
        if (shiftButton != null)
            shiftButton.interactable = inRoom && isHost;
        if (resetItemButton != null)
            resetItemButton.interactable = inRoom && isHost && itemSpawner != null && itemSpawner.CurrentSpawnedItem != null;

        string service = inRoom ? "OTURUMDA" : connecting ? "BAGLANIYOR" : serviceConnected ? "BAGLI" : "HAZIR";
        string role = inRoom ? (isHost ? "HOST" : "CLIENT") : "-";
        string state = multiplayerManager.State.ToString();
        string room = inRoom ? "EVET" : "HAYIR";
        SetStatus($"Servis: {service} | Durum: {state}\nRol: {role} | Oda: {room}\n{lanProbeStatus}");

        string snapshot = $"State={state} Connected={serviceConnected} Connecting={connecting} InRoom={inRoom} Role={role}";
        if (snapshot != lastStateSnapshot)
        {
            lastStateSnapshot = snapshot;
            Debug.Log("[LAN-STATE] " + snapshot, this);
        }
    }

    private void HandleRoomJoined(RoomJoinedEvent args)
    {
        Debug.Log($"[NET] Odaya girildi. Host muyum: {multiplayerManager.IsHost()}", this);
        LogManagerSnapshot("OnRoomJoined");
    }

    private void HandleOtherUserJoined(OtherUserJoinedEvent args)
    {
        Debug.Log("[NET] Diger oyuncu LAN oturumuna katildi.", this);
        LogManagerSnapshot("OnOtherUserJoined");
    }

    private void HandleJoinRejected(JoinRejectedEvent args)
    {
        Debug.LogError(
            $"[NET] LAN katilimi reddedildi. Kod: {multiplayerManager.GetLastBlockResponse()} | " +
            multiplayerManager.GetDebuggingInfo(false, false),
            this);
        LogManagerSnapshot("OnJoinRejected");
    }

    private void HandleNetworkError(NetworkErrorEvent args)
    {
        Debug.LogError(
            "[NET] Ag hatasi. " + multiplayerManager.GetDebuggingInfo(false, false),
            this);
        LogManagerSnapshot("OnNetworkError");
    }

    private IEnumerator TraceConnectionAttempt(int attempt, string operation)
    {
        float[] checkpoints = { 1f, 3f, 7f, 12f };
        float elapsed = 0f;

        foreach (float checkpoint in checkpoints)
        {
            yield return new WaitForSecondsRealtime(checkpoint - elapsed);
            elapsed = checkpoint;

            if (attempt != connectionAttempt)
                yield break;

            LogManagerSnapshot($"Attempt={attempt} Operation={operation} T+{checkpoint:0}s");

            if (multiplayerManager.InRoom)
                yield break;
        }

        if (operation == "JOIN" && !multiplayerManager.InRoom)
        {
            string debugInfo = SafeDebuggingInfo();
            string reason = lanProbeAckReceived
                ? "Host IP bulundu ama Alteruna DirectConnect oturuma giremedi."
                : "UDP LAN host bulunamadi veya cevap istemciye ulasmadi.";
            Debug.LogError(
                $"[LAN-DIAG] Attempt={attempt} JOIN timeout: {reason} " +
                $"LastBlock={multiplayerManager.GetLastBlockResponse()} | {debugInfo}",
                this);
            SetStatus("JOIN zaman asimi. Logcat'i kontrol et.");
        }
    }

    private void LogManagerSnapshot(string point)
    {
        if (multiplayerManager == null)
            return;

        Debug.Log(
            $"[LAN-DIAG] {point} | State={multiplayerManager.State} " +
            $"Connected={multiplayerManager.IsConnected} Connecting={multiplayerManager.IsConnecting} " +
            $"InRoom={multiplayerManager.InRoom} " +
            $"IsHost={(multiplayerManager.InRoom ? multiplayerManager.IsHost().ToString() : "n/a")} | " +
            SafeDebuggingInfo(),
            this);
    }

    private string SafeDebuggingInfo()
    {
        try
        {
            return multiplayerManager.GetDebuggingInfo(false, false);
        }
        catch (Exception exception)
        {
            return "GetDebuggingInfo failed: " + exception.Message;
        }
    }

    private void LogNetworkEnvironment(string point)
    {
        StringBuilder result = new StringBuilder();
        result.Append($"[LAN-NET] {point} | Platform={Application.platform} ");
        result.Append($"Device={SystemInfo.deviceModel} OS={SystemInfo.operatingSystem} ");
        result.Append($"Reachability={Application.internetReachability}");

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    result.Append($" | IF={adapter.Name}/{adapter.NetworkInterfaceType} IPv4={address.Address}");
                }
            }
        }
        catch (Exception exception)
        {
            result.Append(" | Interface scan failed: " + exception.Message);
        }

        Debug.Log(result.ToString(), this);
    }

    private void AcquireAndroidMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (multicastLock != null && multicastLock.Call<bool>("isHeld"))
            {
                Debug.Log("[LAN-ANDROID] MulticastLock zaten aktif.", this);
                return;
            }

            multicastLock?.Dispose();
            multicastLock = null;

            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext");
            using AndroidJavaObject wifiManager = context.Call<AndroidJavaObject>("getSystemService", "wifi");

            multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "MagicGame.AlterunaLan");
            multicastLock.Call("setReferenceCounted", false);
            multicastLock.Call("acquire");

            Debug.Log($"[LAN-ANDROID] MulticastLock acquired={multicastLock.Call<bool>("isHeld")}", this);
        }
        catch (Exception exception)
        {
            Debug.LogError("[LAN-ANDROID] MulticastLock alinamadi: " + exception, this);
        }
#else
        Debug.Log("[LAN-ANDROID] MulticastLock yalniz Android build'de uygulanir.", this);
#endif
    }

    private void ReleaseAndroidMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (multicastLock != null && multicastLock.Call<bool>("isHeld"))
                multicastLock.Call("release");

            multicastLock?.Dispose();
            multicastLock = null;
            Debug.Log("[LAN-ANDROID] MulticastLock birakildi.", this);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[LAN-ANDROID] MulticastLock birakilirken hata: " + exception.Message, this);
        }
#endif
    }

    private void StartHostLanProbe()
    {
        StopLanProbe();

        try
        {
            lanProbeSocket = new UdpClient();
            lanProbeSocket.ExclusiveAddressUse = false;
            lanProbeSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            lanProbeSocket.Client.Bind(new IPEndPoint(IPAddress.Any, LanProbePort));
            lanProbeSocket.EnableBroadcast = true;
            lanProbeSocket.Client.Blocking = false;
            lanProbeIsHost = true;
            lanProbeAckReceived = false;
            autoDirectConnectStarted = false;
            lanProbeStatus = $"PROBE: host UDP/{LanProbePort} dinliyor";
            Debug.Log($"[LAN-PROBE] HOST UDP/{LanProbePort} dinleyicisi acildi.", this);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[LAN-PROBE] HOST UDP/{LanProbePort} acilamadi: {exception}", this);
            StopLanProbe();
        }
    }

    private void StartClientLanProbe()
    {
        StopLanProbe();

        try
        {
            lanProbeSocket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            lanProbeSocket.EnableBroadcast = true;
            lanProbeSocket.Client.Blocking = false;
            lanProbeIsHost = false;
            lanProbeAckReceived = false;
            autoDirectConnectStarted = false;
            lanProbeStatus = "PROBE: kesif paketi gonderiliyor";
            Debug.Log($"[LAN-PROBE] CLIENT UDP socket acildi. Local={lanProbeSocket.Client.LocalEndPoint}", this);
        }
        catch (Exception exception)
        {
            Debug.LogError("[LAN-PROBE] CLIENT UDP socket acilamadi: " + exception, this);
            StopLanProbe();
        }
    }

    private IEnumerator SendLanProbeSequence(int attempt)
    {
        float[] delays = { 0f, 1f, 2f, 3f, 4f, 5f };
        float elapsed = 0f;

        foreach (float delay in delays)
        {
            yield return new WaitForSecondsRealtime(delay - elapsed);
            elapsed = delay;

            if (attempt != connectionAttempt || lanProbeSocket == null || lanProbeIsHost)
                yield break;

            SendLanProbe(attempt, delay);
        }

        if (!lanProbeAckReceived)
        {
            lanProbeStatus = "PROBE: cevap yok (UDP engelli/hosta ulasmadi)";
            Debug.LogError(
                $"[LAN-PROBE] Attempt={attempt} FAILED: 6 kesif yayinina host cevabi gelmedi.",
                this);
        }
    }

    private void SendLanProbe(int attempt, float elapsed)
    {
        byte[] payload = Encoding.UTF8.GetBytes(LanProbeDiscover);
        HashSet<string> targets = GetBroadcastAddresses();

        foreach (string target in targets)
        {
            try
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(target), LanProbePort);
                int bytes = lanProbeSocket.Send(payload, payload.Length, endpoint);
                Debug.Log(
                    $"[LAN-PROBE] Attempt={attempt} T+{elapsed:0}s TX DISCOVER bytes={bytes} target={endpoint}",
                    this);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LAN-PROBE] TX target={target}:{LanProbePort} failed: {exception}", this);
            }
        }
    }

    private void PollLanProbe()
    {
        if (lanProbeSocket == null)
            return;

        try
        {
            while (lanProbeSocket.Available > 0)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] payload = lanProbeSocket.Receive(ref remote);
                string message = Encoding.UTF8.GetString(payload);
                Debug.Log($"[LAN-PROBE] RX '{message}' from={remote} hostMode={lanProbeIsHost}", this);

                if (lanProbeIsHost && message == LanProbeDiscover)
                {
                    lanProbeStatus = $"PROBE: client paketi geldi ({remote.Address})";
                    byte[] ack = Encoding.UTF8.GetBytes(LanProbeAck);
                    int bytes = lanProbeSocket.Send(ack, ack.Length, remote);
                    Debug.Log($"[LAN-PROBE] HOST TX ACK bytes={bytes} target={remote}", this);
                }
                else if (!lanProbeIsHost && message == LanProbeAck)
                {
                    lanProbeAckReceived = true;
                    lanProbeStatus = $"PROBE: UDP CIFT YONLU ({remote.Address})";
                    Debug.Log("[LAN-PROBE] SUCCESS: Iki cihaz arasinda cift yonlu UDP calisiyor.", this);
                    StartAutomaticDirectConnect(remote.Address.ToString());
                }
            }
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock)
        {
            // Non-blocking socket: no packet is currently available.
        }
        catch (Exception exception)
        {
            Debug.LogError("[LAN-PROBE] RX failed: " + exception, this);
        }
    }

    private static HashSet<string> GetBroadcastAddresses()
    {
        HashSet<string> result = new HashSet<string> { "255.255.255.255" };

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null)
                        continue;

                    byte[] ip = address.Address.GetAddressBytes();
                    byte[] mask = address.IPv4Mask.GetAddressBytes();
                    byte[] broadcast = new byte[4];

                    for (int index = 0; index < broadcast.Length; index++)
                        broadcast[index] = (byte)(ip[index] | ~mask[index]);

                    result.Add(new IPAddress(broadcast).ToString());
                }
            }
        }
        catch
        {
            // Global broadcast remains available as a fallback.
        }

        return result;
    }

    private void StartAutomaticDirectConnect(string hostAddress)
    {
        if (autoDirectConnectStarted || multiplayerManager == null || multiplayerManager.InRoom)
            return;

        autoDirectConnectStarted = true;
        lanProbeStatus = "PROBE: host bulundu, Alteruna baglaniyor";

        try
        {
            Debug.Log(
                $"[LAN-DIRECT] Built-in JoinLan 127.0.0.1 hatasi bypass ediliyor. " +
                $"DirectConnect host={hostAddress} port=AlterunaDefault",
                this);
            multiplayerManager.DirectConnect(hostAddress);
            LogManagerSnapshot("DirectConnect immediately-after");
        }
        catch (Exception exception)
        {
            autoDirectConnectStarted = false;
            lanProbeStatus = "PROBE: DirectConnect cagrisi hata verdi";
            Debug.LogError("[LAN-DIRECT] DirectConnect failed: " + exception, this);
        }
    }

    private void StopLanProbe()
    {
        if (lanProbeSocket == null)
            return;

        try
        {
            lanProbeSocket.Close();
            lanProbeSocket.Dispose();
        }
        catch
        {
            // Best effort cleanup while leaving play mode or closing the app.
        }

        lanProbeSocket = null;
        lanProbeIsHost = false;
        lanProbeAckReceived = false;
        autoDirectConnectStarted = false;
    }

    private void DisableCloudAndLocalTestButtons()
    {
        GameObject roomMenu = GameObject.Find("Room Menu");
        if (roomMenu != null)
            roomMenu.SetActive(false);

        GameObject oldNetworkButton = GameObject.Find("AgNesnesiTest");
        if (oldNetworkButton != null)
            oldNetworkButton.SetActive(false);

        GameObject shiftButtonObject = GameObject.Find("Btn_YeniVardiya");
        if (shiftButtonObject != null && shiftButtonObject.TryGetComponent(out shiftButton))
        {
            shiftButton.interactable = false;
            Debug.Log("[LAN] Yeni Vardiya yalniz LAN hostuna acilacak.", shiftButtonObject);
        }
    }

    private void BuildPanel()
    {
        Transform existing = targetCanvas.transform.Find("LAN Connection Panel");
        if (existing != null)
            Destroy(existing.gameObject);

        RectTransform panel = CreateRect("LAN Connection Panel", targetCanvas.transform);
        panel.anchorMin = new Vector2(1f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-20f, -20f);
        panel.sizeDelta = new Vector2(440f, 480f);

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.035f, 0.05f, 0.07f, 0.94f);

        TMP_Text title = CreateText("Title", panel, "ALTERUNA LAN TEST", 30f, FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(20f, -18f), new Vector2(400f, 42f));

        statusText = CreateText("Status", panel, string.Empty, 20f, FontStyles.Normal);
        statusText.color = new Color(0.75f, 0.9f, 1f, 1f);
        SetRect(statusText.rectTransform, new Vector2(20f, -65f), new Vector2(400f, 72f));

        hostButton = CreateButton("Host LAN", panel, "HOST LAN", new Vector2(20f, -145f), HostLan);
        joinButton = CreateButton("Join LAN", panel, "JOIN LAN", new Vector2(230f, -145f), JoinLan);
        spawnButton = CreateButton("Spawn Test", panel, "HOST: TEST KUPU URET", new Vector2(20f, -218f), SpawnTestObject, 400f);
        resetItemButton = CreateButton("Reset Item", panel, "NESNEYI SIFIRLA", new Vector2(20f, -286f), ResetCurrentItem, 400f);

        TMP_Text hint = CreateText(
            "Hint",
            panel,
            "Iki cihaz ayni Wi-Fi/hotspot'ta olmali. Once bir cihaz Host, sonra diger cihaz Join secsin.",
            17f,
            FontStyles.Normal);
        hint.color = new Color(0.72f, 0.72f, 0.72f, 1f);
        SetRect(hint.rectTransform, new Vector2(20f, -360f), new Vector2(400f, 70f));
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 position,
        UnityEngine.Events.UnityAction action,
        float width = 190f)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, position, new Vector2(width, 58f));

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.08f, 0.42f, 0.23f, 1f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.12f, 0.62f, 0.34f, 1f);
        colors.pressedColor = new Color(0.05f, 0.28f, 0.15f, 1f);
        colors.disabledColor = new Color(0.15f, 0.18f, 0.17f, 0.7f);
        button.colors = colors;

        TMP_Text text = CreateText("Label", rect, label, 22f, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        return button;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float size, FontStyles style)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.overflowMode = TextOverflowModes.Overflow;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject instance = new GameObject(name, typeof(RectTransform));
        RectTransform rect = instance.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

}
