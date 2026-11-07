package cn.tyw.launcher.core

import android.content.Context
import android.os.Build
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID

/**
 * 错误匿名上报（POST /report），与 Windows / Linux / macOS 版格式一致。
 *
 * 首行和「平台：」一行都会写明平台，例如
 * `平台：Android（14，Xiaomi 23127PN0CG，arm64）`，
 * 服务端 User-Errors 里的日志就能直接区分四个平台。
 *
 * 先落盘再发送：崩溃时进程马上就没了，上报内容留在 filesDir/reports，
 * 下次启动（且配置了服务端地址）自动补发，成功一条删一条。
 */
object Report {
    private const val MAX_CACHED = 100

    @Volatile
    private var appContext: Context? = null

    /** 在 MainActivity.onCreate 里、Settings.init 之后调用。 */
    fun install(context: Context) {
        appContext = context.applicationContext
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, ex ->
            try {
                record("崩溃", ex)
            } catch (_: Throwable) {
            }
            // 交回系统原处理器，保持原本的崩溃行为
            if (previous != null) {
                previous.uncaughtException(thread, ex)
            } else {
                android.os.Process.killProcess(android.os.Process.myPid())
                kotlin.system.exitProcess(10)
            }
        }
        Thread({ flush() }, "TYW Report").start()
    }

    /** 手动上报一条非致命错误（不会结束应用）。 */
    fun send(kind: String, detail: String, ex: Throwable? = null) {
        try {
            record(kind, ex, detail)
        } catch (_: Throwable) {
        }
    }

    /** 上报用的平台描述：Android 版本 + 厂商机型 + 架构。 */
    fun platformDisplay(): String {
        val abi = Build.SUPPORTED_ABIS.firstOrNull().orEmpty()
        val arch = when {
            abi.contains("arm64") || abi.contains("aarch64") -> "arm64"
            abi.contains("x86_64") || abi.contains("amd64") -> "x64"
            abi.contains("armeabi") -> "arm32"
            abi.contains("x86") -> "x86"
            else -> abi.ifEmpty { "unknown" }
        }
        val device = listOf(Build.MANUFACTURER.orEmpty(), Build.MODEL.orEmpty())
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .distinctBy { it.lowercase(Locale.ROOT) }
            .joinToString(" ")
            .ifEmpty { "未知机型" }
        return "Android（${Build.VERSION.RELEASE}，${device}，${arch}）"
    }

    private fun record(kind: String, ex: Throwable?, detail: String = "") {
        val ctx = appContext ?: return
        val dir = File(ctx.filesDir, "reports").apply { mkdirs() }
        val now = Date()
        val stamp = SimpleDateFormat("yyyyMMdd-HHmmss-SSS", Locale.ROOT).format(now)
        val platform = platformDisplay()
        val body = buildString {
            append("TYW Launcher ").append(platform).append(" 匿名上报（").append(kind).append("）\n")
            append("平台：").append(platform).append('\n')
            append("时间：").append(SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.ROOT).format(now)).append('\n')
            append("版本：").append(AppInfo.versionName).append("（版本码 ").append(AppInfo.versionCode).append("）\n")
            append("版本类型：").append(AppInfo.channel.display)
                .append("（").append(AppInfo.channel.key).append(" 通道）\n")
            append("设备码：").append(Settings.deviceCode).append('\n')
            append("================================================\n")
            if (detail.isNotBlank()) append(detail.trim()).append('\n')
            if (ex != null) {
                append(ex.javaClass.name).append("：").append(ex.message.orEmpty()).append('\n')
                append(ex.stackTraceToString())
            }
        }
        File(dir, "report-$stamp-${UUID.randomUUID().toString().substring(0, 6)}.log")
            .writeText(body, Charsets.UTF_8)
        trim(dir)
        Thread({ flush() }, "TYW Report").start()
    }

    /** 按时间顺序补发缓存，失败就停下等下次启动。没配置服务端时什么都不做。 */
    private fun flush() {
        val ctx = appContext ?: return
        val base = AppInfo.SERVER_ADDRESS
        if (base.isEmpty()) return
        val dir = File(ctx.filesDir, "reports")
        val files = dir.listFiles { f -> f.name.startsWith("report-") && f.name.endsWith(".log") }
            ?.sortedBy { it.name } ?: return
        for (f in files) {
            val content = try {
                f.readText(Charsets.UTF_8)
            } catch (_: Throwable) {
                continue
            }
            if (content.isEmpty()) {
                f.delete()
                continue
            }
            if (!post(base + "report", content)) break
            f.delete()
        }
    }

    private fun post(url: String, body: String): Boolean = try {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            doOutput = true
            connectTimeout = 8000
            readTimeout = 8000
            setRequestProperty("Content-Type", "text/plain; charset=utf-8")
        }
        conn.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
        val code = conn.responseCode
        conn.disconnect()
        code in 200..299
    } catch (_: Throwable) {
        false
    }

    private fun trim(dir: File) {
        val files = dir.listFiles { f -> f.name.startsWith("report-") }?.sortedBy { it.name } ?: return
        for (i in 0 until files.size - MAX_CACHED) {
            try {
                files[i].delete()
            } catch (_: Throwable) {
            }
        }
    }
}
