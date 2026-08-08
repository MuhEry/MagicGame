using TMPro;
using UnityEngine;

/// <summary>
/// World-space Canvas'ı ShiftManager event'leriyle günceller. Update içinde oyun değeri hesaplamaz.
/// </summary>
public class ShiftHudPresenter : MonoBehaviour
{
    [SerializeField] private ShiftManager shiftManager;

    [Header("Vardiya HUD")]
    [SerializeField] private TMP_Text remainingTimeText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text lastDecisionText;
    [SerializeField] private TMP_Text stateText;

    [Header("Rapor")]
    [SerializeField] private GameObject reportPanel;
    [SerializeField] private TMP_Text reportText;

    private bool isSubscribed;
    private LocalPlayerHud localPlayerHud;

    private void Awake()
    {
        localPlayerHud = GetComponent<LocalPlayerHud>();

        if (shiftManager == null)
            shiftManager = ShiftManager.Instance;
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
        RefreshFromManager();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    /// <summary>
    /// XR Poke/Ray butonunun UnityEvent'ine bu parametresiz metot bağlanır.
    /// </summary>
    public void StartNewShiftFromButton()
    {
        if (shiftManager == null)
            shiftManager = ShiftManager.Instance;

        shiftManager?.StartShift();
    }

    private void TrySubscribe()
    {
        if (isSubscribed)
            return;

        if (shiftManager == null)
            shiftManager = ShiftManager.Instance;

        if (shiftManager == null)
            return;

        shiftManager.OnTimeChanged += UpdateRemainingTime;
        shiftManager.OnDecision += ShowDecision;
        shiftManager.OnStateChanged += ShowState;
        shiftManager.OnReportReady += ShowReport;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || shiftManager == null)
            return;

        shiftManager.OnTimeChanged -= UpdateRemainingTime;
        shiftManager.OnDecision -= ShowDecision;
        shiftManager.OnStateChanged -= ShowState;
        shiftManager.OnReportReady -= ShowReport;
        isSubscribed = false;
    }

    private void RefreshFromManager()
    {
        if (shiftManager == null)
            return;

        UpdateRemainingTime(shiftManager.RemainingSeconds);
        UpdateScore();
        ShowState(shiftManager.State);

        if (shiftManager.State == ShiftState.Rapor)
            ShowReport(shiftManager.LastReport);
    }

    private void UpdateRemainingTime(float seconds)
    {
        if (remainingTimeText == null)
            return;

        int wholeSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        remainingTimeText.text = $"Kalan süre: {wholeSeconds / 60:00}:{wholeSeconds % 60:00}";
    }

    private void ShowDecision(DecisionResult result)
    {
        UpdateScore();

        if (lastDecisionText == null)
            return;

        string verdict = result.isCorrect ? "DOĞRU" : "YANLIŞ";
        lastDecisionText.text = verdict + "\n" + result.explanation;
    }

    private void ShowState(ShiftState state)
    {
        if (stateText != null)
        {
            string stateLabel = state == ShiftState.Hazir
                ? "Hazır"
                : state == ShiftState.Vardiya ? "Vardiya" : "Vardiya Raporu";
            stateText.text = localPlayerHud != null
                ? localPlayerHud.ContextLabel + "\n" + stateLabel
                : stateLabel;
        }

        if (reportPanel != null)
            reportPanel.SetActive(state == ShiftState.Rapor);
    }

    public void RefreshPlayerContext()
    {
        if (shiftManager == null)
            shiftManager = ShiftManager.Instance;

        ShowState(shiftManager != null ? shiftManager.State : ShiftState.Hazir);
    }

    private void ShowReport(ShiftReport report)
    {
        if (reportPanel != null)
            reportPanel.SetActive(true);

        if (reportText == null)
            return;

        string confusedCategory = report.hasMostConfusedCategory
            ? report.mostConfusedCategory.ToString()
            : "Yok";

        reportText.text =
            "Vardiya Raporu\n" +
            $"Doğru: {report.correctCount}\n" +
            $"Yanlış: {report.incorrectCount}\n" +
            $"Ortalama karar süresi: {report.averageInspectMs:0} ms\n" +
            $"En çok karıştırılan: {confusedCategory}";
        reportText.text += $"\nToplam sallama: {report.totalShakeCount}";
    }

    private void UpdateScore()
    {
        if (scoreText == null || shiftManager == null)
            return;

        scoreText.text = $"Doğru: {shiftManager.CorrectCount}  Yanlış: {shiftManager.IncorrectCount}";
    }
}
