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
 * 拓展商店：只读取下载站「拓展商店」一级分类下的项目，支持搜索、图标与一键导入。
 *
 * 密钥恒不进源码，由构建期注入 BuildConfig.STORE_API_KEY；运行时只在内存里使用，
 * 界面不显示（连掩码都不显示），绝不打印明文。
 */
object Store {
    private const val BASE_URL = "https://downloads.tywmc.cn/"
    private const val APP_SLUG = "tyw-launcher"

    /** 只读取这个一级分类；分类改名时仍按 slug / kind 识别。 */
    private const val CATEGORY_SLUG = "extensions"
    private const val CATEGORY_NAME = "拓展商店"
    private const val PAGE_SIZE = 100
    private const val MAX_PAGES = 20

    private val apiKey: String get() = BuildConfig.STORE_API_KEY.trim()

    /** 当前平台（与下载站 os 字段一致）。 */
    private val storeOs: String get() = "android"

    /** 当前 CPU 架构，归一到下载站的 arch 取值（x64 / arm64 / x86 / arm）。 */
    private val storeArch: String get() = normArch(Build.SUPPORTED_ABIS.firstOrNull() ?: "")

    /** 分支项目（下载站 depth=2 节点）。icon 已经是完整 URL。 */
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

    /** 只拉取「拓展商店」分类及其项目；分页读取，项目数量不限。 */
    @Throws(Exception::class)
    suspend fun fetchTree(): List<StoreCategory> = withContext(Dispatchers.IO) {
        val all = JSONArray()
        var page = 1
        while (page <= MAX_PAGES) {
            val url = BASE_URL + "api/nodes.php?app=" + URLEncoder.encode(APP_SLUG, "UTF-8") +
                "&depth=2&with=stats&limit=" + PAGE_SIZE + "&page=" + page
            val raw = get(url) ?: throw IllegalStateException("下载站无响应")
            val root = JSONObject(raw)
            val nodes = root.optJSONArray("nodes") ?: JSONArray()
            for (i in 0 until nodes.length()) {
                val n = nodes.optJSONObject(i) ?: continue
                all.put(n)
            }
            val pages = root.optInt("pages", 1)
            if (page >= pages || nodes.length() == 0) break
            page++
        }
        val list = mutableListOf<JSONObject>()
        for (i in 0 until all.length()) (all.optJSONObject(i) ?: continue).also { list.add(it) }

        val cats = mutableListOf<StoreCategory>()
        for (c in list) {
            if (c.optInt("depth", 0) != 1) continue
            val slug = c.optString("slug", "")
            val name = c.optString("name", "")
            val kind = c.optString("kind", "")
            if (!isStoreCategory(slug, name, kind)) continue
            val cat = StoreCategory(
                id = c.optInt("id", 0),
                name = name,
                tagline = c.optString("tagline", ""),
                description = c.optString("description", ""),
                icon = iconUrl(c.optString("icon", ""))
            )
            for (n in list) {
                if (n.optInt("depth", 0) == 2 && n.optInt("parent_id", 0) == cat.id) {
                    cat.projects.add(projectFrom(n))
                }
            }
            cats.add(cat)
        }
        cats
    }

    private fun isStoreCategory(slug: String, name: String, kind: String): Boolean =
        slug.equals(CATEGORY_SLUG, true) || name == CATEGORY_NAME || kind.equals(CATEGORY_SLUG, true)

    /** 下载站返回的 icon 是站内路径，这里补成完整 URL。 */
    private fun iconUrl(icon: String): String {
        if (icon.isBlank()) return ""
        if (icon.startsWith("http", true)) return icon
        return BASE_URL.trimEnd('/') + "/" + icon.trimStart('/')
    }

    private fun projectFrom(j: JSONObject): StoreProject {
        val stats = j.optJSONObject("stats")
        return StoreProject(
            id = j.optInt("id", 0),
            name = j.optString("name", ""),
            slug = j.optString("slug", ""),
            tagline = j.optString("tagline", ""),
            description = j.optString("description", ""),
            icon = iconUrl(j.optString("icon", "")),
            downloads = stats?.optInt("downloads", 0) ?: 0,
            files = stats?.optInt("files", 0) ?: 0,
            sizeHuman = stats?.optString("size_human", "") ?: "",
            latest = stats?.optString("latest", "") ?: ""
        )
    }

    /** 拉取某项目的下载文件清单（带 get_url）。 */
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

    /**
     * 自动分辨通用包与平台专用包，只认拓展包（.tmlext / .jar）：
     *   os/arch 为空或 any/all/universal = 通用包，所有平台都能装；
     *   明确标了其他平台/架构（含文件名带 -windows / -linux / arm64 等）的直接跳过；
     *   多个候选时优先精确匹配当前平台 + 架构的那个。
     */
    fun pickFile(files: List<JSONObject>): JSONObject? {
        var best: JSONObject? = null
        var bestScore = -1
        for (f in files) {
            val fname = f.optString("file", "").lowercase()
            if (!fname.endsWith(".tmlext") && !fname.endsWith(".jar")) continue
            val os = normOs(f.optString("os", ""))
            val arch = normArch(f.optString("arch", ""))
            val hintOs = normOs(osFromFile(fname))
            val hintArch = normArch(archFromFile(fname))

            if (!isAny(os) && os != storeOs) continue
            if (!isAny(arch) && storeArch.isNotEmpty() && arch != storeArch) continue
            if (isAny(os) && hintOs.isNotEmpty() && hintOs != storeOs) continue
            if (isAny(arch) && hintArch.isNotEmpty() && storeArch.isNotEmpty() && hintArch != storeArch) continue

            var score = 0
            if (os == storeOs || (isAny(os) && hintOs == storeOs)) score += 2
            if (arch == storeArch || (isAny(arch) && hintArch == storeArch)) score += 1
            if (score > bestScore) {
                bestScore = score
                best = f
            }
        }
        return best
    }

    private fun isAny(v: String): Boolean =
        v.isEmpty() || v == "any" || v == "all" || v == "universal" ||
            v == "common" || v == "通用" || v == "none" || v == "-"

    private fun normOs(v: String): String = when (v.trim().lowercase()) {
        "win", "win32", "win64", "windows" -> "windows"
        "mac", "macos", "macosx", "osx", "darwin" -> "macos"
        "linux" -> "linux"
        "android" -> "android"
        "ios", "iphone", "ipad" -> "ios"
        else -> v.trim().lowercase()
    }

    private fun normArch(v: String): String = when (v.trim().lowercase()) {
        "x86_64", "amd64", "x64", "64bit", "64" -> "x64"
        "aarch64", "arm64", "arm64-v8a", "armv8" -> "arm64"
        "x86", "i386", "i686", "32bit", "32" -> "x86"
        "armeabi-v7a", "armeabi", "armv7", "arm" -> "arm"
        else -> v.trim().lowercase()
    }

    private fun osFromFile(name: String): String = when {
        name.contains("android") -> "android"
        name.contains("windows") || name.contains("win32") || name.contains("-win") -> "windows"
        name.contains("macos") || name.contains("darwin") || name.contains("-mac") -> "macos"
        name.contains("linux") -> "linux"
        else -> ""
    }

    private fun archFromFile(name: String): String = when {
        name.contains("aarch64") || name.contains("arm64") -> "arm64"
        name.contains("x86_64") || name.contains("amd64") || name.contains("-x64") -> "x64"
        name.contains("armeabi") || name.contains("armv7") -> "arm"
        name.contains("-x86") || name.contains("i686") -> "x86"
        else -> ""
    }

    /** 一键导入：挑当前平台适配的包 → 下载到缓存 → 交给 ExtManager 安装。 */
    @Throws(Exception::class)
    suspend fun downloadAndInstall(project: StoreProject, cacheDir: java.io.File) =
        withContext(Dispatchers.IO) {
            if (project.id == 0) throw IllegalStateException("项目无效")
            val files = fetchFiles(project.id)
            val pick = pickFile(files)
                ?: throw IllegalStateException("没有适配当前平台的拓展包，请在下载站查看其它平台版本。")
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
