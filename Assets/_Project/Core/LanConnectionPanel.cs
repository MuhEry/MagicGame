using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using AlterunaComponents;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built, VR-compatible control panel for Alteruna's LAN sample flow.
/// No cloud connection is attempted: one device calls Host, the other JoinLan.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class LanConnectionPanel : MonoBehaviour
{
    [SerializeField] private MultiplayerManager multiplayerManager;
    [SerializeField] private NetworkTestSpawner networkTestSpawner;
    [SerializeField] private Canvas targetCanvas;

    private TMP_Text statusText;
    private TMP_InputField directConnectInput;
    private Button hostButton;
    private Button joinButton;
    private Button directConnectButton;
    private Button spawnButton;
    private float nextStatusRefresh;

    private void Awake()
    {
        if (multiplayerManager == null)
            multiplayerManager = GetComponent<MultiplayerManager>();

        if (networkTestSpawner == null)
            networkTestSpawner = GetComponent<NetworkTestSpawner>();

        if (multiplayerManager == null)
            return;

        // The Alteruna editor can restore the registered cloud project asset.
        // Keep the manager disabled until the in-memory config is explicitly
        // changed to unregistered, otherwise Awake performs the blocking
        // cloud license request before the LAN UI can appear.
        multiplayerManager.enabled = false;
        if (!ApplyLanOnlyApplicationData())
        {
            Debug.LogError("[LAN] Offline Alteruna ayari uygulanamadi; bulut sorgusunu engellemek icin MultiplayerManager kapali tutuluyor.", this);
            return;
        }

        multiplayerManager.enabled = true;
        Debug.Log("[LAN] Offline Alteruna ayari uygulandi; bulut lisans dogrulamasi devre disi.", this);
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

        DisableCloudAndLocalTestButtons();
        BuildPanel();
        SetStatus("Hazir. Bir cihaz HOST LAN, digeri JOIN LAN secsin.");

        Debug.Log($"[LAN] Panel hazir. Yerel IP: {GetLocalIPv4Address()}", this);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextStatusRefresh)
            return;

        nextStatusRefresh = Time.unscaledTime + 0.5f;
        RefreshState();
    }

    public void HostLan()
    {
        RunConnectionAction("Host baslatiliyor...", () => multiplayerManager.Host());
    }

    public void JoinLan()
    {
        RunConnectionAction("LAN host araniyor...", () => multiplayerManager.JoinLan());
    }

    public void DirectConnect()
    {
        string address = directConnectInput != null ? directConnectInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(address))
        {
            SetStatus("Host IP adresini yaz. Ornek: 192.168.43.2");
            Debug.LogWarning("[LAN] Direct Connect icin IP adresi bos.", this);
            return;
        }

        RunConnectionAction($"{address} adresine baglaniliyor...", () => multiplayerManager.DirectConnect(address));
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

    private void RunConnectionAction(string pendingMessage, Action action)
    {
        if (multiplayerManager.IsConnected)
        {
            SetStatus("Zaten bir LAN oturumuna bagli.");
            return;
        }

        try
        {
            SetStatus(pendingMessage);
            Debug.Log($"[LAN] {pendingMessage}", this);
            action();
        }
        catch (Exception exception)
        {
            SetStatus($"Baglanti hatasi: {exception.Message}");
            Debug.LogException(exception, this);
        }
    }

    private void RefreshState()
    {
        bool connected = multiplayerManager.IsConnected;
        bool inRoom = multiplayerManager.InRoom;
        bool isHost = connected && multiplayerManager.IsHost();

        if (hostButton != null)
            hostButton.interactable = !connected;
        if (joinButton != null)
            joinButton.interactable = !connected;
        if (directConnectButton != null)
            directConnectButton.interactable = !connected;
        if (spawnButton != null)
            spawnButton.interactable = inRoom && isHost;

        string role = connected ? (isHost ? "HOST" : "CLIENT") : "-";
        string state = multiplayerManager.State.ToString();
        string room = inRoom ? "EVET" : "HAYIR";
        SetStatus($"Durum: {state} | Rol: {role} | Oda: {room}");
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
        if (shiftButtonObject != null && shiftButtonObject.TryGetComponent(out Button shiftButton))
        {
            shiftButton.interactable = false;
            Debug.Log("[LAN] Yerel Instantiate kullanan Yeni Vardiya butonu test icin kilitlendi.", shiftButtonObject);
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
        panel.sizeDelta = new Vector2(440f, 550f);

        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.035f, 0.05f, 0.07f, 0.94f);

        TMP_Text title = CreateText("Title", panel, "ALTERUNA LAN TEST", 30f, FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(20f, -18f), new Vector2(400f, 42f));

        statusText = CreateText("Status", panel, string.Empty, 20f, FontStyles.Normal);
        statusText.color = new Color(0.75f, 0.9f, 1f, 1f);
        SetRect(statusText.rectTransform, new Vector2(20f, -65f), new Vector2(400f, 72f));

        TMP_Text ipText = CreateText("Local IP", panel, $"Bu cihaz: {GetLocalIPv4Address()}", 19f, FontStyles.Normal);
        ipText.color = new Color(0.75f, 0.8f, 0.82f, 1f);
        SetRect(ipText.rectTransform, new Vector2(20f, -140f), new Vector2(400f, 32f));

        hostButton = CreateButton("Host LAN", panel, "HOST LAN", new Vector2(20f, -180f), HostLan);
        joinButton = CreateButton("Join LAN", panel, "JOIN LAN", new Vector2(230f, -180f), JoinLan);

        directConnectInput = CreateInputField(panel, new Vector2(20f, -252f), new Vector2(400f, 56f));
        directConnectButton = CreateButton("Direct Connect", panel, "IP ILE BAGLAN", new Vector2(20f, -322f), DirectConnect, 400f);
        spawnButton = CreateButton("Spawn Test", panel, "HOST: TEST KUPU URET", new Vector2(20f, -394f), SpawnTestObject, 400f);

        TMP_Text hint = CreateText(
            "Hint",
            panel,
            "Iki cihaz ayni Wi-Fi/hotspot'ta olmali. Once Host, sonra Join. Join bulamazsa host IP'sini kullan.",
            17f,
            FontStyles.Normal);
        hint.color = new Color(0.72f, 0.72f, 0.72f, 1f);
        SetRect(hint.rectTransform, new Vector2(20f, -466f), new Vector2(400f, 70f));
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

    private static TMP_InputField CreateInputField(Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform root = CreateRect("Host IP Input", parent);
        SetRect(root, position, size);

        Image image = root.gameObject.AddComponent<Image>();
        image.color = new Color(0.93f, 0.95f, 0.96f, 1f);

        TMP_InputField input = root.gameObject.AddComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 64;

        RectTransform viewport = CreateRect("Text Area", root);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(14f, 7f);
        viewport.offsetMax = new Vector2(-14f, -7f);
        viewport.gameObject.AddComponent<RectMask2D>();

        TMP_Text placeholder = CreateText("Placeholder", viewport, "Host IP (istege bagli)", 21f, FontStyles.Italic);
        placeholder.color = new Color(0.35f, 0.4f, 0.43f, 0.7f);
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = Vector2.zero;
        placeholder.rectTransform.offsetMax = Vector2.zero;

        TMP_Text value = CreateText("Text", viewport, string.Empty, 22f, FontStyles.Normal);
        value.color = Color.black;
        value.rectTransform.anchorMin = Vector2.zero;
        value.rectTransform.anchorMax = Vector2.one;
        value.rectTransform.offsetMin = Vector2.zero;
        value.rectTransform.offsetMax = Vector2.zero;

        input.textViewport = viewport;
        input.textComponent = value;
        input.placeholder = placeholder;
        input.targetGraphic = image;

        return input;
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

    private static string GetLocalIPv4Address()
    {
        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Quest and the intended PC fallback both use Wi-Fi. Prefer that
            // address so VirtualBox/Hyper-V adapters are not shown in the UI.
            string wirelessAddress = FindIPv4Address(interfaces, wirelessOnly: true);
            if (!string.IsNullOrEmpty(wirelessAddress))
                return wirelessAddress;

            string physicalAddress = FindIPv4Address(interfaces, wirelessOnly: false);
            if (!string.IsNullOrEmpty(physicalAddress))
                return physicalAddress;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[LAN] Yerel IP okunamadi: {exception.Message}");
        }

        return "IP bulunamadi";
    }

    private static string FindIPv4Address(NetworkInterface[] interfaces, bool wirelessOnly)
    {
        foreach (NetworkInterface networkInterface in interfaces)
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            if (wirelessOnly && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                continue;

            string description = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
            if (description.Contains("virtual") ||
                description.Contains("virtualbox") ||
                description.Contains("vmware") ||
                description.Contains("hyper-v") ||
                description.Contains("loopback"))
                continue;

            foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address) &&
                    !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    return address.Address.ToString();
            }
        }

        return null;
    }

    private static bool ApplyLanOnlyApplicationData()
    {
        UnityEngine.Object applicationData = Resources.Load("ApplicationData");
        if (applicationData == null)
        {
            Debug.LogError("[LAN] Alteruna ApplicationData resource bulunamadi.");
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type dataType = applicationData.GetType();
        FieldInfo applicationIdField = dataType.GetField("_applicationId", flags);
        FieldInfo projectGuidField = dataType.GetField("_projectIDGuid", flags);
        FieldInfo subIdField = dataType.GetField("_subId", flags);
        FieldInfo subGuidField = dataType.GetField("_subIdGuid", flags);

        if (applicationIdField == null || projectGuidField == null)
        {
            Debug.LogError("[LAN] Alteruna ApplicationData alanlari bulunamadi; paket API'si degismis olabilir.");
            return false;
        }

        applicationIdField.SetValue(applicationData, string.Empty);
        projectGuidField.SetValue(applicationData, Guid.Empty);
        subIdField?.SetValue(applicationData, string.Empty);
        subGuidField?.SetValue(applicationData, null);
        return true;
    }
}
