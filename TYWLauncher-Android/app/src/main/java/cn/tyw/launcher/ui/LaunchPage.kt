package cn.tyw.launcher.ui

import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.*
import kotlinx.coroutines.launch

/**
 * 启动页。布局对齐 Windows 版 PageLaunch：
 * 左栏是账户 + 版本选择，右栏是启动按钮与游戏设置；窄屏时上下堆叠。
 */
@Composable
fun LaunchPage(onOpenTouchLayout: () -> Unit) {
    val context = LocalContext.current
    val p = LocalPalette.current
    val scope = rememberCoroutineScope()

    var versions by remember { mutableStateOf(Minecraft.scan(context)) }
    var selected by remember {
        mutableStateOf(versions.firstOrNull { it.name == Settings.getString("LaunchVersionSelect", "") }
            ?: versions.firstOrNull())
    }
    var accountName by remember { mutableStateOf(Settings.accountName) }
    var accountType by remember { mutableStateOf(Settings.accountType) }
    var showVersionPicker by remember { mutableStateOf(false) }
    var showAccountEditor by remember { mutableStateOf(false) }
    var showVersionSettings by remember { mutableStateOf(false) }
    var showLaunchHelp by remember { mutableStateOf(false) }
    var maxMemory by remember { mutableIntStateOf(Settings.getInt("LaunchRam", 2048)) }

    // 正版登录
    var loginOpen by remember { mutableStateOf(false) }
    var loginCode by remember { mutableStateOf("") }
    var loginUri by remember { mutableStateOf("") }
    var loginStatus by remember { mutableStateOf("") }
    var loginRunning by remember { mutableStateOf(false) }
    var cancelLogin by remember { mutableStateOf(false) }

    // Java
    var javaPath by remember { mutableStateOf(JavaRuntime.detect() ?: "") }
    var javaStatus by remember { mutableStateOf("") }
    var javaProgress by remember { mutableFloatStateOf(0f) }
    var javaBusy by remember { mutableStateOf(false) }

    val accountCard: @Composable () -> Unit = {
        TywCard("账户") {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 6.dp)) {
                Box(
                    Modifier
                        .size(46.dp)
                        .clip(RoundedCornerShape(3.dp))
                        .background(p.c6)
                        .border(1.dp, p.c5, RoundedCornerShape(3.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    Text(accountName.take(1).uppercase(), fontSize = 20.sp, color = p.c2, fontWeight = FontWeight.Bold)
                }
                Spacer(Modifier.width(10.dp))
                Column(Modifier.weight(1f)) {
                    Text(accountName, fontSize = 14.sp, color = p.c1, fontWeight = FontWeight.Medium)
                    Text(accountType, fontSize = 11.5.sp, color = p.textHint)
                }
                TywButton("正版登录", highlight = true) {
                    loginOpen = true
                    loginStatus = "正在申请授权码…"
                    loginCode = ""
                    loginRunning = true
                    cancelLogin = false
                    scope.launch {
                        MsAuth.login(
                            onState = { st ->
                                when (st) {
                                    is MsAuth.LoginState.Waiting -> {
                                        loginCode = st.code
                                        loginUri = st.uri
                                        loginStatus = "授权码已复制到剪贴板，去浏览器粘贴即可"
                                    }
                                    is MsAuth.LoginState.Success -> {
                                        accountName = st.account.name
                                        accountType = "正版账户"
                                        loginRunning = false
                                        loginOpen = false
                                        Toast.makeText(context, "登录成功：${st.account.name}", Toast.LENGTH_SHORT).show()
                                    }
                                    is MsAuth.LoginState.Failed -> {
                                        loginStatus = st.reason
                                        loginRunning = false
                                    }
                                    MsAuth.LoginState.Cancelled -> {
                                        loginStatus = "已取消"
                                        loginRunning = false
                                    }
                                }
                            },
                            isCancelled = { cancelLogin }
                        )
                    }
                }
            }
            SettingRow("离线用户名") {
                TywButton("改名字") { showAccountEditor = true }
            }
            // 授权码：登录过程中自动进剪贴板，这里也能手动复制
            CopyRow(
                "授权码",
                Settings.authCode,
                hint = "正版登录时会自动复制；过期后重新点「正版登录」即可换新的。"
            )
            CopyRow("设备码", Settings.deviceCode, hint = "与 Windows 版格式一致，内测码按这个设备码发放。")
        }
    }

    val versionCard: @Composable () -> Unit = {
        TywCard("当前版本") {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 6.dp)) {
                Column(Modifier.weight(1f)) {
                    Text(
                        selected?.name ?: "未找到已安装的版本",
                        fontSize = 14.sp,
                        color = p.c1,
                        fontWeight = FontWeight.Medium
                    )
                    Text(
                        selected?.typeDisplay ?: "请先在「下载」页安装原版游戏",
                        fontSize = 11.5.sp,
                        color = p.textHint
                    )
                }
                TywButton("选择") {
                    versions = Minecraft.scan(context)
                    if (versions.isEmpty()) {
                        Toast.makeText(context, "还没有已安装的版本，先去下载页安装", Toast.LENGTH_SHORT).show()
                    } else {
                        showVersionPicker = true
                    }
                }
                Spacer(Modifier.width(8.dp))
                TywButton("版本设置", enabled = selected != null) { showVersionSettings = true }
            }
        }
    }

    val javaCard: @Composable () -> Unit = {
        TywCard("Java 运行环境") {
            SettingRow("当前 Java") {
                Text(
                    if (javaPath.isEmpty()) "未检测到" else javaPath.substringAfterLast('/')
                        .ifEmpty { javaPath },
                    fontSize = 12.5.sp,
                    color = if (javaPath.isEmpty()) p.textHint else p.c2
                )
            }
            if (javaPath.isNotEmpty()) {
                HintText(JavaRuntime.versionOf(javaPath).ifEmpty { javaPath })
            }
            if (javaBusy) {
                LinearProgressIndicator(
                    progress = { javaProgress },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 6.dp)
                )
                if (javaStatus.isNotEmpty()) HintText(javaStatus)
            }
            Row(Modifier.padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                TywButton("自动补全 Java", highlight = javaPath.isEmpty(), enabled = !javaBusy) {
                    javaBusy = true
                    javaProgress = 0f
                    javaStatus = "准备中…"
                    scope.launch {
                        val r = JavaRuntime.ensure(
                            progress = { javaProgress = it.toFloat() },
                            status = { javaStatus = it }
                        )
                        javaBusy = false
                        javaPath = r.path
                        if (r.ok) {
                            Toast.makeText(context, "Java 已就绪", Toast.LENGTH_SHORT).show()
                        } else {
                            javaStatus = r.message
                            Toast.makeText(context, r.message, Toast.LENGTH_LONG).show()
                        }
                    }
                }
                Spacer(Modifier.width(8.dp))
                TywButton("重新检测", enabled = !javaBusy) {
                    javaPath = JavaRuntime.detect() ?: ""
                    javaStatus = if (javaPath.isEmpty()) "没有找到可用的 Java" else "已找到"
                }
            }
            HintText("内置源没配时会去服务端 www 找 java-android-<架构>.zip，也可以把 JRE 包放进启动器目录。")
        }
    }

    val launchButton: @Composable () -> Unit = {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(58.dp)
                .clip(RoundedCornerShape(6.dp))
                .background(if (selected != null) p.c3 else p.c5)
                .clickable(enabled = selected != null) { showLaunchHelp = true },
            contentAlignment = Alignment.Center
        ) {
            Text(
                if (selected != null) "启动游戏" else "请选择版本",
                color = Color.White,
                fontSize = 17.sp,
                fontWeight = FontWeight.Medium
            )
        }
    }

    BoxWithConstraints(Modifier.fillMaxSize()) {
        val wide = maxWidth > 620.dp
        if (wide) {
            Row(
                Modifier
                    .fillMaxSize()
                    .padding(12.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Column(
                    Modifier
                        .weight(1f)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    accountCard()
                    versionCard()
                }
                Column(
                    Modifier
                        .weight(1f)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    launchButton()
                    javaCard()
                    TouchCard(onOpenTouchLayout)
                    serverCard()
                }
            }
        } else {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                accountCard()
                versionCard()
                launchButton()
                javaCard()
                TouchCard(onOpenTouchLayout)
                serverCard()
            }
        }
    }

    // ===== 正版登录对话框：授权码大字 + 手动复制 =====
    if (loginOpen) {
        AlertDialog(
            onDismissRequest = {
                cancelLogin = true
                loginOpen = false
            },
            title = { Text("微软正版登录", fontSize = 15.sp) },
            text = {
                Column {
                    Text("1. 授权码已自动复制，可再手动复制一次：", fontSize = 12.5.sp)
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .padding(vertical = 8.dp)
                            .clip(RoundedCornerShape(4.dp))
                            .background(p.c7)
                            .clickable(enabled = loginCode.isNotEmpty()) {
                                Clip.copy(loginCode, "授权码")
                            }
                            .padding(vertical = 10.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            loginCode.ifEmpty { "…" },
                            fontSize = 20.sp,
                            fontWeight = FontWeight.Bold,
                            color = p.c1,
                            letterSpacing = 2.sp
                        )
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        TywButton("复制授权码", enabled = loginCode.isNotEmpty()) {
                            Clip.copy(loginCode, "授权码")
                        }
                        Spacer(Modifier.width(8.dp))
                        TywButton("打开登录页", enabled = loginCode.isNotEmpty()) {
                            runCatching {
                                context.startActivity(
                                    Intent(Intent.ACTION_VIEW, Uri.parse(loginUri.ifEmpty { "https://microsoft.com/link" }))
                                )
                            }
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                    Text("2. 在浏览器里输入上面的授权码，登录微软账户并授权。", fontSize = 12.5.sp)
                    if (loginStatus.isNotEmpty()) {
                        HintText(loginStatus)
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    cancelLogin = true
                    loginOpen = false
                }) { Text(if (loginRunning) "取消登录" else "关闭") }
            }
        )
    }

    if (showVersionPicker) {
        AlertDialog(
            onDismissRequest = { showVersionPicker = false },
            title = { Text("选择版本", fontSize = 15.sp) },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState())) {
                    versions.forEach { v ->
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .clickable {
                                    selected = v
                                    Settings.setString("LaunchVersionSelect", v.name)
                                    showVersionPicker = false
                                }
                                .padding(vertical = 10.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(v.name, fontSize = 13.sp, color = p.c1, modifier = Modifier.weight(1f))
                            Text(v.typeDisplay, fontSize = 11.5.sp, color = p.textHint)
                        }
                    }
                }
            },
            confirmButton = { TextButton(onClick = { showVersionPicker = false }) { Text("取消") } }
        )
    }

    if (showAccountEditor) {
        var name by remember { mutableStateOf(accountName) }
        AlertDialog(
            onDismissRequest = { showAccountEditor = false },
            title = { Text("离线账户", fontSize = 15.sp) },
            text = {
                Column {
                    OutlinedTextField(
                        value = name,
                        onValueChange = { name = it },
                        label = { Text("离线用户名") },
                        singleLine = true
                    )
                    HintText("切到离线账户后，正版令牌会保留，随时可以切回来。")
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val n = name.trim().ifEmpty { "Player" }
                    accountName = n
                    accountType = "离线账户"
                    Settings.accountName = n
                    Settings.accountType = "离线账户"
                    showAccountEditor = false
                }) { Text("确定") }
            },
            dismissButton = { TextButton(onClick = { showAccountEditor = false }) { Text("取消") } }
        )
    }

    if (showVersionSettings && selected != null) {
        AlertDialog(
            onDismissRequest = { showVersionSettings = false },
            title = { Text("版本设置 · ${selected!!.name}", fontSize = 15.sp) },
            text = {
                Column {
                    Text("内存分配：$maxMemory MB", fontSize = 13.sp)
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 6.dp)) {
                        TywButton("-512") {
                            maxMemory = (maxMemory - 512).coerceAtLeast(512)
                            Settings.setInt("LaunchRam", maxMemory)
                        }
                        Spacer(Modifier.width(8.dp))
                        TywButton("+512") {
                            maxMemory += 512
                            Settings.setInt("LaunchRam", maxMemory)
                        }
                    }
                    Text("游戏目录：${Minecraft.gameDir(context).absolutePath}", fontSize = 11.5.sp, color = p.textHint)
                    Text(
                        "Java：${javaPath.ifEmpty { "未检测到，去「自动补全 Java」" }}",
                        fontSize = 11.5.sp,
                        color = p.textHint
                    )
                }
            },
            confirmButton = { TextButton(onClick = { showVersionSettings = false }) { Text("关闭") } }
        )
    }

    if (showLaunchHelp) {
        AlertDialog(
            onDismissRequest = { showLaunchHelp = false },
            title = { Text("启动前准备", fontSize = 15.sp) },
            text = {
                Column {
                    Text(
                        "启动参数、版本识别、账户与 Java 路径都按 Windows 版同一套逻辑生成。",
                        fontSize = 12.5.sp,
                        lineHeight = 18.sp
                    )
                    Spacer(Modifier.height(8.dp))
                    Text("Java：${javaPath.ifEmpty { "未检测到" }}", fontSize = 12.sp)
                    Spacer(Modifier.height(4.dp))
                    val args = selected?.let {
                        Minecraft.buildLaunchArgs(it, Minecraft.gameDir(context), accountName, maxMemory)
                    } ?: emptyList()
                    Text("启动参数（前 6 项）：", fontSize = 12.sp, fontWeight = FontWeight.Medium)
                    Text(
                        args.take(6).joinToString("\n"),
                        fontSize = 11.sp,
                        color = p.textHint,
                        lineHeight = 15.sp
                    )
                }
            },
            confirmButton = { TextButton(onClick = { showLaunchHelp = false }) { Text("知道了") } }
        )
    }
}

@Composable
private fun TouchCard(onOpen: () -> Unit) {
    val p = LocalPalette.current
    TywCard("触控布局") {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 6.dp)) {
            Column(Modifier.weight(1f)) {
                Text("游戏内触控按键", fontSize = 13.sp, color = p.c1)
                Text(
                    "摇杆 / 视角 / 攻击 / 使用 / 跳跃 / 潜行 / 疾跑 / 丢弃 / 物品栏，键位与 FCL 默认布局一致",
                    fontSize = 11.5.sp,
                    color = p.textHint
                )
            }
            TywButton("编辑布局", highlight = true, onClick = onOpen)
        }
    }
}

@Composable
private fun serverCard() {
    val p = LocalPalette.current
    TywCard("服务端") {
        SettingRow("连接状态") {
            Text(
                if (Server.online) "已连接" else "离线（不影响本地启动）",
                fontSize = 12.5.sp,
                color = if (Server.online) p.c2 else p.textHint
            )
        }
        SettingRow("服务器") { ValueText(AppInfo.SERVER_ADDRESS.removePrefix("http://").trimEnd('/')) }
        SettingRow("版本通道") { ValueText("${AppInfo.channel.display} · ${AppInfo.versionName}") }
        CopyRow("设备码", Settings.deviceCode)
        HintText("公告、更新与内测校验都走这台服务器，与 Windows / Linux 版是同一套接口。")
    }
}
