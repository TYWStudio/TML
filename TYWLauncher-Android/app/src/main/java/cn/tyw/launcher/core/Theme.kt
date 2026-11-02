package cn.tyw.launcher.core

import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color

/** 一套配色（0-14，与 Windows 版 UiLauncherTheme 完全一致）。 */
data class ThemePreset(val name: String, val hue: Int, val sat: Int, val adjust: Int, val delta: Int)

/** 由主题推导出来的一组颜色，对应 Windows 版 Color1-8 / Bg0 / Bg1。 */
data class Palette(
    val c1: Color,
    val c2: Color,
    val c3: Color,
    val c4: Color,
    val c5: Color,
    val c6: Color,
    val c7: Color,
    val c8: Color,
    val bg0: Color,
    val bg1: Color,
    val hue: Int,
    val sat: Int,
    val adjust: Int,
    val delta: Int,
) {
    /** 顶栏渐变（滑稽彩 / 欧皇彩是彩色渐变条）。 */
    val topBar: Brush = Brush.horizontalGradient(
        listOf(
            Themes.hsl((hue - delta).toDouble(), sat.toDouble(), 48.0 + adjust),
            Themes.hsl(hue.toDouble(), sat.toDouble(), 54.0 + adjust),
            Themes.hsl((hue + delta).toDouble(), sat.toDouble(), 48.0 + adjust),
        )
    )

    /** 主页面背景：彩色背景开着时是斜向淡色渐变，关掉是 #F5F5F5。 */
    val windowBackground: Brush = Brush.linearGradient(
        listOf(
            Themes.hsl((hue - 15).toDouble(), sat * 0.8, 91.0),
            Themes.hsl(hue.toDouble(), sat * 0.8, 91.0),
            Themes.hsl((hue + 15).toDouble(), sat * 0.8, 91.0),
        )
    )

    val textMain: Color get() = c1
    val textSub: Color get() = Color(0xFF737373)
    val textHint: Color get() = Color(0xFF8C8C8C)
    val line: Color get() = Color(0xFFE6E6E6)
    val cardBackground: Color get() = Color(0xFFFFFFFF)
}

object Themes {
    val presets = listOf(
        ThemePreset("龙猫蓝", 210, 85, 0, 0),
        ThemePreset("甜柠青", 150, 65, 8, 0),
        ThemePreset("小草绿", 105, 70, 5, 0),
        ThemePreset("菠萝黄", 48, 85, 6, 0),
        ThemePreset("橡木棕", 25, 45, 2, 0),
        ThemePreset("玄素黑", 210, 0, -2, 0),
        ThemePreset("铁杆粉", 330, 80, 6, 0),
        ThemePreset("神秘紫", 270, 70, 5, 0),
        ThemePreset("秋仪金", 40, 90, 0, 0),
        ThemePreset("活跃橙", 25, 95, 5, 0),
        ThemePreset("跳票红", 350, 85, 0, 0),
        ThemePreset("极客蓝", 205, 90, -3, 0),
        ThemePreset("滑稽彩", 210, 85, 0, 120),
        ThemePreset("欧皇彩", 210, 85, 0, 120),
        ThemePreset("自定义", 210, 85, 0, 0),
    )

    const val CUSTOM = 14

    fun nameOf(index: Int): String = presets.getOrNull(index)?.name ?: presets[0].name

    /** HSL → Color。hue 角度，sat / light 为 0-100 百分比（与 Windows 版 MyColor.FromHSL2 同参数）。 */
    fun hsl(hue: Double, sat: Double, light: Double): Color {
        val h = ((hue % 360) + 360) % 360
        val s = (sat.coerceIn(0.0, 100.0)) / 100.0
        val l = (light.coerceIn(0.0, 100.0)) / 100.0

        val c = (1 - kotlin.math.abs(2 * l - 1)) * s
        val x = c * (1 - kotlin.math.abs(h / 60.0 % 2 - 1))
        val m = l - c / 2

        val (r, g, b) = when {
            h < 60 -> Triple(c, x, 0.0)
            h < 120 -> Triple(x, c, 0.0)
            h < 180 -> Triple(0.0, c, x)
            h < 240 -> Triple(0.0, x, c)
            h < 300 -> Triple(x, 0.0, c)
            else -> Triple(c, 0.0, x)
        }
        return Color(
            ((r + m).coerceIn(0.0, 1.0) * 255).toInt(),
            ((g + m).coerceIn(0.0, 1.0) * 255).toInt(),
            ((b + m).coerceIn(0.0, 1.0) * 255).toInt(),
        )
    }

    fun preview(index: Int): Pair<Color, Color> {
        val p = presets[index.coerceIn(0, presets.size - 1)]
        return hsl(p.hue.toDouble(), p.sat.toDouble(), 55.0 + p.adjust) to
            hsl(p.hue.toDouble(), p.sat.toDouble(), 91 + p.adjust * 0.1)
    }

    /**
     * 生成配色。index 为预设编号；滑稽彩/欧皇彩（12、13）每次调用取随机色相；
     * 自定义（14）从设置里读色相/饱和/亮度补偿/顶栏色相差。
     */
    fun palette(index: Int): Palette {
        val p = presets[index.coerceIn(0, presets.size - 1)]
        var hue = p.hue
        var sat = p.sat
        var adjust = p.adjust
        val delta = if (index == Settings.getInt("UiLauncherTheme", 0) && (index == 12 || index == 13)) 120 else p.delta

        if (index == 12 || index == 13) {
            hue = (System.currentTimeMillis() / 1000 % 360).toInt()
        }
        if (index == CUSTOM) {
            hue = Settings.getInt("UiLauncherHue", 210).coerceIn(0, 359)
            sat = Settings.getInt("UiLauncherSat", 85).coerceIn(0, 100)
            adjust = Settings.getInt("UiLauncherLight", 0).coerceIn(-30, 30)
        }
        val d = if (index == CUSTOM) Settings.getInt("UiLauncherDelta", 0).coerceIn(0, 180) else delta

        return Palette(
            c1 = hsl(hue.toDouble(), sat * 0.2, 25 + adjust * 0.3),
            c2 = hsl(hue.toDouble(), sat.toDouble(), 45.0 + adjust),
            c3 = hsl(hue.toDouble(), sat.toDouble(), 55.0 + adjust),
            c4 = hsl(hue.toDouble(), sat.toDouble(), 65.0 + adjust),
            c5 = hsl(hue.toDouble(), sat.toDouble(), 80 + adjust * 0.4),
            c6 = hsl(hue.toDouble(), sat.toDouble(), 91 + adjust * 0.1),
            c7 = hsl(hue.toDouble(), sat.toDouble(), 95.0),
            c8 = hsl(hue.toDouble(), sat.toDouble(), 97.0),
            bg0 = hsl(hue.toDouble(), sat * 0.8, 88.0),
            bg1 = hsl(hue.toDouble(), sat.toDouble(), 95.0),
            hue = hue,
            sat = sat,
            adjust = adjust,
            delta = d,
        )
    }
}

val LocalPalette = compositionLocalOf { Themes.palette(0) }
