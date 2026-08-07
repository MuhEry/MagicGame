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
| 15 | Faz2 | `NetworkShiftCoordinator.Subscribe()` yalnizca `Start()`'ta cagriliyordu; `Multiplayer` o an null ise bir daha HIC denenmiyor, `OnRoomJoined` gelmiyor, `IsInRoom` sonsuza kadar false kaliyor ve iki gozluk de sessizce CEVRIMDISI oynuyordu | `Update()` icinde `if (!subscribed) Subscribe();` ile tekrar denenir. Kopru kurulunca Console'a `[Network] Alteruna kopru baglandi` yazilir. |
| 16 | Faz2 | `ItemSpawner`, Alteruna `Spawner.Spawn(index, ...)` cagrisina `itemPrefabs.IndexOf(entry)` gonderiyordu. Alteruna indeksi kendi `SpawnableObjects` listesine gore cozer; iki liste ayni sirada olmazsa host bir esyayi, istemci bambaska bir esyayi gorur | Indeks `networkSpawner.SpawnableObjects.IndexOf(prefab)` ile cozulur; bulunamazsa esya uretilmez ve hata loglanir. Faz 2 kontrol listesi iki listeyi karsilastirir. |
| 17 | Faz2 | Odadayken `networkSpawner` atanmamissa kod sessizce yerel `Instantiate`'e dusuyordu: host esyayi goruyor, istemci bos tezgaha bakiyordu | Odadayken spawner yoksa `Debug.LogError` + esya uretilmez. Sessiz desenkronizasyon yerine gorunur hata. |
| 18 | Faz2 | Dogru esyanin rafa girmesi her istemcide kendi yerel soket olayindan tetikleniyordu. Iki cihazda fizik birebir ayni olmadigi icin esya birinde kayboluyor, digerinde tezgahta kaliyordu | Host, karari uyguladiktan sonra `BroadcastItemConsumed(itemId)` yayinlar. `CategorySocket` bu olayi dinler ve esya hala duruyorsa ayni rafa koyar; zaten yerlesmisse hicbir sey yapmaz (idempotent). |
| 19 | Faz2 | `MultiplayerProjectSetup` ve `MultiplayerExperienceSetup`, `[InitializeOnLoadMethod]` ile Main.unity her acildiginda sahneyi degistirip **kaydediyordu**. Herkeste farkli bir Main.unity olusuyor, elle yapilan duzeltmeler bir sonraki acilista geri aliniyordu | Otomatik calisma kaldirildi. Kurulum yalnizca `Tools > Gece Vardiyasi` menusunden, istenerek calisir. `Main Sahnesini Kur` komutu artik Faz 2 kurulumunu da uygular (eskiden siliyordu). |
| 20 | Faz2 | `LocalPlayerHud` hangi paneli gosterecegini `Me.Index % 2` ile seciyordu. Alteruna kullanici indeksleri 0/1 olmak zorunda degil; iki oyuncu da tek (veya cift) indeks alirsa IKISI DE ayni paneli acar, diger panel hicbir cihazda gorunmez | Slot host/istemci rolune gore secilir (`Me.Index == LowestUserIndex`). Iki kisilik ucretsiz katmanda kesin ayrim budur. |
| 21 | Faz2 | **Tuzak:** Alteruna'nin `XRIAvatar.RemoveComponents()` metodu her alt `Behaviour` icin `type.Namespace.Length` okur. Bu projedeki tum scriptler global namespace'te oldugu icin `Namespace` NULL doner -> uzak avatar kurulurken `NullReferenceException`, temizlik yarida kalir, ikinci oyuncunun avatari bozuk (kamera/AudioListener silinmemis) kalir | XR rig'in altina KENDI scriptlerimizi ekleme. `PlayerRefs` rig yerine `Systems` altinda durur. Faz 2 kontrol listesi rig altindaki namespace'siz scriptleri hata olarak raporlar. |
| 22 | Faz2 | `ItemSpawner.seed` sahnede 555'e sabitlenmisti; her vardiya ayni esya sirasi geliyordu | Sahnedeki deger 0'a alindi (kayit #10 ile ayni kural). Odadayken host zaten `ApplyStart` ile kendi seed'ini istemciye gecirir. |
| 23 | Faz2 | Gozlukte bir sey calismadiginda tek gorunen "hicbir sey olmuyor"du; odaya girilemedi mi, host mu secilemedi, esya mi uretilmedi ayirt edilemiyordu | `NetworkDiagnosticsHud`: oda durumu, host/istemci rolu, kullanici sayisi, seed, tezgahtaki esya ve son karar gozlukte gorunur. Varsayilan kapali, Inspector'dan `showOnStart` ile acilir. |
| 24 | Faz2 | Her derlemeden sonra **"Missing Dependency — Alteruna Multiplayer SDK is required but was not detected"** penceresi cikiyordu. SDK asli kurulu, kod ona karsi derleniyordu; pencere yanlis alarmdi. Sucluyu bulmak zor: kontrolcu, XR Template'in kendi klasorunde degil `Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Editor/Plugins/AlterunaDependencyCheck.dll` icinde duruyordu | Bu DLL `[InitializeOnLoad]` ile `Type.GetType("Alteruna.Multiplayer.MultiplayerManager, Alteruna")` ariyor - bu SDK **v1**'in tip adi. Kurulu SDK 2.1'de tip `Alteruna.Multiplayer.Unity.MultiplayerManager`, yani kontrol HER ZAMAN basarisiz oluyordu. DLL silindi (projede baska hicbir referansi yoktu). **`Download`'a basmayin:** SDK'nin ikinci kopyasini `Assets/` altina indirir, her tip iki kez tanimlanir (`CS0101`) ve proje hic derlenmez. |
| 25 | Faz2 | Build Settings'te XRI orneginin `DemoScene.unity`'si duruyordu (devre disi ama listede) | `Tools > Gece Vardiyasi > Build Settings'i Onar` listeyi yalnizca Main.unity'ye indirir. Kontrol listesi artik yalnizca ETKIN sahnelere hata verir, devre disi olanlari uyari olarak gecer. |
| 26 | Faz2 | **Gozlukte hicbir sey algilanmiyordu.** OpenXR (Android) ayarlarinda TEK ozellik acikti: "Meta Quest Support". Hicbir interaction profile acik olmadigi icin OpenXR kontrolcu girdilerini hicbir action'a BAGLAMIYORDU; "Hand Tracking Subsystem" kapali oldugu icin de Hands rig'i el izlemesine abone olamiyordu (`Hand Tracking Subsystem not found or not running`). Ikisi de yalnizca UYARI uretiyor, hata uretmiyordu | `Tools > Gece Vardiyasi > VR Girdisini Onar` komutu Android hedefinde su ozellikleri acar: Oculus Touch Controller Profile, Hand Interaction Profile, Hand Tracking Subsystem. Kontrol listesi artik bunlari denetliyor. |
| 27 | Faz2 | Ayni belirtinin IKINCI sebebi: sahnede `InputActionManager` yoktu. O bilesen olmadan XRI'in Input Action Asset'i HIC etkinlestirilmez; kontrolcu pozisyonu guncellenmez, select/activate calismaz. Unity'nin uyarisi bunu birebir soyluyordu: *"The Input Action Manager behavior can be added to a GameObject in a Scene"* | Ayni onarim komutu sahneye `Input Action Manager` ekleyip `XRI Default Input Actions.inputactions` varligini baglar. `MainSceneBuilder` bunu hic olusturmuyordu - eksik olan buydu. |
| 28 | Faz2 | **ACIK SORUN:** `MultiplayerManager.AvatarPrefab` sahnedeki CANLI XR rig'ini gosteriyor. Alteruna onu klonlayinca ayni `CommunicationBridgeUID` GUID'leri iki nesnede birden oluyor: `Synchronizable already registered ... (Clone)` ardindan orijinal icin `Synchronizable not registered` -> orijinal rig'in transform senkronu OLUYOR. Ustelik `There are 2 audio listeners in the scene` | Henuz duzeltilmedi, cihaz testi gerekiyor. Dogru desen: avatar sablonu CANLI rig olmamali (Alteruna'nin ornek sahnesinde sablon ayri bir nesne). Ayrinti ve iki secenek: FAZ2-TEST.md > "Acik sorun: avatar kopyalanmasi". |
