using UnityEngine;

/// <summary>
/// GECICI sandbox test bileseni.
///
/// Sartnamenin B basari kriteri: "Sandbox sahnesinde elle suruklenen test kupleriyle
/// dogru esya girince yesil+ses+haptik, yanlis esya girince kirmizi+ses+haptik ve
/// esya geri firliyor." Bu bilesen o test kuplerine kategori vermek icindir.
///
/// Gelistirici A'nin ItemIdentity + ItemData'si geldiginde:
///   1. CategorySocket.TryResolveItem icindeki TODO dali acilir,
///   2. bu dosya ve test kupleri sahneden silinir.
/// Bu bilesen Main.unity'ye ASLA girmez, yalnizca Sandbox_B_Cabinets.unity'de yasar.
/// </summary>
[AddComponentMenu("Kayip Esya/Cabinet Test Item (gecici)")]
[DisallowMultipleComponent]
public class CabinetTestItem : MonoBehaviour
{
    [Tooltip("Bu test kupunun gercek kategorisi.")]
    public ItemCategory category = ItemCategory.Sesli;

    [Tooltip("Telemetri satirinda gorunecek sahte id. Gercek id A'nin ItemIdentity'sinden gelecek.")]
    public int itemId = -1;
}
