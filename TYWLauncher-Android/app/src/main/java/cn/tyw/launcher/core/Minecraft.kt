package cn.tyw.launcher.core

import android.content.Context
import org.json.JSONObject
import java.io.File

/** 一个已安装的 Minecraft 版本（对应 Windows 版 McInstance 的极简形态）。 */
data class McVersion(
    val name: String,
    val type: String,
    val id: String,
    val dir: File,
) {
    val typeDisplay: String
        get() = when (type.lowercase()) {
            "release" -> "正式版"
            "snapshot" -> "快照版"
            "old_beta" -> "远古 Beta"
            "old_alpha" -> "远古 Alpha"
            else -> if (type.isBlank()) "未知" else type
        }
}

/**
 * 游戏目录与版本识别。
 * Android 上游戏目录放在应用外部存储（/storage/emulated/0/Android/data/cn.tyw.launcher/files/.minecraft），
 * 与 FCL 的默认布局一致：versions/<版本名>/<版本名>.json。
 */
object Minecraft {
    fun gameDir(context: Context): File =
        File(context.getExternalFilesDir(null) ?: context.filesDir, ".minecraft")

    fun versionsDir(context: Context): File = File(gameDir(context), "versions")

    fun scan(context: Context): List<McVersion> {
        val dir = versionsDir(context)
        if (!dir.isDirectory) return emptyList()
        val list = mutableListOf<McVersion>()
        dir.listFiles()?.forEach { vdir ->
            if (!vdir.isDirectory) return@forEach
            val json = File(vdir, "${vdir.name}.json")
            if (!json.isFile) return@forEach
            var id = vdir.name
            var type = ""
            try {
                val obj = JSONObject(json.readText())
                id = obj.optString("id", vdir.name)
                type = obj.optString("type", "")
            } catch (_: Exception) {
            }
            list.add(McVersion(vdir.name, type, id, vdir))
        }
        return list.sortedByDescending { it.dir.lastModified() }
    }

    /** 是否已安装（用于下载页显示「已安装」）。 */
    fun isInstalled(context: Context, name: String): Boolean =
        File(versionsDir(context), name).isDirectory

    /**
     * 生成 Java 启动参数（不含 java 可执行文件本身）。
     * 运行时（Pojav / FCL 的 JRE + LWJGL 适配层）接入后，直接把返回值交给它即可。
     */
    fun buildLaunchArgs(version: McVersion, javaDir: File, playerName: String, maxMemoryMb: Int): List<String> {
        val natives = File(version.dir, "natives-android")
        val classpath = buildString {
            append(File(version.dir, "${version.id}.jar").absolutePath)
            val libs = File(version.dir, "libraries")
            if (libs.isDirectory) {
                libs.walkTopDown().filter { it.isFile && it.name.endsWith(".jar") }.forEach {
                    append(File.pathSeparator).append(it.absolutePath)
                }
            }
        }
        return listOf(
            "-Xmx${maxMemoryMb}M",
            "-Djava.library.path=${natives.absolutePath}",
            "-Dorg.lwjgl.librarypath=${natives.absolutePath}",
            "-Dminecraft.launcher.brand=TYWLauncher",
            "-Dminecraft.launcher.version=${AppInfo.versionName}",
            "-cp", classpath,
            "net.minecraft.client.main.Main",
            "--username", playerName,
            "--version", version.id,
            "--gameDir", version.dir.parentFile?.absolutePath ?: version.dir.absolutePath,
            "--assetsDir", File(version.dir.parentFile, "assets").absolutePath,
            "--assetIndex", version.id,
            "--uuid", "00000000000000000000000000000000",
            "--accessToken", "0",
            "--userType", "legacy",
            "--versionType", "TYW",
        )
    }
}
