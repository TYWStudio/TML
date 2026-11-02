package cn.tyw.launcher.core

import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/**
 * 触控布局。数据结构参考 FCL（Fold Craft Launcher）的 ControlLayout：
 * 每个控件有类型（摇杆 / 按钮）、位置（相对屏幕宽高的百分比）、尺寸与显示文字，
 * 布局本身可以整体导出成 JSON 存到本地，游戏内渲染层只要按同一份数据画即可。
 */
enum class ControlKind { JOYSTICK, BUTTON }

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
                list.add(
                    TouchControl(
                        id = c.optString("id", UUID.randomUUID().toString()),
                        kind = runCatching { ControlKind.valueOf(c.optString("kind", "BUTTON")) }
                            .getOrDefault(ControlKind.BUTTON),
                        x = c.optDouble("x", 0.5).toFloat(),
                        y = c.optDouble("y", 0.5).toFloat(),
                        size = c.optDouble("size", 0.09).toFloat(),
                        label = c.optString("label", ""),
                        visible = c.optBoolean("visible", true),
                    )
                )
            }
            TouchLayout(obj.optString("name", "默认布局"), list)
        } catch (_: Exception) {
            null
        }

        /**
         * 默认布局，键位参照 FCL 的「默认触控布局」：
         * 左下摇杆，右侧潜行/跳跃，右下攻击/使用，左上是菜单。
         */
        fun default(): TouchLayout = TouchLayout(
            "默认布局",
            mutableListOf(
                TouchControl("joystick", ControlKind.JOYSTICK, 0.14f, 0.76f, 0.30f, "移动"),
                TouchControl("jump", ControlKind.BUTTON, 0.88f, 0.78f, 0.11f, "跳跃"),
                TouchControl("sneak", ControlKind.BUTTON, 0.88f, 0.60f, 0.11f, "潜行"),
                TouchControl("attack", ControlKind.BUTTON, 0.73f, 0.82f, 0.12f, "攻击"),
                TouchControl("use", ControlKind.BUTTON, 0.73f, 0.62f, 0.12f, "使用"),
                TouchControl("inventory", ControlKind.BUTTON, 0.06f, 0.12f, 0.10f, "背包"),
                TouchControl("menu", ControlKind.BUTTON, 0.17f, 0.12f, 0.10f, "菜单"),
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
