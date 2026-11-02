# Değişiklik geçmişi

Bu dosya [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) biçimindedir; sürümler
[Semantic Versioning](https://semver.org/lang/tr/)'e uyar. canfxgen (`CanFxGen`) ve proje şablonu
(`CanFx.Templates`) aynı sürümle yayınlanır; CanFx paketlerinin sürümünden bağımsızdır.

Yayın: `## [Unreleased]` altındakileri `## [x.y.z] - YYYY-AA-GG` başlığına taşı, `Directory.Build.props`'taki
`VersionPrefix`'i bir sonraki sürüme al, commit'le ve `vx.y.z` etiketini push et.

## [Unreleased]

### Düzeltildi

- Şablon: `--Passkey false` ile oluşturulan proje açılmıyordu (passkey komutları `IPasskeyService` istiyordu ama servis
  kayıtlı değildi). Passkey kapalıyken passkey komutları projeye girmez. Doğrulama artık uygulamayı açıp
  `/health/live`'ın 200 döndüğünü de denetler.

### Eklendi

- canfxgen: `generate entity <Ad> [--folder <klasör>] [--id Guid|int|long]`: Domain'de yeni entity; taban sınıf
  seçenekleri (Entity, AuditedEntity, FullAuditedEntity, AggregateRoot ...) ve her birinin eklediği alanlar dosyada
  yorum olarak durur, seçim sınıf satırında yapılır.
- canfxgen: yanıtlarda eklenmeyen denetim alanları yorum olarak (`// public string? CreatedBy ...`) ve soft delete
  açıklaması; `Created...Response`'ta `UpdatedAt` yok.
- Şablon: veritabanı seçimi (`--Database postgresql | sqlserver | sqlite`); paket, sağlayıcı satırı, yerel bağlantı
  dizesi ve docker-compose servisi seçime göre hazır gelir.
- Şablon: `Database:StartupMode` (`Migrate` / `Recreate` / `None`); Development'ta `Recreate` ve örnek veri seed'i.
- Şablon: geliştirme servisleri (CanFx.DevServices): veritabanı vb. bilgisayarda çalışmıyorsa docker-compose'dan başlatılır.
- Şablon: uygulama `Dockerfile`'ı ve `docker compose --profile app` servisi.
- Şablon: Postman eşitleme (`--Postman`, varsayılan açık; anahtar user-secrets'ta).

### Değişti

- Şablon: `AppUser` yalnızca alan taşır (taban sınıf seçenekleri yorumda); kayıt, profil ve rol işlemleri
  `Application/Services/Users/AppUserService.cs`'te.
- Dosya başına tek tip: şablondaki ve canfxgen'in ürettiği handler'lar `{İstek}Handler.cs`'te; diğer çok tipli
  dosyalar da bölündü. Doğrulama bu kuralı denetler.
- Metinler kodda değil, özellik başına gömülü çeviri dosyalarında (`Features/{Özellik}/Localization/{özellik}.{dil}.json`);
  hata kodları `özellik.anahtar` (ör. `auth.invalid_credentials`, `roles.role_not_exists`). **Kırıcı:** istemciler eski
  kodları (`invalid_credentials`, `role.not_exists` ...) kullanıyorsa güncellenmeli. Alan adları da çevrilir
  (`Application/Localization/fields.{dil}.json`). canfxgen yeni entity'nin kodlarını ve çeviri dosyalarını üretir;
  doğrulama her kodun her dilde metni olduğunu denetler.
- appsettings: her bölüm eksiksiz ve açıklamalı (tüm ayarlar, varsayılanları, geçerli değerleri, gizli olanlar);
  seçilmeyen özelliğin bölümü dosyaya girmez; `appsettings.Development.json` yalnızca farklı değerleri tutar.
- Geçersiz ayar açılışta durdurur: bölümlerde yanlış yazılmış anahtar (ör. `"PermitLimt"`), bilinmeyen
  `Mail:Provider` / `Storage:Provider`, hatalı değer tipi.
- CanFx paketleri 0.2.1.

## [0.3.3] - 2026-11-02

### Düzeltildi

- Tire içeren proje adında (`CanFx.Shop-Api`) çözüm derlenmiyordu: klasörler adı olduğu gibi, proje referansları ve
  namespace'ler tiresi `_` yapılmış haliyle kullanıyordu. Artık ad her yerde tek biçimde: kodda, proje ve klasör
  adlarında `CanFx.Shop_Api`; Docker, veritabanı, bucket ve e-posta adlarında `canfx-shop-api`. Doğrulamaya tireli
  adla bir çözüm eklendi.

## [0.3.2] - 2026-10-30

### Değişti

- "CanFx sürümü" ve "CanFx paket kaynağı" artık şablonun seçeneği değil: Rider, Visual Studio'ya özel `ide.host.json`'u
  okumadığı için 0.3.1'deki gizleme Rider'da işe yaramıyordu. CanFx sürümü şablon paketlenirken yazılır; paketler her
  zaman nuget.org'dan gelir. `canfxgen new --feed` kaldırıldı. Yayınlanmamış Core paketleriyle deneme yalnızca
  `templates/verify-templates.sh` ile (`CANFX_VERSION`, `CANFX_FEED`).

## [0.3.1] - 2026-10-30

### Değişti

- Şablonun Rider / Visual Studio "New Solution" ekranında ikonu var; "CanFx paket kaynağı" ve "CanFx sürümü" alanları
  gizlendi (paketler nuget.org'dan, sürüm şablonla birlikte gelir). Komut satırında `--CanFxFeed` / `--CanFxVersion`
  yerel paketlerle denemek için duruyor.

## [0.3.0] - 2026-10-28

### Değişti (kırıcı)

- Aracın NuGet paketi `CanFxGen` → **`CanFx.Generator`** (CanFx paket ailesinin parçası olarak görünsün):
  `dotnet tool install -g CanFx.Generator`. Komut aynı: `canfxgen`. Kod üreten motor `CanFx.Generator.Engine`
  (ayrı paket olarak yayınlanmaz). Rider eklentisinin adı "CanFx Generator".

## [0.2.0] - 2026-10-21

### Değişti (kırıcı)

- Araç yeniden adlandırıldı: paket `CanFx.Generator.Cli` → **`CanFxGen`**, komut `can-gen` → **`canfxgen`**
  (`dotnet tool install -g CanFxGen`). Koddaki işaretler `TODO(canfxgen)` ve `// canfxgen:...` oldu; 0.1.0 şablonuyla
  oluşturulmuş çözümlerdeki eski `// can-gen:...` işaretleri de tanınır.
- `--project`, `--force` ve `--dry-run` seçenekleri kaldırıldı: komutlar bulunulan çözüm klasöründe çalışır, var olan
  dosyaların üzerine yazılmaz (Rider eklentisinden de kaldırıldı).

### Değişti

- Geliştirici araçları CanFx (Core) reposundan bu repoya taşındı (geçmişiyle). Şablon CanFx paketlerini nuget.org'dan
  kullanır; sürümü `Directory.Build.props` → `CanFxVersion` (şablon kendi sürümüyle yayınlanır).

### Eklendi

- Rider eklentisi JetBrains Marketplace'te: İngilizce arayüz, ikon, açıklama; sürümü Generator ile aynı, `v*`
  etiketinde CI ile yayınlanır (isteğe bağlı imza).

## [0.1.0] - 2026-10-21

İlk sürüm, CanFx 0.1.0 ile birlikte Core reposundan yayınlandı: `canfx-monolith` şablonu, can-gen (CRUD, komut,
sorgu, dinamik sorgu üretimi), Rider eklentisi.
