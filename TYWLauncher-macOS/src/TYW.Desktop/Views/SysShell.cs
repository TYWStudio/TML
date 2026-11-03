using System;
using System.Diagnostics;
using TYW;

namespace TYW.Desktop.Views;

/// <summary>
/// 调系统外壳打开链接 / 文件夹：Linux 用 xdg-open，macOS 用 open。
/// 之前各处直接写死 xdg-open，macOS 上会静默失败。
/// </summary>
public static class SysShell
{
    private static string Opener => ModPlatform.IsMac ? "open" : "xdg-open";

    public static void OpenUrl(string url) => Run(url);

    public static void OpenFolder(string dir) => Run(dir);

    private static void Run(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Opener,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add(target);
            Process.Start(psi);
        }
        catch
        {
            // 没有对应命令时退回 ShellExecute
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "打开外部程序：" + target);
            }
        }
    }
}
