package cn.tyw.launcher.core

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.widget.Toast

/**
 * 剪贴板。Windows 版用 My.Computer.Clipboard.SetText，这里对应 ClipboardManager，
 * 复制成功一律给一条 Toast，和 Windows 版「已成功复制！」的提示保持一致。
 */
object Clip {
    private var app: Context? = null

    fun init(context: Context) {
        app = context.applicationContext
    }

    fun copy(text: String, label: String = "文本"): Boolean {
        val ctx = app ?: return false
        if (text.isEmpty()) return false
        return try {
            val cm = ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cm.setPrimaryClip(ClipData.newPlainText(label, text))
            Toast.makeText(ctx, "已成功复制！", Toast.LENGTH_SHORT).show()
            true
        } catch (e: Exception) {
            Toast.makeText(ctx, "复制失败：${e.message}", Toast.LENGTH_SHORT).show()
            false
        }
    }
}
