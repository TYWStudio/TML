using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using TYW.Desktop.Controls;
using TYW;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TYW.Desktop.Views;

/// <summary>
/// 更多页：帮助 / 关于与鸣谢 / 百宝箱。
/// 帮助内容由 Windows 版内置的 Help.zip 转换而来（Assets/Help/help.json），
/// 分类、标题、正文、图片、跳转都与 Windows 版一致。
/// </summary>
public static class OtherPage
{
    public static Control Build(string name)
    {
        switch (name)
        {
            case "帮助": return HelpPage();
            case "关于与鸣谢": return AboutPage();
            default: return TestPage();
        }
    }

    // ===================== 帮助 =====================

    private sealed class HelpEntry
    {
        public string Path = "";
        public string Title = "";
        public string Desc = "";
        public List<string> Types = new();
        public bool Public = true;
        public List<JsonElement> Blocks = new();
    }

    private static List<HelpEntry> _helpEntries;
    private static List<string> _helpCats;

    private static void LoadHelp()
    {
        if (_helpEntries != null) return;
        _helpEntries = new List<HelpEntry>();
        _helpCats = new List<string>();
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://TYWLauncher/Assets/Help/help.json"));
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.TryGetProperty("categories", out var cats))
                foreach (var c in cats.EnumerateArray())
                    _helpCats.Add(c.GetString() ?? "");

            foreach (var e in root.GetProperty("entries").EnumerateArray())
            {
                var item = new HelpEntry
                {
                    Path = e.GetProperty("path").GetString() ?? "",
                    Title = e.GetProperty("title").GetString() ?? "",
                    Desc = e.TryGetProperty("desc", out var d) ? d.GetString() ?? "" : "",
                    Public = !e.TryGetProperty("public", out var p) || p.GetBoolean()
                };
                if (e.TryGetProperty("types", out var types))
                    foreach (var t in types.EnumerateArray())
                        item.Types.Add(t.GetString() ?? "");
                if (e.TryGetProperty("blocks", out var blocks))
                    foreach (var b in blocks.EnumerateArray())
                        item.Blocks.Add(b.Clone());
                _helpEntries.Add(item);
            }

            // 分类顺序与 Windows 版一致：指南置顶
            if (_helpCats.Contains("指南"))
            {
                _helpCats.Remove("指南");
                _helpCats.Insert(0, "指南");
            }
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "加载帮助文档");
        }
    }

    private static Control HelpPage()
    {
        LoadHelp();
        var host = new StackPanel();
        ShowList(host);
        return host;
    }

    private static void ShowList(StackPanel host)
    {
        host.Children.Clear();
        var visible = _helpEntries.Where(e => e.Public).ToList();

        if (visible.Count == 0)
        {
            host.Children.Add(Card("帮助", new[] { "帮助文档加载失败，请查看日志。" }));
            return;
        }

        foreach (var cat in _helpCats)
        {
            var items = visible.Where(e => e.Types.Contains(cat)).ToList();
            if (items.Count == 0) continue;

            // 与 Windows 帮助页一致：每个分类卡片可以点标题折叠
            var card = new PclCard
            {
                Title = cat,
                Margin = new Thickness(0, 0, 0, 15),
                CanSwap = true,
                IsSwapped = false
            };
            var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 10), Spacing = 2 };
            foreach (var entry in items)
            {
                var item = new PclListItem
                {
                    Title = entry.Title,
                    Info = entry.Desc,
                    Height = 46,
                    Logo = Geometry.Parse(PageDefs.Other[0].Logo)
                };
                item.Click += (_, _) => ShowDetail(host, entry);
                stack.Children.Add(item);
            }
            card.Body.Children.Add(stack);
            host.Children.Add(card);
        }
    }

    private static void ShowDetail(StackPanel host, HelpEntry entry)
    {
        host.Children.Clear();

        var bar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        bar.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var back = new PclButton { Text = "← 返回", Height = 30, Width = 84 };
        back.Click += (_, _) => ShowList(host);
        bar.Children.Add(back);
        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);
        host.Children.Add(bar);

        // 正文：按卡片分组，卡片标题前的段落放到「简介」卡片里
        var currentCard = new PclCard { Title = entry.Title, Margin = new Thickness(0, 0, 0, 15) };
        var currentStack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 7 };
        currentCard.Body.Children.Add(currentStack);
        host.Children.Add(currentCard);

        foreach (var block in entry.Blocks)
        {
            var t = block.TryGetProperty("t", out var te) ? te.GetString() : "";
            switch (t)
            {
                case "card":
                {
                    var text = block.GetProperty("text").GetString() ?? "";
                    if (currentStack.Children.Count > 0 || currentCard.Title != entry.Title)
                    {
                        currentCard = new PclCard { Title = text, Margin = new Thickness(0, 0, 0, 15) };
                        currentStack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 7 };
                        currentCard.Body.Children.Add(currentStack);
                        host.Children.Add(currentCard);
                    }
                    else
                    {
                        currentCard.Title = text;
                    }
                    break;
                }
                case "p":
                {
                    currentStack.Children.Add(new TextBlock
                    {
                        Text = block.GetProperty("text").GetString() ?? "",
                        FontSize = 12.5,
                        FontFamily = Pcl.FontFamily,
                        Foreground = Pcl.Res("ColorBrush1"),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 19
                    });
                    break;
                }
                case "hint":
                {
                    var hint = new Border
                    {
                        Background = Pcl.Res("ColorBrushBg0"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 7, 10, 7),
                        Child = new TextBlock
                        {
                            Text = block.GetProperty("text").GetString() ?? "",
                            FontSize = 12,
                            FontFamily = Pcl.FontFamily,
                            Foreground = Pcl.Res("ColorBrush1"),
                            TextWrapping = TextWrapping.Wrap
                        }
                    };
                    currentStack.Children.Add(hint);
                    break;
                }
                case "btn":
                {
                    currentStack.Children.Add(new TextBlock
                    {
                        Text = "▸ " + (block.GetProperty("text").GetString() ?? ""),
                        FontSize = 12.5,
                        FontFamily = Pcl.FontFamily,
                        Foreground = Pcl.Res("ColorBrushGray2"),
                        TextWrapping = TextWrapping.Wrap
                    });
                    break;
                }
                case "img":
                {
                    var src = block.GetProperty("src").GetString() ?? "";
                    var img = new Image
                    {
                        MaxWidth = 420,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 3, 0, 3)
                    };
                    currentStack.Children.Add(img);
                    _ = LoadImage(img, src);
                    break;
                }
                case "link":
                {
                    var to = block.TryGetProperty("to", out var toEl) ? toEl.GetString() : "";
                    var target = _helpEntries.FirstOrDefault(x => x.Path == to);
                    if (target == null) break;
                    var text = block.TryGetProperty("text", out var tEl) ? tEl.GetString() : "";
                    var btn = new PclButton
                    {
                        Text = (string.IsNullOrWhiteSpace(text) ? target.Title : text) + " →",
                        Height = 30,
                        MinWidth = 120,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    btn.Click += (_, _) => ShowDetail(host, target);
                    currentStack.Children.Add(btn);
                    break;
                }
            }
        }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static async Task LoadImage(Image img, string url)
    {
        try
        {
            var dir = Path.Combine(ModPaths.CacheDir, "HelpImg");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, ResPage.Sha1Hex(url) + ".img");
            if (!File.Exists(file))
            {
                var bytes = await Http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(file, bytes);
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try { img.Source = new Bitmap(file); } catch { }
            });
        }
        catch
        {
            // 帮助图片只是辅助，加载失败就留空
        }
    }

    // ===================== 关于与鸣谢 =====================

    private static Control AboutPage()
    {
        var host = new StackPanel();

        var about = new PclCard { Title = "关于", Margin = new Thickness(0, 0, 0, 15) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 2 };
        stack.Children.Add(PersonItem("TYW", "TYW Launcher 的开发者！"));
        stack.Children.Add(PersonItem("龙腾猫跃", "Plain Craft Launcher 原作者，感谢他写出的优秀启动器！"));
        stack.Children.Add(PersonItem("HerobrineXia", "Plain Craft Launcher 的协力开发者！"));
        stack.Children.Add(PersonItem("TYW Launcher",
            "当前版本：" + ModApp.VersionBaseName + "（版本码 " + ModApp.VersionCode + "，" + ModPlatform.DisplayName + "）"));
        var update = new PclButton
        {
            Text = "检查更新",
            Height = 30,
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            ColorType = PclButton.ColorState.Highlight
        };
        update.Click += async (_, _) => await StaticPage.CheckUpdateClickPublic();
        stack.Children.Add(update);
        about.Body.Children.Add(stack);
        host.Children.Add(about);

        var thanks = new PclCard { Title = "鸣谢", Margin = new Thickness(0, 0, 0, 15) };
        thanks.Body.Children.Add(new StackPanel
        {
            Margin = new Thickness(15, 6, 15, 13),
            Spacing = 7,
            Children =
            {
                Para("感谢 Plain Craft Launcher（PCL2）的作者龙腾猫跃：TYW Launcher 的界面、交互与大量基础功能都源自 PCL。"),
                Para("感谢 BangBang93 提供的 BMCLAPI 镜像源，让国内下载 Minecraft 成为可能。"),
                Para("感谢 EasyTier 提供的组网能力，联机功能基于它实现。"),
                Para("感谢 Modrinth 与 CurseForge 提供的模组检索接口。"),
                Para("感谢所有参与测试与反馈问题的玩家。")
            }
        });
        host.Children.Add(thanks);

        var links = new PclCard { Title = "相关链接", Margin = new Thickness(0, 0, 0, 15) };
        var linkStack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };
        linkStack.Children.Add(LinkButton("打开百科", "https://www.mcmod.cn"));
        linkStack.Children.Add(LinkButton("MCIM 主页", "https://github.com/mcmod-info-mirror"));
        linkStack.Children.Add(LinkButton("EasyTier GitHub", "https://github.com/EasyTier/EasyTier"));
        linkStack.Children.Add(LinkButton("打开镜像站", "https://mirrors.cqu.edu.cn/"));
        links.Body.Children.Add(linkStack);
        host.Children.Add(links);

        return host;
    }

    private static Control PersonItem(string name, string info)
    {
        var grid = new Grid { Height = 44, Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        stack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1")
        });
        stack.Children.Add(new TextBlock
        {
            Text = info,
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(stack);
        return grid;
    }

    private static TextBlock Para(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        FontFamily = Pcl.FontFamily,
        Foreground = Pcl.Res("ColorBrush1"),
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19
    };

    private static Control LinkButton(string text, string url)
    {
        var btn = new PclButton
        {
            Text = text,
            Height = 30,
            MinWidth = 130,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        btn.Click += (_, _) => OpenUrl(url);
        return btn;
    }

    // ===================== 百宝箱 =====================

    private static Control TestPage()
    {
        var host = new StackPanel();

        // 工具
        var tools = new PclCard { Title = "工具", Margin = new Thickness(0, 0, 0, 15) };
        var toolStack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var btnMem = new PclButton { Text = "内存优化", Height = 32, MinWidth = 130 };
        btnMem.Click += (_, _) =>
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            ModStartup.HintPublic("已释放启动器自身占用的内存。", false);
        };
        row1.Children.Add(btnMem);
        var btnClean = new PclButton { Text = "垃圾清理", Height = 32, MinWidth = 130 };
        btnClean.Click += async (_, _) => await CleanRubbish();
        row1.Children.Add(btnClean);
        toolStack.Children.Add(row1);
        toolStack.Children.Add(new TextBlock
        {
            Text = "内存优化会触发一次垃圾回收，释放启动器占用的内存；垃圾清理会删除缓存与临时文件，相关文件下次使用时会自动重新下载。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap
        });
        tools.Body.Children.Add(toolStack);
        host.Children.Add(tools);

        // 娱乐
        var fun = new PclCard { Title = "娱乐", Margin = new Thickness(0, 0, 0, 15) };
        var funStack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var btnJrrp = new PclButton { Text = "今日人品", Height = 32, MinWidth = 120 };
        btnJrrp.Click += async (_, _) =>
        {
            var v = JrrpValue();
            var owner = StaticPage.MainWindowPublic;
            if (owner == null) return;
            await PclMsgBox.Show(owner, "今日人品",
                "你今日的人品值为：\n" + v + " 点\n\n" + JrrpComment(v));
        };
        row2.Children.Add(btnJrrp);
        var btnHint = new PclButton { Text = "随机提示", Height = 32, MinWidth = 120 };
        btnHint.Click += async (_, _) =>
        {
            var owner = StaticPage.MainWindowPublic;
            if (owner == null) return;
            await PclMsgBox.Show(owner, "随机提示", RandomOf(Hints));
        };
        row2.Children.Add(btnHint);
        var btnCave = new PclButton { Text = "随机格言", Height = 32, MinWidth = 120 };
        btnCave.Click += async (_, _) =>
        {
            var owner = StaticPage.MainWindowPublic;
            if (owner == null) return;
            await PclMsgBox.Show(owner, "随机格言", RandomOf(Caves));
        };
        row2.Children.Add(btnCave);
        funStack.Children.Add(row2);
        fun.Body.Children.Add(funStack);
        host.Children.Add(fun);

        // 文件下载
        var dl = new PclCard { Title = "文件下载", Margin = new Thickness(0, 0, 0, 15) };
        var dlStack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };
        var urlBox = new PclComboBox
        {
            IsEditable = true,
            Placeholder = "https://……",
            Height = 32
        };
        var nameBox = new PclComboBox
        {
            IsEditable = true,
            Placeholder = "留空则根据链接自动判断",
            Height = 32
        };
        dlStack.Children.Add(LabeledRow("下载地址", urlBox));
        dlStack.Children.Add(LabeledRow("保存文件名", nameBox));

        var btnDl = new PclButton
        {
            Text = "开始下载",
            Height = 32,
            MinWidth = 130,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            ColorType = PclButton.ColorState.Highlight
        };
        var progress = new TextBlock
        {
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap
        };
        btnDl.Click += async (_, _) =>
        {
            var url = (urlBox.Text ?? "").Trim();
            if (url.Length == 0)
            {
                ModStartup.HintPublic("请先输入下载地址。", true);
                return;
            }
            var name = (nameBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                name = url.Split('?')[0].Split('/').LastOrDefault() ?? "download";
                if (name.Length == 0) name = "download";
            }
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, name);
            progress.IsVisible = true;
            progress.Text = "正在下载到 " + target + " ……";
            var ok = await Task.Run(() => ModNet.DownloadFile(url, target,
                p => Dispatcher.UIThread.Post(() => progress.Text = "正在下载…… " + (int)(p * 100) + "%")));
            progress.Text = ok ? "已保存到 " + target : "下载失败，请检查链接后重试。";
            if (!ok) progress.Foreground = Pcl.Res("ColorBrushRedDark") as IBrush ?? progress.Foreground;
        };
        dlStack.Children.Add(btnDl);
        dlStack.Children.Add(progress);
        dl.Body.Children.Add(dlStack);
        host.Children.Add(dl);

        return host;
    }

    private static async Task CleanRubbish()
    {
        var owner = StaticPage.MainWindowPublic;
        if (owner == null) return;
        var choice = await PclMsgBox.Show(owner, "垃圾清理",
            "即将清理 TYW Launcher 的缓存与临时文件（Cache / Temp），清理后相关文件会自动重新下载。\n确定继续吗？",
            "确定", "取消");
        if (choice != 0) return;
        await Task.Run(() =>
        {
            try { Directory.Delete(ModPaths.CacheDir, true); } catch { }
            try { Directory.Delete(ModPaths.TempDir, true); } catch { }
            ModPaths.EnsureAll();
        });
        ModStartup.HintPublic("缓存已清理。", false);
    }

    private static Control LabeledRow(string label, Control right)
    {
        var grid = new Grid { MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(110)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var t = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            VerticalAlignment = VerticalAlignment.Center
        };
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        grid.Children.Add(t);
        grid.Children.Add(right);
        return grid;
    }

    // ===================== 公共 =====================

    private static Control Card(string title, IEnumerable<string> lines)
    {
        var card = new PclCard { Title = title, Margin = new Thickness(0, 0, 0, 15) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 6 };
        foreach (var line in lines) stack.Children.Add(Para(line));
        card.Body.Children.Add(stack);
        return card;
    }

    private static readonly List<string> Hints = new()
    {
        "按下 Ctrl + Tab 可以快速切换页面。",
        "在主页右键可以隐藏卡片。",
        "多版本共存时，记得为每个版本单独设置 Java。",
        "下载慢可以试试切换下载源（设置 → 启动 → 下载）。",
        "游戏崩溃了？查看日志目录里的最新日志能直接看到原因。",
        "版本隔离可以避免不同版本的 Mod 冲突（设置 → 启动 → 默认版本隔离）。"
    };

    private static readonly List<string> Caves = new()
    {
        "与其最终离去，不如从未归来。",
        "在方寸屏幕之间，亦有山川湖海。",
        "每一个像素，都是世界的一块拼图。",
        "红石电路的尽头，是逻辑的浪漫。",
        "世界很大，区块未加载的地方更远。"
    };

    private static string RandomOf(List<string> list) => list[Random.Shared.Next(list.Count)];

    /// <summary>今日人品：按日期哈希，同一天固定（与 Windows 版算法一致）。</summary>
    private static int JrrpValue()
    {
        var seed = System.Text.Encoding.UTF8.GetBytes("TYW-Jrrp-" + DateTime.Now.ToString("yyyyMMdd"));
        var hash = System.Security.Cryptography.MD5.HashData(seed);
        return (int)(BitConverter.ToUInt32(hash, 0) % 101);
    }

    private static string JrrpComment(int value) => value switch
    {
        100 => "满人品！今天干什么都顺，记得把好运分一点给别人。",
        >= 90 => "欧气满满，钻石矿脉正在向你招手。",
        >= 80 => "运气极佳，适合挑战一直不敢打的地牢。",
        >= 70 => "手气挺好，赶紧去挖矿或者开箱吧。",
        >= 60 => "运气不错，适合出门探索新地形。",
        >= 50 => "普普通通的一天，稳扎稳打就好。",
        >= 40 => "差一点点就过半了，别灰心。",
        >= 30 => "今天有点平淡，做点简单的事吧。",
        >= 20 => "不太顺，先整理整理箱子、修修房子。",
        >= 10 => "运气有点背，下矿记得带水桶和床。",
        _ => "今天连方块都不想放，早点休息吧。"
    };

    private static void OpenUrl(string url)
    {
        SysShell.OpenUrl(url);
    }
}
