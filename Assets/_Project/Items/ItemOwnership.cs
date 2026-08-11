using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Bir esyanin ag uzerindeki TUTMA SAHIPLIGI (sartname mimari kural 6).
///
/// SORUN: XRI her cihazda bagimsiz calisir. Iki oyuncu ayni anda ayni esyaya
/// uzanirsa IKISININ DE kendi cihazinda kavrama basarili olur. Sonra iki
/// RigidbodySynchronizable birbirine ters pozisyon gonderir: esya iki el arasinda
/// titrer, ziplar, bazen zeminin altina kacar. Alteruna dokumantasyonu bunu acikca
/// soyler: ayni anda yalnizca bir kullanici sahip olabilir, ama sahiplik API'sini
/// kullanmak ZORUNLU DEGILDIR - korumayi gelistirici yazar. Iste o koruma budur.
///
/// COZUM: Oyuncu esyayi ELE aldigi anda agda "bu esya bende" diye ilan eder.
/// Digerinin cihazinda esyanin XRGrabInteractable'i KAPATILIR; o oyuncu esyayi
/// hic kavrayamaz, cekisme hic baslamaz. Birakildiginda geri acilir.
///
/// Soket (dolap) tutuslari bu kilidin DISINDADIR: dolap esyayi aldiginda sahiplik
/// ilan edilmez, cunku o bir oyuncu tutusu degildir.
///
/// Odada degilken bu bilesen hicbir sey yapmaz - tek oyuncu akisi degismez.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[DisallowMultipleComponent]
[AddComponentMenu("Kayip Esya/Item Ownership")]
public sealed class ItemOwnership : AttributesSync
{
    XRGrabInteractable m_GrabInteractable;
    RigidbodySynchronizable m_SynchronizedBody;

    /// <summary>
    /// Esyanin konumunu yayinlayan bilesen (Rigidbody- veya TransformSynchronizable).
    /// Yazma yetkisi bunun uzerinden devralinir.
    /// </summary>
    CommunicationBridgeUID m_TransformSync;

    /// <summary>Esyayi elinde tutan kullanicinin Alteruna indeksi. -1 = bosta.</summary>
    int m_HolderUserIndex = -1;

    static int LocalUserIndex =>
        NetworkShiftCoordinator.Instance != null ? NetworkShiftCoordinator.Instance.LocalUserIndex : -1;

    static bool InRoom =>
        NetworkShiftCoordinator.Instance != null && NetworkShiftCoordinator.Instance.IsInRoom;

    void Awake()
    {
        m_GrabInteractable = GetComponent<XRGrabInteractable>();
        m_SynchronizedBody = GetComponent<RigidbodySynchronizable>();

        // Once fizik senkronu, yoksa transform senkronu. Ikisi AYNI objede olmaz.
        m_TransformSync = m_SynchronizedBody != null
            ? (CommunicationBridgeUID)m_SynchronizedBody
            : GetComponent<TransformSynchronizable>();
    }

    // DIKKAT: base.OnEnable() cagrilmazsa CommunicationBridgeUID bu bileseni
    // Multiplayer'a KAYDETMEZ ve butun RPC'ler sessizce hic calismaz.
    // Kural: Alteruna sinifindan turerken Unity mesajlarini OVERRIDE et, gizleme.
    public override void OnEnable()
    {
        base.OnEnable();

        if (m_GrabInteractable == null)
            return;

        m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
        m_GrabInteractable.selectExited.AddListener(OnSelectExited);
    }

    // Base sinifta OnDisable YOK (yalnizca OnEnable virtual), bu yuzden override degil.
    void OnDisable()
    {
        if (m_GrabInteractable == null)
            return;

        m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
    }

    /// <summary>Soket tutuslari sahiplik ilan etmez; yalnizca EL tutuslari eder.</summary>
    static bool IsHandInteractor(IXRSelectInteractor interactor)
    {
        return interactor != null && !(interactor is XRSocketInteractor);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!IsHandInteractor(args.interactorObject))
            return;

        // SDK'nin sahiplik API'si: yazma yetkisini kavrayan tarafa al.
        // Bu olmadan iki istemci ayni transformu ayni anda yazar ve esya
        // "lastik gibi" ileri geri ziplar.
        if (InRoom)
            m_TransformSync?.TakeOwnership();

        if (m_SynchronizedBody != null)
        {
            // Alteruna, guncellemeyi GONDEREN istemciyi fizik sahibi sayar.
            m_SynchronizedBody.AllowCollisionToAssumeOwner = true;
            m_SynchronizedBody.SendData = true;
            m_SynchronizedBody.SyncSettings();
            m_SynchronizedBody.ForceUpdate();
        }

        if (!InRoom)
            return;

        int me = LocalUserIndex;
        if (me < 0)
            return;

        ApplyHolder(me, true);
        InvokeRemoteMethod(nameof(ApplyHolder), UserId.All, me, true);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!IsHandInteractor(args.interactorObject))
            return;

        if (m_SynchronizedBody != null)
        {
            m_SynchronizedBody.SyncSettings();
            m_SynchronizedBody.ForceUpdate();
        }

        if (!InRoom)
            return;

        int me = LocalUserIndex;

        // Kilidi yalnizca SAHIBI acabilir.
        if (me < 0 || m_HolderUserIndex != me)
            return;

        // Yazma yetkisini birak; esya artik sokete veya digerine gecebilir.
        m_TransformSync?.ReleaseOwnership();

        ApplyHolder(me, false);
        InvokeRemoteMethod(nameof(ApplyHolder), UserId.All, me, false);
    }

    [SynchronizableMethod]
    void ApplyHolder(int userIndex, bool held)
    {
        // Baskasi tutarken gelen "biraktim" mesajini yok say: gecikmeli bir paket
        // aktif tutusu yanlislikla serbest birakabilir.
        if (!held && m_HolderUserIndex != userIndex)
            return;

        m_HolderUserIndex = held ? userIndex : -1;
        RefreshLocalGrabPermission();
    }

    void RefreshLocalGrabPermission()
    {
        if (m_GrabInteractable == null)
            return;

        int me = LocalUserIndex;
        bool heldBySomeoneElse = m_HolderUserIndex >= 0 && m_HolderUserIndex != me;

        // Kavrama kapatilinca XRI o an tutan eli de birakmaya zorlar; kaybeden
        // taraf esyayi aninda birakir ve cekisme hic olusmaz.
        if (m_GrabInteractable.enabled == !heldBySomeoneElse)
            return;

        m_GrabInteractable.enabled = !heldBySomeoneElse;
    }

    void Update()
    {
        // Odadan cikildiysa kilidi birak, yoksa esya cevrimdisi oyunda
        // sonsuza kadar tutulamaz kalir.
        if (!InRoom && m_HolderUserIndex >= 0)
        {
            m_HolderUserIndex = -1;
            RefreshLocalGrabPermission();
        }
    }
}
