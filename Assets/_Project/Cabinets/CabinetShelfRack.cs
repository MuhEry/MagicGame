using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Bir kategori dolabinin raf yerlesimini yonetir.
/// Aktif soket her dogru esyada siradaki gozun onune gider: soldan saga,
/// raf dolunca bir alt rafa. Yanlis karar bu sirayi kesinlikle ilerletmez.
/// </summary>
[AddComponentMenu("Kayip Esya/Cabinet Shelf Rack")]
[DisallowMultipleComponent]
public class CabinetShelfRack : MonoBehaviour
{
    [Header("Baglantilar")]
    [SerializeField] Transform m_SocketTransform;
    [SerializeField] Transform m_StoredItemsRoot;

    [Header("Raf duzeni")]
    [SerializeField, Min(1)] int m_ShelfCount = 3;
    [SerializeField, Min(1)] int m_SlotsPerShelf = 3;
    [SerializeField, Min(0.01f)] float m_SlotSpacing = 0.18f;
    [SerializeField, Min(0.01f)] float m_ShelfSpacing = 0.25f;
    [SerializeField] float m_TopShelfHeight = 0.82f;
    [SerializeField] float m_SocketFrontOffset = -0.31f;
    [SerializeField] float m_ItemDepthOffset = -0.20f;
    [SerializeField] bool m_RecycleWhenFull = true;

    int m_NextSlotIndex;

    // Vardiya, raf kapasitesinden uzun surerse dolu sayfa bir sonraki dogru
    // yerlestirmede temizlenir ve dolap yeniden ust raftan devam eder.
    public bool HasSpace => m_RecycleWhenFull || m_NextSlotIndex < m_ShelfCount * m_SlotsPerShelf;
    public int FilledSlotCount => m_NextSlotIndex;
    public int Capacity => m_ShelfCount * m_SlotsPerShelf;

    void Awake()
    {
        RefreshSocketPosition();
    }

    void OnValidate()
    {
        m_ShelfCount = Mathf.Max(1, m_ShelfCount);
        m_SlotsPerShelf = Mathf.Max(1, m_SlotsPerShelf);
        m_SlotSpacing = Mathf.Max(0.01f, m_SlotSpacing);
        m_ShelfSpacing = Mathf.Max(0.01f, m_ShelfSpacing);

        if (!Application.isPlaying)
            RefreshSocketPosition();
    }

    /// <summary>
    /// Dogru esyayi anlik aktif goze sabitler, sonra soketi bir sonraki goze tasir.
    /// Bu metot sadece dogru karar sonrasinda CategorySocket tarafindan cagrilir.
    /// </summary>
    public bool Store(IXRSelectInteractable interactable)
    {
        if (!HasSpace || interactable == null)
            return false;

        if (m_NextSlotIndex >= Capacity)
            ClearFilledRack();

        var itemTransform = interactable.transform;
        var placement = GetSlotLocalPosition(m_NextSlotIndex, m_ItemDepthOffset);
        var body = itemTransform.GetComponentInParent<Rigidbody>();
        var grab = itemTransform.GetComponentInParent<XRGrabInteractable>();

        // Soket secimi kapandiktan sonra nesneyi dolaba ait sabit koleksiyona al.
        // Bu, esyanin yeniden yakalanmasini ve fizik tarafindan raftan itilmesini onler.
        if (m_StoredItemsRoot != null)
            itemTransform.SetParent(m_StoredItemsRoot, false);
        else
            itemTransform.SetParent(transform, false);

        itemTransform.localPosition = placement;
        itemTransform.localRotation = Quaternion.identity;

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        // Yerlesen esya dekor olur; tekrar tutulup ikinci kez sayilamaz.
        if (grab != null)
            grab.enabled = false;

        m_NextSlotIndex++;
        RefreshSocketPosition();
        return true;
    }

    Vector3 GetSlotLocalPosition(int slotIndex, float depthOffset)
    {
        int row = slotIndex / m_SlotsPerShelf;
        int column = slotIndex % m_SlotsPerShelf;
        float centeredColumn = column - (m_SlotsPerShelf - 1) * 0.5f;

        return new Vector3(
            centeredColumn * m_SlotSpacing,
            m_TopShelfHeight - row * m_ShelfSpacing,
            depthOffset);
    }

    void RefreshSocketPosition()
    {
        if (m_SocketTransform == null)
            return;

        m_SocketTransform.gameObject.SetActive(HasSpace);
        if (!HasSpace)
            return;

        int visibleSlot = m_NextSlotIndex >= Capacity && m_RecycleWhenFull ? 0 : m_NextSlotIndex;
        m_SocketTransform.localPosition = GetSlotLocalPosition(visibleSlot, m_SocketFrontOffset);
    }

    void ClearFilledRack()
    {
        if (m_StoredItemsRoot != null)
        {
            for (int index = m_StoredItemsRoot.childCount - 1; index >= 0; index--)
                Destroy(m_StoredItemsRoot.GetChild(index).gameObject);
        }

        m_NextSlotIndex = 0;
    }
}
