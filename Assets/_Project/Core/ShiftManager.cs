using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ShiftState
{
    Hazir,
    Vardiya,
    Rapor
}

[Serializable]
public struct ShiftReport
{
    public int sessionId;
    public int correctCount;
    public int incorrectCount;
    public int inspectedItemCount;
    public int totalShakeCount;
    public float averageInspectMs;
    public bool hasMostConfusedCategory;
    public ItemCategory mostConfusedCategory;
}

public class ShiftManager : MonoBehaviour
{
    public static ShiftManager Instance;

    public event Action<DecisionResult> OnDecision;
    public event Action<float> OnTimeChanged;
    public event Action<ShiftState> OnStateChanged;
    public event Action<ShiftReport> OnReportReady;

    [Header("Vardiya")]
    [SerializeField, Min(1f)] private float shiftDurationSeconds = 90f;
    [SerializeField, Min(0f)] private float nextItemDelaySeconds = 0.5f;
    [SerializeField] private ItemSpawner itemSpawner;

    [Tooltip("Faz 2 koprusu. Bos birakilirsa sahnede aranir; hic yoksa oyun cevrimdisi calisir.")]
    [SerializeField] private NetworkShiftCoordinator networkCoordinator;

    private readonly Dictionary<ItemCategory, int> incorrectByCategory =
        new Dictionary<ItemCategory, int>();

    private Coroutine timerRoutine;
    private Coroutine nextItemRoutine;
    private float remainingSeconds;
    private float totalInspectMs;
    private int correctCount;
    private int incorrectCount;
    private int inspectedItemCount;
    private int totalShakeCount;
    private int currentSessionId;
    private int lastPublishedWholeSecond = -1;

    public ShiftState State { get; private set; } = ShiftState.Hazir;
    public float RemainingSeconds => remainingSeconds;
    public int CurrentSessionId => currentSessionId;
    public int CorrectCount => correctCount;
    public int IncorrectCount => incorrectCount;
    public int InspectedItemCount => inspectedItemCount;
    public int TotalShakeCount => totalShakeCount;
    public int Score => correctCount;
    public ShiftReport LastReport { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (itemSpawner == null)
            itemSpawner = FindFirstObjectByType<ItemSpawner>();

        if (networkCoordinator == null)
            networkCoordinator = FindFirstObjectByType<NetworkShiftCoordinator>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnValidate()
    {
        shiftDurationSeconds = Mathf.Max(1f, shiftDurationSeconds);
        nextItemDelaySeconds = Mathf.Max(0f, nextItemDelaySeconds);
    }

    /// <summary>
    /// Faz 2 koprusunu tembel cozer. Sahnede koordinator yoksa null doner ve
    /// butun ag dallari atlanir - yani Faz 1 akisi aynen calisir.
    /// </summary>
    private NetworkShiftCoordinator ResolveCoordinator()
    {
        if (networkCoordinator == null)
            networkCoordinator = NetworkShiftCoordinator.Instance;

        return networkCoordinator;
    }

    /// <summary>Odadayiz ve host DEGILIZ: saat, spawn ve skor host'tan gelir.</summary>
    private bool IsNetworkClient
    {
        get
        {
            NetworkShiftCoordinator coordinator = ResolveCoordinator();
            return coordinator != null && coordinator.IsInRoom && !coordinator.IsHost;
        }
    }

    /// <summary>
    /// UI'daki "Yeni Vardiya" butonunun çağıracağı metot.
    ///
    /// Odadaysak vardiyayi HOST baslatir (iki cihazda iki farkli seed olmasin diye).
    /// Odada degilsek dogrudan cevrimdisi yol calisir - Faz 1 davranisi.
    /// </summary>
    public void StartShift()
    {
        NetworkShiftCoordinator coordinator = ResolveCoordinator();

        if (coordinator != null && coordinator.IsInRoom)
        {
            coordinator.RequestStartShift();
            return;
        }

        StartShiftOffline();
    }

    /// <summary>Tek oyuncu / oda disi vardiya. Faz 1'deki eski StartShift davranisi.</summary>
    public void StartShiftOffline()
    {
        StartShiftInternal(null, true);
    }

    /// <summary>
    /// Host'un yayinladigi vardiya. <paramref name="runAuthoritativeTimer"/> yalnizca
    /// host'ta true'dur; istemci saati ve esya uretimini host'tan alir.
    /// </summary>
    public void StartShiftFromNetwork(int synchronizedSeed, bool runAuthoritativeTimer)
    {
        StartShiftInternal(synchronizedSeed, runAuthoritativeTimer);
    }

    private void StartShiftInternal(int? synchronizedSeed, bool runAuthoritativeTimer)
    {
        if (State == ShiftState.Vardiya)
        {
            Debug.LogWarning("ShiftManager: Aktif vardiya yeniden başlatılamaz.", this);
            return;
        }

        StopActiveRoutines();

        currentSessionId++;
        remainingSeconds = shiftDurationSeconds;
        lastPublishedWholeSecond = -1;
        correctCount = 0;
        incorrectCount = 0;
        inspectedItemCount = 0;
        totalShakeCount = 0;
        totalInspectMs = 0f;
        incorrectByCategory.Clear();
        LastReport = default;

        SetState(ShiftState.Vardiya);
        PublishTime();

        if (itemSpawner == null)
            itemSpawner = FindFirstObjectByType<ItemSpawner>();

        if (itemSpawner == null)
        {
            Debug.LogWarning("ShiftManager: ItemSpawner bulunamadı. Vardiya başladı, ancak eşya üretilemeyecek.", this);
        }
        else
        {
            // Mimari kural 4: ayni seed -> iki istemcide ayni esya sirasi.
            if (synchronizedSeed.HasValue)
                itemSpawner.SetSeed(synchronizedSeed.Value);

            itemSpawner.BeginShift();

            // Esyayi yalnizca otorite uretir; istemcide Alteruna Spawner kopyasini getirir.
            if (runAuthoritativeTimer)
                itemSpawner.SpawnNext();
        }

        // Istemci kendi saatini isletmez, host'un ApplyClock yayinini uygular.
        if (runAuthoritativeTimer)
            timerRoutine = StartCoroutine(RunShiftTimer());
    }

    /// <summary>
    /// B'nin CategorySocket sistemi oyuncu bir eşyayı dolaba bıraktığında bu metodu çağırır.
    ///
    /// Mimari kural 2: TUM durum degisikligi bu tek metottan gecer. Odadayken
    /// karar host'a gonderilir, skoru host tutar; sonucu herkes ApplyDecisionFromNetwork
    /// ile ayni sirayla uygular. Odada degilsek dogrudan uygulanir (Faz 1).
    /// </summary>
    public void RegisterDecision(
        int itemId,
        ItemCategory correct,
        ItemCategory chosen,
        float inspectMs,
        int shakeCount)
    {
        NetworkShiftCoordinator coordinator = ResolveCoordinator();

        if (coordinator != null && coordinator.IsInRoom)
        {
            coordinator.SubmitDecision(itemId, correct, chosen, inspectMs, shakeCount);
            return;
        }

        ApplyDecisionFromNetwork(itemId, correct, chosen, inspectMs, shakeCount);
    }

    /// <summary>
    /// Karari FIILEN uygulayan metot. Cevrimdisi oyunda RegisterDecision dogrudan
    /// buraya duser; odadayken yalnizca host'un onayladigi karar buraya gelir.
    /// </summary>
    public void ApplyDecisionFromNetwork(
        int itemId,
        ItemCategory correct,
        ItemCategory chosen,
        float inspectMs,
        int shakeCount)
    {
        if (State != ShiftState.Vardiya)
        {
            Debug.LogWarning("ShiftManager: Vardiya aktif değilken karar kaydedilemez.", this);
            return;
        }

        bool isCorrect = correct == chosen;
        float safeInspectMs = Mathf.Max(0f, inspectMs);

        DecisionResult result = new DecisionResult
        {
            itemId = itemId,
            correct = correct,
            chosen = chosen,
            shakeCount = Mathf.Max(0, shakeCount),
            isCorrect = isCorrect,
            inspectMs = safeInspectMs,
            explanation = BuildExplanation(correct, isCorrect)
        };

        inspectedItemCount++;
        totalInspectMs += safeInspectMs;
        totalShakeCount += result.shakeCount;

        if (isCorrect)
        {
            correctCount++;
        }
        else
        {
            incorrectCount++;
            incorrectByCategory[correct] = GetIncorrectCount(correct) + 1;
        }

        OnDecision?.Invoke(result);
        QueueNextItem();
    }

    /// <summary>
    /// Vardiyayı dışarıdan erken bitirmek gerekirse kullanılabilir.
    /// Odadayken bitirme karari host'undur; istemci bekler.
    /// </summary>
    public void EndShift()
    {
        if (IsNetworkClient)
            return;

        CompleteShift();
    }

    /// <summary>Host "vardiya bitti" dedi; istemci raporu ayni anda acar.</summary>
    public void EndShiftFromNetwork()
    {
        CompleteShift();
    }

    /// <summary>
    /// Host'un yayinladigi kalan sure. Istemci kendi saatini isletmedigi icin
    /// HUD yalnizca bu degerle guncellenir.
    /// </summary>
    public void ApplyNetworkClock(float synchronizedRemainingSeconds)
    {
        if (State != ShiftState.Vardiya)
            return;

        remainingSeconds = Mathf.Clamp(synchronizedRemainingSeconds, 0f, shiftDurationSeconds);
        PublishTime();
    }

    private void CompleteShift()
    {
        if (State != ShiftState.Vardiya)
            return;

        if (timerRoutine != null)
        {
            StopCoroutine(timerRoutine);
            timerRoutine = null;
        }

        if (nextItemRoutine != null)
        {
            StopCoroutine(nextItemRoutine);
            nextItemRoutine = null;
        }

        remainingSeconds = 0f;
        PublishTime();
        itemSpawner?.StopSpawning();

        LastReport = BuildReport();
        SetState(ShiftState.Rapor);
        OnReportReady?.Invoke(LastReport);
    }

    private IEnumerator RunShiftTimer()
    {
        while (remainingSeconds > 0f)
        {
            yield return null;
            remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.deltaTime);
            PublishTime();
        }

        timerRoutine = null;
        EndShift();
    }

    private void QueueNextItem()
    {
        if (itemSpawner == null || State != ShiftState.Vardiya)
            return;

        // Sonraki esyayi yalnizca otorite uretir. Istemci de uretirse tezgahta
        // iki esya olur ve iki cihaz farkli seyler gorur.
        if (IsNetworkClient)
            return;

        if (nextItemRoutine != null)
            StopCoroutine(nextItemRoutine);

        nextItemRoutine = StartCoroutine(SpawnNextItemAfterDelay());
    }

    private IEnumerator SpawnNextItemAfterDelay()
    {
        if (nextItemDelaySeconds > 0f)
            yield return new WaitForSeconds(nextItemDelaySeconds);

        if (State == ShiftState.Vardiya)
            itemSpawner.SpawnNext();

        nextItemRoutine = null;
    }

    private void StopActiveRoutines()
    {
        if (timerRoutine != null)
        {
            StopCoroutine(timerRoutine);
            timerRoutine = null;
        }

        if (nextItemRoutine != null)
        {
            StopCoroutine(nextItemRoutine);
            nextItemRoutine = null;
        }
    }

    private void SetState(ShiftState newState)
    {
        if (State == newState)
            return;

        State = newState;
        OnStateChanged?.Invoke(State);
    }

    private void PublishTime()
    {
        int wholeSeconds = Mathf.CeilToInt(remainingSeconds);
        if (wholeSeconds == lastPublishedWholeSecond)
            return;

        lastPublishedWholeSecond = wholeSeconds;
        OnTimeChanged?.Invoke(remainingSeconds);
    }

    private ShiftReport BuildReport()
    {
        ShiftReport report = new ShiftReport
        {
            sessionId = currentSessionId,
            correctCount = correctCount,
            incorrectCount = incorrectCount,
            inspectedItemCount = inspectedItemCount,
            totalShakeCount = totalShakeCount,
            averageInspectMs = inspectedItemCount == 0
                ? 0f
                : totalInspectMs / inspectedItemCount
        };

        int highestIncorrectCount = 0;
        foreach (KeyValuePair<ItemCategory, int> pair in incorrectByCategory)
        {
            if (pair.Value <= highestIncorrectCount)
                continue;

            highestIncorrectCount = pair.Value;
            report.hasMostConfusedCategory = true;
            report.mostConfusedCategory = pair.Key;
        }

        return report;
    }

    private int GetIncorrectCount(ItemCategory category)
    {
        return incorrectByCategory.TryGetValue(category, out int count) ? count : 0;
    }

    private static string BuildExplanation(ItemCategory correctCategory, bool isCorrect)
    {
        string evidence;
        switch (correctCategory)
        {
            case ItemCategory.Sesli:
                evidence = "Bu eşya sallandığında ses çıkarıyordu.";
                break;
            case ItemCategory.Parlak:
                evidence = "Bu eşya yakındayken parlıyordu.";
                break;
            case ItemCategory.Agir:
                evidence = "Bu eşya elde sürekli titreşim yaratıyordu.";
                break;
            default:
                evidence = "Eşyanın verdiği sinyali tekrar kontrol et.";
                break;
        }

        return isCorrect ? "Doğru. " + evidence : "Yanlış. " + evidence;
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Start Shift")]
    private void DebugStartShift()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Önce Play Mode'a gir, sonra bu komutu tekrar çalıştır.", this);
            return;
        }

        StartShift();
    }

    [ContextMenu("Debug/Register Correct Decision")]
    private void DebugRegisterCorrectDecision()
    {
        RegisterDebugDecision(ItemCategory.Sesli, ItemCategory.Sesli);
    }

    [ContextMenu("Debug/Register Wrong Decision")]
    private void DebugRegisterWrongDecision()
    {
        RegisterDebugDecision(ItemCategory.Parlak, ItemCategory.Agir);
    }

    [ContextMenu("Debug/End Shift")]
    private void DebugEndShift()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Önce Play Mode'a gir, sonra bu komutu tekrar çalıştır.", this);
            return;
        }

        EndShift();
    }

    private void RegisterDebugDecision(ItemCategory correct, ItemCategory chosen)
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Önce Play Mode'a gir, sonra bu komutu tekrar çalıştır.", this);
            return;
        }

        RegisterDecision(9000 + inspectedItemCount, correct, chosen, 1250f, 2);
    }
#endif
}
