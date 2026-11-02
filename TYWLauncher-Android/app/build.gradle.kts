import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// 版本通道：-PtywChannel=release|snapshot|beta（与 Windows / Linux 三通道一一对应）
val tywChannel: String = (project.findProperty("tywChannel") as String?)?.lowercase() ?: "release"

val baseVersionName = "1.1.15"
val channelVersionName = when (tywChannel) {
    "snapshot" -> "$baseVersionName-snapshot"
    "beta" -> "$baseVersionName-beta"
    else -> baseVersionName
}
// 正式版版本码 14；快照/内测各自独立从小号起，服务端按通道分别下发
val channelVersionCode = when (tywChannel) {
    "snapshot" -> 2
    "beta" -> 2
    else -> 15
}
val channelKey = when (tywChannel) {
    "snapshot" -> "Snapshot"
    "beta" -> "Beta"
    else -> "Release"
}

android {
    namespace = "cn.tyw.launcher"
    compileSdk = 35

    defaultConfig {
        applicationId = "cn.tyw.launcher"
        minSdk = 26
        targetSdk = 35
        versionCode = channelVersionCode
        versionName = channelVersionName

        buildConfigField("String", "CHANNEL", "\"$channelKey\"")
        buildConfigField("String", "VERSION_NAME_CN", "\"$channelVersionName\"")
        buildConfigField("int", "VERSION_CODE_CN", "$channelVersionCode")
        buildConfigField("String", "SERVER_ADDRESS", "\"http://Launcher.mc.tywmc.cn:17403/\"")
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlin.compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // 不签名：CI 里用 debug 密钥签，便于直接安装测试
            signingConfig = signingConfigs.getByName("debug")
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
        debug {
            applicationIdSuffix = ""
        }
    }

    // 产物名与 Windows/Linux 一致：TYWLauncher-android-<通道>.apk
    applicationVariants.all {
        outputs.all {
            val out = this as com.android.build.gradle.internal.api.BaseVariantOutputImpl
            val suffix = when (tywChannel) {
                "snapshot" -> "-snapshot"
                "beta" -> "-beta"
                else -> ""
            }
            out.outputFileName = "TYWLauncher-android$suffix.apk"
        }
    }

    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation(platform("androidx.compose:compose-bom:2024.10.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
