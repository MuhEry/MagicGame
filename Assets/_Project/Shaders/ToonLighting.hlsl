#ifndef RECYCLEGAME_TOON_LIGHTING_INCLUDED
#define RECYCLEGAME_TOON_LIGHTING_INCLUDED

// ---------------------------------------------------------------------------
// RecycleGame ortak toon yardimci fonksiyonlari.
// Tum takim ayni ramp matematigini kullansin diye tek yerde toplandi.
// ---------------------------------------------------------------------------

// Kenari yumusatilmis (anti-aliased) kademe fonksiyonu.
// steps = 1  -> klasik tek kirilmali toon terminatoru
// steps = 2+ -> cok kademeli (cel) gecis
half ToonBand(half value, half steps, half smoothness)
{
    half scaled = value * steps;
    half index  = floor(scaled);
    half frac_  = scaled - index;
    half edge   = smoothstep(0.5h - smoothness, 0.5h + smoothness, frac_);
    return saturate((index + edge) / steps);
}

// Tek esikli yumusak kesim. Specular ve rim icin kullaniliyor.
half ToonStep(half value, half threshold, half smoothness)
{
    return smoothstep(threshold - smoothness, threshold + smoothness, value);
}

// _SpecularSize [0..1] -> dogrudan NdotH esigi. 0 = kucuk nokta, 1 = genis parlama.
//
// Bilerek pow() kullanilmiyor: klasik toon specular pow(NdotH, 2048) gibi cok
// buyuk usler ister; half hassasiyette (Quest'in tercih ettigi hassasiyet) bu
// sonucu sifira ya da cope goturur. NdotH'yi dogrudan esiklemek hem sayisal
// olarak stabil hem de bir pow komutu daha ucuz.
half ToonSpecularThreshold(half size)
{
    return lerp(0.995h, 0.55h, saturate(size));
}

// Baked GI'yi kademelendirir. Renk tonu korunur, sadece parlaklik basamaklanir.
//
// Neden gerekli: bu sahne tamamen bake edilmis bir ic mekan. Directional isik
// iceride shadowmask ile kapali oldugu icin butun yuzeyler ayni banda dusuyor
// ve toon kirilmasi hic olusmuyor; gorunen tum degisim lightmap'ten gelen
// yumusak gradyan. Aydinlatmanin kendisini kademelendirmezsek bu sahnede
// toon shader gorsel olarak hicbir sey yapmaz.
//
// Albedo'ya dokunulmaz: doku detayi korunur, sadece isik duzlesir.
half3 ToonPosterizeGI(half3 gi, half steps, half smoothness, half amount)
{
    half lum = max(Luminance(gi), 1e-4h);
    half banded = ToonBand(saturate(lum), steps, smoothness);
    half3 posterized = gi * (banded / lum);
    return lerp(gi, posterized, saturate(amount));
}

#endif // RECYCLEGAME_TOON_LIGHTING_INCLUDED
