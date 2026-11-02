package cn.tyw.launcher.core

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.OutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * 微软正版登录（设备码流程）。与 Windows / Linux 版共用同一个 OAuth 应用注册。
 *
 * 1. 向微软申请设备码，拿到 user_code（授权码）与 device_code；
 * 2. 用户去 microsoft.com/link 输入授权码，这边轮询换 OAuth 令牌；
 * 3. Xbox Live → XSTS → Minecraft 令牌 → 档案（名字 / UUID）。
 *
 * 授权码拿到后由调用方自动写入剪贴板，界面上也保留「复制授权码」按钮。
 */
object MsAuth {
    const val CLIENT_ID = "852d43f4-7b54-4544-9a0e-fc04c3c35c82"
    private const val AUTHORITY = "https://login.microsoftonline.com/consumers/oauth2/v2.0"
    private const val SCOPE = "XboxLive.signin offline_access"

    data class DeviceCode(
        val deviceCode: String,
        val userCode: String,
        val verificationUri: String,
        val interval: Int,
        val expiresIn: Int,
    )

    data class Account(
        val name: String,
        val uuid: String,
        val accessToken: String,
        val refreshToken: String,
    )

    sealed class LoginState {
        data class Waiting(val code: String, val uri: String) : LoginState()
        data class Success(val account: Account) : LoginState()
        data class Failed(val reason: String) : LoginState()
        data object Cancelled : LoginState()
    }

    /** 第 1 步：申请设备码。失败抛异常，由界面提示。 */
    fun requestDeviceCode(): DeviceCode {
        val body = "client_id=$CLIENT_ID&scope=${URLEncoder.encode(SCOPE, "UTF-8")}"
        val json = JSONObject(postForm("$AUTHORITY/devicecode", body))
        val dc = json.optString("device_code")
        if (dc.isEmpty()) throw IllegalStateException("申请授权码失败：$json")
        return DeviceCode(
            deviceCode = dc,
            userCode = json.optString("user_code"),
            verificationUri = json.optString("verification_uri", "https://microsoft.com/link"),
            interval = json.optInt("interval", 5).coerceAtLeast(2),
            expiresIn = json.optInt("expires_in", 900),
        )
    }

    /**
     * 第 2 步：轮询换取令牌，直到成功 / 过期 / 被取消。
     * onCode 在拿到设备码后立刻回调（界面在这里自动复制授权码）。
     */
    suspend fun login(onState: (LoginState) -> Unit, isCancelled: () -> Boolean): Account? =
        withContext(Dispatchers.IO) {
            val dc = try {
                requestDeviceCode()
            } catch (e: Exception) {
                onState(LoginState.Failed(e.message ?: "无法连接微软登录服务"))
                return@withContext null
            }

            Settings.authCode = dc.userCode
            // 授权码自动进剪贴板：用户切到浏览器就能直接粘贴
            Clip.copy(dc.userCode, "授权码")
            onState(LoginState.Waiting(dc.userCode, dc.verificationUri))

            val deadline = System.currentTimeMillis() + dc.expiresIn * 1000L
            var msToken = ""
            while (System.currentTimeMillis() < deadline) {
                if (isCancelled()) {
                    onState(LoginState.Cancelled)
                    return@withContext null
                }
                delay(dc.interval * 1000L)
                when (val r = pollToken(dc.deviceCode)) {
                    is PollResult.Ok -> {
                        msToken = r.accessToken
                        break
                    }
                    is PollResult.Pending -> Unit
                    is PollResult.Failed -> {
                        onState(LoginState.Failed(r.reason))
                        return@withContext null
                    }
                }
            }
            if (msToken.isEmpty()) {
                onState(LoginState.Failed("授权码已过期，请重新登录"))
                return@withContext null
            }

            try {
                val account = exchange(msToken)
                Settings.authCode = ""
                Settings.accountName = account.name
                Settings.accountUuid = account.uuid
                Settings.accessToken = account.accessToken
                Settings.accountType = "正版账户"
                onState(LoginState.Success(account))
                account
            } catch (e: Exception) {
                onState(LoginState.Failed(e.message ?: "换取 Minecraft 令牌失败"))
                null
            }
        }

    private sealed class PollResult {
        data class Ok(val accessToken: String) : PollResult()
        data object Pending : PollResult()
        data class Failed(val reason: String) : PollResult()
    }

    private fun pollToken(deviceCode: String): PollResult = try {
        val body = "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
            "&client_id=$CLIENT_ID&device_code=${URLEncoder.encode(deviceCode, "UTF-8")}"
        val json = JSONObject(postForm("$AUTHORITY/token", body))
        val token = json.optString("access_token")
        if (token.isNotEmpty()) PollResult.Ok(token)
        else PollResult.Failed(json.optString("error_description", "登录失败"))
    } catch (e: HttpError) {
        val err = e.body.let { runCatching { JSONObject(it).optString("error") }.getOrDefault("") }
        when (err) {
            "authorization_pending" -> PollResult.Pending
            "slow_down" -> PollResult.Pending
            "expired_token" -> PollResult.Failed("授权码已过期，请重新登录")
            "authorization_declined" -> PollResult.Failed("你取消了本次登录")
            else -> PollResult.Failed(
                runCatching { JSONObject(e.body).optString("error_description") }.getOrDefault("登录失败")
                    .ifBlank { "登录失败" }
            )
        }
    } catch (e: Exception) {
        PollResult.Pending // 网络抖动不直接判死，继续轮询
    }

    /** 第 3 步：微软令牌 → XBL → XSTS → Minecraft 令牌 → 档案。 */
    private fun exchange(msToken: String): Account {
        val xbl = JSONObject(
            postJson(
                "https://user.auth.xboxlive.com/user/authenticate",
                """{"Properties":{"AuthMethod":"RPS","SiteName":"user.auth.xboxlive.com","RpsTicket":"d=$msToken"},""" +
                    """"RelyingParty":"http://auth.xboxlive.com","TokenType":"JWT"}"""
            )
        )
        val xblToken = xbl.optString("Token")
        val uhs = xbl.optJSONObject("DisplayClaims")
            ?.optJSONArray("xui")?.optJSONObject(0)?.optString("uhs") ?: ""

        val xsts = JSONObject(
            postJson(
                "https://xsts.auth.xboxlive.com/xsts/authorize",
                """{"Properties":{"SandboxId":"RETAIL","UserTokens":["$xblToken"]},""" +
                    """"RelyingParty":"http://xboxlive.com","TokenType":"JWT"}"""
            )
        )
        val xstsToken = xsts.optString("Token")
        if (xstsToken.isEmpty()) throw IllegalStateException("Xbox Live 授权失败，可能是该账户没有 Xbox 档案")

        val mc = JSONObject(
            postJson(
                "https://api.minecraftservices.com/authentication/login_with_xbox",
                """{"identityToken":"XBL3.0 x=$uhs;$xstsToken"}"""
            )
        )
        val mcToken = mc.optString("access_token")
        if (mcToken.isEmpty()) throw IllegalStateException("Minecraft 令牌获取失败")

        val profile = JSONObject(getAuthed("https://api.minecraftservices.com/minecraft/profile", mcToken))
        val name = profile.optString("name")
        if (name.isEmpty()) throw IllegalStateException("该账户没有购买 Minecraft")

        return Account(
            name = name,
            uuid = profile.optString("id"),
            accessToken = mcToken,
            refreshToken = "",
        )
    }

    // ================= HTTP =================

    private class HttpError(val code: Int, val body: String) : Exception("HTTP $code")

    private fun postForm(url: String, body: String): String =
        post(url, body, "application/x-www-form-urlencoded")

    private fun postJson(url: String, body: String): String =
        post(url, body, "application/json")

    private fun post(url: String, body: String, contentType: String): String {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15000
            readTimeout = 20000
            requestMethod = "POST"
            doOutput = true
            setRequestProperty("Content-Type", contentType)
        }
        return try {
            conn.outputStream.use { out: OutputStream -> out.write(body.toByteArray(Charsets.UTF_8)) }
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = stream?.bufferedReader()?.use { it.readText() } ?: ""
            if (code !in 200..299) throw HttpError(code, text)
            text
        } finally {
            conn.disconnect()
        }
    }

    private fun getAuthed(url: String, token: String): String {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 15000
            readTimeout = 20000
            requestMethod = "GET"
            setRequestProperty("Authorization", "Bearer $token")
        }
        return try {
            val code = conn.responseCode
            val text = (if (code in 200..299) conn.inputStream else conn.errorStream)
                ?.bufferedReader()?.use { it.readText() } ?: ""
            if (code !in 200..299) throw IllegalStateException("HTTP $code：${text.take(200)}")
            text
        } finally {
            conn.disconnect()
        }
    }
}
