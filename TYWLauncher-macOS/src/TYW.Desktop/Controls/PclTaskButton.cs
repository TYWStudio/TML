using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System;

namespace TYW.Desktop.Controls;

/// <summary>
/// 右下角「任务管理」圆形按钮。对应 Windows 版 MyExtraButton（BtnExtraDownload）：
/// 40×40 圆、ColorBrush3 底、白色图标，圆形内从下往上按进度填充，悬停显示进度。
/// </summary>
public class PclTaskButton : Border
{
    private readonly Path _logo;
    private readonly Border _fill;
    private readonly ScaleTransform _scale = new(0, 0);
    private bool _hover;

    public PclTaskButton()
    {
        Width = 40;
        Height = 40;
        CornerRadius = new CornerRadius(1000);
        Background = Pcl.Res("ColorBrush3");
        BoxShadow = BoxShadows.Parse("0 2 10 0 #33000000");
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = _scale;
        IsVisible = false;
        ClipToBounds = true;

        _fill = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#40FFFFFF")),
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 0,
            IsHitTestVisible = false
        };
        _logo = new Path
        {
            Stretch = Stretch.Uniform,
            Fill = Brushes.White,
            MaxWidth = 18,
            MaxHeight = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // 任务管理图标（与 Windows 版 BtnExtraDownload 同一套路径）
            Data = Geometry.Parse("M484.693 225.28 314.027 389.12l-68.267-68.267c-13.653-13.653-40.96-13.653-47.787 0-13.654 13.654-13.654 34.134 0 47.787l95.574 95.573c13.654 13.654 34.134 13.654 47.787 0L532.48 273.067c13.654-13.654 13.654-34.134 0-47.787s-34.133-13.653-47.787 0M819.2 341.333H614.4c-20.48 0-34.133 13.654-34.133 34.134S593.92 409.6 614.4 409.6h204.8c20.48 0 34.133-13.653 34.133-34.133s-13.653-34.134-34.133-34.134m0 341.334H614.4c-20.48 0-34.133 13.653-34.133 34.133s13.653 34.133 34.133 34.133h204.8c20.48 0 34.133-13.653 34.133-34.133s-13.653-34.133-34.133-34.133m-477.867-102.4c-75.093 0-136.533 61.44-136.533 136.533s61.44 136.533 136.533 136.533 136.534-61.44 136.534-136.533-61.44-136.533-136.534-136.533m0 204.8c-40.96 0-68.266-27.307-68.266-68.267s27.306-68.267 68.266-68.267S409.6 675.84 409.6 716.8s-27.307 68.267-68.267 68.267")
        };
        // 图标放在填充层之上
        var layer = new Grid();
        layer.Children.Add(_fill);
        layer.Children.Add(_logo);
        Child = layer;

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
        PointerReleased += (_, e) =>
        {
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Click?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? Click;

    private void SetScale(double v)
    {
        _scale.ScaleX = v;
        _scale.ScaleY = v;
    }

    /// <summary>进度 0~1：圆形内从下往上填充。</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) < 0.0001) return;
            _progress = value;
            _fill.Height = Math.Clamp(value, 0, 1) * 40;
        }
    }
    private double _progress;

    /// <summary>显示/隐藏（带缩放动画，和 Windows 的 MyExtraButton.Show 一致）。</summary>
    public bool Show
    {
        get => IsVisible;
        set
        {
            if (IsVisible == value) return;
            if (value)
            {
                IsVisible = true;
                _ = Ani.Tween(0, 1, 200, v => SetScale(v), Ani.EaseOutBack);
            }
            else
            {
                _ = Ani.Tween(_scale.ScaleX, 0, 140, v => SetScale(v)).ContinueWith(_ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (_scale.ScaleX < 0.05) IsVisible = false;
                    }));
            }
        }
    }

    public string Tip
    {
        get => ToolTip.GetTip(this) as string ?? "";
        set => ToolTip.SetTip(this, value);
    }
}
