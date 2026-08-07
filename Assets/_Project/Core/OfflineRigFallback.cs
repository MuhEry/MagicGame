using UnityEngine;

/// <summary>
/// Oyuncu rig'i bekcisi. "Gozlukte hicbir sey yok / dondu" belirtisinin iki
/// sebebini de kurtarir.
///
/// 1) SAHNEDEKI RIG NEDEN PASIF?
/// Sahnedeki XR rig, Alteruna icin bir avatar SABLONUDUR. Alteruna'nin kendi ornek
/// sahnesinde de pasiftir. Aktif kalirsa `CommunicationBridgeUID.OnEnable` calisir
/// ve rig kendi UID'siyle kaydolur; Alteruna ayni nesneyi klonladigi icin klon AYNI
/// UID'yi tasir, klonun kaydi orijinalinkini disari iter ve orijinal rig
/// "Synchronizable not registered" diyerek senkron gonderemez hale gelir. Ustelik
/// sahnede iki kamera ve iki AudioListener kalir.
///
/// 2) PEKI KAMERA HIC ACILMAZSA?
/// Sablon pasif oldugu icin, odaya girilemezse ya da Alteruna spawn ettigi avatari
/// aktiflestirmezse sahnede HIC kamera kalmaz. Gozlukte bu siyah ekran olarak
/// gorunur ve "oyun dondu" sanilir. Bu bilesen tam olarak onu engeller: belirlenen
/// sure boyunca aktif kamera yoksa once Alteruna'nin spawn ettigi avatari, o da
/// yoksa sablon rig'i acar ve NE YAPTIGINI Console'a yazar.
/// </summary>
[DisallowMultipleComponent]
public sealed class OfflineRigFallback : MonoBehaviour
{
    [Tooltip("Avatar sablonu olarak kullanilan, sahnede PASIF duran XR rig.")]
    [SerializeField] GameObject rigTemplate;

    [Tooltip("Bu kadar saniye aktif kamera bulunamazsa bekci devreye girer.")]
    [SerializeField, Min(0.5f)] float watchdogDelay = 5f;

    float elapsed;
    bool rescued;

    void Update()
    {
        if (rescued)
            return;

        if (HasActiveCamera())
        {
            elapsed = 0f;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < watchdogDelay)
            return;

        rescued = true;
        Rescue();
    }

    /// <summary>
    /// Sahnede oyuncuya goruntu verebilecek etkin bir kamera var mi?
    /// Pasif nesneler dahil taranir, sonra gercekten etkin olanlar ayiklanir.
    /// </summary>
    static bool HasActiveCamera()
    {
        foreach (Camera camera in FindObjectsByType<Camera>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (camera != null && camera.enabled && camera.isActiveAndEnabled)
                return true;
        }

        return false;
    }

    void Rescue()
    {
        NetworkShiftCoordinator network = NetworkShiftCoordinator.Instance;
        bool inRoom = network != null && network.IsInRoom;

        // Once Alteruna'nin spawn ettigi avatari ara. Sablon pasif oldugu icin klon
        // da pasif dogmus olabilir; onu acmak dogru olandir, cunku UID'leri Alteruna
        // atadi ve senkronizasyon ona bagli.
        GameObject spawned = FindInactiveSpawnedAvatar();
        if (spawned != null)
        {
            spawned.SetActive(true);
            Debug.LogWarning(
                $"[Rig Bekcisi] {watchdogDelay:0} sn boyunca aktif kamera bulunamadi. " +
                $"Alteruna'nin spawn ettigi avatar ('{spawned.name}') acildi.", this);
            return;
        }

        if (rigTemplate == null)
        {
            Debug.LogError(
                "[Rig Bekcisi] Aktif kamera yok ve acilacak bir rig de yok. " +
                "Inspector'da 'Rig Template' alani bos - Multiplayer Kurulumunu Uygula komutunu calistir.", this);
            return;
        }

        rigTemplate.SetActive(true);
        Debug.LogWarning(
            $"[Rig Bekcisi] {watchdogDelay:0} sn boyunca aktif kamera bulunamadi" +
            (inRoom ? " (odadayiz ama Alteruna avatar acmadi)" : " (odaya girilemedi)") +
            ". Sablon rig CEVRIMDISI modda acildi; oyun tek kisilik calisiyor.", this);
    }

    /// <summary>
    /// Alteruna'nin urettigi ama pasif kalmis avatari bulur.
    /// UnityEngine.Avatar ile karismasin diye tip tam adiyla yazildi.
    /// </summary>
    static GameObject FindInactiveSpawnedAvatar()
    {
        foreach (Alteruna.Multiplayer.Unity.Avatar avatar in
                 FindObjectsByType<Alteruna.Multiplayer.Unity.Avatar>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (avatar == null)
                continue;

            GameObject go = avatar.gameObject;

            // Sahnedeki sablonun kendisi degil, ondan uretilen kopya aranıyor.
            if (!go.activeSelf && go.name.Contains("(Clone)"))
                return go;
        }

        return null;
    }
}
