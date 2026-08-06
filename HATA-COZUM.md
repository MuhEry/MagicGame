# HATA - COZUM

Ortak hata defteri. **Kural: her hata + cozumu TEK SATIR.** Uzun aciklama yazma.

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
- Haptik: Bilinen tek kumandaya doğrudan `SendHapticImpulse(a, d)` gönderilebilir. İki kumandaya birden göndermek için `HapticsUtility.SendHapticImpulse(amplitude, duration, HapticsUtility.Controller.Both)` kullan — namespace `UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics`.

---

## Kayitlar

| # | Kim | Hata | Cozum |
|---|---|---|---|
| 1 | B | Play acikken script derlendi -> XRI bilesenlerinde `NullReferenceException: routine is null` (XRSocketInteractor.OnEnable) ve `XRGrabInteractable.SubscribeTeleportationProvider` NRE | Play'i kapat, derleme bitsin, sonra Play'e bas. Awake'te atanan alanlar domain reload'da null kalir; kod hatasi degil. |
| 2 | B | Sokete kategori filtresi koyunca yanlis esya hic girmiyor, oyuncu geri bildirim alamiyor | `CanSelect`'i kategoriye gore KISITLAMA. Yanlis esyayi da kabul et, karari `OnSelectEntered`'da ver, yanlissa 0,4 sn sonra `SelectExit` + impulse. |
| 3 | B | Yanlis esya soketten cikinca havada asili kaliyor | `SelectExit`'ten sonra bir kare bekleyip `rb.isKinematic = false` yap, sonra `AddForce`. |
| 4 | B | URP'de emissive rengi runtime'da degismiyor | Materyalde Emission ACIK olmali (`EnableKeyword("_EMISSION")`, `globalIlluminationFlags = RealtimeEmissive`). Rengi `MaterialPropertyBlock` ile ez, materyali klonlama. |
