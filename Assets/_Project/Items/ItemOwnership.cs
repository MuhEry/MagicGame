using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Bir esyanin ag uzerindeki TUTMA SAHIPLIGI.
///
/// SORUN: XRI her istemcide bagimsiz calisir. Iki oyuncu ayni anda ayni esyaya
/// uzanirsa her ikisinin de kendi cihazinda kavrama basarili olur. Sonra iki
/// RigidbodySynchronizable birbirine ters pozisyon gonderir: esya iki el arasinda
/// titrer, ziplar, bazen zeminin altina kacar. Alteruna dokumantasyonu bunu acikca
/// soyler: "only one user can have ownership at a time ... using the ownership API
/// is not mandatory - developers must manually protect attribute access". Yani bu
/// korumayi bizim yazmamiz gerekiyor.
///
/// COZUM: Bir oyuncu esyayi ELE aldigi anda agda "bu esya bende" diye ilan eder.
/// Digerinin cihazinda esyanin XRGrabInteractable'i KAPATILIR; o oyuncu esyayi
/// hic kavrayamaz, dolayisiyla cekisme hic baslamaz. Birakildiginda geri acilir.
///
/// Soket (dolap) tutuslari bu kilidin disindadir: dolap esyayi aldiginda sahiplik
/// ilan edilmez, cunku o bir oyuncu tutusu degildir.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[DisallowMultipleComponent]
public sealed class ItemOwnership : AttributesSync
{
    XRGrabInteractable grabInteractable;
    RigidbodySynchronizable synchronizedBody;

    /// <summary>Esyayi elinde tutan kullanicinin Alteruna indeksi. -1 = bosta.</summary>
    int holderUserIndex = -1;

    int LocalUserIndex =>
        NetworkShiftCoordinator.Instance != null ? NetworkShiftCoordinator.Instance.LocalUserIndex : -1;

    bool InRoom =>
        NetworkShiftCoordinator.Instance != null && NetworkShiftCoordinator.Instance.IsInRoom;

    void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        synchronizedBody = GetComponent<RigidbodySynchronizable>();
    }

    // DIKKAT: base.OnEnable/OnDisable cagrilmazsa CommunicationBridgeUID kendini
    // Multiplayer'a KAYDETMEZ ve bu bilesenin RPC'leri sessizce hic calismaz.
    // (Derleyici bunu CS0114 ile uyarmisti: "hides inherited member".)
    public override void OnEnable()
    {
        base.OnEnable();

        if (grabInteractable == null)
            return;

        grabInteractable.selectEntered.AddListener(OnSelectEntered);
        grabInteractable.selectExited.AddListener(OnSelectExited);
    }

    // Base sinifta OnDisable yok (yalnizca OnEnable virtual), bu yuzden override degil.
    void OnDisable()
    {
        if (grabInteractable == null)
            return;

        grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        grabInteractable.selectExited.RemoveListener(OnSelectExited);
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

        if (synchronizedBody != null)
        {
            // Alteruna, yumusak guncellemeyi GONDEREN istemciyi fizik sahibi sayar.
            // Kavrayan taraf boylece yetkiyi devralir; ayri bir sahiplik sistemi gerekmez.
            synchronizedBody.AllowCollisionToAssumeOwner = true;
            synchronizedBody.SendData = true;
            synchronizedBody.SyncSettings();
            synchronizedBody.ForceUpdate();
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

        if (synchronizedBody != null)
        {
            synchronizedBody.SyncSettings();
            synchronizedBody.ForceUpdate();
        }

        if (!InRoom)
            return;

        int me = LocalUserIndex;
        if (me < 0 || holderUserIndex != me)
            return;

        ApplyHolder(me, false);
        InvokeRemoteMethod(nameof(ApplyHolder), UserId.All, me, false);
    }

    [SynchronizableMethod]
    void ApplyHolder(int userIndex, bool held)
    {
        // Baskasi tutarken gelen "biraktim" mesajini yok say: gecikmeli bir paket
        // aktif tutusu yanlislikla serbest birakabilir.
        if (!held && holderUserIndex != userIndex)
            return;

        holderUserIndex = held ? userIndex : -1;
        RefreshLocalGrabPermission();
    }

    void RefreshLocalGrabPermission()
    {
        if (grabInteractable == null)
            return;

        int me = LocalUserIndex;
        bool heldBySomeoneElse = holderUserIndex >= 0 && holderUserIndex != me;

        // Kavrama kapatilinca XRI o an tutan eli de birakmaya zorlar; kaybeden
        // taraf esyayi aninda birakir ve cekisme hic olusmaz.
        if (grabInteractable.enabled == !heldBySomeoneElse)
            return;

        grabInteractable.enabled = !heldBySomeoneElse;
    }

    void Update()
    {
        // Odadan cikildiysa kilidi birak, yoksa esya cevrimdisi oyunda tutulamaz kalir.
        if (!InRoom && holderUserIndex >= 0)
        {
            holderUserIndex = -1;
            RefreshLocalGrabPermission();
        }
    }
}
