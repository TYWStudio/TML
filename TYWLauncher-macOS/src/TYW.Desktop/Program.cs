using System;
using Avalonia;

namespace TYW.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
