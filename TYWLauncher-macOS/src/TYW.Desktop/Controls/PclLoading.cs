using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace TYW.Desktop.Controls;

/// <summary>
/// 载入动画。对应 Windows 版 <c>Controls/MyLoading</c> 的镐子循环：
/// 抬镐（-20°）→ 砸下（50°）→ 回弹（25°），砸下时溅出两个小碎屑。
/// </summary>
public class PclLoading : Grid
{
    private const string PickaxePath =
        "M 963.6 858.2 410.816 305.504 C 508.116 213.304 609.204 196.8 711.104 128.6 837.11367 49.573762 879.34045 50.334062 751.5 49.5 611.3 52 471.8 96.2 353.3 182.4 309.8 155.7 252.1 161.2 214.5 198.9 176.9 236.6 171.3 294.2 198 337.7 111.8 456.3 67.6 595.8 65.1 735.9 63.315254 883.82034 65.077966 837.29308 144.2 695.488 212.4 593.588 228.888 492.4 321.088 395.2 L 873.9 948 c 0.60001 0.59999 1.6 0.6 2.2 0 l 87.5 -87.5 c 0.6 -0.7 0.6 -1.6 0 -2.3 z";

    private readonly RotateTransform _rotate = new(25);
    private readonly Path _pick;
    private readonly Path _left;
    private readonly Path _right;
    private bool _stopped;

    public PclLoading()
    {
        MinWidth = 50;
        MinHeight = 50;

        var stage = new Canvas
        {
            Width = 60,
            Height = 50,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var block = new Rectangle
        {
            Width = 26,
            Height = 2,
            Fill = Pcl.Res("ColorBrush3")
        };
        Canvas.SetLeft(block, 17);
        Canvas.SetTop(block, 43);
        stage.Children.Add(block);

        _pick = new Path
        {
            Data = Geometry.Parse(PickaxePath),
            Stroke = Pcl.Res("ColorBrush3"),
            StrokeThickness = 2,
            Stretch = Stretch.Uniform,
            Width = 36,
            Height = 36,
            RenderTransformOrigin = new RelativePoint(0.30, 0.88, RelativeUnit.Relative),
            RenderTransform = _rotate
        };
        Canvas.SetLeft(_pick, 12);
        Canvas.SetTop(_pick, 1);
        stage.Children.Add(_pick);

        _left = Particle(-45, 24, 34);
        _right = Particle(45, 38, 34);
        stage.Children.Add(_left);
        stage.Children.Add(_right);

        Children.Add(stage);

        AttachedToVisualTree += (_, _) => { _stopped = false; _ = Loop(); };
        DetachedFromVisualTree += (_, _) => _stopped = true;
    }

    private Path Particle(double angle, double x, double y)
    {
        var p = new Path
        {
            Data = Geometry.Parse("M0,0L2,0L1,4"),
            Fill = Pcl.Res("ColorBrush3"),
            Width = 3,
            Height = 5,
            Opacity = 0,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new RotateTransform(angle)
        };
        Canvas.SetLeft(p, x);
        Canvas.SetTop(p, y);
        return p;
    }

    private async Task Loop()
    {
        // 起始位置摆好，避免第一帧突然弹一下
        _rotate.Angle = 25;
        while (!_stopped)
        {
            try
            {
                await Ani.Tween(25, -20, 260, v => _rotate.Angle = v, EaseInBack, 220);
                if (_stopped) return;
                await Ani.Tween(-20, 50, 220, v => _rotate.Angle = v, Ani.EaseOutFluent);
                if (_stopped) return;
                _ = Splash();
                await Ani.Tween(50, 25, 380, v => _rotate.Angle = v, Ani.EaseOutBack);
                await Task.Delay(520 * Ani.Slow);
            }
            catch
            {
                return;
            }
        }
    }

    private Task Splash()
    {
        Canvas.SetLeft(_left, 24);
        Canvas.SetTop(_left, 34);
        Canvas.SetLeft(_right, 38);
        Canvas.SetTop(_right, 34);
        _left.Opacity = 1;
        _right.Opacity = 1;
        return Ani.Tween(0, 1, 220, v =>
        {
            Canvas.SetLeft(_left, 24 - 5 * v);
            Canvas.SetTop(_left, 34 - 7 * v);
            Canvas.SetLeft(_right, 38 + 5 * v);
            Canvas.SetTop(_right, 34 - 7 * v);
            _left.Opacity = 1 - v;
            _right.Opacity = 1 - v;
        }, Ani.EaseOutFluent);
    }

    private static double EaseInBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return c3 * t * t * t - c1 * t * t;
    }
}
