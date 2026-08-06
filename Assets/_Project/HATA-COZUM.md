# Gece Vardiyası - Hata ve Çözüm Günlüğü

* **Kullanılan XRI Sürümü:** com.unity.xr.interaction.toolkit@3.4.1 (XRI 3.x)

## Karşılaşılan Hatalar ve Çözümleri
1. `XRBaseControllerInteractor` sınıfının XRI 3.0+ sürümünde kullanılmıyor (deprecated) olması uyarısı alındı -> Çözüm: Kod içinde `XRBaseInputInteractor` sınıfı kullanılarak güncellendi.
2. `ItemCategory` ve `DecisionResult` dosyalarının default olarak `Core/` içinde tanımlanmış olması ve repository diagramında `Shared/` altında istenmesi -> Çözüm: Kodlar `Shared/` klasörüne taşındı ve duplicate kod hatası olmaması için `Core/` altındakiler silinerek `Assembly-CSharp.csproj` güncellendi.
