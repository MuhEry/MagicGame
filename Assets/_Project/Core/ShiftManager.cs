using System;
using System.Collections;
using System.Collections.Generic;
using Alteruna.Multiplayer.Unity;
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

public class ShiftManager : AttributesSync
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

    private readonly Dictionary<ItemCategory, int> incorrectByCategory =
        new Dictionary<ItemCategory, int>();

    private Coroutine timerRoutine;
    private Coroutine nextItemRoutine;
    [SynchronizableField] private float remainingSeconds;
    private float totalInspectMs;
    [SynchronizableField] private int correctCount;
    [SynchronizableField] private int incorrectCount;
    [SynchronizableField] private int inspectedItemCount;
    [SynchronizableField] private int totalShakeCount;
    [SynchronizableField] private int currentSessionId;
    [SynchronizableField] private int stateValue;
    [SynchronizableField] private int shiftSeed;
    private int lastPublishedWholeSecond = -1;
    private ShiftState lastObservedState = ShiftState.Hazir;
    private int lastDecisionItemId = -1;
    private ItemCategory lastDecisionCategory;
    private float lastDecisionTime = float.NegativeInfinity;

    public ShiftState State => (ShiftState)stateValue;
    public float RemainingSeconds => remainingSeconds;
    public int CurrentSessionId => currentSessionId;
    public int CorrectCount => correctCount;
    public int IncorrectCount => incorrectCount;
    public int InspectedItemCount => inspectedItemCount;
    public int TotalShakeCount => totalShakeCount;
    public int Score => correctCount;
    public ShiftReport LastReport { get; private set; }
    public bool IsHostAuthority => Multiplayer != null && Multiplayer.InRoom && Multiplayer.IsHost();

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

        lastObservedState = State;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    private void Update()
    {
        if (IsHostAuthority)
            return;

        if (State != lastObservedState)
        {
            lastObservedState = State;
            OnStateChanged?.Invoke(State);
        }

        PublishTime(false);
    }

    private void OnValidate()
    {
        shiftDurationSeconds = Mathf.Max(1f, shiftDurationSeconds);
        nextItemDelaySeconds = Mathf.Max(0f, nextItemDelaySeconds);
    }

    /// <summary>
    /// UI'daki "Yeni Vardiya" butonunun çağıracağı metot.
    /// </summary>
    public void StartShift()
    {
        if (!IsHostAuthority)
        {
            Debug.LogWarning("[ShiftNet] Vardiyayi yalnizca LAN hostu baslatabilir.", this);
            return;
        }

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
            itemSpawner.BeginShift();
            shiftSeed = itemSpawner.Seed;
            itemSpawner.SpawnNext();
        }

        ForceSync();
        timerRoutine = StartCoroutine(RunShiftTimer());
    }

    /// <summary>
    /// B'nin CategorySocket sistemi oyuncu bir eşyayı dolaba bıraktığında bu metodu çağırır.
    /// </summary>
    public void RegisterDecision(
        int itemId,
        ItemCategory correct,
        ItemCategory chosen,
        float inspectMs,
        int shakeCount)
    {
        if (!IsHostAuthority)
        {
            if (Multiplayer == null || !Multiplayer.InRoom)
            {
                Debug.LogWarning("[ShiftNet] Karar gonderilemedi; LAN odasinda degilsin.", this);
                return;
            }

            BroadcastRemoteMethod(
                nameof(ReceiveDecisionRequest),
                itemId,
                (int)correct,
                (int)chosen,
                inspectMs,
                shakeCount);
            return;
        }

        ApplyHostDecision(itemId, correct, chosen, inspectMs, shakeCount);
    }

    [SynchronizableMethod]
    private void ReceiveDecisionRequest(
        int itemId,
        int correct,
        int chosen,
        float inspectMs,
        int shakeCount)
    {
        if (!IsHostAuthority)
            return;

        ApplyHostDecision(
            itemId,
            (ItemCategory)correct,
            (ItemCategory)chosen,
            inspectMs,
            shakeCount);
    }

    private void ApplyHostDecision(
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

        if (itemSpawner == null || itemSpawner.CurrentSpawnedItem == null)
        {
            Debug.LogWarning("[ShiftNet] Aktif ag esyasi yok; karar yok sayildi.", this);
            return;
        }

        ItemIdentity activeIdentity = itemSpawner.CurrentSpawnedItem.GetComponentInChildren<ItemIdentity>();
        if (activeIdentity != null && activeIdentity.ItemId != itemId)
        {
            Debug.LogWarning($"[ShiftNet] Eski/farkli esya karari reddedildi. Gelen={itemId} Aktif={activeIdentity.ItemId}", this);
            return;
        }

        if (lastDecisionItemId == itemId &&
            lastDecisionCategory == chosen &&
            Time.unscaledTime - lastDecisionTime < 0.75f)
        {
            Debug.Log("[ShiftNet] Yinelenen soket karari yok sayildi.", this);
            return;
        }

        lastDecisionItemId = itemId;
        lastDecisionCategory = chosen;
        lastDecisionTime = Time.unscaledTime;

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
        BroadcastRemoteMethod(
            nameof(ReceiveDecisionResult),
            itemId,
            (int)correct,
            (int)chosen,
            result.isCorrect,
            result.inspectMs,
            result.shakeCount,
            result.explanation,
            correctCount,
            incorrectCount,
            inspectedItemCount,
            totalShakeCount);

        ForceSync();

        if (isCorrect)
        {
            itemSpawner.Despawn(itemSpawner.CurrentSpawnedItem);
            QueueNextItem();
        }
    }

    [SynchronizableMethod]
    private void ReceiveDecisionResult(
        int itemId,
        int correct,
        int chosen,
        bool isCorrect,
        float inspectMs,
        int shakeCount,
        string explanation,
        int syncedCorrectCount,
        int syncedIncorrectCount,
        int syncedInspectedCount,
        int syncedShakeCount)
    {
        if (IsHostAuthority)
            return;

        correctCount = syncedCorrectCount;
        incorrectCount = syncedIncorrectCount;
        inspectedItemCount = syncedInspectedCount;
        totalShakeCount = syncedShakeCount;

        OnDecision?.Invoke(new DecisionResult
        {
            itemId = itemId,
            correct = (ItemCategory)correct,
            chosen = (ItemCategory)chosen,
            isCorrect = isCorrect,
            inspectMs = inspectMs,
            shakeCount = shakeCount,
            explanation = explanation
        });
    }

    /// <summary>
    /// Vardiyayı dışarıdan erken bitirmek gerekirse kullanılabilir.
    /// </summary>
    public void EndShift()
    {
        if (!IsHostAuthority)
            return;

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
        BroadcastRemoteMethod(
            nameof(ReceiveShiftReport),
            LastReport.sessionId,
            LastReport.correctCount,
            LastReport.incorrectCount,
            LastReport.inspectedItemCount,
            LastReport.totalShakeCount,
            LastReport.averageInspectMs,
            LastReport.hasMostConfusedCategory,
            (int)LastReport.mostConfusedCategory);
        ForceSync();
    }

    [SynchronizableMethod]
    private void ReceiveShiftReport(
        int sessionId,
        int reportCorrectCount,
        int reportIncorrectCount,
        int reportInspectedCount,
        int reportShakeCount,
        float averageInspectMs,
        bool hasMostConfusedCategory,
        int mostConfusedCategory)
    {
        if (IsHostAuthority)
            return;

        LastReport = new ShiftReport
        {
            sessionId = sessionId,
            correctCount = reportCorrectCount,
            incorrectCount = reportIncorrectCount,
            inspectedItemCount = reportInspectedCount,
            totalShakeCount = reportShakeCount,
            averageInspectMs = averageInspectMs,
            hasMostConfusedCategory = hasMostConfusedCategory,
            mostConfusedCategory = (ItemCategory)mostConfusedCategory
        };
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

        stateValue = (int)newState;
        lastObservedState = newState;
        OnStateChanged?.Invoke(newState);
    }

    private void PublishTime(bool sync = true)
    {
        int wholeSeconds = Mathf.CeilToInt(remainingSeconds);
        if (wholeSeconds == lastPublishedWholeSecond)
            return;

        lastPublishedWholeSecond = wholeSeconds;
        OnTimeChanged?.Invoke(remainingSeconds);

        if (sync && IsHostAuthority)
            ForceSync();
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
