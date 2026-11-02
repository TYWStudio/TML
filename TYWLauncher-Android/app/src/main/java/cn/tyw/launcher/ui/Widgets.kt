package cn.tyw.launcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.LocalPalette

/** 页面标识，顺序与 Windows 版标题栏的 启动/下载/联机/设置/更多 一致。 */
enum class TywPage(val title: String) {
    LAUNCH("启动"), DOWNLOAD("下载"), LINK("联机"), SETTINGS("设置"), MORE("更多")
}

/**
 * 顶部渐变标题栏：左侧 TYW 标志，中间导航（对应 Windows 版 FrmMain.PanTitle）。
 */
@Composable
fun TywTopBar(current: TywPage, onSelect: (TywPage) -> Unit) {
    val p = LocalPalette.current
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(52.dp)
            .background(p.topBar)
    ) {
        Text(
            text = "TYW",
            color = Color.White,
            fontSize = 17.sp,
            fontWeight = FontWeight.Bold,
            modifier = Modifier
                .align(Alignment.CenterStart)
                .padding(start = 16.dp)
        )
        Row(
            modifier = Modifier
                .align(Alignment.Center)
                .padding(start = 8.dp),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TywPage.entries.forEach { page ->
                val active = page == current
                Box(
                    modifier = Modifier
                        .padding(horizontal = 3.dp)
                        .clip(RoundedCornerShape(13.dp))
                        .background(if (active) Color(0x33FFFFFF) else Color.Transparent)
                        .clickable { onSelect(page) }
                        .padding(horizontal = 10.dp, vertical = 6.dp)
                ) {
                    Text(
                        text = page.title,
                        color = if (active) Color.White else Color(0xCCFFFFFF),
                        fontSize = 13.sp,
                        fontWeight = if (active) FontWeight.Medium else FontWeight.Normal,
                    )
                }
            }
        }
    }
}

/** 卡片：白底、圆角 6、标题栏（对应 Windows 版 PclCard）。 */
@Composable
fun TywCard(
    title: String? = null,
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    val p = LocalPalette.current
    Column(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(6.dp))
            .background(p.cardBackground)
            .padding(bottom = 10.dp)
    ) {
        if (title != null) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(start = 15.dp, top = 11.dp, end = 15.dp, bottom = 5.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    Modifier
                        .size(width = 3.dp, height = 13.dp)
                        .clip(RoundedCornerShape(2.dp))
                        .background(p.c3)
                )
                Spacer(Modifier.width(7.dp))
                Text(title, fontSize = 13.5.sp, fontWeight = FontWeight.Medium, color = p.c1)
            }
        }
        Column(
            modifier = Modifier.padding(horizontal = 15.dp),
            content = content
        )
    }
}

/** 一行设置项：左标题 + 右侧内容。 */
@Composable
fun SettingRow(
    title: String,
    modifier: Modifier = Modifier,
    trailing: @Composable () -> Unit,
) {
    val p = LocalPalette.current
    Row(
        modifier = modifier
            .fillMaxWidth()
            .heightIn(min = 36.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, fontSize = 12.5.sp, color = p.c1, modifier = Modifier.weight(1f))
        trailing()
    }
}

/** Windows 版同款按钮：圆角 3、1px 描边，高亮为 C3 描边 + C7 底。 */
@Composable
fun TywButton(
    text: String,
    modifier: Modifier = Modifier,
    highlight: Boolean = false,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    val p = LocalPalette.current
    val border = if (highlight) p.c3 else p.c1
    Box(
        modifier = modifier
            .height(32.dp)
            .clip(RoundedCornerShape(3.dp))
            .background(if (highlight) p.c7 else Color(0xBBFFFFFF))
            .border(1.dp, if (enabled) border else Color(0xFFCCCCCC), RoundedCornerShape(3.dp))
            .clickable(enabled = enabled) { onClick() }
            .padding(horizontal = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text,
            fontSize = 13.sp,
            color = if (enabled) border else Color(0xFFA6A6A6),
            textAlign = TextAlign.Center,
        )
    }
}

/** 次要说明文字。 */
@Composable
fun HintText(text: String, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    Text(
        text,
        fontSize = 11.5.sp,
        color = p.textHint,
        lineHeight = 16.sp,
        modifier = modifier.padding(top = 4.dp),
    )
}

@Composable
fun ValueText(text: String, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    Text(text, fontSize = 12.5.sp, color = p.textSub, modifier = modifier)
}

/** 页面统一的滚动容器内边距。 */
@Composable
fun PageColumn(content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
        content = content,
    )
}

/** 主题预览小色块（设置页配色选择用）。 */
@Composable
fun ThemeSwatch(
    index: Int,
    selected: Boolean,
    colors: Pair<Color, Color>,
    onClick: () -> Unit,
) {
    val p = LocalPalette.current
    Box(
        modifier = Modifier
            .size(38.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(Brush.linearGradient(listOf(colors.first, colors.second)))
            .border(
                width = if (selected) 2.dp else 1.dp,
                color = if (selected) p.c1 else Color.Transparent,
                shape = RoundedCornerShape(6.dp),
            )
            .clickable { onClick() }
    )
}
