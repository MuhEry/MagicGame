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

    [Header("Geri bildirim")]
    [Tooltip("Bos birakilirsa ust objelerde aranir (genelde dolap kokunde durur).")]
    [SerializeField]
    FeedbackController m_Feedback;

    /// <summary>Bu dolabin kabul ettigi kategori.</summary>
    public ItemCategory acceptedCategory
    {
        get => m_AcceptedCategory;
        set => m_AcceptedCategory = value;
    }

    // Yanlis esya disari atilirken baska bir esyayi iceri almamak icin kilit.
    bool m_Rejecting;
    Coroutine m_RejectRoutine;

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
    }

    /// <inheritdoc />
    public override bool CanSelect(IXRSelectInteractable interactable)
    {
        // DIKKAT: Burada BILEREK kategori filtresi YOK.
        // "Sadece dogruyu al" seklinde kisitlamak sartnamenin acikca yasakladigi seydir.
        if (!base.CanSelect(interactable))
            return false;

        // Bir esyayi disari atarken, o esyayi birakana kadar BASKA esya kabul etme.
        // (Su an reddedilmekte olan esya icin true donmeye devam etmeliyiz, yoksa
        //  XRI onu aninda birakir ve 0,4 sn'lik bekleme calismaz.)
        if (m_Rejecting && !IsSelecting(interactable))
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

        // ------------------------------------------------------------------
        // TODO: C'nin ShiftManager'i gelince buraya baglanacak.
        //
        //   ShiftManager.Instance.RegisterDecision(
        //       itemId,
        //       itemCategory,        // correct
        //       m_AcceptedCategory,  // chosen
        //       inspectMs,
        //       shakeCount);         // <- A'nin ItemProbe'undan gelecek
        //
        // ShiftManager henuz repoda YOK. Sartnamenin B basari kriteri geregi
        // "ShiftManager hentiz yokken bile bir Debug.Log ile akis gorunuyor"
        // olmali; sahte/gecici bir ShiftManager sinifi UYDURULMAYACAK.
        // ------------------------------------------------------------------
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

        if (!isCorrect)
        {
            if (m_RejectRoutine != null)
                StopCoroutine(m_RejectRoutine);
            m_RejectRoutine = StartCoroutine(RejectRoutine(interactable));
        }
    }

    /// <inheritdoc />
    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);

        // Esya (oyuncu tarafindan kapilmak dahil) herhangi bir sebeple ayrildiysa
        // reddetme surecini iptal et, kilidi acik birakma.
        if (m_RejectRoutine != null && args.interactableObject != null && !IsSelecting(args.interactableObject))
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

        // ------------------------------------------------------------------
        // TODO: A'nin ItemIdentity + ItemData'si gelince ILK SIRAYA su dal eklenecek:
        //
        //   var identity = interactable.transform.GetComponentInParent<ItemIdentity>();
        //   if (identity != null && identity.data != null)
        //   {
        //       itemId   = identity.id;
        //       category = identity.data.category;
        //       itemName = identity.data.displayName;
        //       return true;
        //   }
        //
        // Assets/_Project/Items/ su an bos - o yuzden simdilik yalnizca
        // sandbox test kupleri (CabinetTestItem) okunuyor.
        // ------------------------------------------------------------------

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

    /// <summary>
    /// Yanlis esyayi rejectDelay kadar bekletip birakir ve hafifce disari iter.
    /// </summary>
    IEnumerator RejectRoutine(IXRSelectInteractable interactable)
    {
        m_Rejecting = true;

        if (m_RejectDelay > 0f)
            yield return new WaitForSeconds(m_RejectDelay);

        var itemTransform = interactable?.transform;

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
