using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TYW.Desktop.Controls;

/// <summary>
/// 单选胶囊。对应 Windows 版 <c>Controls/MyRadioButton.xaml</c> 的 Highlight 配色：
/// 选中 C3 底白字、未选中近透明底 C3 字、悬停 C7 底。
/// </summary>
public class PclRadioButton : Border
{
    private readonly Path _logo;
    private readonly TextBlock _lab;
    private bool _checked;
    private bool _hover;
    private bool _down;

    public PclRadioButton()
    {
        CornerRadius = new CornerRadius(13.5);
        MinHeight = 27;
        MaxHeight = 27;
        Background = Pcl.Res("ColorBrushSemiTransparent");
        Cursor = new Cursor(StandardCursorType.Hand);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        _logo = new Path
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(12, 0, 0, 0),
            MaxHeight = 16,
            MaxWidth = 16,
            Fill = Pcl.Res("ColorBrush3"),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };
        _lab = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Pcl.Res("ColorBrush3"),
            Margin = new Thickness(8, 0, 12, 0),
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(_logo);
        stack.Children.Add(_lab);
        Child = stack;

        PointerEntered += (_, _) => { _hover = true; Refresh(); };
        PointerExited += (_, _) => { _hover = false; _down = false; Refresh(); };
        PointerPressed += (_, _) => { _down = true; Refresh(); };
        PointerReleased += (_, _) =>
        {
            bool wasDown = _down;
            _down = false;
            Refresh();
            if (wasDown) { NavChecked = true; Click?.Invoke(this, System.EventArgs.Empty); }
        };
    }

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

    public bool NavChecked
    {
        get => _checked;
        set { _checked = value; Refresh(); }
    }

    public event System.EventHandler? Click;

    private void Refresh()
    {
        if (_checked)
        {
            Background = Pcl.Res("ColorBrush3");
            _lab.Foreground = Brushes.White;
            _logo.Fill = Brushes.White;
        }
        else if (_hover || _down)
        {
            Background = Pcl.Res("ColorBrush7");
            _lab.Foreground = Pcl.Res("ColorBrush3");
            _logo.Fill = Pcl.Res("ColorBrush3");
        }
        else
        {
            Background = Pcl.Res("ColorBrushSemiTransparent");
            _lab.Foreground = Pcl.Res("ColorBrush3");
            _logo.Fill = Pcl.Res("ColorBrush3");
        }
    }
}
