@file:Suppress("unused")

package cn.tyw.launcher.core.ext

import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Collections
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import java.util.zip.ZipFile

/**
 * TML Extension API v1 —— Android 宿主层。
 *
 * Android 平台安全限制（有意为之）：
 * - 不使用 DexClassLoader / PathClassLoader / 反射，不引入任何脚本引擎；
 * - 不执行应用私有目录里的二进制，不把 jar / dex 加载进宿主进程；
 * - 扩展只以独立子进程形式存在，宿主与其仅通过 stdio 的 NDJSON 通信；
 * - 协议里根本不存在敏感能力，子进程只拿到最小环境变量白名单；
 * - 默认全部禁用，必须由用户确认权限清单后显式启用，运行时缺失时不下载、不安装。
 *
 * 全部进程 IO（启动、stdin 写入、stdout/stderr 读取、进程销毁）都在后台线程完成，
 * 调用方主线程只会做入队与短临界区读，不会被子进程阻塞。
 */

/** 扩展状态：默认禁用、已启用、校验或运行异常。 */
enum class ExtState { DISABLED, ENABLED, ERROR }

/**
 * 扩展的只读快照。字段类型按四端契约固定为 String / List<String> / ExtState / Boolean。
 */
data class ExtInfo(
    val id: String = "",
    val name: String = "",
    val version: String = "",
    val author: String = "",
    val description: String = "",
    val runtime: String = "",
    val format: String = "",
    val filePath: String = "",
    val sha256: String = "",
    val platforms: List<String> = emptyList(),
    val permissions: List<String> = emptyList(),
    val subscribe: List<String> = emptyList(),
    val state: ExtState = ExtState.DISABLED,
    val errorMessage: String = "",
    val needsRuntime: Boolean = false
)

/** 单个扩展的进程句柄与运行时状态，全部字段线程安全。 */
private class ExtProcess(
    val id: String,
    val process: Process,
    val writer: BufferedWriter,
    val privateDir: File,
    val permissions: Set<String>,
    val declaredSubscriptions: Set<String>
) {
    val writeQueue = LinkedBlockingQueue<String>()
    val writeLock = Any()
    val alive = AtomicBoolean(true)
    val ready = AtomicBoolean(false)
    val pending = ConcurrentHashMap<Int, PendingRequest>()
    val lastActivity = AtomicLong(System.currentTimeMillis())
    val processingDeadline = AtomicLong(0L)
    val logLimiter = RateLimiter(20, 1000L)
    val toastLimiter = RateLimiter(5, 1000L)
    val subscriptions: MutableSet<String> =
        Collections.newSetFromMap(ConcurrentHashMap<String, Boolean>())

    fun enqueue(line: String) {
        writeQueue.offer(line)
    }
}

/** 宿主主动发起的请求，等待扩展的响应。 */
private class PendingRequest(val deadlineMs: Long) {
    val latch = CountDownLatch(1)
    @Volatile
    var response: JSONObject? = null
}

/** 简单的时间窗限频器。 */
private class RateLimiter(private val max: Int, private val windowMs: Long) {
    private val lock = Any()
    private var windowStart = 0L
    private var count = 0

    fun allow(): Boolean = synchronized(lock) {
        val now = System.currentTimeMillis()
        if (now - windowStart > windowMs) {
            windowStart = now
            count = 0
        }
        if (count >= max) false else {
            count++
            true
        }
    }
}

/** 清单校验通过后额外保存的入口信息（ExtInfo 契约里不含 entry 字段）。 */
private class ExtMeta(val entry: String)

/**
 * Android 宿主实现。四端暴露同一组方法：Configure / Scan / GetAll / Enable /
 * Disable / IsEnabled / Broadcast / ShutdownAll。
 */
object ExtManager {

    // ---- 协议与配额常量 ----
    private const val API_VERSION = 1
    private const val PLATFORM = "android"
    private const val MAX_LINE_BYTES = 64 * 1024
    private const val MSG_LIMIT = 4 * 1024
    private const val TOAST_LIMIT = 300
    private const val STORAGE_VALUE_LIMIT = 64 * 1024
    private const val SETTINGS_VALUE_LIMIT = 8 * 1024
    private const val MAX_SINGLE_FILE = 2L * 1024 * 1024
    private const val MAX_TOTAL_UNCOMPRESSED = 8L * 1024 * 1024
    private const val MAX_STORAGE_TOTAL = 1024L * 1024
    private const val MAX_FILE_COUNT = 64
    private const val MAX_PROCESSES = 8
    private const val HANDSHAKE_TIMEOUT_MS = 10_000L
    private const val REQUEST_TIMEOUT_MS = 5_000L
    private const val KEY_MAX_LEN = 128
    private const val HEX = "0123456789abcdef"
    private const val SENTINEL = "\u0000__EXT_SHUTDOWN__"
    private const val SAFE_PATH = "/system/bin:/system/xbin:/vendor/bin:/product/bin"

    // ---- 错误码 ----
    private const val ERR_DENIED = -32001
    private const val ERR_INVALID_PARAMS = -32600
    private const val ERR_METHOD_NOT_FOUND = -32601
    private const val ERR_QUOTA = -32002
    private const val ERR_INTERNAL = -32000

    // ---- 白名单 ----
    private val CAPABILITIES = setOf(
        "log", "toast", "host.info", "instances.list", "events", "storage", "settings"
    )
    private val EVENTS = setOf(
        "launcher.started", "launcher.shutdown", "game.launching", "game.launched",
        "game.exited", "instance.listChanged", "settings.changed"
    )
    private val PLATFORMS = setOf("windows", "linux", "macos", "android")
    private val RUNTIMES = setOf("python3", "node", "java")

    /** 硬编码方法黑名单：协议里不存在，收到一律拒绝并记日志。 */
    private val DENIED_METHODS = setOf(
        "file.read", "file.write", "exec", "spawn", "shell", "system", "http.fetch",
        "socket", "net", "request", "download", "upload", "credentials", "token",
        "account", "auth", "device", "code", "registry", "env", "clipboard",
        "screenshot", "update", "install", "launch.game", "settings.global",
        "reflect", "jni", "classloader", "extension.data"
    )

    /** 方法 -> 所需权限，声明了也不代表一定给。 */
    private val METHOD_PERMISSION = mapOf(
        "log" to "log",
        "toast" to "toast",
        "host.info" to "host.info",
        "instances.list" to "instances.list",
        "event.subscribe" to "events",
        "storage.get" to "storage",
        "storage.set" to "storage",
        "settings.get" to "settings",
        "settings.set" to "settings"
    )

    /** 子进程必须显式清除的注入类环境变量。 */
    private val BLOCKED_ENV = arrayOf(
        "NODE_OPTIONS", "NODE_PATH", "PYTHONPATH", "PYTHONSTARTUP", "PYTHONHOME",
        "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "CLASSPATH", "JAVA_HOME",
        "LD_PRELOAD", "LD_LIBRARY_PATH", "BOOTCLASSPATH", "IFS", "CDPATH",
        "BASH_ENV", "ENV", "ANDROID_ROOT"
    )

    private val HOST_INFO_KEYS = listOf(
        "launcherVersion", "channel", "language", "apiVersion", "platform", "arch"
    )
    private val SENSITIVE_KEYS = listOf(
        "token", "password", "secret", "credential", "account", "device", "server",
        "path", "cmd", "url", "env", "address", "code", "key", "session"
    )

    private val ID_RE = Regex("^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$")
    private val KEY_RE = Regex("^[A-Za-z0-9._-]+$")
    private val ENTRY_CHARS = Regex("^[A-Za-z0-9._/-]+$")

    // ---- 注入的宿主回调 ----
    @Volatile
    private var logFn: (String) -> Unit = {}
    @Volatile
    private var toastFn: (String) -> Unit = {}
    @Volatile
    private var hostInfoProvider: () -> JSONObject = { JSONObject() }
    @Volatile
    private var instancesProvider: () -> JSONArray = { JSONArray() }
    @Volatile
    private var settingsGet: (String, String?) -> String? = { _, _ -> null }
    @Volatile
    private var settingsSet: (String, String) -> Unit = { _, _ -> }
    @Volatile
    private var rootDirProvider: () -> File = { File(".") }

    // ---- 内部状态 ----
    private val lock = Any()
    private val extensions = LinkedHashMap<String, ExtInfo>()
    private val metas = ConcurrentHashMap<String, ExtMeta>()
    private val processes = ConcurrentHashMap<String, ExtProcess>()
    private val secureRandom = SecureRandom()

    init {
        startWatchdog()
    }

    // ===================== 公开接口 =====================

    /**
     * 注入宿主能力。参数依次为：写日志、显示提示、宿主信息、实例列表、
     * 设置读、设置写、扩展根目录。核心实现不依赖任何 UI 框架。
     */
    fun configure(
        log: (String) -> Unit,
        toast: (String) -> Unit,
        hostInfo: () -> JSONObject,
        instances: () -> JSONArray,
        getSetting: (String, String?) -> String?,
        setSetting: (String, String) -> Unit,
        extensionRoot: () -> File
    ) {
        logFn = log
        toastFn = toast
        hostInfoProvider = hostInfo
        instancesProvider = instances
        settingsGet = getSetting
        settingsSet = setSetting
        rootDirProvider = extensionRoot
    }

    /**
     * 扫描扩展根目录下的 .tmlext / .jar，校验清单并返回扩展列表。
     * 不启动任何进程；所有扩展默认禁用；包 SHA-256 变化则移除信任并自动禁用。
     */
    fun scan(): List<ExtInfo> {
        val root = rootDirProvider()
        val files = root.listFiles()
            ?.filter { it.isFile && (it.name.endsWith(".tmlext", true) || it.name.endsWith(".jar", true)) }
            ?.sortedBy { it.name }
            ?: emptyList()
        val changed = mutableListOf<String>()
        val snapshot: List<ExtInfo>
        synchronized(lock) {
            extensions.clear()
            metas.clear()
            val trusted = readTrusted()
            val currentById = HashMap<String, String>()
            for (f in files) {
                val (info, entry) = parseOne(f, root)
                if (entry != null) {
                    metas[info.id] = ExtMeta(entry)
                    currentById[info.id] = info.sha256
                }
                extensions[info.id] = info
            }
            // 包变化检测：先收集再删除，避免迭代时修改 JSONObject 导致并发修改异常
            val keys = trusted.keys()
            while (keys.hasNext()) {
                val tid = keys.next()
                val recorded = trusted.optString(tid, "")
                val current = currentById[tid]
                if (current == null || current != recorded) changed.add(tid)
            }
            for (tid in changed) {
                trusted.remove(tid)
                val cur = extensions[tid]
                if (cur != null) {
                    extensions[tid] = cur.copy(errorMessage = "包内容已变化，已自动禁用，需重新确认")
                }
            }
            if (changed.isNotEmpty()) writeTrustedObject(trusted)
            snapshot = extensions.values.toList()
        }
        for (tid in changed) disable(tid)
        return snapshot
    }


    /**
     * 从本地临时文件安装拓展包：复制到扩展根目录 → 重新扫描 → 默认放入扩展目录（禁用状态），
     * 等用户在拓展页确认权限清单后启用。userConfirmed=true 时顺带尝试启用。
     */
    fun installPackage(localPath: String, userConfirmed: Boolean = true): Boolean {
        val src = File(localPath)
        if (!src.isFile) { logFn("安装扩展失败：源文件不存在 $localPath"); return false }
        val root = rootDirProvider()
        if (!root.exists()) root.mkdirs()
        val dest = File(root, src.name)
        try {
            src.inputStream().use { ins -> dest.outputStream().use { outs -> ins.copyTo(outs) } }
        } catch (e: Exception) {
            logFn("安装扩展失败：复制出错 ${e.message}")
            return false
        }
        val list = scan()
        val id = list.firstOrNull { f -> File(f.filePath).name.equals(dest.name, true) }?.id
        if (id == null) { logFn("安装扩展失败：扫描未找到刚复制的包 ${dest.absolutePath}"); return false }
        if (!userConfirmed) {
            logFn("扩展已放入扩展目录，等待用户在拓展页启用：$id")
            return true
        }
        val enabled = enable(id, true)
        if (!enabled) logFn("扩展已放入扩展目录（$id），但自动启用失败（可能缺少运行时），可在拓展页手动启用。")
        return true
    }

    /** 返回当前已发现扩展的快照。 */
    fun getAll(): List<ExtInfo> = synchronized(lock) { extensions.values.toList() }

    /**
     * 用户看过权限清单并确认后启用。userConfirmed 为 false 一律拒绝。
     * 启用前探测系统 PATH 中的解释器，找不到则标记「缺少运行时」并禁用，不下载不安装。
     * 启用时把包 SHA-256 写入 trusted.json；进程启动与握手在后台线程完成。
     */
    fun enable(id: String, userConfirmed: Boolean): Boolean {
        if (!userConfirmed) return false
        val info = synchronized(lock) { extensions[id] } ?: return false
        val meta = metas[id] ?: return false // 清单校验未通过的不允许启用
        val bin = findRuntime(info.runtime)
        if (bin == null) {
            updateInfo(id) {
                it.copy(
                    state = ExtState.ERROR,
                    errorMessage = "缺少运行时: ${info.runtime}",
                    needsRuntime = true
                )
            }
            logFn("扩展 $id 缺少运行时 ${info.runtime}，未启用")
            return false
        }
        // 复核包哈希，防止扫描到启用之间包被替换
        val file = File(info.filePath)
        val currentHash = try {
            sha256(file)
        } catch (e: Exception) {
            ""
        }
        if (!file.isFile || currentHash != info.sha256) {
            updateInfo(id) {
                it.copy(state = ExtState.ERROR, errorMessage = "包内容已变化，请重新扫描")
            }
            return false
        }
        val existing = processes[id]
        if (existing != null && existing.alive.get()) {
            writeTrusted(id, info.sha256)
            updateInfo(id) { it.copy(state = ExtState.ENABLED, errorMessage = "", needsRuntime = false) }
            return true
        }
        if (processes.size >= MAX_PROCESSES) {
            logFn("扩展进程数已达上限 $MAX_PROCESSES，无法启用 $id")
            return false
        }
        writeTrusted(id, info.sha256)
        updateInfo(id) { it.copy(state = ExtState.ENABLED, errorMessage = "", needsRuntime = false) }
        val perms = info.permissions.toSet()
        val subs = info.subscribe.toSet()
        Thread {
            startProcess(info, meta, bin, perms, subs)
        }.apply {
            isDaemon = true
            name = "ext-start-$id"
            start()
        }
        return true
    }

    /** 结束进程并禁用，保留私有数据。 */
    fun disable(id: String) {
        val proc = processes[id]
        if (proc != null) stopProcess(proc, "用户禁用", true, null)
        updateInfo(id) { it.copy(state = ExtState.DISABLED, needsRuntime = false) }
    }

    /** 查询是否处于启用状态。 */
    fun isEnabled(id: String): Boolean =
        synchronized(lock) { extensions[id]?.state == ExtState.ENABLED }

    /**
     * 向已启用且已订阅该事件的扩展广播。只接受事件白名单，data 一律脱敏；
     * settings.changed 仅投递给自身命名空间。
     */
    fun broadcast(type: String, data: JSONObject) {
        if (type !in EVENTS) {
            logFn("忽略非白名单事件: $type")
            return
        }
        val line = JSONObject()
            .put("method", "event")
            .put("params", JSONObject().put("type", type).put("data", sanitize(data)))
            .toString()
        for (proc in processes.values) {
            if (!proc.alive.get() || !proc.ready.get()) continue
            if (!proc.subscriptions.contains(type)) continue
            if (!isEnabled(proc.id)) continue
            if (type == "settings.changed") {
                val ns = data.optString("namespace", "")
                if (ns != "ext.${proc.id}." && ns != "ext.${proc.id}") continue
            }
            proc.enqueue(line)
        }
    }

    /** 启动器退出时结束所有扩展进程。 */
    fun shutdownAll() {
        for (proc in processes.values.toList()) {
            stopProcess(proc, "启动器退出", true, null)
        }
        synchronized(lock) {
            for ((k, v) in extensions) {
                extensions[k] = v.copy(state = ExtState.DISABLED)
            }
        }
    }

    // ===================== 清单解析与校验 =====================

    /** 解析单个包。返回 (ExtInfo, entry)；entry 为 null 表示校验失败、不加载。 */
    private fun parseOne(file: File, root: File): Pair<ExtInfo, String?> {
        val isJar = file.name.endsWith(".jar", true)
        val ext = if (isJar) "jar" else "tmlext"
        val fallbackId = file.nameWithoutExtension
            .replace(Regex("[^a-z0-9._-]"), "_")
            .take(64)
            .ifEmpty { "unknown" }

        fun fail(msg: String): Pair<ExtInfo, String?> = Pair(
            ExtInfo(
                id = fallbackId,
                name = file.name,
                filePath = file.absolutePath,
                format = ext,
                state = ExtState.ERROR,
                errorMessage = msg
            ),
            null
        )

        val hash = try {
            sha256(file)
        } catch (e: Exception) {
            return fail("无法计算 SHA-256: ${e.message}")
        }
        var zip: ZipFile? = null
        try {
            zip = ZipFile(file)
            val z = zip
            // ZIP Slip 校验用虚拟目标目录，不要求目录已存在
            val zipErr = validateZip(z, File(root, "data/$fallbackId/pkg"))
            if (zipErr != null) return fail(zipErr)
            val manifestName = if (isJar) "META-INF/tml-extension.json" else "manifest.json"
            val mEntry = z.getEntry(manifestName) ?: return fail("缺少清单: $manifestName")
            val bytes = z.getInputStream(mEntry).use { it.readBytes() }
            val json = try {
                JSONObject(String(bytes, Charsets.UTF_8))
            } catch (e: Exception) {
                return fail("清单不是合法 JSON")
            }

            if (json.optInt("apiVersion", 0) != API_VERSION) return fail("apiVersion 必须为 1")
            val id = json.optString("id", "")
            if (!ID_RE.matches(id)) return fail("id 不合法")
            val format = json.optString("format", "")
            if (format != ext) return fail("format 与包类型不符")
            val runtime = json.optString("runtime", "")
            if (runtime !in RUNTIMES) return fail("runtime 不合法")
            if (isJar && runtime != "java") return fail("jar 的 runtime 必须为 java")
            if (!isJar && runtime != "python3" && runtime != "node") {
                return fail("tmlext 的 runtime 必须为 python3 / node")
            }
            val entry = json.optString("entry", "")
            if (!validEntry(entry, ext)) return fail("entry 不合法")
            val name = json.optString("name", "")
            if (name.isEmpty()) return fail("name 为空")
            val version = json.optString("version", "")
            if (version.isEmpty()) return fail("version 为空")

            val platforms = toStringList(json.optJSONArray("platforms"))
            if (platforms.isEmpty() ||
                platforms.any { it !in PLATFORMS } ||
                !platforms.contains(PLATFORM) ||
                platforms.distinct().size != platforms.size
            ) {
                return fail("platforms 不合法或不含 android")
            }
            val permissions = toStringList(json.optJSONArray("permissions"))
            if (permissions.isEmpty() || permissions.size > 7 ||
                permissions.any { it !in CAPABILITIES } ||
                permissions.distinct().size != permissions.size
            ) {
                return fail("permissions 不合法")
            }
            val subscribe = toStringList(json.optJSONArray("subscribe"))
            if (subscribe.size > 7 || subscribe.any { it !in EVENTS } ||
                subscribe.distinct().size != subscribe.size
            ) {
                return fail("subscribe 不合法")
            }
            if (!isJar && z.getEntry(entry) == null) return fail("entry 指向的文件不存在")

            val info = ExtInfo(
                id = id,
                name = name,
                version = version,
                author = json.optString("author", ""),
                description = json.optString("description", ""),
                runtime = runtime,
                format = format,
                filePath = file.absolutePath,
                sha256 = hash,
                platforms = platforms,
                permissions = permissions,
                subscribe = subscribe,
                state = ExtState.DISABLED,
                errorMessage = "",
                needsRuntime = false
            )
            return Pair(info, entry)
        } catch (e: Exception) {
            return fail("解析失败: ${e.message}")
        } finally {
            try {
                zip?.close()
            } catch (_: Exception) {
            }
        }
    }

    /** 校验 ZIP 结构：单文件、总数、文件数、ZIP Slip、非法条目名。 */
    private fun validateZip(zip: ZipFile, slipBase: File): String? {
        val root = slipBase.canonicalPath
        var total = 0L
        var count = 0
        val entries = zip.entries()
        while (entries.hasMoreElements()) {
            val e = entries.nextElement()
            if (e.isDirectory) continue
            count++
            if (count > MAX_FILE_COUNT) return "文件数超过 $MAX_FILE_COUNT"
            val size = e.size
            if (size > MAX_SINGLE_FILE) return "单文件超过 2MiB: ${e.name}"
            total += maxOf(0L, size)
            if (total > MAX_TOTAL_UNCOMPRESSED) return "解压总量超过 8MiB"
            val name = e.name
            if (name.contains("..") || name.startsWith("/") ||
                name.contains("\\") || name.contains(":")
            ) {
                return "非法条目名: $name"
            }
            val target = File(slipBase, name)
            val targetPath = target.canonicalPath
            if (targetPath != root && !targetPath.startsWith(root + File.separator)) {
                return "ZIP Slip 防护: $name"
            }
        }
        return null
    }

    /** entry 校验：不含 .. / \ / : / 开头 /，长度与字符集，按格式细分。 */
    private fun validEntry(entry: String, ext: String): Boolean {
        if (entry.isEmpty() || entry.length > 200) return false
        if (entry.contains("..") || entry.contains("\\") ||
            entry.contains(":") || entry.startsWith("/")
        ) {
            return false
        }
        if (ext == "tmlext") {
            if (!ENTRY_CHARS.matches(entry)) return false
            return entry.endsWith(".py") || entry.endsWith(".js") ||
                entry.endsWith(".mjs") || entry.endsWith(".cjs")
        }
        return validClassName(entry)
    }

    /** Java 主类全名：至少两段，每段都是合法 Java 标识符。 */
    private fun validClassName(s: String): Boolean {
        val parts = s.split('.')
        if (parts.size < 2) return false
        return parts.all { p ->
            p.isNotEmpty() && p[0].isJavaIdentifierStart() &&
                p.all { it.isJavaIdentifierPart() }
        }
    }

    private fun toStringList(arr: JSONArray?): List<String> {
        if (arr == null) return emptyList()
        val out = ArrayList<String>(arr.length())
        for (i in 0 until arr.length()) out.add(arr.optString(i, ""))
        return out
    }

    // ===================== 信任与运行时 =====================

    private fun trustedFile(): File = File(rootDirProvider(), "trusted.json")

    private fun readTrusted(): JSONObject {
        val f = trustedFile()
        if (!f.isFile) return JSONObject()
        return try {
            JSONObject(f.readText(Charsets.UTF_8))
        } catch (e: Exception) {
            JSONObject()
        }
    }

    private fun writeTrustedObject(obj: JSONObject) {
        val f = trustedFile()
        try {
            f.parentFile?.mkdirs()
            val tmp = File(f.parentFile, "trusted.json.tmp")
            tmp.writeText(obj.toString(), Charsets.UTF_8)
            if (f.exists()) f.delete()
            if (!tmp.renameTo(f)) tmp.delete()
        } catch (e: Exception) {
            logFn("写入 trusted.json 失败: ${e.message}")
        }
    }

    private fun writeTrusted(id: String, hash: String) {
        val obj = readTrusted()
        obj.put(id, hash)
        writeTrustedObject(obj)
    }

    /** 探测系统 PATH 中的解释器；Android 默认找不到，宿主不下载不安装。 */
    private fun findRuntime(runtime: String): String? {
        val name = when (runtime) {
            "python3" -> "python3"
            "node" -> "node"
            "java" -> "java"
            else -> return null
        }
        for (dir in listOf("/system/bin", "/system/xbin", "/vendor/bin", "/product/bin")) {
            val f = File(dir, name)
            if (f.canExecute()) return f.absolutePath
        }
        val path = System.getenv("PATH") ?: ""
        for (dir in path.split(File.pathSeparator)) {
            if (dir.isEmpty()) continue
            val f = File(dir, name)
            if (f.canExecute()) return f.absolutePath
        }
        return null
    }

    private fun sha256(file: File): String {
        val md = MessageDigest.getInstance("SHA-256")
        FileInputStream(file).use { fis ->
            val buf = ByteArray(16384)
            var n: Int
            while (fis.read(buf).also { n = it } >= 0) {
                md.update(buf, 0, n)
            }
        }
        val sb = StringBuilder()
        for (b in md.digest()) {
            val v = b.toInt() and 0xff
            sb.append(HEX[v ushr 4])
            sb.append(HEX[v and 15])
        }
        return sb.toString()
    }

    private fun newNonce(): String {
        val b = ByteArray(16)
        secureRandom.nextBytes(b)
        val sb = StringBuilder()
        for (x in b) {
            val v = x.toInt() and 0xff
            sb.append(HEX[v ushr 4])
            sb.append(HEX[v and 15])
        }
        return sb.toString()
    }

    // ===================== 子进程与协议 =====================

    /**
     * 启动扩展子进程并完成握手。仅在后台线程调用。
     * 命令：python3 <entry> / node <entry> / java -cp <jar> <entry>；
     * .tmlext 先解压到私有 pkg/；jar 不加载进宿主进程，只作为独立 JVM 的 classpath。
     */
    private fun startProcess(
        info: ExtInfo,
        meta: ExtMeta,
        bin: String,
        perms: Set<String>,
        subs: Set<String>
    ) {
        val id = info.id
        val privateDir = File(File(rootDirProvider(), "data"), id)
        val pkgDir = File(privateDir, "pkg")
        val tmpDir = File(privateDir, "tmp")
        try {
            privateDir.mkdirs()
            tmpDir.mkdirs()
            val cmd = ArrayList<String>()
            if (info.format == "tmlext") {
                val extractErr = extractPackage(File(info.filePath), pkgDir)
                if (extractErr != null) {
                    markDisabled(id, "解压失败: $extractErr")
                    return
                }
                val entryFile = File(pkgDir, meta.entry)
                if (!entryFile.canonicalPath.startsWith(pkgDir.canonicalPath + File.separator)) {
                    markDisabled(id, "entry 越界")
                    return
                }
                cmd.add(bin)
                cmd.add(entryFile.absolutePath)
            } else {
                // 只把 jar 交给独立 JVM，绝不加载进宿主进程、不做反射与 dex 加载
                cmd.add(bin)
                cmd.add("-cp")
                cmd.add(File(info.filePath).absolutePath)
                cmd.add(meta.entry)
            }

            val pb = ProcessBuilder(cmd)
            pb.directory(privateDir)
            val env = pb.environment()
            env.clear()
            // 显式移除可注入变量（clear 后本已为空，这里明确表达意图）
            for (k in BLOCKED_ENV) env.remove(k)
            env["PATH"] = SAFE_PATH
            env["TMPDIR"] = tmpDir.absolutePath
            env["LANG"] = "C.UTF-8"
            env["PYTHONIOENCODING"] = "utf-8"
            pb.redirectErrorStream(false)
            val process = pb.start()

            val writer = BufferedWriter(OutputStreamWriter(process.outputStream, Charsets.UTF_8))
            val stdout = BufferedReader(InputStreamReader(process.inputStream, Charsets.UTF_8))
            val stderr = BufferedReader(InputStreamReader(process.errorStream, Charsets.UTF_8))
            val proc = ExtProcess(id, process, writer, privateDir, perms, subs)
            processes[id] = proc
            startPumpThreads(proc, stdout, stderr)

            val resp = sendRequest(proc, 1, "initialize", buildInitParams(id, privateDir, perms), HANDSHAKE_TIMEOUT_MS)
            val ready = resp?.optJSONObject("result")?.optBoolean("ready", false) == true
            if (!ready) {
                stopProcess(proc, "握手失败或超时", false, "握手失败或超时")
                return
            }
            proc.ready.set(true)
            proc.subscriptions.addAll(subs)
            logFn("扩展 $id 已就绪")
        } catch (e: Exception) {
            val proc = processes[id]
            if (proc != null) {
                stopProcess(proc, "启动失败", false, "启动失败: ${e.message}")
            } else {
                markDisabled(id, "启动失败: ${e.message}")
            }
        }
    }

    /** 解压 .tmlext 到私有 pkg/，逐条再做一次 ZIP Slip 与大小校验。 */
    private fun extractPackage(file: File, pkgDir: File): String? {
        if (!pkgDir.exists() && !pkgDir.mkdirs()) return "无法创建 pkg 目录"
        val zip = try {
            ZipFile(file)
        } catch (e: Exception) {
            return "无法打开包: ${e.message}"
        }
        try {
            val baseErr = validateZip(zip, pkgDir)
            if (baseErr != null) return baseErr
            val root = pkgDir.canonicalPath
            val buf = ByteArray(8192)
            val entries = zip.entries()
            while (entries.hasMoreElements()) {
                val e = entries.nextElement()
                val name = e.name
                if (e.isDirectory) {
                    File(pkgDir, name).mkdirs()
                    continue
                }
                if (name.contains("..") || name.startsWith("/") ||
                    name.contains("\\") || name.contains(":")
                ) {
                    return "非法条目名: $name"
                }
                val target = File(pkgDir, name)
                if (!target.canonicalPath.startsWith(root + File.separator)) {
                    return "ZIP Slip 防护: $name"
                }
                val parent = target.parentFile
                if (parent != null && !parent.exists() && !parent.mkdirs()) {
                    return "无法创建子目录: $name"
                }
                zip.getInputStream(e).use { input ->
                    FileOutputStream(target).use { out ->
                        var written = 0L
                        while (true) {
                            val n = input.read(buf)
                            if (n < 0) break
                            out.write(buf, 0, n)
                            written += n
                            if (written > MAX_SINGLE_FILE) return "单文件超过 2MiB: $name"
                        }
                    }
                }
            }
            return null
        } catch (e: Exception) {
            return "解压异常: ${e.message}"
        } finally {
            try {
                zip.close()
            } catch (_: Exception) {
            }
        }
    }

    private fun buildInitParams(id: String, privateDir: File, perms: Set<String>): JSONObject {
        val host = try {
            hostInfoProvider()
        } catch (e: Exception) {
            JSONObject()
        }
        val p = JSONObject()
        p.put("apiVersion", API_VERSION)
        p.put("extId", id)
        p.put("platform", PLATFORM)
        p.put("launcherVersion", host.optString("launcherVersion", ""))
        p.put("channel", host.optString("channel", "Release"))
        p.put("language", host.optString("language", Locale.getDefault().toLanguageTag()))
        p.put("privateDir", privateDir.absolutePath)
        val arr = JSONArray()
        for (perm in perms) arr.put(perm)
        p.put("permissions", arr)
        p.put("nonce", newNonce())
        return p
    }

    /** 启动写线程、stdout 读线程、stderr 读线程，全部为后台线程。 */
    private fun startPumpThreads(proc: ExtProcess, stdout: BufferedReader, stderr: BufferedReader) {
        // 写线程：所有 stdin 写入都经由无界队列在这里完成，调用方只入队
        Thread {
            try {
                while (true) {
                    val line = proc.writeQueue.poll(200, TimeUnit.MILLISECONDS) ?: continue
                    if (line == SENTINEL) break
                    synchronized(proc.writeLock) {
                        proc.writer.write(line)
                        proc.writer.newLine()
                        proc.writer.flush()
                    }
                }
            } catch (e: Exception) {
                logFn("扩展 ${proc.id} 写入异常: ${e.message}")
            } finally {
                try {
                    proc.writer.close()
                } catch (_: Exception) {
                }
            }
        }.apply {
            isDaemon = true
            name = "ext-write-${proc.id}"
            start()
        }

        // stdout 读线程：逐行解析 NDJSON 并分发
        Thread {
            try {
                while (proc.alive.get()) {
                    val line = stdout.readLine() ?: break
                    proc.lastActivity.set(System.currentTimeMillis())
                    if (line.toByteArray(Charsets.UTF_8).size > MAX_LINE_BYTES) {
                        stopProcess(proc, "单行超过 64KiB", false, "协议错误：单行超过 64KiB")
                        break
                    }
                    if (line.isEmpty()) continue
                    handleLine(proc, line)
                }
            } catch (e: Exception) {
                if (proc.alive.get()) logFn("扩展 ${proc.id} stdout 异常: ${e.message}")
            }
            if (proc.alive.get()) {
                stopProcess(proc, "进程输出结束", false, "进程异常退出")
            }
        }.apply {
            isDaemon = true
            name = "ext-stdout-${proc.id}"
            start()
        }

        // stderr 读线程：收集进宿主日志
        Thread {
            try {
                while (proc.alive.get()) {
                    val line = stderr.readLine() ?: break
                    if (line.isNotEmpty()) logFn("[ext:${proc.id}][stderr] ${line.take(512)}")
                }
            } catch (_: Exception) {
            }
        }.apply {
            isDaemon = true
            name = "ext-stderr-${proc.id}"
            start()
        }
    }

    /** 宿主主动请求并等待响应；超时返回 null。 */
    private fun sendRequest(
        proc: ExtProcess,
        id: Int,
        method: String,
        params: JSONObject,
        timeoutMs: Long
    ): JSONObject? {
        if (!proc.alive.get()) return null
        val req = PendingRequest(System.currentTimeMillis() + timeoutMs)
        proc.pending[id] = req
        val line = JSONObject()
            .put("id", id)
            .put("method", method)
            .put("params", params)
            .toString()
        proc.enqueue(line)
        val ok = try {
            req.latch.await(timeoutMs, TimeUnit.MILLISECONDS)
        } catch (e: InterruptedException) {
            false
        }
        proc.pending.remove(id)
        return if (ok && proc.alive.get()) req.response else null
    }

    /** 分发扩展发来的单行消息。 */
    private fun handleLine(proc: ExtProcess, line: String) {
        val obj = try {
            JSONObject(line)
        } catch (e: Exception) {
            return // 宿主忽略格式错误的行
        }
        val idVal = obj.opt("id")
        if (idVal != null && (obj.has("result") || obj.has("error"))) {
            val key = when (idVal) {
                is Number -> idVal.toInt()
                is String -> idVal.toIntOrNull()
                else -> null
            }
            if (key != null) {
                proc.pending[key]?.let {
                    it.response = obj
                    it.latch.countDown()
                }
            }
            return
        }
        val method = obj.optString("method", "")
        if (idVal != null && method.isNotEmpty()) {
            if (!proc.ready.get()) {
                stopProcess(proc, "握手前收到请求", false, "协议错误：握手未完成")
                return
            }
            // 单次请求 5s 看门狗
            proc.processingDeadline.set(System.currentTimeMillis() + REQUEST_TIMEOUT_MS)
            try {
                val params = obj.optJSONObject("params") ?: JSONObject()
                proc.enqueue(handleRequest(proc, method, params, idVal))
            } catch (e: Exception) {
                proc.enqueue(errorReply(idVal, ERR_INTERNAL, "宿主内部错误: ${e.message}"))
            } finally {
                proc.processingDeadline.set(0L)
            }
            return
        }
        // 无 id 的其它消息（例如扩展误发的 event）一律忽略
    }

    /** 处理扩展调用，返回要写回的响应行。 */
    private fun handleRequest(
        proc: ExtProcess,
        method: String,
        params: JSONObject,
        idVal: Any?
    ): String {
        if (method in DENIED_METHODS || method.startsWith("network.")) {
            logFn("扩展 ${proc.id} 调用被禁止的方法: $method")
            return errorReply(idVal, ERR_DENIED, "permission denied: $method")
        }
        val need = METHOD_PERMISSION[method]
            ?: return errorReply(idVal, ERR_METHOD_NOT_FOUND, "method not found: $method")
        if (need !in proc.permissions) {
            return errorReply(idVal, ERR_DENIED, "permission denied: $method")
        }
        return when (method) {
            "log" -> handleLog(proc, params, idVal)
            "toast" -> handleToast(proc, params, idVal)
            "host.info" -> resultReply(idVal, buildHostInfo())
            "instances.list" -> resultReply(
                idVal, JSONObject().put("instances", cleanInstances())
            )
            "event.subscribe" -> handleSubscribe(proc, params, idVal)
            "storage.get" -> handleStorageGet(proc, params, idVal)
            "storage.set" -> handleStorageSet(proc, params, idVal)
            "settings.get" -> handleSettingsGet(proc, params, idVal)
            "settings.set" -> handleSettingsSet(proc, params, idVal)
            else -> errorReply(idVal, ERR_METHOD_NOT_FOUND, "method not found: $method")
        }
    }

    private fun resultReply(id: Any?, result: JSONObject): String {
        val v = id ?: JSONObject.NULL
        return JSONObject().put("id", v).put("result", result).toString()
    }

    private fun errorReply(id: Any?, code: Int, message: String): String {
        val v = id ?: JSONObject.NULL
        return JSONObject()
            .put("id", v)
            .put("error", JSONObject().put("code", code).put("message", message))
            .toString()
    }

    // ---- 各能力实现 ----

    private fun handleLog(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val message = params.optString("message", "")
        if (message.toByteArray(Charsets.UTF_8).size > MSG_LIMIT) {
            return errorReply(idVal, ERR_INVALID_PARAMS, "message 超过 4KiB")
        }
        if (!proc.logLimiter.allow()) {
            return errorReply(idVal, ERR_QUOTA, "log 限频")
        }
        val raw = params.optString("level", "info")
        val level = if (raw == "warn" || raw == "error") raw else "info"
        logFn("[ext:${proc.id}][$level] $message")
        return resultReply(idVal, JSONObject())
    }

    private fun handleToast(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val message = params.optString("message", "")
        if (message.length > TOAST_LIMIT) {
            return errorReply(idVal, ERR_INVALID_PARAMS, "toast 超过 300 字")
        }
        val bad = message.any { c ->
            c == '<' || c == '>' || c == '\n' || c == '\r' || c == '`' || c == '$'
        }
        if (bad || message.contains("http://") || message.contains("https://")) {
            return errorReply(idVal, ERR_INVALID_PARAMS, "toast 不接受 HTML / 链接 / 命令")
        }
        if (!proc.toastLimiter.allow()) {
            return errorReply(idVal, ERR_QUOTA, "toast 限频")
        }
        toastFn(message)
        return resultReply(idVal, JSONObject())
    }

    /** host.info：只读平台信息；带 Android Build 四项，不含设备码 / 账号 / token / 服务端地址。 */
    private fun buildHostInfo(): JSONObject {
        val raw = try {
            hostInfoProvider()
        } catch (e: Exception) {
            JSONObject()
        }
        val info = JSONObject()
        for (k in HOST_INFO_KEYS) {
            if (raw.has(k)) info.put(k, raw.get(k))
        }
        info.put("apiVersion", API_VERSION)
        info.put("platform", PLATFORM)
        info.put("osRelease", Build.VERSION.RELEASE)
        info.put("manufacturer", Build.MANUFACTURER)
        info.put("model", Build.MODEL)
        val abis = JSONArray()
        for (abi in Build.SUPPORTED_ABIS) abis.put(abi)
        info.put("supportedAbis", abis)
        return info
    }

    /** instances.list：只保留 id / name / gameVersion / loader，其余字段一律脱敏。 */
    private fun cleanInstances(): JSONArray {
        val raw = try {
            instancesProvider()
        } catch (e: Exception) {
            JSONArray()
        }
        val out = JSONArray()
        for (i in 0 until raw.length()) {
            val o = raw.optJSONObject(i) ?: continue
            val c = JSONObject()
            c.put("id", o.optString("id", ""))
            c.put("name", o.optString("name", ""))
            c.put("gameVersion", o.optString("gameVersion", ""))
            c.put("loader", o.optString("loader", ""))
            out.put(c)
        }
        return out
    }

    private fun handleSubscribe(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val arr = params.optJSONArray("events")
        val wanted = if (arr != null) toStringList(arr) else proc.declaredSubscriptions.toList()
        val accepted = wanted.filter { it in EVENTS && it in proc.declaredSubscriptions }.toSet()
        proc.subscriptions.clear()
        proc.subscriptions.addAll(accepted)
        val out = JSONArray()
        for (e in accepted) out.put(e)
        return resultReply(idVal, JSONObject().put("subscribed", out))
    }

    private fun checkKey(key: String): Boolean =
        key.isNotEmpty() && key.length <= KEY_MAX_LEN && KEY_RE.matches(key)

    private fun storageDir(proc: ExtProcess): File =
        File(proc.privateDir, "storage").apply { if (!exists()) mkdirs() }

    /** canonicalFile + startsWith，确保 storage 路径落在扩展私有目录内。 */
    private fun resolveStorage(proc: ExtProcess, key: String): File? {
        val dir = storageDir(proc)
        val target = File(dir, key)
        val root = dir.canonicalFile.path
        val path = target.canonicalFile.path
        if (path != root && !path.startsWith(root + File.separator)) return null
        return target
    }

    private fun storageTotal(dir: File): Long {
        var total = 0L
        dir.listFiles()?.forEach { f -> if (f.isFile) total += f.length() }
        return total
    }

    private fun handleStorageGet(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val key = params.optString("key", "")
        if (!checkKey(key)) return errorReply(idVal, ERR_INVALID_PARAMS, "key 不合法")
        val f = resolveStorage(proc, key)
            ?: return errorReply(idVal, ERR_DENIED, "storage 路径越界")
        val value: Any = if (f.isFile) {
            try {
                f.readText(Charsets.UTF_8)
            } catch (e: Exception) {
                JSONObject.NULL
            }
        } else {
            JSONObject.NULL
        }
        return resultReply(idVal, JSONObject().put("value", value))
    }

    private fun handleStorageSet(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val key = params.optString("key", "")
        if (!checkKey(key)) return errorReply(idVal, ERR_INVALID_PARAMS, "key 不合法")
        val value = params.optString("value", "")
        val bytes = value.toByteArray(Charsets.UTF_8)
        if (bytes.size > STORAGE_VALUE_LIMIT) {
            return errorReply(idVal, ERR_INVALID_PARAMS, "storage value 超过 64KiB")
        }
        val f = resolveStorage(proc, key)
            ?: return errorReply(idVal, ERR_DENIED, "storage 路径越界")
        val dir = f.parentFile
            ?: return errorReply(idVal, ERR_INTERNAL, "storage 目录缺失")
        val old = if (f.isFile) f.length() else 0L
        if (storageTotal(dir) - old + bytes.size > MAX_STORAGE_TOTAL) {
            return errorReply(idVal, ERR_QUOTA, "storage 总量超过 1MiB")
        }
        // 临时文件 + renameTo 原子替换
        val tmp = File(dir, "$key.tmp")
        try {
            tmp.writeText(value, Charsets.UTF_8)
            if (f.exists() && !f.delete()) return errorReply(idVal, ERR_INTERNAL, "无法删除旧值")
            if (!tmp.renameTo(f)) {
                tmp.delete()
                return errorReply(idVal, ERR_INTERNAL, "原子替换失败")
            }
        } catch (e: Exception) {
            tmp.delete()
            return errorReply(idVal, ERR_INTERNAL, "写入失败: ${e.message}")
        }
        return resultReply(idVal, JSONObject())
    }

    private fun settingsKey(proc: ExtProcess, key: String): String = "ext.${proc.id}.$key"

    private fun handleSettingsGet(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val key = params.optString("key", "")
        if (!checkKey(key)) return errorReply(idVal, ERR_INVALID_PARAMS, "key 不合法")
        val value = try {
            settingsGet(settingsKey(proc, key), null)
        } catch (e: Exception) {
            null
        }
        return resultReply(idVal, JSONObject().put("value", value ?: JSONObject.NULL))
    }

    private fun handleSettingsSet(proc: ExtProcess, params: JSONObject, idVal: Any?): String {
        val key = params.optString("key", "")
        if (!checkKey(key)) return errorReply(idVal, ERR_INVALID_PARAMS, "key 不合法")
        val value = params.optString("value", "")
        if (value.toByteArray(Charsets.UTF_8).size > SETTINGS_VALUE_LIMIT) {
            return errorReply(idVal, ERR_INVALID_PARAMS, "settings value 超过 8KiB")
        }
        return try {
            settingsSet(settingsKey(proc, key), value)
            resultReply(idVal, JSONObject())
        } catch (e: Exception) {
            errorReply(idVal, ERR_INTERNAL, "设置写入失败: ${e.message}")
        }
    }

    /** 事件 data 脱敏：只保留简单类型，剔除敏感键与路径 / 链接 / 命令行等字符串。 */
    private fun sanitize(value: Any?): Any = when (value) {
        null -> JSONObject.NULL
        is String -> if (looksSensitive(value)) "" else value
        is Boolean, is Number -> value
        is JSONObject -> {
            val out = JSONObject()
            val keys = value.keys()
            while (keys.hasNext()) {
                val k = keys.next()
                if (SENSITIVE_KEYS.any { k.lowercase(Locale.ROOT).contains(it) }) continue
                out.put(k, sanitize(value.opt(k)))
            }
            out
        }
        is JSONArray -> {
            val out = JSONArray()
            for (i in 0 until value.length()) out.put(sanitize(value.opt(i)))
            out
        }
        else -> JSONObject.NULL
    }

    private fun looksSensitive(s: String): Boolean =
        s.contains('/') || s.contains('\\') || s.contains("://") || s.contains('@') ||
            s.contains('\n') || s.contains('\r') ||
            SENSITIVE_KEYS.any { s.lowercase(Locale.ROOT).contains(it) }

    // ===================== 状态与进程管理 =====================

    private fun updateInfo(id: String, transform: (ExtInfo) -> ExtInfo) {
        synchronized(lock) {
            val cur = extensions[id] ?: return
            extensions[id] = transform(cur)
        }
    }

    private fun markDisabled(id: String, reason: String) {
        logFn("扩展 $id 已禁用: $reason")
        updateInfo(id) { it.copy(state = ExtState.DISABLED, errorMessage = reason) }
    }

    /**
     * 结束进程并自动禁用。sendShutdown 为真时先投递 {"method":"shutdown"}；
     * errorMessage 非 null 时写入错误原因，否则保留原有 errorMessage。
     */
    private fun stopProcess(
        proc: ExtProcess,
        logReason: String,
        sendShutdown: Boolean,
        errorMessage: String?
    ) {
        if (!proc.alive.compareAndSet(true, false)) return
        logFn("扩展 ${proc.id} 结束: $logReason")
        if (sendShutdown) {
            proc.enqueue(JSONObject().put("method", "shutdown").toString())
        }
        proc.enqueue(SENTINEL)
        // 进程销毁放到后台线程，留出 150ms 让 shutdown 行写出
        Thread {
            try {
                Thread.sleep(150)
                proc.process.destroy()
                Thread.sleep(300)
                if (proc.process.isAlive) proc.process.destroyForcibly()
            } catch (_: Exception) {
            }
        }.apply {
            isDaemon = true
            name = "ext-kill-${proc.id}"
            start()
        }
        processes.remove(proc.id, proc)
        if (errorMessage == null) {
            updateInfo(proc.id) { it.copy(state = ExtState.DISABLED) }
        } else {
            updateInfo(proc.id) { it.copy(state = ExtState.DISABLED, errorMessage = errorMessage) }
        }
    }

    /** 全局看门狗：pending 请求超时、单次处理超过 5s 一律结束进程并禁用。 */
    private fun startWatchdog() {
        Thread {
            while (true) {
                try {
                    val now = System.currentTimeMillis()
                    for (proc in processes.values) {
                        if (!proc.alive.get()) continue
                        var bad: String? = null
                        for ((_, req) in proc.pending) {
                            if (now > req.deadlineMs) {
                                bad = "请求超时"
                                break
                            }
                        }
                        if (bad == null) {
                            val dl = proc.processingDeadline.get()
                            if (dl != 0L && now > dl) bad = "单次请求超过 5s"
                        }
                        if (bad != null) stopProcess(proc, bad, false, bad)
                    }
                    Thread.sleep(200)
                } catch (e: InterruptedException) {
                    break
                } catch (e: Exception) {
                    logFn("看门狗异常: ${e.message}")
                }
            }
        }.apply {
            isDaemon = true
            name = "ext-watchdog"
            start()
        }
    }
}
