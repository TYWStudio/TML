using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;
using TYW.Desktop.Controls;

namespace TYW.Desktop.Views;

/// <summary>
/// 模态消息框。对应 Windows 版 MyMsgBox：标题 + 内容 + 右下按钮，返回按钮序号。
/// </summary>
public static class PclMsgBox
{
    /// <summary>显示消息框并阻塞等待，返回点中的按钮序号（从 0 开始）。</summary>
    public static Task<int> Show(Window owner, string title, object content, params string[] buttons)
    {
        if (buttons.Length == 0) buttons = new[] { "确定" };
        var win = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Pcl.Res("ColorBrushBg0"),
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowDecorations = Avalonia.Controls.WindowDecorations.Full,
            MinWidth = 380,
            MaxWidth = 560
        };

        var root = new Border
        {
            Background = Pcl.Res("ColorBrushBg0"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(24, 18, 24, 16)
        };
        var stack = new StackPanel { Spacing = 12 };
        root.Child = stack;

        var labTitle = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(labTitle);

        if (content is string text)
        {
            var scroll = new ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            scroll.Content = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush2"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21
            };
            stack.Children.Add(scroll);
        }
        else if (content is Control c)
        {
            stack.Children.Add(c);
        }

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };
        stack.Children.Add(btnRow);

        var tcs = new TaskCompletionSource<int>();
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var btn = new PclButton
            {
                Text = buttons[i],
                Height = 32,
                MinWidth = 84,
                ColorType = i == 0
                    ? PclButton.ColorState.Highlight
                    : PclButton.ColorState.Normal
            };
            btn.Click += (_, _) =>
            {
                tcs.TrySetResult(idx);
                win.Close();
            };
            btnRow.Children.Add(btn);
        }

        win.Content = root;
        win.Closed += (_, _) => tcs.TrySetResult(-1);
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                tcs.TrySetResult(buttons.Length > 1 ? 1 : 0);
                win.Close();
            }
        };
        return win.ShowDialog<int>(owner);
    }

    /// <summary>带输入框的消息框（对应 MyMsgBoxInput）。取消返回 null。</summary>
    public static async Task<string> ShowInput(Window owner, string title, string desc, string defaultText)
    {
        var input = new TextBox
        {
            Text = defaultText ?? "",
            Height = 32,
            FontSize = 13,
            FontFamily = Pcl.FontFamily
        };
        var text = new TextBlock
        {
            Text = desc,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush2"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(text);
        panel.Children.Add(input);

        var result = await Show(owner, title, panel, "确定", "取消");
        if (result != 0) return null;
        return input.Text;
    }
}
