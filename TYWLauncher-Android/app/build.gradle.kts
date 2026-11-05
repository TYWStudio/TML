import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// 版本基线。三通道共用同一个 versionName，通道后缀由各自的 buildType 追加。
val baseVersionName = "1.1.37"

android {
    namespace = "cn.tyw.launcher"
    compileSdk = 35

    defaultConfig {
        applicationId = "cn.tyw.launcher"
        minSdk = 26
        targetSdk = 35

        // 三个通道共用同一个 applicationId，APK 的 versionCode 也保持同一个值：
        // 这样从正式版切到快照版（版本码更小）时不会被系统判成降级而拒绝安装，
        // 通道自身的版本码由 BuildConfig.VERSION_CODE_CN 提供，只用于和服务端比对更新。
        versionCode = 37
        versionName = baseVersionName

        // 服务端地址：开源仓库默认空（不连任何服务器）。
        // 官方发行包在构建时用环境变量 TYW_SERVER_URL 注入，运行期还能在设置里改。
        buildConfigField("String", "SERVER_ADDRESS", "\"${System.getenv("TYW_SERVER_URL") ?: ""}\"")
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
        // ===== 正式版：build/outputs/apk/release/ =====
        getByName("release") {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debug")
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            buildConfigField("String", "CHANNEL", "\"Release\"")
            buildConfigField("String", "VERSION_NAME_CN", "\"$baseVersionName\"")
            buildConfigField("int", "VERSION_CODE_CN", "19")
        }

        // ===== 快照版：build/outputs/apk/snapshot/ =====
        create("snapshot") {
            isMinifyEnabled = false
            isDebuggable = false
            signingConfig = signingConfigs.getByName("debug")
            versionNameSuffix = "-snapshot"
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            buildConfigField("String", "CHANNEL", "\"Snapshot\"")
            buildConfigField("String", "VERSION_NAME_CN", "\"$baseVersionName-snapshot\"")
            buildConfigField("int", "VERSION_CODE_CN", "6")
        }

        // ===== 内测版：build/outputs/apk/debug/ =====
        // 与 Windows / Linux 一致：Debug 配置就是内测版
        getByName("debug") {
            isMinifyEnabled = false
            isDebuggable = true
            signingConfig = signingConfigs.getByName("debug")
            versionNameSuffix = "-beta"
            applicationIdSuffix = ""
            buildConfigField("String", "CHANNEL", "\"Beta\"")
            buildConfigField("String", "VERSION_NAME_CN", "\"$baseVersionName-beta\"")
            buildConfigField("int", "VERSION_CODE_CN", "6")
        }
    }

    // 产物名与 Windows / Linux 一致：TYWLauncher-android-<通道>.apk
    applicationVariants.all {
        val bt = buildType.name
        val suffix = when (bt) {
            "snapshot" -> "-snapshot"
            "debug" -> "-beta"
            else -> ""
        }
        outputs.all {
            val out = this as com.android.build.gradle.internal.api.BaseVariantOutputImpl
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
    implementation("androidx.compose.animation:animation")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    // Android 原生 JRE 的官方发布是 .tar.xz，需要 XZ 解压（纯 Java 实现）
    implementation("org.tukaani:xz:1.10")
}
