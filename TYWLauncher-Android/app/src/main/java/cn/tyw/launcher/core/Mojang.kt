package cn.tyw.launcher.core

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File

/**
 * 原版游戏清单与安装。对应 Windows 版 PageDownload 的原版游戏部分：
 * 拉 Mojang 的 version_manifest_v2.json → 下载版本 JSON → 客户端 jar → libraries。
 * （assets 体积大且在 Android 上由运行时侧统一处理，这里只落版本本体。）
 */
object Mojang {
    private const val MANIFEST = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"

    data class RemoteVersion(val id: String, val type: String, val url: String, val releaseTime: String)

    /** 最近一次拉取到的最新版本号（正式版 / 快照版）。 */
    @Volatile var latestReleaseId: String = ""
    @Volatile var latestSnapshotId: String = ""
    @Volatile var latestReleaseUrl: String = ""
    @Volatile var latestSnapshotUrl: String = ""

    /** 拉清单，拿到最新正式版 / 快照版的版本号与 JSON 地址。 */
    suspend fun loadManifest(): Boolean = withContext(Dispatchers.IO) {
        try {
            val raw = Server.get(MANIFEST) ?: return@withContext false
            val root = JSONObject(raw)
            val latest = root.optJSONObject("latest")
            latestReleaseId = latest?.optString("release", "") ?: ""
            latestSnapshotId = latest?.optString("snapshot", "") ?: ""
            val arr = root.optJSONArray("versions") ?: return@withContext false
            for (i in 0 until arr.length()) {
                val v = arr.getJSONObject(i)
                val id = v.optString("id", "")
                if (id == latestReleaseId) latestReleaseUrl = v.optString("url", "")
                if (id == latestSnapshotId) latestSnapshotUrl = v.optString("url", "")
            }
            latestReleaseId.isNotEmpty() && latestReleaseUrl.isNotEmpty()
        } catch (_: Exception) {
            false
        }
    }

    /**
     * 安装一个版本：版本 JSON + 客户端 jar + libraries。
     * progress(0~1, 阶段文本)，返回是否成功。
     */
    suspend fun install(
        context: Context,
        versionName: String,
        versionJsonUrl: String,
        progress: (Double, String) -> Unit,
    ): Boolean = withContext(Dispatchers.IO) {
        val versionsDir = Minecraft.versionsDir(context)
        val target = File(versionsDir, versionName)
        target.mkdirs()
        val jsonFile = File(target, "$versionName.json")

        try {
            progress(0.02, "正在获取版本信息……")
            val jsonRaw = Server.get(versionJsonUrl) ?: throw IllegalStateException("下载版本 JSON 失败")
            jsonFile.writeText(jsonRaw)

            val root = JSONObject(jsonRaw)
            val clientUrl = root.optJSONObject("downloads")?.optJSONObject("client")?.optString("url") ?: ""
            if (clientUrl.isNotEmpty()) {
                progress(0.1, "正在下载客户端 jar……")
                if (!Server.download(clientUrl, File(target, "$versionName.jar")) { p ->
                        progress(0.1 + p * 0.45, "正在下载客户端 jar… ${(p * 100).toInt()}%")
                    }) throw IllegalStateException("下载客户端 jar 失败")
            }

            val libs = root.optJSONArray("libraries") ?: org.json.JSONArray()
            val tasks = mutableListOf<Pair<String, File>>()
            for (i in 0 until libs.length()) {
                val lib = libs.getJSONObject(i)
                val artifact = lib.optJSONObject("downloads")?.optJSONObject("artifact") ?: continue
                val path = artifact.optString("path", "")
                val url = artifact.optString("url", "")
                if (path.isEmpty() || url.isEmpty()) continue
                tasks.add(url to File(target, "libraries/$path"))
            }

            tasks.forEachIndexed { index, (url, dest) ->
                if (dest.isFile && dest.length() > 0) return@forEachIndexed
                val done = index.toDouble() / tasks.size.coerceAtLeast(1)
                progress(0.55 + done * 0.44, "正在下载依赖 ${index + 1}/${tasks.size}……")
                Server.download(url, dest)
            }

            progress(1.0, "安装完成")
            true
        } catch (e: Exception) {
            // 失败就清理本次残留（与 Windows / Linux 版一致）
            runCatching { target.deleteRecursively() }
            false
        }
    }
}
