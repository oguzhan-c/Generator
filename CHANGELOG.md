# Değişiklik geçmişi

Bu dosya [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) biçimindedir; sürümler
[Semantic Versioning](https://semver.org/lang/tr/)'e uyar. canfxgen (`CanFxGen`) ve proje şablonu
(`CanFx.Templates`) aynı sürümle yayınlanır; CanFx paketlerinin sürümünden bağımsızdır.

Yayın: `## [Unreleased]` altındakileri `## [x.y.z] - YYYY-AA-GG` başlığına taşı, `Directory.Build.props`'taki
`VersionPrefix`'i bir sonraki sürüme al, commit'le ve `vx.y.z` etiketini push et.

## [Unreleased]

### Eklendi

- canfxgen: `migration add <Ad> | remove | list | update`: `dotnet ef`'i Persistence (migration'lar) ve WebAPI (ayarlar)
  projeleriyle çalıştırır; önce `dotnet tool restore`. `add --startup-mode Migrate`: Development açılışını Migrate yapar.
- canfxgen: `database [postgres|sqlserver]`: veritabanını seçer; sağlayıcı satırı, docker-compose servisi ve volume'u,
  uygulama konteynerinin bağlantısı, Development bağlantı dizesi ve DevServices satırı yorumdan açılır, diğer sağlayıcı
  yorumlanır (`--preview`). Paket eklenmez; eksikse `dotnet add package` komutu verilir. Elle ayarlanmış başka bir
  sağlayıcıya (SQLite, MySQL ...) dokunulmaz. Rider: CanFx → **Database...**
- canfxgen / Rider Features: modüller (değişiklik geçmişi, ayarlar, raporlama) de sonradan eklenip çıkarılır. Tablo
  ekleyen / kaldıran özelliklerde (event bus + outbox ve bu modüller) yeni migration eklenmesi hatırlatılır.
- Rider: CanFx → **Migrations** → Add Migration / Update Database / Remove Last Migration.
- Şablon: `.config/dotnet-tools.json` ile `dotnet-ef` EF Core sürümüne sabitlendi (`dotnet tool restore`); migration
  komutu (Persistence + WebAPI) README'de ve `appsettings.Development.json`'da tam haliyle.

### Değişti

- Şablon: veritabanı sağlayıcısının paketleri (Npgsql, SqlServer, Pkcs) ve Hangfire depo paketleri sabit sürümle
  yorumda verilmez; kullanıcı güncel sürümü `dotnet add package` ile ekler (komutlar açıklamalarda ve README'de).

### Düzeltildi

- Şablon: seçilmeyen kimlik özelliklerinin (OTP, e-posta 2FA, passkey, dış hesapla giriş) kodu artık üretilmez:
  komutlar, servisler (TwoFactorCookie, TwoFactorCodeVerifier, RecoveryCodeIssuer ...), iş kuralları, hata kodları,
  DTO'lar ve controller'lardaki istek tipleri. Hesap güvenliği yanıtı (`/api/auth/account/security`) yalnızca açık
  özelliklerin alanlarını içerir. Doğrulamaya iki bileşim eklendi (yalnızca e-posta 2FA; yalnızca OTP + dış giriş).
- canfxgen: `new` (etkileşimli) özellik listesinde Postman eşitleme yoktu (Rider'ın yeni proje ekranında vardı).

## [0.6.0] - 2026-11-04

### Eklendi

- canfxgen: `mechanisms <Hedef>`: hedefteki mekanizmaların durumu (açık / kapalı / karışık; `--json`).
- canfxgen: `generate seeder --describe` (mevcut seed durumu), `--data <kayıtlar.json>` (başlangıç kayıtları dosyaya
  ya da HasData'ya yazılır), `--sample-kinds "Name=City"` (örnek değer türü), `--preview`.

### Değişti

- Rider: Mechanisms, Features ve Seed Data diyalogları projenin mevcut durumuyla açılır (Keep yerine On / Off /
  Mixed); yalnızca değiştirilen satırlar uygulanır. Generate CRUD son seçilen mekanizmaları hatırlar.
- Rider: Seed Data: başlangıç kayıtları diyalogdaki tabloda girilir (JSON ya da migration), var olanlar tabloda
  gelir; örnek veride kayıt sayısı ve alan başına değer türü (örnekli) seçilir. TODO'lu boş dosya kalmaz.
- canfxgen: `--has-data` artık zorunlu veriyi JSON + seeder yerine migration'a yazar (ikisi birden değil). Enum
  alanları JSON'da adıyla yazılır (`"tone": "Warm"`).
- Şablon: geliştirme servisleri (veritabanı, Redis, Elasticsearch, RabbitMQ, mailpit) varsayılan portlarıyla başlar;
  port doluysa (bilgisayardaki PostgreSQL, başka bir proje) sıradaki boş port kullanılır ve uygulama o porta bağlanır.
  `Program.cs`: `AddCanDevServices(config, builder.Environment)`. CanFx 0.4.0.

## [0.5.0] - 2026-11-03

### Değişti

- Şablon: `Development/DevServicesSetup.cs` kaldırıldı. Geliştirme servisleri yalnızca `appsettings.Development.json`'dan
  okunur: servis başına docker-compose adı (`Compose`) ve adresin okunduğu ayar (`AddressFrom`); `Program.cs`'te tek
  satır (`AddCanDevServices(config)`). Veritabanı satırı PostgreSQL / SQL Server için yorumda hazır.
- CanFx paketleri 0.3.0.

## [0.4.0] - 2026-11-03

### Düzeltildi

- Şablon: `--Passkey false` ile oluşturulan proje açılmıyordu (passkey komutları `IPasskeyService` istiyordu ama servis
  kayıtlı değildi). Passkey kapalıyken passkey komutları projeye girmez. Doğrulama artık uygulamayı açıp
  `/health/live`'ın 200 döndüğünü de denetler.

### Eklendi

- Rider eklentisi: sağ tık → **CanFx** alt menüsü (Generate CRUD, Add Seed Data, Mechanisms, Rate Limit, Features) ve
  Domain projesinde **Add → CanFx Entity** (yalnızca ad sorulur). Var olan dosyaları değiştiren eylemler önce satır
  farkı olarak gösterilir, onayla uygulanır.

- canfxgen: `generate entity <Ad> [--folder <klasör>] [--id Guid|int|long]`: Domain'de yeni entity; taban sınıf
  seçenekleri (Entity, AuditedEntity, FullAuditedEntity, AggregateRoot ...) ve her birinin eklediği alanlar dosyada
  yorum olarak durur, seçim sınıf satırında yapılır.
- canfxgen: `add` / `remove feature <ad>`: redis, search, event-bus, hangfire, file-storage, realtime, sms,
  open-telemetry ve postman sonradan eklenir / çıkarılır. Kurallar elle yazılmaz: şablon projenin seçenekleriyle özellik
  kapalı ve açık olarak üretilir, fark (paket, kayıt, appsettings, docker-compose, yeni dosyalar) projeye bağlamıyla
  yama olarak uygulanır; yerleştirilemeyen parça elle yapılmak üzere raporlanır. `--preview` ile önce görülebilir.
  Şema ya da kimlik akışını değiştiren seçenekler (çoklu kiracılık, OTP, passkey, ayarlar, raporlama ...) desteklenmez.
- Şablon: `canfx.json` projenin şablon seçeneklerini tutar (canfxgen okur; eski projelerde dosyalardan çıkarılır).
- canfxgen: `rate-limit <politika> <uç>`: uca (istek, `Controller.Action`, controller ya da özellik)
  `[EnableRateLimiting("ad")]` bağlar; politika appsettings'te yoksa `RateLimiting:Policies` altına eklenir
  (`--permit`, `--window`, `--partition-by`). Hazır politikalar `auth` ve `sensitive`; `--remove` bağı kaldırır.
- canfxgen: `enable` / `disable <mekanizma...> <hedef>`: önbellek, log, transaction, yetki ve dinamik sorgu sonradan
  açılıp kapatılır. Hedef bir istek (`CreateBrandCommand`), özellik (`Brands`) ya da entity (`Brand`, özelliğin tüm
  istekleri). İstek sınıfına arayüz ve üyeleri (Permissions, CacheKey/CacheTags ...) eklenir ya da çıkarılır; dosyanın
  geri kalanına dokunulmaz. `--preview` değişiklikleri satır farkı olarak gösterir, dosyalara yazmaz (`--json` ile IDE için).
- canfxgen: `generate seeder <Entity>`: `{Entity}Seeder` (her ortamda; `Seeding/Data/{çoğul}.json`'dan, doğal anahtarla
  eşleşmeyen eksik kayıtlar eklenir), `{Entity}SampleSeeder` (yalnızca geliştirmede, `SampleData` ile alan adına ve
  tipine göre örnek değer; ilişkili kayıtlar var olanlardan seçilir), ilişkilere göre sıra önerisi; `--has-data` ile
  EF Core `HasData` (migration'a yazılır), `--required-only`, `--sample-only`, `--count`.
- canfxgen: yanıtlarda eklenmeyen denetim alanları yorum olarak (`// public string? CreatedBy ...`) ve soft delete
  açıklaması; `Created...Response`'ta `UpdatedAt` yok.
- Şablon: veritabanını şablon seçmez; PostgreSQL ve SQL Server için her şey yorum satırı olarak hazır gelir
  (`Directory.Packages.props` ve Persistence `.csproj`'ta paket, `PersistenceServiceRegistration.cs` → "Veritabanı"
  bölümünde sağlayıcı satırı, `docker-compose.yml`'de servis ve volume, `appsettings.Development.json`'da yerel bağlantı
  dizesi). Sağlayıcı seçilmeden açılan uygulama ne yapılacağını söyleyen bir hatayla durur.
- Şablon: `appsettings.Development.json` seçilen özelliklere göre hazır gelir: `DevServices` (servis başına mod),
  `Postman` ve seçilen Redis / Elasticsearch / RabbitMQ için docker-compose adresleri (açılışta docker'da başlatılır).
  `canfxgen add/remove feature` bu dosyayı da günceller. `DevServices` ve `Postman` artık `appsettings.json`'da yok.
- Şablon: `Database:StartupMode` (`Migrate` / `Recreate` / `None`); Development'ta `Recreate` ve örnek veri seed'i.
- Şablon: geliştirme servisleri (CanFx.DevServices): veritabanı vb. bilgisayarda çalışmıyorsa docker-compose'dan başlatılır.
- Şablon: uygulama `Dockerfile`'ı ve `docker compose --profile app` servisi.
- Şablon: Postman eşitleme (`--Postman`, varsayılan açık; anahtar user-secrets'ta).
- canfxgen: `add/remove feature`'da Rider'ın biçimlendirdiği satırlar (boşluk farkı) zaten var sayılır; aynı
  `PackageReference` iki kez eklenmez (NU1008).

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
