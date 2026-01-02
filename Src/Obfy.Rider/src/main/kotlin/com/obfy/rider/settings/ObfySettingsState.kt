package com.obfy.rider.settings

import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.components.service
import com.obfy.rider.services.ObfuscationLevel

/**
 * Persistent global settings for Obfy plugin.
 * Stored in IDE configuration directory.
 */
@Service(Service.Level.APP)
@State(
    name = "ObfySettings",
    storages = [Storage("obfy.xml")]
)
class ObfySettingsState : PersistentStateComponent<ObfySettingsState.State> {

    data class State(
        var defaultLevel: ObfuscationLevel = ObfuscationLevel.Standard,
        var showOutputWindow: Boolean = true,
        var releaseOnly: Boolean = false
    )

    private var myState = State()

    override fun getState(): State = myState

    override fun loadState(state: State) {
        myState = state
    }

    var defaultLevel: ObfuscationLevel
        get() = myState.defaultLevel
        set(value) { myState.defaultLevel = value }

    var showOutputWindow: Boolean
        get() = myState.showOutputWindow
        set(value) { myState.showOutputWindow = value }

    var releaseOnly: Boolean
        get() = myState.releaseOnly
        set(value) { myState.releaseOnly = value }

    companion object {
        fun getInstance(): ObfySettingsState = service()
    }
}
