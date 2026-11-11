using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using TYW.Desktop.Controls;
using TYW;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TYW.Desktop.Views;

/// <summary>
/// 设置页。对应 Windows 版 Pages/PageSetup/*：左栏 4 个分类（启动 / 联机 / 个性化 / 其他），
/// 右栏按分类分卡片，每个卡片里的行式设置与 Windows 版一一对应。
/// 这里只放「在 Linux 上真实生效」的设置项，不放摆设。
/// </summary>
public static class SettingsPage
{
    public static Control Build(string name)
    {
        var host = new StackPanel();
        switch (name)
        {
            case "启动":
                host.Children.Add(LaunchCard());
                host.Children.Add(MemoryCard());
                host.Children.Add(AdvancedCard());
                host.Children.Add(DownloadCard());
                break;

            case "联机":
                host.Children.Add(LinkCard());
                break;

            case "个性化":
                host.Children.Add(UiCard());
                break;

            default:
                host.Children.Add(SystemCard());
                host.Children.Add(ChannelCard());
                host.Children.Add(ServerCard());
                host.Children.Add(StoreCard());
                break;
        }
        return host;
    }

    // ================= 启动 =================

    private static Control LaunchCard()
    {
        var (card, stack) = NewCard("启动选项");

        // 默认版本隔离（LaunchIsolation：0 不隔离 / 1 仅正式版 / 2 全部）
        stack.Children.Add(ComboRow("默认版本隔离",
            new List<string> { "不隔离（共用 .minecraft）", "仅正式版隔离", "全部隔离" },
            ModSettings.GetInt("LaunchIsolation", 0), i =>
            {
                ModSettings.SetValue("LaunchIsolation", i.ToString());
                ModSettings.Save();
            }));

        // 窗口大小：全屏 / 窗口化 + 自定义宽高
        var sizePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var comboType = new PclComboBox { IsEditable = false, Width = 96 };
        comboType.SetItems(new[] { "窗口化", "全屏" }, ModSettings.GetBool("LaunchFullscreen") ? 1 : 0);

        var txtWidth = NumberBox(ModSettings.GetInt("LaunchWindowWidth", 854), 78);
        var txtHeight = NumberBox(ModSettings.GetInt("LaunchWindowHeight", 480), 78);
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
        txtWidth.TextChanged += (_, _) => SaveInt(txtWidth.Text, "LaunchWindowWidth", 854);
        txtHeight.TextChanged += (_, _) => SaveInt(txtHeight.Text, "LaunchWindowHeight", 480);

        sizePanel.Children.Add(comboType);
        sizePanel.Children.Add(txtWidth);
        sizePanel.Children.Add(labX);
        sizePanel.Children.Add(txtHeight);
        stack.Children.Add(Row("窗口大小", sizePanel));

        stack.Children.Add(Row("游戏目录", Value(ModPaths.McFolder)));
        return card;
    }

    private static Control MemoryCard()
    {
        var (card, stack) = NewCard("内存分配");

        var totalMb = SystemMemoryMb();
        var maxMb = Math.Max(1024, totalMb - 1024);
        var curMb = ModSettings.GetInt("LaunchRam", 0);
        var isAuto = curMb <= 0;

        var comboMode = new PclComboBox { IsEditable = false, Width = 150 };
        comboMode.SetItems(new[] { "自动配置", "自定义" }, isAuto ? 0 : 1);

        var slider = new Slider
        {
            Minimum = 1024,
            Maximum = maxMb,
            TickFrequency = 256,
            IsSnapToTickEnabled = true,
            Width = 300,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !isAuto
        };
        var labRam = new TextBlock
        {
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 110,
            TextAlignment = TextAlignment.Right
        };

        void ShowRam(double mb)
        {
            labRam.Text = (int)mb + " MB（" + Math.Round(mb / 1024, 1) + " GB）";
        }

        if (isAuto)
        {
            // 自动 = 系统内存的 1/4，最多 8 GB，最少 2 GB
            var auto = Math.Clamp((int)(totalMb * 0.4), 1024, 8192);
            slider.Value = Math.Clamp(auto, 1024, maxMb);
            ShowRam(auto);
            labRam.Text += "（自动）";
        }
        else
        {
            slider.Value = Math.Clamp(curMb, 1024, maxMb);
            ShowRam(slider.Value);
        }

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            ShowRam(slider.Value);
            ModSettings.SetValue("LaunchRam", ((int)slider.Value).ToString());
            ModSettings.Save();
        };

        comboMode.SelectionChanged += (_, _) =>
        {
            var auto = comboMode.SelectedIndex == 0;
            slider.IsEnabled = !auto;
            ModSettings.SetValue("LaunchRam", auto ? "0" : ((int)slider.Value).ToString());
            ModSettings.Save();
            if (auto)
            {
                var a = Math.Clamp((int)(totalMb * 0.4), 1024, 8192);
                slider.Value = Math.Clamp(a, 1024, maxMb);
                labRam.Text = a + " MB（" + Math.Round(a / 1024.0, 1) + " GB）（自动）";
            }
            else
            {
                ShowRam(slider.Value);
            }
        };

        stack.Children.Add(Row("分配模式", comboMode));
        var ramPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        ramPanel.Children.Add(slider);
        ramPanel.Children.Add(labRam);
        stack.Children.Add(Row("游戏内存", ramPanel));

        stack.Children.Add(new TextBlock
        {
            Text = "系统内存 " + Math.Round(totalMb / 1024.0, 1) + " GB，建议分配 4~8 GB；分配过大反而会拖慢游戏。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return card;
    }

    private static Control AdvancedCard()
    {
        var (card, stack) = NewCard("高级选项");

        stack.Children.Add(ComboRow("Java 路径", JavaOptions(), JavaSelected(), i =>
        {
            if (i <= 0)
            {
                ModSettings.SetValue("LaunchJavaExe", "");
            }
            else
            {
                var list = ModJava.ListJava();
                if (i - 1 < list.Count) ModSettings.SetValue("LaunchJavaExe", list[i - 1].Path);
            }
            ModSettings.Save();
        }));

        var jvm = new PclComboBox
        {
            IsEditable = true,
            Width = 380,
            Placeholder = "-XX:+UseG1GC ……（留空则使用默认参数）",
            Text = ModSettings.GetString("LaunchJvmExtra", ""),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        jvm.TextChanged += (_, _) =>
        {
            ModSettings.SetValue("LaunchJvmExtra", jvm.Text ?? "");
            ModSettings.Save();
        };
        stack.Children.Add(Row("额外 JVM 参数", jvm));

        stack.Children.Add(new TextBlock
        {
            Text = "额外 JVM 参数会追加在默认参数之后，用空格分隔。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap
        });

        return card;
    }

    private static Control DownloadCard()
    {
        var (card, stack) = NewCard("下载");

        stack.Children.Add(ComboRow("下载源",
            new List<string> { "BMCLAPI 镜像（推荐，国内更快）", "Mojang 官方" },
            ModSettings.GetBool("UseOfficialSource") ? 1 : 0, i =>
            {
                ModSettings.SetValue("UseOfficialSource", i == 1);
                ModSettings.Save();
                StaticPage.ApplyMirror();
            }));

        return card;
    }

    // ================= 联机 =================

    private static Control LinkCard()
    {
        var (card, stack) = NewCard("联机设置");

        stack.Children.Add(ComboRow("延迟优化",
            new List<string> { "标准模式", "延迟优先（网络差时更稳）" },
            ModSettings.GetInt("LinkLatencyMode", 0) == 1 ? 1 : 0, i =>
            {
                ModSettings.SetValue("LinkLatencyMode", i == 1 ? "1" : "0");
                ModSettings.Save();
            }));

        stack.Children.Add(Row("联机方式", Value("EasyTier（与 Windows 版房间互通）")));
        stack.Children.Add(Row("设备码", CopyValue(ModSecret.DeviceCode)));

        return card;
    }

    // ================= 个性化 =================

    private static Control UiCard()
    {
        var (card, stack) = NewCard("主题配色");

        var nameValue = Value(ThemeEngine.NameOf(ThemeEngine.Current));
        stack.Children.Add(Row("当前配色", nameValue));

        // ===== 色块网格：15 套预设，与 Windows 版设置 - 个性化 - 主题一一对应 =====
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var marks = new Border[ThemeEngine.Presets.Length];

        void RefreshMarks()
        {
            for (int i = 0; i < marks.Length; i++)
            {
                bool on = i == ThemeEngine.Current;
                marks[i].BorderBrush = on ? Pcl.Res("ColorBrush1") : Brushes.Transparent;
                marks[i].BorderThickness = new Thickness(on ? 2 : 1);
                marks[i].Opacity = on ? 1 : 0.92;
            }
        }

        for (int i = 0; i < ThemeEngine.Presets.Length; i++)
        {
            int idx = i;
            var (top, bottom) = ThemeEngine.PreviewOf(i);
            var sw = new Border
            {
                Width = 38,
                Height = 38,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(top, 0),
                        new GradientStop(bottom, 1)
                    }
                }
            };
            sw.PointerPressed += (_, _) =>
            {
                ThemeEngine.Apply(idx);
                nameValue.Text = ThemeEngine.NameOf(idx);
                RefreshMarks();
            };
            marks[i] = sw;
            ToolTip.SetTip(sw, ThemeEngine.Presets[i].Name);
            wrap.Children.Add(sw);
        }
        stack.Children.Add(wrap);
        RefreshMarks();

        // ===== 自定义配色（主题 14）：色相 / 饱和度 / 亮度补偿 / 顶栏色相差 =====
        var custom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        static PclComboBox N(int v, double w) => new()
        {
            IsEditable = true,
            Width = w,
            Text = v.ToString(),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var nHue = N(ModSettings.GetInt("UiLauncherHue", 210), 64);
        var nSat = N(ModSettings.GetInt("UiLauncherSat", 85), 60);
        var nLight = N(ModSettings.GetInt("UiLauncherLight", 0), 56);
        var nDelta = N(ModSettings.GetInt("UiLauncherDelta", 0), 56);
        void SaveCustom()
        {
            SaveInt(nHue.Text, "UiLauncherHue", 210);
            SaveInt(nSat.Text, "UiLauncherSat", 85);
            SaveInt(nLight.Text, "UiLauncherLight", 0);
            SaveInt(nDelta.Text, "UiLauncherDelta", 0);
            ThemeEngine.Apply(ThemeEngine.CustomTheme);
            nameValue.Text = ThemeEngine.NameOf(ThemeEngine.CustomTheme);
            RefreshMarks();
        }
        nHue.LostFocus += (_, _) => SaveCustom();
        nSat.LostFocus += (_, _) => SaveCustom();
        nLight.LostFocus += (_, _) => SaveCustom();
        nDelta.LostFocus += (_, _) => SaveCustom();

        custom.Children.Add(nHue);
        custom.Children.Add(nSat);
        custom.Children.Add(nLight);
        custom.Children.Add(nDelta);
        stack.Children.Add(Row("自定义（色相/饱和/亮度/色相差）", custom));

        // ===== 彩色背景（对应 Windows 版 UiBackgroundColorful） =====
        var bgBtn = new PclButton
        {
            Height = 28,
            Width = 82,
            Text = ModSettings.GetBool("UiBackgroundColorful", true) ? "已开启" : "已关闭"
        };
        bgBtn.Click += (_, _) =>
        {
            bool now = !ModSettings.GetBool("UiBackgroundColorful", true);
            ModSettings.SetValue("UiBackgroundColorful", now);
            ModSettings.Save();
            bgBtn.Text = now ? "已开启" : "已关闭";
            ThemeEngine.RefreshMain();
        };
        stack.Children.Add(Row("主题色背景", bgBtn));

        stack.Children.Add(Row("语言", Value("简体中文")));
        stack.Children.Add(Row("窗口圆角 / 阴影", Value("跟随系统（Wayland / X11）")));

        stack.Children.Add(new TextBlock
        {
            Text = "配色与 Windows 版完全一致：15 套预设，滑稽彩 / 欧皇彩每次切换随机色相，自定义可填 0-359 的色相。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        return card;
    }

    // ================= 其他（系统） =================

    private static Control SystemCard()
    {
        var (card, stack) = NewCard("关于与更新");

        stack.Children.Add(Row("版本", Value(ModApp.VersionBaseName + "（版本码 " + ModApp.VersionCode + "）")));
        stack.Children.Add(Row("运行环境", Value(ModPlatform.DisplayName + " " + ModApp.RuntimeArch + " · .NET " +
            Environment.Version.ToString(3))));
        stack.Children.Add(Row("数据目录", Value(ModPaths.Root)));
        stack.Children.Add(Row("设备码", CopyValue(ModSecret.DeviceCode)));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var btnUpdate = new PclButton
        {
            Text = "检查更新",
            Height = 30,
            Width = 110,
            ColorType = PclButton.ColorState.Highlight
        };
        btnUpdate.Click += async (_, _) => await StaticPage.CheckUpdateClickPublic();
        buttons.Children.Add(btnUpdate);

        var btnLog = new PclButton { Text = "打开日志目录", Height = 30, Width = 110 };
        btnLog.Click += (_, _) => OpenFolder(ModPaths.LogDir);
        buttons.Children.Add(btnLog);

        var btnCache = new PclButton { Text = "清理缓存", Height = 30, Width = 100 };
        btnCache.Click += async (_, _) => await ClearCache();
        buttons.Children.Add(btnCache);

        stack.Children.Add(buttons);
        return card;
    }

    /// <summary>
    /// 服务端地址：开源版默认不连任何服务器（公告 / 自动更新 / 内测码 / 上报全部关闭）。
    /// 填自己的服务端地址后这些功能才会启用，也可以把地址写成程序目录下的 server.txt。
    /// </summary>
    private static Control ServerCard()
    {
        var (card, stack) = NewCard("服务端（可选）");
        var current = ModNet.ServerAddress;
        stack.Children.Add(Row("当前地址", Value(string.IsNullOrEmpty(current) ? "未配置，纯本地运行" : current)));

        var box = new TextBox
        {
            Watermark = "http://你的服务器:17403/",
            Text = ModSettings.GetString("ServerUrl", ""),
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Width = 300,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        stack.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var save = new PclButton { Text = "保存地址", Height = 30, Width = 100 };
        save.Click += (_, _) =>
        {
            var url = (box.Text ?? "").Trim();
            if (url.Length > 0 && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;
            if (url.Length > 0 && !url.EndsWith("/")) url += "/";
            ModSettings.SetValue("ServerUrl", url);
            ModSettings.Save();
            ModServerUrl.Invalidate();
            ModStartup.HintPublic(url.Length == 0 ? "已清空服务端地址，启动器转为纯本地运行。" : "服务端地址已保存：" + url, false);
        };
        buttons.Children.Add(save);

        var clear = new PclButton { Text = "清空", Height = 30, Width = 76 };
        clear.Click += (_, _) =>
        {
            box.Text = "";
            ModSettings.SetValue("ServerUrl", "");
            ModSettings.Save();
            ModServerUrl.Invalidate();
            ModStartup.HintPublic("已清空服务端地址，启动器转为纯本地运行。", false);
        };
        buttons.Children.Add(clear);
        stack.Children.Add(buttons);

        stack.Children.Add(new TextBlock
        {
            Text = "开源版不带任何服务器地址：不填就纯本地运行，公告、启动器更新、内测码校验、封禁查询、匿名上报全部关闭。\n" +
                   "想接自己的服务端，填地址即可；也可以把地址写成程序目录或数据目录下的 server.txt（一行一个地址）。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return card;
    }

    /// <summary>版本通道切换：下载对应通道的 Linux 包替换重启（对应 Windows 版的快照/内测体验开关）。</summary>
    private static Control ChannelCard()
    {
        var (card, stack) = NewCard("版本通道");
        stack.Children.Add(Row("当前通道", Value(ModApp.ChannelDisplay + "（版本码 " + ModApp.VersionCode + "）")));

        // macOS：三套通道是三个独立签名的 .app，启动器内不能自我替换
        if (!ModPlatform.SupportsChannelSwitch)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "macOS 版的正式版 / 快照版 / 内测版是三套独立签名的 .app，请在官网下载对应通道的安装包后替换，启动器内不支持自动切换。",
                FontSize = 11.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            return card;
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        foreach (var target in new[] { "Release", "Snapshot", "Beta" })
        {
            if (target == ModApp.ChannelKey) continue;
            var t = target;
            var btn = new PclButton
            {
                Text = "切换到" + ModUpdate.ChannelDisplayOf(t),
                Height = 30,
                Width = 120
            };
            btn.Click += async (_, _) => await SwitchChannelClick(t);
            buttons.Children.Add(btn);
        }
        stack.Children.Add(buttons);

        // 内测更新码：单次码，校验通过即被服务端消耗，用于把正式/快照版临时升到内测版
        var codeBtn = new PclButton
        {
            Text = "用内测更新码升级",
            Height = 30,
            Width = 140,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        codeBtn.Click += async (_, _) => await BetaCodeUpdateClick();
        stack.Children.Add(codeBtn);

        stack.Children.Add(new TextBlock
        {
            Text = "切换会下载对应通道的最新包并替换重启，游戏数据不受影响。切换到内测版需要有效内测码；" +
                   "服务端发放的单次内测更新码用一次即失效。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return card;
    }

    /// <summary>单次内测更新码升级：校验 → 用更新码当凭据下载内测包 → 替换重启 → 上报已用。</summary>
    private static async Task BetaCodeUpdateClick()
    {
        var owner = StaticPage.MainWindowPublic;
        if (owner == null) return;
        var code = (await PclMsgBox.ShowInput(owner, "内测更新码",
            "输入服主发放的单次内测更新码（用一次即失效）：", ""))?.Trim() ?? "";
        if (code == "") return;

        ModStartup.HintPublic("正在校验更新码……", false);
        var r = await Task.Run(() => ModBeta.VerifyBetaUpdateCode(code));
        if (!r.Ok)
        {
            ModStartup.HintPublic("更新码不可用：" + r.Message, true);
            return;
        }
        var info = await Task.Run(() => ModServer.GetChannelInfo("Beta"));
        if (string.IsNullOrWhiteSpace(info.File))
        {
            ModStartup.HintPublic("服务端没有配置内测版安装包。", true);
            return;
        }
        var url = info.File.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? info.File
            : ModNet.ServerAddress.TrimEnd('/') + "/" + info.File.TrimStart('/');
        url += (url.Contains('?') ? "&" : "?") + "ident=" + Uri.EscapeDataString(ModSecret.DeviceCode) +
               "&key=" + Uri.EscapeDataString(code);

        var task = TaskCenter.Begin("内测版更新（更新码）");
        var ok = await Task.Run(() =>
        {
            try
            {
                return ModUpdate.ApplyUpdate(url, (p, s) => TaskCenter.Update(task, p, s));
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "内测更新码升级");
                return false;
            }
        });
        if (!ok)
        {
            TaskCenter.Finish(task, "升级失败，详情见日志", false);
            ModStartup.HintPublic("升级失败：更新码可能已失效，或网络异常。", true);
            return;
        }
        TaskCenter.Finish(task, "升级完成，正在重启……");
        ModBeta.ReportBetaUpdateDone(code);
        await Task.Delay(600);
        Environment.Exit(0);
    }

    private static async Task SwitchChannelClick(string target)
    {
        var owner = StaticPage.MainWindowPublic;
        if (owner == null) return;
        var betaCode = "";
        if (target == "Beta")
        {
            betaCode = (await PclMsgBox.ShowInput(owner, "切换到内测版",
                "请输入内测码（向服主获取）：", ModSettings.GetString("BetaCode", "")))?.Trim() ?? "";
            if (betaCode == "") return;
            ModSettings.SetValue("BetaCode", betaCode);
            ModSettings.Save();
        }
        var disp = ModUpdate.ChannelDisplayOf(target);
        var choice = await PclMsgBox.Show(owner, "切换通道",
            $"将下载并切换到{disp}（版本以服务端为准），完成后自动重启。", "立即切换", "取消");
        if (choice != 0) return;

        var task = TaskCenter.Begin("切换到 " + disp);
        var ok = await Task.Run(() =>
        {
            try
            {
                return ModUpdate.SwitchChannel(target, betaCode, (p, s) => TaskCenter.Update(task, p, s));
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "切换通道");
                return false;
            }
        });
        if (!ok)
        {
            TaskCenter.Finish(task, "切换失败，详情见日志", false);
            ModStartup.HintPublic("切换通道失败，详情见日志。", true);
            return;
        }
        TaskCenter.Finish(task, "切换完成，正在重启……");
        await Task.Delay(800);
        Environment.Exit(0);
    }

    private static async Task ClearCache()
    {
        var owner = StaticPage.MainWindowPublic;
        if (owner == null) return;
        var size = DirSize(ModPaths.CacheDir) + DirSize(ModPaths.TempDir);
        var choice = await PclMsgBox.Show(owner, "清理缓存",
            "将删除缓存与临时文件（不影响已下载的版本、Mod 和存档）。\n当前占用：" + FormatSize(size),
            "立即清理", "取消");
        if (choice != 0) return;
        await Task.Run(() =>
        {
            try { Directory.Delete(ModPaths.CacheDir, true); } catch { }
            try { Directory.Delete(ModPaths.TempDir, true); } catch { }
            ModPaths.EnsureAll();
        });
        ModStartup.HintPublic("缓存已清理（释放 " + FormatSize(size) + "）。", false);
    }

    private static void OpenFolder(string dir)
    {
        SysShell.OpenFolder(dir);
    }

    private static long DirSize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch { return 0; }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return Math.Round(bytes / 1024.0 / 1024 / 1024, 2) + " GB";
        if (bytes >= 1L << 20) return Math.Round(bytes / 1024.0 / 1024, 1) + " MB";
        if (bytes >= 1024) return Math.Round(bytes / 1024.0, 1) + " KB";
        return bytes + " B";
    }


    // ================= 拓展商店 =================

    /// <summary>拓展商店：从官方下载站拉取分类/项目并一键导入拓展（.tmlext/.jar）。</summary>
    private static Control StoreCard()
    {
        var (card, stack) = NewCard("拓展商店");
        stack.Children.Add(Row("应用凭证", Value(ModStore.StoreKeyMasked())));
        stack.Children.Add(new TextBlock
        {
            Text = "从官方下载站浏览并一键导入拓展。未配置应用凭证时列表为空（开源版默认不连下载站）。",
            FontSize = 11.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4)
        });

        var listPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        var status = new TextBlock
        {
            FontSize = 12,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray2"),
            Text = "点击「刷新列表」从下载站加载拓展。"
        };

        var btnRefresh = new PclButton { Text = "刷新列表", Height = 30, Width = 110 };
        btnRefresh.Click += async (_, _) =>
        {
            listPanel.Children.Clear();
            status.Text = "正在从下载站加载……";
            btnRefresh.IsEnabled = false;
            try
            {
                var cats = await Task.Run(() => ModStore.FetchTree());
                listPanel.Children.Clear();
                if (cats == null || cats.Count == 0)
                {
                    status.Text = "暂无内容（可能未配置应用凭证，或下载站没有上架拓展）。";
                    return;
                }
                foreach (var cat in cats)
                {
                    if (string.IsNullOrWhiteSpace(cat.Name)) continue;
                    listPanel.Children.Add(new TextBlock
                    {
                        Text = cat.Name,
                        FontSize = 14,
                        FontWeight = FontWeight.Bold,
                        FontFamily = Pcl.FontFamily,
                        Foreground = Pcl.Res("ColorBrush1"),
                        Margin = new Thickness(0, 6, 0, 2)
                    });
                    if (!string.IsNullOrWhiteSpace(cat.Tagline))
                        listPanel.Children.Add(new TextBlock
                        {
                            Text = cat.Tagline,
                            FontSize = 11.5,
                            FontFamily = Pcl.FontFamily,
                            Foreground = Pcl.Res("ColorBrushGray3"),
                            TextWrapping = TextWrapping.Wrap
                        });
                    foreach (var pr in cat.Projects)
                        listPanel.Children.Add(ProjectRow(pr));
                }
                status.Text = "";
            }
            catch (Exception ex)
            {
                status.Text = "加载失败：" + ex.Message;
            }
            finally
            {
                btnRefresh.IsEnabled = true;
            }
        };

        stack.Children.Add(btnRefresh);
        stack.Children.Add(listPanel);
        stack.Children.Add(status);
        return card;
    }

    private static Control ProjectRow(ModStore.StoreProject pr)
    {
        var grid = new Grid { MinHeight = 38, Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = pr.Name,
            FontSize = 13,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrush1")
        });
        if (!string.IsNullOrWhiteSpace(pr.Tagline))
            left.Children.Add(new TextBlock
            {
                Text = pr.Tagline,
                FontSize = 11,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        if (!string.IsNullOrWhiteSpace(pr.SizeHuman) || pr.Downloads > 0)
        {
            var meta = (pr.Downloads > 0 ? (pr.Downloads + " 次下载") : "");
            if (!string.IsNullOrWhiteSpace(pr.SizeHuman)) meta += (meta.Length > 0 ? " · " : "") + pr.SizeHuman;
            left.Children.Add(new TextBlock
            {
                Text = meta,
                FontSize = 10.5,
                FontFamily = Pcl.FontFamily,
                Foreground = Pcl.Res("ColorBrushGray3")
            });
        }

        var btn = new PclButton { Text = "安装", Height = 28, Width = 76, HorizontalAlignment = HorizontalAlignment.Right };
        var prLocal = pr;
        btn.Click += async (_, _) =>
        {
            btn.IsEnabled = false;
            btn.Text = "安装中…";
            try
            {
                await Task.Run(() => ModStore.DownloadAndInstall(prLocal));
                btn.Text = "已导入";
                ModStartup.HintPublic("已导入拓展：" + (prLocal.Name ?? "拓展") + "，可在拓展页启用。", false);
            }
            catch (Exception ex)
            {
                btn.IsEnabled = true;
                btn.Text = "重试";
                ModStartup.HintPublic("安装失败：" + ex.Message, true);
            }
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(btn, 1);
        grid.Children.Add(left);
        grid.Children.Add(btn);
        return grid;
    }

    // ================= 公共辅助 =================

    private static (PclCard Card, StackPanel Stack) NewCard(string title)
    {
        var card = new PclCard { Title = title, Margin = new Thickness(0, 0, 0, 15) };
        var stack = new StackPanel { Margin = new Thickness(15, 6, 15, 13), Spacing = 4 };
        card.Body.Children.Add(stack);
        return (card, stack);
    }

    internal static TextBlock Value(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12.5,
        FontFamily = Pcl.FontFamily,
        Foreground = Pcl.Res("ColorBrushGray2"),
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 420
    };

    /// <summary>值 + 「复制」按钮，用于设备码 / 授权码这类需要手动复制的内容。</summary>
    internal static Control CopyValue(string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(Value(text));
        var btn = new PclButton { Text = "复制", Height = 28, Width = 64 };
        btn.Click += (_, _) =>
        {
            try
            {
                var top = TopLevel.GetTopLevel(btn);
                _ = top?.Clipboard?.SetTextAsync(text ?? "");
                ModStartup.HintPublic("已成功复制！", false);
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "复制文本");
            }
        };
        panel.Children.Add(btn);
        return panel;
    }

    internal static Control Row(string title, Control right)    {
        var grid = new Grid { MinHeight = 34 };
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
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        grid.Children.Add(t);
        grid.Children.Add(right);
        return grid;
    }

    internal static Control ComboRow(string title, List<string> options, int selected, Action<int> changed)
    {
        var combo = new PclComboBox
        {
            IsEditable = false,
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        combo.SetItems(options, Math.Clamp(selected, 0, Math.Max(0, options.Count - 1)));
        combo.SelectionChanged += (_, _) => changed(combo.SelectedIndex);
        return Row(title, combo);
    }

    internal static PclComboBox NumberBox(int value, double width) => new PclComboBox
    {
        IsEditable = true,
        Width = width,
        Text = value.ToString(),
        HorizontalAlignment = HorizontalAlignment.Right
    };

    internal static void SaveInt(string text, string key, int def)
    {
        if (int.TryParse((text ?? "").Trim(), out var v) && v > 0)
        {
            ModSettings.SetValue(key, v.ToString());
            ModSettings.Save();
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            ModSettings.SetValue(key, def.ToString());
            ModSettings.Save();
        }
    }

    internal static List<string> JavaOptions()
    {
        var list = new List<string> { "自动选择（推荐）" };
        foreach (var j in ModJava.ListJava()) list.Add(j.Path + "    " + j.Display);
        return list;
    }

    internal static int JavaSelected()
    {
        var saved = ModSettings.GetString("LaunchJavaExe");
        if (string.IsNullOrEmpty(saved)) return 0;
        var list = ModJava.ListJava();
        for (int i = 0; i < list.Count; i++)
            if (list[i].Path == saved) return i + 1;
        return 0;
    }

    /// <summary>物理内存总量（MB），读 /proc/meminfo。</summary>
    internal static int SystemMemoryMb()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                    return (int)(kb / 1024);
            }
        }
        catch { }
        return 8192;
    }
}
