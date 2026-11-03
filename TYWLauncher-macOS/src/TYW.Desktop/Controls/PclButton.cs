using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace TYW.Desktop.Controls;

/// <summary>
/// 按钮。逐层对应 Windows 版 <c>Controls/MyButton.xaml</c>：
/// 外层 Border（半透明底）→ 内层 Border（圆角 3、1px 描边、半透明白底）→ TextBlock（13 号，颜色跟随描边）。
/// </summary>
public class PclButton : Border
{
    public enum ColorState { Normal, Highlight, Red }

    private readonly Border _fore;
    private readonly TextBlock _lab;

    private ColorState _colorType = ColorState.Normal;
    private bool _hover;
    private bool _down;
    private bool _enabled = true;
    private string _text = "";

    public PclButton()
    {
        Background = Pcl.Res("ColorBrushSemiTransparent");
        Focusable = true;

        _lab = new TextBlock
        {
            FontSize = 13,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.None,
            FontFamily = Pcl.FontFamily,
            Margin = new Thickness(10, 0, 10, 0),
            Foreground = Pcl.Res("ColorBrush1")
        };

        _fore = new Border
        {
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = Pcl.Res("ColorBrush1"),
            Background = Pcl.Res("ColorBrushHalfWhite"),
            Child = _lab,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(1, 1)
        };
        Child = _fore;

        PointerEntered += (_, _) => { _hover = true; RefreshColor(); };
        PointerExited += (_, _) => { _hover = false; _down = false; RefreshColor(); };
        PointerPressed += (_, e) =>
        {
            if (!_enabled) return;
            e.Handled = true;
            _down = true;
            RefreshColor();
            ScaleTo(0.955, 80);
        };
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (!_enabled) return;
            bool wasDown = _down;
            _down = false;
            RefreshColor();
            ScaleTo(1, 300);
            // Bounds 是相对父级的矩形，判断"松手在按钮内"要用自身尺寸
            if (wasDown && new Rect(Bounds.Size).Contains(e.GetPosition(this))) RaiseClick();
        };
    }

    /// <summary>按钮文字。</summary>
    public string Text
    {
        get => _text;
        set { _text = value ?? ""; _lab.Text = _text; }
    }

    public ColorState ColorType
    {
        get => _colorType;
        set { _colorType = value; RefreshColor(); }
    }

    /// <summary>文字内边距（对应 MyButton 的 Padding，主按钮用 30,0,30,15 把文字往上顶）。</summary>
    public Thickness TextPadding
    {
        get => _lab.Margin;
        set => _lab.Margin = value;
    }

    /// <summary>可用状态：不可用时按 MyButton 的灰态绘制（Gray4 描边 + 灰字）。</summary>
    public bool IsEnabledButton
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Cursor = value ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            _hover = false;
            _down = false;
            RefreshColor();
        }
    }

    public event EventHandler? Click;

    private void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    private void ScaleTo(double scale, int ms)
    {
        var ani = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) }
                }
            }
        };
        _ = ani.RunAsync(_fore);
    }

    private void RefreshColor()
    {
        if (!_enabled)
        {
            _fore.BorderBrush = Pcl.Res("ColorBrushGray4");
            _lab.Foreground = Pcl.Res("ColorBrushGray4");
            _fore.Background = Pcl.Res("ColorBrushHalfWhite");
            return;
        }

        if (_hover || _down)
        {
            _fore.BorderBrush = Pcl.Res("ColorBrush3");
            _lab.Foreground = Pcl.Res("ColorBrush3");
            _fore.Background = Pcl.Res(_colorType == ColorState.Red ? "ColorBrushRedBack" : "ColorBrush7");
        }
        else
        {
            _fore.BorderBrush = Pcl.Res(_colorType switch
            {
                ColorState.Highlight => "ColorBrush2",
                ColorState.Red => "ColorBrushRedDark",
                _ => "ColorBrush1"
            });
            _lab.Foreground = _fore.BorderBrush;
            _fore.Background = Pcl.Res("ColorBrushHalfWhite");
        }
    }
}

/// <summary>资源与字体的取用工具。</summary>
public static class Pcl
{
    /// <summary>
    /// 中文字体链：Windows 版用微软雅黑，macOS 用系统的苹方，
    /// 其余环境按 Noto / 思源 / 文泉驿 依次回落。
    /// </summary>
    public static FontFamily FontFamily { get; } = new(OperatingSystem.IsMacOS()
        ? "PingFang SC, Hiragino Sans GB, Heiti SC, Helvetica Neue, sans-serif"
        : "Microsoft YaHei UI, Noto Sans CJK SC, Source Han Sans SC, WenQuanYi Micro Hei, sans-serif");

    public static IBrush Res(string key)
    {
        if (Application.Current is { } app && app.TryFindResource(key, out var v) && v is IBrush b)
            return b;
        return Brushes.Transparent;
    }

    public static Color Col(string key)
    {
        if (Application.Current is { } app && app.TryFindResource(key, out var v))
        {
            if (v is ISolidColorBrush sb) return sb.Color;
            if (v is Color c) return c;
        }
        return Colors.Transparent;
    }

    public static TextBlock Text(string text, double size, string brushKey) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = FontFamily,
        Foreground = Res(brushKey),
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };
}
