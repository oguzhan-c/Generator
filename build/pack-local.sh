#!/usr/bin/env bash
# Yerel paketleme: canfxgen (CanFx.Generator) ve proje şablonu (CanFx.Templates) AYNI yerel sürümle paketlenir.
#
#   build/pack-local.sh             → artifacts/packages (canfxgen), artifacts/templates (şablon)
#   build/pack-local.sh --install   → ayrıca şablonu (dotnet new) ve canfxgen'i (dotnet tool) bu sürümle kurar
#
# Sürüm: <VersionPrefix>-local.<yyyyMMddHHmm>. Şablonun kullandığı CanFx paketleri nuget.org'dan gelir
# (Directory.Build.props → CanFxVersion). Yayınlanmamış Core paketleriyle denemek için Core'da build/pack-local.sh
# çalıştır, sonra: CANFX_VERSION=<o sürüm> CANFX_FEED=<Core>/artifacts/packages templates/verify-templates.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PREFIX="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$ROOT/Directory.Build.props" | head -n 1)"
VERSION="${VERSION:-$PREFIX-local.$(date +%Y%m%d%H%M)}"
PACKAGES="$ROOT/artifacts/packages"
TEMPLATES="$ROOT/artifacts/templates"
INSTALL=false
[[ "${1:-}" == "--install" ]] && INSTALL=true

echo "==> Sürüm: $VERSION"

echo "==> canfxgen"
dotnet pack "$ROOT/CanFx.Generator.slnx" -c Release -o "$PACKAGES" -p:Version="$VERSION"

echo "==> Şablon paketi"
"$ROOT/build/pack-templates.sh" "$VERSION" "$TEMPLATES"

echo "$VERSION" > "$ROOT/artifacts/local-version.txt"

# Eski yerel sürümler: son $KEEP sürüm tutulur (disk dolmasın).
KEEP="${KEEP:-3}"
for folder in "$PACKAGES" "$TEMPLATES"; do
  [[ -d "$folder" ]] || continue
  stamps="$(ls "$folder" | sed -n 's/.*-local\.\([0-9]\{12\}\).*/\1/p' | sort -u)"
  drop=$(( $(printf '%s\n' "$stamps" | grep -c . || true) - KEEP ))
  (( drop > 0 )) || continue
  printf '%s\n' "$stamps" | head -n "$drop" | while IFS= read -r stamp; do   # macOS head negatif sayı almaz
    rm -f "$folder"/*"-local.$stamp".*
  done
done

if $INSTALL; then
  echo "==> Kurulum"
  # Aynı paket kimliğinin her sürümü dotnet new'de AYRI kurulum olarak kalır ve çakışır (ilki kullanılır): hepsini kaldır.
  for id in CanFx.Templates Can.Core.Templates; do
    for _ in $(seq 1 20); do
      dotnet new uninstall "$id" >/dev/null 2>&1 || break
    done
  done
  for id in CanFxGen CanFx.Generator.Cli Can.Core.Generator.Cli; do   # eski adlar
    dotnet tool uninstall -g "$id" >/dev/null 2>&1 || true
  done
  dotnet new install "$TEMPLATES/CanFx.Templates.$VERSION.nupkg"
  dotnet tool update -g CanFx.Generator --add-source "$PACKAGES" --version "$VERSION" \
    || dotnet tool install -g CanFx.Generator --add-source "$PACKAGES" --version "$VERSION"
fi

echo "==> Tamam: $VERSION"
$INSTALL && echo "    Rider açıksa yeni şablonu görmesi için kapatıp aç (⌘Q)."
echo "    canfxgen: $PACKAGES"
echo "    Şablon:  $TEMPLATES/CanFx.Templates.$VERSION.nupkg"
