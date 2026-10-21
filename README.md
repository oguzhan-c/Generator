# CanFx Generator

[CanFx](https://github.com/oguzhan-c/Core) için geliştirici araçları:

| Parça | Paket / yer | Ne yapar |
|---|---|---|
| Proje şablonu | `CanFx.Templates` (`dotnet new canfx-monolith`) | CanFx paketleriyle katmanlı (DDD / Clean Architecture) .NET 10 çözümü |
| can-gen | `CanFx.Generator.Cli` (dotnet tool) | Entity'den CRUD (CQRS) kodu, özelliğe komut / sorgu |
| Rider eklentisi | `rider-plugin/` | Entity'ye sağ tık → CanFx: Generate CRUD (can-gen'i çağırır) |

CanFx paketleri bu repoda değil; şablonla oluşturulan projeler onları nuget.org'dan alır.

## Kurulum

```bash
dotnet new install CanFx.Templates
dotnet tool install -g CanFx.Generator.Cli

can-gen new MyShop                                   # ya da: dotnet new canfx-monolith -n MyShop
cd MyShop && can-gen generate crud Brand
```

## Kullanım

```bash
can-gen new MyShop                                   # şablonla çözüm (veritabanı ve özellikler sorulur)
can-gen generate crud                                # entity ve mekanizmalar sorulur
can-gen generate crud Brand -c -l -t -s -d           # soru sormadan
can-gen generate command SyncBrand Brands -e Put     # özelliğe komut
can-gen generate query GetBrandByName Brands         # özelliğe sorgu
can-gen generate dynamic-query Brand                 # CRUD'a sonradan dinamik sorgu
can-gen entities | features                          # listeler
can-gen --help | can-gen generate --help
```

Mekanizmalar (nArchitecture'daki gibi): `-c` caching, `-l` logging, `-t` transaction, `-s` secured, `-d` dynamic query.
Hiçbiri verilmezse sorulur; etkileşimsiz çalışmada (betik, `--json`) hepsi açıktır.

Diğer: `--project <klasör>`, `--plural <Çoğul>`, `-e/--endpoint-method`, `--force`, `--dry-run`, `--json` (IDE eklentileri).

## Repo düzeni

```
src/CanFx.Generator          can-gen'in motoru (Roslyn ile entity okuma, dosya üretimi, işaretli yerlere ekleme)
src/CanFx.Generator.Cli      can-gen komut satırı aracı (dotnet tool)
tests/CanFx.Generator.Tests  üretici testleri
templates/CanFx.Templates    proje şablonu (content/monolith)
templates/verify-templates.sh  şablondan üç çözüm üretir, can-gen ile CRUD ekler, derler, standart denetimi yapar
rider-plugin                 Rider eklentisi (Kotlin): derleme ve kurulum rider-plugin/README.md
build/                       ci.sh, pack-local.sh, pack-templates.sh
```

## Geliştirme

```bash
dotnet build CanFx.Generator.slnx
dotnet test --solution CanFx.Generator.slnx
build/pack-local.sh --install          # can-gen + şablon yeni yerel sürümle paketlenir ve kurulur
templates/verify-templates.sh          # tam doğrulama → templates.log ("HEPSİ TAMAM")
```

## Sürümler

- can-gen ve şablon aynı sürümle yayınlanır: `Directory.Build.props` → `VersionPrefix`. CanFx paketlerinden bağımsızdır.
- Şablonla oluşturulan projenin kullandığı CanFx sürümü: `Directory.Build.props` → `CanFxVersion`. Yeni CanFx sürümü
  çıkınca bu değer güncellenir ve şablon yeniden yayınlanır.
- Yayınlanmamış Core paketleriyle denemek: Core'da `build/pack-local.sh`, sonra burada
  `CANFX_VERSION=$(cat ../Core/artifacts/local-version.txt) CANFX_FEED=$PWD/../Core/artifacts/packages templates/verify-templates.sh`.
- Yayın: `CHANGELOG.md`'de `## [x.y.z] - YYYY-AA-GG` bölümü, sonra `vx.y.z` etiketi. CI (GitHub Actions) derler, test
  eder, şablonu doğrular ve nuget.org'a gönderir (Trusted Publishing).

Lisans: MIT.
