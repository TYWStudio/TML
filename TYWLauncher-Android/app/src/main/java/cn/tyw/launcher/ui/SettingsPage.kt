package cn.tyw.launcher.ui

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.widget.Toast
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import cn.tyw.launcher.core.*
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.HttpURLConnection
import java.net.URL
import kotlinx.coroutines.launch
import java.io.File
import java.net.URLEncoder

/** 设置页。分类与 Windows 版一致：个性化（主题）/ 版本通道 / 触控 / 关于与更新。 */
@Composable
fun SettingsPage(
    themeIndex: Int,
    colorful: Boolean,
    onThemeChange: (Int) -> Unit,
    onColorfulChange: (Boolean) -> Unit,
    onOpenTouchLayout: () -> Unit,
    onOpenJavaPage: () -> Unit,
) {
    val context = LocalContext.current
    val p = LocalPalette.current
    val scope = rememberCoroutineScope()

    var customHue by remember { mutableIntStateOf(Settings.getInt("UiLauncherHue", 210)) }
    var customSat by remember { mutableIntStateOf(Settings.getInt("UiLauncherSat", 85)) }
    var customLight by remember { mutableIntStateOf(Settings.getInt("UiLauncherLight", 0)) }
    var showBetaDialog by remember { mutableStateOf(false) }
    var betaInput by remember { mutableStateOf(Settings.betaCode) }

    var storeCats by remember { mutableStateOf<List<Store.StoreCategory>?>(null) }
    var storeLoading by remember { mutableStateOf(false) }
    var storeQuery by remember { mutableStateOf("") }
    var downloading by remember { mutableStateOf("") }
    var dlProgress by remember { mutableFloatStateOf(0f) }

    fun installChannel(target: Channel, code: String = "") {
        val info = Server.channelFile(target)
        if (info == null) {
            Toast.makeText(context, "服务端还没有配置「${target.display}」的安装包", Toast.LENGTH_LONG).show()
            return
        }
        downloading = target.display
        dlProgress = 0f
        scope.launch {
            var url = Server.packageUrl(info.file)
            if (target == Channel.BETA && code.isNotEmpty()) {
                url += "?ident=${URLEncoder.encode(Settings.deviceCode, "UTF-8")}" +
                    "&key=${URLEncoder.encode(code, "UTF-8")}"
            }
            val apk = File(context.cacheDir, "TYWLauncher-android${target.packageSuffix}.apk")
            val ok = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                Server.download(url, apk) { dlProgress = it.toFloat() }
            }
            downloading = ""
            if (!ok) {
                Toast.makeText(context, "下载失败，请检查网络", Toast.LENGTH_LONG).show()
                return@launch
            }
            try {
                val uri: Uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", apk)
                val intent = Intent(Intent.ACTION_VIEW).apply {
                    setDataAndType(uri, "application/vnd.android.package-archive")
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                context.startActivity(intent)
            } catch (e: Exception) {
                Toast.makeText(context, "无法打开安装器：${e.message}", Toast.LENGTH_LONG).show()
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        // ===== 主题配色 =====
        TywCard("主题配色") {
            SettingRow("当前配色") { ValueText("${Themes.nameOf(themeIndex)}（${themeIndex + 1}/15）") }
            Column(Modifier.padding(vertical = 6.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Themes.presets.indices.chunked(5).forEach { rowItems ->
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        rowItems.forEach { i ->
                            ThemeSwatch(i, i == themeIndex, Themes.preview(i)) {
                                Settings.setInt("UiLauncherTheme", i)
                                onThemeChange(i)
                            }
                        }
                    }
                }
            }
            SettingRow("主题色背景") {
                TywButton(if (colorful) "已开启" else "已关闭") { onColorfulChange(!colorful) }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("自定义", fontSize = 12.5.sp, color = p.c1, modifier = Modifier.width(58.dp))
                NumberField(customHue, "色相") {
                    customHue = it.coerceIn(0, 359)
                    Settings.setInt("UiLauncherHue", customHue)
                }
                Spacer(Modifier.width(6.dp))
                NumberField(customSat, "饱和") {
                    customSat = it.coerceIn(0, 100)
                    Settings.setInt("UiLauncherSat", customSat)
                }
                Spacer(Modifier.width(6.dp))
                NumberField(customLight, "亮度") {
                    customLight = it.coerceIn(-30, 30)
                    Settings.setInt("UiLauncherLight", customLight)
                }
            }
            Row(Modifier.padding(top = 6.dp)) {
                TywButton("应用自定义配色", highlight = true) {
                    Settings.setInt("UiLauncherHue", customHue)
                    Settings.setInt("UiLauncherSat", customSat)
                    Settings.setInt("UiLauncherLight", customLight)
                    Settings.setInt("UiLauncherTheme", Themes.CUSTOM)
                    onThemeChange(Themes.CUSTOM)
                }
            }
            HintText("与 Windows 版完全一致：15 套预设，滑稽彩 / 欧皇彩随机色相，自定义可调色相与亮度补偿。")
        }

        // ===== 版本通道 =====
        TywCard("版本通道") {
            SettingRow("当前通道") { ValueText("${AppInfo.channel.display}（版本码 ${AppInfo.versionCode}）") }
            if (downloading.isNotEmpty()) {
                LinearProgressIndicator(progress = { dlProgress }, modifier = Modifier.fillMaxWidth())
                Text("正在下载${downloading}安装包 ${(dlProgress * 100).toInt()}%", fontSize = 11.sp, color = p.textHint)
            } else {
                Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (AppInfo.channel != Channel.RELEASE) {
                        TywButton("切换到正式版") { installChannel(Channel.RELEASE) }
                    }
                    if (AppInfo.channel != Channel.SNAPSHOT) {
                        TywButton("切换到快照版") { installChannel(Channel.SNAPSHOT) }
                    }
                    if (AppInfo.channel != Channel.BETA) {
                        TywButton("切换到内测版") { showBetaDialog = true }
                    }
                }
            }
            HintText("切换会下载对应通道的 APK 并交给系统安装器，数据不受影响。内测版需要有效内测码，启动时会联网校验。")
        }

        // ===== Java 运行环境（对应 Windows 设置-启动设置里的 Java 列表） =====
        TywCard("Java 运行环境") {
            val installed = JavaRuntime.scan()
            val current = JavaRuntime.pick()
            SettingRow("已安装的运行时") { ValueText("${installed.size} 个") }
            SettingRow("当前使用") {
                Text(
                    current?.display ?: "未检测到",
                    fontSize = 12.5.sp,
                    color = if (current == null) p.textHint else p.c2
                )
            }
            if (current != null) {
                Text(current.path, fontSize = 11.sp, color = p.textHint, lineHeight = 15.sp)
            }
            Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TywButton("管理 Java", highlight = true) { onOpenJavaPage() }
            }
            HintText("列表覆盖 Java 8 / 11 / 16 / 17 / 21 / 25，可按游戏版本补全，也可单独下载或导入本地 JRE 包。")
        }

        // ===== 触控布局 =====
        TywCard("触控布局") {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 6.dp)) {
                Column(Modifier.weight(1f)) {
                    Text("按键布局编辑器", fontSize = 13.sp, color = p.c1)
                    Text("拖动摆位，可保存 / 恢复默认（FCL 键位）", fontSize = 11.5.sp, color = p.textHint)
                }
                TywButton("编辑", highlight = true, onClick = onOpenTouchLayout)
            }
        }

        // ===== 服务端（可选） =====
        TywCard("服务端（可选）") {
            var urlInput by remember { mutableStateOf(Settings.getString("ServerUrl", "")) }
            SettingRow("当前地址") {
                ValueText(if (AppInfo.SERVER_ADDRESS.isEmpty()) "未配置，纯本地运行" else AppInfo.SERVER_ADDRESS)
            }
            OutlinedTextField(
                value = urlInput,
                onValueChange = { urlInput = it },
                label = { Text("http://你的服务器:17403/", fontSize = 11.sp) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                modifier = Modifier.fillMaxWidth().padding(top = 6.dp)
            )
            Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TywButton("保存地址", highlight = true) {
                    var url = urlInput.trim()
                    if (url.isNotEmpty() && !url.startsWith("http", ignoreCase = true)) url = "http://$url"
                    if (url.isNotEmpty() && !url.endsWith("/")) url += "/"
                    Settings.setString("ServerUrl", url)
                    ServerUrl.invalidate()
                    Toast.makeText(
                        context,
                        if (url.isEmpty()) "已清空服务端地址，启动器转为纯本地运行" else "服务端地址已保存：$url",
                        Toast.LENGTH_SHORT
                    ).show()
                }
                TywButton("清空") {
                    urlInput = ""
                    Settings.setString("ServerUrl", "")
                    ServerUrl.invalidate()
                    Toast.makeText(context, "已清空服务端地址，启动器转为纯本地运行", Toast.LENGTH_SHORT).show()
                }
            }
            HintText("开源版不带任何服务器地址：不填就纯本地运行，公告、启动器更新、内测码校验、封禁查询、匿名上报全部关闭。四端接同一个服务端，地址格式与 Windows / Linux / macOS 版一致。")
        }


        // ===== 拓展商店 =====
        TywCard("拓展商店") {
            OutlinedTextField(
                value = storeQuery,
                onValueChange = { storeQuery = it },
                label = { Text("搜索拓展", fontSize = 11.sp) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            if (storeLoading) {
                LinearProgressIndicator(progress = { 1f }, modifier = Modifier.fillMaxWidth())
                Text("正在从下载站加载……", fontSize = 11.sp, color = p.textHint)
            } else if (storeCats != null) {
                val q = storeQuery.trim()
                val projects = (storeCats ?: emptyList()).flatMap { it.projects }.filter { pr ->
                    q.isEmpty() ||
                        pr.name.contains(q, true) || pr.tagline.contains(q, true) ||
                        pr.description.contains(q, true) || pr.slug.contains(q, true)
                }
                if (projects.isEmpty()) {
                    Text("没有匹配的拓展。", fontSize = 11.sp, color = p.textHint)
                } else {
                    Column {
                        for (pr in projects) {
                            Row(
                                Modifier.padding(vertical = 5.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                StoreIcon(pr.icon, pr.name)
                                Spacer(Modifier.width(10.dp))
                                Column(Modifier.weight(1f)) {
                                    Text(pr.name, fontSize = 13.sp, color = p.c1)
                                    if (pr.tagline.isNotBlank())
                                        Text(pr.tagline, fontSize = 11.sp, color = p.textHint)
                                    val meta = listOfNotNull(
                                        if (pr.downloads > 0) pr.downloads.toString() + " 次下载" else null,
                                        pr.sizeHuman.takeIf { it.isNotBlank() && it != "—" }
                                    )
                                    if (meta.isNotEmpty())
                                        Text(meta.joinToString(" · "), fontSize = 10.sp, color = p.textHint)
                                }
                                TywButton("安装", highlight = true) {
                                    scope.launch {
                                        try {
                                            Store.downloadAndInstall(pr, context.cacheDir)
                                            Toast.makeText(context, "已导入拓展：${pr.name}，可在拓展页启用。", Toast.LENGTH_SHORT).show()
                                        } catch (e: Exception) {
                                            Toast.makeText(context, "安装失败：${e.message}", Toast.LENGTH_LONG).show()
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TywButton("刷新列表", highlight = true) {
                    storeLoading = true
                    scope.launch {
                        try {
                            storeCats = Store.fetchTree()
                        } catch (e: Exception) {
                            Toast.makeText(context, "加载失败：${e.message}", Toast.LENGTH_LONG).show()
                        } finally {
                            storeLoading = false
                        }
                    }
                }
            }
        }

        // ===== 关于与更新 =====
        TywCard("关于与更新") {
            SettingRow("版本") { ValueText("${AppInfo.versionName}（版本码 ${AppInfo.versionCode}）") }
            SettingRow("通道") { ValueText(AppInfo.channel.display) }
            SettingRow("运行环境") { ValueText("Android ${android.os.Build.VERSION.RELEASE}") }
            SettingRow("服务器") { ValueText(if (Server.online) "已连接" else "离线") }
            CopyRow("设备码", Settings.deviceCode, hint = "格式与 Windows 版一致（XXXX-XXXX-XXXX-XXXX），服务端按它发放内测码。")
            CopyRow("授权码", Settings.authCode, hint = "正版登录时自动复制，也可以在这里手动复制。")
            Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TywButton("检查更新", highlight = true) {
                    scope.launch {
                        Server.refresh()
                        val update = Server.updateAvailable()
                        if (update == null) {
                            Toast.makeText(context, "已是最新的${AppInfo.channel.display}", Toast.LENGTH_SHORT).show()
                        } else {
                            installChannel(AppInfo.channel)
                        }
                    }
                }
                TywButton("刷新公告") {
                    scope.launch {
                        Server.refresh()
                        Toast.makeText(
                            context,
                            if (Server.online) "公告已刷新" else "连不上服务端：${Server.lastError}",
                            Toast.LENGTH_SHORT
                        ).show()
                    }
                }
            }
        }
    }

    if (showBetaDialog) {
        AlertDialog(
            onDismissRequest = { showBetaDialog = false },
            title = { Text("切换到内测版", fontSize = 15.sp) },
            text = {
                Column {
                    OutlinedTextField(
                        value = betaInput,
                        onValueChange = { betaInput = it },
                        label = { Text("内测码") },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii)
                    )
                    HintText("内测码由服主发放，会与设备码绑定；服务端校验通过后才允许下载内测包。")
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    showBetaDialog = false
                    val code = betaInput.trim()
                    if (code.isEmpty()) {
                        Toast.makeText(context, "内测码不能为空", Toast.LENGTH_SHORT).show()
                        return@TextButton
                    }
                    scope.launch {
                        val result = Server.verifyBetaCode(code)
                        if (result.ok) {
                            Settings.betaCode = code
                            installChannel(Channel.BETA, code)
                        } else {
                            Toast.makeText(context, "内测码无效：${result.message}", Toast.LENGTH_LONG).show()
                        }
                    }
                }) { Text("校验并下载") }
            },
            dismissButton = { TextButton(onClick = { showBetaDialog = false }) { Text("取消") } }
        )
    }
}

@Composable
private fun NumberField(value: Int, label: String, onChange: (Int) -> Unit) {
    var text by remember { mutableStateOf(value.toString()) }
    OutlinedTextField(
        value = text,
        onValueChange = {
            text = it.filter { c -> c.isDigit() || c == '-' }
            text.toIntOrNull()?.let(onChange)
        },
        label = { Text(label, fontSize = 11.sp) },
        singleLine = true,
        modifier = Modifier.width(88.dp)
    )
}

/** 项目图标：异步从下载站加载，失败时回退到首字占位块（不阻塞列表渲染）。 */
@Composable
private fun StoreIcon(url: String, name: String) {
    val p = LocalPalette.current
    var bitmap by remember(url) { mutableStateOf<Bitmap?>(null) }
    LaunchedEffect(url) {
        if (url.isBlank()) return@LaunchedEffect
        val loaded = withContext(Dispatchers.IO) {
            try {
                val conn = (URL(url).openConnection() as HttpURLConnection).apply {
                    connectTimeout = 8000
                    readTimeout = 8000
                    requestMethod = "GET"
                    setRequestProperty("User-Agent", "TYWLauncher-Android")
                }
                val b = if (conn.responseCode in 200..299) {
                    conn.inputStream.use { BitmapFactory.decodeStream(it) }
                } else null
                conn.disconnect()
                b
            } catch (e: Exception) {
                null
            }
        }
        bitmap = loaded
    }
    if (bitmap != null) {
        Image(
            bitmap = bitmap!!.asImageBitmap(),
            contentDescription = name,
            contentScale = ContentScale.Fit,
            modifier = Modifier.size(42.dp)
        )
    } else {
        Box(
            Modifier.size(42.dp).background(p.bg0, RoundedCornerShape(8.dp)),
            contentAlignment = Alignment.Center
        ) {
            Text(name.trim().take(1).ifEmpty { "拓" }, fontSize = 18.sp, color = p.c1)
        }
    }
}
