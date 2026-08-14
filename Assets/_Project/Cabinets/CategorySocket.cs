using System.Collections;
using Alteruna.Multiplayer.Unity;
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
    ShiftManager m_ShiftManager;

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

        m_ShiftManager = FindFirstObjectByType<ShiftManager>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        if (m_ShiftManager == null)
            m_ShiftManager = FindFirstObjectByType<ShiftManager>();
        if (m_ShiftManager != null)
            m_ShiftManager.OnDecision += ApplyDecisionResult;
    }

    /// <inheritdoc />
    protected override void OnDisable()
    {
        if (m_ShiftManager != null)
            m_ShiftManager.OnDecision -= ApplyDecisionResult;

        base.OnDisable();

        // Soket devre disi kalirsa coroutine'ler oldurulur ama bayraklar kalir.
        // Sifirlanmazsa dolap tekrar acildiginda kilitli kalir ve hicbir esya kabul etmez.
        m_Rejecting = false;
        m_RejectRoutine = null;
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

        if (m_ShiftManager != null && m_ShiftManager.State == ShiftState.Vardiya)
        {
            var ownership = interactable.transform.GetComponentInParent<NetworkGrabOwnership>();
            int playerIndex;
            if (ownership != null)
            {
                playerIndex = ownership.LastInteractorIndex;
                if (playerIndex < 0)
                {
                    StartCoroutine(SubmitDecisionWhenOwnerKnown(
                        ownership,
                        itemId,
                        itemCategory,
                        inspectMs,
                        shakeCount));
                    return;
                }
            }
            else
            {
                playerIndex = m_ShiftManager.LocalPlayerIndex;
            }

            m_ShiftManager.RegisterDecision(
                itemId,
                itemCategory,
                m_AcceptedCategory,
                inspectMs,
                shakeCount,
                playerIndex);
        }

        Debug.Log(
            $"[KARAR] {(isCorrect ? "DOGRU" : "YANLIS")} | dolap={m_AcceptedCategory} | " +
            $"esya='{itemName}' (id={itemId}, kategori={itemCategory}) | " +
            $"inceleme={inspectMs:F0} ms",
            this);

        // Karar ve fizik hostta uygulanir. Her cihaz geri bildirimi OnDecision ile yerel oynatir.
    }

    IEnumerator SubmitDecisionWhenOwnerKnown(
        NetworkGrabOwnership ownership,
        int itemId,
        ItemCategory itemCategory,
        float inspectMs,
        int shakeCount)
    {
        float timeout = Time.unscaledTime + 0.5f;
        while (ownership != null && ownership.LastInteractorIndex < 0 &&
               Time.unscaledTime < timeout)
            yield return null;

        if (ownership == null || ownership.LastInteractorIndex < 0 ||
            m_ShiftManager == null || m_ShiftManager.State != ShiftState.Vardiya)
        {
            Debug.LogWarning("[CategorySocket] Esyayi yerlestiren oyuncu belirlenemedi; karar yok sayildi.", this);
            yield break;
        }

        m_ShiftManager.RegisterDecision(
            itemId,
            itemCategory,
            m_AcceptedCategory,
            inspectMs,
            shakeCount,
            ownership.LastInteractorIndex);
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

    void ApplyDecisionResult(DecisionResult result)
    {
        if (result.chosen != m_AcceptedCategory)
            return;

        if (m_Feedback != null)
            m_Feedback.PlayDecision(result.isCorrect);

        ReleaseMatchingSelection(result.itemId);

        if (!result.isCorrect && m_ShiftManager.IsHostAuthority && m_RejectRoutine == null)
        {
            var spawner = FindFirstObjectByType<ItemSpawner>();
            GameObject item = null;
            spawner?.TryGetSpawnedItem(result.itemId, out item);
            var interactable = item != null ? item.GetComponentInChildren<XRGrabInteractable>() : null;
            if (interactable != null)
                m_RejectRoutine = StartCoroutine(RejectRoutine(interactable));
        }
    }

    void ReleaseMatchingSelection(int itemId)
    {
        for (int index = interactablesSelected.Count - 1; index >= 0; index--)
        {
            var selected = interactablesSelected[index];
            if (TryResolveItem(selected, out int selectedId, out _, out _) && selectedId == itemId)
                interactionManager.SelectExit(this, selected);
        }
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

        ReleaseMatchingSelection(TryResolveItem(interactable, out int itemId, out _, out _) ? itemId : -1);

        // XRGrabInteractable'in Rigidbody durumunu geri almasi icin bir kare bekle.
        yield return null;

        if (itemTransform != null)
        {
            var sync = itemTransform.GetComponentInParent<RigidbodySynchronizable>();
            if (sync != null)
            {
                if (!sync.HasOwnership)
                    sync.TakeOwnership(true);

                float timeout = Time.time + 0.5f;
                while (!sync.HasOwnership && Time.time < timeout)
                    yield return null;

                if (sync.HasOwnership)
                {
                    sync.isKinematic = false;
                    sync.useGravity = true;
                    sync.velocity = Vector3.zero;
                    sync.angularVelocity = Vector3.zero;
                    sync.SyncSettings();
                    sync.AddForce(GetEjectDirection() * m_EjectSpeed, ForceMode.VelocityChange);
                    sync.ForceUpdate(true);
                    yield return new WaitForFixedUpdate();
                    sync.ForceUpdate(true);
                    sync.ReleaseOwnership();
                }
                else
                {
                    Debug.LogError("[CabinetHost] Esya sahipligi alinamadi; yanlis esya firlatilamadi.", this);
                }
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
