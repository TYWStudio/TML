using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TYW.Desktop.Controls;

/// <summary>
/// 简易补间动画，对应 Windows 版 <c>Modules/Base/ModAnimation.vb</c> 的 Aa 系列。
/// 缓动名与 PCL 的 AniEaseOutFluent / AniEaseOutBack 对齐。
/// </summary>
public static class Ani
{
    /// <summary>调试用慢放倍数（TYW_ANIM_SLOW=8 时动画时长 ×8），仅用于肉眼/截图验证动画。</summary>
    public static readonly int Slow =
        int.TryParse(Environment.GetEnvironmentVariable("TYW_ANIM_SLOW"), out var s) && s > 0 ? s : 1;

    public static double EaseOutFluent(double t) => 1 - Math.Pow(1 - t, 3.2);

    public static double EaseInFluent(double t) => Math.Pow(t, 3.2);

    public static double EaseOutBack(double t)
    {
        const double c1 = 1.20158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    public static double Linear(double t) => t;

    /// <summary>从 from 补间到 to，逐帧回调。返回的 Task 在动画结束（或被打断）后完成。</summary>
    public static Task Tween(double from, double to, int ms, Action<double> apply,
                             Func<double, double>? easing = null, int delayMs = 0)
    {
        var tcs = new TaskCompletionSource();
        var sw = Stopwatch.StartNew();
        ms *= Slow;
        delayMs *= Slow;
        double acc = 0;          // 累进时间：按帧推进，单帧最多 50ms，避免 UI 卡顿后一次性跳到终点
        double last = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        apply(from);
        timer.Tick += (_, _) =>
        {
            double now = sw.Elapsed.TotalMilliseconds;
            acc += Math.Min(now - last, 50);
            last = now;
            double elapsed = acc - delayMs;
            if (elapsed < 0) return;
            double t = ms <= 0 ? 1 : Math.Min(1, elapsed / ms);
            double v = from + (to - from) * (easing ?? EaseOutFluent)(t);
            apply(v);
            if (t >= 1)
            {
                timer.Stop();
                tcs.TrySetResult();
            }
        };
        timer.Start();
        return tcs.Task;
    }
}
