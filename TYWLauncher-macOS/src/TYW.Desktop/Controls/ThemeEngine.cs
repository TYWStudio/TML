using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TYW;

namespace TYW.Desktop.Controls;

/// <summary>
/// 主题引擎。与 Windows 版 <c>Modules/ModSecret.vb</c> 的 ThemeRefresh / ThemeRefreshMain 一一对应：
/// 15 套预设配色（12 滑稽彩、13 欧皇彩每次随机色相，14 自定义 HSL）→ 由 HSL 推导 Color1-8 / Bg0 / Bg1
/// → 就地改 Application 资源里的画刷颜色 → 刷新顶栏渐变与主页面背景渐变。
/// </summary>
public static class ThemeEngine
{
    public readonly record struct Preset(string Name, int Hue, int Sat, int Adjust, int Delta);

    /// <summary>预设配色表，顺序与 Windows 版 UiLauncherTheme 的 0-14 完全一致。</summary>
    public static readonly Preset[] Presets =
    {
        new("龙猫蓝", 210, 85, 0, 0),
        new("甜柠青", 150, 65, 8, 0),
        new("小草绿", 105, 70, 5, 0),
        new("菠萝黄", 48, 85, 6, 0),
        new("橡木棕", 25, 45, 2, 0),
        new("玄素黑", 210, 0, -2, 0),
        new("铁杆粉", 330, 80, 6, 0),
        new("神秘紫", 270, 70, 5, 0),
        new("秋仪金", 40, 90, 0, 0),
        new("活跃橙", 25, 95, 5, 0),
        new("跳票红", 350, 85, 0, 0),
        new("极客蓝", 205, 90, -3, 0),
        new("滑稽彩", 210, 85, 0, 120),
        new("欧皇彩", 210, 85, 0, 120),
        new("自定义", 210, 85, 0, 0)
    };

    public const int CustomTheme = 14;

    private static Window? _window;

    /// <summary>当前主题编号。0-14。</summary>
    public static int Current { get; private set; }

    internal static int HueNow { get; private set; } = 210;
    internal static int SatNow { get; private set; } = 85;
    internal static int AdjustNow { get; private set; }
    internal static int DeltaNow { get; private set; }

    /// <summary>主题切换后触发（设置页用来刷新色块选中态）。</summary>
    public static event Action? Changed;

    public static string NameOf(int index) =>
        index >= 0 && index < Presets.Length ? Presets[index].Name : "龙猫蓝";

    /// <summary>预设配色的代表色（用于设置页色块）。</summary>
    public static (Color Top, Color Bottom) PreviewOf(int index)
    {
        var p = Presets[Math.Clamp(index, 0, Presets.Length - 1)];
        // 滑稽彩 / 欧皇彩用固定色相做预览，避免每次刷新设置页颜色乱跳
        int hue = p.Hue;
        return (Hsl(hue, p.Sat, 55 + p.Adjust), Hsl(hue, p.Sat, 91 + p.Adjust * 0.1));
    }

    /// <summary>绑定主窗口，用于刷新顶栏 / 页面背景渐变。</summary>
    public static void Attach(Window window)
    {
        _window = window;
        RefreshMain();
    }

    public static void LoadSaved()
    {
        Apply(ModSettings.GetInt("UiLauncherTheme", 0), persist: false);
    }

    /// <summary>切换主题。persist=true 时写回设置（Windows 版对应 UiLauncherTheme 的 OnChanged）。</summary>
    public static void Apply(int theme, bool persist = true)
    {
        try
        {
            if (theme < 0 || theme >= Presets.Length) theme = 0;
            Current = theme;

            var p = Presets[theme];
            int hue = p.Hue, sat = p.Sat, adj = p.Adjust, delta = p.Delta;

            if (theme == 12 || theme == 13) hue = Random.Shared.Next(0, 360);
            if (theme == CustomTheme)
            {
                hue = Math.Clamp(ModSettings.GetInt("UiLauncherHue", 210), 0, 359);
                sat = Math.Clamp(ModSettings.GetInt("UiLauncherSat", 85), 0, 100);
                adj = Math.Clamp(ModSettings.GetInt("UiLauncherLight", 0), -30, 30);
                delta = Math.Clamp(ModSettings.GetInt("UiLauncherDelta", 0), 0, 180);
            }

            HueNow = hue;
            SatNow = sat;
            AdjustNow = adj;
            DeltaNow = delta;

            var c1 = Hsl(hue, sat * 0.2, 25 + adj * 0.3);
            var c2 = Hsl(hue, sat, 45 + adj);
            var c3 = Hsl(hue, sat, 55 + adj);
            var c4 = Hsl(hue, sat, 65 + adj);
            var c5 = Hsl(hue, sat, 80 + adj * 0.4);
            var c6 = Hsl(hue, sat, 91 + adj * 0.1);
            var c7 = Hsl(hue, sat, 95);
            var c8 = Hsl(hue, sat, 97);
            var bg0 = Mix(Mix(c4, c5, 0.5), Color.FromRgb(166, 166, 166), 0.2);
            var bg1 = WithAlpha(c7, 190);

            SetBrush("ColorBrush1", c1);
            SetBrush("ColorBrush2", c2);
            SetBrush("ColorBrush3", c3);
            SetBrush("ColorBrush4", c4);
            SetBrush("ColorBrush5", c5);
            SetBrush("ColorBrush6", c6);
            SetBrush("ColorBrush7", c7);
            SetBrush("ColorBrush8", c8);
            SetBrush("ColorBrushBg0", bg0);
            SetBrush("ColorBrushBg1", bg1);
            SetBrush("ColorBrushSemiTransparent", WithAlpha(c8, 1));

            if (persist)
            {
                ModSettings.SetValue("UiLauncherTheme", theme.ToString());
                ModSettings.Save();
            }

            RefreshMain();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "刷新主题颜色");
        }
    }

    /// <summary>刷新顶栏渐变与主页面背景（对应 Windows 版 ThemeRefreshMain）。</summary>
    public static void RefreshMain()
    {
        var win = _window;
        if (win == null) return;

        try
        {
            if (win.FindControl<Grid>("PanTitle") is { } panTitle)
                panTitle.Background = TopBarBrush();

            if (win.FindControl<Grid>("PanForm") is { } panForm)
                panForm.Background = ModSettings.GetBool("UiBackgroundColorful", true)
                    ? ColorfulBackgroundBrush()
                    : new SolidColorBrush(Color.FromRgb(245, 245, 245));
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "刷新窗口主题");
        }
    }

    /// <summary>顶栏渐变：色相 ±delta 三段（滑稽彩 / 欧皇彩为彩色渐变条）。</summary>
    public static IBrush TopBarBrush()
    {
        int d0 = -DeltaNow, d1 = 0, d2 = DeltaNow;
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(Hsl(HueNow + d0, SatNow, 48 + AdjustNow), 0));
        brush.GradientStops.Add(new GradientStop(Hsl(HueNow + d1, SatNow, 54 + AdjustNow), 0.5));
        brush.GradientStops.Add(new GradientStop(Hsl(HueNow + d2, SatNow, 48 + AdjustNow), 1));
        return brush;
    }

    /// <summary>主页面背景：斜向三段淡色渐变（对应 UiBackgroundColorful）。</summary>
    public static IBrush ColorfulBackgroundBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.9, 0d, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.1, 1, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(Hsl(HueNow - 15, SatNow * 0.8, 91), -0.1));
        brush.GradientStops.Add(new GradientStop(Hsl(HueNow, SatNow * 0.8, 91), 0.4));
        brush.GradientStops.Add(new GradientStop(Hsl(HueNow + 15, SatNow * 0.8, 91), 1.1));
        return brush;
    }

    // ================= 颜色工具 =================

    /// <summary>HSL → RGB。hue 角度，sat / light 为 0-100 百分比（与 Windows 版 MyColor.FromHSL2 同参数）。</summary>
    public static Color Hsl(double hue, double sat, double light)
    {
        hue = ((hue % 360) + 360) % 360;
        sat = Math.Clamp(sat, 0, 100) / 100.0;
        light = Math.Clamp(light, 0, 100) / 100.0;

        double c = (1 - Math.Abs(2 * light - 1)) * sat;
        double x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        double m = light - c / 2;

        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round(Math.Clamp(r + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(g + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(b + m, 0, 1) * 255));
    }

    private static Color Mix(Color a, Color b, double ratio)
    {
        double r = ratio, o = 1 - ratio;
        return Color.FromRgb(
            (byte)Math.Round(a.R * o + b.R * r),
            (byte)Math.Round(a.G * o + b.G * r),
            (byte)Math.Round(a.B * o + b.B * r));
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>就地改画刷颜色。XAML 里的 StaticResource 引用的是同一个画刷实例，所以界面会立即变色。</summary>
    private static void SetBrush(string key, Color color)
    {
        if (Application.Current is not { } app) return;
        if (app.TryFindResource(key, out var v) && v is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }
        app.Resources[key] = new SolidColorBrush(color);
    }
}
