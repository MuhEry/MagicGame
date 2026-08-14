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
    [SerializeField] private GameObject startShiftButton;

    [Header("Rapor")]
    [SerializeField] private GameObject reportPanel;
    [SerializeField] private TMP_Text reportText;

    private bool isSubscribed;

    private void Awake()
    {
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
        UpdateScore();

        if (remainingTimeText == null)
            return;

        int wholeSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        remainingTimeText.text = $"{wholeSeconds / 60:00}:{wholeSeconds % 60:00}";
    }

    private void ShowDecision(DecisionResult result)
    {
        UpdateScore();

        if (lastDecisionText == null || shiftManager == null ||
            result.playerIndex != shiftManager.LocalPlayerIndex)
            return;

        string verdict = result.isCorrect ? "DOĞRU" : "YANLIŞ";
        lastDecisionText.text = verdict + "\n" + result.explanation;
    }

    private void ShowState(ShiftState state)
    {
        UpdateScore();

        if (stateText != null)
            stateText.text = state == ShiftState.Hazir ? "Hazır" : state == ShiftState.Vardiya ? "Vardiya" : "Vardiya Raporu";

        if (reportPanel != null)
            reportPanel.SetActive(state == ShiftState.Rapor);

        if (startShiftButton != null)
            startShiftButton.SetActive(state != ShiftState.Vardiya);
    }

    private void ShowReport(ShiftReport report)
    {
        UpdateScore();

        if (reportPanel != null)
            reportPanel.SetActive(true);

        if (reportText == null)
            return;

        string player0ConfusedCategory = report.player0HasMostConfusedCategory
            ? report.player0MostConfusedCategory.ToString()
            : "Yok";
        string player1ConfusedCategory = report.player1HasMostConfusedCategory
            ? report.player1MostConfusedCategory.ToString()
            : "Yok";

        string winner = report.winnerIndex < 0
            ? "Berabere"
            : $"Oyuncu {report.winnerIndex + 1}";

        reportText.alignment = TextAlignmentOptions.TopLeft;
        reportText.text =
            "<align=center><size=120%><b>VARDİYA RAPORU</b></size></align>\n" +
            "<size=110%><b><pos=5%>OYUNCU 1<pos=79%>OYUNCU 2</b></size>\n" +
            $"<size=135%><b><pos=9%>{report.player0Score}<pos=39%>Toplam Skor<pos=90%>{report.player1Score}</b></size>\n" +
            $"<pos=10%>{report.player0Incorrect}<pos=38%>Yanlış Eşleştirme<pos=91%>{report.player1Incorrect}\n" +
            $"<pos=10%>{report.player0Correct}<pos=38%>Doğru Eşleştirme<pos=91%>{report.player1Correct}\n" +
            $"<pos=10%>{report.player0ShiftWins}<pos=41%>Vardiya Skoru<pos=91%>{report.player1ShiftWins}\n" +
            $"<pos=5%>{report.player0AverageInspectMs:0} ms<pos=36%>Ort. Karar Süresi<pos=82%>{report.player1AverageInspectMs:0} ms\n" +
            $"<pos=5%>{player0ConfusedCategory}<pos=34%>En Çok Karıştırılan<pos=82%>{player1ConfusedCategory}\n" +
            $"<align=center><b>Kazanan: {winner}</b></align>";
    }

    private void UpdateScore()
    {
        if (scoreText == null || shiftManager == null)
            return;

        scoreText.alignment = TextAlignmentOptions.TopLeft;
        scoreText.text =
            "<size=110%><b><pos=5%>OYUNCU 1<pos=79%>OYUNCU 2</b></size>\n" +
            $"<size=135%><b><pos=9%>{shiftManager.Player0Score}<pos=39%>Toplam Skor<pos=90%>{shiftManager.Player1Score}</b></size>\n" +
            $"<pos=10%>{shiftManager.Player0Incorrect}<pos=38%>Yanlış Eşleştirme<pos=91%>{shiftManager.Player1Incorrect}\n" +
            $"<pos=10%>{shiftManager.Player0Correct}<pos=38%>Doğru Eşleştirme<pos=91%>{shiftManager.Player1Correct}\n" +
            $"<pos=10%>{shiftManager.Player0ShiftWins}<pos=41%>Vardiya Skoru<pos=91%>{shiftManager.Player1ShiftWins}";
    }
}
