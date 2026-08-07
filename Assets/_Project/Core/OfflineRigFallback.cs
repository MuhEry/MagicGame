using UnityEngine;

/// <summary>
/// Sahnedeki XR rig'i Alteruna icin bir AVATAR SABLONUDUR ve bu yuzden PASIF durur.
///
/// NEDEN PASIF (Alteruna'nin kendi ornek sahnesindeki desen):
/// Sablon aktif olursa `CommunicationBridgeUID.OnEnable` calisir ve rig kendi
/// UID'siyle kaydolur. Alteruna odaya girerken ayni nesneyi klonladigi icin klon
/// AYNI UID'yi tasir; klonun kaydi orijinalinkini disari iter ve orijinal rig
/// "Synchronizable not registered" diyerek senkron gonderemez hale gelir. Ustelik
/// sahnede iki kamera ve iki AudioListener kalir. Dokumantasyon bunu acikca soyler:
/// "Each synchronizable receives a unique global identifier ... to prevent collisions."
///
/// PEKI CEVRIMDISI OYNARSAK?
/// Sablon pasif oldugu icin, odaya hic girilmezse sahnede oyuncu rig'i olmaz ve
/// ekran bos kalir. Bu bilesen tam olarak o durumu kurtarir: belirlenen sure
/// boyunca odaya girilmediyse rig'i YEREL olarak aktif eder, boylece Faz 1
/// (tek kisilik) akisi bozulmadan calismaya devam eder. Sonradan odaya girilirse
/// rig tekrar pasiflestirilir; oyuncu rig'ini artik Alteruna spawn eder.
/// </summary>
[DisallowMultipleComponent]
public sealed class OfflineRigFallback : MonoBehaviour
{
    [Tooltip("Avatar sablonu olarak kullanilan, sahnede PASIF duran XR rig.")]
    [SerializeField] GameObject rigTemplate;

    [Tooltip("Bu kadar saniye odaya girilemezse rig cevrimdisi oyun icin acilir.")]
    [SerializeField, Min(0.5f)] float offlineActivationDelay = 5f;

    float elapsed;
    bool activatedOffline;
    bool settled;

    void Reset()
    {
        offlineActivationDelay = 5f;
    }

    void Update()
    {
        if (rigTemplate == null)
            return;

        NetworkShiftCoordinator network = NetworkShiftCoordinator.Instance;
        bool inRoom = network != null && network.IsInRoom;

        if (inRoom)
        {
            // Odaya girildi: oyuncu rig'ini Alteruna spawn eder. Cevrimdisi yedek
            // acilmissa geri kapat, yoksa iki rig ust uste kalir.
            if (activatedOffline)
            {
                rigTemplate.SetActive(false);
                activatedOffline = false;
                Debug.Log("[OfflineRigFallback] Odaya girildi, cevrimdisi rig kapatildi. " +
                          "Oyuncu rig'ini artik Alteruna spawn ediyor.", this);
            }

            settled = true;
            elapsed = 0f;
            return;
        }

        if (settled || activatedOffline)
            return;

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < offlineActivationDelay)
            return;

        activatedOffline = true;
        rigTemplate.SetActive(true);
        Debug.LogWarning(
            $"[OfflineRigFallback] {offlineActivationDelay:0} sn icinde odaya girilemedi. " +
            "XR rig CEVRIMDISI modda acildi; oyun tek kisilik calisiyor.", this);
    }
}
