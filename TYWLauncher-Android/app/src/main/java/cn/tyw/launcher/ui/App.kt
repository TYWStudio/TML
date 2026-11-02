package cn.tyw.launcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.*

/**
 * 应用根组件。结构照搬 Windows 版：顶部渐变标题栏 + 五个页面（启动/下载/联机/设置/更多），
 * 主题由 [Themes] 生成并通过 [LocalPalette] 下发给所有页面。
 */
@Composable
fun TywApp() {
    var themeIndex by remember { mutableIntStateOf(Settings.getInt("UiLauncherTheme", 0)) }
    var colorful by remember { mutableStateOf(Settings.getBool("UiBackgroundColorful", true)) }
    // 自定义配色的色相/饱和/亮度是另外存的，改完要能立刻重算，所以额外给一个版本号当 remember 的 key
    var themeRev by remember { mutableIntStateOf(0) }
    var page by remember { mutableStateOf(TywPage.LAUNCH) }
    var touchEditor by remember { mutableStateOf(false) }

    val palette = remember(themeIndex, colorful, themeRev) { Themes.palette(themeIndex) }

    // 启动时拉取公告与通道配置（没网就走离线，不弹错误）
    LaunchedEffect(Unit) { Server.refresh() }

    CompositionLocalProvider(LocalPalette provides palette) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(if (colorful) palette.windowBackground else SolidColor(Color(0xFFF5F5F5)))
        ) {
            if (touchEditor) {
                TouchEditorPage(onClose = { touchEditor = false })
            } else {
                Column(Modifier.fillMaxSize()) {
                    TywTopBar(page) { page = it }
                    Box(Modifier.weight(1f)) {
                        when (page) {
                            TywPage.LAUNCH -> LaunchPage(onOpenTouchLayout = { touchEditor = true })
                            TywPage.DOWNLOAD -> DownloadPage()
                            TywPage.LINK -> LinkPage()
                            TywPage.MORE -> MorePage()
                            TywPage.SETTINGS -> SettingsPage(
                                themeIndex = themeIndex,
                                colorful = colorful,
                                onThemeChange = {
                                    themeIndex = it
                                    themeRev++
                                    Settings.setInt("UiLauncherTheme", it)
                                },
                                onColorfulChange = {
                                    colorful = it
                                    Settings.setBool("UiBackgroundColorful", it)
                                },
                                onOpenTouchLayout = { touchEditor = true },
                            )
                        }
                    }
                }
            }
        }
    }

    NoticeDialog()
}

/** 服务端公告：同一通道同一条公告只弹一次（指纹存本地，对应 Windows 版的 NoticeShownHash）。 */
@Composable
private fun NoticeDialog() {
    val notice = Server.notice() ?: return
    val fingerprint = "${Settings.channel.key}-${notice.title}-${notice.content.hashCode()}"
    var show by remember { mutableStateOf(Settings.getString("NoticeShownHash", "") != fingerprint) }
    if (!show) return

    AlertDialog(
        onDismissRequest = {
            Settings.setString("NoticeShownHash", fingerprint)
            show = false
        },
        title = { Text(notice.title, fontSize = 15.sp) },
        text = { Text(notice.content, fontSize = 13.sp, lineHeight = 19.sp) },
        confirmButton = {
            TextButton(onClick = {
                Settings.setString("NoticeShownHash", fingerprint)
                show = false
            }) { Text("我知道了") }
        }
    )
}
