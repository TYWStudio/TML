using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using TYW.Desktop.Controls;
using TYW;

namespace TYW.Desktop.Views;

/// <summary>
/// 启动时的服务端流程。对应 Windows 版 ModSecret.ServerLoader：
/// 封禁查询 → 公告弹窗（同一条只弹一次）→ 云控配置 → 检查更新 → 补报离线上报。
/// 没网 / 连不上服务器不算错误：不弹框、不上报，只左下角提示，启动器照常离线使用。
/// </summary>
public static class ModStartup
{
    private static Window _owner;

    public static void Run(Window owner)
    {
        _owner = owner;
        ModLog.Info("启动器通道：" + ModApp.ChannelKey + "（" + ModApp.VersionBaseName + "，版本码 " + ModApp.VersionCode + "）");
        Task.Run(async () =>
        {
            // 1) 封禁查询：被封禁 → 强提示 → 退出（网络失败放行）
            var reason = ModSecret.CheckDeviceBanned();
            if (reason != null)
            {
                var exit = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await PclMsgBox.Show(_owner, "无法启动",
                        "你的设备已被服务端封禁，无法使用 TYW Launcher。\n\n" +
                        "封禁原因：" + (string.IsNullOrWhiteSpace(reason) ? "管理员未填写原因" : reason) +
                        "\n\n设备码：" + ModSecret.DeviceCode +
                        "\n如有疑问请联系服主。", "退出");
                });
                await exit;
                Environment.Exit(0);
                return;
            }

            // 2) 拉公告 + 云控 + 联机配置
            var ok = ModServer.Refresh();
            if (!ok)
            {
                Hint("无法连接服务器，已进入离线模式。", false);
                return;
            }

            // 已连上服务端：补报离线缓存
            ModSecret.FlushPendingReports();

            // 3) 内测通道联网校验（对应 Windows 版 ModBeta.CheckUsable）：连不上或内测码失效 → 拒绝使用
            if (ModApp.Channel == LauncherChannel.Beta)
            {
                ModLog.Info("内测通道：开始联网校验内测码");
                var beta = await Task.Run(() => ModBeta.CheckUsable());
                ModLog.Info("内测通道校验结果：ok=" + beta.Ok + " " + beta.Message);
                if (!beta.Ok)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        PclMsgBox.Show(_owner, "无法启动", beta.Message + "\n\n设备码：" + ModSecret.DeviceCode, "退出"));
                    Environment.Exit(0);
                    return;
                }
            }

            // 4) 云控
            ModServer.ApplyCloudConfig();

            // 5) 检查更新：确认后自动下载并替换重启
            var upd = ModServer.CheckUpdate();
            if (upd.HasValue)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    StaticPage.UpdateFlow(upd.Value.Code, upd.Value.Url));
                return; // 更新后本轮不再弹公告，下次启动再看
            }

            // 6) 公告弹窗：同一条只自动弹一次；例行发布通知不弹
            var n = ModServer.GetNotice();
            if (n != null && !string.IsNullOrWhiteSpace(n.Content) &&
                !IsGenericReleaseNotice(n.Content) &&
                ModServer.ShouldShowNotice(n.Title, n.Content))
            {
                var title = string.IsNullOrWhiteSpace(n.Title) ? "公告" : n.Title;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    PclMsgBox.Show(_owner, title, n.Content));
            }
        });
    }

    /// <summary>例行发布通知（Windows 版同款判断）：内容里出现「已发布…检查更新」就不弹，与本地更新日志重复。</summary>
    private static bool IsGenericReleaseNotice(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("已发布") && text.Contains("检查更新");
    }

    // ===================== 左下角提示（对应 Windows 版 Hint） =====================

    /// <summary>左下角滑出提示，4 秒后消失。red = 红色警告。</summary>
    public static void Hint(string text, bool red)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_owner is MainWindow mw) mw.ShowHint(text, red);
            }
            catch { }
        });
    }

    /// <summary>供其他页面调用的公开入口。</summary>
    public static void HintPublic(string text, bool red) => Hint(text, red);
}
