using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using UnityEngine;

/// <summary>
/// Ag avatarinin govdesi: kafa + 2 el (sartname Faz 2 maddesi 2).
///
/// TASARIM KARARI - XR RIG'I KLONLAMIYORUZ.
/// Alteruna'nin ornek kurulumunda avatar sablonu sahnedeki XR rig'in kendisidir
/// ve rig PASIF birakilir. Bu kurulum uc ayri tuzak uretiyor:
///   - Aktif rig kendi UID'siyle kaydolur; Alteruna onu klonlayinca ayni GUID iki
///     nesnede olur ("Synchronizable already registered ... (Clone)") ve orijinal
///     rig senkron gonderemez.
///   - Sahnede iki AudioListener / iki kamera olusur.
///   - Rig pasif birakilirsa odaya hic girilmedigi durumda sahnede HIC kamera
///     kalmaz; gozlukte siyah ekran gorunur ve Faz 1 akisi bozulur.
///
/// Bunun yerine avatar AYRI ve HAFIF bir prefabdir: yalnizca gorunur kafa ve iki
/// el. Yerel XR rig HER ZAMAN aktif kalir ve hic aga sokulmaz - yani Faz 1
/// davranisi hic degismez. Yerel avatar kopyasi rig'i takip eder, uzak kopya ise
/// TransformSynchronizable ile aldigi pozu uygular.
///
/// Rig referanslari PlayerRefs'ten okunur (mimari kural 8): Camera.main dogrudan
/// cagrilmaz, cunku sahnede iki avatar varken "ana kamera" belirsizdir.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Kayip Esya/Network Avatar Rig")]
public sealed class NetworkAvatarRig : CommunicationBridge
{
    [Header("Avatar parcalari (prefab icindeki cocuklar)")]
    [SerializeField] Transform m_Head;
    [SerializeField] Transform m_LeftHand;
    [SerializeField] Transform m_RightHand;

    [Header("Yerel avatar")]
    [Tooltip("Yerel avatarin gorunur parcalari gizlensin mi. Oyuncu kendi kafasini icten gormemeli.")]
    [SerializeField] bool m_HideOwnHead = true;

    /// <summary>Bu avatar bu cihaza mi ait. Uzak avatarda hicbir sey yapmayiz.</summary>
    bool m_IsLocal;
    bool m_Possessed;
    bool m_WarnedMissingRefs;

    public override void Possessed(bool isMe, User user)
    {
        m_Possessed = true;
        m_IsLocal = isMe;

        // Yerel oyuncu kendi kafasinin icini gormesin; elleri gorunur kalabilir.
        if (isMe && m_HideOwnHead && m_Head != null)
        {
            foreach (var renderer in m_Head.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }

        Debug.Log($"[Avatar] {(isMe ? "YEREL" : "UZAK")} avatar kuruldu (kullanici={user?.Index}).", this);
    }

    public override void Unpossessed()
    {
        m_Possessed = false;
        m_IsLocal = false;
    }

    void LateUpdate()
    {
        // Uzak avatar pozunu agdan alir; buradan ELLEME.
        if (!m_Possessed || !m_IsLocal)
            return;

        PlayerRefs refs = PlayerRefs.Instance;
        if (refs == null)
        {
            WarnOnce("[Avatar] Sahnede PlayerRefs yok; yerel avatar rig'i takip edemiyor.");
            return;
        }

        bool anyFollowed = Follow(m_Head, refs.Head);
        anyFollowed |= Follow(m_LeftHand, refs.LeftHand);
        anyFollowed |= Follow(m_RightHand, refs.RightHand);

        if (!anyFollowed)
            WarnOnce("[Avatar] PlayerRefs'te kafa/el transformlari bos; avatar hareket etmiyor.");
    }

    static bool Follow(Transform target, Transform source)
    {
        if (target == null || source == null)
            return false;

        target.SetPositionAndRotation(source.position, source.rotation);
        return true;
    }

    void WarnOnce(string message)
    {
        if (m_WarnedMissingRefs)
            return;

        m_WarnedMissingRefs = true;
        Debug.LogWarning(message, this);
    }
}
