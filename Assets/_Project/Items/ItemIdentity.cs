using UnityEngine;

public class ItemIdentity : MonoBehaviour
{
    [SerializeField] private ItemData itemData;
    
    public ItemData ItemData
    {
        get => itemData;
        set => itemData = value;
    }

    public int ItemId => itemData != null ? itemData.id : -1;
}
