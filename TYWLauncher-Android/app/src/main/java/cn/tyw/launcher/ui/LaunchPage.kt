package cn.tyw.launcher.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
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

/** 启动页。对应 Windows 版 PageLaunch：左边账户与版本，中间大按钮启动。 */
@Composable
fun LaunchPage(onOpenTouchLayout: () -> Unit) {
    val context = LocalContext.current
    val p = LocalPalette.current

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

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        // ===== 账户（对应 Windows 版左栏 MySkin 卡片） =====
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
                TywButton("切换", highlight = true) { showAccountEditor = true }
            }
            HintText("Android 版沿用 Windows 版的账户体系：正版账户走微软登录，离线账户只改名字。")
        }

        // ===== 版本选择 =====
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

        // ===== 启动按钮 =====
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

        // ===== 触控布局入口（FCL 风格布局编辑器） =====
        TywCard("触控布局") {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 6.dp)) {
                Column(Modifier.weight(1f)) {
                    Text("游戏内触控按键", fontSize = 13.sp, color = p.c1)
                    Text("摇杆 / 跳跃 / 潜行 / 攻击 / 使用，可拖动调整", fontSize = 11.5.sp, color = p.textHint)
                }
                TywButton("编辑布局", highlight = true, onClick = onOpenTouchLayout)
            }
        }

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
            HintText("公告、更新与内测校验都走这台服务器，与 Windows / Linux 版是同一套接口。")
        }
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
            title = { Text("切换账户", fontSize = 15.sp) },
            text = {
                Column {
                    OutlinedTextField(
                        value = name,
                        onValueChange = { name = it },
                        label = { Text("离线用户名") },
                        singleLine = true
                    )
                    HintText("默认离线登录。微软正版登录与 Windows 版共用同一个 OAuth 应用，接入后自动同步皮肤。")
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
                        "窗口大小 / JVM 参数在 Android 上由触控渲染层接管，这里沿用 Windows 版的默认值。",
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
            title = { Text("启动还需要 Java 运行时", fontSize = 15.sp) },
            text = {
                Column {
                    Text(
                        "Android 版已经接入与 Windows 版一致的版本识别与启动参数生成，" +
                            "但运行 Minecraft 需要内置的 Android 版 JRE（Pojav/FCL 那一套 Java 运行时 + LWJGL 适配层）。" +
                            "这一层还没打进包里，所以现在点启动只会显示参数预览。",
                        fontSize = 12.5.sp,
                        lineHeight = 18.sp
                    )
                    Spacer(Modifier.height(8.dp))
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
