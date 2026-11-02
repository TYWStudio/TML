package cn.tyw.launcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.ControlKind
import cn.tyw.launcher.core.LocalPalette
import cn.tyw.launcher.core.TouchControl
import cn.tyw.launcher.core.TouchLayoutStore
import kotlin.math.min
import kotlin.math.roundToInt

/**
 * 触控布局编辑器。按 FCL 的做法把控件摆成相对屏幕的百分比坐标，
 * 这样换分辨率 / 横竖屏都不会错位；布局存成 JSON，游戏内渲染层直接复用。
 */
@Composable
fun TouchEditorPage(onClose: () -> Unit) {
    val p = LocalPalette.current
    var layout by remember { mutableStateOf(TouchLayoutStore.load()) }
    var tick by remember { mutableIntStateOf(0) }
    var selected by remember { mutableStateOf<String?>(null) }

    Column(Modifier.fillMaxSize()) {
        // 顶部操作条
        Row(
            Modifier
                .fillMaxWidth()
                .background(p.topBar)
                .padding(horizontal = 12.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                "触控布局 · ${layout.name}",
                color = Color.White,
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                modifier = Modifier.weight(1f)
            )
            TywButton("恢复默认") {
                layout = TouchLayoutStore.reset()
                selected = null
                tick++
            }
            Spacer(Modifier.width(8.dp))
            TywButton("保存", highlight = true) {
                TouchLayoutStore.save(layout)
                onClose()
            }
            Spacer(Modifier.width(8.dp))
            TywButton("返回") { onClose() }
        }

        Text(
            "拖动控件调整位置；摇杆左下、跳跃/潜行右下是 FCL 的默认键位。保存后布局写入本地设置。",
            fontSize = 11.5.sp,
            color = p.textHint,
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)
        )

        // 画布
        BoxWithConstraints(
            Modifier
                .weight(1f)
                .fillMaxWidth()
                .padding(8.dp)
                .clip(RoundedCornerShape(6.dp))
                .background(Color(0xFF2B2F36))
        ) {
            val density = LocalDensity.current
            val canvasW = constraints.maxWidth.toFloat()
            val canvasH = constraints.maxHeight.toFloat()
            val shortSide = min(canvasW, canvasH)

            layout.controls.forEach { control ->
                val sizePx = (shortSide * control.size).coerceAtLeast(36f)
                val sizeDp = with(density) { sizePx.toDp() }
                // tick 只用来在拖拽时触发重组（控件坐标是普通字段）
                key(control.id, tick) {
                    Box(
                        modifier = Modifier
                            .offset {
                                IntOffset(
                                    (control.x * canvasW - sizePx / 2).roundToInt(),
                                    (control.y * canvasH - sizePx / 2).roundToInt()
                                )
                            }
                            .size(sizeDp)
                            .pointerInput(control.id, canvasW, canvasH) {
                                detectDragGestures(
                                    onDragStart = { selected = control.id },
                                    onDrag = { change, drag ->
                                        change.consume()
                                        if (canvasW > 0 && canvasH > 0) {
                                            control.x = (control.x + drag.x / canvasW).coerceIn(0f, 1f)
                                            control.y = (control.y + drag.y / canvasH).coerceIn(0f, 1f)
                                            tick++
                                        }
                                    }
                                )
                            },
                        contentAlignment = Alignment.Center
                    ) {
                        if (control.kind == ControlKind.JOYSTICK) {
                            JoystickVisual(control, selected == control.id)
                        } else {
                            ButtonVisual(control, selected == control.id)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun JoystickVisual(control: TouchControl, selected: Boolean) {
    val p = LocalPalette.current
    Box(
        Modifier
            .fillMaxSize()
            .clip(CircleShape)
            .background(Color(0x33FFFFFF))
            .border(if (selected) 2.dp else 1.dp, if (selected) p.c4 else Color(0x66FFFFFF), CircleShape),
        contentAlignment = Alignment.Center
    ) {
        Box(
            Modifier
                .fillMaxSize(0.42f)
                .clip(CircleShape)
                .background(Color(0x66FFFFFF))
        )
        Text(control.label, fontSize = 10.sp, color = Color(0xCCFFFFFF))
    }
}

@Composable
private fun ButtonVisual(control: TouchControl, selected: Boolean) {
    val p = LocalPalette.current
    Box(
        Modifier
            .fillMaxSize()
            .clip(RoundedCornerShape(6.dp))
            .background(Color(0x33FFFFFF))
            .border(if (selected) 2.dp else 1.dp, if (selected) p.c4 else Color(0x66FFFFFF), RoundedCornerShape(6.dp)),
        contentAlignment = Alignment.Center
    ) {
        Text(control.label, fontSize = 10.sp, color = Color(0xCCFFFFFF))
    }
}
