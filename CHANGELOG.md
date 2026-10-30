# Değişiklik geçmişi

Bu dosya [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) biçimindedir; sürümler
[Semantic Versioning](https://semver.org/lang/tr/)'e uyar. canfxgen (`CanFxGen`) ve proje şablonu
(`CanFx.Templates`) aynı sürümle yayınlanır; CanFx paketlerinin sürümünden bağımsızdır.

Yayın: `## [Unreleased]` altındakileri `## [x.y.z] - YYYY-AA-GG` başlığına taşı, `Directory.Build.props`'taki
`VersionPrefix`'i bir sonraki sürüme al, commit'le ve `vx.y.z` etiketini push et.

## [Unreleased]

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
