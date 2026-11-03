using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TYW.Desktop.Controls;
using TYW;

namespace TYW.Desktop.Views;

/// <summary>
/// 启动页右栏。正式版默认什么都没有：提示条只在快照版显示，启动日志只在 Debug 构建显示。
/// </summary>
public partial class PageLaunchRight : UserControl
{
    private bool _hintShown;

    public PageLaunchRight()
    {
        InitializeComponent();
    }

    public void ShowHint(string text)
    {
        PanHint.Text = text;
        PanHint.IsVisible = true;
        _hintShown = true;
    }

    /// <summary>对应 PageLaunchRight 的 PanLog：只在 Debug 构建里出现。</summary>
    public void ShowLog()
    {
#if DEBUG
        PanLog.IsVisible = true;
        PanLog.Body.Children.Clear();
        PanLog.Body.Children.Add(new TextBlock
        {
            Text = ModLog.Recent(),
            Margin = new Avalonia.Thickness(15, 6, 15, 13),
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            TextWrapping = TextWrapping.Wrap
        });
#endif
    }
}
