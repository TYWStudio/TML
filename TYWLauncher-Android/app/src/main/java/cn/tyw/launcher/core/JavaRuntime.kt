package cn.tyw.launcher.core

import android.content.Context
import android.net.Uri
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import org.tukaani.xz.XZInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.util.concurrent.TimeUnit
import java.util.zip.GZIPInputStream
import java.util.zip.ZipInputStream

/**
 * Java 运行时管理。对应 Windows 版「设置 → 启动 → Java 列表」：
 *
 * · [scan] 列出本机所有可用运行时（启动器自带的 files/java/<名字>/bin/java、手动指定的、Termux 等外部位置）；
 * · [candidates] 给出 Java 8 / 11 / 16 / 17 / 21 / 25 的完整列表，已装的直接标出来，没装的可以下载或导入；
 * · [requiredFor] 按 Minecraft 版本 JSON 的 javaVersion.majorVersion（没有就按版本号 / 发布时间推）算出需要的 Java；
 * · [ensure] 一键补全时会先看有没有满足该版本要求的运行时，没有才去下载，避免装了个 Java 8 却启动 1.21。
 *
 * 下载源全部直连公网，不依赖服务端托管：与 FCL / Pojav 同源的 Android 原生 JRE（multiarch zip，
 * Java 8 / 17 / 21 / 25 各一份），查询 GitHub Releases 拿到带版本号的最新直链，查询失败时退回固定 tag 的
 * 直链；GitHub 直链不通时自动换公共镜像重试。服务端 config.json 的 Link.JavaAndroid 仍然支持，但只作为
 * 可选的补充源（默认没有配置就不参与）。
 *
 * 为什么不直接下 Azul Zulu：Azul 只发布 Linux(glibc) / Alpine(musl) / Windows / macOS 目标，没有
 * Android(bionic) 构建，`os=android` 查询会被忽略并返回 Linux 包，解压后 bin/java 依赖 ld-linux 无法执行。
 * Windows / Linux 版走 Mojang 官方 java-runtime，那边换成 Azul 才有意义。
 */
object JavaRuntime {
    private lateinit var app: Context

    /** Android 原生 JRE 的来源：与 FCL / Pojav 同源的按架构构建（tar.xz）。 */
    private const val ANDROID_JRE_REPO = "SouhardyaMaybe/EclipseJRE"
    private const val ANDROID_JRE_TAG = "jres-v1.0.0"
    private const val ANDROID_JRE_FALLBACK_DATE = "20261009"
    private val ANDROID_MAJORS = listOf(8, 17, 21, 25)

    /** GitHub 直链不通时的公共镜像前缀，按顺序重试。 */
    private val MIRRORS = listOf("https://ghfast.top/", "https://ghproxy.net/")

    fun init(context: Context) {
        app = context.applicationContext
    }

    val javaRoot: File get() = File(app.filesDir, "java")

    /** 可安装 / 可识别的大版本。16 是 1.17 的硬需求，25 是 2025 之后的新 LTS。 */
    val majors = listOf(8, 11, 16, 17, 21, 25)

    data class Runtime(
        val name: String,
        val major: Int,
        val version: String,
        val path: String,
        val managed: Boolean,
    ) {
        val display: String get() = if (version.isNotEmpty()) "Java $version" else if (major > 0) "Java $major" else "未知版本"
    }

    data class Result(val ok: Boolean, val path: String = "", val message: String = "")

    data class Candidate(
        val major: Int,
        val label: String,
        val source: String,
        val size: Long,
        val available: Boolean,
        val installed: Runtime?,
    ) {
        val isInstalled: Boolean get() = installed != null
        val sizeText: String get() = if (size > 0) "${size / 1048576} MB" else ""
    }

    /** 一个可下载的运行时包：直链 + 展示名 + 体积（体积未知时为 0）。 */
    data class Package(val label: String, val url: String, val size: Long = 0)

    // 版本号读取结果缓存，避免列表滚动时反复起进程
    private val versionCache = HashMap<String, String>()
    private val packageCache = HashMap<String, Package>()
    private var catalogFromApi = false
    private var catalogLoaded = false
    private var catalogAt = 0L

    // ================= 扫描 =================

    /** 列出所有可用运行时，managed 表示是不是启动器自己下载 / 解压到 files/java 里的。 */
    fun scan(): List<Runtime> {
        val out = mutableListOf<Runtime>()
        val seen = mutableSetOf<String>()

        fun add(name: String, path: String, managed: Boolean) {
            if (!seen.add(path)) return
            out += Runtime(name, majorOf(path), versionOf(path), path, managed)
        }

        // 设置里手动指定的
        Settings.javaPath.takeIf { it.isNotEmpty() }?.let { p ->
            if (File(p).exists()) add(File(p).parentFile?.parentFile?.name ?: "手动指定", p, false)
        }

        // 启动器目录：files/java/<任意名字>/...
        javaRoot.listFiles()?.filter { it.isDirectory }?.sortedBy { it.name }?.forEach { dir ->
            val bin = findJava(dir) ?: return@forEach
            bin.setExecutable(true)
            add(dir.name, bin.absolutePath, true)
        }

        // 外部常见位置（Termux / 系统自带 / 手动解压到 /data/local/tmp）
        externalPaths().forEach { p ->
            val f = File(p)
            if (f.canExecute()) add(f.parentFile?.parentFile?.name ?: "系统", p, false)
        }

        return out.sortedWith(compareBy({ it.major }, { it.name }))
    }

    /** 当前应该用的 Java：设置里指定的优先，否则挑满足 [required] 的最小版本，再不行就给最新的。 */
    fun pick(required: Int = 0): Runtime? {
        val list = scan()
        if (list.isEmpty()) return null
        Settings.javaPath.takeIf { it.isNotEmpty() }?.let { p ->
            list.firstOrNull { it.path == p }?.let { if (required <= 0 || it.major >= required) return it }
        }
        if (required > 0) {
            list.firstOrNull { it.major == required }?.let { return it }
            list.firstOrNull { it.major > required }?.let { return it }
        }
        return list.maxByOrNull { it.major }
    }

    fun detect(): String? = pick()?.path

    /** 已装运行时里有没有满足要求的大版本。 */
    fun has(major: Int): Boolean = scan().any { it.major == major }

    // ================= 版本需求 =================

    /**
     * Minecraft 版本需要哪个 Java。优先信版本 JSON 里 Mojang 自己写的 javaVersion.majorVersion，
     * 没有就按版本号推：1.20.5+ → 21、1.18~1.20.4 → 17、1.17.x → 16、更早 → 8；快照按发布时间估。
     */
    fun requiredFor(id: String, releaseTime: String = "", jsonMajor: Int = 0): Int {
        if (jsonMajor > 0) return jsonMajor
        val m = Regex("^1\\.(\\d+)(?:\\.(\\d+))?").find(id.trim())
        if (m != null) {
            val minor = m.groupValues[1].toInt()
            val patch = m.groupValues.getOrNull(2)?.toIntOrNull() ?: 0
            return when {
                minor > 20 -> 21
                minor == 20 && patch >= 5 -> 21
                minor >= 18 -> 17
                minor == 17 -> 16
                else -> 8
            }
        }
        // 快照 / 自定义版本：用发布时间兜底（1.20.5 = 2024-04，1.18 = 2021-11，1.17 = 2021-05）
        val t = releaseTime.replace("-", "").take(8)
        val month = t.take(6)
        return when {
            month >= "202404" -> 21
            month >= "202111" -> 17
            month >= "202105" -> 16
            month.isEmpty() -> 17
            else -> 8
        }
    }

    fun requirementText(required: Int): String = when {
        required <= 0 -> "未识别"
        required == 8 -> "Java 8"
        else -> "Java $required"
    }

    // ================= 列表 =================

    /** Java 8/11/16/17/21/25 全量列表，带上已装状态与在线下载信息。会联网查一次下载源（缓存 6 小时）。 */
    suspend fun catalog(): List<Candidate> = withContext(Dispatchers.IO) {
        ensureCatalog()
        val installed = scan()
        majors.map { major ->
            val pkg = onlinePackage(major)
            Candidate(
                major = major,
                label = labelOf(major),
                source = pkg?.label ?: "",
                size = pkg?.size ?: 0L,
                available = pkg != null,
                installed = installed.firstOrNull { it.major == major },
            )
        }
    }

    private fun labelOf(major: Int): String = "Java $major" + when (major) {
        8 -> "（1.16.5 及更早）"
        16 -> "（1.17.x）"
        17 -> "（1.18 ~ 1.20.4）"
        21 -> "（1.20.5 及更新）"
        25 -> "（实验性）"
        else -> ""
    }

    // ================= 安装 =================

    /** 下载并安装指定大版本。progress 0~1，status 给界面看的文字。 */
    suspend fun install(
        major: Int,
        progress: (Double) -> Unit = {},
        status: (String) -> Unit = {},
    ): Result = withContext(Dispatchers.IO) {
        val packages = resolve(major)
        if (packages.isEmpty()) {
            return@withContext Result(
                false, "",
                "Java $major 没有 Android 原生构建，请用「导入安装包」装自己的 JRE"
            )
        }
        var lastError = "自动补全失败"
        for ((index, pkg) in packages.withIndex()) {
            val sizeText = if (pkg.size > 0) "，约 ${pkg.size / 1048576} MB" else ""
            status("正在下载 ${pkg.label}$sizeText…")
            val archive = File(app.cacheDir, "java-$major-$index.pkg")
            archive.delete()
            if (!Server.download(pkg.url, archive) { progress(it * 0.9) }) {
                lastError = "下载失败：${pkg.label}"
                archive.delete()
                continue
            }
            try {
                status("解压 ${pkg.label}…")
                val dir = File(javaRoot, "jre-$major")
                dir.deleteRecursively()
                dir.mkdirs()
                extract(archive, dir)
                progress(0.95)
                val bin = findJava(dir)
                if (bin == null) {
                    lastError = "解压后没有找到 bin/java，包可能不是可用的运行时"
                    dir.deleteRecursively()
                    archive.delete()
                    continue
                }
                if (!hasVmLibrary(dir)) {
                    lastError = "这个包里没有 libjvm.so，不是 Android 原生运行时"
                    dir.deleteRecursively()
                    archive.delete()
                    continue
                }
                bin.setExecutable(true)
                versionCache.remove(bin.absolutePath)
                Settings.javaPath = bin.absolutePath
                progress(1.0)
                status("Java $major 已就绪")
                archive.delete()
                return@withContext Result(true, bin.absolutePath, "Java $major 安装完成（${pkg.label}）")
            } catch (e: Exception) {
                lastError = "解压失败：${e.message}"
                archive.delete()
            }
        }
        Result(false, "", lastError)
    }

    /** 从系统文件选择器导入一个 JRE 包（zip / tar.gz），自动识别大版本。 */
    suspend fun importArchive(uri: Uri, displayName: String = ""): Result = withContext(Dispatchers.IO) {
        val archive = File(app.cacheDir, "java-import.pkg")
        try {
            app.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(archive).use { output -> input.copyTo(output, 1 shl 20) }
            } ?: return@withContext Result(false, "", "读取安装包失败")
            val temp = File(javaRoot, "import-tmp")
            temp.deleteRecursively()
            temp.mkdirs()
            extract(archive, temp)
            val bin = findJava(temp) ?: run {
                temp.deleteRecursively()
                return@withContext Result(false, "", "包里没有 bin/java，可能不是可用作运行时的 JRE")
            }
            bin.setExecutable(true)
            val major = majorOf(bin.absolutePath)
            val dir = File(javaRoot, "jre-${if (major > 0) major else System.currentTimeMillis()}")
            dir.deleteRecursively()
            if (!temp.renameTo(dir)) {
                // 跨目录改名失败时退回复制
                temp.copyRecursively(dir, overwrite = true)
                temp.deleteRecursively()
            }
            val target = findJava(dir) ?: return@withContext Result(false, "", "安装后找不到 bin/java")
            target.setExecutable(true)
            versionCache.remove(target.absolutePath)
            Settings.javaPath = target.absolutePath
            Result(true, target.absolutePath, "已导入 ${displayName.ifEmpty { dir.name }}")
        } catch (e: Exception) {
            Result(false, "", "导入失败：${e.message}")
        } finally {
            archive.delete()
        }
    }

    /** 删除启动器托管的运行时；外部路径只从列表里移除（清空设置里的选择）。 */
    fun remove(runtime: Runtime): Boolean {
        if (!runtime.managed) {
            if (Settings.javaPath == runtime.path) Settings.javaPath = ""
            return true
        }
        val dir = File(runtime.path).parentFile?.parentFile ?: return false
        if (!dir.absolutePath.startsWith(javaRoot.absolutePath)) return false
        if (Settings.javaPath == runtime.path) Settings.javaPath = ""
        versionCache.remove(runtime.path)
        return dir.deleteRecursively()
    }

    fun select(runtime: Runtime) {
        Settings.javaPath = runtime.path
    }

    /**
     * 自动补全：先看已装的有没有满足 [required] 的，没有就装对应大版本。
     * required = 0 表示不关心版本，随便给一个能用的。
     */
    suspend fun ensure(
        required: Int = 0,
        progress: (Double) -> Unit = {},
        status: (String) -> Unit = {},
    ): Result = withContext(Dispatchers.IO) {
        val list = scan()
        if (required > 0) {
            list.firstOrNull { it.major == required }?.let {
                Settings.javaPath = it.path
                return@withContext Result(true, it.path, "已找到满足要求的 Java $required")
            }
        }
        list.firstOrNull()?.let { if (required <= 0 || it.major > required) {
            Settings.javaPath = it.path
            return@withContext Result(true, it.path, "已找到 Java")
        } }

        val major = when {
            required == 16 -> 17  // 1.17 要 Java 16，但没有 Android 原生 16，17 可以直接跑
            required > 0 -> required
            else -> 17
        }
        status("正在安装 Java $major…")
        install(major, progress, status)
    }

    // ================= 版本信息 =================

    fun versionOf(path: String): String {
        versionCache[path]?.let { return it }
        val home = File(path).parentFile?.parentFile
        var version = ""
        if (home != null) {
            val release = File(home, "release")
            if (release.isFile) {
                version = release.readLines().firstOrNull { it.startsWith("JAVA_VERSION=") }
                    ?.substringAfter('=')?.trim('"', ' ') ?: ""
            }
        }
        if (version.isEmpty()) version = execVersion(path)
        if (version.isNotEmpty()) versionCache[path] = version
        return version
    }

    /** `java -version` 兜底（没有 release 文件的老运行时）。最多等 2 秒，失败就当未知。 */
    private fun execVersion(path: String): String = try {
        val process = ProcessBuilder(path, "-version").redirectErrorStream(true).start()
        val text = process.inputStream.bufferedReader().use { it.readText() }
        process.waitFor(2, TimeUnit.SECONDS)
        Regex("version \"([^\"]+)\"").find(text)?.groupValues?.get(1) ?: ""
    } catch (_: Exception) {
        ""
    }

    fun majorOf(path: String): Int {
        val v = versionOf(path)
        if (v.isEmpty()) return 0
        val parts = v.split('.', '_', '-', '+')
        val first = parts.firstOrNull()?.toIntOrNull() ?: return 0
        return if (first == 1) parts.getOrNull(1)?.toIntOrNull() ?: 0 else first
    }

    // ================= 下载源 =================

    /** 一次安装要尝试的所有直链：官方直链 → 镜像 → 服务端自定义补充源。 */
    private fun resolve(major: Int): List<Package> {
        val out = mutableListOf<Package>()
        onlinePackage(major)?.let { pkg ->
            out += pkg
            MIRRORS.forEach { mirror -> out += Package(pkg.label, mirror + pkg.url, pkg.size) }
        }
        serverPackages(major).forEach { out += it }
        return out
    }

    /** 在线包：优先用 GitHub Releases 里带日期的最新直链，查不到就用固定 tag 的直链兜底。 */
    private fun onlinePackage(major: Int): Package? {
        ensureCatalog()
        packageCache["$major-$arch"]?.let { return it }
        if (catalogFromApi) return null
        if (major !in ANDROID_MAJORS) return null
        return Package(archLabel(major), androidJreUrl(major, ANDROID_JRE_FALLBACK_DATE))
    }

    private fun archLabel(major: Int): String = "Android JRE $major（$arch）"

    private fun androidJreUrl(major: Int, date: String): String =
        "https://github.com/$ANDROID_JRE_REPO/releases/download/$ANDROID_JRE_TAG/jre$major-$arch-$date-release.tar.xz"

    /**
     * 查 GitHub 上 Android 原生 JRE 的最新发布，按 (大版本, 架构) 记住直链与体积。
     * 6 小时内只查一次；查询失败时退回固定 tag 的直链。
     */
    private fun ensureCatalog(force: Boolean = false) {
        val fresh = System.currentTimeMillis() - catalogAt < 6 * 3600_000L
        if (!force && catalogLoaded && fresh) return
        catalogLoaded = true
        try {
            val raw = Server.get(
                "https://api.github.com/repos/$ANDROID_JRE_REPO/releases/latest", 12000
            ) ?: Server.get(
                "https://api.github.com/repos/$ANDROID_JRE_REPO/releases/tags/$ANDROID_JRE_TAG", 12000
            )
            if (raw != null) {
                val assets = JSONObject(raw).optJSONArray("assets")
                if (assets != null) {
                    for (i in 0 until assets.length()) {
                        val a = assets.optJSONObject(i) ?: continue
                        val m = Regex("^jre(\\d+)-(arm64|arm|x86_64|x86)-(\\d{8})-release\\.tar\\.xz$")
                            .find(a.optString("name")) ?: continue
                        val major = m.groupValues[1].toInt()
                        val targetArch = m.groupValues[2]
                        val url = a.optString("browser_download_url")
                        if (url.isEmpty()) continue
                        packageCache["$major-$targetArch"] = Package(
                            "Android JRE $major（$targetArch）", url, a.optLong("size", 0L)
                        )
                    }
                    catalogFromApi = packageCache.isNotEmpty()
                }
            }
        } catch (_: Exception) {
        }
        catalogAt = System.currentTimeMillis()
    }

    /** Android 的 JVM 是原生库，必须和设备的 CPU 架构对得上。 */
    private val arch: String
        get() = when (System.getProperty("os.arch") ?: "") {
            "aarch64", "arm64" -> "arm64"
            "armv7l", "arm", "armv7" -> "arm"
            "x86_64", "amd64" -> "x86_64"
            "x86", "i686", "i386" -> "x86"
            else -> "arm64"
        }

    /** 服务端 config.json 里可选的自定义源（Link.JavaAndroid），默认不参与。 */
    private fun serverPackages(major: Int): List<Package> {
        val out = mutableListOf<Package>()
        try {
            val raw = Server.get("config.json") ?: return out
            val link = JSONObject(raw).optJSONObject("Link") ?: return out
            link.optJSONArray("JavaAndroid")?.let { arr ->
                for (i in 0 until arr.length()) {
                    val o = arr.optJSONObject(i) ?: continue
                    if (o.optInt("major", 0) != major) continue
                    val url = o.optString("url")
                    if (url.isNotEmpty()) out += Package(o.optString("name", "自定义源"), url)
                }
            }
        } catch (_: Exception) {
        }
        return out
    }

    /** 装完的目录里得有 libjvm.so，否则只是空壳（比如误装的桌面 JDK）。 */
    private fun hasVmLibrary(dir: File): Boolean = findVmLibrary(dir, 0)

    private fun findVmLibrary(dir: File, depth: Int): Boolean {
        if (!dir.isDirectory || depth > 4) return false
        if (File(dir, "libjvm.so").isFile) return true
        dir.listFiles()?.forEach { if (findVmLibrary(it, depth + 1)) return true }
        return false
    }

    private fun externalPaths(): List<String> = listOf(
        "/data/data/com.termux/files/usr/bin/java",
        "/system/bin/java",
        "/system/xbin/java",
        "/data/local/tmp/java/bin/java",
    )

    private fun findJava(dir: File, depth: Int = 0): File? {
        if (!dir.isDirectory || depth > 3) return null
        val direct = File(dir, "bin/java")
        if (direct.exists()) return direct
        dir.listFiles()?.forEach { child ->
            findJava(child, depth + 1)?.let { return it }
        }
        return null
    }

    /** .zip / .tar.gz / .tar.xz 都能解，按文件头判断，不看扩展名。 */
    private fun extract(archive: File, dest: File) {
        val head = ByteArray(6)
        val n = FileInputStream(archive).use { it.read(head) }
        val isZip = n >= 2 && head[0] == 0x50.toByte() && head[1] == 0x4B.toByte()
        val isXz = n >= 6 && head[0] == 0xFD.toByte() && head[1] == 0x37.toByte() &&
            head[2] == 0x7A.toByte() && head[3] == 0x58.toByte() &&
            head[4] == 0x5A.toByte() && head[5] == 0x00.toByte()
        if (isZip) {
            unzip(archive, dest)
        } else if (isXz) {
            XZInputStream(FileInputStream(archive).buffered()).use { xz -> untar(xz, dest) }
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
            val size = if (sizeField.isEmpty()) 0L else sizeField.toLongOrNull(8) ?: 0L
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
            // 跳过填充到 512 的整数倍（用读+丢弃，InputStream.skip 可能只跳一点）
            val pad = (512 - (size % 512)) % 512
            var skipped = 0L
            val sink = ByteArray(8 * 1024)
            while (skipped < pad) {
                val n = input.read(sink, 0, minOf(sink.size.toLong(), pad - skipped).toInt())
                if (n <= 0) return
                skipped += n
            }
        }
    }
}
