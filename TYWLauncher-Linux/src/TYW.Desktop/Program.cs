using System;
using System.Threading.Tasks;
using Avalonia;

namespace TYW.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 崩溃 / 未处理的后台异常自动上报（上报内容首行带平台，见 ModPlatform.PlatformDisplay）。
        // 先落盘再发送，进程马上退出也没关系，下次启动会补发。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                    TYW.ModSecret.ReportErrorToServer(ex.ToString(), "崩溃");
            }
            catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try { TYW.ModSecret.ReportErrorToServer(e.Exception?.ToString() ?? "未观察到的任务异常", "后台任务异常"); }
            catch { }
            e.SetObserved();
        };
        try { _ = Task.Run(() => TYW.ModSecret.FlushPendingReports()); } catch { }

        // 命令行自检模式（下载/启动/Java 检测），无参数时进图形界面
        var rc = Cli.TryRun(args);
        if (rc >= 0)
        {
            Environment.ExitCode = rc;
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
