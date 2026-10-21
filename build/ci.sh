#!/usr/bin/env bash
# CI betiği (GitHub Actions bunu çağırır; yerelde de aynı şekilde çalışır):
#   ./build/ci.sh            restore + build + test + pack
#   ./build/ci.sh restore | build | test | pack | templates | publish | version
#     templates  şablonu doğrular (templates/verify-templates.sh: üç çözüm + canfxgen, nuget.org'daki CanFx ile)
#     publish    etiketli derlemede (v0.2.0) paketleri yayınlar; etiket yoksa hiçbir şey yapmaz
# Ortam değişkenleri: CONFIGURATION (Release), ARTIFACTS (artifacts), NUGET_API_KEY (yalnızca publish),
#   NUGET_SOURCE (varsayılan nuget.org)
set -euo pipefail

cd "$(dirname "$0")/.."
CONFIGURATION="${CONFIGURATION:-Release}"
ARTIFACTS="${ARTIFACTS:-artifacts}"
export CI=true
export DOTNET_NOLOGO=true
export DOTNET_CLI_TELEMETRY_OPTOUT=true

log() { printf '\n==> %s\n' "$*"; }

release_tag() {
  local tag=""
  if [[ "${GITHUB_REF_TYPE:-}" == tag ]]; then tag="$GITHUB_REF_NAME"; fi
  if [[ -z "$tag" ]]; then tag="$(git describe --tags --exact-match 2>/dev/null || true)"; fi
  printf '%s' "$tag"
}

# v0.2.0 / v0.2.0-rc.1 etiketi → o sürüm (yayınlanır); yoksa VersionPrefix-ci.<derleme no> (yayınlanmaz).
compute_version() {
  local tag prefix
  tag="$(release_tag)"
  if [[ "$tag" =~ ^v([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$ ]]; then
    VERSION="${BASH_REMATCH[1]}"; IS_RELEASE=true; return
  fi
  prefix="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props | head -n 1)"
  if [[ -n "${GITHUB_RUN_NUMBER:-}" ]]; then VERSION="$prefix-ci.$GITHUB_RUN_NUMBER"; else VERSION="$prefix-local"; fi
  IS_RELEASE=false
}
compute_version

restore() { log "restore"; dotnet restore CanFx.Generator.slnx; }

build() {
  log "build ($CONFIGURATION, sürüm $VERSION)"
  dotnet build CanFx.Generator.slnx -c "$CONFIGURATION" --no-restore -p:Version="$VERSION"
}

run_tests() {
  log "test ($CONFIGURATION)"
  rm -rf "$ARTIFACTS/test-results"
  dotnet test --solution CanFx.Generator.slnx -c "$CONFIGURATION" --no-build \
    --results-directory "$ARTIFACTS/test-results" --report-xunit-junit
}

pack() {
  log "pack (sürüm $VERSION)"
  rm -rf "$ARTIFACTS/packages"
  dotnet pack CanFx.Generator.slnx -c "$CONFIGURATION" --no-build -p:Version="$VERSION" -o "$ARTIFACTS/packages"
  ./build/pack-templates.sh "$VERSION" "$ARTIFACTS/packages"
  printf '    %s paket\n' "$(find "$ARTIFACTS/packages" -name '*.nupkg' | wc -l | tr -d ' ')"
}

templates() {
  log "şablon doğrulama"
  if ! ./templates/verify-templates.sh; then
    tail -n 80 templates.log >&2
    exit 1
  fi
  grep -q "^=== HEPSİ TAMAM" templates.log || { tail -n 80 templates.log >&2; exit 1; }
}

publish() {
  if [[ "$IS_RELEASE" != true ]]; then
    log "publish atlandı (etiketli derleme değil; sürüm $VERSION)"
    return
  fi
  log "publish $VERSION"
  if ! grep -qE "^## \[$VERSION\]" CHANGELOG.md; then
    echo "    CHANGELOG.md'de '## [$VERSION]' bölümü yok: önce değişiklikleri yaz." >&2
    exit 1
  fi
  if [[ -z "${NUGET_API_KEY:-}" ]]; then
    echo "    NUGET_API_KEY yok (nuget.org Trusted Publishing politikası ya da secret)." >&2
    exit 1
  fi
  dotnet nuget push "$ARTIFACTS/packages/*.nupkg" \
    --api-key "$NUGET_API_KEY" \
    --source "${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}" \
    --skip-duplicate
}

case "${1:-all}" in
  restore) restore ;;
  build) build ;;
  test) run_tests ;;
  pack) pack ;;
  templates) templates ;;
  publish) publish ;;
  version) echo "$VERSION" ;;
  all) restore; build; run_tests; pack ;;
  *) echo "Kullanım: $0 [all|restore|build|test|pack|templates|publish|version]" >&2; exit 2 ;;
esac
