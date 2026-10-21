# Değişiklik geçmişi

Bu dosya [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) biçimindedir; sürümler
[Semantic Versioning](https://semver.org/lang/tr/)'e uyar. can-gen (`CanFx.Generator.Cli`) ve proje şablonu
(`CanFx.Templates`) aynı sürümle yayınlanır; CanFx paketlerinin sürümünden bağımsızdır.

Yayın: `## [Unreleased]` altındakileri `## [x.y.z] - YYYY-AA-GG` başlığına taşı, `Directory.Build.props`'taki
`VersionPrefix`'i bir sonraki sürüme al, commit'le ve `vx.y.z` etiketini push et.

## [Unreleased]

### Değişti

- Geliştirici araçları CanFx (Core) reposundan bu repoya taşındı (geçmişiyle). Şablon CanFx paketlerini nuget.org'dan
  kullanır; sürümü `Directory.Build.props` → `CanFxVersion` (şablon kendi sürümüyle yayınlanır).

## [0.1.0] - 2026-10-21

İlk sürüm, CanFx 0.1.0 ile birlikte Core reposundan yayınlandı: `canfx-monolith` şablonu, can-gen (CRUD, komut,
sorgu, dinamik sorgu üretimi), Rider eklentisi.
