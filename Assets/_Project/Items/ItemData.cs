using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "Gece Vardiyasi/ItemData")]
public class ItemData : ScriptableObject
{
    public int id;
    public string displayName;
    public ItemCategory category;
    public float mass = 1.0f;
    public AudioClip rattleClip;
    [ColorUsage(true, true)]
    public Color glowColor = Color.white;
}
