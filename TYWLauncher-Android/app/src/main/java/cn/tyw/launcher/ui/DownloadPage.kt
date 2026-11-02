package cn.tyw.launcher.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.*
import kotlinx.coroutines.launch

/** 下载页。对应 Windows 版 PageDownload 的原版游戏页：只有正式版 / 快照版，没有基岩版。 */
@Composable
fun DownloadPage() {
    val context = LocalContext.current
    val p = LocalPalette.current
    val scope = rememberCoroutineScope()

    var loading by remember { mutableStateOf(true) }
    var installing by remember { mutableStateOf("") }
    var progress by remember { mutableFloatStateOf(0f) }
    var stage by remember { mutableStateOf("") }

    LaunchedEffect(Unit) {
        Mojang.loadManifest()
        loading = false
    }

    fun install(name: String, url: String) {
        if (installing.isNotEmpty()) return
        if (url.isEmpty()) {
            Toast.makeText(context, "拿不到 $name 的下载地址，请检查网络", Toast.LENGTH_SHORT).show()
            return
        }
        installing = name
        progress = 0f
        scope.launch {
            val ok = Mojang.install(context, name, url) { v, s ->
                progress = v.toFloat()
                stage = s
            }
            installing = ""
            Toast.makeText(context, if (ok) "$name 安装完成" else "$name 安装失败，已清理残留", Toast.LENGTH_LONG).show()
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        TywCard("原版游戏") {
            if (loading) {
                Row(Modifier.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("正在获取版本清单……", fontSize = 12.5.sp, color = p.textHint)
                }
            } else {
                listOf(
                    Triple("正式版", Mojang.latestReleaseId, Mojang.latestReleaseUrl),
                    Triple("快照版", Mojang.latestSnapshotId, Mojang.latestSnapshotUrl),
                ).forEach { (label, id, url) ->
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .padding(vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text(label, fontSize = 13.sp, color = p.c1, fontWeight = FontWeight.Medium)
                            Text(
                                id.ifEmpty { "获取失败" },
                                fontSize = 11.5.sp,
                                color = p.textHint
                            )
                        }
                        if (installing == id && id.isNotEmpty()) {
                            Text("${(progress * 100).toInt()}%", fontSize = 12.sp, color = p.c2)
                        } else {
                            TywButton(
                                if (Minecraft.isInstalled(context, id)) "重装" else "安装",
                                highlight = true,
                                enabled = id.isNotEmpty() && installing.isEmpty()
                            ) { install(id, url) }
                        }
                    }
                }
                if (installing.isNotEmpty()) {
                    LinearProgressIndicator(progress = { progress }, modifier = Modifier.fillMaxWidth())
                    Text(stage, fontSize = 11.sp, color = p.textHint, modifier = Modifier.padding(top = 4.dp))
                }
                HintText("安装内容：版本 JSON、客户端 jar 与 libraries。assets 与运行时由后续的 Java 运行环境统一处理。")
            }
        }

        TywCard("基岩版") {
            SettingRow("基岩版 正式版") { ValueText("Android 版不提供") }
            SettingRow("基岩版 预览版") { ValueText("Android 版不提供") }
            HintText("和 Windows / Linux 版一致：TYW Launcher 只做 Java 版，基岩版请使用系统微软商店或 LeviLauncher。")
        }

        TywCard("模组 / 整合包") {
            SettingRow("模组管理") { ValueText("待接入") }
            HintText("Android 版的模组安装会在 Java 运行时接入后一并开放，接口与 Windows 版保持同一套版本隔离规则。")
        }
    }
}
