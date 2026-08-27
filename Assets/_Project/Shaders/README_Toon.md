# RecycleGame Toon Shader

Takimin farkli kaynaklardan gelen assetlerini (Meshy modelleri, primitive'lerle
kurulmus mobilyalar, hazir VR template parcalari) tek gorsel dile sokmak icin
yazildi.

Hedef platform: **Meta Quest / Android, URP 17.3, Unity 6000.3.11f1**

---

## Dosyalar

| Dosya | Ne yapar |
|---|---|
| `Toon.shader` | Asil shader. Materyal listesinde `RecycleGame/Toon` adiyla gorunur. |
| `ToonLighting.hlsl` | Ortak ramp matematigi. |
| `../Scripts/Editor/ToonShaderGUI.cs` | Materyal inspector'i (outline ac/kapat kutucugu). |

Editor menusune (Tools) hicbir sey eklenmez.

---

## Durum

`Assets/Denizinyeri/Materials` altindaki **52 materyal** bu shader'a cevrildi.
Cevrilmeyen 2 materyal:

- `M_Cam` — saydam (cam), URP/Lit'te kalmali
- `M_PetSise` — saydam (pet sise), URP/Lit'te kalmali

Toon shader saydam yuzey desteklemiyor; bu ikisini opaga cevirmek sahneyi bozardi.

---

## Materyal ayarlari

Inspector'da her materyalde:

| Grup | Ne ise yarar |
|---|---|
| **Toon Ramp** | `Shade Color` golge rengi, `Ramp Steps` kademe sayisi (1 = en net toon), `Ramp Threshold` terminatorun yeri, `Baked GI Strength` lightmap katkisi |
| **Specular / Rim** | Varsayilan kapali. Kapaliyken sifir maliyet (`shader_feature_local`). |
| **Emission** | Lamba/ekran icin. Donusturucu URP/Lit'ten otomatik tasidi. |
| **Outline** | `Outline Color`, `Outline Width` (cm). Alttaki **Draw Outline Pass** kutucugu pass'i tamamen kapatir. |

### Ortak palet

Su an tum materyallerde ayni degerler var — asetleri birbirine benzeten asil sey bu:

```
Shade Color     (0.35, 0.39, 0.55)   soguk mavi golge
Outline Color   (0.07, 0.07, 0.09)
Ramp Steps      1
Ramp Threshold  0.25
Ramp Smoothness 0.03
Outline Width   0.5 cm
```

Toplu degistirmek gerekirse: Project'te materyalleri coklu sec, Inspector'dan
degeri bir kez yaz. Unity multi-edit ile hepsine birden yazar.

### Baked GI Posterize — bu sahnenin can damari

Bu sinif **tamamen bake edilmis bir ic mekan**. Directional isik iceride
shadowmask ile kapali, yani her yuzey ayni banda dusuyor ve `NdotL` tabanli
toon kirilmasi hic olusmuyor. Sadece direkt isigi kademelendiren bir toon
shader bu sahnede gorsel olarak **hicbir sey yapmaz** — gorunen tum degisim
lightmap'ten gelen yumusak gradyandir.

Bu yuzden baked GI'nin parlakligi da kademelendiriliyor (`ToonPosterizeGI`).
Renk tonu korunur, albedo'ya dokunulmaz — sadece isik duzlesir.

| Ayar | Anlami |
|---|---|
| `GI Posterize Amount` | 0 = ham lightmap, 1 = tam kademeli |
| `GI Steps` | Kac kademe (4 iyi bir baslangic) |
| `GI Band Smoothness` | Kademe kenarlarinin sertligi |

**Onemli:** buyuk duz mimari yuzeylerde bu deger **0 olmali**. Tavan, duvar ve
zemin gibi genis yuzeylerde lightmap'in yumusak isik havuzlari
kademelendirilince amip seklinde lekeler olusuyor — kamuflaj deseni gibi
duruyor. Su materyallerde bilerek 0 birakildi:

`M_Tavan`, `M_Duvar`, `M_Lambri`, `M_Zemin`, `M_Supurgelik`

Yeni bir duvar/tavan/zemin materyali eklersen onda da 0 yap.

### Ilk ayarlanacak iki dugme

Toon gorunumunun tamami bu ikisinde:

**`Ramp Threshold`** — kirilmanin nerede olacagi. Deger dogrudan `NdotL`
esigidir: 0 = geometrik terminator (kurenin yarisi aydinlik), 0.5 = cok
karanlik (sadece %25 aydinlik). Varsayilan 0.25 ≈ %37 aydinlik.

**`Shade Color`** — golgeli tarafin ne kadar koyu olacagi. Dogrudan isik
rengiyle carpilir, yani (0.35, ...) golgede direkt isigin %35'i kalir demektir.
Cok acik verirsen (0.6+) kirilma zar zor gorunur, toon etkisi kaybolur.

Golgeli taraf yine de tamamen kararmaz cunku baked GI ayrica ekleniyor.

---

## Neden ekran-uzayi (post-process) outline degil?

Yaygin toon egitimlerindeki yontem duz ekran oyunlari icin dogru, ama bu proje
**Quest standalone**. O yontem uc sey gerektiriyor ve ucu de burada pahali:

| Gereksinim | Quest'teki bedeli |
|---|---|
| Depth + Normals prepass | Tum sahne bir kez daha ciziliyor, stereo'da iki kat |
| Fullscreen blit | Tile-based GPU'da tum frame buffer'i RAM'e yaziyor |
| MSAA resolve | Projedeki 4x MSAA cozunuyor, VR'da kenarlar bozuluyor |

Bunun yerine **ters kabuk (inverted hull)** kullanildi: outline ayri bir pass
(`LightMode = SRPDefaultUnlit`), URP'nin opaque cizim listesinde zaten var.
Ekstra prepass yok, blit yok, MSAA bozulmuyor, single-pass instanced stereo ile
sorunsuz calisiyor.

Bedeli: outline'li her obje icin **+1 draw call**. Uzaktaki veya cok yogun
objelerde Inspector'dan `Draw Outline Pass` kapatilabilir.

---

## Optimizasyon notlari

Shader bu projenin gercek ayarlarina gore budandi:

- **Additional lights yok.** Sahnedeki 6 point isik `Baked`, directional isik
  `Mixed`, URP Performance profilinde ek isiklar zaten kapali.
- **Reflection probe / environment reflection yok.** Toon'da gereksiz.
- **Lightmap + shadowmask destekli.** Sahne `Directional` lightmap +
  `Shadowmask` ile bake edilmis. **Mevcut bake'ler gecerli, yeniden bake
  gerekmez** (albedo degismedi).
- **Meta pass var.** Ileride yeniden bake edilirse albedo ve emission dogru
  gider; bu pass olmasa GI kararirdi.
- **SRP Batcher uyumlu.** Tum materyal ozellikleri tek `UnityPerMaterial`
  blogunda.
- `half` hassasiyet; specular/rim/emission kapaliyken derlenmiyor bile.

---

## Bilinen sinirlar

- **Saydam yuzey desteklemiyor.** Cam ve pet sise URP/Lit'te kaldi.
- **Normal map desteklemiyor.** Toon gorunumde zaten istenmez.
- **Terrain'e uygulanmaz.** Terrain kendi shader'ini kullanir; onun icin ayri
  bir terrain toon shader'i gerekir.
- **Outline kalinligi ekranda sabit tutulur**, dunya uzayinda degil: kalinlik
  kameraya olan mesafeyle olceklenir (referans 2 m, sinir 0.1x - 4x). Sabit
  dunya kalinligi kullanilsaydi 20 cm'deki masa kenari sisman siyah bir bant,
  3 m'deki sandalye ise gorunmez bir cizgi olurdu.
- Cok ince/duz geometride (duvar saati kadrani gibi) ters kabuk kesikli bir
  halka olarak gorunebilir. Cozum: o materyalde `Draw Outline Pass` kapat ya da
  `Outline Width` dusur.
- Sert kenarli mesh'lerde kabuk kosede acilabilir. Once `Outline Width` dusur;
  yetmezse mesh'e yumusatilmis normal bake etmek gerekir.

---

## Gorsel birligi tamamlayan sey shader degil

Toon shader isin **shading** kismini birlestirir. Kalan fark su ucunde:

1. **Ortak palet** — herkes ayni 8-12 renkten secsin
2. **Duz doku** — photoreal / detayli albedo yok. Meshy'den gelen dokularin
   detayi toon'da da gurultu olarak kalir; `_BaseMap`'i bosaltip sadece
   `_BaseColor` kullanmak en hizli birlestirici.
3. **Ortak olcek** — assetler ayni birim mantigiyla modellenmeli
