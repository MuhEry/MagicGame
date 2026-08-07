using UnityEngine;

public class ItemIdentity : MonoBehaviour
{
    [SerializeField] private ItemData itemData;
    ItemData runtimeItemData;
    
    public ItemData ItemData
    {
        get => runtimeItemData != null ? runtimeItemData : itemData;
        set => itemData = value;
    }

    public int ItemId => ItemData != null ? ItemData.id : -1;

    /// <summary>
    /// Ayni gorunur prefab farkli vardiyalarda farkli duyusal kategoriyle
    /// oynatilabilir. Kopya yalnizca bu sahne nesnesine aittir; kaynak
    /// ScriptableObject ve diger esyalar degismez.
    /// </summary>
    public void SetRuntimeItemData(ItemData data)
    {
        if (runtimeItemData != null && runtimeItemData != data)
            Destroy(runtimeItemData);

        runtimeItemData = data;
    }

    void OnDestroy()
    {
        if (runtimeItemData != null)
            Destroy(runtimeItemData);
    }
}
