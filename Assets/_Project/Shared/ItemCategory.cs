/// <summary>
/// Esyanin ait oldugu dolap kategorisi.
///
/// SARTNAME NOTU: Bu enum "ilk 20 dakikada tek dosyada, birlikte" yazilip
/// SONRA KIMSE DEGISTIRMEZ kuralina tabidir. Icerik sartnamedeki Gelistirici C
/// blogundan birebir alinmistir:  public enum ItemCategory { Sesli, Parlak, Agir }
///
/// Ekip kendi surumunu push ederse bu dosya CAKISIR -> ekibin surumu alinir.
/// Yeni deger EKLENMEZ, sira DEGISTIRILMEZ (ScriptableObject'lerde int olarak saklanir).
/// </summary>
public enum ItemCategory
{
    /// <summary>Icinde bir sey tikirdiyor - esya sallanirsa ses cikar.</summary>
    Sesli = 0,

    /// <summary>Buyulu isik yayiyor - ~35 cm altinda parlamaya baslar.</summary>
    Parlak = 1,

    /// <summary>Gorunenden cok agir - elde tutulurken kumanda surekli titrer.</summary>
    Agir = 2,
}
