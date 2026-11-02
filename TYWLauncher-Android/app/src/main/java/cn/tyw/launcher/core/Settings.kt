package cn.tyw.launcher.core

import android.content.Context
import android.content.SharedPreferences
import android.provider.Settings as AndroidSettings
import java.security.MessageDigest

/**
 * 本地设置。键名与 Windows / Linux 版保持一致（UiLauncherTheme / UiBackgroundColorful / BetaCode 等），
 * 这样服务端云控 settings 段可以原样下发。
 */
object Settings {
    private lateinit var sp: SharedPreferences

    fun init(context: Context) {
        sp = context.getSharedPreferences("tyw_launcher", Context.MODE_PRIVATE)
        if (getString("DeviceCode", "").isEmpty()) {
            val raw = AndroidSettings.Secure.getString(context.contentResolver, AndroidSettings.Secure.ANDROID_ID) ?: "unknown"
            setString("DeviceCode", "A" + md5(raw).take(10).uppercase())
        }
        if (getString("UiLauncherTheme", "").isEmpty()) setInt("UiLauncherTheme", 0)
        if (getString("UiBackgroundColorful", "").isEmpty()) setBool("UiBackgroundColorful", true)
        setString("LauncherChannel", AppInfo.buildChannel.key)
    }

    private fun md5(text: String): String =
        MessageDigest.getInstance("MD5").digest(text.toByteArray()).joinToString("") { "%02x".format(it) }

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

    var accountName: String
        get() = getString("LaunchAccountName", "Player")
        set(value) = setString("LaunchAccountName", value)

    var accountType: String
        get() = getString("LaunchAccountType", "离线")
        set(value) = setString("LaunchAccountType", value)
}
