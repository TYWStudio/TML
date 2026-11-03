using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TYW.Desktop.Controls;

/// <summary>
/// 提示条。对应 Windows 版 <c>Controls/MyHint.xaml</c>：圆角 3、1px 边框、浅色底、左侧色条、文字 12.5。
/// </summary>
public class PclHint : Border
{
    private readonly TextBlock _text;

    public PclHint()
    {
        CornerRadius = new CornerRadius(3);
        BorderThickness = new Thickness(1);
        BorderBrush = Pcl.Res("ColorBrush6");
        Background = Pcl.Res("ColorBrush8");
        Padding = new Thickness(10, 6, 10, 6);

        _text = new TextBlock
        {
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush3"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Child = _text;
    }

    public string Text
    {
        get => _text.Text ?? "";
        // 注意：正式版右栏不显示提示条，可见性由调用方（快照版 / 内测版）显式设置，
        // 这里只改文字，不能顺带改动 IsVisible，否则 XAML 里写的 IsVisible="False" 会被覆盖。
        set => _text.Text = value ?? "";
    }

    /// <summary>主题：Red=警告（对应 MyHint Theme="Red"），其余为蓝色提示。</summary>
    public string Theme
    {
        set
        {
            if (string.Equals(value, "Red", System.StringComparison.OrdinalIgnoreCase))
            {
                BorderThickness = new Thickness(3, 1, 1, 1);
                BorderBrush = new SolidColorBrush(Color.Parse("#99FF4444"));
                Background = new SolidColorBrush(Color.Parse("#ffdddf"));
                _text.Foreground = new SolidColorBrush(Color.Parse("#bf0b0b"));
            }
            else
            {
                BorderThickness = new Thickness(1);
                BorderBrush = Pcl.Res("ColorBrush6");
                Background = Pcl.Res("ColorBrush8");
                _text.Foreground = Pcl.Res("ColorBrush3");
            }
        }
    }
}

/// <summary>
/// 卡片。对应 Windows 版 <c>Controls/MyCard.xaml</c>：白底、圆角 6、1px Gray6 边框、标题 12.5 灰字。
/// </summary>
public class PclCard : Border
{
    private readonly TextBlock _title;
    private readonly StackPanel _body;
    private readonly StackPanel _root;
    private readonly Avalonia.Controls.Shapes.Path _arrow;
    private bool _canSwap;
    private bool _swapped;

    public PclCard()
    {
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        BorderBrush = Pcl.Res("ColorBrushGray6");
        Background = Brushes.White;

        _title = new TextBlock
        {
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            Margin = new Thickness(15, 10, 15, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _arrow = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2,4 l-2,2 10,10 10,-10 -2,-2 -8,8 -8,-8 z"),
            Stretch = Stretch.Uniform,
            Height = 6,
            Width = 10,
            Fill = Pcl.Res("ColorBrush1"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            RenderTransform = new RotateTransform(180),
            IsHitTestVisible = false,
            IsVisible = false
        };
        var header = new Grid { Height = 30, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        header.Children.Add(_title);
        header.Children.Add(_arrow);
        Grid.SetColumnSpan(_title, 2);

        _body = new StackPanel();
        Body = _body;

        _root = new StackPanel();
        _root.Children.Add(header);
        _root.Children.Add(_body);
        Child = _root;

        header.PointerReleased += (_, _) => { if (_canSwap) IsSwapped = !_swapped; };
    }

    public StackPanel Body { get; }

    public string Title
    {
        get => _title.Text ?? "";
        set
        {
            _title.Text = value ?? "";
            _title.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>标题行是否可以点击折叠（对应 MyCard.CanSwap）。</summary>
    public bool CanSwap
    {
        get => _canSwap;
        set { _canSwap = value; _arrow.IsVisible = value; }
    }

    /// <summary>是否处于折叠状态（对应 MyCard.IsSwapped）。</summary>
    public bool IsSwapped
    {
        get => _swapped;
        set
        {
            if (_swapped == value) return;
            _swapped = value;
            // 与 Windows 版 MyCard 一致（SwapLogoRight=True）：折叠 270°（指向右），展开 180°（指向上）
            _arrow.RenderTransform = new RotateTransform(value ? 270 : 180);
            if (!IsLoaded)
            {
                // 还没进可视树（构造时设初值），直接定态，不播动画
                _body.IsVisible = !value;
                _body.Opacity = 1;
                return;
            }
            if (value)
            {
                // 折叠：先淡出再隐藏
                var tt = new TranslateTransform(0, 0);
                _body.RenderTransform = tt;
                _ = Ani.Tween(1, 0, 90, v => _body.Opacity = v).ContinueWith(_ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (_swapped) _body.IsVisible = false;
                    }));
            }
            else
            {
                _body.IsVisible = true;
                _body.Opacity = 0;
                var tt = new TranslateTransform(0, -6);
                _body.RenderTransform = tt;
                _ = Ani.Tween(0, 1, 150, v => _body.Opacity = v, Ani.EaseOutFluent, 40);
                _ = Ani.Tween(-6, 0, 220, v => tt.Y = v, Ani.EaseOutFluent, 40);
            }
        }
    }
}
