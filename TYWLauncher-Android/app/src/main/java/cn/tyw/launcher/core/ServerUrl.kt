package cn.tyw.launcher.core

import cn.tyw.launcher.BuildConfig

/**
 * 服务端地址解析。
 *
 * 开源仓库里不带任何服务器地址：地址为空时，公告、启动器更新、内测码校验、封禁查询、
 * 匿名上报全部自动关闭，启动器纯本地运行。
 *
 * 想接到自己的服务端，任选一种（优先级从高到低）：
 *   1) 设置 → 服务端（可选）里的地址
 *   2) 构建时用环境变量 TYW_SERVER_URL 注入（BuildConfig.SERVER_ADDRESS）
 *
 * 与 Windows / Linux / macOS 版的解析规则一致，保证四端能接同一个服务端。
 */
object ServerUrl {
    @Volatile
    private var cached: String? = null

    /** 当前生效的地址，保证以 / 结尾。空字符串表示未配置。 */
    val current: String
        get() = cached ?: resolve().also { cached = it }

    /** 设置里改了地址后调用，清掉缓存。 */
    fun invalidate() {
        cached = null
    }

    private fun resolve(): String {
        val custom = Settings.getString("ServerUrl", "").trim()
            .ifEmpty { BuildConfig.SERVER_ADDRESS.trim() }
        if (custom.isEmpty()) return ""
        var url = if (custom.startsWith("http", ignoreCase = true)) custom else "http://$custom"
        if (!url.endsWith("/")) url += "/"
        return url
    }
}
