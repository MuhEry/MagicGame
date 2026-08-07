# Faz 2 — Multiplayer: kurulum ve test

Bu dosya sartnamedeki **"Faz 2 kontrol listesi"** ve **"Asagidakilerin hepsi saglanmadan gun
bitmis sayilmaz"** bolumlerinin nasil dogrulanacagini anlatir.
Hata ve cozumleri [HATA-COZUM.md](HATA-COZUM.md) dosyasina yaz — bir hata, bir satir.

Branch: `faz2-final`. Faz 2 oncesi hali `muhery_C` branch'inde durur, dokunulmadi.

---

## 0. Bir kere: sahneyi kur

Unity'yi ac, `Assets/_Project/Scenes/Main.unity` sahnesini ac ve sirayla:

| # | Menu | Ne yapar |
|---|---|---|
| 1 | `Tools > Gece Vardiyası > Multiplayer Kurulumunu Uygula` | MultiplayerManager, AutoJoin, XR rig avatar bilesenleri, Alteruna Spawner, esya prefabi senkronizasyonu |
| 2 | `Tools > Gece Vardiyası > Oyuncu Konumları ve HUD Kurulumunu Uygula` | Iki oyuncu baslangic noktasi, HUD_Player1 / HUD_Player2, ag teshis satiri |
| 3 | `Tools > Gece Vardiyası > VR Girdisini Onar (OpenXR + Input Actions)` | **Kontrolcu girdisini ayaga kaldirir** — asagiya bak |
| 4 | `Tools > Gece Vardiyası > Build Settings'i Onar (yalnızca Main)` | Build listesinden sizmis ornek sahneleri temizler |
| 5 | `Tools > Gece Vardiyası > Faz 2 Kontrol Listesini Doğrula` | Asagidaki her maddeyi tek tek dogrular |

### 3. adim neden kritik

"Gozlukte hicbir sey algilanmiyor" belirtisinin IKI ayri sebebi vardi ve ikisi de
yalnizca **uyari** uretiyordu, hata degil — bu yuzden Console'da fark edilmeden geciliyordu:

1. **OpenXR'da tek ozellik acikti** ("Meta Quest Support"). Hicbir interaction profile
   acik olmayinca OpenXR kontrolcu girdilerini hicbir action'a baglamaz. `Hand Tracking
   Subsystem` de kapali oldugu icin Hands rig'i el izlemesine abone olamiyordu.
2. **Sahnede `InputActionManager` yoktu.** O bilesen olmadan XRI'in Input Action Asset'i
   hic etkinlestirilmez: kontrolcu pozisyonu guncellenmez, kavrama tetiklenmez.

Komut ikisini de onarir ve neyi degistirdigini yazar.

> **Durum:** Bu adimlar 7 Agustos 2026'da bu makinede calistirildi; kontrol listesi
> **"Tum zorunlu maddeler tamam (0 uyari)"** dondu. Unity Console 0 hata / 0 uyari.

> Sahne tamamen bozulursa `Tools > Kayıp Eşya > Main Sahnesini Kur` sahneyi sifirdan
> uretir ve **artik Faz 2 kurulumunu da uygular**. Eskiden bu komut multiplayer'i siliyordu.

**Onemli:** Bu komutlar artik sahne acilinca KENDILIGINDEN calismaz (bkz. HATA-COZUM #19).
Sahneyi degistirmek istiyorsan menuden sen calistiracaksin.

---

## 1. Editorde dogrulama (2 dakika)

`Tools > Gece Vardiyası > Faz 2 Kontrol Listesini Doğrula` komutunu calistir.
Console'a su basliklar altinda `[OK] / [HATA] / [UYARI]` satirlari yazilir:

1. Paketler — Alteruna SDK + Multiplayer XR Template
2. Oda ve avatar — kapasite 2, ConnectOnStart, AvatarPrefab, iki spawn noktasi, AutoJoin
3. XR rig — Avatar, XRIAvatar, kafa + iki el TransformSynchronizable, **namespace'siz script yok**, locomotion kapali
4. Sistemler — ShiftManager / ItemSpawner / TelemetryLogger / NetworkShiftCoordinator / Spawner ve aralarindaki baglar, seed, **iki spawn listesinin ayni sirada olmasi**
5. Esya prefabi — Grab + RigidbodySynchronizable + XRGrabInteractableSync + NetworkItemState + ItemIdentity + ItemProbe, 9 ItemData
6. Dolaplar — uc kategori de sahnede
7. Paneller — iki HUD, slot 0 ve 1, ag teshis satiri
8. Build — Build Settings yalnizca Main.unity, platform Android, paket adi

**Hicbir `[HATA]` kalmadan cihaza gecme.** Bu kontrol, gozlukte 20 dakika kaybettiren
sahne baglanti hatalarinin hepsini saniyeler icinde yakalar.

---

## 2. Editorde tek kisilik akis testi

1. Play'e bas.
2. HUD'daki **Yeni Vardiya** butonuna bas.
3. Console'da `[ItemSpawner] Vardiya seed = ...` gorunmeli, bacadan esya dusmeli.
4. Esyayi bir dolaba surukle: dogru -> yesil + rafa girer, yanlis -> kirmizi + 0,4 sn sonra disari firlar.
5. Sure dolunca **Vardiya Raporu** paneli acilmali.

Bu asamada ag yok; `NetworkShiftCoordinator` "ODA YOK (cevrimdisi)" der ve oyun
tek kisilik calisir. Faz 1 davranisi hic bozulmadi.

---

## 3. Iki cihazda ag testi

### Hazirlik
- Iki Quest 2 **ayni Wi-Fi agina** bagli olmali.
- Meta developer organizasyonu **dogrulanmis** olmali, yoksa cihaz APK almaz.
- Ilk Android build'i uzun surer; bunu 3. saate birakma.

### Teshis satirini ac
Gozlukte ne oldugunu gormek icin, cihaza gondermeden once:
`HUD_Player1` (ve `HUD_Player2`) uzerindeki **NetworkDiagnosticsHud** bileseninde
`showOnStart` kutusunu isaretle. Panelde su satirlar cikar:

```
AG: ODADA | rol=HOST | kullanici=2 | indeks=1
VARDIYA: Vardiya | kalan=72 sn | D/Y=3/1
ESYA: seed=-1483920174 | tezgahta=Item_Sesli_Kutu_101
SON KARAR: DOGRU id=101 Sesli->Sesli
```

### Adimlar
1. Iki cihaza da ayni APK'yi kur.
2. Birinci cihazda oyunu ac. `AG: ODA YOK` -> birkac saniye icinde `AG: ODADA | rol=HOST` olmali.
3. Ikinci cihazda oyunu ac. Iki panelde de `kullanici=2` gorunmeli; ikinci cihaz `rol=ISTEMCI` demeli.
4. Birbirinizin kafasini ve iki elini goruyor musunuz? (avatar senkronizasyonu)
5. **Yalnizca host** vardiyayi baslatabilir; istemcideki butona basmak host'a istek gonderir.
6. Esyayi biri tutsun, digeri baksin — esya iki cihazda da ayni yerde mi?
7. Esyayi elden ele verin — kavrayan kisi sahiplik almali, esya donmamali.
8. Dogru dolaba yerlestirin — esya **iki cihazda da** rafa girmeli, skor **iki panelde de** artmali.
9. 90 saniye dolsun; rapor iki cihazda da ayni sayilari gostermeli.

### Neye bakilacak
| Belirti | Muhtemel sebep |
|---|---|
| `AG: Multiplayer bileseni bulunamadi` | Sahnede MultiplayerManager yok -> kurulum komutunu calistir |
| `AG: ODA YOK` kalici | AutoJoin yok, ag yok veya iki cihaz farkli Wi-Fi'de |
| Iki cihaz da `rol=HOST` | Ayni odaya girilmemis; oda listesi bos olabilir |
| Ikinci oyuncu gorunmuyor | AvatarPrefab bos veya rig'de Avatar/XRIAvatar yok |
| Ikinci oyuncunun avatari bozuk | Rig altinda namespace'siz script var (HATA-COZUM #21) |
| Esya birinde var digerinde yok | Spawner listesi uyusmuyor (HATA-COZUM #16) veya spawner atanmamis (#17) |
| Skor yalnizca bir tarafta artiyor | NetworkShiftCoordinator baglanmamis |

---

## 4. Telemetriyi cihazdan cek

```bash
adb pull /sdcard/Android/data/com.magicgameteam.gecevardiyasi/files/telemetry.csv
```

Dosya en az 8 satir icermeli ve tum sutunlar dolu olmali:
`zaman_damgasi, oturum_id, esya_id, dogru_kategori, secilen_kategori, dogru_mu, inceleme_suresi_ms, sallama_sayisi`

Skoru host tuttugu icin **host cihazin CSV'si** referanstir.

---

## 5. Gun sonu kontrol listesi (sartnameden)

- [ ] Uc gelistirici de dev branch'ini temiz cekip kendi makinesinden derleyebiliyor
- [ ] APK uc Quest 2'de de aciliyor, 90 saniyelik tur cokmeden tamamlaniyor
- [ ] 9 esyanin 9'u da tutulabiliyor; uc yoklama kanali da calisiyor
- [ ] Dogru/yanlis geri bildirimi uc kanaldan (gorsel + isitsel + haptik) geliyor
- [ ] Yanlis yerlestirmede mikro-aciklama panoda gorunuyor
- [ ] OVR Metrics Tool ile 72 Hz dogrulandi, dusen kare gozlenmedi
- [ ] telemetry.csv cihazdan cekildi, en az 8 satir ve tum sutunlar dolu
- [ ] HATA-COZUM.md en az 5 kayit iceriyor *(su an 25 kayit var)*

### Faz 2'ye ozel
- [ ] Iki Quest ayni odada birbirini goruyor (kafa + 2 el)
- [ ] Kavramada sahiplik devroluyor, esya donmuyor
- [ ] Skoru host tutuyor, istemci yalnizca gosteriyor
- [ ] Ortak vardiya hedefi iki kisiyle birlikte oynanacak sekilde kurgulandi (kapasite 2)

---

## "Missing Dependency" penceresi geri gelirse

Her derlemeden sonra **"Alteruna Multiplayer SDK is required but was not detected"**
penceresi cikiyorsa, birisi `AlterunaDependencyCheck.dll`'i geri getirmis demektir.
Bu DLL SDK **v1**'in tip adini ariyor, kurulu SDK 2.1 oldugu icin **her zaman**
basarisiz olur — yanlis alarmdir.

- **`Download`'a BASMAYIN.** SDK'nin ikinci bir kopyasini `Assets/` altina indirir;
  her Alteruna tipi iki kez tanimlanir (`CS0101`) ve proje hic derlenmez.
- Kalici cozum: `Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Editor/Plugins/AlterunaDependencyCheck.dll`
  dosyasini `.meta`'siyla birlikte sil. Projede baska hicbir referansi yok.

## Editorde Play'e basinca Unity DONUYORSA

**Editorde Quest Link ile test etmeyin.** Gozluk Link ile bagliyken Play'e basildiginda
Unity'nin ana dongusu gozlugun kare temposuna kilitlenir (`xrWaitFrame`). Gozluk
takili degilse, uykudaysa veya Link oturumu goruntu sunmuyorsa Unity beklemede kalir ve
**editor tamamen donmus gorunur**. Cokme degildir; islemci de bosta durur.

Donan oturumun `Editor-prev.log` dosyasindaki imzasi:

```
[XR] Available Layers: (0)
XR: Error setting active audio output driver. Falling back to default.
[Subsystems] Loading plugin UnityOpenXR for subsystem OpenXR Display...
[Subsystems] Loading plugin UnityOpenXR for subsystem OpenXR Input...
   <- burada susar, hicbir istisna veya hata YOKTUR
```

Hata mesaji ARAMAYIN, yok. Dogru yol: **Build & Run** ile APK'yi cihaza atmak.
Sartname de zaten bunu istiyor ("kendi gozlugunde derleyip calistir").

Editorde tek kisilik akisi denemek isterseniz: XR Plug-in Management'ta OpenXR'i
gecici olarak kapatin; oyun masaustu penceresinde calisir, ag katmani aynen calisir.

## "Gozlukte siyah ekran / hicbir sey yok" ise

Sahnedeki rig avatar SABLONU oldugu icin PASIF durur (dogru hal). Alteruna spawn ettigi
avatari acmazsa sahnede hic kamera kalmaz ve gozlukte siyah ekran gorunur.
`OfflineRigFallback` (Rig Bekcisi) bunu yakalar: 5 sn boyunca aktif kamera yoksa once
Alteruna'nin spawn ettigi avatari, o da yoksa sablon rig'i acar ve Console'a ne yaptigini
yazar. Console'da `[Rig Bekcisi]` satirini ararsaniz hangi durumun gerceklestigini
dogrudan gorursunuz.

## Editorde "VR algilanmiyor" ise: once gozlugu kontrol et

Editor log'unda su varsa proje hatasi YOKTUR, bilgisayara gozluk bagli degildir:

```
Function: Display_Initialize
Message: XrResult failure [XR_ERROR_FORM_FACTOR_UNAVAILABLE]
[FAILURE] xrGetSystem: XR_ERROR_FORM_FACTOR_UNAVAILABLE
```

`XR_ERROR_FORM_FACTOR_UNAVAILABLE` = OpenXR bagli bir basliık bulamadi. Editorde
VR ile test etmek icin Quest'in **Link / Air Link** ile bagli ve **Meta Quest Link**
uygulamasinin acik olmasi, OpenXR runtime'inin da Meta olarak secili olmasi gerekir.
Aksi halde dogru yol: **Build & Run** ile APK'yi cihaza atmak.

## ~~Acik sorun: avatar kopyalanmasi~~ (COZULDU)

Editorde Play'e basildiginda log'da sunlar cikiyor:

```
Warning: Synchronizable already registered. XR Origin Hands (XR Rig)(Clone) -> TransformSynchronizable<a5973d4a-...>
Warning: Synchronizable already registered. Main Camera -> TransformSynchronizable<f961ecc6-...>
...
Warning: Synchronizable not registered. XR Origin Hands (XR Rig) -> TransformSynchronizable<a5973d4a-...>
There are 2 audio listeners in the scene.
```

**Sebep:** `MultiplayerManager.AvatarPrefab`, sahnedeki **canli** XR rig'ini gosteriyor.
Alteruna odaya girerken onu klonluyor; klon, orijinalle **ayni** `CommunicationBridgeUID`
GUID'lerini tasiyor. Klonun kaydi orijinalinkini disari itiyor, sonra orijinal rig
"not registered" diyerek senkron gonderemez hale geliyor. Ayrica sahnede iki kamera
ve iki AudioListener olusuyor.

**Cozum:** Alteruna'nin kendi ornek sahnesi incelendi. Orada avatar sablonu **PASIF**
duruyor — `XR Avatar Rig` nesnesinin `m_IsActive` override'i **0**. Pasif oldugu icin
`CommunicationBridgeUID.OnEnable` hic calismaz, rig kendi UID'siyle kaydolmaz ve klonla
cakisma olusmaz. Dokumantasyon da bunu destekliyor: *"Each synchronizable receives a
unique global identifier ... to prevent collisions."*

Bizim rig de artik pasif (`Multiplayer Kurulumunu Uygula` bunu otomatik yapar).
Cevrimdisi oyun bozulmasin diye `OfflineRigFallback` eklendi: belirlenen sure boyunca
odaya girilemezse rig'i yerel olarak acar, sonradan odaya girilirse tekrar kapatir.

Sahnede rig'in **soluk** (pasif) gorunmesi artik DOGRU haldir; oyuncu rig'ini
Alteruna spawn eder.

## Bilinen sinirlar

- **LAN keşfi kapali.** Su an eslesme Alteruna'nin varsayilan yolundan gidiyor, yani
  iki cihazin internete cikabilmesi gerekiyor. Saf LAN icin `NetworkShiftCoordinator`
  uzerindeki `HostLanSession()` / `JoinLanSession(ip)` metotlari hazir ama bir UI'a
  bagli degil. Sartname "LAN uzerinden test edin" diyor; internet varken de gecerli
  bir iki kisilik test yapilabilir.
- **Istemci saati 2 Hz guncellenir.** Host saati 0,5 saniyede bir yayinlar; istemcide
  geri sayim hafif basamakli akar. Skor ve karar akisi etkilenmez.
- **Faz 2'de dogrulama cihazda yapilmadi.** Bu branch'teki degisiklikler derleme
  seviyesinde dogrulandi (`dotnet build`, 0 hata) ve sahne baglantilari kontrol
  listesiyle denetlenebilir hale getirildi; iki Quest ile calistirma testi ekipte.
