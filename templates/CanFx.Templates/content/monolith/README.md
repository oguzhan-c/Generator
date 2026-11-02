# CanAppTemplate

CanFx paketleriyle nArchitecture düzeninde kurulmuş monolith (Clean Architecture + CQRS) uygulama.

```
src/
  CanAppTemplate.Domain           Entities/, Constants/
  CanAppTemplate.Application      Features/{Çoğul}/Commands|Queries|Constants|Profiles|Rules
                                  Services/Repositories (I{Entity}Repository), Services/AuthService ...
                                  ApplicationServiceRegistration.cs
  CanAppTemplate.Persistence      Contexts/BaseDbContext, EntityConfigurations/, Repositories/, Seeding/
                                  PersistenceServiceRegistration.cs
  CanAppTemplate.Infrastructure   dış bağlantılar, her biri kendi klasöründe:
                                  Mailing/, Sms/, FileStorage/, Search/, Messaging/ (event bus), BackgroundJobs/ (Hangfire),
                                  Caching/ (Redis), Realtime/ (SignalR) → {Ad}ServiceRegistration.cs
                                  InfrastructureServiceRegistration.cs (yalnızca hepsini çağırır)
  CanAppTemplate.WebAPI           Controllers/, kimlik akışı (JWT HttpOnly cookie'de), Program.cs
```

Hatalar exception olarak fırlar (`BusinessException` → 400, `NotFoundException` → 404, `ValidationException` → 400 ...)
ve ProblemDetails yanıtına çevrilir.

## İlk çalıştırma

1. CanFx paketleri nuget.org'dan gelir; ek ayar gerekmez (yerel / özel kaynak için `nuget.config`'teki nota bak).
2. Veritabanı hazır: sağlayıcı oluştururken seçildi (`--Database postgresql | sqlserver | sqlite`), yerel bağlantı
   dizesi `appsettings.Development.json`'da. Uygulama Development'ta açılırken veritabanı bilgisayarında çalışıyorsa onu
   kullanır, çalışmıyorsa `docker-compose.yml`'den başlatır (Docker Desktop açık olmalı; SQLite'ta docker gerekmez).
   Ayar: `appsettings.Development.json` → `DevServices` (servis başına `Auto` / `Docker` / `Local` / `Off`).
3. Gizli değerler (appsettings'e YAZMA):

   ```bash
   cd src/CanAppTemplate.WebAPI
   dotnet user-secrets set 'Security:Jwt:SigningKey' '<en az 32 karakter>'
   dotnet user-secrets set 'Security:VerificationCodeKey' '<en az 32 karakter, JWT anahtarından farklı>'
   dotnet user-secrets set 'Seed:AdminPassword' '<yönetici şifresi>'
   ```

   zsh'te `!` içeren değerleri tek tırnakla yaz.
4. `dotnet run --project src/CanAppTemplate.WebAPI` → http://localhost:5280/docs

## Entity ekleme (canfxgen)

Entity'yi `CanAppTemplate.Domain/Entities` içinde yaz, ardından Rider'da entity dosyasına sağ tıkla → **CanFx → Generate
CRUD** (ya da terminalde `canfxgen generate crud Brand`; komut/sorgu eklemek için `canfxgen generate command|query`). Üretilen dosyalar:

- `Application/Features/Brands/Commands/{Create,Update,Delete}/` — komut, handler (`...CommandHandler.cs`), validator, `Created/Updated/DeletedBrandResponse`
- `Application/Features/Brands/Queries/{GetById,GetList,GetListByDynamic}/` — sorgu, handler (`...QueryHandler.cs`), `GetByIdBrandResponse`, `GetListBrandListItemDto`
- `Application/Features/Brands/{Constants,Profiles,Rules}/` — `BrandsMessages`, `BrandsOperationClaims`, `MappingProfiles`, `BrandBusinessRules`
- `Application/Services/Repositories/IBrandRepository.cs`, `Persistence/Repositories/BrandRepository.cs`
- `Persistence/EntityConfigurations/BrandConfiguration.cs`
- `WebAPI/Controllers/BrandsController.cs` (`/api/v1/Brands`)

Generator `// canfxgen:...` işaretlerinin olduğu yerlere ekleme yapar; bu satırları silme. Senin doldurman gereken yerler
`TODO(canfxgen)` ile işaretlidir (Rider: View → Tool Windows → TODO).

## Mekanizmaları sonradan açıp kapatma

```bash
canfxgen enable caching Brand --preview          # önce neyin değişeceğini gör
canfxgen enable caching Brand                    # Brands özelliğinin tüm sorguları önbellekte
canfxgen disable secured logging CreateBrandCommand
canfxgen disable dynamic-query Brand             # GetListByDynamic dosyaları, eşlemesi ve uçları kaldırılır
```

Mekanizmalar: `caching`, `logging`, `transaction`, `secured`, `dynamic-query`. Yalnızca istek sınıfının arayüzleri ve
ilgili üyeleri değişir; handler ve kendi eklediklerin olduğu gibi kalır (transaction kapatılırken handler kaydetmiyorsa uyarılır).

## Hız sınırı

Genel sınır (Global), giriş/kayıt (`auth`) ve hassas işlemler (`sensitive`) hazır; uygulamanın kendi sınırları
appsettings'te `RateLimiting:Policies` altında. Bir uca bağlamak:

```bash
canfxgen rate-limit reports GetSalesReportQuery --permit 20 --window 00:01:00 --partition-by Tenant
canfxgen rate-limit sensitive OrdersController.Cancel
canfxgen rate-limit reports GetSalesReportQuery --remove
```

Uca `[EnableRateLimiting("reports")]` eklenir; politika appsettings'te yoksa yazılır. Tanımsız politika kullanan uç
uygulamayı açılışta durdurur.

## Başlangıç verisi (seed)

```bash
canfxgen generate seeder Brand          # zorunlu veri (JSON) + geliştirme örnek verisi
canfxgen generate seeder Currency --required-only --has-data
```

- `Persistence/Seeding/Data/brands.json`: her ortamda gereken kayıtlar (para birimleri, durumlar ...). `BrandSeeder`
  her açılışta eksik olanları ekler (eşleşme `Code` / `Name` gibi doğal anahtarla), var olanlara dokunmaz.
- `BrandSampleSeeder`: yalnızca Development'ta, ekranlar boş görünmesin diye `SampleData` ile örnek kayıtlar.
- Sıra (`Order`): ilişkili entity'nin seeder'ından sonra (ör. Product, Category'den sonra); canfxgen önerir.
- `--has-data`: EF Core `HasData`; kayıtlar migration'a yazılır, Id'ler sabit olmalı. Nadiren değişen sabit listeler için.

## Metinler ve diller

Hata mesajları kodda değil, özellik başına çeviri dosyalarında; dosyalar Application DLL'ine gömülür:

```
Application/Features/Brands/Constants/BrandsBusinessMessages.cs   BrandNotExists = "brands.brand_not_exists"  (yalnızca kod)
Application/Features/Brands/Localization/brands.tr.json           { "brands": { "brand_not_exists": "Brand kaydı bulunamadı." } }
Application/Features/Brands/Localization/brands.en.json           { "brands": { "brand_not_exists": "Brand not found." } }
Application/Localization/fields.{dil}.json                        alan adları (doğrulama mesajlarında): { "fields": { "Name": "Ad" } }
```

- Kod (`özellik.anahtar`) istemciyle sözleşmedir: yanıtta `"code"` alanı; metin isteğin diline göre `"detail"`'da.
- Yer tutucu: `"{name} adında bir marka var."` + `throw new BusinessException(...) { Code = ... }.With("name", name)`.
- canfxgen her entity için kodları ve iki dilin dosyalarını üretir. Development'ta bir dilde eksik kalan metinler açılışta loglanır.
- Yeni dil: dosyaların `.{dil}.json` kopyası + `Program.cs`'te `SupportedCultures`.
- Kod değiştirmeden metni ezmek için WebAPI/Localization/`{dil}.json` (ör. müşteriye özel metinler).

## Kimlik anahtar tipi

Kullanıcı, rol, yetki, oturum, passkey gibi kimlik tablolarının Id tipi tek yerde seçilir: kökteki `Directory.Build.props`

```xml
<Using Include="System.Guid" Alias="IdentityKey" />   <!-- System.Int32 | System.Int64 -->
```

Kod her yerde `IdentityKey` kullanır (`Role<IdentityKey>`, `IRepository<AppUser, IdentityKey>` ...); yeni kayıt anahtarı
`IdentityKeys.New()` ile alınır (Guid: kodda v7, int / long: veritabanı üretir). Tipi değiştirince başka dosyaya dokunma,
yalnızca yeni bir migration ekle (var olan veritabanında veri taşıması gerekir). Uygulama entity'lerinin (ör. `Color : Entity<int>`)
anahtarı bundan bağımsızdır, her entity kendi tipini seçer.

## Roller ve yetkiler

- **Yetkiler kodla tanımlanır:** her özellik `Features/{Çoğul}/Constants/{Çoğul}OperationClaims.cs`, toplu liste
  `Application/Services/OperationClaims/AppOperationClaims.cs`. Açılışta veritabanı bu listeye eşitlenir; koddan çıkan
  yetki silinmez, "kullanımdan kalktı" olur ve kimseye etki etmez.
- **Dağıtım çalışırken yapılır** (`/api/identity/...`, yetkiler `IdentityPermissions`):
  - `GET/POST/PUT/DELETE /api/identity/roles` — rol listesi, oluştur, düzenle, sil
  - `POST /api/identity/roles/{id}/permissions/grant|revoke` — role yetki ver / al (`{ "operationClaimIds": [...] }`)
  - `GET /api/identity/operation-claims` — yetki kataloğu
  - `GET /api/identity/users/{userId}/authorization` — kullanıcının rolleri ve yetkileri
  - `POST|DELETE /api/identity/users/{userId}/roles/{roleId}` — rol ata / kaldır
  - `POST /api/identity/users/{userId}/permissions/grant|revoke` — doğrudan yetki ver / al
- **Yetki yükseltme yok** (Core: `IAccessGrantGuard`): kimse sahip olmadığı yetkiyi veremez, kapsamadığı rolü atayamaz,
  kendi rollerini/yetkilerini değiştiremez; Admin ve User sistem rolüdür (silinmez, adı değişmez); son yönetici kaldırılamaz.
  "Yetki verme yetkisi" de bir yetkidir: Admin, örneğin `identity.users.assign-role`'ü bir "Moderatör" rolüne verebilir;
  Moderatör yalnızca kendi kapsadığı rolleri dağıtabilir.
- **Değişiklik hemen geçerli:** roller ve yetkiler token'a yazılmaz; her istekte (1 dk önbellekli) veritabanından okunur
  (`UseCanPermissionResolution`). Birden fazla sunucuda değişiklik en geç önbellek süresi sonunda yansır.

## Migration

```bash
dotnet ef migrations add Initial --project src/CanAppTemplate.Persistence --startup-project src/CanAppTemplate.WebAPI
```

Açılışta veritabanına ne yapılacağı `Database:StartupMode` ile belirlenir:

| Mod | Ne yapar | Nerede |
| --- | --- | --- |
| `Recreate` | Siler, güncel entity'lerden yeniden oluşturur, seed çalışır | Development varsayılanı (entity tasarlarken). Yalnızca Development'ta ve yerel veritabanında çalışır |
| `Migrate` | Bekleyen migration'ları uygular; migration yoksa şemayı oluşturur. Veri korunur | Diğer ortamların varsayılanı |
| `None` | Dokunmaz | Migration'lar ayrı bir adımda (CI) uygulanıyorsa |

İlk migration'ı ekledikten sonra Development'ı da `Migrate` yap; `Recreate` açıkken migration varsa açılışta uyarı yazılır.

## Docker

```bash
docker compose up -d                          # yalnızca veritabanı (uygulama bunu kendisi de yapar)
docker compose --profile app up -d --build    # uygulama da konteynerde → http://localhost:8080/docs
```

Konteynerdeki uygulamanın gizli ayarları kök klasördeki `.env` dosyasından okunur (git'e girmez):
`Security__Jwt__SigningKey=...`, `Security__VerificationCodeKey=...`.

## Postman

Proje `--Postman true` (varsayılan) ile oluşturulduysa, Development'ta her açılışta API Postman'deki AYNI koleksiyona
aktarılır (API değişmediyse istek atılmaz) ve `baseUrl`'li bir ortam hazırlanır. Anahtar olmadan hiçbir şey yapılmaz:

```bash
cd src/CanAppTemplate.WebAPI
dotnet user-secrets set 'Postman:ApiKey' 'PMAK-...'            # Postman → Settings → API keys
dotnet user-secrets set 'Postman:WorkspaceId' '<id>'           # isteğe bağlı
```
