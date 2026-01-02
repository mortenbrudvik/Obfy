plugins {
    id("java")
    id("org.jetbrains.kotlin.jvm") version "2.0.21"
    id("org.jetbrains.intellij.platform") version "2.2.1"
}

group = providers.gradleProperty("pluginGroup").get()
version = providers.gradleProperty("pluginVersion").get()

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        rider(providers.gradleProperty("platformVersion").get())

        pluginVerifier()
        zipSigner()
    }

    implementation("com.google.code.gson:gson:2.10.1")

    testImplementation(kotlin("test"))
}

kotlin {
    jvmToolchain(21)
}

intellijPlatform {
    pluginConfiguration {
        id = "com.obfy.rider"
        name = "Obfy"
        version = providers.gradleProperty("pluginVersion")
        description = """
            Obfuscate .NET assemblies directly from JetBrains Rider.

            Features:
            <ul>
                <li>Right-click any C# project to obfuscate</li>
                <li>Configure post-build automatic obfuscation</li>
                <li>Choose from preset levels: Minimal, Standard, Aggressive</li>
                <li>Real-time output logging in tool window</li>
            </ul>
        """.trimIndent()

        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            untilBuild = providers.gradleProperty("pluginUntilBuild")
        }

        vendor {
            name = "Obfy"
            url = "https://github.com/obfy/obfy"
        }
    }

    signing {
        // Configure signing for marketplace publishing
        // certificateChain = providers.fileContents(layout.projectDirectory.file("chain.crt"))
        // privateKey = providers.fileContents(layout.projectDirectory.file("private.pem"))
        // password = providers.environmentVariable("PRIVATE_KEY_PASSWORD")
    }

    publishing {
        // token = providers.environmentVariable("PUBLISH_TOKEN")
    }
}

tasks {
    runIde {
        jvmArgs("-Xmx2g")
    }

    test {
        useJUnitPlatform()
    }
}
