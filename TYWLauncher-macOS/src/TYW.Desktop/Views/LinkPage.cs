using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using TYW.Desktop.Controls;
using TYW;

namespace TYW.Desktop.Views;

/// <summary>
/// 联机页（EasyTier）。对应 Windows 版 PageLinkMain：
/// 创建房间 / 输入邀请码加入 → 加载（下载模块、启动、等连接）→ 完成页（邀请码、Ping、人数、退出）。
/// </summary>
public static class LinkPage
{
    private static Window _owner;
    private static Border _root;
    private static readonly DispatcherTimer TickTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>挂到主窗口。</summary>
    public static void Attach(Window owner)
    {
        _owner = owner;
        _root = new Border();
        _root.Child = BuildWaiting();
        TickTimer.Tick += (_, _) => OnTick();
        TickTimer.Start();
    }

    /// <summary>主窗口显示联机页时调用，返回页面内容。</summary>
    public static Control Build()
    {
        if (_root == null)
        {
            throw new InvalidOperationException("LinkPage 未初始化");
        }
        if (ModLink.State == ModLink.LinkStates.Waiting)
            _root.Child = BuildWaiting();
        else if (ModLink.State == ModLink.LinkStates.Loading || ModLink.State == ModLink.LinkStates.Failed)
            _root.Child = BuildLoading();
        else
            _root.Child = BuildFinished();
        return _root;
    }

    // ===================== Waiting =====================

    private static Control BuildWaiting()
    {
        var host = new StackPanel { Spacing = 15 };

        // macOS：EasyTier 建 TUN 必须 root，首版先不给入口，避免点了必然失败
        if (!ModPlatform.SupportsLink)
        {
            var macCard = new PclCard { Title = "联机（macOS 暂未开放）", Margin = new Thickness(0) };
            macCard.Body.Margin = new Thickness(15, 6, 15, 12);
            macCard.Body.Children.Add(new TextBlock
            {
                Text = "macOS 上 EasyTier 需要创建 TUN 虚拟网卡，必须以管理员权限运行，" +
                       "目前正在适配（后续版本开放，房间协议与 Windows / Linux 版互通）。\n\n" +
                       "现在想在 macOS 上和朋友联机，可以先让朋友用 Windows / Linux 版开房间，你直接填服务器的直连地址进入。",
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"),
                TextWrapping = TextWrapping.Wrap
            });
            host.Children.Add(macCard);
            return host;
        }

        host.Children.Add(Card("创建房间",
            "把你的单人世界或局域网服务器分享给朋友。\n创建后会生成邀请码，发给朋友即可加入。",
            "创建房间", () => OnCreate()));

        host.Children.Add(Card("加入房间",
            "输入房主发给你的邀请码，直接加入对方的联机房间。",
            "加入房间", () => OnJoin()));

        host.Children.Add(new PclHint
        {
            Text = "联机使用 EasyTier 组网，与 Windows 版创建的房间互通。" +
                   "首次联机会自动下载联机模块（约 10MB）。",
            Margin = new Thickness(0, 5, 0, 0)
        });
        return host;
    }

    private static Control Card(string title, string desc, string buttonText, Action click)
    {
        var card = new PclCard { Title = title, Margin = new Thickness(0) };
        var stack = card.Body;
        stack.Margin = new Thickness(15, 6, 15, 12);
        stack.Spacing = 8;
        stack.Children.Add(new TextBlock
        {
            Text = desc,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            TextWrapping = TextWrapping.Wrap
        });
        var btn = new PclButton { Text = buttonText, Height = 30, Width = 110,
            ColorType = PclButton.ColorState.Highlight,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        btn.Click += (_, _) => click();
        stack.Children.Add(btn);
        return card;
    }

    private static async void OnCreate()
    {
        var port = await PclMsgBox.ShowInput(_owner, "创建房间",
            "在单人游戏的暂停菜单选择「对局域网开放」，然后输入端口数字。\n甚至可以输入其他游戏的端口……嗯……",
            "25565");
        if (port == null) return;
        if (!int.TryParse(port, out var p) || p < 1024 || p > 65535)
        {
            ShowMsg("端口有误", "端口必须是 1024 - 65535 之间的数字。");
            return;
        }
        ModLink.CreateRoom(p);
        ModLog.Info($"创建房间，网络名 {ModLink.NetworkName}，端口 {p}");
        StartLoading();
    }

    private static async void OnJoin()
    {
        var code = await PclMsgBox.ShowInput(_owner, "加入房间", "输入房主发给你的邀请码。", "");
        if (code == null) return;
        var err = ModLink.JoinRoom(code);
        if (err != null)
        {
            ShowMsg("邀请码有误", err);
            return;
        }
        ModLog.Info($"加入房间，网络名 {ModLink.NetworkName}，端口 {ModLink.ServerPort}");
        StartLoading();
    }

    // ===================== Loading / Failed =====================

    private static TextBlock _loadTitle;
    private static TextBlock _loadDesc;
    private static ProgressBar _loadBar;

    private static Control BuildLoading()
    {
        var host = new StackPanel { Spacing = 10 };
        _loadTitle = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = ModLink.State == ModLink.LinkStates.Failed
                ? Pcl.Res("ColorBrushRedDark") : Pcl.Res("ColorBrush1"),
            Text = ModLink.State == ModLink.LinkStates.Failed ? "联机失败" : "联机中"
        };
        host.Children.Add(_loadTitle);
        _loadBar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1 };
        host.Children.Add(_loadBar);
        _loadDesc = new TextBlock
        {
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            TextWrapping = TextWrapping.Wrap,
            Text = ModLink.LastError == "" ? "准备中……" : ModLink.LastError
        };
        host.Children.Add(_loadDesc);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        if (ModLink.State == ModLink.LinkStates.Failed)
        {
            var retry = new PclButton { Text = "重试", Height = 30, Width = 90 };
            retry.Click += (_, _) => StartLoading();
            btnRow.Children.Add(retry);
        }
        var cancel = new PclButton { Text = "返回", Height = 30, Width = 90 };
        cancel.Click += (_, _) =>
        {
            ModLink.ProcessStop();
            ModLink.State = ModLink.LinkStates.Waiting;
            Refresh();
        };
        btnRow.Children.Add(cancel);
        host.Children.Add(btnRow);
        return host;
    }

    // ===================== 加载线程 =====================

    private static void StartLoading()
    {
        ModLink.State = ModLink.LinkStates.Loading;
        ModLink.LastError = "";
        ModLink.Progress = 0;
        Refresh();
        Task.Run(() =>
        {
            try
            {
                Report(0.02, "正在初始化联机配置……");
                ModServer.Refresh();

                Report(0.05, "正在准备联机模块……");
                ModLink.EnsureEasyTier((p, s) => Report(p, s));

                Report(0.5, "正在启动联机模块……");
                ModLink.Peers = null;
                ModLink.CliFailCount = 0;
                ModLink.StartCore();
                Report(0.55, "正在连接……");

                // 等待连接（最长约 60 秒）
                var timeout = 0;
                while (true)
                {
                    Thread.Sleep(500);
                    ModLink.ReadCoreLogs();
                    if (ModLink.HasCrashed())
                    {
                        string recent;
                        lock (ModLink.LogHistory) recent = string.Join("\n", ModLink.LogHistory);
                        throw new Exception("联机模块已崩溃\n近期日志：\n" + recent);
                    }
                    ModLink.RefreshPeers();
                    var ping = ModLink.GetPeerPing();
                    if (ping > 0)
                    {
                        ModLog.Info($"已建立连接，Ping {ping:0.0}ms");
                        break;
                    }
                    var count = ModLink.Peers?.Count ?? -1;
                    if (count <= 0) Report(0.6, "正在连接到节点……");
                    else Report(0.7, "正在连接到房主……");
                    if (++timeout > 120)
                    {
                        var hint = count <= 0
                            ? "无法连接到节点，请检查网络环境。"
                            : "无法连接到房主。\n可能原因：你或房主网络不佳、房主已关闭房间、邀请码输错。";
                        throw new Exception(hint);
                    }
                }

                Report(1, "已连接");
                ModLink.State = ModLink.LinkStates.Finished;
                Ui(() => Refresh());
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "联机");
                ModLink.ProcessStop();
                ModLink.State = ModLink.LinkStates.Failed;
                ModLink.LastError = ex.Message;
                Ui(() => Refresh());
            }
        });
    }

    private static void Report(double p, string text)
    {
        ModLink.Progress = p;
        ModLink.StageText = text;
        Ui(() =>
        {
            if (_loadBar != null) _loadBar.Value = p;
            if (_loadDesc != null && ModLink.State == ModLink.LinkStates.Loading) _loadDesc.Text = text;
        });
    }

    // ===================== Finished =====================

    private static TextBlock _finishTitle;
    private static TextBlock _finishPing;
    private static TextBlock _finishPlayers;
    private static TextBlock _finishCode;
    private static TextBlock _finishIp;
    private static int _tick;

    private static Control BuildFinished()
    {
        var host = new StackPanel { Spacing = 12 };
        _finishTitle = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            Text = ModLink.IsServerSide ? "已创建房间" : "已加入房间"
        };
        host.Children.Add(_finishTitle);

        if (ModLink.IsServerSide)
        {
            host.Children.Add(new TextBlock
            {
                Text = "把邀请码发给朋友，让大家加入房间吧！（点击复制）",
                FontSize = 12.5, FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"), TextWrapping = TextWrapping.Wrap
            });
            _finishCode = new TextBlock
            {
                Text = ModLink.GetInviteCode(),
                FontSize = 17, FontWeight = FontWeight.Bold,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush3"),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            _finishCode.PointerReleased += (_, _) =>
            {
                CopyText("在 TYW Launcher 启动器中输入邀请码【" + ModLink.GetInviteCode() + "】，即可加入联机房间！");
                Hint("已复制邀请码！", false);
            };
            host.Children.Add(_finishCode);

            host.Children.Add(new TextBlock
            {
                Text = "游戏端口：" + ModLink.ServerPort,
                FontSize = 12.5, FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2")
            });
        }
        else
        {
            host.Children.Add(new TextBlock
            {
                Text = "在多人游戏页面的最下方就能找到联机房间！\n注意：使用离线登录时不要手动输入 IP！",
                FontSize = 12.5, FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"), TextWrapping = TextWrapping.Wrap
            });
            _finishIp = new TextBlock
            {
                Text = "服务器地址：" + ModLink.ClientAddress + "（点击复制）",
                FontSize = 13, FontWeight = FontWeight.Bold,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush3"),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            _finishIp.PointerReleased += (_, _) =>
            {
                CopyText(ModLink.ClientAddress);
                Hint("已复制服务器地址！", false);
            };
            host.Children.Add(_finishIp);
        }

        var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        _finishPing = new TextBlock
        {
            Text = "延迟：--",
            FontSize = 12.5, FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2")
        };
        _finishPlayers = new TextBlock
        {
            Text = "人数：--",
            FontSize = 12.5, FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2")
        };
        infoRow.Children.Add(_finishPing);
        infoRow.Children.Add(_finishPlayers);
        host.Children.Add(infoRow);

        var exit = new PclButton
        {
            Text = ModLink.IsServerSide ? "关闭房间" : "离开房间",
            Height = 30, Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0)
        };
        exit.Click += (_, _) => ExitRoom();
        host.Children.Add(exit);
        return host;
    }

    private static void ExitRoom()
    {
        ModLink.ProcessStop();
        ModLink.State = ModLink.LinkStates.Waiting;
        Refresh();
    }

    // ===================== 周期刷新 =====================

    private static void OnTick()
    {
        if (ModLink.State != ModLink.LinkStates.Finished) return;
        Task.Run(() =>
        {
            try
            {
                ModLink.ReadCoreLogs();
                if (ModLink.HasCrashed())
                {
                    ModLink.LastError = "联机模块已崩溃，请重试。";
                    ModLink.ProcessStop();
                    ModLink.State = ModLink.LinkStates.Failed;
                    Ui(Refresh);
                    return;
                }
                ModLink.RefreshPeers();
                if (ModLink.CliFailCount >= 4)
                {
                    ModLink.LastError = "获取节点信息失败，请重试。";
                    ModLink.ProcessStop();
                    ModLink.State = ModLink.LinkStates.Failed;
                    Ui(Refresh);
                    return;
                }
                if (ModLink.Peers != null && !ModLink.IsServerSide &&
                    !ModLink.Peers.Exists(p => p.Type == "Server") && ModLink.Peers.Count > 0)
                {
                    // 房主离开：连上过节点但没有房主
                    if (_tick > 3)
                    {
                        Ui(() =>
                        {
                            ShowMsg("联机结束", "房主已离开房间！");
                            ExitRoom();
                        });
                        return;
                    }
                }
                // 广播房间端口
                if (++_tick % 2 == 0) ModLink.BroadcastRoom();

                var ping = ModLink.GetPeerPing();
                var players = ModLink.PeopleCount();
                Ui(() =>
                {
                    if (_finishPing != null)
                        _finishPing.Text = ping > 0 ? $"延迟：{(ping >= 10 ? ping.ToString("0") : ping.ToString("0.0"))} ms" : "延迟：连接优化中";
                    if (_finishPlayers != null)
                        _finishPlayers.Text = players > 1 ? $"人数：{players} 人" : "人数：--";
                });
            }
            catch { }
        });
    }

    // ===================== 工具 =====================

    private static void Refresh()
    {
        // 页面内容整体重建（简单可靠）
        if (_root?.Parent is Panel host)
        {
            int idx = host.Children.IndexOf(_root);
            if (idx >= 0)
            {
                host.Children.RemoveAt(idx);
                _root.Child = null;
                _root = new Border();
                _root.Child = ModLink.State switch
                {
                    ModLink.LinkStates.Waiting => BuildWaiting(),
                    ModLink.LinkStates.Finished => BuildFinished(),
                    _ => BuildLoading()
                };
                host.Children.Insert(idx, _root);
            }
        }
    }

    private static void Ui(Action action) => Dispatcher.UIThread.Post(action);

    private static void ShowMsg(string title, string text)
    {
        Dispatcher.UIThread.Post(() => _ = PclMsgBox.Show(_owner, title, text));
    }

    private static void Hint(string text, bool red)
    {
        if (_owner is MainWindow mw) mw.ShowHint(text, red);
    }

    private static void CopyText(string text)
    {
        try
        {
            var top = TopLevel.GetTopLevel(_owner);
            _ = top?.Clipboard?.SetTextAsync(text ?? "");
        }
        catch { }
    }
}
