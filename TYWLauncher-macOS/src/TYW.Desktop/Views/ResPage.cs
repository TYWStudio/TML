using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TYW.Desktop.Controls;

namespace TYW.Desktop.Views;

/// <summary>
/// 下载页的 Mod / 整合包 / 数据包 / 资源包 / 光影包五个分类。
/// 对应 Windows 版 PageResource + PageDownloadCompDetail：搜索筛选 → 结果卡片 → 详情版本列表 → 下载安装。
/// </summary>
public static class ResPage
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static ResType TypeOf(string name) => name switch
    {
        "Mod" => ResType.Mod,
        "整合包" => ResType.ModPack,
        "数据包" => ResType.DataPack,
        "资源包" => ResType.ResourcePack,
        "光影包" => ResType.Shader,
        _ => ResType.Mod
    };

    private static readonly string[] GameVersionFilter =
    {
        "", "1.21.4", "1.21.1", "1.20.6", "1.20.1", "1.19.2", "1.18.2",
        "1.17.1", "1.16.5", "1.12.2", "1.8.9", "1.7.10"
    };

    // ===================== 页面骨架 =====================

    public static Control Build(string name)
    {
        var type = TypeOf(name);
        var root = new StackPanel();

        // ---- 搜索区 ----
        var searchCard = new PclCard { Title = "搜索 " + name, Margin = new Thickness(0, 0, 0, 15) };
        var searchStack = new StackPanel { Margin = new Thickness(15, 8, 15, 13), Spacing = 8 };
        searchCard.Body.Children.Add(searchStack);

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var searchBox = new TextBox
        {
            Watermark = "输入名称搜索，回车确认",
            FontFamily = Pcl.FontFamily,
            FontSize = 12.5,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        DockPanel.SetDock(searchBox, Dock.Left);
        searchRow.Children.Add(searchBox);
        var btnSearch = new PclButton { Text = "搜索", ColorType = PclButton.ColorState.Highlight, Width = 70, Height = 30 };
        searchRow.Children.Add(btnSearch);
        searchStack.Children.Add(searchRow);

        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        filterRow.Children.Add(new TextBlock
        {
            Text = "游戏版本",
            FontSize = 12,
            FontFamily = Pcl.FontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Pcl.Res("ColorBrushGray2")
        });
        var comboVersion = new PclComboBox { Width = 130, Height = 27 };
        comboVersion.SetItems(GameVersionFilter.ToList(), 0);
        filterRow.Children.Add(comboVersion);
        if (type is ResType.Mod or ResType.ModPack)
        {
            filterRow.Children.Add(new TextBlock
            {
                Text = "加载器",
                FontSize = 12,
                FontFamily = Pcl.FontFamily,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Pcl.Res("ColorBrushGray2")
            });
            var comboLoader = new PclComboBox { Width = 110, Height = 27 };
            comboLoader.SetItems(new List<string> { "", "Forge", "NeoForge", "Fabric", "Quilt" }, 0);
            filterRow.Children.Add(comboLoader);
            root._loaderCombo(comboLoader);
        }
        searchStack.Children.Add(filterRow);
        root.Children.Add(searchCard);

        // ---- 结果区 ----
        var listCard = new PclCard { Title = "搜索结果", Margin = new Thickness(0, 0, 0, 15) };
        listCard.Body.Margin = new Thickness(12, 2, 12, 10);
        var resultList = new StackPanel();
        listCard.Body.Children.Add(resultList);
        root.Children.Add(listCard);

        var status = new TextBlock
        {
            Text = "输入关键词开始搜索，或直接浏览热门资源。",
            FontSize = 12,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            Margin = new Thickness(2, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(status);

        var storage = new ModResource.SearchResult();
        var busy = false;

        async Task RunSearch(bool fresh)
        {
            if (busy) return;
            busy = true;
            btnSearch.IsEnabledButton = false;
            try
            {
                if (fresh)
                {
                    storage = new ModResource.SearchResult();
                    resultList.Children.Clear();
                    status.Text = "正在搜索…";
                }
                else
                {
                    status.Text = "正在加载更多…";
                }
                var query = new ModResource.SearchQuery
                {
                    Type = type,
                    SearchText = searchBox.Text?.Trim() ?? "",
                    GameVersion = comboVersion.SelectedItem ?? "",
                    Loader = root._loaderCombo()?.SelectedItem ?? ""
                };
                var result = await Task.Run(() => ModResource.Search(query, storage));
                // 补充渲染新增结果
                status.Text = result.ErrorMessage != "" ? result.ErrorMessage :
                    $"共 {result.Projects.Count} 个结果";
                var existing = new HashSet<object>(resultList.Children.OfType<Control>()
                    .Where(c => c.Tag is ResProject).Select(c => ((Control)c).Tag));
                foreach (var p in result.Projects)
                {
                    if (existing.Contains(p)) continue;
                    resultList.Children.Add(ProjectItem(p, () => ShowDetail(root, type, p)));
                }
                if (CanContinue(storage)) resultList.Children.Add(MoreButton(async () =>
                {
                    resultList.RemoveMoreButton();
                    await RunSearch(false);
                }));
            }
            catch (Exception ex)
            {
                status.Text = "搜索失败：" + ex.Message;
                ModLog.Err(ex, "资源搜索");
            }
            finally
            {
                busy = false;
                btnSearch.IsEnabledButton = true;
            }
        }

        bool CanContinue(ModResource.SearchResult s) =>
            (s.CurseForgeTotal < 0 || s.CurseForgeOffset < s.CurseForgeTotal) ||
            (s.ModrinthTotal < 0 || s.ModrinthOffset < s.ModrinthTotal);

        btnSearch.Click += (_, _) => _ = RunSearch(true);
        searchBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) _ = RunSearch(true); };
        _ = RunSearch(true);

        return root;
    }

    // ======== 挂在 StackPanel 上的临时字段（避免再建包装类） ========
    private static readonly AttachedProperty<PclComboBox> LoaderComboProp =
        AvaloniaProperty.RegisterAttached<StackPanel, PclComboBox>("ResLoaderCombo", typeof(ResPage));
    private static PclComboBox _loaderCombo(this StackPanel p)
    {
        return p.GetValue(LoaderComboProp);
    }
    private static void _loaderCombo(this StackPanel p, PclComboBox v)
    {
        p.SetValue(LoaderComboProp, v);
    }

    // ===================== 列表卡片（对应 MyResourceItem） =====================

    private static Control ProjectItem(ResProject p, Action open)
    {
        var item = new PclListItem
        {
            Height = 60,
            Title = p.RawName,
            Info = (p.Description.Trim().Length > 0 ? p.Description.Trim() + "\n" : "") +
                   (p.ModLoaders.Count > 0 ? ModResource.JoinLoaders(p.ModLoaders) + "  ·  " : "") +
                   p.GameVersionDesc + "  ·  " + p.Platform + "  ·  " +
                   (p.DownloadCount > 100000000 ? Math.Round(p.DownloadCount / 100000000.0, 1) + " 亿"
                    : p.DownloadCount > 10000 ? p.DownloadCount / 10000 + " 万" : p.DownloadCount.ToString()) + " 下载  ·  " +
                   RelativeTime(p.LastUpdate),
            Tag = p
        };
        item.Click += (_, _) => open();
        if (!string.IsNullOrEmpty(p.LogoUrl)) _ = LoadInto((Image)item.LogoImage, p.LogoUrl);
        return item;
    }

    private static Control MoreButton(Func<Task> load)
    {
        var btn = new PclButton
        {
            Text = "加载更多",
            Height = 28,
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        btn.Click += (_, _) => _ = load();
        btn.Tag = "more";
        return btn;
    }

    // 给 MoreButton 加的移除扩展（藏在扩展方法里）
    private static void RemoveMoreButton(this StackPanel list)
    {
        var more = list.Children.OfType<Control>().Where(c => (c.Tag as string) == "more").ToList();
        foreach (var m in more) list.Children.Remove(m);
    }

    private static string RelativeTime(DateTime t)
    {
        var days = (DateTime.Now - t).TotalDays;
        if (days < 1) return "今天更新";
        if (days < 30) return (int)days + " 天前更新";
        if (days < 365) return (int)(days / 30) + " 个月前更新";
        return (int)(days / 365) + " 年前更新";
    }

    // ===================== 详情页（对应 PageDownloadCompDetail） =====================

    private static void ShowDetail(StackPanel root, ResType type, ResProject project)
    {
        PageAnim.SwapRight(root, () =>
        {
            root.Children.Clear();

            // 返回行
            var backRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
            var btnBack = new PclButton { Text = "返回列表", Width = 84, Height = 28 };
            backRow.Children.Add(btnBack);
            backRow.Children.Add(new TextBlock
            {
                Text = project.RawName,
                FontSize = 15,
                FontWeight = FontWeight.Medium,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush1"),
                VerticalAlignment = VerticalAlignment.Center
            });
            root.Children.Add(backRow);

            // 工程信息卡
            var infoCard = new PclCard { Margin = new Thickness(0, 0, 0, 15) };
            var infoGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), Margin = new Thickness(15, 12, 15, 12) };
            var icon = MakeIcon(project.LogoUrl, big: true);
            Grid.SetColumn(icon, 0);
            infoGrid.Children.Add(icon);
            var infoMid = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(infoMid, 1);
            infoMid.Children.Add(new TextBlock
            {
                Text = project.Description,
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush1"),
                TextWrapping = TextWrapping.Wrap
            });
            infoMid.Children.Add(new TextBlock
            {
                Text = (project.ModLoaders.Count > 0 ? ModResource.JoinLoaders(project.ModLoaders) + "  ·  " : "") +
                       project.GameVersionDesc + "  ·  来源 " + project.Platform + "  ·  " +
                       (project.DownloadCount > 100000000 ? Math.Round(project.DownloadCount / 100000000.0, 1) + " 亿"
                        : project.DownloadCount > 10000 ? project.DownloadCount / 10000 + " 万" : project.DownloadCount.ToString()) + " 下载",
                FontSize = 11.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"),
                Margin = new Thickness(0, 5, 0, 0)
            });
            infoGrid.Children.Add(infoMid);
            infoCard.Body.Children.Add(infoGrid);
            root.Children.Add(infoCard);

            // 安装进度卡（隐藏，安装时显示）
            var progressCard = new PclCard { Title = "安装", Margin = new Thickness(0, 0, 0, 15), IsVisible = false };
            var progressStack = new StackPanel { Margin = new Thickness(15, 8, 15, 13), Spacing = 8 };
            progressCard.Body.Children.Add(progressStack);
            var bar = new ProgressBar
            {
                Height = 4, Minimum = 0, Maximum = 1,
                Foreground = Pcl.Res("ColorBrush1"),
                Background = Pcl.Res("ColorBrushGray6"),
                CornerRadius = new CornerRadius(2)
            };
            progressStack.Children.Add(bar);
            var progressText = new TextBlock
            {
                FontSize = 12,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray2"),
                TextWrapping = TextWrapping.Wrap
            };
            progressStack.Children.Add(progressText);
            root.Children.Add(progressCard);

            // 版本列表卡
            var filesCard = new PclCard { Title = "选择版本", Margin = new Thickness(0, 0, 0, 15) };
            filesCard.Body.Margin = new Thickness(12, 2, 12, 10);
            var filesList = new StackPanel();
            filesCard.Body.Children.Add(filesList);
            root.Children.Add(filesCard);

            btnBack.Click += (_, _) => BackToList(root, type);

            void Report(double p, string s)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    progressCard.IsVisible = true;
                    bar.Value = p;
                    progressText.Text = s;
                });
            }

            void BeginInstall(ResFile file)
            {
                var canceled = false;
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (type == ResType.ModPack)
                        {
                            // 下载整合包到临时目录，再走完整安装（overrides + 依赖下载 + 版本 JSON）
                            var name = SanitizeName(project.RawName);
                            Report(0.01, "下载整合包文件…");
                            ModResource.DownloadResFile(file, ModPaths.TempDir, (p, msg) => Report(0.02 + 0.68 * p, msg), () => canceled, "");
                            var packPath = Path.Combine(ModPaths.TempDir, file.FileName);
                            Report(0.7, "正在安装整合包（解压 + 下载依赖，可能需要较长时间）…");
                            var inst = ModResource.InstallModPack(packPath, name,
                                (p2, msg) => Report(0.7 + 0.28 * p2, msg), () => canceled);
                            try { File.Delete(packPath); } catch { }
                            Report(1, "整合包已安装为实例「" + inst + "」，在启动页选择它即可进入。");
                            return;
                        }
                        string dir;
                        if (type == ResType.DataPack)
                        {
                            var world = root._worldCombo()?.SelectedItem;
                            if (string.IsNullOrEmpty(world)) throw new Exception("请先在上方选择要安装数据包的世界");
                            dir = ModResource.DataPackDir(world);
                        }
                        else
                        {
                            dir = ModResource.InstallDirFor(type);
                        }
                        ModResource.DownloadResFile(file, dir, Report, () => canceled);
                        // 自动安装前置
                        InstallDependencies(file, project, dir, Report, () => canceled);
                        Report(1, "「" + file.FileName + "」已安装完成。");
                    }
                    catch (OperationCanceledException)
                    {
                        Report(0, "已取消安装。");
                    }
                    catch (Exception ex)
                    {
                        ModLog.Err(ex, "资源安装");
                        Report(0, "安装失败：" + ex.Message);
                    }
                });
            }

            // 填充版本列表
            _ = Task.Run(() =>
            {
                try
                {
                    var files = ModResource.FilesOf(project.Id, project.Platform, project.Types);
                    ModResource.LoadDependencyNames(files, project.Platform);
                    Dispatcher.UIThread.Post(() =>
                    {
                        // 数据包需要选世界
                        if (type == ResType.DataPack)
                        {
                            var worldRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2, 0, 0, 8) };
                            worldRow.Children.Add(new TextBlock
                            {
                                Text = "安装到世界",
                                FontSize = 12,
                                FontFamily = Pcl.FontFamily,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = Pcl.Res("ColorBrushGray2")
                            });
                            var comboWorld = new PclComboBox { Width = 200, Height = 27, Placeholder = "选择世界" };
                            var worlds = ModResource.ListWorlds();
                            if (worlds.Count > 0) comboWorld.SetItems(worlds, 0);
                            root._worldCombo(comboWorld);
                            worldRow.Children.Add(comboWorld);
                            filesList.Children.Add(worldRow);
                        }
                        if (files.Count == 0)
                        {
                            filesList.Children.Add(new TextBlock
                            {
                                Text = "该工程没有可下载的文件。",
                                FontSize = 12,
                                FontFamily = Pcl.FontFamily,
                                Foreground = Pcl.Res("ColorBrushGray2"),
                                Margin = new Thickness(2, 4, 0, 8)
                            });
                            return;
                        }
                        foreach (var f in files.Take(100))
                        {
                            var file = f;
                            var item = new PclListItem
                            {
                                Height = 42,
                                Title = (f.Display != "" ? f.Display : f.FileName).BeforeLastExt(),
                                Info = f.InfoLine + (f.Dependencies.Count > 0 ? "，" + f.Dependencies.Count + " 项前置" : "")
                            };
                            item.LogoTint = f.ReleaseType switch
                            {
                                1 => new SolidColorBrush(Color.FromRgb(0x63, 0xA0, 0x4B)),
                                2 => new SolidColorBrush(Color.FromRgb(0xD8, 0xA8, 0x2A)),
                                _ => new SolidColorBrush(Color.FromRgb(0xB0, 0x7A, 0x3A))
                            };
                            item.Click += (_, _) => BeginInstall(file);
                            filesList.Children.Add(item);
                        }
                        if (files.Count > 100)
                        {
                            filesList.Children.Add(new TextBlock
                            {
                                Text = "仅显示最近更新的 100 个版本。",
                                FontSize = 11.5,
                                FontFamily = Pcl.FontFamily,
                                Foreground = Pcl.Res("ColorBrushGray4"),
                                Margin = new Thickness(2, 4, 0, 8)
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    ModLog.Err(ex, "获取版本列表");
                    Dispatcher.UIThread.Post(() =>
                    {
                        filesList.Children.Add(new TextBlock
                        {
                            Text = "获取版本列表失败：" + ex.Message,
                            FontSize = 12,
                            FontFamily = Pcl.FontFamily,
                            Foreground = Pcl.Res("ColorBrushGray2"),
                            Margin = new Thickness(2, 4, 0, 8),
                            TextWrapping = TextWrapping.Wrap
                        });
                    });
                }
            });
        });
    }

    private static PclComboBox _worldCombo(this StackPanel p)
    {
        return p.GetValue(WorldComboProp);
    }
    private static void _worldCombo(this StackPanel p, PclComboBox v)
    {
        p.SetValue(WorldComboProp, v);
    }
    private static readonly AttachedProperty<PclComboBox> WorldComboProp =
        AvaloniaProperty.RegisterAttached<StackPanel, PclComboBox>("ResWorldCombo", typeof(ResPage));

    /// <summary>自动安装前置（最新兼容版本）。</summary>
    private static void InstallDependencies(ResFile file, ResProject project, string dir,
        Action<double, string> report, Func<bool> cancel)
    {
        if (file.Dependencies.Count == 0) return;
        for (int i = 0; i < file.Dependencies.Count; i++)
        {
            var depId = file.Dependencies[i];
            try
            {
                var depProject = ModResource.ProjectById(depId, project.Platform);
                report((0.8 + 0.2 * i / file.Dependencies.Count) * 0.5, "下载前置 " + depProject.RawName + "…");
                var depFiles = ModResource.FilesOf(depId, project.Platform, depProject.Types);
                var target = depFiles.FirstOrDefault(f =>
                    (file.GameVersions.Count == 0 || f.GameVersions.Any(g => file.GameVersions.Contains(g))) &&
                    (file.ModLoaders.Count == 0 || f.ModLoaders.Any(l => file.ModLoaders.Contains(l))) &&
                    f.ReleaseType == 1) ?? depFiles.FirstOrDefault();
                if (target == null) continue;
                ModResource.DownloadResFile(target, dir, report, cancel, "前置 ");
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "下载前置 " + depId);
            }
        }
    }

    private static void BackToList(StackPanel root, ResType type)
    {
        // 重新构建列表页（复用 Build；搜索状态重置）
        PageAnim.SwapRight(root, () =>
        {
            root.Children.Clear();
            root.Children.Add(Build(_typeName(type)));
        });
    }

    private static string _typeName(ResType t) => t switch
    {
        ResType.Mod => "Mod",
        ResType.ModPack => "整合包",
        ResType.DataPack => "数据包",
        ResType.ResourcePack => "资源包",
        ResType.Shader => "光影包",
        _ => "Mod"
    };

    // ===================== 工具 =====================

    private static string BeforeLastExt(this string s)
    {
        var i = s.LastIndexOf('.');
        return i > 0 ? s.Substring(0, i) : s;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var s = sb.ToString().Trim();
        return s.Length == 0 ? "Modpack" : s;
    }

    private static Control MakeIcon(string url, bool big = false)
    {
        var size = big ? 56.0 : 40.0;
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Pcl.Res("ColorBrushBg0"),
            ClipToBounds = true
        };
        var img = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.UniformToFill
        };
        border.Child = img;
        if (!string.IsNullOrEmpty(url)) _ = LoadInto(img, url);
        return border;
    }

    private static async Task LoadInto(Image img, string url)
    {
        try
        {
            var bytes = await IconBytes(url);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(bytes);
                img.Source = new Bitmap(ms);
            });
        }
        catch { }
    }

    private static readonly Dictionary<string, byte[]> IconCache = new();
    private static readonly string IconDir = Path.Combine(ModPaths.CacheDir, "ResIcon");

    private static async Task<byte[]> IconBytes(string url)
    {
        lock (IconCache)
        {
            if (IconCache.TryGetValue(url, out var hit)) return hit;
        }
        Directory.CreateDirectory(IconDir);
        var file = Path.Combine(IconDir, Sha1Hex(url) + ".img");
        byte[] data;
        if (File.Exists(file))
        {
            data = await File.ReadAllBytesAsync(file);
        }
        else
        {
            try
            {
                data = await Http.GetByteArrayAsync(url);
            }
            catch
            {
                data = await Http.GetByteArrayAsync(ModResource.MirrorMod(url));
            }
            await File.WriteAllBytesAsync(file, data);
        }
        lock (IconCache)
        {
            IconCache[url] = data;
        }
        return data;
    }

    internal static string Sha1Hex(string s)
    {
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return BitConverter.ToString(hash).Replace("-", "").ToLower()[..16];
    }
}

