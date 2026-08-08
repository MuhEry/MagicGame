using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Gozlukte gorunen ag teshis satiri.
///
/// NEDEN VAR: Faz 2'de bir sey calismadiginda tek gorunen sey "hicbir sey olmuyor"du.
/// Odaya girilemedi mi, girildi de host mu secilemedi, esya mi uretilmedi -
/// bunlar yalnizca editor Console'unda gorunuyordu, gozlukte degil. Bu panel
/// ayni bilgiyi cihazda gosterir; APK'yi baglamadan sorunun yerini soyler.
///
/// Sartname "kapsam" bolumune uygun kalmak icin panel varsayilan olarak KAPALI
/// baslar ve yalnizca showOnStart / ToggleVisibility ile acilir.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkDiagnosticsHud : MonoBehaviour
{
    [SerializeField] TMP_Text targetText;
    [SerializeField] ShiftManager shiftManager;
    [SerializeField] ItemSpawner itemSpawner;

    [Tooltip("Kapali baslar; ayik testte acmak icin isaretle veya ToggleVisibility cagir.")]
    [SerializeField] bool showOnStart;

    [SerializeField, Min(0.1f)] float refreshInterval = 0.5f;

    readonly StringBuilder builder = new StringBuilder(256);
    float nextRefresh;
    string lastDecisionLine = "-";

    void Awake()
    {
        if (shiftManager == null)
            shiftManager = FindFirstObjectByType<ShiftManager>();
        if (itemSpawner == null)
            itemSpawner = FindFirstObjectByType<ItemSpawner>();
        if (targetText == null)
            targetText = GetComponentInChildren<TMP_Text>(true);

        SetVisible(showOnStart);
    }

    void OnEnable()
    {
        if (shiftManager != null)
            shiftManager.OnDecision += HandleDecision;
    }

    void OnDisable()
    {
        if (shiftManager != null)
            shiftManager.OnDecision -= HandleDecision;
    }

    public void ToggleVisibility()
    {
        SetVisible(targetText == null || !targetText.enabled);
    }

    public void SetVisible(bool visible)
    {
        if (targetText != null)
            targetText.enabled = visible;
    }

    void HandleDecision(DecisionResult result)
    {
        lastDecisionLine = $"{(result.isCorrect ? "DOGRU" : "YANLIS")} " +
                           $"id={result.itemId} {result.correct}->{result.chosen}";
    }

    void Update()
    {
        if (targetText == null || !targetText.enabled || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        targetText.text = BuildReport();
    }

    string BuildReport()
    {
        builder.Clear();

        NetworkShiftCoordinator network = NetworkShiftCoordinator.Instance;
        if (network == null)
        {
            builder.AppendLine("AG: NetworkShiftCoordinator sahnede YOK");
            builder.AppendLine("Tools > Gece Vardiyasi > Multiplayer Kurulumunu Uygula");
            return builder.ToString();
        }

        if (!network.IsBridgeReady)
        {
            builder.AppendLine("AG: Multiplayer bileseni bulunamadi (kopru kurulmadi)");
            builder.AppendLine("Sahnede MultiplayerManager var mi?");
            return builder.ToString();
        }

        builder.Append("AG: ").Append(network.IsInRoom ? "ODADA" : "ODA YOK (cevrimdisi)");
        if (network.IsInRoom)
        {
            builder.Append(" | rol=").Append(network.IsHost ? "HOST" : "ISTEMCI");
            builder.Append(" | kullanici=").Append(network.UserCount);
            builder.Append(" | indeks=").Append(network.LocalUserIndex);
        }
        builder.AppendLine();

        if (shiftManager != null)
        {
            builder.Append("VARDIYA: ").Append(shiftManager.State);
            builder.Append(" | kalan=").Append(Mathf.CeilToInt(shiftManager.RemainingSeconds)).Append(" sn");
            builder.Append(" | D/Y=").Append(shiftManager.CorrectCount).Append('/')
                   .Append(shiftManager.IncorrectCount);
            builder.AppendLine();
        }

        if (itemSpawner != null)
        {
            builder.Append("ESYA: seed=").Append(itemSpawner.Seed);
            builder.Append(" | tezgahta=")
                   .Append(itemSpawner.CurrentSpawnedItem != null ? itemSpawner.CurrentSpawnedItem.name : "yok");
            builder.AppendLine();
        }

        builder.Append("SON KARAR: ").Append(lastDecisionLine);
        return builder.ToString();
    }
}
