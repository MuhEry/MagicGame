using System.Collections;
using UnityEngine;

/// <summary>
/// Bacadan esya duserken ses + isik (ve istege bagli particle).
///
/// Amaci sadece susleme degil: oyuncu sabit duruyor ve bacaya bakmak zorunda
/// degil. Sartnamedeki cekirdek dongu "Bacadan esya duser (ses + isik)" diye
/// basliyor - yeni esyanin geldigini DUYARAK anlamali, gozuyle aramamali.
///
/// Bu bilesen ItemSpawner.ItemSpawned event'ine baglanir. Update icinde hicbir
/// sey yapmaz, spawner'i yoklamaz (mimari kural 7).
///
/// Sahnede Baca_SpawnPoint objesinin uzerinde durur.
/// </summary>
[AddComponentMenu("Kayip Esya/Chimney Effect")]
[DisallowMultipleComponent]
public class ChimneyEffect : MonoBehaviour
{
    [Header("Kaynak")]
    [Tooltip("Bos birakilirsa sahnede aranir.")]
    [SerializeField]
    ItemSpawner m_Spawner;

    [Header("Isik")]
    [Tooltip("Bos birakilirsa bu objedeki Light kullanilir.")]
    [SerializeField]
    Light m_Light;

    [SerializeField]
    Color m_FlashColor = new Color(1f, 0.85f, 0.55f);

    [Tooltip("Parlamanin tepe siddeti.")]
    [SerializeField, Min(0f)]
    float m_FlashIntensity = 4f;

    [Tooltip("Parlamanin sonme suresi (saniye).")]
    [SerializeField, Min(0.01f)]
    float m_FlashDuration = 0.45f;

    [SerializeField, Min(0f)]
    float m_LightRange = 3.5f;

    [Header("Ses")]
    [Tooltip("Bos birakilirsa bu objedeki AudioSource kullanilir.")]
    [SerializeField]
    AudioSource m_AudioSource;

    [SerializeField]
    AudioClip m_DropClip;

    [Tooltip("Ayni ses her seferinde ayni duymasin diye perde sapmasi. 0 = kapali.")]
    [SerializeField, Range(0f, 0.5f)]
    float m_PitchJitter = 0.1f;

    [Header("Bonus")]
    [Tooltip("Istege bagli, bos birakilabilir.")]
    [SerializeField]
    ParticleSystem m_Burst;

    // UnityEngine.Random KULLANILMIYOR - mimari kural 4.
    static readonly System.Random s_Random = new System.Random();

    Coroutine m_FlashRoutine;

    void Awake()
    {
        if (m_Light == null)
            m_Light = GetComponent<Light>();

        if (m_AudioSource == null)
            m_AudioSource = GetComponent<AudioSource>();

        if (m_Light != null)
        {
            m_Light.color = m_FlashColor;
            m_Light.range = m_LightRange;
            m_Light.intensity = 0f;
            m_Light.enabled = false; // bosta kapali dursun, her karede maliyeti olmasin
        }
    }

    void OnEnable()
    {
        Subscribe();
    }

    void OnDisable()
    {
        if (m_Spawner != null)
            m_Spawner.ItemSpawned -= OnItemSpawned;

        // Coroutine oldu; isigi acik birakma.
        m_FlashRoutine = null;
        if (m_Light != null)
        {
            m_Light.intensity = 0f;
            m_Light.enabled = false;
        }
    }

    void Subscribe()
    {
        if (m_Spawner == null)
            m_Spawner = FindFirstObjectByType<ItemSpawner>();

        if (m_Spawner == null)
        {
            Debug.LogWarning("[ChimneyEffect] Sahnede ItemSpawner bulunamadi - baca efekti calismaz.", this);
            return;
        }

        // Cift abonelikten korun (OnEnable birden fazla kez calisabilir).
        m_Spawner.ItemSpawned -= OnItemSpawned;
        m_Spawner.ItemSpawned += OnItemSpawned;
    }

    void OnItemSpawned(GameObject item)
    {
        Play();
    }

    /// <summary>Ses + isik + particle'i ayni anda tetikler.</summary>
    public void Play()
    {
        // Ses
        if (m_AudioSource != null && m_DropClip != null)
        {
            m_AudioSource.pitch = m_PitchJitter > 0f
                ? 1f + (float)((s_Random.NextDouble() * 2.0 - 1.0) * m_PitchJitter)
                : 1f;

            m_AudioSource.PlayOneShot(m_DropClip);
        }

        // Isik
        if (m_Light != null)
        {
            if (m_FlashRoutine != null)
                StopCoroutine(m_FlashRoutine);

            m_FlashRoutine = StartCoroutine(FlashRoutine());
        }

        // Bonus
        if (m_Burst != null)
            m_Burst.Play();
    }

    IEnumerator FlashRoutine()
    {
        m_Light.color = m_FlashColor;
        m_Light.range = m_LightRange;
        m_Light.enabled = true;

        var elapsed = 0f;
        while (elapsed < m_FlashDuration)
        {
            elapsed += Time.deltaTime;

            // Basta sert parlar, sonra yumusakca soner.
            var t = Mathf.Clamp01(elapsed / m_FlashDuration);
            m_Light.intensity = Mathf.Lerp(m_FlashIntensity, 0f, t * t);

            yield return null;
        }

        m_Light.intensity = 0f;
        m_Light.enabled = false;
        m_FlashRoutine = null;
    }

    // Gozluk takmadan denemek icin: Inspector -> bilesenin sag ustundeki ... menusu.
    [ContextMenu("TEST: Baca efektini oynat")]
    void TestPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Once Play Mode'a gir, sonra bu komutu tekrar calistir.", this);
            return;
        }

        Play();
    }
}
