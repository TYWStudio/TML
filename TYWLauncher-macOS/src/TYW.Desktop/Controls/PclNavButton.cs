using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace TYW.Desktop.Controls;

/// <summary>
/// 标题栏导航项。逐层对应 Windows 版 <c>Controls/MyRadioButton.xaml</c>
/// （圆角 13.5、高度 27、图标 16px、左侧 12 间距、文字左右 8/12 间距）。
/// 标题栏用的是 Theme=White：选中白底 + C3 字，未选中近透明底 + 白字，悬停 #32eaf2fe。
/// </summary>
public class PclNavButton : Border
{
    private readonly Path _logo;
    private readonly TextBlock _lab;
    private bool _checked;
    private bool _hover;
    private bool _down;

    public PclNavButton()
    {
        CornerRadius = new CornerRadius(13.5);
        MinHeight = 27;
        MaxHeight = 27;
        Background = Pcl.Res("ColorBrushSemiTransparent");
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);

        _logo = new Path
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(12, 0, 0, 0),
            MaxHeight = 16,
            MaxWidth = 16,
            Fill = Brushes.White,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };
        _lab = new TextBlock
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Brushes.White,
            Margin = new Thickness(8, 0, 12, 0),
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily
        };

        var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        stack.Children.Add(_logo);
        stack.Children.Add(_lab);
        Child = stack;

        PointerEntered += (_, _) => { _hover = true; RefreshColor(); };
        PointerExited += (_, _) => { _hover = false; _down = false; RefreshColor(); };
        PointerPressed += (_, e) =>
        {
            // 必须吃掉事件：标题栏的拖动处理器挂在父级 Grid 上，
            // 事件冒泡上去会立刻 BeginMoveDrag，PointerReleased 就再也回不到按钮，点击等于失效。
            e.Handled = true;
            _down = true;
            _lastPoint = e.GetPosition(this);
            RefreshColor();
        };
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            bool wasDown = _down;
            _down = false;
            RefreshColor();
            // Bounds 是相对父级的矩形，判断"松手在按钮内"要用自身尺寸
            if (wasDown && new Rect(Bounds.Size).Contains(e.GetPosition(this))) Click?.Invoke(this, EventArgs.Empty);
        };
        PointerMoved += (_, e) => _lastPoint = e.GetPosition(this);
        PointerMoved += (_, e) => _lastPoint = e.GetPosition(this);
    }

    private Point _lastPoint;

    public string Text
    {
        get => _lab.Text ?? "";
        set => _lab.Text = value ?? "";
    }

    public Geometry? Logo
    {
        get => _logo.Data;
        set => _logo.Data = value;
    }

    public double LogoScale
    {
        get => _logo.RenderTransform is ScaleTransform st ? st.ScaleX : 1;
        set => _logo.RenderTransform = new ScaleTransform(value, value);
    }

    /// <summary>是否选中（对应 MyRadioButton.Checked）。</summary>
    public bool NavChecked
    {
        get => _checked;
        set { _checked = value; RefreshColor(); }
    }

    public event EventHandler? Click;

    private void RefreshColor()
    {
        if (_checked)
        {
            Background = Pcl.Res("ColorBrushWhite");
            _lab.Foreground = Pcl.Res("ColorBrush3");
            _logo.Fill = Pcl.Res("ColorBrush3");
        }
        else if (_down)
        {
            Background = new SolidColorBrush(Color.Parse("#32eaf2fe"));
            _lab.Foreground = Brushes.White;
            _logo.Fill = Brushes.White;
        }
        else if (_hover)
        {
            Background = new SolidColorBrush(Color.Parse("#32eaf2fe"));
            _lab.Foreground = Brushes.White;
            _logo.Fill = Brushes.White;
        }
        else
        {
            Background = Pcl.Res("ColorBrushSemiTransparent");
            _lab.Foreground = Brushes.White;
            _logo.Fill = Brushes.White;
        }
    }
}
