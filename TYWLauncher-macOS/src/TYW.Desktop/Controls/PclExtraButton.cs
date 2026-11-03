using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace TYW.Desktop.Controls;

/// <summary>
/// 右下角浮动圆形按钮。对应 Windows 版 <c>Controls/MyExtraButton</c>：
/// 40×40 圆、ColorBrush3 底、白色图标、投影，出现/消失带缩放动画。
/// 启动页右侧的「关闭 Minecraft」用的就是它。
/// </summary>
public class PclExtraButton : Border
{
    private readonly Path _logo;
    private readonly ScaleTransform _scale = new(0, 0);
    private bool _hover;
    private bool _shown;

    public PclExtraButton()
    {
        Width = 40;
        Height = 40;
        CornerRadius = new CornerRadius(1000);
        Background = Pcl.Res("ColorBrush3");
        BoxShadow = BoxShadows.Parse("0 2 10 0 #33000000");
        Cursor = new Cursor(StandardCursorType.Hand);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = _scale;
        IsVisible = false;

        _logo = new Path
        {
            Stretch = Stretch.Uniform,
            Fill = Brushes.White,
            MaxWidth = 17,
            MaxHeight = 17,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(1, 1)
        };
        Child = _logo;

        PointerEntered += (_, _) =>
        {
            _hover = true;
            ToolTip.SetIsOpen(this, true);
            _ = Ani.Tween(1, 1.08, 140, v => SetScale(v));
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            ToolTip.SetIsOpen(this, false);
            _ = Ani.Tween(1.08, 1, 140, v => SetScale(v));
        };
        PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _ = Ani.Tween(1, 0.9, 70, v => SetScale(v));
        };
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            _ = Ani.Tween(0.9, _hover ? 1.08 : 1, 160, v => SetScale(v), Ani.EaseOutBack);
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Click?.Invoke(this, EventArgs.Empty);
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

    public string Tip
    {
        get => ToolTip.GetTip(this) as string ?? "";
        set => ToolTip.SetTip(this, value);
    }

    /// <summary>显示 / 隐藏（带 Windows 版同款缩放动画）。</summary>
    public bool Show
    {
        get => _shown;
        set
        {
            if (_shown == value) return;
            _shown = value;
            if (value)
            {
                IsVisible = true;
                _ = Ani.Tween(0, 1, 220, v => SetScale(v), Ani.EaseOutBack);
            }
            else
            {
                _ = Ani.Tween(_scale.ScaleX, 0, 160, v =>
                {
                    SetScale(v);
                    if (v <= 0.02) IsVisible = false;
                }, Ani.EaseInFluent);
            }
        }
    }

    public event EventHandler? Click;

    private void SetScale(double v)
    {
        _scale.ScaleX = v;
        _scale.ScaleY = v;
    }
}
