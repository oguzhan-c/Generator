# canfxgen Rider eklentisi

Rider'da entity dosyasına sağ tık (Solution Explorer ya da editör) → **CanFx: Generate CRUD...**

Eklenti yalnızca arayüzdür: kodu `canfxgen` komut satırı aracı üretir (`--json` ile çağrılır). Önce aracı kur:

```bash
dotnet tool install -g CanFx.Generator      # nuget.org (yerel sürüm için: build/pack-local.sh --install)
```

Araç PATH'te ya da `~/.dotnet/tools` altında değilse Settings → Tools → CanFx Generator'da komutu yaz
(kaynak koddan: `dotnet run --project /yol/Generator/src/CanFx.Generator --`).

## Derleme

Gerekenler: JDK 21 ve (yalnızca ilk sefer, wrapper oluşturmak için) Gradle 9.

```bash
brew install openjdk@21 gradle
export JAVA_HOME="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home"

cd rider-plugin
gradle wrapper --gradle-version 9.1.0     # bir kez; gradlew dosyaları commit'lenir
./gradlew buildPlugin                     # → build/distributions/canfxgen-rider-<pluginVersion>.zip
```

`gradle.properties` içindeki `platformVersion` yüklü Rider'ın ana sürümü olmalı (Rider → About).
İlk derleme Rider SDK'sını indirir (birkaç GB, bir kez).

Kurulum: Rider → Settings → Plugins → ⚙ → **Install Plugin from Disk...** → zip dosyası → Rider'ı yeniden başlat.

## Yayın (JetBrains Marketplace)

Eklentinin sürümü Generator'ınkiyle aynıdır; `v*` etiketinde CI (`rider-plugin` işi) derleyip Marketplace'e gönderir.

**İlk sürüm elle yüklenir** (Marketplace kuralı): `./gradlew buildPlugin` → plugins.jetbrains.com → profil → **Upload
plugin** → `build/distributions/canfxgen-rider-<sürüm>.zip`, lisans: MIT
(`https://github.com/oguzhan-c/Generator/blob/main/LICENSE`). JetBrains ilk sürümü elle inceler (birkaç iş günü).

**Sonraki sürümler için GitHub secret'ları** (Settings → Secrets and variables → Actions):

| Secret | Değer |
|---|---|
| `JETBRAINS_MARKETPLACE_TOKEN` | plugins.jetbrains.com → profil → My Tokens → yeni token |
| `JETBRAINS_CERTIFICATE_CHAIN` | `base64 -i chain.crt` (isteğe bağlı, imza) |
| `JETBRAINS_PRIVATE_KEY` | `base64 -i private.pem` (isteğe bağlı) |
| `JETBRAINS_PRIVATE_KEY_PASSWORD` | anahtarın parolası (isteğe bağlı) |

İmza anahtarı (bir kez; dosyaları repoya koyma, güvenli bir yerde sakla):

```bash
openssl genpkey -aes-256-cbc -algorithm RSA -out private_encrypted.pem -pkeyopt rsa_keygen_bits:4096
openssl rsa -in private_encrypted.pem -out private.pem
openssl req -key private.pem -new -x509 -days 3650 -out chain.crt
```

İmza secret'ları yoksa eklenti imzasız gönderilir (Marketplace yine kendisi imzalar; kurulumda uyarı çıkabilir).

