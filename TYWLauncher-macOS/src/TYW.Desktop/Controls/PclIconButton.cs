using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace TYW.Desktop.Controls;

/// <summary>
/// 标题栏图标按钮。对应 Windows 版 <c>Controls/MyIconButton.xaml</c>（28×28，圆角 3，白色图标，悬停半透明白底）。
/// </summary>
public class PclIconButton : Border
{
    private readonly Path _logo;
    private bool _hover;

    public PclIconButton()
    {
        CornerRadius = new CornerRadius(3);
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        _logo = new Path
        {
            Stretch = Stretch.Uniform,
            Fill = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            MaxWidth = 20,
            MaxHeight = 20,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };
        Child = _logo;

        PointerEntered += (_, _) => { _hover = true; Refresh(); };
        PointerExited += (_, _) => { _hover = false; Refresh(); };
        // 同 PclNavButton：吃掉指针事件，否则标题栏拖动会把点击吞掉
        PointerPressed += (_, e) => { e.Handled = true; };
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Click?.Invoke(this, System.EventArgs.Empty);
        };
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

    public event System.EventHandler? Click;

    private void Refresh()
    {
        Background = _hover ? new SolidColorBrush(Color.Parse("#33ffffff")) : Brushes.Transparent;
    }
}
