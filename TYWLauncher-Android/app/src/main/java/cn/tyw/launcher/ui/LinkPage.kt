package cn.tyw.launcher.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import cn.tyw.launcher.core.AppInfo
import cn.tyw.launcher.core.LocalPalette
import cn.tyw.launcher.core.Server
import cn.tyw.launcher.core.Settings

/** 联机页。EasyTier 的房间互通由 Windows 版负责，Android 端先展示同一套节点与设备码。 */
@Composable
fun LinkPage() {
    val p = LocalPalette.current
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        TywCard("联机方式") {
            SettingRow("EasyTier") { ValueText("与 Windows 版房间互通") }
            CopyRow("设备码", Settings.deviceCode)
            SettingRow("服务端") { ValueText(if (Server.online) "已连接" else "离线") }
            HintText("Android 端的 EasyTier 客户端会在运行时接入后开放，节点列表与 Windows 版共用服务端配置。")
        }
        TywCard("房间") {
            SettingRow("创建房间") { ValueText("待接入") }
            SettingRow("加入房间") { ValueText("待接入") }
            HintText("联机功能依赖虚拟网卡，Android 上需要 VpnService 权限，接入时会单独申请。")
        }
        TywCard("当前节点") {
            Text(
                "${AppInfo.SERVER_ADDRESS} 提供房间发现与公告，当前通道：${AppInfo.channel.display}",
                fontSize = 12.sp,
                color = p.textSub,
                lineHeight = 18.sp
            )
        }
    }
}

/** 更多页。 */
@Composable
fun MorePage() {
    val p = LocalPalette.current
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        TywCard("关于 TYW Launcher") {
            Text("TYW Launcher for Android", fontSize = 14.sp, color = p.c1)
            Text("版本 ${AppInfo.versionName}（${AppInfo.channel.display}）", fontSize = 12.sp, color = p.textSub)
            HintText("与 Windows / Linux 版同一套 UI、同一套主题、同一套服务端接口与三通道发布流程。")
        }
        TywCard("开源与致谢") {
            Text(
                "触控布局的数据结构、默认键位与操作逻辑参考 Fold Craft Launcher（FCL，GPL-3.0），" +
                    "启动参数生成沿用 Windows 版 TYW Launcher 的实现，UI 配色与布局对齐 Windows 版。" +
                    "本启动器只支持 Java 版，不提供基岩版。详见仓库 README。",
                fontSize = 12.sp,
                color = p.textSub,
                lineHeight = 18.sp
            )
        }
        TywCard("设备信息") {
            CopyRow("设备码", Settings.deviceCode, hint = "与 Windows 版同样的 XXXX-XXXX-XXXX-XXXX 格式。")
            SettingRow("通道") { ValueText(AppInfo.channel.display) }
            SettingRow("服务器状态") { ValueText(if (Server.online) "在线" else "离线：${Server.lastError}") }
        }
    }
}
