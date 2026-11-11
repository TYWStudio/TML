package cn.tyw.launcher.core

import android.os.Build
import cn.tyw.launcher.BuildConfig
import cn.tyw.launcher.core.ext.ExtManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * 拓展商店：从下载站（downloads.tywmc.cn）拉取「分支项目树」并在设置里展示，
 * 支持一键把拓展包下载并导入启动器（复用 ExtManager）。
 *
 * 与 Windows / Linux / macOS 版同一套思路：
 *   应用凭证密钥（X-Api-Key）恒不进源码，由构建期注入 BuildConfig.STORE_API_KEY；
 *   运行时只在内存里使用，界面只显示掩码，绝不打印明文。
 *   该密钥用于让启动器成为「可信内部调用方」，绕过下载站的来源域名 / IP 白名单，
 *   在任意客户端上都能读取与下载；密钥无法反推、源码里永远看不到真实值。
 */
object Store {
    private const val BASE_URL = "https://downloads.tywmc.cn/"
    private const val APP_SLUG = "tyw-launcher"

    private val apiKey: String get() = BuildConfig.STORE_API_KEY.trim()

    /** 当前运行平台（与下载站 os 字段一致：android）。 */
    private val storeOs: String get() = "android"

    /** 当前 CPU 架构（与下载站 arch 字段一致：取设备主 ABI）。 */
    private val storeArch: String get() = Build.SUPPORTED_ABIS.firstOrNull() ?: "unknown"

    /** 密钥掩码：用于界面展示，避免泄露明文。 */
    fun keyMasked(): String {
        val k = apiKey
        if (k.isEmpty()) return "（未配置）"
        if (k.length <= 6) return "****"
        return k.take(4) + "****" + k.takeLast(2)
    }

    /** 分支项目（下载站 depth=2 节点）。 */
    data class StoreProject(
        val id: Int = 0,
        val name: String = "",
        val slug: String = "",
        val tagline: String = "",
        val description: String = "",
        val icon: String = "",
        val downloads: Int = 0,
        val files: Int = 0,
        val sizeHuman: String = "",
        val latest: String = ""
    )

    /** 分类（下载站 depth=1 节点），其下挂若干项目。 */
    data class StoreCategory(
        val id: Int = 0,
        val name: String = "",
        val tagline: String = "",
        val description: String = "",
        val icon: String = "",
        val projects: MutableList<StoreProject> = mutableListOf()
    )

    /** 拉取分类树：返回所有可见的 depth=1 分类，以及各自 depth=2 的项目。 */
    @Throws(Exception::class)
    suspend fun fetchTree(): List<StoreCategory> = withContext(Dispatchers.IO) {
        val url = BASE_URL + "api/nodes.php?app=" + URLEncoder.encode(APP_SLUG, "UTF-8") +
            "&depth=2&with=stats"
        val raw = get(url) ?: throw IllegalStateException("下载站无响应")
        val root = JSONObject(raw)
        val nodes = root.optJSONArray("nodes") ?: JSONArray()
        val all = mutableListOf<JSONObject>()
        val depth1 = mutableListOf<JSONObject>()
        for (i in 0 until nodes.length()) {
            val n = nodes.optJSONObject(i) ?: continue
            all.add(n)
            if (n.optInt("depth", 0) == 1) depth1.add(n)
        }
        val cats = mutableListOf<StoreCategory>()
        for (c in depth1) {
            val cat = StoreCategory(
                id = c.optInt("id", 0),
                name = c.optString("name", ""),
                tagline = c.optString("tagline", ""),
                description = c.optString("description", ""),
                icon = c.optString("icon", "")
            )
            val pid = cat.id
            for (n in all) {
                if (n.optInt("depth", 0) == 2 && n.optInt("parent_id", 0) == pid) {
                    cat.projects.add(projectFrom(n))
                }
            }
            cats.add(cat)
        }
        cats
    }

    private fun projectFrom(j: JSONObject): StoreProject {
        val stats = j.optJSONObject("stats")
        return StoreProject(
            id = j.optInt("id", 0),
            name = j.optString("name", ""),
            slug = j.optString("slug", ""),
            tagline = j.optString("tagline", ""),
            description = j.optString("description", ""),
            icon = j.optString("icon", ""),
            downloads = stats?.optInt("downloads", 0) ?: 0,
            files = stats?.optInt("files", 0) ?: 0,
            sizeHuman = stats?.optString("size_human", "") ?: "",
            latest = stats?.optString("latest", "") ?: ""
        )
    }

    /** 拉取某项目的下载文件清单（带 get_url，可直接拼成下载链接）。 */
    @Throws(Exception::class)
    suspend fun fetchFiles(nodeId: Int): List<JSONObject> = withContext(Dispatchers.IO) {
        val url = BASE_URL + "api/files.php?node=" + nodeId +
            "&recursive=1&latest=1&get_url=1&limit=200"
        val raw = get(url) ?: throw IllegalStateException("下载站无响应")
        val root = JSONObject(raw)
        val arr = root.optJSONArray("files") ?: JSONArray()
        val list = mutableListOf<JSONObject>()
        for (i in 0 until arr.length()) (arr.optJSONObject(i) ?: continue).also { list.add(it) }
        list
    }

    /** 按当前平台挑一个最匹配的下载文件；只认拓展包（.tmlext / .jar）。 */
    fun pickFile(files: List<JSONObject>): JSONObject? {
        var best: JSONObject? = null
        var bestScore = -1
        for (f in files) {
            val fname = f.optString("file", "").lowercase()
            if (!fname.endsWith(".tmlext") && !fname.endsWith(".jar")) continue
            val os = f.optString("os", "")
            val arch = f.optString("arch", "")
            var score = 0
            if (os == storeOs) score += 2
            if (arch == storeArch) score += 1
            if (score > bestScore) {
                bestScore = score
                best = f
            }
        }
        return best
    }

    /**
     * 一键导入：挑当前平台的最新构建 → 下载到缓存 → 交给 ExtManager 安装（仅放入扩展目录，
     * 等用户在扩展页确认权限清单后启用）。
     */
    @Throws(Exception::class)
    suspend fun downloadAndInstall(project: StoreProject, cacheDir: java.io.File) =
        withContext(Dispatchers.IO) {
            if (project.id == 0) throw IllegalStateException("项目无效")
            val files = fetchFiles(project.id)
            val pick = pickFile(files)
                ?: throw IllegalStateException("没有适配当前平台（android）的下载，请在下载站查看其它平台版本。")
            val getUrl = pick.optString("get_url", "")
            if (getUrl.isEmpty()) throw IllegalStateException("下载链接缺失。")
            val fullUrl = if (getUrl.startsWith("http", true)) getUrl
            else BASE_URL.trimEnd('/') + "/" + getUrl.trimStart('/')
            var fileName = pick.optString("file", "")
            if (fileName.isEmpty()) fileName = "store-${project.id}.tmlext"
            if (!fileName.endsWith(".tmlext", true) && !fileName.endsWith(".jar", true)) fileName += ".tmlext"
            val tmp = java.io.File(cacheDir, "TYWStore_" + System.nanoTime().toString(36) + "_" + fileName)
            if (!Server.download(fullUrl, tmp)) throw IllegalStateException("下载失败，请检查网络后重试。")
            try {
                if (!ExtManager.installPackage(tmp.absolutePath, true)) throw IllegalStateException("导入失败，详情见日志。")
            } finally {
                tmp.delete()
            }
        }

    // ================= HTTP（带 X-Api-Key） =================

    private fun get(path: String): String? {
        val url = if (path.startsWith("http", true)) path
        else BASE_URL.trimEnd('/') + "/" + path.trimStart('/')
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15000
            readTimeout = 15000
            requestMethod = "GET"
            setRequestProperty("User-Agent", "TYWLauncher-Android/${BuildConfig.VERSION_NAME_CN}")
            val k = apiKey
            if (k.isNotEmpty()) setRequestProperty("X-Api-Key", k)
        }
        return try {
            if (conn.responseCode !in 200..299) return null
            conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }
}
