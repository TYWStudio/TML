using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TYW.Desktop.Controls;

/// <summary>
/// 页面切换的逐个进入/退出动画。
/// 对应 Windows 版 Controls/MyPageRight.vb 的 TriggerEnterAnimation / TriggerExitAnimation
/// 与 Controls/MyPageLeft.vb 的 TriggerShowAnimation / TriggerHideAnimation：
///   右页卡片：从上方 -16 滑落 + 渐显，逐个错开 25ms，落定带回弹；
///   右页退出：上移 6 + 渐隐，错开 15ms；
///   左栏条目：从左侧 -25 滑入，错开 max(15-id,7)×2 ms，带弱回弹；
///   左栏退出：左移 6 + 渐隐，整体 70ms 内错开。
/// </summary>
public static class PageAnim
{
    // ===================== 收集需要做动画的控件 =====================

    /// <summary>右页动画控件：卡片 / 提示条 / 独立文字（对应 MyPageRight.GetAllAnimControls）。</summary>
    public static List<Control> RightControls(Control? root)
    {
        var list = new List<Control>();
        if (root != null) Collect(root, list, rightPage: true);
        return list;
    }

    /// <summary>左栏动画控件：列表项 / 文字等全部可见控件（对应 MyPageLeft.GetAllAnimControls）。</summary>
    public static List<Control> LeftControls(Control? root)
    {
        var list = new List<Control>();
        if (root != null) Collect(root, list, rightPage: false);
        return list;
    }

    private static void Collect(Control el, List<Control> list, bool rightPage)
    {
        if (!el.IsVisible) return;

        if (rightPage)
        {
            // 对应 MyPageRight._GetAllAnimControls：只对卡片 / 提示条 / 文字做动画，
            // 卡片内部不再逐个动画（整卡一起进出）
            if (el is PclCard or PclHint or TextBlock)
            {
                list.Add(el);
                return;
            }
            if (el is ContentControl { Content: Control content })
            {
                Collect(content, list, rightPage);
                return;
            }
            if (el is Panel panel)
            {
                foreach (var child in panel.Children.OfType<Control>())
                    Collect(child, list, rightPage);
            }
            return;
        }

        // 对应 MyPageLeft._GetAllAnimControls：列表项 / 文字 / 其余自定义控件都整体做动画
        if (el is PclListItem or PclButton or PclRadioButton or PclComboBox or PclSkin or PclLoading)
        {
            list.Add(el);
            return;
        }
        if (el is Panel p)
        {
            foreach (var child in p.Children.OfType<Control>())
                Collect(child, list, rightPage);
            return;
        }
        if (el is ContentControl { Content: Control lc })
        {
            Collect(lc, list, rightPage);
            return;
        }
        if (el is Decorator { Child: Control dc })
        {
            Collect(dc, list, rightPage);
            return;
        }
        list.Add(el);
    }

    // ===================== 页内内容替换 =====================

    /// <summary>
    /// 页内右栏内容替换（对应 Windows 版 MyPageRight.TriggerExitAnimation → 重建 → TriggerEnterAnimation）：
    /// 旧内容快速上移淡出 → 重建 → 新内容逐个滑入。replace 里做 Clear + Add。
    /// </summary>
    public static async void SwapRight(Control host, Action replace)
    {
        bool anim = Environment.GetEnvironmentVariable("TYW_NO_ANIM") != "1";
        if (anim) ExitRight(host);
        try { await Task.Delay(anim ? 110 * Ani.Slow : 10); } catch { }
        replace();
        if (anim) EnterRight(host);
    }

    /// <summary>页内左栏内容替换（对应 MyPageLeft.TriggerHideAnimation → 重建 → TriggerShowAnimation）。</summary>
    public static async void SwapLeft(Control host, Action replace)
    {
        bool anim = Environment.GetEnvironmentVariable("TYW_NO_ANIM") != "1";
        if (anim) ExitLeft(host);
        try { await Task.Delay(anim ? 100 * Ani.Slow : 10); } catch { }
        replace();
        if (anim) EnterLeft(host);
    }

    // ===================== 右页 =====================

    /// <summary>右页逐个进入（对应 TriggerEnterAnimation）。</summary>
    public static void EnterRight(Control? root)
    {
        var controls = RightControls(root);
        int delay = 0;
        foreach (var c in controls)
        {
            int d = delay;
            c.IsHitTestVisible = true;
            c.Opacity = 0;
            var tt = new TranslateTransform(0, -16);
            c.RenderTransform = tt;
            _ = Ani.Tween(0, 1, 100, v => c.Opacity = v, Ani.EaseOutFluent, d);
            _ = DropInAsync(tt, d);
            delay += 25;
        }
    }

    /// <summary>卡片落下：-16 → -11（减速），再 -11 → 0（回弹），对应 AaTranslateY 两段。</summary>
    private static async Task DropInAsync(TranslateTransform tt, int delay)
    {
        await Ani.Tween(-16, -11, 250, v => tt.Y = v, Ani.EaseOutFluent, delay);
        await Ani.Tween(-11, 0, 350, v => tt.Y = v, Ani.EaseOutBack);
    }

    /// <summary>右页逐个退出（对应 TriggerExitAnimation）。返回全部动画结束时间。</summary>
    public static int ExitRight(Control? root)
    {
        var controls = RightControls(root);
        int delay = 0;
        foreach (var c in controls)
        {
            int d = delay;
            c.IsHitTestVisible = false;
            var tt = c.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform(0, 0);
                c.RenderTransform = tt;
            }
            _ = Ani.Tween(c.Opacity, 0, 70, v => c.Opacity = v, null, d);
            _ = Ani.Tween(tt.Y, -6, 70, v => tt.Y = v, null, d);
            delay += 15;
        }
        return controls.Count > 0 ? delay + 70 : 0;
    }

    // ===================== 左栏 =====================

    /// <summary>左栏条目逐个进入（对应 TriggerShowAnimation）。</summary>
    public static void EnterLeft(Control? root)
    {
        var controls = LeftControls(root);
        int id = 0, delay = 0;
        foreach (var c in controls)
        {
            int d = delay;
            c.IsHitTestVisible = true;
            c.Opacity = 0;
            var tt = new TranslateTransform(-25, 0);
            c.RenderTransform = tt;
            double opacityTo = c is TextBlock ? 0.6 : 1;
            _ = Ani.Tween(0, opacityTo, 100, v => c.Opacity = v, Ani.EaseOutFluent, d);
            _ = SlideInAsync(tt, d);
            delay += Math.Max(15 - id, 7) * 2;
            id++;
        }
    }

    /// <summary>条目滑入：-25 → -20（减速），再 -20 → 0（弱回弹）。</summary>
    private static async Task SlideInAsync(TranslateTransform tt, int delay)
    {
        await Ani.Tween(-25, -20, 200, v => tt.X = v, Ani.EaseOutFluent, delay);
        await Ani.Tween(-20, 0, 300, v => tt.X = v, Ani.EaseOutBack);
    }

    /// <summary>左栏条目退出（对应 TriggerHideAnimation）。返回全部动画结束时间。</summary>
    public static int ExitLeft(Control? root)
    {
        var controls = LeftControls(root);
        int id = 0;
        foreach (var c in controls)
        {
            int d = controls.Count > 1 ? 70 * id / controls.Count : 0;
            c.IsHitTestVisible = false;
            var tt = c.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform(0, 0);
                c.RenderTransform = tt;
            }
            _ = Ani.Tween(c.Opacity, 0, 50, v => c.Opacity = v, null, d);
            _ = Ani.Tween(tt.X, -6, 50, v => tt.X = v, null, d);
            id++;
        }
        return controls.Count > 0 ? 70 + 50 : 0;
    }
}
