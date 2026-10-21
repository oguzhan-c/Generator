package com.canfx.canfxgen

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.options.BoundConfigurable
import com.intellij.openapi.ui.DialogPanel
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.panel

/** Eklenti ayarları (Settings → Tools → CanFxGen). */
@State(name = "CanGenSettings", storages = [Storage("canfxgen.xml")])
class CanGenSettings : PersistentStateComponent<CanGenSettings.State> {
    class State {
        /**
         * canfxgen komutu. Boşsa PATH'te ve ~/.dotnet/tools altında aranır. Geliştirirken kaynak koddan da çalıştırılabilir:
         * dotnet run --project /yol/Generator/src/CanFxGen --
         */
        var command: String = ""

        /** Üretimden sonra Create komut dosyası açılsın. */
        var openGeneratedFiles: Boolean = true
    }

    private var myState = State()

    override fun getState(): State = myState

    override fun loadState(state: State) {
        myState = state
    }

    companion object {
        fun getInstance(): CanGenSettings = ApplicationManager.getApplication().getService(CanGenSettings::class.java)
    }
}

class CanGenConfigurable : BoundConfigurable("CanFxGen") {
    override fun createPanel(): DialogPanel {
        val state = CanGenSettings.getInstance().state
        return panel {
            row("canfxgen command:") {
                textField()
                    .bindText(state::command)
                    .align(AlignX.FILL)
                    .comment(
                        "If empty, canfxgen is looked up in PATH and ~/.dotnet/tools. Install: " +
                            "dotnet tool install -g CanFxGen<br/>" +
                            "To run from source: dotnet run --project /path/to/Generator/src/CanFxGen --"
                    )
            }
            row {
                checkBox("Open generated files after generation").bindSelected(state::openGeneratedFiles)
            }
        }
    }
}
