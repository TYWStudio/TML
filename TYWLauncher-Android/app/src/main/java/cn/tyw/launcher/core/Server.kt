package cn.tyw.launcher.core

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * 服务端接口。字段名与 Windows / Linux 版一致：
 * Notice.cfg 十段（标题|快照码|正式码|正式公告|快照公告|内测码|内测公告|正式详情|快照详情|内测详情），
 * config.json 的 Android.Channels.<通道>.{VersionCode,File}。
 */
object Server {
    private const val TIMEOUT_MS = 12000

    data class Notice(val title: String, val content: String, val detail: String)
    data class ChannelFile(val versionCode: Int, val file: String)

    var online: Boolean = false
        private set
    var lastError: String = ""
        private set

    private var noticeTitle = ""
    private var segments: List<String> = emptyList()
    var channels: Map<String, ChannelFile> = emptyMap()
        private set
    var cloudSettings: Map<String, String> = emptyMap()
        private set

    /** 拉取 Notice.cfg 与 config.json。没网不算错误，只置 online=false。 */
    suspend fun refresh() = withContext(Dispatchers.IO) {
        try {
            val raw = get("Notice.cfg") ?: throw IllegalStateException("Notice.cfg 为空")
            val parts = raw.split("|")
            if (parts.size < 3) throw IllegalStateException("Notice.cfg 格式错误")
            segments = parts
            noticeTitle = parts[0]

            val cfg = get("config.json")
            if (cfg != null) parseConfig(cfg)

            online = true
            lastError = ""
        } catch (e: Exception) {
            online = false
            lastError = e.message ?: "连接服务端失败"
        }
    }

    private fun parseConfig(raw: String) {
        try {
            val root = JSONObject(raw)
            root.optJSONObject("settings")?.let { s ->
                val map = mutableMapOf<String, String>()
                s.keys().forEach { k -> map[k] = s.optString(k) }
                cloudSettings = map
            }
            val android = root.optJSONObject("Android") ?: return
            val chs = android.optJSONObject("Channels") ?: return
            val map = mutableMapOf<String, ChannelFile>()
            chs.keys().forEach { key ->
                val obj = chs.optJSONObject(key) ?: return@forEach
                val code = obj.optInt("VersionCode", 0)
                val file = obj.optString("File", "")
                if (file.isNotEmpty()) map[key] = ChannelFile(code, file)
            }
            channels = map
        } catch (_: Exception) {
        }
    }

    /** 当前通道的公告：正式/快照/内测各用各的。 */
    fun notice(channel: Channel = Settings.channel): Notice? {
        if (segments.size < 3) return null
        fun at(i: Int) = segments.getOrNull(i)?.replace("\\n", "\n") ?: ""
        val (msgIndex, detailIndex) = when (channel) {
            Channel.RELEASE -> 3 to 7
            Channel.SNAPSHOT -> 4 to 8
            Channel.BETA -> 6 to 9
        }
        val content = at(msgIndex)
        if (content.isBlank()) return null
        return Notice(noticeTitle.ifBlank { "TYW Launcher 公告" }, content, at(detailIndex))
    }

    /** 服务端给该通道配置的版本码与包地址。 */
    fun channelFile(channel: Channel): ChannelFile? = channels[channel.key]

    /** 是否有新版本：服务端版本码大于本地版本码，且配置了包。 */
    fun updateAvailable(channel: Channel = Settings.channel): ChannelFile? {
        val info = channelFile(channel) ?: return null
        return if (info.versionCode > AppInfo.versionCode) info else null
    }

    fun packageUrl(file: String): String =
        if (file.startsWith("http", ignoreCase = true)) file
        else AppInfo.SERVER_ADDRESS.trimEnd('/') + "/" + file.trimStart('/')

    // ================= 内测校验（与 Linux/Windows 同一套接口） =================

    data class BetaResult(val ok: Boolean, val message: String, val versionCode: Int)

    suspend fun verifyBetaCode(code: String): BetaResult = callBeta("beta", code)
    suspend fun verifyBetaUpdateCode(code: String): BetaResult = callBeta("betaupdate", code)

    private suspend fun callBeta(path: String, code: String): BetaResult = withContext(Dispatchers.IO) {
        try {
            val url = "$path?ident=${URLEncoder.encode(Settings.deviceCode, "UTF-8")}" +
                "&code=${URLEncoder.encode(code, "UTF-8")}"
            val raw = get(url) ?: throw IllegalStateException("服务端无响应")
            val obj = JSONObject(raw)
            BetaResult(obj.optBoolean("ok", false), obj.optString("msg", ""), obj.optInt("version_code", 0))
        } catch (e: Exception) {
            BetaResult(false, e.message ?: "无法连接服务端", 0)
        }
    }

    suspend fun reportBetaUpdateDone(code: String) = withContext(Dispatchers.IO) {
        try {
            get("betaupdate?ident=${URLEncoder.encode(Settings.deviceCode, "UTF-8")}" +
                "&code=${URLEncoder.encode(code, "UTF-8")}&done=1")
        } catch (_: Exception) {
        }
    }

    // ================= HTTP =================

    fun get(path: String, timeoutMs: Int = TIMEOUT_MS): String? {
        // 未配置服务端时直接返回 null，不发起请求，也不当成错误
        if (!path.startsWith("http", true) && AppInfo.SERVER_ADDRESS.isEmpty()) return null
        val url = if (path.startsWith("http", true)) path else AppInfo.SERVER_ADDRESS.trimEnd('/') + "/" + path.trimStart('/')
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = timeoutMs
            readTimeout = timeoutMs
            requestMethod = "GET"
            setRequestProperty("User-Agent", "TYWLauncher-Android/${AppInfo.versionName}")
        }
        return try {
            if (conn.responseCode !in 200..299) return null
            conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }

    /** 下载到 dest，progress 回调 0~1。成功返回 true。 */
    fun download(url: String, dest: File, progress: (Double) -> Unit = {}): Boolean {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = TIMEOUT_MS
            readTimeout = 30000
            instanceFollowRedirects = true
        }
        return try {
            if (conn.responseCode !in 200..299) return false
            val total = conn.contentLengthLong
            dest.parentFile?.mkdirs()
            conn.inputStream.use { input ->
                FileOutputStream(dest).use { output ->
                    val buf = ByteArray(64 * 1024)
                    var read: Int
                    var done = 0L
                    while (input.read(buf).also { read = it } > 0) {
                        output.write(buf, 0, read)
                        done += read
                        if (total > 0) progress(done.toDouble() / total)
                    }
                    output.flush()
                }
            }
            true
        } catch (_: Exception) {
            false
        } finally {
            conn.disconnect()
        }
    }
}
