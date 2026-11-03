using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;
using TYW.Desktop.Controls;

namespace TYW.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly PclNavButton[] _nav = new PclNavButton[5];
    private Control? _pageLeft;
    private Control? _pageRight;
    private int _page = -1;
    private readonly RotateTransform _rotate = new(0);
    private readonly TranslateTransform _translate = new(0, 0);

    public MainWindow()
    {
        InitializeComponent();

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        ExtendClientAreaToDecorationsHint = false;

        if (OperatingSystem.IsMacOS()) SetupMacWindow();

        // 软件渲染（llvmpipe）下窗口阴影的模糊很贵，可用 TYW_NO_SHADOW=1 关掉做对比
        if (Environment.GetEnvironmentVariable("TYW_NO_SHADOW") == "1")
            BorderForm.BoxShadow = default;

        // 入场变换：旋转 -4°、下移 60（对应 FormMain.xaml 的 TransformRotate / TransformPos）
        var tg = new TransformGroup();
        tg.Children.Add(_rotate);
        tg.Children.Add(_translate);
        PanBack.RenderTransform = tg;

        _nav[0] = BtnTitleSelect0;
        _nav[1] = BtnTitleSelect1;
        _nav[2] = BtnTitleSelect2;
        _nav[3] = BtnTitleSelect3;
        _nav[4] = BtnTitleSelect4;
        for (int i = 0; i < _nav.Length; i++)
        {
            int idx = i;
            _nav[i].Click += (_, _) => SwitchPage(idx);
        }

        BtnTitleClose.Click += (_, _) => Close();
        BtnTitleMin.Click += (_, _) => WindowState = WindowState.Minimized;

        // 无边框窗口靠标题栏拖动；点到按钮/输入框时不能拖，否则按钮的点击会被拖拽吞掉
        PanTitle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Control c && IsInteractive(c)) return;
            BeginMoveDrag(e);
        };

        // 右下角「关闭 Minecraft」：对应 FormMain 的 BtnExtraShutdown
        BtnExtraShutdown.Click += (_, _) => CloseGame();

        // 右下角任务按钮：有下载/安装任务时出现，显示进度，点击打开任务管理
        BtnExtraTask.Click += (_, _) => TaskCenter.ShowWindow(this);
        TaskCenter.Changed += RefreshTaskButton;
        RefreshTaskButton();

        // 左栏底色宽度跟随左栏实际宽度
        PanMainLeft.SizeChanged += (_, e) => RectLeftBackground.Width = Math.Max(0, e.NewSize.Width);

        Opened += (_, _) =>
        {
            // 主题：绑定窗口后刷新顶栏渐变与主页面背景（对应 Windows 版 ThemeRefreshMain）
            ThemeEngine.Attach(this);
            ThemeEngine.RefreshMain();

            var navEnv = Environment.GetEnvironmentVariable("TYW_SNAPSHOT_NAV");
            SwitchPage(int.TryParse(navEnv, out var navIdx) ? navIdx : 0);

            // 服务端流程：封禁查询 / 公告 / 云控 / 检查更新 / 补报（对应 Windows 版 ServerLoader）
            LinkPage.Attach(this);
            ModStartup.Run(this);

            // 先把初态摆好（不可见），等首帧布局/渲染完成后再启动补间，
            // 否则软件渲染下首帧阻塞会让动画在阻塞期间“空转完”，用户看到的是硬切。
            if (Environment.GetEnvironmentVariable("TYW_NO_ANIM") != "1")
            {
                PanBack.Opacity = 0;
                _rotate.Angle = -4;
                _translate.Y = 60;
                Dispatcher.UIThread.Post(() => _ = AnimateWindowIn(),
                    Avalonia.Threading.DispatcherPriority.Loaded);
            }

            AfterShown();
        };
    }

    /// <summary>
    /// macOS 专属收尾：无边框窗口在 macOS 上拿不到系统给的缩放边缘，
    /// 这里在窗口四周铺透明热区，用 BeginResizeDrag 手动实现缩放；
    /// 顺带把 Dock / Cmd+Tab 用的窗口图标设上（.app 里的 icon 由 Info.plist 管）。
    /// </summary>
    private void SetupMacWindow()
    {
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://TYW.Desktop/Assets/appicon.png"));
            Icon = new WindowIcon(s);
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "设置窗口图标");
        }

        // 热区要铺在内容之上，所以给内容套一层 Grid
        if (Content is not Control inner) return;
        var host = new Grid();
        Content = null;
        host.Children.Add(inner);
        Content = host;

        const double edge = 8, corner = 14;

        void Zone(double w, double h, Avalonia.Layout.HorizontalAlignment ha,
                  Avalonia.Layout.VerticalAlignment va, WindowEdge where, StandardCursorType cursor)
        {
            var zone = new Border
            {
                Width = w,
                Height = h,
                Background = Brushes.Transparent,
                HorizontalAlignment = ha,
                VerticalAlignment = va,
                Cursor = new Cursor(cursor)
            };
            zone.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                BeginResizeDrag(where, e);
                e.Handled = true;
            };
            host.Children.Add(zone);
        }

        const double nan = double.NaN;
        const Avalonia.Layout.HorizontalAlignment HL = Avalonia.Layout.HorizontalAlignment.Left;
        const Avalonia.Layout.HorizontalAlignment HR = Avalonia.Layout.HorizontalAlignment.Right;
        const Avalonia.Layout.HorizontalAlignment HC = Avalonia.Layout.HorizontalAlignment.Center;
        const Avalonia.Layout.VerticalAlignment VT = Avalonia.Layout.VerticalAlignment.Top;
        const Avalonia.Layout.VerticalAlignment VB = Avalonia.Layout.VerticalAlignment.Bottom;
        const Avalonia.Layout.VerticalAlignment VC = Avalonia.Layout.VerticalAlignment.Center;

        Zone(edge, nan, HL, VC, WindowEdge.West, StandardCursorType.SizeWestEast);
        Zone(edge, nan, HR, VC, WindowEdge.East, StandardCursorType.SizeWestEast);
        Zone(nan, edge, HC, VT, WindowEdge.North, StandardCursorType.SizeNorthSouth);
        Zone(nan, edge, HC, VB, WindowEdge.South, StandardCursorType.SizeNorthSouth);
        Zone(corner, corner, HL, VT, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner);
        Zone(corner, corner, HR, VT, WindowEdge.NorthEast, StandardCursorType.TopRightCorner);
        Zone(corner, corner, HL, VB, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner);
        Zone(corner, corner, HR, VB, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner);
    }

    /// <summary>对应 FormMain 的窗口入场：旋转 -4°、下移 60、渐显。</summary>
    private async System.Threading.Tasks.Task AnimateWindowIn()
    {
        if (Environment.GetEnvironmentVariable("TYW_NO_ANIM") == "1") return;
        var t1 = Ani.Tween(0, 1, 300, v => PanBack.Opacity = v, Ani.EaseOutFluent);
        var t2 = Ani.Tween(-4, 0, 320, v => _rotate.Angle = v, Ani.EaseOutFluent);
        var t3 = Ani.Tween(60, 0, 320, v => _translate.Y = v, Ani.EaseOutFluent);
        await System.Threading.Tasks.Task.WhenAll(t1, t2, t3);
    }

    // ================= 运行中的 Minecraft（对应 ModWatcher + BtnExtraShutdown） =================

    private System.Diagnostics.Process? _game;

    /// <summary>启动页把游戏进程交进来后，右下角就出现「关闭 Minecraft」。</summary>
    public void SetGameRunning(System.Diagnostics.Process? game)
    {
        if (ReferenceEquals(_game, game)) return;
        _game = game;
        BtnExtraShutdown.Show = game != null;
        if (game == null) return;
        try
        {
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_game, game)) SetGameRunning(null);
            });
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "监视游戏进程");
        }
    }

    /// <summary>对应 FormMain.BtnExtraShutdown_Click：结束所有运行中的 Minecraft。</summary>
    private void CloseGame()
    {
        var p = _game;
        SetGameRunning(null);
        if (p == null) return;
        try
        {
            if (!p.HasExited) p.Kill(true);
            ModLog.Info("已关闭运行中的 Minecraft");
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "关闭 Minecraft");
        }
    }

    /// <summary>右下角任务按钮状态：有运行中的任务就显示并跟着进度填充，全部结束后 2 秒收起。</summary>
    private DispatcherTimer? _taskHideTimer;

    private void RefreshTaskButton()
    {
        try
        {
            var p = TaskCenter.RunningProgress;
            if (p >= 0)
            {
                _taskHideTimer?.Stop();
                BtnExtraTask.Show = true;
                BtnExtraTask.Progress = p;
                BtnExtraTask.Tip = "任务管理 · " + Math.Round(p * 100) + "%";
                return;
            }

            BtnExtraTask.Progress = 1;
            BtnExtraTask.Tip = "任务管理";
            if (!BtnExtraTask.IsVisible) return;
            if (_taskHideTimer == null)
            {
                _taskHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _taskHideTimer.Tick += (_, _) =>
                {
                    _taskHideTimer!.Stop();
                    if (!TaskCenter.HasRunning) BtnExtraTask.Show = false;
                };
            }
            _taskHideTimer.Stop();
            _taskHideTimer.Start();
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "刷新任务按钮");
        }
    }

    /// <summary>指针是不是落在可交互控件上（标题栏拖动的白名单判断）。</summary>
    private static bool IsInteractive(Control source)
    {
        for (Control? v = source; v != null; v = v.Parent as Control)
        {
            if (v is PclNavButton or PclIconButton or PclButton or PclComboBox or PclExtraButton) return true;
            if (v is TextBox) return true;
            if (v.Name == "PanTitle") return false;
        }
        return false;
    }

    private int _switchGen;

    public void SwitchPage(int index)
    {
        if (_page == index && _pageLeft != null) return;
        _page = index;
        for (int i = 0; i < _nav.Length; i++) _nav[i].NavChecked = i == index;
        _ = DoSwitch(index, ++_switchGen);
    }

    /// <summary>
    /// 对应 FormMain.PageChangeAnim：旧页逐个退出 → 换内容 → 新页逐个进入。
    /// 旧右页卡片上移淡出（15ms 错开）、旧左栏左移淡出，约 140ms 后换页。
    /// </summary>
    private async System.Threading.Tasks.Task DoSwitch(int index, int gen)
    {
        // 1) 退出动画（旧页面还在树上，只是整体不可交互）
        PanMainLeft.IsHitTestVisible = false;
        PanMainRight.IsHitTestVisible = false;
        int exitRightMs = PageAnim.ExitRight(_pageRight);
        PageAnim.ExitLeft(_pageLeft);

        // Windows 版在退出动画播到一半（110ms + 30ms）时就换内容，这里保持同一节奏
        int wait = Math.Min(Math.Max(140, exitRightMs / 2), 260) * Ani.Slow;
        await System.Threading.Tasks.Task.Delay(wait);
        if (gen != _switchGen) return; // 期间又点了别的导航，本次作废

        // 2) 换内容
        Control left;
        Control right;
        if (index == 0)
        {
            var l = new PageLaunchLeft();
            var r = new PageLaunchRight();
            l.Attach(this, r);
            left = l;
            right = r;
        }
        else
        {
            var (sl, sr) = StaticPage.Create(index);
            left = sl;
            right = sr;
        }

        _pageLeft = left;
        _pageRight = right;
        PanMainLeft.Child = left;
        PanMainRight.Child = right;
        PanMainLeft.IsHitTestVisible = true;
        PanMainRight.IsHitTestVisible = true;

        // 3) 进入动画
        if (Environment.GetEnvironmentVariable("TYW_NO_ANIM") != "1")
        {
            PageAnim.EnterLeft(left);
            PageAnim.EnterRight(right);
        }
    }

    // ================= 左下角提示（对应 Windows 版 Hint） =================

    private Border _hintToast;
    private DispatcherTimer _hintTimer;

    /// <summary>左下角滑出提示条，4 秒后消失。red = 红色警告。</summary>
    public void ShowHint(string text, bool red)
    {
        try
        {
            if (_hintToast != null) PanBack.Children.Remove(_hintToast);
            _hintTimer?.Stop();

            _hintToast = new Border
            {
                Background = red ? new SolidColorBrush(Color.Parse("#ffdddf")) : Pcl.Res("ColorBrushBg0"),
                BorderBrush = red ? new SolidColorBrush(Color.Parse("#99FF4444")) : Pcl.Res("ColorBrush6"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 7, 12, 7),
                Opacity = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(18, 0, 0, 16),
                MaxWidth = 460,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    FontFamily = Pcl.FontFamily,
                    Foreground = red ? new SolidColorBrush(Color.Parse("#bf0b0b")) : Pcl.Res("ColorBrush1"),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            PanBack.Children.Add(_hintToast);
            _ = Ani.Tween(0, 1, 180, v => _hintToast.Opacity = v, Ani.EaseOutFluent);

            _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };
            var toast = _hintToast;
            _hintTimer.Tick += (_, _) =>
            {
                _hintTimer.Stop();
                _ = Ani.Tween(1, 0, 300, v => toast.Opacity = v, Ani.EaseOutFluent)
                    .ContinueWith(_ => Dispatcher.UIThread.Post(() => PanBack.Children.Remove(toast)));
            };
            _hintTimer.Start();
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "显示提示");
        }
    }

    // ================= 调试快照：TYW_SNAPSHOT=路径 时把窗口渲染成 png 后退出 =================

    private void AfterShown()
    {
        // 动画数值探针：TYW_ANIM_LOG=文件 时按 16ms 采样入场动画的 opacity/angle/y（不渲染，不干扰时序）
        var logPath = Environment.GetEnvironmentVariable("TYW_ANIM_LOG");
        if (!string.IsNullOrEmpty(logPath))
        {
            var swLog = System.Diagnostics.Stopwatch.StartNew();
            var logger = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            logger.Tick += (_, _) =>
            {
                try
                {
                    File.AppendAllText(logPath,
                        $"{swLog.Elapsed.TotalMilliseconds,7:F0} {PanBack.Opacity:F3} {_rotate.Angle,7:F2} {_translate.Y,6:F1}{Environment.NewLine}");
                }
                catch { }
                if (swLog.Elapsed.TotalMilliseconds > 1500) logger.Stop();
            };
            logger.Start();
        }

        // 动画逐帧取证：TYW_ANIM_SHOTS=目录 时按动画进度抓帧（配合 TYW_ANIM_SLOW 慢放）
        var shots = Environment.GetEnvironmentVariable("TYW_ANIM_SHOTS");
        if (!string.IsNullOrEmpty(shots))
        {
            try { Directory.CreateDirectory(shots); } catch { }
            int totalMs = 320 * Ani.Slow;
            const int n = 9;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        SaveSnapshot(Path.Combine(shots, $"a{idx}.png"));
                        File.AppendAllText(Path.Combine(shots, "values.txt"),
                            $"{totalMs * idx / (n - 1.0),6:F0}ms  opacity={PanBack.Opacity:F3}  angle={_rotate.Angle:F2}  y={_translate.Y:F1}{Environment.NewLine}");
                    }
                    catch { }
                }, TimeSpan.FromMilliseconds(totalMs * i / (n - 1.0)));
            }
            if (Environment.GetEnvironmentVariable("TYW_ANIM_SHOTS_EXIT") == "1")
                DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(totalMs + 500));
        }

        // 实例管理窗口快照（调试用）
        var instPath = Environment.GetEnvironmentVariable("TYW_SNAPSHOT_INSTANCE");
        if (!string.IsNullOrEmpty(instPath))
        {
            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    var win = new InstanceWindow();
                    win.Show(this);
                    DispatcherTimer.RunOnce(() =>
                    {
                        try
                        {
                            var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(
                                new PixelSize((int)win.ClientSize.Width, (int)win.ClientSize.Height), new Vector(96, 96));
                            bmp.Render(win);
                            bmp.Save(instPath);
                            Console.WriteLine("instance snapshot saved: " + instPath);
                        }
                        catch (Exception ex) { Console.WriteLine("instance snapshot failed: " + ex.Message); }
                        win.Close();
                        if (Environment.GetEnvironmentVariable("TYW_SNAPSHOT_EXIT") == "1") Close();
                    }, TimeSpan.FromMilliseconds(800));
                }
                catch (Exception ex) { Console.WriteLine("instance window failed: " + ex.Message); }
            }, TimeSpan.FromMilliseconds(600));
            return;
        }

        var path = Environment.GetEnvironmentVariable("TYW_SNAPSHOT");
        if (string.IsNullOrEmpty(path)) return;

        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                SaveSnapshot(path);
                Console.WriteLine("snapshot saved: " + path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("snapshot failed: " + ex.Message);
            }
            if (Environment.GetEnvironmentVariable("TYW_SNAPSHOT_EXIT") == "1") Close();
        }, TimeSpan.FromMilliseconds(1200));
    }

    public void SaveSnapshot(string path)
    {
        int w = (int)Math.Ceiling(ClientSize.Width);
        int h = (int)Math.Ceiling(ClientSize.Height);
        if (w <= 0 || h <= 0) { w = (int)Width; h = (int)Height; }
        var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        bmp.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path);
    }
}
