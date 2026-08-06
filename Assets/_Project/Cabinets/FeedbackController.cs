using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// Bir dolabin uc kanalli geri bildirimi (sartname - Gelistirici B, madde 3 ve 4).
///
///   Gorsel : dolap materyalinin emissive rengi 0,3 sn yesil / kirmizi
///   Isitsel: ayri dogru / yanlis AudioClip
///   Haptik : dogru -> 0,1 sn / 0,3 genlik, yanlis -> 0,4 sn / 0,8 genlik
///   Bonus  : particle burst
///   Hover  : esya sokete yaklasinca dolabin agzinda hafif highlight
///
/// SARTNAME "Onemli Noktalar #5": emissive'i runtime'da degistirmek icin materyalin
/// Emission'i acik olmali ve MaterialPropertyBlock kullanilmali. Her dolap icin AYRI
/// materyal instance ACILMAZ - uc dolap ayni materyali paylasir, renk MPB ile ezilir.
///
/// XRI SURUM NOTU: XRI 3.4.1 (3.x). Haptik icin 2.x'teki
/// XRBaseController.SendHapticImpulse yolu deprecated; 3.x'te
/// HapticsUtility.SendHapticImpulse(amplitude, duration, Controller) kullaniliyor.
/// </summary>
[AddComponentMenu("Kayip Esya/Feedback Controller")]
[DisallowMultipleComponent]
public class FeedbackController : MonoBehaviour
{
    static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

    [Header("Gorsel")]
    [Tooltip("Dolabin govdesindeki Renderer. Bos birakilirsa cocuklardan ilki bulunur.")]
    [SerializeField]
    Renderer m_BodyRenderer;

    [SerializeField]
    Color m_CorrectColor = new Color(0.15f, 1f, 0.25f);

    [SerializeField]
    Color m_WrongColor = new Color(1f, 0.15f, 0.1f);

    [Tooltip("Sartname: 0,3 sn.")]
    [SerializeField, Min(0f)]
    float m_FlashDuration = 0.3f;

    [Tooltip("Emissive parlaklik carpani. Cok dusukse gozlukte fark edilmez.")]
    [SerializeField, Min(0f)]
    float m_EmissionIntensity = 3f;

    [Header("Hover highlight")]
    [SerializeField]
    Color m_HoverColor = new Color(1f, 0.95f, 0.75f);

    [Tooltip("Hover 'hafif' olmali - karar parlamasindan belirgin sekilde sonuk.")]
    [SerializeField, Min(0f)]
    float m_HoverIntensity = 0.45f;

    [Header("Isitsel")]
    [SerializeField]
    AudioSource m_AudioSource;

    [SerializeField]
    AudioClip m_CorrectClip;

    [SerializeField]
    AudioClip m_WrongClip;

    [Header("Haptik (sartname degerleri)")]
    [SerializeField, Range(0f, 1f)]
    float m_CorrectAmplitude = 0.3f;

    [SerializeField, Min(0f)]
    float m_CorrectHapticDuration = 0.1f;

    [SerializeField, Range(0f, 1f)]
    float m_WrongAmplitude = 0.8f;

    [SerializeField, Min(0f)]
    float m_WrongHapticDuration = 0.4f;

    [Header("Bonus")]
    [Tooltip("Istege bagli. Bos birakilabilir.")]
    [SerializeField]
    ParticleSystem m_Burst;

    MaterialPropertyBlock m_Block;
    Coroutine m_FlashRoutine;
    bool m_Flashing;
    bool m_Hovering;

    void Awake()
    {
        if (m_BodyRenderer == null)
            m_BodyRenderer = GetComponentInChildren<Renderer>();

        if (m_AudioSource == null)
            m_AudioSource = GetComponent<AudioSource>();

        ApplyEmission(Color.black);
    }

    /// <summary>
    /// Karar geri bildirimi: gorsel + isitsel + haptik AYNI ANDA.
    /// </summary>
    public void PlayDecision(bool isCorrect)
    {
        // 1) Gorsel
        if (m_FlashRoutine != null)
            StopCoroutine(m_FlashRoutine);
        m_FlashRoutine = StartCoroutine(FlashRoutine(isCorrect ? m_CorrectColor : m_WrongColor));

        // 2) Isitsel
        var clip = isCorrect ? m_CorrectClip : m_WrongClip;
        if (m_AudioSource != null && clip != null)
            m_AudioSource.PlayOneShot(clip);

        // 3) Haptik
        // Soket, esyayi hangi elin tuttugunu bilmez -> iki kumandaya da gonderiyoruz.
        // Gozluk bagli degilse bu cagri sessizce false doner, hata vermez.
        HapticsUtility.SendHapticImpulse(
            isCorrect ? m_CorrectAmplitude : m_WrongAmplitude,
            isCorrect ? m_CorrectHapticDuration : m_WrongHapticDuration,
            HapticsUtility.Controller.Both);

        // Bonus
        if (m_Burst != null)
        {
            var main = m_Burst.main;
            main.startColor = isCorrect ? m_CorrectColor : m_WrongColor;
            m_Burst.Play();
        }
    }

    /// <summary>
    /// Hover geri bildirimi: esya sokete yaklasinca dolabin agzinda hafif parilti.
    /// Sartname madde 4 - oyuncunun "buraya birakabilirim" sorusunun cevabi.
    /// </summary>
    public void SetHover(bool hovering)
    {
        if (m_Hovering == hovering)
            return;

        m_Hovering = hovering;
        ApplyIdleEmission();
    }

    IEnumerator FlashRoutine(Color color)
    {
        m_Flashing = true;
        ApplyEmission(color * m_EmissionIntensity);

        yield return new WaitForSeconds(m_FlashDuration);

        m_Flashing = false;
        m_FlashRoutine = null;
        ApplyIdleEmission();
    }

    void ApplyIdleEmission()
    {
        // Karar parlamasi devam ediyorsa hover onu ezmesin.
        if (m_Flashing)
            return;

        ApplyEmission(m_Hovering ? m_HoverColor * m_HoverIntensity : Color.black);
    }

    void ApplyEmission(Color color)
    {
        if (m_BodyRenderer == null)
            return;

        m_Block ??= new MaterialPropertyBlock();

        // MaterialPropertyBlock: paylasilan materyali klonlamadan sadece bu Renderer'in
        // _EmissionColor degerini ezer. Boylece dolap basina materyal instance acilmaz.
        m_BodyRenderer.GetPropertyBlock(m_Block);
        m_Block.SetColor(k_EmissionColor, color);
        m_BodyRenderer.SetPropertyBlock(m_Block);
    }

    // Gozluk takmadan Editor'de denemek icin (Inspector -> bilesenin sag ustundeki ... menusu).
    [ContextMenu("TEST: Dogru geri bildirimi")]
    void TestCorrect() => PlayDecision(true);

    [ContextMenu("TEST: Yanlis geri bildirimi")]
    void TestWrong() => PlayDecision(false);

    [ContextMenu("TEST: Hover ac")]
    void TestHoverOn() => SetHover(true);

    [ContextMenu("TEST: Hover kapat")]
    void TestHoverOff() => SetHover(false);
}
