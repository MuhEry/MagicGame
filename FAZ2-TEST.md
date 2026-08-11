# Faz 2 — Alteruna multiplayer: kurulum ve test

Hedef (sartname): **iki Quest 2 ayni Wi-Fi'de, LAN uzerinden birbirini gorsun.**
Ucretsiz katman 2 oyuncu ile sinirli, tasarim da 2 kisiye gore.

> **Altin kural:** oda kurulmadigi surece oyun Faz 1'deki gibi calisir.
> Butun ag yollari `IsInRoom` ile korumali. Bir sey bozulursa once odadan cik,
> tek oyuncu akisi hala calisiyor mu bak — calisiyorsa sorun ag katmanindadir.

---

## 0. Once bunu oku — editorde Play'e basma

Gozluk Link ile bagliyken Unity'nin ana dongusu gozlugun kare temposuna kilitlenir.
Gozluk takili/uyanik degilse **editor donmus gorunur veya Play'e basar basmaz kapanir**,
Console'a tek satir hata yazmadan. Bu bir cokme degil (bkz. HATA-COZUM kayit #12).

Masaustunde denemek isterseniz bir kez su komutu calistirin:

```
Tools > Gece Vardiyasi > Editorde XR Baslatmayi Kapat (Standalone)
```

Bu **yalnizca Standalone** hedefini degistirir. **Android ayari degismez** — APK'da VR aynen calisir.
Geri almak icin `Editorde XR Baslatmayi Ac (Standalone)`.

Gercek test her zaman **Build & Run** ile gozlukte yapilir.

---

## 1. Kurulum (bir kez)

1. `Assets/_Project/Scenes/Main.unity` sahnesini ac.
2. Menuden calistir:

```
Tools > Gece Vardiyasi > Faz 2 Kurulumunu Uygula
```

Komut sunlari yapar:

| Ne | Nereye |
|---|---|
| `MultiplayerManager` | `Multiplayer` objesi (zaten varsa dokunmaz) |
| Alteruna `Spawner` + esya listesi | ayni obje |
| `NetworkShiftCoordinator` | `Systems` |
| `PlayerRefs` (kamera + iki el) | `Systems` |
| `NetworkPlayerAvatar.prefab` (kafa + 2 el) | `Assets/_Project/Prefabs/` |
| `ItemOwnership` + fizik/transform senkronu | 9 esya prefabina |
| 2 baslangic noktasi (aralari 1,5 m) | `PlayerSpawnPoints` |

**Sahne KAYDEDILMEZ.** Kontrol edip kendiniz `Ctrl+S` yapin.
`ProjectSettings`, XR loader'lari ve URP ayarlari bu komuttan **hic etkilenmez**.

3. Kontrol et:

```
Tools > Gece Vardiyasi > Faz 2 Kurulumunu Kontrol Et
```

Console'da "TUMU HAZIR" gorene kadar eksikleri kapatin.

### Transport hakkinda: UDP diye bir secenek YOK

Alteruna 2.1'de `TransportType` yalnizca sunlari icerir:

```
NaN = 0   Default = 1   TCP = 2   TCPS = 3   WebSocket = 4
```

Dogru deger **TCP** (veya Default). WebSocket yalnizca WebGL icindir. "Transport = UDP"
diyen bir kaynak gorurseniz eski surume aittir.

**LAN kesfi ayri bir ayardir** ve UDP yayinini kendi icinde kullanir; sizin secmeniz
gereken tek sey `LAN Discovery` isaretinin acik olmasidir. Bu projede zaten acik:

| Ayar | Mevcut deger | Durum |
|---|---|---|
| LAN Discovery | acik | dogru |
| Transport | TCP | dogru (baska secenek yok) |
| Disable Cameras On Non-Owned Avatars | acik | dogru |
| Veri portu / kesif portu | 20000 / 19090 | varsayilan |
| EnableLOD | acik | 90 sn'lik turda gereksiz, kapatilabilir |

`Faz 2 Kurulumunu Kontrol Et` bu degerleri artik otomatik denetliyor.

### Elle yapilacak tek adim

```
Tools > Gece Vardiyasi > Android Ag Iznini Zorunlu Yap
```

Player Settings > Other Settings > **Internet Access = Require** yapar. `Auto`da Unity
ag iznine ihtiyac olup olmadigini kendi tahmin eder; Alteruna bu tahmine takilabilir ve
sonuc klasik tablodur: editorde calisir, gozlukte hic baglanmaz. Bu bir ProjectSettings
degisikligi oldugu icin kurulum komutuna DAHIL DEGIL, ayri ve istenerek calisir.

`Assets/link.xml` IL2CPP kirpmasini zaten engelliyor; Managed Stripping Level'i
dusurmenize gerek yok.

**Lisans / Application ID gerekmiyor.** Bu kurulum bulut kullanmadigi icin
`Window > Alteruna > Register license` adimi atlanabilir.

### Commit edilmesi gereken iki dosya

| Dosya | Neden |
|---|---|
| `Assets/Resources/AlterunaConfig.asset` | **Su an repoda YOK.** LAN Discovery, transport ve port ayarlari burada; commit edilmezse ekipteki digerlerinde bu ayarlarin hicbiri olmaz. |
| `ProjectSettings/ProjectSettings.asset` | Android ag izni komutu burayi degistirir. |

---

## 2. Cihaz testi (iki Quest 2)

> **Ag secimi:** okul/kurum Wi-Fi'sinde **AP izolasyonu** neredeyse her zaman aciktir
> ve UDP yayinini engeller — LAN kesfi hic calismaz. **Telefon hotspot'u acin ve iki
> gozlugu ona baglayin.** Bunu yedek plan degil, birincil plan yapin; test gunu
> hotspot hazir olsun.

1. Iki gozlugu **ayni aga** (tercihen hotspot) alin.
2. Ikisine de **ayni APK'yi** kurun (`Build & Run`). Farkli derlemeler farkli nesne
   kimlikleri uretir; "bende calisiyordu"nun en sik sebebi budur.
3. Derleme oncesi sahneyi **kaydedin** (Ctrl+S). Alteruna UID'leri sahne dosyasina yazar.
4. Birinci gozluk: `NetworkShiftCoordinator.HostLanSession()` cagrilan butona basar.
5. Ikinci gozluk: `JoinLanSession()` cagrilan butona basar.
   - Kesif calismazsa `JoinDirect("<host-ip>")` ile dogrudan baglanin.
6. Console/log'da su satiri arayin:

```
[Network] Odaya katilindi. Rol: HOST | ISTEMCI, kullanici=2, indeks=...
```

7. "Yeni Vardiya"ya **host** basar. Istemci vardiyayi otomatik olarak ayni seed ile alir.

### Neden bulut yok

Bu kurulum **yalnizca LAN** kullanir. Bulut/oda-listesi yolu lisans dogrulamasi,
internet ve sunucu gecikmesi getirir; iki gozluk ayni odadayken hepsi gereksiz risktir.
`ConnectOnStart` kapali oldugu icin Play'e basmak hicbir ag islemi baslatmaz — baglanti
yalnizca butona basildiginda kurulur. Calisan bir LAN kurulumundan buluta gecmek
kolaydir, tersi degildir.

### Bir sey olmazsa: once logu okuyun

Kod okumaya baslamadan once Console'a bakin. Ag hatalari artik sebebiyle birlikte yaziliyor:

- `[Network] ODAYA ALINMADIK. Sebep: ...` — oda dolu / surum uyusmazligi
- `[Network] AG HATASI. Endpoint=...` — ag katmani
- `[Network] Baglanti koptu.` — gozluk uyku sensoru veya Wi-Fi guc tasarrufu

Daha fazlasi icin `NetworkShiftCoordinator` bileseninin `...` menusunden
**Ag Teshis Bilgisini Yaz** komutunu calistirin. Cihazda canli log:

```bash
adb logcat -s Unity:V
```

### Rol nasil belirlenir

`Me.Index == LowestUserIndex` olan **host**tur. `Index % 2` gibi bir sey KULLANILMAZ:
Alteruna indeksleri 0/1 olmak zorunda degil, ikisi de tek (veya cift) indeks alirsa
iki cihaz da ayni rolu ustlenir.

---

## 3. Neyin kimde calistigi

| Is | Host | Istemci |
|---|---|---|
| Sure sayaci | isletir | host'un `ApplyClock` yayinini uygular |
| Esya uretimi | uretir (Alteruna `Spawner`) | kopyasini alir |
| Skor | hesaplar | yalnizca gosterir |
| Karar dogrulama | kendi sahnesinden **dogrular** | host'a istek gonderir |
| Vardiya bitisi | karar verir | `ApplyShiftEnd` ile ayni anda rapora gecer |

Esyayi kim ELE alirsa o kilitler; digerinin cihazinda `XRGrabInteractable` kapanir.
Dolap soketleri bu kilidin disindadir.

---

## 4. Kontrol listesi

- [ ] Odaya girmeden tek oyuncu akisi **eskisi gibi** calisiyor (en onemli madde)
- [ ] Iki gozluk odaya giriyor, log'da `kullanici=2` yaziyor
- [ ] Iki gozlukte de karsi oyuncunun kafasi ve iki eli gorunuyor ve hareket ediyor
- [ ] Bacadan **ayni** esya iki gozlukte de dusuyor (ayni seed)
- [ ] Bir oyuncu esyayi tutarken digeri ayni esyayi kavrayamiyor
- [ ] Dogru yerlestirmede esya **iki gozlukte de** kayboluyor
- [ ] Skor iki gozlukte ayni
- [ ] 90 saniye dolunca ikisi de ayni anda rapora geciyor
- [ ] `telemetry.csv` cihazdan cekilebiliyor, en az 8 satir:
      `adb pull /sdcard/Android/data/<paket.adi>/files/telemetry.csv`

---

## 5. Sorun giderme

| Belirti | Bak |
|---|---|
| Log'da `[Network] Alteruna kopru baglandi` HIC yok | Sahnede `MultiplayerManager` yok. Kurulum komutunu calistir. |
| Odaya girildi ama esya hic dusmuyor | `LogError` "Alteruna Spawner atanmamis" veya "SpawnableObjects listesinde yok" — kurulum komutunu tekrar calistir. |
| Iki gozlukte farkli esya | Iki `SpawnableObjects` listesi ayni sirada degil. Ayni APK'yi ikisine de kurdugunuzdan emin olun. |
| Karsi oyuncunun avatari yok | `MultiplayerManager.AvatarPrefab` bos veya `AvatarSpawning != SpawnOnJoin`. Kontrol komutu bunu raporlar. |
| Avatar duruyor, hareket etmiyor | `PlayerRefs`te kafa/el transformlari bos. Console'da `[Avatar] ... bos` uyarisi cikar. |
| "Missing Dependency — Alteruna SDK not detected" penceresi | Yanlis alarm (HATA-COZUM #26). **`Download`a basmayin**, proje derlenmez hale gelir. |
| Play'e basinca editor doniyor/kapaniyor | HATA-COZUM #12 (XR) ve #30 (ConnectOnStart). Kurulum komutu ConnectOnStart'i kapatir. |
| Editorde calisiyor, gozlukte hicbir sey senkron degil | `Assets/link.xml` silinmis. IL2CPP RPC'leri kirpiyor. HATA-COZUM #32. |
| Esya iki el arasinda titriyor | Kavramada `TakeOwnership()` yok. HATA-COZUM #34. |
| Uzak avatar kayik duruyor | Avatar parcalarinda `UseGlobalPosition` kapali. HATA-COZUM #35. |
| Iki gozluk birbirini hic gormuyor | Buyuk ihtimalle AP izolasyonu. Telefon hotspot'u kullanin. HATA-COZUM #33. |

---

## 6. Bilinerek yapilmayanlar

- Oda acma/katilma icin **hazir UI yok**. `HostLanSession()`, `JoinFirstAvailableRoom()`
  ve `JoinLanSession(string)` public metotlardir; bir butonun `OnClick`ine baglanmalari yeter.
- Ses (voice chat) yok — sartname kapsaminda degil.
- 2 oyuncudan fazlasi denenmedi; ucretsiz katman zaten 2 ile sinirli.
