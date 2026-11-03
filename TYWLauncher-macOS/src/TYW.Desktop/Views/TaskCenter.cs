using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using TYW.Desktop.Controls;
using TYW;

namespace TYW.Desktop.Views;

/// <summary>一个下载/安装任务。对应 Windows 版 LoaderTaskbar 里的条目。</summary>
public sealed class TaskItem
{
    public string Name = "";
    public string Stage = "";
    public double Progress;
    public bool Running = true;
    public bool Failed;
}

/// <summary>
/// 任务中心：右下角进度按钮的数据来源，点击打开任务列表。
/// 对应 Windows 版的任务栏（BtnExtraDownload + 任务管理页）。
/// </summary>
public static class TaskCenter
{
    public static readonly List<TaskItem> Items = new();

    /// <summary>任务列表变化（新增 / 进度 / 完成）时触发，主窗口用来刷新右下角按钮。</summary>
    public static event Action Changed;

    public static TaskItem Begin(string name)
    {
        var item = new TaskItem { Name = name, Stage = "准备中……", Progress = 0 };
        lock (Items) Items.Add(item);
        Notify();
        return item;
    }

    public static void Update(TaskItem task, double progress, string stage)
    {
        if (task == null) return;
        task.Progress = Math.Clamp(progress, 0, 1);
        if (!string.IsNullOrEmpty(stage)) task.Stage = stage;
        Notify();
    }

    public static void Finish(TaskItem task, string stage, bool ok = true)
    {
        if (task == null) return;
        task.Running = false;
        task.Failed = !ok;
        task.Progress = ok ? 1 : task.Progress;
        if (!string.IsNullOrEmpty(stage)) task.Stage = stage;
        Notify();
    }

    /// <summary>进行中任务的平均进度；没有任务返回 -1。</summary>
    public static double RunningProgress
    {
        get
        {
            lock (Items)
            {
                var running = Items.Where(t => t.Running).ToList();
                if (running.Count == 0) return -1;
                return running.Average(t => t.Progress);
            }
        }
    }

    public static bool HasRunning
    {
        get
        {
            lock (Items) return Items.Any(t => t.Running);
        }
    }

    private static void Notify()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try { Changed?.Invoke(); } catch { }
        });
    }

    // ===================== 任务管理窗口 =====================

    public static void ShowWindow(Window owner)
    {
        var win = new Window
        {
            Title = "任务管理",
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Pcl.Res("ColorBrushBg1") as IBrush ?? Brushes.White,
            WindowDecorations = Avalonia.Controls.WindowDecorations.Full
        };
        var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        var title = new TextBlock
        {
            Text = "任务管理",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1")
        };
        root.Children.Add(title);

        var list = new StackPanel { Spacing = 10 };
        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);

        void Refresh()
        {
            list.Children.Clear();
            List<TaskItem> snapshot;
            lock (Items) snapshot = Items.ToList();
            if (snapshot.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "当前没有任务。",
                    FontFamily = Pcl.FontFamily,
                    FontSize = 12.5,
                    Foreground = Pcl.Res("ColorBrushGray2")
                });
                return;
            }
            foreach (var t in snapshot) list.Children.Add(TaskRow(t));
        }

        Refresh();
        void OnChanged() => Refresh();
        Changed += OnChanged;
        win.Closed += (_, _) => Changed -= OnChanged;
        win.Content = root;
        win.ShowDialog(owner);
    }

    private static Control TaskRow(TaskItem t)
    {
        var card = new Border
        {
            Background = Pcl.Res("ColorBrushBg0"),
            BorderBrush = Pcl.Res("ColorBrush6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 10)
        };
        var stack = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock
        {
            Text = t.Name,
            FontSize = 13,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1")
        });
        var status = new TextBlock
        {
            Text = t.Failed ? "失败" : t.Running ? Math.Round(t.Progress * 100) + "%" : "完成",
            FontSize = 12,
            FontFamily = Pcl.FontFamily,
            Foreground = t.Failed ? Pcl.Res("ColorBrushRedDark") : Pcl.Res("ColorBrushGray2")
        };
        Grid.SetColumn(status, 1);
        head.Children.Add(status);
        stack.Children.Add(head);
        stack.Children.Add(new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 1,
            Value = t.Progress,
            Foreground = Pcl.Res("ColorBrush1"),
            Background = Pcl.Res("ColorBrushGray6"),
            CornerRadius = new CornerRadius(2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = t.Stage,
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            TextWrapping = TextWrapping.Wrap
        });
        card.Child = stack;
        return card;
    }
}
