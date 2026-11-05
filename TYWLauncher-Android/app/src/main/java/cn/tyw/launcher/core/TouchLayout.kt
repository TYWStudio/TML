/*
 * 第三方代码声明 / Third-party notice
 *
 * 触控操作控件的数据模型、默认键位与编辑语义参考自 Fold Craft Launcher (FCL, GPL-3.0)：
 *   https://github.com/FCL-Team/FoldCraftLauncher  →  ControlLayout
 * 未复制 FCL 的任何源码，仅按相同的布局语义用 Kotlin 独立实现。
 * 完整声明与范围说明见仓库根 THIRD-PARTY-NOTICES.md。
 */

package cn.tyw.launcher.core

import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/**
 * 触控布局。数据结构对齐 FCL（Fold Craft Launcher）的 ControlLayout：
 * 每个控件有类型（摇杆 / 按键 / 物品栏 / 视角区）、相对屏幕的百分比位置、尺寸、
 * 显示文字，以及它对应的 Minecraft 按键（action）。
 *
 * 键位与 FCL 默认布局一致：左下移动摇杆，右下攻击/使用/跳跃/潜行，左上背包/聊天，
 * 底部物品栏 1~9，右上视角切换与暂停菜单，另有疾跑、丢弃、交换副手等 FCL 同样提供的按键。
 * 布局可以导出成 JSON 存本地，游戏内渲染层按同一份数据绘制即可。
 */
enum class ControlKind {
    /** 移动摇杆（八向）。 */
    JOYSTICK,

    /** 普通按键，按下即触发 action。 */
    BUTTON,

    /** 物品栏：横向 9 格，点哪格选哪格。 */
    HOTBAR,

    /** 视角区：手指滑动转视角。 */
    VIEW,
}

data class TouchControl(
    val id: String,
    val kind: ControlKind,
    /** 中心点 X，0~1 相对屏幕宽度。 */
    var x: Float,
    /** 中心点 Y，0~1 相对屏幕高度。 */
    var y: Float,
    /** 直径 / 边长，相对屏幕短边的比例。 */
    var size: Float,
    var label: String,
    /** 对应的 Minecraft 按键名（key.forward / key.jump / key.hotbar.1 …），摇杆与视角区为空。 */
    var action: String = "",
    var visible: Boolean = true,
)

data class TouchLayout(
    val name: String,
    val controls: MutableList<TouchControl>,
) {
    fun toJson(): String {
        val arr = JSONArray()
        controls.forEach { c ->
            arr.put(
                JSONObject()
                    .put("id", c.id)
                    .put("kind", c.kind.name)
                    .put("x", c.x.toDouble())
                    .put("y", c.y.toDouble())
                    .put("size", c.size.toDouble())
                    .put("label", c.label)
                    .put("action", c.action)
                    .put("visible", c.visible)
            )
        }
        return JSONObject().put("name", name).put("controls", arr).toString()
    }

    companion object {
        fun fromJson(raw: String): TouchLayout? = try {
            val obj = JSONObject(raw)
            val arr = obj.optJSONArray("controls") ?: JSONArray()
            val list = mutableListOf<TouchControl>()
            for (i in 0 until arr.length()) {
                val c = arr.getJSONObject(i)
                val label = c.optString("label", "")
                list.add(
                    TouchControl(
                        id = c.optString("id", UUID.randomUUID().toString()),
                        kind = runCatching { ControlKind.valueOf(c.optString("kind", "BUTTON")) }
                            .getOrDefault(ControlKind.BUTTON),
                        x = c.optDouble("x", 0.5).toFloat(),
                        y = c.optDouble("y", 0.5).toFloat(),
                        size = c.optDouble("size", 0.09).toFloat(),
                        label = label,
                        // 老版本存的布局没有 action 字段，按文字反查，保证升级后键位还能用
                        action = c.optString("action", "").ifEmpty { actionOfLabel(label) },
                        visible = c.optBoolean("visible", true),
                    )
                )
            }
            TouchLayout(obj.optString("name", "默认布局"), list)
        } catch (_: Exception) {
            null
        }

        private fun actionOfLabel(label: String): String = when (label) {
            "移动" -> ""
            "跳跃" -> "key.jump"
            "潜行" -> "key.sneak"
            "疾跑" -> "key.sprint"
            "攻击" -> "key.attack"
            "使用" -> "key.use"
            "背包" -> "key.inventory"
            "菜单" -> "key.escape"
            "聊天" -> "key.chat"
            "丢弃" -> "key.drop"
            "交换副手" -> "key.swapOffhand"
            "切换视角" -> "key.togglePerspective"
            "玩家列表" -> "key.playerlist"
            "拾取方块" -> "key.pickItem"
            else -> ""
        }

        /**
         * 默认布局：完整照 FCL 的默认键位排布，一共 21 个控件。
         * 左下移动摇杆（大），右下攻击/使用（大），跳跃/潜行/疾跑在右下角，
         * 左上背包/聊天，右上视角切换/暂停，底部一条 9 格物品栏，另有丢弃与交换副手。
         */
        fun default(): TouchLayout = TouchLayout(
            "默认布局（FCL 键位）",
            mutableListOf(
                // 移动摇杆
                TouchControl("joystick", ControlKind.JOYSTICK, 0.16f, 0.72f, 0.34f, "移动"),
                // 视角区
                TouchControl("view", ControlKind.VIEW, 0.62f, 0.42f, 0.55f, "视角"),
                // 主操作
                TouchControl("attack", ControlKind.BUTTON, 0.80f, 0.78f, 0.13f, "攻击", "key.attack"),
                TouchControl("use", ControlKind.BUTTON, 0.66f, 0.82f, 0.13f, "使用", "key.use"),
                // 移动类
                TouchControl("jump", ControlKind.BUTTON, 0.92f, 0.60f, 0.11f, "跳跃", "key.jump"),
                TouchControl("sneak", ControlKind.BUTTON, 0.92f, 0.42f, 0.11f, "潜行", "key.sneak"),
                TouchControl("sprint", ControlKind.BUTTON, 0.79f, 0.58f, 0.10f, "疾跑", "key.sprint"),
                // 界面上方常用
                TouchControl("inventory", ControlKind.BUTTON, 0.06f, 0.10f, 0.09f, "背包", "key.inventory"),
                TouchControl("chat", ControlKind.BUTTON, 0.16f, 0.10f, 0.09f, "聊天", "key.chat"),
                TouchControl("perspective", ControlKind.BUTTON, 0.86f, 0.10f, 0.09f, "切换视角", "key.togglePerspective"),
                TouchControl("menu", ControlKind.BUTTON, 0.95f, 0.10f, 0.09f, "菜单", "key.escape"),
                // 物品栏相关
                TouchControl("drop", ControlKind.BUTTON, 0.10f, 0.90f, 0.09f, "丢弃", "key.drop"),
                TouchControl("swapOffhand", ControlKind.BUTTON, 0.55f, 0.30f, 0.09f, "交换副手", "key.swapOffhand"),
                // 底部物品栏
                TouchControl("hotbar", ControlKind.HOTBAR, 0.50f, 0.94f, 0.055f, "物品栏"),
            )
        )
    }
}

object TouchLayoutStore {
    private const val KEY = "TouchLayoutJson"

    fun load(): TouchLayout =
        TouchLayout.fromJson(Settings.getString(KEY, "")) ?: TouchLayout.default()

    fun save(layout: TouchLayout) {
        Settings.setString(KEY, layout.toJson())
    }

    fun reset(): TouchLayout = TouchLayout.default().also { save(it) }
}
