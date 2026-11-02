package cn.tyw.launcher.ui

import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.slideInVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
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
 * Java 管理页（对应 Windows 版「设置 → 启动 → Java 列表」）。
 * 上半是已装运行时，下半是 Java 8/11/16/17/21/25 的可安装列表，
 * 顶部还会按当前选中的游戏版本标出「这个版本需要哪个 Java」。
 */
@Composable
fun JavaPage(
    versionName: String,
    requiredMajor: Int,
    onClose: () -> Unit,
) {
    val context = LocalContext.current
    val p = LocalPalette.current
    val scope = rememberCoroutineScope()

    var runtimes by remember { mutableStateOf(JavaRuntime.scan()) }
    var current by remember { mutableStateOf(JavaRuntime.pick(requiredMajor)) }
    var busyMajor by remember { mutableIntStateOf(0) }
    var progress by remember { mutableFloatStateOf(0f) }
    var status by remember { mutableStateOf("") }
    var importing by remember { mutableStateOf(false) }
    var appeared by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { appeared = true }

    fun refresh() {
        runtimes = JavaRuntime.scan()
        current = JavaRuntime.pick(requiredMajor)
    }

    fun install(major: Int) {
        if (busyMajor != 0 || importing) return
        busyMajor = major
        progress = 0f
        status = "准备下载 Java $major…"
        scope.launch {
            val r = JavaRuntime.install(
                major,
                progress = { progress = it.toFloat() },
                status = { status = it },
            )
            busyMajor = 0
            refresh()
            Toast.makeText(context, r.message, Toast.LENGTH_LONG).show()
        }
    }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri == null) return@rememberLauncherForActivityResult
        importing = true
        status = "正在导入运行时包…"
        scope.launch {
            val r = JavaRuntime.importArchive(uri, uri.lastPathSegment ?: "")
            importing = false
            refresh()
            Toast.makeText(context, r.message, Toast.LENGTH_LONG).show()
        }
    }

    val progressAnim by animateFloatAsState(progress, tween(220), label = "javaProgress")
    val busy = busyMajor != 0 || importing
    val needText = JavaRuntime.requirementText(requiredMajor)
    val mismatch = requiredMajor > 0 && current != null && current!!.major < requiredMajor

    AnimatedVisibility(
        visible = appeared,
        enter = fadeIn(tween(220)) + slideInVertically(tween(240)) { it / 14 },
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(12.dp)
                .animateContentSize(tween(200)),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            // ===== 顶部返回栏 =====
            Row(verticalAlignment = Alignment.CenterVertically) {
                TywButton("返回") { onClose() }
                Spacer(Modifier.width(10.dp))
                Text("Java 运行环境", fontSize = 15.sp, fontWeight = FontWeight.Medium, color = p.c1)
                Spacer(Modifier.weight(1f))
                TywButton("导入安装包", highlight = true, enabled = !busy) {
                    picker.launch(arrayOf("*/*"))
                }
            }

            // ===== 当前使用 =====
            TywCard("当前使用") {
                SettingRow("游戏版本") {
                    Text(
                        (versionName.ifEmpty { "未选择" }) +
                            if (requiredMajor > 0) " · 需要 $needText" else "",
                        fontSize = 12.5.sp,
                        color = p.textSub
                    )
                }
                SettingRow("已选 Java") {
                    Text(
                        current?.display ?: "未检测到",
                        fontSize = 12.5.sp,
                        color = if (current == null) p.textHint else p.c2
                    )
                }
                current?.let {
                    Text(it.path, fontSize = 11.sp, color = p.textHint, lineHeight = 15.sp)
                }
                if (mismatch) {
                    Text(
                        "当前 Java 低于该版本要求（需要 $needText），启动会失败，建议先安装或切换。",
                        fontSize = 11.5.sp,
                        color = Color(0xFFD9534F),
                        modifier = Modifier.padding(top = 4.dp),
                        lineHeight = 16.sp
                    )
                }
                if (busy) {
                    Spacer(Modifier.height(6.dp))
                    LinearProgressIndicator(progress = { progressAnim }, modifier = Modifier.fillMaxWidth())
                    Text(
                        status + if (busyMajor != 0) " ${(progress * 100).toInt()}%" else "",
                        fontSize = 11.sp,
                        color = p.textHint,
                        modifier = Modifier.padding(top = 4.dp)
                    )
                }
                Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TywButton(
                        if (requiredMajor > 0) "补全 $needText" else "自动补全 Java",
                        highlight = true,
                        enabled = !busy
                    ) {
                        busyMajor = requiredMajor
                        progress = 0f
                        status = "检查已安装的运行时…"
                        scope.launch {
                            val r = JavaRuntime.ensure(
                                required = requiredMajor,
                                progress = { progress = it.toFloat() },
                                status = { status = it },
                            )
                            busyMajor = 0
                            refresh()
                            Toast.makeText(context, r.message, Toast.LENGTH_LONG).show()
                        }
                    }
                    TywButton("重新扫描", enabled = !busy) { refresh() }
                }
                HintText(
                    if (requiredMajor > 0)
                        "补全会优先复用已有的 $needText，不会重复下载其他版本。"
                    else
                        "补全会先复用已装运行时，没有才去服务端下载。"
                )
            }

            // ===== 已安装列表 =====
            TywCard("已安装的 Java（${runtimes.size}）") {
                if (runtimes.isEmpty()) {
                    HintText("还没有可用的 Java。可以在下面的列表里下载，或用右上角「导入安装包」装自己的 JRE。")
                }
                runtimes.forEach { rt ->
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .padding(vertical = 7.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Box(
                            Modifier
                                .size(34.dp)
                                .clip(RoundedCornerShape(4.dp))
                                .background(if (rt.path == current?.path) p.c3 else p.c7)
                                .border(1.dp, p.c5, RoundedCornerShape(4.dp)),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(
                                if (rt.major > 0) "${rt.major}" else "?",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (rt.path == current?.path) Color.White else p.c2
                            )
                        }
                        Spacer(Modifier.width(9.dp))
                        Column(Modifier.weight(1f)) {
                            Text(
                                "${rt.display} · ${rt.name}",
                                fontSize = 13.sp,
                                color = p.c1,
                                fontWeight = FontWeight.Medium
                            )
                            Text(rt.path, fontSize = 10.5.sp, color = p.textHint, maxLines = 1)
                        }
                        if (rt.path == current?.path) {
                            Text("使用中", fontSize = 11.sp, color = p.c2)
                        } else {
                            TywButton("设为默认", enabled = !busy) {
                                JavaRuntime.select(rt)
                                refresh()
                            }
                        }
                        Spacer(Modifier.width(6.dp))
                        TywButton(
                            "移除",
                            enabled = !busy && (rt.managed || rt.path == current?.path)
                        ) {
                            JavaRuntime.remove(rt)
                            refresh()
                        }
                    }
                }
            }

            // ===== 可安装版本列表 =====
            TywCard("可安装的 Java 版本") {
                JavaRuntime.candidates().forEach { c ->
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .padding(vertical = 7.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text(c.label, fontSize = 13.sp, color = p.c1, fontWeight = FontWeight.Medium)
                            Text(
                                if (c.isInstalled) "已安装：${c.installed?.display}" else "未安装",
                                fontSize = 11.sp,
                                color = if (c.isInstalled) p.c2 else p.textHint
                            )
                        }
                        when {
                            busyMajor == c.major -> Text(
                                "${(progress * 100).toInt()}%",
                                fontSize = 12.sp,
                                color = p.c2
                            )

                            c.isInstalled -> TywButton("设为默认") {
                                c.installed?.let { JavaRuntime.select(it) }
                                refresh()
                            }

                            else -> TywButton("下载", highlight = true, enabled = !busy) { install(c.major) }
                        }
                    }
                }
                HintText(
                    "列表覆盖 Java 8 / 11 / 16 / 17 / 21 / 25，下载地址由服务端 config.json 的 Link.JavaAndroid 下发；" +
                        "服务端没配时按 www/java-android-<大版本>-<架构>.zip 找，也可以用「导入安装包」装本地 zip / tar.gz。"
                )
            }

            // ===== 版本要求对照 =====
            TywCard("版本要求对照") {
                Text("1.16.5 及更早 → Java 8", fontSize = 12.sp, color = p.textSub)
                Text("1.17.x → Java 16", fontSize = 12.sp, color = p.textSub)
                Text("1.18 ~ 1.20.4 → Java 17", fontSize = 12.sp, color = p.textSub)
                Text("1.20.5 及更新 → Java 21", fontSize = 12.sp, color = p.textSub)
                HintText("优先读版本 JSON 里的 javaVersion.majorVersion，没写才按版本号和发布时间推断，与 Windows 版判定顺序一致。")
            }
        }
    }
}
