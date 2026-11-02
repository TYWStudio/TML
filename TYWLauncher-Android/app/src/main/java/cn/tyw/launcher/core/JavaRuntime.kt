package cn.tyw.launcher.core

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.util.zip.GZIPInputStream
import java.util.zip.ZipInputStream

/**
 * Java 运行时「自动补全」。对应 FCL 的「自动安装 Java」思路：
 *
 * 1. 先看设置里手动指定的 java，再扫启动器自带的运行时目录（files/java/<版本>/bin/java）；
 * 2. 都没有时，按服务端 config.json 的 Link.JavaDownloadsAndroid 列表（没配就用内置的几个地址）
 *    下载对应架构的运行时，解压到 files/java，chmod +x，写回设置；
 * 3. 解压支持 .zip 与 .tar.gz，下载失败会换下一个源，全部失败才报错。
 *
 * 只负责「把能用的 java 准备好」，真正拉起 JVM 的渲染/适配层由启动流程消费这个路径。
 */
object JavaRuntime {
    private lateinit var app: Context

    fun init(context: Context) {
        app = context.applicationContext
    }

    val javaRoot: File get() = File(app.filesDir, "java")

    data class Result(val ok: Boolean, val path: String = "", val message: String = "")

    /** 扫描可用的 java 可执行文件。 */
    fun detect(): String? {
        val manual = Settings.javaPath
        if (manual.isNotEmpty() && File(manual).canExecute()) return manual

        // 启动器自解压的运行时：files/java/<任意版本>/bin/java
        val dirs = javaRoot.listFiles()?.sortedByDescending { it.name }.orEmpty()
        for (d in dirs) {
            val bin = File(d, "bin/java")
            if (bin.exists()) {
                bin.setExecutable(true)
                return bin.absolutePath
            }
        }
        // 常见外部位置（Termux / 系统镜像 / 用户手动放的）
        val extra = listOf(
            "/data/data/com.termux/files/usr/bin/java",
            "/system/bin/java",
            "/system/xbin/java",
            "/data/local/tmp/java/bin/java",
        )
        return extra.firstOrNull { File(it).canExecute() }
    }

    /** 扫到的运行时版本（跑 java -version 太重，这里读 release 文件，够判断大版本）。 */
    fun versionOf(path: String): String {
        val home = File(path).parentFile?.parentFile ?: return ""
        val release = File(home, "release")
        if (release.exists()) {
            release.readLines().firstOrNull { it.startsWith("JAVA_VERSION=") }
                ?.substringAfter('=')
                ?.trim('"')
                ?.let { return it }
        }
        return ""
    }

    /** 一键补全：有就用，没有就下载。progress 回调 0~1，status 回调给界面看的文字。 */
    suspend fun ensure(
        progress: (Double) -> Unit = {},
        status: (String) -> Unit = {},
    ): Result = withContext(Dispatchers.IO) {
        detect()?.let { return@withContext Result(true, it, "已找到 Java") }

        val urls = downloadUrls()
        if (urls.isEmpty()) return@withContext Result(false, "", "没有配置 Java 下载源")
        status("开始下载 Java 运行时…")

        var lastError = ""
        for ((index, url) in urls.withIndex()) {
            status("下载 Java 运行时（源 ${index + 1}/${urls.size}）…")
            val target = File(javaRoot, "jre-${index + 1}")
            target.deleteRecursively()
            target.mkdirs()
            val archive = File(app.cacheDir, "java-rt-${index + 1}.pkg")
            if (!Server.download(url, archive) { progress(it * 0.85) }) {
                lastError = "下载失败：$url"
                archive.delete()
                continue
            }
            try {
                status("解压 Java 运行时…")
                extract(archive, target)
                progress(0.95)
                val bin = findJava(target)
                if (bin == null) {
                    lastError = "解压后没有找到 bin/java"
                    target.deleteRecursively()
                    archive.delete()
                    continue
                }
                bin.setExecutable(true)
                Settings.javaPath = bin.absolutePath
                progress(1.0)
                status("Java 已就绪：${versionOf(bin.absolutePath)}")
                archive.delete()
                return@withContext Result(true, bin.absolutePath, "自动补全完成")
            } catch (e: Exception) {
                lastError = "解压失败：${e.message}"
                target.deleteRecursively()
                archive.delete()
            }
        }
        Result(false, "", lastError.ifEmpty { "自动补全失败" })
    }

    /** 服务端可下发 Link.JavaDownloadsAndroid；没配就用内置地址。 */
    private fun downloadUrls(): List<String> {
        val list = mutableListOf<String>()
        try {
            val raw = Server.get("config.json")
            if (raw != null) {
                val link = JSONObject(raw).optJSONObject("Link")
                val arr = link?.optJSONArray("JavaDownloadsAndroid")
                if (arr != null) {
                    for (i in 0 until arr.length()) {
                        val s = arr.optString(i)
                        if (s.isNotEmpty()) list += s.replace("{arch}", arch)
                    }
                }
            }
        } catch (_: Exception) {
        }
        if (list.isEmpty()) {
            // 默认：服务端 www 下的通用包（管理员把 JRE 包丢上去即可生效）
            list += AppInfo.SERVER_ADDRESS.trimEnd('/') + "/java-android-$arch.zip"
        }
        return list
    }

    /** Android 的 JVM 要的是本机架构对应的运行时。 */
    private val arch: String
        get() = when (System.getProperty("os.arch") ?: "") {
            "aarch64", "arm64" -> "arm64"
            "armv7l", "arm" -> "arm32"
            "x86_64", "amd64" -> "x64"
            else -> "arm64"
        }

    private fun findJava(dir: File): File? {
        val direct = File(dir, "bin/java")
        if (direct.exists()) return direct
        return dir.listFiles()?.firstNotNullOfOrNull { findJava(it) }
    }

    /** .zip 与 .tar.gz 都能解，按文件头判断，不看扩展名。 */
    private fun extract(archive: File, dest: File) {
        val head = ByteArray(2)
        FileInputStream(archive).use { it.read(head) }
        if (head[0] == 0x50.toByte() && head[1] == 0x4B.toByte()) {
            unzip(archive, dest)
        } else {
            untargz(archive, dest)
        }
    }

    private fun unzip(archive: File, dest: File) {
        ZipInputStream(FileInputStream(archive).buffered()).use { zis ->
            while (true) {
                val entry = zis.nextEntry ?: break
                val out = File(dest, entry.name)
                if (entry.isDirectory) {
                    out.mkdirs()
                } else {
                    out.parentFile?.mkdirs()
                    FileOutputStream(out).use { fos -> zis.copyTo(fos) }
                    if (entry.name.endsWith("/bin/java")) out.setExecutable(true)
                }
                zis.closeEntry()
            }
        }
    }

    private fun untargz(archive: File, dest: File) {
        GZIPInputStream(FileInputStream(archive).buffered()).use { gz -> untar(gz, dest) }
    }

    /** 极简 tar 解析：只处理普通文件与目录，够解 JRE 包了。 */
    private fun untar(input: InputStream, dest: File) {
        val header = ByteArray(512)
        while (true) {
            var read = 0
            while (read < 512) {
                val n = input.read(header, read, 512 - read)
                if (n <= 0) return
                read += n
            }
            if (header.all { it.toInt() == 0 }) return

            val name = String(header, 0, 100, Charsets.UTF_8).trimEnd('\u0000')
            val sizeField = String(header, 124, 12, Charsets.US_ASCII).trim('\u0000', ' ')
            val size = if (sizeField.isEmpty()) 0L else sizeField.toLong(8)
            val type = header[156].toInt().toChar()

            val out = File(dest, name.removePrefix("./"))
            when (type) {
                '5' -> out.mkdirs()
                '0', '\u0000' -> {
                    out.parentFile?.mkdirs()
                    FileOutputStream(out).use { fos ->
                        var left = size
                        val buf = ByteArray(64 * 1024)
                        while (left > 0) {
                            val n = input.read(buf, 0, minOf(buf.size.toLong(), left).toInt())
                            if (n <= 0) break
                            fos.write(buf, 0, n)
                            left -= n
                        }
                    }
                    if (name.endsWith("/bin/java")) out.setExecutable(true)
                }
            }
            // 跳过填充到 512 的整数倍
            val pad = (512 - (size % 512)) % 512
            var skipped = 0L
            while (skipped < pad) {
                val n = input.skip(pad - skipped)
                if (n <= 0) break
                skipped += n
            }
        }
    }
}
