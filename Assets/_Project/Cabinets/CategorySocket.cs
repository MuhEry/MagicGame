using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Bir dolabin agzindaki kategori soketi.
///
/// KRITIK TASARIM KARARI (sartname - Gelistirici B, madde 1):
/// Bu soket YANLIS esyayi da KABUL EDER. CanSelect kategoriye gore KISITLANMAZ.
/// Karar, esya sokete girdikten SONRA verilir. Aksi halde oyuncu yanlis cevabin
/// geri bildirimini hic alamaz ve ogrenme olmaz.
///
/// Akis: selectEntered -> esyanin kategorisi ile acceptedCategory karsilastirilir
///       -> ShiftManager.RegisterDecision(...) cagrilir
///       -> yanlissa rejectDelay (0,4 sn) sonra ForceDeselect + hafif impulse ile disari atilir.
///
/// XRI SURUM NOTU: Bu dosya XRI 3.4.1 (XRI 3.x) icin yazildi.
///   - CanSelect imzasi: CanSelect(IXRSelectInteractable)   [2.x'teki XRBaseInteractable overload'i deprecated]
///   - Namespace'ler 3.x'te bolundu: Interactors / Interactables / Toolkit
/// </summary>
[AddComponentMenu("Kayip Esya/Category Socket")]
public class CategorySocket : XRSocketInteractor
{
    [Header("Dolap")]
    [Tooltip("Bu dolabin kabul ettigi kategori. Prefab varyantlarinda Inspector'dan degistirilir.")]
    [SerializeField]
    ItemCategory m_AcceptedCategory = ItemCategory.Sesli;

    [Header("Yanlis yerlestirme")]
    [Tooltip("Yanlis esya sokete girdikten kac saniye sonra disari atilsin. Sartname: 0,4 sn.")]
    [SerializeField, Min(0f)]
    float m_RejectDelay = 0.4f;

    [Tooltip("Disari atarken uygulanacak hiz (m/sn). 'Hafif impulse' - firlatmak degil, itmek.")]
    [SerializeField, Min(0f)]
    float m_EjectSpeed = 1.2f;

    [Tooltip("Bos birakilirsa soketin kendi -forward yonu (dolaptan disari dogru) kullanilir.")]
    [SerializeField]
    Transform m_EjectDirectionSource;

    [Tooltip("Disari atilan esya bu sure boyunca AYNI soket tarafindan tekrar alinmaz.\n" +
             "0 yapilirsa esya trigger'dan cikamadigi anda sonsuz red dongusu olusur.")]
    [SerializeField, Min(0f)]
    float m_EjectIgnoreDuration = 1.5f;

    [Header("Doğru yerleştirme")]
    [Tooltip("Doğru karar geri bildirimi görünür kaldıktan sonra eşyanın kaldırılacağı süre.")]
    [SerializeField, Min(0f)]
    float m_AcceptDelay = 0.3f;

    [Header("Geri bildirim")]
    [Tooltip("Bos birakilirsa ust objelerde aranir (genelde dolap kokunde durur).")]
    [SerializeField]
    FeedbackController m_Feedback;

    [Tooltip("Bos birakilirsa ust dolapta aranir. Dogru esya yerlesince aktif soketi siradaki rafa tasir.")]
    [SerializeField]
    CabinetShelfRack m_ShelfRack;

    /// <summary>Bu dolabin kabul ettigi kategori.</summary>
    public ItemCategory acceptedCategory
    {
        get => m_AcceptedCategory;
        set => m_AcceptedCategory = value;
    }

    // Yanlis esya disari atilirken baska bir esyayi iceri almamak icin kilit.
    bool m_Rejecting;
    Coroutine m_RejectRoutine;

    // Dogru esya tuketilirken (geri bildirim + yok etme) ayni kilit.
    bool m_Accepting;
    Coroutine m_AcceptRoutine;

    // Disari atilan esya trigger'dan cikamazsa soket onu aninda tekrar yakalar.
    // Son atilan esyayi kisa bir sure yok sayiyoruz.
    IXRSelectInteractable m_LastEjected;
    float m_LastEjectTime = float.NegativeInfinity;

    // Inceleme suresi olcumu: esya bu soketin agzina yaklastigi anda baslar.
    // TODO(A/C): Gercek "inceleme suresi" esya kavrandigi anda baslamali.
    //            ItemProbe (A) veya ShiftManager (C) daha dogru bir deger verirse
    //            asagidaki hover tabanli olcum onunla degistirilecek.
    float m_HoverStartTime = -1f;

    /// <inheritdoc />
    protected override void Awake()
    {
        base.Awake();

        if (m_Feedback == null)
            m_Feedback = GetComponentInParent<FeedbackController>();

        if (m_ShelfRack == null)
            m_ShelfRack = GetComponentInParent<CabinetShelfRack>();
    }

    /// <inheritdoc />
    protected override void OnDisable()
    {
        base.OnDisable();

        // Soket devre disi kalirsa coroutine'ler oldurulur ama bayraklar kalir.
        // Sifirlanmazsa dolap tekrar acildiginda kilitli kalir ve hicbir esya kabul etmez.
        m_Rejecting = false;
        m_Accepting = false;
        m_RejectRoutine = null;
        m_AcceptRoutine = null;
    }

    /// <inheritdoc />
    public override bool CanSelect(IXRSelectInteractable interactable)
    {
        // DIKKAT: Burada BILEREK kategori filtresi YOK.
        // "Sadece dogruyu al" seklinde kisitlamak sartnamenin acikca yasakladigi seydir.
        if (!base.CanSelect(interactable))
            return false;

        // Dolu dolap yeni esya almaz. Bu kontrol kategori filtresi degildir;
        // kapasite korumasidir ve yanlis/ dogru karar davranisini degistirmez.
        if (m_ShelfRack != null && !m_ShelfRack.HasSpace)
            return false;

        // Bir esyayi disari atarken, o esyayi birakana kadar BASKA esya kabul etme.
        // (Su an reddedilmekte olan esya icin true donmeye devam etmeliyiz, yoksa
        //  XRI onu aninda birakir ve 0,4 sn'lik bekleme calismaz.)
        if (m_Rejecting && !IsSelecting(interactable))
            return false;

        // Ayni sey dogru esya tuketilirken de gecerli: 0,3 sn'lik geri bildirim
        // penceresinde araya baska bir esya girmesin.
        if (m_Accepting && !IsSelecting(interactable))
            return false;

        // Yeni disari atilan esyayi kisa sure yok say.
        // Aksi halde: esya trigger kuresinden cikamiyor -> soket onu aninda tekrar
        // yakaliyor -> sonsuz "YANLIS" dongusu. XRI hover'i geri donusum gecikmesiyle
        // bloke eder ama SECIMI etmez; bu yuzden hover hic olmadan secim gerceklesir
        // (log'da inceleme=0 ms bunun imzasidir).
        if (ReferenceEquals(interactable, m_LastEjected) &&
            Time.time - m_LastEjectTime < m_EjectIgnoreDuration)
            return false;

        return true;
    }

    /// <inheritdoc />
    protected override void OnHoverEntered(HoverEnterEventArgs args)
    {
        base.OnHoverEntered(args);

        if (m_HoverStartTime < 0f)
            m_HoverStartTime = Time.time;

        // Sartname madde 4: esya sokete yaklasinca dolabin agzinda hafif highlight.
        if (m_Feedback != null)
            m_Feedback.SetHover(true);
    }

    /// <inheritdoc />
    protected override void OnHoverExited(HoverExitEventArgs args)
    {
        base.OnHoverExited(args);

        if (!hasHover)
            m_HoverStartTime = -1f;

        if (m_Feedback != null)
            m_Feedback.SetHover(hasHover);
    }

    /// <inheritdoc />
    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);

        var interactable = args.interactableObject;
        var inspectMs = m_HoverStartTime >= 0f ? (Time.time - m_HoverStartTime) * 1000f : 0f;
        m_HoverStartTime = -1f;

        if (!TryResolveItem(interactable, out var itemId, out var itemCategory, out var itemName))
        {
            // Kategorisi okunamayan bir sey sokete girdi (orn. sahnedeki rastgele bir kup).
            // Karar uretmiyoruz, sadece uyariyoruz - aksi halde telemetriye cop veri gider.
            Debug.LogWarning(
                $"[CategorySocket] '{name}' kategorisi okunamayan bir nesne aldi: " +
                $"'{interactable.transform.name}'. ItemIdentity veya CabinetTestItem bileseni yok.",
                this);
            return;
        }

        var isCorrect = itemCategory == m_AcceptedCategory;

        // Gerçek eşyalarda A'nın ItemProbe'u sallama sayısını sağlar.
        // B'nin geçici test küplerinde ItemProbe olmadığından değer 0 kalır.
        int shakeCount = GetShakeCount(interactable);

        if (ShiftManager.Instance != null && ShiftManager.Instance.State == ShiftState.Vardiya)
        {
            ShiftManager.Instance.RegisterDecision(
                itemId,
                itemCategory,
                m_AcceptedCategory,
                inspectMs,
                shakeCount);
        }

        Debug.Log(
            $"[KARAR] {(isCorrect ? "DOGRU" : "YANLIS")} | dolap={m_AcceptedCategory} | " +
            $"esya='{itemName}' (id={itemId}, kategori={itemCategory}) | " +
            $"inceleme={inspectMs:F0} ms",
            this);

        // Uc kanal ayni anda: gorsel (0,3 sn emissive) + isitsel + haptik.
        if (m_Feedback != null)
            m_Feedback.PlayDecision(isCorrect);
        else
            Debug.LogWarning($"[CategorySocket] '{name}' uzerinde FeedbackController yok - sadece log var.", this);

        if (isCorrect)
        {
            // Zaten bir esya tuketiliyorsa rutini YENIDEN BASLATMA.
            // Eski kod burada StopCoroutine cagiriyordu: soket, esyayi biraktiktan
            // sonraki bir karelik boslukta ayni esyayi tekrar yakaliyor, bu yeniden
            // giris rutini Destroy satirina varmadan olduruyordu. Sonuc: esya hic
            // yok olmuyor ve arka arkaya "DOGRU" sinyali basiliyordu.
            if (m_AcceptRoutine == null)
                m_AcceptRoutine = StartCoroutine(AcceptRoutine(interactable));
        }
        else
        {
            // Kabul tarafiyla ayni kural: calisan rutini YENIDEN BASLATMA.
            if (m_RejectRoutine == null)
                m_RejectRoutine = StartCoroutine(RejectRoutine(interactable));
        }
    }

    /// <inheritdoc />
    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);

        // Esya (oyuncu tarafindan kapilmak dahil) herhangi bir sebeple ayrildiysa
        // reddetme surecini iptal et, kilidi acik birakma.
        if (!m_Rejecting && m_RejectRoutine != null && args.interactableObject != null && !IsSelecting(args.interactableObject))
        {
            StopCoroutine(m_RejectRoutine);
            m_RejectRoutine = null;
            m_Rejecting = false;
        }
    }

    /// <summary>
    /// Sokete giren nesnenin kimligini ve kategorisini cozer.
    /// </summary>
    bool TryResolveItem(IXRSelectInteractable interactable, out int itemId, out ItemCategory category, out string itemName)
    {
        itemId = 0;
        category = default;
        itemName = interactable != null ? interactable.transform.name : "<null>";

        if (interactable == null)
            return false;

        var identity = interactable.transform.GetComponentInParent<ItemIdentity>();
        if (identity != null && identity.ItemData != null)
        {
            itemId = identity.ItemId;
            category = identity.ItemData.category;
            itemName = identity.ItemData.displayName;
            return true;
        }

        var testItem = interactable.transform.GetComponentInParent<CabinetTestItem>();
        if (testItem != null)
        {
            itemId = testItem.itemId;
            category = testItem.category;
            itemName = testItem.name;
            return true;
        }

        return false;
    }

    static int GetShakeCount(IXRSelectInteractable interactable)
    {
        var probe = interactable?.transform.GetComponentInParent<ItemProbe>();
        return probe != null ? Mathf.Max(0, probe.ShakeCount) : 0;
    }

    /// <summary>
    /// Doğru eşya geri bildirimin ardından soketten çıkarılır ve kaldırılır.
    /// Böylece aynı kategorideki sonraki eşya için soket boş kalır.
    /// </summary>
    IEnumerator AcceptRoutine(IXRSelectInteractable interactable)
    {
        m_Accepting = true;

        var itemTransform = interactable?.transform;

        if (m_AcceptDelay > 0f)
            yield return new WaitForSeconds(m_AcceptDelay);

        // Secimi kontrollu bitir. m_Accepting kilidi bu karede ayni esyanin
        // yeniden sokete alinmasini engeller; esyayi devre disi birakmiyoruz
        // cunku dogru nesne artik rafin icinde gorunur kalacak.
        if (interactionManager != null && interactable != null && IsSelecting(interactable))
            interactionManager.SelectExit(this, interactable);

        yield return null;

        // Dogru esya rafin icinde kalir. Raf yoneticisi soketi ancak burada,
        // yani kesin olarak dogru karar verildikten sonra siradaki goze tasir.
        // Bu sira yanlis denemelerin soket konumunu bozmasini engeller.
        if (m_ShelfRack != null && m_ShelfRack.Store(interactable))
        {
            // Store() esyayi rafta kinematic/dekor haline getirir.
        }
        else if (itemTransform != null)
        {
            // Eski prefablar raf yoneticisi olmadan da calismaya devam eder.
            Destroy(itemTransform.gameObject);
        }

        m_Accepting = false;
        m_AcceptRoutine = null;
    }

    /// <summary>
    /// Yanlis esyayi rejectDelay kadar bekletip birakir ve hafifce disari iter.
    /// </summary>
    IEnumerator RejectRoutine(IXRSelectInteractable interactable)
    {
        m_Rejecting = true;

        var itemTransform = interactable?.transform;

        if (m_RejectDelay > 0f)
            yield return new WaitForSeconds(m_RejectDelay);

        // Esyayi birakmadan ONCE "yeni atildi" damgasini vur. SelectExit,
        // OnSelectExited'i senkron tetikler ve ayni karede CanSelect yeniden
        // sorulabilir; damga o an hazir olmazsa esya aninda geri yakalanir.
        m_LastEjected = interactable;
        m_LastEjectTime = Time.time;

        if (interactionManager != null && interactable != null && IsSelecting(interactable))
            interactionManager.SelectExit(this, interactable);

        // XRGrabInteractable'in Rigidbody durumunu geri almasi icin bir kare bekle.
        yield return null;

        if (itemTransform != null)
        {
            var rb = itemTransform.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                // SARTNAME "Onemli Noktalar #3": esya sokete girince kinematic olur;
                // disari atarken kinematic'i kapatmayi unutma, yoksa havada asili kalir.
                rb.isKinematic = false;
                rb.AddForce(GetEjectDirection() * m_EjectSpeed, ForceMode.VelocityChange);
            }
        }

        m_Rejecting = false;
        m_RejectRoutine = null;
    }

    Vector3 GetEjectDirection()
    {
        var source = m_EjectDirectionSource != null ? m_EjectDirectionSource : transform;
        // Dolabin agzindan disari + hafif yukari.
        return (source.forward + Vector3.up * 0.35f).normalized;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, GetEjectDirection() * 0.4f);
    }
}
