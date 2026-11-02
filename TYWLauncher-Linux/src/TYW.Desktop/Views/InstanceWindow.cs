using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TYW.Desktop.Controls;
using TYW;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace TYW.Desktop.Views;

/// <summary>
/// 版本选择 / 版本设置窗口。对应 Windows 版 Pages/PageSelect 与 Pages/PageInstance：
/// 左栏是已安装版本，右栏是 概览 / 启动设置 / Mod 管理 / 存档 / 操作（重命名、删除、导出）。
/// 从启动页的「版本选择」「版本设置」进入，asSettings = True 时标题为「版本设置」并直接定位到当前版本。
/// </summary>
public class InstanceWindow : Window
{
    private readonly StackPanel _list = new() { Spacing = 2, Margin = new Thickness(6, 8, 6, 8) };
    private readonly ScrollViewer _right;
    private readonly StackPanel _rightHost = new() { Margin = new Thickness(20, 18, 20, 16) };
    private readonly List<(PclListItem Item, McVersionItem Ver)> _items = new();
    private McVersionItem _current;
    private readonly Action _onPicked;

    public InstanceWindow(Action onPicked = null, bool asSettings = false)
    {
        _onPicked = onPicked;
        Title = asSettings ? "版本设置" : "版本选择与实例管理";
        Width = 900;
        Height = 600;
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Pcl.Res("ColorBrushWhite") as IBrush;
        CanResize = true;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(196)));
        root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var leftPanel = new Border
        {
            Background = Pcl.Res("ColorBrushBg1") as IBrush,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _list
            }
        };
        Grid.SetColumn(leftPanel, 0);
        root.Children.Add(leftPanel);

        _right = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _rightHost
        };
        Grid.SetColumn(_right, 1);
        root.Children.Add(_right);

        Content = root;
        LoadVersions();
    }

    private void LoadVersions()
    {
        _list.Children.Clear();
        _items.Clear();

        var versions = ModVersion.ListVersions(true);
        if (versions.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "还没有安装任何版本。",
                FontSize = 12,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3"),
                Margin = new Thickness(10, 6, 10, 6),
                TextWrapping = TextWrapping.Wrap
            });
            _rightHost.Children.Clear();
            _rightHost.Children.Add(InfoCard("暂无版本", "请先到「下载 → 原版游戏」安装一个版本。"));
            return;
        }

        foreach (var v in versions)
        {
            var item = new PclListItem
            {
                Title = v.Id,
                Height = 40,
                LogoImage = PclIcons.Block(ModVersion.IconOf(v))
            };
            item.Click += (_, _) => Select(v);
            _items.Add((item, v));
            _list.Children.Add(item);
        }

        var last = ModSettings.GetString("VersionSelect");
        Select(versions.FirstOrDefault(v => v.Id == last) ?? versions[0]);
    }

    private void Select(McVersionItem v)
    {
        _current = v;
        foreach (var (item, ver) in _items) item.Checked = ver.Id == v.Id;
        BuildRight();
    }

    private void BuildRight()
    {
        _rightHost.Children.Clear();
        var v = _current;
        if (v == null) return;

        // 顶部：版本名 + 选用 / 打开目录
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var titleStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = v.Id,
            FontSize = 19,
            FontWeight = FontWeight.Bold,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1")
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = TypeName(v.Type) + " · " + v.Folder,
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3")
        });
        header.Children.Add(titleStack);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        var btnPick = new PclButton
        {
            Text = "选用此版本",
            Height = 30,
            MinWidth = 100,
            ColorType = PclButton.ColorState.Highlight
        };
        btnPick.Click += (_, _) =>
        {
            ModSettings.SetValue("VersionSelect", v.Id);
            ModSettings.Save();
            _onPicked?.Invoke();
            Close();
        };
        btnRow.Children.Add(btnPick);

        var btnFolder = new PclButton { Text = "打开目录", Height = 30, MinWidth = 92 };
        btnFolder.Click += (_, _) => OpenPath(v.Folder);
        btnRow.Children.Add(btnFolder);
        Grid.SetColumn(btnRow, 1);
        header.Children.Add(btnRow);
        _rightHost.Children.Add(header);

        // 概览
        var overview = NewCard("概览");
        overview.Stack.Children.Add(Kv("版本类型", TypeName(v.Type)));
        overview.Stack.Children.Add(Kv("游戏目录", ModVersion.GameDirOf(v)));
        overview.Stack.Children.Add(Kv("版本大小", HumanSize(DirSize(v.Folder))));
        overview.Stack.Children.Add(Kv("Mod 数量", CountFiles(ModVersion.ModsDirOf(v), "*.jar") + " 个"));
        overview.Stack.Children.Add(Kv("存档数量", CountDirs(ModVersion.SavesDirOf(v)) + " 个"));
        overview.Stack.Children.Add(Kv("启动 Java", JavaLabel()));
        _rightHost.Children.Add(overview.Card);

        // 启动设置（对应 Windows 版本设置 → 设置；Linux 版的启动参数是全局的，与「设置 → 启动」共用同一份配置）
        _rightHost.Children.Add(BuildLaunchSettingsCard());

        // Mod 管理
        var mods = NewCard("Mod 管理");
        mods.Stack.Children.Add(ModList(v));
        _rightHost.Children.Add(mods.Card);

        // 存档
        var saves = NewCard("存档");
        saves.Stack.Children.Add(SaveList(v));
        _rightHost.Children.Add(saves.Card);

        // 操作
        var ops = NewCard("操作");
        var opRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var btnRename = new PclButton { Text = "重命名", Height = 30, MinWidth = 96 };
        btnRename.Click += async (_, _) => await Rename(v);
        opRow.Children.Add(btnRename);

        var btnExport = new PclButton { Text = "导出实例", Height = 30, MinWidth = 96 };
        btnExport.Click += async (_, _) => await Export(v);
        opRow.Children.Add(btnExport);

        var btnDel = new PclButton { Text = "删除实例", Height = 30, MinWidth = 96, ColorType = PclButton.ColorState.Red };
        btnDel.Click += async (_, _) => await DeleteInstance(v);
        opRow.Children.Add(btnDel);
        ops.Stack.Children.Add(opRow);
        ops.Stack.Children.Add(new TextBlock
        {
            Text = "重命名会同时改版本目录与 JSON 内的 id；删除会移除整个版本目录（含实例内的 Mod 与存档）。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        _rightHost.Children.Add(ops.Card);
    }

    // ================= 启动设置 =================

    /// <summary>版本设置里的「启动设置」卡片：Java、内存、窗口大小、JVM 参数。</summary>
    private Control BuildLaunchSettingsCard()
    {
        var (card, stack) = NewCard("启动设置");

        stack.Children.Add(SettingsPage.ComboRow("Java 路径", SettingsPage.JavaOptions(), SettingsPage.JavaSelected(), i =>
        {
            if (i <= 0)
            {
                ModSettings.SetValue("LaunchJavaExe", "");
            }
            else
            {
                var javaList = ModJava.ListJava();
                if (i - 1 < javaList.Count) ModSettings.SetValue("LaunchJavaExe", javaList[i - 1].Path);
            }
            ModSettings.Save();
        }));

        // 内存：自动 / 1~16 GB，只列出不超过物理内存的档位
        var totalMb = SettingsPage.SystemMemoryMb();
        var capMb = Math.Max(2048, totalMb - 1024);
        var steps = new[] { 1024, 2048, 3072, 4096, 6144, 8192, 12288, 16384 };
        var ramOptions = new List<string> { "自动配置（推荐）" };
        var ramValues = new List<int> { 0 };
        foreach (var mb in steps)
        {
            if (mb > capMb) continue;
            ramOptions.Add(mb / 1024 + " GB");
            ramValues.Add(mb);
        }
        var curRam = ModSettings.GetInt("LaunchRam", 0);
        var ramSel = ramValues.IndexOf(curRam);
        if (ramSel < 0) ramSel = 0;
        stack.Children.Add(SettingsPage.ComboRow("游戏内存", ramOptions, ramSel, i =>
        {
            var mb = i >= 0 && i < ramValues.Count ? ramValues[i] : 0;
            ModSettings.SetValue("LaunchRam", mb.ToString());
            ModSettings.Save();
        }));

        // 窗口大小：全屏 / 窗口化 + 宽高
        var sizePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var comboType = new PclComboBox { IsEditable = false, Width = 92 };
        comboType.SetItems(new[] { "窗口化", "全屏" }, ModSettings.GetBool("LaunchFullscreen") ? 1 : 0);
        var txtWidth = SettingsPage.NumberBox(ModSettings.GetInt("LaunchWindowWidth", 854), 72);
        var txtHeight = SettingsPage.NumberBox(ModSettings.GetInt("LaunchWindowHeight", 480), 72);
        var labX = new TextBlock
        {
            Text = "×",
            FontSize = 13,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            VerticalAlignment = VerticalAlignment.Center
        };

        void SyncEnabled()
        {
            var fs = comboType.SelectedIndex == 1;
            txtWidth.IsEnabled = !fs;
            txtHeight.IsEnabled = !fs;
            labX.Opacity = fs ? 0.4 : 1;
        }
        SyncEnabled();

        comboType.SelectionChanged += (_, _) =>
        {
            ModSettings.SetValue("LaunchFullscreen", comboType.SelectedIndex == 1);
            ModSettings.Save();
            SyncEnabled();
        };
        txtWidth.TextChanged += (_, _) => SettingsPage.SaveInt(txtWidth.Text, "LaunchWindowWidth", 854);
        txtHeight.TextChanged += (_, _) => SettingsPage.SaveInt(txtHeight.Text, "LaunchWindowHeight", 480);

        sizePanel.Children.Add(comboType);
        sizePanel.Children.Add(txtWidth);
        sizePanel.Children.Add(labX);
        sizePanel.Children.Add(txtHeight);
        stack.Children.Add(SettingsPage.Row("窗口大小", sizePanel));

        var jvm = new PclComboBox
        {
            IsEditable = true,
            Width = 300,
            Placeholder = "留空则使用默认参数",
            Text = ModSettings.GetString("LaunchJvmExtra", "")
        };
        jvm.TextChanged += (_, _) =>
        {
            ModSettings.SetValue("LaunchJvmExtra", jvm.Text ?? "");
            ModSettings.Save();
        };
        stack.Children.Add(SettingsPage.Row("JVM 参数", jvm));

        stack.Children.Add(new TextBlock
        {
            Text = "这些是全局启动参数，和「设置 → 启动」里的同一份配置，修改后下一次启动生效。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        return card;
    }

    // ================= Mod / 存档 =================

    private Control ModList(McVersionItem v)
    {
        var dir = ModVersion.ModsDirOf(v);
        var stack = new StackPanel { Spacing = 2 };
        Directory.CreateDirectory(dir);

        var files = SafeFiles(dir, "*.jar").Concat(SafeFiles(dir, "*.jar.disabled"))
            .OrderBy(f => Path.GetFileName(f)).ToList();

        if (files.Count == 0)
        {
            stack.Children.Add(HintText("还没有 Mod。把 .jar 放进 mods 目录即可。"));
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var disabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var row = new Grid { Height = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = disabled ? name[..^".disabled".Length] : name,
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = disabled ? Pcl.Res("ColorBrushGray3") : Pcl.Res("ColorBrush1"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = (disabled ? "已禁用 · " : "") + HumanSize(new FileInfo(file).Length),
                FontSize = 11,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3")
            });
            row.Children.Add(info);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            var btnToggle = new PclButton { Text = disabled ? "启用" : "禁用", Height = 28, MinWidth = 62 };
            btnToggle.Click += (_, _) =>
            {
                try
                {
                    var target = disabled ? file[..^".disabled".Length] : file + ".disabled";
                    File.Move(file, target, true);
                    BuildRight();
                }
                catch (Exception ex) { ModLog.Err(ex, "切换 Mod 状态"); }
            };
            buttons.Children.Add(btnToggle);
            var btnDel = new PclButton { Text = "删除", Height = 28, MinWidth = 62, ColorType = PclButton.ColorState.Red };
            btnDel.Click += async (_, _) =>
            {
                var owner = this;
                var choice = await PclMsgBox.Show(owner, "删除 Mod", "确定删除「" + name + "」吗？此操作不可撤销。", "删除", "取消");
                if (choice != 0) return;
                try { File.Delete(file); } catch (Exception ex) { ModLog.Err(ex, "删除 Mod"); }
                BuildRight();
            };
            buttons.Children.Add(btnDel);

            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            stack.Children.Add(row);
        }

        stack.Children.Add(new TextBlock
        {
            Text = "目录：" + dir,
            FontSize = 11,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray4"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return stack;
    }

    private Control SaveList(McVersionItem v)
    {
        var dir = ModVersion.SavesDirOf(v);
        var stack = new StackPanel { Spacing = 2 };
        Directory.CreateDirectory(dir);

        var saves = SafeDirs(dir).OrderBy(d => Path.GetFileName(d)).ToList();
        if (saves.Count == 0) stack.Children.Add(HintText("还没有存档。"));

        foreach (var save in saves)
        {
            var name = Path.GetFileName(save);
            var row = new Grid { Height = 38 };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrush1")
            });
            info.Children.Add(new TextBlock
            {
                Text = HumanSize(DirSize(save)) + " · " + Directory.GetLastWriteTime(save).ToString("yyyy-MM-dd HH:mm"),
                FontSize = 11,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3")
            });
            row.Children.Add(info);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            var btnOpen = new PclButton { Text = "打开", Height = 28, MinWidth = 62 };
            btnOpen.Click += (_, _) => OpenPath(save);
            buttons.Children.Add(btnOpen);
            var btnDel = new PclButton { Text = "删除", Height = 28, MinWidth = 62, ColorType = PclButton.ColorState.Red };
            btnDel.Click += async (_, _) =>
            {
                var choice = await PclMsgBox.Show(this, "删除存档",
                    "确定删除存档「" + name + "」吗？此操作不可撤销。", "删除", "取消");
                if (choice != 0) return;
                try { Directory.Delete(save, true); } catch (Exception ex) { ModLog.Err(ex, "删除存档"); }
                BuildRight();
            };
            buttons.Children.Add(btnDel);

            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            stack.Children.Add(row);
        }

        stack.Children.Add(new TextBlock
        {
            Text = "目录：" + dir,
            FontSize = 11,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray4"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return stack;
    }

    // ================= 操作 =================

    private async Task Rename(McVersionItem v)
    {
        var name = await PclMsgBox.ShowInput(this, "重命名实例",
            "输入新的版本名（只能包含字母、数字、下划线、短横线与点）：", v.Id);
        if (string.IsNullOrWhiteSpace(name) || name == v.Id) return;
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            await PclMsgBox.Show(this, "重命名实例", "版本名包含非法字符。");
            return;
        }
        var target = Path.Combine(ModPaths.McFolder, "versions", name);
        if (Directory.Exists(target))
        {
            await PclMsgBox.Show(this, "重命名实例", "已经存在同名版本。");
            return;
        }
        try
        {
            var src = v.Folder;
            Directory.Move(src, target);
            // 改 JSON 文件名与 id
            var oldJson = Path.Combine(target, v.Id + ".json");
            var newJson = Path.Combine(target, name + ".json");
            if (File.Exists(oldJson))
            {
                var text = File.ReadAllText(oldJson);
                text = text.Replace("\"id\":\"" + v.Id + "\"", "\"id\":\"" + name + "\"")
                           .Replace("\"id\": \"" + v.Id + "\"", "\"id\": \"" + name + "\"");
                File.WriteAllText(newJson, text);
                if (!string.Equals(oldJson, newJson, StringComparison.Ordinal)) File.Delete(oldJson);
            }
            if (ModSettings.GetString("VersionSelect") == v.Id)
            {
                ModSettings.SetValue("VersionSelect", name);
                ModSettings.Save();
            }
            ModVersion.Invalidate();
            LoadVersions();
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "重命名实例");
            await PclMsgBox.Show(this, "重命名实例", "重命名失败：" + ex.Message);
        }
    }

    private async Task Export(McVersionItem v)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, v.Id + ".zip");
        var choice = await PclMsgBox.Show(this, "导出实例",
            "将把整个版本目录打包为：\n" + zip + "\n\n继续吗？", "开始导出", "取消");
        if (choice != 0) return;

        ModStartup.HintPublic("正在打包实例……", false);
        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(v.Folder, zip, CompressionLevel.Optimal, false);
            });
            ModStartup.HintPublic("已导出到 " + zip, false);
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "导出实例");
            await PclMsgBox.Show(this, "导出实例", "导出失败：" + ex.Message);
        }
    }

    private async Task DeleteInstance(McVersionItem v)
    {
        var choice = await PclMsgBox.Show(this, "删除实例",
            "确定删除实例「" + v.Id + "」吗？\n\n" +
            "会删除整个版本目录，其中的 Mod 与存档也会一并删除，此操作不可撤销。",
            "删除", "取消");
        if (choice != 0) return;
        try
        {
            Directory.Delete(v.Folder, true);
            if (ModSettings.GetString("VersionSelect") == v.Id) ModSettings.SetValue("VersionSelect", "");
            ModSettings.Save();
            ModVersion.Invalidate();
            LoadVersions();
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "删除实例");
            await PclMsgBox.Show(this, "删除实例", "删除失败：" + ex.Message);
        }
    }

    // ================= 辅助 =================

    private static (PclCard Card, StackPanel Stack) NewCard(string title)
    {
        var card = new PclCard { Title = title, Margin = new Thickness(0, 0, 0, 14) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 12), Spacing = 2 };
        card.Body.Children.Add(stack);
        return (card, stack);
    }

    private static Control InfoCard(string title, string text)
    {
        var (card, stack) = NewCard(title);
        stack.Children.Add(HintText(text));
        return card;
    }

    private static Control Kv(string key, string value)
    {
        var grid = new Grid { MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(110)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.Children.Add(new TextBlock
        {
            Text = key,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var v = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    private static TextBlock HintText(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontFamily = Pcl.FontFamily,
        Foreground = Pcl.Res("ColorBrushGray3"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };

    private static string TypeName(string type) => type?.ToLowerInvariant() switch
    {
        "release" => "正式版",
        "snapshot" => "快照版",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => string.IsNullOrEmpty(type) ? "未知" : type
    };

    private static string JavaLabel()
    {
        var saved = ModSettings.GetString("LaunchJavaExe");
        if (!string.IsNullOrEmpty(saved)) return saved;
        var list = ModJava.ListJava();
        return list.Count > 0 ? "自动（" + list[0].Display + "）" : "自动（未检测到 Java）";
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static int CountFiles(string dir, string pattern) => SafeFiles(dir, pattern).Count();
    private static int CountDirs(string dir) => SafeDirs(dir).Count();

    private static long DirSize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch { return 0; }
    }

    private static string HumanSize(long bytes)
    {
        if (bytes >= 1L << 30) return Math.Round(bytes / 1024.0 / 1024 / 1024, 2) + " GB";
        if (bytes >= 1L << 20) return Math.Round(bytes / 1024.0 / 1024, 1) + " MB";
        if (bytes >= 1024) return Math.Round(bytes / 1024.0, 1) + " KB";
        return bytes + " B";
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", "\"" + path + "\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "打开目录");
        }
    }
}
