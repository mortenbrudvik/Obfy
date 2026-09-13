package com.obfy.rider.listeners

import com.intellij.openapi.components.service
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.intellij.openapi.startup.ProjectActivity
import com.intellij.task.ProjectTaskListener
import com.intellij.task.ProjectTaskManager
import com.intellij.openapi.client.ClientProjectSession
import com.jetbrains.rd.protocol.SolutionExtListener
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rd.util.reactive.advise
import com.jetbrains.rd.util.reactive.viewNotNull
import com.jetbrains.rider.model.BuildModel
import com.jetbrains.rider.model.BuildResultKind
import com.obfy.rider.services.AssemblyLocator
import com.obfy.rider.services.ObfuscationService
import com.obfy.rider.services.OutputService
import com.obfy.rider.services.ProjectSettingsService
import com.obfy.rider.settings.ObfySettingsState
import java.io.File

/**
 * Startup activity that registers post-build obfuscation hooks.
 *
 * Rider builds via MSBuild, not JPS, so CompilerTopics.COMPILATION_STATUS (and
 * CompilationStatusListener) typically never fire and the compiler OpenAPI is not
 * on the Rider classpath. ProjectTaskListener is the platform build-finished hook;
 * ObfyBuildSessionListener listens to Rider's BuildModel.buildSession.
 */
class BuildListenerStartupActivity : ProjectActivity {

    private val logger = Logger.getInstance(BuildListenerStartupActivity::class.java)

    override suspend fun execute(project: Project) {
        logger.info("Obfy plugin initialized for project: ${project.name}")

        project.messageBus.connect(project).subscribe(ProjectTaskListener.TOPIC, object : ProjectTaskListener {
            override fun finished(result: ProjectTaskManager.Result) {
                if (result.isAborted || result.hasErrors()) return
                PostBuildObfuscator.run(project)
            }
        })
    }
}

/**
 * Rider MSBuild finished hook. Registered via rd.solutionExtListener.
 */
class ObfyBuildSessionListener : SolutionExtListener<BuildModel> {

    private val logger = Logger.getInstance(ObfyBuildSessionListener::class.java)

    override fun extensionCreated(lifetime: Lifetime, session: ClientProjectSession, model: BuildModel) {
        val project = session.project
        model.buildSession.viewNotNull(lifetime) { buildSessionLt, buildSession ->
            buildSession.result.advise(buildSessionLt) { result ->
                when (result.kind) {
                    BuildResultKind.Successful, BuildResultKind.HasWarnings -> PostBuildObfuscator.run(project)
                    else -> logger.debug("Skipping post-build obfuscation for build result ${result.kind}")
                }
            }
        }
    }
}

/**
 * Runs CLI obfuscation for each project that has postBuildEnabled in obfy.json.
 */
internal object PostBuildObfuscator {

    private val logger = Logger.getInstance(PostBuildObfuscator::class.java)
    private val lock = Any()
    @Volatile private var lastRunMs = 0L
    @Volatile private var running = false

    fun run(project: Project) {
        synchronized(lock) {
            val now = System.currentTimeMillis()
            if (running || now - lastRunMs < 2000) return
            lastRunMs = now
            running = true
        }

        ProgressManager.getInstance().run(object : Task.Backgroundable(
            project,
            "Obfy post-build obfuscation",
            true
        ) {
            override fun run(indicator: ProgressIndicator) {
                try {
                    obfuscateEnabledProjects(project, indicator)
                } finally {
                    running = false
                }
            }
        })
    }

    private fun obfuscateEnabledProjects(project: Project, indicator: ProgressIndicator) {
        val rootPath = project.basePath ?: return
        val root = File(rootPath)
        if (!root.isDirectory) return

        val globalSettings = ObfySettingsState.getInstance()
        val settingsService = project.service<ProjectSettingsService>()
        val outputService = project.service<OutputService>()
        val obfuscationService = service<ObfuscationService>()
        val releaseOnly = globalSettings.releaseOnly

        val projectDirs = AssemblyLocator.findProjectDirectories(root)
        val targets = projectDirs.mapNotNull { dir ->
            val settings = try {
                settingsService.loadSettingsForPath(dir.absolutePath)
            } catch (e: Exception) {
                logger.warn("Failed to load obfy.json in ${dir.absolutePath}: ${e.message}")
                return@mapNotNull null
            }
            if (!settings.postBuildEnabled) return@mapNotNull null
            Triple(dir, AssemblyLocator.findProjectName(dir), settings)
        }

        if (targets.isEmpty()) {
            return
        }

        if (globalSettings.showOutputWindow) {
            outputService.activate()
        }

        indicator.isIndeterminate = false
        var index = 0
        for ((dir, projectName, settings) in targets) {
            if (indicator.isCanceled) return
            index++
            indicator.fraction = index.toDouble() / targets.size
            indicator.text = "Obfuscating $projectName..."

            val assembly = AssemblyLocator.findOutputAssembly(dir, projectName, releaseOnly)
            if (assembly == null) {
                if (releaseOnly) {
                    outputService.info("Skipping post-build obfuscation for $projectName (Release-only, no Release assembly)")
                } else {
                    outputService.warning("Could not find output assembly for $projectName")
                }
                continue
            }

            if (releaseOnly && !AssemblyLocator.isReleasePath(assembly.absolutePath)) {
                outputService.info("Skipping post-build obfuscation for $projectName (Release-only)")
                continue
            }

            outputService.info("Post-build obfuscation starting for $projectName...")
            val assemblyPath = assembly.absolutePath
            val configFile = java.io.File(dir.path, "obfy.json")
            val result = obfuscationService.obfuscate(
                assemblyPath,
                assemblyPath,
                settings,
                outputService,
                configFile.takeIf { it.exists() }?.absolutePath
            )

            if (result.success) {
                outputService.success(
                    "Post-build obfuscation complete for $projectName: ${result.statistics.totalTransformations} transformations"
                )
            } else {
                outputService.error("Post-build obfuscation failed for $projectName: ${result.errorMessage}")
            }
        }
    }
}
