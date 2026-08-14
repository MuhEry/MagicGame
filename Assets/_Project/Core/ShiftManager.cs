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
    public int player0Correct;
    public int player0Incorrect;
    public int player0Score;
    public int player0ShiftWins;
    public float player0AverageInspectMs;
    public bool player0HasMostConfusedCategory;
    public ItemCategory player0MostConfusedCategory;
    public int player1Correct;
    public int player1Incorrect;
    public int player1Score;
    public int player1ShiftWins;
    public float player1AverageInspectMs;
    public bool player1HasMostConfusedCategory;
    public ItemCategory player1MostConfusedCategory;
    public int winnerIndex;
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
    private readonly Dictionary<ItemCategory, int> player0IncorrectByCategory =
        new Dictionary<ItemCategory, int>();
    private readonly Dictionary<ItemCategory, int> player1IncorrectByCategory =
        new Dictionary<ItemCategory, int>();

    private Coroutine timerRoutine;
    private Coroutine nextItemRoutine;
    [SynchronizableField] private float remainingSeconds;
    private float totalInspectMs;
    private float player0TotalInspectMs;
    private float player1TotalInspectMs;
    [SynchronizableField] private int correctCount;
    [SynchronizableField] private int incorrectCount;
    [SynchronizableField] private int inspectedItemCount;
    [SynchronizableField] private int totalShakeCount;
    [SynchronizableField] private int currentSessionId;
    [SynchronizableField] private int stateValue;
    [SynchronizableField] private int shiftSeed;
    [SynchronizableField] private int player0Correct;
    [SynchronizableField] private int player0Incorrect;
    [SynchronizableField] private int player1Correct;
    [SynchronizableField] private int player1Incorrect;
    [SynchronizableField] private int player0ShiftWins;
    [SynchronizableField] private int player1ShiftWins;
    private int lastPublishedWholeSecond = -1;
    private ShiftState lastObservedState = ShiftState.Hazir;
    private int lastDecisionItemId = -1;
    private ItemCategory lastDecisionCategory;
    private int lastDecisionPlayerIndex = -1;
    private float lastDecisionTime = float.NegativeInfinity;

    public ShiftState State => (ShiftState)stateValue;
    public float RemainingSeconds => remainingSeconds;
    public int CurrentSessionId => currentSessionId;
    public int CorrectCount => correctCount;
    public int IncorrectCount => incorrectCount;
    public int InspectedItemCount => inspectedItemCount;
    public int TotalShakeCount => totalShakeCount;
    public int Score => correctCount - incorrectCount;
    public ShiftReport LastReport { get; private set; }
    public bool IsHostAuthority => Multiplayer != null && Multiplayer.InRoom && Multiplayer.IsHost();
    public int LocalPlayerIndex => Multiplayer != null && Multiplayer.InRoom ? Multiplayer.Me.Index : -1;
    public int Player0Correct => player0Correct;
    public int Player0Incorrect => player0Incorrect;
    public int Player0Score => player0Correct - player0Incorrect;
    public int Player0ShiftWins => player0ShiftWins;
    public int Player1Correct => player1Correct;
    public int Player1Incorrect => player1Incorrect;
    public int Player1Score => player1Correct - player1Incorrect;
    public int Player1ShiftWins => player1ShiftWins;

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
        player0Correct = 0;
        player0Incorrect = 0;
        player1Correct = 0;
        player1Incorrect = 0;
        totalInspectMs = 0f;
        player0TotalInspectMs = 0f;
        player1TotalInspectMs = 0f;
        incorrectByCategory.Clear();
        player0IncorrectByCategory.Clear();
        player1IncorrectByCategory.Clear();
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
            itemSpawner.FillSpawnSlots();
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
        int shakeCount,
        int playerIndex = -1)
    {
        if (playerIndex < 0)
            playerIndex = LocalPlayerIndex;

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
                shakeCount,
                playerIndex);
            return;
        }

        ApplyHostDecision(itemId, correct, chosen, inspectMs, shakeCount, playerIndex);
    }

    [SynchronizableMethod]
    private void ReceiveDecisionRequest(
        int itemId,
        int correct,
        int chosen,
        float inspectMs,
        int shakeCount,
        int playerIndex)
    {
        if (!IsHostAuthority)
            return;

        ApplyHostDecision(
            itemId,
            (ItemCategory)correct,
            (ItemCategory)chosen,
            inspectMs,
            shakeCount,
            playerIndex);
    }

    private void ApplyHostDecision(
        int itemId,
        ItemCategory correct,
        ItemCategory chosen,
        float inspectMs,
        int shakeCount,
        int playerIndex)
    {
        if (State != ShiftState.Vardiya)
        {
            Debug.LogWarning("ShiftManager: Vardiya aktif değilken karar kaydedilemez.", this);
            return;
        }

        if (playerIndex < 0 || playerIndex > 1)
        {
            Debug.LogWarning($"[ShiftNet] Gecersiz oyuncu indexi: {playerIndex}", this);
            return;
        }

        if (itemSpawner == null || !itemSpawner.TryGetSpawnedItem(itemId, out GameObject activeItem))
        {
            Debug.LogWarning($"[ShiftNet] Aktif ag esyasi bulunamadi. Item={itemId}", this);
            return;
        }

        if (lastDecisionItemId == itemId &&
            lastDecisionCategory == chosen &&
            lastDecisionPlayerIndex == playerIndex &&
            Time.unscaledTime - lastDecisionTime < 0.75f)
        {
            Debug.Log("[ShiftNet] Yinelenen soket karari yok sayildi.", this);
            return;
        }

        lastDecisionItemId = itemId;
        lastDecisionCategory = chosen;
        lastDecisionPlayerIndex = playerIndex;
        lastDecisionTime = Time.unscaledTime;

        bool isCorrect = correct == chosen;
        float safeInspectMs = Mathf.Max(0f, inspectMs);

        DecisionResult result = new DecisionResult
        {
            playerIndex = playerIndex,
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
        if (playerIndex == 0) player0TotalInspectMs += safeInspectMs;
        else player1TotalInspectMs += safeInspectMs;

        if (isCorrect)
        {
            correctCount++;
            if (playerIndex == 0) player0Correct++; else player1Correct++;
        }
        else
        {
            incorrectCount++;
            if (playerIndex == 0) player0Incorrect++; else player1Incorrect++;
            incorrectByCategory[correct] = GetIncorrectCount(correct) + 1;
            Dictionary<ItemCategory, int> playerMistakes =
                playerIndex == 0 ? player0IncorrectByCategory : player1IncorrectByCategory;
            playerMistakes[correct] = GetIncorrectCount(playerMistakes, correct) + 1;
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
            playerIndex,
            player0Correct,
            player0Incorrect,
            player1Correct,
            player1Incorrect);

        ForceSync();

        if (isCorrect)
        {
            itemSpawner.Despawn(activeItem);
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
        int playerIndex,
        int syncedPlayer0Correct,
        int syncedPlayer0Incorrect,
        int syncedPlayer1Correct,
        int syncedPlayer1Incorrect)
    {
        if (IsHostAuthority)
            return;

        player0Correct = syncedPlayer0Correct;
        player0Incorrect = syncedPlayer0Incorrect;
        player1Correct = syncedPlayer1Correct;
        player1Incorrect = syncedPlayer1Incorrect;
        correctCount = player0Correct + player1Correct;
        incorrectCount = player0Incorrect + player1Incorrect;
        inspectedItemCount = correctCount + incorrectCount;

        OnDecision?.Invoke(new DecisionResult
        {
            playerIndex = playerIndex,
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
        itemSpawner?.EndShift();

        int winnerIndex = GetWinnerIndex();
        if (winnerIndex == 0)
            player0ShiftWins++;
        else if (winnerIndex == 1)
            player1ShiftWins++;

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
            (int)LastReport.mostConfusedCategory,
            LastReport.player0Correct,
            LastReport.player0Incorrect,
            LastReport.player0ShiftWins,
            LastReport.player0AverageInspectMs,
            LastReport.player0HasMostConfusedCategory,
            (int)LastReport.player0MostConfusedCategory,
            LastReport.player1Correct,
            LastReport.player1Incorrect,
            LastReport.player1ShiftWins,
            LastReport.player1AverageInspectMs,
            LastReport.player1HasMostConfusedCategory,
            (int)LastReport.player1MostConfusedCategory,
            LastReport.winnerIndex);
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
        int mostConfusedCategory,
        int reportPlayer0Correct,
        int reportPlayer0Incorrect,
        int reportPlayer0ShiftWins,
        float reportPlayer0AverageInspectMs,
        bool reportPlayer0HasMostConfusedCategory,
        int reportPlayer0MostConfusedCategory,
        int reportPlayer1Correct,
        int reportPlayer1Incorrect,
        int reportPlayer1ShiftWins,
        float reportPlayer1AverageInspectMs,
        bool reportPlayer1HasMostConfusedCategory,
        int reportPlayer1MostConfusedCategory,
        int winnerIndex)
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
            mostConfusedCategory = (ItemCategory)mostConfusedCategory,
            player0Correct = reportPlayer0Correct,
            player0Incorrect = reportPlayer0Incorrect,
            player0Score = reportPlayer0Correct - reportPlayer0Incorrect,
            player0ShiftWins = reportPlayer0ShiftWins,
            player0AverageInspectMs = reportPlayer0AverageInspectMs,
            player0HasMostConfusedCategory = reportPlayer0HasMostConfusedCategory,
            player0MostConfusedCategory = (ItemCategory)reportPlayer0MostConfusedCategory,
            player1Correct = reportPlayer1Correct,
            player1Incorrect = reportPlayer1Incorrect,
            player1Score = reportPlayer1Correct - reportPlayer1Incorrect,
            player1ShiftWins = reportPlayer1ShiftWins,
            player1AverageInspectMs = reportPlayer1AverageInspectMs,
            player1HasMostConfusedCategory = reportPlayer1HasMostConfusedCategory,
            player1MostConfusedCategory = (ItemCategory)reportPlayer1MostConfusedCategory,
            winnerIndex = winnerIndex
        };
        player0Correct = reportPlayer0Correct;
        player0Incorrect = reportPlayer0Incorrect;
        player0ShiftWins = reportPlayer0ShiftWins;
        player1Correct = reportPlayer1Correct;
        player1Incorrect = reportPlayer1Incorrect;
        player1ShiftWins = reportPlayer1ShiftWins;
        correctCount = reportCorrectCount;
        incorrectCount = reportIncorrectCount;
        inspectedItemCount = reportInspectedCount;
        totalShakeCount = reportShakeCount;
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
            itemSpawner.FillSpawnSlots();

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
        bool player0HasConfused = TryGetMostConfusedCategory(
            player0IncorrectByCategory, out ItemCategory player0Confused);
        bool player1HasConfused = TryGetMostConfusedCategory(
            player1IncorrectByCategory, out ItemCategory player1Confused);

        ShiftReport report = new ShiftReport
        {
            sessionId = currentSessionId,
            correctCount = correctCount,
            incorrectCount = incorrectCount,
            inspectedItemCount = inspectedItemCount,
            totalShakeCount = totalShakeCount,
            averageInspectMs = inspectedItemCount == 0
                ? 0f
                : totalInspectMs / inspectedItemCount,
            player0Correct = player0Correct,
            player0Incorrect = player0Incorrect,
            player0Score = Player0Score,
            player0ShiftWins = player0ShiftWins,
            player0AverageInspectMs = GetPlayerAverageInspectMs(
                player0TotalInspectMs, player0Correct, player0Incorrect),
            player0HasMostConfusedCategory = player0HasConfused,
            player0MostConfusedCategory = player0Confused,
            player1Correct = player1Correct,
            player1Incorrect = player1Incorrect,
            player1Score = Player1Score,
            player1ShiftWins = player1ShiftWins,
            player1AverageInspectMs = GetPlayerAverageInspectMs(
                player1TotalInspectMs, player1Correct, player1Incorrect),
            player1HasMostConfusedCategory = player1HasConfused,
            player1MostConfusedCategory = player1Confused,
            winnerIndex = GetWinnerIndex()
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

    private int GetWinnerIndex()
    {
        if (Player0Score == Player1Score)
            return -1;

        return Player0Score > Player1Score ? 0 : 1;
    }

    private int GetIncorrectCount(ItemCategory category)
    {
        return incorrectByCategory.TryGetValue(category, out int count) ? count : 0;
    }

    private static int GetIncorrectCount(
        Dictionary<ItemCategory, int> mistakes, ItemCategory category)
    {
        return mistakes.TryGetValue(category, out int count) ? count : 0;
    }

    private static float GetPlayerAverageInspectMs(float totalMs, int correct, int incorrect)
    {
        int decisionCount = correct + incorrect;
        return decisionCount == 0 ? 0f : totalMs / decisionCount;
    }

    private static bool TryGetMostConfusedCategory(
        Dictionary<ItemCategory, int> mistakes, out ItemCategory category)
    {
        category = default;
        int highestCount = 0;
        foreach (KeyValuePair<ItemCategory, int> pair in mistakes)
        {
            if (pair.Value <= highestCount)
                continue;

            highestCount = pair.Value;
            category = pair.Key;
        }

        return highestCount > 0;
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
