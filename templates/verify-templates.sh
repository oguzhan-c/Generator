#!/usr/bin/env bash
# Şablonları doğrular: canfxgen ve şablonu yeni bir yerel sürümle paketleyip kurar, farklı seçeneklerle üç çözüm üretir,
# kurulu canfxgen ile CRUD üretip her birini derler. Çıktı: <repo>/templates.log
#
# CanFx paketleri nuget.org'dan gelir (Directory.Build.props → CanFxVersion). Yayınlanmamış Core paketleriyle denemek
# için: Core'da build/pack-local.sh, sonra
#   CANFX_VERSION=$(cat ../Core/artifacts/local-version.txt) CANFX_FEED=$PWD/../Core/artifacts/packages templates/verify-templates.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/template-test"
LOG="$ROOT/templates.log"

: > "$LOG"
step() { echo "=== $*" | tee -a "$LOG"; }

# canfxgen ve şablon aynı yeni yerel sürümle paketlenir ve kurulur (build/pack-local.sh).
step "Paketleme ve kurulum (build/pack-local.sh --install)"
"$ROOT/build/pack-local.sh" --install >>"$LOG" 2>&1 || { step "HATA: paketleme/kurulum"; exit 1; }
VERSION="$(cat "$ROOT/artifacts/local-version.txt")"
step "Sürüm: $VERSION"

# Yerel CanFx paketleri verildiyse şablon onları kullanır (nuget.config'e kaynak olarak eklenir).
if [[ -n "${CANFX_FEED:-}" ]]; then
  step "CanFx: ${CANFX_VERSION:-?} ($CANFX_FEED)"
else
  step "CanFx: $(sed -n 's:.*<CanFxVersion>\(.*\)</CanFxVersion>.*:\1:p' "$ROOT/Directory.Build.props" | head -n 1) (nuget.org)"
fi

rm -rf "$OUT" && mkdir -p "$OUT"
failed=0

# Şablonda git'in yok saydığı kaynak dosya: yerelde paketlenir ama CI'daki (depodan) pakette olmaz.
ignored="$(cd "$ROOT" && GIT_OPTIONAL_LOCKS=0 git ls-files --others --ignored --exclude-standard templates/CanFx.Templates/content \
  | grep -vE '/(bin|obj)/|\.DS_Store$' || true)"
if [[ -n "$ignored" ]]; then
  step "HATA: şablonda git'in yok saydığı dosyalar var (.gitignore desenini düzelt):"
  printf '  %s\n' $ignored | tee -a "$LOG"
  failed=1
else
  step "TAMAM: şablonun tüm dosyaları git'te"
fi

# Yayınlanmamış CanFx paketleri: CanFx.* bu kaynaktan gelsin (nuget.config'e kaynak + eşleme eklenir).
add_local_feed() {
  python3 - "$1" "$CANFX_FEED" <<'PY'
import sys
path, feed = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8").read()
text = text.replace("  </packageSources>", f'    <add key="canfx-local" value="{feed}" />\n  </packageSources>', 1)
text = text.replace("  </packageSourceMapping>", '    <packageSource key="canfx-local">\n      <package pattern="CanFx.*" />\n    </packageSource>\n  </packageSourceMapping>', 1)
open(path, "w", encoding="utf-8").write(text)
PY
}

# Şablonun kod adı kuralı (AppCodeName): harf, rakam, nokta ve alt çizgi dışı → "_" (My-Shop → My_Shop).
code_name() { printf '%s' "${1//[^A-Za-z0-9_.]/_}"; }

# variant <ad> <veritabanı: postgres | sqlserver | sqlite | none> <şablon seçenekleri...>
# Şablon veritabanı seçmez (sağlayıcılar yorum satırı olarak hazır); kullanıcının seçimi choose-database.py ile taklit
# edilir: postgres / sqlserver hazır yorumları açar, sqlite docker'sız açılış testi içindir, none hiç dokunmaz.
variant() {
  local name="$1" database="$2"; shift 2
  step "Çözüm: $name (veritabanı: $database; $*)"
  if ! dotnet new canfx-monolith -n "$name" -o "$OUT/$name" "$@" >>"$LOG" 2>&1; then
    step "HATA: $name üretilemedi"; failed=1; return
  fi
  [[ -n "${CANFX_FEED:-}" ]] && add_local_feed "$OUT/$name/nuget.config"
  if [[ "$database" != "none" ]] && ! python3 "$ROOT/templates/choose-database.py" "$OUT/$name" "$database" >>"$LOG" 2>&1; then
    step "HATA: $name veritabanı seçilemedi ($database)"; failed=1; return
  fi
  if dotnet build "$OUT/$name/$(code_name "$name").slnx" -c Debug >>"$LOG" 2>&1; then
    step "TAMAM: $name derlendi"
  else
    step "HATA: $name derlenemedi"; failed=1
  fi
  # Kalan TODO sayısı (bilgi)
  echo "    TODO(canfxgen): $(grep -r --include=*.cs --include=*.json --include=*.config 'TODO(canfxgen)' "$OUT/$name" | wc -l | tr -d ' ')" | tee -a "$LOG"
}

variant Minimal sqlite --IdKeyType int --Otp false --EmailTwoFactor false --Passkey false --AuditTrail false --Postman false
variant Full postgres --MultiTenancy true --ExternalLogin true --Redis true --EventBus true --Hangfire true \
  --FileStorage true --Search true --Realtime true --Sms true --Settings true --Reporting true --OpenTelemetry true
variant Tenant sqlserver --IdKeyType long --ExternalLogin true --MultiTenancy true --Settings true --Hangfire true
# Tire ve nokta içeren ad: proje referansları, namespace'ler ve docker/bucket adları tutarlı olmalı
variant My-Shop.Api none --IdKeyType int --Redis true --FileStorage true

# ---------------------------------------------------------------- açılış
# Derlenen uygulama gerçekten açılıyor mu (DI kayıtları, ayarların okunması, veritabanı hazırlığı)? SQLite'lı Minimal
# docker gerektirmez: açılıp /health/live 200 dönmeli.
smoke() {
  local name="$1" port="$2"
  local project="$OUT/$name/src/$(code_name "$name").WebAPI"
  [[ -d "$project" ]] || return
  step "Açılış: $name"
  local log="$OUT/$name/run.log"
  ( cd "$project" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$port" DevServices__Enabled=false \
      Security__Jwt__SigningKey='verify-signing-key-0123456789abcdef01' Security__VerificationCodeKey='verify-code-key-0123456789abcdef0123' \
      dotnet run --no-build --no-launch-profile >"$log" 2>&1 ) &
  local pid=$! code="000"
  for _ in $(seq 1 60); do
    sleep 1
    code="$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/health/live" || true)"
    [[ "$code" == "200" ]] && break
    kill -0 "$pid" 2>/dev/null || break
  done
  pkill -f "$(code_name "$name").WebAPI" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  if [[ "$code" == "200" ]]; then
    step "TAMAM: $name açıldı (/health/live 200)"
  else
    step "HATA: $name açılmadı (ayrıntı: $log)"
    grep -m 5 -E "Exception|error|hata" "$log" | tee -a "$LOG"
    failed=1
  fi
}

smoke Minimal 5391

# ---------------------------------------------------------------- canfxgen
# Üretilen çözümlere iki entity eklenir (biri fabrika/Update metotlu, biri metotsuz: TODO yolları) ve CRUD üretilip
# yeniden derlenir.
step "canfxgen testleri"
if dotnet run --project "$ROOT/tests/CanFx.Generator.Tests" >>"$LOG" 2>&1; then step "TAMAM: canfxgen testleri"; else step "HATA: canfxgen testleri"; failed=1; fi

# Kurulu (paketlenmiş) canfxgen kullanılır: paketin kendisi de denenmiş olur.
CANGEN=("$HOME/.dotnet/tools/canfxgen")
step "canfxgen sürümü: $("${CANGEN[@]}" version 2>&1)"

generate() {
  local name="$1"
  local code; code="$(code_name "$name")"
  local domain="$OUT/$name/src/$code.Domain"
  [[ -d "$domain" ]] || return
  step "canfxgen: $name (Brand, Tag)"
  mkdir -p "$domain/Catalog" "$domain/Tags"
  cat > "$domain/Catalog/Brand.cs" <<CS
using CanFx.Domain.Auditing;
using CanFx.Domain.Results;

namespace $code.Domain.Catalog;

public enum BrandTier { Standard, Premium }

[Audited]
public sealed class Brand : FullAuditedAggregateRoot<Guid>
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 500;

    private Brand() => Name = string.Empty;

    private Brand(Guid id) : base(id) => Name = string.Empty;

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public BrandTier Tier { get; private set; }

    public static Result<Brand> Create(string name, string? description, BrandTier tier) =>
        new Brand(Guid.CreateVersion7()) { Name = name.Trim(), Description = description, Tier = tier };

    public Result<Success> Update(string name, string? description, bool isActive)
    {
        Name = name.Trim();
        Description = description;
        IsActive = isActive;
        return Result.Success;
    }
}
CS
  cat > "$domain/Tags/Tag.cs" <<CS
using CanFx.Domain.Entities;

namespace $code.Domain.Tags;

public sealed class Tag : Entity<int>
{
    public string Name { get; set; } = string.Empty;

    public int UsageCount { get; set; }
}
CS
  run_cangen() { (cd "$OUT/$name" && "${CANGEN[@]}" "$@") >>"$LOG" 2>&1 || { step "HATA: canfxgen $* ($name)"; failed=1; }; }
  run_cangen generate entity Color                            # yeni entity (taban sınıf seçenekleri yorumda)
  run_cangen generate crud Color
  run_cangen generate crud Brand                              # tüm mekanizmalar
  run_cangen generate crud Tag -s                             # yalnızca yetki: transaction yok, önbellek yok, dinamik sorgu yok
  run_cangen generate dynamic-query Tag                       # dinamik sorgu sonradan
  run_cangen generate seeder Brand                            # zorunlu (JSON) + örnek veri
  run_cangen generate seeder Color --has-data                 # zorunlu veri migration'a (HasData) + örnek veri
  run_cangen disable caching dynamic-query Brand              # mekanizmaları sonradan kapat / aç
  run_cangen enable caching logging Tag
  run_cangen disable secured GetByIdTagQuery
  run_cangen rate-limit reports GetListBrandQuery --permit 20 --partition-by Tenant   # politikayı uca bağla
  run_cangen rate-limit sensitive TagsController.Delete
  if [[ "$name" == "Minimal" ]]; then
    run_cangen add feature redis                              # şablon farkı projeye yama olarak
    run_cangen add feature search
  fi
  if [[ "$name" == "Full" ]]; then
    run_cangen remove feature realtime
  fi
  run_cangen generate command SyncBrand Brands -e Put
  run_cangen generate query GetBrandByName Brands
  if dotnet build "$OUT/$name/$(code_name "$name").slnx" -c Debug >>"$LOG" 2>&1; then
    step "TAMAM: $name + canfxgen derlendi"
  else
    step "HATA: $name + canfxgen derlenemedi"; failed=1
  fi
}

generate Minimal
generate Full
generate My-Shop.Api

# ---------------------------------------------------------------- standart
# Şablonla gelen (Auth, Account, Roles ...) ve canfxgen'in ürettiği (Brands ...) özellikler aynı kurala uyar:
#   Features/{Özellik}/Constants/ + Rules/  (mesajlar, yetkiler, iş kuralları)
#   Commands|Queries/{İstek}/  {İstek}.cs, {İstek}Handler.cs, {İstek}Validator.cs, {...}Response.cs / {...}Dto.cs
#   dosya başına tek tip ve dosya adı = tip adı (Domain hariç: entity dosyaları kullanıcının).
#   metinler kodda değil Features/{Özellik}/Localization/*.{dil}.json'da; her kodun her dilde metni var.
#   hatalar exception (Rules) ile; istekler Result<T> dönmez.
standards() {
  local name="$1"
  local features="$OUT/$name/src/$(code_name "$name").Application/Features"
  [[ -d "$features" ]] || return
  local problems=""
  local f d

  f="$(grep -rlE 'IRequest<Result<|Task<Result<|IRequestHandler<[^>]*Result<' --include=*.cs "$features" || true)"
  [[ -n "$f" ]] && problems+=$'\n  Result dönen istek (exception + Rules kullanılmalı):\n'"$f"

  for f in $(grep -rlE 'AbstractValidator<' --include=*.cs "$features"); do
    grep -qE 'IRequest(<|,|[[:space:]]|$)' "$f" && problems+=$'\n  Validator istek dosyasında (ayrı {İstek}Validator.cs olmalı): '"$f"
  done

  for f in $(grep -rlE 'IRequest(<|,|[[:space:]]|$)' --include=*.cs "$features"); do
    grep -qE '(record|class) [A-Za-z0-9_]+(Response|Dto)\b' "$f" && problems+=$'\n  Yanıt/DTO istek dosyasında (ayrı dosya olmalı): '"$f"
  done

  # Dosya başına tek tip, dosya adı = tip adı (şablon ve canfxgen kodu; Domain'deki entity'ler kullanıcının)
  local src="$OUT/$name/src" n t
  while IFS= read -r f; do
    n="$(grep -cE '^(public|internal) ([a-z]+ )*(class|record|interface|enum|struct) ' "$f" || true)"
    t="$(grep -oE '^(public|internal) ([a-z]+ )*(class|record|interface|enum|struct) [A-Za-z0-9_]+' "$f" | head -1 | awk '{print $NF}')"
    if [[ "$n" -gt 1 ]]; then
      problems+=$'\n  Dosyada birden çok tip (her tip kendi dosyasında olmalı): '"$f"
    elif [[ "$n" -eq 1 && "$t" != "$(basename "$f" .cs)" ]]; then
      problems+=$'\n  Dosya adı tip adıyla aynı değil ('"$t"'): '"$f"
    fi
  done < <(find "$src" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*.Domain/*')

  # Her hata kodunun her dilde metni olmalı (metinler Features/*/Localization/*.{dil}.json'da)
  local missing
  if ! missing="$(python3 "$ROOT/templates/check-translations.py" "$OUT/$name/src/$(code_name "$name").Application")"; then
    problems+=$'\n  Eksik çeviri:\n'"$missing"
  fi

  for d in "$features"/*/; do
    [[ -d "$d/Constants" && -d "$d/Rules" ]] || problems+=$'\n  Constants/ ya da Rules/ eksik: '"$d"
  done

  if [[ -n "$problems" ]]; then
    step "HATA: $name standarda uymuyor:"
    echo "$problems" | tee -a "$LOG"
    failed=1
  else
    step "TAMAM: $name standarda uygun (Constants/Rules, dosya başına tek tip, ayrı handler/validator/yanıt, çeviriler tam, Result yok)"
  fi
}
standards Minimal
standards Full
standards Tenant
standards My-Shop.Api

[[ $failed == 0 ]] && step "HEPSİ TAMAM" || step "HATALAR VAR (ayrıntı yukarıda)"
