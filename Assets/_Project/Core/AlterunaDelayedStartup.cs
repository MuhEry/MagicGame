using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class AlterunaDelayedStartup : MonoBehaviour
{
    [SerializeField] private AlterunaComponents.MultiplayerManager multiplayerManager;
    [SerializeField, Min(0f)] private float startupDelaySeconds = 5f;

    private Coroutine startupRoutine;

    private void Awake()
    {
        if (multiplayerManager == null)
            multiplayerManager = GetComponent<AlterunaComponents.MultiplayerManager>();

        if (multiplayerManager == null)
        {
            Debug.LogError("[AlterunaStartup] MultiplayerManager bulunamadi.", this);
            enabled = false;
            return;
        }

        if (multiplayerManager.enabled)
        {
            multiplayerManager.enabled = false;
            Debug.LogWarning("[AlterunaStartup] MultiplayerManager baslangicta kapatildi.", this);
        }
    }

    private void OnEnable()
    {
        if (Application.isPlaying && multiplayerManager != null && startupRoutine == null)
            startupRoutine = StartCoroutine(EnableManagerAfterDelay());
    }

    private void OnDisable()
    {
        if (startupRoutine == null)
            return;

        StopCoroutine(startupRoutine);
        startupRoutine = null;
    }

    private IEnumerator EnableManagerAfterDelay()
    {
        // Manager'i ayni karede acip kapatmak Awake/OnEnable asamasini calistirir,
        // fakat Start bir sonraki kareye kalmadan tekrar kapandigi icin baslamaz.
        // Boylece lisans sorgusu ana baglanti kurulmadan once zaman kazanir.
        multiplayerManager.enabled = true;
        multiplayerManager.enabled = false;

        Debug.Log("[AlterunaStartup] MultiplayerManager Awake tamamlandi; Start geciktirildi.", this);
        Debug.Log($"[AlterunaStartup] Lisans dogrulamasi icin {startupDelaySeconds:0.##} saniye bekleniyor.", this);
        yield return new WaitForSecondsRealtime(startupDelaySeconds);

        if (multiplayerManager == null)
            yield break;

        Debug.Log("[AlterunaStartup] Bekleme tamamlandi, MultiplayerManager etkinlestiriliyor.", this);
        multiplayerManager.enabled = true;
        startupRoutine = null;
    }
}
