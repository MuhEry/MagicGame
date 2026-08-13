using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Her karar için Application.persistentDataPath altındaki telemetry.csv dosyasına bir satır ekler.
/// </summary>
public class TelemetryLogger : MonoBehaviour
{
    private const string Header =
        "zaman_damgasi,oturum_id,esya_id,dogru_kategori,secilen_kategori,dogru_mu,inceleme_suresi_ms,sallama_sayisi";

    [SerializeField] private ShiftManager shiftManager;

    private bool isSubscribed;

    public string LogFilePath => Path.Combine(Application.persistentDataPath, "telemetry.csv");

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
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void TrySubscribe()
    {
        if (isSubscribed)
            return;

        if (shiftManager == null)
            shiftManager = ShiftManager.Instance;

        if (shiftManager == null)
            return;

        shiftManager.OnDecision += WriteDecision;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || shiftManager == null)
            return;

        shiftManager.OnDecision -= WriteDecision;
        isSubscribed = false;
    }

    private void WriteDecision(DecisionResult result)
    {
        if (shiftManager == null || !shiftManager.IsHostAuthority)
            return;

        try
        {
            string directory = Application.persistentDataPath;
            Directory.CreateDirectory(directory);

            bool fileExists = File.Exists(LogFilePath);
            StringBuilder output = new StringBuilder();

            if (!fileExists)
                output.AppendLine(Header);

            int sessionId = shiftManager != null ? shiftManager.CurrentSessionId : 0;
            output.Append(Escape(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            output.Append(sessionId).Append(',');
            output.Append(result.itemId).Append(',');
            output.Append(Escape(result.correct.ToString())).Append(',');
            output.Append(Escape(result.chosen.ToString())).Append(',');
            output.Append(result.isCorrect ? "true" : "false").Append(',');
            output.Append(result.inspectMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            output.Append(result.shakeCount).AppendLine();

            File.AppendAllText(LogFilePath, output.ToString(), new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            Debug.LogError("TelemetryLogger: CSV yazılamadı. " + exception.Message, this);
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
