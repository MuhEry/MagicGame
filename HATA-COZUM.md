# HATA - COZUM

Ortak hata defteri. **Kural: her hata + cozumu TEK SATIR.** Uzun aciklama yazma.

> Bu dosya `Assets/_Project/HATA-COZUM.md` ile birlestirildi. Tek dosya kalsin diye
> oradaki iki kayit (#1 ve #2) buraya tasindi, o kopya silindi.

## Ortam (once bunu oku)

| Ne | Surum |
|---|---|
| Unity | `6000.3.21f1` |
| XR Interaction Toolkit | **`3.4.1` (XRI 3.x)** |
| Render Pipeline | URP `17.3.0` |
| XR Hands | `1.7.3` |
| OpenXR | `1.16.1` |
| Alteruna Multiplayer | `2.1.1r3` / paket `2.1.1003` |

**XRI 3.x farklari - 2.x ornegi kopyalamadan once oku:**

- `CanSelect` imzasi `CanSelect(IXRSelectInteractable)`. 2.x'teki `CanSelect(XRBaseInteractable)` overload'i `XRBaseInteractor.deprecated.cs` icinde, kullanma.
- Namespace'ler bolundu: `...Toolkit.Interactors` (XRSocketInteractor, XRBaseInteractor) / `...Toolkit.Interactables` (XRGrabInteractable, IXRSelectInteractable) / `...Toolkit` (SelectEnterEventArgs, XRInteractionManager).
- Haptik: 2.x'teki `XRBaseController.SendHapticImpulse(a, d)` deprecated. 3.x'te `HapticsUtility.SendHapticImpulse(amplitude, duration, HapticsUtility.Controller.Both)` - namespace `UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics`.
- `XRBaseControllerInteractor` deprecated -> `XRBaseInputInteractor`.

---

## Kayitlar

| # | Kim | Hata | Cozum |
|---|---|---|---|
| 1 | C | `XRBaseControllerInteractor` XRI 3.0+ ile deprecated uyarisi | `XRBaseInputInteractor` ile degistirildi. |
| 2 | C | `ItemCategory` / `DecisionResult` hem `Core/` hem `Shared/` altinda tanimliydi -> `CS0101` duplicate, proje derlenmiyordu | Dosyalar `Shared/` altinda birakildi, `Core/` kopyalari silindi. |
| 3 | B | Play acikken script derlendi -> XRI bilesenlerinde `NullReferenceException: routine is null` (XRSocketInteractor.OnEnable) ve `XRGrabInteractable.SubscribeTeleportationProvider` NRE | Play'i kapat, derleme bitsin, sonra Play'e bas. `Awake`'te atanan alanlar domain reload'da null kalir; kod hatasi degil. |
| 4 | B | Sokete kategori filtresi koyunca yanlis esya hic girmiyor, oyuncu geri bildirim alamiyor | `CanSelect`'i kategoriye gore KISITLAMA. Yanlis esyayi da kabul et, karari `OnSelectEntered`'da ver, yanlissa 0,4 sn sonra `SelectExit` + impulse. |
| 5 | B | Yanlis esya soketten cikinca havada asili kaliyor | `SelectExit`'ten sonra bir kare bekleyip `rb.isKinematic = false` yap, sonra `AddForce`. |
| 6 | B | URP'de emissive rengi runtime'da degismiyor | Materyalde Emission ACIK olmali (`EnableKeyword("_EMISSION")`, `globalIlluminationFlags = RealtimeEmissive`). Rengi `MaterialPropertyBlock` ile ez, materyali klonlama. |
| 7 | B | Dogru esya sokete girince yok olmuyor, arka arkaya "DOGRU" sinyali basiliyor | `AcceptRoutine` esyayi birakip bir kare bekliyordu; o boslukta soket ayni esyayi tekrar yakaliyor, yeniden giris `StopCoroutine` ile rutini `Destroy`'a varmadan olduruyordu. Esyayi ONCE `SetActive(false)` ile etkilesim disina al, SONRA birak; rutin calisirken yeniden baslatma. |
| 8 | B | Esya dolabin icindeyken tikirti sesi caliyor / sallama sayiliyor | `XRGrabInteractable.isSelected` soket tuttugunda da `true` doner. Tutan interactor'in `XRSocketInteractor` olup olmadigina bak; yoklama kanallari yalnizca ELDE calissin. |
| 9 | B | `SelectExit()` cagrisi `OnSelectExited`'i SENKRON tetikliyor; oradaki `StopCoroutine` cagiran coroutine'i kendi icinden olduruyor | Temizlik dallarini bir bayrakla (`m_Rejecting` / `m_Accepting`) koru, yoksa `SelectExit` sonrasi kod hic calismaz. |
| 10 | B | Spawn sirasi her turda ayni (seed sabit `12345`) | `seed = 0` -> `Environment.TickCount`'tan tohum. `UnityEngine.Random` KULLANMA (mimari kural 4); Faz 2'de host `SetSeed(...)` ile istemciye ayni degeri gecirir. Kullanilan seed Console'a loglaniyor. |
| 11 | Ekip | `Main.unity` bosti (2 obje), Build Settings `Assets/Scenes/SampleScene.unity`'yi isaret ediyordu -> APK'da bos ekran | `Tools > Kayip Esya > Main Sahnesini Kur` menusu sahneyi kurar ve Build Settings'i duzeltir. |
| 12 | LAN | Internet acikken kayitli `MultiplayerManager.Awake()` Alteruna `/project` isteginde Quest'i donduruyor | LAN branch'inde `ApplicationData` GUID'i bos; lisans istegi atlanir, bir cihaz `Host()`, digeri `JoinLan()` kullanir. |
| 13 | LAN | Manager'i 8 saniye gec baslatmak gri ekran donmasini sadece geciktirdi | `AlterunaDelayedStartup` kaldirildi; LAN icin sabit bekleme kullanma. |
| 14 | LAN | Iki cihaz baglansa bile yerel `Instantiate` ile uretilen vardiya nesneleri ortak degil | Ilk testte yalniz host `Spawner.Spawn()` cagirir ve `ForceSync` aciktir; test kupu dogrulanmadan `ItemSpawner` ag koduna cevrilmez. |
| 15 | LAN | Unity yeniden acilinca Alteruna editoru bos `ApplicationData` GUID'ini kayitli projeyle geri yazdi; lisans sorgusu ve `No valid port for transport type Default` geri geldi | Manager sahnede kapali tutulur; `LanConnectionPanel` once bellek config'ini `Guid.Empty` yapar, basariliysa Manager'i etkinlestirir. |

## LAN sabah testi

1. Iki gozlugu ayni telefon hotspot'una bagla ve ayni APK'yi temiz kur.
2. Gozluk A `HOST LAN`, Gozluk B `JOIN LAN` secsin.
3. A panelinde `Rol: HOST`, B panelinde `Rol: CLIENT`, ikisinde `Oda: EVET` gorulmeli.
4. Yalniz Gozluk A `HOST: TEST KUPU URET` secsin; kup iki gozlukte ayni konumda gorulmeli.
5. Discovery bulamazsa host panelindeki IP'yi client alanina yazip `IP ILE BAGLAN` sec.
