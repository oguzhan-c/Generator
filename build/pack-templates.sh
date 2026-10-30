#!/usr/bin/env bash
# Şablon paketi (CanFx.Templates): verilen sürümle paketler.
#
#   build/pack-templates.sh <şablon sürümü> <çıktı klasörü>
#
# Şablonla oluşturulan projenin kullanacağı CanFx paket sürümü: Directory.Build.props → CanFxVersion
# (yayınlanmamış Core paketleriyle denemek için CANFX_VERSION ortam değişkeni).
# Çağıranlar: build/pack-local.sh ve build/ci.sh pack. Yalnızca bash + sed (CI imajlarında python gerekmez).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:?Kullanım: build/pack-templates.sh <sürüm> <çıktı klasörü>}"
OUT="${2:?Kullanım: build/pack-templates.sh <sürüm> <çıktı klasörü>}"
prop() { sed -n "s:.*<$1>\(.*\)</$1>.*:\1:p" "$ROOT/Directory.Build.props" | head -n 1; }
PREFIX="$(prop VersionPrefix)"
CANFX_VERSION="${CANFX_VERSION:-$(prop CanFxVersion)}"
STAGE="$ROOT/artifacts/obj/templates-stage"

echo "    şablon $VERSION → CanFx $CANFX_VERSION"
rm -rf "$STAGE" && mkdir -p "$STAGE" "$OUT"
cp -R "$ROOT/templates/CanFx.Templates/." "$STAGE/"

# Kopya, kökün Directory.Build.props'unu bulamaz: sürüm öneki elle verilir.
cat > "$STAGE/Directory.Build.props" <<PROPS
<Project>
  <PropertyGroup>
    <VersionPrefix>$PREFIX</VersionPrefix>
  </PropertyGroup>
</Project>
PROPS
sed -i.bak "s#<VersionPrefix>\$(\[System.Text.RegularExpressions.Regex\].*</VersionPrefix>##" "$STAGE/CanFx.Templates.csproj"

# Yer tutucular: TEMPLATE_PACKAGE_VERSION (şablonun sürümü, Directory.Packages.props'taki not),
# CANFX_PACKAGES_VERSION (Directory.Packages.props → CanFxVersion: kullanılacak CanFx paketleri)
grep -rlE "TEMPLATE_PACKAGE_VERSION|CANFX_PACKAGES_VERSION" "$STAGE/content" | while IFS= read -r file; do
  sed -i.bak -e "s#TEMPLATE_PACKAGE_VERSION#$VERSION#g" -e "s#CANFX_PACKAGES_VERSION#$CANFX_VERSION#g" "$file"
done
find "$STAGE" -name "*.bak" -delete

dotnet pack "$STAGE/CanFx.Templates.csproj" -c Release -o "$OUT" -p:Version="$VERSION"
