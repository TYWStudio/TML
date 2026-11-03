using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TYW.Desktop.Views;

namespace TYW.Desktop;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        TYW.ModPaths.EnsureAll();
        TYW.ModSettings.Load();
        TYW.Desktop.Views.StaticPage.ApplyMirror();

        // 主题：在创建窗口之前先把配色写进资源，避免启动时先闪一下默认蓝
        Controls.ThemeEngine.LoadSaved();

        // macOS 上没有微软雅黑，AXAML 里的 {StaticResource PclFont} 要在窗口加载前换成苹方
        if (OperatingSystem.IsMacOS())
        {
            Resources["PclFont"] = Controls.Pcl.FontFamily;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
