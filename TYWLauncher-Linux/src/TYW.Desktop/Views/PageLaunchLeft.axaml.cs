using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TYW.Desktop.Controls;
using TYW;

namespace TYW.Desktop.Views;

public partial class PageLaunchLeft : UserControl
{
    private MainWindow? _main;
    private PageLaunchRight? _right;
    private McVersionItem? _selected;
    private bool _init;

    /// <summary>离线页的玩家名输入框（对应 PageLoginLegacy 的 ComboName）。</summary>
    private PclComboBox? _comboName;
    /// <summary>名字不合规时的红色提示（对应 PageLoginLegacy 的 HintChinese）。</summary>
    private PclHint? _hintName;

    private PclSkin? _skinMs;
    private PclSkin? _skinLegacy;

    private bool _launching;
    private volatile bool _cancelRequested;
    private System.Diagnostics.Process? _proc;
    private double _shownProgress;

    /// <summary>微软登录的取消标志（登录线程读取）。</summary>
    private volatile bool _msCancel;
    /// <summary>用户主动要求改用配对码。</summary>
    private bool _msLoggingIn;

    public PageLaunchLeft()
    {
        InitializeComponent();

        RadioLoginType5.Click += (_, _) => SetLoginType(5);
        RadioLoginType0.Click += (_, _) => SetLoginType(0);
        BtnLaunch.Click += (_, _) => _ = OnLaunchClick();
        BtnVersion.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null) return;
            var win = new InstanceWindow(() => Build());
            await win.ShowDialog(owner);
        };
        // 版本设置：仅在有选中版本时出现（对应 Windows PageLaunchLeft.RefreshButtonsUI 的 CurrentState = 3）
        BtnMore.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null) return;
            if (ModVersion.SelectedOrDefault() is null)
            {
                _right?.ShowHint("还没有可用的游戏版本，请先到下载页安装一个。");
                return;
            }
            var win = new InstanceWindow(() => Build(), asSettings: true);
            await win.ShowDialog(owner);
        };
        BtnCancelLaunch.Click += (_, _) => OnCancelLaunch();

        Loaded += (_, _) => Build();
    }

    public void Attach(MainWindow main, PageLaunchRight right)
    {
        _main = main;
        _right = right;
    }

    private void Build()
    {
        if (_init) return;
        _init = true;

        SetLoginType(ModSettings.GetInt("LoginType", 0) == 5 ? 5 : 0);
        BuildLoginArea();
        RefreshVersion();
        TryRefreshMsToken();
    }

    /// <summary>重新扫描版本（下载页装完新版本后回到启动页会调用）。</summary>
    public void ReloadVersions()
    {
        ModVersion.Invalidate();
        RefreshVersion();
    }

    /// <summary>上次登录的令牌过期了就静默续期，避免进游戏时才报错。</summary>
    private void TryRefreshMsToken()
    {
        if (ModSettings.GetInt("LoginType", 0) != 5) return;
        var rt = ModSettings.GetString("MsRefreshToken", "");
        if (rt.Length == 0) return;
        long exp = 0;
        long.TryParse(ModSettings.GetString("MsExpiresAt", "0"), out exp);
        if (exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return;

        _ = Task.Run(() =>
        {
            try
            {
                var acc = ModMsLogin.Refresh(rt);
                ModSettings.SetValue("MsName", acc.Name);
                ModSettings.SetValue("MsUuid", acc.Uuid);
                ModSettings.SetValue("MsToken", acc.AccessToken);
                ModSettings.SetValue("MsRefreshToken", acc.RefreshToken);
                ModSettings.SetValue("MsExpiresAt", acc.ExpiresAt.ToString());
                ModSettings.Save();
                Dispatcher.UIThread.Post(() => { if (RadioLoginType5.NavChecked) BuildLoginArea(); });
                ModLog.Info("微软登录已自动续期：" + acc.Name);
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "微软登录续期失败");
            }
        });
    }

    /// <summary>正版 / 离线切换：对应 PageLaunchLeft 的 RadioLoginType5 / 0。</summary>
    private void SetLoginType(int type)
    {
        RadioLoginType5.NavChecked = type == 5;
        RadioLoginType0.NavChecked = type == 0;
        ModSettings.SetValue("LoginType", type.ToString());
        ModSettings.Save();
        if (_init) BuildLoginArea();
    }

    /// <summary>对应 Windows 版 RefreshButtonsUI：没有版本时按钮变成「下载游戏」。</summary>
    private void RefreshVersion()
    {
        _selected = ModVersion.SelectedOrDefault();
        if (_selected is null)
        {
            LabVersion.Text = "未找到可用的游戏版本";
            BtnLaunch.Text = "下载游戏";
            BtnLaunch.IsEnabledButton = true;
            BtnVersion.IsEnabledButton = true;
            // 没版本 / 没选版本时不显示「版本设置」（与 Windows 一致）
            BtnMore.IsVisible = false;
            return;
        }
        LabVersion.Text = _selected.Id;
        BtnLaunch.Text = "启动游戏";
        BtnLaunch.IsEnabledButton = true;
        BtnVersion.IsEnabledButton = true;
        BtnMore.IsVisible = true;
    }

    // ===================== PanLogin：对应 PageLoginMs / PageLoginLegacy =====================

    private void BuildLoginArea()
    {
        PanLogin.Children.Clear();
        PanType.IsVisible = true;
        PanTypeOne.IsVisible = false;
        _comboName = null;
        _hintName = null;

        if (RadioLoginType5.NavChecked)
        {
            // 已经登录过就直接显示账号
            if (!string.IsNullOrEmpty(ModSettings.GetString("MsName", "")))
            {
                ShowLoggedIn();
                return;
            }
            SwapLogin(BuildMsPage());
        }
        else
        {
            SwapLogin(BuildLegacyPage());
        }
    }

    /// <summary>登录区内容替换（对应 Windows 版登录方式切换的 TriggerShowAnimation）。</summary>
    private void SwapLogin(Control content)
    {
        PageAnim.SwapLeft(PanLogin, () =>
        {
            PanLogin.Children.Clear();
            PanLogin.Children.Add(content);
        });
    }

    /// <summary>
    /// 对应 Pages/PageLaunch/PageLoginMs.xaml（正版未登录）：
    /// Grid Margin 0,10,0,0，列 * / Auto，行 Auto / 25 / Auto / Auto
    ///   ├ 人形图标（Width 40，ColorBrush2，Opacity 0.7，跨两列）
    ///   ├ 账号下拉（第 3 行，第 1 列）
    ///   ├ 「登录」按钮 Height 28 Width 60 Margin 10,0,0,0（第 3 行，第 2 列）
    ///   └ 「» 购买正版」「» 前往官网」Opacity 0.3 Margin 2,30,2,0 居中
    /// </summary>
    private Control BuildMsPage()
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        g.RowDefinitions.Add(new RowDefinition(new GridLength(25)));
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var icon = new Path
        {
            Data = Geometry.Parse(LogoIcon.Person),
            Fill = Pcl.Res("ColorBrush2"),
            Opacity = 0.7,
            Width = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumnSpan(icon, 2);
        g.Children.Add(icon);

        var combo = new PclComboBox
        {
            IsEditable = false,
            Placeholder = "点击右侧按钮登录",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(combo, 2);
        g.Children.Add(combo);

        var btn = new PclButton
        {
            Text = "登录",
            ColorType = PclButton.ColorState.Highlight,
            Width = 60,
            Height = 28,
            Margin = new Thickness(10, 0, 0, 0)
        };
        btn.Click += (_, _) => StartMsLogin();
        Grid.SetRow(btn, 2);
        Grid.SetColumn(btn, 1);
        g.Children.Add(btn);

        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 30, 2, 0),
            Opacity = 0.3,
            Spacing = 30
        };
        links.Children.Add(MakeLink("»  购买正版", "https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj"));
        links.Children.Add(MakeLink("»  前往官网", "https://www.minecraft.net/zh-hans"));
        Grid.SetRow(links, 3);
        Grid.SetColumnSpan(links, 2);
        g.Children.Add(links);

        return g;
    }

    // ===================== 微软登录 =====================

    /// <summary>
    /// 只使用配对码登录（与 Windows 版一致）：申请配对码 → 复制到剪贴板 → 打开浏览器 → 轮询等待。
    /// </summary>
    private void StartMsLogin()
    {
        if (_msLoggingIn) return;
        _msLoggingIn = true;
        _msCancel = false;

        var stack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        var title = Pcl.Text("请在浏览器中输入配对码", 12.5, "ColorBrush1");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);

        var codeBlock = new TextBlock
        {
            Text = "· · · · · ·",
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        stack.Children.Add(codeBlock);

        var tip = new TextBlock
        {
            Text = "正在申请配对码…",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 250
        };
        stack.Children.Add(tip);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnOpen = new PclButton { Text = "重新打开", Height = 28, Width = 84, IsVisible = false };
        var btnCancel = new PclButton { Text = "取消", Height = 28, Width = 60 };
        buttons.Children.Add(btnOpen);
        buttons.Children.Add(btnCancel);
        stack.Children.Add(buttons);

        SwapLogin(stack);

        string deviceUrl = null;
        btnOpen.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(deviceUrl)) OpenInBrowser(deviceUrl);
        };
        btnCancel.Click += (_, _) =>
        {
            _msCancel = true;
            tip.Text = "已取消登录";
        };

        _ = Task.Run(() =>
        {
            try
            {
                var acc = ModMsLogin.Login(
                    dc => Dispatcher.UIThread.Post(() =>
                    {
                        codeBlock.Text = dc.UserCode;
                        deviceUrl = dc.VerificationUri;
                        // 配对码自动进剪贴板，用户直接粘贴即可
                        CopyText(dc.UserCode);
                        tip.Text = "配对码已复制到剪贴板，浏览器即将打开 microsoft.com/link，粘贴登录即可。";
                        btnOpen.IsVisible = true;
                        OpenInBrowser(dc.VerificationUri);
                    }),
                    () => _msCancel,
                    s => Dispatcher.UIThread.Post(() => tip.Text = s));

                if (acc != null)
                {
                    Dispatcher.UIThread.Post(() => OnMsSuccess(acc));
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _msLoggingIn = false;
                        BuildLoginArea();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _msLoggingIn = false;
                    BuildLoginArea();
                });
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "微软登录");
                Dispatcher.UIThread.Post(() =>
                {
                    _msLoggingIn = false;
                    tip.Text = ex.Message;
                    tip.Foreground = Pcl.Res("ColorBrushRed") as IBrush ?? tip.Foreground;
                });
            }
        });
    }

    private void OnMsSuccess(MsAccount acc)
    {
        _msLoggingIn = false;
        ModSettings.SetValue("MsName", acc.Name);
        ModSettings.SetValue("MsUuid", acc.Uuid);
        ModSettings.SetValue("MsToken", acc.AccessToken);
        ModSettings.SetValue("MsRefreshToken", acc.RefreshToken);
        ModSettings.SetValue("MsExpiresAt", acc.ExpiresAt.ToString());
        ModSettings.SetValue("LoginType", "5");
        ModSettings.Save();
        ModLog.Info("微软登录成功：" + acc.Name);
        BuildLoginArea();
    }

    /// <summary>已登录：头像 + 名字 + 退出。正版/离线切换条保持可见，随时能切回离线（与 Windows 版一致）。</summary>
    private void ShowLoggedIn()
    {
        // Windows 版 LoginPageType = 0（正版或离线）时 PanType 始终可见，登录后仍能一键切到离线
        PanType.IsVisible = true;
        PanTypeOne.IsVisible = false;
        LabTypeOne.Text = ModSettings.GetString("MsName", "");
        try { PathTypeOne.Data = Geometry.Parse(LogoIcon.Person); } catch { }

        var stack = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var skin = new PclSkin { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        _skinMs = skin;
        stack.Children.Add(skin);
        TryApplySkin(skin, ModSettings.GetString("MsUuid", ""), byName: false);

        var name = Pcl.Text(ModSettings.GetString("MsName", "玩家"), 13, "ColorBrush1");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(name);

        var btnOut = new PclButton
        {
            Text = "退出登录",
            Height = 26,
            Width = 84,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        btnOut.Click += (_, _) =>
        {
            ModSettings.SetValue("MsName", "");
            ModSettings.SetValue("MsUuid", "");
            ModSettings.SetValue("MsToken", "");
            ModSettings.SetValue("MsRefreshToken", "");
            ModSettings.Save();
            BuildLoginArea();
        };
        stack.Children.Add(btnOut);

        SwapLogin(stack);
    }

    /// <summary>用系统默认浏览器打开网页（Linux 走 xdg-open）。</summary>
    private static void OpenInBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(url);
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // 没有 xdg-open 时退回 ShellExecute
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "打开浏览器");
            }
        }
    }

    private void CopyText(string text)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            _ = top?.Clipboard?.SetTextAsync(text ?? "");
        }
        catch
        {
            // 剪贴板失败不影响登录
        }
    }

    /// <summary>
    /// 对应 Pages/PageLaunch/PageLoginLegacy.xaml（离线）：
    /// StackPanel Margin 0,0,0,5 → 红色提示（仅名字非法时显示）→ 头像 → 用户名输入框。
    /// </summary>
    private Control BuildLegacyPage()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };

        _hintName = new PclHint
        {
            Theme = "Red",
            Text = "玩家名不能包含中文等非英文字符，否则进不去 Minecraft 1.18 及以后版本的世界！",
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 15)
        };
        stack.Children.Add(_hintName);

        var skin = new PclSkin
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        };
        _skinLegacy = skin;
        stack.Children.Add(skin);

        _comboName = new PclComboBox
        {
            IsEditable = true,
            Placeholder = "游戏用户名",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var history = OfflineNameHistory();
        if (history.Count > 0) _comboName.SetItems(history);
        _comboName.Text = ModSettings.GetString("OfflineName", ModSettings.GetString("PlayerName", ""));
        _comboName.TextChanged += (_, _) =>
        {
            var name = (_comboName.Text ?? "").Trim();
            if (_hintName != null) _hintName.IsVisible = name.Length > 0 && !McOffline.IsValidName(name);
            // 对应 Windows 版：输入合法名字后自动拉取该玩家的皮肤正脸
            if (McOffline.IsValidName(name) && name != _skinNameFetched)
            {
                _skinNameFetched = name;
                TryApplySkin(skin, name, byName: true);
            }
        };
        // 初始也拉一次
        var initName = ModSettings.GetString("OfflineName", ModSettings.GetString("PlayerName", ""));
        if (McOffline.IsValidName(initName))
        {
            _skinNameFetched = initName;
            TryApplySkin(skin, initName, byName: true);
        }
        stack.Children.Add(_comboName);

        return stack;
    }

    private static List<string> OfflineNameHistory()
    {
        var raw = ModSettings.GetString("OfflineNameHistory", "");
        return raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim())
                  .Where(s => s.Length > 0)
                  .Distinct()
                  .Take(10)
                  .ToList();
    }

    private static void RememberOfflineName(string name)
    {
        var list = OfflineNameHistory();
        list.Remove(name);
        list.Insert(0, name);
        ModSettings.SetValue("OfflineNameHistory", string.Join("\n", list.Take(10)));
    }

    /// <summary>拉取玩家皮肤并应用到头像（后台线程，失败保持默认 Steve 脸）。</summary>
    private void TryApplySkin(PclSkin skin, string key, bool byName)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        Task.Run(() =>
        {
            var path = byName ? ModSkin.FetchByName(key) : ModSkin.FetchByUuid(key);
            if (path.Length == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                // 页面可能已经切换，只应用到还活着的头像
                if (skin == _skinMs || skin == _skinLegacy) skin.ApplyFile(path);
            });
        });
    }

    private string? _skinNameFetched;

    /// <summary>对应 MyTextButton：文字按钮，点击打开网页。</summary>
    private static Control MakeLink(string text, string url)
    {
        var tb = Pcl.Text(text, 12.5, "ColorBrush1");
        tb.Cursor = new Cursor(StandardCursorType.Hand);
        tb.PointerReleased += (_, _) => OpenUrl(url);
        return tb;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "打开网页");
        }
    }

    // ===================== 启动 =====================

    private async Task OnLaunchClick()
    {
        if (_launching) return;
        if (_selected is null)
        {
            // 对应 Windows 版：按钮显示「下载游戏」时点它跳到下载页
            _main?.SwitchPage(1);
            return;
        }

        string playerName;
        if (!RadioLoginType5.NavChecked)
        {
            playerName = (_comboName?.Text ?? "").Trim();
            if (!McOffline.IsValidName(playerName))
            {
                if (_hintName != null) _hintName.IsVisible = true;
                _right?.ShowHint("游戏用户名只能使用英文、数字和下划线，长度 3-16 位。");
                return;
            }
            ModSettings.SetValue("OfflineName", playerName);
            ModSettings.SetValue("OfflineUuid", McOffline.UuidOf(playerName));
        }
        else
        {
            playerName = ModSettings.GetString("MsName", "");
            if (playerName.Length == 0)
            {
                _right?.ShowHint("还没有登录正版账号，请先点「登录」。");
                return;
            }
        }

        ModSettings.SetValue("VersionSelect", _selected.Id);
        ModSettings.Save();

        var opt = new LaunchOptions
        {
            VersionId = _selected.Id,
            PlayerName = playerName,
            Uuid = RadioLoginType5.NavChecked
                    ? ModSettings.GetString("MsUuid", "")
                    : McOffline.UuidOf(playerName),
            AccessToken = RadioLoginType5.NavChecked
                    ? ModSettings.GetString("MsToken", "0")
                    : "0",
            UserType = RadioLoginType5.NavChecked ? "msa" : "legacy",
            JavaPath = ModSettings.GetString("LaunchJavaExe", ""),
            MaxRamMb = ModSettings.GetInt("LaunchRam", 2048),
            GameDir = ModVersion.GameDirOf(_selected),
            WindowWidth = ModSettings.GetInt("LaunchWindowWidth", 854),
            WindowHeight = ModSettings.GetInt("LaunchWindowHeight", 480),
            Fullscreen = ModSettings.GetBool("LaunchFullscreen", false),
            JvmExtra = ModSettings.GetString("LaunchJvmExtra", "")
        };

        _launching = true;
        _cancelRequested = false;
        PrepareLaunching();
        await EnterLaunching();

        try
        {
            var proc = await Task.Run(() => ModLaunch.Launch(opt, (stage, p) =>
            {
                if (_cancelRequested) throw new OperationCanceledException();
                Dispatcher.UIThread.Post(() => SetProgress(p, stage));
            }));

            _proc = proc;
            _main?.SetGameRunning(proc);
            if (!RadioLoginType5.NavChecked) RememberOfflineName(playerName);
            ModLog.Info("已启动游戏：" + _selected.Id);

            FinishLaunching();
            await Task.Delay(Ani.Slow > 1 ? 500 : 900);
            await ExitLaunching();
        }
        catch (OperationCanceledException)
        {
            LabLaunchingTitle.Text = "已取消启动";
            SetProgress(_shownProgress, "已取消");
            await Task.Delay(600);
            await ExitLaunching();
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "启动失败");
            LabLaunchingTitle.Text = "启动失败";
            LabLaunchingTitle.Foreground = Pcl.Res("ColorBrushRedDark");
            LabLaunchingStage.Text = ex.Message;
            LabLaunchingStage.Foreground = Pcl.Res("ColorBrushRedDark");
            await Task.Delay(3500);
            await ExitLaunching();
        }
        finally
        {
            _launching = false;
            _proc = null;
        }
    }

    private void OnCancelLaunch()
    {
        if (!_launching) return;
        _cancelRequested = true;
        LabLaunchingTitle.Text = "正在取消…";
        LabLaunchingStage.Text = "等待当前步骤结束";
        try
        {
            if (_proc != null && !_proc.HasExited) _proc.Kill(true);
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "取消启动");
        }
    }

    /// <summary>对应 PageChangeToLaunching：初始化启动页的各项文字与进度。</summary>
    private void PrepareLaunching()
    {
        LabLaunchingTitle.Text = "正在启动游戏";
        LabLaunchingTitle.Foreground = Pcl.Res("ColorBrush3");
        LabLaunchingName.Text = _selected?.Id ?? "";
        LabLaunchingStage.Text = "初始化";
        LabLaunchingStage.Foreground = Pcl.Res("ColorBrush1");
        LabLaunchingMethod.Text = RadioLoginType5.NavChecked ? "正版登录" : "离线登录";
        LabLaunchingProgress.Text = "0.00 %";
        BtnCancelLaunch.IsVisible = true;
        _shownProgress = 0;
        SetBar(0);

        // 重置启动进度行 / 下载速度行 / 你知道吗（对应 PrepareLaunching 的重置段）
        LabLaunchingProgressLeft.Opacity = 0.5;
        LabLaunchingProgressLeft.IsVisible = true;
        LabLaunchingProgress.Opacity = 1;
        LabLaunchingProgress.IsVisible = true;
        LabLaunchingDownloadLeft.IsVisible = false;
        LabLaunchingDownloadLeft.Opacity = 0;
        LabLaunchingDownload.IsVisible = false;
        LabLaunchingDownload.Opacity = 0;
        LabLaunchingDownload.Text = "0 B/s";
        PanLaunchingHint.IsVisible = false;
        PanLaunchingHint.Opacity = 0;
        LabLaunchingHint.Text = RandomHint();

        // 每 0.2s 刷新下载速度（对应 LaunchingRefresh 的 0.1s，这里放宽到 0.2s）
        _launchTick ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _launchTick.Tick -= LaunchTick;
        _launchTick.Tick += LaunchTick;
        _launchTick.Start();
    }

    private DispatcherTimer? _launchTick;
    private bool _speedShown;

    private void LaunchTick(object? sender, EventArgs e)
    {
        long speed = ModDownload.Speed();
        bool has = speed > 512; // 低于 0.5K/s 视为没在下载（可能只在校验文件）
        if (has == _speedShown) { if (has) LabLaunchingDownload.Text = ModDownload.FormatSpeed(speed); return; }
        _speedShown = has;
        LabLaunchingDownloadLeft.IsVisible = true;
        LabLaunchingDownload.IsVisible = true;
        LabLaunchingDownload.Text = ModDownload.FormatSpeed(speed);
        _ = Ani.Tween(LabLaunchingDownloadLeft.Opacity, has ? 0.5 : 0, 90, v => LabLaunchingDownloadLeft.Opacity = v);
        _ = Ani.Tween(LabLaunchingDownload.Opacity, has ? 1 : 0, 90, v =>
        {
            LabLaunchingDownload.Opacity = v;
            if (v == 0 && !has)
            {
                LabLaunchingDownloadLeft.IsVisible = false;
                LabLaunchingDownload.IsVisible = false;
            }
        });
    }

    /// <summary>「你知道吗」随机提示（对应 PageOtherTest.GetRandomHint）。</summary>
    private static string RandomHint()
    {
        string[] hints =
        {
            "按 F3 可以打开调试界面，查看坐标、帧数和区块信息。",
            "按 F5 可以切换第三人称视角。",
            "潜行（Shift）时不会被方块边缘挤下去。",
            "用剪刀可以无损收集树叶和蛛网。",
            "下雨时露天钓鱼效率更高。",
            "僵尸在阳光下会着火，但戴着头盔就不会。",
            "把水放在方块边缘不会流出来，可以用来做装饰。",
            "铁傀儡会主动保护村庄里的村民。",
            "在铁砧上重命名物品是附魔经验之外唯一不耗等级的操作。",
            "末影人会避开水和雨，还会被盯着看而激怒。",
            "使用「/gamerule keepInventory true」死亡后不掉落物品。",
            "猫会送礼物给早晨醒来的主人。",
            "地图上插旗帜可以标记位置。",
            "骨头粉可以催熟作物，也能催出鲜花。",
            "船在冰上滑得飞快，可以做冰道高速交通。"
        };
        return hints[System.Random.Shared.Next(hints.Length)];
    }

    private void FinishLaunching()
    {
        LabLaunchingTitle.Text = "已启动游戏";
        SetProgress(1, "已完成");
        BtnCancelLaunch.IsVisible = false;

        // 启动进度行淡出，「你知道吗」淡入（对应 LaunchingRefresh 的 IsLaunched 分支）
        _launchTick?.Stop();
        if (_speedShown)
        {
            _speedShown = false;
            LabLaunchingDownloadLeft.IsVisible = false;
            LabLaunchingDownload.IsVisible = false;
            LabLaunchingDownloadLeft.Opacity = 0;
            LabLaunchingDownload.Opacity = 0;
        }
        LabLaunchingProgressLeft.IsVisible = true;
        LabLaunchingProgress.IsVisible = true;
        PanLaunchingHint.IsVisible = true;
        _ = Ani.Tween(LabLaunchingProgressLeft.Opacity, 0, 90, v =>
        {
            LabLaunchingProgressLeft.Opacity = v;
            if (v == 0) LabLaunchingProgressLeft.IsVisible = false;
        });
        _ = Ani.Tween(LabLaunchingProgress.Opacity, 0, 90, v =>
        {
            LabLaunchingProgress.Opacity = v;
            if (v == 0) LabLaunchingProgress.IsVisible = false;
        });
        _ = Ani.Tween(0, 1, 90, v => PanLaunchingHint.Opacity = v);
    }

    private async Task EnterLaunching()
    {
        PanLaunching.IsVisible = true;
        PanLaunching.Opacity = 0;
        PanLaunching.IsHitTestVisible = false;
        var sc = (ScaleTransform)PanLaunching.RenderTransform!;
        sc.ScaleX = sc.ScaleY = 0.8;
        var ps = (ScaleTransform)PanInput.RenderTransform!;
        ps.ScaleX = ps.ScaleY = 1;
        PanInput.IsVisible = true;
        PanInput.IsHitTestVisible = false;

        if (Environment.GetEnvironmentVariable("TYW_NO_ANIM") == "1")
        {
            PanInput.IsVisible = false;
            PanLaunching.Opacity = 1;
            sc.ScaleX = sc.ScaleY = 1;
            PanLaunching.IsHitTestVisible = true;
            return;
        }

        // 参数对应 PageChangeToLaunching：
        //   PanInput  淡出 110ms EaseInFluent + 放大到 1.2（160ms）
        //   PanLaunch 淡入 150ms（延迟 100）+ 0.8→1（500ms，弱回弹）
        _ = Ani.Tween(1, 0, 110, v => PanInput.Opacity = v, Ani.EaseInFluent);
        _ = Ani.Tween(1, 1.2, 160, v => { ps.ScaleX = v; ps.ScaleY = v; }, Ani.EaseOutFluent);
        _ = Ani.Tween(0, 1, 150, v => PanLaunching.Opacity = v, Ani.EaseOutFluent, 100);
        _ = Ani.Tween(0.8, 1, 500, v => { sc.ScaleX = v; sc.ScaleY = v; }, Ani.EaseOutBack, 100);
        await Task.Delay(150 * Ani.Slow);
        PanInput.IsVisible = false;
        PanLaunching.IsHitTestVisible = true;
    }

    private async Task ExitLaunching()
    {
        _launchTick?.Stop();
        // 参数对应 PageChangeToLogin：
        //   PanLaunch 淡出 150ms + 缩回 0.8（150ms）
        //   PanInput  淡入 250ms（延迟 50）+ 1→1 回弹（300ms，延迟 50）
        if (Environment.GetEnvironmentVariable("TYW_NO_ANIM") != "1")
        {
            PanLaunching.IsHitTestVisible = false;
            var sc = (ScaleTransform)PanLaunching.RenderTransform!;
            _ = Ani.Tween(PanLaunching.Opacity, 0, 150, v => PanLaunching.Opacity = v);
            _ = Ani.Tween(sc.ScaleX, 0.8, 150, v => { sc.ScaleX = v; sc.ScaleY = v; }, Ani.EaseOutFluent);
            await Task.Delay(50 * Ani.Slow);
            PanInput.IsVisible = true;
            PanInput.Opacity = 0;
            var ps = (ScaleTransform)PanInput.RenderTransform!;
            _ = Ani.Tween(0, 1, 250, v => PanInput.Opacity = v, null, 50);
            _ = Ani.Tween(1, 1, 300, v => { ps.ScaleX = v; ps.ScaleY = v; }, Ani.EaseOutBack, 50);
            await Task.Delay(250 * Ani.Slow);
        }
        else
        {
            PanInput.Opacity = 1;
        }
        PanLaunching.IsVisible = false;
        PanLaunching.IsHitTestVisible = false;
        PanInput.IsHitTestVisible = true;
        PanInput.Opacity = 1;
        if (PanInput.RenderTransform is ScaleTransform st) { st.ScaleX = st.ScaleY = 1; }
    }

    /// <summary>推进度：柱子按补间走，文字直接跟到目标值。</summary>
    private void SetProgress(double p, string stage)
    {
        p = Math.Clamp(p, 0, 1);
        if (!string.IsNullOrEmpty(stage))
        {
            LabLaunchingStage.Text = stage;
            LabLaunchingStage.Foreground = Pcl.Res("ColorBrush1");
        }
        double from = _shownProgress;
        _shownProgress = p;
        if (Math.Abs(p - from) < 0.002)
        {
            SetBar(p);
            return;
        }
        _ = Ani.Tween(from, p, 200, v =>
        {
            SetBar(v);
            LabLaunchingProgress.Text = (v * 100).ToString("0.00") + " %";
        }, Ani.EaseOutFluent);
    }

    private void SetBar(double p)
    {
        PanProgressBar.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(p, 0, 1), GridUnitType.Star);
        PanProgressBar.ColumnDefinitions[1].Width = new GridLength(Math.Clamp(1 - p, 0, 1), GridUnitType.Star);
        LabLaunchingProgress.Text = (p * 100).ToString("0.00") + " %";
    }
}
