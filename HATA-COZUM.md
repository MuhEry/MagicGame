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

**XRI 3.x farklari — 2.x ornegi kopyalamadan once oku:**

- `CanSelect` imzasi `CanSelect(IXRSelectInteractable)`. 2.x'teki `CanSelect(XRBaseInteractable)` overload'i `XRBaseInteractor.deprecated.cs` icinde, kullanma.
- Namespace'ler bolundu: `...Toolkit.Interactors` (XRSocketInteractor, XRBaseInteractor) / `...Toolkit.Interactables` (XRGrabInteractable, IXRSelectInteractable) / `...Toolkit` (SelectEnterEventArgs, XRInteractionManager).
- Haptik: 2.x'teki `XRBaseController.SendHapticImpulse(a, d)` deprecated. 3.x'te `HapticsUtility.SendHapticImpulse(amplitude, duration, HapticsUtility.Controller.Both)` — namespace `UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics`.
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
| 12 | Ekip | Parlak esya sadece eldeyken kontrol edildigi icin masada/kameraya yakinken hic parlamiyordu | `ItemProbe` parlamayi elde tutmaya degil, ana kameraya 0,35 m yakinliga baglar ve URP emission keyword'unu etkinlestirir. |
| 13 | Ekip | Prefab sekli kategoriyle bire bir eslestigi icin oyuncu duyusal sinyal yerine gorunuse bakiyordu | `ItemSpawner` her dogusta ItemData'nin runtime kopyasina rastgele kategori atar; kaynak asset degismez. |
| 14 | Ekip | Spawn kuyrugu 9 prefab sonra bitiyordu | Kuyruk bitince ayni seeded `System.Random` ile yeniden karistirilir; vardiya sonuna kadar devam eder. |
