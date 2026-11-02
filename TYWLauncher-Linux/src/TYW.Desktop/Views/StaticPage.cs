using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TYW.Desktop.Controls;
using TYW;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TYW.Desktop.Views;

/// <summary>
/// 通用页面：左栏列表（条目与图标取自 Windows 版对应 Left.xaml）+ 右栏卡片内容。
/// 页面 1=下载 2=联机 3=设置 4=更多。
/// </summary>
public static class StaticPage
{
    public static (Control Left, Control Right) Create(int index)
    {
        (string Title, string Logo)[] items;
        double width;
        switch (index)
        {
            case 1: items = PageDefs.Download; width = 168; break;
            case 2: items = Array.Empty<(string Title, string Logo)>(); width = 0; break; // 联机页原版左栏为空
            case 3: items = PageDefs.Setup; width = 168; break;
            default: items = PageDefs.Other; width = 168; break;
        }

        var left = new Border { Width = width, Background = Brushes.Transparent };
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        left.Child = list;

        var right = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var host = new StackPanel { Margin = new Thickness(25, 25, 25, 10) };
        right.Content = host;

        void Show(int i, bool animate = false)
        {
            // 首次进入由 MainWindow 的 DoSwitch 统一播动画；页内切换在这里播
            if (animate)
            {
                PageAnim.SwapRight(host, () =>
                {
                    // 必须先清空：之前这里只 Add，导致每次切换左栏条目都把新页面叠在旧页面上
                    host.Children.Clear();
                    host.Children.Add(BuildContent(index, items, i, () => Show(i, animate: true)));
                });
            }
            else
            {
                host.Children.Clear();
                host.Children.Add(BuildContent(index, items, i, () => Show(i, animate: true)));
            }
        }

        var buttons = new List<PclListItem>();
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var item = new PclListItem { Title = items[i].Title, Height = 36 };
            if (!string.IsNullOrEmpty(items[i].Logo))
            {
                try { item.Logo = Geometry.Parse(items[i].Logo); } catch { }
            }
            item.Click += (_, _) =>
            {
                for (int k = 0; k < buttons.Count; k++) buttons[k].Checked = k == idx;
                Show(idx, animate: true);
            };
            buttons.Add(item);
            list.Children.Add(item);
        }
        if (buttons.Count > 0)
        {
            buttons[0].Checked = true;
            Show(0);
        }
        else
        {
            Show(0);
        }

        return (left, right);
    }

    private static Control BuildContent(int page, (string Title, string Logo)[] items, int index, Action rebuild)
    {
        var host = new StackPanel();
        var name = items.Length > index ? items[index].Title : "";

        switch (page)
        {
            case 1:
                if (name == "原版游戏")
                {
                    host.Children.Add(VanillaVersionList(rebuild));
                }
                else
                {
                    // Mod / 整合包 / 数据包 / 资源包 / 光影包：CurseForge + Modrinth 在线下载
                    host.Children.Add(ResPage.Build(name));
                }
                break;

            case 2:
                host.Children.Add(LinkPage.Build());
                break;

            case 3:
                host.Children.Add(SettingsPage.Build(name));
                break;

            default:
                // 帮助 / 关于与鸣谢 / 百宝箱
                host.Children.Add(OtherPage.Build(name));
                break;
        }
        return host;
    }

    // ===================== 原版安装（对应 Windows 版 ShowMinecraftVersionList） =====================

    /// <summary>
    /// 对应下载页「原版游戏」：分类卡片版本列表（最新版本 / 正式版 / 预览版 / 远古版 / 愚人节版），
    /// 点版本行直接安装。分类、排序与 Windows 版 PageDownloadInstall.ShowMinecraftVersionList 一致。
    /// </summary>
    private static Control VanillaVersionList(Action rebuild)
    {
        var host = new StackPanel();

        // 调试：离线核对安装页/列表图标（不联网）
        var autoInstall = Environment.GetEnvironmentVariable("TYW_AUTO_INSTALL");
        if (!string.IsNullOrEmpty(autoInstall))
        {
            ShowInstall(host, new McVersionSummary { Id = autoInstall, Type = "release", ReleaseTime = "" }, rebuild);
            return host;
        }
        if (Environment.GetEnvironmentVariable("TYW_FAKE_MANIFEST") == "1")
        {
            BuildVanillaCards(host, ClassifyVanilla(new List<McVersionSummary>
            {
                new() { Id = "26.3", Type = "release", ReleaseTime = "2026-09-15T11:23:00+00:00" },
                new() { Id = "26.2", Type = "release", ReleaseTime = "2026-06-16T12:03:00+00:00" },
                new() { Id = "26.4-snapshot-6", Type = "snapshot", ReleaseTime = "2026-10-27T13:40:00+00:00" },
                new() { Id = "1.21.1", Type = "release", ReleaseTime = "2025-12-09T12:23:00+00:00" },
                new() { Id = "b1.7.3", Type = "old_beta", ReleaseTime = "2011-07-08T00:00:00+00:00" }
            }), rebuild);
            return host;
        }

        var loadingCard = new PclCard { Title = "正在获取版本列表", Margin = new Thickness(0, 0, 0, 15) };
        loadingCard.Body.Children.Add(new PclLoading { Margin = new Thickness(15, 4, 15, 14) });
        host.Children.Add(loadingCard);

        _ = Task.Run(() =>
        {
            try
            {
                var all = ModDownload.Manifest();
                var dict = ClassifyVanilla(all);
                Dispatcher.UIThread.Post(() =>
                {
                    BuildVanillaCards(host, dict, rebuild);
                    PageAnim.EnterRight(host);
                });
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "获取版本清单");
                Dispatcher.UIThread.Post(() =>
                {
                    host.Children.Clear();
                    var autoVer = Environment.GetEnvironmentVariable("TYW_AUTO_INSTALL");
                    if (!string.IsNullOrEmpty(autoVer))
                    {
                        ShowInstall(host, new McVersionSummary { Id = autoVer, Type = "release", ReleaseTime = "" }, rebuild);
                        return;
                    }
                    host.Children.Add(Card("版本列表获取失败", new[]
                    {
                        "无法连接下载源（" + ex.Message + "）。",
                        "请检查网络后重新进入本页，或在设置中切换下载源。"
                    }));
                });
            }
        });

        return host;
    }

    /// <summary>愚人节版本（与 Windows 版列表一致）。</summary>
    private static readonly string[] FoolIds =
    {
        "20w14infinite", "3d shareware v1.34", "1.rv-pre1", "15w14a", "2.0",
        "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine", "26w14a"
    };

    /// <summary>把清单按 Windows 版规则分成四类，组内按发布时间倒序。</summary>
    private static Dictionary<string, List<McVersionSummary>> ClassifyVanilla(List<McVersionSummary> all)
    {
        var dict = new Dictionary<string, List<McVersionSummary>>
        {
            { "正式版", new List<McVersionSummary>() },
            { "预览版", new List<McVersionSummary>() },
            { "远古版", new List<McVersionSummary>() },
            { "愚人节版", new List<McVersionSummary>() }
        };
        foreach (var v in all)
        {
            string type;
            switch (v.Type)
            {
                case "release":
                    type = "正式版";
                    break;
                case "snapshot":
                    // Mojang 误分类：1.x 开头且不像快照的当正式版
                    if (v.Id.StartsWith("1.") && !ContainsAny(v.Id, "combat", "rc", "experimental", "pre"))
                    {
                        type = "正式版";
                        v.Type = "release"; // 图标跟着 Windows 一起变成草方块
                    }
                    else if (FoolIds.Contains(v.Id) || IsAprilFools(v.ReleaseTime))
                    {
                        type = "愚人节版";
                        v.Type = "special";
                    }
                    else
                    {
                        type = "预览版";
                    }
                    break;
                case "special":
                    type = "愚人节版";
                    break;
                default:
                    type = "远古版";
                    break;
            }
            dict[type].Add(v);
        }
        foreach (var key in dict.Keys.ToList())
            dict[key] = dict[key].OrderByDescending(v => ParseTime(v.ReleaseTime)).ToList();
        return dict;
    }

    private static bool ContainsAny(string s, params string[] parts) =>
        parts.Any(p => s.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsAprilFools(string releaseTime)
    {
        var t = ParseTime(releaseTime);
        // 与 Windows 版一致：UTC 时间 +2h 后落在 4 月 1 日视为愚人节版
        var d = t.ToUniversalTime().AddHours(2);
        return d.Month == 4 && d.Day == 1;
    }

    private static DateTimeOffset ParseTime(string s)
    {
        if (s.Length == 0) return DateTimeOffset.MinValue;
        return DateTimeOffset.TryParse(s, out var t) ? t : DateTimeOffset.MinValue;
    }

    private static string FormatTime(string s)
    {
        var t = ParseTime(s);
        return t == DateTimeOffset.MinValue ? "" : t.ToLocalTime().ToString("yyyy'/'MM'/'dd HH':'mm");
    }

    /// <summary>版本类型图标色（对应原版的草地/命令方块/金块/圆石图标）。</summary>
    private static IBrush TypeColor(string type) => type switch
    {
        "release" => new SolidColorBrush(Color.FromRgb(0x63, 0xA0, 0x4B)),   // 草方块绿
        "snapshot" => new SolidColorBrush(Color.FromRgb(0xB0, 0x7A, 0x3A)),  // 命令方块棕
        "special" => new SolidColorBrush(Color.FromRgb(0xD8, 0xA8, 0x2A)),   // 金块黄
        _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))            // 圆石灰
    };

    private static void BuildVanillaCards(StackPanel host,
        Dictionary<string, List<McVersionSummary>> dict, Action rebuild)
    {
        host.Children.Clear();

        // 调试：TYW_AUTO_INSTALL=<版本号> 时直接进安装选择页，方便截图核对
        var auto = Environment.GetEnvironmentVariable("TYW_AUTO_INSTALL");
        if (!string.IsNullOrEmpty(auto))
        {
            var hit = dict.Values.SelectMany(v => v).FirstOrDefault(v => v.Id == auto);
            // 清单拉不到时用假条目，主要用于离线环境核对安装页布局
            hit ??= new McVersionSummary { Id = auto, Type = "release", ReleaseTime = "" };
            ShowInstall(host, hit, rebuild);
            return;
        }

        // 最新版本：置顶卡片（最新正式版 + 更新的最新预览版）
        var pinned = new List<(McVersionSummary V, string Info)>();
        if (dict["正式版"].Count > 0)
        {
            var r = dict["正式版"][0];
            pinned.Add((r, "最新正式版，发布于 " + FormatTime(r.ReleaseTime)));
        }
        if (dict["正式版"].Count > 0 && dict["预览版"].Count > 0 &&
            ParseTime(dict["预览版"][0].ReleaseTime) > ParseTime(dict["正式版"][0].ReleaseTime))
        {
            var s = dict["预览版"][0];
            pinned.Add((s, "最新预览版，发布于 " + FormatTime(s.ReleaseTime)));
        }
        if (pinned.Count > 0)
        {
            var card = new PclCard { Title = "最新版本", Margin = new Thickness(0, 0, 0, 15), CanSwap = true };
            card.Body.Margin = new Thickness(12, 2, 12, 10);
            foreach (var (v, info) in pinned)
                card.Body.Children.Add(VersionRow(v, info, v => ShowInstall(host, v, rebuild)));
            host.Children.Add(card);
        }

        // 分类卡片：默认折叠（对应 NewCard.IsSwapped = True）
        foreach (var key in new[] { "正式版", "预览版", "远古版", "愚人节版" })
        {
            var list = dict[key];
            if (list.Count == 0) continue;
            var card = new PclCard
            {
                Title = key,
                Margin = new Thickness(0, 0, 0, 15),
                CanSwap = true,
                IsSwapped = true
            };
            card.Body.Margin = new Thickness(12, 2, 12, 10);
            foreach (var v in list)
                card.Body.Children.Add(VersionRow(v, FormatTime(v.ReleaseTime), v => ShowInstall(host, v, rebuild)));
            host.Children.Add(card);
        }
    }

    /// <summary>版本行（对应 McDownloadListItem）：42px，类型图标 + 版本号 + 发布时间。</summary>
    private static Control VersionRow(McVersionSummary v, string info, Action<McVersionSummary> install)
    {
        var item = new PclListItem { Height = 42, Title = v.Id, Info = info };
        item.LogoImage = PclIcons.Block(BlockIconOf(v));
        item.Click += (_, _) => install(v);
        return item;
    }

    /// <summary>
    /// 版本列表的方块图标（与 Windows 版 McDownloadListItem 一致：只看 type）。
    /// 注意：愚人节判定只在快照里做——正式版即使是 4/1 发布的也是草方块，
    /// 之前把日期判定放在最前面，导致 26.1.1（2026/04/01 发布）被显示成金块。
    /// </summary>
    private static string BlockIconOf(McVersionSummary v)
    {
        if (string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase)) return "Grass";
        if (string.Equals(v.Type, "special", StringComparison.OrdinalIgnoreCase)) return "GoldBlock";
        if (string.Equals(v.Type, "snapshot", StringComparison.OrdinalIgnoreCase))
            return FoolIds.Contains(v.Id) || IsAprilFools(v.ReleaseTime) ? "GoldBlock" : "CommandBlock";
        return "CobbleStone";
    }

    /// <summary>
    /// 点版本行后的选择页（对应 Windows 版 PageDownloadInstall）：Minecraft / Fabric / Forge /
    /// NeoForge / Fabric API 五行卡片，行首是 Minecraft 方块贴图（Grass、Fabric、Anvil、NeoForge），
    /// 点行选中后再选版本。
    /// </summary>
    private static void ShowInstall(StackPanel host, McVersionSummary target, Action rebuild)
    {
        PageAnim.SwapRight(host, () =>
        {
            host.Children.Clear();

            var card = new PclCard { Title = "安装 Minecraft " + target.Id, Margin = new Thickness(0, 0, 0, 15) };
            var stack = new StackPanel { Margin = new Thickness(14, 4, 14, 12), Spacing = 2 };
            card.Body.Children.Add(stack);
            host.Children.Insert(0, card);

            // ---- 实例名称 ----
            var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0, 8, 8) };
            var nameLabel = new TextBlock
            {
                Text = "实例名称",
                FontSize = 13,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush1"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(nameLabel, 0);
            nameRow.Children.Add(nameLabel);
            var txtName = new PclComboBox
            {
                IsEditable = true,
                Height = 30,
                Text = target.Id,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 240
            };
            Grid.SetColumn(txtName, 1);
            nameRow.Children.Add(txtName);
            stack.Children.Add(nameRow);

            // ---- 五个选项行 ----
            var rows = new List<Border>();
            var rowKinds = new List<int>();
            var combos = new List<PclComboBox>();
            var selected = new int[1];

            var comboFabric = LoaderCombo();
            var comboForge = LoaderCombo();
            var comboNeo = LoaderCombo();
            combos.Add(comboFabric);
            combos.Add(comboForge);
            combos.Add(comboNeo);

            var chkFabricApi = new CheckBox
            {
                Content = "自动下载",
                IsChecked = true,
                FontFamily = Pcl.FontFamily,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };

            var tip = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 6, 10, 8)
            };

            void Refresh()
            {
                var bg = Pcl.Res("ColorBrush7") as IBrush;
                for (var i = 0; i < rows.Count; i++)
                    rows[i].Background = rowKinds[i] == selected[0] ? bg : Brushes.Transparent;
                chkFabricApi.IsEnabled = selected[0] == 1;
                chkFabricApi.Opacity = selected[0] == 1 ? 1 : 0.45;
                tip.Text = selected[0] switch
                {
                    0 => "仅安装 Minecraft 本体，不含任何加载器。",
                    1 => "Fabric：轻量加载器，大部分 Mod 和整合包的首选。建议同时下载 Fabric API。",
                    3 => "Forge：老牌加载器，会运行官方安装器，耗时较长。",
                    _ => "NeoForge：Forge 的社区分支，1.20.1 及以上版本推荐。"
                };
                // 只有选中的那一行显示版本下拉
                for (var i = 0; i < combos.Count; i++)
                    combos[i].IsVisible = (i == 0 && selected[0] == 1) || (i == 1 && selected[0] == 3) || (i == 2 && selected[0] == 4);
                if (selected[0] == 1) EnsureLoaderList(comboFabric, target, 1);
                else if (selected[0] == 3) EnsureLoaderList(comboForge, target, 3);
                else if (selected[0] == 4) EnsureLoaderList(comboNeo, target, 4);
            }

            void AddRow(int index, string icon, string title, string subtitle, Control right)
            {
                var idx = index;
                var row = OptionRow(icon, title, subtitle, right, () =>
                {
                    if (idx < 0) return; // Fabric API 行只是勾选框
                    selected[0] = idx;
                    Refresh();
                });
                rows.Add(row);
                rowKinds.Add(idx);
                stack.Children.Add(row);
            }

            AddRow(0, "Grass", "Minecraft " + target.Id, "仅原版", null);
            AddRow(1, "Fabric", "Fabric", "轻量加载器，兼容性最好", comboFabric);
            AddRow(3, "Anvil", "Forge", "老牌加载器，安装较慢", comboForge);
            AddRow(4, "NeoForge", "NeoForge", "Forge 社区分支，1.20.1+ 推荐", comboNeo);
            AddRow(-1, "Fabric", "Fabric API", "Fabric 大多数 Mod 的前置", chkFabricApi);

            stack.Children.Add(tip);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 8, 0) };
            var btnStart = new PclButton { Text = "开始安装", Height = 32, Width = 110, ColorType = PclButton.ColorState.Highlight };
            var btnBack = new PclButton { Text = "返回列表", Height = 32, Width = 90 };
            btnRow.Children.Add(btnStart);
            btnRow.Children.Add(btnBack);
            stack.Children.Add(btnRow);

            selected[0] = 0;
            Refresh();

            btnBack.Click += (_, _) => rebuild();
            btnStart.Click += (_, _) =>
            {
                var name = (txtName.Text ?? "").Trim();
                if (name.Length == 0)
                {
                    tip.Text = "请填写实例名称。";
                    return;
                }
                if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                {
                    tip.Text = "实例名称包含非法字符。";
                    return;
                }

                var kind = selected[0] == 2 ? 1 : selected[0]; // Fabric API 行按 Fabric 处理
                var loaderVer = "";
                if (kind == 1) loaderVer = comboFabric.SelectedItem ?? "";
                else if (kind == 3) loaderVer = comboForge.SelectedItem ?? "";
                else if (kind == 4) loaderVer = comboNeo.SelectedItem ?? "";
                if (kind != 0 && (loaderVer.Length == 0 || loaderVer.StartsWith("加载中") || loaderVer == "获取失败"))
                {
                    tip.Text = "加载器版本还没准备好，稍等一下或换个加载器。";
                    return;
                }
                var wantApi = kind == 1 && chkFabricApi.IsChecked == true;
                StartInstall(host, target, name, kind, loaderVer, wantApi, rebuild);
            };
        });
    }

    /// <summary>加载器版本下拉（空壳，第一次选中时才拉列表）。</summary>
    private static PclComboBox LoaderCombo()
    {
        return new PclComboBox
        {
            IsEditable = false,
            Width = 170,
            Height = 28,
            IsVisible = false,
            Placeholder = "版本"
        };
    }

    /// <summary>按需拉取加载器版本列表，只拉一次。</summary>
    private static void EnsureLoaderList(PclComboBox combo, McVersionSummary target, int kind)
    {
        if (combo.Items.Count > 0) return;
        combo.SetItems(new List<string> { "加载中…" }, 0);
        _ = Task.Run(() =>
        {
            List<string> vers = null;
            string err = null;
            try
            {
                vers = kind switch
                {
                    1 => ModLoader.ListFabric(target.Id).Select(e => e.Version).ToList(),
                    3 => ModLoader.ListForge(target.Id).Select(e => e.Version).ToList(),
                    _ => ModLoader.ListNeoForge(target.Id).Select(e => e.Version).ToList()
                };
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (vers is { Count: > 0 }) combo.SetItems(vers, 0);
                else combo.SetItems(new List<string> { "获取失败" }, 0);
                if (err != null) ModLog.Info("加载器列表失败：" + err);
            });
        });
    }

    /// <summary>一行安装选项：图标 + 标题/副标题 + 右侧控件。选中时整行变主色块。</summary>
    private static Border OptionRow(string icon, string title, string subtitle, Control right, Action onClick)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("46,*,Auto"), Height = 46 };
        var img = PclIcons.BlockImage(icon, 26);
        if (img is not null)
        {
            img.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(img, 0);
            grid.Children.Add(img);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1")
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2")
            });
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Child = grid,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        border.PointerPressed += (_, _) => onClick();
        return border;
    }

    /// <summary>执行安装并显示进度。kind：0 原版，1 Fabric，2 Quilt，3 Forge，4 NeoForge。</summary>
    private static void StartInstall(StackPanel host, McVersionSummary target, string name, int kind,
        string loaderVer, bool fabricApi, Action rebuild)
    {
        PageAnim.SwapRight(host, () =>
        {
            host.Children.Clear();

            var titleKind = kind == 0 ? "原版" : kindNames[kind - 1];
            var card = new PclCard { Title = "安装 " + target.Id + (kind == 0 ? "" : " · " + titleKind + " " + loaderVer), Margin = new Thickness(0, 0, 0, 15) };
            var stack = new StackPanel { Margin = new Thickness(15, 8, 15, 13), Spacing = 10 };
            card.Body.Children.Add(stack);

            var bar = new ProgressBar
            {
                Height = 4,
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Foreground = Pcl.Res("ColorBrush1"),
                Background = Pcl.Res("ColorBrushGray6"),
                CornerRadius = new CornerRadius(2)
            };
            stack.Children.Add(bar);

            var status = new TextBlock
            {
                Text = "准备安装 " + name,
                FontSize = 12,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var btnCancel = new PclButton { Text = "取消安装", Width = 90, Height = 30 };
            var btnBack = new PclButton { Text = "返回列表", Width = 90, Height = 30, IsVisible = false };
            buttons.Children.Add(btnCancel);
            buttons.Children.Add(btnBack);
            stack.Children.Add(buttons);

            host.Children.Insert(0, card);

            // 安装目标目录：失败/取消时若原本不存在，就把半成品删掉，避免留下打不开的残留版本
            var installDir = System.IO.Path.Combine(ModPaths.McFolder, "versions", kind == 0 ? target.Id : name);
            var existedBefore = System.IO.Directory.Exists(installDir);

            var canceled = false;
            btnCancel.Click += (_, _) =>
            {
                if (btnCancel.Text == "重试安装")
                {
                    // 直接重新走一遍安装（会重置进度、任务栏状态与按钮）
                    StartInstall(host, target, name, kind, loaderVer, fabricApi, rebuild);
                    return;
                }
                canceled = true;
            };

            // 右下角任务按钮（对应 Windows 的 BtnExtraDownload 任务栏）
            var task = TaskCenter.Begin("安装 " + name +
                (kind == 0 ? "（原版）" : "（" + kindNames[kind - 1] + " " + loaderVer + "）"));

            _ = Task.Run(() =>
            {
                // 只清理本次新建的目录；覆盖安装不动原有文件
                void CleanupResidue()
                {
                    if (existedBefore) return;
                    try
                    {
                        if (System.IO.Directory.Exists(installDir))
                        {
                            System.IO.Directory.Delete(installDir, true);
                            ModLog.Info("已清理安装残留：" + installDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLog.Err(ex, "清理安装残留");
                    }
                }

                try
                {
                    void Report(double p, string s)
                    {
                        TaskCenter.Update(task, p, s);
                        Dispatcher.UIThread.Post(() =>
                        {
                            bar.Value = p;
                            status.Text = s;
                        });
                    }

                    switch (kind)
                    {
                        case 0:
                            ModDownload.Install(target.Id, target.Url, Report, () => canceled);
                            break;
                        case 1:
                            ModLoader.InstallMetaLoader(LoaderKind.Fabric, target.Id, loaderVer, name, Report, () => canceled);
                            break;
                        case 2:
                            ModLoader.InstallMetaLoader(LoaderKind.Quilt, target.Id, loaderVer, name, Report, () => canceled);
                            break;
                        case 3:
                            ModLoader.InstallForge(LoaderKind.Forge, target.Id, loaderVer, name, Report, () => canceled);
                            break;
                        default:
                            ModLoader.InstallForge(LoaderKind.NeoForge, target.Id, loaderVer, name, Report, () => canceled);
                            break;
                    }

                    if (fabricApi && !canceled)
                    {
                        // Fabric API 放进实例 mods 目录（按版本隔离设置决定位置）
                        var fakeItem = new McVersionItem { Id = name, Type = "release", Folder = name };
                        var modsDir = System.IO.Path.Combine(ModVersion.GameDirOf(fakeItem), "mods");
                        ModLoader.DownloadFabricApi(target.Id, modsDir, (p, s) => Report(0.85 + 0.15 * p, s), () => canceled);
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        bar.Value = 1;
                        status.Text = "「" + name + "」安装完成，可以在启动页直接开始游戏。";
                        btnCancel.IsVisible = false;
                        btnBack.IsVisible = true;
                        rebuild();
                    });
                }
                catch (OperationCanceledException)
                {
                    CleanupResidue();
                    Dispatcher.UIThread.Post(() =>
                    {
                        status.Text = "已取消安装，残留文件已清理。";
                        btnCancel.IsVisible = false;
                        btnBack.IsVisible = true;
                    });
                }
                catch (Exception ex)
                {
                    ModLog.Err(ex, "安装版本 " + target.Id);
                    CleanupResidue();
                    Dispatcher.UIThread.Post(() =>
                    {
                        status.Text = "安装失败：" + ex.Message +
                                      (existedBefore ? "" : "\n已清理本次安装残留的文件。");
                        btnCancel.Text = "重试安装";
                        btnCancel.IsVisible = true;
                        btnBack.IsVisible = true;
                    });
                }
            });

            btnBack.Click += (_, _) => rebuild();
        });
    }

    private static readonly string[] kindNames = { "原版", "Fabric", "Quilt", "Forge", "NeoForge" };

    private static PclCard Card(string title, IEnumerable<string> lines)
    {
        var card = new PclCard { Title = title, Margin = new Thickness(0, 0, 0, 15) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };
        foreach (var line in lines)
        {
            stack.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush1"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        card.Body.Children.Add(stack);
        return card;
    }

    private static PclCard InstalledVersionsCard()
    {
        var card = new PclCard { Title = "已安装版本", Margin = new Thickness(0, 0, 0, 15) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 4 };
        var list = ModVersion.ListVersions();
        if (list.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "还没有安装任何版本。",
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3")
            });
        }
        foreach (var v in list)
        {
            var row = new PclListItem
            {
                Height = 38,
                Title = v.Id,
                Info = v.Type,
                LogoImage = PclIcons.Block(ModVersion.IconOf(v))
            };
            stack.Children.Add(row);
        }
        card.Body.Children.Add(stack);
        return card;
    }

    internal static Window MainWindowPublic => MainWin;

    internal static Task CheckUpdateClickPublic() => CheckUpdateClick();

    private static Window MainWin =>
        (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>系统设置里的「检查更新」按钮。</summary>
    private static async Task CheckUpdateClick()
    {
        var owner = MainWin;
        if (owner == null) return;
        ModStartup.HintPublic("正在检查更新……", false);
        var upd = await Task.Run(() => ModServer.CheckUpdate());
        if (upd == null)
        {
            await PclMsgBox.Show(owner, "检查更新",
                $"当前已是最新版本（{ModApp.VersionBaseName}，版本码 {ModApp.VersionCode}）。");
            return;
        }
        await UpdateFlow(upd.Value.Code, upd.Value.Url);
    }

    /// <summary>
    /// 更新流程（启动自动检查与手动检查共用）：弹更新详情 → 确认 → 下载并解压 →
    /// 由替换脚本在本进程退出后覆盖安装目录并重启。对应 Windows 版 UpdateDownloadAndRestart。
    /// </summary>
    internal static async Task UpdateFlow(int code, string url)
    {
        var owner = MainWin;
        if (owner == null) return;
        var detail = await Task.Run(() => ModServer.GetUpdateDetail());
        var text = $"发现 TYW Launcher 新版本（版本码 {code}）。\n" +
                   $"当前版本：{ModApp.VersionBaseName}（版本码 {ModApp.VersionCode}）\n\n" +
                   (string.IsNullOrWhiteSpace(detail) ? "" : detail + "\n\n") +
                   "更新将自动下载并替换文件，完成后自动重启。";
        var choice = await PclMsgBox.Show(owner, "发现新版本", text, "立即更新", "取消");
        if (choice != 0) return;

        var task = TaskCenter.Begin("更新 TYW Launcher");
        var ok = await Task.Run(() =>
        {
            try
            {
                return ModUpdate.ApplyUpdate(url, (p, s) => TaskCenter.Update(task, p, s));
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "自动更新");
                return false;
            }
        });
        if (!ok)
        {
            TaskCenter.Finish(task, "更新失败，可稍后在设置里重试", false);
            ModStartup.HintPublic("更新失败，详情见日志。", true);
            return;
        }
        TaskCenter.Finish(task, "更新完成，正在重启……");
        await Task.Delay(800);
        Environment.Exit(0);
    }

    private static PclCard SettingsCard(string name)
    {
        var card = new PclCard { Title = name, Margin = new Thickness(0, 0, 0, 15) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };

        switch (name)
        {
            case "启动":
                stack.Children.Add(ComboRow("Java 路径", JavaOptions(), JavaSelected(),
                    i => SaveJava(i)));
                stack.Children.Add(ComboRow("内存分配", RamOptions(), RamSelected(), i => SaveRam(i)));
                stack.Children.Add(Row("游戏目录", ModPaths.McFolder));
                stack.Children.Add(ComboRow("下载源", new List<string> { "BMCLAPI 镜像（推荐）", "Mojang 官方" },
                    ModSettings.GetBool("UseOfficialSource") ? 1 : 0, i =>
                    {
                        ModSettings.SetValue("UseOfficialSource", i == 1);
                        ModSettings.Save();
                        ApplyMirror();
                    }));
                break;

            case "联机":
                stack.Children.Add(Row("联机方式", "EasyTier（与 Windows 版同一套房间）"));
                stack.Children.Add(ComboRow("延迟优化", new List<string> { "标准模式", "延迟优先（更稳的联机体验）" },
                    ModSettings.GetInt("LinkLatencyMode", 1), i =>
                    {
                        ModSettings.SetValue("LinkLatencyMode", i.ToString());
                        ModSettings.Save();
                    }));
                stack.Children.Add(SettingsPage.Row("设备码", SettingsPage.CopyValue(ModSecret.DeviceCode)));
                break;

            case "个性化":
                stack.Children.Add(Row("界面主题", "浅色（与 Windows 版一致）"));
                stack.Children.Add(Row("语言", "简体中文"));
                break;

            default:
                stack.Children.Add(Row("TYW Launcher", "Linux 版 " + ModApp.VersionBaseName + "（版本码 " + ModApp.VersionCode + "）"));
                stack.Children.Add(Row("数据目录", ModPaths.Root));
                stack.Children.Add(Row("日志目录", ModPaths.LogDir));
                stack.Children.Add(SettingsPage.Row("设备码", SettingsPage.CopyValue(ModSecret.DeviceCode)));
                var checkBtn = new PclButton
                {
                    Text = "检查更新",
                    Height = 30,
                    Width = 110,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                    ColorType = PclButton.ColorState.Highlight
                };
                checkBtn.Click += async (_, _) => await CheckUpdateClick();
                stack.Children.Add(checkBtn);
                break;
        }

        card.Body.Children.Add(stack);
        return card;
    }

    private static List<string> JavaOptions()
    {
        var list = new List<string> { "自动选择（推荐）" };
        foreach (var j in ModJava.ListJava()) list.Add(j.Path + "    " + j.Display);
        return list;
    }

    private static int JavaSelected()
    {
        var saved = ModSettings.GetString("LaunchJavaExe");
        if (string.IsNullOrEmpty(saved)) return 0;
        var list = ModJava.ListJava();
        for (int i = 0; i < list.Count; i++)
            if (list[i].Path == saved) return i + 1;
        return 0;
    }

    private static void SaveJava(int index)
    {
        if (index <= 0)
        {
            ModSettings.SetValue("LaunchJavaExe", "");
        }
        else
        {
            var list = ModJava.ListJava();
            if (index - 1 < list.Count) ModSettings.SetValue("LaunchJavaExe", list[index - 1].Path);
        }
        ModSettings.Save();
    }

    private static readonly int[] RamValues = { 0, 1024, 2048, 4096, 6144, 8192, 12288, 16384 };

    private static List<string> RamOptions()
    {
        var list = new List<string> { "自动" };
        foreach (var mb in RamValues)
            if (mb > 0) list.Add(mb + " MB");
        return list;
    }

    private static int RamSelected()
    {
        var cur = ModSettings.GetInt("LaunchRam", 0);
        var i = Array.IndexOf(RamValues, cur);
        return i < 0 ? 0 : i;
    }

    private static void SaveRam(int index)
    {
        if (index < 0 || index >= RamValues.Length) return;
        ModSettings.SetValue("LaunchRam", RamValues[index].ToString());
        ModSettings.Save();
    }

    /// <summary>按设置切换下载源。</summary>
    public static void ApplyMirror()
    {
        ModDownload.Mirror = ModSettings.GetBool("UseOfficialSource")
            ? "https://launchermeta.mojang.com"
            : "https://bmclapi2.bangbang93.com";
    }

    /// <summary>左标题右下拉的设置行。</summary>
    private static Control ComboRow(string title, List<string> options, int selected, Action<int> changed)
    {
        var grid = new Grid { Height = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(210)));

        var t = new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var combo = new PclComboBox
        {
            IsEditable = false,
            Width = 210,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        combo.SetItems(options, Math.Clamp(selected, 0, Math.Max(0, options.Count - 1)));
        combo.SelectionChanged += (_, _) => changed(combo.SelectedIndex);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(t);
        grid.Children.Add(combo);
        return grid;
    }

    private static Control Row(string title, string value)
    {
        var grid = new Grid { Height = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var t = new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var v = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(v, 1);
        grid.Children.Add(t);
        grid.Children.Add(v);
        return grid;
    }
}
