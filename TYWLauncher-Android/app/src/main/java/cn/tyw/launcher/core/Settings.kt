package cn.tyw.launcher.core

import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import android.provider.Settings as AndroidSettings
import java.security.MessageDigest

/**
 * 本地设置。键名与 Windows / Linux 版保持一致（UiLauncherTheme / UiBackgroundColorful / BetaCode 等），
 * 这样服务端云控 settings 段可以原样下发。
 */
object Settings {
    private lateinit var sp: SharedPreferences

    /** 与 Windows 版 DeviceCode 完全一致的四段十六进制格式。 */
    private val deviceCodePattern = Regex("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$")

    fun init(context: Context) {
        sp = context.getSharedPreferences("tyw_launcher", Context.MODE_PRIVATE)
        // 老版本的设备码是「A+10 位」，服务端按 Windows 的 XXXX-XXXX-XXXX-XXXX 解析，认不出来，一律重建
        if (!deviceCodePattern.matches(getString("DeviceCode", ""))) {
            setString("DeviceCode", buildDeviceCode(context))
        }
        if (getString("UiLauncherTheme", "").isEmpty()) setInt("UiLauncherTheme", 0)
        if (getString("UiBackgroundColorful", "").isEmpty()) setBool("UiBackgroundColorful", true)
        setString("LauncherChannel", AppInfo.buildChannel.key)
    }

    /**
     * 设备码：与 Windows 版同构——取一组稳定的设备标识拼成种子做 MD5，取前 16 位十六进制大写，
     * 再按 4-4-4-4 加连字符。Android 上没有 MachineGuid，改用 ANDROID_ID + 厂商 / 型号 / 指纹。
     * 同一台机器、同一个安装，结果稳定不变，也不含明文个人信息。
     */
    private fun buildDeviceCode(context: Context): String = try {
        val androidId = AndroidSettings.Secure.getString(
            context.contentResolver, AndroidSettings.Secure.ANDROID_ID
        ) ?: "unknown"
        val seed = buildString {
            append(androidId).append('|')
            append(Build.MANUFACTURER).append('|')
            append(Build.MODEL).append('|')
            append(Build.DEVICE).append('|')
            append(Build.FINGERPRINT)
        }
        val hash = MessageDigest.getInstance("MD5").digest(seed.toByteArray(Charsets.UTF_8))
        val hex = hash.joinToString("") { "%02X".format(it) }.substring(0, 16)
        "${hex.substring(0, 4)}-${hex.substring(4, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}"
    } catch (e: Exception) {
        "0000-0000-0000-0000"
    }

    fun getString(key: String, def: String = ""): String = sp.getString(key, def) ?: def
    fun setString(key: String, value: String) = sp.edit().putString(key, value).apply()

    fun getInt(key: String, def: Int = 0): Int = getString(key, def.toString()).toIntOrNull() ?: def
    fun setInt(key: String, value: Int) = setString(key, value.toString())

    fun getBool(key: String, def: Boolean = false): Boolean =
        when (getString(key, if (def) "True" else "False").lowercase()) {
            "true", "1" -> true
            "false", "0" -> false
            else -> def
        }

    fun setBool(key: String, value: Boolean) = setString(key, if (value) "True" else "False")

    /** 当前通道。 */
    var channel: Channel
        get() = Channel.fromKey(getString("LauncherChannel", AppInfo.buildChannel.key))
        set(value) = setString("LauncherChannel", value.key)

    val deviceCode: String get() = getString("DeviceCode", "")

    var betaCode: String
        get() = getString("BetaCode", "")
        set(value) = setString("BetaCode", value)

    /**
     * 正版登录的授权码（微软设备码登录里需要用户去 microsoft.com/link 输入的那串码）。
     * 登录开始时写入，登录完成后清空；用户随时可以手动复制。
     */
    var authCode: String
        get() = getString("MsAuthCode", "")
        set(value) = setString("MsAuthCode", value)

    var accountName: String
        get() = getString("LaunchAccountName", "Player")
        set(value) = setString("LaunchAccountName", value)

    var accountType: String
        get() = getString("LaunchAccountType", "离线")
        set(value) = setString("LaunchAccountType", value)

    /** 正版账户的 UUID（离线账户为空）。 */
    var accountUuid: String
        get() = getString("LaunchAccountUuid", "")
        set(value) = setString("LaunchAccountUuid", value)

    /** 正版账户的 access token，启动时拼进 --accessToken。 */
    var accessToken: String
        get() = getString("LaunchAccessToken", "")
        set(value) = setString("LaunchAccessToken", value)

    /** 自动补全的 Java 路径（空表示用系统 / 内置检测结果）。 */
    var javaPath: String
        get() = getString("LaunchJavaPath", "")
        set(value) = setString("LaunchJavaPath", value)
}
