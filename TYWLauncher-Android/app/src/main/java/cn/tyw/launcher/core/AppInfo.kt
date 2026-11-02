package cn.tyw.launcher.core

import cn.tyw.launcher.BuildConfig

/**
 * 版本通道。与 Windows / Linux 版一一对应：
 * Release = 正式版、Snapshot = 快照版、Beta = 内测版。
 */
enum class Channel(val key: String, val display: String, val packageSuffix: String) {
    RELEASE("Release", "正式版", ""),
    SNAPSHOT("Snapshot", "快照版", "-snapshot"),
    BETA("Beta", "内测版", "-beta");

    companion object {
        fun fromKey(key: String?): Channel =
            entries.firstOrNull { it.key.equals(key, ignoreCase = true) } ?: RELEASE
    }
}

object AppInfo {
    const val SERVER_ADDRESS = BuildConfig.SERVER_ADDRESS
    val buildChannel: Channel = Channel.fromKey(BuildConfig.CHANNEL)
    val versionName: String = BuildConfig.VERSION_NAME_CN
    val versionCode: Int = BuildConfig.VERSION_CODE_CN

    /** 当前实际运行的通道（可被设置里的切换覆盖，切换后需要装对应通道的包）。 */
    val channel: Channel get() = Settings.channel

    /** 通道对应的安装包名，与服务端 www 目录里的文件名一致。 */
    fun packageNameOf(channel: Channel): String = "TYWLauncher-android${channel.packageSuffix}.apk"
}
