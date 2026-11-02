package cn.tyw.launcher.ui

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.SizeTransform
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.*

/** 主界面之外的整页视图：触控布局编辑器、Java 管理。 */
private enum class SubScreen { NONE, TOUCH, JAVA }

/**
 * 应用根组件。结构照搬 Windows 版：顶部渐变标题栏 + 五个页面（启动/下载/联机/设置/更多），
 * 主题由 [Themes] 生成并通过 [LocalPalette] 下发给所有页面。
 * 页面切换、子页进出都带过渡动画（Windows 版切页同样是滑动 + 淡入）。
 */
@Composable
fun TywApp() {
    var themeIndex by remember { mutableIntStateOf(Settings.getInt("UiLauncherTheme", 0)) }
    var colorful by remember { mutableStateOf(Settings.getBool("UiBackgroundColorful", true)) }
    // 自定义配色的色相/饱和/亮度是另外存的，改完要能立刻重算，所以额外给一个版本号当 remember 的 key
    var themeRev by remember { mutableIntStateOf(0) }
    var page by remember { mutableStateOf(TywPage.LAUNCH) }
    var sub by remember { mutableStateOf(SubScreen.NONE) }

    val palette = remember(themeIndex, colorful, themeRev) { Themes.palette(themeIndex) }

    // 启动时拉取公告与通道配置（没网就走离线，不弹错误）
    LaunchedEffect(Unit) { Server.refresh() }

    CompositionLocalProvider(LocalPalette provides palette) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(if (colorful) palette.windowBackground else SolidColor(Color(0xFFF5F5F5)))
        ) {
            AnimatedContent(
                targetState = sub,
                transitionSpec = {
                    val transform = if (targetState == SubScreen.NONE) {
                        // 子页退出：从左侧回退进来
                        (fadeIn(tween(220)) + slideInHorizontally(tween(240)) { -it / 7 }) togetherWith
                            (fadeOut(tween(150)) + slideOutHorizontally(tween(200)) { it / 7 })
                    } else {
                        // 子页进入：从右侧推进来
                        (fadeIn(tween(220)) + slideInHorizontally(tween(240)) { it / 7 }) togetherWith
                            (fadeOut(tween(150)) + slideOutHorizontally(tween(200)) { -it / 7 })
                    }
                    transform.using(SizeTransform(clip = false))
                },
                label = "subScreen",
            ) { target ->
                when (target) {
                    SubScreen.NONE -> MainScreen(
                        page = page,
                        onSelectPage = { page = it },
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
                        onOpenTouchLayout = { sub = SubScreen.TOUCH },
                        onOpenJavaPage = { sub = SubScreen.JAVA },
                    )

                    SubScreen.TOUCH -> TouchEditorPage(onClose = { sub = SubScreen.NONE })
                    SubScreen.JAVA -> {
                        val context = LocalContext.current
                        // 当前选中的游戏版本决定「需要哪个 Java」，每次进页面读一次即可
                        val versions = remember { Minecraft.scan(context) }
                        val selected = versions.firstOrNull {
                            it.name == Settings.getString("LaunchVersionSelect", "")
                        } ?: versions.firstOrNull()
                        JavaPage(
                            versionName = selected?.name ?: "",
                            requiredMajor = selected?.requiredJava ?: 0,
                            onClose = { sub = SubScreen.NONE },
                        )
                    }
                }
            }
        }
    }

    NoticeDialog()
}

/** 主界面：标题栏 + 五个页面，切页带水平滑动过渡。 */
@Composable
private fun MainScreen(
    page: TywPage,
    onSelectPage: (TywPage) -> Unit,
    themeIndex: Int,
    colorful: Boolean,
    onThemeChange: (Int) -> Unit,
    onColorfulChange: (Boolean) -> Unit,
    onOpenTouchLayout: () -> Unit,
    onOpenJavaPage: () -> Unit,
) {
    Column(Modifier.fillMaxSize()) {
        TywTopBar(page, onSelectPage)
        AnimatedContent(
            targetState = page,
            transitionSpec = {
                val forward = targetState.ordinal > initialState.ordinal
                val dir = if (forward) 1 else -1
                val transform = (fadeIn(tween(200)) + slideInHorizontally(tween(240)) { it / 10 * dir }) togetherWith
                    (fadeOut(tween(140)) + slideOutHorizontally(tween(200)) { -it / 10 * dir })
                transform.using(SizeTransform(clip = false))
            },
            modifier = Modifier.weight(1f),
            label = "page",
        ) { p ->
            when (p) {
                TywPage.LAUNCH -> LaunchPage(
                    onOpenTouchLayout = onOpenTouchLayout,
                    onOpenJavaPage = onOpenJavaPage,
                )

                TywPage.DOWNLOAD -> DownloadPage()
                TywPage.LINK -> LinkPage()
                TywPage.MORE -> MorePage()
                TywPage.SETTINGS -> SettingsPage(
                    themeIndex = themeIndex,
                    colorful = colorful,
                    onThemeChange = onThemeChange,
                    onColorfulChange = onColorfulChange,
                    onOpenTouchLayout = onOpenTouchLayout,
                    onOpenJavaPage = onOpenJavaPage,
                )
            }
        }
    }
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
