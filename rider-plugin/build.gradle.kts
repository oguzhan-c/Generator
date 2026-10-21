// canfxgen Rider eklentisi: yalnızca arayüz (IntelliJ Platform tarafı). Kodu canfxgen komut satırı aracı üretir; eklenti onu
// çağırır, sonucu bildirir ve üretilen dosyaları açar. Derleme: ./gradlew buildPlugin → build/distributions/*.zip
plugins {
    id("org.jetbrains.kotlin.jvm") version "2.2.20"
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

group = "com.canfx"
version = providers.gradleProperty("pluginVersion").get()

kotlin {
    jvmToolchain(21)
}

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // Rider çok işletim sistemli arşivden indirilir; JetBrains Runtime ayrıca eklenir.
        rider(providers.gradleProperty("platformVersion")) {
            useInstaller = false
        }
        jetbrainsRuntime()
    }
}

// GitHub Actions'ta tanımlanmamış secret boş metin olarak gelir: boşsa ayar yok sayılır.
fun secret(name: String) = providers.environmentVariable(name).filter { it.isNotBlank() }

intellijPlatform {
    pluginConfiguration {
        version = providers.gradleProperty("pluginVersion")
        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            untilBuild = provider { null }
        }
        changeNotes = providers.gradleProperty("pluginVersion").map { version ->
            """
            <p><b>$version</b></p>
            <ul>
              <li>First Marketplace release.</li>
              <li>Right-click an entity in the Domain project → <b>CanFx: Generate CRUD</b>: commands, queries,
                  validators, responses, rules, permissions, EF configuration and endpoints.</li>
              <li>Options: plural name, caching, logging, transaction, secured operation, dynamic query.</li>
            </ul>
            """.trimIndent()
        }
    }
    buildSearchableOptions = false

    // İmza (isteğe bağlı; yoksa Marketplace yine kendisi imzalar, ama kurulumda "imzasız" uyarısı çıkar).
    // Değerler Base64 olarak verilir: rider-plugin/README.md → Yayın.
    signing {
        certificateChain = secret("CERTIFICATE_CHAIN")
        privateKey = secret("PRIVATE_KEY")
        password = secret("PRIVATE_KEY_PASSWORD")
    }

    // ./gradlew publishPlugin: JetBrains Marketplace (ilk sürüm web sitesinden elle yüklenir).
    publishing {
        token = secret("PUBLISH_TOKEN")
    }
}
