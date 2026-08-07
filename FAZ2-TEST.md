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
| 3 | `Tools > Gece Vardiyası > Faz 2 Kontrol Listesini Doğrula` | Asagidaki her maddeyi tek tek dogrular |

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
- [ ] HATA-COZUM.md en az 5 kayit iceriyor *(su an 23 kayit var)*

### Faz 2'ye ozel
- [ ] Iki Quest ayni odada birbirini goruyor (kafa + 2 el)
- [ ] Kavramada sahiplik devroluyor, esya donmuyor
- [ ] Skoru host tutuyor, istemci yalnizca gosteriyor
- [ ] Ortak vardiya hedefi iki kisiyle birlikte oynanacak sekilde kurgulandi (kapasite 2)

---

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
